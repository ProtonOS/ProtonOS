// ProtonOS JIT - Compiled Method Registry
// Tracks compiled methods using a chunked block-based structure.
// Blocks are non-contiguous and allocated on demand - no hard limits.

using ProtonOS.Memory;
using ProtonOS.Platform;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Return type classification for calling convention purposes.
/// </summary>
public enum ReturnKind : byte
{
    Void = 0,       // No return value
    Int32 = 1,      // 32-bit integer (returned in EAX, zero-extended in RAX)
    Int64 = 2,      // 64-bit integer (returned in RAX)
    IntPtr = 3,     // Native pointer (returned in RAX)
    Float32 = 4,    // Single-precision float (returned in XMM0)
    Float64 = 5,    // Double-precision float (returned in XMM0)
    Struct = 6,     // Value type (may use hidden return buffer)
}

/// <summary>
/// Argument type classification for calling convention purposes.
/// </summary>
public enum ArgKind : byte
{
    Int32 = 0,      // 32-bit integer (RCX/RDX/R8/R9 or stack)
    Int64 = 1,      // 64-bit integer
    IntPtr = 2,     // Native pointer
    Float32 = 3,    // Single-precision float (XMM0-XMM3 or stack)
    Float64 = 4,    // Double-precision float
    ByRef = 5,      // Managed reference (pointer)
    Struct = 6,     // Value type (passed by value or hidden pointer)
}

/// <summary>
/// Information about a compiled method, sufficient for generating call sites.
/// </summary>
public unsafe struct CompiledMethodInfo
{
    /// <summary>Metadata token that identifies this method (0 = unused slot).</summary>
    public uint Token;

    /// <summary>Pointer to native code entry point.</summary>
    public void* NativeCode;

    /// <summary>Number of parameters (not including 'this' for instance methods).</summary>
    public byte ArgCount;

    /// <summary>Return type classification.</summary>
    public ReturnKind ReturnKind;

    /// <summary>True if this is an instance method (has implicit 'this').</summary>
    public bool HasThis;

    /// <summary>True if the method has been compiled.</summary>
    public bool IsCompiled;

    /// <summary>True if this is a virtual method requiring vtable dispatch.</summary>
    public bool IsVirtual;

    /// <summary>
    /// Vtable slot index for virtual methods. -1 if not virtual or unknown.
    /// </summary>
    public short VtableSlot;

    /// <summary>
    /// MethodTable pointer for constructors. Used by newobj to allocate the type.
    /// Null for non-constructor methods.
    /// </summary>
    public void* MethodTable;

    /// <summary>True if this is an interface method requiring interface dispatch.</summary>
    public bool IsInterfaceMethod;

    /// <summary>
    /// MethodTable pointer for the interface (only valid if IsInterfaceMethod).
    /// Used for interface dispatch to look up the correct vtable slot.
    /// </summary>
    public void* InterfaceMT;

    /// <summary>
    /// Method index within the interface (0-based, only valid if IsInterfaceMethod).
    /// Combined with InterfaceMT to find the vtable slot at runtime.
    /// </summary>
    public short InterfaceMethodSlot;

    /// <summary>
    /// Argument types for the first 8 arguments.
    /// Packed: 4 bits per arg, args 0-1 in byte 0, args 2-3 in byte 1, etc.
    /// </summary>
    public fixed byte ArgTypes[4];  // 8 args * 4 bits = 32 bits = 4 bytes

    /// <summary>
    /// Get the ArgKind for a specific argument index.
    /// </summary>
    public ArgKind GetArgKind(int index)
    {
        if (index < 0 || index >= 8)
            return ArgKind.Int64;  // Default for args beyond our tracking

        int byteIndex = index / 2;
        int nibbleShift = (index % 2) * 4;

        fixed (byte* ptr = ArgTypes)
        {
            return (ArgKind)((ptr[byteIndex] >> nibbleShift) & 0x0F);
        }
    }

    /// <summary>
    /// Set the ArgKind for a specific argument index.
    /// </summary>
    public void SetArgKind(int index, ArgKind kind)
    {
        if (index < 0 || index >= 8)
            return;

        int byteIndex = index / 2;
        int nibbleShift = (index % 2) * 4;
        byte mask = (byte)(0x0F << nibbleShift);
        byte value = (byte)((byte)kind << nibbleShift);

        fixed (byte* ptr = ArgTypes)
        {
            ptr[byteIndex] = (byte)((ptr[byteIndex] & ~mask) | value);
        }
    }

