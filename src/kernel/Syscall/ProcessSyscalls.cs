// ProtonOS kernel - Process System Calls
// Fork, wait, and exec implementations.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.IO;
using ProtonOS.Process;
using ProtonOS.X64;

namespace ProtonOS.Syscall;

/// <summary>
/// Wait options flags
/// </summary>
[Flags]
public enum WaitOptions : int
{
    None = 0,
    WNOHANG = 1,      // Don't block if no child has exited
    WUNTRACED = 2,    // Also report stopped children
    WCONTINUED = 8,   // Also report continued children
}

/// <summary>
/// Wait status macros (for encoding/decoding status)
/// </summary>
public static class WaitStatus
{
    // Status encoding:
    // Normal exit:  (exitcode << 8) | 0x00
    // Killed by signal: (signum & 0x7F) | (coredump ? 0x80 : 0)
    // Stopped: (signum << 8) | 0x7F
    // Continued: 0xFFFF

    public static int MakeExited(int exitCode) => (exitCode & 0xFF) << 8;
    public static int MakeSignaled(int signum, bool coreDump) => (signum & 0x7F) | (coreDump ? 0x80 : 0);
    public static int MakeStopped(int signum) => ((signum & 0xFF) << 8) | 0x7F;
    public const int Continued = 0xFFFF;

    public static bool IsExited(int status) => (status & 0x7F) == 0;
    public static bool IsSignaled(int status) => ((status & 0x7F) + 1) >> 1 > 0 && !IsStopped(status);
    public static bool IsStopped(int status) => (status & 0xFF) == 0x7F;
    public static bool IsContinued(int status) => status == Continued;

    public static int ExitStatus(int status) => (status >> 8) & 0xFF;
    public static int TermSig(int status) => status & 0x7F;
    public static int StopSig(int status) => (status >> 8) & 0xFF;
}

/// <summary>
/// Process-related system call implementations
/// </summary>
public static unsafe class ProcessSyscalls
{
    /// <summary>
    /// Fork the current process
    /// </summary>
    /// <param name="parent">Parent process</param>
    /// <param name="parentThread">Parent's current thread</param>
    /// <returns>Child PID to parent, 0 to child, or negative error</returns>
    public static long Fork(Process.Process* parent, Thread* parentThread)
    {
        if (parent == null || parentThread == null)
            return -Errno.EINVAL;

        DebugConsole.WriteLine("[Fork] Starting fork...");

        // Allocate new process
        var child = ProcessTable.Allocate();
        if (child == null)
        {
            DebugConsole.WriteLine("[Fork] Failed to allocate child process");
            return -Errno.EAGAIN;
        }

        DebugConsole.Write("[Fork] Child PID: ");
        DebugConsole.WriteDecimal(child->Pid);
        DebugConsole.WriteLine();

        // Clone address space with copy-on-write
        ulong childPml4 = AddressSpace.CloneForFork(parent->PageTableRoot);
        if (childPml4 == 0)
        {
            DebugConsole.WriteLine("[Fork] Failed to clone address space");
            ProcessTable.Free(child);
            return -Errno.ENOMEM;
        }

        child->PageTableRoot = childPml4;

        // Copy process attributes from parent
        CopyProcessAttributes(parent, child);

        // Set up parent-child relationship
        child->ParentPid = parent->Pid;
        child->Parent = parent;

        // Add to parent's children list
        child->NextSibling = parent->FirstChild;
        parent->FirstChild = child;

        // Clone file descriptor table
        if (!FdTable.CloneTable(parent->FdTable, child->FdTable, parent->FdTableSize))
        {
            DebugConsole.WriteLine("[Fork] Failed to clone FD table");
            AddressSpace.DestroyUserSpace(childPml4);
            ProcessTable.Free(child);
            return -Errno.ENOMEM;
        }

        // Copy signal handlers
        CopySignalHandlers(parent, child);

        // Create child's main thread
        var childThread = CreateChildThread(parentThread, child);
        if (childThread == null)
        {
            DebugConsole.WriteLine("[Fork] Failed to create child thread");
            AddressSpace.DestroyUserSpace(childPml4);
            ProcessTable.Free(child);
            return -Errno.EAGAIN;
        }

        child->MainThread = childThread;
        child->ThreadCount = 1;
        child->State = ProcessState.Running;

        // Child thread will return 0 from fork
        // Set child's RAX to 0 in its saved context
        childThread->Context.Rax = 0;

        // Make child runnable
        Scheduler.MakeReady(childThread);

        DebugConsole.Write("[Fork] Fork complete, child PID ");
        DebugConsole.WriteDecimal(child->Pid);
        DebugConsole.WriteLine();

        // Parent returns child's PID
        return child->Pid;
    }

