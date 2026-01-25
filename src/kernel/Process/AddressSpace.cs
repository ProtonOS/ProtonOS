// ProtonOS kernel - Per-Process Address Space Management
// Creates and manages separate virtual address spaces for user processes.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.Process;

/// <summary>
/// User-space virtual address layout constants
/// </summary>
public static class UserLayout
{
    // User-space occupies the lower canonical half (0 to 0x00007FFF_FFFFFFFF)
    // Kernel uses higher half starting at 0xFFFF8000_00000000

    /// <summary>
    /// Base of user-accessible virtual memory (above kernel-mapped region)
    /// Kernel maps 0-256MB as kernel-only, so user space starts at 256MB
    /// </summary>
    public const ulong UserBase = 0x0000_0000_1000_0000;  // 256MB

    /// <summary>
    /// End of user-accessible virtual memory (top of lower canonical half)
    /// </summary>
    public const ulong UserEnd = 0x0000_7FFF_FFFF_FFFF;

    /// <summary>
    /// Default user stack top (grows down from here)
    /// </summary>
    public const ulong UserStackTop = 0x0000_7FFF_FFFF_0000;  // Just below UserEnd

    /// <summary>
    /// Default user stack size
    /// </summary>
    public const ulong UserStackSize = 8 * 1024 * 1024;  // 8MB

    /// <summary>
    /// Default user heap start (grows up from after image)
    /// </summary>
    public const ulong UserHeapStart = 0x0000_0000_2000_0000;  // 512MB

    /// <summary>
    /// Maximum user heap size
    /// </summary>
    public const ulong UserHeapMaxSize = 0x0000_0010_0000_0000;  // 64GB

    /// <summary>
    /// Base address for loading user programs (above kernel region)
    /// Kernel maps 0-256MB kernel-only, so user images start at 256MB
    /// </summary>
    public const ulong UserImageBase = 0x0000_0000_1000_0000;  // 256MB

    /// <summary>
    /// Base for mmap allocations (grows up from here)
    /// </summary>
    public const ulong MmapBase = 0x0000_0002_0000_0000;  // 8GB

    /// <summary>
    /// Maximum size for mmap region
    /// </summary>
    public const ulong MmapMaxSize = 0x0000_0080_0000_0000;  // 512GB

    /// <summary>
    /// Page size (4KB)
    /// </summary>
    public const ulong PageSize = 4096;

    /// <summary>
    /// Large page size (2MB)
    /// </summary>
    public const ulong LargePageSize = 2 * 1024 * 1024;
}

/// <summary>
/// Per-process address space management
/// </summary>
public static unsafe class AddressSpace
{
    private static SpinLock _lock;

    /// <summary>
    /// Create a new user-space address space
    /// </summary>
    /// <returns>Physical address of new PML4, or 0 on failure</returns>
    public static ulong CreateUserSpace()
    {
        // Allocate a new PML4
        ulong pml4Phys = PageAllocator.AllocatePage();
        if (pml4Phys == 0)
        {
            DebugConsole.WriteLine("[AddressSpace] Failed to allocate PML4!");
            return 0;
        }

        // Zero the PML4
        ZeroPage(pml4Phys);

        // Copy kernel mappings (upper half) from current kernel page table
        // This ensures the kernel is accessible when we switch to user mode
        CopyKernelMappings(pml4Phys);

        return pml4Phys;
    }

    /// <summary>
    /// Copy kernel mappings from the kernel's PML4 to a new PML4
    /// </summary>
    private static void CopyKernelMappings(ulong newPml4Phys)
    {
        ulong kernelPml4 = VirtualMemory.Pml4Address;
        if (kernelPml4 == 0)
            return;

        ulong* srcPml4 = (ulong*)kernelPml4;
        ulong* dstPml4 = (ulong*)newPml4Phys;

        // Copy entries 256-511 (upper half of address space)
        // These are the kernel mappings (0xFFFF8000_00000000+)
        for (int i = 256; i < 512; i++)
        {
            dstPml4[i] = srcPml4[i];
        }

        // Also copy PML4[0] from the kernel so the low memory mapping is identical.
        // This shares the kernel's subtables, but since user pages are at 256MB+
        // (PD index 128+), they won't conflict with kernel pages (PD index 0-127).
        //
        // Note: We need to be careful with GetOrCreateTable to not overwrite
        // shared entries. When user pages need entries in the same PDPT/PD,
        // we should add to them rather than replace them.
        dstPml4[0] = srcPml4[0];
    }

