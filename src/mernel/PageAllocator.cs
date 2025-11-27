// netos mernel - Physical page allocator
// Bitmap-based allocator for 4KB pages using UEFI memory map.
// The bitmap is placed in a reserved memory region sized based on actual physical memory.

using System.Runtime.InteropServices;

namespace Mernel;

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

    // Bitmap location (set during Init)
    private static byte* _bitmap;
    private static ulong _bitmapSize;      // Size in bytes
    private static ulong _bitmapPages;     // Pages used by bitmap

    // Memory statistics
    private static ulong _totalPages;      // Total pages we're tracking
    private static ulong _freePages;       // Currently free pages
    private static ulong _topAddress;      // Highest address we track
    private static bool _initialized;

    /// <summary>
    /// Total number of pages managed by the allocator
    /// </summary>
    public static ulong TotalPages => _totalPages;

    /// <summary>
    /// Number of currently free pages
    /// </summary>
    public static ulong FreePages => _freePages;

    /// <summary>
    /// Total memory being tracked in bytes
    /// </summary>
    public static ulong TotalMemory => _totalPages * PageSize;

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
    /// Initialize the page allocator from the UEFI memory map.
    /// Must be called before ExitBootServices.
    ///
    /// This function:
    /// 1. Scans memory map to find highest address (determines bitmap size)
    /// 2. Allocates bitmap from UEFI boot services
    /// 3. Marks all memory as used initially
    /// 4. Marks ConventionalMemory regions as free
    /// 5. Reserves: bitmap, kernel image, runtime services memory
    /// </summary>
    /// <param name="memoryMap">Pointer to UEFI memory map buffer</param>
    /// <param name="descriptorSize">Size of each memory descriptor</param>
    /// <param name="entryCount">Number of entries in the map</param>
    /// <param name="kernelBase">Base address of kernel image</param>
    /// <param name="kernelSize">Size of kernel image in bytes</param>
    public static bool Init(
        byte* memoryMap,
        ulong descriptorSize,
        int entryCount,
        ulong kernelBase,
        ulong kernelSize)
    {
        if (_initialized)
            return false;

        var bs = UefiBoot.BootServices;
        if (bs == null)
        {
            DebugConsole.WriteLine("[PageAlloc] Boot services not available!");
            return false;
        }

        // === Pass 1: Find the highest physical address ===
        _topAddress = 0;
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UefiBoot.GetDescriptor(memoryMap, descriptorSize, i);
            ulong regionEnd = desc->PhysicalStart + desc->NumberOfPages * PageSize;
            if (regionEnd > _topAddress)
                _topAddress = regionEnd;
        }

        if (_topAddress == 0)
        {
            DebugConsole.WriteLine("[PageAlloc] No memory found!");
            return false;
        }

        // Calculate bitmap size needed
        // Each bit represents one 4KB page
        _totalPages = _topAddress / PageSize;
        _bitmapSize = (_totalPages + 7) / 8;  // Round up to bytes
        _bitmapPages = (_bitmapSize + PageSize - 1) / PageSize;  // Round up to pages

        DebugConsole.Write("[PageAlloc] Top address: 0x");
        DebugConsole.WriteHex(_topAddress);
        DebugConsole.Write(", tracking ");
        DebugConsole.WriteHex(_totalPages);
        DebugConsole.WriteLine(" pages");

        DebugConsole.Write("[PageAlloc] Bitmap needs ");
        DebugConsole.WriteHex(_bitmapSize);
        DebugConsole.Write(" bytes (");
        DebugConsole.WriteHex(_bitmapPages);
        DebugConsole.WriteLine(" pages)");

        // === Allocate bitmap from UEFI ===
        void* bitmapPtr = null;
        var status = bs->AllocatePool(
            EfiMemoryType.LoaderData,  // Will become available after ExitBootServices
            _bitmapPages * PageSize,   // Allocate full pages
            &bitmapPtr);

        if (status != EfiStatus.Success || bitmapPtr == null)
        {
            DebugConsole.Write("[PageAlloc] Failed to allocate bitmap: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return false;
        }

        _bitmap = (byte*)bitmapPtr;
        DebugConsole.Write("[PageAlloc] Bitmap at 0x");
        DebugConsole.WriteHex((ulong)_bitmap);
        DebugConsole.WriteLine();

        // === Initialize bitmap: all pages start as USED (0) ===
        for (ulong i = 0; i < _bitmapSize; i++)
            _bitmap[i] = 0;

        _freePages = 0;

        // === Pass 2: Mark usable memory regions as FREE ===
        for (int i = 0; i < entryCount; i++)
        {
            var desc = UefiBoot.GetDescriptor(memoryMap, descriptorSize, i);

            // Only ConventionalMemory is safe to use
            // After ExitBootServices, BootServicesCode/Data also become available,
            // but we're setting this up before that call
            if (desc->Type == EfiMemoryType.ConventionalMemory)
            {
                ulong startPage = desc->PhysicalStart / PageSize;
                ulong pageCount = desc->NumberOfPages;

                MarkRangeFree(startPage, pageCount);
            }
        }

        DebugConsole.Write("[PageAlloc] Free pages after marking conventional: ");
        DebugConsole.WriteHex(_freePages);
        DebugConsole.WriteLine();

        // === Reserve critical regions ===

        // Reserve the bitmap itself
        ulong bitmapStartPage = (ulong)_bitmap / PageSize;
        ReserveRange(bitmapStartPage, _bitmapPages, "bitmap");

        // Reserve the kernel image
        if (kernelSize > 0)
        {
            ulong kernelStartPage = kernelBase / PageSize;
            ulong kernelPages = (kernelSize + PageSize - 1) / PageSize;
            ReserveRange(kernelStartPage, kernelPages, "kernel");
        }

        // Reserve page 0 (null pointer guard)
        ReserveRange(0, 1, "null guard");

        // Note: Runtime services memory is already marked as non-conventional,
        // so it won't be in our free pool

        _initialized = true;

        DebugConsole.Write("[PageAlloc] Initialized: ");
        DebugConsole.WriteHex(_freePages);
        DebugConsole.Write(" free pages (");
        DebugConsole.WriteHex(_freePages * PageSize / (1024 * 1024));
        DebugConsole.WriteLine(" MB)");

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

        if (reserved > 0)
        {
            DebugConsole.Write("[PageAlloc] Reserved ");
            DebugConsole.WriteHex(reserved);
            DebugConsole.Write(" pages for ");
            DebugConsole.WriteLine(name);
        }
    }

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
                SetPageUsed(pageNum);
                _freePages--;
                return pageNum * PageSize;
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
}
