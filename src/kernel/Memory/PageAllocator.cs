// ProtonOS kernel - Physical page allocator
// Bitmap-based allocator for 4KB pages using UEFI memory map.
// The bitmap is placed in a reserved memory region sized based on actual physical memory.
// Supports NUMA-aware allocation when NumaTopology is initialized.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Memory;

/// <summary>
/// Physical page allocator using a bitmap to track free/allocated 4KB pages.
/// The bitmap is dynamically sized based on total physical memory and placed
/// in a reserved memory region.
/// </summary>
public static unsafe class PageAllocator
{
    // 4KB page size
    public const ulong PageSize = 4096;
    public const int PageShift = 12;

    // Static buffer for memory map (8KB should be enough for most systems)
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private unsafe struct MemoryMapBuffer
    {
        public fixed byte Data[8192];
    }
    private static MemoryMapBuffer _memMapBuffer;

    // Bitmap location (set during Init)
    private static byte* _bitmap;
    private static ulong _bitmapSize;      // Size in bytes
    private static ulong _bitmapPages;     // Pages used by bitmap

    // Memory statistics
    private static ulong _totalPages;      // Total pages we're tracking (bitmap size)
    private static ulong _freePages;       // Currently free pages
    private static ulong _usableMemoryPages; // Actual usable RAM pages (not address space)
    private static ulong _topAddress;      // Highest address we track
    private static bool _initialized;

    // NUMA support
    private const int MaxNumaNodes = 16;
    private static byte* _pageNode;        // Node ID per page (0xFF = unknown)
    private static bool _numaInitialized;

    // Per-node page statistics
    [StructLayout(LayoutKind.Sequential)]
    public struct NumaNodePageStats
    {
        public ulong TotalPages;           // Total pages in this node
        public ulong FreePages;            // Free pages in this node
    }
    private static NumaNodePageStats* _nodeStats;

    /// <summary>
    /// Total number of pages managed by the allocator
    /// </summary>
    public static ulong TotalPages => _totalPages;

    /// <summary>
    /// Number of currently free pages
    /// </summary>
    public static ulong FreePages => _freePages;

    /// <summary>
    /// Total usable memory in bytes (actual RAM, not address space)
    /// </summary>
    public static ulong TotalMemory => _usableMemoryPages * PageSize;

    /// <summary>
    /// Free memory in bytes
    /// </summary>
    public static ulong FreeMemory => _freePages * PageSize;

    /// <summary>
    /// Whether the allocator has been initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Physical address of the bitmap
    /// </summary>
    public static ulong BitmapAddress => (ulong)_bitmap;

    /// <summary>
    /// Size of bitmap in bytes
    /// </summary>
    public static ulong BitmapSize => _bitmapSize;

    /// <summary>
    /// Whether NUMA-aware allocation is available
    /// </summary>
    public static bool NumaInitialized => _numaInitialized;

    /// <summary>
    /// Get per-node page statistics
    /// </summary>
    public static NumaNodePageStats* GetNodeStats(int node)
    {
        if (!_numaInitialized || node < 0 || node >= MaxNumaNodes)
            return null;
        return &_nodeStats[node];
    }

    /// <summary>
    /// Get the NUMA node for a physical address
    /// </summary>
    public static int GetPageNode(ulong physicalAddress)
    {
        if (!_numaInitialized)
            return 0;

        ulong pageNum = physicalAddress / PageSize;
        if (pageNum >= _totalPages)
            return 0;

        byte node = _pageNode[pageNum];
        return node == 0xFF ? 0 : node;
    }

    /// <summary>
    /// Initialize the page allocator from the UEFI memory map.
    /// Must be called before ExitBootServices.
    ///
    /// This function:
    /// 1. Gets UEFI memory map from BootInfo (captured before ExitBootServices)
    /// 2. Gets kernel extent from BootInfo
    /// 3. Scans memory map to find highest address (determines bitmap size)
    /// 4. Finds a free memory region for the bitmap
    /// 5. Marks all memory as used initially
    /// 6. Marks ConventionalMemory regions as free
    /// 7. Reserves: bitmap, kernel image, boot info, loaded files, null guard page
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Get BootInfo (contains memory map captured by bootloader)
        var bootInfo = BootInfoAccess.Get();
        if (bootInfo == null || !bootInfo->IsValid)
        {
            DebugConsole.WriteLine("[PageAlloc] BootInfo not available!");
            return false;
        }

