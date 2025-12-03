// ProtonOS JIT - Metadata Integration Layer
// Connects metadata tokens to runtime artifacts (MethodTable pointers, field addresses, etc.)
// This is the "glue" between MetadataReader and the JIT compiler's resolver interfaces.
//
// Phase 2: Routes type/field resolution through AssemblyLoader's per-assembly registries.

using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Entry in the type registry mapping TypeDef/TypeRef tokens to MethodTable pointers.
/// </summary>
public unsafe struct TypeRegistryEntry
{
    /// <summary>Metadata token (TypeDef 0x02xxxxxx, TypeRef 0x01xxxxxx, TypeSpec 0x1Bxxxxxx).</summary>
    public uint Token;

    /// <summary>Pointer to the runtime MethodTable for this type.</summary>
    public MethodTable* MT;

    /// <summary>True if this slot is in use.</summary>
    public bool IsUsed => Token != 0;
}

/// <summary>
/// Entry for tracking static field storage.
/// </summary>
public unsafe struct StaticFieldEntry
{
    /// <summary>Field metadata token.</summary>
    public uint Token;

    /// <summary>Containing type's metadata token.</summary>
    public uint TypeToken;

    /// <summary>Pointer to the allocated static storage.</summary>
    public void* Address;

    /// <summary>Size of the field in bytes.</summary>
    public int Size;

    /// <summary>True if this is a GC reference.</summary>
    public bool IsGCRef;

    /// <summary>True if this slot is in use.</summary>
    public bool IsUsed => Token != 0;
}

/// <summary>
/// Cached field layout information for faster subsequent lookups.
/// </summary>
public unsafe struct FieldLayoutEntry
{
    /// <summary>Field metadata token.</summary>
    public uint Token;

    /// <summary>Byte offset within the object (for instance fields) or 0 for statics.</summary>
    public int Offset;

    /// <summary>Size of the field in bytes.</summary>
    public byte Size;

    /// <summary>True if the field value should be sign-extended when loaded.</summary>
    public bool IsSigned;

    /// <summary>True if this is a static field.</summary>
    public bool IsStatic;

    /// <summary>True if this is a GC reference type.</summary>
    public bool IsGCRef;

    /// <summary>For statics: pointer to storage. For instance: null.</summary>
    public void* StaticAddress;

    /// <summary>True if this slot is in use.</summary>
    public bool IsUsed => Token != 0;
}

/// <summary>
/// Metadata Integration Layer - connects JIT resolvers to metadata and runtime.
/// Provides TypeResolver, FieldResolver, and StringResolver implementations.
///
/// Phase 2: Uses AssemblyLoader's per-assembly registries for type/field resolution.
/// The global registries here are for backward compatibility with well-known AOT types.
/// </summary>
public static unsafe class MetadataIntegration
{
    // Global type registry for well-known AOT types (System.String, etc.)
    // These are registered without an assembly context.
    private const int MaxTypeEntries = 512;
    private static TypeRegistryEntry* _typeRegistry;
    private static int _typeCount;

    // Global static field storage (legacy - used for AOT statics)
    private const int MaxStaticFields = 256;
    private static StaticFieldEntry* _staticFields;
    private static int _staticFieldCount;

    // Global static storage block (legacy - for AOT statics)
    private const int StaticStorageBlockSize = 64 * 1024;  // 64KB per block
    private static byte* _staticStorageBase;
    private static int _staticStorageUsed;

    // Field layout cache (shared across assemblies for now)
    private const int MaxFieldLayoutEntries = 512;
    private static FieldLayoutEntry* _fieldLayoutCache;
    private static int _fieldLayoutCount;

    // Default metadata context (for backward compatibility with single-assembly mode)
    private static MetadataRoot* _metadataRoot;
    private static TablesHeader* _tablesHeader;
    private static TableSizes* _tableSizes;

    // Current assembly ID for resolution context
    private static uint _currentAssemblyId;

    private static bool _initialized;

