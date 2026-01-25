// ProtonOS kernel - x64 Virtual Memory Manager
// Implements 4-level paging (PML4 -> PDPT -> PD -> PT) for x64.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;

using ArchPageFlags = ProtonOS.Arch.PageFlags;

namespace ProtonOS.X64;

/// <summary>
/// Page table entry flags for x64 4-level paging
/// </summary>
public static class PageFlags
{
    public const ulong Present = 1UL << 0;         // Page is present in memory
    public const ulong Writable = 1UL << 1;        // Page is writable
    public const ulong User = 1UL << 2;            // Page is accessible from user mode
    public const ulong WriteThrough = 1UL << 3;    // Write-through caching
    public const ulong CacheDisable = 1UL << 4;    // Disable caching
    public const ulong Accessed = 1UL << 5;        // Page has been accessed
    public const ulong Dirty = 1UL << 6;           // Page has been written to
    public const ulong HugePage = 1UL << 7;        // 2MB page (in PD) or 1GB page (in PDPT)
    public const ulong Global = 1UL << 8;          // Don't invalidate TLB on CR3 switch
    public const ulong NoExecute = 1UL << 63;      // No execute (requires NXE in EFER)

    // Common combinations
    public const ulong KernelData = Present | Writable | NoExecute;
    public const ulong KernelCode = Present;       // Read-only, executable
    public const ulong KernelRW = Present | Writable;
    public const ulong UserData = Present | Writable | User | NoExecute;
    public const ulong UserCode = Present | User;

    // Mask to extract physical address from page table entry
    public const ulong AddressMask = 0x000F_FFFF_FFFF_F000;
}

/// <summary>
/// x64 Virtual Memory Manager
/// Manages page tables and virtual-to-physical mappings.
/// Implements IVirtualMemory interface for architecture abstraction.
/// Note: This is a struct (not static class) to enable static abstract interface implementation,
/// but all members remain static. Use VirtualMemory.Method() syntax as before.
/// </summary>
public unsafe struct VirtualMemory : ProtonOS.Arch.IVirtualMemory<VirtualMemory>
{
    // Page sizes (const for internal use)
    private const ulong _pageSize = 4096;           // 4KB
    private const ulong _largePageSize = 2097152;   // 2MB
    private const ulong _hugePageSize = 1073741824; // 1GB

    // Interface properties (expose as static properties for IVirtualMemory)
    public static ulong PageSize => _pageSize;
    public static ulong LargePageSize => _largePageSize;
    public static ulong HugePageSize => _hugePageSize;

    // Page shift constant
    private const int _pageShift = 12;
    public static int PageShift => _pageShift;

    // Memory layout:
    // 0x0000_0000_0000_0000 - 0x0000_0000_0000_0FFF : Null guard page (unmapped)
    // 0x0000_0000_0000_1000 - 0x0000_0000_FFFF_FFFF : Kernel space (first 4GB)
    // 0x0000_0001_0000_0000 - 0x0000_7FFF_FFFF_FFFF : User space (rest of lower half)
    // 0xFFFF_8000_0000_0000+                        : Physical memory map

    // Kernel occupies the first 4GB (where UEFI loads it)
    public const ulong KernelBase = 0x0000_0000_0000_1000;  // After null guard
    public const ulong KernelEnd = 0x0000_0001_0000_0000;   // 4GB boundary
    public const ulong KernelSize = KernelEnd - KernelBase;

    // User space starts after kernel area
    public const ulong UserBase = 0x0000_0001_0000_0000;    // 4GB
    public const ulong UserEnd = 0x0000_8000_0000_0000;     // End of lower canonical half

    // Physical memory map in higher half (for kernel to access any physical address)
    private const ulong _physicalMapBase = 0xFFFF_8000_0000_0000;
    public static ulong PhysicalMapBase => _physicalMapBase;

    // Size of physical memory to map (4GB covers most systems)
    public const ulong PhysicalMapSize = 4UL * 1024 * 1024 * 1024;

    // Virtual address structure (48-bit canonical):
    // Bits 0-11:  Page offset (12 bits)
    // Bits 12-20: PT index (9 bits)
    // Bits 21-29: PD index (9 bits)
    // Bits 30-38: PDPT index (9 bits)
    // Bits 39-47: PML4 index (9 bits)
    // Bits 48-63: Sign extension of bit 47

    private const int EntriesPerTable = 512;
    private const ulong IndexMask = 0x1FF;  // 9 bits