        // Get memory map from BootInfo (this is the UEFI memory map captured before ExitBootServices)
        byte* memoryMap = (byte*)bootInfo->MemoryMapAddress;
        int entryCount = (int)bootInfo->MemoryMapEntries;
        ulong descriptorSize = bootInfo->MemoryMapEntrySize;

        if (memoryMap == null || entryCount == 0)
        {
            DebugConsole.WriteLine("[PageAlloc] No memory map in BootInfo!");
            return false;
        }

        // Get kernel location from BootInfo
        ulong kernelBase = bootInfo->KernelPhysicalBase;
        ulong kernelSize = bootInfo->KernelSize;

        // Find the highest physical address
        _topAddress = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UEFIBoot.GetDescriptor(memoryMap, descriptorSize, i);
            ulong regionEnd = desc->PhysicalStart + desc->NumberOfPages * PageSize;
            if (regionEnd > _topAddress)
                _topAddress = regionEnd;
        }

        if (_topAddress == 0)
        {
            DebugConsole.WriteLine("[PageAlloc] No memory found!");
            return false;
        }

        // Calculate bitmap size needed (each bit = one 4KB page)
        _totalPages = _topAddress / PageSize;
        _bitmapSize = (_totalPages + 7) / 8;  // Round up to bytes
        _bitmapPages = (_bitmapSize + PageSize - 1) / PageSize;  // Round up to pages
        ulong bitmapBytesNeeded = _bitmapPages * PageSize;

        // Find a suitable free region for the bitmap
        // We need to find ConventionalMemory with enough space
        byte* bitmapPtr = null;
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UEFIBoot.GetDescriptor(memoryMap, descriptorSize, i);
            if (desc->Type == EFIMemoryType.ConventionalMemory)
            {
                ulong regionSize = desc->NumberOfPages * PageSize;
                // Need at least 1MB extra after bitmap for other allocations
                if (regionSize >= bitmapBytesNeeded + (1 * 1024 * 1024))
                {
                    // Use the start of this region for bitmap
                    // Prefer regions above 1MB to avoid low memory
                    if (desc->PhysicalStart >= 0x100000)
                    {
                        bitmapPtr = (byte*)desc->PhysicalStart;
                        break;
                    }
                }
            }
        }

        if (bitmapPtr == null)
        {
            // Fallback: use any suitable region
            for (int i = 0; i < entryCount; i++)
            {
                var desc = UEFIBoot.GetDescriptor(memoryMap, descriptorSize, i);
                if (desc->Type == EFIMemoryType.ConventionalMemory)
                {
                    ulong regionSize = desc->NumberOfPages * PageSize;
                    if (regionSize >= bitmapBytesNeeded)
                    {
                        bitmapPtr = (byte*)desc->PhysicalStart;
                        break;
                    }
                }
            }
        }

        if (bitmapPtr == null)
        {
            DebugConsole.WriteLine("[PageAlloc] No suitable region for bitmap!");
            return false;
        }

        _bitmap = bitmapPtr;

        // Initialize bitmap: all pages start as USED (0)
        for (ulong i = 0; i < _bitmapSize; i++)
            _bitmap[i] = 0;

        _freePages = 0;

        // First pass: count total reclaimable memory
        ulong totalReclaimable = 0;
        ulong totalFirmwareReserved = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UEFIBoot.GetDescriptor(memoryMap, descriptorSize, i);
            ulong regionSize = desc->NumberOfPages * PageSize;

            bool isFree = desc->Type == EFIMemoryType.ConventionalMemory ||
                          desc->Type == EFIMemoryType.LoaderCode ||
                          desc->Type == EFIMemoryType.LoaderData ||
                          desc->Type == EFIMemoryType.BootServicesCode ||
                          desc->Type == EFIMemoryType.BootServicesData;

            if (isFree)
                totalReclaimable += regionSize;
            else if (desc->Type == EFIMemoryType.RuntimeServicesCode ||
                     desc->Type == EFIMemoryType.RuntimeServicesData ||
                     desc->Type == EFIMemoryType.ACPIMemoryNVS ||
                     desc->Type == EFIMemoryType.ACPIReclaimMemory)
                totalFirmwareReserved += regionSize;
        }

        DebugConsole.Write("[PageAlloc] Total reclaimable: ");
        DebugConsole.WriteDecimal((uint)(totalReclaimable / (1024 * 1024)));
        DebugConsole.Write(" MB, firmware reserved: ");
        DebugConsole.WriteDecimal((uint)(totalFirmwareReserved / (1024 * 1024)));
        DebugConsole.WriteLine(" MB");

        // Mark usable memory regions as FREE
        // After ExitBootServices, these types are safe to use:
        // - ConventionalMemory: Always free
        // - LoaderCode/LoaderData: Our bootloader (done executing)
        // - BootServicesCode/BootServicesData: UEFI boot services (gone after ExitBootServices)
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UEFIBoot.GetDescriptor(memoryMap, descriptorSize, i);

            bool isFree = desc->Type == EFIMemoryType.ConventionalMemory ||
                          desc->Type == EFIMemoryType.LoaderCode ||
                          desc->Type == EFIMemoryType.LoaderData ||
                          desc->Type == EFIMemoryType.BootServicesCode ||
                          desc->Type == EFIMemoryType.BootServicesData;

            if (isFree)
            {
                ulong startPage = desc->PhysicalStart / PageSize;
                ulong pageCount = desc->NumberOfPages;
                MarkRangeFree(startPage, pageCount);
            }
        }

        // Save total usable memory (before reserving kernel/bitmap/etc.)
        // This is the actual physical RAM, not the address space size
        _usableMemoryPages = _freePages;

        // Reserve critical regions and track how much
        ulong totalReserved = 0;

        ulong bitmapStartPage = (ulong)_bitmap / PageSize;
        ulong bitmapReserved = ReserveRangeCount(bitmapStartPage, _bitmapPages);
        totalReserved += bitmapReserved;

        ulong kernelReserved = 0;
        if (kernelSize > 0)
        {
            ulong kernelStartPage = kernelBase / PageSize;
            ulong kernelPages = (kernelSize + PageSize - 1) / PageSize;
            _kernelStart = kernelBase;
            _kernelEnd = kernelBase + kernelSize;
            kernelReserved = ReserveRangeCount(kernelStartPage, kernelPages);
            totalReserved += kernelReserved;
        }

        // Reserve BootInfo region (1MB at address 0x100000)
        ulong bootinfoReserved = ReserveRangeCount(0x100000 / PageSize, (1 * 1024 * 1024) / PageSize);
        totalReserved += bootinfoReserved;

        // Reserve loaded files region - calculate actual size needed
        ulong filesReserved = 0;
        if (bootInfo->LoadedFilesAddress != 0 && bootInfo->LoadedFilesCount > 0)
        {
            // Calculate total size of all loaded files
            ulong totalFileBytes = 0;
            var files = (LoadedFile*)bootInfo->LoadedFilesAddress;
            ulong lowestAddr = ulong.MaxValue;
            ulong highestEnd = 0;

            for (uint i = 0; i < bootInfo->LoadedFilesCount; i++)
            {
                totalFileBytes += files[i].Size;
                if (files[i].PhysicalAddress < lowestAddr)
                    lowestAddr = files[i].PhysicalAddress;
                ulong fileEnd = files[i].PhysicalAddress + files[i].Size;
                if (fileEnd > highestEnd)
                    highestEnd = fileEnd;
            }

            // Reserve from lowest file address to highest file end
            if (lowestAddr != ulong.MaxValue && highestEnd > lowestAddr)
            {
                DebugConsole.Write("[PageAlloc] Files at 0x");
                DebugConsole.WriteHex(lowestAddr);
                DebugConsole.Write(" - 0x");
                DebugConsole.WriteHex(highestEnd);
                DebugConsole.Write(" (");
                DebugConsole.WriteDecimal((uint)((highestEnd - lowestAddr) / 1024));
                DebugConsole.WriteLine(" KB)");

                ulong startPage = lowestAddr / PageSize;
                ulong endPage = (highestEnd + PageSize - 1) / PageSize;
                filesReserved = ReserveRangeCount(startPage, endPage - startPage);
                totalReserved += filesReserved;
            }
        }

        ulong nullReserved = ReserveRangeCount(0, 1);
        totalReserved += nullReserved;

        DebugConsole.Write("[PageAlloc] Reserved: kernel ");
        DebugConsole.WriteDecimal((uint)(kernelReserved * PageSize / 1024));
        DebugConsole.Write("KB + bootinfo ");
        DebugConsole.WriteDecimal((uint)(bootinfoReserved * PageSize / 1024));
        DebugConsole.Write("KB + files ");
        DebugConsole.WriteDecimal((uint)(filesReserved * PageSize / 1024));
        DebugConsole.Write("KB = ");
        DebugConsole.WriteDecimal((uint)(totalReserved * PageSize / 1024));
        DebugConsole.WriteLine("KB total");

        _initialized = true;

        DebugConsole.Write("[PageAlloc] Initialized with ");
        DebugConsole.WriteDecimal((uint)(_freePages * PageSize / (1024 * 1024)));
        DebugConsole.WriteLine(" MB free");

        return true;
    }

    /// <summary>
    /// Mark a range of pages as free
    /// </summary>
    private static void MarkRangeFree(ulong startPage, ulong pageCount)
    {
        for (ulong i = 0; i < pageCount; i++)
        {
            ulong pageNum = startPage + i;
            if (pageNum < _totalPages && !IsPageFree(pageNum))
            {
                SetPageFree(pageNum);
                _freePages++;
            }
        }
    }

    /// <summary>
    /// Reserve a range of pages (mark as used)
    /// </summary>
    private static void ReserveRange(ulong startPage, ulong pageCount, string name)
    {
        ReserveRangeCount(startPage, pageCount);
    }

    /// <summary>
    /// Reserve a range of pages and return how many were actually reserved
    /// </summary>
    private static ulong ReserveRangeCount(ulong startPage, ulong pageCount)
    {
        ulong reserved = 0;
        for (ulong i = 0; i < pageCount; i++)
        {
            ulong pageNum = startPage + i;
            if (pageNum < _totalPages && IsPageFree(pageNum))
            {
                SetPageUsed(pageNum);
                _freePages--;
                reserved++;
            }
        }
        return reserved;
    }

    // Debug: kernel region boundaries for detecting bad allocations
    private static ulong _kernelStart;
    private static ulong _kernelEnd;

    /// <summary>
    /// Allocate a single physical page.
    /// </summary>
    /// <returns>Physical address of the allocated page, or 0 on failure</returns>
    public static ulong AllocatePage()
    {
        if (!_initialized || _freePages == 0)
            return 0;

        // Linear search for a free page
        // TODO: Optimize with a hint/cache for next free page
        for (ulong pageNum = 1; pageNum < _totalPages; pageNum++)  // Start at 1 to skip null page
        {
            if (IsPageFree(pageNum))
            {
                ulong physAddr = pageNum * PageSize;
                // Debug: check if allocating from kernel region
                if (physAddr >= _kernelStart && physAddr < _kernelEnd)
                {
                    DebugConsole.Write("[PageAlloc] WARN: Allocating page 0x");
                    DebugConsole.WriteHex(physAddr);
                    DebugConsole.WriteLine(" from kernel region!");
                }
                SetPageUsed(pageNum);
                _freePages--;
                UpdateNodeStatsOnAlloc(pageNum);
                return physAddr;
            }
        }

        return 0;  // No free pages found
    }

    /// <summary>
    /// Allocate contiguous physical pages.
    /// </summary>
    /// <param name="count">Number of pages to allocate</param>
    /// <returns>Physical address of the first page, or 0 on failure</returns>
    public static ulong AllocatePages(ulong count)
    {
        if (!_initialized || count == 0 || _freePages < count)
            return 0;

        // Linear search for contiguous free pages
        ulong consecutive = 0;
        ulong startPage = 0;

        for (ulong pageNum = 1; pageNum < _totalPages; pageNum++)  // Start at 1 to skip null page
        {
            if (IsPageFree(pageNum))
            {
                if (consecutive == 0)
                    startPage = pageNum;
                consecutive++;

                if (consecutive == count)
                {
                    // Found enough contiguous pages, mark them as used
                    for (ulong p = 0; p < count; p++)
                    {
                        SetPageUsed(startPage + p);
                        UpdateNodeStatsOnAlloc(startPage + p);
                    }
                    _freePages -= count;
                    return startPage * PageSize;
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        return 0;  // Not enough contiguous pages
    }

    /// <summary>
    /// Allocate contiguous physical pages above a minimum address.
    /// Used for allocations that must be in GDB-accessible memory (>1MB).
    /// </summary>
    /// <param name="count">Number of pages to allocate</param>
    /// <param name="minAddress">Minimum physical address (allocation will be >= this)</param>
    /// <returns>Physical address of the first page, or 0 on failure</returns>
    public static ulong AllocatePagesAbove(ulong count, ulong minAddress)
    {
        if (!_initialized || count == 0 || _freePages < count)
            return 0;

        // Convert minimum address to minimum page number
        ulong minPage = (minAddress + PageSize - 1) / PageSize;  // Round up
        if (minPage == 0) minPage = 1;  // Skip null page

        // Linear search for contiguous free pages starting from minPage
        ulong consecutive = 0;
        ulong startPage = 0;

        for (ulong pageNum = minPage; pageNum < _totalPages; pageNum++)
        {
            if (IsPageFree(pageNum))
            {
                if (consecutive == 0)
                    startPage = pageNum;
                consecutive++;

                if (consecutive == count)
                {
                    // Found enough contiguous pages, mark them as used
                    for (ulong p = 0; p < count; p++)
                    {
                        SetPageUsed(startPage + p);
                        UpdateNodeStatsOnAlloc(startPage + p);
                    }
                    _freePages -= count;
                    return startPage * PageSize;
                }
            }
            else
            {
                consecutive = 0;
            }
        }

        return 0;  // Not enough contiguous pages above minAddress
    }

    /// <summary>
    /// Free a single physical page.
    /// </summary>
    /// <param name="physicalAddress">Physical address of the page to free</param>
    public static void FreePage(ulong physicalAddress)
    {
        if (!_initialized)
            return;

        ulong pageNum = physicalAddress / PageSize;
        if (pageNum == 0 || pageNum >= _totalPages)  // Don't free null page
            return;

        if (!IsPageFree(pageNum))  // Only free if currently used
        {
            SetPageFree(pageNum);
            _freePages++;
            UpdateNodeStatsOnFree(pageNum);
        }
    }

    /// <summary>
    /// Free contiguous physical pages.
    /// </summary>
    /// <param name="physicalAddress">Physical address of the first page</param>
    /// <param name="count">Number of pages to free</param>
    public static void FreePageRange(ulong physicalAddress, ulong count)
    {
        if (!_initialized || count == 0)
            return;

        ulong startPage = physicalAddress / PageSize;
        if (startPage == 0)  // Don't free null page
        {
            startPage = 1;
            if (count > 0) count--;
        }
        if (startPage >= _totalPages)
            return;
        if (startPage + count > _totalPages)
            count = _totalPages - startPage;

        for (ulong p = 0; p < count; p++)
        {
            ulong pageNum = startPage + p;
            if (!IsPageFree(pageNum))
            {
                SetPageFree(pageNum);
                _freePages++;
                UpdateNodeStatsOnFree(pageNum);
            }
        }
    }

    // Bitmap helpers - bit = 1 means free, bit = 0 means used
    private static bool IsPageFree(ulong pageNum)
    {
        ulong byteIndex = pageNum / 8;
        int bitIndex = (int)(pageNum % 8);
        return (_bitmap[byteIndex] & (1 << bitIndex)) != 0;
    }

    private static void SetPageFree(ulong pageNum)
    {
        ulong byteIndex = pageNum / 8;
        int bitIndex = (int)(pageNum % 8);
        _bitmap[byteIndex] |= (byte)(1 << bitIndex);
    }

    private static void SetPageUsed(ulong pageNum)
    {
        ulong byteIndex = pageNum / 8;
        int bitIndex = (int)(pageNum % 8);
        _bitmap[byteIndex] &= (byte)~(1 << bitIndex);
    }

    // ========================================================================
    // NUMA-aware allocation methods
    // ========================================================================

    /// <summary>
    /// Initialize NUMA information after NumaTopology is available.
    /// Must be called after NumaTopology.Init() and HeapAllocator.Init().
    /// </summary>
    public static bool InitNumaInfo()
    {
        if (!_initialized || _numaInitialized)
            return _numaInitialized;

        if (!NumaTopology.IsInitialized)
        {
            DebugConsole.WriteLine("[PageAlloc] NumaTopology not initialized, skipping NUMA init");
            return false;
        }

        DebugConsole.WriteLine("[PageAlloc] Initializing NUMA page info...");

        // Allocate per-page node array
        _pageNode = (byte*)HeapAllocator.AllocZeroed(_totalPages);
        if (_pageNode == null)
        {
            DebugConsole.WriteLine("[PageAlloc] Failed to allocate page node array!");
            return false;
        }

        // Allocate per-node stats array
        _nodeStats = (NumaNodePageStats*)HeapAllocator.AllocZeroed((ulong)(sizeof(NumaNodePageStats) * MaxNumaNodes));
        if (_nodeStats == null)
        {
            DebugConsole.WriteLine("[PageAlloc] Failed to allocate node stats!");
            return false;
        }

        // Initialize all pages to unknown node
        for (ulong i = 0; i < _totalPages; i++)
            _pageNode[i] = 0xFF;

        // Classify pages by NUMA node using memory ranges from NumaTopology
        int rangeCount = NumaTopology.MemoryRangeCount;
        for (int r = 0; r < rangeCount; r++)
        {
            var range = NumaTopology.GetMemoryRange(r);
            if (range == null || !range->IsEnabled)
                continue;

            uint nodeId = range->NodeId;
            if (nodeId >= MaxNumaNodes)
                continue;

            ulong startPage = range->BaseAddress / PageSize;
            ulong endPage = (range->BaseAddress + range->Length + PageSize - 1) / PageSize;

            if (endPage > _totalPages)
                endPage = _totalPages;

            for (ulong p = startPage; p < endPage; p++)
            {
                _pageNode[p] = (byte)nodeId;
                _nodeStats[nodeId].TotalPages++;

                // If page is free, count it
                if (IsPageFree(p))
                    _nodeStats[nodeId].FreePages++;
            }
        }

        _numaInitialized = true;

        // Log per-node statistics
        DebugConsole.WriteLine("[PageAlloc] NUMA page distribution:");
        int nodeCount = NumaTopology.NodeCount;
        for (int i = 0; i < nodeCount && i < MaxNumaNodes; i++)
        {
            if (_nodeStats[i].TotalPages > 0)
            {
                DebugConsole.Write("[PageAlloc]   Node ");
                DebugConsole.WriteDecimal(i);
                DebugConsole.Write(": ");
                DebugConsole.WriteDecimal((int)(_nodeStats[i].TotalPages * PageSize / (1024 * 1024)));
                DebugConsole.Write(" MB total, ");
                DebugConsole.WriteDecimal((int)(_nodeStats[i].FreePages * PageSize / (1024 * 1024)));
                DebugConsole.WriteLine(" MB free");
            }
        }

        return true;
    }

    /// <summary>
    /// Allocate a page from a specific NUMA node.
    /// Falls back to any node if the requested node has no free pages.
    /// </summary>
    /// <param name="node">Preferred NUMA node</param>
    /// <returns>Physical address of the allocated page, or 0 on failure</returns>
    public static ulong AllocatePageFromNode(uint node)
    {
        if (!_initialized || _freePages == 0)
            return 0;

        // If NUMA not initialized, fall back to regular allocation
        if (!_numaInitialized)
            return AllocatePage();

        // Try to allocate from requested node first
        if (node < MaxNumaNodes && _nodeStats[node].FreePages > 0)
        {
            for (ulong pageNum = 1; pageNum < _totalPages; pageNum++)
            {
                if (_pageNode[pageNum] == node && IsPageFree(pageNum))
                {
                    SetPageUsed(pageNum);
                    _freePages--;
                    _nodeStats[node].FreePages--;
                    return pageNum * PageSize;
                }
            }
        }

        // Fall back to any node
        return AllocatePage();
    }

    /// <summary>
    /// Allocate a page from the current CPU's local NUMA node.
    /// Falls back to any node if the local node has no free pages.
    /// </summary>
    /// <returns>Physical address of the allocated page, or 0 on failure</returns>
    public static ulong AllocatePageLocal()
    {
        if (!_numaInitialized)
            return AllocatePage();

        // Get current CPU's NUMA node (efficient via per-CPU state)
        uint node = Threading.PerCpu.IsInitialized ? Threading.PerCpu.NumaNode : 0;

        return AllocatePageFromNode(node);
    }

    /// <summary>
    /// Allocate contiguous pages from a specific NUMA node.
    /// Falls back to any node if the requested node doesn't have enough contiguous pages.
    /// </summary>
    public static ulong AllocatePagesFromNode(ulong count, uint node)
    {
        if (!_initialized || count == 0 || _freePages < count)
            return 0;

        if (!_numaInitialized)
            return AllocatePages(count);

        // Try to find contiguous pages in the requested node
        if (node < MaxNumaNodes && _nodeStats[node].FreePages >= count)
        {
            ulong consecutive = 0;
            ulong startPage = 0;

            for (ulong pageNum = 1; pageNum < _totalPages; pageNum++)
            {
                if (_pageNode[pageNum] == node && IsPageFree(pageNum))
                {
                    if (consecutive == 0)
                        startPage = pageNum;
                    consecutive++;

                    if (consecutive == count)
                    {
                        // Found enough contiguous pages
                        for (ulong p = 0; p < count; p++)
                        {
                            SetPageUsed(startPage + p);
                            _nodeStats[node].FreePages--;
                        }
                        _freePages -= count;
                        return startPage * PageSize;
                    }
                }
                else
                {
                    consecutive = 0;
                }
            }
        }

        // Fall back to any node
        return AllocatePages(count);
    }

    /// <summary>
    /// Update per-node free count when freeing a page.
    /// Called internally after marking a page as free.
    /// </summary>
    private static void UpdateNodeStatsOnFree(ulong pageNum)
    {
        if (!_numaInitialized)
            return;

        byte node = _pageNode[pageNum];
        if (node < MaxNumaNodes)
            _nodeStats[node].FreePages++;
    }

    /// <summary>
    /// Update per-node free count when allocating a page.
    /// Called internally after marking a page as used.
    /// </summary>
    private static void UpdateNodeStatsOnAlloc(ulong pageNum)
    {
        if (!_numaInitialized)
            return;

        byte node = _pageNode[pageNum];
        if (node < MaxNumaNodes && _nodeStats[node].FreePages > 0)
            _nodeStats[node].FreePages--;
    }
}
