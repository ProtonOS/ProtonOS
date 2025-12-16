// ProtonOS korlib - DDK Interrupts API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Kernel;

/// <summary>
/// Interrupt frame passed to handlers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InterruptFrame
{
    /// <summary>Interrupt vector number.</summary>
    public ulong Vector;
    /// <summary>Error code (0 for interrupts without error codes).</summary>
    public ulong ErrorCode;
    /// <summary>Instruction pointer at interrupt.</summary>
    public ulong RIP;
    /// <summary>Code segment.</summary>
    public ulong CS;
    /// <summary>Flags register.</summary>
    public ulong RFlags;
    /// <summary>Stack pointer at interrupt.</summary>
    public ulong RSP;
    /// <summary>Stack segment.</summary>
    public ulong SS;
}

/// <summary>
/// DDK Interrupts API.
/// </summary>
public static unsafe class Interrupts
{
    /// <summary>
    /// Register an interrupt handler for the specified vector.
    /// </summary>
    public static bool RegisterHandler(byte vector, delegate*<InterruptFrame*, void> handler)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Unregister an interrupt handler.
    /// </summary>
    public static void UnregisterHandler(byte vector) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Send End-Of-Interrupt to the interrupt controller.
    /// </summary>
    public static void SendEOI() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Enable interrupts (STI).
    /// </summary>
    public static void Enable() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Disable interrupts (CLI).
    /// </summary>
    public static void Disable() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Check if interrupts are enabled.
    /// </summary>
    public static bool AreEnabled() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Allocate a dynamic IRQ number.
    /// </summary>
    public static int AllocateIRQ() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Free a previously allocated IRQ.
    /// </summary>
    public static void FreeIRQ(int irq) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Set CPU affinity mask for an IRQ.
    /// </summary>
    public static void SetIRQAffinity(int irq, ulong cpuMask) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Disable interrupts and return previous state.
    /// Use with Restore for critical sections.
    /// </summary>
    public static bool DisableAndSave()
    {
        bool wasEnabled = AreEnabled();
        Disable();
        return wasEnabled;
    }

    /// <summary>
    /// Restore interrupt state from DisableAndSave.
    /// </summary>
    public static void Restore(bool wasEnabled)
    {
        if (wasEnabled)
            Enable();
    }
}
#endif
