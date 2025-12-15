// ProtonOS kernel - Kernel threading primitives
// Low-level threading structures for the kernel scheduler.
// Named with "Kernel" prefix to avoid collision with System.Threading types.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Threading;

/// <summary>
/// Kernel thread states
/// </summary>
public enum ThreadState
{
    Created,      // Thread created but not yet started
    Ready,        // Ready to run, in scheduler queue
    Running,      // Currently executing on CPU
    Blocked,      // Waiting on synchronization object
    Suspended,    // Explicitly suspended
    Terminated,   // Thread has exited
}

/// <summary>
/// CPU context for context switching.
/// Layout matches what we save/restore in assembly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CPUContext
{
    // General purpose registers
    public ulong Rax;
    public ulong Rbx;
    public ulong Rcx;
    public ulong Rdx;
    public ulong Rsi;
    public ulong Rdi;
    public ulong Rbp;
    public ulong R8;
    public ulong R9;
    public ulong R10;
    public ulong R11;
    public ulong R12;
    public ulong R13;
    public ulong R14;
    public ulong R15;

    // Instruction pointer and stack
    public ulong Rip;
    public ulong Rsp;

    // Flags
    public ulong Rflags;

    // Segment selectors (for user mode support later)
    public ulong Cs;
    public ulong Ss;
}

/// <summary>
/// Kernel Thread Control Block - core thread structure.
/// Heap-allocated by Scheduler.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Thread
{
    // Thread identification
    public uint Id;                    // Unique thread ID
    public ThreadState State;    // Current state

    // CPU context (saved during context switch)
    public CPUContext Context;

    // Stack information
    public ulong StackBase;            // Bottom of stack (highest address)
    public ulong StackLimit;           // Top of stack (lowest address)
    public nuint StackSize;            // Stack size in bytes

    // Thread entry point and parameter
    public delegate* unmanaged<void*, uint> EntryPoint;
    public void* Parameter;

    // Exit code (set when thread terminates)
    public uint ExitCode;

    // Scheduling
    public int Priority;               // Thread priority
    public ulong WakeTime;             // For Sleep() - tick count when thread should wake
    public int SuspendCount;           // Suspend count (>0 means suspended)

    // SMP support
    public ulong CpuAffinity;          // Bitmask of allowed CPUs (0 = any CPU)
    public uint LastCpu;               // Last CPU this thread ran on
    public uint PreferredCpu;          // Preferred CPU for cache affinity
    public uint PreferredNumaNode;     // Preferred NUMA node for memory allocation

    // Linked list for general purpose (e.g., wait queues)
    public Thread* Next;
    public Thread* Prev;

    // Ready queue linked list
    public Thread* NextReady;
    public Thread* PrevReady;

    // All-threads list (for iteration)
    public Thread* NextAll;

    // Synchronization - what this thread is waiting on
    public void* WaitObject;           // Object being waited on
    public uint WaitResult;            // Result of wait operation

    // Thread Local Storage (TLS) - array of pointers indexed by TLS slot
    // Allocated on demand when TlsSetValue is first called
    public void** TlsSlots;            // Pointer to TLS slot array
    public uint TlsSlotCount;          // Number of allocated TLS slots

    // Asynchronous Procedure Calls (APC) queue
    public APC* APCQueueHead;          // Head of APC queue (FIFO)
    public APC* APCQueueTail;          // Tail of APC queue
    public bool Alertable;             // Whether thread is in alertable wait state

    // Extended processor state (FPU/SSE/AVX)
    // Pointer to 64-byte aligned buffer for FXSAVE/XSAVE area
    public byte* ExtendedState;        // Pointer to FXSAVE/XSAVE area (64-byte aligned)
    public byte* ExtendedStateRaw;     // Raw allocation pointer (for freeing)
    public uint ExtendedStateSize;     // Size of extended state area in bytes

    // Cleanup tracking
    public Thread* NextCleanup;        // Next thread in cleanup queue
}

/// <summary>
/// Thread creation flags (matching Win32 CreateThread for PAL compatibility)
/// </summary>
public static class ThreadFlags
{
    public const uint None = 0;
    public const uint CreateSuspended = 0x00000004;
}

/// <summary>
/// Thread priority levels
/// </summary>
public static class ThreadPriority
{
    public const int Idle = -15;
    public const int Lowest = -2;
    public const int BelowNormal = -1;
    public const int Normal = 0;
    public const int AboveNormal = 1;
    public const int Highest = 2;
    public const int TimeCritical = 15;
}

/// <summary>
/// Wait result codes (matching Win32 for PAL compatibility)
/// </summary>
public static class WaitResult
{
    public const uint Object0 = 0x00000000;       // Wait succeeded
    public const uint Abandoned = 0x00000080;     // Mutex was abandoned
    public const uint IoCompletion = 0x000000C0;  // APC was delivered (WAIT_IO_COMPLETION)
    public const uint Timeout = 0x00000102;       // Wait timed out
    public const uint Failed = 0xFFFFFFFF;        // Wait failed
}

/// <summary>
/// Asynchronous Procedure Call (APC) entry.
/// Queued to threads for deferred execution during alertable waits.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct APC
{
    /// <summary>
    /// APC function to call: void ApcProc(nuint dwParam)
    /// </summary>
    public delegate* unmanaged<nuint, void> Function;

    /// <summary>
    /// Parameter to pass to the APC function
    /// </summary>
    public nuint Parameter;

    /// <summary>
    /// Next APC in the queue
    /// </summary>
    public APC* Next;
}

/// <summary>
/// Simple spinlock for kernel synchronization.
/// Uses atomic operations, no thread blocking.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SpinLock
{
    // Not using volatile since stdlib:zero doesn't have IsVolatile type.
    // Atomic operations provide necessary memory barriers.
    private int _locked;

    public void Acquire()
    {
        while (CPU.AtomicCompareExchange(ref _locked, 1, 0) != 0)
        {
            CPU.Pause();
        }
    }

    public bool TryAcquire()
    {
        return CPU.AtomicCompareExchange(ref _locked, 1, 0) == 0;
    }

    public void Release()
    {
        CPU.AtomicExchange(ref _locked, 0);
    }
}
