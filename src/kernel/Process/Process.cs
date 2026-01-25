// ProtonOS kernel - Process Control Block
// Core process structure for user-space application support.
// Implements Unix-style process model with UID/GID security.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.IO;

namespace ProtonOS.Process;

/// <summary>
/// Process states
/// </summary>
public enum ProcessState : byte
{
    Created,    // Process created but not yet started
    Running,    // Process has at least one running thread
    Sleeping,   // All threads blocked/sleeping
    Stopped,    // Process stopped (SIGSTOP)
    Zombie      // Process exited, waiting for parent to reap
}

/// <summary>
/// Process Control Block - core process structure.
/// Heap-allocated by ProcessTable.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Process
{
    // ==================== Process Identification ====================

    /// <summary>
    /// Process ID (unique kernel-wide)
    /// </summary>
    public int Pid;

    /// <summary>
    /// Parent process ID
    /// </summary>
    public int ParentPid;

    /// <summary>
    /// Current process state
    /// </summary>
    public ProcessState State;

    // ==================== Security Context (Unix DAC) ====================

    /// <summary>
    /// Real user ID (who started the process)
    /// </summary>
    public uint Uid;

    /// <summary>
    /// Real group ID
    /// </summary>
    public uint Gid;

    /// <summary>
    /// Effective user ID (used for permission checks)
    /// </summary>
    public uint Euid;

    /// <summary>
    /// Effective group ID
    /// </summary>
    public uint Egid;

    /// <summary>
    /// Saved set-user-ID (for suid programs)
    /// </summary>
    public uint Suid;

    /// <summary>
    /// Saved set-group-ID (for sgid programs)
    /// </summary>
    public uint Sgid;

    // ==================== Address Space ====================

    /// <summary>
    /// Physical address of process's PML4 (page table root)
    /// </summary>
    public ulong PageTableRoot;

    /// <summary>
    /// Start of user heap
    /// </summary>
    public ulong HeapStart;

    /// <summary>
    /// Current end of user heap (brk)
    /// </summary>
    public ulong HeapEnd;

    /// <summary>
    /// Top of user stack (grows down)
    /// </summary>
    public ulong StackTop;

    /// <summary>
    /// Bottom of user stack (lowest valid address)
    /// </summary>
    public ulong StackBottom;

    /// <summary>
    /// Base address where program was loaded
    /// </summary>
    public ulong ImageBase;

    /// <summary>
    /// Size of loaded program image
    /// </summary>
    public ulong ImageSize;

    /// <summary>
    /// Next address for mmap allocations (grows down from stack)
    /// </summary>
    public ulong MmapBase;

    /// <summary>
    /// Head of mmap region linked list
    /// </summary>
    public MmapRegion* MmapRegions;

    // ==================== Threading ====================

    /// <summary>
    /// Main thread (first thread in process)
    /// </summary>
    public Thread* MainThread;

    /// <summary>
    /// Number of threads in this process
    /// </summary>
    public int ThreadCount;

    /// <summary>
    /// Lock for thread list operations
    /// </summary>
    public SpinLock ThreadLock;

    // ==================== File Descriptors ====================

    /// <summary>
    /// File descriptor table
    /// </summary>
    public FileDescriptor* FdTable;

    /// <summary>
    /// Size of FD table (max open files)
    /// </summary>
    public int FdTableSize;

    /// <summary>
    /// Lock for FD table operations
    /// </summary>
    public SpinLock FdLock;

    // ==================== Working Directory ====================

    /// <summary>
    /// Current working directory (null-terminated path)
    /// </summary>
    public fixed byte Cwd[256];

    // ==================== Signal State ====================

    /// <summary>
    /// Pending signals bitmask (signals 1-64)
    /// </summary>
    public ulong PendingSignals;

    /// <summary>
    /// Blocked signals bitmask
    /// </summary>
    public ulong BlockedSignals;

    /// <summary>
    /// Signal handlers array (64 entries, heap-allocated)
    /// </summary>
    public SignalHandler* SignalHandlers;

    // ==================== Exit State ====================

    /// <summary>
    /// Exit code (valid when State == Zombie)
    /// </summary>
    public int ExitCode;

    /// <summary>
    /// Exit signal (signal that caused exit, or 0 for normal exit)
    /// </summary>
    public int ExitSignal;

    // ==================== Process Relationships ====================

    /// <summary>
    /// Parent process pointer
    /// </summary>
    public Process* Parent;

    /// <summary>
    /// First child process
    /// </summary>
    public Process* FirstChild;

    /// <summary>
    /// Next sibling process (children of same parent)
    /// </summary>
    public Process* NextSibling;

    // ==================== Process List Links ====================

    /// <summary>
    /// Next process in global process list
    /// </summary>
    public Process* NextAll;

    /// <summary>
    /// Previous process in global process list
    /// </summary>
    public Process* PrevAll;

    // ==================== Resource Limits ====================

    /// <summary>
    /// Maximum file descriptors
    /// </summary>
    public uint MaxOpenFiles;

    /// <summary>
    /// Maximum address space size
    /// </summary>
    public ulong MaxAddressSpace;

    /// <summary>
    /// Maximum stack size
    /// </summary>
    public ulong MaxStackSize;

    // ==================== Timing ====================

    /// <summary>
    /// Tick count when process started
    /// </summary>
    public ulong StartTime;

    /// <summary>
    /// Total user mode CPU time (ticks)
    /// </summary>
    public ulong UserTime;

    /// <summary>
    /// Total kernel mode CPU time (ticks)
    /// </summary>
    public ulong KernelTime;

    // ==================== Session/Process Group ====================

    /// <summary>
    /// Session ID
    /// </summary>
    public int SessionId;

    /// <summary>
    /// Process group ID
    /// </summary>
    public int ProcessGroupId;

    /// <summary>
    /// Controlling terminal (null if none)
    /// </summary>
    public void* ControllingTerminal;

    // ==================== Program Name ====================

    /// <summary>
    /// Program name (basename of executable)
    /// </summary>
    public fixed byte Name[64];
}

