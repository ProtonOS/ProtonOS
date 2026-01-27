// ProtonOS kernel - System Call Dispatch
// Dispatch table and basic syscall implementations.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.IO;
using ProtonOS.Process;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Syscall;

/// <summary>
/// Syscall dispatch table and handler registry
/// </summary>
public static unsafe class SyscallDispatch
{
    /// <summary>
    /// Syscall handler function type
    /// </summary>
    public delegate long SyscallFunc(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread);

    // Handler table
    private static SyscallFunc[] _handlers = null!;
    private static bool _initialized;

    // Ring 3 test exit handler (for testing user mode)
    private static delegate* unmanaged<int, void> _ring3TestExitHandler;

    /// <summary>
    /// Set a custom exit handler for Ring 3 testing.
    /// The handler is called when exit() syscall is invoked.
    /// </summary>
    public static void SetRing3TestExitHandler(delegate* unmanaged<int, void> handler)
    {
        _ring3TestExitHandler = handler;
    }

    /// <summary>
    /// Get the Ring 3 test exit handler
    /// </summary>
    public static delegate* unmanaged<int, void> GetRing3TestExitHandler()
    {
        return _ring3TestExitHandler;
    }

    /// <summary>
    /// Clear the Ring 3 test exit handler
    /// </summary>
    public static void ClearRing3TestExitHandler()
    {
        _ring3TestExitHandler = null;
    }

    // Directory/file operation callbacks (set by DDK when filesystem drivers initialize)
    private static delegate* unmanaged<byte*, int, int> _mkdirHandler;
    private static delegate* unmanaged<byte*, int> _rmdirHandler;
    private static delegate* unmanaged<byte*, int> _unlinkHandler;
    private static delegate* unmanaged<byte*, byte*, int, long*, int> _getdentsHandler;
    private static delegate* unmanaged<byte*, int, int> _accessHandler;
    private static delegate* unmanaged<byte*, byte*, int> _renameHandler;
    private static delegate* unmanaged<byte*, int, int> _chmodHandler;
    private static delegate* unmanaged<byte*, uint, uint, int> _chownHandler;
    private static delegate* unmanaged<byte*, byte*, int> _linkHandler;
    private static delegate* unmanaged<byte*, byte*, int> _symlinkHandler;
    private static delegate* unmanaged<byte*, byte*, int, int> _readlinkHandler;
    private static delegate* unmanaged<byte*, long, int> _truncateHandler;

    /// <summary>
    /// Register mkdir handler from DDK
    /// </summary>
    public static void RegisterMkdirHandler(delegate* unmanaged<byte*, int, int> handler)
    {
        _mkdirHandler = handler;
    }

    /// <summary>
    /// Register rmdir handler from DDK
    /// </summary>
    public static void RegisterRmdirHandler(delegate* unmanaged<byte*, int> handler)
    {
        _rmdirHandler = handler;
    }

    /// <summary>
    /// Register unlink handler from DDK
    /// </summary>
    public static void RegisterUnlinkHandler(delegate* unmanaged<byte*, int> handler)
    {
        _unlinkHandler = handler;
    }

    /// <summary>
    /// Register getdents handler from DDK
    /// </summary>
    public static void RegisterGetdentsHandler(delegate* unmanaged<byte*, byte*, int, long*, int> handler)
    {
        _getdentsHandler = handler;
    }

    /// <summary>
    /// Register access handler from DDK
    /// </summary>
    public static void RegisterAccessHandler(delegate* unmanaged<byte*, int, int> handler)
    {
        _accessHandler = handler;
    }

    /// <summary>
    /// Register rename handler from DDK
    /// </summary>
    public static void RegisterRenameHandler(delegate* unmanaged<byte*, byte*, int> handler)
    {
        _renameHandler = handler;
    }

    /// <summary>
    /// Register chmod handler from DDK
    /// </summary>
    public static void RegisterChmodHandler(delegate* unmanaged<byte*, int, int> handler)
    {
        _chmodHandler = handler;
    }

    /// <summary>
    /// Register chown handler from DDK
    /// </summary>
    public static void RegisterChownHandler(delegate* unmanaged<byte*, uint, uint, int> handler)
    {
        _chownHandler = handler;
    }

    /// <summary>
    /// Register link handler from DDK
    /// </summary>
    public static void RegisterLinkHandler(delegate* unmanaged<byte*, byte*, int> handler)
    {
        _linkHandler = handler;
    }

    /// <summary>
    /// Register symlink handler from DDK
    /// </summary>
    public static void RegisterSymlinkHandler(delegate* unmanaged<byte*, byte*, int> handler)
    {
        _symlinkHandler = handler;
    }

    /// <summary>
    /// Register readlink handler from DDK
    /// </summary>
    public static void RegisterReadlinkHandler(delegate* unmanaged<byte*, byte*, int, int> handler)
    {
        _readlinkHandler = handler;
    }

    /// <summary>
    /// Register truncate handler from DDK
    /// </summary>
    public static void RegisterTruncateHandler(delegate* unmanaged<byte*, long, int> handler)
    {
        _truncateHandler = handler;
    }

    /// <summary>
    /// Initialize the syscall dispatch table
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[SyscallDispatch] Initializing syscall table...");

        _handlers = new SyscallFunc[SyscallNumbers.SYS_MAX];

        // Register basic syscalls
        RegisterProcessSyscalls();
        RegisterFileSyscalls();
        RegisterIdentitySyscalls();
        RegisterMemorySyscalls();
        RegisterTimeSyscalls();

        // Initialize futex subsystem
        InitFutex();

