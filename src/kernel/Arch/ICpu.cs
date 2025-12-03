// ProtonOS Architecture Abstraction - CPU Interface
// Architecture-neutral CPU operations for kernel use.

namespace ProtonOS.Arch;

/// <summary>
/// CPU operations interface using static abstract members.
/// Provides portable access to CPU primitives like interrupt control,
/// memory barriers, and atomic operations.
///
/// Note: Architecture-specific operations (like CR register access on x64)
/// are NOT in this interface. Those stay in the arch-specific CPU class.
/// This interface only contains operations that have equivalents across all architectures.
/// </summary>
public unsafe interface ICpu<TSelf> where TSelf : ICpu<TSelf>
{
    // ==================== Interrupt Control ====================

    /// <summary>
    /// Enable interrupts.
    /// x64: STI
    /// ARM64: MSR DAIFClr, #0xF
    /// </summary>
    static abstract void EnableInterrupts();

    /// <summary>
    /// Disable interrupts.
    /// x64: CLI
    /// ARM64: MSR DAIFSet, #0xF
    /// </summary>
    static abstract void DisableInterrupts();

    /// <summary>
    /// Halt the CPU until next interrupt.
    /// x64: HLT
    /// ARM64: WFI
    /// </summary>
    static abstract void Halt();

    /// <summary>
    /// Trigger a software breakpoint.
    /// x64: INT3
    /// ARM64: BRK #0
    /// </summary>
    static abstract void Breakpoint();

    /// <summary>
    /// Halt forever (disable interrupts and halt in loop).
    /// Used for fatal errors.
    /// </summary>
    static abstract void HaltForever();

    // ==================== Memory Barriers ====================

    /// <summary>
    /// Full memory barrier (prevents all reordering across the barrier).
    /// x64: MFENCE
    /// ARM64: DMB ISH
    /// </summary>
    static abstract void MemoryBarrier();

    /// <summary>
    /// Spin-wait hint for busy loops.
    /// x64: PAUSE
    /// ARM64: YIELD
    /// </summary>
    static abstract void Pause();

    // ==================== Atomic Operations (32-bit) ====================

    /// <summary>
    /// Atomic compare-and-exchange for 32-bit integers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    static abstract int AtomicCompareExchange(ref int location, int value, int comparand);

    /// <summary>
    /// Atomic exchange for 32-bit integers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    static abstract int AtomicExchange(ref int location, int value);

    /// <summary>
    /// Atomic add for 32-bit integers.
    /// Returns the original value (before addition).
    /// </summary>
    static abstract int AtomicAdd(ref int location, int addend);

    /// <summary>
    /// Atomic increment for 32-bit integers.
    /// Returns the original value (before increment).
    /// </summary>
    static abstract int AtomicIncrement(ref int location);

    /// <summary>
    /// Atomic decrement for 32-bit integers.
    /// Returns the original value (before decrement).
    /// </summary>
    static abstract int AtomicDecrement(ref int location);

    // ==================== Atomic Operations (64-bit) ====================

    /// <summary>
    /// Atomic compare-and-exchange for 64-bit integers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    static abstract long AtomicCompareExchange64(ref long location, long value, long comparand);

    /// <summary>
    /// Atomic exchange for 64-bit integers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    static abstract long AtomicExchange64(ref long location, long value);

    /// <summary>
    /// Atomic add for 64-bit integers.
    /// Returns the original value (before addition).
    /// </summary>
    static abstract long AtomicAdd64(ref long location, long addend);

    /// <summary>
    /// Atomic compare-and-exchange for pointers.
    /// If *location == comparand, sets *location = value.
    /// Returns the original value at location.
    /// </summary>
    static abstract void* AtomicCompareExchangePointer(ref void* location, void* value, void* comparand);

    /// <summary>
    /// Atomic exchange for pointers.
    /// Sets *location = value and returns the original value.
    /// </summary>
    static abstract void* AtomicExchangePointer(ref void* location, void* value);

    // ==================== Memory Operations ====================

    /// <summary>
    /// Copy memory from source to destination.
    /// Uses optimized implementation (e.g., REP MOVSB on x64).
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="src">Source pointer</param>
    /// <param name="count">Number of bytes to copy</param>
    /// <returns>Destination pointer</returns>
    static abstract void* MemCopy(void* dest, void* src, ulong count);

    /// <summary>
    /// Fill memory with a byte value.
    /// Uses optimized implementation (e.g., REP STOSB on x64).
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="value">Byte value to fill with</param>
    /// <param name="count">Number of bytes to fill</param>
    /// <returns>Destination pointer</returns>
    static abstract void* MemSet(void* dest, byte value, ulong count);

    /// <summary>
    /// Zero memory.
    /// </summary>
    /// <param name="dest">Destination pointer</param>
    /// <param name="count">Number of bytes to zero</param>
    /// <returns>Destination pointer</returns>
    static abstract void* MemZero(void* dest, ulong count);

    // ==================== Stack ====================

    /// <summary>
    /// Get the current stack pointer value.
    /// Used for stack bounds detection.
    /// </summary>
    static abstract ulong GetStackPointer();
}