    /// <summary>
    /// Copy process attributes from parent to child
    /// </summary>
    private static void CopyProcessAttributes(Process.Process* parent, Process.Process* child)
    {
        // Copy security context
        child->Uid = parent->Uid;
        child->Gid = parent->Gid;
        child->Euid = parent->Euid;
        child->Egid = parent->Egid;
        child->Suid = parent->Suid;
        child->Sgid = parent->Sgid;

        // Copy address space layout
        child->HeapStart = parent->HeapStart;
        child->HeapEnd = parent->HeapEnd;
        child->StackTop = parent->StackTop;
        child->StackBottom = parent->StackBottom;
        child->ImageBase = parent->ImageBase;
        child->ImageSize = parent->ImageSize;

        // Copy resource limits
        child->MaxOpenFiles = parent->MaxOpenFiles;
        child->MaxAddressSpace = parent->MaxAddressSpace;
        child->MaxStackSize = parent->MaxStackSize;

        // Copy session/process group
        child->SessionId = parent->SessionId;
        child->ProcessGroupId = parent->ProcessGroupId;
        child->ControllingTerminal = parent->ControllingTerminal;

        // Copy blocked signals (pending signals are NOT inherited)
        child->BlockedSignals = parent->BlockedSignals;
        child->PendingSignals = 0;

        // Copy working directory
        for (int i = 0; i < 256; i++)
            child->Cwd[i] = parent->Cwd[i];

        // Copy program name
        for (int i = 0; i < 64; i++)
            child->Name[i] = parent->Name[i];
    }

    /// <summary>
    /// Copy signal handlers from parent to child
    /// </summary>
    private static void CopySignalHandlers(Process.Process* parent, Process.Process* child)
    {
        if (parent->SignalHandlers == null || child->SignalHandlers == null)
            return;

        for (int i = 0; i < Signals.SIGMAX; i++)
        {
            child->SignalHandlers[i] = parent->SignalHandlers[i];
        }
    }

    /// <summary>
    /// Create a child thread as a copy of the parent thread
    /// </summary>
    private static Thread* CreateChildThread(Thread* parentThread, Process.Process* childProcess)
    {
        // Allocate kernel stack for child (8KB = 2 pages)
        ulong childKernelStack = PageAllocator.AllocatePages(2);
        if (childKernelStack == 0)
            return null;

        // Allocate thread structure
        var childThread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (childThread == null)
        {
            // Free 2 pages
            PageAllocator.FreePage(childKernelStack);
            PageAllocator.FreePage(childKernelStack + 4096);
            return null;
        }

        // Copy thread state from parent
        *childThread = *parentThread;

        // Set up child-specific fields
        childThread->Process = childProcess;
        // Id will be assigned when we add to scheduler
        childThread->State = ThreadState.Ready;

        // Set up kernel stack
        ulong stackTop = childKernelStack + 2 * 4096; // Top of 8KB stack
        childThread->KernelStackTop = stackTop;
        childThread->StackBase = stackTop;
        childThread->StackLimit = childKernelStack;
        childThread->StackSize = 2 * 4096;

        // Copy parent's saved register state (for returning from fork)
        // The child will resume at the same point as the parent
        childThread->Context.Rsp = parentThread->Context.Rsp;
        childThread->Context.Rip = parentThread->Context.Rip;
        childThread->Context.Rbp = parentThread->Context.Rbp;
        childThread->Context.Rbx = parentThread->Context.Rbx;
        childThread->Context.R12 = parentThread->Context.R12;
        childThread->Context.R13 = parentThread->Context.R13;
        childThread->Context.R14 = parentThread->Context.R14;
        childThread->Context.R15 = parentThread->Context.R15;
        childThread->Context.Rflags = parentThread->Context.Rflags;

        // User mode state
        childThread->UserRsp = parentThread->UserRsp;
        childThread->UserRip = parentThread->UserRip;
        childThread->IsUserMode = parentThread->IsUserMode;

        // Clear thread-specific state
        childThread->Next = null;
        childThread->Prev = null;
        childThread->NextReady = null;
        childThread->PrevReady = null;
        childThread->NextAll = null;
        childThread->NextCleanup = null;
        childThread->WaitObject = null;
        childThread->WaitResult = 0;

        return childThread;
    }