    /// <summary>
    /// Destroy a user-space address space
    /// </summary>
    /// <param name="pml4Phys">Physical address of PML4 to destroy</param>
    public static void DestroyUserSpace(ulong pml4Phys)
    {
        if (pml4Phys == 0 || pml4Phys == VirtualMemory.Pml4Address)
            return;

        // Walk and free user-space page tables (entries 0-255)
        // Don't free kernel mappings (256-511)
        ulong* pml4 = (ulong*)pml4Phys;

        for (int pml4Idx = 0; pml4Idx < 256; pml4Idx++)
        {
            ulong pml4Entry = pml4[pml4Idx];
            if ((pml4Entry & PageFlags.Present) == 0)
                continue;

            ulong pdptPhys = pml4Entry & PageFlags.AddressMask;
            ulong* pdpt = (ulong*)pdptPhys;

            for (int pdptIdx = 0; pdptIdx < 512; pdptIdx++)
            {
                ulong pdptEntry = pdpt[pdptIdx];
                if ((pdptEntry & PageFlags.Present) == 0)
                    continue;

                // Check for 1GB huge page
                if ((pdptEntry & PageFlags.HugePage) != 0)
                {
                    // Free the 1GB page
                    ulong hugePagePhys = pdptEntry & PageFlags.AddressMask;
                    // Would need to free 512 consecutive 2MB pages
                    continue;
                }

                ulong pdPhys = pdptEntry & PageFlags.AddressMask;
                ulong* pd = (ulong*)pdPhys;

                for (int pdIdx = 0; pdIdx < 512; pdIdx++)
                {
                    ulong pdEntry = pd[pdIdx];
                    if ((pdEntry & PageFlags.Present) == 0)
                        continue;

                    // Check for 2MB large page
                    if ((pdEntry & PageFlags.HugePage) != 0)
                    {
                        // Free the 2MB page (512 consecutive 4KB pages)
                        ulong largePagePhys = pdEntry & PageFlags.AddressMask;
                        for (int i = 0; i < 512; i++)
                        {
                            PageAllocator.FreePage(largePagePhys + (ulong)i * UserLayout.PageSize);
                        }
                        continue;
                    }

                    // 4KB page table
                    ulong ptPhys = pdEntry & PageFlags.AddressMask;
                    ulong* pt = (ulong*)ptPhys;

                    for (int ptIdx = 0; ptIdx < 512; ptIdx++)
                    {
                        ulong ptEntry = pt[ptIdx];
                        if ((ptEntry & PageFlags.Present) != 0)
                        {
                            // Free the 4KB page (if user-owned, not COW shared)
                            ulong pagePhys = ptEntry & PageFlags.AddressMask;
                            if (!CopyOnWrite.IsShared(pagePhys))
                            {
                                PageAllocator.FreePage(pagePhys);
                            }
                            else
                            {
                                CopyOnWrite.DecrementRef(pagePhys);
                            }
                        }
                    }

                    // Free the PT itself
                    PageAllocator.FreePage(ptPhys);
                }

                // Free the PD
                PageAllocator.FreePage(pdPhys);
            }

            // Free the PDPT
            PageAllocator.FreePage(pdptPhys);
        }

        // Free the PML4 itself
        PageAllocator.FreePage(pml4Phys);
    }

    /// <summary>
    /// Clone an address space for fork (with copy-on-write)
    /// </summary>
    /// <param name="parentPml4">Parent's PML4 physical address</param>
    /// <returns>Child's PML4 physical address, or 0 on failure</returns>
    public static ulong CloneForFork(ulong parentPml4)
    {
        if (parentPml4 == 0)
            return 0;

        // Allocate new PML4 for child
        ulong childPml4Phys = PageAllocator.AllocatePage();
        if (childPml4Phys == 0)
            return 0;

        ZeroPage(childPml4Phys);

        ulong* parentPml4Ptr = (ulong*)parentPml4;
        ulong* childPml4Ptr = (ulong*)childPml4Phys;

        // Copy kernel mappings (upper half)
        for (int i = 256; i < 512; i++)
        {
            childPml4Ptr[i] = parentPml4Ptr[i];
        }

        // Clone user-space mappings with COW (entries 0-255)
        for (int pml4Idx = 0; pml4Idx < 256; pml4Idx++)
        {
            ulong pml4Entry = parentPml4Ptr[pml4Idx];
            if ((pml4Entry & PageFlags.Present) == 0)
                continue;

            // Clone PDPT
            ulong parentPdptPhys = pml4Entry & PageFlags.AddressMask;
            ulong childPdptPhys = ClonePdpt(parentPdptPhys, true);
            if (childPdptPhys == 0)
            {
                // Cleanup and fail
                DestroyUserSpace(childPml4Phys);
                return 0;
            }

            // Set child's PML4 entry
            childPml4Ptr[pml4Idx] = childPdptPhys | (pml4Entry & ~PageFlags.AddressMask);
        }

        return childPml4Phys;
    }

