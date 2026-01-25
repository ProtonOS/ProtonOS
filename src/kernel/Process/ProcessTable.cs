// ProtonOS kernel - Process Table Management
// Global process table and process lifecycle management.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.IO;
using ProtonOS.X64;

namespace ProtonOS.Process;

/// <summary>
/// Process table management - global registry of all processes
/// </summary>
public static unsafe class ProcessTable
{
    // Maximum number of processes (can be increased dynamically)
    private const int InitialMaxProcesses = 1024;
    private const int DefaultMaxOpenFiles = 256;
    private const ulong DefaultMaxAddressSpace = 0x0000_7FFF_FFFF_0000; // ~128TB
    private const ulong DefaultMaxStackSize = 8 * 1024 * 1024;          // 8MB

    // Process table (sparse array indexed by PID)
    private static Process** _processes;
    private static int _maxProcesses;

    // Next PID to allocate (starts at 1, PID 0 is invalid)
    private static int _nextPid = 1;

    // Global process list (for iteration)
    private static Process* _allProcessesHead;
    private static Process* _allProcessesTail;

    // Statistics
    private static int _processCount;
    private static int _zombieCount;

    // Lock for process table operations
    private static SpinLock _lock;

    // Whether the process table is initialized
    private static bool _initialized;

    // Init process (PID 1) - parent of orphaned processes
    private static Process* _initProcess;

    // Kernel process (PID 0) - represents kernel threads
    private static Process* _kernelProcess;

    /// <summary>
    /// Whether the process table is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Number of active processes
    /// </summary>
    public static int ProcessCount => _processCount;

    /// <summary>
    /// Number of zombie processes
    /// </summary>
    public static int ZombieCount => _zombieCount;

    /// <summary>
    /// Get the init process (PID 1)
    /// </summary>
    public static Process* InitProcess => _initProcess;

    /// <summary>
    /// Get the kernel process (PID 0)
    /// </summary>
    public static Process* KernelProcess => _kernelProcess;

    /// <summary>
    /// Initialize the process table
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[ProcTable] Initializing process table...");

        _maxProcesses = InitialMaxProcesses;
        _processes = (Process**)HeapAllocator.AllocZeroed((ulong)(sizeof(Process*) * _maxProcesses));
        if (_processes == null)
        {
            DebugConsole.WriteLine("[ProcTable] Failed to allocate process table!");
            return;
        }

        _allProcessesHead = null;
        _allProcessesTail = null;
        _processCount = 0;
        _zombieCount = 0;
        _nextPid = 1;

        // Create kernel process (PID 0) - represents kernel threads
        _kernelProcess = CreateKernelProcess();

