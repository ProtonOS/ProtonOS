# Growable Block Allocator Design

A reusable block allocator for kernel registries that grow without hard limits.

## Problem Statement

Many kernel registries use fixed-size arrays that can be exhausted:
- JITMethodRegistry (1024 methods)
- TypeRegistry (256 types per assembly)
- FunctionTableStorage (2048 entries)
- Various caches (64-512 entries)

When these fill up, functionality silently fails or crashes.

## Design Goals

1. **No hard limit**: Grow until system memory is exhausted
2. **Efficient lookup**: O(n) in worst case, but good locality
3. **Simple implementation**: Minimal code, easy to audit
4. **Reusable**: Single implementation for all registries
5. **No external allocations for small uses**: First block inline

## Block Allocator Structure

```csharp
/// <summary>
/// A single block in the block allocator chain.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct AllocatorBlock<T> where T : unmanaged
{
    /// <summary>Pointer to next block (null if last).</summary>
    public AllocatorBlock<T>* Next;

    /// <summary>Number of entries used in this block.</summary>
    public int Used;

    /// <summary>Capacity of this block (entries).</summary>
    public int Capacity;

    // Entries follow immediately after this header
    // T Entries[Capacity];
}

/// <summary>
/// Growable block-based allocator for registry entries.
/// </summary>
public unsafe struct BlockAllocator<T> where T : unmanaged
{
    /// <summary>First block (may be inline or allocated).</summary>
    public AllocatorBlock<T>* First;

    /// <summary>Current block being filled.</summary>
    public AllocatorBlock<T>* Current;

    /// <summary>Total number of entries across all blocks.</summary>
    public int TotalCount;

    /// <summary>Entries per block (for new allocations).</summary>
    public int BlockSize;
}
```

## API

```csharp
public static unsafe class BlockAllocatorOps
{
    /// <summary>
    /// Initialize a block allocator with the first block.
    /// </summary>
    public static void Init<T>(BlockAllocator<T>* allocator, int blockSize)
        where T : unmanaged
    {
        var block = AllocBlock<T>(blockSize);
        allocator->First = block;
        allocator->Current = block;
        allocator->TotalCount = 0;
        allocator->BlockSize = blockSize;
    }

    /// <summary>
    /// Add an entry to the allocator. Returns pointer to stored entry.
    /// </summary>
    public static T* Add<T>(BlockAllocator<T>* allocator, T* entry)
        where T : unmanaged
    {
        var current = allocator->Current;

        // Allocate new block if current is full
        if (current->Used >= current->Capacity)
        {
            var newBlock = AllocBlock<T>(allocator->BlockSize);
            if (newBlock == null)
                return null;  // Out of memory

            current->Next = newBlock;
            allocator->Current = newBlock;
            current = newBlock;
        }

        // Get pointer to next slot
        T* entries = (T*)((byte*)current + sizeof(AllocatorBlock<T>));
        T* slot = &entries[current->Used];

        // Copy entry
        *slot = *entry;
        current->Used++;
        allocator->TotalCount++;

        return slot;
    }

    /// <summary>
    /// Iterate all entries in the allocator.
    /// </summary>
    public static void ForEach<T>(BlockAllocator<T>* allocator, delegate*<T*, void> callback)
        where T : unmanaged
    {
        var block = allocator->First;
        while (block != null)
        {
            T* entries = (T*)((byte*)block + sizeof(AllocatorBlock<T>));
            for (int i = 0; i < block->Used; i++)
            {
                callback(&entries[i]);
            }
            block = block->Next;
        }
    }

    /// <summary>
    /// Find an entry matching a predicate.
    /// </summary>
    public static T* Find<T>(BlockAllocator<T>* allocator, delegate*<T*, bool> predicate)
        where T : unmanaged
    {
        var block = allocator->First;
        while (block != null)
        {
            T* entries = (T*)((byte*)block + sizeof(AllocatorBlock<T>));
            for (int i = 0; i < block->Used; i++)
            {
                if (predicate(&entries[i]))
                    return &entries[i];
            }
            block = block->Next;
        }
        return null;
    }

    private static AllocatorBlock<T>* AllocBlock<T>(int capacity)
        where T : unmanaged
    {
        int size = sizeof(AllocatorBlock<T>) + capacity * sizeof(T);
        var block = (AllocatorBlock<T>*)HeapAllocator.AllocZeroed((ulong)size);
        if (block != null)
        {
            block->Next = null;
            block->Used = 0;
            block->Capacity = capacity;
        }
        return block;
    }
}
```

