// ProtonOS DDK - Memory Kernel Wrappers
// DllImport wrappers for kernel memory allocation and mapping exports.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// DDK wrappers for kernel memory management APIs.
/// </summary>
public static unsafe class Memory
{
    [DllImport("*", EntryPoint = "Kernel_AllocatePage")]
    public static extern ulong AllocatePage();

    [DllImport("*", EntryPoint = "Kernel_AllocatePages")]
    public static extern ulong AllocatePages(ulong count);

    [DllImport("*", EntryPoint = "Kernel_FreePage")]
    public static extern void FreePage(ulong physicalAddress);

    [DllImport("*", EntryPoint = "Kernel_FreePages")]
    public static extern void FreePages(ulong physicalAddress, ulong count);

    [DllImport("*", EntryPoint = "Kernel_PhysToVirt")]
    public static extern ulong PhysToVirt(ulong physicalAddress);

    [DllImport("*", EntryPoint = "Kernel_VirtToPhys")]
    public static extern ulong VirtToPhys(ulong virtualAddress);

    [DllImport("*", EntryPoint = "Kernel_MapMMIO")]
    public static extern ulong MapMMIO(ulong physicalAddress, ulong size);

    [DllImport("*", EntryPoint = "Kernel_UnmapMMIO")]
    public static extern void UnmapMMIO(ulong virtualAddress, ulong size);

    /// <summary>
    /// Allocate a DMA-capable buffer.
    /// Returns virtual address, outputs physical address for hardware programming.
    /// </summary>
    public static void* AllocateDMABuffer(out ulong physicalAddress)
    {
        physicalAddress = AllocatePage();
        if (physicalAddress == 0)
            return null;
        return (void*)PhysToVirt(physicalAddress);
    }

    /// <summary>
    /// Allocate multiple contiguous pages for DMA.
    /// </summary>
    public static void* AllocateDMABuffer(ulong pageCount, out ulong physicalAddress)
    {
        physicalAddress = AllocatePages(pageCount);
        if (physicalAddress == 0)
            return null;
        return (void*)PhysToVirt(physicalAddress);
    }

    /// <summary>
    /// Free a DMA buffer allocated with AllocateDMABuffer.
    /// </summary>
    public static void FreeDMABuffer(void* virtualAddress)
    {
        if (virtualAddress == null)
            return;
        ulong physAddr = VirtToPhys((ulong)virtualAddress);
        FreePage(physAddr);
    }

    /// <summary>
    /// Free a multi-page DMA buffer.
    /// </summary>
    public static void FreeDMABuffer(void* virtualAddress, ulong pageCount)
    {
        if (virtualAddress == null)
            return;
        ulong physAddr = VirtToPhys((ulong)virtualAddress);
        FreePages(physAddr, pageCount);
    }
}
