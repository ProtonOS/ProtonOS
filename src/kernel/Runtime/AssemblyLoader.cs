// ProtonOS - Multi-Assembly Loader
// Manages loading, tracking, and unloading of .NET assemblies.
// Each assembly gets its own context (metadata, type registry, static storage).

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

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
/// </summary>
public unsafe struct TypeRegistry
{
    public const int MaxTypes = 256;

    /// <summary>Array of type entries.</summary>
    public TypeRegistryEntry* Entries;

    /// <summary>Number of registered types.</summary>
    public int Count;

    /// <summary>Owner assembly ID.</summary>
    public uint AssemblyId;

    /// <summary>
    /// Initialize the type registry (allocates storage).
    /// </summary>
    public bool Initialize(uint assemblyId)
    {
        AssemblyId = assemblyId;
        Count = 0;
        Entries = (TypeRegistryEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxTypes * sizeof(TypeRegistryEntry)));
        return Entries != null;
    }

    /// <summary>
    /// Register a type token to MethodTable mapping.
    /// </summary>
    public bool Register(uint token, MethodTable* mt)
    {
        if (Entries == null || mt == null)
            return false;

        // Check if already registered
        for (int i = 0; i < Count; i++)
        {
            if (Entries[i].Token == token)
            {
                Entries[i].MT = mt;  // Update existing
                return true;
            }
        }

        // Add new entry
        if (Count >= MaxTypes)
            return false;

        Entries[Count].Token = token;
        Entries[Count].MT = mt;
        Count++;
        return true;
    }

    /// <summary>
    /// Look up a MethodTable by token.
    /// </summary>
    public MethodTable* Lookup(uint token)
    {
        if (Entries == null)
            return null;

        for (int i = 0; i < Count; i++)
        {
            if (Entries[i].Token == token)
                return Entries[i].MT;
        }
        return null;
    }

    /// <summary>
    /// Free the registry storage.
    /// </summary>
    public void Free()
    {
        if (Entries != null)
        {
            HeapAllocator.Free(Entries);
            Entries = null;
        }
        Count = 0;
    }
}

/// <summary>
/// Per-assembly static field storage.
/// Each LoadedAssembly has its own storage block for clean unloading.
/// </summary>
public unsafe struct StaticFieldStorage
{
    public const int StorageBlockSize = 64 * 1024;  // 64KB per assembly
    public const int MaxFields = 256;

    /// <summary>Allocated storage block.</summary>
    public byte* Storage;

    /// <summary>Bytes used in storage block.</summary>
    public int Used;

    /// <summary>Owner assembly ID.</summary>
    public uint AssemblyId;

    /// <summary>Array of field tracking entries.</summary>
    public StaticFieldEntry* Fields;

    /// <summary>Number of registered static fields.</summary>
    public int FieldCount;

    /// <summary>
    /// Initialize the static field storage (allocates on first use).
    /// </summary>
    public bool Initialize(uint assemblyId)
    {
        AssemblyId = assemblyId;
        Used = 0;
        FieldCount = 0;
        Storage = null;  // Allocated on demand

        Fields = (StaticFieldEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxFields * sizeof(StaticFieldEntry)));
        return Fields != null;
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
    /// </summary>
    public void* Register(uint token, uint typeToken, int size, bool isGCRef)
    {
        if (Fields == null)
            return null;

        // Check if already registered
        for (int i = 0; i < FieldCount; i++)
        {
            if (Fields[i].Token == token)
                return Fields[i].Address;
        }

        // Allocate storage
        void* addr = Allocate(size);
        if (addr == null)
            return null;

        // Register
        if (FieldCount >= MaxFields)
            return null;

        Fields[FieldCount].Token = token;
        Fields[FieldCount].TypeToken = typeToken;
        Fields[FieldCount].Address = addr;
        Fields[FieldCount].Size = size;
        Fields[FieldCount].IsGCRef = isGCRef;
        FieldCount++;

        return addr;
    }

    /// <summary>
    /// Look up a static field's address.
    /// </summary>
    public void* Lookup(uint token)
    {
        if (Fields == null)
            return null;

        for (int i = 0; i < FieldCount; i++)
        {
            if (Fields[i].Token == token)
                return Fields[i].Address;
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
        if (Fields != null)
        {
            HeapAllocator.Free(Fields);
            Fields = null;
        }
        Used = 0;
        FieldCount = 0;
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
                return asm->Types.Lookup(token);

            case 0x01:  // TypeRef - resolve cross-assembly
                return ResolveTypeRef(asm, token);

            case 0x1B:  // TypeSpec - generic instantiation (not implemented)
                return null;

            default:
                return null;
        }
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

        // ResolutionScope points to Module, ModuleRef, AssemblyRef, or TypeRef
        if (resScope.Table == MetadataTableId.AssemblyRef)
        {
            // Find the target assembly
            uint targetAsmId = ResolveAssemblyRef(sourceAsm, resScope.RowId);
            if (targetAsmId == InvalidAssemblyId)
                return null;

            LoadedAssembly* targetAsm = GetAssembly(targetAsmId);
            if (targetAsm == null)
                return null;

            // Get the type name and namespace from the TypeRef
            uint nameIdx = MetadataReader.GetTypeRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);
            uint nsIdx = MetadataReader.GetTypeRefNamespace(ref sourceAsm->Tables, ref sourceAsm->Sizes, rowId);

            // Find the matching TypeDef in the target assembly
            return FindTypeDefByName(targetAsm, nameIdx, nsIdx, &sourceAsm->Metadata);
        }

        // Other resolution scopes not yet implemented
        return null;
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
            return sourceAsm->Dependencies[assemblyRefRow - 1];
        }

        // Get the assembly name from AssemblyRef table
        uint nameIdx = MetadataReader.GetAssemblyRefName(ref sourceAsm->Tables, ref sourceAsm->Sizes, assemblyRefRow);
        byte* name = MetadataReader.GetString(ref sourceAsm->Metadata, nameIdx);

        if (name == null)
            return InvalidAssemblyId;

        // Find loaded assembly by name
        LoadedAssembly* target = FindByName(name);
        if (target == null)
        {
            DebugConsole.Write("[AsmLoader] AssemblyRef not found: ");
            for (int i = 0; name[i] != 0 && i < 64; i++)
                DebugConsole.WriteChar((char)name[i]);
            DebugConsole.WriteLine();
            return InvalidAssemblyId;
        }

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
                return asm->Types.Lookup(typeDefToken);
            }
        }

        return null;
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

                // Check if this assembly owns the MethodTable
                for (int j = 0; j < _assemblies[i].Types.Count; j++)
                {
                    if (_assemblies[i].Types.Entries[j].MT == mt)
                    {
                        targetAsm = &_assemblies[i];
                        typeDefToken = _assemblies[i].Types.Entries[j].Token;
                        break;
                    }
                }
                if (targetAsm != null)
                    break;
            }
        }
        else if (classRef.Table == MetadataTableId.TypeDef)
        {
            // MemberRef in same assembly (unusual but possible)
            targetAsm = sourceAsm;
            typeDefToken = 0x02000000 | classRef.RowId;
        }
        else
        {
            // Other class types (ModuleRef, MethodDef, TypeSpec) not implemented
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

    // ============================================================================
    // Statistics
    // ============================================================================

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