## Implementation Strategy

### Phase 1: Non-Generic Version
Since our kernel may not have full generic support, implement as:

```csharp
[StructLayout(LayoutKind.Sequential)]
public unsafe struct GenericBlock
{
    public GenericBlock* Next;
    public int Used;
    public int Capacity;
    public int EntrySize;
    // byte Data[Capacity * EntrySize] follows
}

public static unsafe class BlockAllocator
{
    public static GenericBlock* Create(int entrySize, int blockCapacity)
    {
        int totalSize = sizeof(GenericBlock) + blockCapacity * entrySize;
        var block = (GenericBlock*)HeapAllocator.AllocZeroed((ulong)totalSize);
        if (block != null)
        {
            block->Capacity = blockCapacity;
            block->EntrySize = entrySize;
        }
        return block;
    }

    public static void* Add(GenericBlock** head, GenericBlock** current, void* entry)
    {
        if ((*current)->Used >= (*current)->Capacity)
        {
            var newBlock = Create((*current)->EntrySize, (*current)->Capacity);
            if (newBlock == null) return null;
            (*current)->Next = newBlock;
            *current = newBlock;
        }

        byte* data = (byte*)(*current) + sizeof(GenericBlock);
        byte* slot = data + (*current)->Used * (*current)->EntrySize;

        // Copy entry
        for (int i = 0; i < (*current)->EntrySize; i++)
            slot[i] = ((byte*)entry)[i];

        (*current)->Used++;
        return slot;
    }
}
```

### Phase 2: Apply to Registries
Convert each registry to use the block allocator:

1. **JITMethodRegistry**: Replace `JITMethodInfo* _methods` with block chain
2. **FunctionTableStorage**: Replace fixed array with block chain
3. **TypeRegistry**: Replace fixed array with block chain per assembly
4. **MetadataIntegration caches**: Replace each fixed array

### Inline First Block Optimization
For structures that are often small, embed the first block:

```csharp
public unsafe struct TypeRegistryV2
{
    // Inline storage for first 64 entries (no allocation needed)
    public fixed byte InlineStorage[64 * sizeof(TypeRegistryEntry) + sizeof(GenericBlock)];

    // Points to InlineStorage initially, then to allocated blocks
    public GenericBlock* First;
    public GenericBlock* Current;
    public int TotalCount;

    public void Initialize()
    {
        fixed (byte* ptr = InlineStorage)
        {
            First = (GenericBlock*)ptr;
            Current = First;
            First->Capacity = 64;
            First->EntrySize = sizeof(TypeRegistryEntry);
        }
    }
}
```

## Memory Overhead

| Registry | Fixed Size | Block Overhead |
|----------|------------|----------------|
| 256 entries × 24 bytes | 6,144 bytes | 6,144 + 24 per block |
| 1024 entries × 40 bytes | 40,960 bytes | Same + grows as needed |

Block overhead is minimal: 24 bytes per block (Next + Used + Capacity + EntrySize).

## Lookup Performance

Linear scan remains O(n), but:
- Good cache locality within blocks
- No worse than current linear scans
- Could add hash table on top for O(1) lookup if needed

## Migration Path

1. Create `BlockAllocator.cs` with generic implementation
2. Add `BlockAllocator.Create/Add/Find` helpers
3. Convert one registry (e.g., TypeRegistry) as proof of concept
4. Measure any performance impact
5. Convert remaining registries
6. Remove old fixed-size limits

## Alternative: Exponential Resize

Instead of blocks, could use realloc-style growth:
- Start with 64 entries
- When full, allocate 128, copy, free old
- Continue doubling

Pros: Better cache locality, simpler iteration
Cons: Requires copy during resize, fragmentation risk
