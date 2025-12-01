// netos kernel - GC Heap Allocator
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
using Kernel.Platform;
using Kernel.X64;

namespace Kernel.Memory;

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

    /// <summary>Reserved for future use (bit 3).</summary>
    public const ulong Reserved3 = 1UL << 3;

    /// <summary>Generation bit 0 - low bit of 2-bit generation field (bits 4-5).</summary>
    public const ulong Generation0 = 1UL << 4;

    /// <summary>Generation bit 1 - high bit of 2-bit generation field (bits 4-5).</summary>
    public const ulong Generation1 = 1UL << 5;

    /// <summary>Generation mask (bits 4-5) - covers Gen0=0, Gen1=1, Gen2=2, (3=reserved).</summary>
    public const ulong GenerationMask = 0x30UL;

    /// <summary>Shift to get/set generation value.</summary>
    public const int GenerationShift = 4;

    /// <summary>Reserved for future use (bit 6).</summary>
    public const ulong Reserved6 = 1UL << 6;

    /// <summary>Reserved for future use (bit 7).</summary>
    public const ulong Reserved7 = 1UL << 7;

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

    // Free list for reclaimed objects
    // Free block layout:
    //   -8: Header [Flags (32 bits) | Size (32 bits)] - size stored where hash code was
    //    0: Next pointer (8 bytes) - stored where MethodTable* was
    // Minimum free block size = ObjectHeaderSize + 8 = 16 bytes
    private static void* _freeList;
    private static ulong _freeListBytes;
    private static ulong _freeListCount;

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

        DebugConsole.Write("[GCHeap] Initialized: ");
        DebugConsole.WriteHex((ulong)_regionStart);
        DebugConsole.Write(" - ");
        DebugConsole.WriteHex((ulong)_regionEnd);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)(InitialRegionSize / 1024));
        DebugConsole.WriteLine(" KB)");

        return true;
    }

    /// <summary>
    /// Allocate a new object with proper header.
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
}
