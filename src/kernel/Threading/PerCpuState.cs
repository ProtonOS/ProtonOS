// ProtonOS kernel - Per-CPU State
// Each CPU has its own PerCpuState structure accessible via GS segment base.
// This enables efficient per-CPU scheduling queues and current thread tracking.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.Threading;

/// <summary>
/// Per-CPU state structure.
/// Each CPU has one of these, accessed via the GS segment base register.
/// The structure is carefully laid out so GS:0 contains the self-pointer for fast access.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerCpuState
{
    // ==================== Self-pointer (must be at offset 0) ====================
    // This allows fast access to the per-CPU state via "mov rax, gs:[0]"

    /// <summary>
    /// Self-pointer for fast GS-relative access. Must be at offset 0.
    /// </summary>
    public PerCpuState* Self;

    // ==================== CPU Identification ====================

    /// <summary>
    /// Kernel-assigned CPU index (0, 1, 2, ...)
    /// </summary>
    public uint CpuIndex;

    /// <summary>
    /// Hardware APIC ID
    /// </summary>
    public uint ApicId;

    /// <summary>
    /// Whether this is the Bootstrap Processor
    /// </summary>
    public bool IsBsp;

    // ==================== Scheduler State (per-CPU queue) ====================

    /// <summary>
    /// Currently running thread on this CPU
    /// </summary>
    public Thread* CurrentThread;

    /// <summary>
    /// Head of per-CPU ready queue (dequeue from head)
    /// </summary>
    public Thread* ReadyQueueHead;

    /// <summary>
    /// Tail of per-CPU ready queue (enqueue at tail, steal from tail)
    /// </summary>
    public Thread* ReadyQueueTail;

    /// <summary>
    /// Per-CPU scheduler lock
    /// </summary>
    public SpinLock SchedulerLock;

    /// <summary>
    /// Idle thread for this CPU (runs when no other threads ready)
    /// </summary>
    public Thread* IdleThread;

    /// <summary>
    /// Number of threads in this CPU's ready queue
    /// </summary>
    public int ReadyQueueCount;

    // ==================== Timing ====================

    /// <summary>
    /// Per-CPU tick count (incremented by timer interrupt)
    /// </summary>
    public ulong TickCount;

    // ==================== Interrupt State ====================

    /// <summary>
    /// Interrupt nesting depth (0 = not in interrupt)
    /// </summary>
    public int InterruptDepth;

    /// <summary>
    /// Saved interrupt flag state for nested interrupt disable
    /// </summary>
    public int InterruptDisableCount;

    // ==================== Extended State ====================

    /// <summary>
    /// Extended state save area for interrupt context (64-byte aligned)
    /// Used when an interrupt occurs to save FPU/SSE/AVX state
    /// </summary>
    public byte* InterruptExtendedState;

    // ==================== IST Stacks ====================
    // Interrupt Stack Table stacks for special exceptions

    /// <summary>
    /// NMI stack top (for IST1)
    /// </summary>
    public ulong NmiStackTop;

    /// <summary>
    /// Double Fault stack top (for IST2)
    /// </summary>
    public ulong DoubleFaultStackTop;

    /// <summary>
    /// Machine Check stack top (for IST3)
    /// </summary>
    public ulong MachineCheckStackTop;

    // ==================== Statistics ====================

    /// <summary>
    /// Number of context switches on this CPU
    /// </summary>
    public ulong ContextSwitchCount;

    /// <summary>
    /// Number of times work was stolen from this CPU
    /// </summary>
    public ulong WorkStolenCount;

    /// <summary>
    /// Number of times this CPU stole work from others
    /// </summary>
    public ulong WorkStealCount;
}

/// <summary>
/// Static helper class for accessing per-CPU state.
/// Provides convenient access to the current CPU's state via GS segment.
/// </summary>
public static unsafe class PerCpu
{
    private static PerCpuState** _allCpuStates;
    private static int _cpuCount;
    private static bool _initialized;

    /// <summary>
    /// Whether per-CPU state is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get the current CPU's per-CPU state.
    /// Uses GS segment base which points to the PerCpuState structure.
    /// </summary>
    public static PerCpuState* Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // Read GS base which points to PerCpuState
            // The Self field at offset 0 contains the same pointer for verification
            return (PerCpuState*)CPU.GetGsBase();
        }
    }

    /// <summary>
    /// Get the current thread running on this CPU
    /// </summary>
    public static Thread* CurrentThread
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Current->CurrentThread;
    }

    /// <summary>
    /// Get the current CPU index
    /// </summary>
    public static uint CpuIndex
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Current->CpuIndex;
    }

    /// <summary>
    /// Get the current CPU's APIC ID
    /// </summary>
    public static uint ApicId
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Current->ApicId;
    }

    /// <summary>
    /// Check if we're on the BSP
    /// </summary>
    public static bool IsBsp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Current->IsBsp;
    }

    /// <summary>
    /// Check if we're currently in an interrupt handler
    /// </summary>
    public static bool InInterrupt
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Current->InterruptDepth > 0;
    }

    /// <summary>
    /// Initialize the per-CPU state array.
    /// Called once during SMP init before starting APs.
    /// </summary>
    public static void Init(int cpuCount)
    {
        _cpuCount = cpuCount;
        _allCpuStates = (PerCpuState**)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(PerCpuState*) * cpuCount));
        _initialized = true;

        DebugConsole.Write("[PerCpu] Initialized for ");
        DebugConsole.WriteDecimal(cpuCount);
        DebugConsole.WriteLine(" CPUs");
    }

    /// <summary>
    /// Register a CPU's per-CPU state in the global array
    /// </summary>
    public static void RegisterCpu(int cpuIndex, PerCpuState* state)
    {
        if (cpuIndex >= 0 && cpuIndex < _cpuCount)
        {
            _allCpuStates[cpuIndex] = state;
        }
    }

    /// <summary>
    /// Get per-CPU state for a specific CPU by index
    /// </summary>
    public static PerCpuState* GetCpu(int cpuIndex)
    {
        if (cpuIndex < 0 || cpuIndex >= _cpuCount || _allCpuStates == null)
            return null;
        return _allCpuStates[cpuIndex];
    }

    /// <summary>
    /// Get the number of CPUs
    /// </summary>
    public static int CpuCount => _cpuCount;

    /// <summary>
    /// Enter interrupt context - increment interrupt depth
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void EnterInterrupt()
    {
        Current->InterruptDepth++;
    }

    /// <summary>
    /// Leave interrupt context - decrement interrupt depth
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void LeaveInterrupt()
    {
        Current->InterruptDepth--;
    }

    /// <summary>
    /// Increment context switch count for statistics
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IncrementContextSwitchCount()
    {
        Current->ContextSwitchCount++;
    }
}
