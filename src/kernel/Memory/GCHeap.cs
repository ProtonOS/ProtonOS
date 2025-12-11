// ProtonOS kernel - GC Heap Allocator
// Manages the managed object heap with proper object headers for garbage collection.
//
// Block Layout (16 bytes header + object data):
//  -16: Block Size Header (8 bytes)
//        Bits 0-31:  Block size (total allocation including headers)
//        Bits 32-63: Reserved
//   -8: Object Header (8 bytes)
//        Bit 0:      Mark bit (1 = reachable during GC)
//        Bit 1:      Pinned (cannot be relocated)
//        Bit 2:      Free block flag (1 = in free list)
//        Bit 3:      Reserved
//        Bits 4-5:   Generation (0=Gen0, 1=Gen1, 2=Gen2, 3=reserved)
//        Bits 6-7:   Reserved
//        Bits 8-31:  Sync Block Index (24 bits = 16M entries)
//        Bits 32-63: Identity Hash Code
//    0: MethodTable* (8 bytes) - object reference points here
//   +8: Fields...
//
// The GC heap uses bump allocation within contiguous regions obtained from PageAllocator.
// When a region fills, we either trigger GC or allocate a new region.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.Memory;

/// <summary>
/// Object header layout (16 bytes total, stored before the MethodTable pointer):
///
///   Offset -16: Block size header (8 bytes)
///     Bits 0-31:  Block size (total allocation size including both headers)
///     Bits 32-63: Reserved (unused)
///
///   Offset -8: Object header (8 bytes)
///     Bit 0:      Mark bit (1 = reachable during GC)
///     Bit 1:      Pinned (cannot be relocated by GC)
///     Bit 2:      Free block flag (1 = this is a free block in the free list)
///     Bit 3:      Reserved
///     Bits 4-5:   Generation (0=Gen0, 1=Gen1, 2=Gen2, 3=reserved)
///     Bits 6-7:   Reserved
///     Bits 8-31:  Sync Block Index (24 bits = 16M entries)
///     Bits 32-63: Identity Hash Code
///
///   Offset 0: MethodTable* (object reference points here)
///   Offset 8+: Object fields...
/// </summary>
public static class ObjectHeaderFlags
{
    // === Object Header (at offset -8 from object reference) ===
    //
    // Layout (64 bits):
    //   Bits 0-7:   Flags (Mark, Pinned, FreeBlock, Reserved, Gen0, Gen1, Reserved, Reserved)
    //   Bits 8-31:  Sync Block Index (24 bits = 16M entries)
    //   Bits 32-63: Identity Hash Code (32 bits)

    /// <summary>Mark bit - set during GC mark phase, cleared during sweep.</summary>
    public const ulong Mark = 1UL << 0;

    /// <summary>Pinned - object cannot be relocated by GC.</summary>
    public const ulong Pinned = 1UL << 1;

    /// <summary>Free block flag - block is in the free list, not a live object.</summary>
    public const ulong FreeBlock = 1UL << 2;

    /// <summary>HasForwardingPointer - object has been moved, forwarding address stored.</summary>
    public const ulong HasForwardingPointer = 1UL << 3;

    /// <summary>Generation bit 0 - low bit of 2-bit generation field (bits 4-5).</summary>
    public const ulong Generation0 = 1UL << 4;

    /// <summary>Generation bit 1 - high bit of 2-bit generation field (bits 4-5).</summary>
    public const ulong Generation1 = 1UL << 5;

    /// <summary>Generation mask (bits 4-5) - covers Gen0=0, Gen1=1, Gen2=2, (3=reserved).</summary>
    public const ulong GenerationMask = 0x30UL;

    /// <summary>Shift to get/set generation value.</summary>
    public const int GenerationShift = 4;

    /// <summary>LOH flag - object is allocated on Large Object Heap (bit 6).</summary>
    public const ulong LOHFlag = 1UL << 6;

    /// <summary>ForwardingInPlace - forwarding pointer stored at MethodTable* slot (bit 7).</summary>
    public const ulong ForwardingInPlace = 1UL << 7;

