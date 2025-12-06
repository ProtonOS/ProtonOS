// ProtonOS DDK - Interrupt Kernel Wrappers
// DllImport wrappers for interrupt management operations.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// Interrupt handler delegate type.
/// </summary>
public unsafe delegate void InterruptHandler(InterruptFrame* frame);

/// <summary>
/// Interrupt frame passed to handlers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InterruptFrame
{
    // Pushed by ISR stub
    public ulong Vector;
    public ulong ErrorCode;

    // Pushed by CPU on interrupt
    public ulong RIP;
    public ulong CS;
    public ulong RFlags;
    public ulong RSP;
    public ulong SS;
}

/// <summary>
/// DDK wrappers for kernel interrupt management APIs.
/// </summary>
public static unsafe class Interrupts
{
    [DllImport("*", EntryPoint = "Kernel_RegisterInterruptHandler")]
    public static extern bool RegisterHandler(byte vector, delegate*<InterruptFrame*, void> handler);

    [DllImport("*", EntryPoint = "Kernel_UnregisterInterruptHandler")]
    public static extern void UnregisterHandler(byte vector);

    [DllImport("*", EntryPoint = "Kernel_SendEOI")]
    public static extern void SendEOI();

    [DllImport("*", EntryPoint = "Kernel_EnableInterrupts")]
    public static extern void Enable();

    [DllImport("*", EntryPoint = "Kernel_DisableInterrupts")]
    public static extern void Disable();

    [DllImport("*", EntryPoint = "Kernel_AreInterruptsEnabled")]
    public static extern bool AreEnabled();

    [DllImport("*", EntryPoint = "Kernel_AllocateIRQ")]
    public static extern int AllocateIRQ();

    [DllImport("*", EntryPoint = "Kernel_FreeIRQ")]
    public static extern void FreeIRQ(int irq);

    [DllImport("*", EntryPoint = "Kernel_SetIRQAffinity")]
    public static extern void SetIRQAffinity(int irq, ulong cpuMask);

    /// <summary>
    /// Disable interrupts and return previous state.
    /// Use with RestoreInterrupts for critical sections.
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