        _initialized = true;
        DebugConsole.WriteLine("[ProcTable] Initialized");
    }

    /// <summary>
    /// Create the kernel process (PID 0)
    /// </summary>
    private static Process* CreateKernelProcess()
    {
        var proc = (Process*)HeapAllocator.AllocZeroed((ulong)sizeof(Process));
        if (proc == null)
            return null;

        proc->Pid = 0;
        proc->ParentPid = 0;
        proc->State = ProcessState.Running;
        proc->Uid = UserIds.Root;
        proc->Gid = GroupIds.Root;
        proc->Euid = UserIds.Root;
        proc->Egid = GroupIds.Root;
        proc->PageTableRoot = VirtualMemory.Pml4Address;
        proc->MaxOpenFiles = DefaultMaxOpenFiles;
        proc->MaxAddressSpace = DefaultMaxAddressSpace;
        proc->MaxStackSize = DefaultMaxStackSize;
        proc->StartTime = APIC.TickCount;
        proc->SessionId = 0;
        proc->ProcessGroupId = 0;

        // Set name to "kernel"
        var name = stackalloc byte[] { (byte)'k', (byte)'e', (byte)'r', (byte)'n', (byte)'e', (byte)'l', 0 };
        for (int i = 0; i < 7 && i < 64; i++)
            proc->Name[i] = name[i];

        // Set cwd to "/"
        proc->Cwd[0] = (byte)'/';
        proc->Cwd[1] = 0;

        // Add to process table (PID 0 is a special case)
        // We don't add it to the regular array since PID 0 is special

        return proc;
    }

    /// <summary>
    /// Allocate a new process structure
    /// </summary>
    /// <returns>Pointer to new process, or null on failure</returns>
    public static Process* Allocate()
    {
        if (!_initialized)
            return null;

        _lock.Acquire();

        // Find next available PID
        int pid = FindFreePid();
        if (pid < 0)
        {
            _lock.Release();
            DebugConsole.WriteLine("[ProcTable] No free PIDs!");
            return null;
        }

        // Allocate process structure
        var proc = (Process*)HeapAllocator.AllocZeroed((ulong)sizeof(Process));
        if (proc == null)
        {
            _lock.Release();
            DebugConsole.WriteLine("[ProcTable] Failed to allocate process structure!");
            return null;
        }

        // Initialize process
        proc->Pid = pid;
        proc->State = ProcessState.Created;
        proc->MaxOpenFiles = DefaultMaxOpenFiles;
        proc->MaxAddressSpace = DefaultMaxAddressSpace;
        proc->MaxStackSize = DefaultMaxStackSize;
        proc->StartTime = APIC.TickCount;

        // Allocate file descriptor table
        proc->FdTableSize = (int)proc->MaxOpenFiles;
        proc->FdTable = (FileDescriptor*)HeapAllocator.AllocZeroed(
            (ulong)(sizeof(FileDescriptor) * proc->FdTableSize));
        if (proc->FdTable == null)
        {
            HeapAllocator.Free(proc);
            _lock.Release();
            return null;
        }

        // Allocate signal handlers
        proc->SignalHandlers = (SignalHandler*)HeapAllocator.AllocZeroed(
            (ulong)(sizeof(SignalHandler) * Signals.SIGMAX));
        if (proc->SignalHandlers == null)
        {
            HeapAllocator.Free(proc->FdTable);
            HeapAllocator.Free(proc);
            _lock.Release();
            return null;
        }

        // Set default cwd
        proc->Cwd[0] = (byte)'/';
        proc->Cwd[1] = 0;

        // Add to process table
        _processes[pid] = proc;

        // Add to global list
        proc->PrevAll = _allProcessesTail;
        proc->NextAll = null;
        if (_allProcessesTail != null)
            _allProcessesTail->NextAll = proc;
        else
            _allProcessesHead = proc;
        _allProcessesTail = proc;

        _processCount++;

        _lock.Release();

        DebugConsole.WriteLine(string.Format("[ProcTable] Allocated process PID {0}", pid));
        return proc;
    }

    /// <summary>
    /// Find a free PID
    /// </summary>
    private static int FindFreePid()
    {
        // Try sequential allocation first
        int startPid = _nextPid;
        int pid = startPid;

        do
        {
            if (pid < _maxProcesses && _processes[pid] == null)
            {
                _nextPid = pid + 1;
                if (_nextPid >= _maxProcesses)
                    _nextPid = 1;
                return pid;
            }

            pid++;
            if (pid >= _maxProcesses)
                pid = 1;
        }
        while (pid != startPid);

        // All PIDs exhausted - could grow the table here
        return -1;
    }

    /// <summary>
    /// Get a process by PID
    /// </summary>
    public static Process* GetByPid(int pid)
    {
        if (!_initialized || pid < 0 || pid >= _maxProcesses)
            return null;

        if (pid == 0)
            return _kernelProcess;

        return _processes[pid];
    }

    /// <summary>
    /// Get the current process (from current thread)
    /// </summary>
    public static Process* GetCurrent()
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return _kernelProcess;

        // Get process from thread (we'll add this field to Thread)
        // For now, return kernel process
        return _kernelProcess;
    }

    /// <summary>
    /// Free a process structure (after zombie is reaped)
    /// </summary>
    public static void Free(Process* proc)
    {
        if (proc == null || !_initialized)
            return;

        _lock.Acquire();

        int pid = proc->Pid;
        if (pid > 0 && pid < _maxProcesses && _processes[pid] == proc)
        {
            _processes[pid] = null;
        }

        // Remove from global list
        if (proc->PrevAll != null)
            proc->PrevAll->NextAll = proc->NextAll;
        else
            _allProcessesHead = proc->NextAll;

        if (proc->NextAll != null)
            proc->NextAll->PrevAll = proc->PrevAll;
        else
            _allProcessesTail = proc->PrevAll;

        if (proc->State == ProcessState.Zombie)
            _zombieCount--;
        _processCount--;

        _lock.Release();

        // Free resources
        if (proc->FdTable != null)
            HeapAllocator.Free(proc->FdTable);
        if (proc->SignalHandlers != null)
            HeapAllocator.Free(proc->SignalHandlers);

        HeapAllocator.Free(proc);

        DebugConsole.WriteLine(string.Format("[ProcTable] Freed process PID {0}", pid));
    }

    /// <summary>
    /// Mark a process as zombie (after exit)
    /// </summary>
    public static void MarkZombie(Process* proc, int exitCode, int exitSignal)
    {
        if (proc == null)
            return;

        _lock.Acquire();

        proc->State = ProcessState.Zombie;
        proc->ExitCode = exitCode;
        proc->ExitSignal = exitSignal;
        _zombieCount++;

        // Reparent children to init process
        ReparentChildren(proc);

        _lock.Release();

        // Signal parent (SIGCHLD)
        if (proc->Parent != null)
        {
            // Queue SIGCHLD to parent
            SignalProcess(proc->Parent, Signals.SIGCHLD);
        }
    }

    /// <summary>
    /// Reparent a process's children to init
    /// </summary>
    private static void ReparentChildren(Process* proc)
    {
        if (_initProcess == null)
            return;

        var child = proc->FirstChild;
        while (child != null)
        {
            var next = child->NextSibling;

            // Reparent to init
            child->Parent = _initProcess;
            child->ParentPid = 1;

            // Add to init's children
            child->NextSibling = _initProcess->FirstChild;
            _initProcess->FirstChild = child;

            // If child is zombie, init can reap it
            if (child->State == ProcessState.Zombie)
            {
                // Wake init if it's waiting
                // (handled by init's wait implementation)
            }

            child = next;
        }

        proc->FirstChild = null;
    }

    /// <summary>
    /// Send a signal to a process
    /// </summary>
    public static bool SignalProcess(Process* proc, int signum)
    {
        if (proc == null || signum < 1 || signum > Signals.SIGMAX)
            return false;

        _lock.Acquire();

        // Check if signal is blocked
        ulong mask = Signals.Mask(signum);
        if ((proc->BlockedSignals & mask) != 0 && Signals.CanCatch(signum))
        {
            // Signal is blocked - add to pending
            proc->PendingSignals |= mask;
            _lock.Release();
            return true;
        }

        // Add to pending signals
        proc->PendingSignals |= mask;

        // Wake up process if sleeping
        if (proc->State == ProcessState.Sleeping)
        {
            proc->State = ProcessState.Running;
            // Wake main thread (would need to wake all interruptible threads)
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Set the init process (called during system init)
    /// </summary>
    public static void SetInitProcess(Process* proc)
    {
        _initProcess = proc;
    }

    /// <summary>
    /// Get the head of the all-processes list for iteration
    /// </summary>
    public static Process* AllProcessesHead => _allProcessesHead;

    /// <summary>
    /// Get the process table lock for safe iteration
    /// </summary>
    public static ref SpinLock Lock => ref _lock;

    /// <summary>
    /// Find processes by user ID
    /// </summary>
    public static int FindByUid(uint uid, Process** buffer, int maxCount)
    {
        if (!_initialized || buffer == null || maxCount <= 0)
            return 0;

        _lock.Acquire();

        int count = 0;
        for (var proc = _allProcessesHead; proc != null && count < maxCount; proc = proc->NextAll)
        {
            if (proc->Uid == uid || proc->Euid == uid)
            {
                buffer[count++] = proc;
            }
        }

        _lock.Release();
        return count;
    }

    /// <summary>
    /// Find processes in a process group
    /// </summary>
    public static int FindByProcessGroup(int pgid, Process** buffer, int maxCount)
    {
        if (!_initialized || buffer == null || maxCount <= 0)
            return 0;

        _lock.Acquire();

        int count = 0;
        for (var proc = _allProcessesHead; proc != null && count < maxCount; proc = proc->NextAll)
        {
            if (proc->ProcessGroupId == pgid)
            {
                buffer[count++] = proc;
            }
        }

        _lock.Release();
        return count;
    }

    /// <summary>
    /// Find processes in a session
    /// </summary>
    public static int FindBySession(int sid, Process** buffer, int maxCount)
    {
        if (!_initialized || buffer == null || maxCount <= 0)
            return 0;

        _lock.Acquire();

        int count = 0;
        for (var proc = _allProcessesHead; proc != null && count < maxCount; proc = proc->NextAll)
        {
            if (proc->SessionId == sid)
            {
                buffer[count++] = proc;
            }
        }

        _lock.Release();
        return count;
    }
}
