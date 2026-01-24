// ProtonOS JIT - Metadata Integration Layer
// Connects metadata tokens to runtime artifacts (MethodTable pointers, field addresses, etc.)
// This is the "glue" between MetadataReader and the JIT compiler's resolver interfaces.
//
// Phase 2: Routes type/field resolution through AssemblyLoader's per-assembly registries.

using System;
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

    /// <summary>Hash of type arguments for generic instantiations (0 if not generic).</summary>
    public ulong TypeArgHash;

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
/// Entry for tracking static constructor (cctor) contexts per type.
/// </summary>
public unsafe struct CctorRegistryEntry
{
    /// <summary>TypeDef token (0x02xxxxxx) for the type.</summary>
    public uint TypeToken;

    /// <summary>Assembly ID containing the type.</summary>
    public uint AssemblyId;

    /// <summary>Hash of type arguments for generic instantiations.</summary>
    public ulong TypeArgHash;

    /// <summary>Pointer to the StaticClassConstructionContext for this type.</summary>
    public nint* ContextAddress;

    /// <summary>True if this slot is in use.</summary>
    public bool IsUsed => TypeToken != 0;
}

/// <summary>
/// Cached field layout information for faster subsequent lookups.
/// </summary>
public unsafe struct FieldLayoutEntry
{
    /// <summary>Field metadata token.</summary>
    public uint Token;

    /// <summary>Assembly ID this field belongs to.</summary>
    public uint AssemblyId;

    /// <summary>Hash of type arguments for generic instantiations (0 if not generic).</summary>
    public ulong TypeArgHash;

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

    /// <summary>Declaring type token (TypeDef 0x02) for the field.</summary>
    public uint DeclaringTypeToken;

    /// <summary>Assembly ID containing the declaring type (for cctor lookup).</summary>
    public uint DeclaringTypeAssemblyId;

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
    // Now uses BlockChain for unlimited growth.
    private const int TypeBlockSize = 32;
    private static BlockChain _typeChain;

    // Global static field storage (legacy - used for AOT statics)
    // Now uses BlockChain for unlimited growth.
    private const int StaticFieldBlockSize = 32;
    private static BlockChain _staticFieldChain;

    // Global static storage block (legacy - for AOT statics)
    private const int StaticStorageBlockSize = 64 * 1024;  // 64KB per block
    private static byte* _staticStorageBase;
    private static int _staticStorageUsed;

    // Field layout cache (shared across assemblies for now)
    // Now uses BlockChain for unlimited growth.
    private const int FieldLayoutBlockSize = 32;
    private static BlockChain _fieldLayoutChain;

    // Default metadata context (for backward compatibility with single-assembly mode)
    private static MetadataRoot* _metadataRoot;
    private static TablesHeader* _tablesHeader;
    private static TableSizes* _tableSizes;

    // Current assembly ID for resolution context
    private static uint _currentAssemblyId;

    // MethodSpec type argument context for generic method resolution
    // This holds the parsed type arguments from the MethodSpec instantiation blob
    // Used when resolving MVAR types in method signatures
    private const int MaxMethodTypeArgs = 8;
    private static byte* _methodTypeArgs;  // Array of ELEMENT_TYPE bytes for each type arg
    private static ushort* _methodTypeArgSizes;  // Size in bytes for each type arg
    private static MethodTable** _methodTypeArgMTs;  // MethodTable pointers for each type arg (for constrained. support)
    private static int _methodTypeArgCount;

    // Type type argument context for generic type resolution
    // This holds the type arguments for the current generic type instantiation
    // Used when resolving VAR types in type signatures
    private const int MaxTypeTypeArgs = 8;
    private static MethodTable** _typeTypeArgMTs;  // MethodTable pointers for each type arg
    private static int _typeTypeArgCount;

    // Static constructor (cctor) context registry
    // Tracks StaticClassConstructionContext addresses for types with static constructors
    // Now uses BlockChain for unlimited growth.
    private const int CctorBlockSize = 32;
    private static BlockChain _cctorChain;

    // Synthetic MethodTable for ArgIterator (value type, 8 bytes)
    // Used by newobj to know the size of the value type to allocate on stack
    private static byte* _argIteratorMTBuffer;
    private static MethodTable* _argIteratorMT;

    // IDisposable interface MethodTable - captured when first type implementing IDisposable is created
    // Used for interface dispatch on callvirt IDisposable.Dispose()
    private static MethodTable* _iDisposableMT;

    private static bool _initialized;