    /// <summary>
    /// Clone a PDPT (for fork)
    /// </summary>
    private static ulong ClonePdpt(ulong parentPdptPhys, bool enableCow)
    {
        ulong childPdptPhys = PageAllocator.AllocatePage();
        if (childPdptPhys == 0)
            return 0;

        ZeroPage(childPdptPhys);

        ulong* parentPdpt = (ulong*)parentPdptPhys;
        ulong* childPdpt = (ulong*)childPdptPhys;

        for (int i = 0; i < 512; i++)
        {
            ulong pdptEntry = parentPdpt[i];
            if ((pdptEntry & PageFlags.Present) == 0)
                continue;

            if ((pdptEntry & PageFlags.HugePage) != 0)
            {
                // 1GB page - mark as COW if writable
                if (enableCow && (pdptEntry & PageFlags.Writable) != 0)
                {
                    ulong pagePhys = pdptEntry & PageFlags.AddressMask;
                    CopyOnWrite.MarkShared(pagePhys);

                    // Remove writable flag, keep other flags
                    pdptEntry &= ~PageFlags.Writable;
                    parentPdpt[i] = pdptEntry;
                }
                childPdpt[i] = pdptEntry;
            }
            else
            {
                // Clone PD
                ulong parentPdPhys = pdptEntry & PageFlags.AddressMask;
                ulong childPdPhys = ClonePd(parentPdPhys, enableCow, parentPdpt, i);
                if (childPdPhys == 0)
                {
                    // Cleanup
                    PageAllocator.FreePage(childPdptPhys);
                    return 0;
                }
                childPdpt[i] = childPdPhys | (pdptEntry & ~PageFlags.AddressMask);
            }
        }

        return childPdptPhys;
    }

    /// <summary>
    /// Clone a PD (for fork)
    /// </summary>
    private static ulong ClonePd(ulong parentPdPhys, bool enableCow, ulong* parentPdpt, int pdptIdx)
    {
        ulong childPdPhys = PageAllocator.AllocatePage();
        if (childPdPhys == 0)
            return 0;

        ZeroPage(childPdPhys);

        ulong* parentPd = (ulong*)parentPdPhys;
        ulong* childPd = (ulong*)childPdPhys;

        for (int i = 0; i < 512; i++)
        {
            ulong pdEntry = parentPd[i];
            if ((pdEntry & PageFlags.Present) == 0)
                continue;

            if ((pdEntry & PageFlags.HugePage) != 0)
            {
                // 2MB page - mark as COW if writable
                if (enableCow && (pdEntry & PageFlags.Writable) != 0)
                {
                    ulong pagePhys = pdEntry & PageFlags.AddressMask;
                    CopyOnWrite.MarkShared(pagePhys);

                    // Remove writable flag
                    pdEntry &= ~PageFlags.Writable;
                    parentPd[i] = pdEntry;
                }
                childPd[i] = pdEntry;
            }
            else
            {
                // Clone PT
                ulong parentPtPhys = pdEntry & PageFlags.AddressMask;
                ulong childPtPhys = ClonePt(parentPtPhys, enableCow, parentPd, i);
                if (childPtPhys == 0)
                {
                    PageAllocator.FreePage(childPdPhys);
                    return 0;
                }
                childPd[i] = childPtPhys | (pdEntry & ~PageFlags.AddressMask);
            }
        }

        return childPdPhys;
    }

    /// <summary>
    /// Clone a PT (for fork)
    /// </summary>
    private static ulong ClonePt(ulong parentPtPhys, bool enableCow, ulong* parentPd, int pdIdx)
    {
        ulong childPtPhys = PageAllocator.AllocatePage();
        if (childPtPhys == 0)
            return 0;

        ZeroPage(childPtPhys);

        ulong* parentPt = (ulong*)parentPtPhys;
        ulong* childPt = (ulong*)childPtPhys;

        for (int i = 0; i < 512; i++)
        {
            ulong ptEntry = parentPt[i];
            if ((ptEntry & PageFlags.Present) == 0)
                continue;

            // Mark as COW if writable and COW is enabled
            if (enableCow && (ptEntry & PageFlags.Writable) != 0)
            {
                ulong pagePhys = ptEntry & PageFlags.AddressMask;
                CopyOnWrite.IncrementRef(pagePhys);

                // Remove writable flag from both parent and child
                ptEntry &= ~PageFlags.Writable;
                parentPt[i] = ptEntry;
            }

            childPt[i] = ptEntry;
        }

        return childPtPhys;
    }

