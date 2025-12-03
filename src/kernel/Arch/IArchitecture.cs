// ProtonOS Architecture Abstraction - Main Architecture Interface
// Top-level interface for architecture initialization and management.

namespace ProtonOS.Arch;

/// <summary>
/// Main architecture interface for kernel initialization.
/// Each architecture implements this to provide its specific initialization sequence.
///
/// This interface covers:
/// - Two-phase initialization (pre-heap and post-heap)
/// - Interrupt handler registration
/// - Timer management
/// </summary>
public unsafe interface IArchitecture<TSelf> where TSelf : IArchitecture<TSelf>
{
    // ==================== Initialization ====================

    /// <summary>
    /// Stage 1 initialization - before heap is available.
    /// Sets up minimal hardware: GDT, IDT (or equivalent), basic CPU state.
    /// Called very early in kernel startup.
    /// </summary>
    static abstract void InitStage1();

    /// <summary>
    /// Stage 2 initialization - after heap is available.
    /// Sets up timers, enables interrupts, completes hardware init.
    /// Called after memory management is initialized.
    /// </summary>
    static abstract void InitStage2();

    /// <summary>
    /// Check if Stage 1 initialization is complete.
    /// </summary>
    static abstract bool IsStage1Complete { get; }

    /// <summary>
    /// Check if Stage 2 initialization is complete.
    /// </summary>
    static abstract bool IsStage2Complete { get; }

    // ==================== Interrupt Management ====================

    /// <summary>
    /// Register an interrupt handler for a specific vector.
    /// </summary>
    /// <param name="vector">Interrupt vector number (0-255 on x64)</param>
    /// <param name="handler">Handler function pointer</param>
    static abstract void RegisterInterruptHandler(int vector, delegate*<void*, void> handler);

    /// <summary>
    /// Unregister an interrupt handler.
    /// </summary>
    /// <param name="vector">Interrupt vector number</param>
    static abstract void UnregisterInterruptHandler(int vector);

    /// <summary>
    /// Signal end-of-interrupt to the interrupt controller.
    /// x64: APIC EOI
    /// ARM64: GIC EOI
    /// </summary>
    /// <param name="vector">Interrupt vector that was handled</param>
    static abstract void EndOfInterrupt(int vector);

    // ==================== Timer ====================

    /// <summary>
    /// Get the current timer tick count.
    /// Monotonically increasing, starting from 0 at boot.
    /// </summary>
    static abstract ulong GetTickCount();

    /// <summary>
    /// Get the timer frequency in Hz.
    /// </summary>
    static abstract ulong GetTimerFrequency();

    /// <summary>
    /// Busy-wait for the specified number of nanoseconds.
    /// </summary>
    static abstract void BusyWaitNs(ulong nanoseconds);

    /// <summary>
    /// Busy-wait for the specified number of milliseconds.
    /// </summary>
    static abstract void BusyWaitMs(ulong milliseconds);

    // ==================== Exception Handling ====================

    /// <summary>
    /// Get function pointer to the throw exception routine.
    /// Used by JIT-compiled code to throw managed exceptions.
    /// </summary>
    static abstract delegate*<void*, void> GetThrowExceptionFuncPtr();

    /// <summary>
    /// Get function pointer to the rethrow routine.
    /// Used by JIT-compiled code to rethrow current exception.
    /// </summary>
    static abstract delegate*<void> GetRethrowFuncPtr();

    // ==================== Context ====================

    /// <summary>
    /// Size of CPU context structure in bytes.
    /// Used for thread context save/restore.
    /// </summary>
    static abstract int ContextSize { get; }

    /// <summary>
    /// Size of extended state (FPU/SSE/AVX) in bytes.
    /// </summary>
    static abstract int ExtendedStateSize { get; }
}