    /// <summary>
    /// Initialize the metadata integration layer.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Initialize type registry block chain
        fixed (BlockChain* chain = &_typeChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(TypeRegistryEntry), TypeBlockSize))
            {
                DebugConsole.WriteLine("[MetaInt] Failed to init type registry chain");
                return;
            }
        }

        // Initialize static field registry block chain
        fixed (BlockChain* chain = &_staticFieldChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(StaticFieldEntry), StaticFieldBlockSize))
            {
                DebugConsole.WriteLine("[MetaInt] Failed to init static field chain");
                return;
            }
        }

        // Initialize field layout cache block chain
        fixed (BlockChain* chain = &_fieldLayoutChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(FieldLayoutEntry), FieldLayoutBlockSize))
            {
                DebugConsole.WriteLine("[MetaInt] Failed to init field layout chain");
                return;
            }
        }

        // Initialize cctor registry block chain
        fixed (BlockChain* chain = &_cctorChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(CctorRegistryEntry), CctorBlockSize))
            {
                DebugConsole.WriteLine("[MetaInt] Failed to init cctor chain");
                return;
            }
        }

        // Allocate MethodSpec type argument storage
        _methodTypeArgs = (byte*)HeapAllocator.AllocZeroed((ulong)MaxMethodTypeArgs);
        _methodTypeArgSizes = (ushort*)HeapAllocator.AllocZeroed((ulong)(MaxMethodTypeArgs * sizeof(ushort)));
        _methodTypeArgMTs = (MethodTable**)HeapAllocator.AllocZeroed((ulong)(MaxMethodTypeArgs * sizeof(MethodTable*)));
        if (_methodTypeArgs == null || _methodTypeArgSizes == null || _methodTypeArgMTs == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate method type arg storage");
            return;
        }
        _methodTypeArgCount = 0;

        // Allocate type type argument storage (for VAR resolution in generic types)
        _typeTypeArgMTs = (MethodTable**)HeapAllocator.AllocZeroed((ulong)(MaxTypeTypeArgs * sizeof(MethodTable*)));
        if (_typeTypeArgMTs == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate type type arg storage");
            return;
        }
        _typeTypeArgCount = 0;

        _staticStorageBase = null;
        _staticStorageUsed = 0;

        // Create synthetic MethodTable for ArgIterator (value type, 8 bytes actual size)
        // ArgIterator has one field: byte* _current (8 bytes)
        // BaseSize = actual size + 8 (header) to be consistent with JIT's BaseSize - 8 formula
        _argIteratorMTBuffer = (byte*)HeapAllocator.AllocZeroed((ulong)MethodTableWithVtableSize);
        if (_argIteratorMTBuffer != null)
        {
            _argIteratorMT = (MethodTable*)_argIteratorMTBuffer;
            _argIteratorMT->_usComponentSize = 0;
            _argIteratorMT->_usFlags = 0x0020;  // IsValueType flag
            _argIteratorMT->_uBaseSize = 16;  // 8 (actual) + 8 (header)
            _argIteratorMT->_relatedType = null;
            _argIteratorMT->_usNumVtableSlots = 0;
            _argIteratorMT->_usNumInterfaces = 0;
            _argIteratorMT->_uHashCode = 0;
            DebugConsole.WriteLine("[MetaInt] Created ArgIterator synthetic MethodTable");
        }

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

            // Also update MetadataReader's cached root for ldstr resolution
            // This ensures string tokens are resolved from the correct assembly's #US heap
            MetadataReader.SetMetadataRoot(&asm->Metadata);
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
        // Primitive types
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

        // Delegate types - for multicast delegate support
        public const uint Delegate = 0xF0000018;
        public const uint MulticastDelegate = 0xF0000019;

        // Exception types - for JIT assemblies to reference AOT exception classes
        public const uint Exception = 0xF0000020;
        public const uint ArgumentException = 0xF0000021;
        public const uint ArgumentNullException = 0xF0000022;
        public const uint ArgumentOutOfRangeException = 0xF0000023;
        public const uint InvalidOperationException = 0xF0000024;
        public const uint NotSupportedException = 0xF0000025;
        public const uint NotImplementedException = 0xF0000026;
        public const uint IndexOutOfRangeException = 0xF0000027;
        public const uint NullReferenceException = 0xF0000028;
        public const uint InvalidCastException = 0xF0000029;
        public const uint FormatException = 0xF000002A;
        public const uint DivideByZeroException = 0xF000002B;
        public const uint OverflowException = 0xF000002C;
        public const uint StackOverflowException = 0xF000002D;
        public const uint AggregateException = 0xF000002E;
        public const uint TaskCanceledException = 0xF000002F;
        public const uint ArrayTypeMismatchException = 0xF0000050;
        public const uint InvalidProgramException = 0xF0000051;
        public const uint TypeLoadException = 0xF0000052;

        // Reflection types - for GetType() support
        public const uint Type = 0xF0000030;
        public const uint RuntimeType = 0xF0000031;

        // Varargs types - for __arglist support
        public const uint TypedReference = 0xF0000040;
        public const uint RuntimeArgumentHandle = 0xF0000041;
        public const uint ArgIterator = 0xF0000042;

        // Runtime handle types - for reflection handle access
        public const uint RuntimeTypeHandle = 0xF0000043;
        public const uint RuntimeMethodHandle = 0xF0000044;
        public const uint RuntimeFieldHandle = 0xF0000045;

        // Interface types - for using statement / IDisposable support
        // Note: 0xF0000050 is taken by ArrayTypeMismatchException above
        public const uint IDisposable = 0xF0000080;
        // Note: IEnumerable/IEnumerator are NOT well-known types because their methods
        // need to be resolved from korlib metadata (for interface dispatch).
        // They are resolved via korlib fallback in ResolveTypeRef.

        // Span types - ref struct memory access
        public const uint Span = 0xF0000060;           // Span`1
        public const uint ReadOnlySpan = 0xF0000061;   // ReadOnlySpan`1

        // Nullable types - for lifted operators
        public const uint Nullable = 0xF0000068;       // Nullable`1

        // Base types - for type hierarchy
        public const uint ValueType = 0xF0000070;
        public const uint Enum = 0xF0000071;
        public const uint Array = 0xF0000072;
        public const uint Void = 0xF0000073;
    }

    /// <summary>
    /// Register the IDisposable interface MethodTable.
    /// Called by AssemblyLoader when creating the first type that implements IDisposable.
    /// </summary>
    public static void RegisterIDisposableMT(MethodTable* mt)
    {
        if (mt == null || _iDisposableMT != null)
            return;  // Already registered or invalid

        _iDisposableMT = mt;

        // Also register in the type chain so LookupType can find it
        RegisterType(WellKnownTypes.IDisposable, mt);

        DebugConsole.Write("[MetaInt] Registered IDisposable MT: 0x");
        DebugConsole.WriteHex((ulong)mt);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Get the IDisposable interface MethodTable.
    /// Returns null if no type implementing IDisposable has been created yet.
    /// </summary>
    public static MethodTable* GetIDisposableMT()
    {
        return _iDisposableMT;
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
        MethodTable* stringMT = (MethodTable*)emptyStr.m_pMethodTable;
        if (RegisterType(WellKnownTypes.String, stringMT))
            count++;
        // Register String with ReflectionRuntime so FieldType lookups work
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.String, stringMT);

        // System.Object - use the parent type pointer from String's MT
        // String inherits from Object, so String's parent is Object
        if (stringMT != null)
        {
            MethodTable* objectMT = stringMT->GetParentType();
            if (objectMT != null)
            {
                if (RegisterType(WellKnownTypes.Object, objectMT))
                    count++;
                // Register Object with ReflectionRuntime
                Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Object, objectMT);
            }
        }

        // Primitive types: extract MethodTable from array element types
        // Arrays are reference types, so we can create them without boxing.
        // The array's _relatedType field points to the element type's MethodTable.
        count += RegisterPrimitiveTypesFromArrays();

        // Exception types: extract MethodTables from instances
        // This allows JIT code to reference exception types from korlib
        count += RegisterExceptionTypes();

        // Delegate types: extract from concrete delegate inheritance chain
        count += RegisterDelegateTypes();

        // Reflection types: Type and RuntimeType for GetType() support
        count += RegisterReflectionTypes();

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
    // These are used for newarr to get element sizes AND for virtual method dispatch
    // Each MethodTable needs space for vtable slots (3 slots: ToString, Equals, GetHashCode)
    private static byte* _primitiveMethodTableBuffer;
    private const int NumPrimitiveTypes = 16;
    private const int NumVtableSlots = 3;  // Slot 0: ToString, Slot 1: Equals, Slot 2: GetHashCode
    private const int MethodTableWithVtableSize = MethodTable.HeaderSize + (NumVtableSlots * 8);  // 24 + 24 = 48 bytes

    // Store Object.ToString/Equals/GetHashCode addresses for comparison when resolving primitive overrides
    private static nint _objectToString;
    private static nint _objectEquals;
    private static nint _objectGetHashCode;

    /// <summary>
    /// Get a MethodTable pointer from the buffer at the given index.
    /// </summary>
    private static MethodTable* GetPrimitiveMT(int index)
    {
        return (MethodTable*)(_primitiveMethodTableBuffer + (index * MethodTableWithVtableSize));
    }

    /// <summary>
    /// Check if a MethodTable pointer is one of the primitive types.
    /// Used by JIT to optimize constrained callvirt on primitives.
    /// </summary>
    public static bool IsPrimitiveMT(MethodTable* mt)
    {
        if (_primitiveMethodTableBuffer == null || mt == null)
            return false;

        byte* mtAddr = (byte*)mt;
        byte* bufferStart = _primitiveMethodTableBuffer;
        byte* bufferEnd = _primitiveMethodTableBuffer + (NumPrimitiveTypes * MethodTableWithVtableSize);

        return mtAddr >= bufferStart && mtAddr < bufferEnd;
    }

    /// <summary>
    /// Get the primitive type index for a MethodTable.
    /// Returns -1 if not a primitive type.
    /// Index mapping: 0=Int32, 1=Int64, 2=Boolean, 3=Byte, 4=Char, 5=Double,
    ///                6=Single, 7=Int16, 8=UInt16, 9=UInt32, 10=UInt64,
    ///                11=IntPtr, 12=UIntPtr, 13=SByte
    /// </summary>
    public static int GetPrimitiveIndex(MethodTable* mt)
    {
        if (_primitiveMethodTableBuffer == null || mt == null)
            return -1;

        byte* mtAddr = (byte*)mt;
        byte* bufferStart = _primitiveMethodTableBuffer;
        byte* bufferEnd = _primitiveMethodTableBuffer + (NumPrimitiveTypes * MethodTableWithVtableSize);

        if (mtAddr < bufferStart || mtAddr >= bufferEnd)
            return -1;

        return (int)((mtAddr - bufferStart) / MethodTableWithVtableSize);
    }

    /// <summary>
    /// Get primitive type name by index.
    /// Index mapping: 0=Int32, 1=Int64, 2=Boolean, 3=Byte, 4=Char, 5=Double,
    ///                6=Single, 7=Int16, 8=UInt16, 9=UInt32, 10=UInt64,
    ///                11=IntPtr, 12=UIntPtr, 13=SByte
    /// </summary>
    private static string GetPrimitiveTypeShortName(int index)
    {
        if (index == 0) return "Int32";
        if (index == 1) return "Int64";
        if (index == 2) return "Boolean";
        if (index == 3) return "Byte";
        if (index == 4) return "Char";
        if (index == 5) return "Double";
        if (index == 6) return "Single";
        if (index == 7) return "Int16";
        if (index == 8) return "UInt16";
        if (index == 9) return "UInt32";
        if (index == 10) return "UInt64";
        if (index == 11) return "IntPtr";
        if (index == 12) return "UIntPtr";
        if (index == 13) return "SByte";
        return null;
    }

    /// <summary>
    /// Get the full System.* type name for a primitive MethodTable.
    /// Returns null if not a primitive type.
    /// Used by JIT to look up byref calling convention variants.
    /// </summary>
    public static string? GetPrimitiveTypeName(MethodTable* mt)
    {
        int index = GetPrimitiveIndex(mt);
        if (index < 0) return null;

        // Return full type name for AOT registry lookup
        if (index == 0) return "System.Int32";
        if (index == 1) return "System.Int64";
        if (index == 2) return "System.Boolean";
        if (index == 3) return "System.Byte";
        if (index == 4) return "System.Char";
        if (index == 5) return "System.Double";
        if (index == 6) return "System.Single";
        if (index == 7) return "System.Int16";
        if (index == 8) return "System.UInt16";
        if (index == 9) return "System.UInt32";
        if (index == 10) return "System.UInt64";
        if (index == 11) return "System.IntPtr";
        if (index == 12) return "System.UIntPtr";
        if (index == 13) return "System.SByte";
        return null;
    }

    /// <summary>
    /// Try to resolve a primitive's virtual method override from korlib.
    /// This is called when a primitive's vtable slot contains Object's method
    /// but the actual primitive has an override defined in korlib.
    /// </summary>
    /// <param name="mt">The primitive MethodTable</param>
    /// <param name="vtableSlot">The vtable slot (0=ToString, 1=Equals, 2=GetHashCode)</param>
    /// <param name="currentSlotValue">The current value in the slot</param>
    /// <returns>The compiled override method address, or 0 if not found</returns>
    public static nint TryResolvePrimitiveVirtualOverride(MethodTable* mt, int vtableSlot, nint currentSlotValue)
    {
        // NOTE: This function is no longer actively used. Primitive vtable slots are now
        // pre-filled with the correct methods during initialization in RegisterPrimitiveViaArrayAllocation.
        // Kept for potential future debugging or edge cases.

        // Check if this is a primitive with Object's method in the slot
        int primIndex = GetPrimitiveIndex(mt);
        if (primIndex < 0 || primIndex > 13)
            return 0;

        // Check if current value is Object's method (the fallback we want to replace)
        nint objectMethod = 0;
        if (vtableSlot == 0) objectMethod = _objectToString;
        else if (vtableSlot == 1) objectMethod = _objectEquals;
        else if (vtableSlot == 2) objectMethod = _objectGetHashCode;

        if (objectMethod == 0 || currentSlotValue != objectMethod)
            return 0;  // Not using Object's fallback, no need to resolve

        // Get method name for the slot
        byte* methodNameBytes = null;
        if (vtableSlot == 0)
        {
            // "ToString\0"
            byte* buf = stackalloc byte[16];
            buf[0] = (byte)'T'; buf[1] = (byte)'o'; buf[2] = (byte)'S'; buf[3] = (byte)'t';
            buf[4] = (byte)'r'; buf[5] = (byte)'i'; buf[6] = (byte)'n'; buf[7] = (byte)'g';
            buf[8] = 0;
            methodNameBytes = buf;
        }
        else if (vtableSlot == 1)
        {
            // "Equals\0"
            byte* buf = stackalloc byte[16];
            buf[0] = (byte)'E'; buf[1] = (byte)'q'; buf[2] = (byte)'u'; buf[3] = (byte)'a';
            buf[4] = (byte)'l'; buf[5] = (byte)'s'; buf[6] = 0;
            methodNameBytes = buf;
        }
        else if (vtableSlot == 2)
        {
            // "GetHashCode\0"
            byte* buf = stackalloc byte[16];
            buf[0] = (byte)'G'; buf[1] = (byte)'e'; buf[2] = (byte)'t'; buf[3] = (byte)'H';
            buf[4] = (byte)'a'; buf[5] = (byte)'s'; buf[6] = (byte)'h'; buf[7] = (byte)'C';
            buf[8] = (byte)'o'; buf[9] = (byte)'d'; buf[10] = (byte)'e'; buf[11] = 0;
            methodNameBytes = buf;
        }

        if (methodNameBytes == null)
            return 0;

        string typeName = GetPrimitiveTypeShortName(primIndex);
        if (typeName == null)
            return 0;

        // Get korlib assembly
        var korlib = AssemblyLoader.GetCoreLib();
        if (korlib == null)
            return 0;

        // Find the TypeDef for this primitive in korlib
        uint typeDefToken = AssemblyLoader.FindTypeDefByFullName(korlib->AssemblyId, "System", typeName);
        if (typeDefToken == 0)
            return 0;

        uint methodToken = FindMethodByName(korlib->AssemblyId, typeDefToken, methodNameBytes);
        if (methodToken == 0)
            return 0;

        // Compile the method
        var result = Tier0JIT.CompileMethod(korlib->AssemblyId, methodToken);
        if (!result.Success || result.CodeAddress == null)
        {
            return 0;
        }

        nint nativeCode = (nint)result.CodeAddress;

        // Update the primitive's vtable slot with the compiled override
        nint* vtable = mt->GetVtablePtr();
        vtable[vtableSlot] = nativeCode;

        return nativeCode;
    }

    /// <summary>
    /// Check if a vtable slot value is Object.ToString (used to detect fallback).
    /// </summary>
    public static bool IsObjectToString(nint slotValue)
    {
        return slotValue == _objectToString;
    }

    /// <summary>
    /// Set a vtable slot for a primitive MethodTable.
    /// </summary>
    private static void SetVtableSlot(MethodTable* mt, int slot, nint functionPtr)
    {
        nint* vtable = mt->GetVtablePtr();
        vtable[slot] = functionPtr;
    }

    /// <summary>
    /// Initialize a primitive MethodTable with vtable entries.
    /// </summary>
    private static void InitPrimitiveMT(MethodTable* mt, ushort componentSize, uint baseSize,
                                         nint toStringPtr, nint equalsPtr, nint getHashCodePtr)
    {
        const ushort ValueTypeFlag = 0x0020;  // IsValueType flag

        mt->_usComponentSize = componentSize;
        mt->_usFlags = ValueTypeFlag;
        mt->_uBaseSize = baseSize;
        mt->_relatedType = null;  // Value types keep null - IsReferenceType checks this
        mt->_usNumVtableSlots = NumVtableSlots;
        mt->_usNumInterfaces = 0;
        mt->_uHashCode = 0;

        // Set vtable entries
        SetVtableSlot(mt, 0, toStringPtr);   // Slot 0: ToString
        SetVtableSlot(mt, 1, equalsPtr);     // Slot 1: Equals
        SetVtableSlot(mt, 2, getHashCodePtr); // Slot 2: GetHashCode

        // Debug: verify slots were written
        nint* vtable = mt->GetVtablePtr();
        DebugConsole.Write("[MetaInt] MT@0x");
        DebugConsole.WriteHex((ulong)mt);
        DebugConsole.Write(" slots[0]=0x");
        DebugConsole.WriteHex((ulong)vtable[0]);
        DebugConsole.Write(" [1]=0x");
        DebugConsole.WriteHex((ulong)vtable[1]);
        DebugConsole.Write(" [2]=0x");
        DebugConsole.WriteHex((ulong)vtable[2]);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Register primitives by creating synthetic MethodTables with correct element sizes.
    /// These are used by newarr to determine array element sizes.
    /// The MethodTables include vtable entries for virtual method dispatch.
    /// </summary>
    private static int RegisterPrimitiveViaArrayAllocation()
    {
        // Allocate storage for primitive MethodTables WITH vtable space
        _primitiveMethodTableBuffer = (byte*)HeapAllocator.AllocZeroed(
            (ulong)(NumPrimitiveTypes * MethodTableWithVtableSize));

        if (_primitiveMethodTableBuffer == null)
        {
            DebugConsole.WriteLine("[MetaInt] Failed to allocate primitive MethodTables");
            return 0;
        }

        // Get AOT method pointers for Object's virtual methods (fallback for types without overrides)
        nint objectToString = AotMethodRegistry.LookupByName("System.Object", "ToString");
        nint objectEquals = AotMethodRegistry.LookupByName("System.Object", "Equals");
        nint objectGetHashCode = AotMethodRegistry.LookupByName("System.Object", "GetHashCode");

        // Store for later comparison when resolving primitive korlib overrides
        _objectToString = objectToString;
        _objectEquals = objectEquals;
        _objectGetHashCode = objectGetHashCode;

        DebugConsole.Write("[MetaInt] AOT vtable ptrs: ToString=0x");
        DebugConsole.WriteHex((ulong)objectToString);
        DebugConsole.Write(" Equals=0x");
        DebugConsole.WriteHex((ulong)objectEquals);
        DebugConsole.Write(" GetHashCode=0x");
        DebugConsole.WriteHex((ulong)objectGetHashCode);
        DebugConsole.WriteLine();

        // Fallback: if AOT methods not found, use null (will crash but provides debug info)
        if (objectToString == 0)
        {
            DebugConsole.WriteLine("[MetaInt] WARNING: Object.ToString not found in AOT registry");
        }
        if (objectEquals == 0)
        {
            DebugConsole.WriteLine("[MetaInt] WARNING: Object.Equals not found in AOT registry");
        }
        if (objectGetHashCode == 0)
        {
            DebugConsole.WriteLine("[MetaInt] WARNING: Object.GetHashCode not found in AOT registry");
        }

        // Get type-specific method pointers for primitives (override Object's virtual methods)
        // If a type-specific method is not found, fall back to Object's method
        nint int32ToString = AotMethodRegistry.LookupByName("System.Int32", "ToString");
        nint int64ToString = AotMethodRegistry.LookupByName("System.Int64", "ToString");
        nint boolToString = AotMethodRegistry.LookupByName("System.Boolean", "ToString");
        nint byteToString = AotMethodRegistry.LookupByName("System.Byte", "ToString");
        nint charToString = AotMethodRegistry.LookupByName("System.Char", "ToString");
        nint doubleToString = AotMethodRegistry.LookupByName("System.Double", "ToString");
        nint singleToString = AotMethodRegistry.LookupByName("System.Single", "ToString");
        nint int16ToString = AotMethodRegistry.LookupByName("System.Int16", "ToString");
        nint uint16ToString = AotMethodRegistry.LookupByName("System.UInt16", "ToString");
        nint uint32ToString = AotMethodRegistry.LookupByName("System.UInt32", "ToString");
        nint uint64ToString = AotMethodRegistry.LookupByName("System.UInt64", "ToString");
        nint sbyteToString = AotMethodRegistry.LookupByName("System.SByte", "ToString");

        // GetHashCode overrides for primitives
        nint int32GetHashCode = AotMethodRegistry.LookupByName("System.Int32", "GetHashCode");
        nint int64GetHashCode = AotMethodRegistry.LookupByName("System.Int64", "GetHashCode");
        nint boolGetHashCode = AotMethodRegistry.LookupByName("System.Boolean", "GetHashCode");
        nint byteGetHashCode = AotMethodRegistry.LookupByName("System.Byte", "GetHashCode");
        nint charGetHashCode = AotMethodRegistry.LookupByName("System.Char", "GetHashCode");
        nint doubleGetHashCode = AotMethodRegistry.LookupByName("System.Double", "GetHashCode");
        nint singleGetHashCode = AotMethodRegistry.LookupByName("System.Single", "GetHashCode");
        nint int16GetHashCode = AotMethodRegistry.LookupByName("System.Int16", "GetHashCode");
        nint uint16GetHashCode = AotMethodRegistry.LookupByName("System.UInt16", "GetHashCode");
        nint uint32GetHashCode = AotMethodRegistry.LookupByName("System.UInt32", "GetHashCode");
        nint uint64GetHashCode = AotMethodRegistry.LookupByName("System.UInt64", "GetHashCode");
        nint sbyteGetHashCode = AotMethodRegistry.LookupByName("System.SByte", "GetHashCode");

        // Use fallbacks for missing type-specific methods
        if (int32ToString == 0) int32ToString = objectToString;
        if (int64ToString == 0) int64ToString = objectToString;
        if (boolToString == 0) boolToString = objectToString;
        if (byteToString == 0) byteToString = objectToString;
        if (charToString == 0) charToString = objectToString;
        if (doubleToString == 0) doubleToString = objectToString;
        if (singleToString == 0) singleToString = objectToString;
        if (int16ToString == 0) int16ToString = objectToString;
        if (uint16ToString == 0) uint16ToString = objectToString;
        if (uint32ToString == 0) uint32ToString = objectToString;
        if (uint64ToString == 0) uint64ToString = objectToString;
        if (sbyteToString == 0) sbyteToString = objectToString;

        if (int32GetHashCode == 0) int32GetHashCode = objectGetHashCode;
        if (int64GetHashCode == 0) int64GetHashCode = objectGetHashCode;
        if (boolGetHashCode == 0) boolGetHashCode = objectGetHashCode;
        if (byteGetHashCode == 0) byteGetHashCode = objectGetHashCode;
        if (charGetHashCode == 0) charGetHashCode = objectGetHashCode;
        if (doubleGetHashCode == 0) doubleGetHashCode = objectGetHashCode;
        if (singleGetHashCode == 0) singleGetHashCode = objectGetHashCode;
        if (int16GetHashCode == 0) int16GetHashCode = objectGetHashCode;
        if (uint16GetHashCode == 0) uint16GetHashCode = objectGetHashCode;
        if (uint32GetHashCode == 0) uint32GetHashCode = objectGetHashCode;
        if (uint64GetHashCode == 0) uint64GetHashCode = objectGetHashCode;
        if (sbyteGetHashCode == 0) sbyteGetHashCode = objectGetHashCode;

        int count = 0;
        MethodTable* mt;

        // NOTE: For value types, baseSize is the BOXED object size (value + 8-byte header).
        // This is required for RhpNewFast allocation. GetTypeSize subtracts 8 to get raw value size.
        // The componentSize field is used for arrays - it stores the element size.

        // Int32 - 4 bytes value, 12 bytes boxed
        mt = GetPrimitiveMT(0);
        InitPrimitiveMT(mt, 4, 12, int32ToString, objectEquals, int32GetHashCode);
        if (RegisterType(WellKnownTypes.Int32, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Int32, mt);

        // Int64 - 8 bytes value, 16 bytes boxed
        mt = GetPrimitiveMT(1);
        InitPrimitiveMT(mt, 8, 16, int64ToString, objectEquals, int64GetHashCode);
        if (RegisterType(WellKnownTypes.Int64, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Int64, mt);

        // Boolean - 1 byte value, 9 bytes boxed
        mt = GetPrimitiveMT(2);
        InitPrimitiveMT(mt, 1, 9, boolToString, objectEquals, boolGetHashCode);
        if (RegisterType(WellKnownTypes.Boolean, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Boolean, mt);

        // Byte - 1 byte value, 9 bytes boxed
        mt = GetPrimitiveMT(3);
        InitPrimitiveMT(mt, 1, 9, byteToString, objectEquals, byteGetHashCode);
        if (RegisterType(WellKnownTypes.Byte, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Byte, mt);

        // Char - 2 bytes value, 10 bytes boxed
        mt = GetPrimitiveMT(4);
        InitPrimitiveMT(mt, 2, 10, charToString, objectEquals, charGetHashCode);
        if (RegisterType(WellKnownTypes.Char, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Char, mt);

        // Double - 8 bytes value, 16 bytes boxed
        mt = GetPrimitiveMT(5);
        InitPrimitiveMT(mt, 8, 16, doubleToString, objectEquals, doubleGetHashCode);
        if (RegisterType(WellKnownTypes.Double, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Double, mt);

        // Single - 4 bytes value, 12 bytes boxed
        mt = GetPrimitiveMT(6);
        InitPrimitiveMT(mt, 4, 12, singleToString, objectEquals, singleGetHashCode);
        if (RegisterType(WellKnownTypes.Single, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Single, mt);

        // Int16 - 2 bytes value, 10 bytes boxed
        mt = GetPrimitiveMT(7);
        InitPrimitiveMT(mt, 2, 10, int16ToString, objectEquals, int16GetHashCode);
        if (RegisterType(WellKnownTypes.Int16, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.Int16, mt);

        // UInt16 - 2 bytes value, 10 bytes boxed
        mt = GetPrimitiveMT(8);
        InitPrimitiveMT(mt, 2, 10, uint16ToString, objectEquals, uint16GetHashCode);
        if (RegisterType(WellKnownTypes.UInt16, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.UInt16, mt);

        // UInt32 - 4 bytes value, 12 bytes boxed
        mt = GetPrimitiveMT(9);
        InitPrimitiveMT(mt, 4, 12, uint32ToString, objectEquals, uint32GetHashCode);
        if (RegisterType(WellKnownTypes.UInt32, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.UInt32, mt);

        // UInt64 - 8 bytes value, 16 bytes boxed
        mt = GetPrimitiveMT(10);
        InitPrimitiveMT(mt, 8, 16, uint64ToString, objectEquals, uint64GetHashCode);
        if (RegisterType(WellKnownTypes.UInt64, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.UInt64, mt);

        // IntPtr - 8 bytes value, 16 bytes boxed (64-bit)
        mt = GetPrimitiveMT(11);
        InitPrimitiveMT(mt, 8, 16, objectToString, objectEquals, objectGetHashCode);
        if (RegisterType(WellKnownTypes.IntPtr, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.IntPtr, mt);

        // UIntPtr - 8 bytes value, 16 bytes boxed (64-bit)
        mt = GetPrimitiveMT(12);
        InitPrimitiveMT(mt, 8, 16, objectToString, objectEquals, objectGetHashCode);
        if (RegisterType(WellKnownTypes.UIntPtr, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.UIntPtr, mt);

        // SByte - 1 byte value, 9 bytes boxed
        mt = GetPrimitiveMT(13);
        InitPrimitiveMT(mt, 1, 9, sbyteToString, objectEquals, sbyteGetHashCode);
        if (RegisterType(WellKnownTypes.SByte, mt)) count++;
        Reflection.ReflectionRuntime.RegisterTypeInfo(0, WellKnownTypes.SByte, mt);

        return count;
    }

    /// <summary>
    /// Register exception types by extracting their MethodTables from instances.
    /// This allows JIT code to reference exception types defined in korlib.
    /// </summary>
    private static int RegisterExceptionTypes()
    {
        int count = 0;

        // Create exception instances and extract their MethodTables
        // We use try to create minimal instances just to get MTs

        // Base Exception type
        var ex = new Exception();
        MethodTable* exMT = (MethodTable*)ex.m_pMethodTable;
        if (exMT != null && RegisterType(WellKnownTypes.Exception, exMT))
            count++;

        // ArgumentException hierarchy
        var argEx = new ArgumentException();
        MethodTable* argExMT = (MethodTable*)argEx.m_pMethodTable;
        if (argExMT != null && RegisterType(WellKnownTypes.ArgumentException, argExMT))
            count++;

        var argNullEx = new ArgumentNullException();
        MethodTable* argNullExMT = (MethodTable*)argNullEx.m_pMethodTable;
        if (argNullExMT != null && RegisterType(WellKnownTypes.ArgumentNullException, argNullExMT))
            count++;

        var argRangeEx = new ArgumentOutOfRangeException();
        MethodTable* argRangeExMT = (MethodTable*)argRangeEx.m_pMethodTable;
        if (argRangeExMT != null && RegisterType(WellKnownTypes.ArgumentOutOfRangeException, argRangeExMT))
            count++;

        // InvalidOperationException
        var invalidOpEx = new InvalidOperationException();
        MethodTable* invalidOpExMT = (MethodTable*)invalidOpEx.m_pMethodTable;
        if (invalidOpExMT != null && RegisterType(WellKnownTypes.InvalidOperationException, invalidOpExMT))
            count++;

        // NotSupportedException
        var notSupportEx = new NotSupportedException();
        MethodTable* notSupportExMT = (MethodTable*)notSupportEx.m_pMethodTable;
        if (notSupportExMT != null && RegisterType(WellKnownTypes.NotSupportedException, notSupportExMT))
            count++;

        // NotImplementedException
        var notImplEx = new NotImplementedException();
        MethodTable* notImplExMT = (MethodTable*)notImplEx.m_pMethodTable;
        if (notImplExMT != null && RegisterType(WellKnownTypes.NotImplementedException, notImplExMT))
            count++;

        // IndexOutOfRangeException
        var indexEx = new IndexOutOfRangeException();
        MethodTable* indexExMT = (MethodTable*)indexEx.m_pMethodTable;
        if (indexExMT != null && RegisterType(WellKnownTypes.IndexOutOfRangeException, indexExMT))
            count++;

        // NullReferenceException
        var nullRefEx = new NullReferenceException();
        MethodTable* nullRefExMT = (MethodTable*)nullRefEx.m_pMethodTable;
        if (nullRefExMT != null && RegisterType(WellKnownTypes.NullReferenceException, nullRefExMT))
            count++;

        // InvalidCastException
        var invalidCastEx = new InvalidCastException();
        MethodTable* invalidCastExMT = (MethodTable*)invalidCastEx.m_pMethodTable;
        if (invalidCastExMT != null && RegisterType(WellKnownTypes.InvalidCastException, invalidCastExMT))
            count++;

        // FormatException
        var formatEx = new FormatException();
        MethodTable* formatExMT = (MethodTable*)formatEx.m_pMethodTable;
        if (formatExMT != null && RegisterType(WellKnownTypes.FormatException, formatExMT))
            count++;

        // DivideByZeroException
        var divByZeroEx = new DivideByZeroException();
        MethodTable* divByZeroExMT = (MethodTable*)divByZeroEx.m_pMethodTable;
        if (divByZeroExMT != null && RegisterType(WellKnownTypes.DivideByZeroException, divByZeroExMT))
            count++;

        // OverflowException
        var overflowEx = new OverflowException();
        MethodTable* overflowExMT = (MethodTable*)overflowEx.m_pMethodTable;
        if (overflowExMT != null && RegisterType(WellKnownTypes.OverflowException, overflowExMT))
            count++;

        // StackOverflowException
        var stackOverflowEx = new StackOverflowException();
        MethodTable* stackOverflowExMT = (MethodTable*)stackOverflowEx.m_pMethodTable;
        if (stackOverflowExMT != null && RegisterType(WellKnownTypes.StackOverflowException, stackOverflowExMT))
            count++;

        // AggregateException
        var aggregateEx = new AggregateException();
        MethodTable* aggregateExMT = (MethodTable*)aggregateEx.m_pMethodTable;
        if (aggregateExMT != null && RegisterType(WellKnownTypes.AggregateException, aggregateExMT))
            count++;

        // TaskCanceledException
        var taskCanceledEx = new System.Threading.Tasks.TaskCanceledException();
        MethodTable* taskCanceledExMT = (MethodTable*)taskCanceledEx.m_pMethodTable;
        if (taskCanceledExMT != null && RegisterType(WellKnownTypes.TaskCanceledException, taskCanceledExMT))
            count++;

        // ArrayTypeMismatchException
        var arrayTypeMismatchEx = new ArrayTypeMismatchException();
        MethodTable* arrayTypeMismatchExMT = (MethodTable*)arrayTypeMismatchEx.m_pMethodTable;
        if (arrayTypeMismatchExMT != null && RegisterType(WellKnownTypes.ArrayTypeMismatchException, arrayTypeMismatchExMT))
            count++;

        // InvalidProgramException
        var invalidProgEx = new InvalidProgramException();
        MethodTable* invalidProgExMT = (MethodTable*)invalidProgEx.m_pMethodTable;
        if (invalidProgExMT != null && RegisterType(WellKnownTypes.InvalidProgramException, invalidProgExMT))
            count++;

        // TypeLoadException
        var typeLoadEx = new TypeLoadException();
        MethodTable* typeLoadExMT = (MethodTable*)typeLoadEx.m_pMethodTable;
        if (typeLoadExMT != null && RegisterType(WellKnownTypes.TypeLoadException, typeLoadExMT))
            count++;

        return count;
    }

    // Dummy static method for delegate type registration
    private static void DummyDelegateTarget() { }

    /// <summary>
    /// Register Delegate and MulticastDelegate types.
    /// Since these are abstract, we get them by walking up from a concrete delegate type.
    /// Concrete delegates (like Action, Func, etc.) inherit: ConcreteDelegate -> MulticastDelegate -> Delegate -> Object
    /// </summary>
    private static int RegisterDelegateTypes()
    {
        int count = 0;

        // Create a concrete delegate to get the inheritance chain
        // Use a static method reference instead of a lambda (lambdas create closures)
        Action testDelegate = DummyDelegateTarget;
        MethodTable* concreteMT = (MethodTable*)testDelegate.m_pMethodTable;

        if (concreteMT != null)
        {
            // Walk up the inheritance chain
            // ConcreteDelegate -> MulticastDelegate -> Delegate -> Object
            MethodTable* multicastDelegateMT = concreteMT->GetParentType();
            if (multicastDelegateMT != null)
            {
                if (RegisterType(WellKnownTypes.MulticastDelegate, multicastDelegateMT))
                    count++;

                MethodTable* delegateMT = multicastDelegateMT->GetParentType();
                if (delegateMT != null)
                {
                    if (RegisterType(WellKnownTypes.Delegate, delegateMT))
                        count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// Register System.Type and System.RuntimeType for reflection support.
    /// RuntimeType is concrete; Type is abstract, so we walk up from RuntimeType.
    /// Inheritance: RuntimeType -> Type -> MemberInfo -> Object
    /// </summary>
    private static int RegisterReflectionTypes()
    {
        int count = 0;

        // Create a RuntimeType instance to get its MethodTable
        // Use a null MethodTable* - we just need the RuntimeType's own MT
        var runtimeType = new RuntimeType(null);
        MethodTable* runtimeTypeMT = (MethodTable*)runtimeType.m_pMethodTable;

        if (runtimeTypeMT != null)
        {
            if (RegisterType(WellKnownTypes.RuntimeType, runtimeTypeMT))
                count++;

            // Walk up inheritance chain to get Type's MT
            // RuntimeType -> Type -> MemberInfo -> Object
            MethodTable* typeMT = runtimeTypeMT->GetParentType();
            if (typeMT != null)
            {
                if (RegisterType(WellKnownTypes.Type, typeMT))
                    count++;
            }
        }

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

        // Check if already registered - iterate through blocks
        fixed (BlockChain* chain = &_typeChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (TypeRegistryEntry*)block->GetEntry(i);
                    if (entry->Token == token)
                    {
                        entry->MT = mt;  // Update existing
                        return true;
                    }
                }
                block = block->Next;
            }

            // Add new entry
            TypeRegistryEntry newEntry;
            newEntry.Token = token;
            newEntry.MT = mt;
            if (BlockAllocator.Add(chain, &newEntry) == null)
            {
                DebugConsole.WriteLine("[MetaInt] Type registry allocation failed");
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Look up a MethodTable by type token.
    /// </summary>
    public static MethodTable* LookupType(uint token)
    {
        fixed (BlockChain* chain = &_typeChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (TypeRegistryEntry*)block->GetEntry(i);
                    if (entry->Token == token)
                        return entry->MT;
                }
                block = block->Next;
            }
        }
        return null;
    }

    /// <summary>
    /// Check if a MethodTable represents a signed primitive type (SByte, Int16, Int32, Int64).
    /// Used by JIT to determine if sign extension is needed when loading values.
    /// </summary>
    public static bool IsSignedPrimitiveType(MethodTable* mt)
    {
        if (mt == null) return false;

        // Compare against known signed primitive MethodTables
        MethodTable* sbyteMT = LookupType(WellKnownTypes.SByte);
        if (sbyteMT != null && mt == sbyteMT) return true;

        MethodTable* int16MT = LookupType(WellKnownTypes.Int16);
        if (int16MT != null && mt == int16MT) return true;

        MethodTable* int32MT = LookupType(WellKnownTypes.Int32);
        if (int32MT != null && mt == int32MT) return true;

        MethodTable* int64MT = LookupType(WellKnownTypes.Int64);
        if (int64MT != null && mt == int64MT) return true;

        // IntPtr on x64 is also signed
        MethodTable* intPtrMT = LookupType(WellKnownTypes.IntPtr);
        if (intPtrMT != null && mt == intPtrMT) return true;

        return false;
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
                // Debug: log TypeDef resolutions for FullTest (assembly 5)
                if (_currentAssemblyId == 5 && (token >> 24) == 0x02)
                {
                    DebugConsole.Write("[MetaInt.ResolveType] token=0x");
                    DebugConsole.WriteHex(token);
                    DebugConsole.Write(" asm=");
                    DebugConsole.WriteDecimal(_currentAssemblyId);
                    DebugConsole.Write(" MT=0x");
                    DebugConsole.WriteHex((ulong)mt);
                    DebugConsole.WriteLine();
                }
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

        // Special handling for TypeSpec: parse the blob to resolve the actual type
        if (tableId == 0x1B)
        {
            MethodTable* resolvedMt = ResolveTypeSpecBlob(token);
            if (resolvedMt != null)
            {
                methodTablePtr = resolvedMt;
                return true;
            }
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
            return false;
        }

        // Get or create an array MethodTable for this element type
        MethodTable* elementMT = (MethodTable*)elementMTPtr;
        MethodTable* arrayMT = AssemblyLoader.GetOrCreateArrayMethodTable(elementMT);

        if (arrayMT == null)
        {
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
        // Extract table ID for debugging
        byte tableId = (byte)(token >> 24);

        // Handle well-known handle types directly - they're all nint wrappers (8 bytes on x64)
        if ((token & 0xFF000000) == 0xF0000000)
        {
            switch (token)
            {
                case WellKnownTypes.RuntimeTypeHandle:
                case WellKnownTypes.RuntimeMethodHandle:
                case WellKnownTypes.RuntimeFieldHandle:
                case WellKnownTypes.RuntimeArgumentHandle:
                    return 8;  // All handle types are nint wrappers
            }
        }

        // Try to resolve the type to get its MethodTable
        void* mtPtr;
        if (!ResolveType(token, out mtPtr) || mtPtr == null)
        {
            // If we can't resolve, fall back to pointer size
            return 8;
        }

        // Get BaseSize from MethodTable
        MethodTable* mt = (MethodTable*)mtPtr;
        uint baseSize = mt->_uBaseSize;

        // For value types, we need the raw struct size (no object header).
        // - Primitive types (Int32, etc.): ComponentSize is set to raw size, BaseSize is boxed size
        // - User-defined structs: ComponentSize is 0, BaseSize is boxed size (raw + 8)
        if (mt->IsValueType)
        {
            // For primitives, ComponentSize is the raw value size
            ushort componentSize = mt->_usComponentSize;
            if (componentSize > 0)
            {
                return componentSize;
            }
            // For user-defined structs, BaseSize includes header, subtract 8 for raw size
            return baseSize >= 8 ? baseSize - 8 : baseSize;
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
    /// For generic types, each instantiation gets its own storage.
    /// </summary>
    public static void* RegisterStaticField(uint fieldToken, uint typeToken, int size, bool isGCRef)
    {
        if (!_initialized)
            Initialize();

        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = GetTypeTypeArgHash();

        // Check if already registered - iterate through blocks
        fixed (BlockChain* chain = &_staticFieldChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (StaticFieldEntry*)block->GetEntry(i);
                    // For generic statics, must match both token AND type arg hash
                    if (entry->Token == fieldToken && entry->TypeArgHash == typeArgHash)
                        return entry->Address;  // Return existing
                }
                block = block->Next;
            }

            // Allocate storage
            void* addr = AllocateStaticStorage(size);
            if (addr == null)
                return null;

            // Add new entry
            StaticFieldEntry newEntry;
            newEntry.Token = fieldToken;
            newEntry.TypeToken = typeToken;
            newEntry.TypeArgHash = typeArgHash;
            newEntry.Address = addr;
            newEntry.Size = size;
            newEntry.IsGCRef = isGCRef;
            if (BlockAllocator.Add(chain, &newEntry) == null)
            {
                DebugConsole.WriteLine("[MetaInt] Static field registry allocation failed");
                return null;
            }

            DebugConsole.Write("[MetaInt] Registered static field 0x");
            DebugConsole.WriteHex(fieldToken);
            if (typeArgHash != 0)
            {
                DebugConsole.Write(" typeArgHash=0x");
                DebugConsole.WriteHex(typeArgHash);
            }
            DebugConsole.Write(" at 0x");
            DebugConsole.WriteHex((ulong)addr);
            DebugConsole.Write(" size ");
            DebugConsole.WriteDecimal((uint)size);
            DebugConsole.WriteLine();

            return addr;
        }
    }

    /// <summary>
    /// Look up a static field's storage address.
    /// For generic types, uses the current type arg context to find the right instantiation.
    /// </summary>
    public static void* LookupStaticField(uint fieldToken)
    {
        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = GetTypeTypeArgHash();

        fixed (BlockChain* chain = &_staticFieldChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (StaticFieldEntry*)block->GetEntry(i);
                    // For generic statics, must match both token AND type arg hash
                    if (entry->Token == fieldToken && entry->TypeArgHash == typeArgHash)
                        return entry->Address;
                }
                block = block->Next;
            }
        }
        return null;
    }

    /// <summary>
    /// Cache a field's layout information for faster subsequent lookups.
    /// Cache key is (token, assemblyId, typeArgHash) to handle generic statics.
    /// </summary>
    public static void CacheFieldLayout(uint token, int offset, byte size, bool isSigned,
                                         bool isStatic, bool isGCRef, void* staticAddress,
                                         bool isDeclaringTypeValueType = false, int declaringTypeSize = 0,
                                         bool isFieldTypeValueType = false,
                                         uint declaringTypeToken = 0, uint declaringTypeAssemblyId = 0)
    {
        if (!_initialized)
            Initialize();

        uint asmId = _currentAssemblyId;
        // Get type arg hash to distinguish generic instantiations.
        // For generic types like Entry<TKey, TValue>, the field layout (size, type) depends
        // on the type arguments. Entry<int, string>.key is 4 bytes, Entry<string, int>.key is 8 bytes.
        // We must include the type arg hash for ALL fields, not just static ones.
        ulong typeArgHash = GetTypeTypeArgHash();

        // Check if already cached - iterate through blocks
        fixed (BlockChain* chain = &_fieldLayoutChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (FieldLayoutEntry*)block->GetEntry(i);
                    // For statics, must also match type arg hash
                    if (entry->Token == token && entry->AssemblyId == asmId && entry->TypeArgHash == typeArgHash)
                    {
                        // Update existing entry
                        entry->Offset = offset;
                        entry->Size = size;
                        entry->IsSigned = isSigned;
                        entry->IsStatic = isStatic;
                        entry->IsGCRef = isGCRef;
                        entry->StaticAddress = staticAddress;
                        entry->IsDeclaringTypeValueType = isDeclaringTypeValueType;
                        entry->DeclaringTypeSize = declaringTypeSize;
                        entry->IsFieldTypeValueType = isFieldTypeValueType;
                        entry->DeclaringTypeToken = declaringTypeToken;
                        entry->DeclaringTypeAssemblyId = declaringTypeAssemblyId;
                        return;
                    }
                }
                block = block->Next;
            }

            // Add new entry
            FieldLayoutEntry newEntry;
            newEntry.Token = token;
            newEntry.AssemblyId = asmId;
            newEntry.TypeArgHash = typeArgHash;
            newEntry.Offset = offset;
            newEntry.Size = size;
            newEntry.IsSigned = isSigned;
            newEntry.IsStatic = isStatic;
            newEntry.IsGCRef = isGCRef;
            newEntry.StaticAddress = staticAddress;
            newEntry.IsDeclaringTypeValueType = isDeclaringTypeValueType;
            newEntry.DeclaringTypeSize = declaringTypeSize;
            newEntry.IsFieldTypeValueType = isFieldTypeValueType;
            newEntry.DeclaringTypeToken = declaringTypeToken;
            newEntry.DeclaringTypeAssemblyId = declaringTypeAssemblyId;
            if (BlockAllocator.Add(chain, &newEntry) == null)
            {
                DebugConsole.WriteLine("[MetaInt] Field layout cache allocation failed");
            }
        }
    }

    /// <summary>
    /// Look up cached field layout.
    /// Matches type arg hash to distinguish generic instantiations (e.g., Entry&lt;int,string&gt; vs Entry&lt;string,int&gt;).
    /// </summary>
    public static bool LookupFieldLayout(uint token, out FieldLayoutEntry entry)
    {
        entry = default;

        // Get type arg hash to distinguish generic instantiations.
        // Entry<int, string>.key has different size than Entry<string, int>.key.
        ulong typeArgHash = GetTypeTypeArgHash();

        // Cache key is (token, assemblyId, typeArgHash) - same token in different assemblies
        // or different generic instantiations refers to different fields
        fixed (BlockChain* chain = &_fieldLayoutChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var e = (FieldLayoutEntry*)block->GetEntry(i);
                    // Must match token, assembly, AND type arg hash
                    if (e->Token == token && e->AssemblyId == _currentAssemblyId && e->TypeArgHash == typeArgHash)
                    {
                        entry = *e;
                        return true;
                    }
                }
                block = block->Next;
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
            case 0x15:  // GENERICINST - generic type instantiation
                // GENERICINST needs type lookup to determine if value/ref type and size
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
            result.DeclaringTypeToken = cached.DeclaringTypeToken;
            result.DeclaringTypeAssemblyId = cached.DeclaringTypeAssemblyId;
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
        // Value types include:
        // - 0x02-0x0D: primitives (Boolean, Char, I1, U1, I2, U2, I4, U4, I8, U8, R4, R8)
        // - 0x0F: Ptr (*T) - pointers are value types (contain value, not reference)
        // - 0x11: ValueType (struct)
        // - 0x18: I (IntPtr/nint)
        // - 0x19: U (UIntPtr/nuint)
        // - 0x1B: FnPtr (function pointer)
        // GENERICINST (0x15) can be either value type or ref type - check the gen kind
        bool fieldIsValueType = (elementType == 0x11) ||
                                 (elementType >= 0x02 && elementType <= 0x0D) ||
                                 (elementType == 0x0F) ||  // Ptr
                                 (elementType == 0x18) ||  // IntPtr
                                 (elementType == 0x19) ||  // UIntPtr
                                 (elementType == 0x1B);    // FnPtr

        // For GENERICINST (0x15), check if it's a value type instantiation
        // Signature: 0x15 <genKind> <typeDefOrRef> <argCount> <args...>
        // genKind: 0x11 = VALUETYPE, 0x12 = CLASS
        if (elementType == 0x15 && sigLen >= 3)
        {
            byte genKind = sig[2];
            fieldIsValueType = (genKind == 0x11);  // VALUETYPE
        }

        // For ELEMENT_TYPE_VAR (0x13), look up the actual type argument from generic context
        // Field signature: 0x06 (calling conv) + 0x13 (VAR) + <var_index>
        // The var_index is a compressed uint indicating which type parameter (T=0, U=1, etc.)
        if (elementType == 0x13 && sigLen >= 3)
        {
            // Read the var index (compressed uint)
            // sig[0] = 0x06 (field calling convention)
            // sig[1] = 0x13 (ELEMENT_TYPE_VAR)
            // sig[2]... = var index (compressed uint)
            byte* sigPtr = sig + 2;  // Skip 0x06 and 0x13
            uint varIndex = MetadataReader.ReadCompressedUInt(ref sigPtr);

            // Get the actual type argument from the current generic context
            MethodTable* typeArgMT = GetTypeTypeArgMethodTable((int)varIndex);
            if (typeArgMT != null)
            {
                fieldIsValueType = typeArgMT->IsValueType;
                isGCRef = !fieldIsValueType;

                // Compute field size:
                // - For value types: _uBaseSize includes MT pointer (8 bytes), subtract it
                // - For reference types: always 8 bytes (pointer size)
                uint fieldSize;
                if (fieldIsValueType)
                {
                    // _uBaseSize = 8 (MT ptr) + actual_value_size
                    fieldSize = typeArgMT->_uBaseSize > 8 ? typeArgMT->_uBaseSize - 8 : typeArgMT->_uBaseSize;
                }
                else
                {
                    fieldSize = 8;  // Reference types are pointers
                }

                DebugConsole.Write("[VAR-FLD] varIdx=");
                DebugConsole.WriteDecimal(varIndex);
                DebugConsole.Write(" MT=0x");
                DebugConsole.WriteHex((ulong)typeArgMT);
                DebugConsole.Write(" baseSize=");
                DebugConsole.WriteDecimal(typeArgMT->_uBaseSize);
                DebugConsole.Write(" fieldSize=");
                DebugConsole.WriteDecimal(fieldSize);
                DebugConsole.Write(" isVT=");
                DebugConsole.Write(fieldIsValueType ? "Y" : "N");
                DebugConsole.WriteLine();

                size = (byte)(fieldSize > 255 ? 255 : fieldSize);

                // Update the result with the resolved type info
                result.IsSigned = false;  // Generic type args don't preserve signedness
                result.IsGCRef = isGCRef;
            }
            else
            {
                DebugConsole.Write("[VAR-FLD] varIdx=");
                DebugConsole.WriteDecimal(varIndex);
                DebugConsole.Write(" typeArgCnt=");
                DebugConsole.WriteDecimal((uint)GetTypeTypeArgCount());
                DebugConsole.WriteLine(" MT=NULL (no type context)");
            }
        }

        result.IsFieldTypeValueType = fieldIsValueType;

        if (size == 0)
        {
            // ValueType or GENERICINST - need to look up actual size from metadata
            if ((elementType == 0x11 || elementType == 0x15) && sigLen >= 3)
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

        // Get the declaring type token for cctor lookup
        uint declaringTypeToken = FindContainingType(rowId);
        uint declaringTypeRow = declaringTypeToken & 0x00FFFFFF;

        if (isStatic)
        {
            // Static field - first check if it has RVA data (array initializers, etc.)
            void* addr = null;

            // Check for FieldRVA entry - used for embedded static data like <PrivateImplementationDetails>
            if (_currentAssemblyId != 0)
            {
                byte* rvaData = AssemblyLoader.GetFieldDataAddress(_currentAssemblyId, rowId);
                if (rvaData != null)
                {
                    // Field has RVA data embedded in PE - use that address directly
                    addr = rvaData;
                }
            }

            // If no RVA data, allocate or look up storage from per-assembly storage
            if (addr == null && _currentAssemblyId != 0)
            {
                var asm = AssemblyLoader.GetAssembly(_currentAssemblyId);
                if (asm != null)
                {
                    addr = asm->Statics.Lookup(token);
                    if (addr == null)
                    {
                        // Allocate in per-assembly storage
                        addr = asm->Statics.Register(token, declaringTypeToken, size, isGCRef);
                    }
                }
            }

            // Fallback to global storage (legacy/AOT compatibility)
            if (addr == null)
            {
                addr = LookupStaticField(token);
                if (addr == null)
                {
                    addr = RegisterStaticField(token, declaringTypeToken, size, isGCRef);
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
            bool isValueType = (declaringTypeRow > 0) && IsTypeDefValueType(declaringTypeRow);

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
            if (isValueType && declaringTypeRow > 0)
            {
                result.DeclaringTypeSize = (int)CalculateTypeDefSize(declaringTypeRow);
            }
        }

        // Set declaring type info for cctor lookup
        result.DeclaringTypeToken = declaringTypeToken;
        result.DeclaringTypeAssemblyId = _currentAssemblyId;

        result.IsValid = true;

        // Cache the result
        CacheFieldLayout(token, result.Offset, result.Size, result.IsSigned,
                        result.IsStatic, result.IsGCRef, result.StaticAddress,
                        result.IsDeclaringTypeValueType, result.DeclaringTypeSize,
                        result.IsFieldTypeValueType,
                        declaringTypeToken, _currentAssemblyId);

        return true;
    }

    /// <summary>
    /// Try to resolve a MemberRef field token via the AOT static field registry.
    /// This handles static fields on AOT types like Boolean.TrueString, IntPtr.Zero, etc.
    /// </summary>
    private static bool TryResolveAotStaticField(uint token, out ResolvedField result)
    {
        result = default;

        if (_metadataRoot == null || _tablesHeader == null || _tableSizes == null)
            return false;

        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get member name and signature
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, rowId);
        uint sigIdx = MetadataReader.GetMemberRefSignature(ref *_tablesHeader, ref *_tableSizes, rowId);

        byte* memberName = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

        if (memberName == null || sig == null || sigLen == 0)
            return false;

        // Check if this is a field signature (FIELD = 0x06)
        if (sig[0] != 0x06)
            return false; // Not a field

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, rowId);

        // Get the type name from the TypeRef or TypeSpec
        byte* typeName = null;
        if (classRef.Table == MetadataTableId.TypeRef)
        {
            uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
            uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

            byte* ns = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);

            typeName = BuildFullTypeName(ns, name);
        }
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - parse to get the underlying generic type definition
            uint typeSpecRow = classRef.RowId;
            if (typeSpecRow > 0 && typeSpecRow <= _tablesHeader->RowCounts[(int)MetadataTableId.TypeSpec])
            {
                uint tsSigIdx = MetadataReader.GetTypeSpecSignature(ref *_tablesHeader, ref *_tableSizes, typeSpecRow);
                byte* tsSig = MetadataReader.GetBlob(ref *_metadataRoot, tsSigIdx, out uint tsSigLen);
                if (tsSig != null && tsSigLen > 0)
                {
                    int tsPos = 0;
                    byte elementType = tsSig[tsPos++];

                    // ELEMENT_TYPE_GENERICINST (0x15) - generic type instantiation
                    if (elementType == 0x15 && tsPos < (int)tsSigLen)
                    {
                        tsPos++; // Skip CLASS (0x12) or VALUETYPE (0x11)

                        // Decode the TypeDefOrRefOrSpec coded index
                        uint codedIndex = 0;
                        byte b1 = tsSig[tsPos++];
                        if ((b1 & 0x80) == 0)
                            codedIndex = b1;
                        else if ((b1 & 0xC0) == 0x80 && tsPos < (int)tsSigLen)
                            codedIndex = (uint)(((b1 & 0x3F) << 8) | tsSig[tsPos++]);
                        else if ((b1 & 0xE0) == 0xC0 && tsPos + 2 < (int)tsSigLen)
                            codedIndex = (uint)(((b1 & 0x1F) << 24) | (tsSig[tsPos++] << 16) | (tsSig[tsPos++] << 8) | tsSig[tsPos++]);

                        uint tableId = codedIndex & 0x03;
                        uint typeRow = codedIndex >> 2;

                        if (tableId == 1 && typeRow > 0) // TypeRef
                        {
                            uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, typeRow);
                            uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, typeRow);
                            byte* ns = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);
                            byte* name = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
                            typeName = BuildFullTypeName(ns, name);
                        }
                    }
                }
            }
        }

        if (typeName == null)
            return false;

        // Convert type name and field name to strings for registry lookup
        string typeNameStr = BytePtrToString(typeName);
        string fieldNameStr = BytePtrToString(memberName);

        if (typeNameStr == null || fieldNameStr == null)
            return false;

        // Check the AOT static field registry
        if (!AotStaticFieldRegistry.Lookup(typeNameStr, fieldNameStr, out AotStaticFieldEntry entry))
            return false;

        // Found in AOT registry - populate result
        result.IsValid = true;
        result.IsStatic = true;
        result.StaticAddress = (void*)entry.Address;
        result.Size = (byte)entry.Size;
        result.IsSigned = entry.IsSigned;
        result.Offset = 0; // Not used for static fields
        result.IsGCRef = entry.Size == 8; // Reference type fields are 8 bytes (pointers)
        result.IsDeclaringTypeValueType = IsKnownValueType(typeNameStr);
        result.DeclaringTypeSize = 0;
        result.IsFieldTypeValueType = !result.IsGCRef;

        DebugConsole.Write("[AOT Static] Resolved ");
        DebugConsole.Write(typeNameStr);
        DebugConsole.Write(".");
        DebugConsole.Write(fieldNameStr);
        DebugConsole.Write(" -> 0x");
        DebugConsole.WriteHex((ulong)entry.Address);
        DebugConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Check if a type name is a known value type.
    /// </summary>
    private static bool IsKnownValueType(string typeName)
    {
        if (typeName == null) return false;
        return typeName == "System.Boolean" ||
               typeName == "System.Byte" ||
               typeName == "System.SByte" ||
               typeName == "System.Int16" ||
               typeName == "System.UInt16" ||
               typeName == "System.Int32" ||
               typeName == "System.UInt32" ||
               typeName == "System.Int64" ||
               typeName == "System.UInt64" ||
               typeName == "System.Single" ||
               typeName == "System.Double" ||
               typeName == "System.Char" ||
               typeName == "System.IntPtr" ||
               typeName == "System.UIntPtr";
    }

    /// <summary>
    /// Resolve a MemberRef field token to field information.
    /// This handles cross-assembly field references.
    /// </summary>
    private static bool ResolveMemberRefField(uint token, out ResolvedField result)
    {
        result = default;

        // First, try the AOT static field registry for excluded types
        if (TryResolveAotStaticField(token, out result))
            return true;

        // Check if this MemberRef is on a generic instantiation (e.g., Dictionary<int, string>.KeyCollection.Enumerator)
        // If so, get the instantiated MethodTable which has the type argument info
        MethodTable* genericInstMT = AssemblyLoader.GetMemberRefGenericInstMT(_currentAssemblyId, token);
        bool hasGenericContext = false;
        int savedTypeArgCount = _typeTypeArgCount;
        MethodTable** savedTypeArgs = stackalloc MethodTable*[4];
        for (int i = 0; i < 4 && i < savedTypeArgCount; i++)
            savedTypeArgs[i] = _typeTypeArgMTs[i];

        if (genericInstMT != null)
        {
            // Set up the type argument context using all type arguments from the cache
            MethodTable** typeArgs = stackalloc MethodTable*[4];
            int typeArgCount;

            if (AssemblyLoader.GetGenericInstTypeArgs(genericInstMT, typeArgs, out typeArgCount) && typeArgCount > 0)
            {
                SetTypeTypeArgs(typeArgs, typeArgCount);
                hasGenericContext = true;

                DebugConsole.Write("[FieldTypeArgs] Set ");
                DebugConsole.WriteDecimal((uint)typeArgCount);
                DebugConsole.Write(" args for field MemberRef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine();
            }
            else if (genericInstMT->_relatedType != null)
            {
                // Fallback: use _relatedType for single type argument (legacy path)
                typeArgs[0] = genericInstMT->_relatedType;
                SetTypeTypeArgs(typeArgs, 1);
                hasGenericContext = true;

                DebugConsole.Write("[FieldTypeArgs] Set legacy MT=0x");
                DebugConsole.WriteHex((ulong)genericInstMT->_relatedType);
                DebugConsole.Write(" for field MemberRef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine();
            }
        }

        // Use AssemblyLoader to resolve the MemberRef to a FieldDef in another assembly
        if (!AssemblyLoader.ResolveMemberRefField(_currentAssemblyId, token,
                                                   out uint fieldToken, out uint targetAsmId))
        {
            DebugConsole.Write("[MetaInt] Failed to resolve MemberRef field 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            // Restore type arg context on failure
            if (hasGenericContext)
            {
                for (int i = 0; i < savedTypeArgCount; i++)
                    _typeTypeArgMTs[i] = savedTypeArgs[i];
                _typeTypeArgCount = savedTypeArgCount;
            }
            return false;
        }

        // Now resolve the FieldDef in the target assembly context
        // Save current context
        uint savedAsmId = _currentAssemblyId;

        // Switch to target assembly context
        SetCurrentAssembly(targetAsmId);

        // Resolve the field in the target assembly
        uint fieldRowId = fieldToken & 0x00FFFFFF;
        bool success = ResolveFieldDef(fieldRowId, fieldToken, out result);

        // Restore original context
        // Must use SetCurrentAssembly to also update MetadataReader's cached root
        SetCurrentAssembly(savedAsmId);

        // Debug: Log MemberRef field resolution
        // if (success)
        // {
        //     DebugConsole.Write("[MemberRefField] asm=");
        //     DebugConsole.WriteDecimal((int)savedAsmId);
        //     DebugConsole.Write(" token=0x");
        //     DebugConsole.WriteHex(token);
        //     DebugConsole.Write(" -> FieldDef=0x");
        //     DebugConsole.WriteHex(fieldToken);
        //     DebugConsole.Write(" offset=");
        //     DebugConsole.WriteDecimal(result.Offset);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal(result.Size);
        //     DebugConsole.WriteLine();
        // }

        // Cache with the original MemberRef token and original assembly ID
        // so subsequent lookups don't need to re-resolve
        if (success)
        {
            CacheFieldLayout(token, result.Offset, (byte)result.Size, result.IsSigned,
                            result.IsStatic, result.IsGCRef, result.StaticAddress,
                            result.IsDeclaringTypeValueType, result.DeclaringTypeSize,
                            result.IsFieldTypeValueType);
        }

        // Restore type arg context
        if (hasGenericContext)
        {
            for (int i = 0; i < savedTypeArgCount; i++)
                _typeTypeArgMTs[i] = savedTypeArgs[i];
            _typeTypeArgCount = savedTypeArgCount;
        }

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
            return false;
        }

        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
        {
            return false;
        }

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, rowId);

        // Get member name and signature
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, rowId);
        uint sigIdx = MetadataReader.GetMemberRefSignature(ref *_tablesHeader, ref *_tableSizes, rowId);

        byte* memberName = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

        if (memberName == null || sig == null || sigLen == 0)
        {
            return false;
        }

        // Check if this is a method signature (NOT 0x06 which is FIELD)
        if (sig[0] == 0x06)
        {
            // It's a field, not a method - silently skip (fields are common)
            return false;
        }

        // Get the type name from the TypeRef or TypeSpec
        byte* typeName = null;
        bool firstGenericArgIsRefType = false;  // Track if first generic type arg is reference type
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
        else if (classRef.Table == MetadataTableId.TypeSpec)
        {
            // TypeSpec - parse to get the underlying generic type definition
            uint typeSpecRow = classRef.RowId;
            if (typeSpecRow > 0 && typeSpecRow <= _tablesHeader->RowCounts[(int)MetadataTableId.TypeSpec])
            {
                uint tsSigIdx = MetadataReader.GetTypeSpecSignature(ref *_tablesHeader, ref *_tableSizes, typeSpecRow);
                byte* tsSig = MetadataReader.GetBlob(ref *_metadataRoot, tsSigIdx, out uint tsSigLen);
                if (tsSig != null && tsSigLen > 0)
                {
                    int tsPos = 0;
                    byte elementType = tsSig[tsPos++];

                    // ELEMENT_TYPE_GENERICINST (0x15) - generic type instantiation
                    if (elementType == 0x15 && tsPos < (int)tsSigLen)
                    {
                        // Skip CLASS (0x12) or VALUETYPE (0x11)
                        tsPos++;

                        // Decode the TypeDefOrRefOrSpec coded index
                        uint codedIndex = 0;
                        byte b1 = tsSig[tsPos++];
                        if ((b1 & 0x80) == 0)
                            codedIndex = b1;
                        else if ((b1 & 0xC0) == 0x80 && tsPos < (int)tsSigLen)
                            codedIndex = (uint)(((b1 & 0x3F) << 8) | tsSig[tsPos++]);
                        else if ((b1 & 0xE0) == 0xC0 && tsPos + 2 < (int)tsSigLen)
                            codedIndex = (uint)(((b1 & 0x1F) << 24) | (tsSig[tsPos++] << 16) | (tsSig[tsPos++] << 8) | tsSig[tsPos++]);

                        // TypeDefOrRefOrSpec: bits 0-1 = table (0=TypeDef, 1=TypeRef, 2=TypeSpec)
                        uint tableId = codedIndex & 0x03;
                        uint typeRow = codedIndex >> 2;

                        if (tableId == 1 && typeRow > 0) // TypeRef
                        {
                            uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, typeRow);
                            uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, typeRow);
                            byte* ns = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);
                            byte* name = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
                            typeName = BuildFullTypeName(ns, name);

                            // Parse generic argument count and first type argument
                            if (tsPos < (int)tsSigLen)
                            {
                                // Decode argCount (compressed integer)
                                uint argCount = 0;
                                byte ac = tsSig[tsPos++];
                                if ((ac & 0x80) == 0)
                                    argCount = ac;
                                else if ((ac & 0xC0) == 0x80 && tsPos < (int)tsSigLen)
                                    argCount = (uint)(((ac & 0x3F) << 8) | tsSig[tsPos++]);

                                // Parse first type argument to check if reference type
                                if (argCount > 0 && tsPos < (int)tsSigLen)
                                {
                                    byte argElemType = tsSig[tsPos];
                                    // Reference types: CLASS(0x12), STRING(0x0E), OBJECT(0x1C), SZARRAY(0x1D), ARRAY(0x14)
                                    // Value types: VALUETYPE(0x11), I1-I8, U1-U8, R4, R8, BOOLEAN, CHAR, etc.
                                    firstGenericArgIsRefType =
                                        argElemType == 0x12 || // CLASS
                                        argElemType == 0x0E || // STRING
                                        argElemType == 0x1C || // OBJECT
                                        argElemType == 0x1D || // SZARRAY
                                        argElemType == 0x14;   // ARRAY
                                }
                            }
                        }
                    }
                }
            }
        }

        if (typeName == null)
        {
            return false;
        }

        // Check if this is a well-known AOT type
        bool isWellKnown = AotMethodRegistry.IsWellKnownAotType(typeName);

        if (!isWellKnown)
        {
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
        DebugConsole.Write("[AotMemberRef] Looking up: ");
        WriteByteString(typeName);
        DebugConsole.Write(".");
        WriteByteString(memberName);
        DebugConsole.Write(" args=");
        DebugConsole.WriteDecimal(paramCount);
        DebugConsole.WriteLine();

        // Special handling for ReadOnlyCollection<T>.get_Item - only use AOT for reference types
        // The AOT helper uses 8-byte pointer indexing which only works for reference type arrays
        if (NameEquals(typeName, "System.Collections.ObjectModel.ReadOnlyCollection`1") &&
            NameEquals(memberName, "get_Item") && !firstGenericArgIsRefType)
        {
            DebugConsole.WriteLine("[AotMemberRef] ReadOnlyCollection.get_Item: T is value type, skip AOT");
            return false;  // Fall through to JIT for value types
        }

        // Special handling for String constructors - need to distinguish char* from char[]
        // For .ctor with 3 params, check if first param is pointer (char*) vs array (char[])
        bool useCharPtrVariant = false;
        if (NameEquals(typeName, "System.String") && NameEquals(memberName, ".ctor") && paramCount == 3)
        {
            // Parse signature to check first parameter type
            // sig format: calling_conv | param_count | return_type | param_types...
            // pos 0: calling convention
            // pos 1: param count (compressed)
            // pos 2+: return type (void = 0x01), then param types
            // Debug: dump first 6 bytes of signature
            DebugConsole.Write("[AotMemberRef] String..ctor sig: ");
            for (int d = 0; d < 6 && d < sigLen; d++)
            {
                DebugConsole.WriteHex(sig[d]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();

            int pos = sigPos;  // Continue from where param count was decoded
            // Skip return type - void is 0x01
            pos++;
            // Now at first param type - check for pointer (0x0F = ELEMENT_TYPE_PTR)
            DebugConsole.Write("[AotMemberRef] First param type at pos ");
            DebugConsole.WriteDecimal((uint)pos);
            DebugConsole.Write(" = 0x");
            DebugConsole.WriteHex(sig[pos]);
            DebugConsole.WriteLine();

            if (sig[pos] == 0x0F)  // PTR
            {
                useCharPtrVariant = true;
                DebugConsole.WriteLine("[AotMemberRef] String..ctor detected char* variant");
            }
        }

        // Special handling for AggregateException constructors with 2 params
        // Need to distinguish (string, Exception[]) from (string, List<Exception>) from (string, Exception)
        bool useListVariant = false;
        bool useSingleExceptionVariant = false;
        if (NameEquals(typeName, "System.AggregateException") && NameEquals(memberName, ".ctor") && paramCount == 2)
        {
            int pos = sigPos;  // Continue from where param count was decoded
            // Skip return type
            pos++;
            // Skip first param (string)
            if (sig[pos] == 0x0E)  // ELEMENT_TYPE_STRING
                pos++;
            else if (sig[pos] == 0x12)  // ELEMENT_TYPE_CLASS
            {
                pos++;
                // Skip compressed TypeRef/TypeDef token
                while ((sig[pos] & 0x80) != 0) pos++;
                pos++;
            }
            // Now at second param - check type:
            // 0x1D = SZARRAY (Exception[]) - default array variant
            // 0x15 = GENERICINST (List<Exception>) - list variant
            // 0x12 = CLASS (single Exception) - single exception variant
            DebugConsole.Write("[AotMemberRef] AggregateException..ctor second param type = 0x");
            DebugConsole.WriteHex(sig[pos]);
            DebugConsole.WriteLine();
            if (sig[pos] == 0x15)  // GENERICINST - List<Exception>
            {
                useListVariant = true;
                DebugConsole.WriteLine("[AotMemberRef] AggregateException..ctor detected List variant");
            }
            else if (sig[pos] == 0x12)  // CLASS - single Exception
            {
                useSingleExceptionVariant = true;
                DebugConsole.WriteLine("[AotMemberRef] AggregateException..ctor detected single Exception variant");
            }
            // else 0x1D = SZARRAY - array variant (default)
        }

        // Compute signature hash from the IL signature blob for proper overload resolution
        ulong signatureHash = AotMethodRegistry.ComputeSignatureHashFromBlob(sig, (int)sigLen);

        // For methods that use variant names (constructors with special overloads),
        // use the legacy lookup with variant flags instead of signature hash.
        // This is because those methods are registered with variant names like ".ctor$single"
        // rather than with signature hashes.
        bool useVariantLookup = useCharPtrVariant || useListVariant || useSingleExceptionVariant;

        bool found;
        AotMethodEntry entry;
        if (useVariantLookup)
        {
            // Use legacy lookup with variant flags for constructors with special overloads
            found = AotMethodRegistry.TryLookup(typeName, memberName, paramCount, out entry, useCharPtrVariant, useListVariant, useSingleExceptionVariant);
        }
        else
        {
            // Try lookup with signature hash first (for methods with overloads like String.Replace)
            found = AotMethodRegistry.TryLookupWithSignature(typeName, memberName, paramCount, signatureHash, out entry);

            // Fall back to legacy lookup if signature hash didn't match
            if (!found)
            {
                found = AotMethodRegistry.TryLookup(typeName, memberName, paramCount, out entry, false, false, false);
            }
        }

        if (found)
        {
            DebugConsole.Write("[AotMemberRef] Found AOT method: ");
            WriteByteString(typeName);
            DebugConsole.Write(".");
            WriteByteString(memberName);
            DebugConsole.Write(" -> 0x");
            DebugConsole.WriteHex((ulong)entry.NativeCode);
            DebugConsole.WriteLine();

            result.NativeCode = (void*)entry.NativeCode;
            result.ArgCount = (byte)entry.ArgCount;
            result.ReturnKind = entry.ReturnKind;
            result.ReturnStructSize = entry.ReturnStructSize;
            result.HasThis = entry.HasThis;
            result.IsValid = true;
            result.IsVirtual = entry.IsVirtual;
            // Determine vtable slot for virtual Object methods
            // Slot 0: ToString, Slot 1: Equals, Slot 2: GetHashCode
            if (entry.IsVirtual && IsObjectMethod(typeName))
            {
                result.VtableSlot = GetObjectMethodVtableSlot(memberName);
            }
            else
            {
                result.VtableSlot = -1;  // Not a vtable method
            }
            // For constructors, we need to provide the MethodTable so that newobj can allocate
            // For value types: allocate stack space
            // For reference types: allocate heap object via RhpNewFast
            result.MethodTable = GetMethodTableForTypeName(typeName);
            result.IsInterfaceMethod = false;
            result.InterfaceMT = null;
            result.InterfaceMethodSlot = 0;
            result.RegistryEntry = null;
            return true;
        }

        // No AOT method found - caller will fall through to JIT compilation
        // This is expected for generic instantiations and methods not in AOT registry
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
    /// Convert a null-terminated UTF-8 byte pointer to a managed string.
    /// </summary>
    private static string BytePtrToString(byte* ptr)
    {
        if (ptr == null)
            return null!;

        int len = 0;
        while (ptr[len] != 0)
            len++;

        if (len == 0)
            return string.Empty;

        char* chars = stackalloc char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)ptr[i];

        return new string(chars, 0, len);
    }

    /// <summary>
    /// Try to resolve a MemberRef as an MD array method (Get, Set, Address, .ctor).
    /// MD array methods are synthetic - they have no IL body and are handled by the JIT.
    /// </summary>
    private static bool TryResolveMDArrayMemberRef(uint token, out ResolvedMethod result)
    {
        result = default;

        if (_metadataRoot == null || _tablesHeader == null || _tableSizes == null)
            return false;

        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Get the Class coded index (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, rowId);

        // MD array methods have their class as a TypeSpec (not TypeRef)
        if (classRef.Table != MetadataTableId.TypeSpec)
            return false;

        // Get the TypeSpec blob
        uint typeSpecSigIdx = MetadataReader.GetTypeSpecSignature(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        byte* typeSpecBlob = MetadataReader.GetBlob(ref *_metadataRoot, typeSpecSigIdx, out uint typeSpecLen);

        if (typeSpecBlob == null || typeSpecLen < 2)
            return false;

        // Check if it's ELEMENT_TYPE_ARRAY (0x14) - multi-dimensional array
        if (typeSpecBlob[0] != 0x14)
            return false;

        // Parse the MD array type to get rank and element type
        byte* ptr = typeSpecBlob + 1;
        byte* end = typeSpecBlob + typeSpecLen;

        // Resolve element type
        MethodTable* elemMt = ResolveTypeSigToMethodTable(ref ptr, end);
        if (elemMt == null)
        {
            DebugConsole.WriteLine("[MetaInt] MD array MemberRef: failed to resolve element type");
            return false;
        }

        // Read rank
        uint rank = MetadataReader.ReadCompressedUInt(ref ptr);
        if (rank < 2 || rank > 32)
            return false;

        // Get member name to determine method kind
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, rowId);
        byte* memberName = MetadataReader.GetString(ref *_metadataRoot, nameIdx);

        if (memberName == null)
            return false;

        // Determine method kind from name
        MDArrayMethodKind kind = MDArrayMethodKind.None;
        if (ByteStringEquals(memberName, ".ctor"))
        {
            kind = MDArrayMethodKind.Ctor;
        }
        else if (ByteStringEquals(memberName, "Get"))
        {
            kind = MDArrayMethodKind.Get;
        }
        else if (ByteStringEquals(memberName, "Set"))
        {
            kind = MDArrayMethodKind.Set;
        }
        else if (ByteStringEquals(memberName, "Address"))
        {
            kind = MDArrayMethodKind.Address;
        }

        if (kind == MDArrayMethodKind.None)
            return false;

        // Get element size - for value types, use raw size (BaseSize includes header)
        ushort elemSize;
        if (elemMt->IsValueType)
        {
            // Use ComponentSize if available (AOT primitives), otherwise BaseSize - 8
            elemSize = elemMt->_usComponentSize > 0 ? elemMt->_usComponentSize :
                       (ushort)(elemMt->BaseSize >= 8 ? elemMt->BaseSize - 8 : elemMt->BaseSize);
        }
        else
        {
            elemSize = 8;  // Reference types are pointers
        }

        // Get or create the MD array MethodTable
        MethodTable* arrayMT = AssemblyLoader.GetOrCreateMDArrayMethodTable(elemMt, (int)rank);

        // Fill in the result
        result.IsValid = true;
        result.IsMDArrayMethod = true;
        result.MDArrayKind = kind;
        result.MDArrayRank = (byte)rank;
        result.MDArrayElemSize = elemSize;
        result.MethodTable = arrayMT;
        result.NativeCode = null;  // Handled inline by JIT
        result.HasThis = true;  // Array methods are instance methods (array is 'this')

        // Set ArgCount based on method kind
        // .ctor: rank args (dimensions)
        // Get: rank args (indices)
        // Set: rank args (indices) + 1 (value)
        // Address: rank args (indices)
        switch (kind)
        {
            case MDArrayMethodKind.Ctor:
            case MDArrayMethodKind.Get:
            case MDArrayMethodKind.Address:
                result.ArgCount = (byte)rank;
                break;
            case MDArrayMethodKind.Set:
                result.ArgCount = (byte)(rank + 1);  // indices + value
                break;
        }

        // Set return type
        switch (kind)
        {
            case MDArrayMethodKind.Ctor:
                result.ReturnKind = ReturnKind.Void;
                break;
            case MDArrayMethodKind.Get:
                // Return type depends on element type
                if (elemSize <= 8)
                    result.ReturnKind = ReturnKind.IntPtr;  // Primitive or reference
                else
                {
                    result.ReturnKind = ReturnKind.Struct;
                    result.ReturnStructSize = elemSize;
                }
                break;
            case MDArrayMethodKind.Set:
                result.ReturnKind = ReturnKind.Void;
                break;
            case MDArrayMethodKind.Address:
                result.ReturnKind = ReturnKind.IntPtr;  // Returns pointer
                break;
        }

        DebugConsole.Write("[MetaInt] MD array method: rank=");
        DebugConsole.WriteDecimal(rank);
        DebugConsole.Write(" kind=");
        DebugConsole.WriteDecimal((uint)kind);
        DebugConsole.Write(" elemSize=");
        DebugConsole.WriteDecimal(elemSize);
        DebugConsole.WriteLine();

        return true;
    }

    /// <summary>
    /// Try to resolve a korlib MethodDef token to AOT native code.
    /// When JIT compiling a korlib.dll method that calls another korlib method,
    /// the target method is AOT-compiled and needs to be resolved via the AOT registry
    /// instead of being JIT compiled.
    /// </summary>
    private static bool TryResolveKorlibAotMethodDef(uint token, out ResolvedMethod result)
    {
        result = default;

        // Only applies to korlib assembly
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(_currentAssemblyId);
        if (asm == null || !asm->IsCoreLib)
            return false;

        uint methodRow = token & 0x00FFFFFF;
        if (methodRow == 0)
            return false;

        // Find which TypeDef owns this MethodDef
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
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeDefName(ref asm->Tables, ref asm->Sizes, ownerTypeRow);
        uint typeNsIdx = MetadataReader.GetTypeDefNamespace(ref asm->Tables, ref asm->Sizes, ownerTypeRow);
        byte* typeName = MetadataReader.GetString(ref asm->Metadata, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref asm->Metadata, typeNsIdx);

        // Build full type name
        byte* fullTypeName = BuildFullTypeName(typeNs, typeName);
        if (fullTypeName == null)
            return false;

        // Check if this is a well-known AOT type
        if (!AotMethodRegistry.IsWellKnownAotType(fullTypeName))
            return false;

        // Get method name
        uint methodNameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* methodName = MetadataReader.GetString(ref asm->Metadata, methodNameIdx);
        if (methodName == null)
            return false;

        // Get method signature to determine parameter count
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRow);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 2)
            return false;

        // Parse param count from signature
        int sigPos = 1;  // Skip calling convention byte
        byte paramCount = 0;
        byte b = sig[sigPos];
        if ((b & 0x80) == 0)
            paramCount = b;
        else if ((b & 0xC0) == 0x80)
            paramCount = (byte)(((b & 0x3F) << 8) | sig[sigPos + 1]);

        // Try to look up in AOT registry
        if (AotMethodRegistry.TryLookup(fullTypeName, methodName, paramCount, out AotMethodEntry entry))
        {
            DebugConsole.Write("[KorlibMethodDef] AOT resolved: ");
            WriteByteString(fullTypeName);
            DebugConsole.Write(".");
            WriteByteString(methodName);
            DebugConsole.Write(" -> 0x");
            DebugConsole.WriteHex((ulong)entry.NativeCode);
            DebugConsole.WriteLine();

            result.NativeCode = (void*)entry.NativeCode;
            result.ArgCount = (byte)entry.ArgCount;
            result.ReturnKind = entry.ReturnKind;
            result.ReturnStructSize = entry.ReturnStructSize;
            result.HasThis = entry.HasThis;
            result.IsValid = true;
            result.IsVirtual = entry.IsVirtual;
            result.VtableSlot = -1;
            result.MethodTable = null;
            result.IsInterfaceMethod = false;
            result.InterfaceMT = null;
            result.InterfaceMethodSlot = 0;
            result.RegistryEntry = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Compare a null-terminated byte string to a literal.
    /// </summary>
    private static bool ByteStringEquals(byte* str, string literal)
    {
        if (str == null)
            return false;

        int i = 0;
        while (i < literal.Length && str[i] != 0)
        {
            if (str[i] != (byte)literal[i])
                return false;
            i++;
        }

        // Both must end at same position
        return i == literal.Length && str[i] == 0;
    }

    /// <summary>
    /// Resolve a MemberRef method token to method call information.
    /// This handles cross-assembly method references.
    /// </summary>
    private static bool ResolveMemberRefMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        // Check for RuntimeHelpers.InitializeArray - handle as JIT intrinsic
        uint memberRefRowId = token & 0x00FFFFFF;
        if (IsRuntimeHelpersInitializeArrayMemberRef(memberRefRowId))
        {
            DebugConsole.WriteLine("[MetaInt] Detected RuntimeHelpers.InitializeArray - handling as JIT intrinsic");
            result.IsValid = true;
            result.IsInitializeArray = true;
            result.NativeCode = null;  // Handled inline by JIT
            result.HasThis = false;    // Static method
            result.ArgCount = 2;       // (Array array, RuntimeFieldHandle fldHandle)
            result.ReturnKind = ReturnKind.Void;
            result.IsVirtual = false;
            result.VtableSlot = -1;
            result.MethodTable = null;
            result.RegistryEntry = null;
            return true;
        }

        // First, try to resolve via the AOT method registry for well-known types like String
        if (TryResolveAotMemberRef(token, out result))
        {
            return true;
        }

        // Check if this MemberRef is on an MD array type (Get, Set, Address, .ctor)
        if (TryResolveMDArrayMemberRef(token, out result))
        {
            return true;
        }

        // Check if this MemberRef is an interface method call
        // If so, we don't try to JIT compile the abstract method - instead we return
        // interface dispatch info so callvirt can resolve at runtime
        MethodTable* interfaceMT;
        short interfaceSlot;
        bool isIface = AssemblyLoader.IsInterfaceMethod(_currentAssemblyId, token, out interfaceMT, out interfaceSlot);
        // Debug: trace interface method check for get_Item
        if (_tablesHeader != null && _tableSizes != null && _metadataRoot != null)
        {
            uint rowId = token & 0x00FFFFFF;
            uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, rowId);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
            if (name != null && name[0] == 'g' && name[1] == 'e' && name[2] == 't' && name[3] == '_')
            {
                DebugConsole.Write("[MemberRef] IsIface tok=0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" = ");
                DebugConsole.Write(isIface ? "Y" : "N");
                DebugConsole.Write(" MT=0x");
                DebugConsole.WriteHex((ulong)interfaceMT);
                DebugConsole.WriteLine();
            }
        }
        if (isIface)
        {
            result.IsValid = true;
            result.IsInterfaceMethod = true;
            result.InterfaceMT = interfaceMT;
            result.InterfaceMethodSlot = interfaceSlot;
            result.NativeCode = null;  // Will be resolved at runtime via interface dispatch
            result.HasThis = true;  // Interface methods are always instance methods
            result.ArgCount = 0;  // Will be parsed from MemberRef signature
            result.ReturnKind = ReturnKind.IntPtr;  // Generic fallback, actual return handled by call
            result.IsVirtual = true;
            result.VtableSlot = -1;  // Not a direct vtable slot
            result.MethodTable = null;
            result.RegistryEntry = null;

            // Parse the MemberRef signature to get arg count and return type
            // This is needed for proper interface method invocation
            ParseMemberRefSignatureForDelegate(_currentAssemblyId, token, ref result);

            return true;
        }

        // Check if this MemberRef is a delegate Invoke method
        // Delegate Invoke methods have "runtime managed" attribute - no IL body
        // We return delegate invoke info so callvirt can generate runtime dispatch code
        MethodTable* delegateMT;
        if (AssemblyLoader.IsDelegateInvoke(_currentAssemblyId, token, out delegateMT))
        {
            result.IsValid = true;
            result.IsDelegateInvoke = true;
            result.MethodTable = delegateMT;
            result.NativeCode = null;  // No native code - runtime dispatched
            result.HasThis = true;  // Invoke is always instance method
            result.ArgCount = 0;  // Will be parsed from MemberRef signature
            result.ReturnKind = ReturnKind.IntPtr;  // Generic fallback
            result.IsVirtual = false;
            result.VtableSlot = -1;
            result.IsInterfaceMethod = false;
            result.InterfaceMT = null;
            result.InterfaceMethodSlot = -1;
            result.RegistryEntry = null;

            // Parse the MemberRef signature to get arg count and return type
            // This is needed for proper delegate invocation code generation
            ParseMemberRefSignatureForDelegate(_currentAssemblyId, token, ref result);

            return true;
        }

        // Check if this MemberRef is a delegate constructor (.ctor on a delegate type)
        // Delegate constructors are "runtime managed" with no IL body - the JIT handles them specially
        MethodTable* delegateCtorMT;
        if (AssemblyLoader.IsDelegateCtor(_currentAssemblyId, token, out delegateCtorMT))
        {
            result.IsValid = true;
            result.IsDelegateCtor = true;
            result.MethodTable = delegateCtorMT;
            result.NativeCode = null;  // No native code - handled by CompileNewobjDelegate
            result.HasThis = true;  // Constructor is instance method
            result.ArgCount = 2;    // Delegate .ctor takes (object, IntPtr)
            result.ReturnKind = ReturnKind.Void;
            result.IsVirtual = false;
            result.VtableSlot = -1;
            result.IsInterfaceMethod = false;
            result.InterfaceMT = null;
            result.InterfaceMethodSlot = -1;
            result.RegistryEntry = null;

            return true;
        }

        // Check if this MemberRef is on a generic instantiation (e.g., SimpleList<int>)
        // If so, get the instantiated MethodTable which has the type argument info
        MethodTable* genericInstMT = AssemblyLoader.GetMemberRefGenericInstMT(_currentAssemblyId, token);
        bool hasGenericContext = false;
        int savedTypeArgCount = _typeTypeArgCount;
        MethodTable** savedTypeArgs = stackalloc MethodTable*[4];
        for (int i = 0; i < 4 && i < savedTypeArgCount; i++)
            savedTypeArgs[i] = _typeTypeArgMTs[i];

        if (genericInstMT != null)
        {
            // Set up the type argument context using all type arguments from the cache
            MethodTable** typeArgs = stackalloc MethodTable*[4];
            int typeArgCount;

            if (AssemblyLoader.GetGenericInstTypeArgs(genericInstMT, typeArgs, out typeArgCount) && typeArgCount > 0)
            {
                SetTypeTypeArgs(typeArgs, typeArgCount);
                hasGenericContext = true;

                DebugConsole.Write("[MetaInt] Set type arg context: ");
                DebugConsole.WriteDecimal((uint)typeArgCount);
                DebugConsole.Write(" args for MemberRef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine();
            }
            else if (genericInstMT->_relatedType != null)
            {
                // Fallback: use _relatedType for single type argument (legacy path)
                typeArgs[0] = genericInstMT->_relatedType;
                SetTypeTypeArgs(typeArgs, 1);
                hasGenericContext = true;

                DebugConsole.Write("[MetaInt] Set type arg context (legacy): MT=0x");
                DebugConsole.WriteHex((ulong)genericInstMT->_relatedType);
                DebugConsole.Write(" for MemberRef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine();
            }
        }

        // Fall back to JIT assembly resolution
        if (!AssemblyLoader.ResolveMemberRefMethod(_currentAssemblyId, token,
                                                    out uint methodToken, out uint targetAsmId))
        {
            DebugConsole.Write("[MetaInt] Failed to resolve MemberRef method 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            if (hasGenericContext)
            {
                // Restore type arg context on failure
                for (int i = 0; i < savedTypeArgCount; i++)
                    _typeTypeArgMTs[i] = savedTypeArgs[i];
                _typeTypeArgCount = savedTypeArgCount;
            }
            return false;
        }

        // Check for synthetic AOT method token (0xFA prefix)
        // These are well-known korlib methods looked up via AotMethodRegistry
        if ((methodToken & 0xFF000000) == 0xFA000000)
        {
            // The lower 24 bits don't contain the address - we need to look up again
            // to get the full AotMethodEntry with all info
            // Actually we can get the info directly from the MemberRef name
            LoadedAssembly* srcAsm = AssemblyLoader.GetAssembly(_currentAssemblyId);
            if (srcAsm != null)
            {
                uint rowId = token & 0x00FFFFFF;
                uint nameIdx = MetadataReader.GetMemberRefName(ref srcAsm->Tables, ref srcAsm->Sizes, rowId);
                uint sigIdx = MetadataReader.GetMemberRefSignature(ref srcAsm->Tables, ref srcAsm->Sizes, rowId);
                byte* memberName = MetadataReader.GetString(ref srcAsm->Metadata, nameIdx);
                byte* sig = MetadataReader.GetBlob(ref srcAsm->Metadata, sigIdx, out uint sigLen);

                // Get type name from the class reference
                CodedIndex classRef = MetadataReader.GetMemberRefClass(ref srcAsm->Tables, ref srcAsm->Sizes, rowId);
                if (classRef.Table == MetadataTableId.TypeRef)
                {
                    uint typeRefNameIdx = MetadataReader.GetTypeRefName(ref srcAsm->Tables, ref srcAsm->Sizes, classRef.RowId);
                    uint typeRefNsIdx = MetadataReader.GetTypeRefNamespace(ref srcAsm->Tables, ref srcAsm->Sizes, classRef.RowId);
                    byte* typeName = MetadataReader.GetString(ref srcAsm->Metadata, typeRefNameIdx);
                    byte* typeNs = MetadataReader.GetString(ref srcAsm->Metadata, typeRefNsIdx);

                    // Build full type name
                    byte* fullTypeName = stackalloc byte[64];
                    int pos = 0;
                    for (int i = 0; typeNs != null && typeNs[i] != 0 && pos < 48; i++)
                        fullTypeName[pos++] = typeNs[i];
                    fullTypeName[pos++] = (byte)'.';
                    for (int i = 0; typeName != null && typeName[i] != 0 && pos < 62; i++)
                        fullTypeName[pos++] = typeName[i];
                    fullTypeName[pos] = 0;

                    byte argCount = (sig != null && sigLen > 1) ? sig[1] : (byte)0;

                    if (AotMethodRegistry.TryLookup(fullTypeName, memberName, argCount, out AotMethodEntry entry))
                    {
                        result.NativeCode = (void*)entry.NativeCode;
                        result.ArgCount = (byte)entry.ArgCount;
                        result.ReturnKind = entry.ReturnKind;
                        result.ReturnStructSize = entry.ReturnStructSize;
                        result.HasThis = entry.HasThis;
                        result.IsValid = true;
                        result.IsVirtual = entry.IsVirtual;
                        result.VtableSlot = -1;
                        result.MethodTable = null;
                        result.IsInterfaceMethod = false;
                        result.InterfaceMT = null;
                        result.InterfaceMethodSlot = -1;
                        result.RegistryEntry = null;

                        if (hasGenericContext)
                        {
                            for (int i = 0; i < savedTypeArgCount; i++)
                                _typeTypeArgMTs[i] = savedTypeArgs[i];
                            _typeTypeArgCount = savedTypeArgCount;
                        }
                        return true;
                    }
                }
            }
            // AOT lookup failed
            if (hasGenericContext)
            {
                for (int i = 0; i < savedTypeArgCount; i++)
                    _typeTypeArgMTs[i] = savedTypeArgs[i];
                _typeTypeArgCount = savedTypeArgCount;
            }
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

        // Debug: Trace get_Item resolution
        if (_tablesHeader != null && _tableSizes != null && _metadataRoot != null)
        {
            uint nameIdx = MetadataReader.GetMethodDefName(ref *_tablesHeader, ref *_tableSizes, methodRowId);
            byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
            if (name != null && name[0] == 'g' && name[1] == 'e' && name[2] == 't' && name[3] == '_')
            {
                DebugConsole.Write("[ResolveMDef] get_* method=0x");
                DebugConsole.WriteHex(methodToken);
                DebugConsole.Write(" asm=");
                DebugConsole.WriteDecimal(targetAsmId);
                DebugConsole.Write(" info=");
                DebugConsole.WriteHex((ulong)info);
                if (info != null)
                {
                    DebugConsole.Write(" isComp=");
                    DebugConsole.Write(info->IsCompiled ? "Y" : "N");
                    DebugConsole.Write(" isVirt=");
                    DebugConsole.Write(info->IsVirtual ? "Y" : "N");
                    DebugConsole.Write(" slot=");
                    DebugConsole.WriteDecimal((uint)(ushort)info->VtableSlot);
                }
                DebugConsole.WriteLine();
            }
        }

        if (info != null && info->IsCompiled)
        {
            // Method already compiled
            result.NativeCode = info->NativeCode;
            result.ArgCount = info->ArgCount;
            result.ReturnKind = info->ReturnKind;
            result.ReturnStructSize = info->ReturnStructSize;
            result.HasThis = info->HasThis;
            result.IsValid = true;
            result.IsVirtual = info->IsVirtual;
            result.VtableSlot = info->VtableSlot;
            result.MethodTable = info->MethodTable;
            result.IsInterfaceMethod = info->IsInterfaceMethod;
            result.InterfaceMT = info->InterfaceMT;
            result.InterfaceMethodSlot = info->InterfaceMethodSlot;
            result.RegistryEntry = info;
            TraceMemberRef(methodToken, targetAsmId, info->ReturnKind, info->ArgCount);

            // Devirtualization for high vtable slots (>=3) when native code is available.
            // NativeAOT optimizes vtables and may not include all virtual slot entries.
            // Slots 0-2 are standard Object virtuals (ToString, Equals, GetHashCode).
            // For higher slots, prefer direct call to avoid vtable out-of-bounds issues
            // with generic instantiations where vtable slots may not exist at runtime.
            //
            // KNOWN LIMITATION: This prevents polymorphism for sealed class overrides
            // called through base class references. JIT-only types with proper vtable
            // setup would work correctly with vtable dispatch, but distinguishing them
            // from AOT types with optimized vtables is complex.
            if (result.IsVirtual && result.NativeCode != null && result.VtableSlot >= 3)
            {
                // Use direct call instead of vtable dispatch
                result.IsVirtual = false;
                result.VtableSlot = -1;
            }

            success = true;
        }
        else
        {
            // Method not compiled - check if it's abstract (can't be JIT compiled)
            // Abstract methods need vtable dispatch - calculate the slot from metadata
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref *_tablesHeader, ref *_tableSizes, methodRowId);
            bool isAbstract = (methodFlags & MethodDefFlags.Abstract) != 0;
            bool isVirtual = (methodFlags & MethodDefFlags.Virtual) != 0;

            if (isAbstract && isVirtual)
            {
                // Abstract virtual method - calculate vtable slot for runtime dispatch
                short vtableSlot = CalculateVtableSlotForMethod(targetAsmId, methodRowId);
                if (vtableSlot >= 0)
                {
                    // Get signature info for the abstract method
                    uint sigIdx = MetadataReader.GetMethodDefSignature(ref *_tablesHeader, ref *_tableSizes, methodRowId);
                    byte* sigBlob = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);

                    byte argCount = 0;
                    bool hasThis = true;  // Instance method
                    ReturnKind retKind = ReturnKind.IntPtr;  // Default

                    if (sigBlob != null && sigLen > 0)
                    {
                        int sigPos = 0;
                        byte callConv = sigBlob[sigPos++];
                        hasThis = (callConv & 0x20) != 0;

                        // Read generic param count if present
                        if ((callConv & 0x10) != 0)
                        {
                            byte* sigPtr = sigBlob + sigPos;
                            MetadataReader.ReadCompressedUInt(ref sigPtr);
                            sigPos = (int)(sigPtr - sigBlob);
                        }

                        // Decode parameter count
                        uint paramCount = 0;
                        byte b = sigBlob[sigPos++];
                        if ((b & 0x80) == 0)
                            paramCount = b;
                        else if ((b & 0xC0) == 0x80)
                            paramCount = (uint)(((b & 0x3F) << 8) | sigBlob[sigPos++]);
                        argCount = (byte)paramCount;

                        byte retType = sigBlob[sigPos];
                        retKind = (retType == 0x01) ? ReturnKind.Void : ReturnKind.IntPtr;
                    }

                    result.NativeCode = null;  // No code - will be resolved via vtable dispatch
                    result.ArgCount = argCount;
                    result.ReturnKind = retKind;
                    result.HasThis = hasThis;
                    result.IsValid = true;
                    result.IsVirtual = true;
                    result.VtableSlot = vtableSlot;
                    result.MethodTable = genericInstMT;  // Use the instantiated MT if available
                    result.IsInterfaceMethod = false;
                    result.InterfaceMT = null;
                    result.InterfaceMethodSlot = -1;
                    result.RegistryEntry = null;

                    success = true;
                }
            }

            // If not abstract or slot calculation failed, try to JIT it
            if (!success && _tablesHeader != null && _tableSizes != null && _metadataRoot != null)
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
                    // JIT context debug (verbose - commented out)
                    // DebugConsole.WriteDecimal(targetAsmId);
                    // DebugConsole.Write(" ctx=");
                    // DebugConsole.WriteDecimal(_currentAssemblyId);
                    // DebugConsole.WriteLine();

                    JitResult jitResult = Tier0JIT.CompileMethod(targetAsmId, methodToken);

                    // JIT context debug (verbose - commented out)
                    // DebugConsole.WriteDecimal(targetAsmId);
                    // DebugConsole.Write(" ctx=");
                    // DebugConsole.WriteDecimal(_currentAssemblyId);
                    // DebugConsole.WriteLine();

                    if (jitResult.Success)
                    {
                        // Method was successfully compiled, try to get from registry again
                        info = CompiledMethodRegistry.Lookup(methodToken, targetAsmId);
                        if (info != null)
                        {
                            result.NativeCode = info->NativeCode;
                            result.ArgCount = info->ArgCount;
                            result.ReturnKind = info->ReturnKind;
                            result.ReturnStructSize = info->ReturnStructSize;
                            result.HasThis = info->HasThis;
                            result.IsValid = true;
                            result.IsVirtual = info->IsVirtual;
                            result.VtableSlot = info->VtableSlot;
                            result.MethodTable = info->MethodTable;
                            result.IsInterfaceMethod = info->IsInterfaceMethod;
                            result.InterfaceMT = info->InterfaceMT;
                            result.InterfaceMethodSlot = info->InterfaceMethodSlot;
                            result.RegistryEntry = info;
                            TraceMemberRef(methodToken, targetAsmId, info->ReturnKind, info->ArgCount);

                            // Apply same devirtualization for high vtable slots (>= 3)
                            if (result.IsVirtual && result.NativeCode != null && result.VtableSlot >= 3)
                            {
                                result.IsVirtual = false;
                                result.VtableSlot = -1;
                            }

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
        // Must use SetCurrentAssembly to also update MetadataReader's cached root
        SetCurrentAssembly(savedAsmId);

        // If this MemberRef is on a generic instantiation (e.g., Container<string>),
        // override the MethodTable with the instantiated type's MT.
        // This is critical for newobj to allocate the correct instantiated type
        // which has the properly substituted interface map.
        if (success && genericInstMT != null && result.MethodTable != null)
        {
            result.MethodTable = genericInstMT;
        }

        // Restore type argument context if we set it up
        if (hasGenericContext)
        {
            for (int i = 0; i < savedTypeArgCount; i++)
                _typeTypeArgMTs[i] = savedTypeArgs[i];
            _typeTypeArgCount = savedTypeArgCount;
        }

        // Parse vararg info from the MemberRef signature if this is a vararg call
        if (success)
        {
            ParseVarargInfo(_currentAssemblyId, token, ref result);

            // Debug: Trace get_Item resolution result
            if (_tablesHeader != null && _tableSizes != null && _metadataRoot != null)
            {
                uint nameIdx = MetadataReader.GetMethodDefName(ref *_tablesHeader, ref *_tableSizes, methodRowId);
                byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
                if (name != null && name[0] == 'g' && name[1] == 'e' && name[2] == 't' && name[3] == '_')
                {
                    DebugConsole.Write("[ResolveMDef] get_* result: isVirt=");
                    DebugConsole.Write(result.IsVirtual ? "Y" : "N");
                    DebugConsole.Write(" slot=");
                    DebugConsole.WriteDecimal((uint)(ushort)result.VtableSlot);
                    DebugConsole.Write(" code=0x");
                    DebugConsole.WriteHex((ulong)result.NativeCode);
                    DebugConsole.WriteLine();
                }
            }
        }

        return success;
    }

    /// <summary>
    /// Parse a MethodSpec instantiation blob and populate the type argument context.
    /// The instantiation blob format is: GENERICINST(0x0A) + ArgCount + Type*
    /// This must be called before resolving the underlying method when handling MethodSpec tokens.
    /// </summary>
    /// <param name="instantiationBlob">Pointer to the instantiation blob data</param>
    /// <param name="blobLen">Length of the blob</param>
    /// <returns>True if parsing succeeded</returns>
    private static bool ParseMethodSpecInstantiation(byte* instantiationBlob, uint blobLen)
    {
        // Save old context before clearing - MVAR in the blob may refer to outer method's type args
        int savedCount = _methodTypeArgCount;
        MethodTable** savedMTs = stackalloc MethodTable*[MaxMethodTypeArgs];
        byte* savedTypes = stackalloc byte[MaxMethodTypeArgs];
        ushort* savedSizes = stackalloc ushort[MaxMethodTypeArgs];
        for (int i = 0; i < MaxMethodTypeArgs; i++)
        {
            savedMTs[i] = _methodTypeArgMTs[i];
            savedTypes[i] = _methodTypeArgs[i];
            savedSizes[i] = _methodTypeArgSizes[i];
        }

        _methodTypeArgCount = 0;

        // Clear MT array
        for (int i = 0; i < MaxMethodTypeArgs; i++)
            _methodTypeArgMTs[i] = null;

        if (instantiationBlob == null || blobLen < 2)
            return false;

        byte* ptr = instantiationBlob;
        byte* end = instantiationBlob + blobLen;

        // First byte should be GENERICINST (0x0A)
        byte marker = *ptr++;
        if (marker != 0x0A)
        {
            DebugConsole.Write("[MetaInt] MethodSpec blob missing GENERICINST marker, got 0x");
            DebugConsole.WriteHex(marker);
            DebugConsole.WriteLine();
            return false;
        }

        // Read argument count (compressed uint)
        uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
        if (argCount > MaxMethodTypeArgs)
        {
            DebugConsole.Write("[MetaInt] Too many method type args: ");
            DebugConsole.WriteDecimal(argCount);
            DebugConsole.WriteLine();
            return false;
        }

        // Parse each type argument
        for (uint i = 0; i < argCount && ptr < end; i++)
        {
            byte elemType = *ptr;

            // Handle different type formats
            switch (elemType)
            {
                // Primitive types - store element type directly, compute size, and lookup MT
                case 0x02: // Boolean
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 1;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x03: // Char
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 2;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x04: // I1
                case 0x05: // U1
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 1;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x06: // I2
                case 0x07: // U2
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 2;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x08: // I4
                case 0x09: // U4
                case 0x0C: // R4
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 4;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x0A: // I8
                case 0x0B: // U8
                case 0x0D: // R8
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8;
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x18: // IntPtr (I)
                case 0x19: // UIntPtr (U)
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8; // 64-bit native int
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x0E: // String
                case 0x1C: // Object
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8; // Reference types are pointer-sized
                    _methodTypeArgMTs[i] = GetPrimitiveMethodTableFromElementType(elemType);
                    ptr++;
                    break;
                case 0x12: // Class
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8; // Reference types are pointer-sized
                    ptr++;
                    {
                        // Read TypeDefOrRef and get the MethodTable
                        uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                        uint tag = typeDefOrRef & 0x03;
                        uint typeRid = typeDefOrRef >> 2;
                        uint fullToken = 0;
                        if (tag == 0)
                            fullToken = 0x02000000 | typeRid; // TypeDef
                        else if (tag == 1)
                            fullToken = 0x01000000 | typeRid; // TypeRef
                        else if (tag == 2)
                            fullToken = 0x1B000000 | typeRid; // TypeSpec
                        _methodTypeArgMTs[i] = GetOrCreateMethodTableForToken(fullToken);
                        DebugConsole.Write("[MetaInt] MethodSpec Class arg ");
                        DebugConsole.WriteDecimal(i);
                        DebugConsole.Write(" token=0x");
                        DebugConsole.WriteHex(fullToken);
                        DebugConsole.Write(" MT=0x");
                        DebugConsole.WriteHex((ulong)_methodTypeArgMTs[i]);
                        DebugConsole.WriteLine();
                    }
                    break;
                case 0x11: // ValueType
                    _methodTypeArgs[i] = elemType;
                    ptr++;
                    {
                        // Read TypeDefOrRef and compute size and MT
                        uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                        uint tag = typeDefOrRef & 0x03;
                        uint typeRid = typeDefOrRef >> 2;
                        uint fullToken = 0;
                        if (tag == 0)
                            fullToken = 0x02000000 | typeRid; // TypeDef
                        else if (tag == 1)
                            fullToken = 0x01000000 | typeRid; // TypeRef
                        else if (tag == 2)
                            fullToken = 0x1B000000 | typeRid; // TypeSpec
                        _methodTypeArgSizes[i] = (ushort)GetTypeSize(fullToken);
                        _methodTypeArgMTs[i] = GetOrCreateMethodTableForToken(fullToken);
                    }
                    break;
                case 0x15: // GenericInst
                    _methodTypeArgs[i] = elemType;
                    ptr++;
                    {
                        // GenericInst format: CLASS/VALUETYPE + TypeDefOrRef + ArgCount + Type*
                        byte genKind = *ptr++;
                        bool isValueType = (genKind == 0x11);
                        uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                        uint genArgCount = MetadataReader.ReadCompressedUInt(ref ptr);
                        // Skip the generic type arguments
                        for (uint j = 0; j < genArgCount && ptr < end; j++)
                        {
                            SkipTypeSig(ref ptr, end);
                        }
                        if (isValueType)
                        {
                            uint tag = typeDefOrRef & 0x03;
                            uint typeRid = typeDefOrRef >> 2;
                            uint fullToken = 0;
                            if (tag == 0) fullToken = 0x02000000 | typeRid;
                            else if (tag == 1) fullToken = 0x01000000 | typeRid;
                            else if (tag == 2) fullToken = 0x1B000000 | typeRid;
                            _methodTypeArgSizes[i] = (ushort)GetTypeSize(fullToken);
                        }
                        else
                        {
                            _methodTypeArgSizes[i] = 8; // Reference type
                        }
                    }
                    break;
                case 0x1D: // SzArray
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8; // Arrays are reference types
                    ptr++;
                    SkipTypeSig(ref ptr, end);
                    break;
                case 0x0F: // Ptr
                case 0x10: // ByRef
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8; // Pointers/refs are pointer-sized
                    ptr++;
                    SkipTypeSig(ref ptr, end);
                    break;
                case 0x13: // VAR - type type parameter from enclosing generic type
                    ptr++;
                    {
                        uint varIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                        DebugConsole.Write("[MetaInt] VAR(");
                        DebugConsole.WriteDecimal(varIndex);
                        DebugConsole.Write(") typeArgCount=");
                        DebugConsole.WriteDecimal((uint)_typeTypeArgCount);
                        if (_typeTypeArgCount > 0)
                        {
                            DebugConsole.Write(" typeMT[0]=0x");
                            DebugConsole.WriteHex((ulong)_typeTypeArgMTs[0]);
                        }
                        DebugConsole.WriteLine();
                        if ((int)varIndex < _typeTypeArgCount && _typeTypeArgMTs[varIndex] != null)
                        {
                            // Substitute with the actual type from enclosing type's context
                            MethodTable* mt = _typeTypeArgMTs[varIndex];
                            _methodTypeArgMTs[i] = mt;
                            // Determine element type and size from MethodTable properties
                            if (mt->IsValueType)
                            {
                                // Value type - use ComponentSize if available, else BaseSize - 8 (header)
                                _methodTypeArgs[i] = 0x11; // ValueType
                                ushort compSize = mt->_usComponentSize;
                                _methodTypeArgSizes[i] = compSize > 0 ? compSize :
                                    (ushort)(mt->BaseSize >= 8 ? mt->BaseSize - 8 : 4);
                            }
                            else
                            {
                                // Reference type
                                _methodTypeArgs[i] = 0x12; // Class
                                _methodTypeArgSizes[i] = 8;
                            }
                            DebugConsole.Write("[MetaInt] VAR resolved to MT=0x");
                            DebugConsole.WriteHex((ulong)_methodTypeArgMTs[i]);
                            DebugConsole.WriteLine();
                        }
                        else
                        {
                            // No type context - treat as pointer-sized reference
                            DebugConsole.WriteLine("[MetaInt] VAR fallback - treating as ptr");
                            _methodTypeArgs[i] = elemType;
                            _methodTypeArgSizes[i] = 8;
                        }
                    }
                    break;
                case 0x1E: // MVAR - method type parameter, resolve from saved outer context
                    ptr++;
                    {
                        uint mvarIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                        DebugConsole.Write("[MetaInt] MVAR(");
                        DebugConsole.WriteDecimal(mvarIndex);
                        DebugConsole.Write(") savedCount=");
                        DebugConsole.WriteDecimal((uint)savedCount);
                        if (savedCount > 0)
                        {
                            DebugConsole.Write(" savedMT[0]=0x");
                            DebugConsole.WriteHex((ulong)savedMTs[0]);
                        }
                        DebugConsole.WriteLine();
                        if (mvarIndex < savedCount && savedMTs[mvarIndex] != null)
                        {
                            // Substitute with the actual type from outer method's context
                            _methodTypeArgs[i] = savedTypes[mvarIndex];
                            _methodTypeArgSizes[i] = savedSizes[mvarIndex];
                            _methodTypeArgMTs[i] = savedMTs[mvarIndex];
                            DebugConsole.Write("[MetaInt] MVAR resolved to MT=0x");
                            DebugConsole.WriteHex((ulong)_methodTypeArgMTs[i]);
                            DebugConsole.WriteLine();
                        }
                        else
                        {
                            // No context or out of range - treat as pointer-sized
                            DebugConsole.WriteLine("[MetaInt] MVAR fallback - treating as ptr");
                            _methodTypeArgs[i] = elemType;
                            _methodTypeArgSizes[i] = 8;
                        }
                    }
                    break;
                default:
                    // Unknown type - treat as pointer-sized
                    DebugConsole.Write("[MetaInt] Unknown element type in MethodSpec arg: 0x");
                    DebugConsole.WriteHex(elemType);
                    DebugConsole.WriteLine();
                    _methodTypeArgs[i] = elemType;
                    _methodTypeArgSizes[i] = 8;
                    ptr++;
                    break;
            }
        }

        _methodTypeArgCount = (int)argCount;
        return true;
    }

    /// <summary>
    /// Skip over a type signature in a blob.
    /// </summary>
    private static void SkipTypeSig(ref byte* ptr, byte* end)
    {
        if (ptr >= end) return;

        byte elemType = *ptr++;

        switch (elemType)
        {
            // Simple types with no additional data
            case 0x01: // Void
            case 0x02: // Boolean
            case 0x03: // Char
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
            case 0x0E: // String
            case 0x18: // IntPtr
            case 0x19: // UIntPtr
            case 0x1C: // Object
                // No additional data
                break;

            // Types with TypeDefOrRef token
            case 0x11: // ValueType
            case 0x12: // Class
                MetadataReader.ReadCompressedUInt(ref ptr);
                break;

            // Pointer and ByRef - followed by another type
            case 0x0F: // Ptr
            case 0x10: // ByRef
            case 0x1D: // SzArray
                SkipTypeSig(ref ptr, end);
                break;

            // Generic instantiation
            case 0x15: // GenericInst
                ptr++; // Skip CLASS/VALUETYPE marker
                MetadataReader.ReadCompressedUInt(ref ptr); // Skip TypeDefOrRef
                {
                    uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                    for (uint i = 0; i < argCount && ptr < end; i++)
                    {
                        SkipTypeSig(ref ptr, end);
                    }
                }
                break;

            // Generic type/method parameters
            case 0x13: // Var (type parameter)
            case 0x1E: // MVar (method parameter)
                MetadataReader.ReadCompressedUInt(ref ptr); // Skip index
                break;

            // Array
            case 0x14: // Array (with dimensions)
                SkipTypeSig(ref ptr, end); // Element type
                {
                    uint rank = MetadataReader.ReadCompressedUInt(ref ptr);
                    uint numSizes = MetadataReader.ReadCompressedUInt(ref ptr);
                    for (uint i = 0; i < numSizes; i++)
                        MetadataReader.ReadCompressedUInt(ref ptr);
                    uint numLoBounds = MetadataReader.ReadCompressedUInt(ref ptr);
                    for (uint i = 0; i < numLoBounds; i++)
                        MetadataReader.ReadCompressedUInt(ref ptr);
                }
                break;

            // Function pointer
            case 0x1B: // FnPtr
                // Skip method signature - complex, just skip the calling convention for now
                if (ptr < end) ptr++;
                break;
        }
    }

    /// <summary>
    /// Clear the method type argument context.
    /// Should be called at the start of top-level method compilation to avoid stale context.
    /// </summary>
    public static void ClearMethodTypeArgContext()
    {
        DebugConsole.Write("[MetaInt] CLEAR context: count ");
        DebugConsole.WriteDecimal((uint)_methodTypeArgCount);
        DebugConsole.Write(" -> 0");
        DebugConsole.WriteLine();
        _methodTypeArgCount = 0;
        // Also clear the MT array to prevent stale MTs from being saved
        for (int i = 0; i < MaxMethodTypeArgs; i++)
            _methodTypeArgMTs[i] = null;
    }

    /// <summary>
    /// Restore the method type argument context from saved values.
    /// Used to preserve outer context during nested MethodSpec resolution.
    /// </summary>
    private static void RestoreMethodTypeArgContext(int savedCount, MethodTable** savedMTs, byte* savedTypes, ushort* savedSizes)
    {
        DebugConsole.Write("[MetaInt] RESTORE context: count ");
        DebugConsole.WriteDecimal((uint)_methodTypeArgCount);
        DebugConsole.Write(" -> ");
        DebugConsole.WriteDecimal((uint)savedCount);
        if (savedCount > 0)
        {
            DebugConsole.Write(" MT[0]=0x");
            DebugConsole.WriteHex((ulong)savedMTs[0]);
        }
        DebugConsole.WriteLine();
        _methodTypeArgCount = savedCount;
        for (int i = 0; i < MaxMethodTypeArgs; i++)
        {
            _methodTypeArgMTs[i] = savedMTs[i];
            _methodTypeArgs[i] = savedTypes[i];
            _methodTypeArgSizes[i] = savedSizes[i];
        }
    }

    /// <summary>
    /// Get the current method type argument count.
    /// </summary>
    public static int GetMethodTypeArgCount()
    {
        return _methodTypeArgCount;
    }

    /// <summary>
    /// Get the element type for a method type parameter (MVAR) at the given index.
    /// Returns 0 if the index is out of range.
    /// </summary>
    public static byte GetMethodTypeArgElementType(int index)
    {
        if (index < 0 || index >= _methodTypeArgCount)
            return 0;
        return _methodTypeArgs[index];
    }

    /// <summary>
    /// Get the size for a method type parameter (MVAR) at the given index.
    /// Returns 8 (pointer size) if the index is out of range.
    /// </summary>
    public static ushort GetMethodTypeArgSize(int index)
    {
        if (index < 0 || index >= _methodTypeArgCount)
            return 8;
        return _methodTypeArgSizes[index];
    }

    /// <summary>
    /// Check if there's an active method type argument context.
    /// Used by signature parsing to determine if MVAR should be resolved.
    /// </summary>
    public static bool HasMethodTypeArgContext()
    {
        return _methodTypeArgCount > 0;
    }

    /// <summary>
    /// Get the MethodTable for a method type parameter (MVAR) at the given index.
    /// Returns null if the index is out of range or no MT is registered.
    /// </summary>
    public static MethodTable* GetMethodTypeArgMethodTable(int index)
    {
        if (index < 0 || index >= _methodTypeArgCount)
            return null;
        return _methodTypeArgMTs[index];
    }

    /// <summary>
    /// Get the MethodTable for a type type parameter (VAR) at the given index.
    /// Returns null if the index is out of range or no MT is registered.
    /// </summary>
    public static MethodTable* GetTypeTypeArgMethodTable(int index)
    {
        DebugConsole.Write("[GetTypeArg] idx=");
        DebugConsole.WriteDecimal((uint)index);
        DebugConsole.Write(" count=");
        DebugConsole.WriteDecimal((uint)_typeTypeArgCount);

        if (index < 0 || index >= _typeTypeArgCount)
        {
            DebugConsole.WriteLine(" -> OUT OF RANGE");
            return null;
        }

        DebugConsole.Write(" -> MT=0x");
        DebugConsole.WriteHex((ulong)_typeTypeArgMTs[index]);
        DebugConsole.WriteLine();
        return _typeTypeArgMTs[index];
    }

    /// <summary>
    /// Set type type arguments (for generic type instantiation context).
    /// </summary>
    public static void SetTypeTypeArgs(MethodTable** mts, int count)
    {
        DebugConsole.Write("[SetTypeArgs] count=");
        DebugConsole.WriteDecimal((uint)count);
        for (int i = 0; i < count && i < 4; i++)
        {
            DebugConsole.Write(" arg");
            DebugConsole.WriteDecimal((uint)i);
            DebugConsole.Write("=0x");
            DebugConsole.WriteHex((ulong)mts[i]);
        }
        DebugConsole.WriteLine();

        _typeTypeArgCount = count > MaxTypeTypeArgs ? MaxTypeTypeArgs : count;
        for (int i = 0; i < _typeTypeArgCount; i++)
            _typeTypeArgMTs[i] = mts[i];
    }

    /// <summary>
    /// Clear type type argument context.
    /// </summary>
    public static void ClearTypeTypeArgs()
    {
        _typeTypeArgCount = 0;
        for (int i = 0; i < MaxTypeTypeArgs; i++)
            _typeTypeArgMTs[i] = null;
    }

    /// <summary>
    /// Get the current type type argument count.
    /// </summary>
    public static int GetTypeTypeArgCount()
    {
        return _typeTypeArgCount;
    }

    /// <summary>
    /// Compute a hash of the current type type arguments.
    /// Returns 0 if there are no type arguments (non-generic context).
    /// This is used to distinguish static fields in different generic instantiations.
    /// </summary>
    public static ulong GetTypeTypeArgHash()
    {
        if (_typeTypeArgCount == 0)
            return 0;

        // XOR all type argument MTs together for a simple hash
        ulong hash = 0;
        for (int i = 0; i < _typeTypeArgCount; i++)
        {
            if (_typeTypeArgMTs[i] != null)
            {
                // Mix in each MT pointer with rotation to spread bits
                ulong mtVal = (ulong)_typeTypeArgMTs[i];
                hash ^= mtVal;
                hash = (hash << 13) | (hash >> 51);  // Rotate
            }
        }
        return hash;
    }

    /// <summary>
    /// Compute a hash of the current method type arguments.
    /// Returns 0 if there are no method type arguments (non-generic method).
    /// This is used to distinguish generic method instantiations for code caching.
    /// </summary>
    public static ulong GetMethodTypeArgHash()
    {
        if (_methodTypeArgCount == 0)
            return 0;

        // XOR all type argument MTs together for a simple hash
        ulong hash = 0;
        for (int i = 0; i < _methodTypeArgCount; i++)
        {
            if (_methodTypeArgMTs[i] != null)
            {
                // Mix in each MT pointer with rotation to spread bits
                ulong mtVal = (ulong)_methodTypeArgMTs[i];
                hash ^= mtVal;
                hash = (hash << 13) | (hash >> 51);  // Rotate
            }
        }
        return hash;
    }

    /// <summary>
    /// Resolve a TypeSpec blob to a MethodTable.
    /// Handles VAR, MVAR, GENERICINST, SZARRAY, etc.
    /// </summary>
    private static MethodTable* ResolveTypeSpecBlob(uint token)
    {
        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
            return null;

        // Try to get blob from current assembly context first
        byte* blob = null;
        uint blobLen = 0;

        if (_tablesHeader != null && _tableSizes != null && _metadataRoot != null)
        {
            uint blobIdx = MetadataReader.GetTypeSpecSignature(ref *_tablesHeader, ref *_tableSizes, rowId);
            blob = MetadataReader.GetBlob(ref *_metadataRoot, blobIdx, out blobLen);
        }

        // Fallback to current assembly ID
        if (blob == null && _currentAssemblyId != 0)
        {
            LoadedAssembly* asm = AssemblyLoader.GetAssembly(_currentAssemblyId);
            if (asm != null)
            {
                uint blobIdx = MetadataReader.GetTypeSpecSignature(ref asm->Tables, ref asm->Sizes, rowId);
                blob = MetadataReader.GetBlob(ref asm->Metadata, blobIdx, out blobLen);
            }
        }

        if (blob == null || blobLen < 1)
            return null;

        byte elemType = blob[0];
        byte* ptr = blob + 1;
        byte* end = blob + blobLen;

        switch (elemType)
        {
            case 0x13:  // VAR - type generic parameter
            {
                uint varIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                return GetTypeTypeArgMethodTable((int)varIndex);
            }

            case 0x1E:  // MVAR - method generic parameter
            {
                uint mvarIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                return GetMethodTypeArgMethodTable((int)mvarIndex);
            }

            case 0x15:  // GENERICINST - generic type instantiation (e.g., List<int>)
            {
                return ResolveGenericInstType(ptr, end);
            }

            case 0x1D:  // SZARRAY - single-dimension array with zero lower bound
            {
                // Get element type and create array MT
                MethodTable* elemMt = ResolveTypeSigToMethodTable(ref ptr, end);
                if (elemMt != null)
                {
                    return AssemblyLoader.GetOrCreateArrayMethodTable(elemMt);
                }
                return null;
            }

            case 0x14:  // ARRAY - multi-dimensional array
            {
                // Format: ElementType Rank NumSizes Size* NumLoBounds LoBound*
                // Get element type
                MethodTable* elemMt = ResolveTypeSigToMethodTable(ref ptr, end);
                if (elemMt == null)
                {
                    DebugConsole.WriteLine("[MetaInt] MD array: failed to resolve element type");
                    return null;
                }

                // Read rank
                uint rank = MetadataReader.ReadCompressedUInt(ref ptr);
                if (rank < 2 || rank > 32)
                {
                    DebugConsole.Write("[MetaInt] MD array: invalid rank ");
                    DebugConsole.WriteDecimal(rank);
                    DebugConsole.WriteLine();
                    return null;
                }

                // Skip NumSizes and Size* values (we don't need them at runtime)
                uint numSizes = MetadataReader.ReadCompressedUInt(ref ptr);
                for (uint i = 0; i < numSizes; i++)
                {
                    MetadataReader.ReadCompressedUInt(ref ptr);  // Skip each size
                }

                // Skip NumLoBounds and LoBound* values
                uint numLoBounds = MetadataReader.ReadCompressedUInt(ref ptr);
                for (uint i = 0; i < numLoBounds; i++)
                {
                    MetadataReader.ReadCompressedInt(ref ptr);  // Skip each loBound (signed)
                }

                // Create MD array MethodTable
                return AssemblyLoader.GetOrCreateMDArrayMethodTable(elemMt, (int)rank);
            }

            case 0x0F:  // PTR - pointer type
            case 0x10:  // BYREF - by-reference type
            {
                // For ptr/byref, we just need the underlying type size info
                // Return IntPtr MT as a stand-in (pointers are pointer-sized)
                return LookupType(WellKnownTypes.IntPtr);
            }

            default:
                // Try as a primitive element type
                return GetPrimitiveMethodTableFromElementType(elemType);
        }
    }

    /// <summary>
    /// Resolve a GENERICINST type signature to a MethodTable.
    /// Format: CLASS/VALUETYPE TypeDefOrRef GenArgCount Type+
    /// </summary>
    private static MethodTable* ResolveGenericInstType(byte* ptr, byte* end)
    {
        if (ptr >= end)
            return null;

        byte kind = *ptr++;  // CLASS (0x12) or VALUETYPE (0x11)
        bool isValueType = (kind == 0x11);

        // Read the generic type definition (TypeDefOrRef coded index)
        uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
        uint tag = typeDefOrRef & 0x03;
        uint typeRid = typeDefOrRef >> 2;

        // Convert to full token
        uint genDefToken = tag switch
        {
            0 => 0x02000000 | typeRid,  // TypeDef
            1 => 0x01000000 | typeRid,  // TypeRef
            2 => 0x1B000000 | typeRid,  // TypeSpec (nested generic)
            _ => 0
        };

        if (genDefToken == 0)
            return null;

        // Read the number of type arguments
        uint genArgCount = MetadataReader.ReadCompressedUInt(ref ptr);
        if (genArgCount == 0 || genArgCount > MaxTypeTypeArgs)
            return null;

        // Parse each type argument and get its MethodTable
        MethodTable** argMTs = stackalloc MethodTable*[(int)genArgCount];
        for (uint i = 0; i < genArgCount && ptr < end; i++)
        {
            argMTs[i] = ResolveTypeSigToMethodTable(ref ptr, end);
            if (argMTs[i] == null)
            {
                return null;
            }
        }

        // Get or create the instantiated generic type
        return AssemblyLoader.GetOrCreateGenericInstMethodTable(genDefToken, argMTs, (int)genArgCount, isValueType);
    }

    /// <summary>
    /// Resolve a type signature blob to a MethodTable.
    /// This handles the various ELEMENT_TYPE formats in type signatures.
    /// </summary>
    private static MethodTable* ResolveTypeSigToMethodTable(ref byte* ptr, byte* end)
    {
        if (ptr >= end)
            return null;

        byte elemType = *ptr++;

        switch (elemType)
        {
            // Primitive types
            case 0x01: return null;  // VOID - no MT
            case 0x02: return LookupType(WellKnownTypes.Boolean);
            case 0x03: return LookupType(WellKnownTypes.Char);
            case 0x04: return LookupType(WellKnownTypes.SByte);
            case 0x05: return LookupType(WellKnownTypes.Byte);
            case 0x06: return LookupType(WellKnownTypes.Int16);
            case 0x07: return LookupType(WellKnownTypes.UInt16);
            case 0x08: return LookupType(WellKnownTypes.Int32);
            case 0x09: return LookupType(WellKnownTypes.UInt32);
            case 0x0A: return LookupType(WellKnownTypes.Int64);
            case 0x0B: return LookupType(WellKnownTypes.UInt64);
            case 0x0C: return LookupType(WellKnownTypes.Single);
            case 0x0D: return LookupType(WellKnownTypes.Double);
            case 0x0E: return LookupType(WellKnownTypes.String);
            case 0x18: return LookupType(WellKnownTypes.IntPtr);
            case 0x19: return LookupType(WellKnownTypes.UIntPtr);
            case 0x1C: return LookupType(WellKnownTypes.Object);

            case 0x11:  // VALUETYPE TypeDefOrRef
            case 0x12:  // CLASS TypeDefOrRef
            {
                uint typeDefOrRef = MetadataReader.ReadCompressedUInt(ref ptr);
                uint tag = typeDefOrRef & 0x03;
                uint typeRid = typeDefOrRef >> 2;
                uint fullToken = tag switch
                {
                    0 => 0x02000000 | typeRid,
                    1 => 0x01000000 | typeRid,
                    2 => 0x1B000000 | typeRid,
                    _ => 0
                };
                if (fullToken == 0) return null;

                void* mt;
                if (ResolveType(fullToken, out mt))
                    return (MethodTable*)mt;
                return null;
            }

            case 0x13:  // VAR - type generic parameter
            {
                uint varIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                return GetTypeTypeArgMethodTable((int)varIndex);
            }

            case 0x1E:  // MVAR - method generic parameter
            {
                uint mvarIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                return GetMethodTypeArgMethodTable((int)mvarIndex);
            }

            case 0x15:  // GENERICINST
            {
                return ResolveGenericInstType(ptr, end);
            }

            case 0x1D:  // SZARRAY
            {
                MethodTable* elemMt = ResolveTypeSigToMethodTable(ref ptr, end);
                if (elemMt != null)
                    return AssemblyLoader.GetOrCreateArrayMethodTable(elemMt);
                return null;
            }

            case 0x0F:  // PTR
            case 0x10:  // BYREF
            {
                // Skip the underlying type
                SkipTypeSig(ref ptr, end);
                return LookupType(WellKnownTypes.IntPtr);
            }

            default:
                DebugConsole.Write("[MetaInt] Unknown element type in TypeSig: 0x");
                DebugConsole.WriteHex(elemType);
                DebugConsole.WriteLine();
                return null;
        }
    }

    /// <summary>
    /// Get a MethodTable pointer for a primitive ELEMENT_TYPE code.
    /// Uses the well-known type registry.
    /// </summary>
    private static MethodTable* GetPrimitiveMethodTableFromElementType(byte elementType)
    {
        uint token = elementType switch
        {
            0x02 => WellKnownTypes.Boolean,  // ELEMENT_TYPE_BOOLEAN
            0x03 => WellKnownTypes.Char,     // ELEMENT_TYPE_CHAR
            0x04 => WellKnownTypes.SByte,    // ELEMENT_TYPE_I1
            0x05 => WellKnownTypes.Byte,     // ELEMENT_TYPE_U1
            0x06 => WellKnownTypes.Int16,    // ELEMENT_TYPE_I2
            0x07 => WellKnownTypes.UInt16,   // ELEMENT_TYPE_U2
            0x08 => WellKnownTypes.Int32,    // ELEMENT_TYPE_I4
            0x09 => WellKnownTypes.UInt32,   // ELEMENT_TYPE_U4
            0x0A => WellKnownTypes.Int64,    // ELEMENT_TYPE_I8
            0x0B => WellKnownTypes.UInt64,   // ELEMENT_TYPE_U8
            0x0C => WellKnownTypes.Single,   // ELEMENT_TYPE_R4
            0x0D => WellKnownTypes.Double,   // ELEMENT_TYPE_R8
            0x0E => WellKnownTypes.String,   // ELEMENT_TYPE_STRING
            0x18 => WellKnownTypes.IntPtr,   // ELEMENT_TYPE_I
            0x19 => WellKnownTypes.UIntPtr,  // ELEMENT_TYPE_U
            0x1C => WellKnownTypes.Object,   // ELEMENT_TYPE_OBJECT
            _ => 0
        };

        if (token == 0)
            return null;

        return LookupType(token);
    }

    /// <summary>
    /// Get or create a MethodTable for a TypeDefOrRef or TypeSpec token.
    /// </summary>
    private static MethodTable* GetOrCreateMethodTableForToken(uint typeToken)
    {
        if (typeToken == 0)
            return null;

        // Get the current assembly ID for context
        uint asmId = _currentAssemblyId;
        if (asmId == 0)
            return null;

        // Use AssemblyLoader to resolve the type
        return AssemblyLoader.ResolveType(asmId, typeToken);
    }

    /// <summary>
    /// Resolve a MethodSpec token (0x2B) to method call information.
    /// This handles generic method instantiations like Foo<int>().
    /// </summary>
    private static bool ResolveMethodSpecMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        uint rowId = token & 0x00FFFFFF;
        if (rowId == 0)
            return false;

        // Save outer context for nested MethodSpec resolution
        // When compiling InlineArrayAsSpan<A,B> which calls CreateSpan<B>,
        // we must preserve InlineArrayAsSpan's type args during nested resolution
        int savedOuterCount = _methodTypeArgCount;
        MethodTable** savedOuterMTs = stackalloc MethodTable*[MaxMethodTypeArgs];
        byte* savedOuterTypes = stackalloc byte[MaxMethodTypeArgs];
        ushort* savedOuterSizes = stackalloc ushort[MaxMethodTypeArgs];
        for (int i = 0; i < MaxMethodTypeArgs; i++)
        {
            savedOuterMTs[i] = _methodTypeArgMTs[i];
            savedOuterTypes[i] = _methodTypeArgs[i];
            savedOuterSizes[i] = _methodTypeArgSizes[i];
        }

        // Get the underlying method (MethodDefOrRef coded index)
        uint methodDefOrRef = MetadataReader.GetMethodSpecMethod(ref *_tablesHeader, ref *_tableSizes, rowId, ref *_tablesHeader);
        if (methodDefOrRef == 0)
        {
            DebugConsole.WriteLine("[MetaInt] MethodSpec has null method reference");
            RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
            return false;
        }

        // Get the instantiation blob
        uint instantiationIdx = MetadataReader.GetMethodSpecInstantiation(ref *_tablesHeader, ref *_tableSizes, rowId, ref *_tablesHeader);
        byte* instantiationBlob = MetadataReader.GetBlob(ref *_metadataRoot, instantiationIdx, out uint blobLen);

        // Parse the instantiation blob to populate type argument context
        if (!ParseMethodSpecInstantiation(instantiationBlob, blobLen))
        {
            DebugConsole.WriteLine("[MetaInt] Failed to parse MethodSpec instantiation blob");
            RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
            return false;
        }

        // Decode MethodDefOrRef: 1-bit tag (0=MethodDef, 1=MemberRef), rest is row ID
        uint tag = methodDefOrRef & 0x01;
        uint underlyingRowId = methodDefOrRef >> 1;

        uint underlyingToken;
        if (tag == 0)
        {
            // MethodDef
            underlyingToken = 0x06000000 | underlyingRowId;
        }
        else
        {
            // MemberRef
            underlyingToken = 0x0A000000 | underlyingRowId;
        }

        // Check for Activator.CreateInstance<T>() BEFORE resolving the underlying method
        // This is a JIT intrinsic that we handle specially - we don't actually call the method
        if (tag == 1 && _methodTypeArgCount == 1)  // MemberRef with 1 type arg
        {
            if (IsActivatorCreateInstanceMemberRef(underlyingRowId))
            {
                DebugConsole.WriteLine("[MetaInt] Detected Activator.CreateInstance<T>!");
                result.IsActivatorCreateInstance = true;
                result.ActivatorTypeArgMT = _methodTypeArgMTs[0];
                result.IsValid = true;
                result.NativeCode = null;  // No native code - JIT intrinsic
                result.ArgCount = 0;
                result.HasThis = false;
                result.RegistryEntry = null;
                // Return kind is based on the type argument
                MethodTable* typeArgMT = _methodTypeArgMTs[0];
                if (typeArgMT != null)
                {
                    if ((typeArgMT->CombinedFlags & MTFlags.IsValueType) != 0)
                    {
                        result.ReturnKind = ReturnKind.Struct;
                        // For value types, _uBaseSize includes header (8 bytes), use raw size
                        result.ReturnStructSize = (ushort)(typeArgMT->_usComponentSize > 0 ?
                            typeArgMT->_usComponentSize :
                            (typeArgMT->_uBaseSize >= 8 ? typeArgMT->_uBaseSize - 8 : typeArgMT->_uBaseSize));
                    }
                    else
                    {
                        result.ReturnKind = ReturnKind.IntPtr;
                    }
                }
                else
                {
                    result.ReturnKind = ReturnKind.IntPtr;
                }
                // Restore outer context and return success - don't resolve the actual method
                RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
                return true;
            }
        }

        // Check for Unsafe.As<TFrom, TTo>(ref TFrom) - JIT intrinsic for type coercion
        // Takes ref TFrom (1 arg) and returns ref TTo - we just pass through the reference
        if (tag == 1 && _methodTypeArgCount == 2)  // MemberRef with 2 type args
        {
            if (IsUnsafeAsMemberRef(underlyingRowId))
            {
                DebugConsole.WriteLine("[MetaInt] Detected Unsafe.As<TFrom, TTo>!");
                result.IsUnsafeAs = true;
                result.IsValid = true;
                result.NativeCode = null;  // No native code - JIT intrinsic
                result.ArgCount = 1;  // Takes ref TFrom
                result.HasThis = false;
                result.RegistryEntry = null;
                result.ReturnKind = ReturnKind.IntPtr;  // Returns ref TTo (pointer)
                RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
                return true;
            }
        }

        // Check for Unsafe.Add<T>(ref T, int) - JIT intrinsic for pointer arithmetic
        // Takes ref T (arg0) and int offset (arg1), returns ref T
        if (tag == 1 && _methodTypeArgCount == 1)  // MemberRef with 1 type arg
        {
            if (IsUnsafeAddMemberRef(underlyingRowId))
            {
                DebugConsole.Write("[MetaInt] Detected Unsafe.Add<T>! sizeof(T)=");
                ushort elemSize = _methodTypeArgSizes[0];
                DebugConsole.WriteDecimal(elemSize);
                DebugConsole.WriteLine();
                result.IsUnsafeAdd = true;
                result.UnsafeAddElementSize = elemSize;
                result.IsValid = true;
                result.NativeCode = null;  // No native code - JIT intrinsic
                result.ArgCount = 2;  // Takes ref T and int
                result.HasThis = false;
                result.RegistryEntry = null;
                result.ReturnKind = ReturnKind.IntPtr;  // Returns ref T (pointer)
                RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
                return true;
            }
        }

        // Check for MemoryMarshal.CreateSpan<T>(ref T, int) - JIT intrinsic for span creation
        // Takes ref T (arg0) and int length (arg1), returns Span<T> (16-byte struct: pointer + length)
        if (tag == 1 && _methodTypeArgCount == 1)  // MemberRef with 1 type arg
        {
            if (IsMemoryMarshalCreateSpanMemberRef(underlyingRowId))
            {
                DebugConsole.WriteLine("[MetaInt] Detected MemoryMarshal.CreateSpan<T>!");
                result.IsCreateSpan = true;
                result.IsValid = true;
                result.NativeCode = null;  // No native code - JIT intrinsic
                result.ArgCount = 2;  // Takes ref T and int length
                result.HasThis = false;
                result.RegistryEntry = null;
                result.ReturnKind = ReturnKind.Struct;  // Returns Span<T> (16 bytes)
                result.ReturnStructSize = 16;
                RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);
                return true;
            }
        }

        // For generic methods (MethodDef with type args), we need to handle code sharing correctly.
        // Different instantiations with different-sized value types need different native code
        // because operations like sizeof(T) and Unsafe.Add<T> depend on the actual type size.
        // Compare the current method type arg hash with the stored hash to detect mismatches.
        if (tag == 0 && _methodTypeArgCount > 0)  // MethodDef with type args
        {
            CompiledMethodInfo* existingInfo = CompiledMethodRegistry.Lookup(underlyingToken, _currentAssemblyId);
            if (existingInfo != null && existingInfo->IsCompiled)
            {
                // Check if the compiled code was for a different instantiation
                ulong currentHash = GetMethodTypeArgHash();
                if (existingInfo->TypeArgHash != currentHash)
                {
                    // Different instantiation - need to recompile with current type args
                    // DebugConsole.Write("[MetaInt] Hash mismatch for generic method 0x");
                    // DebugConsole.WriteHex(underlyingToken);
                    // DebugConsole.Write(" stored=0x");
                    // DebugConsole.WriteHex(existingInfo->TypeArgHash);
                    // DebugConsole.Write(" current=0x");
                    // DebugConsole.WriteHex(currentHash);
                    // DebugConsole.WriteLine();
                    existingInfo->IsCompiled = false;
                    // Update the stored hash to current
                    existingInfo->TypeArgHash = currentHash;
                }
            }
        }

        // Now resolve the underlying method with type args in context
        // The signature parsing functions will use GetMethodTypeArgSize() for MVAR types
        bool success = ResolveMethod(underlyingToken, out result);

        // For generic methods, ensure the TypeArgHash is updated after compilation
        // to reflect the method type args used for this instantiation
        if (success && tag == 0 && _methodTypeArgCount > 0)
        {
            CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(underlyingToken, _currentAssemblyId);
            if (info != null)
            {
                ulong methodHash = GetMethodTypeArgHash();
                info->TypeArgHash = methodHash;
            }
        }

        // Debug: trace return type for MethodSpec resolution (always trace for MethodDef tokens)
        if (success && tag == 0 && _methodTypeArgCount > 0)
        {
            DebugConsole.Write("[MetaInt] MethodSpec->MethodDef tok=0x");
            DebugConsole.WriteHex(underlyingToken);
            DebugConsole.Write(" retKind=");
            DebugConsole.WriteDecimal((uint)result.ReturnKind);
            DebugConsole.Write(" structSz=");
            DebugConsole.WriteDecimal(result.ReturnStructSize);
            DebugConsole.WriteLine();
        }

        // Check if this is Activator.CreateInstance<T>() - handle as JIT intrinsic (backup check after resolution)
        if (success && tag == 1 && _methodTypeArgCount == 1)  // MemberRef with 1 type arg
        {
            DebugConsole.Write("[MetaInt] Checking Activator: rowId=");
            DebugConsole.WriteDecimal(underlyingRowId);
            DebugConsole.Write(" typeArgCount=");
            DebugConsole.WriteDecimal((uint)_methodTypeArgCount);
            DebugConsole.Write(" MT[0]=0x");
            DebugConsole.WriteHex((ulong)_methodTypeArgMTs[0]);
            DebugConsole.WriteLine();
            if (IsActivatorCreateInstanceMemberRef(underlyingRowId))
            {
                DebugConsole.WriteLine("[MetaInt] Detected Activator.CreateInstance<T>!");
                result.IsActivatorCreateInstance = true;
                result.ActivatorTypeArgMT = _methodTypeArgMTs[0];
                // Override the signature - CreateInstance<T> returns T and takes no args
                result.ArgCount = 0;
                result.HasThis = false;
                // Return kind is based on the type argument
                // For reference types: ReturnKind.Ref
                // For value types: ReturnKind.Struct with appropriate size
                MethodTable* typeArgMT = _methodTypeArgMTs[0];
                if (typeArgMT != null)
                {
                    if ((typeArgMT->CombinedFlags & MTFlags.IsValueType) != 0)
                    {
                        result.ReturnKind = ReturnKind.Struct;
                        // For value types, _uBaseSize includes header (8 bytes), use raw size
                        result.ReturnStructSize = (ushort)(typeArgMT->_usComponentSize > 0 ?
                            typeArgMT->_usComponentSize :
                            (typeArgMT->_uBaseSize >= 8 ? typeArgMT->_uBaseSize - 8 : typeArgMT->_uBaseSize));
                    }
                    else
                    {
                        result.ReturnKind = ReturnKind.IntPtr;
                    }
                }
                else
                {
                    result.ReturnKind = ReturnKind.IntPtr;  // Default to reference type
                }
            }
        }

        // Restore the outer context after resolution to avoid affecting other methods
        RestoreMethodTypeArgContext(savedOuterCount, savedOuterMTs, savedOuterTypes, savedOuterSizes);

        return success;
    }

    /// <summary>
    /// Check if a MemberRef row refers to System.Activator.CreateInstance.
    /// </summary>
    private static bool IsActivatorCreateInstanceMemberRef(uint memberRefRowId)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the member name
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        if (name == null)
            return false;

        // Debug: show the name
        DebugConsole.Write("[MetaInt] MemberRef ");
        DebugConsole.WriteDecimal(memberRefRowId);
        DebugConsole.Write(" name='");
        for (int i = 0; i < 20 && name[i] != 0; i++)
            DebugConsole.WriteChar((char)name[i]);
        DebugConsole.WriteLine("'");

        // Check if name is "CreateInstance"
        if (!IsCreateInstanceName(name))
            return false;

        // Get the class (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);

        // Must be a TypeRef to System.Activator
        if (classRef.Table != MetadataTableId.TypeRef)
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

        byte* typeName = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);

        // Check if type is "Activator" in namespace "System"
        return IsActivatorName(typeName) && IsSystemNamespace(typeNs);
    }

    /// <summary>Check if name equals "CreateInstance".</summary>
    private static bool IsCreateInstanceName(byte* name)
    {
        if (name == null) return false;
        // "CreateInstance" = 14 chars
        return name[0] == 'C' && name[1] == 'r' && name[2] == 'e' && name[3] == 'a' &&
               name[4] == 't' && name[5] == 'e' && name[6] == 'I' && name[7] == 'n' &&
               name[8] == 's' && name[9] == 't' && name[10] == 'a' && name[11] == 'n' &&
               name[12] == 'c' && name[13] == 'e' && name[14] == 0;
    }

    /// <summary>Check if name equals "Activator".</summary>
    private static bool IsActivatorName(byte* name)
    {
        if (name == null) return false;
        // "Activator" = 9 chars
        return name[0] == 'A' && name[1] == 'c' && name[2] == 't' && name[3] == 'i' &&
               name[4] == 'v' && name[5] == 'a' && name[6] == 't' && name[7] == 'o' &&
               name[8] == 'r' && name[9] == 0;
    }

    /// <summary>Check if namespace equals "System".</summary>
    private static bool IsSystemNamespace(byte* ns)
    {
        if (ns == null) return false;
        // "System" = 6 chars
        return ns[0] == 'S' && ns[1] == 'y' && ns[2] == 's' && ns[3] == 't' &&
               ns[4] == 'e' && ns[5] == 'm' && ns[6] == 0;
    }

    /// <summary>
    /// Check if a MemberRef row refers to System.Runtime.CompilerServices.RuntimeHelpers.InitializeArray.
    /// </summary>
    private static bool IsRuntimeHelpersInitializeArrayMemberRef(uint memberRefRowId)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the member name
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        if (name == null)
            return false;

        // Check if name is "InitializeArray"
        if (!IsInitializeArrayName(name))
            return false;

        // Get the class (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);

        // Must be a TypeRef to RuntimeHelpers
        if (classRef.Table != MetadataTableId.TypeRef)
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

        byte* typeName = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);

        // Check if type is "RuntimeHelpers" in namespace "System.Runtime.CompilerServices"
        return IsRuntimeHelpersName(typeName) && IsCompilerServicesNamespace(typeNs);
    }

    /// <summary>Check if name equals "InitializeArray".</summary>
    private static bool IsInitializeArrayName(byte* name)
    {
        if (name == null) return false;
        // "InitializeArray" = 15 chars
        return name[0] == 'I' && name[1] == 'n' && name[2] == 'i' && name[3] == 't' &&
               name[4] == 'i' && name[5] == 'a' && name[6] == 'l' && name[7] == 'i' &&
               name[8] == 'z' && name[9] == 'e' && name[10] == 'A' && name[11] == 'r' &&
               name[12] == 'r' && name[13] == 'a' && name[14] == 'y' && name[15] == 0;
    }

    /// <summary>Check if name equals "RuntimeHelpers".</summary>
    private static bool IsRuntimeHelpersName(byte* name)
    {
        if (name == null) return false;
        // "RuntimeHelpers" = 14 chars
        return name[0] == 'R' && name[1] == 'u' && name[2] == 'n' && name[3] == 't' &&
               name[4] == 'i' && name[5] == 'm' && name[6] == 'e' && name[7] == 'H' &&
               name[8] == 'e' && name[9] == 'l' && name[10] == 'p' && name[11] == 'e' &&
               name[12] == 'r' && name[13] == 's' && name[14] == 0;
    }

    /// <summary>Check if namespace equals "System.Runtime.CompilerServices".</summary>
    private static bool IsCompilerServicesNamespace(byte* ns)
    {
        if (ns == null) return false;
        // "System.Runtime.CompilerServices" = 31 chars
        return ns[0] == 'S' && ns[1] == 'y' && ns[2] == 's' && ns[3] == 't' &&
               ns[4] == 'e' && ns[5] == 'm' && ns[6] == '.' && ns[7] == 'R' &&
               ns[8] == 'u' && ns[9] == 'n' && ns[10] == 't' && ns[11] == 'i' &&
               ns[12] == 'm' && ns[13] == 'e' && ns[14] == '.' && ns[15] == 'C' &&
               ns[16] == 'o' && ns[17] == 'm' && ns[18] == 'p' && ns[19] == 'i' &&
               ns[20] == 'l' && ns[21] == 'e' && ns[22] == 'r' && ns[23] == 'S' &&
               ns[24] == 'e' && ns[25] == 'r' && ns[26] == 'v' && ns[27] == 'i' &&
               ns[28] == 'c' && ns[29] == 'e' && ns[30] == 's' && ns[31] == 0;
    }

    /// <summary>
    /// Check if a MemberRef row refers to System.Runtime.CompilerServices.Unsafe.As.
    /// </summary>
    private static bool IsUnsafeAsMemberRef(uint memberRefRowId)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the member name
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        if (name == null)
            return false;

        // Check if name is "As"
        if (!IsAsName(name))
            return false;

        // Get the class (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);

        // Must be a TypeRef to Unsafe
        if (classRef.Table != MetadataTableId.TypeRef)
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

        byte* typeName = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);

        // Check if type is "Unsafe" in namespace "System.Runtime.CompilerServices"
        return IsUnsafeName(typeName) && IsCompilerServicesNamespace(typeNs);
    }

    /// <summary>
    /// Check if a MemberRef row refers to System.Runtime.CompilerServices.Unsafe.Add.
    /// </summary>
    private static bool IsUnsafeAddMemberRef(uint memberRefRowId)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the member name
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        if (name == null)
            return false;

        // Check if name is "Add"
        if (!IsAddName(name))
            return false;

        // Get the class (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);

        // Must be a TypeRef to Unsafe
        if (classRef.Table != MetadataTableId.TypeRef)
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

        byte* typeName = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);

        // Check if type is "Unsafe" in namespace "System.Runtime.CompilerServices"
        return IsUnsafeName(typeName) && IsCompilerServicesNamespace(typeNs);
    }

    /// <summary>Check if name equals "As".</summary>
    private static bool IsAsName(byte* name)
    {
        if (name == null) return false;
        // "As" = 2 chars
        return name[0] == 'A' && name[1] == 's' && name[2] == 0;
    }

    /// <summary>Check if name equals "Add".</summary>
    private static bool IsAddName(byte* name)
    {
        if (name == null) return false;
        // "Add" = 3 chars
        return name[0] == 'A' && name[1] == 'd' && name[2] == 'd' && name[3] == 0;
    }

    /// <summary>Check if name equals "Unsafe".</summary>
    private static bool IsUnsafeName(byte* name)
    {
        if (name == null) return false;
        // "Unsafe" = 6 chars
        return name[0] == 'U' && name[1] == 'n' && name[2] == 's' && name[3] == 'a' &&
               name[4] == 'f' && name[5] == 'e' && name[6] == 0;
    }

    /// <summary>
    /// Check if a MemberRef row refers to System.Runtime.InteropServices.MemoryMarshal.CreateSpan.
    /// </summary>
    private static bool IsMemoryMarshalCreateSpanMemberRef(uint memberRefRowId)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return false;

        // Get the member name
        uint nameIdx = MetadataReader.GetMemberRefName(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);
        if (name == null)
            return false;

        // Check if name is "CreateSpan"
        if (!IsCreateSpanName(name))
            return false;

        // Get the class (MemberRefParent)
        CodedIndex classRef = MetadataReader.GetMemberRefClass(ref *_tablesHeader, ref *_tableSizes, memberRefRowId);

        // Must be a TypeRef to MemoryMarshal
        if (classRef.Table != MetadataTableId.TypeRef)
            return false;

        // Get type name and namespace
        uint typeNameIdx = MetadataReader.GetTypeRefName(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);
        uint typeNsIdx = MetadataReader.GetTypeRefNamespace(ref *_tablesHeader, ref *_tableSizes, classRef.RowId);

        byte* typeName = MetadataReader.GetString(ref *_metadataRoot, typeNameIdx);
        byte* typeNs = MetadataReader.GetString(ref *_metadataRoot, typeNsIdx);

        // Check if type is "MemoryMarshal" in namespace "System.Runtime.InteropServices"
        return IsMemoryMarshalName(typeName) && IsInteropServicesNamespace(typeNs);
    }

    /// <summary>Check if name equals "CreateSpan".</summary>
    private static bool IsCreateSpanName(byte* name)
    {
        if (name == null) return false;
        // "CreateSpan" = 10 chars
        return name[0] == 'C' && name[1] == 'r' && name[2] == 'e' && name[3] == 'a' &&
               name[4] == 't' && name[5] == 'e' && name[6] == 'S' && name[7] == 'p' &&
               name[8] == 'a' && name[9] == 'n' && name[10] == 0;
    }

    /// <summary>Check if name equals "MemoryMarshal".</summary>
    private static bool IsMemoryMarshalName(byte* name)
    {
        if (name == null) return false;
        // "MemoryMarshal" = 13 chars
        return name[0] == 'M' && name[1] == 'e' && name[2] == 'm' && name[3] == 'o' &&
               name[4] == 'r' && name[5] == 'y' && name[6] == 'M' && name[7] == 'a' &&
               name[8] == 'r' && name[9] == 's' && name[10] == 'h' && name[11] == 'a' &&
               name[12] == 'l' && name[13] == 0;
    }

    /// <summary>Check if namespace equals "System.Runtime.InteropServices".</summary>
    private static bool IsInteropServicesNamespace(byte* ns)
    {
        if (ns == null) return false;
        // "System.Runtime.InteropServices" = 30 chars
        return ns[0] == 'S' && ns[1] == 'y' && ns[2] == 's' && ns[3] == 't' &&
               ns[4] == 'e' && ns[5] == 'm' && ns[6] == '.' && ns[7] == 'R' &&
               ns[8] == 'u' && ns[9] == 'n' && ns[10] == 't' && ns[11] == 'i' &&
               ns[12] == 'm' && ns[13] == 'e' && ns[14] == '.' && ns[15] == 'I' &&
               ns[16] == 'n' && ns[17] == 't' && ns[18] == 'e' && ns[19] == 'r' &&
               ns[20] == 'o' && ns[21] == 'p' && ns[22] == 'S' && ns[23] == 'e' &&
               ns[24] == 'r' && ns[25] == 'v' && ns[26] == 'i' && ns[27] == 'c' &&
               ns[28] == 'e' && ns[29] == 's' && ns[30] == 0;
    }

    /// <summary>
    /// Find and compile the default constructor (.ctor with no parameters) for a type.
    /// Returns the native code pointer or null if no default constructor exists.
    /// </summary>
    public static void* FindDefaultConstructor(MethodTable* mt)
    {
        if (mt == null)
        {
            DebugConsole.WriteLine("[FindDefCtor] mt is null");
            return null;
        }

        DebugConsole.Write("[FindDefCtor] Searching for MT 0x");
        DebugConsole.WriteHex((ulong)mt);
        DebugConsole.WriteLine();

        // Search all loaded assemblies for this MethodTable
        for (uint asmId = 0; asmId < 16; asmId++)  // Max assemblies
        {
            LoadedAssembly* asm = AssemblyLoader.GetAssembly(asmId);
            if (asm == null || !asm->IsLoaded)
                continue;

            // Search this assembly's type registry using reverse lookup
            uint typeDefToken = asm->Types.FindTokenByMT(mt);
            if (typeDefToken != 0)
            {
                uint typeDefRow = typeDefToken & 0x00FFFFFF;
                DebugConsole.Write("[FindDefCtor] Found in asm ");
                DebugConsole.WriteDecimal(asmId);
                DebugConsole.Write(" token 0x");
                DebugConsole.WriteHex(typeDefToken);
                DebugConsole.WriteLine();
                return FindAndCompileDefaultCtor(asm, typeDefRow);
            }
        }

        // If not in registry, try generic instantiation cache
        MethodTable* genDefMT = AssemblyLoader.GetGenericDefinitionMT(mt);
        if (genDefMT != null && genDefMT != mt)
        {
            // For generic instantiations, we need to find the ctor on the generic definition
            // and then the JIT-compiled version for this instantiation
            // For now, look up the definition's ctor
            DebugConsole.WriteLine("[FindDefCtor] Trying generic def MT");
            return FindDefaultConstructor(genDefMT);
        }

        // TODO: Add support for AOT types by looking up in ReflectionRuntime's type info registry
        // For now, just log and return null for unresolved types
        DebugConsole.WriteLine("[FindDefCtor] MT not found in any registry!");
        return null;
    }

    /// <summary>
    /// Find the .ctor method with no parameters on a type and compile it if needed.
    /// </summary>
    private static void* FindAndCompileDefaultCtor(LoadedAssembly* asm, uint typeDefRow)
    {
        if (asm == null)
            return null;

        // Get the method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(
            ref asm->Tables, ref asm->Sizes, typeDefRow);

        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint methodEnd;
        if (typeDefRow < typeDefCount)
            methodEnd = MetadataReader.GetTypeDefMethodList(
                ref asm->Tables, ref asm->Sizes, typeDefRow + 1);
        else
            methodEnd = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

        DebugConsole.Write("[FindAndCompileCtor] methods ");
        DebugConsole.WriteDecimal(methodStart);
        DebugConsole.Write("-");
        DebugConsole.WriteDecimal(methodEnd);
        DebugConsole.WriteLine();

        // Search for .ctor with no parameters
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            // Get method name
            uint nameIdx = MetadataReader.GetMethodDefName(
                ref asm->Tables, ref asm->Sizes, methodRow);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            // Check if it's a constructor
            if (name == null || !IsCtorName(name))
                continue;

            // Check if it has no parameters (signature: 20 00 01 = hasthis, 0 params, void)
            uint sigIdx = MetadataReader.GetMethodDefSignature(
                ref asm->Tables, ref asm->Sizes, methodRow);
            byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);

            if (sig == null || sigLen < 2)
                continue;

            // hasthis(20) + paramCount(00) = default constructor
            // The signature format is: CallingConv ParamCount RetType [ParamType...]
            // For a default ctor: 0x20 (hasthis) 0x00 (param count) 0x01 (void return)
            byte callingConv = sig[0];
            byte paramCount = sig[1];

            DebugConsole.Write("[FindAndCompileCtor] found .ctor, params=");
            DebugConsole.WriteDecimal(paramCount);
            DebugConsole.WriteLine();

            if ((callingConv & 0x20) != 0 && paramCount == 0)  // HasThis and 0 params
            {
                // Found default constructor - compile it if not already compiled
                uint ctorToken = 0x06000000 | methodRow;

                // Check if already compiled in registry - use assembly-aware lookup!
                // Without assembly ID, token collisions with other assemblies can occur.
                CompiledMethodInfo* existing = CompiledMethodRegistry.Lookup(ctorToken, asm->AssemblyId);
                if (existing != null && existing->IsCompiled && existing->NativeCode != null)
                {
                    DebugConsole.Write("[FindAndCompileCtor] Already compiled at 0x");
                    DebugConsole.WriteHex((ulong)existing->NativeCode);
                    DebugConsole.Write(" (asm ");
                    DebugConsole.WriteDecimal(asm->AssemblyId);
                    DebugConsole.WriteLine(")");
                    return existing->NativeCode;
                }

                // Set the assembly context and resolve the method
                uint savedAsmId = _currentAssemblyId;
                SetCurrentAssembly(asm->AssemblyId);

                ResolvedMethod resolved;
                bool success = ResolveMethod(ctorToken, out resolved);

                SetCurrentAssembly(savedAsmId);

                if (success && resolved.NativeCode != null)
                {
                    DebugConsole.Write("[FindAndCompileCtor] Compiled to 0x");
                    DebugConsole.WriteHex((ulong)resolved.NativeCode);
                    DebugConsole.WriteLine();
                    return resolved.NativeCode;
                }

                DebugConsole.WriteLine("[FindAndCompileCtor] Failed to compile!");
                return null;
            }
        }

        DebugConsole.WriteLine("[FindAndCompileCtor] No default ctor found!");
        return null;  // No default constructor found
    }

    /// <summary>Check if name equals ".ctor".</summary>
    private static bool IsCtorName(byte* name)
    {
        if (name == null) return false;
        // ".ctor" = 5 chars
        return name[0] == '.' && name[1] == 'c' && name[2] == 't' &&
               name[3] == 'o' && name[4] == 'r' && name[5] == 0;
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

        // Debug: trace IsTypeDefValueType for rows around Queue (250-260 in korlib)
        if (_currentAssemblyId == 1 && typeDefRow >= 250 && typeDefRow <= 260)
        {
            DebugConsole.Write("[IsTypeDefVT] row=");
            DebugConsole.WriteDecimal(typeDefRow);
            DebugConsole.Write(" ext.Table=");
            DebugConsole.WriteDecimal((uint)extendsIdx.Table);
            DebugConsole.Write(" ext.Row=");
            DebugConsole.WriteDecimal(extendsIdx.RowId);
        }

        // TypeDefOrRef: 0=TypeDef, 1=TypeRef, 2=TypeSpec
        if (extendsIdx.Table != MetadataTableId.TypeRef)
        {
            // Debug: show when returning false for non-TypeRef
            if (_currentAssemblyId == 1 && typeDefRow >= 250 && typeDefRow <= 260)
            {
                DebugConsole.Write(" -> notTypeRef, ret FALSE\n");
            }
            return false;
        }

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
        {
            if (_currentAssemblyId == 1 && typeDefRow >= 250 && typeDefRow <= 260)
            {
                DebugConsole.Write(" -> ValueType, ret TRUE\n");
            }
            return true;
        }

        if (name[0] == 'E' && name[1] == 'n' && name[2] == 'u' && name[3] == 'm' && name[4] == 0)
        {
            if (_currentAssemblyId == 1 && typeDefRow >= 250 && typeDefRow <= 260)
            {
                DebugConsole.Write(" -> Enum, ret TRUE\n");
            }
            return true;
        }

        // Debug: show when returning false (not ValueType or Enum)
        if (_currentAssemblyId == 1 && typeDefRow >= 250 && typeDefRow <= 260)
        {
            DebugConsole.Write(" -> ext=System.");
            // Print first few chars of name
            for (int i = 0; i < 10 && name[i] != 0; i++)
                DebugConsole.WriteChar((char)name[i]);
            DebugConsole.Write(", ret FALSE\n");
        }

        return false;
    }

    /// <summary>
    /// Calculate the size of a value type (struct) by summing field sizes.
    /// Handles explicit layout structs by checking ClassLayout and FieldLayout tables.
    /// </summary>
    private static uint CalculateTypeDefSize(uint typeRow)
    {
        if (_tablesHeader == null || _tableSizes == null || _metadataRoot == null)
            return 8;  // Default fallback

        // First, check ClassLayout table for an explicit size
        uint classLayoutCount = _tablesHeader->RowCounts[(int)MetadataTableId.ClassLayout];
        for (uint i = 1; i <= classLayoutCount; i++)
        {
            uint parent = MetadataReader.GetClassLayoutParent(ref *_tablesHeader, ref *_tableSizes, i);
            if (parent == typeRow)
            {
                uint explicitSize = MetadataReader.GetClassLayoutClassSize(ref *_tablesHeader, ref *_tableSizes, i);
                if (explicitSize > 0)
                    return explicitSize;
                // If ClassSize is 0, fall through to calculate from fields
                break;
            }
        }

        // Get the type's field list range
        uint firstField = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow);
        uint typeDefCount = _tablesHeader->RowCounts[(int)MetadataTableId.TypeDef];
        uint nextFieldList;
        if (typeRow < typeDefCount)
            nextFieldList = MetadataReader.GetTypeDefFieldList(ref *_tablesHeader, ref *_tableSizes, typeRow + 1);
        else
            nextFieldList = _tablesHeader->RowCounts[(int)MetadataTableId.Field] + 1;

        // Check if any field has an explicit offset (indicates explicit layout)
        // If so, calculate size as max(offset + fieldSize)
        uint maxExplicitEnd = 0;
        bool hasExplicitLayout = false;

        for (uint f = firstField; f < nextFieldList; f++)
        {
            // Skip static fields
            ushort flags = MetadataReader.GetFieldFlags(ref *_tablesHeader, ref *_tableSizes, f);
            if ((flags & 0x0010) != 0)  // fdStatic
                continue;

            // Check for explicit offset
            uint explicitOffset;
            if (HasExplicitFieldOffset(f, out explicitOffset))
            {
                hasExplicitLayout = true;

                // Get field size
                uint sigIdx = MetadataReader.GetFieldSignature(ref *_tablesHeader, ref *_tableSizes, f);
                byte* sig = MetadataReader.GetBlob(ref *_metadataRoot, sigIdx, out uint sigLen);
                uint fieldSize = 4;  // Default

                if (sig != null && sigLen >= 2 && sig[0] == 0x06)
                {
                    byte elementType = sig[1];
                    if (elementType == ElementType.ValueType || elementType == ElementType.GenericInst)
                    {
                        fieldSize = AssemblyLoader.GetFieldTypeSizeForAssembly(_currentAssemblyId, sig + 1, sigLen - 1);
                    }
                    else
                    {
                        GetFieldSizeFromElementType(elementType, out byte primSize, out _, out _);
                        fieldSize = primSize > 0 ? primSize : (uint)8;
                    }
                }

                uint fieldEnd = explicitOffset + fieldSize;
                if (fieldEnd > maxExplicitEnd)
                    maxExplicitEnd = fieldEnd;
            }
        }

        // If explicit layout, return the calculated size (aligned to 8 bytes for safety)
        if (hasExplicitLayout && maxExplicitEnd > 0)
        {
            // Align to natural alignment (max 8)
            uint alignedSize = (maxExplicitEnd + 7) & ~7u;
            return alignedSize;
        }

        // Fall back to sequential layout calculation
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
            // Base class size debug (verbose - commented out)
            // DebugConsole.Write(" base=");
            // DebugConsole.WriteDecimal(baseSize);
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

        // Check if this is a value type (struct/enum)
        // Value types accessed via byref don't have an MT pointer, so offsets start at 0
        bool isValueType = IsTypeDefValueType(typeRow);

        // Check ClassLayout table for packing size (Pack attribute)
        // Packing size of 0 or 1 means no alignment padding
        uint packingSize = 0;  // Default: natural alignment
        uint classLayoutCount = _tablesHeader->RowCounts[(int)MetadataTableId.ClassLayout];
        for (uint i = 1; i <= classLayoutCount; i++)
        {
            uint parent = MetadataReader.GetClassLayoutParent(ref *_tablesHeader, ref *_tableSizes, i);
            if (parent == typeRow)
            {
                packingSize = MetadataReader.GetClassLayoutPackingSize(ref *_tablesHeader, ref *_tableSizes, i);
                break;
            }
        }

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
                else if (elementType == 0x13)  // ELEMENT_TYPE_VAR (generic type parameter)
                {
                    // Look up the actual type argument from the generic context
                    // sig[0] = 0x06 (field calling convention)
                    // sig[1] = 0x13 (ELEMENT_TYPE_VAR)
                    // sig[2]... = var index (compressed uint)
                    byte* sigPtr = sig + 2;  // Skip 0x06 and 0x13
                    uint varIndex = MetadataReader.ReadCompressedUInt(ref sigPtr);
                    MethodTable* typeArgMT = GetTypeTypeArgMethodTable((int)varIndex);
                    if (typeArgMT != null)
                    {
                        // For value types: _uBaseSize now includes header (8 bytes)
                        // For reference types: always 8 bytes (pointer size)
                        if (typeArgMT->IsValueType)
                        {
                            // For AOT primitives, use ComponentSize if set
                            if (typeArgMT->_usComponentSize > 0)
                                size = typeArgMT->_usComponentSize;
                            else
                                size = (int)(typeArgMT->_uBaseSize >= 8 ? typeArgMT->_uBaseSize - 8 : typeArgMT->_uBaseSize);
                        }
                        else
                            size = 8;
                    }
                    else
                        size = 8;  // Fallback if no type context
                }
                else
                {
                    GetFieldSizeFromElementType(elementType, out byte primSize, out _, out _);
                    size = primSize;
                    if (size == 0)
                        size = 8;  // Default for unknown types
                }

                // Align offset to field size (capped at 8, or by packingSize if set)
                int alignment = size;
                if (alignment > 8) alignment = 8;
                // packingSize=1 means no alignment padding
                if (packingSize > 0 && packingSize < (uint)alignment)
                    alignment = (int)packingSize;
                if (alignment > 1)
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
            else if (targetElementType == 0x13)  // ELEMENT_TYPE_VAR (generic type parameter)
            {
                // Look up the actual type argument from the generic context
                // targetSig[0] = 0x06 (field calling convention)
                // targetSig[1] = 0x13 (ELEMENT_TYPE_VAR)
                // targetSig[2]... = var index (compressed uint)
                byte* sigPtr = targetSig + 2;  // Skip 0x06 and 0x13
                uint varIndex = MetadataReader.ReadCompressedUInt(ref sigPtr);
                MethodTable* typeArgMT = GetTypeTypeArgMethodTable((int)varIndex);
                if (typeArgMT != null)
                {
                    // For value types: _uBaseSize includes MT pointer (8 bytes)
                    // For reference types: always 8 bytes (pointer size)
                    if (typeArgMT->IsValueType)
                        targetSize = (int)(typeArgMT->_uBaseSize > 8 ? typeArgMT->_uBaseSize - 8 : typeArgMT->_uBaseSize);
                    else
                        targetSize = 8;
                }
                else
                    targetSize = 8;  // Fallback if no type context
            }
            else
            {
                GetFieldSizeFromElementType(targetElementType, out byte primSize, out _, out _);
                targetSize = primSize;
                if (targetSize == 0)
                    targetSize = 8;
            }

            int alignment = targetSize;
            if (alignment > 8) alignment = 8;
            // packingSize=1 means no alignment padding
            if (packingSize > 0 && packingSize < (uint)alignment)
                alignment = (int)packingSize;
            if (alignment > 1)
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
        compiler.SetMethodResolver(ResolveMethod);
    }

    /// <summary>
    /// Resolve a method token to native code, JIT compiling if necessary.
    /// This enables lazy JIT compilation of called methods.
    /// </summary>
    public static bool ResolveMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        // Debug: trace which tokens are being resolved
        uint tableId = (token >> 24) & 0xFF;
        if (tableId == 0x06)
        {
            // Only trace MethodDef tokens that might be delegate ctors
            uint methodRow = token & 0x00FFFFFF;
            // Tokens 0xF6, 0xFA, 0xFE are the delegate ctors based on error messages
            if (methodRow >= 0xF0 && methodRow <= 0xFF)
            {
                DebugConsole.Write("[ResolveMethod] MethodDef 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" asm=");
                DebugConsole.WriteDecimal(_currentAssemblyId);
                DebugConsole.WriteLine();
            }
        }

        // First check if already in registry (use assembly-aware lookup)
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(token, _currentAssemblyId);
        if (info != null)
        {
            if (info->IsCompiled)
            {
                result.NativeCode = info->NativeCode;
                result.ArgCount = info->ArgCount;
                result.ReturnKind = info->ReturnKind;
                result.ReturnStructSize = info->ReturnStructSize;
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
                // DebugConsole.Write("[MetaInt] RECURSIVE CALL detected for token 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" - using indirect call");
                result.NativeCode = null;  // Will be filled in later
                result.ArgCount = info->ArgCount;
                result.ReturnKind = info->ReturnKind;
                result.ReturnStructSize = info->ReturnStructSize;
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
        // Extract table ID from token to determine token type (reusing tableId from above)

        if (tableId == 0x06) // MethodDef token
        {
            // JIT compile the method
            if (_currentAssemblyId == 0)
            {
                // DebugConsole.WriteLine("[MetaInt] No current assembly set for JIT");
                return false;
            }

            // Check if this MethodDef belongs to an interface type
            // Interface methods are abstract and cannot be JIT compiled directly.
            // We return interface dispatch info so callvirt can resolve at runtime.
            MethodTable* interfaceMT;
            short interfaceSlot;
            if (AssemblyLoader.IsMethodDefInterfaceMethod(_currentAssemblyId, token, out interfaceMT, out interfaceSlot))
            {
                result.IsValid = true;
                result.IsInterfaceMethod = true;
                result.InterfaceMT = interfaceMT;
                result.InterfaceMethodSlot = interfaceSlot;
                result.NativeCode = null;  // Will be resolved at runtime via interface dispatch
                result.HasThis = true;  // Interface methods are always instance methods
                result.ArgCount = 0;  // Will be parsed from MethodDef signature
                result.ReturnKind = ReturnKind.IntPtr;  // Generic fallback, actual return handled by call
                result.IsVirtual = true;
                result.VtableSlot = -1;  // Not a direct vtable slot
                result.MethodTable = null;
                result.RegistryEntry = null;

                // Parse the MethodDef signature to get arg count and return type
                ParseMethodDefSignature(_currentAssemblyId, token, ref result);

                return true;
            }

            // Check if this MethodDef is a delegate constructor
            // Delegates have "runtime managed" constructors with no IL body
            // We return delegate info so newobj can generate runtime dispatch code
            MethodTable* delegateMT;
            int delegateArgCount;
            if (AssemblyLoader.IsDelegateConstructor(_currentAssemblyId, token, out delegateMT, out delegateArgCount))
            {
                DebugConsole.Write("[ResolveMethod] Delegate ctor detected 0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" MT=0x");
                DebugConsole.WriteHex((ulong)delegateMT);
                DebugConsole.Write(" args=");
                DebugConsole.WriteDecimal((uint)delegateArgCount);
                DebugConsole.WriteLine();
                result.IsValid = true;
                result.NativeCode = null;  // No native code - runtime handled
                result.HasThis = true;  // Constructor is instance method
                result.ArgCount = (byte)delegateArgCount;
                result.ReturnKind = ReturnKind.Void;  // Constructors return void
                result.MethodTable = delegateMT;  // Important: set MT so newobj can detect delegate
                result.RegistryEntry = null;
                result.IsVirtual = false;
                result.VtableSlot = -1;
                return true;
            }

            // Check if this MethodDef is a delegate Invoke method
            // Delegates have "runtime managed" Invoke methods with no IL body
            // We return delegate invoke info so callvirt can generate runtime dispatch code
            int invokeArgCount;
            ReturnKind invokeReturnKind;
            if (AssemblyLoader.IsDelegateInvokeMethodDef(_currentAssemblyId, token, out delegateMT, out invokeArgCount, out invokeReturnKind))
            {
                result.IsValid = true;
                result.IsDelegateInvoke = true;
                result.NativeCode = null;  // No native code - runtime dispatched
                result.HasThis = true;  // Invoke is always instance method
                result.ArgCount = (byte)invokeArgCount;
                result.ReturnKind = invokeReturnKind;
                result.MethodTable = delegateMT;
                result.RegistryEntry = null;
                result.IsVirtual = false;
                result.VtableSlot = -1;
                result.IsInterfaceMethod = false;
                result.InterfaceMT = null;
                result.InterfaceMethodSlot = -1;
                return true;
            }

            // Check if this is a korlib MethodDef that can be resolved to AOT native code.
            // When JIT compiling korlib.dll methods (e.g., List<T>.ctor), internal calls to
            // other korlib methods (e.g., Exception.ctor) need to be resolved to AOT code
            // instead of being JIT compiled.
            if (TryResolveKorlibAotMethodDef(token, out result))
            {
                return true;
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
                // DebugConsole.Write("[MetaInt] Failed to JIT compile method 0x");
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
            result.ReturnStructSize = info->ReturnStructSize;
            result.HasThis = info->HasThis;
            result.IsValid = true;
            result.IsVirtual = info->IsVirtual;
            result.VtableSlot = info->VtableSlot;
            result.MethodTable = info->MethodTable;
            result.IsInterfaceMethod = info->IsInterfaceMethod;
            result.InterfaceMT = info->InterfaceMT;
            result.InterfaceMethodSlot = info->InterfaceMethodSlot;
            result.RegistryEntry = info;  // Set registry entry

            // Check if this is a vararg method (has VARARG calling convention)
            // This is needed even for direct calls with 0 varargs
            // Only set IsVarargMethod, don't overwrite other fields from registry
            CheckVarargMethod(_currentAssemblyId, token, ref result);

            return true;
        }
        else if (tableId == 0x0A) // MemberRef token
        {
            // External method reference - resolve through the assembly's references
            return ResolveMemberRefMethod(token, out result);
        }
        else if (tableId == 0x2B) // MethodSpec token
        {
            // Generic method instantiation - resolve through MethodSpec handler
            return ResolveMethodSpecMethod(token, out result);
        }
        else
        {
            DebugConsole.Write("[MetaInt] Unknown method token table: 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }
    }

    private static void TraceMemberRef(uint methodToken, uint asmId, ReturnKind kind, byte argCount)
    {
        if (asmId != 3 || _metadataRoot == null || _tablesHeader == null || _tableSizes == null)
            return;

        uint rid = methodToken & 0x00FFFFFF;
        uint nameIdx = MetadataReader.GetMethodDefName(ref *_tablesHeader, ref *_tableSizes, rid);
        byte* name = MetadataReader.GetString(ref *_metadataRoot, nameIdx);

        // if (NameEquals(name, "Initialize"))
        // {
        //     DebugConsole.Write("[MetaInt] MemberRef Initialize token=0x");
        //     DebugConsole.WriteHex(methodToken);
        //     DebugConsole.Write(" returnKind=");
        //     DebugConsole.WriteDecimal((uint)kind);
        //     DebugConsole.Write(" argCount=");
        //     DebugConsole.WriteDecimal(argCount);
        //     DebugConsole.Write(" asm=");
        //     DebugConsole.WriteDecimal(asmId);
        //     DebugConsole.WriteLine();
        // }
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

    // Cached MethodTable for Span/ReadOnlySpan types (they all have the same layout)
    private static MethodTable* _spanMT = null;

    /// <summary>
    /// Get the MethodTable for a type given its full name (e.g., "System.AggregateException").
    /// Used when resolving AOT constructors to provide the MethodTable for newobj allocation.
    /// </summary>
    private static void* GetMethodTableForTypeName(byte* typeName)
    {
        // Special case for ArgIterator - uses cached MT
        if (NameEquals(typeName, "System.ArgIterator"))
            return _argIteratorMT;

        // Special case for Span and ReadOnlySpan - they are value types with fixed size
        // Layout: pointer (8 bytes) + length (4 bytes) = 12 bytes, padded to 16 bytes
        if (NameEquals(typeName, "System.Span`1"))
        {
            // First check if already registered via well-known type
            MethodTable* registered = LookupType(WellKnownTypes.Span);
            if (registered != null)
                return registered;

            if (_spanMT == null)
            {
                // Create a minimal MethodTable for span types
                _spanMT = (MethodTable*)HeapAllocator.AllocZeroed((nuint)sizeof(MethodTable));
                if (_spanMT != null)
                {
                    // Span layout: pointer(8) + length(4) = 12, aligned to 16 bytes actual size
                    // Add 8 for object header to be consistent with JIT's BaseSize - 8 formula
                    _spanMT->_uBaseSize = 24;  // 16 (actual) + 8 (header)
                    // MTFlags.IsValueType is 0x00200000, high 16 bits go in _usFlags
                    _spanMT->_usFlags = (ushort)(MTFlags.IsValueType >> 16);  // 0x0020
                    _spanMT->_usNumVtableSlots = 0;
                    _spanMT->_usNumInterfaces = 0;
                    // Register so all paths use the same MT
                    RegisterType(WellKnownTypes.Span, _spanMT);
                }
            }
            return _spanMT;
        }

        if (NameEquals(typeName, "System.ReadOnlySpan`1"))
        {
            // First check if already registered via well-known type
            MethodTable* registered = LookupType(WellKnownTypes.ReadOnlySpan);
            if (registered != null)
                return registered;

            if (_spanMT == null)
            {
                // Create a minimal MethodTable for span types
                _spanMT = (MethodTable*)HeapAllocator.AllocZeroed((nuint)sizeof(MethodTable));
                if (_spanMT != null)
                {
                    // Span layout: pointer(8) + length(4) = 12, aligned to 16 bytes actual size
                    // Add 8 for object header to be consistent with JIT's BaseSize - 8 formula
                    _spanMT->_uBaseSize = 24;  // 16 (actual) + 8 (header)
                    // MTFlags.IsValueType is 0x00200000, high 16 bits go in _usFlags
                    _spanMT->_usFlags = (ushort)(MTFlags.IsValueType >> 16);  // 0x0020
                    _spanMT->_usNumVtableSlots = 0;
                    _spanMT->_usNumInterfaces = 0;
                    // Register so all paths use the same MT
                    RegisterType(WellKnownTypes.ReadOnlySpan, _spanMT);
                }
            }
            return _spanMT;
        }

        // Map well-known type names to their tokens
        uint wellKnownToken = 0;

        if (NameEquals(typeName, "System.Object"))
            wellKnownToken = WellKnownTypes.Object;
        else if (NameEquals(typeName, "System.String"))
            wellKnownToken = WellKnownTypes.String;
        else if (NameEquals(typeName, "System.Exception"))
            wellKnownToken = WellKnownTypes.Exception;
        else if (NameEquals(typeName, "System.ArgumentException"))
            wellKnownToken = WellKnownTypes.ArgumentException;
        else if (NameEquals(typeName, "System.ArgumentNullException"))
            wellKnownToken = WellKnownTypes.ArgumentNullException;
        else if (NameEquals(typeName, "System.ArgumentOutOfRangeException"))
            wellKnownToken = WellKnownTypes.ArgumentOutOfRangeException;
        else if (NameEquals(typeName, "System.InvalidOperationException"))
            wellKnownToken = WellKnownTypes.InvalidOperationException;
        else if (NameEquals(typeName, "System.NotSupportedException"))
            wellKnownToken = WellKnownTypes.NotSupportedException;
        else if (NameEquals(typeName, "System.NotImplementedException"))
            wellKnownToken = WellKnownTypes.NotImplementedException;
        else if (NameEquals(typeName, "System.IndexOutOfRangeException"))
            wellKnownToken = WellKnownTypes.IndexOutOfRangeException;
        else if (NameEquals(typeName, "System.NullReferenceException"))
            wellKnownToken = WellKnownTypes.NullReferenceException;
        else if (NameEquals(typeName, "System.InvalidCastException"))
            wellKnownToken = WellKnownTypes.InvalidCastException;
        else if (NameEquals(typeName, "System.FormatException"))
            wellKnownToken = WellKnownTypes.FormatException;
        else if (NameEquals(typeName, "System.DivideByZeroException"))
            wellKnownToken = WellKnownTypes.DivideByZeroException;
        else if (NameEquals(typeName, "System.OverflowException"))
            wellKnownToken = WellKnownTypes.OverflowException;
        else if (NameEquals(typeName, "System.AggregateException"))
            wellKnownToken = WellKnownTypes.AggregateException;
        else if (NameEquals(typeName, "System.Delegate"))
            wellKnownToken = WellKnownTypes.Delegate;
        else if (NameEquals(typeName, "System.MulticastDelegate"))
            wellKnownToken = WellKnownTypes.MulticastDelegate;
        else if (NameEquals(typeName, "System.Type"))
            wellKnownToken = WellKnownTypes.Type;
        else if (NameEquals(typeName, "System.RuntimeType"))
            wellKnownToken = WellKnownTypes.RuntimeType;
        else if (NameEquals(typeName, "System.ValueType"))
            wellKnownToken = WellKnownTypes.ValueType;
        else if (NameEquals(typeName, "System.Enum"))
            wellKnownToken = WellKnownTypes.Enum;
        else if (NameEquals(typeName, "System.Array"))
            wellKnownToken = WellKnownTypes.Array;
        // Primitive types
        else if (NameEquals(typeName, "System.Int32"))
            wellKnownToken = WellKnownTypes.Int32;
        else if (NameEquals(typeName, "System.Int64"))
            wellKnownToken = WellKnownTypes.Int64;
        else if (NameEquals(typeName, "System.Boolean"))
            wellKnownToken = WellKnownTypes.Boolean;
        else if (NameEquals(typeName, "System.Byte"))
            wellKnownToken = WellKnownTypes.Byte;
        else if (NameEquals(typeName, "System.Char"))
            wellKnownToken = WellKnownTypes.Char;
        else if (NameEquals(typeName, "System.Double"))
            wellKnownToken = WellKnownTypes.Double;
        else if (NameEquals(typeName, "System.Single"))
            wellKnownToken = WellKnownTypes.Single;
        else if (NameEquals(typeName, "System.Int16"))
            wellKnownToken = WellKnownTypes.Int16;
        else if (NameEquals(typeName, "System.UInt16"))
            wellKnownToken = WellKnownTypes.UInt16;
        else if (NameEquals(typeName, "System.UInt32"))
            wellKnownToken = WellKnownTypes.UInt32;
        else if (NameEquals(typeName, "System.UInt64"))
            wellKnownToken = WellKnownTypes.UInt64;
        else if (NameEquals(typeName, "System.IntPtr"))
            wellKnownToken = WellKnownTypes.IntPtr;
        else if (NameEquals(typeName, "System.UIntPtr"))
            wellKnownToken = WellKnownTypes.UIntPtr;
        else if (NameEquals(typeName, "System.SByte"))
            wellKnownToken = WellKnownTypes.SByte;

        if (wellKnownToken != 0)
            return LookupType(wellKnownToken);

        return null;
    }

    /// <summary>
    /// Check if a type name is System.Object (for vtable slot determination).
    /// </summary>
    private static bool IsObjectMethod(byte* typeName)
    {
        return NameEquals(typeName, "System.Object");
    }

    /// <summary>
    /// Get the vtable slot for an Object virtual method.
    /// Slot 0: ToString, Slot 1: Equals, Slot 2: GetHashCode
    /// </summary>
    private static short GetObjectMethodVtableSlot(byte* methodName)
    {
        if (NameEquals(methodName, "ToString")) return 0;
        if (NameEquals(methodName, "Equals")) return 1;
        if (NameEquals(methodName, "GetHashCode")) return 2;
        return -1;  // Unknown virtual method
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

        // Get stats from block chains
        int typeEntries, typeBlocks, typeCapacity;
        int staticFieldEntries, staticFieldBlocks, staticFieldCapacity;
        int fieldLayoutEntries, fieldLayoutBlocks, fieldLayoutCapacity;
        int cctorEntries, cctorBlocks, cctorCapacity;

        fixed (BlockChain* chain = &_typeChain)
            BlockAllocator.GetStats(chain, out typeEntries, out typeBlocks, out typeCapacity);
        fixed (BlockChain* chain = &_staticFieldChain)
            BlockAllocator.GetStats(chain, out staticFieldEntries, out staticFieldBlocks, out staticFieldCapacity);
        fixed (BlockChain* chain = &_fieldLayoutChain)
            BlockAllocator.GetStats(chain, out fieldLayoutEntries, out fieldLayoutBlocks, out fieldLayoutCapacity);
        fixed (BlockChain* chain = &_cctorChain)
            BlockAllocator.GetStats(chain, out cctorEntries, out cctorBlocks, out cctorCapacity);

        DebugConsole.Write("  Types registered: ");
        DebugConsole.WriteDecimal((uint)typeEntries);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)typeBlocks);
        DebugConsole.WriteLine(" blocks)");

        DebugConsole.Write("  Static fields: ");
        DebugConsole.WriteDecimal((uint)staticFieldEntries);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)staticFieldBlocks);
        DebugConsole.WriteLine(" blocks)");

        DebugConsole.Write("  Static storage used: ");
        DebugConsole.WriteDecimal((uint)_staticStorageUsed);
        DebugConsole.Write("/");
        DebugConsole.WriteDecimal(StaticStorageBlockSize);
        DebugConsole.WriteLine();

        DebugConsole.Write("  Field layouts cached: ");
        DebugConsole.WriteDecimal((uint)fieldLayoutEntries);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)fieldLayoutBlocks);
        DebugConsole.WriteLine(" blocks)");

        DebugConsole.Write("  Cctor contexts: ");
        DebugConsole.WriteDecimal((uint)cctorEntries);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((uint)cctorBlocks);
        DebugConsole.WriteLine(" blocks)");
    }

    /// <summary>
    /// Parse a MemberRef signature to get arg count and return type for delegate Invoke.
    /// </summary>
    private static void ParseMemberRefSignatureForDelegate(uint asmId, uint memberRefToken, ref ResolvedMethod result)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(asmId);
        if (asm == null)
            return;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return;

        uint sigIdx = MetadataReader.GetMemberRefSignature(ref asm->Tables, ref asm->Sizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 2)
            return;

        // Parse method signature
        int sigPos = 0;
        byte callConv = sig[sigPos++];
        result.HasThis = (callConv & 0x20) != 0;

        // Decode compressed parameter count
        byte b = sig[sigPos++];
        uint paramCount = 0;
        if ((b & 0x80) == 0)
            paramCount = b;
        else if ((b & 0xC0) == 0x80)
            paramCount = (uint)(((b & 0x3F) << 8) | sig[sigPos++]);
        else if ((b & 0xE0) == 0xC0)
        {
            paramCount = (uint)(((b & 0x1F) << 24) | (sig[sigPos] << 16) | (sig[sigPos + 1] << 8) | sig[sigPos + 2]);
            sigPos += 3;
        }

        result.ArgCount = (byte)paramCount;

        // Parse return type
        if (sigPos < sigLen)
        {
            byte retType = sig[sigPos];

            // Map ECMA-335 element types to ReturnKind
            switch (retType)
            {
                case 0x01: // ELEMENT_TYPE_VOID
                    result.ReturnKind = ReturnKind.Void;
                    break;
                case 0x02: // ELEMENT_TYPE_BOOLEAN
                case 0x03: // ELEMENT_TYPE_CHAR
                case 0x04: // ELEMENT_TYPE_I1
                case 0x05: // ELEMENT_TYPE_U1
                case 0x06: // ELEMENT_TYPE_I2
                case 0x07: // ELEMENT_TYPE_U2
                case 0x08: // ELEMENT_TYPE_I4
                case 0x09: // ELEMENT_TYPE_U4
                    result.ReturnKind = ReturnKind.Int32;
                    break;
                case 0x0A: // ELEMENT_TYPE_I8
                case 0x0B: // ELEMENT_TYPE_U8
                    result.ReturnKind = ReturnKind.Int64;
                    break;
                case 0x0C: // ELEMENT_TYPE_R4
                    result.ReturnKind = ReturnKind.Float32;
                    break;
                case 0x0D: // ELEMENT_TYPE_R8
                    result.ReturnKind = ReturnKind.Float64;
                    break;
                case 0x18: // ELEMENT_TYPE_I (native int)
                case 0x19: // ELEMENT_TYPE_U (native uint)
                case 0x1C: // ELEMENT_TYPE_OBJECT
                case 0x0E: // ELEMENT_TYPE_STRING
                case 0x12: // ELEMENT_TYPE_CLASS
                case 0x14: // ELEMENT_TYPE_SZARRAY
                    result.ReturnKind = ReturnKind.IntPtr;
                    break;
                case 0x16: // ELEMENT_TYPE_TYPEDBYREF - TypedReference is a 16-byte struct
                    result.ReturnKind = ReturnKind.Struct;
                    result.ReturnStructSize = 16;
                    break;
                default:
                    result.ReturnKind = ReturnKind.IntPtr;
                    break;
            }
        }
    }

    /// <summary>
    /// Check if a MethodDef has VARARG calling convention.
    /// Only sets IsVarargMethod, does not modify other fields.
    /// </summary>
    private static void CheckVarargMethod(uint asmId, uint methodDefToken, ref ResolvedMethod result)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(asmId);
        if (asm == null)
            return;

        uint rowId = methodDefToken & 0x00FFFFFF;
        if (rowId == 0)
            return;

        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 1)
            return;

        // Read calling convention and check for VARARG (0x05)
        byte callConv = sig[0];
        result.IsVarargMethod = (callConv & 0x0F) == 0x05;
    }

    /// <summary>
    /// Parse a MethodDef signature to get arg count and return type for interface methods.
    /// Also detects VARARG calling convention.
    /// </summary>
    private static void ParseMethodDefSignature(uint asmId, uint methodDefToken, ref ResolvedMethod result)
    {
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(asmId);
        if (asm == null)
            return;

        uint rowId = methodDefToken & 0x00FFFFFF;
        if (rowId == 0)
            return;

        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 2)
            return;

        // Parse method signature
        int sigPos = 0;
        byte callConv = sig[sigPos++];
        result.HasThis = (callConv & 0x20) != 0;

        // Check for VARARG calling convention (0x05)
        result.IsVarargMethod = (callConv & 0x0F) == 0x05;

        // Decode compressed parameter count
        byte b = sig[sigPos++];
        uint paramCount = 0;
        if ((b & 0x80) == 0)
            paramCount = b;
        else if ((b & 0xC0) == 0x80)
            paramCount = (uint)(((b & 0x3F) << 8) | sig[sigPos++]);
        else if ((b & 0xE0) == 0xC0)
        {
            paramCount = (uint)(((b & 0x1F) << 24) | (sig[sigPos] << 16) | (sig[sigPos + 1] << 8) | sig[sigPos + 2]);
            sigPos += 3;
        }

        result.ArgCount = (byte)paramCount;

        // Parse return type
        if (sigPos < sigLen)
        {
            byte retType = sig[sigPos];

            // Map ECMA-335 element types to ReturnKind
            switch (retType)
            {
                case 0x01: // ELEMENT_TYPE_VOID
                    result.ReturnKind = ReturnKind.Void;
                    break;
                case 0x02: // ELEMENT_TYPE_BOOLEAN
                case 0x03: // ELEMENT_TYPE_CHAR
                case 0x04: // ELEMENT_TYPE_I1
                case 0x05: // ELEMENT_TYPE_U1
                case 0x06: // ELEMENT_TYPE_I2
                case 0x07: // ELEMENT_TYPE_U2
                case 0x08: // ELEMENT_TYPE_I4
                case 0x09: // ELEMENT_TYPE_U4
                    result.ReturnKind = ReturnKind.Int32;
                    break;
                case 0x0A: // ELEMENT_TYPE_I8
                case 0x0B: // ELEMENT_TYPE_U8
                    result.ReturnKind = ReturnKind.Int64;
                    break;
                case 0x0C: // ELEMENT_TYPE_R4
                    result.ReturnKind = ReturnKind.Float32;
                    break;
                case 0x0D: // ELEMENT_TYPE_R8
                    result.ReturnKind = ReturnKind.Float64;
                    break;
                case 0x18: // ELEMENT_TYPE_I (native int)
                case 0x19: // ELEMENT_TYPE_U (native uint)
                case 0x1C: // ELEMENT_TYPE_OBJECT
                case 0x0E: // ELEMENT_TYPE_STRING
                case 0x12: // ELEMENT_TYPE_CLASS
                case 0x14: // ELEMENT_TYPE_SZARRAY
                    result.ReturnKind = ReturnKind.IntPtr;
                    break;
                case 0x16: // ELEMENT_TYPE_TYPEDBYREF - TypedReference is a 16-byte struct
                    result.ReturnKind = ReturnKind.Struct;
                    result.ReturnStructSize = 16;
                    break;
                default:
                    result.ReturnKind = ReturnKind.IntPtr;
                    break;
            }
        }
    }

    // Buffer for storing vararg MethodTable pointers (max 16 varargs)
    private const int MaxVarargs = 16;
    private static MethodTable** _varargMTBuffer;

    /// <summary>
    /// Parse vararg info from a MemberRef signature.
    /// Detects if the signature has SENTINEL (varargs) and gets the MethodTable for each vararg type.
    /// </summary>
    private static void ParseVarargInfo(uint asmId, uint memberRefToken, ref ResolvedMethod result)
    {
        result.IsVarargCall = false;
        result.VarargCount = 0;
        result.VarargMTs = null;

        LoadedAssembly* asm = AssemblyLoader.GetAssembly(asmId);
        if (asm == null)
            return;

        uint rowId = memberRefToken & 0x00FFFFFF;
        if (rowId == 0)
            return;

        uint sigIdx = MetadataReader.GetMemberRefSignature(ref asm->Tables, ref asm->Sizes, rowId);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 3)
            return;

        // Parse the signature looking for SENTINEL (0x41)
        int sigPos = 0;
        byte callConv = sig[sigPos++];

        // Decode compressed parameter count
        uint paramCount = DecodeCompressedUint(sig, ref sigPos, (int)sigLen);

        // Skip return type
        SkipTypeInSignature(sig, ref sigPos, (int)sigLen);

        // Scan through declared params looking for SENTINEL
        uint declaredParams = 0;
        while (sigPos < sigLen && declaredParams < paramCount)
        {
            if (sig[sigPos] == ElementType.Sentinel)
            {
                // Found SENTINEL - everything after this is varargs
                sigPos++;  // Skip SENTINEL

                // Count and parse vararg types
                int varargStartPos = sigPos;
                byte varargCount = 0;

                // First pass: count varargs
                int countPos = sigPos;
                while (countPos < sigLen && varargCount < MaxVarargs)
                {
                    SkipTypeInSignature(sig, ref countPos, (int)sigLen);
                    varargCount++;
                }

                // Mark this as a vararg call even when varargCount == 0
                // We need to emit the sentinel TypedReference in all cases
                result.IsVarargCall = true;
                result.VarargCount = varargCount;

                if (varargCount > 0)
                {
                    // Allocate buffer for vararg MTs if needed
                    if (_varargMTBuffer == null)
                    {
                        _varargMTBuffer = (MethodTable**)HeapAllocator.AllocZeroed((ulong)(MaxVarargs * sizeof(MethodTable*)));
                    }

                    // Second pass: get MethodTable for each vararg type
                    sigPos = varargStartPos;
                    for (int i = 0; i < varargCount; i++)
                    {
                        _varargMTBuffer[i] = GetMethodTableForSignatureType(sig, ref sigPos, (int)sigLen, asmId);
                    }

                    result.VarargMTs = (void**)_varargMTBuffer;
                }

                DebugConsole.Write("[Vararg] Found ");
                DebugConsole.WriteDecimal(varargCount);
                DebugConsole.Write(" varargs for MemberRef 0x");
                DebugConsole.WriteHex(memberRefToken);
                DebugConsole.WriteLine();
                return;
            }
            // Skip this declared parameter type
            SkipTypeInSignature(sig, ref sigPos, (int)sigLen);
            declaredParams++;
        }
    }

    /// <summary>
    /// Decode a compressed unsigned integer from a signature blob.
    /// </summary>
    private static uint DecodeCompressedUint(byte* sig, ref int pos, int maxLen)
    {
        if (pos >= maxLen)
            return 0;

        byte b = sig[pos++];
        if ((b & 0x80) == 0)
            return b;
        if ((b & 0xC0) == 0x80)
        {
            if (pos >= maxLen) return 0;
            return (uint)(((b & 0x3F) << 8) | sig[pos++]);
        }
        if ((b & 0xE0) == 0xC0)
        {
            if (pos + 2 >= maxLen) return 0;
            uint val = (uint)(((b & 0x1F) << 24) | (sig[pos] << 16) | (sig[pos + 1] << 8) | sig[pos + 2]);
            pos += 3;
            return val;
        }
        return 0;
    }

    /// <summary>
    /// Skip a type in a signature blob (advances pos past the type).
    /// </summary>
    private static void SkipTypeInSignature(byte* sig, ref int pos, int maxLen)
    {
        if (pos >= maxLen)
            return;

        byte elemType = sig[pos++];

        switch (elemType)
        {
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
            case 0x18: // I (native int)
            case 0x19: // U (native uint)
            case 0x1C: // OBJECT
            case 0x16: // TYPEDBYREF
                // Simple types - no additional data
                break;

            case 0x0F: // PTR
            case 0x10: // BYREF
            case 0x1D: // SZARRAY
            case 0x45: // PINNED
                // These are followed by another type
                SkipTypeInSignature(sig, ref pos, maxLen);
                break;

            case 0x11: // VALUETYPE
            case 0x12: // CLASS
                // Followed by TypeDefOrRefOrSpecEncoded
                DecodeCompressedUint(sig, ref pos, maxLen);
                break;

            case 0x14: // ARRAY
                // Skip element type, then rank, then sizes and lower bounds
                SkipTypeInSignature(sig, ref pos, maxLen);
                uint rank = DecodeCompressedUint(sig, ref pos, maxLen);
                uint numSizes = DecodeCompressedUint(sig, ref pos, maxLen);
                for (uint i = 0; i < numSizes; i++)
                    DecodeCompressedUint(sig, ref pos, maxLen);
                uint numLoBounds = DecodeCompressedUint(sig, ref pos, maxLen);
                for (uint i = 0; i < numLoBounds; i++)
                    DecodeCompressedUint(sig, ref pos, maxLen);
                break;

            case 0x15: // GENERICINST
                // Skip generic type kind (CLASS/VALUETYPE), then base type, then type args
                pos++;  // Skip CLASS or VALUETYPE
                DecodeCompressedUint(sig, ref pos, maxLen);  // Skip type token
                uint typeArgCount = DecodeCompressedUint(sig, ref pos, maxLen);
                for (uint i = 0; i < typeArgCount; i++)
                    SkipTypeInSignature(sig, ref pos, maxLen);
                break;

            case 0x13: // VAR (type parameter)
            case 0x1E: // MVAR (method type parameter)
                DecodeCompressedUint(sig, ref pos, maxLen);
                break;

            case 0x1B: // FNPTR
                // Skip the entire method signature
                // Skip calling convention
                pos++;
                uint fnParamCount = DecodeCompressedUint(sig, ref pos, maxLen);
                SkipTypeInSignature(sig, ref pos, maxLen);  // Return type
                for (uint i = 0; i < fnParamCount; i++)
                    SkipTypeInSignature(sig, ref pos, maxLen);
                break;

            case 0x20: // CMOD_OPT
            case 0x1F: // CMOD_REQD
                DecodeCompressedUint(sig, ref pos, maxLen);  // Skip modifier type
                SkipTypeInSignature(sig, ref pos, maxLen);   // Skip actual type
                break;

            default:
                // Unknown type - best effort skip
                break;
        }
    }

    /// <summary>
    /// Get the MethodTable for a type encoded in a signature.
    /// </summary>
    private static MethodTable* GetMethodTableForSignatureType(byte* sig, ref int pos, int maxLen, uint asmId)
    {
        if (pos >= maxLen)
            return null;

        byte elemType = sig[pos++];

        switch (elemType)
        {
            case 0x02: // BOOLEAN
                return LookupType(WellKnownTypes.Boolean);
            case 0x03: // CHAR
                return LookupType(WellKnownTypes.Char);
            case 0x04: // I1
                return LookupType(WellKnownTypes.SByte);
            case 0x05: // U1
                return LookupType(WellKnownTypes.Byte);
            case 0x06: // I2
                return LookupType(WellKnownTypes.Int16);
            case 0x07: // U2
                return LookupType(WellKnownTypes.UInt16);
            case 0x08: // I4
                return LookupType(WellKnownTypes.Int32);
            case 0x09: // U4
                return LookupType(WellKnownTypes.UInt32);
            case 0x0A: // I8
                return LookupType(WellKnownTypes.Int64);
            case 0x0B: // U8
                return LookupType(WellKnownTypes.UInt64);
            case 0x0C: // R4
                return LookupType(WellKnownTypes.Single);
            case 0x0D: // R8
                return LookupType(WellKnownTypes.Double);
            case 0x18: // I (native int)
                return LookupType(WellKnownTypes.IntPtr);
            case 0x19: // U (native uint)
                return LookupType(WellKnownTypes.UIntPtr);
            case 0x0E: // STRING
                return LookupType(WellKnownTypes.String);
            case 0x1C: // OBJECT
                return LookupType(WellKnownTypes.Object);

            case 0x11: // VALUETYPE
            case 0x12: // CLASS
            {
                // Decode TypeDefOrRefOrSpecEncoded token
                uint codedToken = DecodeCompressedUint(sig, ref pos, maxLen);
                // Decode coded index: low 2 bits are table tag (0=TypeDef, 1=TypeRef, 2=TypeSpec)
                uint tableTag = codedToken & 0x3;
                uint rowId = codedToken >> 2;
                uint token;
                if (tableTag == 0)
                    token = 0x02000000 | rowId;  // TypeDef
                else if (tableTag == 1)
                    token = 0x01000000 | rowId;  // TypeRef
                else
                    token = 0x1B000000 | rowId;  // TypeSpec

                // Look up the MethodTable for this type
                return AssemblyLoader.ResolveType(asmId, token);
            }

            default:
                // For complex types, skip them and return Object MT as fallback
                pos--;  // Put back the element type
                SkipTypeInSignature(sig, ref pos, maxLen);
                return LookupType(WellKnownTypes.Object);
        }
    }

    /// <summary>
    /// Parse a StandAloneSig token for calli instruction.
    /// Returns the argument count, return kind, and whether the method has 'this'.
    /// </summary>
    /// <param name="sigToken">StandAloneSig token (0x11xxxxxx)</param>
    /// <param name="argCount">Output: number of arguments</param>
    /// <param name="returnKind">Output: return value kind</param>
    /// <param name="hasThis">Output: true if instance call (has 'this' parameter)</param>
    /// <returns>True if parsing succeeded</returns>
    public static bool ParseCalliSignature(uint sigToken, out int argCount, out ReturnKind returnKind, out bool hasThis)
    {
        argCount = 0;
        returnKind = ReturnKind.Void;
        hasThis = false;

        // StandAloneSig token format: 0x11xxxxxx
        uint tableId = (sigToken >> 24) & 0xFF;
        uint rid = sigToken & 0x00FFFFFF;

        if (tableId != 0x11 || rid == 0)
        {
            DebugConsole.Write("[ParseCalliSig] Invalid token 0x");
            DebugConsole.WriteHex(sigToken);
            DebugConsole.WriteLine();
            return false;
        }

        // Get the assembly - use current assembly context
        LoadedAssembly* asm = AssemblyLoader.GetAssembly(_currentAssemblyId);
        if (asm == null)
        {
            DebugConsole.WriteLine("[ParseCalliSig] No current assembly");
            return false;
        }

        // Get signature blob from StandAloneSig table
        uint sigIdx = MetadataReader.GetStandAloneSigSignature(ref asm->Tables, ref asm->Sizes, rid);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);

        if (sig == null || sigLen < 2)
        {
            DebugConsole.WriteLine("[ParseCalliSig] Invalid signature blob");
            return false;
        }

        // Parse call signature
        // Format: CallingConvention ParamCount RetType [ParamTypes...]
        int sigPos = 0;
        byte callConv = sig[sigPos++];

        // Check for HASTHIS (0x20) - instance method
        hasThis = (callConv & 0x20) != 0;

        // Decode compressed parameter count
        byte b = sig[sigPos++];
        uint paramCount = 0;
        if ((b & 0x80) == 0)
            paramCount = b;
        else if ((b & 0xC0) == 0x80)
            paramCount = (uint)(((b & 0x3F) << 8) | sig[sigPos++]);
        else if ((b & 0xE0) == 0xC0)
        {
            paramCount = (uint)(((b & 0x1F) << 24) | (sig[sigPos] << 16) | (sig[sigPos + 1] << 8) | sig[sigPos + 2]);
            sigPos += 3;
        }

        argCount = (int)paramCount;

        // Parse return type
        if (sigPos < sigLen)
        {
            byte retType = sig[sigPos];

            // Map ECMA-335 element types to ReturnKind
            switch (retType)
            {
                case 0x01: // ELEMENT_TYPE_VOID
                    returnKind = ReturnKind.Void;
                    break;
                case 0x02: // ELEMENT_TYPE_BOOLEAN
                case 0x03: // ELEMENT_TYPE_CHAR
                case 0x04: // ELEMENT_TYPE_I1
                case 0x05: // ELEMENT_TYPE_U1
                case 0x06: // ELEMENT_TYPE_I2
                case 0x07: // ELEMENT_TYPE_U2
                case 0x08: // ELEMENT_TYPE_I4
                case 0x09: // ELEMENT_TYPE_U4
                    returnKind = ReturnKind.Int32;
                    break;
                case 0x0A: // ELEMENT_TYPE_I8
                case 0x0B: // ELEMENT_TYPE_U8
                    returnKind = ReturnKind.Int64;
                    break;
                case 0x0C: // ELEMENT_TYPE_R4
                    returnKind = ReturnKind.Float32;
                    break;
                case 0x0D: // ELEMENT_TYPE_R8
                    returnKind = ReturnKind.Float64;
                    break;
                case 0x18: // ELEMENT_TYPE_I (native int)
                case 0x19: // ELEMENT_TYPE_U (native uint)
                case 0x1C: // ELEMENT_TYPE_OBJECT
                case 0x0E: // ELEMENT_TYPE_STRING
                case 0x12: // ELEMENT_TYPE_CLASS
                case 0x0F: // ELEMENT_TYPE_PTR
                case 0x14: // ELEMENT_TYPE_SZARRAY
                default:
                    returnKind = ReturnKind.IntPtr;
                    break;
            }
        }

        return true;
    }

    // ========================================================================
    // Static Constructor (cctor) Registry
    // ========================================================================

    /// <summary>
    /// Register a static constructor context for a type.
    /// Called when a type with a .cctor is loaded.
    /// </summary>
    /// <param name="assemblyId">Assembly ID containing the type.</param>
    /// <param name="typeToken">TypeDef token (0x02xxxxxx).</param>
    /// <param name="typeArgHash">Hash of type arguments for generic instantiations.</param>
    /// <param name="contextAddress">Address of StaticClassConstructionContext.</param>
    public static void RegisterCctorContext(uint assemblyId, uint typeToken, ulong typeArgHash, nint* contextAddress)
    {
        if (!_initialized)
            Initialize();

        // Check if already registered - iterate through blocks
        fixed (BlockChain* chain = &_cctorChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (CctorRegistryEntry*)block->GetEntry(i);
                    if (entry->AssemblyId == assemblyId && entry->TypeToken == typeToken && entry->TypeArgHash == typeArgHash)
                    {
                        return;  // Already registered
                    }
                }
                block = block->Next;
            }

            // Add new entry
            CctorRegistryEntry newEntry;
            newEntry.AssemblyId = assemblyId;
            newEntry.TypeToken = typeToken;
            newEntry.TypeArgHash = typeArgHash;
            newEntry.ContextAddress = contextAddress;
            if (BlockAllocator.Add(chain, &newEntry) == null)
            {
                DebugConsole.WriteLine("[MetaInt] Cctor registry allocation failed");
            }
        }

        // DebugConsole.Write("[MetaInt] Registered cctor for type 0x");
        // DebugConsole.WriteHex(typeToken);
        // DebugConsole.Write(" asm ");
        // DebugConsole.WriteDecimal(assemblyId);
        // DebugConsole.Write(" ctx=0x");
        // DebugConsole.WriteHex((ulong)contextAddress);
        // DebugConsole.WriteLine();
    }

    /// <summary>
    /// Look up the StaticClassConstructionContext address for a type.
    /// For generic types, uses the current type arg context to find the right instantiation.
    /// </summary>
    /// <param name="assemblyId">Assembly ID containing the type.</param>
    /// <param name="typeToken">TypeDef token (0x02xxxxxx).</param>
    /// <returns>Pointer to the context, or null if no cctor.</returns>
    public static nint* GetCctorContext(uint assemblyId, uint typeToken)
    {
        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = GetTypeTypeArgHash();

        fixed (BlockChain* chain = &_cctorChain)
        {
            if (chain->First == null)
                return null;

            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var entry = (CctorRegistryEntry*)block->GetEntry(i);
                    if (entry->AssemblyId == assemblyId && entry->TypeToken == typeToken && entry->TypeArgHash == typeArgHash)
                    {
                        return entry->ContextAddress;
                    }
                }
                block = block->Next;
            }
        }

        return null;
    }

    /// <summary>
    /// Get the address of the CheckStaticClassConstruction helper function.
    /// Used by the JIT to emit cctor check calls.
    /// </summary>
    public static nint CheckStaticClassConstructionAddress { get; private set; }

    /// <summary>
    /// Set the address of CheckStaticClassConstruction.
    /// Called during initialization after the helper is available.
    /// </summary>
    public static void SetCheckStaticClassConstructionAddress(nint address)
    {
        CheckStaticClassConstructionAddress = address;
        DebugConsole.Write("[MetaInt] CheckStaticClassConstruction at 0x");
        DebugConsole.WriteHex((ulong)address);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Find the .cctor (static constructor) method token for a type, if any.
    /// </summary>
    /// <param name="assemblyId">Assembly ID containing the type.</param>
    /// <param name="typeToken">TypeDef token (0x02xxxxxx).</param>
    /// <returns>MethodDef token (0x06xxxxxx) of the .cctor, or 0 if none.</returns>
    public static uint FindTypeCctor(uint assemblyId, uint typeToken)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        uint typeRow = typeToken & 0x00FFFFFF;
        if (typeRow == 0)
            return 0;

        // Get method range for this type
        uint methodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow);
        uint methodEnd;

        // Get the number of TypeDef rows
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        if (typeRow < typeDefCount)
        {
            methodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow + 1);
        }
        else
        {
            // Last type - methods extend to end of MethodDef table
            methodEnd = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;
        }

        // Scan methods for .cctor
        for (uint methodRow = methodStart; methodRow < methodEnd; methodRow++)
        {
            uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRow);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (name != null && IsCctorName(name))
            {
                return 0x06000000 | methodRow;
            }
        }

        return 0;
    }

    /// <summary>
    /// Check if a method name is ".cctor" (static constructor).
    /// </summary>
    private static bool IsCctorName(byte* name)
    {
        if (name == null)
            return false;

        // Check for ".cctor"
        return name[0] == '.' &&
               name[1] == 'c' &&
               name[2] == 'c' &&
               name[3] == 't' &&
               name[4] == 'o' &&
               name[5] == 'r' &&
               name[6] == 0;
    }

    /// <summary>
    /// Ensure a cctor context is registered for a type.
    /// Called the first time a static field is accessed for a type.
    /// If the type has a .cctor, this allocates a context and registers it.
    /// For generic types, each instantiation gets its own cctor context.
    /// </summary>
    /// <param name="assemblyId">Assembly ID containing the type.</param>
    /// <param name="typeToken">TypeDef token (0x02xxxxxx).</param>
    /// <returns>Pointer to the context if type has cctor, null otherwise.</returns>
    public static nint* EnsureCctorContextRegistered(uint assemblyId, uint typeToken)
    {
        // Get type arg hash to distinguish generic instantiations
        ulong typeArgHash = GetTypeTypeArgHash();

        // Check if already registered
        nint* existing = GetCctorContext(assemblyId, typeToken);
        if (existing != null)
            return existing;

        // Find if this type has a .cctor
        uint cctorToken = FindTypeCctor(assemblyId, typeToken);
        if (cctorToken == 0)
            return null;  // No cctor - no need for context

        // Allocate StaticClassConstructionContext (single IntPtr field)
        nint* context = (nint*)HeapAllocator.AllocZeroed((ulong)sizeof(nint));
        if (context == null)
            return null;

        // IMPORTANT: Register the context BEFORE compiling the cctor.
        // The cctor may access static fields of its own type, which would trigger
        // a recursive call to EnsureCctorContextRegistered. By registering first
        // with address=0, those accesses will find the context and skip the cctor check
        // (since 0 means "already run" or "being run").
        RegisterCctorContext(assemblyId, typeToken, typeArgHash, context);

        // The context holds the cctor method address
        // We need to compile the cctor and store its address
        var result = Tier0JIT.CompileMethod(assemblyId, cctorToken);
        if (result.Success && result.CodeAddress != null)
        {
            *context = (nint)result.CodeAddress;
        }
        // If compile failed, leave context as 0 - will be treated as "already run"

        return context;
    }

    /// <summary>
    /// Get the method token for a method in an interface at a given index.
    /// </summary>
    /// <param name="assemblyId">The assembly ID containing the interface.</param>
    /// <param name="interfaceTypeToken">The TypeDef token of the interface.</param>
    /// <param name="methodIndex">The 0-based index of the method in the interface.</param>
    /// <returns>The MethodDef token, or 0 if not found.</returns>
    public static uint GetInterfaceMethodToken(uint assemblyId, uint interfaceTypeToken, int methodIndex)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        // Get type RID from token
        uint typeRid = interfaceTypeToken & 0x00FFFFFF;

        // Get the method list start for this type
        uint methodListStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRid);

        // Get method count by looking at next type's method list
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint totalMethodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];
        uint nextMethodList;

        if (typeRid < typeDefCount)
        {
            nextMethodList = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRid + 1);
        }
        else
        {
            nextMethodList = totalMethodCount + 1;
        }

        // Calculate method count
        int methodCount = (int)(nextMethodList - methodListStart);

        if (methodIndex < 0 || methodIndex >= methodCount)
            return 0;

        // Return the method token at this index
        uint methodRid = methodListStart + (uint)methodIndex;
        return 0x06000000 | methodRid;
    }

    /// <summary>
    /// Check if an interface method has a body (is not abstract).
    /// Methods with bodies have non-zero RVA in their metadata.
    /// </summary>
    /// <param name="assemblyId">The assembly ID containing the method.</param>
    /// <param name="methodToken">The MethodDef token.</param>
    /// <returns>True if the method has a body, false if abstract.</returns>
    public static bool InterfaceMethodHasBody(uint assemblyId, uint methodToken)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return false;

        // Get method RID from token
        uint methodRid = methodToken & 0x00FFFFFF;

        // Get the method's RVA - if non-zero, it has a body
        uint rva = MetadataReader.GetMethodDefRva(ref asm->Tables, ref asm->Sizes, methodRid);

        return rva != 0;
    }

    /// <summary>
    /// Find a method in a type by name (first match with a body).
    /// Used to find implementing methods for interface calls.
    /// </summary>
    /// <param name="assemblyId">The assembly ID containing the type.</param>
    /// <param name="typeToken">The TypeDef token of the type to search.</param>
    /// <param name="methodName">The name of the method to find.</param>
    /// <returns>The MethodDef token, or 0 if not found.</returns>
    public static uint FindMethodByName(uint assemblyId, uint typeToken, byte* methodName)
    {
        return FindMethodByNameWithParamCount(assemblyId, typeToken, methodName, -1);
    }

    /// <summary>
    /// Find a method in a type by name and parameter count.
    /// Used to find implementing methods for interface calls when there are overloads.
    /// </summary>
    /// <param name="assemblyId">The assembly ID containing the type.</param>
    /// <param name="typeToken">The TypeDef token of the type to search.</param>
    /// <param name="methodName">The name of the method to find.</param>
    /// <param name="expectedParamCount">Expected parameter count, or -1 to match any.</param>
    /// <returns>The MethodDef token, or 0 if not found.</returns>
    public static uint FindMethodByNameWithParamCount(uint assemblyId, uint typeToken, byte* methodName, int expectedParamCount)
    {
        if (methodName == null)
            return 0;

        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return 0;

        // Get type RID from token
        uint typeRid = typeToken & 0x00FFFFFF;

        // Get the method list start for this type
        uint methodListStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRid);

        // Get method count by looking at next type's method list
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint totalMethodCount = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef];
        uint nextMethodList;

        if (typeRid < typeDefCount)
        {
            nextMethodList = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRid + 1);
        }
        else
        {
            nextMethodList = totalMethodCount + 1;
        }

        // Search through methods for a match
        for (uint methodRid = methodListStart; methodRid < nextMethodList; methodRid++)
        {
            uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRid);
            byte* name = MetadataReader.GetString(ref asm->Metadata, nameIdx);

            if (MetadataReader.StringEquals(name, methodName))
            {
                // Check if this method has a body (not abstract)
                uint rva = MetadataReader.GetMethodDefRva(ref asm->Tables, ref asm->Sizes, methodRid);
                if (rva != 0)
                {
                    // If param count matching is required, check it
                    if (expectedParamCount >= 0)
                    {
                        uint methodToken = 0x06000000 | methodRid;
                        int actualParamCount = GetMethodParamCount(assemblyId, methodToken);
                        if (actualParamCount != expectedParamCount)
                        {
                            continue;  // Keep searching for correct overload
                        }
                    }
                    return 0x06000000 | methodRid;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Get the name of a method from its token.
    /// </summary>
    public static byte* GetMethodName(uint assemblyId, uint methodToken)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return null;

        uint methodRid = methodToken & 0x00FFFFFF;
        uint nameIdx = MetadataReader.GetMethodDefName(ref asm->Tables, ref asm->Sizes, methodRid);
        return MetadataReader.GetString(ref asm->Metadata, nameIdx);
    }

    /// <summary>
    /// Get the parameter count from a method's signature (excluding 'this').
    /// Returns -1 on error.
    /// </summary>
    public static int GetMethodParamCount(uint assemblyId, uint methodToken)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return -1;

        uint methodRid = methodToken & 0x00FFFFFF;
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRid);
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out uint sigLen);
        if (sig == null || sigLen < 2)
            return -1;

        // Parse signature: Header [GenParamCount] ParamCount ...
        byte header = sig[0];
        int pos = 1;

        // If generic method (header & 0x10), skip GenParamCount
        if ((header & 0x10) != 0)
        {
            // Read and skip compressed GenParamCount
            byte b = sig[pos];
            if ((b & 0x80) == 0)
                pos += 1;
            else if ((b & 0xC0) == 0x80)
                pos += 2;
            else
                pos += 4;
        }

        // Read ParamCount (compressed uint)
        if ((uint)pos >= sigLen)
            return -1;

        byte pb = sig[pos];
        if ((pb & 0x80) == 0)
            return pb;
        else if ((pb & 0xC0) == 0x80 && (uint)(pos + 1) < sigLen)
            return ((pb & 0x3F) << 8) | sig[pos + 1];
        else if ((uint)(pos + 3) < sigLen)
            return (int)(((pb & 0x1F) << 24) | (sig[pos + 1] << 16) | (sig[pos + 2] << 8) | sig[pos + 3]);

        return -1;
    }

    /// <summary>
    /// Calculate the vtable slot for an abstract/virtual method.
    /// Abstract methods don't have CompiledMethodInfo, so we calculate the slot from metadata.
    /// Returns -1 if the method is not virtual or slot cannot be determined.
    /// </summary>
    public static short CalculateVtableSlotForMethod(uint assemblyId, uint methodRowId)
    {
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
            return -1;

        // Get method flags
        ushort flags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRowId);
        bool isVirtual = (flags & MethodDefFlags.Virtual) != 0;
        bool isNewSlot = (flags & MethodDefFlags.NewSlot) != 0;
        bool isStatic = (flags & MethodDefFlags.Static) != 0;

        if (!isVirtual || isStatic)
            return -1;  // Not a virtual method

        // Find the declaring TypeDef for this method
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint declaringTypeRow = 0;

        for (uint typeRow = 1; typeRow <= typeDefCount; typeRow++)
        {
            uint typeMethodStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow);
            uint typeMethodEnd;
            if (typeRow < typeDefCount)
                typeMethodEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, typeRow + 1);
            else
                typeMethodEnd = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

            if (methodRowId >= typeMethodStart && methodRowId < typeMethodEnd)
            {
                declaringTypeRow = typeRow;
                break;
            }
        }

        if (declaringTypeRow == 0)
            return -1;

        // Calculate base vtable slots from parent type
        short baseVtableSlots = 3;  // Default: Object has 3 slots (ToString, Equals, GetHashCode)

        // Check extends to determine base slot count
        CodedIndex extendsIdx = MetadataReader.GetTypeDefExtends(ref asm->Tables, ref asm->Sizes, declaringTypeRow);
        if (extendsIdx.RowId > 0 && extendsIdx.Table == MetadataTableId.TypeRef)
        {
            // Check if base is ValueType or Enum (value types don't inherit Object vtable)
            uint typeRefNameIdx = MetadataReader.GetTypeRefName(ref asm->Tables, ref asm->Sizes, extendsIdx.RowId);
            byte* baseName = MetadataReader.GetString(ref asm->Metadata, typeRefNameIdx);
            // Check for "ValueType" or "Enum"
            if (baseName != null)
            {
                if ((baseName[0] == 'V' && baseName[1] == 'a' && baseName[2] == 'l' && baseName[3] == 'u' &&
                     baseName[4] == 'e' && baseName[5] == 'T' && baseName[6] == 'y' && baseName[7] == 'p' &&
                     baseName[8] == 'e' && baseName[9] == 0) ||
                    (baseName[0] == 'E' && baseName[1] == 'n' && baseName[2] == 'u' && baseName[3] == 'm' && baseName[4] == 0))
                {
                    baseVtableSlots = 0;
                }
            }
        }
        else if (extendsIdx.RowId > 0 && extendsIdx.Table == MetadataTableId.TypeDef)
        {
            // Extending another type in the same assembly
            MethodTable* baseMT = asm->Types.Lookup(0x02000000 | extendsIdx.RowId);
            if (baseMT != null)
            {
                baseVtableSlots = (short)baseMT->_usNumVtableSlots;
            }
        }
        else if (extendsIdx.RowId > 0 && extendsIdx.Table == MetadataTableId.TypeSpec)
        {
            // Base is a generic type - try to get its MT from cache
            // Note: Cannot call ResolveTypeSpec directly as it's private
            // For now, assume Object base (3 slots) unless we can find the MT
            // This is a simplification but works for common cases like EqualityComparer<T>
            // where the base is Object
            baseVtableSlots = 3;
        }

        // Count newslot virtual methods before this method to get its slot
        uint methodListStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, declaringTypeRow);
        short currentSlot = baseVtableSlots;

        for (uint methodRow = methodListStart; methodRow < methodRowId; methodRow++)
        {
            ushort mFlags = MetadataReader.GetMethodDefFlags(ref asm->Tables, ref asm->Sizes, methodRow);
            bool mVirtual = (mFlags & MethodDefFlags.Virtual) != 0;
            bool mNewSlot = (mFlags & MethodDefFlags.NewSlot) != 0;
            bool mStatic = (mFlags & MethodDefFlags.Static) != 0;

            if (mVirtual && mNewSlot && !mStatic)
            {
                currentSlot++;
            }
        }

        // If the target method is a newslot virtual, it has slot = currentSlot
        if (isNewSlot)
            return currentSlot;

        // Not a newslot - it's an override, need to find slot in base class
        // For now, return -1 (overrides should already be in registry)
        return -1;
    }
}
