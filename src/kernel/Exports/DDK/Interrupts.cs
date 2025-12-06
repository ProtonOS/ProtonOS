// ProtonOS kernel - DDK Interrupt Exports
// Exposes interrupt management operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for interrupt management.
/// </summary>
public static unsafe class InterruptExports
{
    /// <summary>
    /// Register an interrupt handler.
    /// </summary>
    /// <param name="vector">Interrupt vector (0-255)</param>
    /// <param name="handler">Function pointer to handler</param>
    /// <returns>true if registration succeeded</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterInterruptHandler")]
    public static bool RegisterInterruptHandler(byte vector, delegate*<void*, void> handler)
    {
        if (handler == null)
            return false;

        ProtonOS.X64.Arch.RegisterInterruptHandler(vector, handler);
        return true;
    }

    /// <summary>
    /// Unregister an interrupt handler.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_UnregisterInterruptHandler")]
    public static void UnregisterInterruptHandler(byte vector)
    {
        ProtonOS.X64.Arch.UnregisterInterruptHandler(vector);
    }

    /// <summary>
    /// Send End-Of-Interrupt to the APIC.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SendEOI")]
    public static void SendEOI()
    {
        APIC.SendEoi();
    }

    /// <summary>
    /// Enable interrupts (STI).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_EnableInterrupts")]
    public static void EnableInterrupts()
    {
        CPU.EnableInterrupts();
    }

    /// <summary>
    /// Disable interrupts (CLI).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DisableInterrupts")]
    public static void DisableInterrupts()
    {
        CPU.DisableInterrupts();
    }

    /// <summary>
    /// Check if interrupts are enabled.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AreInterruptsEnabled")]
    public static bool AreInterruptsEnabled()
    {
        // Check IF flag in RFLAGS
        // This requires reading RFLAGS which we can do via PUSHFQ
        // For now, we'll return true as a default
        // TODO: Implement proper RFLAGS check
        return true;
    }

    /// <summary>
    /// Allocate an IRQ for a device.
    /// Returns a vector number, or -1 if none available.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocateIRQ")]
    public static int AllocateIRQ()
    {
        // TODO: Implement IRQ allocation from a pool
        // For now, return a fixed vector range (48-79 are typically available)
        return -1;
    }

    /// <summary>
    /// Free an allocated IRQ.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FreeIRQ")]
    public static void FreeIRQ(int irq)
    {
        // TODO: Implement IRQ deallocation
    }

    /// <summary>
    /// Set CPU affinity for an IRQ (which CPUs can receive it).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SetIRQAffinity")]
    public static void SetIRQAffinity(int irq, ulong cpuMask)
    {
        // TODO: Configure I/O APIC redirection for this IRQ
    }
}