    /// <summary>Sync block index mask (bits 8-31, 24 bits = 16M entries).</summary>
    public const ulong SyncBlockMask = 0xFFFFFF00UL;

    /// <summary>Shift to get/set sync block index.</summary>
    public const int SyncBlockShift = 8;

    /// <summary>Hash code is stored in upper 32 bits.</summary>
    public const int HashCodeShift = 32;

    // === Block Size Header (at offset -16 from object reference) ===

    /// <summary>Block size mask (lower 32 bits of block size header).</summary>
    public const ulong BlockSizeMask = 0xFFFFFFFFUL;
}

/// <summary>
/// GC Heap allocator using bump allocation from contiguous page regions.
/// </summary>
public static unsafe class GCHeap
{
    // Initial region size: 1MB (256 pages)
    private const ulong InitialRegionPages = 256;
    private const ulong InitialRegionSize = InitialRegionPages * PageAllocator.PageSize;

    // LOH threshold: 85KB (matching .NET)
    public const uint LOHThreshold = 85000;

    // Minimum object data size (MT pointer slot, which is reused for free list next pointer)
    // Actual minimum allocation = ObjectHeaderSize (16) + MinObjectSize (8) = 24 bytes
    private const uint MinObjectSize = 8;

    // Total header size (block size header + object header, stored before the MethodTable pointer)
    // Layout: [-16: block size (8)] [-8: obj header (8)] [0: MT*] [8+: fields]
    public const uint ObjectHeaderSize = 16;

    // Current allocation region
    private static byte* _regionStart;
    private static byte* _regionEnd;
    private static byte* _allocPtr;

    // Statistics
    private static ulong _totalAllocated;
    private static ulong _objectCount;

    // Track all regions for sweep phase (simple linked list using first 8 bytes of each region)
    private static byte* _firstRegion;

    // Free list for reclaimed objects (SOH)
    // Free block layout:
    //   -8: Header [Flags (32 bits) | Size (32 bits)] - size stored where hash code was
    //    0: Next pointer (8 bytes) - stored where MethodTable* was
    // Minimum free block size = ObjectHeaderSize + 8 = 16 bytes
    private static void* _freeList;
    private static ulong _freeListBytes;
    private static ulong _freeListCount;

    // LOH (Large Object Heap) region tracking
    private static byte* _lohFirstRegion;
    private static byte* _lohRegionStart;
    private static byte* _lohRegionEnd;
    private static byte* _lohAllocPtr;

    // LOH statistics
    private static ulong _lohTotalAllocated;
    private static ulong _lohObjectCount;

    // LOH free list
    private static void* _lohFreeList;
    private static ulong _lohFreeListBytes;
    private static ulong _lohFreeListCount;

    private static bool _initialized;

    /// <summary>
    /// Initialize the GC heap by allocating the first region.
    /// Must be called after PageAllocator.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Allocate initial region from page allocator
        ulong physAddr = PageAllocator.AllocatePages(InitialRegionPages);
        if (physAddr == 0)
        {
            DebugConsole.WriteLine("[GCHeap] Failed to allocate initial region!");
            return false;
        }

        // Convert to virtual address (using physmap)
        _regionStart = (byte*)VirtualMemory.PhysToVirt(physAddr);
        _regionEnd = _regionStart + InitialRegionSize;
        _allocPtr = _regionStart;

        // Zero the region
        for (ulong i = 0; i < InitialRegionSize; i++)
            _regionStart[i] = 0;

        // Track this region
        _firstRegion = _regionStart;
        *(byte**)_regionStart = null; // No next region yet

        // Reserve space for region header (next pointer)
        _allocPtr += 8;

        _initialized = true;

        DebugConsole.WriteLine(string.Format("[GCHeap] Initialized: 0x{0} - 0x{1} ({2} KB)",
            ((ulong)_regionStart).ToString("X", null), ((ulong)_regionEnd).ToString("X", null),
            (uint)(InitialRegionSize / 1024)));