    // PML4 (root page table) physical address
    private static ulong _pml4PhysAddr;
    private static bool _initialized;

    /// <summary>
    /// Physical address of the PML4 (root page table)
    /// </summary>
    public static ulong Pml4Address => _pml4PhysAddr;

    /// <summary>
    /// Whether paging has been initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Convert a physical address to its higher-half virtual address
    /// </summary>
    public static ulong PhysToVirt(ulong physAddr) => physAddr + PhysicalMapBase;

    /// <summary>
    /// Convert a higher-half virtual address to its physical address
    /// </summary>
    public static ulong VirtToPhys(ulong virtAddr) => virtAddr - PhysicalMapBase;

    /// <summary>
    /// Convert a physical pointer to a higher-half virtual pointer
    /// </summary>
    public static T* PhysToVirt<T>(T* physPtr) where T : unmanaged
        => (T*)((ulong)physPtr + PhysicalMapBase);

    /// <summary>
    /// Convert a higher-half virtual pointer to a physical pointer
    /// </summary>
    public static T* VirtToPhys<T>(T* virtPtr) where T : unmanaged
        => (T*)((ulong)virtPtr - PhysicalMapBase);

    /// <summary>
    /// Initialize virtual memory with identity mapping for kernel.
    /// Must be called after PageAllocator is initialized.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        if (!PageAllocator.IsInitialized)
        {
            DebugConsole.WriteLine("[VMem] PageAllocator not initialized!");
            return false;
        }

        DebugConsole.WriteLine("[VMem] Initializing virtual memory...");

        // Allocate PML4
        _pml4PhysAddr = PageAllocator.AllocatePage();
        if (_pml4PhysAddr == 0)
        {
            DebugConsole.WriteLine("[VMem] Failed to allocate PML4!");
            return false;
        }

        // Zero out PML4
        ZeroPage(_pml4PhysAddr);

        DebugConsole.Write("[VMem] PML4 at 0x");
        DebugConsole.WriteHex(_pml4PhysAddr);
        DebugConsole.WriteLine();

        // Map kernel space (first 4GB) using 2MB pages
        // We map from 0 but will unmap page 0 separately for null guard
        if (!IdentityMapRange(0, PhysicalMapSize))
        {
            DebugConsole.WriteLine("[VMem] Failed to create kernel mapping!");
            return false;
        }

        // Unmap page 0 (null guard) - this catches null pointer dereferences
        // We need to use 4KB pages for the first 2MB to allow unmapping just page 0
        UnmapNullGuardPage();

        DebugConsole.WriteLine("[VMem] Kernel space: 0x1000 - 0x1_0000_0000 (null guard at 0x0)");

        // Map physical memory to higher half (0xFFFF_8000_0000_0000+)
        // This allows kernel to access any physical memory through higher-half pointers
        if (!MapHigherHalf(0, PhysicalMapSize))
        {
            DebugConsole.WriteLine("[VMem] Failed to create physical memory map!");
            return false;
        }

        DebugConsole.Write("[VMem] Physical map: 0x");
        DebugConsole.WriteHex(PhysicalMapBase);
        DebugConsole.WriteLine(" (4GB)");

        // Switch to our page tables
        DebugConsole.Write("[VMem] Loading CR3 with 0x");
        DebugConsole.WriteHex(_pml4PhysAddr);
        DebugConsole.WriteLine();

        CPU.WriteCr3(_pml4PhysAddr);