    /// <summary>
    /// Initialize the metadata integration layer.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Allocate type registry
        _typeRegistry = (TypeRegistryEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxTypeEntries * sizeof(TypeRegistryEntry)));
        if (_typeRegistry == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate type registry");
            return;
        }

        // Allocate static field storage registry
        _staticFields = (StaticFieldEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxStaticFields * sizeof(StaticFieldEntry)));
        if (_staticFields == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate static field registry");
            return;
        }

        // Allocate field layout cache
        _fieldLayoutCache = (FieldLayoutEntry*)HeapAllocator.AllocZeroed(
            (ulong)(MaxFieldLayoutEntries * sizeof(FieldLayoutEntry)));
        if (_fieldLayoutCache == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate field layout cache");
            return;
        }

        _typeCount = 0;
        _staticFieldCount = 0;
        _fieldLayoutCount = 0;
        _staticStorageBase = null;
        _staticStorageUsed = 0;
        _initialized = true;

        DebugConsole.WriteLine("[MetaInt] Initialized metadata integration layer");
    }

    /// <summary>
    /// Set the metadata context for resolution.
    /// Must be called after parsing an assembly's metadata.
    /// </summary>
    public static void SetMetadataContext(MetadataRoot* root, TablesHeader* tables, TableSizes* sizes)
    {
        _metadataRoot = root;
        _tablesHeader = tables;
        _tableSizes = sizes;

        DebugConsole.WriteLine("[MetaInt] Metadata context set");
    }

    /// <summary>
    /// Set the current assembly ID for resolution.
    /// Call this before compiling methods from a specific assembly.
    /// </summary>
    public static void SetCurrentAssembly(uint assemblyId)
    {
        _currentAssemblyId = assemblyId;

        // Also update the metadata context from the assembly
        // Note: asm is already a pointer, so we get field addresses directly
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm != null)
        {
            // Get pointers to the assembly's metadata structures
            // These are stable since LoadedAssembly lives in the kernel heap
            _metadataRoot = &asm->Metadata;
            _tablesHeader = &asm->Tables;
            _tableSizes = &asm->Sizes;
        }
    }

    /// <summary>
    /// Get the current assembly ID.
    /// </summary>
    public static uint GetCurrentAssemblyId() => _currentAssemblyId;

    /// <summary>
    /// Well-known type tokens for System.Runtime/mscorlib types.
    /// These use synthetic token values in the 0xF0xxxxxx range.
    /// Real TypeRef tokens from loaded assemblies will need to be mapped to these.
    /// </summary>
    public static class WellKnownTypes
    {
        public const uint Object = 0xF0000001;
        public const uint String = 0xF0000002;
        public const uint Int32 = 0xF0000003;
        public const uint Int64 = 0xF0000004;
        public const uint Boolean = 0xF0000005;
        public const uint Byte = 0xF0000006;
        public const uint Char = 0xF0000007;
        public const uint Double = 0xF0000008;
        public const uint Single = 0xF0000009;
        public const uint Int16 = 0xF000000A;
        public const uint UInt16 = 0xF000000B;
        public const uint UInt32 = 0xF000000C;
        public const uint UInt64 = 0xF000000D;
        public const uint IntPtr = 0xF000000E;
        public const uint UIntPtr = 0xF000000F;
        public const uint SByte = 0xF0000010;
    }

    /// <summary>
    /// Register well-known AOT types from korlib by extracting MethodTables from instances.
    /// Call this after GCHeap is initialized.
    /// </summary>
    public static void RegisterWellKnownTypes()
    {
        if (!_initialized)
            Initialize();

        int count = 0;

        // System.String - extract from empty string literal
        string emptyStr = "";
        if (RegisterType(WellKnownTypes.String, (MethodTable*)emptyStr.m_pMethodTable))
            count++;

        // System.Object - extract from boxed int (simplest way to get Object MT)
        // Note: We can't easily get Object's MT directly, but String's parent is Object
        // For now, just register String which is most commonly needed

        DebugConsole.Write("[MetaInt] Registered ");
        DebugConsole.WriteDecimal((uint)count);
        DebugConsole.WriteLine(" well-known AOT types");
    }

    // ============================================================================
    // Type Registry
    // ============================================================================

    /// <summary>
    /// Register a type token to MethodTable mapping.
    /// Used to map AOT-compiled types from korlib/bflat.
    /// </summary>
    public static bool RegisterType(uint token, MethodTable* mt)
    {
        if (!_initialized)
            Initialize();

        if (mt == null)
            return false;

        // Check if already registered
        for (int i = 0; i < _typeCount; i++)
        {
            if (_typeRegistry[i].Token == token)
            {
                _typeRegistry[i].MT = mt;  // Update existing
                return true;
            }
        }

        // Add new entry
        if (_typeCount >= MaxTypeEntries)
        {
            DebugConsole.WriteLine("[MetaInt] Type registry full");
            return false;
        }

        _typeRegistry[_typeCount].Token = token;
        _typeRegistry[_typeCount].MT = mt;
        _typeCount++;

        return true;
    }

    /// <summary>
    /// Look up a MethodTable by type token.
    /// </summary>
    public static MethodTable* LookupType(uint token)
    {
        for (int i = 0; i < _typeCount; i++)
        {
            if (_typeRegistry[i].Token == token)
                return _typeRegistry[i].MT;
        }
        return null;
    }

    /// <summary>
    /// TypeResolver implementation for ILCompiler.
    /// Resolves type tokens (TypeDef, TypeRef, TypeSpec) to MethodTable pointers.
    /// Uses the current assembly context set via SetCurrentAssembly().
    /// </summary>
    public static bool ResolveType(uint token, out void* methodTablePtr)
    {
        methodTablePtr = null;

        if (!_initialized)
            return false;

        // Extract table type from token
        byte tableId = (byte)(token >> 24);

        // Check global well-known type cache first (0xF0xxxxxx tokens)
        if ((token & 0xFF000000) == 0xF0000000)
        {
            MethodTable* mt = LookupType(token);
            if (mt != null)
            {
                methodTablePtr = mt;
                return true;
            }
            return false;
        }

        // For assembly-specific tokens, use AssemblyLoader's per-assembly registry
        if (_currentAssemblyId != 0)
        {
            MethodTable* mt = AssemblyLoader.ResolveType(_currentAssemblyId, token);
            if (mt != null)
            {
                methodTablePtr = mt;
                return true;
            }
        }

        // Fallback: check global registry (for backward compatibility)
        MethodTable* globalMt = LookupType(token);
        if (globalMt != null)
        {
            methodTablePtr = globalMt;
            return true;
        }

        // Not found - log for debugging
        switch (tableId)
        {
            case 0x02:  // TypeDef
                DebugConsole.Write("[MetaInt] TypeDef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" not in registry");
                break;

            case 0x01:  // TypeRef
                DebugConsole.Write("[MetaInt] TypeRef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" not resolved");
                break;

            case 0x1B:  // TypeSpec
                DebugConsole.Write("[MetaInt] TypeSpec 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" not resolved");
                break;
        }

        return false;
    }

    /// <summary>
    /// TypeResolver with explicit assembly ID (for cross-assembly calls).
    /// </summary>
    public static bool ResolveTypeInAssembly(uint assemblyId, uint token, out void* methodTablePtr)
    {
        methodTablePtr = null;

        if (!_initialized)
            return false;

        // Well-known types don't need assembly context
        if ((token & 0xFF000000) == 0xF0000000)
        {
            MethodTable* mt = LookupType(token);
            if (mt != null)
            {
                methodTablePtr = mt;
                return true;
            }
            return false;
        }

        // Use AssemblyLoader for assembly-specific resolution
        MethodTable* mt2 = AssemblyLoader.ResolveType(assemblyId, token);
        if (mt2 != null)
        {
            methodTablePtr = mt2;
            return true;
        }

        return false;
    }

    // ============================================================================
    // Field Resolution
    // ============================================================================

    /// <summary>
    /// Allocate static field storage from the static storage block.
    /// </summary>
    private static void* AllocateStaticStorage(int size, int alignment = 8)
    {
        if (!_initialized)
            Initialize();

        // Allocate storage block on demand
        if (_staticStorageBase == null)
        {
            _staticStorageBase = (byte*)HeapAllocator.AllocZeroed(StaticStorageBlockSize);
            if (_staticStorageBase == null)
            {
                DebugConsole.WriteLine("[MetaInt] Failed to allocate static storage block");
                return null;
            }
            _staticStorageUsed = 0;
            DebugConsole.Write("[MetaInt] Allocated static storage block at 0x");
            DebugConsole.WriteHex((ulong)_staticStorageBase);
            DebugConsole.WriteLine();
        }

        // Align the offset
        int alignedOffset = (_staticStorageUsed + alignment - 1) & ~(alignment - 1);

        // Check if we have space
        if (alignedOffset + size > StaticStorageBlockSize)
        {
            DebugConsole.WriteLine("[MetaInt] Static storage block exhausted");
            return null;
        }

        void* addr = _staticStorageBase + alignedOffset;
        _staticStorageUsed = alignedOffset + size;

        return addr;
    }

    /// <summary>
    /// Register a static field and allocate storage for it.
    /// </summary>
    public static void* RegisterStaticField(uint fieldToken, uint typeToken, int size, bool isGCRef)
    {
        if (!_initialized)
            Initialize();

        // Check if already registered
        for (int i = 0; i < _staticFieldCount; i++)
        {
            if (_staticFields[i].Token == fieldToken)
                return _staticFields[i].Address;  // Return existing
        }

        // Allocate storage
        void* addr = AllocateStaticStorage(size);
        if (addr == null)
            return null;

        // Register
        if (_staticFieldCount >= MaxStaticFields)
        {
            DebugConsole.WriteLine("[MetaInt] Static field registry full");
            return null;
        }

        _staticFields[_staticFieldCount].Token = fieldToken;
        _staticFields[_staticFieldCount].TypeToken = typeToken;
        _staticFields[_staticFieldCount].Address = addr;
        _staticFields[_staticFieldCount].Size = size;
        _staticFields[_staticFieldCount].IsGCRef = isGCRef;
        _staticFieldCount++;

        DebugConsole.Write("[MetaInt] Registered static field 0x");
        DebugConsole.WriteHex(fieldToken);
        DebugConsole.Write(" at 0x");
        DebugConsole.WriteHex((ulong)addr);
        DebugConsole.Write(" size ");
        DebugConsole.WriteDecimal((uint)size);
        DebugConsole.WriteLine();

        return addr;
    }

    /// <summary>
    /// Look up a static field's storage address.
    /// </summary>
    public static void* LookupStaticField(uint fieldToken)
    {
        for (int i = 0; i < _staticFieldCount; i++)
        {
            if (_staticFields[i].Token == fieldToken)
                return _staticFields[i].Address;
        }
        return null;
    }

    /// <summary>
    /// Cache a field's layout information for faster subsequent lookups.
    /// </summary>
    public static void CacheFieldLayout(uint token, int offset, byte size, bool isSigned,
                                         bool isStatic, bool isGCRef, void* staticAddress)
    {
        if (!_initialized)
            Initialize();

        // Check if already cached
        for (int i = 0; i < _fieldLayoutCount; i++)
        {
            if (_fieldLayoutCache[i].Token == token)
            {
                // Update existing entry
                _fieldLayoutCache[i].Offset = offset;
                _fieldLayoutCache[i].Size = size;
                _fieldLayoutCache[i].IsSigned = isSigned;
                _fieldLayoutCache[i].IsStatic = isStatic;
                _fieldLayoutCache[i].IsGCRef = isGCRef;
                _fieldLayoutCache[i].StaticAddress = staticAddress;
                return;
            }
        }

        // Add new entry
        if (_fieldLayoutCount >= MaxFieldLayoutEntries)
        {
            DebugConsole.WriteLine("[MetaInt] Field layout cache full");
            return;
        }

        _fieldLayoutCache[_fieldLayoutCount].Token = token;
        _fieldLayoutCache[_fieldLayoutCount].Offset = offset;
        _fieldLayoutCache[_fieldLayoutCount].Size = size;
        _fieldLayoutCache[_fieldLayoutCount].IsSigned = isSigned;
        _fieldLayoutCache[_fieldLayoutCount].IsStatic = isStatic;
        _fieldLayoutCache[_fieldLayoutCount].IsGCRef = isGCRef;
        _fieldLayoutCache[_fieldLayoutCount].StaticAddress = staticAddress;
        _fieldLayoutCount++;
    }

    /// <summary>
    /// Look up cached field layout.
    /// </summary>
    public static bool LookupFieldLayout(uint token, out FieldLayoutEntry entry)
    {
        entry = default;

        for (int i = 0; i < _fieldLayoutCount; i++)
        {
            if (_fieldLayoutCache[i].Token == token)
            {
                entry = _fieldLayoutCache[i];
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the size and sign of a field based on its element type.
    /// </summary>
    private static void GetFieldSizeFromElementType(byte elementType, out byte size, out bool isSigned, out bool isGCRef)
    {
        size = 4;  // Default
        isSigned = false;
        isGCRef = false;

        switch (elementType)
        {
            case ElementType.Boolean:
            case ElementType.U1:
                size = 1;
                isSigned = false;
                break;
            case ElementType.I1:
                size = 1;
                isSigned = true;
                break;
            case ElementType.Char:
            case ElementType.U2:
                size = 2;
                isSigned = false;
                break;
            case ElementType.I2:
                size = 2;
                isSigned = true;
                break;
            case ElementType.U4:
                size = 4;
                isSigned = false;
                break;
            case ElementType.I4:
                size = 4;
                isSigned = true;
                break;
            case ElementType.U8:
                size = 8;
                isSigned = false;
                break;
            case ElementType.I8:
                size = 8;
                isSigned = true;
                break;
            case ElementType.R4:
                size = 4;
                isSigned = false;  // Floats don't use sign extension
                break;
            case ElementType.R8:
                size = 8;
                isSigned = false;
                break;
            case ElementType.I:  // IntPtr
            case ElementType.U:  // UIntPtr
            case ElementType.Ptr:
            case ElementType.FnPtr:
                size = 8;  // 64-bit platform
                isSigned = false;
                break;
            case ElementType.String:
            case ElementType.Class:
            case ElementType.Object:
            case ElementType.SzArray:
            case ElementType.Array:
                size = 8;  // Reference types are pointers
                isSigned = false;
                isGCRef = true;
                break;
            case ElementType.ValueType:
                // ValueType size depends on the specific type
                // Caller needs to determine from type definition
                size = 0;  // Unknown - needs type lookup
                break;
            default:
                size = 8;  // Assume pointer size for unknown types
                break;
        }
    }

    /// <summary>
    /// FieldResolver implementation for ILCompiler.
    /// Resolves field tokens to offset/size/address information.
    /// </summary>
    public static bool ResolveField(uint token, out ResolvedField result)
    {
        result = default;

        if (!_initialized)
            return false;

        // Check field layout cache first
        if (LookupFieldLayout(token, out FieldLayoutEntry cached))
        {
            result.Offset = cached.Offset;
            result.Size = cached.Size;
            result.IsSigned = cached.IsSigned;
            result.IsStatic = cached.IsStatic;
            result.IsGCRef = cached.IsGCRef;
            result.StaticAddress = cached.StaticAddress;
            result.IsValid = true;
            return true;
        }

        // Not cached - try to resolve from metadata
        if (_metadataRoot == null || _tablesHeader == null || _tableSizes == null)
            return false;

        // Extract table type from token
        byte tableId = (byte)(token >> 24);
        uint rowId = token & 0x00FFFFFF;

        switch (tableId)
        {
            case 0x04:  // FieldDef
                return ResolveFieldDef(rowId, token, out result);

            case 0x0A:  // MemberRef
                // MemberRef can reference fields in other assemblies
                return ResolveMemberRefField(token, out result);

            default:
                return false;
        }
    }

    /// <summary>
    /// Resolve a FieldDef token to field information.
    /// </summary>
    private static bool ResolveFieldDef(uint rowId, uint token, out ResolvedField result)
    {
        result = default;

        if (rowId == 0 || _metadataRoot == null || _tablesHeader == null || _tableSizes == null)
            return false;

        // Get field attributes
        ushort flags = MetadataReader.GetFieldFlags(ref *_tablesHeader, ref *_tableSizes, rowId);
        bool isStatic = (flags & 0x0010) != 0;  // fdStatic = 0x0010

        // Get field signature to determine type
        uint sigIdx = MetadataReader.GetFieldSignature(ref *_tablesHeader, ref *_tableSizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

        if (sig == null || sigLen < 2)
        {
            DebugConsole.Write("[MetaInt] Invalid field signature for row ");
            DebugConsole.WriteDecimal(rowId);
            DebugConsole.WriteLine();
            return false;
        }

        // Field signature: FIELD (0x06) + Type
        if (sig[0] != 0x06)  // FIELD calling convention
        {
            DebugConsole.WriteLine("[MetaInt] Invalid field signature calling convention");
            return false;
        }

        // Parse the type from signature (simplified - handles basic types)
        byte elementType = sig[1];
        GetFieldSizeFromElementType(elementType, out byte size, out bool isSigned, out bool isGCRef);

        result.IsSigned = isSigned;
        result.IsGCRef = isGCRef;
        result.IsStatic = isStatic;

        if (size == 0)
        {
            // ValueType - need to look up actual size
            // For now, assume 8 bytes
            size = 8;
        }
        result.Size = size;

        if (isStatic)
        {
            // Static field - allocate or look up storage from per-assembly storage
            void* addr = null;

            // Try per-assembly storage first if we have an assembly context
            if (_currentAssemblyId != 0)
            {
                var asm = AssemblyLoader.GetAssembly(_currentAssemblyId);
                if (asm != null)
                {
                    addr = asm->Statics.Lookup(token);
                    if (addr == null)
                    {
                        // Allocate in per-assembly storage
                        uint typeToken = FindContainingType(rowId);
                        addr = asm->Statics.Register(token, typeToken, size, isGCRef);
                    }
                }
            }

            // Fallback to global storage (legacy/AOT compatibility)
            if (addr == null)
            {
                addr = LookupStaticField(token);
                if (addr == null)
                {
                    uint typeToken = FindContainingType(rowId);
                    addr = RegisterStaticField(token, typeToken, size, isGCRef);
                }
            }

            if (addr == null)
                return false;

            result.StaticAddress = addr;
            result.Offset = 0;  // Not used for statics
        }
        else
        {
            // Instance field - calculate offset
            // Need to determine the field's offset within the object

            // Check FieldLayout table for explicit offset
            uint explicitOffset;
            if (HasExplicitFieldOffset(rowId, out explicitOffset))
            {
                result.Offset = (int)explicitOffset + 8;  // +8 for MethodTable pointer
            }
            else
            {
                // Auto layout - calculate offset based on field order
                // This requires knowing all fields in the type and their sizes
                int offset = CalculateFieldOffset(rowId);
                result.Offset = offset;
            }

            result.StaticAddress = null;
        }

        result.IsValid = true;

        // Cache the result
        CacheFieldLayout(token, result.Offset, result.Size, result.IsSigned,
                        result.IsStatic, result.IsGCRef, result.StaticAddress);

        return true;
    }

    /// <summary>
    /// Resolve a MemberRef field token to field information.
    /// This handles cross-assembly field references.
    /// </summary>
    private static bool ResolveMemberRefField(uint token, out ResolvedField result)
    {
        result = default;

        // Use AssemblyLoader to resolve the MemberRef to a FieldDef in another assembly
        if (!AssemblyLoader.ResolveMemberRefField(_currentAssemblyId, token,
                                                   out uint fieldToken, out uint targetAsmId))
        {
            DebugConsole.Write("[MetaInt] Failed to resolve MemberRef field 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // Now resolve the FieldDef in the target assembly context
        // Save current context
        uint savedAsmId = _currentAssemblyId;
        MetadataRoot* savedMdRoot = _metadataRoot;
        TablesHeader* savedTables = _tablesHeader;
        TableSizes* savedSizes = _tableSizes;

        // Switch to target assembly context
        SetCurrentAssembly(targetAsmId);

        // Resolve the field in the target assembly
        uint fieldRowId = fieldToken & 0x00FFFFFF;
        bool success = ResolveFieldDef(fieldRowId, fieldToken, out result);

        // Restore original context
        _currentAssemblyId = savedAsmId;
        _metadataRoot = savedMdRoot;
        _tablesHeader = savedTables;
        _tableSizes = savedSizes;

        return success;
    }

    /// <summary>
    /// Check if a field has an explicit layout offset in the FieldLayout table.
    /// </summary>
    private static bool HasExplicitFieldOffset(uint fieldRow, out uint offset)
    {
        offset = 0;

        if (_tablesHeader == null || _tableSizes == null)
            return false;

        uint layoutRowCount = _tablesHeader->RowCounts[(int)MetadataTableId.FieldLayout];

        for (uint i = 1; i <= layoutRowCount; i++)
        {
            uint layoutFieldRow = MetadataReader.GetFieldLayoutField(ref *_tablesHeader, ref *_tableSizes, i);
            if (layoutFieldRow == fieldRow)
            {
                offset = MetadataReader.GetFieldLayoutOffset(ref *_tablesHeader, ref *_tableSizes, i);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Find the TypeDef that contains a given field row.
    /// </summary>
    private static uint FindContainingType(uint fieldRow)
    {
        if (_tablesHeader == null || _tableSizes == null)
            return 0;

        uint typeDefCount = _tablesHeader->RowCounts[(int)MetadataTableId.TypeDef];

        for (uint i = 1; i <= typeDefCount; i++)
        {
            uint fieldList = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, i);
            uint nextFieldList;

            if (i < typeDefCount)
                nextFieldList = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, i + 1);
            else
                nextFieldList = _tablesHeader->RowCounts[(int)MetadataTableId.Field] + 1;

            if (fieldRow >= fieldList && fieldRow < nextFieldList)
            {
                // Found the containing type - return its token
                return 0x02000000 | i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Calculate field offset for auto-layout fields.
    /// Simple sequential layout: fields are placed in order with natural alignment.
    /// </summary>
    private static int CalculateFieldOffset(uint fieldRow)
    {
        // Find the containing type
        uint typeToken = FindContainingType(fieldRow);
        if (typeToken == 0)
            return 8;  // Default: right after MethodTable*

        uint typeRow = typeToken & 0x00FFFFFF;

        // Get the type's field list
        uint firstField = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow);

        // Calculate offset by summing sizes of preceding fields
        // Start after MethodTable pointer (8 bytes)
        int offset = 8;

        for (uint f = firstField; f < fieldRow; f++)
        {
            // Get field flags to check if static (static fields don't contribute to offset)
            ushort flags = MetadataReader.GetFieldFlags(ref *_tablesHeader, ref *_tableSizes, f);
            if ((flags & 0x0010) != 0)  // fdStatic
                continue;

            // Get field size from signature
            uint sigIdx = MetadataReader.GetFieldSignature(ref *_tablesHeader, ref *_tableSizes, f);
            byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

            if (sig != null && sigLen >= 2 && sig[0] == 0x06)
            {
                byte elementType = sig[1];
                GetFieldSizeFromElementType(elementType, out byte size, out _, out _);

                if (size == 0)
                    size = 8;  // Default for unknown types

                // Align offset
                int alignment = size < 8 ? size : 8;
                offset = (offset + alignment - 1) & ~(alignment - 1);
                offset += size;
            }
        }

        // Align the final offset for this field
        uint targetSigIdx = MetadataReader.GetFieldSignature(ref *_tablesHeader, ref *_tableSizes, fieldRow);
        byte* targetSig = MetadataReader.GetBlob(ref *_metadataRoot, targetSigIdx, out uint targetSigLen);

        if (targetSig != null && targetSigLen >= 2 && targetSig[0] == 0x06)
        {
            GetFieldSizeFromElementType(targetSig[1], out byte targetSize, out _, out _);
            if (targetSize == 0)
                targetSize = 8;
            int alignment = targetSize < 8 ? targetSize : 8;
            offset = (offset + alignment - 1) & ~(alignment - 1);
        }

        return offset;
    }

    // ============================================================================
    // Resolver Accessors for ILCompiler
    // ============================================================================

    /// <summary>
    /// Get a TypeResolver delegate for use with ILCompiler.SetTypeResolver().
    /// Note: Delegates may not be fully supported in minimal korlib.
    /// </summary>
    public static TypeResolver GetTypeResolverDelegate()
    {
        return ResolveType;
    }

    /// <summary>
    /// Get a FieldResolver delegate for use with ILCompiler.SetFieldResolver().
    /// Note: Delegates may not be fully supported in minimal korlib.
    /// </summary>
    public static FieldResolver GetFieldResolverDelegate()
    {
        return ResolveField;
    }

    /// <summary>
    /// Wire up an ILCompiler instance with resolvers from MetadataIntegration.
    /// </summary>
    public static void WireCompiler(ref ILCompiler compiler)
    {
        compiler.SetTypeResolver(ResolveType);
        compiler.SetFieldResolver(ResolveField);
    }

    // ============================================================================
    // Statistics and Debugging
    // ============================================================================

    /// <summary>
    /// Get statistics about the integration layer.
    /// </summary>
    public static void PrintStatistics()
    {
        DebugConsole.WriteLine("[MetaInt] Statistics:");
        DebugConsole.Write("  Types registered: ");
        DebugConsole.WriteDecimal((uint)_typeCount);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(MaxTypeEntries);
        DebugConsole.WriteLine();

        DebugConsole.Write("  Static fields: ");
        DebugConsole.WriteDecimal((uint)_staticFieldCount);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(MaxStaticFields);
        DebugConsole.WriteLine();

        DebugConsole.Write("  Static storage used: ");
        DebugConsole.WriteDecimal((uint)_staticStorageUsed);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(StaticStorageBlockSize);
        DebugConsole.WriteLine();

        DebugConsole.Write("  Field layouts cached: ");
        DebugConsole.WriteDecimal((uint)_fieldLayoutCount);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(MaxFieldLayoutEntries);
        DebugConsole.WriteLine();
    }
}
