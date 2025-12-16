// ProtonOS korlib - DDK Memory API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// DDK Memory management API.
/// </summary>
public static unsafe class Memory
{
    /// <summary>
    /// Allocate a single physical page (4KB).
    /// </summary>
    /// <returns>Physical address of the allocated page, or 0 on failure.</returns>
    public static ulong AllocatePage() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Allocate multiple contiguous physical pages.
    /// </summary>
    /// <param name="count">Number of pages to allocate.</param>
    /// <returns>Physical address of the first page, or 0 on failure.</returns>
    public static ulong AllocatePages(ulong count) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Free a physical page.
    /// </summary>
    /// <param name="physicalAddress">Physical address of the page to free.</param>
    public static void FreePage(ulong physicalAddress) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Free multiple contiguous physical pages.
    /// </summary>
    /// <param name="physicalAddress">Physical address of the first page.</param>
    /// <param name="count">Number of pages to free.</param>
    public static void FreePages(ulong physicalAddress, ulong count) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Convert physical address to virtual address (higher-half direct map).
    /// </summary>
    /// <param name="physicalAddress">Physical address to convert.</param>
    /// <returns>Virtual address.</returns>
    public static ulong PhysToVirt(ulong physicalAddress) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Convert virtual address to physical address.
    /// </summary>
    /// <param name="virtualAddress">Virtual address to convert.</param>
    /// <returns>Physical address.</returns>
    public static ulong VirtToPhys(ulong virtualAddress) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Map a memory-mapped I/O region.
    /// </summary>
    /// <param name="physicalAddress">Physical address of MMIO region.</param>
    /// <param name="size">Size of the region in bytes.</param>
    /// <returns>Virtual address of the mapped region.</returns>
    public static ulong MapMMIO(ulong physicalAddress, ulong size) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Unmap a memory-mapped I/O region.
    /// </summary>
    /// <param name="virtualAddress">Virtual address of mapped region.</param>
    /// <param name="size">Size of the region in bytes.</param>
    public static void UnmapMMIO(ulong virtualAddress, ulong size) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get total physical memory in bytes.
    /// </summary>
    public static ulong GetTotalMemory() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get free physical memory in bytes.
    /// </summary>
    public static ulong GetFreeMemory() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get page size (typically 4096).
    /// </summary>
    public static ulong GetPageSize() => throw new PlatformNotSupportedException();

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
#endif