    /// <summary>
    /// Wait for a child process to change state
    /// </summary>
    /// <param name="proc">Current process</param>
    /// <param name="pid">PID to wait for (-1 for any, >0 for specific, 0 for same pgid, &lt;-1 for specific pgid)</param>
    /// <param name="status">Pointer to store exit status</param>
    /// <param name="options">Wait options (WNOHANG, etc.)</param>
    /// <returns>PID of waited child, 0 if WNOHANG and no child ready, or negative error</returns>
    public static long Wait4(Process.Process* proc, int pid, int* status, int options)
    {
        if (proc == null)
            return -Errno.EINVAL;

        WaitOptions opts = (WaitOptions)options;

        DebugConsole.Write("[Wait4] pid=");
        DebugConsole.WriteDecimal(pid);
        DebugConsole.Write(" options=");
        DebugConsole.WriteDecimal(options);
        DebugConsole.WriteLine();

        // Check if we have any children at all
        if (proc->FirstChild == null)
        {
            DebugConsole.WriteLine("[Wait4] No children");
            return -Errno.ECHILD;
        }

        while (true)
        {
            // Search for matching child
            Process.Process* found = null;
            bool hasMatchingChild = false;

            ProcessTable.Lock.Acquire();

            for (var child = proc->FirstChild; child != null; child = child->NextSibling)
            {
                // Check if this child matches the pid criteria
                if (!MatchesPid(child, proc, pid))
                    continue;

                hasMatchingChild = true;

                // Check for zombie (exited)
                if (child->State == ProcessState.Zombie)
                {
                    found = child;
                    break;
                }

                // Check for stopped (if WUNTRACED)
                if ((opts & WaitOptions.WUNTRACED) != 0 && child->State == ProcessState.Stopped)
                {
                    found = child;
                    break;
                }
            }

            ProcessTable.Lock.Release();

            // No matching children at all
            if (!hasMatchingChild)
            {
                DebugConsole.WriteLine("[Wait4] No matching children");
                return -Errno.ECHILD;
            }

            // Found a child to reap
            if (found != null)
            {
                return ReapChild(proc, found, status);
            }

            // No child ready - check WNOHANG
            if ((opts & WaitOptions.WNOHANG) != 0)
            {
                DebugConsole.WriteLine("[Wait4] WNOHANG, no child ready");
                return 0;
            }

            // Block until a child changes state
            // For now, we'll do a simple spin-wait with yield
            // TODO: Proper sleep/wake mechanism using WaitQueue
            DebugConsole.WriteLine("[Wait4] Blocking...");

            // Yield to other threads
            Scheduler.Yield();

            // Check for signals that should interrupt wait
            if ((proc->PendingSignals & ~proc->BlockedSignals) != 0)
            {
                DebugConsole.WriteLine("[Wait4] Interrupted by signal");
                return -Errno.EINTR;
            }
        }
    }

