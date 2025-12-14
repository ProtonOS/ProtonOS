// ProtonOS Kernel - Growable Block Allocator
// Provides dynamically growing storage for kernel registries without hard limits.

using System.Runtime.InteropServices;
using ProtonOS.Memory;
using ProtonOS.Platform;

namespace ProtonOS.Runtime;

/// <summary>
/// Header for a block in the allocator chain.
/// Entries follow immediately after this header.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BlockHeader
{
    /// <summary>Pointer to next block in chain (null if last).</summary>
    public BlockHeader* Next;

    /// <summary>Number of entries used in this block.</summary>
    public int Used;

    /// <summary>Maximum entries this block can hold.</summary>
    public int Capacity;

    /// <summary>Size of each entry in bytes.</summary>
    public int EntrySize;

    /// <summary>Get pointer to the data area (immediately after header).</summary>
    public byte* Data => (byte*)((BlockHeader*)Unsafe.AsPointer(ref this) + 1);

    /// <summary>Get pointer to entry at index.</summary>
    public byte* GetEntry(int index) => Data + index * EntrySize;

    /// <summary>Check if this block is full.</summary>
    public bool IsFull => Used >= Capacity;
}

/// <summary>
/// Manages a chain of blocks for a registry.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BlockChain
{
    /// <summary>First block in the chain.</summary>
    public BlockHeader* First;

    /// <summary>Current block being filled (for fast adds).</summary>
    public BlockHeader* Current;

    /// <summary>Total number of entries across all blocks.</summary>
    public int TotalCount;

    /// <summary>Size of each entry in bytes.</summary>
    public int EntrySize;

    /// <summary>Number of entries per block.</summary>
    public int BlockCapacity;
}

/// <summary>
/// Block allocator operations.
/// </summary>
public static unsafe class BlockAllocator
{
    /// <summary>
    /// Initialize a block chain with the first block.
    /// </summary>
    /// <param name="chain">Block chain to initialize.</param>
    /// <param name="entrySize">Size of each entry in bytes.</param>
    /// <param name="blockCapacity">Number of entries per block.</param>
    /// <returns>True if initialization succeeded.</returns>
    public static bool Init(BlockChain* chain, int entrySize, int blockCapacity)
    {
        if (chain == null || entrySize <= 0 || blockCapacity <= 0)
            return false;

        var block = AllocateBlock(entrySize, blockCapacity);
        if (block == null)
            return false;

        chain->First = block;
        chain->Current = block;
        chain->TotalCount = 0;
        chain->EntrySize = entrySize;
        chain->BlockCapacity = blockCapacity;

        return true;
    }

    /// <summary>
    /// Add an entry to the block chain.
    /// </summary>
    /// <param name="chain">Block chain to add to.</param>
    /// <param name="entry">Pointer to entry data to copy.</param>
    /// <returns>Pointer to stored entry, or null if allocation failed.</returns>
    public static byte* Add(BlockChain* chain, void* entry)
    {
        if (chain == null || chain->Current == null || entry == null)
            return null;

        // Allocate new block if current is full
        if (chain->Current->IsFull)
        {
            var newBlock = AllocateBlock(chain->EntrySize, chain->BlockCapacity);
            if (newBlock == null)
            {
                DebugConsole.WriteLine("[BlockAlloc] Failed to allocate new block");
                return null;
            }

            chain->Current->Next = newBlock;
            chain->Current = newBlock;
        }

        // Get slot and copy entry
        byte* slot = chain->Current->GetEntry(chain->Current->Used);
        byte* src = (byte*)entry;
        for (int i = 0; i < chain->EntrySize; i++)
            slot[i] = src[i];

        chain->Current->Used++;
        chain->TotalCount++;

        return slot;
    }

    /// <summary>
    /// Find an entry by iterating all blocks.
    /// </summary>
    /// <param name="chain">Block chain to search.</param>
    /// <param name="predicate">Function that returns true for matching entry.</param>
    /// <returns>Pointer to matching entry, or null if not found.</returns>
    public static byte* Find(BlockChain* chain, delegate*<byte*, bool> predicate)
    {
        if (chain == null || chain->First == null)
            return null;

        var block = chain->First;
        while (block != null)
        {
            for (int i = 0; i < block->Used; i++)
            {
                byte* entry = block->GetEntry(i);
                if (predicate(entry))
                    return entry;
            }
            block = block->Next;
        }

        return null;
    }

    /// <summary>
    /// Get entry at a specific index across all blocks.
    /// </summary>
    /// <param name="chain">Block chain.</param>
    /// <param name="index">Global index.</param>
    /// <returns>Pointer to entry, or null if out of range.</returns>
    public static byte* GetAt(BlockChain* chain, int index)
    {
        if (chain == null || chain->First == null || index < 0 || index >= chain->TotalCount)
            return null;

        var block = chain->First;
        int remaining = index;

        while (block != null)
        {
            if (remaining < block->Used)
                return block->GetEntry(remaining);

            remaining -= block->Used;
            block = block->Next;
        }

        return null;
    }

    /// <summary>
    /// Iterate all entries in the block chain.
    /// </summary>
    /// <param name="chain">Block chain to iterate.</param>
    /// <param name="callback">Function called for each entry.</param>
    public static void ForEach(BlockChain* chain, delegate*<byte*, void> callback)
    {
        if (chain == null || chain->First == null || callback == null)
            return;

        var block = chain->First;
        while (block != null)
        {
            for (int i = 0; i < block->Used; i++)
            {
                callback(block->GetEntry(i));
            }
            block = block->Next;
        }
    }

    /// <summary>
    /// Get statistics about the block chain.
    /// </summary>
    public static void GetStats(BlockChain* chain, out int totalEntries, out int blockCount, out int totalCapacity)
    {
        totalEntries = 0;
        blockCount = 0;
        totalCapacity = 0;

        if (chain == null || chain->First == null)
            return;

        totalEntries = chain->TotalCount;

        var block = chain->First;
        while (block != null)
        {
            blockCount++;
            totalCapacity += block->Capacity;
            block = block->Next;
        }
    }

    /// <summary>
    /// Allocate a new block.
    /// </summary>
    private static BlockHeader* AllocateBlock(int entrySize, int capacity)
    {
        int totalSize = sizeof(BlockHeader) + capacity * entrySize;
        var block = (BlockHeader*)HeapAllocator.AllocZeroed((ulong)totalSize);

        if (block != null)
        {
            block->Next = null;
            block->Used = 0;
            block->Capacity = capacity;
            block->EntrySize = entrySize;
        }

        return block;
    }
}

/// <summary>
/// Unsafe helper for getting pointer from ref.
/// </summary>
internal static class Unsafe
{
    public static unsafe void* AsPointer<T>(ref T value) where T : unmanaged
    {
        fixed (T* ptr = &value)
            return ptr;
    }
}