    /// <summary>
    /// Map a page in a user address space
    /// </summary>
    public static bool MapUserPage(ulong pml4Phys, ulong vaddr, ulong paddr, ulong flags)
    {
        if (pml4Phys == 0)
            return false;

        // Ensure user flag is set
        flags |= PageFlags.User;

        // Ensure address is in user space
        if (vaddr >= UserLayout.UserEnd)
            return false;

        return MapPage(pml4Phys, vaddr, paddr, flags);
    }

    /// <summary>
    /// Map a page in a specific address space
    /// </summary>
    private static bool MapPage(ulong pml4Phys, ulong vaddr, ulong paddr, ulong flags)
    {
        // Ensure page alignment
        if ((vaddr & (UserLayout.PageSize - 1)) != 0 ||
            (paddr & (UserLayout.PageSize - 1)) != 0)
            return false;

        // Calculate indices
        int pml4Idx = (int)((vaddr >> 39) & 0x1FF);
        int pdptIdx = (int)((vaddr >> 30) & 0x1FF);
        int pdIdx = (int)((vaddr >> 21) & 0x1FF);
        int ptIdx = (int)((vaddr >> 12) & 0x1FF);

        ulong* pml4 = (ulong*)pml4Phys;

        // Get or create PDPT
        ulong pdptPhys = GetOrCreateTable(pml4, pml4Idx, flags);
        if (pdptPhys == 0)
            return false;

        ulong* pdpt = (ulong*)pdptPhys;

        // Get or create PD
        ulong pdPhys = GetOrCreateTable(pdpt, pdptIdx, flags);
        if (pdPhys == 0)
            return false;

        ulong* pd = (ulong*)pdPhys;

        // Get or create PT
        ulong ptPhys = GetOrCreateTable(pd, pdIdx, flags);
        if (ptPhys == 0)
            return false;

        ulong* pt = (ulong*)ptPhys;

        // Set the page entry
        pt[ptIdx] = (paddr & PageFlags.AddressMask) | flags | PageFlags.Present;

        return true;
    }

    /// <summary>
    /// Get or create a page table at the given index.
    /// For user mappings (flags has User bit), if the existing entry doesn't have User bit,
    /// we ADD the User bit to enable traversal. The leaf pages control actual access.
    /// </summary>
    private static ulong GetOrCreateTable(ulong* parentTable, int index, ulong flags)
    {
        ulong entry = parentTable[index];

        if ((entry & PageFlags.Present) != 0)
        {
            // Check if this is a large page (HugePage) - if so, we can't use it as a table
            if ((entry & PageFlags.HugePage) != 0)
            {
                // This entry is a large page, not a pointer to a table.
                // Allocate a new table to allow 4KB page mappings.
                // TODO: Properly split the large page to preserve existing mappings
                ulong splitTable = PageAllocator.AllocatePage();
                if (splitTable == 0)
                    return 0;

                ZeroPage(splitTable);
                parentTable[index] = splitTable | PageFlags.Present | PageFlags.Writable | PageFlags.User;
                return splitTable;
            }

            // Check if we need User access but the entry doesn't have it
            bool needsUser = (flags & PageFlags.User) != 0;
            bool hasUser = (entry & PageFlags.User) != 0;

            if (needsUser && !hasUser)
            {
                // The existing table doesn't have User bit - ADD it.
                parentTable[index] = entry | PageFlags.User;
            }

            // Use existing table
            return entry & PageFlags.AddressMask;
        }

        // No existing entry - allocate new table
        ulong newTable = PageAllocator.AllocatePage();
        if (newTable == 0)
            return 0;

        ZeroPage(newTable);

        // Set parent entry with User, Writable, Present flags
        parentTable[index] = newTable | PageFlags.Present | PageFlags.Writable | PageFlags.User;

        return newTable;
    }

    /// <summary>
    /// Unmap a page from a user address space
    /// </summary>
    public static bool UnmapUserPage(ulong pml4Phys, ulong vaddr)
    {
        if (pml4Phys == 0)
            return false;

        // Calculate indices
        int pml4Idx = (int)((vaddr >> 39) & 0x1FF);
        int pdptIdx = (int)((vaddr >> 30) & 0x1FF);
        int pdIdx = (int)((vaddr >> 21) & 0x1FF);
        int ptIdx = (int)((vaddr >> 12) & 0x1FF);

        ulong* pml4 = (ulong*)pml4Phys;
        ulong pml4Entry = pml4[pml4Idx];
        if ((pml4Entry & PageFlags.Present) == 0)
            return false;

        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIdx];
        if ((pdptEntry & PageFlags.Present) == 0)
            return false;

