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

    /// <summary>True if the declaring type is a value type.</summary>
    public bool IsDeclaringTypeValueType;

    /// <summary>Size of the declaring type in bytes (for value types).</summary>
    public int DeclaringTypeSize;

    /// <summary>True if the field type itself is a value type.</summary>
    public bool IsFieldTypeValueType;

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

        // System.Object - use the parent type pointer from String's MT
        // String inherits from Object, so String's parent is Object
        MethodTable* stringMT = (MethodTable*)emptyStr.m_pMethodTable;
        if (stringMT != null)
        {
            MethodTable* objectMT = stringMT->GetParentType();
            if (objectMT != null)
            {
                if (RegisterType(WellKnownTypes.Object, objectMT))
                    count++;
            }
        }

        // Primitive types: extract MethodTable from array element types
        // Arrays are reference types, so we can create them without boxing.
        // The array's _relatedType field points to the element type's MethodTable.
        count += RegisterPrimitiveTypesFromArrays();

        DebugConsole.Write("[MetaInt] Registered ");
        DebugConsole.WriteDecimal((uint)count);
        DebugConsole.WriteLine(" well-known AOT types");
    }

    /// <summary>
    /// Register primitive types by extracting their MethodTables from array element types.
    /// Arrays are reference types, so we can create them and extract element type MTs.
    /// </summary>
    private static int RegisterPrimitiveTypesFromArrays()
    {
        int count = 0;

        // Create a single byte[] array and use runtime helpers to get primitive type MTs
        // The JIT already has hardcoded handling for primitive array types
        // We can use the array's element type field

        // Use RuntimeHelpers to allocate arrays and extract element type MTs
        // Int32 - we need to get this from the runtime's knowledge of int[]
        // Since arrays are already working, the element type MTs must exist

        // Alternative approach: Check if the JIT already handles these types
        // and register them if we can find them through the type system

        // For now, we'll register what we can safely access:
        // The primitive types are already working in array contexts because
        // the JIT has hardcoded element size handling. The TypeRef resolution
        // warnings are for generic instantiation which needs additional work.

        // Register primitive types through the GCHeap array allocation path
        // which already knows about these types
        count += RegisterPrimitiveViaArrayAllocation();

        return count;
    }

    // Static storage for synthetic primitive MethodTables
    // These are used for newarr to get element sizes
    private static MethodTable* _primitiveMethodTables;
    private const int NumPrimitiveTypes = 16;

    /// <summary>
    /// Register primitives by creating synthetic MethodTables with correct element sizes.
    /// These are used by newarr to determine array element sizes.
    /// </summary>
    private static int RegisterPrimitiveViaArrayAllocation()
    {
        // Allocate storage for primitive MethodTables
        _primitiveMethodTables = (MethodTable*)HeapAllocator.AllocZeroed(
            (ulong)(NumPrimitiveTypes * sizeof(MethodTable)));

        if (_primitiveMethodTables == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate primitive MethodTables");
            return 0;
        }

        int count = 0;

        // Create synthetic MethodTables for each primitive type
        // The key field is _usComponentSize which RhpNewArray uses for element size

        // Int32 - 4 bytes
        _primitiveMethodTables[0]._usComponentSize = 4;
        _primitiveMethodTables[0]._uBaseSize = 12;  // MT* (8) + value (4)
        if (RegisterType(WellKnownTypes.Int32, &_primitiveMethodTables[0]))
            count++;

        // Int64 - 8 bytes
        _primitiveMethodTables[1]._usComponentSize = 8;
        _primitiveMethodTables[1]._uBaseSize = 16;  // MT* (8) + value (8)
        if (RegisterType(WellKnownTypes.Int64, &_primitiveMethodTables[1]))
            count++;

        // Boolean - 1 byte
        _primitiveMethodTables[2]._usComponentSize = 1;
        _primitiveMethodTables[2]._uBaseSize = 9;
        if (RegisterType(WellKnownTypes.Boolean, &_primitiveMethodTables[2]))
            count++;

        // Byte - 1 byte
        _primitiveMethodTables[3]._usComponentSize = 1;
        _primitiveMethodTables[3]._uBaseSize = 9;
        if (RegisterType(WellKnownTypes.Byte, &_primitiveMethodTables[3]))
            count++;

        // Char - 2 bytes
        _primitiveMethodTables[4]._usComponentSize = 2;
        _primitiveMethodTables[4]._uBaseSize = 10;
        if (RegisterType(WellKnownTypes.Char, &_primitiveMethodTables[4]))
            count++;

        // Double - 8 bytes
        _primitiveMethodTables[5]._usComponentSize = 8;
        _primitiveMethodTables[5]._uBaseSize = 16;
        if (RegisterType(WellKnownTypes.Double, &_primitiveMethodTables[5]))
            count++;

        // Single - 4 bytes
        _primitiveMethodTables[6]._usComponentSize = 4;
        _primitiveMethodTables[6]._uBaseSize = 12;
        if (RegisterType(WellKnownTypes.Single, &_primitiveMethodTables[6]))
            count++;

        // Int16 - 2 bytes
        _primitiveMethodTables[7]._usComponentSize = 2;
        _primitiveMethodTables[7]._uBaseSize = 10;
        if (RegisterType(WellKnownTypes.Int16, &_primitiveMethodTables[7]))
            count++;

        // UInt16 - 2 bytes
        _primitiveMethodTables[8]._usComponentSize = 2;
        _primitiveMethodTables[8]._uBaseSize = 10;
        if (RegisterType(WellKnownTypes.UInt16, &_primitiveMethodTables[8]))
            count++;

        // UInt32 - 4 bytes
        _primitiveMethodTables[9]._usComponentSize = 4;
        _primitiveMethodTables[9]._uBaseSize = 12;
        if (RegisterType(WellKnownTypes.UInt32, &_primitiveMethodTables[9]))
            count++;

        // UInt64 - 8 bytes
        _primitiveMethodTables[10]._usComponentSize = 8;
        _primitiveMethodTables[10]._uBaseSize = 16;
        if (RegisterType(WellKnownTypes.UInt64, &_primitiveMethodTables[10]))
            count++;

        // IntPtr - 8 bytes (64-bit)
        _primitiveMethodTables[11]._usComponentSize = 8;
        _primitiveMethodTables[11]._uBaseSize = 16;
        if (RegisterType(WellKnownTypes.IntPtr, &_primitiveMethodTables[11]))
            count++;

        // UIntPtr - 8 bytes (64-bit)
        _primitiveMethodTables[12]._usComponentSize = 8;
        _primitiveMethodTables[12]._uBaseSize = 16;
        if (RegisterType(WellKnownTypes.UIntPtr, &_primitiveMethodTables[12]))
            count++;

        // SByte - 1 byte
        _primitiveMethodTables[13]._usComponentSize = 1;
        _primitiveMethodTables[13]._uBaseSize = 9;
        if (RegisterType(WellKnownTypes.SByte, &_primitiveMethodTables[13]))
            count++;

        return count;
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
                DebugConsole.Write(" not in registry (asm ");
                DebugConsole.WriteDecimal(_currentAssemblyId);
                DebugConsole.WriteLine(")");
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
    /// Resolve an element type token to an ARRAY MethodTable for newarr.
    /// This takes the element type token, resolves it to the element MT,
    /// then creates/returns an array MT with proper ComponentSize.
    /// </summary>
    public static bool ResolveArrayElementType(uint token, out void* arrayMethodTablePtr)
    {
        arrayMethodTablePtr = null;

        // First resolve the element type to get its MethodTable
        void* elementMTPtr;
        if (!ResolveType(token, out elementMTPtr) || elementMTPtr == null)
        {
            DebugConsole.Write("[MetaInt] Failed to resolve array element type 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // Get or create an array MethodTable for this element type
        MethodTable* elementMT = (MethodTable*)elementMTPtr;
        MethodTable* arrayMT = AssemblyLoader.GetOrCreateArrayMethodTable(elementMT);

        if (arrayMT == null)
        {
            DebugConsole.Write("[MetaInt] Failed to create array MT for element type 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        arrayMethodTablePtr = arrayMT;
        return true;
    }

    /// <summary>
    /// Get the size of a type from its token.
    /// For value types (structs), returns the size of the struct.
    /// For reference types, returns 8 (pointer size).
    /// Used by ldelema to compute array element addresses.
    /// </summary>
    public static uint GetTypeSize(uint token)
    {
        // Try to resolve the type to get its MethodTable
        void* mtPtr;
        if (!ResolveType(token, out mtPtr) || mtPtr == null)
        {
            // If we can't resolve, fall back to pointer size
            DebugConsole.Write("[MetaInt] GetTypeSize: failed to resolve token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(", defaulting to 8");
            return 8;
        }

        // Get BaseSize from MethodTable
        MethodTable* mt = (MethodTable*)mtPtr;

        // For value types, ComponentSize = 0 and we use the type's calculated size
        // For arrays, ComponentSize holds the element size
        // BaseSize includes MT pointer overhead (8 bytes), so subtract it for value types
        uint baseSize = mt->_uBaseSize;

        // If it's a value type (non-array, non-object), return the actual struct size
        // Value type MethodTables typically have BaseSize = struct size + 8 (for boxing purposes)
        // But for ldelema, we want the raw struct size
        if (mt->_usComponentSize == 0 && baseSize > 8)
        {
            // This is a value type - subtract the MT pointer overhead
            return baseSize - 8;
        }

        // For reference types, return pointer size
        return 8;
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
                                         bool isStatic, bool isGCRef, void* staticAddress,
                                         bool isDeclaringTypeValueType = false, int declaringTypeSize = 0,
                                         bool isFieldTypeValueType = false)
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
                _fieldLayoutCache[i].IsDeclaringTypeValueType = isDeclaringTypeValueType;
                _fieldLayoutCache[i].DeclaringTypeSize = declaringTypeSize;
                _fieldLayoutCache[i].IsFieldTypeValueType = isFieldTypeValueType;
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
        _fieldLayoutCache[_fieldLayoutCount].IsDeclaringTypeValueType = isDeclaringTypeValueType;
        _fieldLayoutCache[_fieldLayoutCount].DeclaringTypeSize = declaringTypeSize;
        _fieldLayoutCache[_fieldLayoutCount].IsFieldTypeValueType = isFieldTypeValueType;
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
            result.IsDeclaringTypeValueType = cached.IsDeclaringTypeValueType;
            result.DeclaringTypeSize = cached.DeclaringTypeSize;
            result.IsFieldTypeValueType = cached.IsFieldTypeValueType;
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

        // Check if the field type itself is a value type
        // ElementType.ValueType = 0x11, plus primitives (0x02-0x0D) are also value types
        bool fieldIsValueType = (elementType == 0x11) ||
                                 (elementType >= 0x02 && elementType <= 0x0D);
        result.IsFieldTypeValueType = fieldIsValueType;

        if (size == 0)
        {
            // ValueType - need to look up actual size from metadata
            if (elementType == 0x11 && sigLen >= 3)
            {
                // Parse the TypeDefOrRef token to get the actual size
                uint fieldTypeSize = AssemblyLoader.GetFieldTypeSizeForAssembly(_currentAssemblyId, sig + 1, sigLen - 1);
                size = fieldTypeSize > 0 && fieldTypeSize <= 255 ? (byte)fieldTypeSize : (byte)8;
            }
            else
            {
                // Fallback: assume 8 bytes
                size = 8;
            }
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

            // Check if this field belongs to a value type
            // Value types accessed via byref don't have an MT pointer, so offsets start at 0
            uint typeToken = FindContainingType(rowId);
            uint typeRow = typeToken & 0x00FFFFFF;
            bool isValueType = (typeRow > 0) && IsTypeDefValueType(typeRow);

            // Check FieldLayout table for explicit offset
            uint explicitOffset;
            if (HasExplicitFieldOffset(rowId, out explicitOffset))
            {
                // For value types, use explicit offset directly (no MT pointer)
                // For reference types, add 8 for the MT pointer
                result.Offset = (int)explicitOffset + (isValueType ? 0 : 8);
            }
            else
            {
                // Auto layout - calculate offset based on field order
                // This requires knowing all fields in the type and their sizes
                int offset = CalculateFieldOffset(rowId);
                result.Offset = offset;
            }

            result.StaticAddress = null;

            // Set value type info for the declaring type
            result.IsDeclaringTypeValueType = isValueType;
            if (isValueType && typeRow > 0)
            {
                result.DeclaringTypeSize = (int)CalculateTypeDefSize(typeRow);
            }
        }

        result.IsValid = true;

        // Cache the result
        CacheFieldLayout(token, result.Offset, result.Size, result.IsSigned,
                        result.IsStatic, result.IsGCRef, result.StaticAddress,
                        result.IsDeclaringTypeValueType, result.DeclaringTypeSize,
                        result.IsFieldTypeValueType);

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
    /// Try to resolve a MemberRef token via the AOT method registry.
    /// This is used for well-known types like String that are AOT-compiled into the kernel.
    /// </summary>
    private static bool TryResolveAotMemberRef(uint token, out ResolvedMethod result)
    {
        result = default;

        if (_metadataRoot == null || _tablesHeader == null || _tableSizes == null)
        {
            DebugConsole.Write("[AotMemberRef] No metadata for token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, rowId);

        // Get member name and signature
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, rowId);
        uint sigIdx = MetadataReader.GetMemberRefSignature(ref *_tablesHeader, ref *_tableSizes, rowId);

        byte* memberName = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

        if (memberName == null || sig == null || sigLen == 0)
            return false;

        // Check if this is a method signature (NOT 0x06 which is FIELD)
        if (sig[0] == 0x06)
            return false;  // It's a field, not a method

        // Get the type name from the TypeRef
        byte* typeName = null;
        if (classRef.Table == MetadataTableId.TypeRef)
        {
            uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
            uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

            // Build full type name (namespace.name)
            byte* ns = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);

            // Combine into full name
            typeName = BuildFullTypeName(ns, name);
        }
        else
        {
            DebugConsole.Write("[AotMemberRef] Token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" classRef.Table=");
            DebugConsole.WriteDecimal((int)classRef.Table);
            DebugConsole.WriteLine(" (not TypeRef)");
        }

        if (typeName == null)
            return false;

        // Check if this is a well-known AOT type
        if (!AotMethodRegistry.IsWellKnownAotType(typeName))
        {
            // Debug: show the type name that wasn't recognized
            DebugConsole.Write("[AotMemberRef] Token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" type '");
            WriteByteString(typeName);
            DebugConsole.WriteLine("' is not a well-known AOT type");
            return false;
        }

        // Parse the signature to get parameter count
        int sigPos = 0;
        byte callConv = sig[sigPos++];
        // Skip the calling convention, just need the parameter count
        _ = callConv; // HasThis flag at (callConv & 0x20) - not needed for lookup

        // Decode compressed parameter count
        byte paramCount = 0;
        byte b = sig[sigPos++];
        if ((b & 0x80) == 0)
            paramCount = b;
        else if ((b & 0xC0) == 0x80)
            paramCount = (byte)(((b & 0x3F) << 8) | sig[sigPos++]);

        // Try to look up in the AOT registry (pass paramCount, not including 'this')
        if (AotMethodRegistry.TryLookup(typeName, memberName, paramCount, out AotMethodEntry entry))
        {
            DebugConsole.Write("[AotMemberRef] Found AOT method: ");
            WriteByteString(typeName);
            DebugConsole.Write(".");
            WriteByteString(memberName);
            DebugConsole.Write(" -> 0x");
            DebugConsole.WriteHex((ulong)entry.NativeCode);
            DebugConsole.WriteLine();

            result.NativeCode = (void*)entry.NativeCode;
            result.ArgCount = entry.ArgCount;
            result.ReturnKind = entry.ReturnKind;
            result.HasThis = entry.HasThis;
            result.IsValid = true;
            result.IsVirtual = entry.IsVirtual;
            result.VtableSlot = 0;
            result.MethodTable = null;
            result.IsInterfaceMethod = false;
            result.InterfaceMT = null;
            result.InterfaceMethodSlot = 0;
            result.RegistryEntry = null;
            return true;
        }

        // Debug: TryLookup failed
        DebugConsole.Write("[AotMemberRef] AOT lookup failed for ");
        WriteByteString(typeName);
        DebugConsole.Write(".");
        WriteByteString(memberName);
        DebugConsole.Write(" paramCount=");
        DebugConsole.WriteDecimal(paramCount);
        DebugConsole.WriteLine();

        return false;
    }

    // Temp buffer for building full type names
    private static byte* _typeNameBuffer;
    private const int TypeNameBufferSize = 256;

    /// <summary>
    /// Write a null-terminated byte string to debug console.
    /// </summary>
    private static void WriteByteString(byte* s)
    {
        if (s == null) return;
        while (*s != 0)
        {
            DebugConsole.WriteByte(*s);
            s++;
        }
    }

    /// <summary>
    /// Build a full type name from namespace and name.
    /// </summary>
    private static byte* BuildFullTypeName(byte* ns, byte* name)
    {
        if (_typeNameBuffer == null)
        {
            _typeNameBuffer = (byte*)HeapAllocator.AllocZeroed(TypeNameBufferSize);
            if (_typeNameBuffer == null)
                return null;
        }

        int pos = 0;

        // Copy namespace if present
        if (ns != null && *ns != 0)
        {
            while (*ns != 0 && pos < TypeNameBufferSize - 2)
            {
                _typeNameBuffer[pos++] = *ns++;
            }
            _typeNameBuffer[pos++] = (byte)'.';
        }

        // Copy type name
        if (name != null)
        {
            while (*name != 0 && pos < TypeNameBufferSize - 1)
            {
                _typeNameBuffer[pos++] = *name++;
            }
        }

        _typeNameBuffer[pos] = 0;
        return _typeNameBuffer;
    }

    /// <summary>
    /// Resolve a MemberRef method token to method call information.
    /// This handles cross-assembly method references.
    /// </summary>
    private static bool ResolveMemberRefMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        // First, try to resolve via the AOT method registry for well-known types like String
        if (TryResolveAotMemberRef(token, out result))
        {
            return true;
        }

        // Fall back to JIT assembly resolution
        if (!AssemblyLoader.ResolveMemberRefMethod(_currentAssemblyId, token,
                                                    out uint methodToken, out uint targetAsmId))
        {
            DebugConsole.Write("[MetaInt] Failed to resolve MemberRef method 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // Save current context
        uint savedAsmId = _currentAssemblyId;
        MetadataRoot* savedMdRoot = _metadataRoot;
        TablesHeader* savedTables = _tablesHeader;
        TableSizes* savedSizes = _tableSizes;

        // Switch to target assembly context
        SetCurrentAssembly(targetAsmId);

        // Check if the method is already compiled in the registry
        uint methodRowId = methodToken & 0x00FFFFFF;
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, targetAsmId);

        bool success = false;

        if (info != null && info->IsCompiled)
        {
            // Method already compiled
            result.NativeCode = info->NativeCode;
            result.ArgCount = info->ArgCount;
            result.ReturnKind = info->ReturnKind;
            result.HasThis = info->HasThis;
            result.IsValid = true;
            result.IsVirtual = info->IsVirtual;
            result.VtableSlot = info->VtableSlot;
            result.MethodTable = info->MethodTable;
            result.IsInterfaceMethod = info->IsInterfaceMethod;
            result.InterfaceMT = info->InterfaceMT;
            result.InterfaceMethodSlot = info->InterfaceMethodSlot;
            result.RegistryEntry = info;
            success = true;
        }
        else
        {
            // Method not compiled - need to JIT it
            // First, get method signature info from metadata
            if (_tablesHeader != null && _tableSizes != null && _metadataRoot != null)
            {
                uint sigIdx = MetadataReader.GetMethodDefSignature(ref *_tablesHeader, ref *_tableSizes, methodRowId);
                byte* sigBlob = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

                if (sigBlob != null && sigLen > 0)
                {
                    // Parse the signature to get parameter count and return type
                    int sigPos = 0;
                    byte callConv = sigBlob[sigPos++];
                    bool hasThis = (callConv & 0x20) != 0;

                    // Decode compressed unsigned integer (parameter count)
                    uint paramCount = 0;
                    byte b = sigBlob[sigPos++];
                    if ((b & 0x80) == 0)
                        paramCount = b;
                    else if ((b & 0xC0) == 0x80)
                        paramCount = (uint)(((b & 0x3F) << 8) | sigBlob[sigPos++]);
                    else if ((b & 0xE0) == 0xC0)
                    {
                        paramCount = (uint)(((b & 0x1F) << 24) | (sigBlob[sigPos] << 16) | (sigBlob[sigPos + 1] << 8) | sigBlob[sigPos + 2]);
                        sigPos += 3;
                    }

                    byte retType = sigBlob[sigPos];

                    // JIT compile the method
                    DebugConsole.Write("[MetaInt] Before JIT asm ");
                    DebugConsole.WriteDecimal(targetAsmId);
                    DebugConsole.Write(" ctx=");
                    DebugConsole.WriteDecimal(_currentAssemblyId);
                    DebugConsole.WriteLine();

                    JitResult jitResult = Tier0JIT.CompileMethod(targetAsmId, methodToken);

                    DebugConsole.Write("[MetaInt] After JIT asm ");
                    DebugConsole.WriteDecimal(targetAsmId);
                    DebugConsole.Write(" ctx=");
                    DebugConsole.WriteDecimal(_currentAssemblyId);
                    DebugConsole.WriteLine();

                    if (jitResult.Success)
                    {
                        // Method was successfully compiled, try to get from registry again
                        info = CompiledMethodRegistry.Lookup(methodToken, targetAsmId);
                        if (info != null)
                        {
                            result.NativeCode = info->NativeCode;
                            result.ArgCount = info->ArgCount;
                            result.ReturnKind = info->ReturnKind;
                            result.HasThis = info->HasThis;
                            result.IsValid = true;
                            result.IsVirtual = info->IsVirtual;
                            result.VtableSlot = info->VtableSlot;
                            result.MethodTable = info->MethodTable;
                            result.RegistryEntry = info;
                            success = true;
                        }
                        else
                        {
                            // Fallback: use the code address directly
                            result.NativeCode = jitResult.CodeAddress;
                            result.ArgCount = (byte)paramCount;
                            result.ReturnKind = (retType == 0x01) ? ReturnKind.Void : ReturnKind.IntPtr;
                            result.HasThis = hasThis;
                            result.IsValid = true;
                            success = true;
                        }
                    }
                }
            }
        }

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
    /// Check if a TypeDef row represents a value type (extends System.ValueType or System.Enum).
    /// </summary>
    private static bool IsTypeDefValueType(uint typeDefRow)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the type's Extends field
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref *_tablesHeader, ref *_tableSizes, typeDefRow);

        // TypeDefOrRef: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (extendsIdx.Table != MetadataTableId.TypeRef)
            return false;

        // Get the TypeRef name and namespace
        uint nameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, extendsIdx.RowId);
        uint nsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, extendsIdx.RowId);

        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        byte* ns = MetadataReader.GetString(ref *_metadataRoot, nsIdx);

        if (ns == null || name == null)
            return false;

        // Check for "System" namespace
        if (ns[0] != 'S' || ns[1] != 'y' || ns[2] != 's' || ns[3] != 't' ||
            ns[4] != 'e' || ns[5] != 'm' || ns[6] != 0)
            return false;

        // Check for "ValueType" or "Enum"
        if (name[0] == 'V' && name[1] == 'a' && name[2] == 'l' && name[3] == 'u' &&
            name[4] == 'e' && name[5] == 'T' && name[6] == 'y' && name[7] == 'p' &&
            name[8] == 'e' && name[9] == 0)
            return true;

        if (name[0] == 'E' && name[1] == 'n' && name[2] == 'u' && name[3] == 'm' && name[4] == 0)
            return true;

        return false;
    }

    /// <summary>
    /// Calculate the size of a value type (struct) by summing field sizes.
    /// </summary>
    private static uint CalculateTypeDefSize(uint typeRow)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return 8;  // Default fallback

        // Get the type's field list range
        uint firstField = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow);
        uint typeDefCount = _tablesHeader->RowCounts[(int)MetadataTableId.TypeDef];
        uint nextFieldList;
        if (typeRow < typeDefCount)
            nextFieldList = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow + 1);
        else
            nextFieldList = _tablesHeader->RowCounts[(int)MetadataTableId.Field] + 1;

        uint totalSize = 0;
        uint maxAlignment = 1;

        for (uint f = firstField; f < nextFieldList; f++)
        {
            // Skip static fields
            ushort flags = MetadataReader.GetFieldFlags(ref *_tablesHeader, ref *_tableSizes, f);
            if ((flags & 0x0010) != 0)  // fdStatic
                continue;

            // Get field size from signature
            uint sigIdx = MetadataReader.GetFieldSignature(ref *_tablesHeader, ref *_tableSizes, f);
            byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

            if (sig != null && sigLen >= 2 && sig[0] == 0x06)
            {
                byte elementType = sig[1];
                uint fieldSize;

                if (elementType == ElementType.ValueType || elementType == ElementType.GenericInst)
                {
                    fieldSize = AssemblyLoader.GetFieldTypeSizeForAssembly(_currentAssemblyId, sig + 1, sigLen - 1);
                }
                else
                {
                    GetFieldSizeFromElementType(elementType, out byte primSize, out _, out _);
                    fieldSize = primSize > 0 ? primSize : (uint)8;
                }

                // Track alignment
                uint align = fieldSize < 8 ? fieldSize : 8;
                if (align > maxAlignment)
                    maxAlignment = align;

                // Align and add
                totalSize = (totalSize + align - 1) & ~(align - 1);
                totalSize += fieldSize;
            }
        }

        // Final alignment
        totalSize = (totalSize + maxAlignment - 1) & ~(maxAlignment - 1);

        return totalSize > 0 ? totalSize : 1;  // Minimum 1 byte
    }

    /// <summary>
    /// Get the size of the base class for field offset calculation.
    /// Returns 8 for types that directly extend Object/ValueType.
    /// </summary>
    private static uint GetBaseClassSizeForOffset(uint typeRow)
    {
        // Get the TypeDef's extends CodedIndex
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref *_tablesHeader, ref *_tableSizes, typeRow);
        if (extendsIdx.RowId == 0)
            return 8;

        // Check if this is a well-known base type (Object, ValueType, Enum)
        if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            uint nameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, extendsIdx.RowId);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);

            if (name != null)
            {
                // Check for "Object", "ValueType", or "Enum"
                if ((name[0] == 'O' && name[1] == 'b' && name[2] == 'j' && name[3] == 'e' &&
                     name[4] == 'c' && name[5] == 't' && name[6] == 0) ||
                    (name[0] == 'V' && name[1] == 'a' && name[2] == 'l' && name[3] == 'u' &&
                     name[4] == 'e' && name[5] == 'T' && name[6] == 'y' && name[7] == 'p' &&
                     name[8] == 'e' && name[9] == 0) ||
                    (name[0] == 'E' && name[1] == 'n' && name[2] == 'u' && name[3] == 'm' && name[4] == 0))
                {
                    return 8;  // Just the MT pointer
                }
            }
        }
        else if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // Base class is in the same assembly - check if it's Object
            uint nameIdx = MetadataReader.GetTypeDefName(ref *_tablesHeader, ref *_tableSizes, extendsIdx.RowId);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);

            if (name != null && name[0] == 'O' && name[1] == 'b' && name[2] == 'j' && name[3] == 'e' &&
                name[4] == 'c' && name[5] == 't' && name[6] == 0)
            {
                return 8;
            }
        }

        // Resolve the base class to get its MethodTable and size
        uint baseTypeToken;
        if (extendsIdx.Table == MetadataTableId.TypeDef)
        {
            baseTypeToken = 0x02000000 | extendsIdx.RowId;
        }
        else if (extendsIdx.Table == MetadataTableId.TypeRef)
        {
            baseTypeToken = 0x01000000 | extendsIdx.RowId;
        }
        else
        {
            return 8;
        }

        void* baseMT;
        if (ResolveType(baseTypeToken, out baseMT) && baseMT != null)
        {
            MethodTable* mt = (MethodTable*)baseMT;
            uint baseSize = mt->_uBaseSize;
            DebugConsole.Write(" base=");
            DebugConsole.WriteDecimal(baseSize);
            return baseSize;
        }

        return 8;  // Fallback
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

        DebugConsole.Write("[CalcFieldOff] fld=");
        DebugConsole.WriteDecimal(fieldRow);
        DebugConsole.Write(" type=");
        DebugConsole.WriteDecimal(typeRow);
        DebugConsole.Write(" asm=");
        DebugConsole.WriteDecimal(_currentAssemblyId);

        // Check if this is a value type (struct/enum)
        // Value types accessed via byref don't have an MT pointer, so offsets start at 0
        bool isValueType = IsTypeDefValueType(typeRow);

        // Get the type's field list
        uint firstField = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow);

        // Calculate offset by summing sizes of preceding fields
        // Reference types start after MethodTable pointer (8 bytes)
        // Value types start at 0 (no MT pointer in raw struct data)
        // For derived types, start after the base class fields
        int offset;
        if (isValueType)
        {
            offset = 0;
        }
        else
        {
            // Get base class size if this type extends something other than Object/ValueType
            offset = (int)GetBaseClassSizeForOffset(typeRow);
            if (offset < 8)
                offset = 8;  // Minimum is MT pointer size
        }

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
                int size;

                // For ValueType fields, use AssemblyLoader to compute actual struct size
                if (elementType == ElementType.ValueType || elementType == ElementType.GenericInst)
                {
                    // Pass the type signature (after the 0x06 calling convention byte)
                    size = (int)AssemblyLoader.GetFieldTypeSizeForAssembly(_currentAssemblyId, sig + 1, sigLen - 1);
                }
                else
                {
                    GetFieldSizeFromElementType(elementType, out byte primSize, out _, out _);
                    size = primSize;
                    if (size == 0)
                        size = 8;  // Default for unknown types
                }

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
            byte targetElementType = targetSig[1];
            int targetSize;

            // For ValueType fields, use AssemblyLoader to compute actual struct size
            if (targetElementType == ElementType.ValueType || targetElementType == ElementType.GenericInst)
            {
                targetSize = (int)AssemblyLoader.GetFieldTypeSizeForAssembly(_currentAssemblyId, targetSig + 1, targetSigLen - 1);
            }
            else
            {
                GetFieldSizeFromElementType(targetElementType, out byte primSize, out _, out _);
                targetSize = primSize;
                if (targetSize == 0)
                    targetSize = 8;
            }

            int alignment = targetSize < 8 ? targetSize : 8;
            offset = (offset + alignment - 1) & ~(alignment - 1);
        }

        DebugConsole.Write(" -> offset=");
        DebugConsole.WriteDecimal((uint)offset);
        DebugConsole.WriteLine();
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
        compiler.SetMethodResolver(ResolveMethod);
    }

    /// <summary>
    /// Resolve a method token to native code, JIT compiling if necessary.
    /// This enables lazy JIT compilation of called methods.
    /// </summary>
    public static bool ResolveMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        // First check if already in registry (use assembly-aware lookup)
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(token, _currentAssemblyId);
        if (info != null)
        {
            if (info->IsCompiled)
            {
                result.NativeCode = info->NativeCode;
                result.ArgCount = info->ArgCount;
                result.ReturnKind = info->ReturnKind;
                result.HasThis = info->HasThis;
                result.IsValid = true;
                result.IsVirtual = info->IsVirtual;
                result.VtableSlot = info->VtableSlot;
                result.MethodTable = info->MethodTable;
                result.IsInterfaceMethod = info->IsInterfaceMethod;
                result.InterfaceMT = info->InterfaceMT;
                result.InterfaceMethodSlot = info->InterfaceMethodSlot;
                result.RegistryEntry = info;  // Always set registry entry
                return true;
            }
            else if (info->IsBeingCompiled)
            {
                // Method is being compiled - this is a recursive call
                // We need to emit an indirect call through the registry entry
                // The native code will be filled in when compilation completes
                DebugConsole.Write("[MetaInt] RECURSIVE CALL detected for token 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" - using indirect call");
                result.NativeCode = null;  // Will be filled in later
                result.ArgCount = info->ArgCount;
                result.ReturnKind = info->ReturnKind;
                result.HasThis = info->HasThis;
                result.IsValid = true;
                result.IsVirtual = false;
                result.VtableSlot = -1;
                result.MethodTable = null;
                result.IsInterfaceMethod = false;
                result.InterfaceMT = null;
                result.InterfaceMethodSlot = -1;
                result.RegistryEntry = info;  // Important: pass registry entry for indirect call
                return true;
            }
        }

        // Not compiled yet - need to JIT it
        // Extract table ID from token to determine token type
        uint tableId = (token >> 24) & 0xFF;

        if (tableId == 0x06) // MethodDef token
        {
            // JIT compile the method
            if (_currentAssemblyId == 0)
            {
                DebugConsole.WriteLine("[MetaInt] No current assembly set for JIT");
                return false;
            }

            var jitResult = Tier0JIT.CompileMethod(_currentAssemblyId, token);
            if (!jitResult.Success)
            {
                // Check if it failed because of recursion (method being compiled)
                info = CompiledMethodRegistry.Lookup(token, _currentAssemblyId);
                if (info != null && info->IsBeingCompiled)
                {
                    // Recursive call - return info for indirect call
                    result.NativeCode = null;
                    result.ArgCount = info->ArgCount;
                    result.ReturnKind = info->ReturnKind;
                    result.HasThis = info->HasThis;
                    result.IsValid = true;
                    result.RegistryEntry = info;  // Important: set registry entry for indirect call
                    return true;
                }
                DebugConsole.Write("[MetaInt] Failed to JIT compile method 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine();
                return false;
            }

            // Now look it up again - should be registered
            // IMPORTANT: Use the same assembly ID we passed to CompileMethod
            info = CompiledMethodRegistry.Lookup(token, _currentAssemblyId);
            if (info == null || !info->IsCompiled)
            {
                DebugConsole.Write("[MetaInt] Method 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" not in registry after JIT (asm ");
                DebugConsole.WriteDecimal(_currentAssemblyId);
                DebugConsole.Write(") - trying token-only lookup: ");

                // Debug: Try lookup without assembly ID to see if it's registered elsewhere
                var anyInfo = CompiledMethodRegistry.Lookup(token);
                if (anyInfo != null)
                {
                    DebugConsole.Write("found in asm ");
                    DebugConsole.WriteDecimal(anyInfo->AssemblyId);
                }
                else
                {
                    DebugConsole.Write("not found anywhere");
                }
                DebugConsole.WriteLine();
                return false;
            }

            result.NativeCode = info->NativeCode;
            result.ArgCount = info->ArgCount;
            result.ReturnKind = info->ReturnKind;
            result.HasThis = info->HasThis;
            result.IsValid = true;
            result.IsVirtual = info->IsVirtual;
            result.VtableSlot = info->VtableSlot;
            result.MethodTable = info->MethodTable;
            result.IsInterfaceMethod = info->IsInterfaceMethod;
            result.InterfaceMT = info->InterfaceMT;
            result.InterfaceMethodSlot = info->InterfaceMethodSlot;
            result.RegistryEntry = info;  // Set registry entry
            return true;
        }
        else if (tableId == 0x0A) // MemberRef token
        {
            // External method reference - resolve through the assembly's references
            return ResolveMemberRefMethod(token, out result);
        }
        else
        {
            DebugConsole.Write("[MetaInt] Unknown method token table: 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }
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