        return true;
    }

    /// <summary>
    /// Allocate a new object with proper header.
    /// Objects >= LOHThreshold (85KB) are allocated on the Large Object Heap.
    /// </summary>
    /// <param name="size">Size requested (BaseSize from MethodTable, includes MT pointer).</param>
    /// <returns>Pointer to the MethodTable* slot (object reference), or null on failure.</returns>
    public static void* Alloc(uint size)
    {
        if (!_initialized)
        {
            DebugConsole.WriteLine("[GCHeap] ERROR: Not initialized!");
            return null;
        }

        // Route large objects to LOH
        if (size >= LOHThreshold)
            return AllocLOH(size);

        // Enforce minimum size
        if (size < MinObjectSize)
            size = MinObjectSize;

        // Align to 8 bytes
        size = (size + 7) & ~7u;

        // Total allocation includes the object header
        uint totalSize = size + ObjectHeaderSize;

        // Try free list first
        void* fromFreeList = AllocFromFreeList(totalSize);
        if (fromFreeList != null)
        {
            if (_traceAllocs)
            {
                DebugConsole.Write("[GCHeap] Alloc from free list: ");
                DebugConsole.WriteHex((ulong)fromFreeList);
                DebugConsole.WriteLine();
            }
            _totalAllocated += totalSize;
            return fromFreeList;
        }

        // Check if we have space in current region
        if (_allocPtr + totalSize > _regionEnd)
        {
            // Need to allocate a new region (or trigger GC in future)
            if (!AllocateNewRegion())
            {
                DebugConsole.WriteLine("[GCHeap] Out of memory!");
                return null;
            }
        }

        // Bump allocate
        byte* blockStart = _allocPtr;
        _allocPtr += totalSize;

        if (_traceAllocs)
        {
            DebugConsole.Write("[GCHeap] Bump alloc at ");
            DebugConsole.WriteHex((ulong)(blockStart + ObjectHeaderSize));
            DebugConsole.Write(" AllocPtr now ");
            DebugConsole.WriteHex((ulong)_allocPtr);
            DebugConsole.WriteLine();
        }

        // Initialize block size header (at offset 0 from block start)
        *(ulong*)blockStart = totalSize; // Block size in lower 32 bits, free flag = 0

        // Initialize object header (at offset 8 from block start)
        *(ulong*)(blockStart + 8) = 0; // mark=0, hash=0, sync=0

        // Object reference points past both headers to the MethodTable* slot
        void* objPtr = blockStart + ObjectHeaderSize;

        // Update statistics
        _totalAllocated += totalSize;
        _objectCount++;

        return objPtr;
    }

    /// <summary>
    /// Allocate a new object and zero it.
    /// </summary>
    public static void* AllocZeroed(uint size)
    {
        void* ptr = Alloc(size);
        if (ptr != null)
        {
            // Zero the object data (header is already zeroed, zero from MT* onward)
            byte* dataPtr = (byte*)ptr;
            for (uint i = 0; i < size; i++)
                dataPtr[i] = 0;
        }
        return ptr;
    }

    /// <summary>
    /// Allocate a new region when current one is full.
    /// </summary>
    private static bool AllocateNewRegion()
    {
        ulong physAddr = PageAllocator.AllocatePages(InitialRegionPages);
        if (physAddr == 0)
            return false;

        byte* newRegion = (byte*)VirtualMemory.PhysToVirt(physAddr);

        // Zero the new region
        for (ulong i = 0; i < InitialRegionSize; i++)
            newRegion[i] = 0;

        // Link into region list
        *(byte**)newRegion = _firstRegion;
        _firstRegion = newRegion;

        // Set up for allocation
        _regionStart = newRegion;
        _regionEnd = newRegion + InitialRegionSize;
        _allocPtr = newRegion + 8; // Skip region header

        DebugConsole.Write("[GCHeap] New region: ");
        DebugConsole.WriteHex((ulong)newRegion);
        DebugConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Get the object header (flags/sync/hash) for an object reference.
    /// </summary>
    /// <param name="obj">Pointer to MethodTable* slot.</param>
    /// <returns>Pointer to 8-byte object header at offset -8.</returns>
    public static ulong* GetHeader(void* obj)
    {
        return (ulong*)((byte*)obj - 8);
    }

    /// <summary>
    /// Get the block size header for an object reference.
    /// </summary>
    /// <param name="obj">Pointer to MethodTable* slot.</param>
    /// <returns>Pointer to 8-byte block size header at offset -16.</returns>
    public static ulong* GetBlockSizeHeader(void* obj)
    {
        return (ulong*)((byte*)obj - 16);
    }

    /// <summary>
    /// Get the block size for an object (total allocation including headers).
    /// </summary>
    public static uint GetBlockSize(void* obj)
    {
        ulong* blockHeader = GetBlockSizeHeader(obj);
        return (uint)(*blockHeader & ObjectHeaderFlags.BlockSizeMask);
    }

    /// <summary>
    /// Set the block size for an object.
    /// </summary>
    public static void SetBlockSize(void* obj, uint size)
    {
        ulong* blockHeader = GetBlockSizeHeader(obj);
        *blockHeader = (*blockHeader & ~ObjectHeaderFlags.BlockSizeMask) | size;
    }

    /// <summary>
    /// Check if a block is marked as free (in the free list).
    /// Uses bit 2 of the object header at offset -8.
    /// </summary>
    public static bool IsFreeBlock(void* obj)
    {
        ulong* header = GetHeader(obj);
        return (*header & ObjectHeaderFlags.FreeBlock) != 0;
    }

    /// <summary>
    /// Set or clear the free block flag.
    /// Uses bit 2 of the object header at offset -8.
    /// </summary>
    public static void SetFreeBlockFlag(void* obj, bool isFree)
    {
        ulong* header = GetHeader(obj);
        if (isFree)
            *header |= ObjectHeaderFlags.FreeBlock;
        else
            *header &= ~ObjectHeaderFlags.FreeBlock;
    }

    /// <summary>
    /// Check if an object is marked (reachable).
    /// </summary>
    public static bool IsMarked(void* obj)
    {
        ulong* header = GetHeader(obj);
        return (*header & ObjectHeaderFlags.Mark) != 0;
    }

    /// <summary>
    /// Set the mark bit on an object.
    /// </summary>
    public static void SetMark(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header |= ObjectHeaderFlags.Mark;
    }

    /// <summary>
    /// Clear the mark bit on an object.
    /// </summary>
    public static void ClearMark(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header &= ~ObjectHeaderFlags.Mark;
    }

    /// <summary>
    /// Get or compute the identity hash code for an object.
    /// </summary>
    public static int GetHashCode(void* obj)
    {
        ulong* header = GetHeader(obj);
        int hash = (int)(*header >> ObjectHeaderFlags.HashCodeShift);

        if (hash == 0)
        {
            // Compute hash from address (simple but unique per object)
            // Use the object address shifted to fit in 31 bits (positive int)
            hash = (int)(((ulong)obj >> 3) & 0x7FFFFFFF);
            if (hash == 0) hash = 1; // Never return 0

            // Store it
            *header |= ((ulong)hash << ObjectHeaderFlags.HashCodeShift);
        }

        return hash;
    }

    /// <summary>
    /// Check if an address is within the GC heap.
    /// </summary>
    public static bool IsInHeap(void* ptr)
    {
        byte* p = (byte*)ptr;
        byte* region = _firstRegion;

        while (region != null)
        {
            if (p >= region && p < region + InitialRegionSize)
                return true;
            region = *(byte**)region; // Follow linked list
        }

        return false;
    }

    /// <summary>
    /// Get GC heap statistics.
    /// </summary>
    public static void GetStats(out ulong totalAllocated, out ulong objectCount, out ulong freeSpace)
    {
        totalAllocated = _totalAllocated;
        objectCount = _objectCount;
        freeSpace = (ulong)(_regionEnd - _allocPtr);
    }

    /// <summary>
    /// Dump GC heap statistics to debug console.
    /// </summary>
    public static void DumpStats()
    {
        DebugConsole.Write("[GCHeap] Allocated: ");
        DebugConsole.WriteDecimal((uint)(_totalAllocated / 1024));
        DebugConsole.Write(" KB, Objects: ");
        DebugConsole.WriteDecimal((uint)_objectCount);
        DebugConsole.Write(", Free in region: ");
        DebugConsole.WriteDecimal((uint)((_regionEnd - _allocPtr) / 1024));
        DebugConsole.WriteLine(" KB");
    }

    /// <summary>
    /// Check if the GC heap has been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get the start of the first region (for heap walking).
    /// </summary>
    public static byte* FirstRegion => _firstRegion;

    /// <summary>
    /// Get the current allocation pointer (for heap walking).
    /// </summary>
    public static byte* AllocPtr => _allocPtr;

    /// <summary>
    /// Get the region size constant (for heap walking).
    /// </summary>
    public static ulong RegionSize => InitialRegionSize;

    /// <summary>
    /// Add a block to the free list.
    /// Called by GC sweep phase when an unmarked object is found.
    /// </summary>
    /// <param name="obj">Pointer to the object (MethodTable* slot).</param>
    public static void AddToFreeList(void* obj)
    {
        // Get block size from block size header (at offset -16)
        ulong* blockHeader = GetBlockSizeHeader(obj);
        uint blockSize = (uint)(*blockHeader & ObjectHeaderFlags.BlockSizeMask);

        // Set free flag in object header (bit 2 at offset -8)
        ulong* objHeader = GetHeader(obj);
        *objHeader = ObjectHeaderFlags.FreeBlock; // Clear other bits, set free flag

        // Store next pointer where MethodTable* was
        *(void**)obj = _freeList;
        _freeList = obj;

        _freeListBytes += blockSize;
        _freeListCount++;
    }

    // Debug flag for tracing allocations
    private static bool _traceAllocs = false;
    public static void SetTraceAllocs(bool trace) { _traceAllocs = trace; }

    /// <summary>
    /// Try to allocate from the free list.
    /// Uses first-fit strategy for simplicity.
    /// </summary>
    /// <param name="totalNeeded">Total size needed including headers (already aligned).</param>
    /// <returns>Pointer to allocated object, or null if no suitable block found.</returns>
    public static void* AllocFromFreeList(uint totalNeeded)
    {
        if (_freeList == null)
            return null;

        void* prev = null;
        void* current = _freeList;

        while (current != null)
        {
            ulong* blockSizeHeader = GetBlockSizeHeader(current);
            uint blockSize = (uint)(*blockSizeHeader & ObjectHeaderFlags.BlockSizeMask);
            void* next = *(void**)current;

            if (_traceAllocs)
            {
                DebugConsole.Write("[GCHeap] FreeList check: need=");
                DebugConsole.WriteDecimal(totalNeeded);
                DebugConsole.Write(" block=");
                DebugConsole.WriteDecimal(blockSize);
                DebugConsole.WriteLine();
            }

            if (blockSize >= totalNeeded)
            {
                // Found a suitable block

                // Check if we should split the block
                // Minimum useful remainder = 16 (headers) + 8 (MT/next ptr) = 24 bytes
                uint remainder = blockSize - totalNeeded;
                if (remainder >= 24) // Worth splitting
                {
                    // Create a new free block from the remainder
                    // The remainder starts at current block start + totalNeeded
                    byte* currentBlockStart = (byte*)current - ObjectHeaderSize;
                    byte* remainderBlockStart = currentBlockStart + totalNeeded;
                    void* remainderObj = remainderBlockStart + ObjectHeaderSize;

                    // Set up remainder's block size header (just the size, no flags here)
                    ulong* remainderBlockSizeHeader = (ulong*)remainderBlockStart;
                    *remainderBlockSizeHeader = remainder;

                    // Set up remainder's object header with free flag (bit 2)
                    ulong* remainderObjHeader = (ulong*)(remainderBlockStart + 8);
                    *remainderObjHeader = ObjectHeaderFlags.FreeBlock;

                    // Link remainder into free list (in place of current)
                    *(void**)remainderObj = next;
                    if (prev == null)
                        _freeList = remainderObj;
                    else
                        *(void**)prev = remainderObj;

                    // Update current block's size to the actual allocation
                    *blockSizeHeader = totalNeeded;

                    // Update stats
                    _freeListBytes -= totalNeeded;
                    // _freeListCount stays same (replaced one block with another)
                }
                else
                {
                    // Use entire block, remove from free list
                    if (prev == null)
                        _freeList = next;
                    else
                        *(void**)prev = next;

                    // Block size stays the same (we use the whole block)
                    _freeListBytes -= blockSize;
                    _freeListCount--;
                }

                // Clear object header (removes free flag, ready for new object)
                ulong* objHeader = GetHeader(current);
                *objHeader = 0;

                _objectCount++;
                return current;
            }

            prev = current;
            current = next;
        }

        return null; // No suitable block found
    }

    /// <summary>
    /// Get free list statistics.
    /// </summary>
    public static void GetFreeListStats(out ulong freeBytes, out ulong freeCount)
    {
        freeBytes = _freeListBytes;
        freeCount = _freeListCount;
    }

    /// <summary>
    /// Clear the free list (called at start of GC before sweep rebuilds it).
    /// </summary>
    public static void ClearFreeList()
    {
        _freeList = null;
        _freeListBytes = 0;
        _freeListCount = 0;
    }

    // ========================================================================
    // Large Object Heap (LOH) Methods
    // ========================================================================

    /// <summary>
    /// Allocate a large object on the LOH.
    /// LOH objects are marked with bit 6 and are never compacted.
    /// </summary>
    public static void* AllocLOH(uint size)
    {
        // Enforce minimum size
        if (size < MinObjectSize)
            size = MinObjectSize;

        // Align to 8 bytes
        size = (size + 7) & ~7u;

        // Total allocation includes headers
        uint totalSize = size + ObjectHeaderSize;

        // Try LOH free list first
        void* fromFreeList = AllocFromLOHFreeList(totalSize);
        if (fromFreeList != null)
        {
            if (_traceAllocs)
            {
                DebugConsole.Write("[GCHeap] LOH alloc from free list: ");
                DebugConsole.WriteHex((ulong)fromFreeList);
                DebugConsole.WriteLine();
            }
            _lohTotalAllocated += totalSize;
            return fromFreeList;
        }

        // Check if we have space in current LOH region
        if (_lohAllocPtr == null || _lohAllocPtr + totalSize > _lohRegionEnd)
        {
            // Need a new LOH region
            if (!AllocateLOHRegion(totalSize))
            {
                DebugConsole.WriteLine("[GCHeap] LOH out of memory!");
                return null;
            }
        }

        // Bump allocate in LOH
        byte* blockStart = _lohAllocPtr;
        _lohAllocPtr += totalSize;

        if (_traceAllocs)
        {
            DebugConsole.Write("[GCHeap] LOH bump alloc at ");
            DebugConsole.WriteHex((ulong)(blockStart + ObjectHeaderSize));
            DebugConsole.WriteLine();
        }

        // Initialize block size header
        *(ulong*)blockStart = totalSize;

        // Initialize object header with LOH flag
        *(ulong*)(blockStart + 8) = ObjectHeaderFlags.LOHFlag;

        void* objPtr = blockStart + ObjectHeaderSize;

        _lohTotalAllocated += totalSize;
        _lohObjectCount++;

        return objPtr;
    }

    /// <summary>
    /// Allocate a new LOH region.
    /// LOH regions may be larger than SOH regions to accommodate large objects.
    /// </summary>
    private static bool AllocateLOHRegion(uint minSize)
    {
        // Calculate pages needed (at least 1MB, or enough for the object)
        ulong bytesNeeded = minSize + 8; // +8 for region header
        ulong pagesNeeded = (bytesNeeded + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
        if (pagesNeeded < InitialRegionPages)
            pagesNeeded = InitialRegionPages;

        ulong physAddr = PageAllocator.AllocatePages(pagesNeeded);
        if (physAddr == 0)
            return false;

        byte* newRegion = (byte*)VirtualMemory.PhysToVirt(physAddr);
        ulong regionSize = pagesNeeded * PageAllocator.PageSize;

        // Zero the new region
        for (ulong i = 0; i < regionSize; i++)
            newRegion[i] = 0;

        // Link into LOH region list
        *(byte**)newRegion = _lohFirstRegion;
        _lohFirstRegion = newRegion;

        // Set up for allocation
        _lohRegionStart = newRegion;
        _lohRegionEnd = newRegion + regionSize;
        _lohAllocPtr = newRegion + 8; // Skip region header

        DebugConsole.Write("[GCHeap] New LOH region: ");
        DebugConsole.WriteHex((ulong)newRegion);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)(regionSize / 1024));
        DebugConsole.WriteLine(" KB)");

        return true;
    }

    /// <summary>
    /// Check if an object is on the LOH.
    /// </summary>
    public static bool IsLOHObject(void* obj)
    {
        if (obj == null) return false;
        ulong* header = GetHeader(obj);
        return (*header & ObjectHeaderFlags.LOHFlag) != 0;
    }

    /// <summary>
    /// Set the LOH flag on an object.
    /// </summary>
    public static void SetLOHFlag(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header |= ObjectHeaderFlags.LOHFlag;
    }

    /// <summary>
    /// Try to allocate from the LOH free list.
    /// </summary>
    public static void* AllocFromLOHFreeList(uint totalNeeded)
    {
        if (_lohFreeList == null)
            return null;

        void* prev = null;
        void* current = _lohFreeList;

        while (current != null)
        {
            ulong* blockSizeHeader = GetBlockSizeHeader(current);
            uint blockSize = (uint)(*blockSizeHeader & ObjectHeaderFlags.BlockSizeMask);
            void* next = *(void**)current;

            if (blockSize >= totalNeeded)
            {
                // Found a suitable block - use entire block (no splitting for LOH)
                if (prev == null)
                    _lohFreeList = next;
                else
                    *(void**)prev = next;

                _lohFreeListBytes -= blockSize;
                _lohFreeListCount--;

                // Clear free flag, keep LOH flag
                ulong* objHeader = GetHeader(current);
                *objHeader = ObjectHeaderFlags.LOHFlag;

                _lohObjectCount++;
                return current;
            }

            prev = current;
            current = next;
        }

        return null;
    }

    /// <summary>
    /// Add a block to the LOH free list.
    /// </summary>
    public static void AddToLOHFreeList(void* obj)
    {
        ulong* blockHeader = GetBlockSizeHeader(obj);
        uint blockSize = (uint)(*blockHeader & ObjectHeaderFlags.BlockSizeMask);

        // Set free flag, keep LOH flag
        ulong* objHeader = GetHeader(obj);
        *objHeader = ObjectHeaderFlags.FreeBlock | ObjectHeaderFlags.LOHFlag;

        // Store next pointer where MethodTable* was
        *(void**)obj = _lohFreeList;
        _lohFreeList = obj;

        _lohFreeListBytes += blockSize;
        _lohFreeListCount++;
    }

    /// <summary>
    /// Clear the LOH free list.
    /// </summary>
    public static void ClearLOHFreeList()
    {
        _lohFreeList = null;
        _lohFreeListBytes = 0;
        _lohFreeListCount = 0;
    }

    /// <summary>
    /// Get LOH statistics.
    /// </summary>
    public static void GetLOHStats(out ulong totalAllocated, out ulong objectCount)
    {
        totalAllocated = _lohTotalAllocated;
        objectCount = _lohObjectCount;
    }

    /// <summary>
    /// Get LOH free list statistics.
    /// </summary>
    public static void GetLOHFreeListStats(out ulong freeBytes, out ulong freeCount)
    {
        freeBytes = _lohFreeListBytes;
        freeCount = _lohFreeListCount;
    }

    /// <summary>
    /// Get the first LOH region (for heap walking).
    /// </summary>
    public static byte* LOHFirstRegion => _lohFirstRegion;

    /// <summary>
    /// Get the current LOH allocation pointer.
    /// </summary>
    public static byte* LOHAllocPtr => _lohAllocPtr;

    /// <summary>
    /// Check if an address is in the LOH.
    /// </summary>
    public static bool IsInLOH(void* ptr)
    {
        byte* p = (byte*)ptr;
        byte* region = _lohFirstRegion;

        while (region != null)
        {
            // LOH regions can be variable size, check using region header info
            // For now, use InitialRegionSize as maximum check
            if (p >= region && p < region + InitialRegionSize * 4) // Allow larger LOH regions
            {
                // More precise check using allocation bounds
                if (region == _lohRegionStart && p < _lohAllocPtr)
                    return true;
                else if (region != _lohRegionStart)
                    return true; // Older region, fully allocated
            }
            region = *(byte**)region;
        }

        return false;
    }

    // ========================================================================
    // Forwarding Pointer Methods (for compaction)
    // ========================================================================

    /// <summary>
    /// Check if an object has a forwarding pointer set.
    /// </summary>
    public static bool HasForwardingPointer(void* obj)
    {
        if (obj == null) return false;
        ulong* header = GetHeader(obj);
        return (*header & ObjectHeaderFlags.HasForwardingPointer) != 0;
    }

    /// <summary>
    /// Set a forwarding address for an object.
    /// The forwarding address is stored in the upper 32 bits of the block size header (as delta)
    /// or at the MethodTable* slot if the delta is too large.
    /// </summary>
    public static void SetForwardingAddress(void* obj, void* newAddr)
    {
        ulong* header = GetHeader(obj);
        ulong* blockHeader = GetBlockSizeHeader(obj);

        // Calculate delta (can be negative if compacting to lower address)
        long delta = (long)((byte*)newAddr - (byte*)obj);

        // Check if delta fits in 32 bits (signed)
        if (delta >= int.MinValue && delta <= int.MaxValue)
        {
            // Store delta in upper 32 bits of block size header
            uint blockSize = (uint)(*blockHeader & ObjectHeaderFlags.BlockSizeMask);
            *blockHeader = blockSize | ((ulong)(uint)(int)delta << 32);
            *header |= ObjectHeaderFlags.HasForwardingPointer;
        }
        else
        {
            // Delta too large, store full pointer at MethodTable* slot
            *(void**)obj = newAddr;
            *header |= ObjectHeaderFlags.HasForwardingPointer | ObjectHeaderFlags.ForwardingInPlace;
        }
    }

    /// <summary>
    /// Get the forwarding address for an object.
    /// </summary>
    public static void* GetForwardingAddress(void* obj)
    {
        ulong* header = GetHeader(obj);

        if ((*header & ObjectHeaderFlags.HasForwardingPointer) == 0)
            return obj; // No forwarding, return original

        if ((*header & ObjectHeaderFlags.ForwardingInPlace) != 0)
        {
            // Full pointer stored at MethodTable* slot
            return *(void**)obj;
        }
        else
        {
            // Delta stored in upper 32 bits of block size header
            ulong* blockHeader = GetBlockSizeHeader(obj);
            int delta = (int)(*blockHeader >> 32);
            return (byte*)obj + delta;
        }
    }

    /// <summary>
    /// Clear the forwarding pointer from an object (after move).
    /// </summary>
    public static void ClearForwardingPointer(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header &= ~(ObjectHeaderFlags.HasForwardingPointer | ObjectHeaderFlags.ForwardingInPlace);

        // Clear the upper 32 bits of block size header
        ulong* blockHeader = GetBlockSizeHeader(obj);
        *blockHeader &= ObjectHeaderFlags.BlockSizeMask;
    }

    /// <summary>
    /// Check if an object is pinned (cannot be moved).
    /// </summary>
    public static bool IsPinned(void* obj)
    {
        if (obj == null) return false;
        ulong* header = GetHeader(obj);
        return (*header & ObjectHeaderFlags.Pinned) != 0;
    }

    /// <summary>
    /// Pin an object (prevent it from being moved during compaction).
    /// </summary>
    public static void SetPinned(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header |= ObjectHeaderFlags.Pinned;
    }

    /// <summary>
    /// Unpin an object.
    /// </summary>
    public static void ClearPinned(void* obj)
    {
        ulong* header = GetHeader(obj);
        *header &= ~ObjectHeaderFlags.Pinned;
    }

    /// <summary>
    /// Reset the SOH allocation pointer (after compaction).
    /// </summary>
    public static void SetAllocPtr(byte* newPtr)
    {
        _allocPtr = newPtr;
    }

    /// <summary>
    /// Get the current SOH region start.
    /// </summary>
    public static byte* RegionStart => _regionStart;
}
