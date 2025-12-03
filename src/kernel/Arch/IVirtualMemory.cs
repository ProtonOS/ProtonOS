// ProtonOS Architecture Abstraction - Virtual Memory Interface
// Architecture-neutral virtual memory management.

namespace ProtonOS.Arch;

/// <summary>
/// Page protection flags (architecture-neutral).
/// Each architecture maps these to its specific page table bits.
/// </summary>
public enum PageFlags : ulong
{
    None = 0,

    /// <summary>Page is present/valid</summary>
    Present = 1UL << 0,

    /// <summary>Page is writable</summary>
    Writable = 1UL << 1,

    /// <summary>Page is accessible from user mode</summary>
    User = 1UL << 2,

    /// <summary>Write-through caching</summary>
    WriteThrough = 1UL << 3,

    /// <summary>Disable caching</summary>
    NoCache = 1UL << 4,

    /// <summary>Page has been accessed</summary>
    Accessed = 1UL << 5,

    /// <summary>Page has been written (dirty)</summary>
    Dirty = 1UL << 6,

    /// <summary>Large page (2MB on x64)</summary>
    LargePage = 1UL << 7,

    /// <summary>Global page (not flushed on CR3 reload)</summary>
    Global = 1UL << 8,

    /// <summary>No execute (requires NX support)</summary>
    NoExecute = 1UL << 63,

    // Common combinations
    KernelCode = Present,
    KernelData = Present | Writable | NoExecute,
    KernelStack = Present | Writable | NoExecute,
    UserCode = Present | User,
    UserData = Present | Writable | User | NoExecute,
    DeviceMemory = Present | Writable | NoCache | NoExecute,
}

/// <summary>
/// Virtual memory management interface using static abstract members.
/// Provides portable access to page table operations.
///
/// Each architecture implements this with its specific paging model:
/// - x64: 4-level paging (PML4, PDPT, PD, PT)
/// - ARM64: 4-level translation tables (TTBR0/TTBR1)
/// </summary>
public unsafe interface IVirtualMemory<TSelf> where TSelf : IVirtualMemory<TSelf>
{
    // ==================== Constants ====================

    /// <summary>
    /// Base page size in bytes.
    /// Both x64 and ARM64: 4096 (4KB)
    /// </summary>
    static abstract ulong PageSize { get; }

    /// <summary>
    /// Large page size in bytes.
    /// x64: 2MB (0x200000)
    /// ARM64: 2MB (0x200000)
    /// </summary>
    static abstract ulong LargePageSize { get; }

    /// <summary>
    /// Huge page size in bytes.
    /// x64: 1GB (0x40000000)
    /// ARM64: 1GB (0x40000000)
    /// </summary>
    static abstract ulong HugePageSize { get; }

    /// <summary>
    /// Page shift (log2 of PageSize).
    /// Both x64 and ARM64: 12
    /// </summary>
    static abstract int PageShift { get; }

    /// <summary>
    /// Virtual address where physical memory is identity-mapped.
    /// x64: 0xFFFF_8000_0000_0000 (higher half)
    /// ARM64: Architecture-specific
    /// </summary>
    static abstract ulong PhysicalMapBase { get; }

    // ==================== Initialization ====================

    /// <summary>
    /// Initialize virtual memory system.
    /// Called early in kernel startup after basic hardware init.
    /// </summary>
    /// <returns>true if successful</returns>
    static abstract bool Init();

    /// <summary>
    /// Check if virtual memory system is initialized.
    /// </summary>
    static abstract bool IsInitialized { get; }

    // ==================== Address Translation ====================

    /// <summary>
    /// Convert physical address to virtual address.
    /// Uses the physical memory map region.
    /// </summary>
    static abstract ulong PhysToVirt(ulong physAddr);

    /// <summary>
    /// Convert virtual address to physical address.
    /// Walks page tables to find physical mapping.
    /// Returns 0 if not mapped.
    /// </summary>
    static abstract ulong VirtToPhys(ulong virtAddr);

    // ==================== Page Mapping ====================

    /// <summary>
    /// Map a 4KB page.
    /// </summary>
    /// <param name="virtAddr">Virtual address (must be page-aligned)</param>
    /// <param name="physAddr">Physical address (must be page-aligned)</param>
    /// <param name="flags">Page protection flags</param>
    /// <returns>true if successful</returns>
    static abstract bool MapPage(ulong virtAddr, ulong physAddr, PageFlags flags);

    /// <summary>
    /// Map a large page (2MB).
    /// </summary>
    /// <param name="virtAddr">Virtual address (must be 2MB-aligned)</param>
    /// <param name="physAddr">Physical address (must be 2MB-aligned)</param>
    /// <param name="flags">Page protection flags</param>
    /// <returns>true if successful</returns>
    static abstract bool MapLargePage(ulong virtAddr, ulong physAddr, PageFlags flags);

    /// <summary>
    /// Unmap a page at the given virtual address.
    /// </summary>
    /// <param name="virtAddr">Virtual address to unmap</param>
    /// <returns>true if page was mapped and is now unmapped</returns>
    static abstract bool UnmapPage(ulong virtAddr);

    /// <summary>
    /// Get the page table entry for a virtual address.
    /// Returns the raw entry value, or 0 if not mapped.
    /// </summary>
    static abstract ulong GetPageEntry(ulong virtAddr);

    /// <summary>
    /// Change protection flags for an existing page.
    /// </summary>
    /// <param name="virtAddr">Virtual address</param>
    /// <param name="newFlags">New protection flags</param>
    /// <returns>Old flags, or 0 if page was not mapped</returns>
    static abstract PageFlags ChangeProtection(ulong virtAddr, PageFlags newFlags);

    // ==================== TLB Management ====================

    /// <summary>
    /// Invalidate a single TLB entry for the given virtual address.
    /// </summary>
    static abstract void InvalidatePage(ulong virtAddr);

    /// <summary>
    /// Flush the entire TLB.
    /// </summary>
    static abstract void FlushTlb();

    // ==================== Utility ====================

    /// <summary>
    /// Check if an address is page-aligned.
    /// </summary>
    static abstract bool IsPageAligned(ulong addr);

    /// <summary>
    /// Round address down to page boundary.
    /// </summary>
    static abstract ulong PageAlignDown(ulong addr);

    /// <summary>
    /// Round address up to page boundary.
    /// </summary>
    static abstract ulong PageAlignUp(ulong addr);
}
