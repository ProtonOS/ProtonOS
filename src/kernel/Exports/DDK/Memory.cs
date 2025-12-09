// ProtonOS kernel - DDK Memory Exports
// Exposes memory allocation and mapping operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for memory management operations.
/// </summary>
public static unsafe class MemoryExports
{
    /// <summary>
    /// Allocate a single physical page.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePage")]
    public static ulong AllocatePage() => PageAllocator.AllocatePage();

    /// <summary>
    /// Allocate multiple contiguous physical pages.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePages")]
    public static ulong AllocatePages(ulong count) => PageAllocator.AllocatePages(count);

    /// <summary>
    /// Free a physical page.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FreePage")]
    public static void FreePage(ulong physicalAddress) => PageAllocator.FreePage(physicalAddress);

    /// <summary>
    /// Free multiple contiguous physical pages.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FreePages")]
    public static void FreePages(ulong physicalAddress, ulong count) => PageAllocator.FreePageRange(physicalAddress, count);

    /// <summary>
    /// Convert physical address to virtual address.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PhysToVirt")]
    public static ulong PhysToVirt(ulong physicalAddress) => VirtualMemory.PhysToVirt(physicalAddress);

    /// <summary>
    /// Convert virtual address to physical address.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_VirtToPhys")]
    public static ulong VirtToPhys(ulong virtualAddress) => VirtualMemory.VirtToPhys(virtualAddress);

    /// <summary>
    /// Map a memory-mapped I/O region.
    /// Creates page table entries to map the physical MMIO region to virtual memory.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_MapMMIO")]
    public static ulong MapMMIO(ulong physicalAddress, ulong size)
    {
        // Calculate the virtual address in the higher-half physical map
        ulong virtAddr = VirtualMemory.PhysToVirt(physicalAddress);

        // Round down to 2MB boundary for mapping
        ulong physStart = physicalAddress & ~(VirtualMemory.LargePageSize - 1);
        ulong physEnd = (physicalAddress + size + VirtualMemory.LargePageSize - 1) & ~(VirtualMemory.LargePageSize - 1);

        // Map each 2MB page in the range
        for (ulong phys = physStart; phys < physEnd; phys += VirtualMemory.LargePageSize)
        {
            ulong virt = VirtualMemory.PhysToVirt(phys);
            // Use cache-disable flags for MMIO
            if (!VirtualMemory.MapLargePage(virt, phys, PageFlags.KernelRW | PageFlags.CacheDisable))
            {
                // Page might already be mapped - that's OK for MMIO
                // Continue to the next page
            }
        }

        return virtAddr;
    }

    /// <summary>
    /// Unmap a memory-mapped I/O region.
    /// Currently a no-op since we use direct physical mapping.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_UnmapMMIO")]
    public static void UnmapMMIO(ulong virtualAddress, ulong size)
    {
        // No-op for now since we use direct physical mapping
    }

    /// <summary>
    /// Get total physical memory in bytes.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetTotalMemory")]
    public static ulong GetTotalMemory() => PageAllocator.TotalMemory;

    /// <summary>
    /// Get free physical memory in bytes.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetFreeMemory")]
    public static ulong GetFreeMemory() => PageAllocator.FreeMemory;

    /// <summary>
    /// Get page size.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetPageSize")]
    public static ulong GetPageSize() => PageAllocator.PageSize;
}