        _initialized = true;
        DebugConsole.WriteLine("[VMem] Initialized (kernel 0-4GB, physmap in higher half)");
        return true;
    }

    /// <summary>
    /// Identity map a range of physical memory using 2MB pages
    /// </summary>
    private static bool IdentityMapRange(ulong physStart, ulong size)
    {
        // Align to 2MB boundary
        physStart &= ~(LargePageSize - 1);
        ulong physEnd = physStart + size;

        for (ulong addr = physStart; addr < physEnd; addr += LargePageSize)
        {
            if (!MapLargePage(addr, addr, PageFlags.KernelRW))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Map physical memory to higher-half virtual addresses using 2MB pages.
    /// Maps physical 0 -> PhysicalMapBase, physical 2MB -> PhysicalMapBase + 2MB, etc.
    /// </summary>
    private static bool MapHigherHalf(ulong physStart, ulong size)
    {
        // Align to 2MB boundary
        physStart &= ~(LargePageSize - 1);
        ulong physEnd = physStart + size;

        for (ulong phys = physStart; phys < physEnd; phys += LargePageSize)
        {
            ulong virt = phys + PhysicalMapBase;
            if (!MapLargePage(virt, phys, PageFlags.KernelRW))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Map a 2MB large page (virtual -> physical)
    /// </summary>
    public static bool MapLargePage(ulong virtAddr, ulong physAddr, ulong flags)
    {
        // Ensure addresses are 2MB aligned
        if ((virtAddr & (LargePageSize - 1)) != 0 ||
            (physAddr & (LargePageSize - 1)) != 0)
            return false;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);

        // Get or create PDPT
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pdptPhys = GetOrCreateTable(pml4, pml4Index);
        if (pdptPhys == 0)
            return false;

        // Get or create PD
        ulong* pdpt = (ulong*)pdptPhys;
        ulong pdPhys = GetOrCreateTable(pdpt, pdptIndex);
        if (pdPhys == 0)
            return false;

        // Set PD entry for 2MB page
        ulong* pd = (ulong*)pdPhys;
        pd[pdIndex] = (physAddr & PageFlags.AddressMask) | flags | PageFlags.HugePage | PageFlags.Present;

        return true;
    }

    /// <summary>
    /// Map a 4KB page (virtual -> physical)
    /// </summary>
    public static bool MapPage(ulong virtAddr, ulong physAddr, ulong flags)
    {
        // Ensure addresses are 4KB aligned
        if ((virtAddr & (PageSize - 1)) != 0 ||
            (physAddr & (PageSize - 1)) != 0)
            return false;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        // Get or create PDPT
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pdptPhys = GetOrCreateTable(pml4, pml4Index);
        if (pdptPhys == 0)
            return false;

        // Get or create PD
        ulong* pdpt = (ulong*)pdptPhys;
        ulong pdPhys = GetOrCreateTable(pdpt, pdptIndex);
        if (pdPhys == 0)
            return false;

        // Get or create PT
        ulong* pd = (ulong*)pdPhys;
        ulong ptPhys = GetOrCreateTable(pd, pdIndex);
        if (ptPhys == 0)
            return false;

        // Set PT entry for 4KB page
        ulong* pt = (ulong*)ptPhys;
        pt[ptIndex] = (physAddr & PageFlags.AddressMask) | flags | PageFlags.Present;

        return true;
    }

    /// <summary>
    /// Get or create a page table at the given index
    /// </summary>
    private static ulong GetOrCreateTable(ulong* parentTable, int index)
    {
        ulong entry = parentTable[index];

        if ((entry & PageFlags.Present) != 0)
        {
            // Table already exists
            return entry & PageFlags.AddressMask;
        }

        // Allocate new table
        ulong newTable = PageAllocator.AllocatePage();
        if (newTable == 0)
            return 0;

        ZeroPage(newTable);

        // Set parent entry
        parentTable[index] = newTable | PageFlags.Present | PageFlags.Writable | PageFlags.User;

        return newTable;
    }

    /// <summary>
    /// Zero out a 4KB page
    /// </summary>
    private static void ZeroPage(ulong physAddr)
    {
        ulong* ptr = (ulong*)physAddr;
        for (int i = 0; i < 512; i++)
            ptr[i] = 0;
    }

    /// <summary>
    /// Unmap page 0 to create null guard.
    /// This converts the first 2MB large page into 4KB pages, then unmaps page 0.
    /// </summary>
    private static void UnmapNullGuardPage()
    {
        // Get PML4 -> PDPT -> PD for virtual address 0
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pdptPhys = pml4[0] & PageFlags.AddressMask;
        if (pdptPhys == 0) return;

        ulong* pdpt = (ulong*)pdptPhys;
        ulong pdPhys = pdpt[0] & PageFlags.AddressMask;
        if (pdPhys == 0) return;

        ulong* pd = (ulong*)pdPhys;
        ulong pdEntry = pd[0];

        // Check if this is a 2MB large page
        if ((pdEntry & PageFlags.HugePage) == 0)
            return;  // Already using 4KB pages

        // Get the physical address this 2MB page maps to (should be 0)
        ulong largePagePhys = pdEntry & PageFlags.AddressMask;

        // Allocate a page table for 4KB pages
        ulong ptPhys = PageAllocator.AllocatePage();
        if (ptPhys == 0) return;

        ulong* pt = (ulong*)ptPhys;

        // Map pages 1-511 (skip page 0 for null guard)
        // Page 0 entry stays 0 (not present)
        pt[0] = 0;  // Null guard - not present
        for (int i = 1; i < 512; i++)
        {
            ulong pagePhys = largePagePhys + (ulong)i * PageSize;
            pt[i] = pagePhys | PageFlags.Present | PageFlags.Writable;
        }

        // Replace the 2MB page entry with pointer to our PT
        // Remove HugePage flag since we're now using a page table
        pd[0] = ptPhys | PageFlags.Present | PageFlags.Writable;

        // Note: Don't call WriteCr3 here - we're still using UEFI's page tables.
        // The TLB will be flushed when we call WriteCr3 at the end of Init().
    }

    /// <summary>
    /// Invalidate a single TLB entry
    /// </summary>
    public static void InvalidatePage(ulong virtAddr)
    {
        CPU.Invlpg(virtAddr);
    }

    /// <summary>
    /// Get the page table entry for a virtual address.
    /// Returns 0 if not mapped.
    /// </summary>
    public static ulong GetPageEntry(ulong virtAddr)
    {
        if (_pml4PhysAddr == 0)
            return 0;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        // Walk PML4
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pml4Entry = pml4[pml4Index];
        if ((pml4Entry & PageFlags.Present) == 0)
            return 0;

        // Walk PDPT
        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIndex];
        if ((pdptEntry & PageFlags.Present) == 0)
            return 0;

        // Check for 1GB huge page
        if ((pdptEntry & PageFlags.HugePage) != 0)
            return pdptEntry;

        // Walk PD
        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIndex];
        if ((pdEntry & PageFlags.Present) == 0)
            return 0;

        // Check for 2MB large page
        if ((pdEntry & PageFlags.HugePage) != 0)
            return pdEntry;

        // Walk PT
        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        return pt[ptIndex];
    }

    /// <summary>
    /// Change the protection flags on a page.
    /// Returns the old flags, or 0 if page is not mapped.
    /// </summary>
    public static ulong ChangePageProtection(ulong virtAddr, ulong newFlags)
    {
        if (_pml4PhysAddr == 0)
            return 0;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        // Walk PML4
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pml4Entry = pml4[pml4Index];
        if ((pml4Entry & PageFlags.Present) == 0)
            return 0;

        // Walk PDPT
        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIndex];
        if ((pdptEntry & PageFlags.Present) == 0)
            return 0;

        // Check for 1GB huge page
        if ((pdptEntry & PageFlags.HugePage) != 0)
        {
            ulong oldEntry = pdptEntry;
            ulong physAddr = oldEntry & PageFlags.AddressMask;
            pdpt[pdptIndex] = physAddr | newFlags | PageFlags.HugePage | PageFlags.Present;
            InvalidatePage(virtAddr);
            return oldEntry;
        }

        // Walk PD
        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIndex];
        if ((pdEntry & PageFlags.Present) == 0)
            return 0;

        // Check for 2MB large page
        if ((pdEntry & PageFlags.HugePage) != 0)
        {
            ulong oldEntry = pdEntry;
            ulong physAddr = oldEntry & PageFlags.AddressMask;
            pd[pdIndex] = physAddr | newFlags | PageFlags.HugePage | PageFlags.Present;
            InvalidatePage(virtAddr);
            return oldEntry;
        }

        // Walk PT - 4KB page
        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        ulong ptEntry = pt[ptIndex];
        if ((ptEntry & PageFlags.Present) == 0)
            return 0;

        ulong oldPtEntry = ptEntry;
        ulong pagePhysAddr = ptEntry & PageFlags.AddressMask;
        pt[ptIndex] = pagePhysAddr | newFlags | PageFlags.Present;
        InvalidatePage(virtAddr);
        return oldPtEntry;
    }

    /// <summary>
    /// Change protection on a range of pages.
    /// </summary>
    public static bool ChangeRangeProtection(ulong virtAddr, ulong size, ulong newFlags, out ulong oldFlags)
    {
        oldFlags = 0;

        if (size == 0)
            return false;

        // Align to page boundaries
        ulong startPage = virtAddr & ~(PageSize - 1);
        ulong endAddr = virtAddr + size;
        ulong endPage = (endAddr + PageSize - 1) & ~(PageSize - 1);

        bool first = true;
        for (ulong addr = startPage; addr < endPage; addr += PageSize)
        {
            ulong old = ChangePageProtection(addr, newFlags);
            if (old == 0)
                return false;

            if (first)
            {
                oldFlags = old;
                first = false;
            }
        }

        return true;
    }

    /// <summary>
    /// Set or clear the User bit on a page and all parent page table entries.
    /// This is required for user-mode access because the User bit must be set
    /// on all levels of the page table hierarchy.
    /// </summary>
    /// <param name="virtAddr">Virtual address of the page</param>
    /// <param name="userAccessible">True to allow user access, false to deny</param>
    /// <returns>True if successful, false if page is not mapped</returns>
    public static bool SetUserAccessible(ulong virtAddr, bool userAccessible)
    {
        if (_pml4PhysAddr == 0)
            return false;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        // Walk and modify PML4
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pml4Entry = pml4[pml4Index];
        if ((pml4Entry & PageFlags.Present) == 0)
            return false;

        if (userAccessible)
            pml4[pml4Index] = pml4Entry | PageFlags.User;
        else
            pml4[pml4Index] = pml4Entry & ~PageFlags.User;

        // Walk and modify PDPT
        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIndex];
        if ((pdptEntry & PageFlags.Present) == 0)
            return false;

        if (userAccessible)
            pdpt[pdptIndex] = pdptEntry | PageFlags.User;
        else
            pdpt[pdptIndex] = pdptEntry & ~PageFlags.User;

        // Check for 1GB huge page
        if ((pdptEntry & PageFlags.HugePage) != 0)
        {
            InvalidatePage(virtAddr);
            return true;
        }

        // Walk and modify PD
        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIndex];
        if ((pdEntry & PageFlags.Present) == 0)
            return false;

        if (userAccessible)
            pd[pdIndex] = pdEntry | PageFlags.User;
        else
            pd[pdIndex] = pdEntry & ~PageFlags.User;

        // Check for 2MB large page
        if ((pdEntry & PageFlags.HugePage) != 0)
        {
            InvalidatePage(virtAddr);
            return true;
        }

        // Walk and modify PT - 4KB page
        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        ulong ptEntry = pt[ptIndex];
        if ((ptEntry & PageFlags.Present) == 0)
            return false;

        if (userAccessible)
            pt[ptIndex] = ptEntry | PageFlags.User;
        else
            pt[ptIndex] = ptEntry & ~PageFlags.User;

        InvalidatePage(virtAddr);
        return true;
    }

    // ==================== Virtual Address Space Management ====================

    // Virtual allocation tracking
    private static VirtualAllocationEntry* _allocListHead;
    private static SpinLock _allocLock;

    // Next virtual address for dynamic allocations (start at 8GB, above kernel space)
    private static ulong _nextVirtualAddress = 0x0000_0002_0000_0000;

    /// <summary>
    /// Allocate virtual address space and optionally commit physical pages.
    /// </summary>
    /// <param name="requestedAddress">Desired address (0 for any)</param>
    /// <param name="size">Size in bytes (will be rounded up to page size)</param>
    /// <param name="commit">If true, allocate and map physical pages</param>
    /// <param name="flags">Page flags to use when mapping</param>
    /// <returns>Base address of allocation, or 0 on failure</returns>
    public static ulong AllocateVirtualRange(ulong requestedAddress, ulong size, bool commit, ulong flags)
    {
        if (size == 0)
            return 0;

        // Round up to page boundary
        size = (size + PageSize - 1) & ~(PageSize - 1);

        ulong virtAddr;
        if (requestedAddress != 0)
        {
            // Use requested address (must be page-aligned)
            if ((requestedAddress & (PageSize - 1)) != 0)
                return 0;
            virtAddr = requestedAddress;
        }
        else
        {
            // Allocate from next available address
            _allocLock.Acquire();
            virtAddr = _nextVirtualAddress;
            _nextVirtualAddress += size;
            _allocLock.Release();
        }

        // If committing, actually allocate physical pages and map them
        if (commit)
        {
            ulong numPages = size / PageSize;

            for (ulong i = 0; i < numPages; i++)
            {
                // Allocate physical page
                ulong physPage = PageAllocator.AllocatePage();
                if (physPage == 0)
                {
                    // Allocation failed - should unmap what we've done
                    return 0;
                }

                // Zero the page
                byte* pagePtr = (byte*)physPage;
                for (int j = 0; j < (int)PageSize; j++)
                    pagePtr[j] = 0;

                // Map virtual to physical
                ulong virt = virtAddr + i * PageSize;
                if (!MapPage(virt, physPage, flags))
                {
                    PageAllocator.FreePage(physPage);
                    return 0;
                }

                InvalidatePage(virt);
            }
        }

        // Track the allocation
        var entry = (VirtualAllocationEntry*)HeapAllocator.AllocZeroed((ulong)sizeof(VirtualAllocationEntry));
        if (entry != null)
        {
            entry->BaseAddress = virtAddr;
            entry->Size = size;
            entry->IsCommitted = commit;
            entry->Flags = flags;

            _allocLock.Acquire();
            entry->Next = _allocListHead;
            _allocListHead = entry;
            _allocLock.Release();
        }

        return virtAddr;
    }

    /// <summary>
    /// Free virtual address space.
    /// </summary>
    /// <param name="address">Base address of allocation</param>
    /// <param name="releaseAll">If true, release entire allocation; if false, just decommit</param>
    /// <returns>True on success</returns>
    public static bool FreeVirtualRange(ulong address, bool releaseAll)
    {
        if (address == 0)
            return false;

        // Find the allocation
        _allocLock.Acquire();
        VirtualAllocationEntry* prev = null;
        VirtualAllocationEntry* current = _allocListHead;

        while (current != null)
        {
            if (current->BaseAddress == address)
                break;
            prev = current;
            current = current->Next;
        }

        if (current == null)
        {
            _allocLock.Release();
            return false;
        }

        ulong size = current->Size;

        if (releaseAll)
        {
            // Remove from list
            if (prev != null)
                prev->Next = current->Next;
            else
                _allocListHead = current->Next;

            _allocLock.Release();

            // Invalidate TLB entries
            ulong numPages = size / PageSize;
            for (ulong i = 0; i < numPages; i++)
            {
                InvalidatePage(address + i * PageSize);
            }

            HeapAllocator.Free(current);
        }
        else
        {
            // Just decommit - keep the reservation
            current->IsCommitted = false;
            _allocLock.Release();

            // Invalidate TLB entries
            ulong numPages = size / PageSize;
            for (ulong i = 0; i < numPages; i++)
            {
                InvalidatePage(address + i * PageSize);
            }
        }

        return true;
    }

    /// <summary>
    /// Find a virtual allocation by address.
    /// </summary>
    /// <param name="address">Address within the allocation</param>
    /// <param name="outSize">Output: size of allocation</param>
    /// <returns>Base address of allocation, or 0 if not found</returns>
    public static ulong FindAllocation(ulong address, out ulong outSize)
    {
        outSize = 0;

        _allocLock.Acquire();
        var current = _allocListHead;

        while (current != null)
        {
            if (address >= current->BaseAddress &&
                address < current->BaseAddress + current->Size)
            {
                outSize = current->Size;
                _allocLock.Release();
                return current->BaseAddress;
            }
            current = current->Next;
        }

        _allocLock.Release();
        return 0;
    }

    // ==================== IVirtualMemory Interface Implementation ====================

    /// <summary>
    /// Convert architecture-neutral PageFlags to x64 page table flags.
    /// </summary>
    private static ulong ConvertFlags(ArchPageFlags flags)
    {
        ulong result = 0;
        if ((flags & ArchPageFlags.Present) != 0) result |= PageFlags.Present;
        if ((flags & ArchPageFlags.Writable) != 0) result |= PageFlags.Writable;
        if ((flags & ArchPageFlags.User) != 0) result |= PageFlags.User;
        if ((flags & ArchPageFlags.WriteThrough) != 0) result |= PageFlags.WriteThrough;
        if ((flags & ArchPageFlags.NoCache) != 0) result |= PageFlags.CacheDisable;
        if ((flags & ArchPageFlags.Accessed) != 0) result |= PageFlags.Accessed;
        if ((flags & ArchPageFlags.Dirty) != 0) result |= PageFlags.Dirty;
        if ((flags & ArchPageFlags.LargePage) != 0) result |= PageFlags.HugePage;
        if ((flags & ArchPageFlags.Global) != 0) result |= PageFlags.Global;
        if ((flags & ArchPageFlags.NoExecute) != 0) result |= PageFlags.NoExecute;
        return result;
    }

    /// <summary>
    /// Convert x64 page table flags to architecture-neutral PageFlags.
    /// </summary>
    private static ArchPageFlags ConvertToArchFlags(ulong flags)
    {
        ArchPageFlags result = ArchPageFlags.None;
        if ((flags & PageFlags.Present) != 0) result |= ArchPageFlags.Present;
        if ((flags & PageFlags.Writable) != 0) result |= ArchPageFlags.Writable;
        if ((flags & PageFlags.User) != 0) result |= ArchPageFlags.User;
        if ((flags & PageFlags.WriteThrough) != 0) result |= ArchPageFlags.WriteThrough;
        if ((flags & PageFlags.CacheDisable) != 0) result |= ArchPageFlags.NoCache;
        if ((flags & PageFlags.Accessed) != 0) result |= ArchPageFlags.Accessed;
        if ((flags & PageFlags.Dirty) != 0) result |= ArchPageFlags.Dirty;
        if ((flags & PageFlags.HugePage) != 0) result |= ArchPageFlags.LargePage;
        if ((flags & PageFlags.Global) != 0) result |= ArchPageFlags.Global;
        if ((flags & PageFlags.NoExecute) != 0) result |= ArchPageFlags.NoExecute;
        return result;
    }

    /// <summary>
    /// Map a 4KB page (IVirtualMemory interface).
    /// </summary>
    public static bool MapPage(ulong virtAddr, ulong physAddr, ArchPageFlags flags)
    {
        return MapPage(virtAddr, physAddr, ConvertFlags(flags));
    }

    /// <summary>
    /// Map a 2MB large page (IVirtualMemory interface).
    /// </summary>
    public static bool MapLargePage(ulong virtAddr, ulong physAddr, ArchPageFlags flags)
    {
        return MapLargePage(virtAddr, physAddr, ConvertFlags(flags));
    }

    /// <summary>
    /// Unmap a page at the given virtual address.
    /// </summary>
    public static bool UnmapPage(ulong virtAddr)
    {
        if (_pml4PhysAddr == 0)
            return false;

        // Get table indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        // Walk to PT
        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pml4Entry = pml4[pml4Index];
        if ((pml4Entry & PageFlags.Present) == 0)
            return false;

        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIndex];
        if ((pdptEntry & PageFlags.Present) == 0)
            return false;

        // 1GB page - can't unmap individual 4KB page within it
        if ((pdptEntry & PageFlags.HugePage) != 0)
            return false;

        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIndex];
        if ((pdEntry & PageFlags.Present) == 0)
            return false;

        // 2MB page - can't unmap individual 4KB page within it
        if ((pdEntry & PageFlags.HugePage) != 0)
            return false;

        // Clear the PT entry
        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        pt[ptIndex] = 0;

        InvalidatePage(virtAddr);
        return true;
    }

    /// <summary>
    /// Change protection flags for an existing page (IVirtualMemory interface).
    /// </summary>
    public static ArchPageFlags ChangeProtection(ulong virtAddr, ArchPageFlags newFlags)
    {
        ulong oldFlags = ChangePageProtection(virtAddr, ConvertFlags(newFlags));
        return ConvertToArchFlags(oldFlags);
    }

    /// <summary>
    /// Flush the entire TLB.
    /// </summary>
    public static void FlushTlb()
    {
        CPU.FlushTlb();
    }

    /// <summary>
    /// Check if an address is page-aligned.
    /// </summary>
    public static bool IsPageAligned(ulong addr)
    {
        return (addr & (_pageSize - 1)) == 0;
    }

    /// <summary>
    /// Round address down to page boundary.
    /// </summary>
    public static ulong PageAlignDown(ulong addr)
    {
        return addr & ~(_pageSize - 1);
    }

    /// <summary>
    /// Round address up to page boundary.
    /// </summary>
    public static ulong PageAlignUp(ulong addr)
    {
        return (addr + _pageSize - 1) & ~(_pageSize - 1);
    }

    /// <summary>
    /// Dump page table walk for debugging.
    /// Shows each level of the page table hierarchy for a virtual address.
    /// </summary>
    public static void DumpPageTableWalk(ulong virtAddr)
    {
        DebugConsole.Write("[VMem] Page table walk for 0x");
        DebugConsole.WriteHex(virtAddr);
        DebugConsole.WriteLine();

        if (_pml4PhysAddr == 0)
        {
            DebugConsole.WriteLine("  ERROR: PML4 not initialized!");
            return;
        }

        // Calculate indices
        int pml4Index = (int)((virtAddr >> 39) & IndexMask);
        int pdptIndex = (int)((virtAddr >> 30) & IndexMask);
        int pdIndex = (int)((virtAddr >> 21) & IndexMask);
        int ptIndex = (int)((virtAddr >> 12) & IndexMask);

        DebugConsole.Write("  Indices: PML4=");
        DebugConsole.WriteDecimal(pml4Index);
        DebugConsole.Write(" PDPT=");
        DebugConsole.WriteDecimal(pdptIndex);
        DebugConsole.Write(" PD=");
        DebugConsole.WriteDecimal(pdIndex);
        DebugConsole.Write(" PT=");
        DebugConsole.WriteDecimal(ptIndex);
        DebugConsole.WriteLine();

        // Walk PML4
        DebugConsole.Write("  PML4 at 0x");
        DebugConsole.WriteHex(_pml4PhysAddr);
        DebugConsole.WriteLine();

        ulong* pml4 = (ulong*)_pml4PhysAddr;
        ulong pml4Entry = pml4[pml4Index];
        DebugConsole.Write("  PML4[");
        DebugConsole.WriteDecimal(pml4Index);
        DebugConsole.Write("] = 0x");
        DebugConsole.WriteHex(pml4Entry);
        DebugConsole.WriteLine();

        if ((pml4Entry & PageFlags.Present) == 0)
        {
            DebugConsole.WriteLine("  -> NOT PRESENT (page fault expected)");
            return;
        }

        // Walk PDPT
        ulong pdptPhys = pml4Entry & PageFlags.AddressMask;
        DebugConsole.Write("  PDPT at 0x");
        DebugConsole.WriteHex(pdptPhys);
        DebugConsole.WriteLine();

        ulong* pdpt = (ulong*)pdptPhys;
        ulong pdptEntry = pdpt[pdptIndex];
        DebugConsole.Write("  PDPT[");
        DebugConsole.WriteDecimal(pdptIndex);
        DebugConsole.Write("] = 0x");
        DebugConsole.WriteHex(pdptEntry);
        DebugConsole.WriteLine();

        if ((pdptEntry & PageFlags.Present) == 0)
        {
            DebugConsole.WriteLine("  -> NOT PRESENT (page fault expected)");
            return;
        }

        if ((pdptEntry & PageFlags.HugePage) != 0)
        {
            ulong physBase = pdptEntry & PageFlags.AddressMask;
            DebugConsole.Write("  -> 1GB page at phys 0x");
            DebugConsole.WriteHex(physBase);
            DebugConsole.WriteLine();
            return;
        }

        // Walk PD
        ulong pdPhys = pdptEntry & PageFlags.AddressMask;
        DebugConsole.Write("  PD at 0x");
        DebugConsole.WriteHex(pdPhys);
        DebugConsole.WriteLine();

        ulong* pd = (ulong*)pdPhys;
        ulong pdEntry = pd[pdIndex];
        DebugConsole.Write("  PD[");
        DebugConsole.WriteDecimal(pdIndex);
        DebugConsole.Write("] = 0x");
        DebugConsole.WriteHex(pdEntry);
        DebugConsole.WriteLine();

        if ((pdEntry & PageFlags.Present) == 0)
        {
            DebugConsole.WriteLine("  -> NOT PRESENT (page fault expected)");
            return;
        }

        if ((pdEntry & PageFlags.HugePage) != 0)
        {
            ulong physBase = pdEntry & PageFlags.AddressMask;
            DebugConsole.Write("  -> 2MB page at phys 0x");
            DebugConsole.WriteHex(physBase);
            DebugConsole.WriteLine(" (MAPPED OK)");
            return;
        }

        // Walk PT (4KB pages)
        ulong ptPhys = pdEntry & PageFlags.AddressMask;
        DebugConsole.Write("  PT at 0x");
        DebugConsole.WriteHex(ptPhys);
        DebugConsole.WriteLine();

        ulong* pt = (ulong*)ptPhys;
        ulong ptEntry = pt[ptIndex];
        DebugConsole.Write("  PT[");
        DebugConsole.WriteDecimal(ptIndex);
        DebugConsole.Write("] = 0x");
        DebugConsole.WriteHex(ptEntry);
        DebugConsole.WriteLine();

        if ((ptEntry & PageFlags.Present) == 0)
        {
            DebugConsole.WriteLine("  -> NOT PRESENT (page fault expected)");
        }
        else
        {
            ulong physBase = ptEntry & PageFlags.AddressMask;
            DebugConsole.Write("  -> 4KB page at phys 0x");
            DebugConsole.WriteHex(physBase);
            DebugConsole.WriteLine(" (MAPPED OK)");
        }
    }
}

/// <summary>
/// Tracks a virtual memory allocation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct VirtualAllocationEntry
{
    public ulong BaseAddress;
    public ulong Size;
    public bool IsCommitted;
    public ulong Flags;
    public VirtualAllocationEntry* Next;
}
