// ProtonOS - Multi-Assembly Loader
// Manages loading, tracking, and unloading of .NET assemblies.
// Each assembly gets its own context (metadata, type registry, static storage).

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;
using ProtonOS.Runtime.Reflection;

namespace ProtonOS.Runtime;

/// <summary>
/// Lifecycle flags for a loaded assembly.
/// Note: No [Flags] attribute in minimal korlib - treat as flags manually.
/// </summary>
public enum AssemblyFlags : uint
{
    None = 0,
    /// <summary>Assembly binary loaded, metadata parsed.</summary>
    Loaded = 1,
    /// <summary>TypeRef/AssemblyRef references resolved to other loaded assemblies.</summary>
    ReferencesResolved = 2,
    /// <summary>Type initializers (.cctor) have been run.</summary>
    Initialized = 4,
    /// <summary>This assembly can be unloaded (drivers, plugins).</summary>
    Unloadable = 8,
    /// <summary>This is the core library (korlib/mscorlib).</summary>
    CoreLib = 16,
    /// <summary>This is the kernel assembly (AOT compiled).</summary>
    Kernel = 32,
}

/// <summary>
/// Per-assembly type registry mapping TypeDef tokens to MethodTable pointers.
/// Each LoadedAssembly has its own TypeRegistry to avoid token collisions.
/// Uses block allocator for dynamic growth - no fixed limit.
/// </summary>
public unsafe struct TypeRegistry
{
    // Block size for type registry - small to exercise growth during tests
    public const int TypeBlockSize = 32;

    /// <summary>Block chain for type entries.</summary>
    public BlockChain Chain;

    /// <summary>Owner assembly ID.</summary>
    public uint AssemblyId;

    /// <summary>True if initialized.</summary>
    public bool Initialized;

    /// <summary>Number of registered types.</summary>
    public int Count
    {
        get
        {
            fixed (BlockChain* chainPtr = &Chain)
            {
                return chainPtr->TotalCount;
            }
        }
    }

    /// <summary>
    /// Initialize the type registry (allocates first block).
    /// </summary>
    public bool Initialize(uint assemblyId)
    {
        AssemblyId = assemblyId;
        fixed (BlockChain* chainPtr = &Chain)
        {
            if (!BlockAllocator.Init(chainPtr, sizeof(TypeRegistryEntry), TypeBlockSize))
                return false;
        }
        Initialized = true;
        return true;
    }

    /// <summary>
    /// Register a type token to MethodTable mapping.
    /// Also registers with ReflectionRuntime for reverse lookup support.
    /// </summary>
    public bool Register(uint token, MethodTable* mt)
    {
        if (!Initialized || mt == null)
            return false;

        // Debug: log registrations for assembly 5
        if (AssemblyId == 5)
        {
            DebugConsole.Write("[TypeReg.Register] asm=");
            DebugConsole.WriteDecimal(AssemblyId);
            DebugConsole.Write(" token=0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" MT=0x");
            DebugConsole.WriteHex((ulong)mt);
            DebugConsole.WriteLine();
        }

        fixed (BlockChain* chainPtr = &Chain)
        {
            // Check if already registered - iterate through all blocks
            var block = chainPtr->First;
            while (block != null)
            {
                var entries = (TypeRegistryEntry*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    if (entries[i].Token == token)
                    {
                        entries[i].MT = mt;  // Update existing
                        ReflectionRuntime.RegisterTypeInfo(AssemblyId, token, mt);
                        return true;
                    }
                }
                block = block->Next;
            }

            // Add new entry
            TypeRegistryEntry newEntry;
            newEntry.Token = token;
            newEntry.MT = mt;

            byte* result = BlockAllocator.Add(chainPtr, &newEntry);
            if (result == null)
                return false;

            // Register with ReflectionRuntime for reverse lookup (MT* -> assembly/token)
            ReflectionRuntime.RegisterTypeInfo(AssemblyId, token, mt);
            return true;
        }
    }

    /// <summary>
    /// Look up a MethodTable by token.
    /// </summary>
    public MethodTable* Lookup(uint token)
    {
        if (!Initialized)
            return null;

        fixed (BlockChain* chainPtr = &Chain)
        {
            var block = chainPtr->First;
            while (block != null)
            {
                var entries = (TypeRegistryEntry*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    if (entries[i].Token == token)
                        return entries[i].MT;
                }
                block = block->Next;
            }
        }
        return null;
    }

    /// <summary>
    /// Reverse lookup - find a token by MethodTable pointer.
    /// </summary>
    public uint FindTokenByMT(MethodTable* mt)
    {
        if (!Initialized || mt == null)
            return 0;

        fixed (BlockChain* chainPtr = &Chain)
        {
            var block = chainPtr->First;
            while (block != null)
            {
                var entries = (TypeRegistryEntry*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    if (entries[i].MT == mt)
                        return entries[i].Token;
                }
                block = block->Next;
            }
        }
        return 0;
    }

    /// <summary>
    /// Free the registry storage.
    /// Note: Block allocator memory is not freed (no deallocation support yet).
    /// </summary>
    public void Free()
    {
        // Block allocator doesn't support deallocation currently
        // Just mark as uninitialized
        Initialized = false;
    }
}

/// <summary>
/// Per-assembly static field storage.
/// Each LoadedAssembly has its own storage block for clean unloading.
/// Uses block allocator for field registry - no fixed limit.
/// </summary>
public unsafe struct StaticFieldStorage
{
    public const int StorageBlockSize = 64 * 1024;  // 64KB per assembly
    public const int FieldBlockSize = 32;  // Small block size to exercise growth

    /// <summary>Allocated storage block.</summary>
    public byte* Storage;

    /// <summary>Bytes used in storage block.</summary>
    public int Used;

    /// <summary>Owner assembly ID.</summary>
    public uint AssemblyId;

    /// <summary>Block chain for field tracking entries.</summary>
    public BlockChain FieldChain;

    /// <summary>True if initialized.</summary>
    public bool Initialized;

    /// <summary>Number of registered static fields.</summary>
    public int FieldCount
    {
        get
        {
            fixed (BlockChain* chainPtr = &FieldChain)
            {
                return chainPtr->TotalCount;
            }
        }
    }

    /// <summary>
    /// Initialize the static field storage (allocates on first use).
    /// </summary>
    public bool Initialize(uint assemblyId)
    {
        AssemblyId = assemblyId;
        Used = 0;
        Storage = null;  // Allocated on demand

        fixed (BlockChain* chainPtr = &FieldChain)
        {
            if (!BlockAllocator.Init(chainPtr, sizeof(StaticFieldEntry), FieldBlockSize))
                return false;
        }
        Initialized = true;
        return true;
    }

    /// <summary>
    /// Allocate storage for a static field.
    /// </summary>
    public void* Allocate(int size, int alignment = 8)
    {
        // Allocate storage block on demand
        if (Storage == null)
        {
            Storage = (byte*)HeapAllocator.AllocZeroed(StorageBlockSize);
            if (Storage == null)
                return null;
            Used = 0;
        }

        // Align the offset
        int alignedOffset = (Used + alignment - 1) & ~(alignment - 1);

        // Check if we have space
        if (alignedOffset + size > StorageBlockSize)
            return null;

        void* addr = Storage + alignedOffset;
        Used = alignedOffset + size;
        return addr;
    }

    /// <summary>
    /// Register a static field.
    /// For generic types, each instantiation gets its own storage.
    /// </summary>
    public void* Register(uint token, uint typeToken, int size, bool isGCRef)
    {
        if (!Initialized)
            return null;

        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = JIT.MetadataIntegration.GetTypeTypeArgHash();

        fixed (BlockChain* chainPtr = &FieldChain)
        {
            // Check if already registered - iterate through all blocks
            var block = chainPtr->First;
            while (block != null)
            {
                var fields = (StaticFieldEntry*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    // For generic statics, must match both token AND type arg hash
                    if (fields[i].Token == token && fields[i].TypeArgHash == typeArgHash)
                        return fields[i].Address;
                }
                block = block->Next;
            }

            // Allocate storage
            void* addr = Allocate(size);
            if (addr == null)
                return null;

            // Register new field entry
            StaticFieldEntry newEntry;
            newEntry.Token = token;
            newEntry.TypeToken = typeToken;
            newEntry.TypeArgHash = typeArgHash;
            newEntry.Address = addr;
            newEntry.Size = size;
            newEntry.IsGCRef = isGCRef;

            byte* result = BlockAllocator.Add(chainPtr, &newEntry);
            if (result == null)
                return null;

            return addr;
        }
    }

    /// <summary>
    /// Look up a static field's address.
    /// For generic types, uses the current type arg context to find the right instantiation.
    /// </summary>
    public void* Lookup(uint token)
    {
        if (!Initialized)
            return null;

        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = JIT.MetadataIntegration.GetTypeTypeArgHash();

        fixed (BlockChain* chainPtr = &FieldChain)
        {
            var block = chainPtr->First;
            while (block != null)
            {
                var fields = (StaticFieldEntry*)block->Data;
                for (int i = 0; i < block->Used; i++)
                {
                    // For generic statics, must match both token AND type arg hash
                    if (fields[i].Token == token && fields[i].TypeArgHash == typeArgHash)
                        return fields[i].Address;
                }
                block = block->Next;
            }
        }
        return null;
    }

    /// <summary>
    /// Free all storage.
    /// </summary>
    public void Free()
    {
        if (Storage != null)
        {
            HeapAllocator.Free(Storage);
            Storage = null;
        }
        // Block allocator doesn't support deallocation currently
        Initialized = false;
        Used = 0;
    }
}

/// <summary>
/// Represents a loaded .NET assembly with all its metadata and runtime state.
/// </summary>
public unsafe struct LoadedAssembly
{
    /// <summary>Maximum length of assembly name (UTF-8).</summary>
    public const int MaxNameLength = 64;

    /// <summary>Maximum length of version string.</summary>
    public const int MaxVersionLength = 16;

    /// <summary>Maximum number of assembly dependencies.</summary>
    public const int MaxDependencies = 32;

    // ============================================================================
    // Identity
    // ============================================================================

    /// <summary>Unique assembly ID assigned by the loader.</summary>
    public uint AssemblyId;

    /// <summary>Assembly simple name (UTF-8, null-terminated).</summary>
    public fixed byte Name[MaxNameLength];

    /// <summary>Version string "major.minor.build.revision".</summary>
    public fixed byte Version[MaxVersionLength];

    // ============================================================================
    // Binary Data
    // ============================================================================

    /// <summary>PE file bytes in memory (persists after UEFI exit).</summary>
    public byte* ImageBase;

    /// <summary>Size of PE file in bytes.</summary>
    public ulong ImageSize;

    // ============================================================================
    // Parsed Metadata
    // ============================================================================

    /// <summary>Parsed metadata streams (pointers into ImageBase).</summary>
    public MetadataRoot Metadata;

    /// <summary>#~ tables header.</summary>
    public TablesHeader Tables;

    /// <summary>Computed index sizes.</summary>
    public TableSizes Sizes;

    // ============================================================================
    // Runtime State (allocated separately)
    // ============================================================================

    /// <summary>Per-assembly type registry.</summary>
    public TypeRegistry Types;

    /// <summary>Per-assembly static field storage.</summary>
    public StaticFieldStorage Statics;

    // ============================================================================
    // Dependencies
    // ============================================================================

    /// <summary>Number of assemblies this assembly references.</summary>
    public ushort DependencyCount;

    /// <summary>Assembly IDs of referenced assemblies (resolved).</summary>
    public fixed uint Dependencies[MaxDependencies];

    // ============================================================================
    // Flags
    // ============================================================================

    /// <summary>Lifecycle flags.</summary>
    public AssemblyFlags Flags;

    // ============================================================================
    // Helper Methods
    // ============================================================================

    /// <summary>Check if this slot is in use.</summary>
    public bool IsLoaded => (Flags & AssemblyFlags.Loaded) != 0;

    /// <summary>Check if this is the core library.</summary>
    public bool IsCoreLib => (Flags & AssemblyFlags.CoreLib) != 0;

    /// <summary>Check if this assembly can be unloaded.</summary>
    public bool CanUnload => (Flags & AssemblyFlags.Unloadable) != 0;

    /// <summary>
    /// Set the assembly name from a null-terminated string.
    /// </summary>
    public void SetName(byte* name)
    {
        if (name == null)
            return;

        fixed (byte* dst = Name)
        {
            int i = 0;
            while (i < MaxNameLength - 1 && name[i] != 0)
            {
                dst[i] = name[i];
                i++;
            }
            dst[i] = 0;
        }
    }

    /// <summary>
    /// Set the version string.
    /// </summary>
    public void SetVersion(ushort major, ushort minor, ushort build, ushort revision)
    {
        fixed (byte* dst = Version)
        {
            // Simple version string formatting
            int pos = 0;
            pos = WriteDecimalToBuffer(dst, pos, major);
            dst[pos++] = (byte)'.';
            pos = WriteDecimalToBuffer(dst, pos, minor);
            dst[pos++] = (byte)'.';
            pos = WriteDecimalToBuffer(dst, pos, build);
            dst[pos++] = (byte)'.';
            pos = WriteDecimalToBuffer(dst, pos, revision);
            dst[pos] = 0;
        }
    }

    private static int WriteDecimalToBuffer(byte* buf, int pos, ushort value)
    {
        if (value == 0)
        {
            buf[pos++] = (byte)'0';
            return pos;
        }

        // Write digits in reverse, then reverse them
        int start = pos;
        while (value > 0)
        {
            buf[pos++] = (byte)('0' + (value % 10));
            value /= 10;
        }

        // Reverse the digits
        int end = pos - 1;
        while (start < end)
        {
            byte tmp = buf[start];
            buf[start] = buf[end];
            buf[end] = tmp;
            start++;
            end--;
        }

        return pos;
    }

    /// <summary>
    /// Print the assembly name to debug console.
    /// </summary>
    public void PrintName()
    {
        fixed (byte* name = Name)
        {
            for (int i = 0; i < MaxNameLength && name[i] != 0; i++)
            {
                DebugConsole.WriteChar((char)name[i]);
            }
        }
    }

    /// <summary>
    /// Initialize runtime structures for this assembly.
    /// </summary>
    public bool InitializeRuntime()
    {
        if (!Types.Initialize(AssemblyId))
            return false;
        if (!Statics.Initialize(AssemblyId))
            return false;
        return true;
    }

    /// <summary>
    /// Free all resources associated with this assembly.
    /// </summary>
    public void Free()
    {
        Types.Free();
        Statics.Free();
        // Note: ImageBase is typically not freed (UEFI-allocated or permanent)
        Flags = AssemblyFlags.None;
        AssemblyId = 0;
    }
}

/// <summary>
/// Global assembly loader - manages all loaded assemblies.
/// </summary>
public static unsafe class AssemblyLoader
{
    /// <summary>Maximum number of loaded assemblies.</summary>
    public const int MaxAssemblies = 32;

    /// <summary>Special assembly IDs.</summary>
    public const uint InvalidAssemblyId = 0;
    public const uint CoreLibAssemblyId = 1;
    public const uint KernelAssemblyId = 2;

    private static LoadedAssembly* _assemblies;
    private static int _assemblyCount;
    private static uint _nextAssemblyId;
    private static bool _initialized;

    // ============================================================================
    // Initialization
    // ============================================================================