        _initialized = true;
        DebugConsole.WriteLine("[SyscallDispatch] Initialized");
    }

    /// <summary>
    /// Register a syscall handler
    /// </summary>
    public static void Register(int number, SyscallFunc handler)
    {
        if (number >= 0 && number < SyscallNumbers.SYS_MAX)
        {
            _handlers[number] = handler;
        }
    }

    /// <summary>
    /// Dispatch a syscall to the appropriate handler
    /// </summary>
    public static long Dispatch(long number, long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        if (!_initialized)
            Init();

        if (number < 0 || number >= SyscallNumbers.SYS_MAX)
            return -Errno.ENOSYS;

        var handler = _handlers[number];
        if (handler == null)
            return -Errno.ENOSYS;

        return handler(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    /// <summary>
    /// Register process control syscalls
    /// </summary>
    private static void RegisterProcessSyscalls()
    {
        _handlers[SyscallNumbers.SYS_EXIT] = SysExit;
        _handlers[SyscallNumbers.SYS_EXIT_GROUP] = SysExitGroup;
        _handlers[SyscallNumbers.SYS_GETPID] = SysGetpid;
        _handlers[SyscallNumbers.SYS_GETPPID] = SysGetppid;
        _handlers[SyscallNumbers.SYS_FORK] = SysFork;
        _handlers[SyscallNumbers.SYS_VFORK] = SysVfork;
        _handlers[SyscallNumbers.SYS_EXECVE] = SysExecve;
        _handlers[SyscallNumbers.SYS_WAIT4] = SysWait4;
        _handlers[SyscallNumbers.SYS_KILL] = SysKill;
        _handlers[SyscallNumbers.SYS_GETPGID] = SysGetpgid;
        _handlers[SyscallNumbers.SYS_SETPGID] = SysSetpgid;
        _handlers[SyscallNumbers.SYS_GETSID] = SysGetsid;
        _handlers[SyscallNumbers.SYS_SETSID] = SysSetsid;

        // Thread-related syscalls
        _handlers[SyscallNumbers.SYS_GETTID] = SysGettid;
        _handlers[SyscallNumbers.SYS_ARCH_PRCTL] = SysArchPrctl;
        _handlers[SyscallNumbers.SYS_SET_TID_ADDRESS] = SysSetTidAddress;
        _handlers[SyscallNumbers.SYS_CLONE] = SysClone;
        _handlers[SyscallNumbers.SYS_FUTEX] = SysFutex;
    }

    /// <summary>
    /// Register file operation syscalls
    /// </summary>
    private static void RegisterFileSyscalls()
    {
        _handlers[SyscallNumbers.SYS_READ] = SysRead;
        _handlers[SyscallNumbers.SYS_WRITE] = SysWrite;
        _handlers[SyscallNumbers.SYS_OPEN] = SysOpen;
        _handlers[SyscallNumbers.SYS_CLOSE] = SysClose;
        _handlers[SyscallNumbers.SYS_STAT] = SysStat;
        _handlers[SyscallNumbers.SYS_FSTAT] = SysFstat;
        _handlers[SyscallNumbers.SYS_LSTAT] = SysLstat;
        _handlers[SyscallNumbers.SYS_LSEEK] = SysLseek;
        _handlers[SyscallNumbers.SYS_PIPE] = SysPipe;
        _handlers[SyscallNumbers.SYS_DUP] = SysDup;
        _handlers[SyscallNumbers.SYS_DUP2] = SysDup2;
        _handlers[SyscallNumbers.SYS_FCNTL] = SysFcntl;
        _handlers[SyscallNumbers.SYS_GETCWD] = SysGetcwd;
        _handlers[SyscallNumbers.SYS_CHDIR] = SysChdir;
        _handlers[SyscallNumbers.SYS_MKDIR] = SysMkdir;
        _handlers[SyscallNumbers.SYS_RMDIR] = SysRmdir;
        _handlers[SyscallNumbers.SYS_UNLINK] = SysUnlink;
        _handlers[SyscallNumbers.SYS_GETDENTS] = SysGetdents;
        _handlers[SyscallNumbers.SYS_GETDENTS64] = SysGetdents;  // Same handler, compatible format
        _handlers[SyscallNumbers.SYS_POLL] = SysPoll;
        _handlers[SyscallNumbers.SYS_ACCESS] = SysAccess;
        _handlers[SyscallNumbers.SYS_RENAME] = SysRename;
        _handlers[SyscallNumbers.SYS_IOCTL] = SysIoctl;
        _handlers[SyscallNumbers.SYS_PREAD64] = SysPread64;
        _handlers[SyscallNumbers.SYS_PWRITE64] = SysPwrite64;
        _handlers[SyscallNumbers.SYS_READV] = SysReadv;
        _handlers[SyscallNumbers.SYS_WRITEV] = SysWritev;
        _handlers[SyscallNumbers.SYS_TRUNCATE] = SysTruncate;
        _handlers[SyscallNumbers.SYS_FTRUNCATE] = SysFtruncate;
        _handlers[SyscallNumbers.SYS_FCHDIR] = SysFchdir;
        _handlers[SyscallNumbers.SYS_CREAT] = SysCreat;
        _handlers[SyscallNumbers.SYS_LINK] = SysLink;
        _handlers[SyscallNumbers.SYS_SYMLINK] = SysSymlink;
        _handlers[SyscallNumbers.SYS_READLINK] = SysReadlink;
        _handlers[SyscallNumbers.SYS_CHMOD] = SysChmod;
        _handlers[SyscallNumbers.SYS_FCHMOD] = SysFchmod;
        _handlers[SyscallNumbers.SYS_CHOWN] = SysChown;
        _handlers[SyscallNumbers.SYS_FCHOWN] = SysFchown;
        _handlers[SyscallNumbers.SYS_LCHOWN] = SysLchown;
        _handlers[SyscallNumbers.SYS_DUP3] = SysDup3;
    }

    /// <summary>
    /// Register user/group identity syscalls
    /// </summary>
    private static void RegisterIdentitySyscalls()
    {
        _handlers[SyscallNumbers.SYS_GETUID] = SysGetuid;
        _handlers[SyscallNumbers.SYS_GETGID] = SysGetgid;
        _handlers[SyscallNumbers.SYS_GETEUID] = SysGeteuid;
        _handlers[SyscallNumbers.SYS_GETEGID] = SysGetegid;
        _handlers[SyscallNumbers.SYS_SETUID] = SysSetuid;
        _handlers[SyscallNumbers.SYS_SETGID] = SysSetgid;
        _handlers[SyscallNumbers.SYS_UNAME] = SysUname;
        _handlers[SyscallNumbers.SYS_SYSINFO] = SysSysinfo;
    }

    /// <summary>
    /// Register memory management syscalls
    /// </summary>
    private static void RegisterMemorySyscalls()
    {
        _handlers[SyscallNumbers.SYS_BRK] = SysBrk;
        _handlers[SyscallNumbers.SYS_MMAP] = SysMmap;
        _handlers[SyscallNumbers.SYS_MPROTECT] = SysMprotect;
        _handlers[SyscallNumbers.SYS_MUNMAP] = SysMunmap;
        _handlers[SyscallNumbers.SYS_MSYNC] = SysMsync;
    }

    /// <summary>
    /// Register time-related syscalls
    /// </summary>
    private static void RegisterTimeSyscalls()
    {
        _handlers[SyscallNumbers.SYS_CLOCK_GETTIME] = SysClockGettime;
        _handlers[SyscallNumbers.SYS_CLOCK_GETRES] = SysClockGetres;
        _handlers[SyscallNumbers.SYS_GETTIMEOFDAY] = SysGettimeofday;
        _handlers[SyscallNumbers.SYS_NANOSLEEP] = SysNanosleep;
        _handlers[SyscallNumbers.SYS_GETRANDOM] = SysGetrandom;
    }

    // ==================== Process Control ====================

    private static long SysExit(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int exitCode = (int)arg0;
        DebugConsole.Write("[Syscall] exit(");
        DebugConsole.WriteDecimal(exitCode);
        DebugConsole.WriteLine(")");

        // Check if Ring 3 test handler is installed
        if (_ring3TestExitHandler != null)
        {
            DebugConsole.WriteLine("[Syscall] Calling Ring 3 test exit handler");
            var handler = _ring3TestExitHandler;
            _ring3TestExitHandler = null;  // Clear handler to prevent re-entry
            handler(exitCode);
            // Handler should not return, but if it does, continue to normal exit
        }

        // Mark process as zombie and store exit code
        ProcessTable.MarkZombie(proc, exitCode, 0);

        // Terminate the current thread
        Scheduler.ExitThread((uint)exitCode);

        // Should never reach here
        return 0;
    }

    private static long SysExitGroup(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        // exit_group terminates all threads in the process
        return SysExit(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    private static long SysGetpid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Pid;
    }

    private static long SysGetppid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->ParentPid;
    }

    private static long SysFork(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        // Fork implementation will be in ProcessSyscalls.cs
        return ProcessSyscalls.Fork(proc, thread);
    }

    private static long SysVfork(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // vfork is similar to fork but shares address space until exec
        // For now, implement as regular fork
        return ProcessSyscalls.Fork(proc, thread);
    }

    private static long SysExecve(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        byte** argv = (byte**)arg1;
        byte** envp = (byte**)arg2;

        DebugConsole.Write("[Syscall] execve(");
        if (path != null)
        {
            for (int i = 0; path[i] != 0 && i < 64; i++)
                DebugConsole.WriteChar((char)path[i]);
        }
        DebugConsole.WriteLine(")");

        // Execute the .NET assembly
        return Process.NetExecutable.Exec(proc, path, argv, envp);
    }

    private static long SysWait4(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int* status = (int*)arg1;
        int options = (int)arg2;

        return ProcessSyscalls.Wait4(proc, pid, status, options);
    }

    private static long SysKill(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int sig = (int)arg1;

        if (pid <= 0)
        {
            // Process group signals not yet implemented
            return -Errno.ENOSYS;
        }

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        // Check permissions (can only signal processes we own, or be root)
        if (proc->Euid != 0 && proc->Euid != target->Uid && proc->Uid != target->Uid)
            return -Errno.EPERM;

        if (!ProcessTable.SignalProcess(target, sig))
            return -Errno.EINVAL;

        return 0;
    }

    private static long SysGetpgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        if (pid == 0)
            return proc->ProcessGroupId;

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        return target->ProcessGroupId;
    }

    private static long SysSetpgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int pgid = (int)arg1;

        var target = pid == 0 ? proc : ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        // Can only change own or children's process group
        if (target != proc && target->ParentPid != proc->Pid)
            return -Errno.ESRCH;

        if (pgid == 0)
            pgid = target->Pid;

        target->ProcessGroupId = pgid;
        return 0;
    }

    private static long SysGetsid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        if (pid == 0)
            return proc->SessionId;

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        return target->SessionId;
    }

    private static long SysSetsid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        // Process must not already be a session leader
        if (proc->Pid == proc->SessionId)
            return -Errno.EPERM;

        // Create new session
        proc->SessionId = proc->Pid;
        proc->ProcessGroupId = proc->Pid;
        proc->ControllingTerminal = null;

        return proc->Pid;
    }

    // ==================== Thread Operations ====================

    /// <summary>
    /// gettid() - Get thread ID
    /// Returns the kernel thread ID of the calling thread.
    /// </summary>
    private static long SysGettid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return thread->Id;
    }

    /// <summary>
    /// arch_prctl(code, addr) - Set architecture-specific thread state
    /// Used primarily for Thread Local Storage (TLS) setup via FS base register.
    /// </summary>
    private static long SysArchPrctl(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        int code = (int)arg0;
        ulong addr = (ulong)arg1;

        // arch_prctl codes
        const int ARCH_SET_GS = 0x1001;
        const int ARCH_SET_FS = 0x1002;
        const int ARCH_GET_FS = 0x1003;
        const int ARCH_GET_GS = 0x1004;

        switch (code)
        {
            case ARCH_SET_FS:
                // Validate address is in user space (below kernel)
                if (addr >= 0x800000000000UL)
                    return -Errno.EPERM;

                // Store in thread structure for context switch restoration
                thread->UserFsBase = addr;

                // Set the FS base register immediately
                CPU.SetFsBase(addr);
                return 0;

            case ARCH_GET_FS:
                // Write current FS base to user pointer
                if (addr == 0 || addr >= 0x800000000000UL)
                    return -Errno.EFAULT;
                *(ulong*)addr = thread->UserFsBase;
                return 0;

            case ARCH_SET_GS:
                // GS is typically used by kernel, but we allow user to set it
                if (addr >= 0x800000000000UL)
                    return -Errno.EPERM;
                thread->UserGsBase = addr;
                // Note: We don't set GS base directly as kernel uses it for per-CPU data
                // User GS would need to be saved/restored on syscall entry/exit
                return 0;

            case ARCH_GET_GS:
                if (addr == 0 || addr >= 0x800000000000UL)
                    return -Errno.EFAULT;
                *(ulong*)addr = thread->UserGsBase;
                return 0;

            default:
                return -Errno.EINVAL;
        }
    }

    /// <summary>
    /// set_tid_address(tidptr) - Set pointer for thread ID clearing on exit
    /// The kernel will write 0 to *tidptr and do a futex wake when the thread exits.
    /// This enables pthread_join() implementation.
    /// </summary>
    private static long SysSetTidAddress(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                          Process.Process* proc, Thread* thread)
    {
        uint* tidptr = (uint*)arg0;

        // Store the address - kernel will clear it and wake waiters on thread exit
        thread->ClearChildTid = tidptr;

        // Return the thread ID
        return thread->Id;
    }

    // ==================== Clone Flags (Linux-compatible) ====================
    private const ulong CLONE_VM = 0x00000100;            // Share address space
    private const ulong CLONE_FS = 0x00000200;            // Share filesystem info
    private const ulong CLONE_FILES = 0x00000400;         // Share file descriptor table
    private const ulong CLONE_SIGHAND = 0x00000800;       // Share signal handlers
    private const ulong CLONE_THREAD = 0x00010000;        // Same thread group
    private const ulong CLONE_SETTLS = 0x00080000;        // Set TLS for new thread
    private const ulong CLONE_PARENT_SETTID = 0x00100000; // Write TID to parent_tidptr
    private const ulong CLONE_CHILD_SETTID = 0x01000000;  // Write TID to child_tidptr
    private const ulong CLONE_CHILD_CLEARTID = 0x00200000; // Clear TID on exit (futex wake)

    /// <summary>
    /// clone(flags, child_stack, parent_tidptr, child_tidptr, tls) - Create a new thread
    /// Linux ABI for x86_64 clone:
    ///   arg0 (rdi) = flags
    ///   arg1 (rsi) = child_stack
    ///   arg2 (rdx) = parent_tidptr
    ///   arg3 (r10) = child_tidptr
    ///   arg4 (r8)  = tls
    /// Returns: child TID to parent, 0 to child
    /// </summary>
    private static long SysClone(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        ulong flags = (ulong)arg0;
        ulong childStack = (ulong)arg1;
        uint* parentTidptr = (uint*)arg2;
        uint* childTidptr = (uint*)arg3;
        ulong tls = (ulong)arg4;

        // For .NET threading, we expect thread-style clone with shared VM
        // Reject fork-style clone (would need COW, full process creation)
        if ((flags & CLONE_VM) == 0)
        {
            // Fork not yet supported
            return -Errno.ENOSYS;
        }

        // For CLONE_THREAD, CLONE_VM must be set (already checked)
        // Also expect CLONE_SIGHAND and CLONE_FS for full thread semantics
        bool isThread = (flags & CLONE_THREAD) != 0;

        // Allocate kernel stack for new thread (8KB)
        ulong kernelStack = PageAllocator.AllocatePages(2);
        if (kernelStack == 0)
            return -Errno.ENOMEM;

        // Allocate thread structure
        var newThread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (newThread == null)
        {
            PageAllocator.FreePage(kernelStack);
            PageAllocator.FreePage(kernelStack + 4096);
            return -Errno.ENOMEM;
        }

        // Allocate extended state area for FPU/SSE
        byte* extendedStateRaw = (byte*)HeapAllocator.Alloc(512 + 64);
        if (extendedStateRaw == null)
        {
            HeapAllocator.Free(newThread);
            PageAllocator.FreePage(kernelStack);
            PageAllocator.FreePage(kernelStack + 4096);
            return -Errno.ENOMEM;
        }

        // Align extended state to 64 bytes
        ulong aligned = ((ulong)extendedStateRaw + 63) & ~63UL;
        newThread->ExtendedStateRaw = extendedStateRaw;
        newThread->ExtendedState = (byte*)aligned;
        newThread->ExtendedStateSize = 512;

        // Initialize FPU state
        for (int i = 0; i < 512; i++)
            newThread->ExtendedState[i] = 0;

        // Generate thread ID
        uint childTid = Scheduler.AllocateThreadId();

        // Set up the thread structure
        newThread->Id = childTid;
        newThread->State = ThreadState.Ready;
        newThread->Priority = thread->Priority;
        newThread->Process = proc;
        newThread->IsUserMode = true;

        // Kernel stack
        ulong stackTop = kernelStack + 2 * 4096;
        newThread->KernelStackTop = stackTop;
        newThread->StackBase = stackTop;
        newThread->StackLimit = kernelStack;
        newThread->StackSize = 2 * 4096;

        // The child stack is provided by the caller (user space allocated)
        // Set up context to resume in user mode at the syscall return point
        // Child returns 0 from clone
        newThread->UserRsp = childStack;
        newThread->UserRip = thread->UserRip; // Same instruction pointer - clone returns here

        // Set up CPU context for the child
        // The child will start in kernel mode at ClonedUserThreadWrapper,
        // which then uses iretq to jump to Ring 3 at UserRip/UserRsp
        newThread->Context = thread->Context;

        // Child gets return value 0 in rax (this will be passed to user mode)
        newThread->Context.Rax = 0;

        // Set RIP to the kernel-mode wrapper that transitions to Ring 3
        // load_context uses 'ret', so we need a kernel address
        newThread->Context.Rip = Scheduler.GetClonedUserThreadWrapperAddress();

        // Set kernel stack for the wrapper to use (top of kernel stack)
        newThread->Context.Rsp = newThread->KernelStackTop;

        // Kernel-mode context (wrapper runs in Ring 0 then transitions to Ring 3)
        newThread->Context.Cs = GDTSelectors.KernelCode;
        newThread->Context.Ss = GDTSelectors.KernelData;
        newThread->Context.Rflags = 0x202; // IF=1

        // Handle CLONE_SETTLS - set Thread Local Storage base
        if ((flags & CLONE_SETTLS) != 0)
        {
            newThread->UserFsBase = tls;
        }

        // Handle CLONE_PARENT_SETTID - write TID to parent's pointer
        if ((flags & CLONE_PARENT_SETTID) != 0 && parentTidptr != null)
        {
            *parentTidptr = childTid;
        }

        // Handle CLONE_CHILD_SETTID - write TID to child's pointer
        // Note: In shared VM, this writes to the same memory visible to both
        if ((flags & CLONE_CHILD_SETTID) != 0 && childTidptr != null)
        {
            *childTidptr = childTid;
        }

        // Handle CLONE_CHILD_CLEARTID - set up futex wake on exit
        if ((flags & CLONE_CHILD_CLEARTID) != 0)
        {
            newThread->ClearChildTid = childTidptr;
        }

        // Register thread with scheduler
        Scheduler.RegisterClonedThread(newThread);

        // Increment process thread count
        proc->ThreadCount++;

        // Return child TID to parent
        return childTid;
    }

    // ==================== Futex Operations ====================
    private const int FUTEX_WAIT = 0;
    private const int FUTEX_WAKE = 1;
    private const int FUTEX_WAIT_PRIVATE = 128;
    private const int FUTEX_WAKE_PRIVATE = 129;
    private const int FUTEX_PRIVATE_FLAG = 128;
    private const int FUTEX_CMD_MASK = 127;  // Mask out private flag

    // Simple futex wait queue - array of waiting threads
    // Key is the futex address, value is the waiting thread
    private const int MAX_FUTEX_WAITERS = 256;
    private static FutexWaiter* _futexWaiters;
    private static int _futexWaiterCount;
    private static SpinLock _futexLock;

    [StructLayout(LayoutKind.Sequential)]
    private struct FutexWaiter
    {
        public ulong Address;      // Futex address being waited on
        public Thread* Thread;     // Waiting thread
        public bool Active;        // Whether this entry is in use
    }

    /// <summary>
    /// Initialize the futex subsystem
    /// </summary>
    public static void InitFutex()
    {
        _futexWaiters = (FutexWaiter*)HeapAllocator.AllocZeroed((ulong)(sizeof(FutexWaiter) * MAX_FUTEX_WAITERS));
        _futexWaiterCount = 0;
        _futexLock = default;
    }

    /// <summary>
    /// futex(uaddr, op, val, timeout, uaddr2, val3) - Fast userspace locking
    /// Linux ABI for x86_64 futex:
    ///   arg0 (rdi) = uaddr (uint*)
    ///   arg1 (rsi) = op (futex operation)
    ///   arg2 (rdx) = val (value to compare/wake count)
    ///   arg3 (r10) = timeout (timespec* for WAIT, or uaddr2 for some ops)
    ///   arg4 (r8)  = uaddr2 (second address for some ops)
    ///   arg5 (r9)  = val3 (third value for some ops)
    /// </summary>
    private static long SysFutex(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        uint* uaddr = (uint*)arg0;
        int op = (int)arg1;
        uint val = (uint)arg2;
        // timespec* timeout = (timespec*)arg3;  // Not yet implemented
        // uint* uaddr2 = (uint*)arg4;
        // uint val3 = (uint)arg5;

        // Mask out the private flag - we treat all futexes as private for now
        // (single address space per process)
        int cmd = op & FUTEX_CMD_MASK;

        switch (cmd)
        {
            case FUTEX_WAIT:
                return FutexWait(uaddr, val, thread);

            case FUTEX_WAKE:
                return FutexWake(uaddr, (int)val);

            default:
                // Unsupported operation
                return -Errno.ENOSYS;
        }
    }

    /// <summary>
    /// FUTEX_WAIT: If *uaddr == val, block until woken
    /// </summary>
    private static long FutexWait(uint* uaddr, uint val, Thread* thread)
    {
        _futexLock.Acquire();

        // Check if the value still matches
        // This must be done atomically with adding to wait queue
        if (*uaddr != val)
        {
            _futexLock.Release();
            return -Errno.EAGAIN;
        }

        // Find a free waiter slot
        int slot = -1;
        for (int i = 0; i < MAX_FUTEX_WAITERS; i++)
        {
            if (!_futexWaiters[i].Active)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            _futexLock.Release();
            return -Errno.ENOMEM;  // Too many waiters
        }

        // Add to wait queue
        _futexWaiters[slot].Address = (ulong)uaddr;
        _futexWaiters[slot].Thread = thread;
        _futexWaiters[slot].Active = true;
        _futexWaiterCount++;

        // Block the thread
        thread->State = ThreadState.Blocked;
        thread->WaitObject = uaddr;

        _futexLock.Release();

        // Yield to scheduler - thread will be woken by FutexWake
        Scheduler.Schedule();

        // When we return, check if we were actually woken or interrupted
        // For now, just return 0 (success)
        return 0;
    }

    /// <summary>
    /// FUTEX_WAKE: Wake up to 'count' waiters on uaddr
    /// </summary>
    private static long FutexWake(uint* uaddr, int count)
    {
        if (count <= 0)
            return 0;

        _futexLock.Acquire();

        int woken = 0;
        ulong addr = (ulong)uaddr;

        for (int i = 0; i < MAX_FUTEX_WAITERS && woken < count; i++)
        {
            if (_futexWaiters[i].Active && _futexWaiters[i].Address == addr)
            {
                var waitingThread = _futexWaiters[i].Thread;

                // Remove from wait queue
                _futexWaiters[i].Active = false;
                _futexWaiters[i].Thread = null;
                _futexWaiterCount--;

                // Wake the thread
                if (waitingThread != null && waitingThread->State == ThreadState.Blocked)
                {
                    waitingThread->State = ThreadState.Ready;
                    waitingThread->WaitObject = null;
                    Scheduler.AddToReadyQueuePublic(waitingThread);
                    woken++;
                }
            }
        }

        _futexLock.Release();

        return woken;
    }

    /// <summary>
    /// Wake all futex waiters on an address (used for thread exit with CLONE_CHILD_CLEARTID)
    /// </summary>
    public static void FutexWakeAll(uint* uaddr)
    {
        FutexWake(uaddr, int.MaxValue);
    }

    // ==================== File Operations ====================

    private static long SysRead(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For stdin, read from debug console (serial port)
        if (fd == StdFd.Stdin)
        {
            if (count <= 0)
                return 0;

            // Read characters until newline or buffer full
            // This provides line-buffered input like a terminal
            int bytesRead = 0;
            while (bytesRead < count)
            {
                byte b = DebugConsole.ReadByte();

                // Echo the character back
                DebugConsole.WriteByte(b);

                // Handle backspace
                if (b == 0x7F || b == 0x08)
                {
                    if (bytesRead > 0)
                    {
                        bytesRead--;
                        // Erase character on terminal: backspace, space, backspace
                        DebugConsole.WriteByte(0x08);
                        DebugConsole.WriteByte((byte)' ');
                        DebugConsole.WriteByte(0x08);
                    }
                    continue;
                }

                // Store the byte
                buf[bytesRead++] = b;

                // Return on newline (CR or LF)
                if (b == '\r' || b == '\n')
                {
                    // Echo newline if we got CR
                    if (b == '\r')
                        DebugConsole.WriteByte((byte)'\n');
                    // Convert CR to LF for Unix convention
                    buf[bytesRead - 1] = (byte)'\n';
                    break;
                }
            }
            return bytesRead;
        }

        if (fdEntry->Ops == null || fdEntry->Ops->Read == null)
            return -Errno.ENOTSUP;

        return fdEntry->Ops->Read(fdEntry, buf, count);
    }

    private static long SysWrite(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For stdout/stderr, write to debug console
        if (fd == StdFd.Stdout || fd == StdFd.Stderr)
        {
            for (int i = 0; i < count; i++)
            {
                DebugConsole.WriteChar((char)buf[i]);
            }
            return count;
        }

        if (fdEntry->Ops == null || fdEntry->Ops->Write == null)
            return -Errno.ENOTSUP;

        return fdEntry->Ops->Write(fdEntry, buf, count);
    }

    // VFS file operations table (static to keep it alive)
    private static FileOps _vfsFileOps;
    private static bool _vfsOpsInitialized;

    private static void InitVfsFileOps()
    {
        if (_vfsOpsInitialized)
            return;

        _vfsFileOps.Read = &VfsFileRead;
        _vfsFileOps.Write = &VfsFileWrite;
        _vfsFileOps.Seek = &VfsFileSeek;
        _vfsFileOps.Close = &VfsFileClose;
        _vfsOpsInitialized = true;
    }

    [UnmanagedCallersOnly]
    private static int VfsFileRead(FileDescriptor* fd, byte* buf, int count)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Read(vfsHandle, buf, count);
    }

    [UnmanagedCallersOnly]
    private static int VfsFileWrite(FileDescriptor* fd, byte* buf, int count)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Write(vfsHandle, buf, count);
    }

    [UnmanagedCallersOnly]
    private static long VfsFileSeek(FileDescriptor* fd, long offset, int whence)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Seek(vfsHandle, offset, whence);
    }

    [UnmanagedCallersOnly]
    private static int VfsFileClose(FileDescriptor* fd)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Close(vfsHandle);
    }

    // ==================== Directory Operations ====================

    // Directory operations table
    private static FileOps _dirOps;
    private static bool _dirOpsInitialized;

    private static void InitDirOps()
    {
        if (_dirOpsInitialized)
            return;

        _dirOps.Read = null;   // Can't read directories directly
        _dirOps.Write = null;  // Can't write directories directly
        _dirOps.Seek = &DirSeek;
        _dirOps.Close = &DirClose;
        _dirOpsInitialized = true;
    }

    [UnmanagedCallersOnly]
    private static long DirSeek(FileDescriptor* fd, long offset, int whence)
    {
        // Only support SEEK_SET to position 0 (rewind)
        if (whence == 0 && offset == 0)
        {
            DirectoryHandle* dirHandle = (DirectoryHandle*)fd->Data;
            if (dirHandle != null)
            {
                dirHandle->Position = 0;
                return 0;
            }
        }
        return -Errno.EINVAL;
    }

    [UnmanagedCallersOnly]
    private static int DirClose(FileDescriptor* fd)
    {
        DirectoryHandle* dirHandle = (DirectoryHandle*)fd->Data;
        if (dirHandle != null)
        {
            HeapAllocator.Free(dirHandle);
            fd->Data = null;
        }
        return 0;
    }

    // ==================== Pipe Operations ====================

    // Pipe operations tables (read and write ends have different ops)
    private static FileOps _pipeReadOps;
    private static FileOps _pipeWriteOps;
    private static bool _pipeOpsInitialized;

    private static void InitPipeOps()
    {
        if (_pipeOpsInitialized)
            return;

        // Read end operations
        _pipeReadOps.Read = &PipeRead;
        _pipeReadOps.Write = null;  // Can't write to read end
        _pipeReadOps.Seek = &PipeSeek;
        _pipeReadOps.Close = &PipeReadClose;

        // Write end operations
        _pipeWriteOps.Read = null;  // Can't read from write end
        _pipeWriteOps.Write = &PipeWrite;
        _pipeWriteOps.Seek = &PipeSeek;
        _pipeWriteOps.Close = &PipeWriteClose;

        _pipeOpsInitialized = true;
    }

    [UnmanagedCallersOnly]
    private static int PipeRead(FileDescriptor* fd, byte* buf, int count)
    {
        if (buf == null || count < 0)
            return -Errno.EINVAL;

        if (count == 0)
            return 0;

        Pipe* pipe = (Pipe*)fd->Data;
        if (pipe == null)
            return -Errno.EBADF;

        pipe->Lock.Acquire();

        // Check for empty pipe
        if (pipe->Count == 0)
        {
            // If no writers left, return EOF
            if (pipe->Writers == 0)
            {
                pipe->Lock.Release();
                return 0;  // EOF
            }

            // Non-blocking mode: return EAGAIN
            if ((fd->Flags & FileFlags.NonBlock) != 0)
            {
                pipe->Lock.Release();
                return -Errno.EAGAIN;
            }

            // Blocking would require scheduler support - for now return 0 (EOF behavior)
            // TODO: Implement proper blocking with wait queues
            pipe->Lock.Release();
            return 0;
        }

        // Read up to count bytes from ring buffer
        int toRead = count < pipe->Count ? count : pipe->Count;
        int bytesRead = 0;

        byte* buffer = pipe->Buffer;
        while (bytesRead < toRead)
        {
            buf[bytesRead] = buffer[pipe->ReadPos];
            pipe->ReadPos = (pipe->ReadPos + 1) % Pipe.BufferSize;
            bytesRead++;
        }

        pipe->Count -= bytesRead;
        pipe->Lock.Release();

        return bytesRead;
    }

    [UnmanagedCallersOnly]
    private static int PipeWrite(FileDescriptor* fd, byte* buf, int count)
    {
        if (buf == null || count < 0)
            return -Errno.EINVAL;

        if (count == 0)
            return 0;

        Pipe* pipe = (Pipe*)fd->Data;
        if (pipe == null)
            return -Errno.EBADF;

        pipe->Lock.Acquire();

        // Check for broken pipe (no readers)
        if (pipe->Readers == 0)
        {
            pipe->Lock.Release();
            return -Errno.EPIPE;
        }

        // Check for full pipe
        int available = Pipe.BufferSize - pipe->Count;
        if (available == 0)
        {
            // Non-blocking mode: return EAGAIN
            if ((fd->Flags & FileFlags.NonBlock) != 0)
            {
                pipe->Lock.Release();
                return -Errno.EAGAIN;
            }

            // Blocking would require scheduler support - for now partial write
            // TODO: Implement proper blocking with wait queues
            pipe->Lock.Release();
            return 0;
        }

        // Write up to available space
        int toWrite = count < available ? count : available;
        int bytesWritten = 0;

        byte* buffer = pipe->Buffer;
        while (bytesWritten < toWrite)
        {
            buffer[pipe->WritePos] = buf[bytesWritten];
            pipe->WritePos = (pipe->WritePos + 1) % Pipe.BufferSize;
            bytesWritten++;
        }

        pipe->Count += bytesWritten;
        pipe->Lock.Release();

        return bytesWritten;
    }

    [UnmanagedCallersOnly]
    private static long PipeSeek(FileDescriptor* fd, long offset, int whence)
    {
        // Pipes don't support seeking
        return -Errno.ESPIPE;
    }

    [UnmanagedCallersOnly]
    private static int PipeReadClose(FileDescriptor* fd)
    {
        Pipe* pipe = (Pipe*)fd->Data;
        if (pipe == null)
            return 0;

        pipe->Lock.Acquire();
        pipe->Readers--;

        // If no more references, free the pipe
        if (pipe->Readers == 0 && pipe->Writers == 0)
        {
            pipe->Lock.Release();
            HeapAllocator.Free(pipe);
        }
        else
        {
            pipe->Lock.Release();
        }

        return 0;
    }

    [UnmanagedCallersOnly]
    private static int PipeWriteClose(FileDescriptor* fd)
    {
        Pipe* pipe = (Pipe*)fd->Data;
        if (pipe == null)
            return 0;

        pipe->Lock.Acquire();
        pipe->Writers--;

        // If no more references, free the pipe
        if (pipe->Readers == 0 && pipe->Writers == 0)
        {
            pipe->Lock.Release();
            HeapAllocator.Free(pipe);
        }
        else
        {
            pipe->Lock.Release();
        }

        return 0;
    }

    private static long SysOpen(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        int flags = (int)arg1;
        int mode = (int)arg2;

        if (path == null)
            return -Errno.EFAULT;

        // Check if opening a directory
        bool isDirectory = (flags & (int)FileFlags.IsDirectory) != 0;

        if (isDirectory)
        {
            // Opening a directory - allocate DirectoryHandle and store path
            InitDirOps();

            // Get path length
            int pathLen = 0;
            while (path[pathLen] != 0 && pathLen < 255)
                pathLen++;

            if (pathLen == 0)
                return -Errno.EINVAL;

            // Allocate directory handle
            DirectoryHandle* dirHandle = (DirectoryHandle*)HeapAllocator.AllocZeroed((ulong)sizeof(DirectoryHandle));
            if (dirHandle == null)
                return -Errno.ENOMEM;

            // Copy path
            for (int i = 0; i < pathLen; i++)
                dirHandle->Path[i] = path[i];
            dirHandle->Path[pathLen] = 0;
            dirHandle->Position = 0;

            // Allocate file descriptor
            int fd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, 0);
            if (fd < 0)
            {
                HeapAllocator.Free(dirHandle);
                return fd;
            }

            // Set up file descriptor for directory
            var fdEntry = &proc->FdTable[fd];
            fdEntry->Type = FileType.Directory;
            fdEntry->Flags = (FileFlags)flags;
            fdEntry->RefCount = 1;
            fdEntry->Offset = 0;
            fdEntry->Data = dirHandle;
            fdEntry->Ops = (FileOps*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref _dirOps);

            return fd;
        }

        // Regular file - use VFS
        InitVfsFileOps();

        // Open through VFS
        int vfsHandle = VFS.Open(path, flags, mode);
        if (vfsHandle < 0)
            return vfsHandle;  // Return error code

        // Allocate file descriptor
        int fd2 = FdTable.Allocate(proc->FdTable, proc->FdTableSize, 0);
        if (fd2 < 0)
        {
            VFS.Close(vfsHandle);
            return fd2;
        }

        // Set up file descriptor
        var fdEntry2 = &proc->FdTable[fd2];
        fdEntry2->Type = FileType.Regular;
        fdEntry2->Flags = (FileFlags)flags;
        fdEntry2->RefCount = 1;
        fdEntry2->Offset = 0;
        fdEntry2->Data = (void*)(long)vfsHandle;  // Store VFS handle
        fdEntry2->Ops = (FileOps*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref _vfsFileOps);

        return fd2;
    }

    private static long SysClose(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        return FdTable.Close(fdEntry);
    }

    /// <summary>
    /// stat(path, buf) - Get file status by path
    /// </summary>
    private static long SysStat(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        Stat* buf = (Stat*)arg1;

        if (path == null || buf == null)
            return -Errno.EFAULT;

        // Try to open the file to get its information
        int vfsHandle = VFS.Open(path, 0, 0);  // O_RDONLY
        if (vfsHandle < 0)
            return vfsHandle;  // Return the error (ENOENT, etc.)

        // Fill in stat structure with what we know
        FillStatFromVfs(buf, vfsHandle, FileType.Regular, proc);

        // Close the file
        VFS.Close(vfsHandle);

        return 0;
    }

    /// <summary>
    /// fstat(fd, buf) - Get file status by file descriptor
    /// </summary>
    private static long SysFstat(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        Stat* buf = (Stat*)arg1;

        if (buf == null)
            return -Errno.EFAULT;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // Fill stat structure based on file descriptor type
        FillStatFromFd(buf, fdEntry, proc);

        return 0;
    }

    /// <summary>
    /// lstat(path, buf) - Get file status (don't follow symlinks)
    /// Currently same as stat since we don't have symlinks yet
    /// </summary>
    private static long SysLstat(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // For now, just call stat - we don't have symlinks yet
        return SysStat(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    /// <summary>
    /// Fill a stat structure from a VFS file handle
    /// </summary>
    private static void FillStatFromVfs(Stat* buf, int vfsHandle, FileType type, Process.Process* proc)
    {
        // Clear the structure
        *buf = default;

        // Get file size by seeking to end
        long savedPos = VFS.Seek(vfsHandle, 0, (int)SeekOrigin.Current);
        long size = VFS.Seek(vfsHandle, 0, (int)SeekOrigin.End);
        VFS.Seek(vfsHandle, savedPos, (int)SeekOrigin.Set);

        // Device ID (use 1 for root device)
        buf->st_dev = 1;

        // Inode number (use the VFS handle as a fake inode for now)
        buf->st_ino = (ulong)vfsHandle + 1;

        // Number of hard links (always 1 for now)
        buf->st_nlink = 1;

        // File mode: type + permissions
        buf->st_mode = StatMode.S_IFREG | 0x1B6;  // Regular file + rw-rw-rw- (0666)

        // Owner (from process)
        buf->st_uid = proc->Uid;
        buf->st_gid = proc->Gid;

        // Device ID for special files (0 for regular files)
        buf->st_rdev = 0;

        // File size
        buf->st_size = size >= 0 ? size : 0;

        // Block size (use 4K, typical page size)
        buf->st_blksize = 4096;

        // Blocks (size / 512, rounded up)
        buf->st_blocks = (buf->st_size + 511) / 512;

        // Timestamps (use current time placeholder - all zeros for now)
        // TODO: Get actual timestamps from filesystem
        buf->st_atime = 0;
        buf->st_atime_nsec = 0;
        buf->st_mtime = 0;
        buf->st_mtime_nsec = 0;
        buf->st_ctime = 0;
        buf->st_ctime_nsec = 0;
    }

    /// <summary>
    /// Fill a stat structure from a file descriptor
    /// </summary>
    private static void FillStatFromFd(Stat* buf, FileDescriptor* fd, Process.Process* proc)
    {
        // Clear the structure
        *buf = default;

        // Device ID
        buf->st_dev = 1;

        // Inode (use pointer address as fake inode)
        buf->st_ino = (ulong)fd->Data + 1;

        // Number of hard links
        buf->st_nlink = 1;

        // File mode based on file type
        switch (fd->Type)
        {
            case FileType.Regular:
                buf->st_mode = StatMode.S_IFREG | 0x1B6;  // rw-rw-rw-
                break;
            case FileType.Directory:
                buf->st_mode = StatMode.S_IFDIR | 0x1ED;  // rwxr-xr-x
                break;
            case FileType.Pipe:
                buf->st_mode = StatMode.S_IFIFO | 0x1B6;  // rw-rw-rw-
                break;
            case FileType.Socket:
                buf->st_mode = StatMode.S_IFSOCK | 0x1B6;
                break;
            case FileType.Device:
            case FileType.Terminal:
                buf->st_mode = StatMode.S_IFCHR | 0x1B6;  // Character device
                break;
            default:
                buf->st_mode = StatMode.S_IFREG | 0x1B6;
                break;
        }

        // Owner
        buf->st_uid = proc->Uid;
        buf->st_gid = proc->Gid;

        // Device ID for special files
        if (fd->Type == FileType.Device || fd->Type == FileType.Terminal)
            buf->st_rdev = 1;
        else
            buf->st_rdev = 0;

        // For regular files, try to get size via seek
        if (fd->Type == FileType.Regular && fd->Ops != null && fd->Ops->Seek != null)
        {
            long savedPos = fd->Offset;
            long size = fd->Ops->Seek(fd, 0, (int)SeekOrigin.End);
            fd->Ops->Seek(fd, savedPos, (int)SeekOrigin.Set);
            buf->st_size = size >= 0 ? size : 0;
        }
        else
        {
            buf->st_size = 0;
        }

        // Block size
        buf->st_blksize = 4096;

        // Blocks
        buf->st_blocks = (buf->st_size + 511) / 512;

        // Timestamps (placeholder)
        buf->st_atime = 0;
        buf->st_mtime = 0;
        buf->st_ctime = 0;
    }

    private static long SysLseek(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        long offset = arg1;
        int whence = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (fdEntry->Ops == null || fdEntry->Ops->Seek == null)
            return -Errno.ESPIPE;

        return fdEntry->Ops->Seek(fdEntry, offset, whence);
    }

    private static long SysPipe(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int* pipefd = (int*)arg0;

        if (pipefd == null)
            return -Errno.EFAULT;

        // Initialize pipe operations if needed
        InitPipeOps();

        // Allocate the pipe structure
        Pipe* pipe = (Pipe*)HeapAllocator.AllocZeroed((ulong)sizeof(Pipe));
        if (pipe == null)
            return -Errno.ENOMEM;

        // Initialize pipe
        pipe->ReadPos = 0;
        pipe->WritePos = 0;
        pipe->Count = 0;
        pipe->Readers = 1;
        pipe->Writers = 1;
        pipe->Lock = new SpinLock();

        // Allocate two file descriptors
        int readFd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, 0);
        if (readFd < 0)
        {
            HeapAllocator.Free(pipe);
            return readFd;
        }

        int writeFd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, readFd + 1);
        if (writeFd < 0)
        {
            proc->FdTable[readFd] = default;
            HeapAllocator.Free(pipe);
            return writeFd;
        }

        // Set up read end (pipefd[0])
        var readEntry = &proc->FdTable[readFd];
        readEntry->Type = FileType.Pipe;
        readEntry->Flags = FileFlags.ReadOnly;
        readEntry->RefCount = 1;
        readEntry->Offset = 0;
        readEntry->Data = pipe;
        readEntry->Ops = (FileOps*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref _pipeReadOps);

        // Set up write end (pipefd[1])
        var writeEntry = &proc->FdTable[writeFd];
        writeEntry->Type = FileType.Pipe;
        writeEntry->Flags = FileFlags.WriteOnly;
        writeEntry->RefCount = 1;
        writeEntry->Offset = 0;
        writeEntry->Data = pipe;
        writeEntry->Ops = (FileOps*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref _pipeWriteOps);

        // Return fd numbers to caller
        pipefd[0] = readFd;
        pipefd[1] = writeFd;

        return 0;
    }

    private static long SysDup(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                Process.Process* proc, Thread* thread)
    {
        int oldfd = (int)arg0;
        return FdTable.Dup(proc->FdTable, proc->FdTableSize, oldfd);
    }

    private static long SysDup2(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int oldfd = (int)arg0;
        int newfd = (int)arg1;
        return FdTable.Dup2(proc->FdTable, proc->FdTableSize, oldfd, newfd);
    }

    private static long SysFcntl(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        int cmd = (int)arg1;
        long arg = arg2;

        // Validate fd
        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        FileDescriptor* fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        switch (cmd)
        {
            case FcntlCmd.F_DUPFD:
            {
                // Duplicate fd to lowest available >= arg
                int minFd = (int)arg;
                if (minFd < 0 || minFd >= proc->FdTableSize)
                    return -Errno.EINVAL;

                int newFd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, minFd);
                if (newFd < 0)
                    return newFd;

                proc->FdTable[newFd] = *fdEntry;
                proc->FdTable[newFd].RefCount++;
                // Clear CLOEXEC on the new fd (F_DUPFD behavior)
                proc->FdTable[newFd].Flags &= ~FileFlags.CloseOnExec;

                if (fdEntry->Ops != null && fdEntry->Ops->Dup != null)
                    fdEntry->Ops->Dup(fdEntry);

                return newFd;
            }

            case FcntlCmd.F_DUPFD_CLOEXEC:
            {
                // Duplicate fd to lowest available >= arg, with CLOEXEC set
                int minFd = (int)arg;
                if (minFd < 0 || minFd >= proc->FdTableSize)
                    return -Errno.EINVAL;

                int newFd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, minFd);
                if (newFd < 0)
                    return newFd;

                proc->FdTable[newFd] = *fdEntry;
                proc->FdTable[newFd].RefCount++;
                // Set CLOEXEC on the new fd
                proc->FdTable[newFd].Flags |= FileFlags.CloseOnExec;

                if (fdEntry->Ops != null && fdEntry->Ops->Dup != null)
                    fdEntry->Ops->Dup(fdEntry);

                return newFd;
            }

            case FcntlCmd.F_GETFD:
                // Return FD_CLOEXEC if close-on-exec is set
                return (fdEntry->Flags & FileFlags.CloseOnExec) != 0 ? FdFlags.FD_CLOEXEC : 0;

            case FcntlCmd.F_SETFD:
                // Set or clear close-on-exec flag
                if ((arg & FdFlags.FD_CLOEXEC) != 0)
                    fdEntry->Flags |= FileFlags.CloseOnExec;
                else
                    fdEntry->Flags &= ~FileFlags.CloseOnExec;
                return 0;

            case FcntlCmd.F_GETFL:
                // Return file status flags (access mode + O_APPEND, O_NONBLOCK, etc.)
                return (long)(fdEntry->Flags & (FileFlags.AccessMask | FileFlags.Append |
                                                FileFlags.NonBlock | FileFlags.Sync |
                                                FileFlags.Async | FileFlags.Direct));

            case FcntlCmd.F_SETFL:
            {
                // Only certain flags can be changed: O_APPEND, O_NONBLOCK, O_ASYNC, O_DIRECT
                FileFlags changeable = FileFlags.Append | FileFlags.NonBlock |
                                       FileFlags.Async | FileFlags.Direct;
                FileFlags newFlags = (FileFlags)arg & changeable;

                // Clear changeable flags and set new ones
                fdEntry->Flags = (fdEntry->Flags & ~changeable) | newFlags;
                return 0;
            }

            case FcntlCmd.F_GETLK:
            case FcntlCmd.F_SETLK:
            case FcntlCmd.F_SETLKW:
                // File locking not implemented yet
                return -Errno.ENOSYS;

            case FcntlCmd.F_GETOWN:
            case FcntlCmd.F_SETOWN:
                // Signal ownership not implemented yet
                return -Errno.ENOSYS;

            default:
                return -Errno.EINVAL;
        }
    }

    private static long SysGetcwd(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* buf = (byte*)arg0;
        int size = (int)arg1;

        if (buf == null || size < 1)
            return -Errno.EINVAL;

        // Copy cwd to buffer
        int len = 0;
        while (len < 255 && proc->Cwd[len] != 0)
            len++;

        if (len + 1 > size)
            return -Errno.ERANGE;

        for (int i = 0; i <= len; i++)
            buf[i] = proc->Cwd[i];

        return (long)buf;
    }

    private static long SysChdir(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;

        // TODO: Validate path exists and is a directory
        // For now, just copy the path
        int len = 0;
        while (len < 255 && path[len] != 0)
        {
            proc->Cwd[len] = path[len];
            len++;
        }
        proc->Cwd[len] = 0;

        return 0;
    }

    private static long SysMkdir(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        int mode = (int)arg1;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_mkdirHandler != null)
            return _mkdirHandler(path, mode);

        return -Errno.ENOSYS;
    }

    private static long SysRmdir(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_rmdirHandler != null)
            return _rmdirHandler(path);

        return -Errno.ENOSYS;
    }

    private static long SysUnlink(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_unlinkHandler != null)
            return _unlinkHandler(path);

        return -Errno.ENOSYS;
    }

    private static long SysGetdents(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;

        if (buf == null)
            return -Errno.EFAULT;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (fdEntry->Type != FileType.Directory)
            return -Errno.ENOTDIR;

        // Get the directory handle with path
        DirectoryHandle* dirHandle = (DirectoryHandle*)fdEntry->Data;
        if (dirHandle == null)
            return -Errno.EBADF;

        // Check if handler is registered
        if (_getdentsHandler == null)
            return -Errno.ENOSYS;

        // Call the DDK getdents handler with the directory path
        // Note: dirHandle->Path is a fixed buffer, so we can get its address directly
        byte* pathPtr = &dirHandle->Path[0];
        long offset = dirHandle->Position;
        int result = _getdentsHandler(pathPtr, buf, count, &offset);
        if (result >= 0)
            dirHandle->Position = offset;
        return result;
    }

    private static long SysPoll(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        PollFd* fds = (PollFd*)arg0;
        int nfds = (int)arg1;
        int timeout = (int)arg2;

        if (nfds < 0)
            return -Errno.EINVAL;

        if (nfds > 0 && fds == null)
            return -Errno.EFAULT;

        // Simple implementation: check each fd and set revents
        int ready = 0;
        for (int i = 0; i < nfds; i++)
        {
            fds[i].revents = PollEvents.None;
            int fd = fds[i].fd;

            if (fd < 0)
                continue;

            if (fd >= proc->FdTableSize)
            {
                fds[i].revents = PollEvents.POLLNVAL;
                ready++;
                continue;
            }

            var fdEntry = &proc->FdTable[fd];
            if (fdEntry->Type == FileType.None)
            {
                fds[i].revents = PollEvents.POLLNVAL;
                ready++;
                continue;
            }

            // For regular files and pipes, assume always ready for requested operations
            if ((fds[i].events & PollEvents.POLLIN) != 0)
            {
                // Check if data is available
                if (fdEntry->Type == FileType.Pipe)
                {
                    // For pipes, check if there's data
                    Pipe* pipeData = (Pipe*)fdEntry->Data;
                    if (pipeData != null && pipeData->Count > 0)
                    {
                        fds[i].revents |= PollEvents.POLLIN;
                        ready++;
                    }
                    else if (pipeData != null && pipeData->Writers == 0)
                    {
                        // Pipe has no writers - EOF
                        fds[i].revents |= PollEvents.POLLHUP;
                        ready++;
                    }
                }
                else
                {
                    // Regular files are always readable
                    fds[i].revents |= PollEvents.POLLIN;
                    ready++;
                }
            }

            if ((fds[i].events & PollEvents.POLLOUT) != 0)
            {
                // Check if we can write
                if (fdEntry->Type == FileType.Pipe)
                {
                    Pipe* pipeData = (Pipe*)fdEntry->Data;
                    if (pipeData != null && pipeData->Readers > 0)
                    {
                        fds[i].revents |= PollEvents.POLLOUT;
                        if ((fds[i].revents & PollEvents.POLLIN) == 0)
                            ready++;
                    }
                    else if (pipeData != null && pipeData->Readers == 0)
                    {
                        // No readers - would get SIGPIPE
                        fds[i].revents |= PollEvents.POLLERR;
                        if ((fds[i].revents & PollEvents.POLLIN) == 0)
                            ready++;
                    }
                }
                else
                {
                    // Regular files are always writable (if open for write)
                    if ((fdEntry->Flags & FileFlags.WriteOnly) != 0 ||
                        (fdEntry->Flags & FileFlags.ReadWrite) != 0)
                    {
                        fds[i].revents |= PollEvents.POLLOUT;
                        if ((fds[i].revents & PollEvents.POLLIN) == 0)
                            ready++;
                    }
                }
            }
        }

        // TODO: If timeout > 0 and ready == 0, we should block
        // For now, just return immediately
        return ready;
    }

    private static long SysAccess(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        int mode = (int)arg1;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_accessHandler != null)
            return _accessHandler(path, mode);

        return -Errno.ENOSYS;
    }

    private static long SysRename(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* oldpath = (byte*)arg0;
        byte* newpath = (byte*)arg1;

        if (oldpath == null || newpath == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_renameHandler != null)
            return _renameHandler(oldpath, newpath);

        return -Errno.ENOSYS;
    }

    private static long SysIoctl(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        uint cmd = (uint)arg1;
        void* argp = (void*)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // Call the file-specific ioctl if available
        if (fdEntry->Ops != null && fdEntry->Ops->Ioctl != null)
            return fdEntry->Ops->Ioctl(fdEntry, cmd, argp);

        // For TTY, handle basic TCGETS/TCSETS (terminal attributes)
        if (fdEntry->Type == FileType.Terminal || fd == StdFd.Stdin || fd == StdFd.Stdout || fd == StdFd.Stderr)
        {
            const uint TCGETS = 0x5401;
            const uint TCSETS = 0x5402;
            const uint TIOCGWINSZ = 0x5413;

            switch (cmd)
            {
                case TCGETS:
                case TCSETS:
                    // Pretend success for terminal attribute operations
                    return 0;

                case TIOCGWINSZ:
                    // Return default window size (80x24)
                    if (argp != null)
                    {
                        ushort* ws = (ushort*)argp;
                        ws[0] = 24;  // rows
                        ws[1] = 80;  // cols
                        ws[2] = 0;   // xpixel
                        ws[3] = 0;   // ypixel
                    }
                    return 0;
            }
        }

        return -Errno.ENOTTY;
    }

    private static long SysPread64(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;
        long offset = arg3;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (buf == null)
            return -Errno.EFAULT;

        if (fdEntry->Type == FileType.Pipe)
            return -Errno.ESPIPE;

        if (fdEntry->Ops == null || fdEntry->Ops->Read == null || fdEntry->Ops->Seek == null)
            return -Errno.ENOTSUP;

        // Save current position, seek to offset, read, restore position
        long savedPos = fdEntry->Ops->Seek(fdEntry, 0, (int)SeekOrigin.Current);
        if (savedPos < 0)
            return savedPos;

        long seekResult = fdEntry->Ops->Seek(fdEntry, offset, (int)SeekOrigin.Set);
        if (seekResult < 0)
            return seekResult;

        int result = fdEntry->Ops->Read(fdEntry, buf, count);

        // Restore position
        fdEntry->Ops->Seek(fdEntry, savedPos, (int)SeekOrigin.Set);

        return result;
    }

    private static long SysPwrite64(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;
        long offset = arg3;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (buf == null)
            return -Errno.EFAULT;

        if (fdEntry->Type == FileType.Pipe)
            return -Errno.ESPIPE;

        if (fdEntry->Ops == null || fdEntry->Ops->Write == null || fdEntry->Ops->Seek == null)
            return -Errno.ENOTSUP;

        // Save current position, seek to offset, write, restore position
        long savedPos = fdEntry->Ops->Seek(fdEntry, 0, (int)SeekOrigin.Current);
        if (savedPos < 0)
            return savedPos;

        long seekResult = fdEntry->Ops->Seek(fdEntry, offset, (int)SeekOrigin.Set);
        if (seekResult < 0)
            return seekResult;

        int result = fdEntry->Ops->Write(fdEntry, buf, count);

        // Restore position
        fdEntry->Ops->Seek(fdEntry, savedPos, (int)SeekOrigin.Set);

        return result;
    }

    // iovec structure for readv/writev
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct IoVec
    {
        public byte* iov_base;
        public ulong iov_len;
    }

    private static long SysReadv(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        IoVec* iov = (IoVec*)arg1;
        int iovcnt = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (iov == null || iovcnt <= 0)
            return -Errno.EINVAL;

        if (iovcnt > 1024)  // IOV_MAX
            return -Errno.EINVAL;

        if (fdEntry->Ops == null || fdEntry->Ops->Read == null)
            return -Errno.ENOTSUP;

        long totalRead = 0;
        for (int i = 0; i < iovcnt; i++)
        {
            if (iov[i].iov_base == null && iov[i].iov_len > 0)
                return -Errno.EFAULT;

            if (iov[i].iov_len == 0)
                continue;

            int result = fdEntry->Ops->Read(fdEntry, iov[i].iov_base, (int)iov[i].iov_len);
            if (result < 0)
            {
                if (totalRead > 0)
                    return totalRead;  // Return what we've read so far
                return result;
            }

            totalRead += result;

            // Short read - don't continue to next iovec
            if ((ulong)result < iov[i].iov_len)
                break;
        }

        return totalRead;
    }

    private static long SysWritev(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        IoVec* iov = (IoVec*)arg1;
        int iovcnt = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (iov == null || iovcnt <= 0)
            return -Errno.EINVAL;

        if (iovcnt > 1024)  // IOV_MAX
            return -Errno.EINVAL;

        // For stdout/stderr, write directly to debug console
        if (fd == StdFd.Stdout || fd == StdFd.Stderr)
        {
            long totalWritten = 0;
            for (int i = 0; i < iovcnt; i++)
            {
                if (iov[i].iov_base == null && iov[i].iov_len > 0)
                    return -Errno.EFAULT;

                for (ulong j = 0; j < iov[i].iov_len; j++)
                {
                    DebugConsole.WriteChar((char)iov[i].iov_base[j]);
                }
                totalWritten += (long)iov[i].iov_len;
            }
            return totalWritten;
        }

        if (fdEntry->Ops == null || fdEntry->Ops->Write == null)
            return -Errno.ENOTSUP;

        long totalWrite = 0;
        for (int i = 0; i < iovcnt; i++)
        {
            if (iov[i].iov_base == null && iov[i].iov_len > 0)
                return -Errno.EFAULT;

            if (iov[i].iov_len == 0)
                continue;

            int result = fdEntry->Ops->Write(fdEntry, iov[i].iov_base, (int)iov[i].iov_len);
            if (result < 0)
            {
                if (totalWrite > 0)
                    return totalWrite;
                return result;
            }

            totalWrite += result;

            // Short write - don't continue to next iovec
            if ((ulong)result < iov[i].iov_len)
                break;
        }

        return totalWrite;
    }

    private static long SysTruncate(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        long length = arg1;

        if (path == null)
            return -Errno.EFAULT;

        if (length < 0)
            return -Errno.EINVAL;

        // Call registered DDK handler if available
        if (_truncateHandler != null)
            return _truncateHandler(path, length);

        return -Errno.ENOSYS;
    }

    private static long SysFtruncate(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        long length = arg1;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (length < 0)
            return -Errno.EINVAL;

        // Can't truncate pipes
        if (fdEntry->Type == FileType.Pipe)
            return -Errno.EINVAL;

        // VFS truncate if we have a file handle
        if (fdEntry->Type == FileType.Regular && fdEntry->Data != null)
        {
            int vfsHandle = (int)(long)fdEntry->Data;
            return VFS.Truncate(vfsHandle, length);
        }

        return -Errno.EINVAL;
    }

    private static long SysFchdir(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (fdEntry->Type != FileType.Directory)
            return -Errno.ENOTDIR;

        // Get the directory handle with path
        DirectoryHandle* dirHandle = (DirectoryHandle*)fdEntry->Data;
        if (dirHandle == null)
            return -Errno.EBADF;

        // Copy path from directory handle to process cwd
        int len = 0;
        while (len < 255 && dirHandle->Path[len] != 0)
        {
            proc->Cwd[len] = dirHandle->Path[len];
            len++;
        }
        proc->Cwd[len] = 0;

        return 0;
    }

    private static long SysCreat(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // creat(path, mode) is equivalent to open(path, O_CREAT|O_WRONLY|O_TRUNC, mode)
        int flags = (int)(FileFlags.Create | FileFlags.WriteOnly | FileFlags.Truncate);
        return SysOpen(arg0, flags, arg1, arg3, arg4, arg5, proc, thread);
    }

    private static long SysLink(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        byte* oldpath = (byte*)arg0;
        byte* newpath = (byte*)arg1;

        if (oldpath == null || newpath == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_linkHandler != null)
            return _linkHandler(oldpath, newpath);

        return -Errno.ENOSYS;
    }

    private static long SysSymlink(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        byte* target = (byte*)arg0;
        byte* linkpath = (byte*)arg1;

        if (target == null || linkpath == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_symlinkHandler != null)
            return _symlinkHandler(target, linkpath);

        return -Errno.ENOSYS;
    }

    private static long SysReadlink(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        byte* buf = (byte*)arg1;
        int bufsiz = (int)arg2;

        if (path == null || buf == null)
            return -Errno.EFAULT;

        if (bufsiz <= 0)
            return -Errno.EINVAL;

        // Call registered DDK handler if available
        if (_readlinkHandler != null)
            return _readlinkHandler(path, buf, bufsiz);

        return -Errno.ENOSYS;
    }

    private static long SysChmod(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        int mode = (int)arg1;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_chmodHandler != null)
            return _chmodHandler(path, mode);

        return -Errno.ENOSYS;
    }

    private static long SysFchmod(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        int mode = (int)arg1;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For VFS files, we need to implement fchmod in VFS
        // For now, return success as permissions are not enforced
        return 0;
    }

    private static long SysChown(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        uint owner = (uint)arg1;
        uint group = (uint)arg2;

        if (path == null)
            return -Errno.EFAULT;

        // Call registered DDK handler if available
        if (_chownHandler != null)
            return _chownHandler(path, owner, group);

        return -Errno.ENOSYS;
    }

    private static long SysFchown(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        uint owner = (uint)arg1;
        uint group = (uint)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For VFS files, we need to implement fchown in VFS
        // For now, return success as ownership is not enforced
        return 0;
    }

    private static long SysLchown(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        // lchown is like chown but doesn't follow symlinks
        // Since we don't have symlinks yet, treat it the same as chown
        return SysChown(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    private static long SysDup3(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int oldfd = (int)arg0;
        int newfd = (int)arg1;
        int flags = (int)arg2;

        // dup3 doesn't allow oldfd == newfd (unlike dup2)
        if (oldfd == newfd)
            return -Errno.EINVAL;

        // Perform the dup2 operation
        long result = FdTable.Dup2(proc->FdTable, proc->FdTableSize, oldfd, newfd);
        if (result < 0)
            return result;

        // O_CLOEXEC is the only flag supported
        const int O_CLOEXEC = 0x80000;
        if ((flags & O_CLOEXEC) != 0)
        {
            proc->FdTable[newfd].Flags |= FileFlags.CloseOnExec;
        }

        return result;
    }

    // ==================== User/Group Identity ====================

    private static long SysGetuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Uid;
    }

    private static long SysGetgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Gid;
    }

    private static long SysGeteuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->Euid;
    }

    private static long SysGetegid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->Egid;
    }

    private static long SysSetuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        uint uid = (uint)arg0;

        // Root can set any UID
        if (proc->Euid == 0)
        {
            proc->Uid = uid;
            proc->Euid = uid;
            proc->Suid = uid;
            return 0;
        }

        // Non-root can only set UID to real, effective, or saved UID
        if (uid == proc->Uid || uid == proc->Euid || uid == proc->Suid)
        {
            proc->Euid = uid;
            return 0;
        }

        return -Errno.EPERM;
    }

    private static long SysSetgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        uint gid = (uint)arg0;

        // Root can set any GID
        if (proc->Euid == 0)
        {
            proc->Gid = gid;
            proc->Egid = gid;
            proc->Sgid = gid;
            return 0;
        }

        // Non-root can only set GID to real, effective, or saved GID
        if (gid == proc->Gid || gid == proc->Egid || gid == proc->Sgid)
        {
            proc->Egid = gid;
            return 0;
        }

        return -Errno.EPERM;
    }

    private static long SysUname(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        Utsname* buf = (Utsname*)arg0;

        if (buf == null)
            return -Errno.EFAULT;

        // Clear the structure first
        byte* p = (byte*)buf;
        for (int i = 0; i < sizeof(Utsname); i++)
            p[i] = 0;

        // Fill in system information
        CopyString(buf->sysname, "ProtonOS", Utsname.FieldLength);
        CopyString(buf->nodename, "proton", Utsname.FieldLength);
        CopyString(buf->release, "0.1.0", Utsname.FieldLength);
        CopyString(buf->version, "#1 SMP", Utsname.FieldLength);
        CopyString(buf->machine, "x86_64", Utsname.FieldLength);
        CopyString(buf->domainname, "(none)", Utsname.FieldLength);

        return 0;
    }

    private static void CopyString(byte* dest, string src, int maxLen)
    {
        int i = 0;
        while (i < src.Length && i < maxLen - 1)
        {
            dest[i] = (byte)src[i];
            i++;
        }
        dest[i] = 0;  // Null terminate
    }

    private static long SysSysinfo(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        Sysinfo* info = (Sysinfo*)arg0;

        if (info == null)
            return -Errno.EFAULT;

        // Clear the structure
        byte* p = (byte*)info;
        for (int i = 0; i < sizeof(Sysinfo); i++)
            p[i] = 0;

        // Uptime in seconds
        if (X64.HPET.IsInitialized)
        {
            ulong nanoseconds = X64.HPET.TicksToNanoseconds(X64.HPET.ReadCounter());
            info->uptime = (long)(nanoseconds / 1_000_000_000);
        }

        // Memory information
        if (PageAllocator.IsInitialized)
        {
            info->totalram = PageAllocator.TotalMemory;
            info->freeram = PageAllocator.FreeMemory;
        }

        // Load averages (not implemented, set to 0)
        info->loads0 = 0;
        info->loads1 = 0;
        info->loads2 = 0;

        // Shared/buffer memory (not tracked separately)
        info->sharedram = 0;
        info->bufferram = 0;

        // Swap (not implemented)
        info->totalswap = 0;
        info->freeswap = 0;

        // Process count
        info->procs = (ushort)ProcessTable.ProcessCount;

        // High memory (not used on x86-64)
        info->totalhigh = 0;
        info->freehigh = 0;

        // Memory unit (1 byte)
        info->mem_unit = 1;

        // Debug output for visibility
        DebugConsole.WriteLine("[sysinfo] System Information:");
        DebugConsole.Write("  Uptime: ");
        DebugConsole.WriteDecimal((int)info->uptime);
        DebugConsole.WriteLine(" seconds");
        DebugConsole.Write("  Total RAM: ");
        DebugConsole.WriteDecimal((int)(info->totalram / (1024 * 1024)));
        DebugConsole.WriteLine(" MB");
        DebugConsole.Write("  Free RAM: ");
        DebugConsole.WriteDecimal((int)(info->freeram / (1024 * 1024)));
        DebugConsole.WriteLine(" MB");
        DebugConsole.Write("  Processes: ");
        DebugConsole.WriteDecimal(info->procs);
        DebugConsole.WriteLine("");

        return 0;
    }

    // ==================== Time ====================

    private static long SysClockGettime(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                         Process.Process* proc, Thread* thread)
    {
        int clockId = (int)arg0;
        Timespec* ts = (Timespec*)arg1;

        if (ts == null)
            return -Errno.EFAULT;

        ulong nanoseconds;

        switch (clockId)
        {
            case ClockId.CLOCK_MONOTONIC:
            case ClockId.CLOCK_MONOTONIC_RAW:
            case ClockId.CLOCK_MONOTONIC_COARSE:
            case ClockId.CLOCK_BOOTTIME:
                // Return time since boot using HPET
                if (!X64.HPET.IsInitialized)
                    return -Errno.ENODEV;
                nanoseconds = X64.HPET.TicksToNanoseconds(X64.HPET.ReadCounter());
                break;

            case ClockId.CLOCK_REALTIME:
            case ClockId.CLOCK_REALTIME_COARSE:
                // For now, return monotonic time (no RTC support yet)
                // TODO: Add RTC support for wall-clock time
                if (!X64.HPET.IsInitialized)
                    return -Errno.ENODEV;
                nanoseconds = X64.HPET.TicksToNanoseconds(X64.HPET.ReadCounter());
                break;

            case ClockId.CLOCK_PROCESS_CPUTIME_ID:
            case ClockId.CLOCK_THREAD_CPUTIME_ID:
                // Not yet implemented
                return -Errno.EINVAL;

            default:
                return -Errno.EINVAL;
        }

        ts->tv_sec = (long)(nanoseconds / 1_000_000_000);
        ts->tv_nsec = (long)(nanoseconds % 1_000_000_000);

        return 0;
    }

    private static long SysClockGetres(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                        Process.Process* proc, Thread* thread)
    {
        int clockId = (int)arg0;
        Timespec* ts = (Timespec*)arg1;

        // Validate clock ID
        switch (clockId)
        {
            case ClockId.CLOCK_MONOTONIC:
            case ClockId.CLOCK_MONOTONIC_RAW:
            case ClockId.CLOCK_BOOTTIME:
            case ClockId.CLOCK_REALTIME:
                break;
            case ClockId.CLOCK_MONOTONIC_COARSE:
            case ClockId.CLOCK_REALTIME_COARSE:
                // Coarse clocks have lower resolution
                if (ts != null)
                {
                    ts->tv_sec = 0;
                    ts->tv_nsec = 1_000_000;  // 1ms resolution
                }
                return 0;
            default:
                return -Errno.EINVAL;
        }

        // ts can be null (just checking if clock is valid)
        if (ts != null)
        {
            // HPET typically provides nanosecond-level resolution
            ts->tv_sec = 0;
            ts->tv_nsec = 1;  // 1 nanosecond
        }

        return 0;
    }

    private static long SysGettimeofday(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                         Process.Process* proc, Thread* thread)
    {
        // struct timeval { time_t tv_sec; suseconds_t tv_usec; }
        // On x86-64, both are 64-bit (8 bytes each)
        long* tv = (long*)arg0;
        // arg1 is timezone pointer (deprecated, usually NULL)

        if (tv == null)
            return -Errno.EFAULT;

        if (!X64.HPET.IsInitialized)
            return -Errno.ENODEV;

        ulong nanoseconds = X64.HPET.TicksToNanoseconds(X64.HPET.ReadCounter());

        tv[0] = (long)(nanoseconds / 1_000_000_000);          // tv_sec
        tv[1] = (long)((nanoseconds % 1_000_000_000) / 1000); // tv_usec

        return 0;
    }

    private static long SysNanosleep(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        Timespec* req = (Timespec*)arg0;
        Timespec* rem = (Timespec*)arg1;  // Can be NULL

        if (req == null)
            return -Errno.EFAULT;

        // Validate timespec
        if (req->tv_nsec < 0 || req->tv_nsec >= 1_000_000_000)
            return -Errno.EINVAL;

        if (req->tv_sec < 0)
            return -Errno.EINVAL;

        if (!X64.HPET.IsInitialized)
            return -Errno.ENODEV;

        // Calculate total nanoseconds to sleep
        ulong totalNs = (ulong)req->tv_sec * 1_000_000_000 + (ulong)req->tv_nsec;

        // Use HPET busy-wait for sleep
        // TODO: Use scheduler sleep instead of busy-wait for better efficiency
        X64.HPET.BusyWaitNs(totalNs);

        // Since we don't have signals yet, sleep always completes fully
        // Set remaining time to zero if rem is provided
        if (rem != null)
        {
            rem->tv_sec = 0;
            rem->tv_nsec = 0;
        }

        return 0;
    }

    // Simple PRNG state for getrandom (xorshift64)
    private static ulong _prngState;
    private static bool _prngInitialized;

    private static ulong NextRandom()
    {
        // Initialize PRNG state from TSC if needed
        if (!_prngInitialized)
        {
            _prngState = CPU.ReadTsc();
            if (_prngState == 0)
                _prngState = 0x853c49e6748fea9bUL;  // Fallback seed
            _prngInitialized = true;
        }

        // xorshift64 algorithm
        ulong x = _prngState;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        _prngState = x;
        return x;
    }

    private static long SysGetrandom(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        byte* buf = (byte*)arg0;
        ulong buflen = (ulong)arg1;
        uint flags = (uint)arg2;

        // GRND_NONBLOCK = 1, GRND_RANDOM = 2
        const uint GRND_NONBLOCK = 1;
        const uint GRND_RANDOM = 2;

        if (buf == null)
            return -Errno.EFAULT;

        if (buflen == 0)
            return 0;

        // Limit to reasonable size per call
        if (buflen > 256)
            buflen = 256;

        // Mix in current time for additional entropy
        if (X64.HPET.IsInitialized)
        {
            ulong hpet = X64.HPET.ReadCounter();
            _prngState ^= hpet;
        }

        // Fill buffer with random bytes
        ulong remaining = buflen;
        ulong offset = 0;

        while (remaining > 0)
        {
            ulong rand = NextRandom();

            // Copy up to 8 bytes from this random value
            int toCopy = remaining >= 8 ? 8 : (int)remaining;
            for (int i = 0; i < toCopy; i++)
            {
                buf[offset++] = (byte)(rand & 0xFF);
                rand >>= 8;
            }
            remaining -= (ulong)toCopy;
        }

        return (long)buflen;
    }

    // ==================== Memory Management ====================

    private static long SysBrk(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                Process.Process* proc, Thread* thread)
    {
        ulong newBrk = (ulong)arg0;

        // If arg is 0, return current brk
        if (newBrk == 0)
            return (long)proc->HeapEnd;

        // Can't shrink below heap start
        if (newBrk < proc->HeapStart)
            return (long)proc->HeapEnd;

        // TODO: Allocate/deallocate pages as needed
        proc->HeapEnd = newBrk;
        return (long)proc->HeapEnd;
    }

    private static long SysMmap(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        // mmap(addr, length, prot, flags, fd, offset)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int prot = (int)arg2;
        int flags = (int)arg3;
        int fd = (int)arg4;
        long offset = arg5;

        const ulong PageSize = 4096;

        // Validate length
        if (length == 0)
            return -Errno.EINVAL;

        // Page-align length
        ulong alignedLength = (length + PageSize - 1) & ~(PageSize - 1);

        bool isAnonymous = (flags & MmapFlags.Anonymous) != 0;
        FileDescriptor* fdEntry = null;

        // Validate file descriptor for file-backed mappings
        if (!isAnonymous)
        {
            if (fd < 0 || fd >= proc->FdTableSize)
                return -Errno.EBADF;

            fdEntry = &proc->FdTable[fd];
            if (fdEntry->Type == FileType.None)
                return -Errno.EBADF;

            // Check that fd supports read (for mapping)
            if (fdEntry->Ops == null || fdEntry->Ops->Read == null)
                return -Errno.ENODEV;

            // Offset must be page-aligned
            if ((offset & (long)(PageSize - 1)) != 0)
                return -Errno.EINVAL;
        }

        // Determine mapping address
        ulong mapAddr;
        if ((flags & MmapFlags.Fixed) != 0)
        {
            // Use exact address requested
            if (addr == 0 || (addr & (PageSize - 1)) != 0)
                return -Errno.EINVAL;
            mapAddr = addr;
        }
        else
        {
            // Allocate from mmap region (below stack)
            if (proc->MmapBase == 0)
            {
                // Start mmap region below stack with some gap
                proc->MmapBase = proc->StackBottom - 0x10000000; // 256MB below stack
            }

            // Allocate from top down
            proc->MmapBase -= alignedLength;
            proc->MmapBase &= ~(PageSize - 1);
            mapAddr = proc->MmapBase;
        }

        // Convert protection to page flags
        ulong pageFlags = PageFlags.Present | PageFlags.User;
        if ((prot & MmapProt.Write) != 0)
            pageFlags |= PageFlags.Writable;
        if ((prot & MmapProt.Exec) == 0)
            pageFlags |= PageFlags.NoExecute;

        // Allocate and map pages
        ulong numPages = alignedLength / PageSize;
        for (ulong i = 0; i < numPages; i++)
        {
            ulong pagePhys = PageAllocator.AllocatePage();
            if (pagePhys == 0)
            {
                // Rollback: unmap already-mapped pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = mapAddr + j * PageSize;
                    ulong rollbackPhys = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, rollbackAddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, rollbackAddr);
                    if (rollbackPhys != 0)
                        PageAllocator.FreePage(rollbackPhys);
                }
                return -Errno.ENOMEM;
            }

            // Zero the page first
            CPU.MemZero((void*)pagePhys, PageSize);

            // Map the page
            ulong vaddr = mapAddr + i * PageSize;
            if (!AddressSpace.MapUserPage(proc->PageTableRoot, vaddr, pagePhys, pageFlags))
            {
                PageAllocator.FreePage(pagePhys);
                // Rollback previous pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = mapAddr + j * PageSize;
                    ulong rollbackPhys = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, rollbackAddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, rollbackAddr);
                    if (rollbackPhys != 0)
                        PageAllocator.FreePage(rollbackPhys);
                }
                return -Errno.ENOMEM;
            }
        }

        // For file-backed mappings, read file content into the mapped pages
        if (!isAnonymous && fdEntry != null)
        {
            // Save current file position
            long savedPos = -1;
            if (fdEntry->Ops->Seek != null)
            {
                savedPos = fdEntry->Ops->Seek(fdEntry, 0, 1); // SEEK_CUR
                fdEntry->Ops->Seek(fdEntry, offset, 0); // SEEK_SET to mapping offset
            }

            // Read file content into mapped memory
            ulong bytesToRead = length; // Use original length, not aligned
            ulong bytesRead = 0;

            while (bytesRead < bytesToRead)
            {
                ulong pageOffset = bytesRead / PageSize;
                ulong offsetInPage = bytesRead % PageSize;
                ulong vaddr = mapAddr + pageOffset * PageSize + offsetInPage;

                // Get physical address for this virtual address
                ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                if (physAddr == 0)
                    break;

                // Calculate how much to read (up to end of page or end of file region)
                ulong remainingInPage = PageSize - offsetInPage;
                ulong remainingTotal = bytesToRead - bytesRead;
                int toRead = (int)(remainingInPage < remainingTotal ? remainingInPage : remainingTotal);

                // Read directly into physical memory
                int result = fdEntry->Ops->Read(fdEntry, (byte*)physAddr, toRead);
                if (result <= 0)
                    break; // EOF or error

                bytesRead += (ulong)result;
            }

            // Restore file position
            if (savedPos >= 0 && fdEntry->Ops->Seek != null)
            {
                fdEntry->Ops->Seek(fdEntry, savedPos, 0); // SEEK_SET
            }
        }

        // Track the mapping
        var region = (MmapRegion*)HeapAllocator.AllocZeroed((ulong)sizeof(MmapRegion));
        if (region != null)
        {
            region->Start = mapAddr;
            region->Length = alignedLength;
            region->Prot = prot;
            region->Flags = flags;
            region->Fd = isAnonymous ? -1 : fd;
            region->FileOffset = offset;
            region->Next = proc->MmapRegions;
            proc->MmapRegions = region;
        }

        return (long)mapAddr;
    }

    private static long SysMunmap(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        // munmap(addr, length)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return -Errno.EINVAL;

        ulong unmapEnd = addr + length;

        // Find and handle overlapping regions
        MmapRegion** prevPtr = &proc->MmapRegions;
        MmapRegion* region = proc->MmapRegions;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps with the unmap range
            if (region->Start < unmapEnd && regionEnd > addr)
            {
                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < unmapEnd ? regionEnd : unmapEnd;

                // Unmap pages in the overlap and free physical memory
                for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                {
                    ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, vaddr);
                    if (physAddr != 0)
                        PageAllocator.FreePage(physAddr);
                }

                // Case 1: Entire region unmapped - remove it
                if (overlapStart == region->Start && overlapEnd == regionEnd)
                {
                    MmapRegion* toFree = region;
                    *prevPtr = region->Next;
                    region = region->Next;
                    HeapAllocator.Free(toFree);
                    continue;
                }
                // Case 2: Unmap from start - shrink region
                else if (overlapStart == region->Start)
                {
                    ulong shrinkBy = overlapEnd - region->Start;
                    region->Start = overlapEnd;
                    region->Length -= shrinkBy;
                    if (region->Fd >= 0)
                        region->FileOffset += (long)shrinkBy;
                }
                // Case 3: Unmap from end - shrink region
                else if (overlapEnd == regionEnd)
                {
                    region->Length = overlapStart - region->Start;
                }
                // Case 4: Unmap from middle - split into two regions
                else
                {
                    // Create new region for the part after the hole
                    var newRegion = (MmapRegion*)HeapAllocator.AllocZeroed((ulong)sizeof(MmapRegion));
                    if (newRegion != null)
                    {
                        newRegion->Start = overlapEnd;
                        newRegion->Length = regionEnd - overlapEnd;
                        newRegion->Prot = region->Prot;
                        newRegion->Flags = region->Flags;
                        newRegion->Fd = region->Fd;
                        newRegion->FileOffset = region->FileOffset + (long)(overlapEnd - region->Start);
                        newRegion->Next = region->Next;
                        region->Next = newRegion;
                    }
                    // Shrink original region to part before the hole
                    region->Length = overlapStart - region->Start;
                }
            }

            prevPtr = &region->Next;
            region = region->Next;
        }

        return 0;
    }

    private static long SysMprotect(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        // mprotect(addr, length, prot)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int prot = (int)arg2;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return 0; // Nothing to do

        ulong addrEnd = addr + length;

        // Convert protection to page flags
        ulong newFlags = PageFlags.Present | PageFlags.User;
        if ((prot & MmapProt.Write) != 0)
            newFlags |= PageFlags.Writable;
        if ((prot & MmapProt.Exec) == 0)
            newFlags |= PageFlags.NoExecute;

        // Find and update matching regions
        MmapRegion* region = proc->MmapRegions;
        bool foundRegion = false;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps with the protection range
            if (region->Start < addrEnd && regionEnd > addr)
            {
                foundRegion = true;

                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < addrEnd ? regionEnd : addrEnd;

                // Update page table entries for the overlap
                for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                {
                    // Get current physical address
                    ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                    if (physAddr != 0)
                    {
                        // Remap with new flags
                        AddressSpace.UnmapUserPage(proc->PageTableRoot, vaddr);
                        AddressSpace.MapUserPage(proc->PageTableRoot, vaddr, physAddr, newFlags);
                    }
                }

                // Update region protection if entire region is affected
                if (overlapStart == region->Start && overlapEnd == regionEnd)
                {
                    region->Prot = prot;
                }
                // TODO: Split region if partial protection change
            }

            region = region->Next;
        }

        // Linux mprotect succeeds even if no mapping exists
        return 0;
    }

    private static long SysMsync(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // msync(addr, length, flags)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int flags = (int)arg2;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return 0;

        // Must specify either MS_ASYNC or MS_SYNC (but not both)
        bool async = (flags & MsyncFlags.Async) != 0;
        bool sync = (flags & MsyncFlags.Sync) != 0;
        if (async && sync)
            return -Errno.EINVAL;
        if (!async && !sync)
            return -Errno.EINVAL;

        ulong addrEnd = addr + length;

        // Find overlapping MAP_SHARED regions with file backing
        MmapRegion* region = proc->MmapRegions;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps and is MAP_SHARED with file backing
            if (region->Start < addrEnd && regionEnd > addr &&
                (region->Flags & MmapFlags.Shared) != 0 &&
                region->Fd >= 0)
            {
                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < addrEnd ? regionEnd : addrEnd;

                // Get the file descriptor
                if (region->Fd >= 0 && region->Fd < proc->FdTableSize)
                {
                    var fdEntry = &proc->FdTable[region->Fd];
                    if (fdEntry->Type != FileType.None && fdEntry->Ops != null && fdEntry->Ops->Write != null)
                    {
                        // Calculate file offset for the overlap start
                        long fileOffset = region->FileOffset + (long)(overlapStart - region->Start);

                        // Seek to position if possible
                        if (fdEntry->Ops->Seek != null)
                        {
                            fdEntry->Ops->Seek(fdEntry, fileOffset, 0); // SEEK_SET
                        }

                        // Write each page back to the file
                        for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                        {
                            ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                            if (physAddr != 0)
                            {
                                // Calculate bytes to write (may be partial page at end)
                                ulong remaining = overlapEnd - vaddr;
                                int toWrite = remaining > PageSize ? (int)PageSize : (int)remaining;

                                fdEntry->Ops->Write(fdEntry, (byte*)physAddr, toWrite);
                            }
                        }
                    }
                }
            }

            region = region->Next;
        }

        return 0;
    }
}
