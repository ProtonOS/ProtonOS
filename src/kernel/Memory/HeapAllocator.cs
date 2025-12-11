// ProtonOS kernel - Kernel heap allocator
// Simple free-list allocator for variable-sized allocations.
// Uses PageAllocator for backing memory, grows on demand.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.Memory;

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
    /// Get the usable size of an allocated block.
    /// </summary>
    /// <param name="ptr">Pointer returned by Alloc</param>
    /// <returns>Usable size in bytes, or 0 if invalid</returns>
    public static ulong GetSize(void* ptr)
    {
        if (!_initialized || ptr == null)
            return 0;

        // Get block header (located before the user pointer)
        var block = (HeapBlock*)((byte*)ptr - sizeof(HeapBlock));

        // Validate magic number
        if (block->Magic != HeapBlock.MagicValue)
            return 0;

        // Don't return size for free blocks
        if (block->IsFree)
            return 0;

        // Return usable size (total size minus header)
        return block->DataSize;
    }

    /// <summary>
    /// Reallocate memory, attempting in-place resize when possible.
    /// </summary>
    /// <param name="ptr">Pointer returned by Alloc (null = new allocation)</param>
    /// <param name="newSize">New size in bytes (0 = free)</param>
    /// <returns>Pointer to reallocated memory, or null on failure</returns>
    public static void* Realloc(void* ptr, ulong newSize)
    {
        if (!_initialized)
            return null;

        // Null pointer = new allocation
        if (ptr == null)
            return Alloc(newSize);

        // Size 0 = free
        if (newSize == 0)
        {
            Free(ptr);
            return null;
        }

        // Get block header
        var block = (HeapBlock*)((byte*)ptr - sizeof(HeapBlock));

        // Validate
        if (block->Magic != HeapBlock.MagicValue || block->IsFree)
            return null;

        ulong oldSize = block->DataSize;

        // Calculate new total block size needed
        ulong newTotalSize = (ulong)sizeof(HeapBlock) + newSize;
        newTotalSize = Align(newTotalSize, Alignment);
        if (newTotalSize < MinBlockSize)
            newTotalSize = MinBlockSize;

        ulong oldTotalSize = block->Size;

        // Case 1: New size fits in current block (shrinking or same)
        if (newTotalSize <= oldTotalSize)
        {
            ulong remaining = oldTotalSize - newTotalSize;

            // If there's enough space left over, split and free the remainder
            if (remaining >= MinBlockSize)
            {
                // Create new free block from the remainder
                var newBlock = (HeapBlock*)((byte*)block + newTotalSize);
                newBlock->Size = remaining;
                newBlock->Next = null;
                newBlock->Magic = HeapBlock.MagicValue;
                newBlock->Flags = HeapBlock.FlagFree;

                // Shrink current block
                block->Size = newTotalSize;

                // Update stats
                _totalAllocated -= remaining;
                _totalFree += remaining;

                // Insert remainder into free list and coalesce
                InsertFreeBlock(newBlock);
                CoalesceBlocks();
            }
            // else: remainder too small to split, just keep the extra space

            return ptr;
        }

        // Case 2: Need more space - check if next block is free and adjacent
        ulong blockEnd = (ulong)block + oldTotalSize;
        ulong extraNeeded = newTotalSize - oldTotalSize;

        // Search free list for adjacent block
        HeapBlock* prevFree = null;
        HeapBlock* nextFree = _freeList;

        while (nextFree != null)
        {
            if ((ulong)nextFree == blockEnd)
            {
                // Found adjacent free block - can we use it?
                if (nextFree->Size >= extraNeeded)
                {
                    // Yes! Expand into the free block
                    ulong freeSize = nextFree->Size;
                    ulong combined = oldTotalSize + freeSize;
                    ulong remaining = combined - newTotalSize;

                    // Remove free block from free list
                    if (prevFree == null)
                        _freeList = nextFree->Next;
                    else
                        prevFree->Next = nextFree->Next;

                    _totalFree -= freeSize;

                    if (remaining >= MinBlockSize)
                    {
                        // Expand and create new smaller free block
                        block->Size = newTotalSize;
                        _totalAllocated += (newTotalSize - oldTotalSize);

                        var remainBlock = (HeapBlock*)((byte*)block + newTotalSize);
                        remainBlock->Size = remaining;
                        remainBlock->Next = null;
                        remainBlock->Magic = HeapBlock.MagicValue;
                        remainBlock->Flags = HeapBlock.FlagFree;

                        _totalFree += remaining;
                        InsertFreeBlock(remainBlock);
                    }
                    else
                    {
                        // Use entire combined space
                        block->Size = combined;
                        _totalAllocated += (combined - oldTotalSize);
                    }

                    return ptr;
                }
                break;  // Found adjacent but too small
            }
            prevFree = nextFree;
            nextFree = nextFree->Next;
        }

        // Case 3: Cannot expand in place - allocate new, copy, free old
        void* newPtr = Alloc(newSize);
        if (newPtr == null)
            return null;

        // Copy old data using fast memcpy (only up to old size)
        ulong copySize = oldSize < newSize ? oldSize : newSize;
        CPU.MemCopy(newPtr, ptr, copySize);

        Free(ptr);
        return newPtr;
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

            // Heap growth debug (verbose - commented out)
            // DebugConsole.Write("[Heap] Grew by ");
            // DebugConsole.WriteHex(growSize / 1024);
            // DebugConsole.WriteLine(" KB (contiguous)");
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

            // Heap growth debug (verbose - commented out)
            // DebugConsole.Write("[Heap] Grew by ");
            // DebugConsole.WriteHex(growSize / 1024);
            // DebugConsole.WriteLine(" KB (non-contiguous)");
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