    /// <summary>
    /// Check if a child matches the pid criteria for wait
    /// </summary>
    private static bool MatchesPid(Process.Process* child, Process.Process* parent, int pid)
    {
        if (pid == -1)
        {
            // Wait for any child
            return true;
        }
        else if (pid > 0)
        {
            // Wait for specific child
            return child->Pid == pid;
        }
        else if (pid == 0)
        {
            // Wait for any child in same process group
            return child->ProcessGroupId == parent->ProcessGroupId;
        }
        else
        {
            // pid < -1: wait for any child in process group |pid|
            return child->ProcessGroupId == -pid;
        }
    }

    /// <summary>
    /// Reap a zombie child and return its status
    /// </summary>
    private static long ReapChild(Process.Process* parent, Process.Process* child, int* status)
    {
        int childPid = child->Pid;

        DebugConsole.Write("[Wait4] Reaping child PID ");
        DebugConsole.WriteDecimal(childPid);
        DebugConsole.WriteLine();

        // Build status
        int exitStatus;
        if (child->ExitSignal != 0)
        {
            // Killed by signal
            exitStatus = WaitStatus.MakeSignaled(child->ExitSignal, false);
        }
        else
        {
            // Normal exit
            exitStatus = WaitStatus.MakeExited(child->ExitCode);
        }

        // Store status if pointer provided
        if (status != null)
        {
            *status = exitStatus;
        }

        // Remove from parent's children list
        RemoveFromChildList(parent, child);

        // Destroy child's address space
        if (child->PageTableRoot != 0 && child->PageTableRoot != VirtualMemory.Pml4Address)
        {
            AddressSpace.DestroyUserSpace(child->PageTableRoot);
        }

        // Free child process structure
        ProcessTable.Free(child);

        return childPid;
    }

    /// <summary>
    /// Remove a child from parent's children list
    /// </summary>
    private static void RemoveFromChildList(Process.Process* parent, Process.Process* child)
    {
        ProcessTable.Lock.Acquire();

        if (parent->FirstChild == child)
        {
            parent->FirstChild = child->NextSibling;
        }
        else
        {
            for (var sibling = parent->FirstChild; sibling != null; sibling = sibling->NextSibling)
            {
                if (sibling->NextSibling == child)
                {
                    sibling->NextSibling = child->NextSibling;
                    break;
                }
            }
        }

        child->NextSibling = null;
        child->Parent = null;

        ProcessTable.Lock.Release();
    }

    /// <summary>
    /// Clone (fork with flags) - Linux clone() syscall
    /// </summary>
    /// <param name="proc">Current process</param>
    /// <param name="thread">Current thread</param>
    /// <param name="flags">Clone flags</param>
    /// <param name="childStack">Child stack pointer (0 to share with parent)</param>
    /// <param name="parentTidPtr">Where to store parent TID</param>
    /// <param name="childTidPtr">Where to store child TID</param>
    /// <param name="tls">TLS descriptor</param>
    /// <returns>Child PID/TID to parent, 0 to child, or negative error</returns>
    public static long Clone(Process.Process* proc, Thread* thread, ulong flags, ulong childStack,
                             int* parentTidPtr, int* childTidPtr, ulong tls)
    {
        // For now, just implement as fork
        // TODO: Implement full clone semantics with flag handling
        return Fork(proc, thread);
    }

    /// <summary>
    /// Execve - replace current process image with new program
    /// </summary>
    /// <param name="proc">Current process</param>
    /// <param name="thread">Current thread</param>
    /// <param name="pathname">Path to executable</param>
    /// <param name="argv">Argument array</param>
    /// <param name="envp">Environment array</param>
    /// <returns>Does not return on success, negative error on failure</returns>
    public static long Execve(Process.Process* proc, Thread* thread,
                              byte* pathname, byte** argv, byte** envp)
    {
        if (proc == null || pathname == null)
            return -Errno.EINVAL;

        DebugConsole.WriteLine("[Execve] Not yet implemented - use ExecRaw for raw binaries");
        return -Errno.ENOSYS;
    }

