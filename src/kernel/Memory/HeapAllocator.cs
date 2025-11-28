// netos mernel - Kernel heap allocator
// Simple free-list allocator for variable-sized allocations.
// Uses PageAllocator for backing memory, grows on demand.

using System.Runtime.InteropServices;
using Kernel.Platform;

namespace Kernel.Memory;

/// <summary>
/// Block header stored at the start of each allocation.
/// Free blocks form a linked list; allocated blocks store size only.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct HeapBlock
{
    public ulong Size;          // Size of this block (including header)
    public HeapBlock* Next;     // Next free block (only valid when free)
    public uint Magic;          // Magic number for validation
    public uint Flags;          // Bit 0: 1 = free, 0 = allocated

    public const uint MagicValue = 0x48454150;  // "HEAP" in ASCII
    public const uint FlagFree = 1;

    public bool IsFree => (Flags & FlagFree) != 0;
    public ulong DataSize => Size - (ulong)sizeof(HeapBlock);
}

/// <summary>
/// Kernel heap allocator using a first-fit free list.
/// Thread-safety: NOT thread-safe (add spinlock when we have threads).
/// </summary>
public static unsafe class HeapAllocator
{
    private const ulong MinBlockSize = 32;          // Minimum allocation (includes header)
    private const ulong InitialHeapPages = 16;      // 64KB initial heap
    private const ulong GrowthPages = 16;           // Grow by 64KB at a time
    private const ulong Alignment = 16;             // 16-byte alignment for allocations

    private static HeapBlock* _freeList;            // Head of free list
    private static ulong _heapStart;                // Start of heap region
    private static ulong _heapEnd;                  // End of heap region (can grow)
    private static ulong _totalAllocated;           // Bytes currently allocated
    private static ulong _totalFree;                // Bytes currently free
    private static bool _initialized;

    /// <summary>
    /// Total bytes currently allocated
    /// </summary>
    public static ulong TotalAllocated => _totalAllocated;

    /// <summary>
    /// Total bytes currently free
    /// </summary>
    public static ulong TotalFree => _totalFree;

    /// <summary>
    /// Whether the heap has been initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initialize the kernel heap.
    /// Must be called after PageAllocator.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        if (!PageAllocator.IsInitialized)
        {
            DebugConsole.WriteLine("[Heap] PageAllocator not initialized!");
            return false;
        }

        DebugConsole.WriteLine("[Heap] Initializing kernel heap...");

        // Allocate initial heap pages
        ulong heapSize = InitialHeapPages * PageAllocator.PageSize;
        _heapStart = PageAllocator.AllocatePages(InitialHeapPages);
        if (_heapStart == 0)
        {
            DebugConsole.WriteLine("[Heap] Failed to allocate initial heap!");
            return false;
        }

        _heapEnd = _heapStart + heapSize;

        // Create single free block spanning entire heap
        var block = (HeapBlock*)_heapStart;
        block->Size = heapSize;
        block->Next = null;
        block->Magic = 0x48454150; // "HEAP" in ASCII
        block->Flags = HeapBlock.FlagFree;

        _freeList = block;
        _totalFree = heapSize - (ulong)sizeof(HeapBlock);
        _totalAllocated = 0;
        _initialized = true;

        DebugConsole.Write("[Heap] Initialized at 0x");
        DebugConsole.WriteHex(_heapStart);
        DebugConsole.Write(" size ");
        DebugConsole.WriteHex(heapSize / 1024);
        DebugConsole.WriteLine(" KB");