        if ((pdptEntry & PageFlags.HugePage) != 0)
            return false; // Can't unmap part of huge page

        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIdx];
        if ((pdEntry & PageFlags.Present) == 0)
            return false;

        if ((pdEntry & PageFlags.HugePage) != 0)
            return false; // Can't unmap part of large page

        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        ulong ptEntry = pt[ptIdx];
        if ((ptEntry & PageFlags.Present) == 0)
            return false;

        // Clear the entry
        pt[ptIdx] = 0;

        // Invalidate TLB
        CPU.Invlpg(vaddr);

        return true;
    }

    /// <summary>
    /// Allocate and map user pages for heap/stack growth
    /// </summary>
    public static ulong AllocateUserPages(ulong pml4Phys, ulong vaddr, ulong numPages, ulong flags)
    {
        flags |= PageFlags.User;

        for (ulong i = 0; i < numPages; i++)
        {
            ulong pagePhys = PageAllocator.AllocatePage();
            if (pagePhys == 0)
            {
                // Rollback: unmap and free already allocated pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = vaddr + j * UserLayout.PageSize;
                    // Get and free the physical page
                    ulong physPage = GetPhysicalAddress(pml4Phys, rollbackAddr);
                    if (physPage != 0)
                    {
                        UnmapUserPage(pml4Phys, rollbackAddr);
                        PageAllocator.FreePage(physPage);
                    }
                }
                return 0;
            }

            // Zero the page
            ZeroPage(pagePhys);

            if (!MapUserPage(pml4Phys, vaddr + i * UserLayout.PageSize, pagePhys, flags))
            {
                PageAllocator.FreePage(pagePhys);
                // Rollback previous pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = vaddr + j * UserLayout.PageSize;
                    ulong physPage = GetPhysicalAddress(pml4Phys, rollbackAddr);
                    if (physPage != 0)
                    {
                        UnmapUserPage(pml4Phys, rollbackAddr);
                        PageAllocator.FreePage(physPage);
                    }
                }
                return 0;
            }
        }

        return vaddr;
    }

    /// <summary>
    /// Get the physical address for a virtual address
    /// </summary>
    public static ulong GetPhysicalAddress(ulong pml4Phys, ulong vaddr)
    {
        if (pml4Phys == 0)
            return 0;

        int pml4Idx = (int)((vaddr >> 39) & 0x1FF);
        int pdptIdx = (int)((vaddr >> 30) & 0x1FF);
        int pdIdx = (int)((vaddr >> 21) & 0x1FF);
        int ptIdx = (int)((vaddr >> 12) & 0x1FF);

        ulong* pml4 = (ulong*)pml4Phys;
        ulong pml4Entry = pml4[pml4Idx];
        if ((pml4Entry & PageFlags.Present) == 0)
            return 0;

        ulong* pdpt = (ulong*)(pml4Entry & PageFlags.AddressMask);
        ulong pdptEntry = pdpt[pdptIdx];
        if ((pdptEntry & PageFlags.Present) == 0)
            return 0;

        if ((pdptEntry & PageFlags.HugePage) != 0)
        {
            // 1GB page
            return (pdptEntry & PageFlags.AddressMask) | (vaddr & 0x3FFFFFFF);
        }

        ulong* pd = (ulong*)(pdptEntry & PageFlags.AddressMask);
        ulong pdEntry = pd[pdIdx];
        if ((pdEntry & PageFlags.Present) == 0)
            return 0;

        if ((pdEntry & PageFlags.HugePage) != 0)
        {
            // 2MB page
            return (pdEntry & PageFlags.AddressMask) | (vaddr & 0x1FFFFF);
        }

        ulong* pt = (ulong*)(pdEntry & PageFlags.AddressMask);
        ulong ptEntry = pt[ptIdx];
        if ((ptEntry & PageFlags.Present) == 0)
            return 0;

        return (ptEntry & PageFlags.AddressMask) | (vaddr & 0xFFF);
    }

    /// <summary>
    /// Zero a 4KB page
    /// </summary>
    private static void ZeroPage(ulong physAddr)
    {
        ulong* ptr = (ulong*)physAddr;
        for (int i = 0; i < 512; i++)
            ptr[i] = 0;
    }

    /// <summary>
    /// Switch to a process's address space
    /// </summary>
    public static void SwitchTo(ulong pml4Phys)
    {
        if (pml4Phys != 0)
        {
            CPU.WriteCr3(pml4Phys);
        }
    }
}