    /// <summary>Check if this slot is in use.</summary>
    public bool IsUsed => Token != 0;
}

/// <summary>
/// Header at the start of each method block.
/// </summary>
public struct MethodBlockHeader
{
    /// <summary>
    /// Index of next free entry in this block (0-255), or 0xFF if block is full.
    /// </summary>
    public byte NextFreeIndex;

    /// <summary>
    /// Number of entries currently in use in this block.
    /// </summary>
    public byte UsedCount;

    /// <summary>Reserved for future use.</summary>
    public ushort Reserved1;

    /// <summary>Reserved for future use.</summary>
    public uint Reserved2;
}

/// <summary>
/// A single block of method entries.
/// </summary>
public unsafe struct MethodBlock
{
    public const int EntriesPerBlock = 256;

    public MethodBlockHeader Header;
    // Entries follow immediately after header in memory
    // We access them via pointer arithmetic

    /// <summary>
    /// Get pointer to entries array (immediately after header).
    /// </summary>
    public CompiledMethodInfo* GetEntries()
    {
        fixed (MethodBlockHeader* headerPtr = &Header)
        {
            return (CompiledMethodInfo*)((byte*)headerPtr + sizeof(MethodBlockHeader));
        }
    }

    /// <summary>
    /// Calculate total block size in bytes.
    /// </summary>
    public static int BlockSize => sizeof(MethodBlockHeader) + EntriesPerBlock * sizeof(CompiledMethodInfo);

    /// <summary>
    /// Check if block has any free entries.
    /// </summary>
    public bool HasFreeSlot => Header.NextFreeIndex != 0xFF;

    /// <summary>
    /// Check if block is completely empty.
    /// </summary>
    public bool IsEmpty => Header.UsedCount == 0;
}

/// <summary>
/// Registry of compiled methods for the JIT.
/// Uses chunked block-based storage for unlimited growth without reallocation.
/// </summary>
public static unsafe class CompiledMethodRegistry
{
    private const int InitialBlockPointers = 8;  // Start with capacity for 2048 methods

    // Array of pointers to method blocks
    private static MethodBlock** _blocks;
    private static int _blockCount;       // Number of allocated blocks
    private static int _blockCapacity;    // Size of _blocks array
    private static int _totalCount;       // Total methods registered
    private static bool _initialized;

    /// <summary>
    /// Initialize the registry. Must be called before use.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Allocate initial block pointer array
        _blockCapacity = InitialBlockPointers;
        _blocks = (MethodBlock**)HeapAllocator.Alloc((ulong)(_blockCapacity * sizeof(MethodBlock*)));

        if (_blocks == null)
        {
            DebugConsole.WriteLine("[JIT Registry] Failed to allocate block pointer array");
            return;
        }

        // Zero-initialize block pointers
        for (int i = 0; i < _blockCapacity; i++)
        {
            _blocks[i] = null;
        }

        _blockCount = 0;
        _totalCount = 0;
        _initialized = true;

