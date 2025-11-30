// netos kernel - GC Heap Allocator
// Manages the managed object heap with proper object headers for garbage collection.
//
// Object Layout:
//   -8: Object Header (8 bytes)
//        Bits 0:     Mark bit (1 = reachable during GC)
//        Bits 1:     Pinned (cannot be relocated)
//        Bits 2-3:   Reserved (future: generation)
//        Bits 4-31:  Sync Block Index (28 bits)
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
/// Object header flags stored in the 8 bytes before the MethodTable pointer.
/// </summary>
public static class ObjectHeaderFlags
{
    /// <summary>Mark bit - set during GC mark phase, cleared during sweep.</summary>
    public const ulong Mark = 1UL << 0;

    /// <summary>Pinned - object cannot be relocated by GC.</summary>
    public const ulong Pinned = 1UL << 1;

    /// <summary>Reserved for future generation tracking.</summary>
    public const ulong GenerationMask = 0b1100UL; // bits 2-3

    /// <summary>Sync block index mask (bits 4-31, 28 bits = 256M entries).</summary>
    public const ulong SyncBlockMask = 0x0FFFFFFF0UL;

    /// <summary>Shift to get/set sync block index.</summary>
    public const int SyncBlockShift = 4;

    /// <summary>Hash code is stored in upper 32 bits.</summary>
    public const int HashCodeShift = 32;
}

/// <summary>
/// GC Heap allocator using bump allocation from contiguous page regions.
/// </summary>
public static unsafe class GCHeap
{
    // Initial region size: 1MB (256 pages)
    private const ulong InitialRegionPages = 256;
    private const ulong InitialRegionSize = InitialRegionPages * PageAllocator.PageSize;

    // Minimum object size (header + MT pointer + at least 8 bytes for free list linking)
    private const uint MinObjectSize = 24;

    // Object header size (stored before the MethodTable pointer)
    public const uint ObjectHeaderSize = 8;

    // Current allocation region
    private static byte* _regionStart;
    private static byte* _regionEnd;
    private static byte* _allocPtr;

    // Statistics
    private static ulong _totalAllocated;
    private static ulong _objectCount;

    // Track all regions for sweep phase (simple linked list using first 8 bytes of each region)
    private static byte* _firstRegion;

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
        byte* headerPtr = _allocPtr;
        _allocPtr += totalSize;

        // Initialize object header to zero (mark=0, hash=0, sync=0)
        *(ulong*)headerPtr = 0;

        // Object reference points past the header to the MethodTable* slot
        void* objPtr = headerPtr + ObjectHeaderSize;

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
    /// Get the object header for an object reference.
    /// </summary>
    /// <param name="obj">Pointer to MethodTable* slot.</param>
    /// <returns>Pointer to 8-byte header before the object.</returns>
    public static ulong* GetHeader(void* obj)
    {
        return (ulong*)((byte*)obj - ObjectHeaderSize);
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
}