        return true;
    }

    /// <summary>
    /// Allocate memory from the heap.
    /// </summary>
    /// <param name="size">Number of bytes to allocate</param>
    /// <returns>Pointer to allocated memory, or null if out of memory</returns>
    public static void* Alloc(ulong size)
    {
        if (!_initialized || size == 0)
            return null;

        // Calculate total block size needed (header + data, aligned)
        ulong totalSize = (ulong)sizeof(HeapBlock) + size;
        totalSize = Align(totalSize, Alignment);
        if (totalSize < MinBlockSize)
            totalSize = MinBlockSize;

        // First-fit search through free list
        HeapBlock* prev = null;
        HeapBlock* current = _freeList;

        while (current != null)
        {
            if (current->IsFree && current->Size >= totalSize)
            {
                // Found a suitable block
                ulong remaining = current->Size - totalSize;

                if (remaining >= MinBlockSize)
                {
                    // Split the block
                    var newBlock = (HeapBlock*)((byte*)current + totalSize);
                    newBlock->Size = remaining;
                    newBlock->Next = current->Next;
                    newBlock->Magic = 0x48454150;
                    newBlock->Flags = HeapBlock.FlagFree;

                    current->Size = totalSize;
                    current->Next = newBlock;

                    // Update free list
                    if (prev == null)
                        _freeList = newBlock;
                    else
                        prev->Next = newBlock;
                }
                else
                {
                    // Use entire block (don't split)
                    if (prev == null)
                        _freeList = current->Next;
                    else
                        prev->Next = current->Next;
                }

                // Mark as allocated
                current->Flags &= ~HeapBlock.FlagFree;
                current->Next = null;

                _totalAllocated += current->Size;
                _totalFree -= current->Size;

                // Return pointer past the header
                return (byte*)current + sizeof(HeapBlock);
            }

            prev = current;
            current = current->Next;
        }

        // No suitable block found - try to grow the heap
        if (Grow(totalSize))
        {
            // Retry allocation after growing
            return Alloc(size);
        }

        return null;  // Out of memory
    }

    /// <summary>
    /// Allocate zeroed memory from the heap.
    /// </summary>
    public static void* AllocZeroed(ulong size)
    {
        void* ptr = Alloc(size);
        if (ptr != null)
        {
            // Zero the memory
            byte* p = (byte*)ptr;
            for (ulong i = 0; i < size; i++)
                p[i] = 0;
        }
        return ptr;
    }

    /// <summary>
    /// Free previously allocated memory.
    /// </summary>
    /// <param name="ptr">Pointer returned by Alloc</param>
    public static void Free(void* ptr)
    {
        if (!_initialized || ptr == null)
            return;

        // Get block header
        var block = (HeapBlock*)((byte*)ptr - sizeof(HeapBlock));

        // Validate magic number
        if (block->Magic != 0x48454150)
        {
            DebugConsole.WriteLine("[Heap] Free: invalid block (bad magic)!");
            return;
        }

        // Check not already free
        if (block->IsFree)
        {
            DebugConsole.WriteLine("[Heap] Free: double free detected!");
            return;
        }

        // Mark as free
        block->Flags |= HeapBlock.FlagFree;
        _totalAllocated -= block->Size;
        _totalFree += block->Size;

        // Insert into free list (sorted by address for coalescing)
        InsertFreeBlock(block);

        // Coalesce adjacent free blocks
        CoalesceBlocks();
    }

    /// <summary>
    /// Grow the heap by allocating more pages.
    /// </summary>
    private static bool Grow(ulong minSize)
    {
        // Calculate how many pages we need
        ulong pagesNeeded = (minSize + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
        if (pagesNeeded < GrowthPages)
            pagesNeeded = GrowthPages;

        // Try to allocate contiguous pages at the end of heap
        ulong newPages = PageAllocator.AllocatePages(pagesNeeded);
        if (newPages == 0)
            return false;

        ulong growSize = pagesNeeded * PageAllocator.PageSize;

        // Check if new pages are contiguous with existing heap
        if (newPages == _heapEnd)
        {
            // Contiguous - extend the heap
            _heapEnd = newPages + growSize;

            // Create new free block
            var block = (HeapBlock*)newPages;
            block->Size = growSize;
            block->Next = null;
            block->Magic = 0x48454150;
            block->Flags = HeapBlock.FlagFree;

            _totalFree += growSize - (ulong)sizeof(HeapBlock);
            InsertFreeBlock(block);
            CoalesceBlocks();

            DebugConsole.Write("[Heap] Grew by ");
            DebugConsole.WriteHex(growSize / 1024);
            DebugConsole.WriteLine(" KB (contiguous)");
        }
        else
        {
            // Non-contiguous - add as separate region
            // For simplicity, just add it to the free list
            var block = (HeapBlock*)newPages;
            block->Size = growSize;
            block->Next = null;
            block->Magic = 0x48454150;
            block->Flags = HeapBlock.FlagFree;

            _totalFree += growSize - (ulong)sizeof(HeapBlock);
            InsertFreeBlock(block);

            DebugConsole.Write("[Heap] Grew by ");
            DebugConsole.WriteHex(growSize / 1024);
            DebugConsole.WriteLine(" KB (non-contiguous)");
        }

        return true;
    }

    /// <summary>
    /// Insert a block into the free list (sorted by address).
    /// </summary>
    private static void InsertFreeBlock(HeapBlock* block)
    {
        if (_freeList == null || (ulong)block < (ulong)_freeList)
        {
            // Insert at head
            block->Next = _freeList;
            _freeList = block;
            return;
        }

        // Find insertion point
        HeapBlock* current = _freeList;
        while (current->Next != null && (ulong)current->Next < (ulong)block)
        {
            current = current->Next;
        }

        block->Next = current->Next;
        current->Next = block;
    }

    /// <summary>
    /// Merge adjacent free blocks.
    /// </summary>
    private static void CoalesceBlocks()
    {
        HeapBlock* current = _freeList;

        while (current != null && current->Next != null)
        {
            // Check if current and next are adjacent
            ulong currentEnd = (ulong)current + current->Size;
            if (currentEnd == (ulong)current->Next)
            {
                // Merge with next block
                HeapBlock* next = current->Next;
                current->Size += next->Size;
                current->Next = next->Next;
                // Don't advance - check if we can merge more
            }
            else
            {
                current = current->Next;
            }
        }
    }

    /// <summary>
    /// Align a value up to the specified alignment.
    /// </summary>
    private static ulong Align(ulong value, ulong alignment)
    {
        return (value + alignment - 1) & ~(alignment - 1);
    }

    /// <summary>
    /// Print heap statistics (for debugging).
    /// </summary>
    public static void PrintStats()
    {
        DebugConsole.Write("[Heap] Allocated: ");
        DebugConsole.WriteHex(_totalAllocated);
        DebugConsole.Write(" Free: ");
        DebugConsole.WriteHex(_totalFree);
        DebugConsole.WriteLine();

        // Count free blocks
        int freeBlocks = 0;
        HeapBlock* current = _freeList;
        while (current != null)
        {
            freeBlocks++;
            current = current->Next;
        }

        DebugConsole.Write("[Heap] Free blocks: ");
        DebugConsole.WriteHex((ushort)freeBlocks);
        DebugConsole.WriteLine();
    }
}