    /// <summary>
    /// Execute raw binary code in the current process (for testing without ELF)
    /// </summary>
    /// <param name="proc">Current process</param>
    /// <param name="thread">Current thread</param>
    /// <param name="code">Pointer to raw code bytes</param>
    /// <param name="codeSize">Size of code in bytes</param>
    /// <returns>Does not return on success, negative error on failure</returns>
    public static long ExecRaw(Process.Process* proc, Thread* thread, byte* code, ulong codeSize)
    {
        if (proc == null || thread == null || code == null || codeSize == 0)
            return -Errno.EINVAL;

        DebugConsole.WriteLine("[ExecRaw] Starting exec...");

        // If process already has an address space (not kernel's), destroy it
        if (proc->PageTableRoot != 0 && proc->PageTableRoot != VirtualMemory.Pml4Address)
        {
            AddressSpace.DestroyUserSpace(proc->PageTableRoot);
            proc->PageTableRoot = 0;
        }

        // Create new user address space
        ulong newPml4 = AddressSpace.CreateUserSpace();
        if (newPml4 == 0)
        {
            DebugConsole.WriteLine("[ExecRaw] Failed to create address space");
            return -Errno.ENOMEM;
        }

        proc->PageTableRoot = newPml4;

        // Load the raw code
        ulong entryPoint = BinaryLoader.LoadRawCode(proc, code, codeSize);
        if (entryPoint == 0)
        {
            DebugConsole.WriteLine("[ExecRaw] Failed to load code");
            AddressSpace.DestroyUserSpace(newPml4);
            proc->PageTableRoot = 0;
            return -Errno.ENOEXEC;
        }

        // Set up user stack
        ulong userRsp = BinaryLoader.SetupUserStack(proc, 0);
        if (userRsp == 0)
        {
            DebugConsole.WriteLine("[ExecRaw] Failed to set up stack");
            AddressSpace.DestroyUserSpace(newPml4);
            proc->PageTableRoot = 0;
            return -Errno.ENOMEM;
        }

        // Close close-on-exec file descriptors
        FdTable.CloseOnExec(proc->FdTable, proc->FdTableSize);

        // Reset signal handlers to default
        if (proc->SignalHandlers != null)
        {
            for (int i = 0; i < Signals.SIGMAX; i++)
            {
                proc->SignalHandlers[i].Action = SignalAction.Default;
                proc->SignalHandlers[i].Handler = 0;
                proc->SignalHandlers[i].Mask = 0;
                proc->SignalHandlers[i].Flags = SignalFlags.None;
            }
        }

        // Clear pending signals
        proc->PendingSignals = 0;

        DebugConsole.Write("[ExecRaw] Ready to jump to user mode at 0x");
        DebugConsole.WriteHex(entryPoint);
        DebugConsole.Write(", RSP=0x");
        DebugConsole.WriteHex(userRsp);
        DebugConsole.WriteLine();

        // Update thread for user mode
        thread->IsUserMode = true;
        thread->UserRip = entryPoint;
        thread->UserRsp = userRsp;

        // Switch to new address space
        AddressSpace.SwitchTo(newPml4);

        // Jump to user mode using iretq
        JumpToUserMode(entryPoint, userRsp);

        // Should never reach here
        return -Errno.ENOEXEC;
    }

    /// <summary>
    /// Jump to user mode (assembly helper)
    /// </summary>
    [System.Runtime.InteropServices.DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void jump_to_ring3(ulong userRip, ulong userRsp);

    /// <summary>
    /// Jump to user mode with the given entry point and stack
    /// </summary>
    private static void JumpToUserMode(ulong entryPoint, ulong userRsp)
    {
        jump_to_ring3(entryPoint, userRsp);
    }
}