    /// <summary>
    /// Initialize the assembly loader.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _assemblies = (LoadedAssembly*)HeapAllocator.AllocZeroed(
            (ulong)(MaxAssemblies * sizeof(LoadedAssembly)));
        if (_assemblies == null)
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to allocate assembly array");
            return;
        }

        _assemblyCount = 0;
        _nextAssemblyId = 1;  // Start at 1 (0 is invalid)
        _initialized = true;

        DebugConsole.WriteLine("[AsmLoader] Initialized assembly loader");
    }

    // ============================================================================
    // Loading
    // ============================================================================

    /// <summary>
    /// Load an assembly from PE bytes.
    /// Returns the assembly ID, or 0 on failure.
    /// </summary>
    public static uint Load(byte* peBytes, ulong size, AssemblyFlags extraFlags = AssemblyFlags.None)
    {
        if (!_initialized)
            Initialize();

        if (peBytes == null || size < 64)
        {
            DebugConsole.WriteLine("[AsmLoader] Invalid PE bytes");
            return InvalidAssemblyId;
        }

        // Verify PE signature
        if (peBytes[0] != 'M' || peBytes[1] != 'Z')
        {
            DebugConsole.WriteLine("[AsmLoader] Invalid PE signature (not MZ)");
            return InvalidAssemblyId;
        }

        // Find a free slot
        int slot = -1;
        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (!_assemblies[i].IsLoaded)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
        {
            DebugConsole.WriteLine("[AsmLoader] No free assembly slots");
            return InvalidAssemblyId;
        }

        // Assign ID
        uint assemblyId = _nextAssemblyId++;
        LoadedAssembly* asm = &_assemblies[slot];

        // Clear the slot
        *asm = default;
        asm->AssemblyId = assemblyId;
        asm->ImageBase = peBytes;
        asm->ImageSize = size;

        // Get CLI header
        var corHeader = PEHelper.GetCorHeaderFromFile(peBytes);
        if (corHeader == null)
        {
            DebugConsole.WriteLine("[AsmLoader] No CLI header found");
            return InvalidAssemblyId;
        }

        // Get metadata root
        byte* metadataRoot = (byte*)PEHelper.GetMetadataRootFromFile(peBytes);
        if (metadataRoot == null)
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to locate metadata");
            return InvalidAssemblyId;
        }

        // Verify BSJB signature
        uint signature = *(uint*)metadataRoot;
        if (signature != PEConstants.METADATA_SIGNATURE)
        {
            DebugConsole.Write("[AsmLoader] Invalid metadata signature: 0x");
            DebugConsole.WriteHex(signature);
            DebugConsole.WriteLine();
            return InvalidAssemblyId;
        }

        // Parse metadata streams
        if (!MetadataReader.Init(metadataRoot, corHeader->MetaData.Size, out asm->Metadata))
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to parse metadata streams");
            return InvalidAssemblyId;
        }
        // Debug: log blob heap for each assembly
        DebugConsole.Write("[AsmLoader] Assembly ID ");
        DebugConsole.WriteDecimal(assemblyId);
        DebugConsole.Write(" blob heap at 0x");
        DebugConsole.WriteHex((ulong)asm->Metadata.BlobHeap);
        DebugConsole.Write(" size=");
        DebugConsole.WriteDecimal(asm->Metadata.BlobHeapSize);
        // Check bytes at indices 0x44 and 0x4D for size=8480 (korlib)
        if (asm->Metadata.BlobHeapSize == 8480)
        {
            DebugConsole.Write(" [0x44]=0x");
            DebugConsole.WriteHex(asm->Metadata.BlobHeap[0x44]);
            DebugConsole.Write(" [0x4D]=0x");
            DebugConsole.WriteHex(asm->Metadata.BlobHeap[0x4D]);
        }
        DebugConsole.WriteLine("");

        // Parse tables header
        if (!MetadataReader.ParseTablesHeader(ref asm->Metadata, out asm->Tables))
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to parse #~ header");
            return InvalidAssemblyId;
        }

        // Calculate table sizes
        asm->Sizes = TableSizes.Calculate(ref asm->Tables);

        // Extract assembly name and version from Assembly table (0x20)
        ExtractAssemblyIdentity(asm);

        // Initialize runtime structures
        if (!asm->InitializeRuntime())
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to initialize runtime structures");
            return InvalidAssemblyId;
        }

        // Mark as loaded
        asm->Flags = AssemblyFlags.Loaded | extraFlags;
        _assemblyCount++;

        DebugConsole.Write("[AsmLoader] Loaded assembly ");
        asm->PrintName();
        DebugConsole.Write(" (ID ");
        DebugConsole.WriteDecimal(assemblyId);
        DebugConsole.WriteLine(")");

        return assemblyId;
    }

    /// <summary>
    /// Extract assembly name and version from the Assembly table.
    /// </summary>
    private static void ExtractAssemblyIdentity(LoadedAssembly* asm)
    {
        uint assemblyRowCount = asm->Tables.RowCounts[(int)MetadataTableId.Assembly];
        if (assemblyRowCount == 0)
        {
            // No Assembly table - this might be a module, not an assembly
            // Use a default name - access fixed buffer directly through pointer
            byte* name = asm->Name;
            name[0] = (byte)'<';
            name[1] = (byte)'u';
            name[2] = (byte)'n';
            name[3] = (byte)'k';
            name[4] = (byte)'n';
            name[5] = (byte)'o';
            name[6] = (byte)'w';
            name[7] = (byte)'n';
            name[8] = (byte)'>';
            name[9] = 0;
            return;
        }

        // Get name from row 1 of Assembly table
        uint nameIdx = MetadataReader.GetAssemblyName(ref asm->Tables, ref asm->Sizes, 1);
        byte* namePtr = MetadataReader.GetString(ref asm->Metadata, nameIdx);
        asm->SetName(namePtr);

        // Get version
        ushort major, minor, build, revision;
        MetadataReader.GetAssemblyVersion(ref asm->Tables, ref asm->Sizes, 1,
            out major, out minor, out build, out revision);
        asm->SetVersion(major, minor, build, revision);
    }

    // ============================================================================
    // Lookup
    // ============================================================================

    /// <summary>
    /// Get a loaded assembly by ID.
    /// </summary>
    public static LoadedAssembly* GetAssembly(uint assemblyId)
    {
        if (!_initialized || assemblyId == InvalidAssemblyId)
            return null;

        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (_assemblies[i].IsLoaded && _assemblies[i].AssemblyId == assemblyId)
                return &_assemblies[i];
        }
        return null;
    }

    /// <summary>
    /// Find a loaded assembly by name.
    /// </summary>
    public static LoadedAssembly* FindByName(byte* name)
    {
        if (!_initialized || name == null)
            return null;

        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (!_assemblies[i].IsLoaded)
                continue;

            // Compare names - access fixed buffer directly
            byte* asmName = _assemblies[i].Name;
            bool match = true;
            for (int j = 0; j < LoadedAssembly.MaxNameLength; j++)
            {
                if (asmName[j] != name[j])
                {
                    match = false;
                    break;
                }
                if (asmName[j] == 0)
                    break;
            }
            if (match)
                return &_assemblies[i];
        }
        return null;
    }

    /// <summary>
    /// Get the count of loaded assemblies.
    /// </summary>
    public static int GetAssemblyCount() => _assemblyCount;

    // ============================================================================
    // Type Resolution
    // ============================================================================

    /// <summary>
    /// Resolve a type token within an assembly.
    /// For TypeDef: looks up in the assembly's type registry.
    /// For TypeRef: resolves to the target assembly's TypeDef.
    /// </summary>
    public static MethodTable* ResolveType(uint assemblyId, uint token)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return null;

        // Extract table type from token
        byte tableId = (byte)(token >> 24);

        switch (tableId)
        {
            case 0x02:  // TypeDef - look up in this assembly's registry
            {
                MethodTable* mt = asm->Types.Lookup(token);
                if (mt != null)
                    return mt;
                // Not registered yet - create on-demand
                return CreateTypeDefMethodTable(asm, token);
            }

            case 0x01:  // TypeRef - resolve cross-assembly
                return ResolveTypeRef(asm, token);

            case 0x1B:  // TypeSpec - array types, generic instantiations
                return ResolveTypeSpec(asm, token);

            case 0xF0:  // Well-known type - look up in MetadataIntegration registry
                return JIT.MetadataIntegration.LookupType(token);

            default:
                return null;
        }
    }

    /// <summary>
    /// Resolve a TypeSpec token (array types, generics, etc.).
    /// TypeSpec contains a signature blob describing the type.
    /// </summary>
    private static MethodTable* ResolveTypeSpec(LoadedAssembly* asm, uint token)
    {
        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0 || rowId > asm->Tables.RowCounts[(int)MetadataTableId.TypeSpec])
        {
            DebugConsole.Write("[AsmLoader] TypeSpec 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(" invalid row");
            return null;
        }

        // Get the TypeSpec signature blob
        uint sigIdx = MetadataReader.GetTypeSpecSignature(ref asm->Tables, ref asm->Sizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen == 0)
        {
            DebugConsole.Write("[AsmLoader] TypeSpec 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" asm=");
            DebugConsole.WriteDecimal(asm->AssemblyId);
            DebugConsole.Write(" row=");
            DebugConsole.WriteDecimal(rowId);
            DebugConsole.Write(" blobIdx=");
            DebugConsole.WriteHex(sigIdx);
            DebugConsole.Write(" blobHeap=");
            DebugConsole.WriteHex((ulong)asm->Metadata.BlobHeap);
            DebugConsole.Write(" blobSize=");
            DebugConsole.WriteDecimal(asm->Metadata.BlobHeapSize);
            DebugConsole.WriteLine(" no signature");
            return null;
        }

        // TypeSpec signature debug (verbose - commented out)
        // DebugConsole.Write("[AsmLoader] TypeSpec sig[0]=0x");
        // DebugConsole.WriteHex((uint)sig[0]);
        // DebugConsole.Write(" sig[1]=0x");
        // DebugConsole.WriteHex((uint)sig[1]);
        // DebugConsole.Write(" len=");
        // DebugConsole.WriteDecimal(sigLen);
        // DebugConsole.WriteLine("");

        int pos = 0;
        byte elementType = sig[pos++];

        DebugConsole.Write("[AsmLoader] ResolveTypeSpec 0x");
        DebugConsole.WriteHex(token);
        DebugConsole.Write(" elemType=0x");
        DebugConsole.WriteHex(elementType);
        DebugConsole.WriteLine();

        // ELEMENT_TYPE_SZARRAY = 0x1D - single-dimension zero-lower-bound array
        if (elementType == 0x1D)
        {
            DebugConsole.Write("[AsmLoader] TypeSpec SZARRAY: parsing element type, next byte=0x");
            if (pos < (int)sigLen)
                DebugConsole.WriteHex(sig[pos]);
            DebugConsole.WriteLine();

            // Next is the element type
            MethodTable* elementMT = ParseTypeFromSignature(asm, sig, ref pos, sigLen);
            if (elementMT == null)
            {
                DebugConsole.WriteLine("[AsmLoader] TypeSpec array element MT is null");
                return null;
            }

            DebugConsole.Write("[AsmLoader] TypeSpec SZARRAY: element MT=0x");
            DebugConsole.WriteHex((ulong)elementMT);
            DebugConsole.WriteLine();

            // Get or create array MethodTable
            return GetOrCreateArrayMethodTable(elementMT);
        }

        // ELEMENT_TYPE_PTR = 0x0F - pointer type (e.g., void*)
        // Pointers are IntPtr-sized, so use IntPtr's MethodTable
        if (elementType == 0x0F)
        {
            // Skip the pointed-to type (we don't need it for sizing)
            // Just need to know it's a pointer, which is pointer-sized (IntPtr)
            MethodTable* ptrMt = GetPrimitiveMethodTable(0x18);  // IntPtr
            if (ptrMt == null)
            {
                DebugConsole.WriteLine("[AsmLoader] TypeSpec PTR: IntPtr MT not found");
                return null;
            }
            // DebugConsole.WriteLine("[AsmLoader] TypeSpec PTR resolved to IntPtr");
            return ptrMt;
        }

        // ELEMENT_TYPE_MVAR = 0x1E - method type variable (generic method parameter)
        // Used when a TypeSpec references a generic method's type parameter
        if (elementType == 0x1E)
        {
            // Read the method type parameter index (compressed uint)
            uint index = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    index = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    index = ((uint)(b & 0x3F) << 8) | sig[pos++];
                else if ((b & 0xE0) == 0xC0 && pos + 2 < (int)sigLen)
                {
                    index = ((uint)(b & 0x1F) << 24) | ((uint)sig[pos++] << 16) | ((uint)sig[pos++] << 8) | sig[pos++];
                }
            }

            // Check if we have a method type argument context
            if (!JIT.MetadataIntegration.HasMethodTypeArgContext())
            {
                DebugConsole.Write("[AsmLoader] TypeSpec MVAR !!");
                DebugConsole.WriteDecimal(index);
                DebugConsole.WriteLine(" but no MethodSpec context");
                return null;
            }

            // Get the MethodTable directly from the method type arg context
            // This was already computed during SetupMethodTypeArgs with proper MT lookup
            MethodTable* mvarMt = JIT.MetadataIntegration.GetMethodTypeArgMethodTable((int)index);
            if (mvarMt != null)
            {
                return mvarMt;
            }

            // Fallback: get element type and try to map to primitive MT
            byte mvarElemType = JIT.MetadataIntegration.GetMethodTypeArgElementType((int)index);
            if (mvarElemType == 0)
            {
                DebugConsole.Write("[AsmLoader] TypeSpec MVAR !!");
                DebugConsole.WriteDecimal(index);
                DebugConsole.WriteLine(" - index out of range");
                return null;
            }

            // Try primitive MT lookup as fallback
            mvarMt = GetPrimitiveMethodTable(mvarElemType);
            if (mvarMt != null)
            {
                return mvarMt;
            }

            // Not a primitive - for CLASS (0x12) or other reference types, use Object
            if (mvarElemType == 0x12 || mvarElemType == 0x1C || mvarElemType == 0x0E)
            {
                return GetPrimitiveMethodTable(0x1C);  // Object
            }

            DebugConsole.Write("[AsmLoader] TypeSpec MVAR !!");
            DebugConsole.WriteDecimal(index);
            DebugConsole.Write(" unknown elemType 0x");
            DebugConsole.WriteHex(mvarElemType);
            DebugConsole.WriteLine();
            return null;
        }

        // ELEMENT_TYPE_VAR = 0x13 - type type variable (generic type parameter)
        // Similar to MVAR but for generic types instead of generic methods
        if (elementType == 0x13)
        {
            // Read the type parameter index (compressed uint)
            uint index = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    index = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    index = ((uint)(b & 0x3F) << 8) | sig[pos++];
            }

            // Try to get the actual type argument from the type context
            MethodTable* varMt = JIT.MetadataIntegration.GetTypeTypeArgMethodTable((int)index);
            if (varMt != null)
            {
                DebugConsole.Write("[AsmLoader] TypeSpec VAR index=");
                DebugConsole.WriteDecimal(index);
                DebugConsole.Write(" resolved to MT=0x");
                DebugConsole.WriteHex((ulong)varMt);
                DebugConsole.WriteLine();
                return varMt;
            }

            // Fallback to Object for unresolved VAR
            DebugConsole.Write("[AsmLoader] TypeSpec VAR index=");
            DebugConsole.WriteDecimal(index);
            DebugConsole.WriteLine(" NO TYPE CONTEXT! using Object");
            return GetPrimitiveMethodTable(0x1C);  // Object
        }

        // ELEMENT_TYPE_GENERICINST = 0x15 - generic type instantiation
        // Format: 0x15 (CLASS=0x12 | VALUETYPE=0x11) TypeDefOrRefOrSpec GenArgCount Type1 Type2 ...
        if (elementType == 0x15)
        {
            if (pos >= (int)sigLen)
            {
                DebugConsole.WriteLine("[AsmLoader] TypeSpec GENERICINST: unexpected end of signature");
                return null;
            }

            // Read CLASS (0x12) or VALUETYPE (0x11)
            byte classOrValueType = sig[pos++];
            bool isValueType = (classOrValueType == 0x11);

            // Decode the generic type definition token (TypeDefOrRefOrSpec)
            uint genericTypeToken = DecodeTypeDefOrRefOrSpec(sig, ref pos, sigLen);
            if (genericTypeToken == 0)
            {
                DebugConsole.WriteLine("[AsmLoader] TypeSpec GENERICINST: failed to decode generic type token");
                return null;
            }

            // Normalize the generic type token to a canonical form for cache consistency
            // TypeRef tokens need to be resolved to TypeDef tokens
            uint normalizedGenericToken = NormalizeGenericDefToken(asm, genericTypeToken);
            if (normalizedGenericToken == 0)
            {
                // Fall back to original token if normalization fails
                normalizedGenericToken = genericTypeToken;
            }

            // Read the generic argument count (compressed uint)
            uint genArgCount = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    genArgCount = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    genArgCount = ((uint)(b & 0x3F) << 8) | sig[pos++];
            }

            // Parse the type arguments and create a proper GenericInst MethodTable
            if (genArgCount > 0 && genArgCount <= 8)
            {
                MethodTable** typeArgMTs = stackalloc MethodTable*[(int)genArgCount];
                bool allResolved = true;

                for (uint i = 0; i < genArgCount && pos < (int)sigLen; i++)
                {
                    // Debug: peek at the element type before parsing
                    byte peekElem = sig[pos];
                    typeArgMTs[i] = ParseTypeFromSignature(asm, sig, ref pos, sigLen);
                    if (typeArgMTs[i] == null)
                    {
                        DebugConsole.Write("[AsmLoader] TypeSpec GENERICINST: failed to resolve type arg ");
                        DebugConsole.WriteDecimal(i);
                        DebugConsole.Write(" elemType=0x");
                        DebugConsole.WriteHex(peekElem);
                        DebugConsole.WriteLine();
                        allResolved = false;
                        break;
                    }
                    // DEBUG: trace resolved type args for GenericInst
                    DebugConsole.Write("[GenArg] ");
                    DebugConsole.WriteDecimal(i);
                    DebugConsole.Write(" elemType=0x");
                    DebugConsole.WriteHex(peekElem);
                    DebugConsole.Write(" -> MT=0x");
                    DebugConsole.WriteHex((ulong)typeArgMTs[i]);
                    DebugConsole.WriteLine();
                }

                if (allResolved)
                {
                    // Create instantiated generic type with type arguments
                    // Use normalized token for cache consistency
                    MethodTable* instMT = GetOrCreateGenericInstMethodTable(normalizedGenericToken, typeArgMTs, (int)genArgCount, isValueType);
                    if (instMT != null)
                    {
                        return instMT;
                    }
                }
            }

            // Fallback: use the open generic type's MethodTable
            MethodTable* genericMt = ResolveType(asm->AssemblyId, normalizedGenericToken);
            if (genericMt != null)
            {
                // Skip over any remaining type arguments
                for (uint i = 0; i < genArgCount && pos < (int)sigLen; i++)
                {
                    SkipTypeInSignature(sig, ref pos, sigLen);
                }
                return genericMt;
            }

            // Fallback: for reference types use Object, for value types we need the actual type
            if (!isValueType)
            {
                // Skip type arguments
                for (uint i = 0; i < genArgCount && pos < (int)sigLen; i++)
                {
                    SkipTypeInSignature(sig, ref pos, sigLen);
                }
                return GetPrimitiveMethodTable(0x1C);  // Object
            }

            DebugConsole.Write("[AsmLoader] TypeSpec GENERICINST: failed to resolve generic type 0x");
            DebugConsole.WriteHex(genericTypeToken);
            DebugConsole.WriteLine();
            return null;
        }

        // ELEMENT_TYPE_BYREF = 0x10 - by-reference type
        // Treat as pointer-sized (IntPtr)
        if (elementType == 0x10)
        {
            // Skip the referenced type
            SkipTypeInSignature(sig, ref pos, sigLen);
            return GetPrimitiveMethodTable(0x18);  // IntPtr
        }

        DebugConsole.Write("[AsmLoader] TypeSpec unhandled elementType 0x");
        DebugConsole.WriteHex((uint)elementType);
        DebugConsole.WriteLine("");
        // Other TypeSpec kinds not implemented yet
        return null;
    }

    /// <summary>
    /// Parse a type from a signature blob.
    /// Returns the MethodTable for the parsed type.
    /// </summary>
    private static MethodTable* ParseTypeFromSignature(LoadedAssembly* asm, byte* sig, ref int pos, uint sigLen)
    {
        if (pos >= (int)sigLen)
        {
            DebugConsole.WriteLine("[AsmLoader] ParseType: pos >= sigLen");
            return null;
        }

        byte elementType = sig[pos++];
        // ParseType debug (verbose - commented out)
        // DebugConsole.Write("[AsmLoader] ParseType elementType=0x");
        // DebugConsole.WriteHex((uint)elementType);
        // DebugConsole.WriteLine("");

        // ELEMENT_TYPE_CLASS or ELEMENT_TYPE_VALUETYPE - followed by TypeDefOrRefOrSpecEncoded
        if (elementType == 0x12 || elementType == 0x11)  // CLASS=0x12, VALUETYPE=0x11
        {
            // Decode compressed unsigned integer for TypeDefOrRefOrSpec
            uint typeToken = DecodeTypeDefOrRefOrSpec(sig, ref pos, sigLen);
            // DebugConsole.Write("[AsmLoader] ParseType decoded token=0x");
            // DebugConsole.WriteHex(typeToken);
            // DebugConsole.WriteLine("");
            if (typeToken == 0)
                return null;

            MethodTable* mt = ResolveType(asm->AssemblyId, typeToken);
            if (mt == null)
            {
                DebugConsole.Write("[AsmLoader] ParseType ResolveType failed for 0x");
                DebugConsole.WriteHex(typeToken);
                DebugConsole.WriteLine("");
            }
            return mt;
        }

        // ELEMENT_TYPE_MVAR = 0x1E - method type variable
        if (elementType == 0x1E)
        {
            // Read the method type parameter index
            uint index = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    index = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    index = ((uint)(b & 0x3F) << 8) | sig[pos++];
            }

            if (JIT.MetadataIntegration.HasMethodTypeArgContext())
            {
                byte mvarElemType = JIT.MetadataIntegration.GetMethodTypeArgElementType((int)index);
                if (mvarElemType != 0)
                {
                    MethodTable* mvarMt = GetPrimitiveMethodTable(mvarElemType);
                    if (mvarMt != null)
                        return mvarMt;
                    // Reference types use Object
                    if (mvarElemType == 0x12 || mvarElemType == 0x1C || mvarElemType == 0x0E)
                        return GetPrimitiveMethodTable(0x1C);
                }
            }
            // Fallback to Object for unresolved MVAR
            return GetPrimitiveMethodTable(0x1C);
        }

        // ELEMENT_TYPE_VAR = 0x13 - type type variable
        if (elementType == 0x13)
        {
            // Read the type parameter index (compressed uint)
            uint index = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    index = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    index = ((uint)(b & 0x3F) << 8) | sig[pos++];
            }

            DebugConsole.Write("[AsmLoader] ParseType VAR index=");
            DebugConsole.WriteDecimal(index);

            // Try to get the actual type argument from the type context
            MethodTable* varMt = JIT.MetadataIntegration.GetTypeTypeArgMethodTable((int)index);
            if (varMt != null)
            {
                DebugConsole.Write(" resolved to MT=0x");
                DebugConsole.WriteHex((ulong)varMt);
                DebugConsole.WriteLine();
                return varMt;
            }

            // Fallback to Object for unresolved VAR
            DebugConsole.WriteLine(" NO TYPE CONTEXT! using Object");
            return GetPrimitiveMethodTable(0x1C);  // Object
        }

        // ELEMENT_TYPE_GENERICINST = 0x15 - handle nested generic type instantiation
        // This happens when a type argument is itself a generic type (e.g., List<Dictionary<K,V>>)
        if (elementType == 0x15)
        {
            if (pos >= (int)sigLen)
                return GetPrimitiveMethodTable(0x1C);  // Object fallback

            // Read CLASS (0x12) or VALUETYPE (0x11)
            byte classOrValueType = sig[pos++];
            bool isValueType = (classOrValueType == 0x11);

            // Decode the generic type definition token (TypeDefOrRefOrSpec)
            uint genericTypeToken = DecodeTypeDefOrRefOrSpec(sig, ref pos, sigLen);
            if (genericTypeToken == 0)
                return GetPrimitiveMethodTable(0x1C);  // Object fallback

            // Normalize the generic type token
            uint normalizedGenericToken = NormalizeGenericDefToken(asm, genericTypeToken);
            if (normalizedGenericToken == 0)
                normalizedGenericToken = genericTypeToken;

            // Read the generic argument count (compressed uint)
            uint genArgCount = 0;
            if (pos < (int)sigLen)
            {
                byte b = sig[pos++];
                if ((b & 0x80) == 0)
                    genArgCount = b;
                else if ((b & 0xC0) == 0x80 && pos < (int)sigLen)
                    genArgCount = ((uint)(b & 0x3F) << 8) | sig[pos++];
            }

            // Parse the type arguments recursively
            if (genArgCount > 0 && genArgCount <= 8)
            {
                MethodTable** typeArgMTs = stackalloc MethodTable*[(int)genArgCount];
                bool allResolved = true;

                for (uint i = 0; i < genArgCount && pos < (int)sigLen; i++)
                {
                    typeArgMTs[i] = ParseTypeFromSignature(asm, sig, ref pos, sigLen);
                    if (typeArgMTs[i] == null)
                    {
                        allResolved = false;
                        break;
                    }
                }

                if (allResolved)
                {
                    // Create instantiated generic type
                    MethodTable* instMT = GetOrCreateGenericInstMethodTable(normalizedGenericToken, typeArgMTs, (int)genArgCount, isValueType);
                    if (instMT != null)
                        return instMT;
                }
            }

            // Fallback: skip remaining type args and return open generic type or Object
            for (uint i = 0; i < genArgCount && pos < (int)sigLen; i++)
            {
                SkipTypeInSignature(sig, ref pos, sigLen);
            }
            MethodTable* genericMt = ResolveType(asm->AssemblyId, normalizedGenericToken);
            return genericMt != null ? genericMt : GetPrimitiveMethodTable(0x1C);
        }

        // Primitive types - get AOT MethodTables from registry
        MethodTable* primMt = GetPrimitiveMethodTable(elementType);
        if (primMt == null)
        {
            DebugConsole.Write("[AsmLoader] ParseType no primitive for 0x");
            DebugConsole.WriteHex((uint)elementType);
            DebugConsole.WriteLine("");
        }
        return primMt;
    }

    /// <summary>
    /// Decode a TypeDefOrRefOrSpec coded index from a signature blob.
    /// Returns the full token (0x02xxxxxx for TypeDef, 0x01xxxxxx for TypeRef, 0x1Bxxxxxx for TypeSpec).
    /// </summary>
    private static uint DecodeTypeDefOrRefOrSpec(byte* sig, ref int pos, uint sigLen)
    {
        // First decode compressed unsigned integer
        if (pos >= (int)sigLen)
            return 0;

        uint value = 0;
        byte b = sig[pos++];
        if ((b & 0x80) == 0)
        {
            // 1-byte encoding
            value = b;
        }
        else if ((b & 0xC0) == 0x80)
        {
            // 2-byte encoding
            if (pos >= (int)sigLen) return 0;
            value = ((uint)(b & 0x3F) << 8) | sig[pos++];
        }
        else if ((b & 0xE0) == 0xC0)
        {
            // 4-byte encoding
            if (pos + 2 >= (int)sigLen) return 0;
            value = ((uint)(b & 0x1F) << 24) | ((uint)sig[pos++] << 16) | ((uint)sig[pos++] << 8) | sig[pos++];
        }
        else
        {
            return 0;
        }

        // TypeDefOrRefOrSpec encoding: low 2 bits are table, rest is row ID
        uint table = value & 0x03;
        uint row = value >> 2;

        switch (table)
        {
            case 0: return 0x02000000 | row;  // TypeDef
            case 1: return 0x01000000 | row;  // TypeRef
            case 2: return 0x1B000000 | row;  // TypeSpec
            default: return 0;
        }
    }

    /// <summary>
    /// Normalize a generic type definition token to a canonical form for cache consistency.
    /// TypeRef tokens are resolved to TypeDef tokens to ensure the same type always
    /// maps to the same cache key regardless of how it's referenced.
    /// </summary>
    private static uint NormalizeGenericDefToken(LoadedAssembly* asm, uint token)
    {
        uint table = token >> 24;
        uint row = token & 0x00FFFFFF;

        // TypeDef tokens are already canonical
        if (table == 0x02)
        {
            // Include assembly ID to make it globally unique
            return (asm->AssemblyId << 24) | row;
        }

        // TypeRef - resolve to TypeDef in target assembly
        if (table == 0x01)
        {
            LoadedAssembly* targetAsm = null;
            uint targetToken = 0;
            if (ResolveTypeRefToTypeDef(asm, row, out targetAsm, out targetToken))
            {
                // Return normalized token with target assembly ID
                uint normalized = (targetAsm->AssemblyId << 24) | (targetToken & 0x00FFFFFF);
                DebugConsole.Write("[Normalize] TypeRef row=");
                DebugConsole.WriteDecimal(row);
                DebugConsole.Write(" asm=");
                DebugConsole.WriteDecimal(asm->AssemblyId);
                DebugConsole.Write(" -> 0x");
                DebugConsole.WriteHex(normalized);
                DebugConsole.WriteLine();
                return normalized;
            }
            // Fall through if resolution fails
            DebugConsole.Write("[Normalize] TypeRef row=");
            DebugConsole.WriteDecimal(row);
            DebugConsole.Write(" asm=");
            DebugConsole.WriteDecimal(asm->AssemblyId);
            DebugConsole.WriteLine(" FAILED");
        }

        // TypeSpec or failed resolution - return original with assembly context
        return (asm->AssemblyId << 24) | row;
    }

    /// <summary>
    /// Skip over a type in a signature blob without parsing it.
    /// Used when we need to advance past type arguments we're not using.
    /// </summary>
    private static void SkipTypeInSignature(byte* sig, ref int pos, uint sigLen)
    {
        if (pos >= (int)sigLen)
            return;

        byte elementType = sig[pos++];

        switch (elementType)
        {
            // Primitives - no additional data
            case 0x01: // VOID
            case 0x02: // BOOLEAN
            case 0x03: // CHAR
            case 0x04: // I1
            case 0x05: // U1
            case 0x06: // I2
            case 0x07: // U2
            case 0x08: // I4
            case 0x09: // U4
            case 0x0A: // I8
            case 0x0B: // U8
            case 0x0C: // R4
            case 0x0D: // R8
            case 0x0E: // STRING
            case 0x18: // I (IntPtr)
            case 0x19: // U (UIntPtr)
            case 0x1C: // OBJECT
            case 0x16: // TYPEDBYREF
                break;

            // Types with TypeDefOrRefOrSpec
            case 0x11: // VALUETYPE
            case 0x12: // CLASS
                SkipCompressedUInt(sig, ref pos, sigLen);
                break;

            // Types with a single nested type
            case 0x0F: // PTR
            case 0x10: // BYREF
            case 0x1D: // SZARRAY
            case 0x45: // PINNED
                SkipTypeInSignature(sig, ref pos, sigLen);
                break;

            // VAR and MVAR - followed by compressed uint (index)
            case 0x13: // VAR
            case 0x1E: // MVAR
                SkipCompressedUInt(sig, ref pos, sigLen);
                break;

            // GENERICINST: CLASS/VALUETYPE TypeDefOrRefOrSpec GenArgCount Type*
            case 0x15:
                if (pos < (int)sigLen)
                {
                    pos++; // Skip CLASS/VALUETYPE byte
                    SkipCompressedUInt(sig, ref pos, sigLen); // TypeDefOrRefOrSpec
                    uint genArgCount = ReadCompressedUInt(sig, ref pos, sigLen);
                    for (uint i = 0; i < genArgCount; i++)
                    {
                        SkipTypeInSignature(sig, ref pos, sigLen);
                    }
                }
                break;

            // ARRAY: ElementType Rank NumSizes Size* NumLoBounds LoBound*
            case 0x14:
                SkipTypeInSignature(sig, ref pos, sigLen); // Element type
                uint rank = ReadCompressedUInt(sig, ref pos, sigLen);
                uint numSizes = ReadCompressedUInt(sig, ref pos, sigLen);
                for (uint i = 0; i < numSizes; i++)
                    SkipCompressedUInt(sig, ref pos, sigLen);
                uint numLoBounds = ReadCompressedUInt(sig, ref pos, sigLen);
                for (uint i = 0; i < numLoBounds; i++)
                    SkipCompressedInt(sig, ref pos, sigLen);
                break;

            // FNPTR: MethodSig (complex, skip for now by reading until reasonable end)
            case 0x1B:
                // Function pointer - skip calling convention, param count, return type, params
                if (pos < (int)sigLen)
                {
                    pos++; // calling convention
                    uint paramCount = ReadCompressedUInt(sig, ref pos, sigLen);
                    SkipTypeInSignature(sig, ref pos, sigLen); // return type
                    for (uint i = 0; i < paramCount; i++)
                        SkipTypeInSignature(sig, ref pos, sigLen);
                }
                break;

            // CMOD_REQD, CMOD_OPT: TypeDefOrRefOrSpec followed by Type
            case 0x1F: // CMOD_REQD
            case 0x20: // CMOD_OPT
                SkipCompressedUInt(sig, ref pos, sigLen);
                SkipTypeInSignature(sig, ref pos, sigLen);
                break;

            default:
                // Unknown element type - can't skip safely
                break;
        }
    }

    /// <summary>
    /// Skip a compressed unsigned integer in a signature blob.
    /// </summary>
    private static void SkipCompressedUInt(byte* sig, ref int pos, uint sigLen)
    {
        if (pos >= (int)sigLen)
            return;

        byte b = sig[pos++];
        if ((b & 0x80) == 0)
        {
            // 1-byte encoding - already consumed
        }
        else if ((b & 0xC0) == 0x80)
        {
            // 2-byte encoding
            if (pos < (int)sigLen) pos++;
        }
        else if ((b & 0xE0) == 0xC0)
        {
            // 4-byte encoding
            if (pos + 2 < (int)sigLen) pos += 3;
        }
    }

    /// <summary>
    /// Skip a compressed signed integer in a signature blob.
    /// </summary>
    private static void SkipCompressedInt(byte* sig, ref int pos, uint sigLen)
    {
        // Compressed signed ints use the same encoding as unsigned
        SkipCompressedUInt(sig, ref pos, sigLen);
    }

    /// <summary>
    /// Read a compressed unsigned integer from a signature blob.
    /// </summary>
    private static uint ReadCompressedUInt(byte* sig, ref int pos, uint sigLen)
    {
        if (pos >= (int)sigLen)
            return 0;

        byte b = sig[pos++];
        if ((b & 0x80) == 0)
        {
            return b;
        }
        else if ((b & 0xC0) == 0x80)
        {
            if (pos >= (int)sigLen) return 0;
            return ((uint)(b & 0x3F) << 8) | sig[pos++];
        }
        else if ((b & 0xE0) == 0xC0)
        {
            if (pos + 2 >= (int)sigLen) return 0;
            return ((uint)(b & 0x1F) << 24) | ((uint)sig[pos++] << 16) | ((uint)sig[pos++] << 8) | sig[pos++];
        }
        return 0;
    }

    /// <summary>
    /// Get the MethodTable for a primitive ELEMENT_TYPE code.
    /// Maps ECMA-335 element type codes to registered well-known type MethodTables.
    /// </summary>
    private static MethodTable* GetPrimitiveMethodTable(byte elementType)
    {
        uint token = elementType switch
        {
            0x02 => JIT.MetadataIntegration.WellKnownTypes.Boolean,  // ELEMENT_TYPE_BOOLEAN
            0x03 => JIT.MetadataIntegration.WellKnownTypes.Char,     // ELEMENT_TYPE_CHAR
            0x04 => JIT.MetadataIntegration.WellKnownTypes.SByte,    // ELEMENT_TYPE_I1
            0x05 => JIT.MetadataIntegration.WellKnownTypes.Byte,     // ELEMENT_TYPE_U1
            0x06 => JIT.MetadataIntegration.WellKnownTypes.Int16,    // ELEMENT_TYPE_I2
            0x07 => JIT.MetadataIntegration.WellKnownTypes.UInt16,   // ELEMENT_TYPE_U2
            0x08 => JIT.MetadataIntegration.WellKnownTypes.Int32,    // ELEMENT_TYPE_I4
            0x09 => JIT.MetadataIntegration.WellKnownTypes.UInt32,   // ELEMENT_TYPE_U4
            0x0A => JIT.MetadataIntegration.WellKnownTypes.Int64,    // ELEMENT_TYPE_I8
            0x0B => JIT.MetadataIntegration.WellKnownTypes.UInt64,   // ELEMENT_TYPE_U8
            0x0C => JIT.MetadataIntegration.WellKnownTypes.Single,   // ELEMENT_TYPE_R4
            0x0D => JIT.MetadataIntegration.WellKnownTypes.Double,   // ELEMENT_TYPE_R8
            0x0E => JIT.MetadataIntegration.WellKnownTypes.String,   // ELEMENT_TYPE_STRING
            0x18 => JIT.MetadataIntegration.WellKnownTypes.IntPtr,   // ELEMENT_TYPE_I
            0x19 => JIT.MetadataIntegration.WellKnownTypes.UIntPtr,  // ELEMENT_TYPE_U
            0x1C => JIT.MetadataIntegration.WellKnownTypes.Object,   // ELEMENT_TYPE_OBJECT
            _ => 0
        };

        if (token == 0)
            return null;

        return JIT.MetadataIntegration.LookupType(token);
    }

    /// <summary>
    /// Get variance flags for an interface's generic type parameters.
    /// Returns a packed uint where bits 0-1 are variance for param 0, bits 2-3 for param 1, etc.
    /// Returns 0 if no variance (all invariant).
    /// </summary>
    private static uint GetInterfaceVariance(LoadedAssembly* asm, uint typeDefRowId)
    {
        uint varianceFlags = 0;
        int genericParamCount = (int)asm->Tables.RowCounts[(int)MetadataTableId.GenericParam];

        // Build the TypeOrMethodDef coded index for this TypeDef (tag=0 for TypeDef)
        uint ownerCodedIndex = (typeDefRowId << 1) | 0;  // TypeDef uses tag 0

        // Scan GenericParam rows looking for those owned by this TypeDef
        for (int i = 1; i <= genericParamCount; i++)
        {
            uint owner = MetadataReader.GetGenericParamOwner(ref asm->Tables, ref asm->Sizes, (uint)i, ref asm->Tables);
            if (owner == ownerCodedIndex)
            {
                // This generic param belongs to our type
                ushort paramNumber = MetadataReader.GetGenericParamNumber(ref asm->Tables, ref asm->Sizes, (uint)i);
                ushort paramFlags = MetadataReader.GetGenericParamFlags(ref asm->Tables, ref asm->Sizes, (uint)i);
                ushort variance = (ushort)(paramFlags & MetadataReader.GenericParamVarianceMask);

                if (variance != 0 && paramNumber < 16)  // Max 16 type params
                {
                    varianceFlags |= (uint)(variance << (paramNumber * 2));
                }
            }
        }

        return varianceFlags;
    }

    /// <summary>
    /// Create a MethodTable on-demand for a TypeDef in a JIT-compiled assembly.
    /// This handles types that weren't pre-registered during assembly loading.
    /// Allocates vtable slots and copies base class vtable entries.
    /// </summary>
    private static MethodTable* CreateTypeDefMethodTable(LoadedAssembly* asm, uint token)
    {
        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0 || rowId > asm->Tables.RowCounts[(int)MetadataTableId.TypeDef])
            return null;

        // Get type metadata
        uint typeDefFlags = MetadataReader.GetTypeDefFlags(ref asm->Tables, ref asm->Sizes, rowId);
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, rowId);

        // Check if it's a value type (extends System.ValueType or System.Enum)
        bool isValueType = false;
        bool isDelegate = false;
        bool isInterface = (typeDefFlags & 0x00000020) != 0;  // tdInterface

        if (!isInterface && extendsIdx.RowId != 0)
        {
            // Check if extends System.ValueType or System.Enum
            isValueType = IsValueTypeBase(asm, extendsIdx);
            // Check if extends System.MulticastDelegate
            if (!isValueType)
                isDelegate = IsDelegateBase(asm, extendsIdx);
        }

        // Compute instance size from fields
        uint instanceSize = ComputeInstanceSize(asm, rowId, isValueType);

        // Get base class MethodTable to inherit vtable from
        MethodTable* baseMT = null;
        ushort baseVtableSlots = 0;

        // Debug for korlib classes to trace base class resolution
        bool debugBase = (asm->AssemblyId == 1 && rowId >= 0x40 && rowId <= 0xB0);
        if (debugBase)
        {
            // Get type name
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, rowId);
            byte* typeName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            DebugConsole.Write("[BaseClass] row=0x");
            DebugConsole.WriteHex(rowId);
            DebugConsole.Write(" '");
            if (typeName != null)
            {
                for (int i = 0; i < 30 && typeName[i] != 0; i++)
                    DebugConsole.WriteByte(typeName[i]);
            }
            DebugConsole.Write("' isVT=");
            DebugConsole.Write(isValueType ? "Y" : "N");
            DebugConsole.Write(" isIF=");
            DebugConsole.Write(isInterface ? "Y" : "N");
            DebugConsole.Write(" extRow=");
            DebugConsole.WriteDecimal(extendsIdx.RowId);
            DebugConsole.Write(" extTab=");
            DebugConsole.WriteDecimal((uint)extendsIdx.Table);
            DebugConsole.WriteLine();
        }

        if (!isValueType && !isInterface)
        {
            if (extendsIdx.RowId != 0)
            {
                baseMT = GetBaseClassMethodTable(asm, extendsIdx);
                bool isObj = IsObjectBase(asm, extendsIdx);
                if (debugBase)
                {
                    DebugConsole.Write("[BaseClass] baseMT=0x");
                    DebugConsole.WriteHex((ulong)baseMT);
                    DebugConsole.Write(" isObj=");
                    DebugConsole.Write(isObj ? "Y" : "N");
                    DebugConsole.WriteLine();
                }
                if (baseMT != null)
                {
                    baseVtableSlots = baseMT->_usNumVtableSlots;
                }
                else if (isObj)
                {
                    // System.Object has 3 virtual methods: ToString, Equals, GetHashCode
                    baseVtableSlots = 3;
                }
            }
            else
            {
                // No explicit extends clause - implicitly extends Object
                // System.Object has 3 virtual methods: ToString, Equals, GetHashCode
                if (debugBase)
                {
                    DebugConsole.WriteLine("[BaseClass] No extends -> implicit Object");
                }
                baseVtableSlots = 3;
            }
        }

        // Delegates: AOT MulticastDelegate only has 3 slots but korlib expects 5
        // (3 Object slots + CombineImpl + RemoveImpl)
        if (isDelegate && baseVtableSlots < 5)
        {
            baseVtableSlots = 5;  // Force 5 base slots for delegates
        }

        // Count new virtual slots introduced by this type
        ushort newVirtualSlots = CountNewVirtualSlots(asm, rowId);

        // Count interfaces and interface method slots
        // Include both directly implemented interfaces AND inherited interfaces from base class
        ushort numInterfaces = 0;
        ushort interfaceMethodSlots = 0;
        ushort baseInterfaces = 0;
        if (!isInterface)
        {
            numInterfaces = CountInterfacesForType(asm, rowId);

            // Add inherited interfaces from base class
            if (baseMT != null)
            {
                baseInterfaces = baseMT->_usNumInterfaces;
                numInterfaces += baseInterfaces;
            }

            if (numInterfaces > 0)
            {
                interfaceMethodSlots = CountInterfaceMethodSlots(asm, rowId);
                // Add inherited interface method slots
                if (baseMT != null && baseInterfaces > 0)
                {
                    // The base class already has slots for its interfaces
                    // We need to count them for proper slot allocation
                    interfaceMethodSlots += CountBaseInterfaceMethodSlots(baseMT);
                }
            }
        }

        // Total vtable slots = inherited + max(newVirtualSlots, interfaceMethodSlots)
        // - newVirtualSlots counts methods the class defines (including interface implementations)
        // - interfaceMethodSlots counts all interface methods (including defaults not implemented)
        // Using max() ensures we have enough slots for both cases:
        // - Regular classes: newVirtualSlots covers implementations
        // - Classes with default interface methods: interfaceMethodSlots covers defaults
        ushort extraSlots = newVirtualSlots > interfaceMethodSlots ? newVirtualSlots : interfaceMethodSlots;
        ushort totalVtableSlots = (ushort)(baseVtableSlots + extraSlots);

        // Allocate MethodTable with vtable space AND interface map space
        ulong mtSize = (ulong)(MethodTable.HeaderSize + totalVtableSlots * sizeof(nint) + numInterfaces * InterfaceMapEntry.Size);
        MethodTable* mt = (MethodTable*)HeapAllocator.AllocZeroed(mtSize);
        if (mt == null)
            return null;

        // Initialize the MethodTable
        mt->_usComponentSize = 0;
        ushort flags = 0;
        if (isInterface)
            flags |= (ushort)(MTFlags.IsInterface >> 16);
        if (isValueType)
            flags |= (ushort)(MTFlags.IsValueType >> 16);
        if (isDelegate)
            flags |= (ushort)(MTFlags.IsDelegate >> 16);

        // For interfaces, check for variance and store in _uHashCode
        uint hashOrVariance = token;  // Default to token
        if (isInterface)
        {
            uint variance = GetInterfaceVariance(asm, rowId);
            if (variance != 0)
            {
                flags |= (ushort)(MTFlags.HasVariance >> 16);
                hashOrVariance = variance;  // Store variance flags instead of token
            }
        }

        mt->_usFlags = flags;
        mt->_uBaseSize = instanceSize;
        mt->_relatedType = baseMT;  // Point to base class MT
        mt->_usNumVtableSlots = totalVtableSlots;
        mt->_usNumInterfaces = numInterfaces;
        mt->_uHashCode = hashOrVariance;  // Variance for interfaces with variance, else token

        // Get bitmask of slots that this type overrides - leave those as 0 for lazy JIT
        byte overriddenSlots = GetOverriddenObjectSlots(asm, rowId);

        // Copy base class vtable entries, except for overridden slots
        if (baseMT != null && baseVtableSlots > 0)
        {
            nint* srcVtable = baseMT->GetVtablePtr();
            nint* dstVtable = mt->GetVtablePtr();
            // Only copy up to actual source slots (not forced delegate slots)
            int copySlots = baseMT->_usNumVtableSlots;
            for (int i = 0; i < copySlots; i++)
            {
                // Only copy if this slot is NOT overridden by this type
                if (i < 3 && ((overriddenSlots >> i) & 1) != 0)
                {
                    // This slot is overridden - leave as 0 for lazy JIT
                    continue;
                }
                dstVtable[i] = srcVtable[i];
            }
        }
        else if (baseVtableSlots == 3 || (isDelegate && baseMT == null))
        {
            // Direct Object base - get AOT vtable entries for Object's methods
            // But skip slots that are overridden
            nint* vtable = mt->GetVtablePtr();
            if ((overriddenSlots & 0x01) == 0)  // ToString not overridden
                vtable[0] = AotMethodRegistry.LookupByName("System.Object", "ToString");
            if ((overriddenSlots & 0x02) == 0)  // Equals not overridden
                vtable[1] = AotMethodRegistry.LookupByName("System.Object", "Equals");
            if ((overriddenSlots & 0x04) == 0)  // GetHashCode not overridden
                vtable[2] = AotMethodRegistry.LookupByName("System.Object", "GetHashCode");
        }

        // Delegates: populate slots 3 and 4 with CombineImpl and RemoveImpl from korlib
        // These are virtual methods in korlib's MulticastDelegate that JIT delegates inherit
        if (isDelegate)
        {
            nint* vtable = mt->GetVtablePtr();
            // Slot 3: CombineImpl - from MulticastDelegate
            vtable[3] = AotMethodRegistry.LookupByName("System.MulticastDelegate", "CombineImpl");
            // Slot 4: RemoveImpl - from MulticastDelegate
            vtable[4] = AotMethodRegistry.LookupByName("System.MulticastDelegate", "RemoveImpl");
        }

        DebugConsole.Write("[CreateMT] token=0x");
        DebugConsole.WriteHex(token);
        DebugConsole.Write(" base=");
        DebugConsole.WriteDecimal(baseVtableSlots);
        DebugConsole.Write(" new=");
        DebugConsole.WriteDecimal(newVirtualSlots);
        DebugConsole.Write(" iface=");
        DebugConsole.WriteDecimal(interfaceMethodSlots);
        DebugConsole.Write(" total=");
        DebugConsole.WriteDecimal(totalVtableSlots);
        DebugConsole.WriteLine();

        // Register in the assembly's type registry first (needed for JIT compilation)
        asm->Types.Register(token, mt);

        // Register override methods in CompiledMethodRegistry for lazy JIT lookup
        // This allows EnsureVtableSlotCompiled to find and compile them on first call
        // Always call this since there may be overrides of user-defined virtual methods
        // (not just Object methods tracked in overriddenSlots)
        RegisterOverrideMethodsForLazyJit(asm, rowId, mt, overriddenSlots);

        // Populate interface map BEFORE registering new virtual methods
        // This is needed so that FindExplicitInterfaceSlot can look up interface slots
        // for explicit interface implementations
        if (numInterfaces > 0)
        {
            // Interface methods start at baseVtableSlots (after Object methods)
            // For types like ValueImpl : IValue, the GetValue() method is at slot 3
            // (after ToString=0, Equals=1, GetHashCode=2)
            PopulateInterfaceMap(asm, rowId, mt, baseVtableSlots, baseMT);
        }

        // Register new virtual methods (newslot) for lazy JIT compilation
        // These are methods introduced by this type (like interface implementations)
        // Now that interface map is populated, explicit interface implementations
        // can be registered at the correct interface slots
        if (newVirtualSlots > 0)
        {
            RegisterNewVirtualMethodsForLazyJit(asm, rowId, mt, baseVtableSlots);
        }

        return mt;
    }

    /// <summary>
    /// Find the interface slot for an explicit interface implementation method.
    /// Detects explicit implementations by method name (containing interface prefix, e.g. "IEnumerable.GetEnumerator")
    /// or via MethodImpl table entries.
    /// Returns the vtable slot if this method implements an interface method, or -1 if not.
    /// </summary>
    /// <param name="asm">The assembly containing the type</param>
    /// <param name="typeDefRow">The TypeDef row ID</param>
    /// <param name="methodRow">The MethodDef row ID to check</param>
    /// <param name="mt">The MethodTable to look up interface slots from</param>
    /// <returns>The vtable slot for the interface method, or -1 if not an explicit implementation</returns>
    private static short FindExplicitInterfaceSlot(LoadedAssembly* asm, uint typeDefRow, uint methodRow, MethodTable* mt)
    {
        // Get the method name
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);
        if (methodName == null)
            return -1;

        // Check if this is an explicit interface implementation by name pattern
        // Explicit implementations have names like "System.Collections.Generic.IEnumerable<T>.GetEnumerator"
        // We detect this by looking for '.' followed by interface method name at the end
        // Find the last '.' to extract the method name part
        int len = 0;
        int lastDot = -1;
        for (int i = 0; methodName[i] != 0; i++)
        {
            if (methodName[i] == '.')
                lastDot = i;
            len++;
        }

        // If no dot or dot at start/end, not an explicit implementation
        if (lastDot <= 0 || lastDot >= len - 1)
        {
            // Also try MethodImpl table as fallback
            return FindExplicitInterfaceSlotViaMethodImpl(asm, typeDefRow, methodRow, mt);
        }

        // Extract the interface prefix (everything before the last dot)
        byte* interfacePrefix = methodName;
        int prefixLen = lastDot;

        // Extract the simple method name (everything after the last dot)
        byte* simpleMethodName = methodName + lastDot + 1;

        // Search through the type's interface map to find a matching interface
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int numInterfaces = mt->_usNumInterfaces;

        for (int i = 0; i < numInterfaces; i++)
        {
            MethodTable* interfaceMT = map[i].InterfaceMT;
            if (interfaceMT == null)
                continue;

            // Get the interface type name to compare with the prefix
            uint ifAsmId, ifTypeToken;
            Reflection.ReflectionRuntime.LookupTypeInfo(interfaceMT, out ifAsmId, out ifTypeToken);
            if (ifAsmId == 0 || ifTypeToken == 0)
                continue;

            LoadedAssembly* ifAsm = GetAssembly(ifAsmId);
            if (ifAsm == null)
                continue;

            uint ifTypeDefRow = ifTypeToken & 0x00FFFFFF;
            uint ifNameIdx = MetadataReader.GetTypeDefName(ref ifAsm->Tables, ref ifAsm->Sizes, ifTypeDefRow);
            uint ifNsIdx = MetadataReader.GetTypeDefNamespace(ref ifAsm->Tables, ref ifAsm->Sizes, ifTypeDefRow);
            byte* ifName = MetadataReader.GetString(ref ifAsm->Metadata, ifNameIdx);
            byte* ifNs = MetadataReader.GetString(ref ifAsm->Metadata, ifNsIdx);

            // Build the full interface name to compare
            // For generic interfaces, the name in the method might include type params like "IEnumerable<T>"
            // But the TypeDef name is "IEnumerable`1"
            // We need to match flexibly
            if (InterfaceNameMatchesPrefix(ifNs, ifName, interfacePrefix, prefixLen))
            {
                // Found matching interface - now find the method index
                short methodIndex = FindMethodIndexInInterface(interfaceMT, simpleMethodName, asm);
                if (methodIndex >= 0)
                {
                    return (short)(map[i].StartSlot + methodIndex);
                }
            }
        }

        // Fallback to MethodImpl table
        return FindExplicitInterfaceSlotViaMethodImpl(asm, typeDefRow, methodRow, mt);
    }

    /// <summary>
    /// Check if an interface name matches a method name prefix.
    /// Handles generic types where the prefix might be "IEnumerable<T>" but the type name is "IEnumerable`1".
    /// </summary>
    private static bool InterfaceNameMatchesPrefix(byte* ifNs, byte* ifName, byte* prefix, int prefixLen)
    {
        // Build expected prefix from namespace and name
        // Format: "Namespace.TypeName" or for generics "Namespace.TypeName<T>"

        // First, match the namespace part
        int pos = 0;
        if (ifNs != null && ifNs[0] != 0)
        {
            for (int i = 0; ifNs[i] != 0; i++)
            {
                if (pos >= prefixLen || ifNs[i] != prefix[pos])
                    return false;
                pos++;
            }
            // Expect a dot after namespace
            if (pos >= prefixLen || prefix[pos] != '.')
                return false;
            pos++;
        }

        // Now match the type name part
        // For generic types, ifName might be "IEnumerable`1" but prefix might be "IEnumerable<T>"
        int namePos = 0;
        while (ifName[namePos] != 0 && ifName[namePos] != '`')
        {
            if (pos >= prefixLen || ifName[namePos] != prefix[pos])
                return false;
            pos++;
            namePos++;
        }

        // If we're at a backtick, the prefix might have <...> instead
        if (ifName[namePos] == '`')
        {
            // Skip the generic arity in the type name
            // The prefix should have < at this point
            if (pos < prefixLen && prefix[pos] == '<')
            {
                // Skip to the end of the prefix (we don't validate the generic args)
                return true;
            }
            // Or the prefix might just end here (no generic args in name)
            return pos >= prefixLen;
        }

        // Both should be at the end
        return pos >= prefixLen;
    }

    /// <summary>
    /// Find the interface slot via MethodImpl table (fallback for assemblies that use MethodImpl).
    /// </summary>
    private static short FindExplicitInterfaceSlotViaMethodImpl(LoadedAssembly* asm, uint typeDefRow, uint methodRow, MethodTable* mt)
    {
        uint methodImplCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodImpl];
        if (methodImplCount == 0)
            return -1;

        for (uint i = 1; i <= methodImplCount; i++)
        {
            uint implClass = MetadataReader.GetMethodImplClass(ref asm->Tables, ref asm->Sizes, i);
            if (implClass != typeDefRow)
                continue;

            // Get MethodBody - the implementing method
            uint methodBody = MetadataReader.GetMethodImplMethodBody(ref asm->Tables, ref asm->Sizes, i, ref asm->Tables);

            // MethodDefOrRef coded index: MethodDef=0, MemberRef=1
            uint bodyTag = methodBody & 0x01;
            uint bodyRow = methodBody >> 1;

            // We're looking for MethodDef entries matching our method row
            if (bodyTag == 0 && bodyRow == methodRow)
            {
                // Found a MethodImpl entry for this method
                // Now get the MethodDeclaration (interface method being implemented)
                uint methodDecl = MetadataReader.GetMethodImplMethodDeclaration(ref asm->Tables, ref asm->Sizes, i, ref asm->Tables);
                uint declTag = methodDecl & 0x01;
                uint declRow = methodDecl >> 1;

                // MethodDeclaration is usually a MemberRef pointing to the interface method
                if (declTag == 1)  // MemberRef
                {
                    // Get the interface type from the MemberRef parent
                    CodedIndex classRef = MetadataReader.GetMemberRefClass(
                        ref asm->Tables, ref asm->Sizes, declRow);

                    MethodTable* interfaceMT = null;

                    if (classRef.Table == MetadataTableId.TypeRef)
                    {
                        // Resolve TypeRef to get interface MT
                        LoadedAssembly* targetAsm;
                        uint targetToken;
                        if (ResolveTypeRefToTypeDef(asm, classRef.RowId, out targetAsm, out targetToken) && targetAsm != null)
                        {
                            interfaceMT = targetAsm->Types.Lookup(targetToken);
                        }
                    }
                    else if (classRef.Table == MetadataTableId.TypeSpec)
                    {
                        // Generic interface instantiation
                        uint typeSpecToken = 0x1B000000 | classRef.RowId;
                        interfaceMT = ResolveTypeSpec(asm, typeSpecToken);
                    }
                    else if (classRef.Table == MetadataTableId.TypeDef)
                    {
                        uint ifaceToken = 0x02000000 | classRef.RowId;
                        interfaceMT = asm->Types.Lookup(ifaceToken);
                    }

                    if (interfaceMT != null)
                    {
                        // Find this interface in the MT's interface map and get the slot
                        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
                        int numInterfaces = mt->_usNumInterfaces;

                        for (int j = 0; j < numInterfaces; j++)
                        {
                            if (map[j].InterfaceMT == interfaceMT)
                            {
                                // Found the interface - now find which method within the interface
                                // Get the method name from the MemberRef
                                uint nameIdx = MetadataReader.GetMemberRefName(ref asm->Tables, ref asm->Sizes, declRow);
                                byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

                                // Find this method's index in the interface's method list
                                // For now, use a simple name-based lookup in the interface
                                short methodIndex = FindMethodIndexInInterface(interfaceMT, methodName, asm);
                                if (methodIndex >= 0)
                                {
                                    return (short)(map[j].StartSlot + methodIndex);
                                }
                            }
                        }
                    }
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Find the index of a method by name in an interface's method list.
    /// Also compares parameter count to avoid matching overloaded methods incorrectly.
    /// </summary>
    private static short FindMethodIndexInInterface(MethodTable* interfaceMT, byte* methodName, LoadedAssembly* contextAsm, uint implParamCount = 0xFFFFFFFF)
    {
        // Look up the interface type info to find its methods
        uint asmId, typeToken;
        Reflection.ReflectionRuntime.LookupTypeInfo(interfaceMT, out asmId, out typeToken);

        if (asmId == 0 || typeToken == 0)
            return -1;

        LoadedAssembly* ifaceAsm = GetAssembly(asmId);
        if (ifaceAsm == null)
            return -1;

        uint typeDefRow = typeToken & 0x00FFFFFF;

        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, typeDefRow);
        uint typeDefCount = ifaceAsm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = ifaceAsm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        short index = 0;
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            uint nameIdx = MetadataReader.GetMethodDefName(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, methodRow);
            byte* ifaceMethodName = MetadataReader.GetString(ref ifaceAsm->Metadata, nameIdx);

            if (ifaceMethodName != null && NameEquals(methodName, ifaceMethodName))
            {
                // Name matches - also check parameter count if specified
                if (implParamCount != 0xFFFFFFFF)
                {
                    // Get the interface method's parameter count from its signature
                    uint sigIdx = MetadataReader.GetMethodDefSignature(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, methodRow);
                    byte* sigBlob = MetadataReader.GetBlob(ref ifaceAsm->Metadata, sigIdx, out uint sigLen);
                    if (sigBlob != null && sigLen > 0)
                    {
                        MethodSignature sig;
                        if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out sig))
                        {
                            // Only match if parameter counts are equal
                            if (sig.ParamCount != implParamCount)
                            {
                                // Name matches but param count differs - skip and continue searching
                                ushort mflags = MetadataReader.GetMethodDefFlags(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, methodRow);
                                if ((mflags & MethodDefFlags.Virtual) != 0)
                                {
                                    index++;
                                }
                                continue;
                            }
                        }
                    }
                }
                return index;
            }

            // Only count virtual methods (interface methods are always virtual)
            ushort flags = MetadataReader.GetMethodDefFlags(ref ifaceAsm->Tables, ref ifaceAsm->Sizes, methodRow);
            if ((flags & MethodDefFlags.Virtual) != 0)
            {
                index++;
            }
        }

        return -1;
    }

    /// <summary>
    /// Find the interface slot for an implicit interface implementation.
    /// An implicit implementation is a public method with the same name as an interface method.
    /// Returns -1 if not an implicit interface implementation.
    /// </summary>
    private static short FindImplicitInterfaceSlot(LoadedAssembly* asm, uint typeDefRow, uint methodRow, MethodTable* mt)
    {
        // Get the method name
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);
        if (methodName == null)
            return -1;

        // Check if the method is public (implicit implementations must be public)
        ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);
        if ((methodFlags & MethodDefFlags.MemberAccessMask) != MethodDefFlags.Public)
            return -1;

        // Get the implementation method's parameter count from its signature
        uint implParamCount = 0;
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* sigBlob = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sigBlob != null && sigLen > 0)
        {
            MethodSignature sig;
            if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out sig))
            {
                implParamCount = sig.ParamCount;
            }
        }

        // Search through the type's interface map to find a matching interface method
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int numInterfaces = mt->_usNumInterfaces;

        for (int i = 0; i < numInterfaces; i++)
        {
            MethodTable* interfaceMT = map[i].InterfaceMT;
            if (interfaceMT == null)
                continue;

            // Find the method index in this interface - must match both name AND param count
            short methodIndex = FindMethodIndexInInterface(interfaceMT, methodName, asm, implParamCount);
            if (methodIndex >= 0)
            {
                // Found a match - return the interface slot
                return (short)(map[i].StartSlot + methodIndex);
            }
        }

        return -1;
    }

    /// <summary>
    /// Register new virtual methods (newslot) for lazy JIT compilation.
    /// These are methods introduced by this type that need their own vtable slots.
    /// </summary>
    private static void RegisterNewVirtualMethodsForLazyJit(LoadedAssembly* asm, uint typeDefRow, MethodTable* mt, ushort baseVtableSlots)
    {
        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        // Calculate the first slot after all interface slots
        // Interface slots start at baseVtableSlots and each interface takes up its method count
        short currentSlot = (short)baseVtableSlots;
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int numInterfaces = mt->_usNumInterfaces;
        for (int i = 0; i < numInterfaces; i++)
        {
            if (map[i].InterfaceMT != null)
            {
                ushort interfaceMethodCount = map[i].InterfaceMT->_usNumVtableSlots;
                if (interfaceMethodCount == 0)
                    interfaceMethodCount = 1;  // At least 1 slot per interface
                short endSlot = (short)(map[i].StartSlot + interfaceMethodCount);
                if (endSlot > currentSlot)
                    currentSlot = endSlot;
            }
        }
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            // Look for virtual methods WITH newslot flag (new virtual methods)
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;
            bool isAbstract = (methodFlags & MethodDefFlags.Abstract) != 0;

            if (isVirtual && isNewSlot && !isStatic && !isAbstract)
            {
                uint methodToken = 0x06000000 | methodRow;

                // Check if this is an explicit interface implementation
                // If so, register at the interface slot instead of sequential slot
                short interfaceSlot = FindExplicitInterfaceSlot(asm, typeDefRow, methodRow, mt);
                if (interfaceSlot >= 0)
                {
                    // Explicit interface implementation - register at interface slot
                    JIT.CompiledMethodRegistry.RegisterUncompiledOverride(
                        methodToken, asm->AssemblyId, mt, interfaceSlot);
                    // Don't increment currentSlot - this uses an interface slot
                }
                else
                {
                    // Not an explicit implementation - check for implicit interface implementation
                    short implicitSlot = FindImplicitInterfaceSlot(asm, typeDefRow, methodRow, mt);
                    if (implicitSlot >= 0)
                    {
                        // Implicit interface implementation - register at interface slot
                        JIT.CompiledMethodRegistry.RegisterUncompiledOverride(
                            methodToken, asm->AssemblyId, mt, implicitSlot);
                        // Don't increment currentSlot - this uses an interface slot
                    }
                    else
                    {
                        // Regular new virtual method - register at sequential slot
                        JIT.CompiledMethodRegistry.RegisterUncompiledOverride(
                            methodToken, asm->AssemblyId, mt, currentSlot);
                        currentSlot++;
                    }
                }
            }
            else if (isVirtual && isNewSlot && !isStatic && isAbstract)
            {
                // Abstract methods also consume a slot but can't be compiled
                currentSlot++;
            }
        }
    }

    /// <summary>
    /// Register override methods in CompiledMethodRegistry so JitStubs can find them
    /// when the vtable slot is accessed and needs lazy compilation.
    /// </summary>
    private static void RegisterOverrideMethodsForLazyJit(LoadedAssembly* asm, uint typeDefRow, MethodTable* mt, byte overriddenSlots)
    {
        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            // Look for virtual methods that are NOT newslot (i.e., overrides)
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;

            if (isVirtual && !isNewSlot && !isStatic)
            {
                // This is an override - determine vtable slot
                uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
                byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

                short vtableSlot = -1;

                // For Object method overrides, we can only determine the slot by signature
                // since methods like Equals/GetHashCode can exist with multiple signatures
                // Object.ToString() -> slot 0 (no params, returns String)
                // Object.Equals(object) -> slot 1 (1 param, param is Object)
                // Object.GetHashCode() -> slot 2 (no params, returns int)

                // Get the method signature to determine param count
                uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRow);
                byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
                int paramCount = 0;
                if (sig != null && sigLen > 1)
                {
                    // Skip calling convention byte, decode param count
                    int pos = 1;
                    byte b = sig[pos++];
                    if ((b & 0x80) == 0)
                        paramCount = b;
                }

                if (NameEquals(methodName, "ToString") && paramCount == 0)
                    vtableSlot = 0;
                else if (NameEquals(methodName, "Equals") && paramCount == 1)
                {
                    // Object.Equals(object) has 1 param - need to check if it's Object type
                    // For now, assume if no base class slot found, try slot 1
                    vtableSlot = FindVtableSlotInBaseClass(asm, typeDefRow, methodName);
                    if (vtableSlot < 0)
                        vtableSlot = 1;  // Fallback to Object.Equals slot
                }
                else if (NameEquals(methodName, "GetHashCode") && paramCount == 0)
                    vtableSlot = 2;
                else
                {
                    // Not an Object override - search base class hierarchy for vtable slot
                    vtableSlot = FindVtableSlotInBaseClass(asm, typeDefRow, methodName);
                    if (vtableSlot < 0)
                    {
                        DebugConsole.Write("[LazyJIT] WARN: Could not find vtable slot for override '");
                        for (int k = 0; methodName[k] != 0 && k < 32; k++)
                            DebugConsole.WriteChar((char)methodName[k]);
                        DebugConsole.Write("' in type row ");
                        DebugConsole.WriteDecimal(typeDefRow);
                        DebugConsole.WriteLine();
                    }
                }

                if (vtableSlot >= 0)
                {
                    // Register this override method for lazy JIT
                    uint methodToken = 0x06000000 | methodRow;
                    JIT.CompiledMethodRegistry.RegisterUncompiledOverride(
                        methodToken, asm->AssemblyId, mt, vtableSlot);

                    DebugConsole.Write("[LazyJIT] Registered override slot ");
                    DebugConsole.WriteDecimal((uint)vtableSlot);
                    DebugConsole.Write(" token 0x");
                    DebugConsole.WriteHex(methodToken);
                    DebugConsole.Write(" MT 0x");
                    DebugConsole.WriteHex((ulong)mt);
                    DebugConsole.WriteLine();
                }
            }
        }
    }

    /// <summary>
    /// Find the vtable slot for an overridden method by searching the base class hierarchy.
    /// Returns -1 if not found.
    /// </summary>
    private static short FindVtableSlotInBaseClass(LoadedAssembly* asm, uint typeDefRow, byte* methodName)
    {
        // Get the base type for this TypeDef
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, typeDefRow);
        if (extendsIdx.RowId == 0)
            return -1;  // No base type

        LoadedAssembly* baseAsm = asm;
        uint baseTypeDefRow = 0;

        if (extendsIdx.Table == MetadataTableId.TypeDef)  // Base class in same assembly
        {
            baseTypeDefRow = extendsIdx.RowId;
        }
        else if (extendsIdx.Table == MetadataTableId.TypeRef)  // Base class in different assembly
        {
            // Resolve TypeRef to get the base assembly and TypeDef
            CodedIndex resScope = MetadataReader.GetTypeRefResolutionScope(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            if (resScope.Table == MetadataTableId.AssemblyRef)
            {
                uint refNameIdx = MetadataReader.GetAssemblyRefName(ref asm->Tables, ref asm->Sizes, resScope.RowId);
                byte* refName = MetadataReader.GetString(ref asm->Metadata, refNameIdx);

                baseAsm = GetLoadedAssemblyByName(refName);
                if (baseAsm == null)
                    return -1;

                // Find TypeDef in base assembly by name
                uint typeNameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
                byte* typeName = MetadataReader.GetString(ref asm->Metadata, typeNameIdx);

                baseTypeDefRow = FindTypeDefByName(baseAsm, typeName);
                if (baseTypeDefRow == 0)
                    return -1;
            }
            else
            {
                return -1;  // Unsupported resolution scope
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeSpec)  // Generic base class like EqualityComparer<T>
        {
            // TypeSpec is a generic instantiation - need to resolve it to find the underlying TypeDef
            uint typeSpecToken = 0x1B000000 | extendsIdx.RowId;
            uint typeSpecRow = extendsIdx.RowId;
            uint blobIdx = MetadataReader.GetTypeSpecSignature(ref asm->Tables, ref asm->Sizes, typeSpecRow);
            byte* blob = MetadataReader.GetBlob(ref asm->Metadata, blobIdx, out uint blobLen);

            if (blob != null && blobLen > 0 && blob[0] == 0x15)  // GENERICINST
            {
                // GenericInst format: GENERICINST + CLASS/VALUETYPE + TypeDefOrRef + ArgCount + Type*
                int pos = 1;
                byte classOrVT = blob[pos++];  // 0x12 for CLASS, 0x11 for VALUETYPE

                // Decode TypeDefOrRef (compressed)
                uint typeDefOrRef = 0;
                byte b = blob[pos++];
                if ((b & 0x80) == 0)
                    typeDefOrRef = b;
                else if ((b & 0xC0) == 0x80)
                    typeDefOrRef = (uint)(((b & 0x3F) << 8) | blob[pos++]);

                // TypeDefOrRef coded index: 2-bit tag (0=TypeDef, 1=TypeRef, 2=TypeSpec)
                uint tag = typeDefOrRef & 0x03;
                uint row = typeDefOrRef >> 2;

                if (tag == 0)  // TypeDef
                {
                    baseTypeDefRow = row;
                    // Same assembly
                }
                else if (tag == 1)  // TypeRef
                {
                    // Resolve TypeRef to get the assembly
                    CodedIndex resScope = MetadataReader.GetTypeRefResolutionScope(ref asm->Tables, ref asm->Sizes, row);
                    if (resScope.Table == MetadataTableId.AssemblyRef)
                    {
                        uint refNameIdx = MetadataReader.GetAssemblyRefName(ref asm->Tables, ref asm->Sizes, resScope.RowId);
                        byte* refName = MetadataReader.GetString(ref asm->Metadata, refNameIdx);
                        baseAsm = GetLoadedAssemblyByName(refName);
                        if (baseAsm == null)
                            return -1;

                        uint typeNameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, row);
                        byte* typeName = MetadataReader.GetString(ref asm->Metadata, typeNameIdx);
                        baseTypeDefRow = FindTypeDefByName(baseAsm, typeName);
                    }
                }
            }

            if (baseTypeDefRow == 0)
                return -1;
        }
        else
        {
            return -1;  // Unsupported
        }

        // Search for the method in the base class
        short slot = FindVirtualMethodSlotByName(baseAsm, baseTypeDefRow, methodName);
        if (slot >= 0)
            return slot;

        // Recurse up the hierarchy
        return FindVtableSlotInBaseClass(baseAsm, baseTypeDefRow, methodName);
    }

    /// <summary>
    /// Find a virtual method's slot in a TypeDef by name.
    /// Returns the slot index (3+) for newslot virtuals, or -1 if not found.
    /// </summary>
    private static short FindVirtualMethodSlotByName(LoadedAssembly* asm, uint typeDefRow, byte* methodName)
    {
        // First count inherited slots from base class
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, typeDefRow);
        ushort baseSlots = 3;  // Default: Object's 3 slots

        if (extendsIdx.RowId != 0)
        {
            if (extendsIdx.Table == MetadataTableId.TypeDef)  // TypeDef in same assembly
            {
                baseSlots = CountTotalVtableSlots(asm, extendsIdx.RowId);
            }
            else if (extendsIdx.Table == MetadataTableId.TypeRef)  // TypeRef
            {
                // Resolve and count base class slots
                CodedIndex resScope = MetadataReader.GetTypeRefResolutionScope(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
                if (resScope.Table == MetadataTableId.AssemblyRef)
                {
                    uint refNameIdx = MetadataReader.GetAssemblyRefName(ref asm->Tables, ref asm->Sizes, resScope.RowId);
                    byte* refName = MetadataReader.GetString(ref asm->Metadata, refNameIdx);

                    LoadedAssembly* baseAsm = GetLoadedAssemblyByName(refName);
                    if (baseAsm != null)
                    {
                        uint typeNameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
                        byte* typeName = MetadataReader.GetString(ref asm->Metadata, typeNameIdx);

                        uint baseTypeDefRow = FindTypeDefByName(baseAsm, typeName);
                        if (baseTypeDefRow > 0)
                            baseSlots = CountTotalVtableSlots(baseAsm, baseTypeDefRow);
                    }
                }
            }
        }

        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        short currentSlot = (short)baseSlots;
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;

            if (isVirtual && isNewSlot && !isStatic)
            {
                // This is a new virtual method - check if it matches
                uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
                byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);

                if (NameEquals(name, methodName))
                    return currentSlot;

                currentSlot++;
            }
        }

        return -1;  // Not found in this type
    }

    /// <summary>
    /// Count total vtable slots for a type (inherited + new).
    /// </summary>
    private static ushort CountTotalVtableSlots(LoadedAssembly* asm, uint typeDefRow)
    {
        // Get inherited slots from base
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, typeDefRow);
        ushort baseSlots = 3;  // Default: Object's 3 slots

        if (extendsIdx.RowId != 0)
        {
            if (extendsIdx.Table == MetadataTableId.TypeDef)  // TypeDef in same assembly
            {
                baseSlots = CountTotalVtableSlots(asm, extendsIdx.RowId);
            }
            // For TypeRef, we'd need to resolve - for now assume 3 for System.Object
        }

        // Add new slots from this type
        ushort newSlots = CountNewVirtualSlots(asm, typeDefRow);
        return (ushort)(baseSlots + newSlots);
    }

    /// <summary>
    /// Find a TypeDef row by type name.
    /// </summary>
    private static uint FindTypeDefByName(LoadedAssembly* asm, byte* typeName)
    {
        uint count = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        for (uint row = 1; row <= count; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            if (NameEquals(name, typeName))
                return row;
        }
        return 0;
    }

    /// <summary>
    /// Find a TypeDef token by type name and namespace.
    /// Returns 0x02xxxxxx TypeDef token, or 0 if not found.
    /// </summary>
    private static uint FindTypeDefByName(LoadedAssembly* asm, byte* typeName, byte* typeNs)
    {
        uint count = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        for (uint row = 1; row <= count; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, row);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);
            if (NameEquals(name, typeName) && NameEquals(ns, typeNs))
                return 0x02000000 | row;
        }
        return 0;
    }

    /// <summary>
    /// Get a loaded assembly by name.
    /// </summary>
    private static LoadedAssembly* GetLoadedAssemblyByName(byte* name)
    {
        // Check if it's System.Runtime (resolves to Object having only 3 slots)
        if (NameEquals(name, "System.Runtime"))
            return null;  // Can't look up slots in System.Runtime base types

        // Search loaded assemblies
        for (int i = 0; i < _assemblyCount; i++)
        {
            if (_assemblies[i].IsLoaded && NameEquals(_assemblies[i].Name, name))
                return &_assemblies[i];
        }
        return null;
    }

    /// <summary>
    /// Get a bitmask of Object vtable slots (0=ToString, 1=Equals, 2=GetHashCode) that this type overrides.
    /// Used to avoid copying base class pointers for slots that have overrides.
    /// </summary>
    private static byte GetOverriddenObjectSlots(LoadedAssembly* asm, uint typeDefRow)
    {
        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        byte overriddenSlots = 0;
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            // Look for virtual methods that are NOT newslot (i.e., overrides)
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;

            if (isVirtual && !isNewSlot && !isStatic)
            {
                // This is an override - check if it's ToString, Equals, or GetHashCode
                uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
                byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

                if (NameEquals(methodName, "ToString"))
                    overriddenSlots |= 0x01;  // Slot 0
                else if (NameEquals(methodName, "Equals"))
                    overriddenSlots |= 0x02;  // Slot 1
                else if (NameEquals(methodName, "GetHashCode"))
                    overriddenSlots |= 0x04;  // Slot 2
            }
        }

        return overriddenSlots;
    }

    /// <summary>
    /// Count the number of new virtual slots introduced by a TypeDef.
    /// This counts methods that are virtual AND have NewSlot set.
    /// </summary>
    private static ushort CountNewVirtualSlots(LoadedAssembly* asm, uint typeDefRow)
    {
        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        // Debug for EqualityComparer types (row 0x84 or 0x85) - values from korlib TypeDef table
        bool debug = (typeDefRow == 0x84 || typeDefRow == 0x85);
        if (debug)
        {
            DebugConsole.Write("[CountNewVirt] row=0x");
            DebugConsole.WriteHex(typeDefRow);
            DebugConsole.Write(" asm=");
            DebugConsole.WriteDecimal(asm->AssemblyId);
            DebugConsole.Write(" methods ");
            DebugConsole.WriteDecimal(methodStart);
            DebugConsole.Write("-");
            DebugConsole.WriteDecimal(methodEnd);
            DebugConsole.WriteLine();
        }

        ushort count = 0;
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            // Count methods that are virtual AND introduce a new slot
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;

            if (debug)
            {
                DebugConsole.Write("[CountNewVirt]   m");
                DebugConsole.WriteDecimal(methodRow);
                DebugConsole.Write(" flags=0x");
                DebugConsole.WriteHex(methodFlags);
                DebugConsole.Write(" V=");
                DebugConsole.Write(isVirtual ? "Y" : "N");
                DebugConsole.Write(" N=");
                DebugConsole.Write(isNewSlot ? "Y" : "N");
                DebugConsole.Write(" S=");
                DebugConsole.Write(isStatic ? "Y" : "N");
                DebugConsole.WriteLine();
            }

            if (isVirtual && isNewSlot && !isStatic)
            {
                count++;
            }
        }

        if (debug)
        {
            DebugConsole.Write("[CountNewVirt] count=");
            DebugConsole.WriteDecimal(count);
            DebugConsole.WriteLine();
        }

        return count;
    }

    /// <summary>
    /// Eagerly compile Object override methods (ToString, Equals, GetHashCode) and update vtable slots.
    /// This ensures virtual dispatch works correctly even before the override method is first called.
    /// </summary>
    private static void EagerlyCompileObjectOverrides(LoadedAssembly* asm, uint typeDefRow, MethodTable* mt)
    {
        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = methodCount + 1;

        nint* vtable = mt->GetVtablePtr();

        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);

            // Look for virtual methods that are NOT newslot (i.e., overrides)
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;
            bool isNewSlot = (methodFlags & MethodDefFlags.NewSlot) != 0;
            bool isStatic = (methodFlags & MethodDefFlags.Static) != 0;

            if (isVirtual && !isNewSlot && !isStatic)
            {
                // This is an override - check if it's ToString, Equals, or GetHashCode
                uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
                byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

                int vtableSlot = -1;
                if (NameEquals(methodName, "ToString"))
                    vtableSlot = 0;
                else if (NameEquals(methodName, "Equals"))
                    vtableSlot = 1;
                else if (NameEquals(methodName, "GetHashCode"))
                    vtableSlot = 2;

                if (vtableSlot >= 0 && vtableSlot < mt->_usNumVtableSlots)
                {
                    // JIT compile this method and update the vtable slot
                    uint methodToken = 0x06000000 | methodRow;
                    var jitResult = Tier0JIT.CompileMethod(asm->AssemblyId, methodToken);
                    if (jitResult.Success && jitResult.CodeAddress != null)
                    {
                        vtable[vtableSlot] = (nint)jitResult.CodeAddress;
                    }
                }
            }
        }
    }

    private static bool NameEquals(byte* name, string expected)
    {
        if (name == null)
            return false;
        for (int i = 0; i < expected.Length; i++)
        {
            if (name[i] != (byte)expected[i])
                return false;
        }
        return name[expected.Length] == 0;
    }

    /// <summary>
    /// Compare two null-terminated byte* strings for equality.
    /// </summary>
    private static bool NameEquals(byte* name1, byte* name2)
    {
        if (name1 == null || name2 == null)
            return name1 == name2;

        int i = 0;
        while (true)
        {
            if (name1[i] != name2[i])
                return false;
            if (name1[i] == 0)
                return true;
            i++;
        }
    }

    /// <summary>
    /// Count the number of interfaces implemented directly by a TypeDef.
    /// Scans the InterfaceImpl table for entries pointing to this type.
    /// </summary>
    private static ushort CountInterfacesForType(LoadedAssembly* asm, uint typeDefRow)
    {
        uint interfaceImplCount = asm->Tables.RowCounts[(int)MetadataTableId.InterfaceImpl];
        ushort count = 0;

        for (uint i = 1; i <= interfaceImplCount; i++)
        {
            uint classRowId = MetadataReader.GetInterfaceImplClass(ref asm->Tables, ref asm->Sizes, i);
            if (classRowId == typeDefRow)
                count++;
        }

        return count;
    }

    /// <summary>
    /// Count the total number of vtable slots needed for interface methods.
    /// This sums up all methods from all interfaces the type implements.
    /// </summary>
    private static ushort CountInterfaceMethodSlots(LoadedAssembly* asm, uint typeDefRow)
    {
        uint interfaceImplCount = asm->Tables.RowCounts[(int)MetadataTableId.InterfaceImpl];
        ushort totalSlots = 0;

        for (uint i = 1; i <= interfaceImplCount; i++)
        {
            uint classRowId = MetadataReader.GetInterfaceImplClass(ref asm->Tables, ref asm->Sizes, i);
            if (classRowId != typeDefRow)
                continue;

            // Get the interface TypeDefOrRef
            uint rawInterface = MetadataReader.GetInterfaceImplInterface(ref asm->Tables, ref asm->Sizes, i);
            uint tag = rawInterface & 0x03;
            uint rowId = rawInterface >> 2;

            MethodTable* interfaceMT = null;

            if (tag == 0)  // TypeDef
            {
                uint interfaceToken = 0x02000000 | rowId;
                interfaceMT = asm->Types.Lookup(interfaceToken);
                if (interfaceMT == null)
                    interfaceMT = CreateTypeDefMethodTable(asm, interfaceToken);
            }
            else if (tag == 1)  // TypeRef
            {
                LoadedAssembly* targetAsm = null;
                uint targetToken = 0;
                if (ResolveTypeRefToTypeDef(asm, rowId, out targetAsm, out targetToken) && targetAsm != null)
                {
                    interfaceMT = targetAsm->Types.Lookup(targetToken);
                    if (interfaceMT == null)
                        interfaceMT = CreateTypeDefMethodTable(targetAsm, targetToken);
                }
            }
            else if (tag == 2)  // TypeSpec - generic interface
            {
                // DON'T resolve TypeSpec here - it can cause infinite recursion!
                // When counting interface slots for type T that implements IEquatable<T>,
                // resolving IEquatable<T> would try to get T's MethodTable which isn't
                // registered yet (we're in the middle of creating it).
                //
                // Instead, we parse the TypeSpec signature to find the generic interface
                // definition and count its methods directly (without resolving type args).

                // Get the TypeSpec signature
                uint sigIdx = MetadataReader.GetTypeSpecSignature(ref asm->Tables, ref asm->Sizes, rowId);
                byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);

                if (sig != null && sigLen > 2 && sig[0] == 0x15)  // GENERICINST
                {
                    // Parse: 0x15 CLASS/VALUETYPE TypeDefOrRef ArgCount ...
                    byte* ptr = sig + 1;  // skip GENERICINST byte
                    ptr++;  // skip CLASS/VALUETYPE byte

                    // Decode the generic interface definition token
                    uint genDefEncoded = MetadataReader.ReadCompressedUInt(ref ptr);
                    uint genDefTag = genDefEncoded & 0x03;
                    uint genDefRow = genDefEncoded >> 2;

                    // Try to resolve just the interface definition (not the instantiation)
                    if (genDefTag == 0)  // TypeDef
                    {
                        uint interfaceDefToken = 0x02000000 | genDefRow;
                        interfaceMT = asm->Types.Lookup(interfaceDefToken);
                        if (interfaceMT == null)
                            interfaceMT = CreateTypeDefMethodTable(asm, interfaceDefToken);
                    }
                    else if (genDefTag == 1)  // TypeRef
                    {
                        LoadedAssembly* targetAsm = null;
                        uint targetToken = 0;
                        if (ResolveTypeRefToTypeDef(asm, genDefRow, out targetAsm, out targetToken) && targetAsm != null)
                        {
                            interfaceMT = targetAsm->Types.Lookup(targetToken);
                            if (interfaceMT == null)
                                interfaceMT = CreateTypeDefMethodTable(targetAsm, targetToken);
                        }
                    }
                }
            }

            if (interfaceMT != null)
            {
                // Add the number of methods in this interface
                totalSlots += interfaceMT->_usNumVtableSlots;
            }
            else
            {
                // Fallback: assume 1 method if we can't resolve the interface
                totalSlots++;
            }
        }

        return totalSlots;
    }

    /// <summary>
    /// Count the total number of vtable slots used by interfaces in a base class.
    /// This is used to properly size derived type vtables.
    /// </summary>
    private static ushort CountBaseInterfaceMethodSlots(MethodTable* baseMT)
    {
        if (baseMT == null || baseMT->_usNumInterfaces == 0)
            return 0;

        ushort totalSlots = 0;
        InterfaceMapEntry* map = baseMT->GetInterfaceMapPtr();
        for (int i = 0; i < baseMT->_usNumInterfaces; i++)
        {
            if (map[i].InterfaceMT != null)
            {
                totalSlots += map[i].InterfaceMT->_usNumVtableSlots;
            }
        }
        return totalSlots;
    }

    /// <summary>
    /// Populate the interface map for a MethodTable.
    /// For each interface, records the interface MT and the starting vtable slot.
    /// Also copies inherited interfaces from the base class.
    /// </summary>
    /// <param name="asm">The assembly containing the type</param>
    /// <param name="typeDefRow">The TypeDef row ID</param>
    /// <param name="mt">The MethodTable to populate</param>
    /// <param name="interfaceStartSlot">The first vtable slot for interface methods</param>
    /// <param name="baseMT">The base class MethodTable (can be null)</param>
    private static void PopulateInterfaceMap(LoadedAssembly* asm, uint typeDefRow, MethodTable* mt, ushort interfaceStartSlot, MethodTable* baseMT = null)
    {
        uint interfaceImplCount = asm->Tables.RowCounts[(int)MetadataTableId.InterfaceImpl];
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int mapIndex = 0;
        ushort currentSlot = interfaceStartSlot;

        // First, copy inherited interfaces from the base class
        if (baseMT != null && baseMT->_usNumInterfaces > 0)
        {
            InterfaceMapEntry* baseMap = baseMT->GetInterfaceMapPtr();
            for (int i = 0; i < baseMT->_usNumInterfaces; i++)
            {
                map[mapIndex].InterfaceMT = baseMap[i].InterfaceMT;
                map[mapIndex].StartSlot = currentSlot;

                // Advance slot by number of methods in the interface
                if (baseMap[i].InterfaceMT != null)
                {
                    ushort interfaceMethodCount = baseMap[i].InterfaceMT->_usNumVtableSlots;
                    if (interfaceMethodCount == 0)
                        interfaceMethodCount = 1;  // Fallback for safety
                    currentSlot += interfaceMethodCount;
                }
                mapIndex++;
            }
        }

        for (uint i = 1; i <= interfaceImplCount; i++)
        {
            uint classRowId = MetadataReader.GetInterfaceImplClass(ref asm->Tables, ref asm->Sizes, i);
            if (classRowId != typeDefRow)
                continue;

            // Get the interface TypeDefOrRef
            uint rawInterface = MetadataReader.GetInterfaceImplInterface(ref asm->Tables, ref asm->Sizes, i);

            // Decode TypeDefOrRef coded index (2-bit tag)
            uint tag = rawInterface & 0x03;
            uint rowId = rawInterface >> 2;

            MethodTable* interfaceMT = null;
            uint resolvedToken = 0;  // Track resolved token for IDisposable registration

            if (tag == 0)  // TypeDef
            {
                uint interfaceToken = 0x02000000 | rowId;
                interfaceMT = asm->Types.Lookup(interfaceToken);
                if (interfaceMT == null)
                    interfaceMT = CreateTypeDefMethodTable(asm, interfaceToken);
            }
            else if (tag == 1)  // TypeRef
            {
                // Resolve TypeRef to another assembly's TypeDef
                LoadedAssembly* targetAsm = null;
                uint targetToken = 0;
                if (ResolveTypeRefToTypeDef(asm, rowId, out targetAsm, out targetToken) && targetAsm != null)
                {
                    resolvedToken = targetToken;  // Save for later IDisposable check

                    // Check for well-known interface types (0xF0xxxxxx tokens)
                    if ((targetToken & 0xFF000000) == 0xF0000000)
                    {
                        // Well-known type - try MetadataIntegration first
                        interfaceMT = JIT.MetadataIntegration.LookupType(targetToken);
                        // If not found, try assembly lookup (may work for some AOT types)
                        if (interfaceMT == null)
                        {
                            interfaceMT = targetAsm->Types.Lookup(targetToken);
                        }
                        // Final fallback - try to create (works if there's metadata)
                        if (interfaceMT == null)
                        {
                            interfaceMT = CreateTypeDefMethodTable(targetAsm, targetToken);
                        }
                    }
                    else
                    {
                        interfaceMT = targetAsm->Types.Lookup(targetToken);
                        if (interfaceMT == null)
                            interfaceMT = CreateTypeDefMethodTable(targetAsm, targetToken);
                    }
                }
            }
            else if (tag == 2)  // TypeSpec - generic interface instantiation
            {
                // TypeSpec is used for generic interfaces like IContainer<int>
                // ResolveTypeSpec handles deduplication via GetOrCreateGenericInstMethodTable
                uint typeSpecToken = 0x1B000000 | rowId;
                interfaceMT = ResolveTypeSpec(asm, typeSpecToken);
            }

            if (interfaceMT != null)
            {
                map[mapIndex].InterfaceMT = interfaceMT;
                map[mapIndex].StartSlot = currentSlot;

                // Check if this is IDisposable and register its MT for later use
                if (resolvedToken == JIT.MetadataIntegration.WellKnownTypes.IDisposable)
                {
                    JIT.MetadataIntegration.RegisterIDisposableMT(interfaceMT);
                }

                // Advance slot by number of methods in the interface
                ushort interfaceMethodCount = interfaceMT->_usNumVtableSlots;
                if (interfaceMethodCount == 0)
                    interfaceMethodCount = 1;  // Fallback for safety
                currentSlot += interfaceMethodCount;
                mapIndex++;
            }
        }
    }

    /// <summary>
    /// Populate the interface map for a generic instantiation MethodTable.
    /// This is similar to PopulateInterfaceMap but uses the type context for type variable substitution.
    /// </summary>
    /// <param name="asm">The assembly containing the generic type definition</param>
    /// <param name="typeDefRow">The TypeDef row ID of the generic type definition</param>
    /// <param name="mt">The instantiated MethodTable to populate</param>
    /// <param name="interfaceStartSlot">The first vtable slot for interface methods</param>
    private static void PopulateGenericInstInterfaceMap(LoadedAssembly* asm, uint typeDefRow, MethodTable* mt, ushort interfaceStartSlot)
    {
        uint interfaceImplCount = asm->Tables.RowCounts[(int)MetadataTableId.InterfaceImpl];
        InterfaceMapEntry* map = mt->GetInterfaceMapPtr();
        int mapIndex = 0;
        ushort currentSlot = interfaceStartSlot;

        for (uint i = 1; i <= interfaceImplCount; i++)
        {
            uint classRowId = MetadataReader.GetInterfaceImplClass(ref asm->Tables, ref asm->Sizes, i);
            if (classRowId != typeDefRow)
                continue;

            // Get the interface TypeDefOrRef
            uint rawInterface = MetadataReader.GetInterfaceImplInterface(ref asm->Tables, ref asm->Sizes, i);

            // Decode TypeDefOrRef coded index (2-bit tag)
            uint tag = rawInterface & 0x03;
            uint rowId = rawInterface >> 2;

            MethodTable* interfaceMT = null;

            if (tag == 0)  // TypeDef
            {
                uint interfaceToken = 0x02000000 | rowId;
                interfaceMT = asm->Types.Lookup(interfaceToken);
                if (interfaceMT == null)
                    interfaceMT = CreateTypeDefMethodTable(asm, interfaceToken);
            }
            else if (tag == 1)  // TypeRef
            {
                // Resolve TypeRef to another assembly's TypeDef
                LoadedAssembly* targetAsm = null;
                uint targetToken = 0;
                if (ResolveTypeRefToTypeDef(asm, rowId, out targetAsm, out targetToken) && targetAsm != null)
                {
                    interfaceMT = targetAsm->Types.Lookup(targetToken);
                    if (interfaceMT == null)
                        interfaceMT = CreateTypeDefMethodTable(targetAsm, targetToken);
                }
            }
            else if (tag == 2)  // TypeSpec - generic interface instantiation
            {
                // TypeSpec is used for generic interfaces like IContainer<T>
                // With the type context set, ResolveTypeSpec will substitute type variables
                uint typeSpecToken = 0x1B000000 | rowId;
                interfaceMT = ResolveTypeSpec(asm, typeSpecToken);
            }

            if (interfaceMT != null)
            {
                map[mapIndex].InterfaceMT = interfaceMT;
                map[mapIndex].StartSlot = currentSlot;

                // Advance slot by number of methods in the interface
                ushort interfaceMethodCount = interfaceMT->_usNumVtableSlots;
                if (interfaceMethodCount == 0)
                    interfaceMethodCount = 1;  // Fallback for safety
                currentSlot += interfaceMethodCount;
                mapIndex++;
            }
        }
    }

    /// <summary>
    /// Get the base class MethodTable for a TypeDefOrRef coded index.
    /// Returns null if the base is System.Object (which we handle specially).
    /// </summary>
    private static MethodTable* GetBaseClassMethodTable(LoadedAssembly* asm, CodedIndex extendsIdx)
    {
        // Don't resolve System.Object or System.ValueType
        if (IsObjectBase(asm, extendsIdx) || IsValueTypeBase(asm, extendsIdx))
            return null;

        // TypeDefOrRef coded index: TypeDef=0, TypeRef=1, TypeSpec=2
        if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // Base class is in the same assembly - resolve its MT
            uint baseToken = 0x02000000 | extendsIdx.RowId;
            return ResolveType(asm->AssemblyId, baseToken);
        }
        else if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            // Base class is in another assembly - resolve TypeRef to TypeDef
            LoadedAssembly* targetAsm;
            uint typeDefToken;
            if (ResolveTypeRefToTypeDef(asm, extendsIdx.RowId, out targetAsm, out typeDefToken))
            {
                // Well-known types (0xF0xxxxxx) have targetAsm = null
                // Use MetadataIntegration.LookupType directly for them
                if ((typeDefToken >> 24) == 0xF0)
                {
                    MethodTable* wkMt = JIT.MetadataIntegration.LookupType(typeDefToken);
                    DebugConsole.Write("[GetBaseMT] WK token=0x");
                    DebugConsole.WriteHex(typeDefToken);
                    DebugConsole.Write(" MT=0x");
                    DebugConsole.WriteHex((ulong)wkMt);
                    if (wkMt != null)
                    {
                        DebugConsole.Write(" slots=");
                        DebugConsole.WriteDecimal(wkMt->_usNumVtableSlots);
                    }
                    DebugConsole.WriteLine();
                    return wkMt;
                }
                if (targetAsm != null && typeDefToken != 0)
                {
                    return ResolveType(targetAsm->AssemblyId, typeDefToken);
                }
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - base class is a generic type instantiation (e.g., EqualityComparer<T>)
            // We need to resolve the generic type definition to get vtable slot information
            DebugConsole.Write("[GetBaseMT] TypeSpec row=");
            DebugConsole.WriteDecimal(extendsIdx.RowId);
            DebugConsole.WriteLine();
            uint tsBlobIdx = MetadataReader.GetTypeSpecSignature(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            byte* tsSig = MetadataReader.GetBlob(ref asm->Metadata, tsBlobIdx, out uint tsSigLen);

            if (tsSig != null && tsSigLen > 2 && tsSig[0] == 0x15)  // ELEMENT_TYPE_GENERICINST
            {
                // Parse GENERICINST: 0x15 CLASS/VALUETYPE TypeDefOrRef GenArgCount Type...
                int tsPos = 1;
                byte classOrVt = tsSig[tsPos++];  // 0x11 or 0x12

                // Decode the generic type definition token
                uint genDefToken = DecodeTypeDefOrRefOrSpec(tsSig, ref tsPos, tsSigLen);
                if (genDefToken != 0)
                {
                    uint table = genDefToken >> 24;
                    uint row = genDefToken & 0x00FFFFFF;

                    DebugConsole.Write("[GetBaseMT] TypeSpec generic def token=0x");
                    DebugConsole.WriteHex(genDefToken);
                    DebugConsole.WriteLine();

                    if (table == 0x02)  // TypeDef - in same assembly
                    {
                        return ResolveType(asm->AssemblyId, genDefToken);
                    }
                    else if (table == 0x01)  // TypeRef - resolve to target assembly
                    {
                        DebugConsole.Write("[GetBaseMT] TypeRef row=");
                        DebugConsole.WriteDecimal(row);
                        DebugConsole.WriteLine();
                        LoadedAssembly* targetAsm;
                        uint typeDefToken;
                        if (ResolveTypeRefToTypeDef(asm, row, out targetAsm, out typeDefToken))
                        {
                            DebugConsole.Write("[GetBaseMT] Resolved to asm=");
                            DebugConsole.WriteDecimal(targetAsm != null ? targetAsm->AssemblyId : 0);
                            DebugConsole.Write(" token=0x");
                            DebugConsole.WriteHex(typeDefToken);
                            DebugConsole.WriteLine();
                            if (targetAsm != null && typeDefToken != 0)
                            {
                                MethodTable* result = ResolveType(targetAsm->AssemblyId, typeDefToken);
                                DebugConsole.Write("[GetBaseMT] ResolveType returned MT=0x");
                                DebugConsole.WriteHex((ulong)result);
                                if (result != null)
                                {
                                    DebugConsole.Write(" slots=");
                                    DebugConsole.WriteDecimal(result->_usNumVtableSlots);
                                }
                                DebugConsole.WriteLine();
                                return result;
                            }
                        }
                        else
                        {
                            DebugConsole.WriteLine("[GetBaseMT] TypeRef resolution failed");
                        }
                    }
                }
            }
            DebugConsole.WriteLine("[GetBaseMT] TypeSpec resolution returned null");
        }

        return null;
    }

    /// <summary>
    /// Check if a TypeDefOrRef coded index refers to System.ValueType or System.Enum.
    /// </summary>
    private static bool IsValueTypeBase(LoadedAssembly* asm, CodedIndex extendsIdx)
    {
        // TypeDefOrRef: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            // Get the TypeRef name and namespace
            uint nameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeRefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);

            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            // Check for System.ValueType or System.Enum
            if (ns != null && name != null)
            {
                if (IsSystemNamespace(ns))
                {
                    if (IsValueTypeName(name) || IsEnumName(name))
                        return true;
                }
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // TypeDef - ValueType/Enum is in the same assembly (e.g., korlib)
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);

            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            if (ns != null && name != null)
            {
                if (IsSystemNamespace(ns))
                {
                    if (IsValueTypeName(name) || IsEnumName(name))
                        return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a TypeDefOrRef coded index refers to System.MulticastDelegate.
    /// </summary>
    private static bool IsDelegateBase(LoadedAssembly* asm, CodedIndex extendsIdx)
    {
        // TypeDefOrRef: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            // Get the TypeRef name and namespace
            uint nameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeRefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);

            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            // Check for System.MulticastDelegate
            if (ns != null && name != null)
            {
                bool isSysNs = IsSystemNamespace(ns);
                bool isMcd = IsMulticastDelegateName(name);
                if (isSysNs && isMcd)
                    return true;
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // TypeDef - MulticastDelegate is in the same assembly (e.g., korlib)
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);

            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            if (ns != null && name != null)
            {
                bool isSysNs = IsSystemNamespace(ns);
                bool isMcd = IsMulticastDelegateName(name);
                if (isSysNs && isMcd)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Compute instance size for a type from its fields, including base class.
    /// Handles explicit layout structs by checking ClassLayout and FieldLayout tables.
    /// </summary>
    private static uint ComputeInstanceSize(LoadedAssembly* asm, uint typeDefRow, bool isValueType)
    {
        // First, check ClassLayout table for an explicit size
        uint classLayoutCount = asm->Tables.RowCounts[(int)MetadataTableId.ClassLayout];
        for (uint i = 1; i <= classLayoutCount; i++)
        {
            uint parent = MetadataReader.GetClassLayoutParent(ref asm->Tables, ref asm->Sizes, i);
            if (parent == typeDefRow)
            {
                uint explicitSize = MetadataReader.GetClassLayoutClassSize(ref asm->Tables, ref asm->Sizes, i);
                if (explicitSize > 0)
                {
                    // For reference types, add object header (8 bytes)
                    return isValueType ? explicitSize : (explicitSize + 8);
                }
                // If ClassSize is 0, fall through to calculate from fields
                break;
            }
        }

        // For reference types, start with pointer size (for MethodTable*)
        uint size = isValueType ? 0u : 8u;

        // Get base class size if not a value type and has a base class
        if (!isValueType)
        {
            CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, typeDefRow);
            if (extendsIdx.RowId != 0 && !IsValueTypeBase(asm, extendsIdx) && !IsObjectBase(asm, extendsIdx))
            {
                // Special handling for delegates: System.Delegate and System.MulticastDelegate
                // may not be resolved from external assemblies, but we know their layout:
                // - offset 0: MethodTable* (8 bytes)
                // - offset 8: _firstParameter (object, 8 bytes)
                // - offset 16: _helperObject (object, 8 bytes)
                // - offset 24: _extraFunctionPointerOrData (nint, 8 bytes)
                // - offset 32: _functionPointer (IntPtr, 8 bytes)
                // - offset 40: _invocationList (object[], 8 bytes) - MulticastDelegate
                // - offset 48: _invocationCount (int, 8 bytes with padding) - MulticastDelegate
                // Total: 56 bytes
                if (IsDelegateBase(asm, extendsIdx))
                {
                    size = 56;  // Fixed delegate base size including MulticastDelegate fields
                }
                else
                {
                    // Get the base class's method table to get its size
                    uint baseSize = GetBaseClassSize(asm, extendsIdx);
                    if (baseSize > 8)
                    {
                        // Base class size already includes the object header
                        size = baseSize;
                    }
                }
            }
        }

        // Get field range for this type
        uint fieldStart = MetadataReader.GetTypeDefFieldList(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint fieldDefCount = asm->Tables.RowCounts[(int)MetadataTableId.Field];
        uint fieldEnd;

        if (typeDefRow < typeDefCount)
        {
            fieldEnd = MetadataReader.GetTypeDefFieldList(ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        }
        else
        {
            fieldEnd = fieldDefCount + 1;
        }

        // Check if any field has explicit offset (indicates explicit layout)
        uint maxExplicitEnd = 0;
        bool hasExplicitLayout = false;
        uint fieldLayoutCount = asm->Tables.RowCounts[(int)MetadataTableId.FieldLayout];

        for (uint fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
        {
            ushort fieldFlags = MetadataReader.GetFieldFlags(ref asm->Tables, ref asm->Sizes, fieldRow);
            if ((fieldFlags & 0x0010) != 0)  // Skip static fields
                continue;

            // Check FieldLayout table for explicit offset
            for (uint fl = 1; fl <= fieldLayoutCount; fl++)
            {
                uint layoutFieldRow = MetadataReader.GetFieldLayoutField(ref asm->Tables, ref asm->Sizes, fl);
                if (layoutFieldRow == fieldRow)
                {
                    hasExplicitLayout = true;
                    uint explicitOffset = MetadataReader.GetFieldLayoutOffset(ref asm->Tables, ref asm->Sizes, fl);

                    // Get field size
                    uint sigIdx = MetadataReader.GetFieldSignature(ref asm->Tables, ref asm->Sizes, fieldRow);
                    byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
                    uint fieldSize = 4;  // Default

                    if (sig != null && sigLen >= 2 && sig[0] == 0x06)
                    {
                        fieldSize = GetFieldTypeSize(asm, sig + 1, sigLen - 1);
                    }

                    uint fieldEnd2 = explicitOffset + fieldSize;
                    if (fieldEnd2 > maxExplicitEnd)
                        maxExplicitEnd = fieldEnd2;
                    break;
                }
            }
        }

        // If explicit layout, return calculated size
        if (hasExplicitLayout && maxExplicitEnd > 0)
        {
            // For value types, size is just the explicit layout size
            // For reference types, add object header (8 bytes)
            uint explicitSize = (maxExplicitEnd + 7) & ~7u;  // Align to 8
            return isValueType ? explicitSize : (explicitSize + 8);
        }

        // Fall back to sequential layout calculation
        for (uint fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
        {
            // Get field flags
            ushort fieldFlags = MetadataReader.GetFieldFlags(ref asm->Tables, ref asm->Sizes, fieldRow);

            // Skip static fields (fdStatic = 0x0010)
            if ((fieldFlags & 0x0010) != 0)
                continue;

            // Get field signature to determine size
            uint sigIdx = MetadataReader.GetFieldSignature(ref asm->Tables, ref asm->Sizes, fieldRow);
            byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);

            if (sig != null && sigLen >= 2)
            {
                // Field signature: FIELD (0x06), followed by type
                if (sig[0] == 0x06)
                {
                    uint fieldSize = GetFieldTypeSize(asm, sig + 1, sigLen - 1);
                    // Align to field's natural alignment (min of field size and 8)
                    uint align = fieldSize < 8 ? fieldSize : 8;
                    if (align > 1)
                    {
                        size = (size + align - 1) & ~(align - 1);
                    }
                    size += fieldSize;
                }
            }
            else
            {
                // Unknown field, assume pointer size and alignment
                size = (size + 7) & ~7u;
                size += 8;
            }
        }

        // Minimum size for reference types is pointer size + 8 (sync block + MT ptr)
        if (!isValueType && size < 16)
            size = 16;

        // Align to 8 bytes
        size = (size + 7) & ~7u;

        return size;
    }

    /// <summary>
    /// Check if extends index points to System.Object
    /// </summary>
    private static bool IsObjectBase(LoadedAssembly* asm, CodedIndex extendsIdx)
    {
        // TypeDefOrRef coded index: TypeDef=0, TypeRef=1, TypeSpec=2
        if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            uint nameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeRefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            if (ns != null && name != null)
            {
                return IsSystemNamespace(ns) && IsObjectName(name);
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // TypeDef - Object is in the same assembly (e.g., korlib)
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            if (ns != null && name != null)
            {
                return IsSystemNamespace(ns) && IsObjectName(name);
            }
        }
        return false;
    }

    /// <summary>
    /// Get the base class size by resolving the extends coded index.
    /// </summary>
    private static uint GetBaseClassSize(LoadedAssembly* asm, CodedIndex extendsIdx)
    {
        // TypeDefOrRef coded index: TypeDef=0, TypeRef=1, TypeSpec=2
        if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // Base class is in the same assembly
            return ComputeInstanceSize(asm, extendsIdx.RowId, false);
        }
        else if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            // Base class is in another assembly - resolve to TypeDef and compute size
            LoadedAssembly* targetAsm;
            uint typeDefToken;
            if (ResolveTypeRefToTypeDef(asm, extendsIdx.RowId, out targetAsm, out typeDefToken))
            {
                // Extract row from token
                uint typeDefRow = typeDefToken & 0x00FFFFFF;
                if (targetAsm != null && typeDefRow > 0)
                {
                    return ComputeInstanceSize(targetAsm, typeDefRow, false);
                }
            }
        }
        return 8; // Default to just object header
    }

    /// <summary>
    /// Get the size of a primitive type from its element type byte.
    /// </summary>
    private static uint GetTypeSize(byte elementType)
    {
        switch (elementType)
        {
            case 0x01:  // Void
                return 0;
            case 0x02:  // Boolean
            case 0x04:  // I1
            case 0x05:  // U1
                return 1;
            case 0x03:  // Char
            case 0x06:  // I2
            case 0x07:  // U2
                return 2;
            case 0x08:  // I4
            case 0x09:  // U4
            case 0x0C:  // R4
                return 4;
            case 0x0A:  // I8
            case 0x0B:  // U8
            case 0x0D:  // R8
            case 0x18:  // I (IntPtr)
            case 0x19:  // U (UIntPtr)
            case 0x0E:  // String
            case 0x0F:  // Ptr
            case 0x10:  // ByRef
            case 0x1C:  // Object
            case 0x12:  // Class
            case 0x14:  // Array
            case 0x1D:  // SzArray
                return 8;
            case 0x11:  // ValueType
            case 0x15:  // GenericInst
                // These need more complex handling - assume 8 for now
                return 8;
            default:
                return 8;  // Default to pointer size
        }
    }

    /// <summary>
    /// Get the size of a field type from its signature bytes.
    /// Handles complex types like ValueType and Class references.
    /// </summary>
    private static uint GetFieldTypeSize(LoadedAssembly* asm, byte* typeSig, uint sigLen)
    {
        if (typeSig == null || sigLen == 0)
            return 8;

        byte elementType = typeSig[0];

        // Handle type variable (ELEMENT_TYPE_VAR = 0x13) - look up from type context
        if (elementType == 0x13)
        {
            if (sigLen >= 2)
            {
                // Get the type variable index
                uint varIndex = typeSig[1];
                int typeArgCount = JIT.MetadataIntegration.GetTypeTypeArgCount();
                if (varIndex < (uint)typeArgCount)
                {
                    MethodTable* typeArgMT = JIT.MetadataIntegration.GetTypeTypeArgMethodTable((int)varIndex);
                    if (typeArgMT != null)
                    {
                        // For value types, use the actual size; for reference types, it's 8
                        if (typeArgMT->IsValueType)
                        {
                            // For AOT primitives, ComponentSize is the raw value size
                            if (typeArgMT->_usComponentSize > 0)
                                return typeArgMT->_usComponentSize;
                            // For JIT value types, BaseSize IS the raw struct size
                            return typeArgMT->_uBaseSize;
                        }
                        else
                        {
                            return 8;  // Reference type
                        }
                    }
                }
            }
            return 8;  // Default to pointer size if context not available
        }

        // For primitive types, use simple lookup
        if (elementType != 0x11 && elementType != 0x12 && elementType != 0x15)
        {
            return GetTypeSize(elementType);
        }

        // VALUETYPE (0x11) or CLASS (0x12) - followed by TypeDefOrRef token
        // GenericInst (0x15) - GENERICINST <genKind> <TypeDefOrRef> <argCount> <args...>
        if (elementType == 0x15)
        {
            if (sigLen < 3)
                return 8;

            byte genKind = typeSig[1];

            // CLASS generic instantiation (0x12) - always a reference (8 bytes)
            if (genKind == 0x12)
                return 8;

            // VALUETYPE generic instantiation (0x11) - need to compute size
            if (genKind == 0x11)
            {
                // Parse the TypeDefOrRef token after genKind
                byte* ptr = typeSig + 2;
                byte* end = typeSig + sigLen;

                uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                uint genTag = typeDefOrRef & 0x03;
                uint genRow = typeDefOrRef >> 2;

                if (genRow == 0)
                    return 8;

                // Get base type size
                uint baseSize = 0;
                if (genTag == 0)  // TypeDef in same assembly
                {
                    baseSize = ComputeInstanceSize(asm, genRow, true);
                }
                else if (genTag == 1)  // TypeRef in another assembly
                {
                    LoadedAssembly* targetAsm;
                    uint typeDefToken;
                    if (ResolveTypeRefToTypeDef(asm, genRow, out targetAsm, out typeDefToken))
                    {
                        uint typeDefRow = typeDefToken & 0x00FFFFFF;
                        if (targetAsm != null && typeDefRow > 0)
                        {
                            baseSize = ComputeInstanceSize(targetAsm, typeDefRow, true);
                        }
                    }
                }

                if (baseSize == 0)
                    baseSize = 8;

                // Parse type arguments and add their sizes
                if (ptr < end)
                {
                    uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                    uint typeArgTotal = 0;

                    for (uint i = 0; i < argCount && ptr < end; i++)
                    {
                        // Get size of each type argument
                        uint argStartLen = (uint)(end - ptr);
                        uint argSize = GetFieldTypeSize(asm, ptr, argStartLen);

                        // Skip past this type argument in the signature
                        // Simple skip for common cases
                        if (ptr < end)
                        {
                            byte argElemType = *ptr++;
                            if (argElemType == 0x11 || argElemType == 0x12)
                            {
                                // VALUETYPE or CLASS - skip compressed token
                                if (ptr < end) MetadataReader.ReadCompressedUInt(ref ptr);
                            }
                            else if (argElemType == 0x15)
                            {
                                // Nested GENERICINST - skip genKind, token, argcount, args
                                if (ptr < end) ptr++;  // genKind
                                if (ptr < end) MetadataReader.ReadCompressedUInt(ref ptr);  // token
                                if (ptr < end)
                                {
                                    uint nestedArgCount = MetadataReader.ReadCompressedUInt(ref ptr);
                                    // Skip nested args (simplified - may need recursive skip)
                                    for (uint j = 0; j < nestedArgCount && ptr < end; j++)
                                    {
                                        byte nestedElemType = *ptr++;
                                        if (nestedElemType == 0x11 || nestedElemType == 0x12)
                                        {
                                            if (ptr < end) MetadataReader.ReadCompressedUInt(ref ptr);
                                        }
                                    }
                                }
                            }
                            // Primitive types are single byte, already skipped
                        }

                        typeArgTotal += argSize;
                    }

                    // Add type argument sizes to base size
                    // For generic structs, type args contribute to the instance size
                    if (typeArgTotal > 0)
                    {
                        uint instantiatedSize = baseSize + typeArgTotal;
                        instantiatedSize = (instantiatedSize + 7) & ~7u;  // Align to 8
                        return instantiatedSize;
                    }
                }

                return baseSize;
            }

            // Unknown gen kind
            return 8;
        }

        if (sigLen < 2)
            return 8;

        // Decode compressed token
        uint token = 0;
        int bytesRead = 0;

        // Simple compressed integer decoding
        if ((typeSig[1] & 0x80) == 0)
        {
            token = typeSig[1];
            bytesRead = 1;
        }
        else if ((typeSig[1] & 0xC0) == 0x80)
        {
            if (sigLen < 3) return 8;
            token = ((uint)(typeSig[1] & 0x3F) << 8) | typeSig[2];
            bytesRead = 2;
        }
        else if ((typeSig[1] & 0xE0) == 0xC0)
        {
            if (sigLen < 5) return 8;
            token = ((uint)(typeSig[1] & 0x1F) << 24) | ((uint)typeSig[2] << 16) | ((uint)typeSig[3] << 8) | typeSig[4];
            bytesRead = 4;
        }
        else
        {
            return 8;
        }

        // Token is TypeDefOrRef coded index (2-bit table, rest is row)
        // Table: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        uint table = token & 0x03;
        uint row = token >> 2;

        if (row == 0)
            return 8;

        if (elementType == 0x12)  // CLASS
        {
            // Class fields are references (pointers)
            return 8;
        }

        // VALUETYPE - need to compute actual size
        if (table == 0)  // TypeDef in same assembly
        {
            return ComputeInstanceSize(asm, row, true);
        }
        else if (table == 1)  // TypeRef in another assembly
        {
            // Resolve TypeRef to TypeDef in target assembly
            LoadedAssembly* targetAsm;
            uint typeDefToken;
            if (ResolveTypeRefToTypeDef(asm, row, out targetAsm, out typeDefToken))
            {
                uint typeDefRow = typeDefToken & 0x00FFFFFF;
                if (targetAsm != null && typeDefRow > 0)
                {
                    return ComputeInstanceSize(targetAsm, typeDefRow, true);
                }
                else
                {
                    // DebugConsole.Write("[GetFieldTypeSize] TypeRef resolved but invalid: targetAsm=");
                    DebugConsole.WriteHex((ulong)targetAsm);
                    DebugConsole.Write(" typeDefRow=");
                    DebugConsole.WriteDecimal(typeDefRow);
                    DebugConsole.WriteLine();
                }
            }
            else
            {
                // DebugConsole.Write("[GetFieldTypeSize] FAILED to resolve TypeRef row=");
                DebugConsole.WriteDecimal(row);
                DebugConsole.Write(" in asm=");
                DebugConsole.WriteDecimal(asm->AssemblyId);
                DebugConsole.WriteLine();
            }
        }
        else if (table == 2)  // TypeSpec - needs more complex handling
        {
            // DebugConsole.Write("[GetFieldTypeSize] TypeSpec table not fully supported, row=");
            DebugConsole.WriteDecimal(row);
            DebugConsole.WriteLine();
        }

        return 8;  // Fallback
    }

    /// <summary>
    /// Get the size of a field type from its signature bytes.
    /// Public wrapper for MetadataIntegration to call during JIT compilation.
    /// </summary>
    /// <param name="assemblyId">Assembly ID from which the field signature originates</param>
    /// <param name="typeSig">Pointer to the type signature bytes (after the field signature calling convention byte)</param>
    /// <param name="sigLen">Length of the type signature</param>
    /// <returns>Size of the field in bytes</returns>
    public static uint GetFieldTypeSizeForAssembly(uint assemblyId, byte* typeSig, uint sigLen)
    {
        if (assemblyId == 0 || assemblyId > (uint)_assemblyCount)
            return 8;

        LoadedAssembly* asm = &_assemblies[assemblyId - 1];
        return GetFieldTypeSize(asm, typeSig, sigLen);
    }

    /// <summary>
    /// Resolve a TypeRef to the target assembly's TypeDef.
    /// </summary>
    private static MethodTable* ResolveTypeRef(LoadedAssembly* sourceAsm, uint typeRefToken)
    {
        // Get the TypeRef row
        uint rowId = typeRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return null;

        // Get resolution scope (coded index: ResolutionScope)
        CodedIndex resScope = MetadataReader.GetTypeRefResolutionScope(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Get the type name and namespace from the TypeRef
        uint nameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        uint nsIdx = MetadataReader.GetTypeRefNamespace(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        byte* name = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        byte* ns = MetadataReader.GetString(ref sourceAsm->Metadata, nsIdx);

        // Debug: Log the type being resolved
        // DebugConsole.Write("[AsmLoader] ResolveTypeRef 0x");
        // DebugConsole.WriteHex(typeRefToken);
        // DebugConsole.Write(" srcAsm=");
        // DebugConsole.WriteDecimal(sourceAsm->AssemblyId);
        // DebugConsole.Write(" scope=");
        // DebugConsole.WriteDecimal((uint)resScope.Table);
        // DebugConsole.Write(":");
        // DebugConsole.WriteDecimal(resScope.RowId);
        // DebugConsole.Write(" type=");
        // if (ns != null && ns[0] != 0)
        // {
        //     for (int i = 0; ns[i] != 0 && i < 24; i++)
        //         DebugConsole.WriteChar((char)ns[i]);
        //     DebugConsole.WriteChar('.');
        // }
        // if (name != null)
        // {
        //     for (int i = 0; name[i] != 0 && i < 24; i++)
        //         DebugConsole.WriteChar((char)name[i]);
        // }
        // DebugConsole.WriteLine();

        // ResolutionScope points to Module, ModuleRef, AssemblyRef, or TypeRef
        if (resScope.Table == MetadataTableId.AssemblyRef)
        {
            // Check for well-known primitive types that are type forwarders in System.Runtime
            // These types are defined in korlib (AOT kernel) and need special handling
            MethodTable* wellKnownMT = TryResolveWellKnownType(name, ns);
            if (wellKnownMT != null)
            {
                // DebugConsole.WriteLine("  -> well-known type");
                return wellKnownMT;
            }

            // Find the target assembly
            uint targetAsmId = ResolveAssemblyRef(sourceAsm, resScope.RowId);
            if (targetAsmId == InvalidAssemblyId)
            {
                DebugConsole.WriteLine("  -> FAILED: assembly ref not resolved");
                return null;
            }

            LoadedAssembly* targetAsm = GetAssembly(targetAsmId);
            if (targetAsm == null)
            {
                DebugConsole.Write("  -> FAILED: target asm ");
                DebugConsole.WriteDecimal(targetAsmId);
                DebugConsole.WriteLine(" not found");
                return null;
            }

            // DebugConsole.Write("  -> target asm ");
            // DebugConsole.WriteDecimal(targetAsmId);
            // DebugConsole.WriteLine("");

            // Find the matching TypeDef in the target assembly
            MethodTable* result = FindTypeDefByName(targetAsm, nameIdx, nsIdx, &sourceAsm->Metadata);
            if (result == null)
            {
                DebugConsole.WriteLine("  -> FAILED: type not found in target asm");
            }
            // else
            // {
            //     DebugConsole.Write("  -> resolved MT=0x");
            //     DebugConsole.WriteHex((ulong)result);
            //     DebugConsole.Write(" isVT=");
            //     DebugConsole.WriteDecimal(result->IsValueType ? 1u : 0u);
            //     DebugConsole.Write(" size=");
            //     DebugConsole.WriteDecimal(result->_uBaseSize);
            //     DebugConsole.WriteLine();
            // }
            return result;
        }

        // TypeRef resolution scope means this is a nested type
        // The resolution scope points to the enclosing type's TypeRef
        if (resScope.Table == MetadataTableId.TypeRef)
        {
            // Get the nested type name from the TypeRef
            // We already have name and ns from above

            // DebugConsole.Write("[AsmLoader] ResolveTypeRef nested: ");
            // for (int k = 0; name[k] != 0 && k < 32; k++)
            //     DebugConsole.WriteChar((char)name[k]);
            // DebugConsole.Write(" enclosing TypeRef row=");
            // DebugConsole.WriteDecimal(resScope.RowId);
            // DebugConsole.WriteLine();

            // Recursively resolve the enclosing type
            uint enclosingTypeRefToken = 0x01000000 | resScope.RowId;
            MethodTable* enclosingMT = ResolveTypeRef(sourceAsm, enclosingTypeRefToken);
            if (enclosingMT == null)
            {
                // DebugConsole.WriteLine("[AsmLoader] Failed to resolve enclosing type for nested type");
                return null;
            }

            // Find which assembly owns the enclosing type
            LoadedAssembly* targetAsm = FindAssemblyOwningMethodTable(enclosingMT);
            if (targetAsm == null)
            {
                // DebugConsole.WriteLine("[AsmLoader] Failed to find assembly owning enclosing type");
                return null;
            }

            // Find the enclosing type's TypeDef row in the target assembly
            uint enclosingRow = FindTypeDefRowForMethodTable(targetAsm, enclosingMT);
            if (enclosingRow == 0)
            {
                // DebugConsole.WriteLine("[AsmLoader] Failed to find TypeDef for enclosing type");
                return null;
            }

            // Search NestedClass table for a nested type with matching name
            uint nestedClassCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.NestedClass];
            for (uint ncRow = 1; ncRow <= nestedClassCount; ncRow++)
            {
                uint nestedTypeRow = MetadataReader.GetNestedClassNestedClass(ref targetAsm->Tables, ref targetAsm->Sizes, ncRow);
                uint enclosingTypeRow = MetadataReader.GetNestedClassEnclosingClass(ref targetAsm->Tables, ref targetAsm->Sizes, ncRow);

                // Check if this nested type belongs to our enclosing type
                if (enclosingTypeRow != enclosingRow)
                    continue;

                // Check if the nested type name matches
                uint nestedDefNameIdx = MetadataReader.GetTypeDefName(ref targetAsm->Tables, ref targetAsm->Sizes, nestedTypeRow);
                byte* nestedDefName = MetadataReader.GetString(ref targetAsm->Metadata, nestedDefNameIdx);

                if (StringsEqual(name, nestedDefName))
                {
                    // Found the nested type - resolve/create its MethodTable
                    uint nestedTypeDefToken = 0x02000000 | nestedTypeRow;
                    MethodTable* nestedMT = ResolveType(targetAsm->AssemblyId, nestedTypeDefToken);
                    // DebugConsole.Write("[AsmLoader] Found nested type MT=0x");
                    // DebugConsole.WriteHex((ulong)nestedMT);
                    // DebugConsole.Write(" isVT=");
                    // DebugConsole.WriteDecimal(nestedMT != null && nestedMT->IsValueType ? 1u : 0u);
                    // DebugConsole.WriteLine();
                    return nestedMT;
                }
            }

            DebugConsole.Write("[AsmLoader] Nested type not found: ");
            for (int k = 0; name[k] != 0 && k < 32; k++)
                DebugConsole.WriteChar((char)name[k]);
            DebugConsole.Write(" in enclosing MT=0x");
            DebugConsole.WriteHex((ulong)enclosingMT);
            DebugConsole.WriteLine();
            return null;
        }

        // Other resolution scopes (Module, ModuleRef) not yet implemented
        DebugConsole.Write("  -> FAILED: unsupported scope ");
        DebugConsole.WriteDecimal((uint)resScope.Table);
        DebugConsole.WriteLine();
        return null;
    }

    /// <summary>
    /// Find which assembly owns a given MethodTable.
    /// </summary>
    private static LoadedAssembly* FindAssemblyOwningMethodTable(MethodTable* mt)
    {
        // For AOT types (kernel), they belong to kernel assembly
        // For JIT types, we need to search through assemblies

        // Check kernel assembly first
        LoadedAssembly* kernelAsm = GetAssembly(KernelAssemblyId);
        if (kernelAsm != null)
        {
            // Kernel types don't use the registry - they're AOT compiled
            // Check if the MT pointer is in the kernel's type registry
            MethodTable* found = kernelAsm->Types.Lookup(0x02000001);  // Just check if we can lookup any types
            if (found == null)
            {
                // Kernel doesn't use type registry, so this might be a kernel type
                // For simplicity, assume kernel types are at low addresses
                // Actually, check each loaded assembly
            }
        }

        // Search each loaded assembly's type registry
        for (uint asmId = 1; asmId <= _assemblyCount; asmId++)
        {
            LoadedAssembly* asm = GetAssembly(asmId);
            if (asm == null)
                continue;

            // Check if this assembly's type registry contains this MT
            // We need to iterate through all TypeDef tokens and check
            uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
            for (uint row = 1; row <= typeDefCount; row++)
            {
                uint token = 0x02000000 | row;
                MethodTable* regMT = asm->Types.Lookup(token);
                if (regMT == mt)
                    return asm;
            }
        }

        // Not found - might be a kernel AOT type
        // For kernel types, we can return the korlib assembly
        return GetAssembly(2);  // korlib is typically assembly 2
    }

    /// <summary>
    /// Find the TypeDef row for a MethodTable in a given assembly.
    /// </summary>
    private static uint FindTypeDefRowForMethodTable(LoadedAssembly* asm, MethodTable* mt)
    {
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        for (uint row = 1; row <= typeDefCount; row++)
        {
            uint token = 0x02000000 | row;
            MethodTable* regMT = asm->Types.Lookup(token);
            if (regMT == mt)
                return row;
        }
        return 0;
    }

    /// <summary>
    /// Resolve a TypeRef to a TypeDef token and target assembly without requiring a MethodTable.
    /// This is used for JIT-compiled assemblies where types don't have pre-allocated MethodTables.
    /// </summary>
    private static bool ResolveTypeRefToTypeDef(LoadedAssembly* sourceAsm, uint typeRefRow,
                                                 out LoadedAssembly* targetAsm, out uint typeDefToken)
    {
        targetAsm = null;
        typeDefToken = 0;

        if (typeRefRow == 0)
            return false;

        // Get resolution scope (coded index: ResolutionScope)
        CodedIndex resScope = MetadataReader.GetTypeRefResolutionScope(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);

        // ResolutionScope points to Module, ModuleRef, AssemblyRef, or TypeRef
        if (resScope.Table == MetadataTableId.AssemblyRef)
        {
            // Get the type name and namespace from the TypeRef BEFORE resolving assembly
            uint nameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);
            uint nsIdx = MetadataReader.GetTypeRefNamespace(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);
            byte* typeName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
            byte* typeNs = MetadataReader.GetString(ref sourceAsm->Metadata, nsIdx);

            // Debug: Print what we're looking up
            DebugConsole.Write("[AsmLoader] ResolveTypeRef row=");
            DebugConsole.WriteDecimal(typeRefRow);
            DebugConsole.Write(" asmRef=");
            DebugConsole.WriteDecimal(resScope.RowId);
            DebugConsole.Write(" type=");
            if (typeNs != null && typeNs[0] != 0)
            {
                for (int i = 0; typeNs[i] != 0 && i < 32; i++)
                    DebugConsole.WriteChar((char)typeNs[i]);
                DebugConsole.WriteChar('.');
            }
            if (typeName != null)
            {
                for (int i = 0; typeName[i] != 0 && i < 32; i++)
                    DebugConsole.WriteChar((char)typeName[i]);
            }
            DebugConsole.WriteLine();

            // Find the target assembly
            uint targetAsmId = ResolveAssemblyRef(sourceAsm, resScope.RowId);
            if (targetAsmId == InvalidAssemblyId)
                return false;

            targetAsm = GetAssembly(targetAsmId);
            if (targetAsm == null)
                return false;

            // Get the name strings
            byte* targetName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
            byte* targetNs = MetadataReader.GetString(ref sourceAsm->Metadata, nsIdx);

            if (targetName == null)
                return false;

            // PRIORITY: Check well-known types FIRST, before searching the target assembly
            // This ensures korlib types like System.Delegate are always resolved to our
            // MethodTables even when System.Runtime has its own TypeDef for them
            if (IsSystemNamespace(targetNs))
            {
                uint wellKnownToken = GetWellKnownTypeToken(targetName);
                if (wellKnownToken != 0)
                {
                    DebugConsole.Write("[AsmLoader] WellKnown type resolve: ");
                    for (int i = 0; targetName[i] != 0 && i < 32; i++)
                        DebugConsole.WriteChar((char)targetName[i]);
                    DebugConsole.Write(" -> 0x");
                    DebugConsole.WriteHex(wellKnownToken);
                    DebugConsole.WriteLine();
                    targetAsm = GetAssembly(KernelAssemblyId);  // Well-known types belong to kernel
                    typeDefToken = wellKnownToken;
                    return true;
                }
            }

            // Find the matching TypeDef in the target assembly by name
            uint typeDefCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

            // Debug: Show TypeDef search parameters for System.Reflection namespace
            if (targetNs != null && targetNs[0] == 'S' && targetNs[7] == 'R' && targetNs[8] == 'e')
            {
                DebugConsole.Write("[AsmLoader] Searching asm ");
                DebugConsole.WriteDecimal(targetAsm->AssemblyId);
                DebugConsole.Write(" TypeDef table (");
                DebugConsole.WriteDecimal(typeDefCount);
                DebugConsole.Write(" rows) for ");
                if (targetNs != null && targetNs[0] != 0)
                {
                    for (int i = 0; targetNs[i] != 0 && i < 32; i++)
                        DebugConsole.WriteChar((char)targetNs[i]);
                    DebugConsole.WriteChar('.');
                }
                if (targetName != null)
                {
                    for (int i = 0; targetName[i] != 0 && i < 32; i++)
                        DebugConsole.WriteChar((char)targetName[i]);
                }
                DebugConsole.WriteLine();
            }

            // Debug: Check if looking for MethodBase
            bool debugMethodBase = targetName != null && targetName[0] == 'M' && targetName[1] == 'e' &&
                                   targetName[2] == 't' && targetName[3] == 'h' && targetName[4] == 'o' &&
                                   targetName[5] == 'd' && targetName[6] == 'B';

            for (uint row = 1; row <= typeDefCount; row++)
            {
                uint defNameIdx = MetadataReader.GetTypeDefName(ref targetAsm->Tables, ref targetAsm->Sizes, row);
                uint defNsIdx = MetadataReader.GetTypeDefNamespace(ref targetAsm->Tables, ref targetAsm->Sizes, row);

                byte* defName = MetadataReader.GetString(ref targetAsm->Metadata, defNameIdx);
                byte* defNs = MetadataReader.GetString(ref targetAsm->Metadata, defNsIdx);

                // Compare names
                if (!StringsEqual(targetName, defName))
                    continue;

                // Debug: Found name match for MethodBase
                if (debugMethodBase)
                {
                    DebugConsole.Write("[AsmLoader] Name match row ");
                    DebugConsole.WriteDecimal(row);
                    DebugConsole.Write(" defNs='");
                    if (defNs != null)
                        for (int i = 0; defNs[i] != 0 && i < 32; i++)
                            DebugConsole.WriteChar((char)defNs[i]);
                    DebugConsole.Write("' targetNs='");
                    if (targetNs != null)
                        for (int i = 0; targetNs[i] != 0 && i < 32; i++)
                            DebugConsole.WriteChar((char)targetNs[i]);
                    DebugConsole.Write("' StringsEqual=");
                    DebugConsole.Write(StringsEqual(targetNs, defNs) ? "Y" : "N");
                    DebugConsole.WriteLine();
                }

                // Compare namespaces (both null/empty is a match)
                bool nsMatch = false;
                if ((targetNs == null || targetNs[0] == 0) && (defNs == null || defNs[0] == 0))
                    nsMatch = true;
                else if (targetNs != null && defNs != null && StringsEqual(targetNs, defNs))
                    nsMatch = true;

                if (nsMatch)
                {
                    typeDefToken = 0x02000000 | row;
                    return true;
                }
            }

            // Type not found in target assembly - check for well-known korlib types
            // This handles type forwarding where TypeRefs point to System.Runtime but the
            // actual type is defined in korlib (e.g., System.String, System.Object, etc.)
            if (IsSystemNamespace(targetNs))
            {
                // Check if this is a well-known type from the AOT kernel
                // Well-known types have synthetic tokens (0xF0xxxxxx) and don't have
                // searchable metadata tables - use the token directly
                uint wellKnownToken = GetWellKnownTypeToken(targetName);
                if (wellKnownToken != 0)
                {
                    // Return the well-known token directly - these are synthetic tokens
                    // registered by MetadataIntegration.RegisterWellKnownTypes() that map
                    // to MethodTables extracted from live AOT objects
                    targetAsm = GetAssembly(KernelAssemblyId);  // Well-known types belong to kernel
                    typeDefToken = wellKnownToken;
                    return true;
                }
            }

            // Debug: print what type wasn't found
            DebugConsole.Write("[AsmLoader] TypeRef not found: ");
            if (targetNs != null && targetNs[0] != 0)
            {
                for (int i = 0; targetNs[i] != 0 && i < 64; i++)
                    DebugConsole.WriteChar((char)targetNs[i]);
                DebugConsole.WriteChar('.');
            }
            if (targetName != null)
            {
                for (int i = 0; targetName[i] != 0 && i < 64; i++)
                    DebugConsole.WriteChar((char)targetName[i]);
            }
            DebugConsole.Write(" in ");
            if (targetAsm->Name != null)
            {
                for (int i = 0; targetAsm->Name[i] != 0 && i < 64; i++)
                    DebugConsole.WriteChar((char)targetAsm->Name[i]);
            }
            DebugConsole.WriteLine();
            return false;  // Type not found in target assembly
        }

        // TypeRef resolution scope means this is a nested type
        // The resolution scope points to the enclosing type's TypeRef
        if (resScope.Table == MetadataTableId.TypeRef)
        {
            // Get the nested type name from the TypeRef
            uint nestedNameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);
            byte* nestedName = MetadataReader.GetString(ref sourceAsm->Metadata, nestedNameIdx);

            if (nestedName == null)
                return false;

            DebugConsole.Write("[AsmLoader] ResolveTypeRef nested type: ");
            for (int i = 0; nestedName[i] != 0 && i < 32; i++)
                DebugConsole.WriteChar((char)nestedName[i]);
            DebugConsole.Write(" enclosing TypeRef row=");
            DebugConsole.WriteDecimal(resScope.RowId);
            DebugConsole.WriteLine();

            // Recursively resolve the enclosing type
            LoadedAssembly* enclosingAsm = null;
            uint enclosingToken = 0;
            if (!ResolveTypeRefToTypeDef(sourceAsm, resScope.RowId, out enclosingAsm, out enclosingToken))
            {
                DebugConsole.WriteLine("[AsmLoader] Failed to resolve enclosing type for nested type");
                return false;
            }

            // Now find the nested type within the enclosing type
            // Nested types are listed in the NestedClass table
            targetAsm = enclosingAsm;
            uint enclosingRow = enclosingToken & 0x00FFFFFF;

            // Search NestedClass table for a nested type with matching name
            uint nestedClassCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.NestedClass];
            for (uint ncRow = 1; ncRow <= nestedClassCount; ncRow++)
            {
                uint nestedTypeRow = MetadataReader.GetNestedClassNestedClass(ref targetAsm->Tables, ref targetAsm->Sizes, ncRow);
                uint enclosingTypeRow = MetadataReader.GetNestedClassEnclosingClass(ref targetAsm->Tables, ref targetAsm->Sizes, ncRow);

                // Check if this nested type belongs to our enclosing type
                if (enclosingTypeRow != enclosingRow)
                    continue;

                // Check if the nested type name matches
                uint nestedDefNameIdx = MetadataReader.GetTypeDefName(ref targetAsm->Tables, ref targetAsm->Sizes, nestedTypeRow);
                byte* nestedDefName = MetadataReader.GetString(ref targetAsm->Metadata, nestedDefNameIdx);

                if (StringsEqual(nestedName, nestedDefName))
                {
                    typeDefToken = 0x02000000 | nestedTypeRow;
                    DebugConsole.Write("[AsmLoader] Found nested type at row ");
                    DebugConsole.WriteDecimal(nestedTypeRow);
                    DebugConsole.Write(" -> token 0x");
                    DebugConsole.WriteHex(typeDefToken);
                    DebugConsole.WriteLine();
                    return true;
                }
            }

            DebugConsole.Write("[AsmLoader] Nested type not found: ");
            for (int i = 0; nestedName[i] != 0 && i < 32; i++)
                DebugConsole.WriteChar((char)nestedName[i]);
            DebugConsole.Write(" in enclosing type 0x");
            DebugConsole.WriteHex(enclosingToken);
            DebugConsole.WriteLine();
            return false;
        }

        // Other resolution scopes (Module, ModuleRef) not yet implemented
        return false;
    }

    /// <summary>
    /// Resolve an AssemblyRef row to a loaded assembly ID.
    /// </summary>
    private static uint ResolveAssemblyRef(LoadedAssembly* sourceAsm, uint assemblyRefRow)
    {
        if (assemblyRefRow == 0)
            return InvalidAssemblyId;

        // Check if already resolved in dependencies array
        if (assemblyRefRow <= sourceAsm->DependencyCount &&
            sourceAsm->Dependencies[assemblyRefRow - 1] != InvalidAssemblyId)
        {
            uint cachedId = sourceAsm->Dependencies[assemblyRefRow - 1];
            // DebugConsole.Write("[AsmLoader] ResolveAsmRef row=");
            // DebugConsole.WriteDecimal(assemblyRefRow);
            // DebugConsole.Write(" -> cached asm ");
            // DebugConsole.WriteDecimal(cachedId);
            // DebugConsole.WriteLine();
            return cachedId;
        }

        // Get the assembly name from AssemblyRef table
        uint nameIdx = MetadataReader.GetAssemblyRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, assemblyRefRow);
        byte* name = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);

        if (name == null)
            return InvalidAssemblyId;

        // Debug: Print what we're looking up
        // DebugConsole.Write("[AsmLoader] ResolveAsmRef row=");
        // DebugConsole.WriteDecimal(assemblyRefRow);
        // DebugConsole.Write(" name='");
        // for (int i = 0; name[i] != 0 && i < 32; i++)
        //     DebugConsole.WriteChar((char)name[i]);
        // DebugConsole.Write("'");

        // Find loaded assembly by name
        LoadedAssembly* target = FindByName(name);

        // Virtual assembly mapping: System.Runtime, System.Private.CoreLib, netstandard → korlib
        // Only applies when the target assembly is NOT loaded (fallback to korlib)
        // This allows removing System.Runtime.dll while keeping type resolution working
        if (target == null && IsVirtualAssembly(name))
        {
            // Fallback to korlib when System.Runtime is not loaded
            LoadedAssembly* korlib = GetCoreLib();
            if (korlib != null)
            {
                // Cache the resolution
                if (assemblyRefRow <= LoadedAssembly.MaxDependencies)
                {
                    sourceAsm->Dependencies[assemblyRefRow - 1] = korlib->AssemblyId;
                    if (assemblyRefRow > sourceAsm->DependencyCount)
                        sourceAsm->DependencyCount = (ushort)assemblyRefRow;
                }
                return korlib->AssemblyId;
            }
        }
        if (target == null)
        {
            DebugConsole.WriteLine("[AsmLoader] ResolveAsmRef NOT FOUND");
            return InvalidAssemblyId;
        }

        // DebugConsole.Write(" -> found asm ");
        // DebugConsole.WriteDecimal(target->AssemblyId);
        // DebugConsole.WriteLine();

        // Cache the resolution
        if (assemblyRefRow <= LoadedAssembly.MaxDependencies)
        {
            sourceAsm->Dependencies[assemblyRefRow - 1] = target->AssemblyId;
            if (assemblyRefRow > sourceAsm->DependencyCount)
                sourceAsm->DependencyCount = (ushort)assemblyRefRow;
        }

        return target->AssemblyId;
    }

    /// <summary>
    /// Check if an assembly name is a "virtual" assembly that should redirect to korlib.
    /// Virtual assemblies: System.Runtime, System.Private.CoreLib, netstandard, System.Threading, System.Collections
    /// </summary>
    private static bool IsVirtualAssembly(byte* name)
    {
        if (name == null)
            return false;

        // Check for "System.*" assemblies
        if (name[0] == 'S' && name[1] == 'y' && name[2] == 's' && name[3] == 't' &&
            name[4] == 'e' && name[5] == 'm' && name[6] == '.')
        {
            // "System.Runtime"
            if (name[7] == 'R' && name[8] == 'u' && name[9] == 'n' && name[10] == 't' &&
                name[11] == 'i' && name[12] == 'm' && name[13] == 'e' && name[14] == 0)
                return true;

            // "System.Private.CoreLib"
            if (name[7] == 'P' && name[8] == 'r' && name[9] == 'i' && name[10] == 'v' &&
                name[11] == 'a' && name[12] == 't' && name[13] == 'e' && name[14] == '.' &&
                name[15] == 'C' && name[16] == 'o' && name[17] == 'r' && name[18] == 'e' &&
                name[19] == 'L' && name[20] == 'i' && name[21] == 'b' && name[22] == 0)
                return true;

            // "System.Threading"
            if (name[7] == 'T' && name[8] == 'h' && name[9] == 'r' && name[10] == 'e' &&
                name[11] == 'a' && name[12] == 'd' && name[13] == 'i' && name[14] == 'n' &&
                name[15] == 'g' && name[16] == 0)
                return true;

            // "System.Collections"
            if (name[7] == 'C' && name[8] == 'o' && name[9] == 'l' && name[10] == 'l' &&
                name[11] == 'e' && name[12] == 'c' && name[13] == 't' && name[14] == 'i' &&
                name[15] == 'o' && name[16] == 'n' && name[17] == 's' && name[18] == 0)
                return true;
        }

        // Check for "netstandard"
        if (name[0] == 'n' && name[1] == 'e' && name[2] == 't' && name[3] == 's' &&
            name[4] == 't' && name[5] == 'a' && name[6] == 'n' && name[7] == 'd' &&
            name[8] == 'a' && name[9] == 'r' && name[10] == 'd' && name[11] == 0)
            return true;

        return false;
    }

    /// <summary>
    /// Get the loaded CoreLib assembly (korlib).
    /// </summary>
    public static LoadedAssembly* GetCoreLib()
    {
        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (_assemblies[i].IsLoaded && _assemblies[i].IsCoreLib)
                return &_assemblies[i];
        }
        return null;
    }

    /// <summary>
    /// Find a TypeDef in an assembly by name and namespace.
    /// </summary>
    private static MethodTable* FindTypeDefByName(LoadedAssembly* asm, uint nameIdx, uint nsIdx,
                                                   MetadataRoot* sourceMetadata)
    {
        // Get the name strings from the source metadata (where the TypeRef lives)
        byte* targetName = MetadataReader.GetString(ref *sourceMetadata, nameIdx);
        byte* targetNs = MetadataReader.GetString(ref *sourceMetadata, nsIdx);

        if (targetName == null)
            return null;

        // Search TypeDef table in target assembly
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeDefCount; row++)
        {
            uint defNameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            uint defNsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, row);

            byte* defName = MetadataReader.GetString(ref asm->Metadata, defNameIdx);
            byte* defNs = MetadataReader.GetString(ref asm->Metadata, defNsIdx);

            // Compare names
            if (!StringsEqual(targetName, defName))
                continue;

            // Compare namespaces (both null/empty is a match)
            bool nsMatch = false;
            if ((targetNs == null || targetNs[0] == 0) && (defNs == null || defNs[0] == 0))
                nsMatch = true;
            else if (targetNs != null && defNs != null && StringsEqual(targetNs, defNs))
                nsMatch = true;

            if (nsMatch)
            {
                // Found it - return the MethodTable from the type registry
                uint typeDefToken = 0x02000000 | row;
                MethodTable* mt = asm->Types.Lookup(typeDefToken);
                if (mt != null)
                    return mt;
                // Type not registered yet - create on-demand for JIT assemblies
                mt = CreateTypeDefMethodTable(asm, typeDefToken);
                if (mt == null)
                {
                    DebugConsole.Write("[FindTypeDef] CreateTypeDefMethodTable failed for token 0x");
                    DebugConsole.WriteHex(typeDefToken);
                    DebugConsole.Write(" asm ");
                    DebugConsole.WriteDecimal(asm->AssemblyId);
                    DebugConsole.WriteLine();
                }
                return mt;
            }
        }

        // Type not found in target assembly - check for well-known AOT types
        // This handles cases where TypeRefs point to System.Runtime but the type
        // is actually defined in korlib (AOT-compiled into the kernel)
        return TryResolveWellKnownType(targetName, targetNs);
    }

    /// <summary>
    /// Try to resolve a type name to a well-known AOT type from korlib.
    /// This handles primitive types like System.Int32, System.String, etc.
    /// </summary>
    private static MethodTable* TryResolveWellKnownType(byte* name, byte* ns)
    {
        // Only handle System namespace
        if (ns == null || !IsSystemNamespace(ns))
            return null;

        // Check for well-known primitive types
        uint wellKnownToken = GetWellKnownTypeToken(name);

        if (wellKnownToken != 0)
            return JIT.MetadataIntegration.LookupType(wellKnownToken);

        return null;
    }

    /// <summary>
    /// Check if the namespace is "System" or starts with "System."
    /// This covers System, System.Collections, System.Collections.Generic, etc.
    /// </summary>
    private static bool IsSystemNamespace(byte* ns)
    {
        // Must start with "System"
        if (!(ns[0] == 'S' && ns[1] == 'y' && ns[2] == 's' && ns[3] == 't' &&
              ns[4] == 'e' && ns[5] == 'm'))
            return false;

        // Either exactly "System" or "System.*"
        return ns[6] == 0 || ns[6] == '.';
    }

    /// <summary>
    /// Check if the name is "ValueType".
    /// </summary>
    private static bool IsValueTypeName(byte* name)
    {
        // "ValueType" = V a l u e T y p e \0
        return name[0] == 'V' && name[1] == 'a' && name[2] == 'l' && name[3] == 'u' &&
               name[4] == 'e' && name[5] == 'T' && name[6] == 'y' && name[7] == 'p' &&
               name[8] == 'e' && name[9] == 0;
    }

    /// <summary>
    /// Check if the name is "Enum".
    /// </summary>
    private static bool IsEnumName(byte* name)
    {
        // "Enum" = E n u m \0
        return name[0] == 'E' && name[1] == 'n' && name[2] == 'u' && name[3] == 'm' && name[4] == 0;
    }

    /// <summary>
    /// Check if the name is "Object".
    /// </summary>
    private static bool IsObjectName(byte* name)
    {
        // "Object" = O b j e c t \0
        return name[0] == 'O' && name[1] == 'b' && name[2] == 'j' && name[3] == 'e' &&
               name[4] == 'c' && name[5] == 't' && name[6] == 0;
    }

    /// <summary>
    /// Check if the name is "Nullable`1".
    /// </summary>
    private static bool IsNullableName(byte* name)
    {
        // "Nullable`1" = N u l l a b l e ` 1 \0
        return name[0] == 'N' && name[1] == 'u' && name[2] == 'l' && name[3] == 'l' &&
               name[4] == 'a' && name[5] == 'b' && name[6] == 'l' && name[7] == 'e' &&
               name[8] == '`' && name[9] == '1' && name[10] == 0;
    }

    /// <summary>
    /// Check if the name is "MulticastDelegate".
    /// </summary>
    private static bool IsMulticastDelegateName(byte* name)
    {
        // "MulticastDelegate" = M u l t i c a s t D e l e g a t e \0
        return name[0] == 'M' && name[1] == 'u' && name[2] == 'l' && name[3] == 't' &&
               name[4] == 'i' && name[5] == 'c' && name[6] == 'a' && name[7] == 's' &&
               name[8] == 't' && name[9] == 'D' && name[10] == 'e' && name[11] == 'l' &&
               name[12] == 'e' && name[13] == 'g' && name[14] == 'a' && name[15] == 't' &&
               name[16] == 'e' && name[17] == 0;
    }

    /// <summary>
    /// Check if a generic definition token refers to System.Nullable`1.
    /// The token format is normalized: (assemblyId << 24) | typeDefRow
    /// </summary>
    private static bool IsNullableGenericDef(uint genDefToken)
    {
        // Normalized token format: (assemblyId << 24) | typeDefRow
        uint asmId = genDefToken >> 24;
        uint typeDefRow = genDefToken & 0x00FFFFFF;

        if (asmId == 0 || typeDefRow == 0)
            return false;

        LoadedAssembly* asm = GetAssembly(asmId);
        if (asm == null)
            return false;

        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        if (typeDefRow > typeDefCount)
            return false;

        uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, typeDefRow);
        uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, typeDefRow);
        byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);
        byte* ns = MetadataReader.GetString(ref asm->Metadata, nsIdx);

        return IsSystemNamespace(ns) && IsNullableName(name);
    }

    /// <summary>
    /// Get the well-known type token for a type name in the System namespace.
    /// </summary>
    private static uint GetWellKnownTypeToken(byte* name)
    {
        if (name == null || name[0] == 0)
            return 0;

        // Dispatch based on first character for efficiency
        switch (name[0])
        {
            case (byte)'I':  // Int32, Int64, Int16, IntPtr, IDisposable, IndexOutOfRangeException, InvalidOperationException, InvalidCastException
                if (name[1] == 'n' && name[2] == 't')
                {
                    if (name[3] == '3' && name[4] == '2' && name[5] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.Int32;
                    if (name[3] == '6' && name[4] == '4' && name[5] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.Int64;
                    if (name[3] == '1' && name[4] == '6' && name[5] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.Int16;
                    if (name[3] == 'P' && name[4] == 't' && name[5] == 'r' && name[6] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.IntPtr;
                }
                // IDisposable - for using statement support
                if (name[1] == 'D' && name[2] == 'i' && name[3] == 's' && name[4] == 'p' &&
                    name[5] == 'o' && name[6] == 's' && name[7] == 'a' && name[8] == 'b' &&
                    name[9] == 'l' && name[10] == 'e' && name[11] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.IDisposable;
                // IndexOutOfRangeException
                if (name[1] == 'n' && name[2] == 'd' && name[3] == 'e' && name[4] == 'x' &&
                    name[5] == 'O' && name[6] == 'u' && name[7] == 't' && name[8] == 'O' &&
                    name[9] == 'f' && name[10] == 'R' && name[11] == 'a' && name[12] == 'n' &&
                    name[13] == 'g' && name[14] == 'e' && name[15] == 'E' && name[16] == 'x' &&
                    name[17] == 'c' && name[18] == 'e' && name[19] == 'p' && name[20] == 't' &&
                    name[21] == 'i' && name[22] == 'o' && name[23] == 'n' && name[24] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.IndexOutOfRangeException;
                // InvalidOperationException
                if (name[1] == 'n' && name[2] == 'v' && name[3] == 'a' && name[4] == 'l' &&
                    name[5] == 'i' && name[6] == 'd' && name[7] == 'O' && name[8] == 'p' &&
                    name[9] == 'e' && name[10] == 'r' && name[11] == 'a' && name[12] == 't' &&
                    name[13] == 'i' && name[14] == 'o' && name[15] == 'n' && name[16] == 'E' &&
                    name[17] == 'x' && name[18] == 'c' && name[19] == 'e' && name[20] == 'p' &&
                    name[21] == 't' && name[22] == 'i' && name[23] == 'o' && name[24] == 'n' && name[25] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.InvalidOperationException;
                // InvalidCastException
                if (name[1] == 'n' && name[2] == 'v' && name[3] == 'a' && name[4] == 'l' &&
                    name[5] == 'i' && name[6] == 'd' && name[7] == 'C' && name[8] == 'a' &&
                    name[9] == 's' && name[10] == 't' && name[11] == 'E' && name[12] == 'x' &&
                    name[13] == 'c' && name[14] == 'e' && name[15] == 'p' && name[16] == 't' &&
                    name[17] == 'i' && name[18] == 'o' && name[19] == 'n' && name[20] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.InvalidCastException;
                // Note: IEnumerable/IEnumerator are NOT well-known types.
                // They're interfaces defined in korlib that need metadata resolution
                // for proper interface dispatch. They're resolved via korlib fallback.
                break;

            case (byte)'U':  // UInt32, UInt64, UInt16, UIntPtr
                if (name[1] == 'I' && name[2] == 'n' && name[3] == 't')
                {
                    if (name[4] == '3' && name[5] == '2' && name[6] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.UInt32;
                    if (name[4] == '6' && name[5] == '4' && name[6] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.UInt64;
                    if (name[4] == '1' && name[5] == '6' && name[6] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.UInt16;
                    if (name[4] == 'P' && name[5] == 't' && name[6] == 'r' && name[7] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.UIntPtr;
                }
                break;

            case (byte)'B':  // Byte, Boolean
                if (name[1] == 'y' && name[2] == 't' && name[3] == 'e' && name[4] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Byte;
                if (name[1] == 'o' && name[2] == 'o' && name[3] == 'l' && name[4] == 'e' &&
                    name[5] == 'a' && name[6] == 'n' && name[7] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Boolean;
                break;

            case (byte)'S':  // SByte, Single, String, Span`1, StackOverflowException
                if (name[1] == 'B' && name[2] == 'y' && name[3] == 't' && name[4] == 'e' && name[5] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.SByte;
                if (name[1] == 'i' && name[2] == 'n' && name[3] == 'g' && name[4] == 'l' &&
                    name[5] == 'e' && name[6] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Single;
                if (name[1] == 't' && name[2] == 'r' && name[3] == 'i' && name[4] == 'n' &&
                    name[5] == 'g' && name[6] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.String;
                // Span`1
                if (name[1] == 'p' && name[2] == 'a' && name[3] == 'n' && name[4] == '`' &&
                    name[5] == '1' && name[6] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Span;
                // StackOverflowException
                if (name[1] == 't' && name[2] == 'a' && name[3] == 'c' && name[4] == 'k' &&
                    name[5] == 'O' && name[6] == 'v' && name[7] == 'e' && name[8] == 'r' &&
                    name[9] == 'f' && name[10] == 'l' && name[11] == 'o' && name[12] == 'w' &&
                    name[13] == 'E' && name[14] == 'x' && name[15] == 'c' && name[16] == 'e' &&
                    name[17] == 'p' && name[18] == 't' && name[19] == 'i' && name[20] == 'o' &&
                    name[21] == 'n' && name[22] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.StackOverflowException;
                break;

            case (byte)'C':  // Char
                if (name[1] == 'h' && name[2] == 'a' && name[3] == 'r' && name[4] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Char;
                break;

            case (byte)'D':  // Double, Delegate, DivideByZeroException
                if (name[1] == 'o' && name[2] == 'u' && name[3] == 'b' && name[4] == 'l' &&
                    name[5] == 'e' && name[6] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Double;
                // Delegate
                if (name[1] == 'e' && name[2] == 'l' && name[3] == 'e' && name[4] == 'g' &&
                    name[5] == 'a' && name[6] == 't' && name[7] == 'e' && name[8] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Delegate;
                // DivideByZeroException
                if (name[1] == 'i' && name[2] == 'v' && name[3] == 'i' && name[4] == 'd' &&
                    name[5] == 'e' && name[6] == 'B' && name[7] == 'y' && name[8] == 'Z' &&
                    name[9] == 'e' && name[10] == 'r' && name[11] == 'o' && name[12] == 'E' &&
                    name[13] == 'x' && name[14] == 'c' && name[15] == 'e' && name[16] == 'p' &&
                    name[17] == 't' && name[18] == 'i' && name[19] == 'o' && name[20] == 'n' && name[21] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.DivideByZeroException;
                break;

            case (byte)'O':  // Object, OverflowException
                if (name[1] == 'b' && name[2] == 'j' && name[3] == 'e' && name[4] == 'c' &&
                    name[5] == 't' && name[6] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Object;
                // OverflowException
                if (name[1] == 'v' && name[2] == 'e' && name[3] == 'r' && name[4] == 'f' &&
                    name[5] == 'l' && name[6] == 'o' && name[7] == 'w' && name[8] == 'E' &&
                    name[9] == 'x' && name[10] == 'c' && name[11] == 'e' && name[12] == 'p' &&
                    name[13] == 't' && name[14] == 'i' && name[15] == 'o' && name[16] == 'n' && name[17] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.OverflowException;
                break;

            case (byte)'E':  // Exception, Enum
                if (name[1] == 'x' && name[2] == 'c' && name[3] == 'e' && name[4] == 'p' &&
                    name[5] == 't' && name[6] == 'i' && name[7] == 'o' && name[8] == 'n' && name[9] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Exception;
                // Enum
                if (name[1] == 'n' && name[2] == 'u' && name[3] == 'm' && name[4] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Enum;
                break;

            case (byte)'A':  // AggregateException, ArgumentException, ArgumentNullException, ArgumentOutOfRangeException, ArgIterator, Array
                // AggregateException
                if (name[1] == 'g' && name[2] == 'g' && name[3] == 'r' && name[4] == 'e' &&
                    name[5] == 'g' && name[6] == 'a' && name[7] == 't' && name[8] == 'e' &&
                    name[9] == 'E' && name[10] == 'x' && name[11] == 'c' && name[12] == 'e' &&
                    name[13] == 'p' && name[14] == 't' && name[15] == 'i' && name[16] == 'o' &&
                    name[17] == 'n' && name[18] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.AggregateException;
                // Array
                if (name[1] == 'r' && name[2] == 'r' && name[3] == 'a' && name[4] == 'y' && name[5] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Array;
                if (name[1] == 'r' && name[2] == 'g')
                {
                    // ArgIterator
                    if (name[3] == 'I' && name[4] == 't' && name[5] == 'e' && name[6] == 'r' &&
                        name[7] == 'a' && name[8] == 't' && name[9] == 'o' && name[10] == 'r' && name[11] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.ArgIterator;
                    // Argument* exceptions
                    if (name[3] == 'u' && name[4] == 'm' && name[5] == 'e' && name[6] == 'n' && name[7] == 't')
                    {
                        // ArgumentException
                        if (name[8] == 'E' && name[9] == 'x' && name[10] == 'c' && name[11] == 'e' &&
                            name[12] == 'p' && name[13] == 't' && name[14] == 'i' && name[15] == 'o' &&
                            name[16] == 'n' && name[17] == 0)
                            return JIT.MetadataIntegration.WellKnownTypes.ArgumentException;
                        // ArgumentNullException
                        if (name[8] == 'N' && name[9] == 'u' && name[10] == 'l' && name[11] == 'l' &&
                            name[12] == 'E' && name[13] == 'x' && name[14] == 'c' && name[15] == 'e' &&
                            name[16] == 'p' && name[17] == 't' && name[18] == 'i' && name[19] == 'o' &&
                            name[20] == 'n' && name[21] == 0)
                            return JIT.MetadataIntegration.WellKnownTypes.ArgumentNullException;
                        // ArgumentOutOfRangeException
                        if (name[8] == 'O' && name[9] == 'u' && name[10] == 't' && name[11] == 'O' &&
                            name[12] == 'f' && name[13] == 'R' && name[14] == 'a' && name[15] == 'n' &&
                            name[16] == 'g' && name[17] == 'e' && name[18] == 'E' && name[19] == 'x' &&
                            name[20] == 'c' && name[21] == 'e' && name[22] == 'p' && name[23] == 't' &&
                            name[24] == 'i' && name[25] == 'o' && name[26] == 'n' && name[27] == 0)
                            return JIT.MetadataIntegration.WellKnownTypes.ArgumentOutOfRangeException;
                    }
                }
                break;

            case (byte)'N':  // NotSupportedException, NotImplementedException, NullReferenceException
                if (name[1] == 'o' && name[2] == 't')
                {
                    // NotSupportedException
                    if (name[3] == 'S' && name[4] == 'u' && name[5] == 'p' && name[6] == 'p' &&
                        name[7] == 'o' && name[8] == 'r' && name[9] == 't' && name[10] == 'e' &&
                        name[11] == 'd' && name[12] == 'E' && name[13] == 'x' && name[14] == 'c' &&
                        name[15] == 'e' && name[16] == 'p' && name[17] == 't' && name[18] == 'i' &&
                        name[19] == 'o' && name[20] == 'n' && name[21] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.NotSupportedException;
                    // NotImplementedException
                    if (name[3] == 'I' && name[4] == 'm' && name[5] == 'p' && name[6] == 'l' &&
                        name[7] == 'e' && name[8] == 'm' && name[9] == 'e' && name[10] == 'n' &&
                        name[11] == 't' && name[12] == 'e' && name[13] == 'd' && name[14] == 'E' &&
                        name[15] == 'x' && name[16] == 'c' && name[17] == 'e' && name[18] == 'p' &&
                        name[19] == 't' && name[20] == 'i' && name[21] == 'o' && name[22] == 'n' && name[23] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.NotImplementedException;
                }
                // NullReferenceException
                if (name[1] == 'u' && name[2] == 'l' && name[3] == 'l' && name[4] == 'R' &&
                    name[5] == 'e' && name[6] == 'f' && name[7] == 'e' && name[8] == 'r' &&
                    name[9] == 'e' && name[10] == 'n' && name[11] == 'c' && name[12] == 'e' &&
                    name[13] == 'E' && name[14] == 'x' && name[15] == 'c' && name[16] == 'e' &&
                    name[17] == 'p' && name[18] == 't' && name[19] == 'i' && name[20] == 'o' &&
                    name[21] == 'n' && name[22] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.NullReferenceException;
                // NOTE: Nullable`1 is NOT a well-known type because it's generic and needs
                // to resolve via korlib fallback for method lookups to work
                break;

            case (byte)'F':  // FormatException
                if (name[1] == 'o' && name[2] == 'r' && name[3] == 'm' && name[4] == 'a' &&
                    name[5] == 't' && name[6] == 'E' && name[7] == 'x' && name[8] == 'c' &&
                    name[9] == 'e' && name[10] == 'p' && name[11] == 't' && name[12] == 'i' &&
                    name[13] == 'o' && name[14] == 'n' && name[15] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.FormatException;
                break;

            case (byte)'M':  // MulticastDelegate
                // MulticastDelegate
                if (name[1] == 'u' && name[2] == 'l' && name[3] == 't' && name[4] == 'i' &&
                    name[5] == 'c' && name[6] == 'a' && name[7] == 's' && name[8] == 't' &&
                    name[9] == 'D' && name[10] == 'e' && name[11] == 'l' && name[12] == 'e' &&
                    name[13] == 'g' && name[14] == 'a' && name[15] == 't' && name[16] == 'e' && name[17] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.MulticastDelegate;
                break;

            case (byte)'T':  // Type, TypedReference, TaskCanceledException
                if (name[1] == 'y' && name[2] == 'p' && name[3] == 'e')
                {
                    if (name[4] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.Type;
                    // TypedReference
                    if (name[4] == 'd' && name[5] == 'R' && name[6] == 'e' && name[7] == 'f' &&
                        name[8] == 'e' && name[9] == 'r' && name[10] == 'e' && name[11] == 'n' &&
                        name[12] == 'c' && name[13] == 'e' && name[14] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.TypedReference;
                }
                // TaskCanceledException
                if (name[1] == 'a' && name[2] == 's' && name[3] == 'k' && name[4] == 'C' &&
                    name[5] == 'a' && name[6] == 'n' && name[7] == 'c' && name[8] == 'e' &&
                    name[9] == 'l' && name[10] == 'e' && name[11] == 'd' && name[12] == 'E' &&
                    name[13] == 'x' && name[14] == 'c' && name[15] == 'e' && name[16] == 'p' &&
                    name[17] == 't' && name[18] == 'i' && name[19] == 'o' && name[20] == 'n' &&
                    name[21] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.TaskCanceledException;
                break;

            case (byte)'R':  // RuntimeType, RuntimeArgumentHandle, ReadOnlySpan`1
                if (name[1] == 'u' && name[2] == 'n' && name[3] == 't' && name[4] == 'i' &&
                    name[5] == 'm' && name[6] == 'e')
                {
                    // RuntimeType
                    if (name[7] == 'T' && name[8] == 'y' && name[9] == 'p' && name[10] == 'e' && name[11] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.RuntimeType;
                    // RuntimeArgumentHandle
                    if (name[7] == 'A' && name[8] == 'r' && name[9] == 'g' && name[10] == 'u' &&
                        name[11] == 'm' && name[12] == 'e' && name[13] == 'n' && name[14] == 't' &&
                        name[15] == 'H' && name[16] == 'a' && name[17] == 'n' && name[18] == 'd' &&
                        name[19] == 'l' && name[20] == 'e' && name[21] == 0)
                        return JIT.MetadataIntegration.WellKnownTypes.RuntimeArgumentHandle;
                }
                // ReadOnlySpan`1
                if (name[1] == 'e' && name[2] == 'a' && name[3] == 'd' && name[4] == 'O' &&
                    name[5] == 'n' && name[6] == 'l' && name[7] == 'y' && name[8] == 'S' &&
                    name[9] == 'p' && name[10] == 'a' && name[11] == 'n' && name[12] == '`' &&
                    name[13] == '1' && name[14] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.ReadOnlySpan;
                break;

            case (byte)'V':  // ValueType, Void
                // ValueType
                if (name[1] == 'a' && name[2] == 'l' && name[3] == 'u' && name[4] == 'e' &&
                    name[5] == 'T' && name[6] == 'y' && name[7] == 'p' && name[8] == 'e' && name[9] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.ValueType;
                // Void
                if (name[1] == 'o' && name[2] == 'i' && name[3] == 'd' && name[4] == 0)
                    return JIT.MetadataIntegration.WellKnownTypes.Void;
                break;
        }

        return 0;
    }

    /// <summary>
    /// Compare two null-terminated strings.
    /// </summary>
    private static bool StringsEqual(byte* a, byte* b)
    {
        if (a == null && b == null)
            return true;
        if (a == null || b == null)
            return false;

        int i = 0;
        while (a[i] != 0 && b[i] != 0)
        {
            if (a[i] != b[i])
                return false;
            i++;
        }
        return a[i] == b[i];  // Both should be null-terminator
    }

    // ============================================================================
    // MemberRef Resolution
    // ============================================================================

    /// <summary>
    /// Resolve a MemberRef token to find field information in another assembly.
    /// MemberRef can reference either a field or a method. This is for fields.
    /// Returns true if it's a field and populates fieldToken/targetAsmId.
    /// </summary>
    public static bool ResolveMemberRefField(uint sourceAsmId, uint memberRefToken,
                                              out uint fieldToken, out uint targetAsmId)
    {
        fieldToken = 0;
        targetAsmId = InvalidAssemblyId;

        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
            return false;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Get member name and signature
        uint nameIdx = MetadataReader.GetMemberRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        uint sigIdx = MetadataReader.GetMemberRefSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        byte* memberName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        byte* sig = MetadataReader.GetBlob(ref sourceAsm->Metadata, sigIdx, out uint sigLen);

        if (memberName == null || sig == null || sigLen == 0)
            return false;

        // Check if this is a field signature (0x06 = FIELD calling convention)
        if (sig[0] != 0x06)
            return false;  // Not a field, it's a method

        // Resolve the containing type
        LoadedAssembly* targetAsm = null;
        uint typeDefToken = 0;

        if (classRef.Table == MetadataTableId.TypeRef)
        {
            // MemberRef in another assembly via TypeRef
            // Resolve TypeRef directly to TypeDef token and target assembly
            // (don't require MethodTable* for JIT-compiled assemblies)
            if (!ResolveTypeRefToTypeDef(sourceAsm, classRef.RowId, out targetAsm, out typeDefToken))
            {
                // Fallback: try MethodTable-based resolution for AOT types
                uint typeRefToken = 0x01000000 | classRef.RowId;
                MethodTable* mt = ResolveTypeRef(sourceAsm, typeRefToken);
                if (mt == null)
                    return false;

                // Find which assembly this MethodTable belongs to
                // We need to search all assemblies' type registries
                for (int i = 0; i < MaxAssemblies; i++)
                {
                    if (!_assemblies[i].IsLoaded)
                        continue;

                    // Check if this assembly owns the MethodTable using reverse lookup
                    uint token = _assemblies[i].Types.FindTokenByMT(mt);
                    if (token != 0)
                    {
                        targetAsm = &_assemblies[i];
                        typeDefToken = token;
                        break;
                    }
                }
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly (unusual but possible)
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // MemberRef for a field in a generic type instantiation (e.g., SimpleList<T>._items)
            // Parse the TypeSpec to get the underlying generic type definition
            uint typeSpecIdx = MetadataReader.GetTypeSpecSignature(
                ref sourceAsm->Tables, ref sourceAsm->Sizes, classRef.RowId);
            byte* typeSpecSig = MetadataReader.GetBlob(ref sourceAsm->Metadata, typeSpecIdx, out uint typeSpecLen);

            if (typeSpecSig == null || typeSpecLen < 2)
            {
                DebugConsole.Write("[ResolveMemberRefField] Invalid TypeSpec signature for row ");
                DebugConsole.WriteDecimal(classRef.RowId);
                DebugConsole.WriteLine();
                return false;
            }

            // TypeSpec for generic instantiation: GENERICINST (0x15) CLASS/VALUETYPE TypeDefOrRef GenArgCount ...
            if (typeSpecSig[0] == 0x15) // GENERICINST
            {
                uint pos = 1;
                byte classOrVt = typeSpecSig[pos++];  // CLASS (0x12) or VALUETYPE (0x11)

                // Parse the TypeDefOrRef coded index using proper CLI compressed int format
                uint codedIdx = DecodeCompressedUInt(typeSpecSig, typeSpecLen, ref pos);

                // Decode TypeDefOrRef: low 2 bits = table, rest = row
                uint table = codedIdx & 0x03;
                uint row = codedIdx >> 2;

                if (table == 0) // TypeDef
                {
                    targetAsm = sourceAsm;
                    typeDefToken = 0x02000000 | row;
                }
                else if (table == 1) // TypeRef
                {
                    // Resolve TypeRef to TypeDef
                    if (!ResolveTypeRefToTypeDef(sourceAsm, row, out targetAsm, out typeDefToken))
                    {
                        DebugConsole.Write("[ResolveMemberRefField] Failed to resolve TypeRef ");
                        DebugConsole.WriteDecimal(row);
                        DebugConsole.WriteLine();
                        return false;
                    }
                }
                else
                {
                    // TypeSpec nested in TypeSpec not supported
                    DebugConsole.Write("[ResolveMemberRefField] Nested TypeSpec not supported, table=");
                    DebugConsole.WriteDecimal(table);
                    DebugConsole.WriteLine();
                    return false;
                }
            }
            else
            {
                DebugConsole.Write("[ResolveMemberRefField] TypeSpec not GENERICINST: 0x");
                DebugConsole.WriteHex(typeSpecSig[0]);
                DebugConsole.WriteLine();
                return false;
            }
        }
        else
        {
            // Other class types (ModuleRef, MethodDef) not implemented
            DebugConsole.Write("[ResolveMemberRefField] Unsupported class table: ");
            DebugConsole.WriteDecimal((uint)classRef.Table);
            DebugConsole.WriteLine();
            return false;
        }

        if (targetAsm == null || typeDefToken == 0)
            return false;

        targetAsmId = targetAsm->AssemblyId;

        // Now find the matching FieldDef in the target type
        fieldToken = FindFieldDefByName(targetAsm, typeDefToken, memberName);
        return fieldToken != 0;
    }

    /// <summary>
    /// Find a FieldDef token by name within a specific type.
    /// </summary>
    private static uint FindFieldDefByName(LoadedAssembly* asm, uint typeDefToken, byte* fieldName)
    {
        uint typeRow = typeDefToken & 0x00FFFFFF;
        if (typeRow == 0)
            return 0;

        // Get field range for this type
        uint fieldStart = MetadataReader.GetTypeDefFieldList(ref asm->Tables, ref asm->Sizes, typeRow);
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint fieldEnd;

        if (typeRow < typeDefCount)
        {
            fieldEnd = MetadataReader.GetTypeDefFieldList(ref asm->Tables, ref asm->Sizes, typeRow + 1);
        }
        else
        {
            fieldEnd = asm->Tables.RowCounts[(int)MetadataTableId.Field] + 1;
        }

        // Search fields in range
        for (uint fieldRow = fieldStart; fieldRow < fieldEnd; fieldRow++)
        {
            uint nameIdx = MetadataReader.GetFieldName(ref asm->Tables, ref asm->Sizes, fieldRow);
            byte* defName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (StringsEqual(fieldName, defName))
            {
                return 0x04000000 | fieldRow;  // FieldDef token
            }
        }

        return 0;
    }

    /// <summary>
    /// Resolve a MemberRef token to find method information in another assembly.
    /// MemberRef can reference either a field or a method. This is for methods.
    /// Returns true if it's a method and populates methodToken/targetAsmId.
    /// </summary>
    public static bool ResolveMemberRefMethod(uint sourceAsmId, uint memberRefToken,
                                               out uint methodToken, out uint targetAsmId)
    {
        methodToken = 0;
        targetAsmId = InvalidAssemblyId;

        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
            return false;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Debug: Print MemberRef resolution info
        DebugConsole.Write("[AsmLoader] ResolveMemberRef 0x");
        DebugConsole.WriteHex(memberRefToken);
        DebugConsole.Write(" from asm ");
        DebugConsole.WriteDecimal(sourceAsmId);
        DebugConsole.Write(", class table=");
        DebugConsole.WriteDecimal((uint)classRef.Table);
        DebugConsole.Write(" row=");
        DebugConsole.WriteDecimal(classRef.RowId);
        DebugConsole.WriteLine();

        // Get member name and signature
        uint nameIdx = MetadataReader.GetMemberRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        uint sigIdx = MetadataReader.GetMemberRefSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        byte* memberName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        byte* sig = MetadataReader.GetBlob(ref sourceAsm->Metadata, sigIdx, out uint sigLen);

        if (memberName == null || sig == null || sigLen == 0)
            return false;

        // Check if this is a method signature (NOT 0x06 which is FIELD)
        // Method calling conventions: 0x00 (default), 0x01-0x05 (various), 0x20 (generic)
        if (sig[0] == 0x06)
            return false;  // It's a field, not a method

        // Resolve the containing type
        LoadedAssembly* targetAsm = null;
        uint typeDefToken = 0;

        if (classRef.Table == MetadataTableId.TypeRef)
        {
            // MemberRef in another assembly via TypeRef
            // Resolve TypeRef directly to TypeDef token and target assembly
            // (don't require MethodTable* for JIT-compiled assemblies)
            if (!ResolveTypeRefToTypeDef(sourceAsm, classRef.RowId, out targetAsm, out typeDefToken))
            {
                // Fallback: try MethodTable-based resolution for AOT types (e.g., System.Int32)
                uint typeRefToken = 0x01000000 | classRef.RowId;
                MethodTable* mt = ResolveTypeRef(sourceAsm, typeRefToken);
                if (mt == null)
                {
                    // Debug: print the method name that failed to resolve
                    DebugConsole.Write("[AsmLoader] Failed MemberRef - method name: ");
                    for (int i = 0; memberName[i] != 0 && i < 64; i++)
                        DebugConsole.WriteChar((char)memberName[i]);
                    DebugConsole.WriteLine();
                    return false;
                }

                // Find which assembly this MethodTable belongs to
                // We need to search all assemblies' type registries
                for (int i = 0; i < MaxAssemblies; i++)
                {
                    if (!_assemblies[i].IsLoaded)
                        continue;

                    // Check if this assembly owns the MethodTable using reverse lookup
                    uint token = _assemblies[i].Types.FindTokenByMT(mt);
                    if (token != 0)
                    {
                        targetAsm = &_assemblies[i];
                        typeDefToken = token;
                        break;
                    }
                }

                // Fallback for AOT korlib types (System.Object, System.String, etc.)
                // These types have MethodTables but aren't registered in type registry
                if (targetAsm == null && mt != null)
                {
                    // Get the type name from the TypeRef
                    uint typeRefNameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, classRef.RowId);
                    uint typeRefNsIdx = MetadataReader.GetTypeRefNamespace(ref sourceAsm->Tables, ref sourceAsm->Sizes, classRef.RowId);
                    byte* typeName = MetadataReader.GetString(ref sourceAsm->Metadata, typeRefNameIdx);
                    byte* typeNs = MetadataReader.GetString(ref sourceAsm->Metadata, typeRefNsIdx);

                    if (typeName != null && IsSystemNamespace(typeNs))
                    {
                        uint wellKnownToken = GetWellKnownTypeToken(typeName);
                        if (wellKnownToken != 0)
                        {
                            targetAsm = GetAssembly(KernelAssemblyId);
                            typeDefToken = wellKnownToken;
                        }
                    }
                }
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // MemberRef on a TypeSpec (e.g., value type struct like DefaultInterpolatedStringHandler)
            // Parse the TypeSpec signature to get the underlying TypeDef/TypeRef
            uint typeSpecRow = classRef.RowId;
            DebugConsole.Write("[AsmLoader] TypeSpec row ");
            DebugConsole.WriteDecimal(typeSpecRow);
            DebugConsole.Write(" for method ");
            for (int i = 0; memberName != null && memberName[i] != 0 && i < 32; i++)
                DebugConsole.WriteChar((char)memberName[i]);
            DebugConsole.WriteLine();
            if (typeSpecRow == 0 || typeSpecRow > sourceAsm->Tables.RowCounts[(int)MetadataTableId.TypeSpec])
                return false;

            uint tsSigIdx = MetadataReader.GetTypeSpecSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeSpecRow);
            byte* tsSig = MetadataReader.GetBlob(ref sourceAsm->Metadata, tsSigIdx, out uint tsSigLen);
            if (tsSig == null || tsSigLen == 0)
                return false;

            int tsPos = 0;
            byte elementType = tsSig[tsPos++];

            // ELEMENT_TYPE_GENERICINST (0x15) - generic type instantiation
            // Format: 0x15 (CLASS=0x12 | VALUETYPE=0x11) TypeDefOrRefOrSpec GenArgCount Type1 Type2 ...
            if (elementType == 0x15)
            {
                if (tsPos >= (int)tsSigLen)
                    return false;
                // Skip the CLASS/VALUETYPE byte
                tsPos++;
                // Now decode the underlying type token
                elementType = 0x12;  // Treat as CLASS for the rest of the logic
            }

            // ELEMENT_TYPE_VALUETYPE (0x11) or ELEMENT_TYPE_CLASS (0x12) - followed by TypeDefOrRef
            if (elementType == 0x11 || elementType == 0x12)
            {
                uint underlyingToken = DecodeTypeDefOrRefOrSpec(tsSig, ref tsPos, tsSigLen);
                if (underlyingToken == 0)
                    return false;

                uint underlyingTable = underlyingToken >> 24;
                uint underlyingRow = underlyingToken & 0x00FFFFFF;

                if (underlyingTable == 0x02)
                {
                    // TypeDef - same assembly
                    targetAsm = sourceAsm;
                    typeDefToken = underlyingToken;
                    DebugConsole.Write("[AsmLoader] TypeSpec -> TypeDef 0x");
                    DebugConsole.WriteHex(typeDefToken);
                    DebugConsole.WriteLine();
                }
                else if (underlyingTable == 0x01)
                {
                    // TypeRef - resolve to target assembly
                    if (!ResolveTypeRefToTypeDef(sourceAsm, underlyingRow, out targetAsm, out typeDefToken))
                        return false;
                    DebugConsole.Write("[AsmLoader] TypeSpec -> TypeRef row ");
                    DebugConsole.WriteDecimal(underlyingRow);
                    DebugConsole.Write(" resolved to token 0x");
                    DebugConsole.WriteHex(typeDefToken);
                    DebugConsole.WriteLine();
                }
                else
                {
                    // Nested TypeSpec - not supported yet
                    return false;
                }
            }
            else
            {
                // Other TypeSpec kinds (arrays, pointers, etc.) not supported for MemberRef
                return false;
            }
        }
        else if (classRef.Table == MetadataTableId.MethodDef)
        {
            // MemberRef to a MethodDef in the same assembly
            // This happens for varargs call sites - the MemberRef has a signature with
            // the actual vararg types, but the class is the original MethodDef
            targetAsm = sourceAsm;
            methodToken = 0x06000000 | classRef.RowId;
            targetAsmId = sourceAsmId;
            return true;
        }
        else
        {
            // Other class types (ModuleRef) not implemented
            return false;
        }

        // Check if this is a well-known type (0xF0xxxxxx) - use AOT registry for method lookup
        // Well-known types don't have a target assembly, so handle them before the null check
        if ((typeDefToken & 0xFF000000) == 0xF0000000)
        {
            DebugConsole.Write("[AsmLoader] WellKnown type 0x");
            DebugConsole.WriteHex(typeDefToken);
            DebugConsole.Write(" method ");
            for (int i = 0; memberName != null && memberName[i] != 0 && i < 32; i++)
                DebugConsole.WriteChar((char)memberName[i]);
            DebugConsole.WriteLine();

            // Well-known types don't have metadata, use AOT method registry
            // Get the original type name - need to handle both TypeRef and TypeSpec
            uint typeRefRow = 0;
            if (classRef.Table == MetadataTableId.TypeRef)
            {
                typeRefRow = classRef.RowId;
            }
            else if (classRef.Table == MetadataTableId.TypeSpec)
            {
                // For TypeSpec, we need to find the underlying TypeRef
                // Parse the TypeSpec signature to get the TypeRef row
                uint typeSpecRow = classRef.RowId;
                uint tsSigIdx = MetadataReader.GetTypeSpecSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeSpecRow);
                byte* tsSig = MetadataReader.GetBlob(ref sourceAsm->Metadata, tsSigIdx, out uint tsSigLen);
                if (tsSig != null && tsSigLen > 0)
                {
                    int tsPos = 0;
                    byte elementType = tsSig[tsPos++];
                    // Skip GENERICINST header if present
                    if (elementType == 0x15 && tsPos < (int)tsSigLen)
                    {
                        tsPos++;  // Skip CLASS/VALUETYPE
                        elementType = 0x12;
                    }
                    if ((elementType == 0x11 || elementType == 0x12) && tsPos < (int)tsSigLen)
                    {
                        uint underlyingToken = DecodeTypeDefOrRefOrSpec(tsSig, ref tsPos, tsSigLen);
                        if ((underlyingToken >> 24) == 0x01)  // TypeRef
                        {
                            typeRefRow = underlyingToken & 0x00FFFFFF;
                        }
                    }
                }
            }

            if (typeRefRow != 0)
            {
                uint typeRefNameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);
                uint typeRefNsIdx = MetadataReader.GetTypeRefNamespace(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeRefRow);
                byte* typeName = MetadataReader.GetString(ref sourceAsm->Metadata, typeRefNameIdx);
                byte* typeNs = MetadataReader.GetString(ref sourceAsm->Metadata, typeRefNsIdx);

                // Build full type name "System.Delegate"
                // For simplicity, try AOT lookup with just the type name if it's in System namespace
                if (typeName != null && IsSystemNamespace(typeNs))
                {
                    // Count arg count from signature for overload resolution
                    byte argCount = 0;
                    if (sig != null && sigLen > 1)
                    {
                        // sig[0] = calling convention, sig[1] = param count
                        argCount = sig[1];
                    }

                    // Build "System.TypeName" for AOT lookup
                    byte* fullTypeName = stackalloc byte[64];
                    int pos = 0;
                    // Copy namespace
                    for (int i = 0; typeNs != null && typeNs[i] != 0 && pos < 48; i++)
                        fullTypeName[pos++] = typeNs[i];
                    fullTypeName[pos++] = (byte)'.';
                    // Copy type name
                    for (int i = 0; typeName[i] != 0 && pos < 62; i++)
                        fullTypeName[pos++] = typeName[i];
                    fullTypeName[pos] = 0;

                    if (AotMethodRegistry.TryLookup(fullTypeName, memberName, argCount, out AotMethodEntry entry))
                    {
                        // Return a synthetic AOT method token
                        // We'll use 0xFA (AOT marker) + unique identifier based on native code address
                        methodToken = 0xFA000000 | (uint)(entry.NativeCode & 0x00FFFFFF);
                        DebugConsole.Write("[AsmLoader] AOT found: code=0x");
                        DebugConsole.WriteHex((ulong)entry.NativeCode);
                        DebugConsole.WriteLine();
                        return true;
                    }
                    else
                    {
                        DebugConsole.Write("[AsmLoader] AOT lookup FAILED for ");
                        for (int i = 0; fullTypeName[i] != 0 && i < 64; i++)
                            DebugConsole.WriteChar((char)fullTypeName[i]);
                        DebugConsole.Write(".");
                        for (int i = 0; memberName[i] != 0 && i < 32; i++)
                            DebugConsole.WriteChar((char)memberName[i]);
                        DebugConsole.Write(" args=");
                        DebugConsole.WriteDecimal(argCount);
                        DebugConsole.WriteLine();

                        // For certain well-known types (Span`1, ReadOnlySpan`1, Array), fall back to
                        // korlib.dll metadata lookup since generic methods need JIT compilation
                        if (typeDefToken == JIT.MetadataIntegration.WellKnownTypes.Span ||
                            typeDefToken == JIT.MetadataIntegration.WellKnownTypes.ReadOnlySpan ||
                            typeDefToken == JIT.MetadataIntegration.WellKnownTypes.Array)
                        {
                            DebugConsole.WriteLine("[AsmLoader] Falling back to korlib metadata for generic method");
                            // Get korlib.dll - it's the CoreLib assembly
                            targetAsm = GetCoreLib();
                            DebugConsole.Write("[AsmLoader] GetCoreLib() returned: ");
                            DebugConsole.WriteHex((ulong)targetAsm);
                            DebugConsole.WriteLine();
                            if (targetAsm != null)
                            {
                                // Find the actual TypeDef in korlib.dll
                                DebugConsole.Write("[AsmLoader] Looking for type: ");
                                for (int dbgi = 0; typeName != null && typeName[dbgi] != 0 && dbgi < 32; dbgi++)
                                    DebugConsole.WriteChar((char)typeName[dbgi]);
                                DebugConsole.Write(" in ns: ");
                                for (int dbgi = 0; typeNs != null && typeNs[dbgi] != 0 && dbgi < 32; dbgi++)
                                    DebugConsole.WriteChar((char)typeNs[dbgi]);
                                DebugConsole.WriteLine();
                                typeDefToken = FindTypeDefByName(targetAsm, typeName, typeNs);
                                DebugConsole.Write("[AsmLoader] FindTypeDefByName returned 0x");
                                DebugConsole.WriteHex(typeDefToken);
                                DebugConsole.WriteLine();
                                if (typeDefToken != 0)
                                {
                                    DebugConsole.Write("[AsmLoader] Found korlib TypeDef 0x");
                                    DebugConsole.WriteHex(typeDefToken);
                                    DebugConsole.WriteLine();
                                    // Fall through to normal method lookup below
                                    goto doNormalLookup;
                                }
                            }
                        }
                    }
                }
            }
            // If AOT lookup failed for well-known types, we can't fall through because
            // there's no target assembly to search in
            DebugConsole.WriteLine("[AsmLoader] Well-known type method not found in AOT registry");
            return false;
        }

        doNormalLookup:

        // Normal lookup requires a target assembly - check for null now
        if (targetAsm == null || typeDefToken == 0)
            return false;

        targetAsmId = targetAsm->AssemblyId;

        // Find the matching MethodDef in the target type
        methodToken = FindMethodDefByName(sourceAsm, targetAsm, typeDefToken, memberName, sig, sigLen);

        // Debug: Check if method token row is valid for target assembly
        uint methodRow = methodToken & 0x00FFFFFF;
        uint methodDefCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.MethodDef];
        if (methodRow > methodDefCount)
        {
            DebugConsole.Write("[AsmLoader] BUG: method row ");
            DebugConsole.WriteDecimal(methodRow);
            DebugConsole.Write(" > table size ");
            DebugConsole.WriteDecimal(methodDefCount);
            DebugConsole.Write(" in asm ");
            DebugConsole.WriteDecimal(targetAsmId);
            DebugConsole.WriteLine();
        }

        return methodToken != 0;
    }

    /// <summary>
    /// Check if a MemberRef token refers to an interface method.
    /// If so, returns the interface MethodTable and method slot within the interface.
    /// </summary>
    /// <param name="sourceAsmId">Source assembly ID where the MemberRef is defined</param>
    /// <param name="memberRefToken">The MemberRef token (0x0A table)</param>
    /// <param name="interfaceMT">Output: the interface's MethodTable (if interface method)</param>
    /// <param name="methodSlot">Output: the method's slot index within the interface (0-based)</param>
    /// <returns>True if this is an interface method</returns>
    public static bool IsInterfaceMethod(uint sourceAsmId, uint memberRefToken,
                                          out MethodTable* interfaceMT, out short methodSlot)
    {
        interfaceMT = null;
        methodSlot = -1;

        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
            return false;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Get member name and signature for method lookup
        uint nameIdx = MetadataReader.GetMemberRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        byte* memberName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        if (memberName == null)
            return false;

        // Resolve the containing type
        LoadedAssembly* targetAsm = null;
        uint typeDefToken = 0;

        if (classRef.Table == MetadataTableId.TypeRef)
        {
            // MemberRef in another assembly via TypeRef
            if (!ResolveTypeRefToTypeDef(sourceAsm, classRef.RowId, out targetAsm, out typeDefToken))
                return false;

            // Special handling for well-known interfaces like IDisposable
            // These have synthetic tokens (0xF0xxxxxx) and need hardcoded slot info
            if (typeDefToken == JIT.MetadataIntegration.WellKnownTypes.IDisposable)
            {
                // IDisposable has one method: Dispose() at slot 0
                // Get the registered IDisposable MT (captured when first type implementing it was created)
                methodSlot = 0;
                interfaceMT = JIT.MetadataIntegration.GetIDisposableMT();
                if (interfaceMT == null)
                {
                    DebugConsole.WriteLine("[AsmLoader] IsInterfaceMethod: IDisposable MT not registered yet");
                    return false;  // Can't dispatch without valid MT
                }
                return true;
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - could be a generic interface instantiation like IContainer<int>
            // Get the TypeSpec signature and check if it's a generic interface
            uint typeSpecRow = classRef.RowId;
            uint sigIdx = MetadataReader.GetTypeSpecSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeSpecRow);
            byte* sig = MetadataReader.GetBlob(ref sourceAsm->Metadata, sigIdx, out uint sigLen);
            if (sig == null || sigLen == 0)
                return false;

            // Check for GENERICINST (0x15) signature
            if (sig[0] != 0x15)
                return false;

            // sig[1] is CLASS (0x12) or VALUETYPE (0x11)
            // sig[2..] is TypeDefOrRefOrSpecEncoded (compressed token)
            int pos = 1;
            byte elemType = sig[pos++];
            if (elemType != 0x12)  // Must be CLASS for interfaces
                return false;

            // Read the TypeDefOrRefOrSpecEncoded token
            uint encoded = 0;
            byte b = sig[pos++];
            if ((b & 0x80) == 0)
                encoded = b;
            else if ((b & 0xC0) == 0x80)
                encoded = ((uint)(b & 0x3F) << 8) | sig[pos++];
            else
                encoded = ((uint)(b & 0x1F) << 24) | ((uint)sig[pos++] << 16) | ((uint)sig[pos++] << 8) | sig[pos++];

            // Decode TypeDefOrRef coded index
            uint tag = encoded & 0x03;
            uint idx = encoded >> 2;

            if (tag == 0) // TypeDef
            {
                targetAsm = sourceAsm;
                typeDefToken = 0x02000000 | idx;
            }
            else if (tag == 1) // TypeRef
            {
                if (!ResolveTypeRefToTypeDef(sourceAsm, idx, out targetAsm, out typeDefToken))
                    return false;
            }
            else
            {
                return false;  // TypeSpec nested - not supported
            }

            // Now we have the generic type definition token
            // Get the instantiated interface MT for proper interface dispatch
            // Use ResolveTypeSpec to get/create the generic instantiation MT
            interfaceMT = ResolveTypeSpec(sourceAsm, 0x1B000000 | typeSpecRow);
        }
        else
        {
            // Unknown table - not interface
            return false;
        }

        if (targetAsm == null || typeDefToken == 0)
            return false;

        // Get the TypeDef flags to check if it's an interface
        uint typeDefRow = typeDefToken & 0x00FFFFFF;
        uint typeFlags = MetadataReader.GetTypeDefFlags(ref targetAsm->Tables, ref targetAsm->Sizes, typeDefRow);

        // Check for interface flag (tdInterface = 0x20 in TypeAttributes)
        const uint tdInterface = 0x20;
        if ((typeFlags & 0x20) != tdInterface)
            return false;  // Not an interface

        // This is an interface method - get the interface's MethodTable
        // For TypeSpec (generic interfaces), interfaceMT was already set above
        if (interfaceMT == null)
        {
            interfaceMT = targetAsm->Types.Lookup(typeDefToken);
            if (interfaceMT == null)
            {
                // Not created yet - create on demand
                interfaceMT = CreateTypeDefMethodTable(targetAsm, typeDefToken);
            }
            if (interfaceMT == null)
            {
                DebugConsole.Write("[AsmLoader] IsInterfaceMethod: failed to get interface MT for 0x");
                DebugConsole.WriteHex(typeDefToken);
                DebugConsole.WriteLine();
                return false;
            }
        }

        // Find the method slot within the interface
        // Interface methods start at slot 0 (no Object vtable inheritance for interfaces)
        // We need to iterate through MethodDef rows belonging to this TypeDef
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref targetAsm->Tables, ref targetAsm->Sizes, typeDefRow);
        uint methodEnd;
        uint typeDefCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(ref targetAsm->Tables, ref targetAsm->Sizes, typeDefRow + 1);
        else
            methodEnd = targetAsm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

        short slot = 0;
        for (uint m = methodStart; m < methodEnd; m++)
        {
            uint mNameIdx = MetadataReader.GetMethodDefName(ref targetAsm->Tables, ref targetAsm->Sizes, m);
            byte* mName = MetadataReader.GetString(ref targetAsm->Metadata, mNameIdx);
            if (mName != null && StringEquals(memberName, mName))
            {
                methodSlot = slot;
                return true;
            }
            slot++;
        }

        return false;
    }

    /// <summary>
    /// Compare two null-terminated byte strings for equality.
    /// </summary>
    private static bool StringEquals(byte* a, byte* b)
    {
        if (a == null || b == null)
            return a == b;
        while (*a != 0 && *b != 0)
        {
            if (*a != *b)
                return false;
            a++;
            b++;
        }
        return *a == *b;
    }

    /// <summary>
    /// Check if the name is "Invoke".
    /// </summary>
    private static bool IsInvokeName(byte* name)
    {
        // "Invoke" = I n v o k e \0
        return name[0] == 'I' && name[1] == 'n' && name[2] == 'v' &&
               name[3] == 'o' && name[4] == 'k' && name[5] == 'e' && name[6] == 0;
    }

    /// <summary>
    /// Check if the name is ".ctor".
    /// </summary>
    private static bool IsCtorName(byte* name)
    {
        // ".ctor" = . c t o r \0
        return name[0] == '.' && name[1] == 'c' && name[2] == 't' &&
               name[3] == 'o' && name[4] == 'r' && name[5] == 0;
    }

    /// <summary>
    /// Check if a MethodDef token is a delegate constructor.
    /// Delegates have "runtime managed" constructors with no IL body.
    /// </summary>
    /// <param name="asmId">Assembly ID containing the MethodDef</param>
    /// <param name="methodDefToken">MethodDef token (0x06xxxxxx)</param>
    /// <param name="delegateMT">Output: the delegate type's MethodTable</param>
    /// <param name="argCount">Output: number of constructor args (excluding 'this')</param>
    /// <returns>True if this is a delegate constructor</returns>
    public static bool IsDelegateConstructor(uint asmId, uint methodDefToken,
                                              out MethodTable* delegateMT, out int argCount)
    {
        delegateMT = null;
        argCount = 0;

        LoadedAssembly* asm = GetAssembly(asmId);
        if (asm == null)
        {
            DebugConsole.Write("[IsDelegateCtor] asm null for ");
            DebugConsole.WriteDecimal(asmId);
            DebugConsole.WriteLine();
            return false;
        }

        uint methodRow = methodDefToken & 0x00FFFFFF;
        if (methodRow == 0)
            return false;

        // Check if method name is ".ctor"
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

        // Debug: trace ctor name check for delegate candidate tokens
        if (methodRow >= 0xF0 && methodRow <= 0xFF)
        {
            DebugConsole.Write("[IsDelegateCtor] token 0x");
            DebugConsole.WriteHex(methodDefToken);
            DebugConsole.Write(" name='");
            if (methodName != null)
            {
                for (int i = 0; i < 10 && methodName[i] != 0; i++)
                    DebugConsole.WriteByte(methodName[i]);
            }
            DebugConsole.Write("' isCtorName=");
            DebugConsole.Write((methodName != null && IsCtorName(methodName)) ? "Y" : "N");
            DebugConsole.WriteLine();
        }

        if (methodName == null || !IsCtorName(methodName))
            return false;

        // Find which TypeDef owns this MethodDef
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint ownerTypeRow = 0;

        // Debug: passed name check, now finding owner type
        if (methodRow >= 0xF0 && methodRow <= 0xFF)
        {
            DebugConsole.Write("[IsDelegateCtor] passed name check, typeDefCount=");
            DebugConsole.WriteDecimal(typeDefCount);
            DebugConsole.WriteLine();
        }

        for (uint t = 1; t <= typeDefCount; t++)
        {
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t);
            uint methodEnd = (t == typeDefCount)
                ? asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1
                : MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t + 1);

            if (methodRow >= methodStart && methodRow < methodEnd)
            {
                ownerTypeRow = t;
                break;
            }
        }

        if (ownerTypeRow == 0)
        {
            if (methodRow >= 0xF0 && methodRow <= 0xFF)
            {
                DebugConsole.Write("[IsDelegateCtor] ownerTypeRow=0 for method ");
                DebugConsole.WriteDecimal(methodRow);
                DebugConsole.WriteLine();
            }
            return false;
        }

        // Debug: found owner type
        if (methodRow >= 0xF0 && methodRow <= 0xFF)
        {
            DebugConsole.Write("[IsDelegateCtor] ownerTypeRow=");
            DebugConsole.WriteDecimal(ownerTypeRow);
            DebugConsole.WriteLine();
        }

        // Get or create the type's MethodTable
        uint typeDefToken = 0x02000000 | ownerTypeRow;
        delegateMT = asm->Types.Lookup(typeDefToken);
        if (delegateMT == null)
        {
            delegateMT = CreateTypeDefMethodTable(asm, typeDefToken);
        }
        if (delegateMT == null)
        {
            if (methodRow >= 0xF0 && methodRow <= 0xFF)
                DebugConsole.WriteLine("[IsDelegateCtor] delegateMT null after create");
            return false;
        }

        // Check if it's a delegate type
        if (!delegateMT->IsDelegate)
        {
            if (methodRow >= 0xF0 && methodRow <= 0xFF)
            {
                DebugConsole.Write("[IsDelegateCtor] MT not delegate. MT=0x");
                DebugConsole.WriteHex((ulong)delegateMT);
                DebugConsole.Write(" flags=0x");
                DebugConsole.WriteHex(delegateMT->CombinedFlags);
                DebugConsole.WriteLine();
            }
            return false;
        }

        // Parse signature to get arg count
        // Delegate constructor signature: instance void(object, native int)
        // Should have 2 args (target, functionPointer)
        uint sigBlobIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigBlobIdx, out uint sigLen);
        if (sig != null && sigLen >= 2)
        {
            // sig[0] = calling convention (should have HASTHIS)
            // sig[1] = param count
            argCount = sig[1];
        }
        else
        {
            // Assume 2 args (standard delegate ctor)
            argCount = 2;
        }

        return true;
    }

    /// <summary>
    /// Check if a MethodDef token is a delegate Invoke method.
    /// Used when resolving callvirt with MethodDef tokens (not MemberRef).
    /// </summary>
    /// <param name="asmId">Assembly ID where the MethodDef is defined</param>
    /// <param name="methodDefToken">The MethodDef token (0x06 table)</param>
    /// <param name="delegateMT">Output: the delegate type's MethodTable</param>
    /// <param name="argCount">Output: number of Invoke args (excluding 'this')</param>
    /// <param name="returnKind">Output: return type kind</param>
    /// <returns>True if this is a delegate Invoke method</returns>
    public static bool IsDelegateInvokeMethodDef(uint asmId, uint methodDefToken,
                                                  out MethodTable* delegateMT, out int argCount,
                                                  out ReturnKind returnKind)
    {
        delegateMT = null;
        argCount = 0;
        returnKind = ReturnKind.Void;

        LoadedAssembly* asm = GetAssembly(asmId);
        if (asm == null)
            return false;

        uint methodRow = methodDefToken & 0x00FFFFFF;
        if (methodRow == 0)
            return false;

        // Check if method name is "Invoke"
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* methodName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

        // Debug: trace Invoke check for delegate candidate tokens
        if (methodRow >= 0xF0 && methodRow <= 0xFF)
        {
            DebugConsole.Write("[IsDelegateInvoke] token 0x");
            DebugConsole.WriteHex(methodDefToken);
            DebugConsole.Write(" name='");
            if (methodName != null)
            {
                for (int i = 0; i < 10 && methodName[i] != 0; i++)
                    DebugConsole.WriteByte(methodName[i]);
            }
            DebugConsole.Write("' isInvoke=");
            DebugConsole.Write((methodName != null && IsInvokeName(methodName)) ? "Y" : "N");
            DebugConsole.WriteLine();
        }

        if (methodName == null || !IsInvokeName(methodName))
            return false;

        // Find which TypeDef owns this MethodDef
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint ownerTypeRow = 0;

        for (uint t = 1; t <= typeDefCount; t++)
        {
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t);
            uint methodEnd = (t == typeDefCount)
                ? asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1
                : MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t + 1);

            if (methodRow >= methodStart && methodRow < methodEnd)
            {
                ownerTypeRow = t;
                break;
            }
        }

        if (ownerTypeRow == 0)
            return false;

        // Get or create the type's MethodTable
        uint typeDefToken = 0x02000000 | ownerTypeRow;
        delegateMT = asm->Types.Lookup(typeDefToken);
        if (delegateMT == null)
        {
            delegateMT = CreateTypeDefMethodTable(asm, typeDefToken);
        }
        if (delegateMT == null)
            return false;

        // Check if it's a delegate type
        if (!delegateMT->IsDelegate)
            return false;

        // Parse signature to get arg count and return type
        uint sigBlobIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigBlobIdx, out uint sigLen);
        if (sig != null && sigLen >= 3)
        {
            // sig[0] = calling convention (should have HASTHIS)
            // sig[1] = param count
            argCount = sig[1];

            // sig[2] = return type (element type)
            byte retType = sig[2];
            returnKind = retType switch
            {
                0x01 => ReturnKind.Void,       // ELEMENT_TYPE_VOID
                0x04 => ReturnKind.Int32,      // ELEMENT_TYPE_I1 (sign-extend to int32)
                0x05 => ReturnKind.Int32,      // ELEMENT_TYPE_U1 (zero-extend to int32)
                0x06 => ReturnKind.Int32,      // ELEMENT_TYPE_I2 (sign-extend to int32)
                0x07 => ReturnKind.Int32,      // ELEMENT_TYPE_U2 (zero-extend to int32)
                0x08 => ReturnKind.Int32,      // ELEMENT_TYPE_I4
                0x09 => ReturnKind.Int32,      // ELEMENT_TYPE_U4 (treated as int32)
                0x0A => ReturnKind.Int64,      // ELEMENT_TYPE_I8
                0x0B => ReturnKind.Int64,      // ELEMENT_TYPE_U8 (treated as int64)
                0x0C => ReturnKind.Float32,    // ELEMENT_TYPE_R4
                0x0D => ReturnKind.Float64,    // ELEMENT_TYPE_R8
                0x18 => ReturnKind.IntPtr,     // ELEMENT_TYPE_I
                0x19 => ReturnKind.IntPtr,     // ELEMENT_TYPE_U
                _ => ReturnKind.IntPtr,        // Default for objects/refs
            };
        }

        return true;
    }

    /// <summary>
    /// Check if a MemberRef token refers to a delegate Invoke method.
    /// Returns true if this is a delegate.Invoke call that needs runtime handling.
    /// </summary>
    /// <param name="sourceAsmId">Source assembly ID where the MemberRef is defined</param>
    /// <param name="memberRefToken">The MemberRef token (0x0A table)</param>
    /// <param name="delegateMT">Output: the delegate type's MethodTable</param>
    /// <returns>True if this is a delegate Invoke method</returns>
    public static bool IsDelegateInvoke(uint sourceAsmId, uint memberRefToken, out MethodTable* delegateMT)
    {
        delegateMT = null;

        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
            return false;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get member name to check if it's "Invoke"
        uint nameIdx = MetadataReader.GetMemberRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        byte* memberName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        if (memberName == null || !IsInvokeName(memberName))
            return false;  // Not "Invoke"

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Resolve the containing type
        LoadedAssembly* targetAsm = null;
        uint typeDefToken = 0;

        if (classRef.Table == MetadataTableId.TypeRef)
        {
            // MemberRef in another assembly via TypeRef
            if (!ResolveTypeRefToTypeDef(sourceAsm, classRef.RowId, out targetAsm, out typeDefToken))
                return false;

            delegateMT = targetAsm->Types.Lookup(typeDefToken);
            if (delegateMT == null)
            {
                delegateMT = CreateTypeDefMethodTable(targetAsm, typeDefToken);
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;

            delegateMT = targetAsm->Types.Lookup(typeDefToken);
            if (delegateMT == null)
            {
                delegateMT = CreateTypeDefMethodTable(targetAsm, typeDefToken);
            }
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - could be generic delegate instantiation (e.g., Transformer<int, int>)
            // Use GetMemberRefGenericInstMT to get the instantiated MethodTable
            delegateMT = GetMemberRefGenericInstMT(sourceAsmId, memberRefToken);
        }
        else
        {
            return false;
        }

        if (delegateMT == null)
            return false;

        // Check if it's a delegate type
        if (!delegateMT->IsDelegate)
            return false;

        return true;
    }

    /// <summary>
    /// Check if a MemberRef token is a delegate constructor (.ctor method on a delegate type).
    /// This handles both regular delegates and generic delegate instantiations.
    /// </summary>
    /// <param name="sourceAsmId">The assembly containing the MemberRef token</param>
    /// <param name="memberRefToken">MemberRef token (0x0Axxxxxx)</param>
    /// <param name="delegateMT">Output: the delegate type's MethodTable</param>
    /// <returns>True if this is a delegate constructor</returns>
    public static bool IsDelegateCtor(uint sourceAsmId, uint memberRefToken, out MethodTable* delegateMT)
    {
        delegateMT = null;

        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
            return false;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get member name to check if it's ".ctor"
        uint nameIdx = MetadataReader.GetMemberRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
        byte* memberName = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);
        if (memberName == null || !IsCtorName(memberName))
            return false;  // Not ".ctor"

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Resolve the containing type
        LoadedAssembly* targetAsm = null;
        uint typeDefToken = 0;

        if (classRef.Table == MetadataTableId.TypeRef)
        {
            // MemberRef in another assembly via TypeRef
            if (!ResolveTypeRefToTypeDef(sourceAsm, classRef.RowId, out targetAsm, out typeDefToken))
                return false;

            // Get the type's MethodTable
            delegateMT = targetAsm->Types.Lookup(typeDefToken);
            if (delegateMT == null)
            {
                delegateMT = CreateTypeDefMethodTable(targetAsm, typeDefToken);
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;

            delegateMT = targetAsm->Types.Lookup(typeDefToken);
            if (delegateMT == null)
            {
                delegateMT = CreateTypeDefMethodTable(targetAsm, typeDefToken);
            }
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - could be generic delegate instantiation (e.g., Transformer<int, int>)
            // Use GetMemberRefGenericInstMT to get the instantiated MethodTable
            delegateMT = GetMemberRefGenericInstMT(sourceAsmId, memberRefToken);
        }
        else
        {
            return false;
        }

        if (delegateMT == null)
            return false;

        // Check if it's a delegate type
        if (!delegateMT->IsDelegate)
            return false;

        return true;
    }

    /// <summary>
    /// Check if a MethodDef token belongs to an interface type (same assembly).
    /// If so, returns interface dispatch info so callvirt can resolve at runtime.
    /// </summary>
    /// <param name="asmId">Assembly ID containing the MethodDef</param>
    /// <param name="methodDefToken">MethodDef token (0x06xxxxxx)</param>
    /// <param name="interfaceMT">Output: the interface's MethodTable (if interface method)</param>
    /// <param name="methodSlot">Output: the method's slot index within the interface (0-based)</param>
    /// <returns>True if this is an interface method</returns>
    public static bool IsMethodDefInterfaceMethod(uint asmId, uint methodDefToken,
                                                   out MethodTable* interfaceMT, out short methodSlot)
    {
        interfaceMT = null;
        methodSlot = -1;

        LoadedAssembly* asm = GetAssembly(asmId);
        if (asm == null)
            return false;

        uint methodRow = methodDefToken & 0x00FFFFFF;
        if (methodRow == 0)
            return false;

        // Find which TypeDef owns this MethodDef by iterating through all TypeDefs
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint ownerTypeRow = 0;

        for (uint t = 1; t <= typeDefCount; t++)
        {
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t);
            uint methodEnd;
            if (t < typeDefCount)
                methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, t + 1);
            else
                methodEnd = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

            if (methodRow >= methodStart && methodRow < methodEnd)
            {
                ownerTypeRow = t;
                break;
            }
        }

        if (ownerTypeRow == 0)
            return false;  // Couldn't find owner TypeDef

        // Check if the owner type is an interface
        uint typeFlags = MetadataReader.GetTypeDefFlags(ref asm->Tables, ref asm->Sizes, ownerTypeRow);
        const uint tdInterface = 0x20;
        if ((typeFlags & 0x20) != tdInterface)
            return false;  // Not an interface

        // This is an interface method - get the interface's MethodTable
        uint typeDefToken = 0x02000000 | ownerTypeRow;
        interfaceMT = asm->Types.Lookup(typeDefToken);
        if (interfaceMT == null)
        {
            // Not created yet - create on demand
            interfaceMT = CreateTypeDefMethodTable(asm, typeDefToken);
        }
        if (interfaceMT == null)
        {
            DebugConsole.Write("[AsmLoader] IsMethodDefInterfaceMethod: failed to get interface MT for 0x");
            DebugConsole.WriteHex(typeDefToken);
            DebugConsole.WriteLine();
            return false;
        }

        // Find the method slot within the interface (0-based from first method)
        uint methodStart2 = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, ownerTypeRow);
        methodSlot = (short)(methodRow - methodStart2);

        return true;
    }

    /// <summary>
    /// Get the instantiated generic type MethodTable for a MemberRef if its class is a GenericInst TypeSpec.
    /// Returns null if the MemberRef class is not a generic instantiation.
    /// This is used to set up type argument context before JIT compiling methods on generic types.
    /// </summary>
    public static MethodTable* GetMemberRefGenericInstMT(uint sourceAsmId, uint memberRefToken)
    {
        LoadedAssembly* sourceAsm = GetAssembly(sourceAsmId);
        if (sourceAsm == null)
        {
            DebugConsole.WriteLine("[AsmLoader] GetMemberRefGenericInstMT: sourceAsm null");
            return null;
        }

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
        {
            DebugConsole.WriteLine("[AsmLoader] GetMemberRefGenericInstMT: rowId=0");
            return null;
        }

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(
            ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

        // Only handle TypeSpec references
        if (classRef.Table != MetadataTableId.TypeSpec)
        {
            return null;
        }

        uint typeSpecRow = classRef.RowId;
        if (typeSpecRow == 0 || typeSpecRow > sourceAsm->Tables.RowCounts[(int)MetadataTableId.TypeSpec])
        {
            DebugConsole.WriteLine("[AsmLoader] GetMemberRefGenericInstMT: invalid TypeSpec row");
            return null;
        }

        // Get the TypeSpec signature
        uint tsSigIdx = MetadataReader.GetTypeSpecSignature(ref sourceAsm->Tables, ref sourceAsm->Sizes, typeSpecRow);
        byte* tsSig = MetadataReader.GetBlob(ref sourceAsm->Metadata, tsSigIdx, out uint tsSigLen);
        if (tsSig == null || tsSigLen == 0)
        {
            DebugConsole.WriteLine("[AsmLoader] GetMemberRefGenericInstMT: no TypeSpec sig");
            return null;
        }

        // Check if it's a GenericInst
        if (tsSig[0] != 0x15)  // ELEMENT_TYPE_GENERICINST
        {
            return null;
        }

        // Parse the GenericInst to get the instantiated MethodTable
        // This will resolve the full generic instantiation including type arguments
        uint typeSpecToken = 0x1B000000 | typeSpecRow;
        MethodTable* result = ResolveTypeSpec(sourceAsm, typeSpecToken);
        return result;
    }

    /// <summary>
    /// Compare two method signatures for overload resolution across assemblies.
    /// Signatures may contain TypeRef tokens that differ between assemblies but refer to the same type.
    /// </summary>
    private static bool SignaturesMatch(
        LoadedAssembly* sourceAsm, byte* sig1, uint sig1Len,
        LoadedAssembly* targetAsm, byte* sig2, uint sig2Len)
    {
        if (sig1 == null || sig2 == null)
            return sig1 == sig2;

        // Parse both signatures element by element
        uint pos1 = 0;
        uint pos2 = 0;

        // Compare calling convention (first byte)
        if (pos1 >= sig1Len || pos2 >= sig2Len)
            return false;
        if (sig1[pos1] != sig2[pos2])
            return false;
        byte callingConv = sig1[pos1];
        pos1++;
        pos2++;

        // If generic, compare generic param count
        if ((callingConv & 0x10) != 0)
        {
            uint genCount1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
            uint genCount2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
            if (genCount1 != genCount2)
                return false;
        }

        // Compare parameter count
        uint paramCount1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
        uint paramCount2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
        if (paramCount1 != paramCount2)
            return false;

        // Compare return type and each parameter type
        uint totalTypes = paramCount1 + 1; // return type + params
        for (uint i = 0; i < totalTypes; i++)
        {
            if (!TypesMatch(sourceAsm, sig1, sig1Len, ref pos1,
                           targetAsm, sig2, sig2Len, ref pos2))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Decode a compressed unsigned integer from a signature blob.
    /// </summary>
    private static uint DecodeCompressedUInt(byte* sig, uint sigLen, ref uint pos)
    {
        if (pos >= sigLen)
            return 0;

        byte b0 = sig[pos++];
        if ((b0 & 0x80) == 0)
            return b0;

        if ((b0 & 0xC0) == 0x80)
        {
            if (pos >= sigLen)
                return 0;
            byte b1 = sig[pos++];
            return (uint)(((b0 & 0x3F) << 8) | b1);
        }

        if ((b0 & 0xE0) == 0xC0)
        {
            if (pos + 2 >= sigLen)
                return 0;
            byte b1 = sig[pos++];
            byte b2 = sig[pos++];
            byte b3 = sig[pos++];
            return (uint)(((b0 & 0x1F) << 24) | (b1 << 16) | (b2 << 8) | b3);
        }

        return 0;
    }

    /// <summary>
    /// Compare two type encodings in signatures, resolving TypeRefs across assemblies.
    /// </summary>
    private static bool TypesMatch(
        LoadedAssembly* sourceAsm, byte* sig1, uint sig1Len, ref uint pos1,
        LoadedAssembly* targetAsm, byte* sig2, uint sig2Len, ref uint pos2)
    {
        if (pos1 >= sig1Len || pos2 >= sig2Len)
            return false;

        byte elem1 = sig1[pos1++];
        byte elem2 = sig2[pos2++];

        // Handle custom modifiers (CMOD_OPT = 0x20, CMOD_REQD = 0x1F)
        while (elem1 == 0x20 || elem1 == 0x1F)
        {
            DecodeCompressedUInt(sig1, sig1Len, ref pos1); // Skip the modifier token
            if (pos1 >= sig1Len)
                return false;
            elem1 = sig1[pos1++];
        }
        while (elem2 == 0x20 || elem2 == 0x1F)
        {
            DecodeCompressedUInt(sig2, sig2Len, ref pos2); // Skip the modifier token
            if (pos2 >= sig2Len)
                return false;
            elem2 = sig2[pos2++];
        }

        // Element type codes that must match exactly
        // ELEMENT_TYPE_* constants from ECMA-335
        const byte ELEMENT_TYPE_VOID = 0x01;
        const byte ELEMENT_TYPE_BOOLEAN = 0x02;
        const byte ELEMENT_TYPE_CHAR = 0x03;
        const byte ELEMENT_TYPE_I1 = 0x04;
        const byte ELEMENT_TYPE_U1 = 0x05;
        const byte ELEMENT_TYPE_I2 = 0x06;
        const byte ELEMENT_TYPE_U2 = 0x07;
        const byte ELEMENT_TYPE_I4 = 0x08;
        const byte ELEMENT_TYPE_U4 = 0x09;
        const byte ELEMENT_TYPE_I8 = 0x0A;
        const byte ELEMENT_TYPE_U8 = 0x0B;
        const byte ELEMENT_TYPE_R4 = 0x0C;
        const byte ELEMENT_TYPE_R8 = 0x0D;
        const byte ELEMENT_TYPE_STRING = 0x0E;
        const byte ELEMENT_TYPE_PTR = 0x0F;
        const byte ELEMENT_TYPE_BYREF = 0x10;
        const byte ELEMENT_TYPE_VALUETYPE = 0x11;
        const byte ELEMENT_TYPE_CLASS = 0x12;
        const byte ELEMENT_TYPE_VAR = 0x13;
        const byte ELEMENT_TYPE_ARRAY = 0x14;
        const byte ELEMENT_TYPE_GENERICINST = 0x15;
        const byte ELEMENT_TYPE_TYPEDBYREF = 0x16;
        const byte ELEMENT_TYPE_I = 0x18;
        const byte ELEMENT_TYPE_U = 0x19;
        const byte ELEMENT_TYPE_FNPTR = 0x1B;
        const byte ELEMENT_TYPE_OBJECT = 0x1C;
        const byte ELEMENT_TYPE_SZARRAY = 0x1D;
        const byte ELEMENT_TYPE_MVAR = 0x1E;

        // Primitive types must match exactly
        if (elem1 <= ELEMENT_TYPE_STRING || elem1 == ELEMENT_TYPE_TYPEDBYREF ||
            elem1 == ELEMENT_TYPE_I || elem1 == ELEMENT_TYPE_U || elem1 == ELEMENT_TYPE_OBJECT)
        {
            return elem1 == elem2;
        }

        // Both must be the same element type
        if (elem1 != elem2)
            return false;

        switch (elem1)
        {
            case ELEMENT_TYPE_PTR:
            case ELEMENT_TYPE_BYREF:
            case ELEMENT_TYPE_SZARRAY:
                // These have a nested type
                return TypesMatch(sourceAsm, sig1, sig1Len, ref pos1,
                                 targetAsm, sig2, sig2Len, ref pos2);

            case ELEMENT_TYPE_VALUETYPE:
            case ELEMENT_TYPE_CLASS:
                // TypeDefOrRef token - need to resolve and compare semantically
                uint token1 = DecodeTypeDefOrRef(sig1, sig1Len, ref pos1);
                uint token2 = DecodeTypeDefOrRef(sig2, sig2Len, ref pos2);
                return TypeTokensMatch(sourceAsm, token1, targetAsm, token2);

            case ELEMENT_TYPE_VAR:
            case ELEMENT_TYPE_MVAR:
                // Generic parameter index
                uint idx1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
                uint idx2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
                return idx1 == idx2;

            case ELEMENT_TYPE_GENERICINST:
                // Generic instantiation: element type + type arg count + type args
                if (!TypesMatch(sourceAsm, sig1, sig1Len, ref pos1,
                               targetAsm, sig2, sig2Len, ref pos2))
                    return false;
                uint argCount1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
                uint argCount2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
                if (argCount1 != argCount2)
                    return false;
                for (uint i = 0; i < argCount1; i++)
                {
                    if (!TypesMatch(sourceAsm, sig1, sig1Len, ref pos1,
                                   targetAsm, sig2, sig2Len, ref pos2))
                        return false;
                }
                return true;

            case ELEMENT_TYPE_ARRAY:
                // Multi-dimensional array: element type + rank + sizes + lower bounds
                if (!TypesMatch(sourceAsm, sig1, sig1Len, ref pos1,
                               targetAsm, sig2, sig2Len, ref pos2))
                    return false;
                uint rank1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
                uint rank2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
                if (rank1 != rank2)
                    return false;
                // Skip sizes
                uint numSizes1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
                uint numSizes2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
                if (numSizes1 != numSizes2)
                    return false;
                for (uint i = 0; i < numSizes1; i++)
                {
                    if (DecodeCompressedUInt(sig1, sig1Len, ref pos1) !=
                        DecodeCompressedUInt(sig2, sig2Len, ref pos2))
                        return false;
                }
                // Skip lower bounds
                uint numLoBounds1 = DecodeCompressedUInt(sig1, sig1Len, ref pos1);
                uint numLoBounds2 = DecodeCompressedUInt(sig2, sig2Len, ref pos2);
                if (numLoBounds1 != numLoBounds2)
                    return false;
                for (uint i = 0; i < numLoBounds1; i++)
                {
                    if (DecodeCompressedUInt(sig1, sig1Len, ref pos1) !=
                        DecodeCompressedUInt(sig2, sig2Len, ref pos2))
                        return false;
                }
                return true;

            case ELEMENT_TYPE_FNPTR:
                // Function pointer - compare the full method signature
                return SignaturesMatch(sourceAsm, sig1 + pos1, sig1Len - pos1,
                                      targetAsm, sig2 + pos2, sig2Len - pos2);

            default:
                // Unknown element type
                return false;
        }
    }

    /// <summary>
    /// Decode a TypeDefOrRef coded index from a signature.
    /// </summary>
    private static uint DecodeTypeDefOrRef(byte* sig, uint sigLen, ref uint pos)
    {
        uint coded = DecodeCompressedUInt(sig, sigLen, ref pos);
        // Coded index: 2 bits for table, rest for row
        // 0 = TypeDef, 1 = TypeRef, 2 = TypeSpec
        return coded;
    }

    /// <summary>
    /// Compare two type tokens across assemblies, resolving TypeRefs to check if they refer to the same type.
    /// </summary>
    private static bool TypeTokensMatch(LoadedAssembly* asm1, uint codedToken1,
                                        LoadedAssembly* asm2, uint codedToken2)
    {
        // Decode TypeDefOrRef coded index
        uint tag1 = codedToken1 & 0x03;
        uint row1 = codedToken1 >> 2;
        uint tag2 = codedToken2 & 0x03;
        uint row2 = codedToken2 >> 2;

        // Get the full type name from each token
        byte* ns1 = null, name1 = null;
        byte* ns2 = null, name2 = null;

        // Tag: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (tag1 == 0) // TypeDef
        {
            ns1 = MetadataReader.GetString(ref asm1->Metadata,
                MetadataReader.GetTypeDefNamespace(ref asm1->Tables, ref asm1->Sizes, row1));
            name1 = MetadataReader.GetString(ref asm1->Metadata,
                MetadataReader.GetTypeDefName(ref asm1->Tables, ref asm1->Sizes, row1));
        }
        else if (tag1 == 1) // TypeRef
        {
            ns1 = MetadataReader.GetString(ref asm1->Metadata,
                MetadataReader.GetTypeRefNamespace(ref asm1->Tables, ref asm1->Sizes, row1));
            name1 = MetadataReader.GetString(ref asm1->Metadata,
                MetadataReader.GetTypeRefName(ref asm1->Tables, ref asm1->Sizes, row1));
        }
        else
        {
            // TypeSpec - would need to compare the blob content
            return false;
        }

        if (tag2 == 0) // TypeDef
        {
            ns2 = MetadataReader.GetString(ref asm2->Metadata,
                MetadataReader.GetTypeDefNamespace(ref asm2->Tables, ref asm2->Sizes, row2));
            name2 = MetadataReader.GetString(ref asm2->Metadata,
                MetadataReader.GetTypeDefName(ref asm2->Tables, ref asm2->Sizes, row2));
        }
        else if (tag2 == 1) // TypeRef
        {
            ns2 = MetadataReader.GetString(ref asm2->Metadata,
                MetadataReader.GetTypeRefNamespace(ref asm2->Tables, ref asm2->Sizes, row2));
            name2 = MetadataReader.GetString(ref asm2->Metadata,
                MetadataReader.GetTypeRefName(ref asm2->Tables, ref asm2->Sizes, row2));
        }
        else
        {
            // TypeSpec - would need to compare the blob content
            return false;
        }

        // Compare namespace and name
        return StringsEqual(ns1, ns2) && StringsEqual(name1, name2);
    }

    /// <summary>
    /// Find a MethodDef token by name within a specific type.
    /// </summary>
    /// <param name="sourceAsm">The assembly containing the MemberRef (for signature resolution)</param>
    /// <param name="targetAsm">The assembly containing the MethodDef to search</param>
    private static uint FindMethodDefByName(LoadedAssembly* sourceAsm, LoadedAssembly* targetAsm, uint typeDefToken, byte* methodName, byte* sig, uint sigLen)
    {
        uint typeRow = typeDefToken & 0x00FFFFFF;
        if (typeRow == 0)
            return 0;

        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref targetAsm->Tables, ref targetAsm->Sizes, typeRow);
        uint typeDefCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodDefCount = targetAsm->Tables.RowCounts[(int)MetadataTableId.MethodDef];
        uint methodEnd;

        if (typeRow < typeDefCount)
        {
            methodEnd = MetadataReader.GetTypeDefMethodList(ref targetAsm->Tables, ref targetAsm->Sizes, typeRow + 1);
        }
        else
        {
            methodEnd = methodDefCount + 1;
        }

        // Debug: Show method range
        // DebugConsole.Write("[AsmLoader] FindMethod in type row ");
        // DebugConsole.WriteDecimal(typeRow);
        // DebugConsole.Write(": methodStart=");
        // DebugConsole.WriteDecimal(methodStart);
        // DebugConsole.Write(", methodEnd=");
        // DebugConsole.WriteDecimal(methodEnd);
        // DebugConsole.Write(", totalMethods=");
        // DebugConsole.WriteDecimal(methodDefCount);
        // DebugConsole.Write(", searching for ");
        // for (int i = 0; methodName[i] != 0 && i < 40; i++)
        //     DebugConsole.WriteChar((char)methodName[i]);
        // DebugConsole.WriteLine();

        // Sanity check: if methodStart is > totalMethods, we have corrupt metadata
        if (methodStart > methodDefCount)
        {
            DebugConsole.Write("[AsmLoader] BUG: methodStart ");
            DebugConsole.WriteDecimal(methodStart);
            DebugConsole.Write(" > totalMethods ");
            DebugConsole.WriteDecimal(methodDefCount);
            DebugConsole.WriteLine(" - metadata corruption!");
            return 0;
        }

        // Search methods in range
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            uint nameIdx = MetadataReader.GetMethodDefName(ref targetAsm->Tables, ref targetAsm->Sizes, methodRow);
            byte* defName = MetadataReader.GetString(ref targetAsm->Metadata, nameIdx);

            if (StringsEqual(methodName, defName))
            {
                // Compare signatures for overload resolution (cross-assembly aware)
                uint defSigIdx = MetadataReader.GetMethodDefSignature(ref targetAsm->Tables, ref targetAsm->Sizes, methodRow);
                byte* defSig = MetadataReader.GetBlob(ref targetAsm->Metadata, defSigIdx, out uint defSigLen);

                if (SignaturesMatch(sourceAsm, sig, sigLen, targetAsm, defSig, defSigLen))
                {
                    return 0x06000000 | methodRow;  // MethodDef token
                }
            }
        }

        return 0;
    }

    // ============================================================================
    // Unloading
    // ============================================================================

    /// <summary>
    /// Unload an assembly and free its resources.
    /// Returns false if the assembly cannot be unloaded (dependencies, CoreLib, etc.).
    /// </summary>
    public static bool Unload(uint assemblyId)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return false;

        // Check if unloadable
        if (!asm->CanUnload)
        {
            DebugConsole.Write("[AsmLoader] Cannot unload assembly ");
            asm->PrintName();
            DebugConsole.WriteLine(" (not marked as unloadable)");
            return false;
        }

        // Check if any other assembly depends on this one
        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (!_assemblies[i].IsLoaded || _assemblies[i].AssemblyId == assemblyId)
                continue;

            for (int d = 0; d < _assemblies[i].DependencyCount; d++)
            {
                if (_assemblies[i].Dependencies[d] == assemblyId)
                {
                    DebugConsole.Write("[AsmLoader] Cannot unload: ");
                    _assemblies[i].PrintName();
                    DebugConsole.WriteLine(" depends on this assembly");
                    return false;
                }
            }
        }

        DebugConsole.Write("[AsmLoader] Unloading assembly ");
        asm->PrintName();
        DebugConsole.WriteLine();

        // Remove all JIT'd methods for this assembly
        int removedMethods = CompiledMethodRegistry.RemoveByAssembly(assemblyId);
        DebugConsole.Write("[AsmLoader] Removed ");
        DebugConsole.WriteDecimal((uint)removedMethods);
        DebugConsole.WriteLine(" JIT'd methods");

        // Free resources (types, statics, etc.)
        asm->Free();
        _assemblyCount--;

        return true;
    }

    /// <summary>
    /// Initialize critical interface types from korlib that need to be available
    /// before any JIT code that uses them runs.
    /// This creates MethodTables for IDisposable and registers them with MetadataIntegration.
    /// </summary>
    public static void InitializeKorlibInterfaces(uint korlibId)
    {
        DebugConsole.WriteLine("[AsmLoader] Initializing korlib interfaces...");

        // Find and create IDisposable MethodTable
        uint iDisposableToken = FindTypeDefByFullName(korlibId, "System", "IDisposable");
        if (iDisposableToken != 0)
        {
            MethodTable* iDisposableMT = ResolveType(korlibId, iDisposableToken);
            if (iDisposableMT != null)
            {
                JIT.MetadataIntegration.RegisterIDisposableMT(iDisposableMT);
            }
            else
            {
                DebugConsole.WriteLine("[AsmLoader] WARNING: Failed to create IDisposable MT");
            }
        }
        else
        {
            DebugConsole.WriteLine("[AsmLoader] WARNING: IDisposable not found in korlib");
        }
    }

    // ============================================================================
    // Type/Method Lookup by Name
    // ============================================================================

    /// <summary>
    /// Find a TypeDef by name in an assembly.
    /// Returns the TypeDef token (0x02xxxxxx) or 0 if not found.
    /// Note: This searches by simple name only (no namespace).
    /// </summary>
    public static uint FindTypeDefByName(uint assemblyId, string name)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeCount; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            byte* typeName = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (StringEqualsLiteral(typeName, name))
            {
                return 0x02000000 | row;  // TypeDef token
            }
        }
        return 0;
    }

    /// <summary>
    /// Find a TypeDef by namespace and name in an assembly.
    /// Returns the TypeDef token (0x02xxxxxx) or 0 if not found.
    /// </summary>
    public static uint FindTypeDefByFullName(uint assemblyId, string ns, string name)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeCount; row++)
        {
            uint nameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, row);
            uint nsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, row);

            byte* typeName = MetadataReader.GetString(ref asm->Metadata, nameIdx);
            byte* typeNs = MetadataReader.GetString(ref asm->Metadata, nsIdx);

            // Match namespace (empty string or null both match empty)
            bool nsMatch = false;
            if ((ns == null || ns.Length == 0) && (typeNs == null || typeNs[0] == 0))
                nsMatch = true;
            else if (ns != null && typeNs != null && StringEqualsLiteral(typeNs, ns))
                nsMatch = true;

            if (nsMatch && StringEqualsLiteral(typeName, name))
            {
                return 0x02000000 | row;  // TypeDef token
            }
        }
        return 0;
    }

    /// <summary>
    /// Find the TypeDef that declares a given MethodDef.
    /// Returns the TypeDef token (0x02xxxxxx) or 0 if not found.
    /// </summary>
    public static uint FindDeclaringType(uint assemblyId, uint methodToken)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        // Method token must be a MethodDef (0x06xxxxxx)
        uint tableId = (methodToken >> 24) & 0xFF;
        if (tableId != 0x06)
            return 0;

        uint methodRow = methodToken & 0x00FFFFFF;
        if (methodRow == 0)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        // Iterate through TypeDefs to find which one owns this method
        for (uint typeRow = 1; typeRow <= typeCount; typeRow++)
        {
            uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow);
            uint methodEnd;
            if (typeRow < typeCount)
            {
                methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow + 1);
            }
            else
            {
                methodEnd = methodCount + 1;
            }

            // Check if this method falls within this type's method range
            if (methodRow >= methodStart && methodRow < methodEnd)
            {
                return 0x02000000 | typeRow;  // TypeDef token
            }
        }
        return 0;
    }

    /// <summary>
    /// Find a MethodDef by name within a specific TypeDef.
    /// Returns the MethodDef token (0x06xxxxxx) or 0 if not found.
    /// </summary>
    public static uint FindMethodDefByName(uint assemblyId, uint typeToken, string name)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRow = typeToken & 0x00FFFFFF;
        if (typeRow == 0)
            return 0;

        uint typeCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        // Get the method list start for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow);

        // Get the method list end (start of next type's methods, or total method count + 1)
        uint methodEnd;
        if (typeRow < typeCount)
        {
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow + 1);
        }
        else
        {
            methodEnd = methodCount + 1;
        }

        // Search methods in this type's method list
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            uint methodNameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
            byte* methodName = MetadataReader.GetString(ref asm->Metadata, methodNameIdx);

            if (StringEqualsLiteral(methodName, name))
            {
                return 0x06000000 | methodRow;  // MethodDef token
            }
        }
        return 0;
    }

    /// <summary>
    /// Compare a null-terminated heap string with a literal C# string.
    /// </summary>
    private static bool StringEqualsLiteral(byte* heapStr, string literal)
    {
        if (heapStr == null)
            return literal == null || literal.Length == 0;
        if (literal == null)
            return heapStr[0] == 0;

        for (int i = 0; i < literal.Length; i++)
        {
            if (heapStr[i] != (byte)literal[i])
                return false;
        }
        return heapStr[literal.Length] == 0;  // Ensure null-terminator
    }

    // ============================================================================
    // Statistics
    // ============================================================================

    // ============================================================================
    // Array MethodTable Creation for newarr
    // ============================================================================

    // Cache of created array MethodTables (element MT ptr -> array MT ptr)
    private const int MaxArrayMTCache = 128;
    private static MethodTable** _arrayMTCacheElementMTs;
    private static MethodTable** _arrayMTCacheArrayMTs;
    private static int _arrayMTCacheCount;

    /// <summary>
    /// Get or create an array MethodTable for a given element type.
    /// The array MT has ComponentSize set to the element size.
    /// Used by newarr opcode to properly allocate arrays.
    /// </summary>
    public static MethodTable* GetOrCreateArrayMethodTable(MethodTable* elementMT)
    {
        if (elementMT == null)
            return null;

        // Initialize cache on first use
        if (_arrayMTCacheElementMTs == null)
        {
            _arrayMTCacheElementMTs = (MethodTable**)HeapAllocator.AllocZeroed(
                (ulong)(MaxArrayMTCache * sizeof(MethodTable*)));
            _arrayMTCacheArrayMTs = (MethodTable**)HeapAllocator.AllocZeroed(
                (ulong)(MaxArrayMTCache * sizeof(MethodTable*)));
            _arrayMTCacheCount = 0;

            if (_arrayMTCacheElementMTs == null || _arrayMTCacheArrayMTs == null)
            {
                DebugConsole.WriteLine("[AsmLoader] Failed to allocate array MT cache");
                return null;
            }
        }

        // Check cache for existing array MT
        for (int i = 0; i < _arrayMTCacheCount; i++)
        {
            if (_arrayMTCacheElementMTs[i] == elementMT)
                return _arrayMTCacheArrayMTs[i];
        }

        // Compute element size
        ushort elementSize;
        if (elementMT->IsValueType)
        {
            // For primitives (Int32, etc.), ComponentSize holds the raw value size (e.g., 4 for int).
            // For user-defined structs, ComponentSize is 0 and BaseSize holds the raw struct size.
            // IMPORTANT: BaseSize for primitives is the BOXED size (includes 8-byte header), not raw size!
            ushort componentSize = elementMT->_usComponentSize;
            if (componentSize > 0)
            {
                // Primitive: use ComponentSize (raw value size)
                elementSize = componentSize;
            }
            else
            {
                // User-defined struct: use BaseSize (which IS the raw struct size for non-primitives)
                elementSize = (ushort)elementMT->BaseSize;
            }
        }
        else
        {
            // Reference type: element is a pointer (8 bytes on 64-bit)
            elementSize = 8;
        }

        // Create new array MethodTable
        MethodTable* arrayMT = (MethodTable*)HeapAllocator.AllocZeroed((ulong)MethodTable.HeaderSize);
        if (arrayMT == null)
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to allocate array MethodTable");
            return null;
        }

        // Initialize array MT
        arrayMT->_usComponentSize = elementSize;
        // Set IsArray flag (bit 19 of combined flags, which is bit 3 of _usFlags)
        arrayMT->_usFlags = (ushort)((MTFlags.IsArray | MTFlags.HasComponentSize) >> 16);
        // Array base size: MT ptr (8) + length field (8) = 16 bytes
        arrayMT->_uBaseSize = 16;
        // _relatedType points to element type's MethodTable
        arrayMT->_relatedType = elementMT;
        arrayMT->_usNumVtableSlots = 0;
        arrayMT->_usNumInterfaces = 0;
        arrayMT->_uHashCode = (uint)(ulong)elementMT;  // Use element MT address as hash

        // Cache the new array MT
        if (_arrayMTCacheCount < MaxArrayMTCache)
        {
            _arrayMTCacheElementMTs[_arrayMTCacheCount] = elementMT;
            _arrayMTCacheArrayMTs[_arrayMTCacheCount] = arrayMT;
            _arrayMTCacheCount++;
        }

        // Array MT debug (verbose - commented out)
        // DebugConsole.WriteHex((ulong)arrayMT);
        // DebugConsole.Write(" elemSize=");
        // DebugConsole.WriteDecimal(elementSize);
        // DebugConsole.Write(" for elem MT 0x");
        // DebugConsole.WriteHex((ulong)elementMT);
        // DebugConsole.Write(" baseSize=");
        // DebugConsole.WriteDecimal(elementMT->BaseSize);
        // DebugConsole.Write(" isVal=");
        // DebugConsole.Write(elementMT->IsValueType ? "Y" : "N");
        // DebugConsole.WriteLine();

        return arrayMT;
    }

    // Cache of created MD array MethodTables (element MT ptr, rank -> array MT ptr)
    private const int MaxMDArrayMTCache = 64;
    private static MethodTable** _mdArrayMTCacheElementMTs;
    private static int* _mdArrayMTCacheRanks;
    private static MethodTable** _mdArrayMTCacheArrayMTs;
    private static int _mdArrayMTCacheCount;

    /// <summary>
    /// Get or create a multi-dimensional array MethodTable for a given element type and rank.
    /// The array MT has ComponentSize set to the element size.
    /// Used by newobj for MD array construction.
    /// </summary>
    /// <param name="elementMT">Element type's MethodTable</param>
    /// <param name="rank">Number of dimensions (2, 3, etc.)</param>
    public static MethodTable* GetOrCreateMDArrayMethodTable(MethodTable* elementMT, int rank)
    {
        if (elementMT == null || rank < 2)
            return null;

        // Initialize cache on first use
        if (_mdArrayMTCacheElementMTs == null)
        {
            _mdArrayMTCacheElementMTs = (MethodTable**)HeapAllocator.AllocZeroed(
                (ulong)(MaxMDArrayMTCache * sizeof(MethodTable*)));
            _mdArrayMTCacheRanks = (int*)HeapAllocator.AllocZeroed(
                (ulong)(MaxMDArrayMTCache * sizeof(int)));
            _mdArrayMTCacheArrayMTs = (MethodTable**)HeapAllocator.AllocZeroed(
                (ulong)(MaxMDArrayMTCache * sizeof(MethodTable*)));
            _mdArrayMTCacheCount = 0;

            if (_mdArrayMTCacheElementMTs == null || _mdArrayMTCacheRanks == null || _mdArrayMTCacheArrayMTs == null)
            {
                DebugConsole.WriteLine("[AsmLoader] Failed to allocate MD array MT cache");
                return null;
            }
        }

        // Check cache for existing MD array MT
        for (int i = 0; i < _mdArrayMTCacheCount; i++)
        {
            if (_mdArrayMTCacheElementMTs[i] == elementMT && _mdArrayMTCacheRanks[i] == rank)
                return _mdArrayMTCacheArrayMTs[i];
        }

        // Compute element size
        ushort elementSize;
        if (elementMT->IsValueType)
        {
            // For primitives, ComponentSize holds the raw value size.
            // For user-defined structs, ComponentSize is 0 and BaseSize holds the raw struct size.
            ushort componentSize = elementMT->_usComponentSize;
            if (componentSize > 0)
            {
                elementSize = componentSize;
            }
            else
            {
                elementSize = (ushort)elementMT->BaseSize;
            }
        }
        else
        {
            elementSize = 8;  // Reference type pointer
        }

        // Create new MD array MethodTable
        MethodTable* arrayMT = (MethodTable*)HeapAllocator.AllocZeroed((ulong)MethodTable.HeaderSize);
        if (arrayMT == null)
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to allocate MD array MethodTable");
            return null;
        }

        // Initialize MD array MT
        arrayMT->_usComponentSize = elementSize;
        // Set IsArray flag
        arrayMT->_usFlags = (ushort)((MTFlags.IsArray | MTFlags.HasComponentSize) >> 16);
        // MD array base size: MT ptr (8) + length (4) + rank (4) + bounds (4*rank) + loBounds (4*rank)
        // = 16 + 8*rank bytes
        arrayMT->_uBaseSize = (uint)(16 + 8 * rank);
        // _relatedType points to element type's MethodTable
        arrayMT->_relatedType = elementMT;
        // Store rank in NumVtableSlots (we don't use vtable for arrays anyway)
        // This is a bit of a hack but avoids adding a new field
        arrayMT->_usNumVtableSlots = (ushort)rank;
        arrayMT->_usNumInterfaces = 0;
        arrayMT->_uHashCode = (uint)((ulong)elementMT ^ (uint)(rank << 16));

        // Cache the new MD array MT
        if (_mdArrayMTCacheCount < MaxMDArrayMTCache)
        {
            _mdArrayMTCacheElementMTs[_mdArrayMTCacheCount] = elementMT;
            _mdArrayMTCacheRanks[_mdArrayMTCacheCount] = rank;
            _mdArrayMTCacheArrayMTs[_mdArrayMTCacheCount] = arrayMT;
            _mdArrayMTCacheCount++;
        }

        DebugConsole.Write("[AsmLoader] Created MD array MT: rank=");
        DebugConsole.WriteDecimal((uint)rank);
        DebugConsole.Write(" elemSize=");
        DebugConsole.WriteDecimal(elementSize);
        DebugConsole.Write(" MT=0x");
        DebugConsole.WriteHex((ulong)arrayMT);
        DebugConsole.WriteLine();

        return arrayMT;
    }

    /// <summary>
    /// Check if a MethodTable represents an MD array and get its rank.
    /// Returns 0 for non-arrays or single-dimension arrays, rank for MD arrays.
    /// </summary>
    public static int GetMDArrayRank(MethodTable* mt)
    {
        if (mt == null || !mt->IsArray)
            return 0;

        // Check if BaseSize matches MD array pattern (16 + 8*rank)
        // Single-dim arrays have BaseSize = 16
        if (mt->BaseSize <= 16)
            return 0;

        // Calculate rank from BaseSize
        int rank = (int)(mt->BaseSize - 16) / 8;
        if (rank >= 2)
            return rank;

        return 0;
    }

    // Cache for generic instantiation MethodTables
    private const int MaxGenericInstCache = 512;
    private const int MaxTypeArgsPerInst = 4;  // Support up to 4 type args per generic type
    private static uint* _genericInstCacheDefTokens;
    private static ulong* _genericInstCacheArgHashes;  // Hash of type arg MTs
    private static MethodTable** _genericInstCacheInstMTs;
    private static byte* _genericInstCacheTypeArgCounts;  // Number of type args per entry
    private static MethodTable** _genericInstCacheTypeArgs;  // Flat array: [entry0_arg0, entry0_arg1, ..., entry1_arg0, ...]
    private static int _genericInstCacheCount;

    /// <summary>
    /// Get or create a MethodTable for an instantiated generic type (e.g., List&lt;int&gt;).
    /// </summary>
    /// <param name="genDefToken">Token for the generic type definition (e.g., List&lt;&gt;)</param>
    /// <param name="typeArgMTs">Array of MethodTable pointers for each type argument</param>
    /// <param name="typeArgCount">Number of type arguments</param>
    /// <param name="isValueType">Whether the generic type is a value type</param>
    public static MethodTable* GetOrCreateGenericInstMethodTable(uint genDefToken, MethodTable** typeArgMTs, int typeArgCount, bool isValueType)
    {
        if (typeArgCount <= 0 || typeArgMTs == null)
            return null;

        // Initialize cache on first use
        if (_genericInstCacheDefTokens == null)
        {
            _genericInstCacheDefTokens = (uint*)HeapAllocator.AllocZeroed((ulong)(MaxGenericInstCache * sizeof(uint)));
            _genericInstCacheArgHashes = (ulong*)HeapAllocator.AllocZeroed((ulong)(MaxGenericInstCache * sizeof(ulong)));
            _genericInstCacheInstMTs = (MethodTable**)HeapAllocator.AllocZeroed((ulong)(MaxGenericInstCache * sizeof(MethodTable*)));
            _genericInstCacheTypeArgCounts = (byte*)HeapAllocator.AllocZeroed((ulong)(MaxGenericInstCache * sizeof(byte)));
            _genericInstCacheTypeArgs = (MethodTable**)HeapAllocator.AllocZeroed((ulong)(MaxGenericInstCache * MaxTypeArgsPerInst * sizeof(MethodTable*)));
            _genericInstCacheCount = 0;

            if (_genericInstCacheDefTokens == null || _genericInstCacheArgHashes == null || _genericInstCacheInstMTs == null ||
                _genericInstCacheTypeArgCounts == null || _genericInstCacheTypeArgs == null)
            {
                DebugConsole.WriteLine("[AsmLoader] Failed to allocate generic inst cache");
                return null;
            }
        }

        // Compute hash of type arguments for cache lookup
        ulong argHash = 0;
        for (int i = 0; i < typeArgCount; i++)
        {
            argHash = argHash * 31 + (ulong)typeArgMTs[i];
        }

        // Debug: show cache lookup
        DebugConsole.Write("[GenInst] Lookup: def=0x");
        DebugConsole.WriteHex(genDefToken);
        DebugConsole.Write(" argHash=0x");
        DebugConsole.WriteHex(argHash);
        DebugConsole.Write(" args=[");
        for (int i = 0; i < typeArgCount; i++)
        {
            if (i > 0) DebugConsole.Write(",");
            DebugConsole.Write("0x");
            DebugConsole.WriteHex((ulong)typeArgMTs[i]);
        }
        DebugConsole.WriteLine("]");

        // Check cache for existing instantiation
        for (int i = 0; i < _genericInstCacheCount; i++)
        {
            if (_genericInstCacheDefTokens[i] == genDefToken && _genericInstCacheArgHashes[i] == argHash)
            {
                DebugConsole.Write("[GenInst] CACHE HIT at ");
                DebugConsole.WriteDecimal((uint)i);
                DebugConsole.Write(" MT=0x");
                DebugConsole.WriteHex((ulong)_genericInstCacheInstMTs[i]);
                DebugConsole.WriteLine();
                return _genericInstCacheInstMTs[i];
            }
        }

        // Try to resolve the generic type definition to get base info
        // genDefToken can be:
        //   0x02xxxxxx = TypeDef (need to look up which assembly from _currentLoadingAsm context)
        //   0x01xxxxxx = TypeRef (need to resolve to target assembly and TypeDef)
        //   other = normalized token (asmId << 24 | row) from earlier resolution
        uint tokenTable = genDefToken >> 24;
        uint defTypeDefRow = genDefToken & 0x00FFFFFF;

        MethodTable* genDefMT = null;
        LoadedAssembly* defAsm = null;
        uint defTypeDefToken = 0;

        // Check if tokenTable is a valid loaded assembly ID (indicates normalized token)
        // Assembly IDs start at 1, and we can check if it's a loaded assembly
        // Note: This can be confusing because token table IDs (0x01=TypeRef, 0x02=TypeDef)
        // overlap with low assembly IDs. We check if an assembly exists with that ID.
        LoadedAssembly* potentialAsm = (tokenTable > 0 && tokenTable < MaxAssemblies) ? GetAssembly(tokenTable) : null;

        DebugConsole.Write("[GetOrCreate] token=0x");
        DebugConsole.WriteHex(genDefToken);
        DebugConsole.Write(" table=");
        DebugConsole.WriteDecimal(tokenTable);
        DebugConsole.Write(" row=0x");
        DebugConsole.WriteHex(defTypeDefRow);
        DebugConsole.Write(" potAsm=");
        DebugConsole.WriteHex((ulong)potentialAsm);
        DebugConsole.WriteLine();

        if (potentialAsm != null)  // Normalized token (asmId << 24 | row)
        {
            // Already normalized - extract assembly ID and row
            defAsm = potentialAsm;
            defTypeDefToken = 0x02000000 | defTypeDefRow;
            genDefMT = ResolveType(tokenTable, defTypeDefToken);
        }
        else if (tokenTable == 0x02)  // Raw TypeDef token - use JIT compilation context
        {
            uint refAsmId = JIT.MetadataIntegration.GetCurrentAssemblyId();
            if (refAsmId != 0)
            {
                defAsm = GetAssembly(refAsmId);
                defTypeDefToken = 0x02000000 | defTypeDefRow;
                genDefMT = ResolveType(refAsmId, defTypeDefToken);
            }
        }
        else if (tokenTable == 0x01)  // Raw TypeRef token - need to resolve to target assembly
        {
            uint refAsmId = JIT.MetadataIntegration.GetCurrentAssemblyId();
            LoadedAssembly* refAsm = GetAssembly(refAsmId);
            if (refAsm != null)
            {
                LoadedAssembly* targetAsm;
                if (ResolveTypeRefToTypeDef(refAsm, defTypeDefRow, out targetAsm, out defTypeDefToken))
                {
                    if (targetAsm != null && defTypeDefToken != 0)
                    {
                        defAsm = targetAsm;
                        genDefMT = ResolveType(targetAsm->AssemblyId, defTypeDefToken);
                    }
                }
            }

            if (genDefMT == null)
            {
                DebugConsole.Write("[GenInst] WARN: TypeRef 0x01");
                DebugConsole.WriteHex(defTypeDefRow);
                DebugConsole.WriteLine(" could not be resolved");
            }
        }
        if (genDefMT == null)
        {
            // Generic type definition not found - create a minimal MT
            // This happens for types like List<T> from System.Collections.Generic
            // that we don't have full metadata for
            DebugConsole.Write("[AsmLoader] GenericInst: def token 0x");
            DebugConsole.WriteHex(genDefToken);
            DebugConsole.WriteLine(" not resolved, creating minimal MT");
        }

        // Determine vtable slots count and interface count from the generic definition
        ushort numVtableSlots = 3;  // Default: Object's 3 virtual methods (ToString, Equals, GetHashCode)
        ushort numInterfaces = 0;
        if (genDefMT != null)
        {
            numVtableSlots = genDefMT->_usNumVtableSlots;
            numInterfaces = genDefMT->_usNumInterfaces;
            DebugConsole.Write("[GenInst] genDefMT=0x");
            DebugConsole.WriteHex((ulong)genDefMT);
            DebugConsole.Write(" slots=");
            DebugConsole.WriteDecimal(numVtableSlots);
            DebugConsole.Write(" ifaces=");
            DebugConsole.WriteDecimal(numInterfaces);
            DebugConsole.WriteLine();
        }
        else
        {
            DebugConsole.Write("[GenInst] genDefMT is NULL for token 0x");
            DebugConsole.WriteHex(genDefToken);
            DebugConsole.WriteLine();
        }

        // Create new instantiated MethodTable with vtable and interface map space
        ulong mtSize = (ulong)(MethodTable.HeaderSize + numVtableSlots * sizeof(nint) + numInterfaces * InterfaceMapEntry.Size);
        MethodTable* instMT = (MethodTable*)HeapAllocator.AllocZeroed(mtSize);
        if (instMT == null)
        {
            DebugConsole.WriteLine("[AsmLoader] Failed to allocate generic inst MethodTable");
            return null;
        }

        // Copy base info from generic definition if available
        if (genDefMT != null)
        {
            instMT->_usFlags = genDefMT->_usFlags;
            instMT->_usNumVtableSlots = genDefMT->_usNumVtableSlots;
            instMT->_usNumInterfaces = genDefMT->_usNumInterfaces;

            // For value types, recalculate the base size with type context
            // This is critical for generic value types like HashSet<T>.Enumerator where
            // fields of type T need to use the actual type argument size, not 8 bytes
            if (isValueType && defAsm != null && defTypeDefToken != 0)
            {
                uint typeDefRow = defTypeDefToken & 0x00FFFFFF;

                // Set type context before computing size
                int savedContextCnt = JIT.MetadataIntegration.GetTypeTypeArgCount();
                MethodTable** savedCtxArgs = stackalloc MethodTable*[savedContextCnt > 0 ? savedContextCnt : 1];
                for (int ctx = 0; ctx < savedContextCnt; ctx++)
                    savedCtxArgs[ctx] = JIT.MetadataIntegration.GetTypeTypeArgMethodTable(ctx);

                JIT.MetadataIntegration.SetTypeTypeArgs(typeArgMTs, typeArgCount);

                // Recalculate the instance size with type context set
                uint computedSize = ComputeInstanceSize(defAsm, typeDefRow, true);
                instMT->_uBaseSize = computedSize;

                DebugConsole.Write("[GenInst] ValueType recalculated size: ");
                DebugConsole.WriteDecimal(computedSize);
                DebugConsole.Write(" (was ");
                DebugConsole.WriteDecimal(genDefMT->_uBaseSize);
                DebugConsole.WriteLine(")");

                // Restore context
                if (savedContextCnt > 0)
                    JIT.MetadataIntegration.SetTypeTypeArgs(savedCtxArgs, savedContextCnt);
                else
                    JIT.MetadataIntegration.ClearTypeTypeArgs();
            }
            else
            {
                instMT->_uBaseSize = genDefMT->_uBaseSize;
            }

            // Copy vtable entries from the generic definition
            if (numVtableSlots > 0)
            {
                nint* srcVtable = genDefMT->GetVtablePtr();
                nint* dstVtable = instMT->GetVtablePtr();
                for (int i = 0; i < numVtableSlots; i++)
                {
                    dstVtable[i] = srcVtable[i];
                }

                // For reference types, ensure Object vtable entries (slots 0-2) are populated
                // The open generic type's vtable might have empty inherited slots
                if (!isValueType && numVtableSlots >= 3)
                {
                    if (dstVtable[0] == 0)
                        dstVtable[0] = AotMethodRegistry.LookupByName("System.Object", "ToString");
                    if (dstVtable[1] == 0)
                        dstVtable[1] = AotMethodRegistry.LookupByName("System.Object", "Equals");
                    if (dstVtable[2] == 0)
                        dstVtable[2] = AotMethodRegistry.LookupByName("System.Object", "GetHashCode");
                }

                // For slots >= 3 that are empty, eagerly compile them
                // This is needed because AOT code (korlib) doesn't call EnsureVtableSlotCompiled
                // and will fail if vtable slots are empty
                for (int i = 3; i < numVtableSlots; i++)
                {
                    if (dstVtable[i] == 0)
                    {
                        // Try to find and compile the method for this slot
                        // Look up by the generic definition's MT and slot
                        JIT.CompiledMethodInfo* info = JIT.CompiledMethodRegistry.LookupByVtableSlot(genDefMT, (short)i);

                        // Debug: trace failed lookups for slot 3
                        if (info == null && i == 3)
                        {
                            DebugConsole.Write("[GenInst] LookupByVtableSlot failed for slot ");
                            DebugConsole.WriteDecimal((uint)i);
                            DebugConsole.Write(" genDefMT=0x");
                            DebugConsole.WriteHex((ulong)genDefMT);
                            DebugConsole.Write(" instMT=0x");
                            DebugConsole.WriteHex((ulong)instMT);
                            DebugConsole.WriteLine();
                        }

                        if (info != null && info->Token != 0)
                        {
                            // Set up type context for JIT compilation
                            int savedContext = JIT.MetadataIntegration.GetTypeTypeArgCount();
                            MethodTable** savedArgs = stackalloc MethodTable*[savedContext > 0 ? savedContext : 1];
                            for (int j = 0; j < savedContext; j++)
                                savedArgs[j] = JIT.MetadataIntegration.GetTypeTypeArgMethodTable(j);

                            JIT.MetadataIntegration.SetTypeTypeArgs(typeArgMTs, typeArgCount);

                            DebugConsole.Write("[GenInst] Eager compile slot ");
                            DebugConsole.WriteDecimal((uint)i);
                            DebugConsole.Write(" token=0x");
                            DebugConsole.WriteHex(info->Token);
                            DebugConsole.Write(" for MT=0x");
                            DebugConsole.WriteHex((ulong)instMT);
                            DebugConsole.WriteLine();

                            // JIT compile the method
                            JIT.JitResult jitResult = JIT.Tier0JIT.CompileMethod(info->AssemblyId, info->Token);
                            if (jitResult.Success && jitResult.CodeAddress != null)
                            {
                                dstVtable[i] = (nint)jitResult.CodeAddress;
                                DebugConsole.Write("[GenInst] Slot ");
                                DebugConsole.WriteDecimal((uint)i);
                                DebugConsole.Write(" = 0x");
                                DebugConsole.WriteHex((ulong)jitResult.CodeAddress);
                                DebugConsole.WriteLine();
                            }

                            // Restore context
                            if (savedContext > 0)
                                JIT.MetadataIntegration.SetTypeTypeArgs(savedArgs, savedContext);
                            else
                                JIT.MetadataIntegration.ClearTypeTypeArgs();
                        }
                    }
                }
            }

            // Populate interface map with instantiated interface MTs
            if (numInterfaces > 0)
            {
                // Extract assembly ID and TypeDef row from normalized token
                uint asmId = genDefToken >> 24;
                uint typeDefRow = genDefToken & 0x00FFFFFF;
                LoadedAssembly* defAsmInner = GetAssembly(asmId);

                if (defAsmInner != null)
                {
                    // Save the current type context before overwriting
                    // This is important for nested generic type resolution (e.g., EqualityComparer<T> inside List<T>)
                    int savedTypeArgCount = JIT.MetadataIntegration.GetTypeTypeArgCount();
                    MethodTable** savedTypeArgs = stackalloc MethodTable*[savedTypeArgCount > 0 ? savedTypeArgCount : 1];
                    for (int i = 0; i < savedTypeArgCount; i++)
                    {
                        savedTypeArgs[i] = JIT.MetadataIntegration.GetTypeTypeArgMethodTable(i);
                    }

                    // Set the type context so type variable resolution works
                    JIT.MetadataIntegration.SetTypeTypeArgs(typeArgMTs, typeArgCount);

                    // Calculate base vtable slots (from parent type, typically Object=3)
                    // Interface methods start after inherited slots
                    ushort baseVtableSlots = 3;  // Default: Object has 3 slots

                    // Value types don't inherit Object's vtable slots
                    // Their interface methods start at slot 0
                    if (genDefMT->IsValueType)
                    {
                        baseVtableSlots = 0;
                    }
                    else if (genDefMT->_relatedType != null)
                    {
                        baseVtableSlots = genDefMT->_relatedType->_usNumVtableSlots;
                    }

                    // First, copy inherited interface map entries from base class
                    // This is needed for types like ObjectComparer<T> that inherit interfaces
                    // from their generic base class (Comparer<T>) but don't directly declare them
                    // IMPORTANT: We need to get the INSTANTIATED base class MT (e.g., Comparer<int>)
                    // not the open generic one (Comparer<T>). The type context is already set.

                    // Get the base class reference from the TypeDef metadata and resolve it
                    // with the current type context to get the instantiated base class
                    CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref defAsm->Tables, ref defAsm->Sizes, typeDefRow);
                    MethodTable* instBaseMT = null;
                    if (extendsIdx.RowId != 0 && !IsObjectBase(defAsm, extendsIdx) && !IsValueTypeBase(defAsm, extendsIdx))
                    {
                        // TypeDefOrRef coded index: TypeDef=0, TypeRef=1, TypeSpec=2
                        if (extendsIdx.Table == MetadataTableId.TypeSpec)
                        {
                            // TypeSpec - this is a generic base class like Comparer<T>
                            // With type context set, resolving it will give us Comparer<int>
                            uint typeSpecToken = 0x1B000000 | extendsIdx.RowId;
                            instBaseMT = ResolveTypeSpec(defAsm, typeSpecToken);
                            DebugConsole.Write("[GenInst] Resolved TypeSpec base class MT=0x");
                            DebugConsole.WriteHex((ulong)instBaseMT);
                            if (instBaseMT != null)
                            {
                                DebugConsole.Write(" ifaces=");
                                DebugConsole.WriteDecimal(instBaseMT->_usNumInterfaces);
                            }
                            DebugConsole.WriteLine();
                        }
                        else if (extendsIdx.Table == MetadataTableId.TypeDef)
                        {
                            uint baseToken = 0x02000000 | extendsIdx.RowId;
                            instBaseMT = ResolveType(defAsm->AssemblyId, baseToken);
                        }
                        else if (extendsIdx.Table == MetadataTableId.TypeRef)
                        {
                            LoadedAssembly* targetAsm;
                            uint targetToken;
                            if (ResolveTypeRefToTypeDef(defAsm, extendsIdx.RowId, out targetAsm, out targetToken))
                            {
                                if (targetAsm != null && targetToken != 0)
                                    instBaseMT = ResolveType(targetAsm->AssemblyId, targetToken);
                            }
                        }
                    }

                    int inheritedIfaceCount = 0;
                    if (instBaseMT != null && instBaseMT->_usNumInterfaces > 0)
                    {
                        inheritedIfaceCount = instBaseMT->_usNumInterfaces;
                        InterfaceMapEntry* srcMap = instBaseMT->GetInterfaceMapPtr();
                        InterfaceMapEntry* dstMap = instMT->GetInterfaceMapPtr();

                        DebugConsole.Write("[GenInst] Copying ");
                        DebugConsole.WriteDecimal((uint)inheritedIfaceCount);
                        DebugConsole.WriteLine(" inherited interfaces from instantiated base");

                        for (int i = 0; i < inheritedIfaceCount; i++)
                        {
                            dstMap[i].InterfaceMT = srcMap[i].InterfaceMT;
                            dstMap[i].StartSlot = srcMap[i].StartSlot;
                        }
                    }

                    // Populate the directly declared interfaces (after inherited ones)
                    // Note: PopulateGenericInstInterfaceMap will fill map entries starting at index 0,
                    // so we need to adjust if there are inherited interfaces
                    // For now, just call it - it will only populate entries for directly declared interfaces
                    PopulateGenericInstInterfaceMap(defAsm, typeDefRow, instMT, baseVtableSlots);

                    // Restore the previous type context (don't clear - that breaks nested resolution)
                    if (savedTypeArgCount > 0)
                    {
                        JIT.MetadataIntegration.SetTypeTypeArgs(savedTypeArgs, savedTypeArgCount);
                    }
                    else
                    {
                        JIT.MetadataIntegration.ClearTypeTypeArgs();
                    }
                }
            }
        }
        else
        {
            // Default to reference type with object header size
            instMT->_uBaseSize = 24;  // MT pointer (8) + sync block (8) + min fields (8)
            instMT->_usFlags = 0;
            instMT->_usNumVtableSlots = 3;
            instMT->_usNumInterfaces = 0;

            // Set up default Object vtable entries
            nint* vtable = instMT->GetVtablePtr();
            vtable[0] = AotMethodRegistry.LookupByName("System.Object", "ToString");
            vtable[1] = AotMethodRegistry.LookupByName("System.Object", "Equals");
            vtable[2] = AotMethodRegistry.LookupByName("System.Object", "GetHashCode");
        }

        // Mark as value type if specified
        if (isValueType)
        {
            instMT->_usFlags |= (ushort)(MTFlags.IsValueType >> 16);
        }

        // Check if this is Nullable<T> and mark it for special boxing/unboxing
        if (isValueType && typeArgCount == 1 && IsNullableGenericDef(genDefToken))
        {
            instMT->_usFlags |= (ushort)(MTFlags.IsNullable >> 16);
        }

        // Store pointer to first type argument MT in _relatedType (for simple generic lookups)
        instMT->_relatedType = typeArgMTs[0];

        // Generate unique hash code for this instantiation
        instMT->_uHashCode = (uint)(genDefToken ^ argHash ^ (argHash >> 32));

        // Cache the new instantiated MT
        if (_genericInstCacheCount < MaxGenericInstCache)
        {
            DebugConsole.Write("[GenInst] CACHE MISS - creating at ");
            DebugConsole.WriteDecimal((uint)_genericInstCacheCount);
            DebugConsole.Write(" MT=0x");
            DebugConsole.WriteHex((ulong)instMT);
            DebugConsole.WriteLine();
            _genericInstCacheDefTokens[_genericInstCacheCount] = genDefToken;
            _genericInstCacheArgHashes[_genericInstCacheCount] = argHash;
            _genericInstCacheInstMTs[_genericInstCacheCount] = instMT;

            // Store type arguments (up to MaxTypeArgsPerInst)
            int argsToStore = typeArgCount < MaxTypeArgsPerInst ? typeArgCount : MaxTypeArgsPerInst;
            _genericInstCacheTypeArgCounts[_genericInstCacheCount] = (byte)argsToStore;
            int baseIdx = _genericInstCacheCount * MaxTypeArgsPerInst;
            for (int i = 0; i < argsToStore; i++)
            {
                _genericInstCacheTypeArgs[baseIdx + i] = typeArgMTs[i];
            }

            _genericInstCacheCount++;
        }

        // Register type info for this instantiated MT so JitStubs can look it up
        // This is needed for interface method resolution (e.g., IComparer<int>.Compare)
        // We register with the generic definition's assembly ID and TypeDef token
        if (defAsm != null)
        {
            Reflection.ReflectionRuntime.RegisterTypeInfo(defAsm->AssemblyId, defTypeDefToken, instMT);
        }

        // Debug output
        DebugConsole.Write("[AsmLoader] Created GenericInst MT 0x");
        DebugConsole.WriteHex((ulong)instMT);
        DebugConsole.Write(" for def 0x");
        DebugConsole.WriteHex(genDefToken);
        DebugConsole.Write(" with ");
        DebugConsole.WriteDecimal((uint)typeArgCount);
        DebugConsole.Write(" type args, isVT=");
        DebugConsole.Write(isValueType ? "Y" : "N");
        DebugConsole.Write(" [args:");
        for (int i = 0; i < typeArgCount && i < 4; i++)
        {
            DebugConsole.Write(" 0x");
            DebugConsole.WriteHex((ulong)typeArgMTs[i]);
        }
        DebugConsole.Write("]");
        DebugConsole.WriteLine();

        return instMT;
    }

    /// <summary>
    /// Propagate a vtable slot update to all instantiated MTs for a generic type.
    /// When a virtual method on a generic type is JIT compiled, the vtable slot is updated
    /// on the generic definition's MT. But instantiated MTs (like Container`1<int>)
    /// that were created earlier may have copied null vtable entries before the methods
    /// were compiled. This function updates those cached instantiated MTs.
    /// </summary>
    public static void PropagateVtableSlotToInstantiations(uint assemblyId, uint typeDefRow, int vtableSlot, nint nativeCode)
    {
        // Create normalized token for this type definition
        uint normalizedGenDefToken = (assemblyId << 24) | typeDefRow;

        // Debug: show what we're looking for
        DebugConsole.Write("[PropagateVT] Searching for def 0x");
        DebugConsole.WriteHex(normalizedGenDefToken);
        DebugConsole.Write(" slot ");
        DebugConsole.WriteDecimal((uint)vtableSlot);
        DebugConsole.Write(" in cache (count=");
        DebugConsole.WriteDecimal((uint)_genericInstCacheCount);
        DebugConsole.WriteLine(")");

        // Scan the generic instantiation cache for MTs derived from this generic definition
        for (int i = 0; i < _genericInstCacheCount; i++)
        {
            if (_genericInstCacheDefTokens[i] == normalizedGenDefToken)
            {
                MethodTable* instMT = _genericInstCacheInstMTs[i];
                if (instMT != null && vtableSlot < instMT->_usNumVtableSlots)
                {
                    nint* vtable = instMT->GetVtablePtr();
                    if (vtable[vtableSlot] == 0)  // Only update if currently null
                    {
                        DebugConsole.Write("[PropagateVT] Updated slot ");
                        DebugConsole.WriteDecimal((uint)vtableSlot);
                        DebugConsole.Write(" on MT 0x");
                        DebugConsole.WriteHex((ulong)instMT);
                        DebugConsole.Write(" with code 0x");
                        DebugConsole.WriteHex((ulong)nativeCode);
                        DebugConsole.WriteLine();
                        vtable[vtableSlot] = nativeCode;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Get the generic definition MethodTable for an instantiated generic type.
    /// Returns null if the MT is not an instantiated generic or if the definition can't be found.
    /// </summary>
    public static MethodTable* GetGenericDefinitionMT(MethodTable* instMT)
    {
        if (instMT == null || _genericInstCacheCount == 0)
            return null;

        // Search the cache for this instantiated MT
        for (int i = 0; i < _genericInstCacheCount; i++)
        {
            if (_genericInstCacheInstMTs[i] == instMT)
            {
                // Found it - now resolve the generic definition token
                uint genDefToken = _genericInstCacheDefTokens[i];

                // The genDefToken is in normalized format: (asmId << 24) | typeDefRow
                uint defAsmId = genDefToken >> 24;
                uint defTypeDefRow = genDefToken & 0x00FFFFFF;
                uint defTypeDefToken = 0x02000000 | defTypeDefRow;

                // Resolve to get the generic definition MT
                return ResolveType(defAsmId, defTypeDefToken);
            }
        }

        return null;
    }

    /// <summary>
    /// Get the type arguments for an instantiated generic type.
    /// Returns true if the MT is a generic instantiation and type args were retrieved.
    /// </summary>
    /// <param name="instMT">The instantiated MethodTable to look up</param>
    /// <param name="typeArgMTs">Output buffer for type argument MTs (should have at least MaxTypeArgsPerInst slots)</param>
    /// <param name="typeArgCount">Output: number of type arguments</param>
    public static bool GetGenericInstTypeArgs(MethodTable* instMT, MethodTable** typeArgMTs, out int typeArgCount)
    {
        typeArgCount = 0;

        if (instMT == null || _genericInstCacheCount == 0 || typeArgMTs == null)
            return false;

        // Search the cache for this instantiated MT
        for (int i = 0; i < _genericInstCacheCount; i++)
        {
            if (_genericInstCacheInstMTs[i] == instMT)
            {
                // Found it - copy type arguments
                typeArgCount = _genericInstCacheTypeArgCounts[i];
                int baseIdx = i * MaxTypeArgsPerInst;
                for (int j = 0; j < typeArgCount; j++)
                {
                    typeArgMTs[j] = _genericInstCacheTypeArgs[baseIdx + j];
                }
                return true;
            }
        }

        return false;
    }

    // ============================================================================
    // Field RVA Resolution (for array initializers)
    // ============================================================================

    /// <summary>
    /// Get the data address for a field that has an RVA entry (static data like array initializers).
    /// The FieldRVA table maps FieldDef row IDs to RVAs where static data is stored in the PE.
    /// Returns null if the field has no RVA (not a static data field).
    /// </summary>
    /// <param name="assemblyId">Assembly containing the field</param>
    /// <param name="fieldDefRowId">FieldDef row ID (not token - just the row number)</param>
    /// <returns>Pointer to the field's static data, or null if not found</returns>
    public static byte* GetFieldDataAddress(uint assemblyId, uint fieldDefRowId)
    {
        LoadedAssembly* asm = GetAssembly(assemblyId);
        if (asm == null)
            return null;

        // Get the number of rows in the FieldRVA table
        uint fieldRvaRowCount = asm->Tables.RowCounts[(int)MetadataTableId.FieldRVA];
        if (fieldRvaRowCount == 0)
            return null;

        // Search the FieldRVA table for a matching field
        // FieldRVA layout: RVA (4 bytes) + Field (field index)
        for (uint row = 1; row <= fieldRvaRowCount; row++)
        {
            uint fieldRowId = MetadataReader.GetFieldRvaField(ref asm->Tables, ref asm->Sizes, row);
            if (fieldRowId == fieldDefRowId)
            {
                // Found the field - get the RVA
                uint rva = MetadataReader.GetFieldRvaRva(ref asm->Tables, ref asm->Sizes, row);
                if (rva == 0)
                    return null;

                // Convert RVA to actual address
                // RVA is relative to section virtual addresses, not file offsets
                // Use PEHelper.RvaToFilePointer to do proper section-aware conversion
                byte* dataAddress = (byte*)PEHelper.RvaToFilePointer(asm->ImageBase, rva);

                DebugConsole.Write("[AsmLoader] GetFieldDataAddress: field row ");
                DebugConsole.WriteDecimal(fieldDefRowId);
                DebugConsole.Write(" RVA=0x");
                DebugConsole.WriteHex(rva);
                DebugConsole.Write(" addr=0x");
                DebugConsole.WriteHex((ulong)dataAddress);
                DebugConsole.WriteLine();

                return dataAddress;
            }
        }

        return null;  // Field not in FieldRVA table (not static data)
    }

    /// <summary>
    /// Get the data address for a field token (FieldDef or MemberRef).
    /// This handles the token-to-row-ID conversion and cross-assembly resolution.
    /// </summary>
    /// <param name="assemblyId">Assembly where the token appears</param>
    /// <param name="fieldToken">Field token (FieldDef 0x04 or MemberRef 0x0A)</param>
    /// <returns>Pointer to the field's static data, or null if not found</returns>
    public static byte* GetFieldDataAddressByToken(uint assemblyId, uint fieldToken)
    {
        byte tableId = (byte)(fieldToken >> 24);
        uint rowId = fieldToken & 0x00FFFFFF;

        if (tableId == 0x04)  // FieldDef
        {
            // Direct field definition - look up in this assembly
            return GetFieldDataAddress(assemblyId, rowId);
        }
        else if (tableId == 0x0A)  // MemberRef
        {
            // Cross-assembly reference - need to resolve to target assembly
            uint targetFieldToken, targetAsmId;
            if (ResolveMemberRefField(assemblyId, fieldToken, out targetFieldToken, out targetAsmId))
            {
                uint targetRowId = targetFieldToken & 0x00FFFFFF;
                return GetFieldDataAddress(targetAsmId, targetRowId);
            }
        }

        return null;
    }

    /// <summary>
    /// Print loader statistics.
    /// </summary>
    public static void PrintStatistics()
    {
        DebugConsole.WriteLine("[AsmLoader] Statistics:");
        DebugConsole.Write("  Loaded assemblies: ");
        DebugConsole.WriteDecimal((uint)_assemblyCount);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(MaxAssemblies);
        DebugConsole.WriteLine();

        for (int i = 0; i < MaxAssemblies; i++)
        {
            if (!_assemblies[i].IsLoaded)
                continue;

            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(_assemblies[i].AssemblyId);
            DebugConsole.Write("] ");
            _assemblies[i].PrintName();
            DebugConsole.Write(" - ");
            DebugConsole.WriteDecimal((uint)_assemblies[i].Types.Count);
            DebugConsole.Write(" types, ");
            DebugConsole.WriteDecimal((uint)_assemblies[i].Statics.FieldCount);
            DebugConsole.WriteLine(" statics");
        }
    }
}