/// <summary>
/// Memory mapping region (for mmap tracking)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MmapRegion
{
    /// <summary>
    /// Virtual address start
    /// </summary>
    public ulong Start;

    /// <summary>
    /// Length in bytes
    /// </summary>
    public ulong Length;

    /// <summary>
    /// Protection flags (MmapProt)
    /// </summary>
    public int Prot;

    /// <summary>
    /// Mapping flags (MmapFlags)
    /// </summary>
    public int Flags;

    /// <summary>
    /// File descriptor (-1 for anonymous)
    /// </summary>
    public int Fd;

    /// <summary>
    /// Offset in file
    /// </summary>
    public long FileOffset;

    /// <summary>
    /// Next region in linked list
    /// </summary>
    public MmapRegion* Next;
}

/// <summary>
/// Signal handler entry
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct SignalHandler
{
    /// <summary>
    /// Handler action type
    /// </summary>
    public SignalAction Action;

    /// <summary>
    /// User-mode handler function pointer (if Action == Handler)
    /// </summary>
    public ulong Handler;

    /// <summary>
    /// Signals to block while handler runs
    /// </summary>
    public ulong Mask;

    /// <summary>
    /// Handler flags
    /// </summary>
    public SignalFlags Flags;
}

/// <summary>
/// Signal handler action type
/// </summary>
public enum SignalAction : byte
{
    Default,    // Use default action for this signal
    Ignore,     // Ignore the signal
    Handler     // Call user-defined handler
}

/// <summary>
/// Signal handler flags
/// </summary>
[Flags]
public enum SignalFlags : uint
{
    None = 0,
    NoChildStop = 0x0001,   // SA_NOCLDSTOP: Don't receive SIGCHLD when children stop
    NoChildWait = 0x0002,   // SA_NOCLDWAIT: Don't create zombie children
    SigInfo = 0x0004,       // SA_SIGINFO: Handler receives siginfo_t
    OnStack = 0x0008,       // SA_ONSTACK: Use alternate signal stack
    Restart = 0x0010,       // SA_RESTART: Restart interruptible functions
    NoDefer = 0x0020,       // SA_NODEFER: Don't block signal while handling
    ResetHand = 0x0040,     // SA_RESETHAND: Reset to SIG_DFL after handling
}

/// <summary>
/// Standard signal numbers (POSIX compatible)
/// </summary>
public static class Signals
{
    public const int SIGHUP = 1;      // Hangup
    public const int SIGINT = 2;      // Interrupt (Ctrl+C)
    public const int SIGQUIT = 3;     // Quit (Ctrl+\)
    public const int SIGILL = 4;      // Illegal instruction
    public const int SIGTRAP = 5;     // Trace/breakpoint trap
    public const int SIGABRT = 6;     // Abort
    public const int SIGBUS = 7;      // Bus error
    public const int SIGFPE = 8;      // Floating point exception
    public const int SIGKILL = 9;     // Kill (cannot be caught or ignored)
    public const int SIGUSR1 = 10;    // User-defined signal 1
    public const int SIGSEGV = 11;    // Segmentation fault
    public const int SIGUSR2 = 12;    // User-defined signal 2
    public const int SIGPIPE = 13;    // Broken pipe
    public const int SIGALRM = 14;    // Alarm clock
    public const int SIGTERM = 15;    // Termination
    public const int SIGSTKFLT = 16;  // Stack fault
    public const int SIGCHLD = 17;    // Child stopped or terminated
    public const int SIGCONT = 18;    // Continue if stopped
    public const int SIGSTOP = 19;    // Stop (cannot be caught or ignored)
    public const int SIGTSTP = 20;    // Terminal stop (Ctrl+Z)
    public const int SIGTTIN = 21;    // Background read from terminal
    public const int SIGTTOU = 22;    // Background write to terminal
    public const int SIGURG = 23;     // Urgent data on socket
    public const int SIGXCPU = 24;    // CPU time limit exceeded
    public const int SIGXFSZ = 25;    // File size limit exceeded
    public const int SIGVTALRM = 26;  // Virtual timer expired
    public const int SIGPROF = 27;    // Profiling timer expired
    public const int SIGWINCH = 28;   // Window size changed
    public const int SIGIO = 29;      // I/O possible
    public const int SIGPWR = 30;     // Power failure
    public const int SIGSYS = 31;     // Bad system call

    public const int SIGMAX = 64;     // Maximum signal number

    /// <summary>
    /// Create a signal mask with a single signal
    /// </summary>
    public static ulong Mask(int signum) => 1UL << (signum - 1);

    /// <summary>
    /// Check if a signal is pending in a mask
    /// </summary>
    public static bool IsPending(ulong mask, int signum) => (mask & Mask(signum)) != 0;

    /// <summary>
    /// Check if a signal can be caught/ignored
    /// </summary>
    public static bool CanCatch(int signum) => signum != SIGKILL && signum != SIGSTOP;
}

/// <summary>
/// Special user IDs
/// </summary>
public static class UserIds
{
    public const uint Root = 0;           // Superuser
    public const uint Nobody = 65534;     // Nobody user
}

/// <summary>
/// Special group IDs
/// </summary>
public static class GroupIds
{
    public const uint Root = 0;           // Root group
    public const uint Nobody = 65534;     // Nobody group
}