        DebugConsole.Write("[JIT Registry] Initialized (");
        DebugConsole.WriteDecimal((uint)_blockCapacity);
        DebugConsole.Write(" block slots, ");
        DebugConsole.WriteDecimal((uint)(_blockCapacity * MethodBlock.EntriesPerBlock));
        DebugConsole.WriteLine(" max methods before growth)");
    }

    /// <summary>
    /// Register a compiled method.
    /// </summary>
    public static bool Register(uint token, void* code, byte argCount, ReturnKind returnKind, bool hasThis = false)
    {
        return RegisterVirtual(token, code, argCount, returnKind, hasThis, false, -1);
    }

    /// <summary>
    /// Register a compiled virtual method with vtable slot information.
    /// </summary>
    /// <param name="token">Metadata token identifying the method.</param>
    /// <param name="code">Native code entry point.</param>
    /// <param name="argCount">Number of parameters (not including 'this').</param>
    /// <param name="returnKind">Return type classification.</param>
    /// <param name="hasThis">True if this is an instance method.</param>
    /// <param name="isVirtual">True if this is a virtual method.</param>
    /// <param name="vtableSlot">Vtable slot index for virtual methods (-1 if not virtual).</param>
    public static bool RegisterVirtual(uint token, void* code, byte argCount, ReturnKind returnKind,
        bool hasThis, bool isVirtual, int vtableSlot)
    {
        if (!_initialized)
            Initialize();

        if (token == 0)
            return false;  // Token 0 is reserved as "unused"

        // Check if already registered - update if so
        CompiledMethodInfo* existing = Lookup(token);
        if (existing != null)
        {
            existing->NativeCode = code;
            existing->ArgCount = argCount;
            existing->ReturnKind = returnKind;
            existing->HasThis = hasThis;
            existing->IsCompiled = true;
            existing->IsVirtual = isVirtual;
            existing->VtableSlot = (short)vtableSlot;
            return true;
        }

        // Find a block with free space
        int blockIndex = FindBlockWithFreeSlot();
        if (blockIndex < 0)
        {
            // Need to allocate a new block
            blockIndex = AllocateNewBlock();
            if (blockIndex < 0)
                return false;
        }

        // Get the block and allocate an entry
        MethodBlock* block = _blocks[blockIndex];
        CompiledMethodInfo* entries = block->GetEntries();

        byte slotIndex = block->Header.NextFreeIndex;
        CompiledMethodInfo* entry = &entries[slotIndex];

        // Fill in the entry
        entry->Token = token;
        entry->NativeCode = code;
        entry->ArgCount = argCount;
        entry->ReturnKind = returnKind;
        entry->HasThis = hasThis;
        entry->IsCompiled = true;
        entry->IsVirtual = isVirtual;
        entry->VtableSlot = (short)vtableSlot;

        // Clear arg types
        for (int i = 0; i < 4; i++)
            entry->ArgTypes[i] = 0;

        // Update block header
        block->Header.UsedCount++;
        UpdateNextFreeIndex(block);

        _totalCount++;
        return true;
    }

    /// <summary>
    /// Register a constructor method with its MethodTable.
    /// Used by newobj to allocate the correct type then call the constructor.
    /// </summary>
    /// <param name="token">Metadata token identifying the constructor.</param>
    /// <param name="code">Native code entry point.</param>
    /// <param name="argCount">Number of parameters (not including 'this').</param>
    /// <param name="methodTable">MethodTable of the declaring type (used for allocation).</param>
    public static bool RegisterConstructor(uint token, void* code, byte argCount, void* methodTable)
    {
        if (!_initialized)
            Initialize();

        if (token == 0)
            return false;

        // Check if already registered - update if so
        CompiledMethodInfo* existing = Lookup(token);
        if (existing != null)
        {
            existing->NativeCode = code;
            existing->ArgCount = argCount;
            existing->ReturnKind = ReturnKind.Void;  // Constructors return void
            existing->HasThis = true;                 // Constructors always have 'this'
            existing->IsCompiled = true;
            existing->IsVirtual = false;
            existing->VtableSlot = -1;
            existing->MethodTable = methodTable;
            return true;
        }

        // Find a block with free space
        int blockIndex = FindBlockWithFreeSlot();
        if (blockIndex < 0)
        {
            blockIndex = AllocateNewBlock();
            if (blockIndex < 0)
                return false;
        }

        // Get the block and allocate an entry
        MethodBlock* block = _blocks[blockIndex];
        CompiledMethodInfo* entries = block->GetEntries();

        byte slotIndex = block->Header.NextFreeIndex;
        CompiledMethodInfo* entry = &entries[slotIndex];

        // Fill in the entry
        entry->Token = token;
        entry->NativeCode = code;
        entry->ArgCount = argCount;
        entry->ReturnKind = ReturnKind.Void;  // Constructors return void
        entry->HasThis = true;                 // Constructors always have 'this'
        entry->IsCompiled = true;
        entry->IsVirtual = false;
        entry->VtableSlot = -1;
        entry->MethodTable = methodTable;

        // Clear arg types
        for (int i = 0; i < 4; i++)
            entry->ArgTypes[i] = 0;

        // Update block header
        block->Header.UsedCount++;
        UpdateNextFreeIndex(block);

        _totalCount++;
        return true;
    }

    /// <summary>
    /// Register an interface method requiring interface dispatch.
    /// </summary>
    /// <param name="token">Metadata token identifying the interface method.</param>
    /// <param name="argCount">Number of parameters (not including 'this').</param>
    /// <param name="returnKind">Return type classification.</param>
    /// <param name="interfaceMT">MethodTable of the interface.</param>
    /// <param name="interfaceMethodSlot">Method index within the interface (0-based).</param>
    public static bool RegisterInterface(uint token, byte argCount, ReturnKind returnKind,
        void* interfaceMT, int interfaceMethodSlot)
    {
        if (!_initialized)
            Initialize();

        if (token == 0)
            return false;

        // Check if already registered - update if so
        CompiledMethodInfo* existing = Lookup(token);
        if (existing != null)
        {
            existing->NativeCode = null;  // No direct code - uses runtime dispatch
            existing->ArgCount = argCount;
            existing->ReturnKind = returnKind;
            existing->HasThis = true;  // Interface methods always have 'this'
            existing->IsCompiled = true;
            existing->IsVirtual = false;
            existing->VtableSlot = -1;
            existing->IsInterfaceMethod = true;
            existing->InterfaceMT = interfaceMT;
            existing->InterfaceMethodSlot = (short)interfaceMethodSlot;
            return true;
        }

        // Find a block with free space
        int blockIndex = FindBlockWithFreeSlot();
        if (blockIndex < 0)
        {
            blockIndex = AllocateNewBlock();
            if (blockIndex < 0)
                return false;
        }

        // Get the block and allocate an entry
        MethodBlock* block = _blocks[blockIndex];
        CompiledMethodInfo* entries = block->GetEntries();

        byte slotIndex = block->Header.NextFreeIndex;
        CompiledMethodInfo* entry = &entries[slotIndex];

        // Fill in the entry
        entry->Token = token;
        entry->NativeCode = null;  // No direct code - uses runtime dispatch
        entry->ArgCount = argCount;
        entry->ReturnKind = returnKind;
        entry->HasThis = true;  // Interface methods always have 'this'
        entry->IsCompiled = true;
        entry->IsVirtual = false;
        entry->VtableSlot = -1;
        entry->IsInterfaceMethod = true;
        entry->InterfaceMT = interfaceMT;
        entry->InterfaceMethodSlot = (short)interfaceMethodSlot;

        // Clear arg types
        for (int i = 0; i < 4; i++)
            entry->ArgTypes[i] = 0;

        // Update block header
        block->Header.UsedCount++;
        UpdateNextFreeIndex(block);

        _totalCount++;
        return true;
    }

    /// <summary>
    /// Register a method with full argument type information.
    /// </summary>
    public static bool RegisterWithArgTypes(uint token, void* code, byte argCount,
        ReturnKind returnKind, bool hasThis, ArgKind* argKinds)
    {
        if (!Register(token, code, argCount, returnKind, hasThis))
            return false;

        // Find the entry we just registered and set arg types
        CompiledMethodInfo* entry = Lookup(token);
        if (entry != null && argKinds != null)
        {
            int maxArgs = argCount > 8 ? 8 : argCount;
            for (int i = 0; i < maxArgs; i++)
            {
                entry->SetArgKind(i, argKinds[i]);
            }
        }

        return true;
    }

    /// <summary>
    /// Look up a method by token.
    /// </summary>
    public static CompiledMethodInfo* Lookup(uint token)
    {
        if (!_initialized || token == 0)
            return null;

        // Scan all blocks for the token
        for (int b = 0; b < _blockCount; b++)
        {
            MethodBlock* block = _blocks[b];
            if (block == null || block->IsEmpty)
                continue;

            CompiledMethodInfo* entries = block->GetEntries();
            for (int i = 0; i < MethodBlock.EntriesPerBlock; i++)
            {
                if (entries[i].Token == token)
                    return &entries[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Get the native code address for a method.
    /// </summary>
    public static void* GetNativeCode(uint token)
    {
        CompiledMethodInfo* info = Lookup(token);
        if (info != null && info->IsCompiled)
            return info->NativeCode;
        return null;
    }

    /// <summary>
    /// Reserve a slot for a method that will be compiled later.
    /// Used for forward references in recursive or mutually-recursive methods.
    /// </summary>
    public static bool Reserve(uint token, byte argCount, ReturnKind returnKind, bool hasThis = false)
    {
        if (!_initialized)
            Initialize();

        if (token == 0)
            return false;

        // Check if already registered
        if (Lookup(token) != null)
            return true;  // Already exists

        // Find a block with free space
        int blockIndex = FindBlockWithFreeSlot();
        if (blockIndex < 0)
        {
            blockIndex = AllocateNewBlock();
            if (blockIndex < 0)
                return false;
        }

        // Get the block and allocate an entry
        MethodBlock* block = _blocks[blockIndex];
        CompiledMethodInfo* entries = block->GetEntries();

        byte slotIndex = block->Header.NextFreeIndex;
        CompiledMethodInfo* entry = &entries[slotIndex];

        // Fill in the entry (not yet compiled)
        entry->Token = token;
        entry->NativeCode = null;
        entry->ArgCount = argCount;
        entry->ReturnKind = returnKind;
        entry->HasThis = hasThis;
        entry->IsCompiled = false;
        entry->IsVirtual = false;
        entry->VtableSlot = -1;

        // Update block header
        block->Header.UsedCount++;
        UpdateNextFreeIndex(block);

        _totalCount++;
        return true;
    }

    /// <summary>
    /// Update a reserved entry with the compiled code address.
    /// </summary>
    public static bool Complete(uint token, void* code)
    {
        CompiledMethodInfo* entry = Lookup(token);
        if (entry == null)
            return false;

        entry->NativeCode = code;
        entry->IsCompiled = true;
        return true;
    }

    /// <summary>
    /// Remove a method from the registry (for unloading).
    /// </summary>
    public static bool Remove(uint token)
    {
        if (!_initialized || token == 0)
            return false;

        // Find the entry
        for (int b = 0; b < _blockCount; b++)
        {
            MethodBlock* block = _blocks[b];
            if (block == null || block->IsEmpty)
                continue;

            CompiledMethodInfo* entries = block->GetEntries();
            for (int i = 0; i < MethodBlock.EntriesPerBlock; i++)
            {
                if (entries[i].Token == token)
                {
                    // Clear the entry
                    entries[i] = default;

                    // Update block header
                    block->Header.UsedCount--;

                    // If this slot is before NextFreeIndex, update it
                    if (i < block->Header.NextFreeIndex || block->Header.NextFreeIndex == 0xFF)
                    {
                        block->Header.NextFreeIndex = (byte)i;
                    }

                    _totalCount--;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Get the total number of registered methods.
    /// </summary>
    public static int Count => _totalCount;

    /// <summary>
    /// Get the number of allocated blocks.
    /// </summary>
    public static int BlockCount => _blockCount;

    /// <summary>
    /// Debug: Print registry statistics.
    /// </summary>
    public static void DumpStats()
    {
        DebugConsole.Write("[JIT Registry] ");
        DebugConsole.WriteDecimal((uint)_totalCount);
        DebugConsole.Write(" methods in ");
        DebugConsole.WriteDecimal((uint)_blockCount);
        DebugConsole.Write(" blocks (capacity ");
        DebugConsole.WriteDecimal((uint)_blockCapacity);
        DebugConsole.WriteLine(" blocks)");

        for (int b = 0; b < _blockCount; b++)
        {
            MethodBlock* block = _blocks[b];
            if (block != null)
            {
                DebugConsole.Write("  Block ");
                DebugConsole.WriteDecimal((uint)b);
                DebugConsole.Write(": ");
                DebugConsole.WriteDecimal(block->Header.UsedCount);
                DebugConsole.Write("/256 used, nextFree=");
                if (block->Header.NextFreeIndex == 0xFF)
                    DebugConsole.Write("FULL");
                else
                    DebugConsole.WriteDecimal(block->Header.NextFreeIndex);
                DebugConsole.WriteLine();
            }
        }
    }

    /// <summary>
    /// Debug: Print all registered methods.
    /// </summary>
    public static void DumpMethods()
    {
        DebugConsole.Write("[JIT Registry] ");
        DebugConsole.WriteDecimal((uint)_totalCount);
        DebugConsole.WriteLine(" methods:");

        for (int b = 0; b < _blockCount; b++)
        {
            MethodBlock* block = _blocks[b];
            if (block == null || block->IsEmpty)
                continue;

            CompiledMethodInfo* entries = block->GetEntries();
            for (int i = 0; i < MethodBlock.EntriesPerBlock; i++)
            {
                if (entries[i].IsUsed)
                {
                    DebugConsole.Write("  0x");
                    DebugConsole.WriteHex(entries[i].Token);
                    DebugConsole.Write(" -> 0x");
                    DebugConsole.WriteHex((ulong)entries[i].NativeCode);
                    DebugConsole.Write(" args=");
                    DebugConsole.WriteDecimal(entries[i].ArgCount);
                    DebugConsole.Write(" ret=");
                    DebugConsole.WriteDecimal((uint)entries[i].ReturnKind);
                    if (!entries[i].IsCompiled)
                        DebugConsole.Write(" (pending)");
                    if (entries[i].HasThis)
                        DebugConsole.Write(" (instance)");
                    DebugConsole.WriteLine();
                }
            }
        }
    }

    // === Private helpers ===

    /// <summary>
    /// Find index of a block with at least one free slot.
    /// </summary>
    private static int FindBlockWithFreeSlot()
    {
        for (int i = 0; i < _blockCount; i++)
        {
            if (_blocks[i] != null && _blocks[i]->HasFreeSlot)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Allocate a new block and add it to the registry.
    /// </summary>
    private static int AllocateNewBlock()
    {
        // Check if we need to grow the block pointer array
        if (_blockCount >= _blockCapacity)
        {
            if (!GrowBlockPointerArray())
                return -1;
        }

        // Allocate the block
        MethodBlock* block = (MethodBlock*)HeapAllocator.Alloc((ulong)MethodBlock.BlockSize);
        if (block == null)
        {
            DebugConsole.WriteLine("[JIT Registry] Failed to allocate method block");
            return -1;
        }

        // Initialize block header
        block->Header.NextFreeIndex = 0;
        block->Header.UsedCount = 0;
        block->Header.Reserved1 = 0;
        block->Header.Reserved2 = 0;

        // Zero-initialize all entries
        CompiledMethodInfo* entries = block->GetEntries();
        for (int i = 0; i < MethodBlock.EntriesPerBlock; i++)
        {
            entries[i] = default;
        }

        // Add to block array
        int blockIndex = _blockCount;
        _blocks[blockIndex] = block;
        _blockCount++;

        return blockIndex;
    }

    /// <summary>
    /// Grow the block pointer array (doubles capacity).
    /// </summary>
    private static bool GrowBlockPointerArray()
    {
        int newCapacity = _blockCapacity * 2;

        MethodBlock** newBlocks = (MethodBlock**)HeapAllocator.Alloc((ulong)(newCapacity * sizeof(MethodBlock*)));
        if (newBlocks == null)
        {
            DebugConsole.WriteLine("[JIT Registry] Failed to grow block pointer array");
            return false;
        }

        // Copy existing pointers
        for (int i = 0; i < _blockCount; i++)
        {
            newBlocks[i] = _blocks[i];
        }

        // Zero-initialize new slots
        for (int i = _blockCount; i < newCapacity; i++)
        {
            newBlocks[i] = null;
        }

        // Free old array and update
        HeapAllocator.Free(_blocks);
        _blocks = newBlocks;
        _blockCapacity = newCapacity;

        DebugConsole.Write("[JIT Registry] Grew block array to ");
        DebugConsole.WriteDecimal((uint)_blockCapacity);
        DebugConsole.Write(" slots (");
        DebugConsole.WriteDecimal((uint)(_blockCapacity * MethodBlock.EntriesPerBlock));
        DebugConsole.WriteLine(" max methods)");

        return true;
    }

    /// <summary>
    /// Update NextFreeIndex after an entry is allocated or freed.
    /// Scans forward from current position to find next free slot.
    /// </summary>
    private static void UpdateNextFreeIndex(MethodBlock* block)
    {
        CompiledMethodInfo* entries = block->GetEntries();

        // Start from current NextFreeIndex and scan forward
        for (int i = block->Header.NextFreeIndex; i < MethodBlock.EntriesPerBlock; i++)
        {
            if (!entries[i].IsUsed)
            {
                block->Header.NextFreeIndex = (byte)i;
                return;
            }
        }

        // No free slots found
        block->Header.NextFreeIndex = 0xFF;
    }
}
