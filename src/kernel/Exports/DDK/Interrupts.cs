// ProtonOS kernel - DDK Interrupt Exports
// Exposes interrupt management operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.X64;
using ProtonOS.Platform;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for interrupt management.
/// </summary>
public static unsafe class InterruptExports
{
    // IRQ allocation pool: vectors 48-79 (32 vectors for device IRQs)
    private const int IrqPoolBase = 48;
    private const int IrqPoolSize = 32;
    private static uint _irqPoolBitmap;  // 32 bits, 1 = allocated

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
        return CPU.AreInterruptsEnabled();
    }

    /// <summary>
    /// Allocate an IRQ for a device.
    /// Returns a vector number (48-79), or -1 if none available.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocateIRQ")]
    public static int AllocateIRQ()
    {
        // Find first free bit in the pool bitmap
        for (int i = 0; i < IrqPoolSize; i++)
        {
            uint bit = 1u << i;
            if ((_irqPoolBitmap & bit) == 0)
            {
                // Mark as allocated (atomic to be safe)
                uint oldVal = _irqPoolBitmap;
                uint newVal = oldVal | bit;
                fixed (uint* bitmapPtr = &_irqPoolBitmap)
                {
                    if (CPU.AtomicCompareExchange(ref *(int*)bitmapPtr, (int)newVal, (int)oldVal) == (int)oldVal)
                    {
                        return IrqPoolBase + i;
                    }
                }
                // CAS failed, retry from start
                i = -1;
                continue;
            }
        }
        return -1;  // No free IRQs
    }

    /// <summary>
    /// Free an allocated IRQ.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FreeIRQ")]
    public static void FreeIRQ(int vector)
    {
        // Validate vector is in our pool range
        if (vector < IrqPoolBase || vector >= IrqPoolBase + IrqPoolSize)
            return;

        int index = vector - IrqPoolBase;
        uint bit = 1u << index;

        // Clear the bit (mark as free)
        uint oldVal, newVal;
        fixed (uint* bitmapPtr = &_irqPoolBitmap)
        {
            do
            {
                oldVal = _irqPoolBitmap;
                newVal = oldVal & ~bit;
            } while (CPU.AtomicCompareExchange(ref *(int*)bitmapPtr, (int)newVal, (int)oldVal) != (int)oldVal);
        }
    }

    /// <summary>
    /// Set CPU affinity for an IRQ (which CPUs can receive it).
    /// Routes the IRQ to the first CPU in the mask.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SetIRQAffinity")]
    public static void SetIRQAffinity(int irq, ulong cpuMask)
    {
        if (!IOAPIC.IsInitialized || irq < 0 || irq > 23 || cpuMask == 0)
            return;

        // Find the first set CPU in the mask
        uint targetCpu = 0;
        for (int i = 0; i < 64; i++)
        {
            if ((cpuMask & (1UL << i)) != 0)
            {
                targetCpu = (uint)i;
                break;
            }
        }

        // Get APIC ID for this CPU and route the IRQ
        var cpuInfo = CPUTopology.GetCpu((int)targetCpu);
        if (cpuInfo == null)
            return;
        uint apicId = cpuInfo->ApicId;
        int vector = 32 + irq;  // Standard ISA IRQ vector mapping
        IOAPIC.SetIrqRoute(irq, vector, apicId);
    }
}
