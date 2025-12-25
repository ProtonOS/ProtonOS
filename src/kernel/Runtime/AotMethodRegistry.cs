// ProtonOS kernel - AOT Method Registry
// Provides lookup for AOT-compiled korlib methods that can be called from JIT code.
// These methods have no JIT metadata - they're compiled directly into the kernel.

using System;
using ProtonOS.Memory;
using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;
using ProtonOS.Runtime.Reflection;

namespace ProtonOS.Runtime;

/// <summary>
/// Flags byte for AotMethodEntry.
/// </summary>
[Flags]
public enum AotMethodFlags : byte
{
    None = 0,
    HasThis = 1,
    IsVirtual = 2,
    HasRefParams = 4,
    HasPointerParams = 8,
}

/// <summary>
/// Entry for a registered AOT method.
/// Upgraded to 48 bytes to support generic types, method signatures, and instantiations.
/// </summary>
public unsafe struct AotMethodEntry
{
    /// <summary>Type name hash (e.g., hash of "System.String").</summary>
    public ulong TypeNameHash;

    /// <summary>Method name hash (e.g., hash of "get_Length").</summary>
    public ulong MethodNameHash;

    /// <summary>Parameter type and modifier signature hash for overload resolution.</summary>
    public ulong SignatureHash;

    /// <summary>Native code address.</summary>
    public nint NativeCode;

    /// <summary>Hash of generic instantiation arguments (0 for non-generic or open generic).</summary>
    public uint InstantiationHash;

    /// <summary>Number of parameters (NOT including 'this' for instance methods).</summary>
    public ushort ArgCount;

    /// <summary>Return type kind.</summary>
    public ReturnKind ReturnKind;

    /// <summary>Size in bytes for struct returns (0 for non-struct returns).</summary>
    public byte ReturnStructSize;

    /// <summary>Number of generic type parameters on the declaring type (0 for non-generic).</summary>
    public byte TypeGenericArity;

    /// <summary>Number of generic method parameters (0 for non-generic methods).</summary>
    public byte MethodGenericArity;

    /// <summary>Method flags (HasThis, IsVirtual, HasRefParams, etc.).</summary>
    public AotMethodFlags Flags;

    /// <summary>Reserved for future use.</summary>
    public byte Reserved;

    /// <summary>Whether this is an instance method (has 'this' pointer).</summary>
    public bool HasThis => (Flags & AotMethodFlags.HasThis) != 0;

    /// <summary>Whether this is a virtual method.</summary>
    public bool IsVirtual => (Flags & AotMethodFlags.IsVirtual) != 0;

    /// <summary>Whether this method has ref/out/in parameters.</summary>
    public bool HasRefParams => (Flags & AotMethodFlags.HasRefParams) != 0;
}

/// <summary>
/// Encodes method parameter signatures into a 64-bit hash for overload resolution.
/// Each parameter gets 6 bits: 4 bits for ElementType, 2 bits for modifier (ref/out/in).
/// Supports up to 10 parameters (60 bits used).
/// </summary>
public static unsafe class SignatureEncoder
{
    // Parameter modifiers
    public const byte ModNone = 0;
    public const byte ModRef = 1;   // ref parameter
    public const byte ModOut = 2;   // out parameter
    public const byte ModIn = 3;    // in parameter (readonly ref)

    // Common element types (subset of ECMA-335 element types)
    public const byte TypeVoid = 0x01;
    public const byte TypeBoolean = 0x02;
    public const byte TypeChar = 0x03;
    public const byte TypeI1 = 0x04;
    public const byte TypeU1 = 0x05;
    public const byte TypeI2 = 0x06;
    public const byte TypeU2 = 0x07;
    public const byte TypeI4 = 0x08;
    public const byte TypeU4 = 0x09;
    public const byte TypeI8 = 0x0A;
    public const byte TypeU8 = 0x0B;
    public const byte TypeR4 = 0x0C;
    public const byte TypeR8 = 0x0D;
    public const byte TypeString = 0x0E;
    public const byte TypePtr = 0x0F;    // Pointer type

    /// <summary>
    /// Encode parameter types and modifiers into a 64-bit hash.
    /// </summary>
    /// <param name="paramCount">Number of parameters to encode.</param>
    /// <param name="types">Array of element types (4 bits each).</param>
    /// <param name="modifiers">Array of modifiers (2 bits each, optional).</param>
    /// <returns>64-bit signature hash.</returns>
    public static ulong Encode(int paramCount, byte* types, byte* modifiers = null)
    {
        ulong hash = 0;
        int count = paramCount > 10 ? 10 : paramCount;

        for (int i = 0; i < count; i++)
        {
            byte type = (byte)(types[i] & 0x0F);
            byte mod = modifiers != null ? (byte)(modifiers[i] & 0x03) : (byte)0;
            ulong encoded = (ulong)type | ((ulong)mod << 4);
            hash |= encoded << (i * 6);
        }
        return hash;
    }

    /// <summary>
    /// Encode a single parameter type and modifier.
    /// Useful for building signatures incrementally.
    /// </summary>
    public static ulong EncodeParam(int paramIndex, byte elementType, byte modifier = ModNone)
    {
        if (paramIndex >= 10)
            return 0;

        byte type = (byte)(elementType & 0x0F);
        byte mod = (byte)(modifier & 0x03);
        ulong encoded = (ulong)type | ((ulong)mod << 4);
        return encoded << (paramIndex * 6);
    }

    /// <summary>
    /// Extract the element type from a signature hash for a given parameter index.
    /// </summary>
    public static byte GetParamType(ulong signatureHash, int paramIndex)
    {
        if (paramIndex >= 10)
            return 0;
        return (byte)((signatureHash >> (paramIndex * 6)) & 0x0F);
    }

    /// <summary>
    /// Extract the modifier from a signature hash for a given parameter index.
    /// </summary>
    public static byte GetParamModifier(ulong signatureHash, int paramIndex)
    {
        if (paramIndex >= 10)
            return 0;
        return (byte)((signatureHash >> (paramIndex * 6 + 4)) & 0x03);
    }
}

/// <summary>
/// Token-based AOT method entry for reliable lookup by (assemblyId, methodToken).
/// Used alongside hash-based lookup for backwards compatibility.
/// </summary>
public unsafe struct AotTokenEntry
{
    /// <summary>Assembly ID (1 = korlib/CoreLib).</summary>
    public uint AssemblyId;

    /// <summary>MethodDef token (0x06xxxxxx).</summary>
    public uint MethodToken;

    /// <summary>Native code address.</summary>
    public nint NativeCode;

    /// <summary>Method flags (HasThis, IsVirtual, etc.).</summary>
    public AotMethodFlags Flags;

    /// <summary>Reserved padding.</summary>
    public byte Reserved1;
    public byte Reserved2;
    public byte Reserved3;
}

/// <summary>
/// Registry for AOT-compiled korlib methods that JIT code can call.
/// This allows JIT-compiled assemblies to call methods like String.get_Length
/// which are AOT-compiled into the kernel without JIT metadata.
/// Now uses BlockChain for unlimited growth.
/// Supports both hash-based (legacy) and token-based (new) lookup.
/// </summary>
public static unsafe class AotMethodRegistry
{
    private const int EntryBlockSize = 32;
    private const int TokenEntryBlockSize = 64;

    // Hash-based entries (legacy)
    private static BlockChain _entryChain;

    // Token-based entries (new, more reliable)
    private static BlockChain _tokenChain;

    private static bool _initialized;
    private static bool _tokenRegistryInitialized;

    /// <summary>
    /// Initialize the AOT method registry.
    /// Must be called during kernel initialization before JIT compilation.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[AotRegistry] Initializing...");

        // Initialize block chain for entries
        fixed (BlockChain* chain = &_entryChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(AotMethodEntry), EntryBlockSize))
            {
                DebugConsole.WriteLine("[AotRegistry] Failed to init entry chain");
                return;
            }
        }

        // Register well-known String methods
        RegisterStringMethods();

        // Register well-known Object methods
        RegisterObjectMethods();

        // Register well-known Type methods (for reflection)
        RegisterTypeMethods();

        // Register well-known Int32 methods
        RegisterInt32Methods();

        // Register well-known primitive methods (UInt16, etc.)
        RegisterPrimitiveMethods();

        // Register well-known Exception methods
        RegisterExceptionMethods();

        // Register Delegate methods
        RegisterDelegateMethods();

        // Register Array methods
        RegisterArrayMethods();

        // Register ArgIterator methods (for varargs support)
        RegisterArgIteratorMethods();

        // Register Span helpers (for memory operations)
        RegisterSpanMethods();

        // Register GC methods (for Dispose pattern support)
        RegisterGCMethods();

        // Thread methods for thread management
        RegisterThreadMethods();

        _initialized = true;

        int count;
        fixed (BlockChain* chain = &_entryChain)
            count = chain->TotalCount;
        DebugConsole.WriteLine(string.Format("[AotRegistry] Initialized with {0} methods", count));
    }

    /// <summary>
    /// Initialize the token-based registry after korlib.dll is loaded.
    /// Called from Kernel when korlib.dll is available.
    /// </summary>
    public static void InitTokenRegistry()
    {
        if (_tokenRegistryInitialized)
            return;

        fixed (BlockChain* chain = &_tokenChain)
        {
            if (!BlockAllocator.Init(chain, sizeof(AotTokenEntry), TokenEntryBlockSize))
            {
                DebugConsole.WriteLine("[AotRegistry] Failed to init token chain");
                return;
            }
        }

        _tokenRegistryInitialized = true;
        DebugConsole.WriteLine("[AotRegistry] Token registry initialized");
    }

    /// <summary>
    /// Register an AOT method by token for direct token-based lookup.
    /// </summary>
    /// <param name="assemblyId">Assembly ID (1 = korlib).</param>
    /// <param name="methodToken">MethodDef token (0x06xxxxxx).</param>
    /// <param name="nativeCode">Native code address.</param>
    /// <param name="flags">Method flags.</param>
    public static void RegisterByToken(uint assemblyId, uint methodToken, nint nativeCode, AotMethodFlags flags = AotMethodFlags.None)
    {
        if (!_tokenRegistryInitialized)
        {
            InitTokenRegistry();
        }

        AotTokenEntry entry;
        entry.AssemblyId = assemblyId;
        entry.MethodToken = methodToken;
        entry.NativeCode = nativeCode;
        entry.Flags = flags;
        entry.Reserved1 = 0;
        entry.Reserved2 = 0;
        entry.Reserved3 = 0;

        fixed (BlockChain* chain = &_tokenChain)
        {
            if (BlockAllocator.Add(chain, &entry) == null)
            {
                DebugConsole.WriteLine("[AotRegistry] Failed to add token entry");
            }
        }
    }

    /// <summary>
    /// Look up an AOT method by (assemblyId, methodToken).
    /// This is the preferred lookup method - more reliable than hash-based lookup.
    /// </summary>
    /// <param name="assemblyId">Assembly ID to search.</param>
    /// <param name="methodToken">MethodDef token (0x06xxxxxx).</param>
    /// <param name="entry">Output: the found entry.</param>
    /// <returns>True if found, false otherwise.</returns>
    public static bool TryLookupByToken(uint assemblyId, uint methodToken, out AotTokenEntry entry)
    {
        entry = default;

        if (!_tokenRegistryInitialized)
            return false;

        fixed (BlockChain* chain = &_tokenChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var e = (AotTokenEntry*)block->GetEntry(i);
                    if (e->AssemblyId == assemblyId && e->MethodToken == methodToken)
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
    /// Get the count of token-based entries.
    /// </summary>
    public static int TokenEntryCount
    {
        get
        {
            if (!_tokenRegistryInitialized)
                return 0;

            fixed (BlockChain* chain = &_tokenChain)
            {
                return chain->TotalCount;
            }
        }
    }

    /// <summary>
    /// Register String methods with their wrapper addresses.
    /// </summary>
    private static void RegisterStringMethods()
    {
        // String.get_Length (instance property getter) - 0 parameters, HasThis=true
        Register(
            "System.String", "get_Length",
            (nint)(delegate*<string, int>)&StringHelpers.GetLength,
            0, ReturnKind.Int32, true, false);

        // String.Concat(string, string) (static method)
        Register(
            "System.String", "Concat",
            (nint)(delegate*<string?, string?, string>)&StringHelpers.Concat2,
            2, ReturnKind.IntPtr, false, false);

        // String.Concat(string, string, string) (static method)
        Register(
            "System.String", "Concat",
            (nint)(delegate*<string?, string?, string?, string>)&StringHelpers.Concat3,
            3, ReturnKind.IntPtr, false, false);

        // String.Concat(params string?[] values) (static method) - 1 array parameter
        Register(
            "System.String", "Concat",
            (nint)(delegate*<string?[]?, string>)&StringHelpers.ConcatArray,
            1, ReturnKind.IntPtr, false, false);

        // String.get_Chars (indexer getter) - 1 int parameter, HasThis=true
        Register(
            "System.String", "get_Chars",
            (nint)(delegate*<string, int, char>)&StringHelpers.GetChars,
            1, ReturnKind.Int32, true, false);

        // String.IsNullOrEmpty (static method)
        Register(
            "System.String", "IsNullOrEmpty",
            (nint)(delegate*<string?, bool>)&StringHelpers.IsNullOrEmpty,
            1, ReturnKind.Int32, false, false);

        // String.Equals(string) - 1 string parameter, HasThis=true
        Register(
            "System.String", "Equals",
            (nint)(delegate*<string, string?, bool>)&StringHelpers.Equals,
            1, ReturnKind.Int32, true, false);

        // String.op_Equality (static) - 2 string parameters
        Register(
            "System.String", "op_Equality",
            (nint)(delegate*<string?, string?, bool>)&StringHelpers.OpEquality,
            2, ReturnKind.Int32, false, false);

        // String.op_Inequality (static) - 2 string parameters
        Register(
            "System.String", "op_Inequality",
            (nint)(delegate*<string?, string?, bool>)&StringHelpers.OpInequality,
            2, ReturnKind.Int32, false, false);

        // String.GetHashCode() - 0 parameters, HasThis=true, returns int (virtual)
        Register(
            "System.String", "GetHashCode",
            (nint)(delegate*<string, int>)&StringHelpers.GetHashCode,
            0, ReturnKind.Int32, true, true);

        // String.GetPinnableReference() - 0 parameters, HasThis=true, returns ref char (pointer)
        Register(
            "System.String", "GetPinnableReference",
            (nint)(delegate*<string, nint>)&StringHelpers.GetPinnableReference,
            0, ReturnKind.IntPtr, true, false);

        // String.CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        // 4 parameters (sourceIndex, destination, destinationIndex, count), HasThis=true, returns void
        Register(
            "System.String", "CopyTo",
            (nint)(delegate*<string, int, char[], int, int, void>)&StringHelpers.CopyTo,
            4, ReturnKind.Void, true, false);

        // String constructor: .ctor(char[], int, int) - 3 parameters, static (factory method)
        // This is used by StringBuilder.ToString to create strings from char arrays.
        // The JIT transforms newobj String::.ctor to a call to this factory method.
        Register(
            "System.String", ".ctor",
            (nint)(delegate*<char[], int, int, string>)&StringHelpers.Ctor_CharArrayStartLength,
            3, ReturnKind.IntPtr, false, false);

        // String constructor: .ctor(char[]) - 1 parameter, static (factory method)
        Register(
            "System.String", ".ctor",
            (nint)(delegate*<char[], string>)&StringHelpers.Ctor_CharArray,
            1, ReturnKind.IntPtr, false, false);

        // String constructor: .ctor(char*, int, int) - 3 parameters, static (factory method)
        // This is used by BytePtrToString for reflection APIs.
        // Registered as ".ctor$ptr" to distinguish from char[] variant.
        Register(
            "System.String", ".ctor$ptr",
            (nint)(delegate*<char*, int, int, string>)&StringHelpers.Ctor_CharPtrStartLength,
            3, ReturnKind.IntPtr, false, false);

        // String.Format overloads - static methods for formatted strings
        // Format(string, object) - 2 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, string>)&StringHelpers.Format1,
            2, ReturnKind.IntPtr, false, false);

        // Format(string, object, object) - 3 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, object?, string>)&StringHelpers.Format2,
            3, ReturnKind.IntPtr, false, false);

        // Format(string, object, object, object) - 4 parameters
        Register(
            "System.String", "Format",
            (nint)(delegate*<string, object?, object?, object?, string>)&StringHelpers.Format3,
            4, ReturnKind.IntPtr, false, false);

        // String.Replace(string, string) - 2 parameters (oldValue, newValue), HasThis=true
        // Signature hash distinguishes from Replace(char, char)
        RegisterWithSignature(
            "System.String", "Replace",
            (nint)(delegate*<string, string, string?, string>)&StringHelpers.ReplaceString,
            2, ReturnKind.IntPtr, true, false,
            ComputeSignatureHash(ELEMENT_TYPE_STRING, ELEMENT_TYPE_STRING));

        // String.Replace(char, char) - 2 parameters (oldChar, newChar), HasThis=true
        // Signature hash distinguishes from Replace(string, string)
        RegisterWithSignature(
            "System.String", "Replace",
            (nint)(delegate*<string, char, char, string>)&StringHelpers.ReplaceChar,
            2, ReturnKind.IntPtr, true, false,
            ComputeSignatureHash(ELEMENT_TYPE_CHAR, ELEMENT_TYPE_CHAR));

        // String.Contains(string) - 1 parameter, HasThis=true, returns bool
        Register(
            "System.String", "Contains",
            (nint)(delegate*<string, string, bool>)&StringHelpers.Contains,
            1, ReturnKind.Int32, true, false);

        // String.IndexOf(string) - 1 parameter, HasThis=true, returns int
        // NOTE: IndexOf(char) overload is NOT registered because the AOT lookup
        // can't distinguish it from IndexOf(string) (both have 1 param).
        // Use "x".IndexOf("W") instead of "x".IndexOf('W') in JIT code.
        Register(
            "System.String", "IndexOf",
            (nint)(delegate*<string, string, int>)&StringHelpers.IndexOfString,
            1, ReturnKind.Int32, true, false);

        // String.StartsWith(string) - 1 parameter, HasThis=true, returns bool
        Register(
            "System.String", "StartsWith",
            (nint)(delegate*<string, string, bool>)&StringHelpers.StartsWith,
            1, ReturnKind.Int32, true, false);

        // String.EndsWith(string) - 1 parameter, HasThis=true, returns bool
        Register(
            "System.String", "EndsWith",
            (nint)(delegate*<string, string, bool>)&StringHelpers.EndsWith,
            1, ReturnKind.Int32, true, false);

        // String.LastIndexOf(char) - 1 parameter, HasThis=true, returns int
        Register(
            "System.String", "LastIndexOf",
            (nint)(delegate*<string, char, int>)&StringHelpers.LastIndexOfChar,
            1, ReturnKind.Int32, true, false);

        // String.Substring(int, int) - 2 parameters, HasThis=true, returns string
        Register(
            "System.String", "Substring",
            (nint)(delegate*<string, int, int, string>)&StringHelpers.Substring,
            2, ReturnKind.IntPtr, true, false);

        // String.Substring(int) - 1 parameter, HasThis=true, returns string
        Register(
            "System.String", "Substring",
            (nint)(delegate*<string, int, string>)&StringHelpers.SubstringFrom,
            1, ReturnKind.IntPtr, true, false);
    }

    /// <summary>
    /// Register Object methods with their wrapper addresses.
    /// </summary>
    private static void RegisterObjectMethods()
    {
        // Object..ctor() - constructor, 0 parameters (but HasThis=true for instance method)
        // For constructors, we just return - the object is already allocated
        Register(
            "System.Object", ".ctor",
            (nint)(delegate*<object, void>)&ObjectHelpers.Ctor,
            0, ReturnKind.Void, true, false);

        // Object.GetHashCode() - instance method, returns int
        Register(
            "System.Object", "GetHashCode",
            (nint)(delegate*<object, int>)&ObjectHelpers.GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Object.Equals(object) - instance method, 1 parameter, returns bool
        Register(
            "System.Object", "Equals",
            (nint)(delegate*<object, object?, bool>)&ObjectHelpers.Equals,
            1, ReturnKind.Int32, true, true);

        // Object.Equals(object, object) - static method, 2 parameters, returns bool
        // This is used by ObjectEqualityComparer<T>.Equals for value type comparison
        nint staticEqualsAddr = (nint)(delegate*<object?, object?, bool>)&ObjectHelpers.StaticEquals;
        DebugConsole.Write("[AOT Reg] Object.Equals(2) at 0x");
        DebugConsole.WriteHex((ulong)staticEqualsAddr);
        DebugConsole.WriteLine();
        Register(
            "System.Object", "Equals",
            staticEqualsAddr,
            2, ReturnKind.Int32, false, false);

        // Object.ToString() - instance method, returns string
        Register(
            "System.Object", "ToString",
            (nint)(delegate*<object, string>)&ObjectHelpers.ToString,
            0, ReturnKind.IntPtr, true, true);

        // Object.GetType() - instance method, returns Type (non-virtual, final)
        Register(
            "System.Object", "GetType",
            (nint)(delegate*<object, Type>)&ObjectHelpers.GetType,
            0, ReturnKind.IntPtr, true, false);  // Not virtual - GetType is final
    }

    /// <summary>
    /// Register Type methods (for reflection support).
    /// These bypass virtual dispatch since RuntimeType's vtable isn't properly set up by AOT.
    /// </summary>
    private static void RegisterTypeMethods()
    {
        // Type.get_Name - property getter, returns string
        Register(
            "System.Type", "get_Name",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // MemberInfo.get_Name - polymorphic handler for all MemberInfo subclasses
        // This is called when JIT code calls .Name on Type, MethodInfo, FieldInfo, etc.
        // We need to dispatch to the correct implementation based on actual runtime type.
        Register(
            "System.Reflection.MemberInfo", "get_Name",
            (nint)(delegate*<System.Reflection.MemberInfo, string?>)&MemberInfoHelpers.GetName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.get_FullName - property getter, returns string
        Register(
            "System.Type", "get_FullName",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetFullName,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.get_Namespace - property getter, returns string
        Register(
            "System.Type", "get_Namespace",
            (nint)(delegate*<Type, string?>)&TypeMethodHelpers.GetNamespace,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.get_TypeHandle - property getter, returns RuntimeTypeHandle (struct with nint)
        // RuntimeTypeHandle is passed as nint by value
        Register(
            "System.Type", "get_TypeHandle",
            (nint)(delegate*<Type, nint>)&TypeMethodHelpers.GetTypeHandle,
            0, ReturnKind.IntPtr, true, true);  // Virtual

        // Type.GetTypeFromHandle(RuntimeTypeHandle) - static method for typeof() support
        // RuntimeTypeHandle is a struct with just nint _value, passed as native int
        Register(
            "System.Type", "GetTypeFromHandle",
            (nint)(delegate*<nint, Type?>)&TypeMethodHelpers.GetTypeFromHandle,
            1, ReturnKind.IntPtr, false, false);  // Static, non-virtual

        // MethodBase.GetMethodFromHandle(RuntimeMethodHandle) - static method for method handle support
        // RuntimeMethodHandle is a struct with just nint _value, passed as native int
        Register(
            "System.Reflection.MethodBase", "GetMethodFromHandle",
            (nint)(delegate*<nint, System.Reflection.MethodBase?>)&TypeMethodHelpers.GetMethodFromHandle,
            1, ReturnKind.IntPtr, false, false);  // Static, non-virtual

        // FieldInfo.GetFieldFromHandle(RuntimeFieldHandle) - static method for field handle support
        // RuntimeFieldHandle is a struct with just nint _value, passed as native int
        Register(
            "System.Reflection.FieldInfo", "GetFieldFromHandle",
            (nint)(delegate*<nint, System.Reflection.FieldInfo?>)&TypeMethodHelpers.GetFieldFromHandle,
            1, ReturnKind.IntPtr, false, false);  // Static, non-virtual

        // MethodBase.Invoke(object?, object?[]?) - virtual method
        // Wrapper that dispatches to the appropriate runtime implementation
        Register(
            "System.Reflection.MethodBase", "Invoke",
            (nint)(delegate*<System.Reflection.MethodBase, object?, object?[]?, object?>)&ReflectionHelpers.MethodBaseInvoke,
            2, ReturnKind.IntPtr, true, true);  // HasThis=true, IsVirtual=true

        // Type.GetMethods() - virtual method, returns MethodInfo[]
        Register(
            "System.Type", "GetMethods",
            (nint)(delegate*<Type, System.Reflection.MethodInfo[]>)&TypeMethodHelpers.GetMethods,
            0, ReturnKind.IntPtr, true, true);

        // Type.GetFields() - virtual method, returns FieldInfo[]
        Register(
            "System.Type", "GetFields",
            (nint)(delegate*<Type, System.Reflection.FieldInfo[]>)&TypeMethodHelpers.GetFields,
            0, ReturnKind.IntPtr, true, true);

        // Type.GetConstructors() - virtual method, returns ConstructorInfo[]
        Register(
            "System.Type", "GetConstructors",
            (nint)(delegate*<Type, System.Reflection.ConstructorInfo[]>)&TypeMethodHelpers.GetConstructors,
            0, ReturnKind.IntPtr, true, true);

        // Type.GetMethod(string) - virtual method, returns MethodInfo?
        Register(
            "System.Type", "GetMethod",
            (nint)(delegate*<Type, string?, System.Reflection.MethodInfo?>)&TypeMethodHelpers.GetMethod,
            1, ReturnKind.IntPtr, true, true);

        // Type.GetField(string) - virtual method, returns FieldInfo?
        Register(
            "System.Type", "GetField",
            (nint)(delegate*<Type, string?, System.Reflection.FieldInfo?>)&TypeMethodHelpers.GetField,
            1, ReturnKind.IntPtr, true, true);

        // MethodInfo.op_Equality - static, reference comparison
        Register(
            "System.Reflection.MethodInfo", "op_Equality",
            (nint)(delegate*<System.Reflection.MethodInfo?, System.Reflection.MethodInfo?, bool>)&ReflectionHelpers.MethodInfoEquals,
            2, ReturnKind.Int32, false, false);

        // MethodInfo.op_Inequality - static, reference comparison
        Register(
            "System.Reflection.MethodInfo", "op_Inequality",
            (nint)(delegate*<System.Reflection.MethodInfo?, System.Reflection.MethodInfo?, bool>)&ReflectionHelpers.MethodInfoNotEquals,
            2, ReturnKind.Int32, false, false);

        // MethodBase.op_Equality - static, reference comparison
        Register(
            "System.Reflection.MethodBase", "op_Equality",
            (nint)(delegate*<System.Reflection.MethodBase?, System.Reflection.MethodBase?, bool>)&ReflectionHelpers.MethodBaseEquals,
            2, ReturnKind.Int32, false, false);

        // MethodBase.op_Inequality - static, reference comparison
        Register(
            "System.Reflection.MethodBase", "op_Inequality",
            (nint)(delegate*<System.Reflection.MethodBase?, System.Reflection.MethodBase?, bool>)&ReflectionHelpers.MethodBaseNotEquals,
            2, ReturnKind.Int32, false, false);

        // FieldInfo.op_Equality - static, reference comparison
        Register(
            "System.Reflection.FieldInfo", "op_Equality",
            (nint)(delegate*<System.Reflection.FieldInfo?, System.Reflection.FieldInfo?, bool>)&ReflectionHelpers.FieldInfoEquals,
            2, ReturnKind.Int32, false, false);

        // FieldInfo.op_Inequality - static, reference comparison
        Register(
            "System.Reflection.FieldInfo", "op_Inequality",
            (nint)(delegate*<System.Reflection.FieldInfo?, System.Reflection.FieldInfo?, bool>)&ReflectionHelpers.FieldInfoNotEquals,
            2, ReturnKind.Int32, false, false);

        // MemberInfo.op_Equality - static, reference comparison
        Register(
            "System.Reflection.MemberInfo", "op_Equality",
            (nint)(delegate*<System.Reflection.MemberInfo?, System.Reflection.MemberInfo?, bool>)&ReflectionHelpers.MemberInfoEquals,
            2, ReturnKind.Int32, false, false);

        // MemberInfo.op_Inequality - static, reference comparison
        Register(
            "System.Reflection.MemberInfo", "op_Inequality",
            (nint)(delegate*<System.Reflection.MemberInfo?, System.Reflection.MemberInfo?, bool>)&ReflectionHelpers.MemberInfoNotEquals,
            2, ReturnKind.Int32, false, false);

        // FieldInfo.get_FieldType - virtual property getter
        // Bypasses vtable dispatch since korlib vtables aren't properly set up by AOT
        Register(
            "System.Reflection.FieldInfo", "get_FieldType",
            (nint)(delegate*<System.Reflection.FieldInfo, Type?>)&ReflectionHelpers.FieldInfoGetFieldType,
            0, ReturnKind.IntPtr, true, true);

        // PropertyInfo.get_PropertyType - virtual property getter
        Register(
            "System.Reflection.PropertyInfo", "get_PropertyType",
            (nint)(delegate*<System.Reflection.PropertyInfo, Type?>)&ReflectionHelpers.PropertyInfoGetPropertyType,
            0, ReturnKind.IntPtr, true, true);

        // MethodInfo.GetParameters() - virtual method
        Register(
            "System.Reflection.MethodInfo", "GetParameters",
            (nint)(delegate*<System.Reflection.MethodInfo, System.Reflection.ParameterInfo[]?>)&ReflectionHelpers.MethodInfoGetParameters,
            0, ReturnKind.IntPtr, true, true);

        // MethodInfo.get_ReturnType - virtual property getter
        Register(
            "System.Reflection.MethodInfo", "get_ReturnType",
            (nint)(delegate*<System.Reflection.MethodInfo, Type?>)&ReflectionHelpers.MethodInfoGetReturnType,
            0, ReturnKind.IntPtr, true, true);

        // MethodInfo.GetBaseDefinition() - virtual method
        Register(
            "System.Reflection.MethodInfo", "GetBaseDefinition",
            (nint)(delegate*<System.Reflection.MethodInfo, System.Reflection.MethodInfo>)&ReflectionHelpers.MethodInfoGetBaseDefinition,
            0, ReturnKind.IntPtr, true, true);

        // MethodBase.GetParameters() - virtual method (IL calls this on MethodBase type reference)
        Register(
            "System.Reflection.MethodBase", "GetParameters",
            (nint)(delegate*<System.Reflection.MethodBase, System.Reflection.ParameterInfo[]?>)&ReflectionHelpers.MethodBaseGetParameters,
            0, ReturnKind.IntPtr, true, true);

        // MethodBase.get_Attributes - virtual property getter
        Register(
            "System.Reflection.MethodBase", "get_Attributes",
            (nint)(delegate*<System.Reflection.MethodBase, System.Reflection.MethodAttributes>)&ReflectionHelpers.MethodBaseGetAttributes,
            0, ReturnKind.Int32, true, true);

        // MethodBase.get_IsPublic - virtual property getter
        Register(
            "System.Reflection.MethodBase", "get_IsPublic",
            (nint)(delegate*<System.Reflection.MethodBase, bool>)&ReflectionHelpers.MethodBaseGetIsPublic,
            0, ReturnKind.Int32, true, true);

        // MethodBase.get_IsStatic - virtual property getter
        Register(
            "System.Reflection.MethodBase", "get_IsStatic",
            (nint)(delegate*<System.Reflection.MethodBase, bool>)&ReflectionHelpers.MethodBaseGetIsStatic,
            0, ReturnKind.Int32, true, true);

        // MethodBase.get_IsVirtual - virtual property getter
        Register(
            "System.Reflection.MethodBase", "get_IsVirtual",
            (nint)(delegate*<System.Reflection.MethodBase, bool>)&ReflectionHelpers.MethodBaseGetIsVirtual,
            0, ReturnKind.Int32, true, true);

        // MethodBase.get_IsAbstract - virtual property getter
        Register(
            "System.Reflection.MethodBase", "get_IsAbstract",
            (nint)(delegate*<System.Reflection.MethodBase, bool>)&ReflectionHelpers.MethodBaseGetIsAbstract,
            0, ReturnKind.Int32, true, true);

        // MethodBase.GetMethodBody() - virtual method
        Register(
            "System.Reflection.MethodBase", "GetMethodBody",
            (nint)(delegate*<System.Reflection.MethodBase, System.Reflection.MethodBody?>)&ReflectionHelpers.MethodBaseGetMethodBody,
            0, ReturnKind.IntPtr, true, true);

        // ConstructorInfo.GetParameters() - virtual method
        Register(
            "System.Reflection.ConstructorInfo", "GetParameters",
            (nint)(delegate*<System.Reflection.ConstructorInfo, System.Reflection.ParameterInfo[]?>)&ReflectionHelpers.ConstructorInfoGetParameters,
            0, ReturnKind.IntPtr, true, true);

        // ParameterInfo.get_ParameterType - virtual property getter
        Register(
            "System.Reflection.ParameterInfo", "get_ParameterType",
            (nint)(delegate*<System.Reflection.ParameterInfo, Type?>)&ReflectionHelpers.ParameterInfoGetParameterType,
            0, ReturnKind.IntPtr, true, true);
    }

    /// <summary>
    /// Register Int32 methods with their wrapper addresses.
    /// </summary>
    private static void RegisterInt32Methods()
    {
        // Int32.ToString() - instance method on boxed value type
        // For boxed value types, 'this' is a pointer to the boxed object
        Register(
            "System.Int32", "ToString",
            (nint)(delegate*<nint, string>)&Int32Helpers.ToString,
            0, ReturnKind.IntPtr, true, false);

        // Int32.GetHashCode() - returns the int value itself
        Register(
            "System.Int32", "GetHashCode",
            (nint)(delegate*<nint, int>)&Int32Helpers.GetHashCode,
            0, ReturnKind.Int32, true, true);  // virtual method

        // Int32.CompareTo(int) - compares this value to another
        Register(
            "System.Int32", "CompareTo",
            (nint)(delegate*<nint, int, int>)&Int32Helpers.CompareTo,
            1, ReturnKind.Int32, true, false);
    }

    /// <summary>
    /// Register primitive type methods (UInt16, Int16, etc.)
    /// </summary>
    private static void RegisterPrimitiveMethods()
    {
        // UInt16.CompareTo(UInt16) - instance method on boxed/unboxed value type
        Register(
            "System.UInt16", "CompareTo",
            (nint)(delegate*<nint, ushort, int>)&PrimitiveHelpers.UInt16_CompareTo,
            1, ReturnKind.Int32, true, false);

        // Int16.CompareTo(Int16)
        Register(
            "System.Int16", "CompareTo",
            (nint)(delegate*<nint, short, int>)&PrimitiveHelpers.Int16_CompareTo,
            1, ReturnKind.Int32, true, false);

        // =========================================================================
        // ToString methods for all primitive types (virtual override, called via vtable)
        // =========================================================================

        // Byte.ToString() - 0 parameters, HasThis=true, IsVirtual=true
        Register(
            "System.Byte", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Byte_ToString,
            0, ReturnKind.IntPtr, true, true);

        // SByte.ToString()
        Register(
            "System.SByte", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.SByte_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Boolean.ToString()
        Register(
            "System.Boolean", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Boolean_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Char.ToString()
        Register(
            "System.Char", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Char_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Int16.ToString()
        Register(
            "System.Int16", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Int16_ToString,
            0, ReturnKind.IntPtr, true, true);

        // UInt16.ToString()
        Register(
            "System.UInt16", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.UInt16_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Int64.ToString()
        Register(
            "System.Int64", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Int64_ToString,
            0, ReturnKind.IntPtr, true, true);

        // UInt64.ToString()
        Register(
            "System.UInt64", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.UInt64_ToString,
            0, ReturnKind.IntPtr, true, true);

        // UInt32.ToString()
        Register(
            "System.UInt32", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.UInt32_ToString,
            0, ReturnKind.IntPtr, true, true);

        // IntPtr.ToString()
        Register(
            "System.IntPtr", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.IntPtr_ToString,
            0, ReturnKind.IntPtr, true, true);

        // UIntPtr.ToString()
        Register(
            "System.UIntPtr", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.UIntPtr_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Single.ToString()
        Register(
            "System.Single", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Single_ToString,
            0, ReturnKind.IntPtr, true, true);

        // Double.ToString()
        Register(
            "System.Double", "ToString",
            (nint)(delegate*<nint, string>)&PrimitiveHelpers.Double_ToString,
            0, ReturnKind.IntPtr, true, true);

        // =========================================================================
        // GetHashCode methods for all primitive types
        // =========================================================================

        // Byte.GetHashCode()
        Register(
            "System.Byte", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.Byte_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // SByte.GetHashCode()
        Register(
            "System.SByte", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.SByte_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Boolean.GetHashCode()
        Register(
            "System.Boolean", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.Boolean_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Char.GetHashCode()
        Register(
            "System.Char", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.Char_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Int16.GetHashCode()
        Register(
            "System.Int16", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.Int16_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // UInt16.GetHashCode()
        Register(
            "System.UInt16", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.UInt16_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // Int64.GetHashCode()
        Register(
            "System.Int64", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.Int64_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // UInt64.GetHashCode()
        Register(
            "System.UInt64", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.UInt64_GetHashCode,
            0, ReturnKind.Int32, true, true);

        // UInt32.GetHashCode()
        Register(
            "System.UInt32", "GetHashCode",
            (nint)(delegate*<nint, int>)&PrimitiveHelpers.UInt32_GetHashCode,
            0, ReturnKind.Int32, true, true);
    }

    /// <summary>
    /// Register Exception constructor methods for JIT code.
    /// These allow JIT-compiled code to call newobj for exception types.
    /// </summary>
    private static void RegisterExceptionMethods()
    {
        // Exception constructors - factory style (JIT transforms newobj to call)
        // Exception() - 0 parameters
        Register(
            "System.Exception", ".ctor",
            (nint)(delegate*<Exception>)&ExceptionHelpers.Ctor_Exception,
            0, ReturnKind.IntPtr, false, false);

        // Exception(string) - 1 parameter
        Register(
            "System.Exception", ".ctor",
            (nint)(delegate*<string?, Exception>)&ExceptionHelpers.Ctor_Exception_String,
            1, ReturnKind.IntPtr, false, false);

        // Exception(string, Exception) - 2 parameters
        Register(
            "System.Exception", ".ctor",
            (nint)(delegate*<string?, Exception?, Exception>)&ExceptionHelpers.Ctor_Exception_String_Exception,
            2, ReturnKind.IntPtr, false, false);

        // ArgumentException constructors
        Register(
            "System.ArgumentException", ".ctor",
            (nint)(delegate*<ArgumentException>)&ExceptionHelpers.Ctor_ArgumentException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.ArgumentException", ".ctor",
            (nint)(delegate*<string?, ArgumentException>)&ExceptionHelpers.Ctor_ArgumentException_String,
            1, ReturnKind.IntPtr, false, false);

        // ArgumentNullException constructors
        Register(
            "System.ArgumentNullException", ".ctor",
            (nint)(delegate*<ArgumentNullException>)&ExceptionHelpers.Ctor_ArgumentNullException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.ArgumentNullException", ".ctor",
            (nint)(delegate*<string?, ArgumentNullException>)&ExceptionHelpers.Ctor_ArgumentNullException_String,
            1, ReturnKind.IntPtr, false, false);

        // InvalidOperationException constructors
        Register(
            "System.InvalidOperationException", ".ctor",
            (nint)(delegate*<InvalidOperationException>)&ExceptionHelpers.Ctor_InvalidOperationException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.InvalidOperationException", ".ctor",
            (nint)(delegate*<string?, InvalidOperationException>)&ExceptionHelpers.Ctor_InvalidOperationException_String,
            1, ReturnKind.IntPtr, false, false);

        // NotSupportedException constructors
        Register(
            "System.NotSupportedException", ".ctor",
            (nint)(delegate*<NotSupportedException>)&ExceptionHelpers.Ctor_NotSupportedException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NotSupportedException", ".ctor",
            (nint)(delegate*<string?, NotSupportedException>)&ExceptionHelpers.Ctor_NotSupportedException_String,
            1, ReturnKind.IntPtr, false, false);

        // NotImplementedException constructors
        Register(
            "System.NotImplementedException", ".ctor",
            (nint)(delegate*<NotImplementedException>)&ExceptionHelpers.Ctor_NotImplementedException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NotImplementedException", ".ctor",
            (nint)(delegate*<string?, NotImplementedException>)&ExceptionHelpers.Ctor_NotImplementedException_String,
            1, ReturnKind.IntPtr, false, false);

        // IndexOutOfRangeException constructors
        Register(
            "System.IndexOutOfRangeException", ".ctor",
            (nint)(delegate*<IndexOutOfRangeException>)&ExceptionHelpers.Ctor_IndexOutOfRangeException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.IndexOutOfRangeException", ".ctor",
            (nint)(delegate*<string?, IndexOutOfRangeException>)&ExceptionHelpers.Ctor_IndexOutOfRangeException_String,
            1, ReturnKind.IntPtr, false, false);

        // NullReferenceException constructors
        Register(
            "System.NullReferenceException", ".ctor",
            (nint)(delegate*<NullReferenceException>)&ExceptionHelpers.Ctor_NullReferenceException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.NullReferenceException", ".ctor",
            (nint)(delegate*<string?, NullReferenceException>)&ExceptionHelpers.Ctor_NullReferenceException_String,
            1, ReturnKind.IntPtr, false, false);

        // InvalidCastException constructors
        Register(
            "System.InvalidCastException", ".ctor",
            (nint)(delegate*<InvalidCastException>)&ExceptionHelpers.Ctor_InvalidCastException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.InvalidCastException", ".ctor",
            (nint)(delegate*<string?, InvalidCastException>)&ExceptionHelpers.Ctor_InvalidCastException_String,
            1, ReturnKind.IntPtr, false, false);

        // FormatException constructors
        Register(
            "System.FormatException", ".ctor",
            (nint)(delegate*<FormatException>)&ExceptionHelpers.Ctor_FormatException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.FormatException", ".ctor",
            (nint)(delegate*<string?, FormatException>)&ExceptionHelpers.Ctor_FormatException_String,
            1, ReturnKind.IntPtr, false, false);

        // AggregateException constructors
        Register(
            "System.AggregateException", ".ctor",
            (nint)(delegate*<AggregateException>)&ExceptionHelpers.Ctor_AggregateException,
            0, ReturnKind.IntPtr, false, false);

        Register(
            "System.AggregateException", ".ctor",
            (nint)(delegate*<string?, AggregateException>)&ExceptionHelpers.Ctor_AggregateException_String,
            1, ReturnKind.IntPtr, false, false);

        Register(
            "System.AggregateException", ".ctor",
            (nint)(delegate*<string?, Exception[], AggregateException>)&ExceptionHelpers.Ctor_AggregateException_String_ExceptionArray,
            2, ReturnKind.IntPtr, false, false);

        // Use special ".ctor$list" name to distinguish from (string, Exception[]) variant
        Register(
            "System.AggregateException", ".ctor$list",
            (nint)(delegate*<string?, System.Collections.Generic.List<Exception>, AggregateException>)&ExceptionHelpers.Ctor_AggregateException_String_ListException,
            2, ReturnKind.IntPtr, false, false);

        // Use special ".ctor$single" name for (string, Exception) variant - single inner exception
        Register(
            "System.AggregateException", ".ctor$single",
            (nint)(delegate*<string?, Exception, AggregateException>)&ExceptionHelpers.Ctor_AggregateException_String_Exception,
            2, ReturnKind.IntPtr, false, false);

        // AggregateException.InnerExceptions property getter
        Register(
            "System.AggregateException", "get_InnerExceptions",
            (nint)(delegate*<AggregateException, System.Collections.ObjectModel.ReadOnlyCollection<Exception>>)&ExceptionHelpers.AggregateException_get_InnerExceptions,
            0, ReturnKind.IntPtr, true, false);

        // AggregateException.Flatten method
        Register(
            "System.AggregateException", "Flatten",
            (nint)(delegate*<AggregateException, AggregateException>)&ExceptionHelpers.AggregateException_Flatten,
            0, ReturnKind.IntPtr, true, false);

        // Exception.Message property getter
        Register(
            "System.Exception", "get_Message",
            (nint)(delegate*<Exception, string?>)&ExceptionHelpers.Exception_get_Message,
            0, ReturnKind.IntPtr, true, false);

        // Exception.InnerException property getter
        Register(
            "System.Exception", "get_InnerException",
            (nint)(delegate*<Exception, Exception?>)&ExceptionHelpers.Exception_get_InnerException,
            0, ReturnKind.IntPtr, true, false);

        // ReadOnlyCollection<T>.Count property getter (works for any T since layout is same)
        Register(
            "System.Collections.ObjectModel.ReadOnlyCollection`1", "get_Count",
            (nint)(delegate*<System.Collections.ObjectModel.ReadOnlyCollection<Exception>, int>)&CollectionHelpers.ReadOnlyCollectionException_get_Count,
            0, ReturnKind.Int32, true, false);

        // NOTE: ReadOnlyCollection<T>.get_Item is NOT registered here because the helper assumes
        // reference types (8-byte elements). For value types like int (4-byte elements), the
        // element offset calculation is wrong. Let the JIT compile this for each instantiation.

        // ReadOnlyCollection<Exception> constructor
        Register(
            "System.Collections.ObjectModel.ReadOnlyCollection`1", ".ctor",
            (nint)(delegate*<System.Collections.Generic.IList<Exception>, System.Collections.ObjectModel.ReadOnlyCollection<Exception>>)&CollectionHelpers.ReadOnlyCollectionException_Ctor,
            1, ReturnKind.IntPtr, false, false);

        // TaskCanceledException constructors
        Register(
            "System.Threading.Tasks.TaskCanceledException", ".ctor",
            (nint)(delegate*<System.Threading.Tasks.TaskCanceledException>)&ExceptionHelpers.Ctor_TaskCanceledException,
            0, ReturnKind.IntPtr, false, false);
        Register(
            "System.Threading.Tasks.TaskCanceledException", ".ctor",
            (nint)(delegate*<string?, System.Threading.Tasks.TaskCanceledException>)&ExceptionHelpers.Ctor_TaskCanceledException_String,
            1, ReturnKind.IntPtr, false, false);
        Register(
            "System.Threading.Tasks.TaskCanceledException", ".ctor",
            (nint)(delegate*<string?, Exception?, System.Threading.Tasks.TaskCanceledException>)&ExceptionHelpers.Ctor_TaskCanceledException_String_Exception,
            2, ReturnKind.IntPtr, false, false);
    }

    /// <summary>
    /// Register Delegate methods for multicast delegate support.
    /// </summary>
    private static void RegisterDelegateMethods()
    {
        // Delegate.Combine(Delegate?, Delegate?) - static method, returns Delegate?
        Register(
            "System.Delegate", "Combine",
            (nint)(delegate*<Delegate?, Delegate?, Delegate?>)&DelegateHelpers.Combine,
            2, ReturnKind.IntPtr, false, false);

        // Delegate.Remove(Delegate?, Delegate?) - static method, returns Delegate?
        Register(
            "System.Delegate", "Remove",
            (nint)(delegate*<Delegate?, Delegate?, Delegate?>)&DelegateHelpers.Remove,
            2, ReturnKind.IntPtr, false, false);

        // MulticastDelegate.CombineImpl(Delegate?) - instance virtual method for vtable slot 3
        // This is called through the vtable when combining multicast delegates
        Register(
            "System.MulticastDelegate", "CombineImpl",
            (nint)(delegate*<MulticastDelegate, Delegate?, Delegate?>)&DelegateHelpers.CombineImplWrapper,
            1, ReturnKind.IntPtr, true, true);

        // MulticastDelegate.RemoveImpl(Delegate) - instance virtual method for vtable slot 4
        // This is called through the vtable when removing from multicast delegates
        Register(
            "System.MulticastDelegate", "RemoveImpl",
            (nint)(delegate*<MulticastDelegate, Delegate, Delegate?>)&DelegateHelpers.RemoveImplWrapper,
            1, ReturnKind.IntPtr, true, true);
    }

    /// <summary>
    /// Register Array methods with their wrapper addresses.
    /// </summary>
    private static void RegisterArrayMethods()
    {
        // Array.get_Length - instance property getter
        // Returns the total number of elements in the array (stored at offset 8)
        Register(
            "System.Array", "get_Length",
            (nint)(delegate*<Array, int>)&ArrayHelpers.GetLength,
            0, ReturnKind.Int32, true, false);

        // Array.get_LongLength - instance property getter (64-bit version)
        Register(
            "System.Array", "get_LongLength",
            (nint)(delegate*<Array, long>)&ArrayHelpers.GetLongLength,
            0, ReturnKind.Int64, true, false);

        // Array.get_Rank - instance property getter
        // For single-dimension arrays returns 1, for MD arrays returns stored rank
        Register(
            "System.Array", "get_Rank",
            (nint)(delegate*<Array, int>)&ArrayHelpers.GetRank,
            0, ReturnKind.Int32, true, false);

        // Array.Copy(Array, Array, int) - static method, 3 parameters
        Register(
            "System.Array", "Copy",
            (nint)(delegate*<Array, Array, int, void>)&ArrayHelpers.Copy3,
            3, ReturnKind.Void, false, false);

        // Array.Copy(Array, int, Array, int, int) - static method, 5 parameters
        Register(
            "System.Array", "Copy",
            (nint)(delegate*<Array, int, Array, int, int, void>)&ArrayHelpers.Copy5,
            5, ReturnKind.Void, false, false);

        // Array.Clear(Array, int, int) - static method, 3 parameters
        Register(
            "System.Array", "Clear",
            (nint)(delegate*<Array, int, int, void>)&ArrayHelpers.Clear3,
            3, ReturnKind.Void, false, false);

        // Array.Clear(Array) - static method, 1 parameter
        Register(
            "System.Array", "Clear",
            (nint)(delegate*<Array, void>)&ArrayHelpers.Clear1,
            1, ReturnKind.Void, false, false);
    }

    /// <summary>
    /// Register ArgIterator methods for varargs support.
    /// ArgIterator is a ref struct (value type) with a single _current pointer field.
    /// 'this' is passed as a pointer to the stack-allocated ArgIterator.
    /// </summary>
    private static void RegisterArgIteratorMethods()
    {
        // ArgIterator::.ctor(RuntimeArgumentHandle)
        // 'this' is pointer to ArgIterator, arg1 is RuntimeArgumentHandle (which is just nint)
        Register(
            "System.ArgIterator", ".ctor",
            (nint)(delegate*<nint, nint, void>)&ArgIteratorHelpers.Ctor,
            1, ReturnKind.Void, true, false);

        // ArgIterator::GetNextArg() - returns TypedReference (16 bytes)
        // 'this' is pointer to ArgIterator
        // TRICK: Register with ReturnStructSize=17 to force hidden buffer mode
        // This makes the JIT pass: RCX=retBuf, RDX=thisPtr (shifting 'this' to second arg)
        // which matches the AOT helper's signature
        Register(
            "System.ArgIterator", "GetNextArg",
            (nint)(delegate*<nint, nint, void>)&ArgIteratorHelpers.GetNextArg,
            0, ReturnKind.Struct, true, false, 17);

        // ArgIterator::GetRemainingCount() - returns int
        Register(
            "System.ArgIterator", "GetRemainingCount",
            (nint)(delegate*<nint, int>)&ArgIteratorHelpers.GetRemainingCount,
            0, ReturnKind.Int32, true, false);

        // ArgIterator::End() - no-op
        Register(
            "System.ArgIterator", "End",
            (nint)(delegate*<nint, void>)&ArgIteratorHelpers.End,
            0, ReturnKind.Void, true, false);
    }

    /// <summary>
    /// Register Span helper methods.
    /// These are static helpers that operate on the raw memory layout of Span<T>.
    /// Registered under "ProtonOS.Runtime.SpanHelpers" for direct JIT access.
    /// </summary>
    private static void RegisterSpanMethods()
    {
        // SpanHelpers.GetLength(nint spanPtr) - works for any Span<T>
        Register(
            "ProtonOS.Runtime.SpanHelpers", "GetLength",
            (nint)(delegate*<nint, int>)&SpanHelpers.GetLength,
            1, ReturnKind.Int32, false, false);

        // SpanHelpers.GetPointer(nint spanPtr) - get data pointer
        Register(
            "ProtonOS.Runtime.SpanHelpers", "GetPointer",
            (nint)(delegate*<nint, nint>)&SpanHelpers.GetPointer,
            1, ReturnKind.IntPtr, false, false);

        // SpanHelpers.IsEmpty(nint spanPtr)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "IsEmpty",
            (nint)(delegate*<nint, bool>)&SpanHelpers.IsEmpty,
            1, ReturnKind.Int32, false, false);

        // SpanHelpers.InitByteSpanFromArray(nint spanPtr, byte[] array)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "InitByteSpanFromArray",
            (nint)(delegate*<nint, byte[]?, void>)&SpanHelpers.InitByteSpanFromArray,
            2, ReturnKind.Void, false, false);

        // SpanHelpers.InitIntSpanFromArray(nint spanPtr, int[] array)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "InitIntSpanFromArray",
            (nint)(delegate*<nint, int[]?, void>)&SpanHelpers.InitIntSpanFromArray,
            2, ReturnKind.Void, false, false);

        // SpanHelpers.InitSpanFromPointer(nint spanPtr, void* pointer, int length)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "InitSpanFromPointer",
            (nint)(delegate*<nint, void*, int, void>)&SpanHelpers.InitSpanFromPointer,
            3, ReturnKind.Void, false, false);

        // SpanHelpers.GetByte(nint spanPtr, int index)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "GetByte",
            (nint)(delegate*<nint, int, byte>)&SpanHelpers.GetByte,
            2, ReturnKind.Int32, false, false);

        // SpanHelpers.SetByte(nint spanPtr, int index, byte value)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "SetByte",
            (nint)(delegate*<nint, int, byte, void>)&SpanHelpers.SetByte,
            3, ReturnKind.Void, false, false);

        // SpanHelpers.GetInt(nint spanPtr, int index)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "GetInt",
            (nint)(delegate*<nint, int, int>)&SpanHelpers.GetInt,
            2, ReturnKind.Int32, false, false);

        // SpanHelpers.SetInt(nint spanPtr, int index, int value)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "SetInt",
            (nint)(delegate*<nint, int, int, void>)&SpanHelpers.SetInt,
            3, ReturnKind.Void, false, false);

        // SpanHelpers.ClearByteSpan(nint spanPtr)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "ClearByteSpan",
            (nint)(delegate*<nint, void>)&SpanHelpers.ClearByteSpan,
            1, ReturnKind.Void, false, false);

        // SpanHelpers.FillByteSpan(nint spanPtr, byte value)
        Register(
            "ProtonOS.Runtime.SpanHelpers", "FillByteSpan",
            (nint)(delegate*<nint, byte, void>)&SpanHelpers.FillByteSpan,
            2, ReturnKind.Void, false, false);
    }

    /// <summary>
    /// Register GC methods for Dispose pattern support.
    /// GC.SuppressFinalize is commonly called in Dispose methods.
    /// </summary>
    private static void RegisterGCMethods()
    {
        // GC.SuppressFinalize(object) - static method, 1 parameter, returns void
        Register(
            "System.GC", "SuppressFinalize",
            (nint)(delegate*<object?, void>)&GCHelpers.SuppressFinalize,
            1, ReturnKind.Void, false, false);

        // GC.KeepAlive(object) - static method, 1 parameter, returns void
        Register(
            "System.GC", "KeepAlive",
            (nint)(delegate*<object?, void>)&GCHelpers.KeepAlive,
            1, ReturnKind.Void, false, false);

        // GC.Collect() - static method, 0 parameters, returns void
        Register(
            "System.GC", "Collect",
            (nint)(delegate*<void>)&GCHelpers.Collect,
            0, ReturnKind.Void, false, false);
    }

    /// <summary>
    /// Register System.Threading.Thread methods for thread management.
    /// </summary>
    private static void RegisterThreadMethods()
    {
        // Thread.Sleep(int) - static method, 1 parameter, returns void
        Register(
            "System.Threading.Thread", "Sleep",
            (nint)(delegate*<int, void>)&ThreadHelpers.Sleep_Int,
            1, ReturnKind.Void, false, false);

        // Thread.Sleep(TimeSpan) - static method, 1 parameter (struct passed as 8-byte value), returns void
        Register(
            "System.Threading.Thread", "Sleep",
            (nint)(delegate*<TimeSpan, void>)&ThreadHelpers.Sleep_TimeSpan,
            1, ReturnKind.Void, false, false);

        // Thread.Yield() - static method, 0 parameters, returns bool
        Register(
            "System.Threading.Thread", "Yield",
            (nint)(delegate*<bool>)&ThreadHelpers.Yield,
            0, ReturnKind.Int32, false, false);

        // Thread.SpinWait(int) - static method, 1 parameter, returns void
        Register(
            "System.Threading.Thread", "SpinWait",
            (nint)(delegate*<int, void>)&ThreadHelpers.SpinWait,
            1, ReturnKind.Void, false, false);

        // Thread.get_CurrentThread - static property getter, returns Thread reference
        Register(
            "System.Threading.Thread", "get_CurrentThread",
            (nint)(delegate*<System.Threading.Thread?>)&ThreadHelpers.get_CurrentThread,
            0, ReturnKind.IntPtr, false, false);

        // Thread.get_ManagedThreadId - instance property getter, returns int
        Register(
            "System.Threading.Thread", "get_ManagedThreadId",
            (nint)(delegate*<System.Threading.Thread, int>)&ThreadHelpers.get_ManagedThreadId,
            0, ReturnKind.Int32, true, false);
    }

    /// <summary>
    /// Register an AOT method (legacy overload for backwards compatibility).
    /// </summary>
    private static void Register(string typeName, string methodName, nint nativeCode,
                                  byte argCount, ReturnKind returnKind, bool hasThis, bool isVirtual,
                                  ushort returnStructSize = 0)
    {
        AotMethodFlags flags = AotMethodFlags.None;
        if (hasThis) flags |= AotMethodFlags.HasThis;
        if (isVirtual) flags |= AotMethodFlags.IsVirtual;

        RegisterEx(typeName, methodName, nativeCode, argCount, returnKind,
                   (byte)returnStructSize, flags, 0, 0, 0, 0);
    }

    /// <summary>
    /// Register an AOT method with signature hash for overload resolution.
    /// Use this when multiple overloads have the same parameter count.
    /// </summary>
    /// <param name="signatureHash">Hash computed from parameter element types using ComputeSignatureHash()</param>
    private static void RegisterWithSignature(string typeName, string methodName, nint nativeCode,
                                               byte argCount, ReturnKind returnKind, bool hasThis, bool isVirtual,
                                               ulong signatureHash, ushort returnStructSize = 0)
    {
        AotMethodFlags flags = AotMethodFlags.None;
        if (hasThis) flags |= AotMethodFlags.HasThis;
        if (isVirtual) flags |= AotMethodFlags.IsVirtual;

        RegisterEx(typeName, methodName, nativeCode, argCount, returnKind,
                   (byte)returnStructSize, flags, signatureHash, 0, 0, 0);
    }

    /// <summary>
    /// Register an AOT method with full control over all fields.
    /// </summary>
    private static void RegisterEx(string typeName, string methodName, nint nativeCode,
                                    ushort argCount, ReturnKind returnKind, byte returnStructSize,
                                    AotMethodFlags flags, ulong signatureHash = 0,
                                    uint instantiationHash = 0, byte typeGenericArity = 0,
                                    byte methodGenericArity = 0)
    {
        // Debug: trace entry
        // DebugConsole.Write("[AOT RegEx] ");
        // DebugConsole.Write(typeName);
        // DebugConsole.Write(".");
        // DebugConsole.Write(methodName);
        // DebugConsole.WriteLine();

        ulong typeHash = HashString(typeName);
        ulong methodHash = HashString(methodName);

        AotMethodEntry entry;
        entry.TypeNameHash = typeHash;
        entry.MethodNameHash = methodHash;
        entry.SignatureHash = signatureHash;
        entry.NativeCode = nativeCode;
        entry.InstantiationHash = instantiationHash;
        entry.ArgCount = argCount;
        entry.ReturnKind = returnKind;
        entry.ReturnStructSize = returnStructSize;
        entry.TypeGenericArity = typeGenericArity;
        entry.MethodGenericArity = methodGenericArity;
        entry.Flags = flags;
        entry.Reserved = 0;

        fixed (BlockChain* chain = &_entryChain)
        {
            if (BlockAllocator.Add(chain, &entry) == null)
            {
                DebugConsole.WriteLine("[AotRegistry] Failed to add entry");
            }
        }
    }

    /// <summary>
    /// Look up an AOT method by type and method name.
    /// Returns true if found and populates the entry.
    /// Uses three-tier lookup: exact match, open generic, legacy arg-count.
    /// </summary>
    public static bool TryLookup(byte* typeName, byte* methodName, byte argCount, out AotMethodEntry entry,
                                  bool isCharPtrVariant = false, bool isListVariant = false, bool isSingleExceptionVariant = false)
    {
        // Legacy lookup - use extended lookup with no signature/instantiation
        return TryLookupEx(typeName, methodName, argCount, 0, 0, out entry, isCharPtrVariant, isListVariant, isSingleExceptionVariant);
    }

    /// <summary>
    /// Look up an AOT method with signature hash for proper overload resolution.
    /// </summary>
    public static bool TryLookupWithSignature(byte* typeName, byte* methodName, byte argCount,
                                               ulong signatureHash, out AotMethodEntry entry)
    {
        return TryLookupEx(typeName, methodName, argCount, signatureHash, 0, out entry, false, false, false);
    }

    /// <summary>
    /// Extended lookup with signature and instantiation hash support.
    /// Implements three-tier lookup:
    /// - Tier 1: Exact match (type + method + signature + instantiation)
    /// - Tier 2: Open generic match (type + method + signature, ignore instantiation)
    /// - Tier 3: Legacy arg-count match (type + method + arg count, ignore signature)
    /// </summary>
    public static bool TryLookupEx(byte* typeName, byte* methodName, byte argCount,
                                    ulong signatureHash, uint instantiationHash,
                                    out AotMethodEntry entry, bool isCharPtrVariant = false, bool isListVariant = false, bool isSingleExceptionVariant = false)
    {
        entry = default;

        if (typeName == null || methodName == null)
            return false;

        ulong typeHash = HashBytes(typeName);
        ulong methodHash = HashBytes(methodName);

        // For char* variant constructors, look for the special ".ctor$ptr" entry
        // For List<> variant constructors (e.g., AggregateException), look for ".ctor$list"
        // For single Exception variant constructors (e.g., AggregateException(string, Exception)), look for ".ctor$single"
        ulong methodHashPtrVariant = isCharPtrVariant ? HashString(".ctor$ptr") : 0;
        ulong methodHashListVariant = isListVariant ? HashString(".ctor$list") : 0;
        ulong methodHashSingleVariant = isSingleExceptionVariant ? HashString(".ctor$single") : 0;
        ulong targetMethodHash = isCharPtrVariant ? methodHashPtrVariant :
                                 (isListVariant ? methodHashListVariant :
                                 (isSingleExceptionVariant ? methodHashSingleVariant : methodHash));

        // Tier 1: Exact match (including signature and instantiation)
        if (signatureHash != 0)
        {
            fixed (BlockChain* chain = &_entryChain)
            {
                var block = chain->First;
                while (block != null)
                {
                    for (int i = 0; i < block->Used; i++)
                    {
                        var e = (AotMethodEntry*)block->GetEntry(i);
                        if (e->TypeNameHash == typeHash && e->MethodNameHash == targetMethodHash &&
                            e->SignatureHash == signatureHash && e->InstantiationHash == instantiationHash)
                        {
                            entry = *e;
                            return true;
                        }
                    }
                    block = block->Next;
                }
            }

            // Tier 2: Open generic match (ignore instantiation hash)
            fixed (BlockChain* chain = &_entryChain)
            {
                var block = chain->First;
                while (block != null)
                {
                    for (int i = 0; i < block->Used; i++)
                    {
                        var e = (AotMethodEntry*)block->GetEntry(i);
                        if (e->TypeNameHash == typeHash && e->MethodNameHash == targetMethodHash &&
                            e->SignatureHash == signatureHash && e->TypeGenericArity > 0)
                        {
                            entry = *e;
                            return true;
                        }
                    }
                    block = block->Next;
                }
            }
        }

        // Tier 3: Legacy arg-count match (backwards compatible)
        fixed (BlockChain* chain = &_entryChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var e = (AotMethodEntry*)block->GetEntry(i);
                    if (e->TypeNameHash == typeHash && e->MethodNameHash == targetMethodHash)
                    {
                        // For overloaded methods, match by arg count
                        // Note: argCount includes 'this' for instance methods
                        if (e->ArgCount == argCount ||
                            (argCount == 0 && !e->HasThis))  // Don't enforce count if not provided
                        {
                            entry = *e;
                            return true;
                        }
                    }
                }
                block = block->Next;
            }
        }

        return false;
    }

    /// <summary>
    /// Look up an AOT method by type and method name (managed strings).
    /// Returns the native code address or 0 if not found.
    /// </summary>
    public static nint LookupByName(string typeName, string methodName)
    {
        if (typeName == null || methodName == null)
            return 0;

        ulong typeHash = HashString(typeName);
        ulong methodHash = HashString(methodName);

        fixed (BlockChain* chain = &_entryChain)
        {
            var block = chain->First;
            while (block != null)
            {
                for (int i = 0; i < block->Used; i++)
                {
                    var e = (AotMethodEntry*)block->GetEntry(i);
                    if (e->TypeNameHash == typeHash && e->MethodNameHash == methodHash)
                    {
                        return e->NativeCode;
                    }
                }
                block = block->Next;
            }
        }

        return 0;
    }

    /// <summary>
    /// Check if a type name is a well-known AOT type.
    /// </summary>
    public static bool IsWellKnownAotType(byte* typeName)
    {
        if (typeName == null)
            return false;

        // Check for System.String
        if (StringMatches(typeName, "System.String"))
            return true;

        // Check for System.Object
        if (StringMatches(typeName, "System.Object"))
            return true;

        // Check for System.Int32
        if (StringMatches(typeName, "System.Int32"))
            return true;

        // Exception types
        if (StringMatches(typeName, "System.Exception"))
            return true;
        if (StringMatches(typeName, "System.ArgumentException"))
            return true;
        if (StringMatches(typeName, "System.ArgumentNullException"))
            return true;
        if (StringMatches(typeName, "System.ArgumentOutOfRangeException"))
            return true;
        if (StringMatches(typeName, "System.InvalidOperationException"))
            return true;
        if (StringMatches(typeName, "System.NotSupportedException"))
            return true;
        if (StringMatches(typeName, "System.NotImplementedException"))
            return true;
        if (StringMatches(typeName, "System.IndexOutOfRangeException"))
            return true;
        if (StringMatches(typeName, "System.NullReferenceException"))
            return true;
        if (StringMatches(typeName, "System.InvalidCastException"))
            return true;
        if (StringMatches(typeName, "System.FormatException"))
            return true;
        if (StringMatches(typeName, "System.AggregateException"))
            return true;
        if (StringMatches(typeName, "System.Threading.Tasks.TaskCanceledException"))
            return true;

        // Collection types (generic - match by name without type args)
        if (StringMatches(typeName, "System.Collections.ObjectModel.ReadOnlyCollection`1"))
            return true;

        // Delegate types
        if (StringMatches(typeName, "System.Delegate"))
            return true;
        if (StringMatches(typeName, "System.MulticastDelegate"))
            return true;

        // Array type
        if (StringMatches(typeName, "System.Array"))
            return true;

        // Reflection types
        if (StringMatches(typeName, "System.Type"))
            return true;
        if (StringMatches(typeName, "System.RuntimeType"))
            return true;
        if (StringMatches(typeName, "System.Reflection.MemberInfo"))
            return true;
        if (StringMatches(typeName, "System.Reflection.MethodBase"))
            return true;
        if (StringMatches(typeName, "System.Reflection.MethodInfo"))
            return true;
        if (StringMatches(typeName, "System.Reflection.FieldInfo"))
            return true;
        if (StringMatches(typeName, "System.Reflection.PropertyInfo"))
            return true;
        if (StringMatches(typeName, "System.Reflection.ParameterInfo"))
            return true;
        if (StringMatches(typeName, "System.Reflection.ConstructorInfo"))
            return true;

        // Varargs types
        if (StringMatches(typeName, "System.TypedReference"))
            return true;
        if (StringMatches(typeName, "System.RuntimeArgumentHandle"))
            return true;
        if (StringMatches(typeName, "System.ArgIterator"))
            return true;

        // GC type (for Dispose pattern support)
        if (StringMatches(typeName, "System.GC"))
            return true;

        return false;
    }

    /// <summary>
    /// Hash a managed string for lookup (64-bit).
    /// </summary>
    private static ulong HashString(string s)
    {
        if (s == null)
            return 0;

        ulong hash = 5381;
        for (int i = 0; i < s.Length; i++)
        {
            hash = ((hash << 5) + hash) ^ (ulong)s[i];
        }
        return hash;
    }

    /// <summary>
    /// Hash a null-terminated byte string (64-bit).
    /// </summary>
    private static ulong HashBytes(byte* s)
    {
        if (s == null)
            return 0;

        ulong hash = 5381;
        while (*s != 0)
        {
            hash = ((hash << 5) + hash) ^ *s;
            s++;
        }
        return hash;
    }

    /// <summary>
    /// Check if a byte string matches a managed string.
    /// </summary>
    private static bool StringMatches(byte* bytes, string str)
    {
        if (bytes == null || str == null)
            return false;

        for (int i = 0; i < str.Length; i++)
        {
            if (bytes[i] == 0 || bytes[i] != (byte)str[i])
                return false;
        }
        return bytes[str.Length] == 0;
    }

    // ==========================================================================
    // Signature Hash Computation
    // ==========================================================================
    // ECMA-335 Element Types for signature matching
    public const byte ELEMENT_TYPE_VOID = 0x01;
    public const byte ELEMENT_TYPE_BOOLEAN = 0x02;
    public const byte ELEMENT_TYPE_CHAR = 0x03;
    public const byte ELEMENT_TYPE_I1 = 0x04;
    public const byte ELEMENT_TYPE_U1 = 0x05;
    public const byte ELEMENT_TYPE_I2 = 0x06;
    public const byte ELEMENT_TYPE_U2 = 0x07;
    public const byte ELEMENT_TYPE_I4 = 0x08;
    public const byte ELEMENT_TYPE_U4 = 0x09;
    public const byte ELEMENT_TYPE_I8 = 0x0A;
    public const byte ELEMENT_TYPE_U8 = 0x0B;
    public const byte ELEMENT_TYPE_R4 = 0x0C;
    public const byte ELEMENT_TYPE_R8 = 0x0D;
    public const byte ELEMENT_TYPE_STRING = 0x0E;
    public const byte ELEMENT_TYPE_PTR = 0x0F;
    public const byte ELEMENT_TYPE_BYREF = 0x10;
    public const byte ELEMENT_TYPE_VALUETYPE = 0x11;
    public const byte ELEMENT_TYPE_CLASS = 0x12;
    public const byte ELEMENT_TYPE_VAR = 0x13;
    public const byte ELEMENT_TYPE_ARRAY = 0x14;
    public const byte ELEMENT_TYPE_GENERICINST = 0x15;
    public const byte ELEMENT_TYPE_TYPEDBYREF = 0x16;
    public const byte ELEMENT_TYPE_I = 0x18;
    public const byte ELEMENT_TYPE_U = 0x19;
    public const byte ELEMENT_TYPE_OBJECT = 0x1C;
    public const byte ELEMENT_TYPE_SZARRAY = 0x1D;
    public const byte ELEMENT_TYPE_MVAR = 0x1E;

    /// <summary>
    /// Compute a signature hash from an array of element types.
    /// Used when registering AOT methods with known parameter types.
    /// </summary>
    public static ulong ComputeSignatureHash(params byte[] elementTypes)
    {
        ulong hash = 5381;
        foreach (var t in elementTypes)
        {
            hash = ((hash << 5) + hash) ^ t;
        }
        return hash;
    }

    /// <summary>
    /// Compute a signature hash from a raw IL signature blob.
    /// Used by MetadataIntegration when looking up methods.
    /// </summary>
    /// <param name="sig">Pointer to signature blob</param>
    /// <param name="sigLen">Length of signature blob</param>
    /// <returns>Hash of parameter element types</returns>
    public static ulong ComputeSignatureHashFromBlob(byte* sig, int sigLen)
    {
        if (sig == null || sigLen < 2)
            return 0;

        int pos = 0;

        // Skip calling convention byte
        pos++;

        // Read parameter count (compressed uint)
        int paramCount = 0;
        if ((sig[pos] & 0x80) == 0)
        {
            paramCount = sig[pos++];
        }
        else if ((sig[pos] & 0xC0) == 0x80)
        {
            paramCount = ((sig[pos] & 0x3F) << 8) | sig[pos + 1];
            pos += 2;
        }
        else
        {
            // 4-byte encoding - unlikely for param count
            pos += 4;
        }

        // Skip return type
        pos = SkipType(sig, pos, sigLen);
        if (pos < 0) return 0;

        // Hash parameter types
        ulong hash = 5381;
        for (int i = 0; i < paramCount && pos < sigLen; i++)
        {
            byte elemType = sig[pos];
            hash = ((hash << 5) + hash) ^ elemType;
            pos = SkipType(sig, pos, sigLen);
            if (pos < 0) break;
        }

        return hash;
    }

    /// <summary>
    /// Skip a type in a signature blob, returning the new position.
    /// Returns -1 on error.
    /// </summary>
    private static int SkipType(byte* sig, int pos, int sigLen)
    {
        if (pos >= sigLen) return -1;

        byte elemType = sig[pos++];

        switch (elemType)
        {
            // Primitive types - just the element type byte
            case ELEMENT_TYPE_VOID:
            case ELEMENT_TYPE_BOOLEAN:
            case ELEMENT_TYPE_CHAR:
            case ELEMENT_TYPE_I1:
            case ELEMENT_TYPE_U1:
            case ELEMENT_TYPE_I2:
            case ELEMENT_TYPE_U2:
            case ELEMENT_TYPE_I4:
            case ELEMENT_TYPE_U4:
            case ELEMENT_TYPE_I8:
            case ELEMENT_TYPE_U8:
            case ELEMENT_TYPE_R4:
            case ELEMENT_TYPE_R8:
            case ELEMENT_TYPE_STRING:
            case ELEMENT_TYPE_OBJECT:
            case ELEMENT_TYPE_I:
            case ELEMENT_TYPE_U:
            case ELEMENT_TYPE_TYPEDBYREF:
                return pos;

            // PTR or BYREF - followed by another type
            case ELEMENT_TYPE_PTR:
            case ELEMENT_TYPE_BYREF:
            case ELEMENT_TYPE_SZARRAY:
                return SkipType(sig, pos, sigLen);

            // CLASS or VALUETYPE - followed by compressed TypeDefOrRef token
            case ELEMENT_TYPE_CLASS:
            case ELEMENT_TYPE_VALUETYPE:
                return SkipCompressedToken(sig, pos, sigLen);

            // VAR or MVAR - followed by compressed index
            case ELEMENT_TYPE_VAR:
            case ELEMENT_TYPE_MVAR:
                return SkipCompressedUint(sig, pos, sigLen);

            // GENERICINST - GenericInst (CLASS | VALUETYPE) TypeDefOrRef GenArgCount Type*
            case ELEMENT_TYPE_GENERICINST:
                pos++;  // Skip CLASS or VALUETYPE
                pos = SkipCompressedToken(sig, pos, sigLen);  // Skip type token
                if (pos < 0) return -1;
                // Read generic arg count
                int genArgCount = 0;
                if (pos < sigLen && (sig[pos] & 0x80) == 0)
                    genArgCount = sig[pos++];
                else if (pos + 1 < sigLen && (sig[pos] & 0xC0) == 0x80)
                {
                    genArgCount = ((sig[pos] & 0x3F) << 8) | sig[pos + 1];
                    pos += 2;
                }
                // Skip each generic argument type
                for (int i = 0; i < genArgCount && pos > 0; i++)
                    pos = SkipType(sig, pos, sigLen);
                return pos;

            // ARRAY - Type ArrayShape
            case ELEMENT_TYPE_ARRAY:
                pos = SkipType(sig, pos, sigLen);  // Element type
                if (pos < 0) return -1;
                // Skip ArrayShape - this is complex, simplified for now
                // Rank (compressed uint)
                pos = SkipCompressedUint(sig, pos, sigLen);
                if (pos < 0) return -1;
                // NumSizes (compressed uint) + sizes
                int numSizes = 0;
                if (pos < sigLen && (sig[pos] & 0x80) == 0)
                    numSizes = sig[pos++];
                for (int i = 0; i < numSizes; i++)
                    pos = SkipCompressedUint(sig, pos, sigLen);
                if (pos < 0) return -1;
                // NumLoBounds (compressed uint) + lobounds
                int numLoBounds = 0;
                if (pos < sigLen && (sig[pos] & 0x80) == 0)
                    numLoBounds = sig[pos++];
                for (int i = 0; i < numLoBounds; i++)
                    pos = SkipCompressedUint(sig, pos, sigLen);
                return pos;

            default:
                // Unknown type - try to continue
                return pos;
        }
    }

    /// <summary>
    /// Skip a compressed TypeDefOrRef token.
    /// </summary>
    private static int SkipCompressedToken(byte* sig, int pos, int sigLen)
    {
        return SkipCompressedUint(sig, pos, sigLen);
    }

    /// <summary>
    /// Skip a compressed unsigned integer.
    /// </summary>
    private static int SkipCompressedUint(byte* sig, int pos, int sigLen)
    {
        if (pos >= sigLen) return -1;

        if ((sig[pos] & 0x80) == 0)
            return pos + 1;
        else if ((sig[pos] & 0xC0) == 0x80)
            return pos + 2;
        else
            return pos + 4;
    }
}

/// <summary>
/// Wrapper methods for String operations.
/// These are thin wrappers that forward to the actual String methods.
/// The JIT calls these wrappers because we can get their function pointers.
/// </summary>
public static unsafe class StringHelpers
{
    /// <summary>
    /// Wrapper for String.get_Length.
    /// </summary>
    public static int GetLength(string s)
    {
        if (s == null)
            return 0;
        int len = s.Length;
        // Debug: print what GetLength is returning
        ProtonOS.Platform.DebugConsole.Write("[GetLen] len=");
        ProtonOS.Platform.DebugConsole.WriteDecimal((uint)len);
        ProtonOS.Platform.DebugConsole.WriteLine();
        return len;
    }

    /// <summary>
    /// Wrapper for String.Concat(string, string).
    /// </summary>
    public static string Concat2(string? str0, string? str1)
    {
        return string.Concat(str0, str1);
    }

    /// <summary>
    /// Wrapper for String.Concat(string, string, string).
    /// </summary>
    public static string Concat3(string? str0, string? str1, string? str2)
    {
        return string.Concat(str0, str1, str2);
    }

    /// <summary>
    /// Wrapper for String.Concat(params string?[] values).
    /// </summary>
    public static string ConcatArray(string?[]? values)
    {
        if (values == null || values.Length == 0) return string.Empty;
        if (values.Length == 1) return values[0] ?? string.Empty;
        if (values.Length == 2) return string.Concat(values[0], values[1]);
        if (values.Length == 3) return string.Concat(values[0], values[1], values[2]);

        // For longer arrays, chain the concatenations
        string result = values[0] ?? string.Empty;
        for (int i = 1; i < values.Length; i++)
        {
            result = string.Concat(result, values[i]);
        }
        return result;
    }

    /// <summary>
    /// Wrapper for String indexer (get_Chars).
    /// </summary>
    public static char GetChars(string s, int index)
    {
        if (s == null)
            return '\0';
        if ((uint)index >= (uint)s.Length)
            return '\0';  // Could throw, but simpler for now
        return s[index];
    }

    /// <summary>
    /// Wrapper for String.IsNullOrEmpty.
    /// </summary>
    public static bool IsNullOrEmpty(string? value)
    {
        return string.IsNullOrEmpty(value);
    }

    /// <summary>
    /// Wrapper for String.Equals(string).
    /// </summary>
    public static bool Equals(string s, string? other)
    {
        if (s == null)
            return other == null;
        return s.Equals(other);
    }

    /// <summary>
    /// Wrapper for String.op_Equality (== operator).
    /// </summary>
    public static bool OpEquality(string? a, string? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return false;

        // Debug: check lengths
        int lenA = a.Length;
        int lenB = b.Length;
        if (lenA != lenB)
        {
            // Only log when looking for method names (short strings)
            if (lenA < 20 && lenB < 20 && lenA > 0 && lenB > 0)
            {
                ProtonOS.Platform.DebugConsole.Write("[StrEq] len mismatch: ");
                ProtonOS.Platform.DebugConsole.WriteDecimal((uint)lenA);
                ProtonOS.Platform.DebugConsole.Write(" vs ");
                ProtonOS.Platform.DebugConsole.WriteDecimal((uint)lenB);
                ProtonOS.Platform.DebugConsole.WriteLine();
            }
            return false;
        }

        for (int i = 0; i < lenA; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Wrapper for String.op_Inequality (!= operator).
    /// </summary>
    public static bool OpInequality(string? a, string? b)
    {
        return !OpEquality(a, b);
    }

    /// <summary>
    /// Wrapper for String.GetHashCode().
    /// Returns a content-based hash code for the string.
    /// </summary>
    public static int GetHashCode(string s)
    {
        if (s == null)
            return 0;

        // Simple but effective hash algorithm (similar to Java's String.hashCode)
        int hash = 0;
        int len = s.Length;
        for (int i = 0; i < len; i++)
        {
            hash = 31 * hash + s[i];
        }
        return hash;
    }

    /// <summary>
    /// Wrapper for String.GetPinnableReference().
    /// Returns a pointer to the first character of the string.
    /// </summary>
    public static unsafe nint GetPinnableReference(string s)
    {
        if (s == null || s.Length == 0)
            return 0;
        // Get reference and convert to pointer
        fixed (char* ptr = &s.GetPinnableReference())
        {
            return (nint)ptr;
        }
    }

    /// <summary>
    /// Wrapper for String.CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count).
    /// Copies characters from the string to a char array.
    /// </summary>
    public static void CopyTo(string s, int sourceIndex, char[] destination, int destinationIndex, int count)
    {
        if (s == null || destination == null)
            return;
        if (sourceIndex < 0 || destinationIndex < 0 || count <= 0)
            return;
        if (sourceIndex + count > s.Length)
            return;
        if (destinationIndex + count > destination.Length)
            return;

        for (int i = 0; i < count; i++)
        {
            destination[destinationIndex + i] = s[sourceIndex + i];
        }
    }

    /// <summary>
    /// Factory method for String..ctor(char[], int, int).
    /// Creates a new string from a portion of a char array.
    /// </summary>
    public static string Ctor_CharArrayStartLength(char[] value, int startIndex, int length)
    {
        return string.Ctor_CharArrayStartLength(value, startIndex, length);
    }

    /// <summary>
    /// Factory method for String..ctor(char[]).
    /// Creates a new string from a char array.
    /// </summary>
    public static string Ctor_CharArray(char[] value)
    {
        return string.Ctor_CharArray(value);
    }

    /// <summary>
    /// Factory method for String..ctor(char*, int, int).
    /// Creates a new string from a portion of a char pointer.
    /// </summary>
    public static unsafe string Ctor_CharPtrStartLength(char* value, int startIndex, int length)
    {
        return string.Ctor_CharPtrStartLength(value, startIndex, length);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object).
    /// </summary>
    public static string Format1(string format, object? arg0)
    {
        return string.Format(format, arg0);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object, object).
    /// </summary>
    public static string Format2(string format, object? arg0, object? arg1)
    {
        return string.Format(format, arg0, arg1);
    }

    /// <summary>
    /// Wrapper for String.Format(string, object, object, object).
    /// </summary>
    public static string Format3(string format, object? arg0, object? arg1, object? arg2)
    {
        return string.Format(format, arg0, arg1, arg2);
    }

    /// <summary>
    /// Wrapper for String.Replace(char, char).
    /// Replaces all occurrences of oldChar with newChar.
    /// </summary>
    public static string ReplaceChar(string s, char oldChar, char newChar)
    {
        if (s == null || s.Length == 0)
            return s ?? string.Empty;

        // Check if oldChar exists
        bool found = false;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == oldChar)
            {
                found = true;
                break;
            }
        }
        if (!found) return s;

        // Create new string with replacements
        char[] chars = new char[s.Length];
        for (int i = 0; i < s.Length; i++)
        {
            chars[i] = s[i] == oldChar ? newChar : s[i];
        }
        return new string(chars);
    }

    /// <summary>
    /// Wrapper for String.Replace(string, string).
    /// Replaces all occurrences of oldValue with newValue.
    /// </summary>
    public static string ReplaceString(string s, string oldValue, string? newValue)
    {
        if (s == null)
            return string.Empty;
        if (oldValue == null || oldValue.Length == 0)
            return s;  // Can't replace empty string
        newValue ??= string.Empty;

        // Count occurrences
        int count = 0;
        int pos = 0;
        while ((pos = IndexOf(s, oldValue, pos)) >= 0)
        {
            count++;
            pos += oldValue.Length;
        }

        if (count == 0) return s;

        // Calculate new length
        int newLength = s.Length + (newValue.Length - oldValue.Length) * count;
        if (newLength == 0) return string.Empty;

        // Build result
        char[] chars = new char[newLength];
        int srcPos = 0;
        int dstPos = 0;

        while ((pos = IndexOf(s, oldValue, srcPos)) >= 0)
        {
            // Copy characters before the match
            int copyLen = pos - srcPos;
            for (int i = 0; i < copyLen; i++)
                chars[dstPos++] = s[srcPos + i];

            // Copy replacement
            for (int i = 0; i < newValue.Length; i++)
                chars[dstPos++] = newValue[i];

            srcPos = pos + oldValue.Length;
        }

        // Copy remaining characters
        while (srcPos < s.Length)
            chars[dstPos++] = s[srcPos++];

        return new string(chars);
    }

    /// <summary>
    /// Helper method to find substring in string (internal use).
    /// </summary>
    private static int IndexOf(string s, string value, int startIndex)
    {
        if (s == null || value == null)
            return -1;
        if (startIndex < 0)
            startIndex = 0;
        if (value.Length == 0)
            return startIndex;
        if (startIndex + value.Length > s.Length)
            return -1;

        for (int i = startIndex; i <= s.Length - value.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < value.Length; j++)
            {
                if (s[i + j] != value[j])
                {
                    match = false;
                    break;
                }
            }
            if (match) return i;
        }
        return -1;
    }

    /// <summary>
    /// Wrapper for String.Contains(string).
    /// Returns true if the specified value occurs within this string.
    /// </summary>
    public static bool Contains(string s, string value)
    {
        return IndexOfString(s, value) >= 0;
    }

    /// <summary>
    /// Wrapper for String.IndexOf(string).
    /// Returns the zero-based index of the first occurrence of value, or -1.
    /// </summary>
    public static int IndexOfString(string s, string value)
    {
        return IndexOf(s, value, 0);
    }

    /// <summary>
    /// Wrapper for String.IndexOf(char).
    /// Returns the zero-based index of the first occurrence of character, or -1.
    /// </summary>
    public static int IndexOfChar(string s, char value)
    {
        if (s == null)
            return -1;

        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == value)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Wrapper for String.StartsWith(string).
    /// Returns true if this string starts with the specified prefix.
    /// </summary>
    public static bool StartsWith(string s, string value)
    {
        if (s == null || value == null)
            return false;
        if (value.Length > s.Length)
            return false;
        if (value.Length == 0)
            return true;

        for (int i = 0; i < value.Length; i++)
        {
            if (s[i] != value[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Wrapper for String.EndsWith(string).
    /// Returns true if this string ends with the specified suffix.
    /// </summary>
    public static bool EndsWith(string s, string value)
    {
        if (s == null || value == null)
            return false;
        if (value.Length > s.Length)
            return false;
        if (value.Length == 0)
            return true;

        int offset = s.Length - value.Length;
        for (int i = 0; i < value.Length; i++)
        {
            if (s[offset + i] != value[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Wrapper for String.LastIndexOf(char).
    /// Returns the zero-based index of the last occurrence of character, or -1.
    /// </summary>
    public static int LastIndexOfChar(string s, char value)
    {
        if (s == null)
            return -1;

        for (int i = s.Length - 1; i >= 0; i--)
        {
            if (s[i] == value)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Wrapper for String.Substring(int startIndex, int length).
    /// Returns a substring starting at the specified index with the specified length.
    /// </summary>
    public static string Substring(string s, int startIndex, int length)
    {
        if (s == null)
            return "";
        if (startIndex < 0)
            startIndex = 0;
        if (startIndex >= s.Length)
            return "";
        if (length <= 0)
            return "";
        if (startIndex + length > s.Length)
            length = s.Length - startIndex;

        // Allocate new string with correct length
        char* chars = stackalloc char[length + 1];
        for (int i = 0; i < length; i++)
        {
            chars[i] = s[startIndex + i];
        }
        chars[length] = '\0';

        return new string(chars, 0, length);
    }

    /// <summary>
    /// Wrapper for String.Substring(int startIndex).
    /// Returns a substring starting at the specified index to the end.
    /// </summary>
    public static string SubstringFrom(string s, int startIndex)
    {
        if (s == null)
            return "";
        if (startIndex < 0)
            startIndex = 0;
        if (startIndex >= s.Length)
            return "";

        int length = s.Length - startIndex;
        return Substring(s, startIndex, length);
    }
}

/// <summary>
/// Wrapper methods for Object operations.
/// These are thin wrappers that provide base Object behavior.
/// NOTE: We avoid virtual calls to prevent triggering unboxing code generation
/// which requires System.Runtime.RuntimeExports.
/// </summary>
public static unsafe class ObjectHelpers
{
    /// <summary>
    /// Wrapper for Object..ctor().
    /// Object's constructor does nothing - the object is already allocated.
    /// </summary>
    public static void Ctor(object obj)
    {
        // Nothing to do - object is already allocated
    }

    /// <summary>
    /// Wrapper for Object.GetHashCode().
    /// Returns a pointer-based hash code (base Object behavior).
    /// </summary>
    public static int GetHashCode(object obj)
    {
        if (obj == null)
            return 0;
        // Use pointer-based hash (this is what base Object.GetHashCode() does)
        return (int)(nint)System.Runtime.CompilerServices.Unsafe.AsPointer(ref obj);
    }

    /// <summary>
    /// Wrapper for Object.Equals(object).
    /// Uses reference equality (base Object behavior).
    /// </summary>
    public static bool Equals(object obj, object? other)
    {
        // Base Object.Equals uses reference equality
        return ReferenceEquals(obj, other);
    }

    /// <summary>
    /// Wrapper for static Object.Equals(object, object).
    /// This is the static helper that properly handles value type comparison.
    /// Avoids virtual dispatch since AOT vtable layout may differ from JIT types.
    /// </summary>
    public static bool StaticEquals(object? objA, object? objB)
    {
        // Reference equality first
        if (ReferenceEquals(objA, objB))
            return true;
        // Null checks
        if (objA == null || objB == null)
            return false;

        // Get MethodTables using the m_pMethodTable field (works for AOT objects)
        System.Runtime.MethodTable* mtA = objA.m_pMethodTable;
        System.Runtime.MethodTable* mtB = objB.m_pMethodTable;

        // Must be same type to be equal
        if (mtA != mtB)
            return false;

        // For value types, compare the actual boxed values
        // Also check if base size is small (12-32 bytes) which indicates a boxed primitive
        // AOT MTs for primitives may not have IsValueType flag set correctly,
        // so we use size-based heuristic as fallback.
        // Base size = MT ptr (8) + value bytes:
        //   - int/float: 8 + 4 = 12
        //   - long/double: 8 + 8 = 16
        //   - decimal/Guid: 8 + 16 = 24
        //   - structs up to ~24 bytes data = 32
        bool isSmallBoxedValue = (mtA->_uBaseSize >= 12 && mtA->_uBaseSize <= 32);
        bool likelyValueType = mtA->IsValueType || isSmallBoxedValue;
        if (likelyValueType)
        {
            // Get object addresses by dereferencing the object references
            void* ptrA = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref objA);
            void* ptrB = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref objB);

            // Value data starts at offset 8 (after the MT pointer)
            byte* dataA = (byte*)ptrA + 8;
            byte* dataB = (byte*)ptrB + 8;

            // Calculate value size: BaseSize - 8 (MT pointer)
            // Use ValueTypeSize if IsValueType flag is set, otherwise calculate from BaseSize
            uint valueSize = mtA->IsValueType ? mtA->ValueTypeSize : (mtA->_uBaseSize - 8);

            for (uint i = 0; i < valueSize; i++)
            {
                if (dataA[i] != dataB[i])
                    return false;
            }
            return true;
        }

        // For reference types with same MT but different references, they're not equal
        return false;
    }

    /// <summary>
    /// Wrapper for Object.ToString().
    /// Returns a type name placeholder (avoids virtual dispatch).
    /// </summary>
    public static string ToString(object obj)
    {
        if (obj == null)
            return "null";
        // Return a simple placeholder - avoids virtual ToString() dispatch
        return "object";
    }

    /// <summary>
    /// Wrapper for Object.GetType().
    /// Returns the Type object for this object's runtime type.
    /// </summary>
    public static Type GetType(object obj)
    {
        if (obj == null)
            return null!;

        // Get the MethodTable pointer from the object (first field)
        // Object layout: [MethodTable* m_pMethodTable, ...fields...]
        // First dereference: get the object pointer from the reference
        void* objPtr = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref obj);

        // Second dereference: read the MethodTable from the start of the object
        void* mt = *(void**)objPtr;

        // Create and return a RuntimeType wrapping the MethodTable
        // Cast to System.Runtime.MethodTable* which RuntimeType expects
        return new RuntimeType((System.Runtime.MethodTable*)mt);
    }

    private static bool ReferenceEquals(object? a, object? b)
    {
        return (object?)a == (object?)b;
    }
}

/// <summary>
/// Wrapper methods for Int32 operations.
/// When called via vtable dispatch on a boxed value type, 'this' is the boxed object pointer.
/// The actual value is at offset 8 (after the MethodTable pointer).
/// </summary>
public static unsafe class Int32Helpers
{
    /// <summary>
    /// Wrapper for Int32.ToString() when called on a boxed Int32.
    /// When called through vtable dispatch, 'this' is the boxed object pointer.
    /// The actual int value is at offset 8 (after the MethodTable pointer).
    /// </summary>
    public static string ToString(nint thisPtr)
    {
        if (thisPtr == 0)
            return "0";
        // thisPtr is a boxed object: [MethodTable*][int value]
        // Value is at offset 8
        int* valuePtr = (int*)(thisPtr + 8);
        return System.Int32.FormatInt32(*valuePtr);
    }

    /// <summary>
    /// Wrapper for Int32.GetHashCode() called on either a boxed Int32 or a byref.
    /// Returns the int value itself as the hash code.
    /// </summary>
    /// <remarks>
    /// This method is called in two scenarios:
    /// 1. Boxed object (via vtable dispatch): thisPtr points to [MethodTable*][int value]
    /// 2. Byref (via call instance): thisPtr points directly to [int value]
    ///
    /// We detect which case by checking if [thisPtr] looks like a pointer (> 0x100000)
    /// or looks like a small int value. MethodTable pointers are always large addresses.
    /// </remarks>
    public static int GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0)
            return 0;

        // Read the first 8 bytes at thisPtr
        ulong firstQword = *(ulong*)thisPtr;

        // If it looks like a MethodTable pointer (large address in kernel/heap space),
        // then this is a boxed object and value is at offset 8.
        // MethodTable pointers are typically > 0x100000 (1MB).
        // Actual int values being hashed would almost never be that large in practice,
        // and even if they were, this heuristic covers the common cases correctly.
        if (firstQword > 0x100000)
        {
            // Boxed object: value at offset 8
            int* valuePtr = (int*)(thisPtr + 8);
            return *valuePtr;
        }
        else
        {
            // Byref: value at offset 0
            int* valuePtr = (int*)thisPtr;
            return *valuePtr;
        }
    }

    /// <summary>
    /// Wrapper for Int32.CompareTo(int) - compares this int to another.
    /// Returns -1 if less than, 0 if equal, 1 if greater than.
    /// </summary>
    public static int CompareTo(nint thisPtr, int other)
    {
        if (thisPtr == 0)
            return -1;

        // Read the first 8 bytes to detect boxed vs byref
        ulong firstQword = *(ulong*)thisPtr;
        int thisValue;

        if (firstQword > 0x100000)
        {
            // Boxed object: value at offset 8
            thisValue = *(int*)(thisPtr + 8);
        }
        else
        {
            // Byref: value at offset 0
            thisValue = *(int*)thisPtr;
        }

        if (thisValue < other) return -1;
        if (thisValue > other) return 1;
        return 0;
    }
}

/// <summary>
/// Wrapper methods for primitive type operations (UInt16, Int16, etc.).
/// NOTE: For small primitives (2 bytes), we can't safely detect boxed vs byref
/// by reading 8 bytes (would read garbage from stack). We assume byref (direct call
/// on local/field) which is the common case. Boxed calls should be rare.
/// </summary>
public static unsafe class PrimitiveHelpers
{
    /// <summary>
    /// UInt16.CompareTo(UInt16) - compares this value to another.
    /// </summary>
    public static int UInt16_CompareTo(nint thisPtr, ushort other)
    {
        // Read value directly - assume byref (most common for value type methods)
        ushort thisValue = *(ushort*)thisPtr;

        if (thisValue < other) return -1;
        if (thisValue > other) return 1;
        return 0;
    }

    /// <summary>
    /// Int16.CompareTo(Int16) - compares this value to another.
    /// </summary>
    public static int Int16_CompareTo(nint thisPtr, short other)
    {
        // Read value directly - assume byref (most common for value type methods)
        short thisValue = *(short*)thisPtr;

        if (thisValue < other) return -1;
        if (thisValue > other) return 1;
        return 0;
    }

    // =========================================================================
    // ToString helpers for primitive types - called via vtable dispatch on boxed values
    // For boxed objects: thisPtr points to [MethodTable*][value]
    // The value is at offset 8 (after the 8-byte MethodTable pointer)
    // =========================================================================

    /// <summary>
    /// Byte.ToString() - formats unsigned 8-bit integer.
    /// </summary>
    public static string Byte_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        byte value = *(byte*)(thisPtr + 8);
        return System.Int32.FormatInt32((int)value);
    }

    /// <summary>
    /// SByte.ToString() - formats signed 8-bit integer.
    /// </summary>
    public static string SByte_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        sbyte value = *(sbyte*)(thisPtr + 8);
        return System.Int32.FormatInt32((int)value);
    }

    /// <summary>
    /// Boolean.ToString() - returns "True" or "False".
    /// </summary>
    public static string Boolean_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "False";
        bool value = *(bool*)(thisPtr + 8);
        return value ? "True" : "False";
    }

    /// <summary>
    /// Char.ToString() - returns single character as string.
    /// </summary>
    public static string Char_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "\0";
        char value = *(char*)(thisPtr + 8);
        return new string(value, 1);
    }

    /// <summary>
    /// Int16.ToString() - formats signed 16-bit integer.
    /// </summary>
    public static string Int16_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        short value = *(short*)(thisPtr + 8);
        return System.Int32.FormatInt32((int)value);
    }

    /// <summary>
    /// UInt16.ToString() - formats unsigned 16-bit integer.
    /// </summary>
    public static string UInt16_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        ushort value = *(ushort*)(thisPtr + 8);
        return System.Int32.FormatInt32((int)value);
    }

    /// <summary>
    /// Int64.ToString() - formats signed 64-bit integer.
    /// </summary>
    public static string Int64_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        long value = *(long*)(thisPtr + 8);
        return System.Int64.FormatInt64(value);
    }

    /// <summary>
    /// UInt64.ToString() - formats unsigned 64-bit integer.
    /// </summary>
    public static string UInt64_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        ulong value = *(ulong*)(thisPtr + 8);
        return System.UInt64.FormatUInt64(value);
    }

    /// <summary>
    /// UInt32.ToString() - formats unsigned 32-bit integer.
    /// </summary>
    public static string UInt32_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        uint value = *(uint*)(thisPtr + 8);
        return System.UInt32.FormatUInt32(value);
    }

    /// <summary>
    /// IntPtr.ToString() - formats native integer.
    /// </summary>
    public static string IntPtr_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        nint value = *(nint*)(thisPtr + 8);
        return System.Int64.FormatInt64((long)value);
    }

    /// <summary>
    /// UIntPtr.ToString() - formats unsigned native integer.
    /// </summary>
    public static string UIntPtr_ToString(nint thisPtr)
    {
        if (thisPtr == 0) return "0";
        nuint value = *(nuint*)(thisPtr + 8);
        return System.UInt64.FormatUInt64((ulong)value);
    }

    /// <summary>
    /// Single.ToString() - placeholder for float formatting.
    /// </summary>
    public static string Single_ToString(nint thisPtr)
    {
        return "Single";  // TODO: Implement proper float formatting
    }

    /// <summary>
    /// Double.ToString() - placeholder for double formatting.
    /// </summary>
    public static string Double_ToString(nint thisPtr)
    {
        return "Double";  // TODO: Implement proper double formatting
    }

    // =========================================================================
    // GetHashCode helpers for primitive types
    // =========================================================================

    /// <summary>
    /// Byte.GetHashCode() - returns the byte value itself.
    /// </summary>
    public static int Byte_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(byte*)(thisPtr + 8);
    }

    /// <summary>
    /// SByte.GetHashCode() - returns the sbyte value itself.
    /// </summary>
    public static int SByte_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(sbyte*)(thisPtr + 8);
    }

    /// <summary>
    /// Boolean.GetHashCode() - returns 1 for true, 0 for false.
    /// </summary>
    public static int Boolean_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(bool*)(thisPtr + 8) ? 1 : 0;
    }

    /// <summary>
    /// Char.GetHashCode() - returns the char value itself.
    /// </summary>
    public static int Char_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(char*)(thisPtr + 8);
    }

    /// <summary>
    /// Int16.GetHashCode() - returns the short value itself.
    /// </summary>
    public static int Int16_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(short*)(thisPtr + 8);
    }

    /// <summary>
    /// UInt16.GetHashCode() - returns the ushort value itself.
    /// </summary>
    public static int UInt16_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return *(ushort*)(thisPtr + 8);
    }

    /// <summary>
    /// Int64.GetHashCode() - returns XOR of high and low 32 bits.
    /// </summary>
    public static int Int64_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        long value = *(long*)(thisPtr + 8);
        return (int)value ^ (int)(value >> 32);
    }

    /// <summary>
    /// UInt64.GetHashCode() - returns XOR of high and low 32 bits.
    /// </summary>
    public static int UInt64_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        ulong value = *(ulong*)(thisPtr + 8);
        return (int)value ^ (int)(value >> 32);
    }

    /// <summary>
    /// UInt32.GetHashCode() - returns the uint value itself.
    /// </summary>
    public static int UInt32_GetHashCode(nint thisPtr)
    {
        if (thisPtr == 0) return 0;
        return (int)*(uint*)(thisPtr + 8);
    }
}

/// <summary>
/// Helper methods for reflection equality operators.
/// These provide implementations for op_Equality/op_Inequality on reflection types.
/// </summary>
public static class ReflectionHelpers
{
    // MethodInfo equality - compare by metadata token (same method = same token)
    public static bool MethodInfoEquals(System.Reflection.MethodInfo? left, System.Reflection.MethodInfo? right)
    {
        if (left is null)
            return right is null;
        if (right is null)
            return false;
        // Reference equality first (fast path)
        if (ReferenceEquals(left, right))
            return true;
        // Compare by metadata token - same method has same token
        return left.MetadataToken == right.MetadataToken;
    }

    public static bool MethodInfoNotEquals(System.Reflection.MethodInfo? left, System.Reflection.MethodInfo? right)
        => !MethodInfoEquals(left, right);

    // MethodBase equality - compare by metadata token (same method = same token)
    public static bool MethodBaseEquals(System.Reflection.MethodBase? left, System.Reflection.MethodBase? right)
    {
        if (left is null)
            return right is null;
        if (right is null)
            return false;
        // Reference equality first (fast path)
        if (ReferenceEquals(left, right))
            return true;
        // Compare by metadata token - same method has same token
        return left.MetadataToken == right.MetadataToken;
    }

    public static bool MethodBaseNotEquals(System.Reflection.MethodBase? left, System.Reflection.MethodBase? right)
        => !MethodBaseEquals(left, right);

    // FieldInfo equality - reference comparison
    public static bool FieldInfoEquals(System.Reflection.FieldInfo? left, System.Reflection.FieldInfo? right)
    {
        if (left is null)
            return right is null;
        if (right is null)
            return false;
        return ReferenceEquals(left, right);
    }

    public static bool FieldInfoNotEquals(System.Reflection.FieldInfo? left, System.Reflection.FieldInfo? right)
        => !FieldInfoEquals(left, right);

    // MemberInfo equality - reference comparison
    public static bool MemberInfoEquals(System.Reflection.MemberInfo? left, System.Reflection.MemberInfo? right)
    {
        if (left is null)
            return right is null;
        if (right is null)
            return false;
        return ReferenceEquals(left, right);
    }

    public static bool MemberInfoNotEquals(System.Reflection.MemberInfo? left, System.Reflection.MemberInfo? right)
        => !MemberInfoEquals(left, right);

    /// <summary>
    /// Get the FieldType of a FieldInfo.
    /// Bypasses vtable dispatch since korlib reflection types don't have properly set up vtables.
    /// </summary>
    public static Type? FieldInfoGetFieldType(System.Reflection.FieldInfo field)
    {
        ProtonOS.Platform.DebugConsole.Write("[FieldInfoGetFieldType] called");
        ProtonOS.Platform.DebugConsole.WriteLine();

        if (field is null)
        {
            ProtonOS.Platform.DebugConsole.WriteLine("[FieldInfoGetFieldType] field is null");
            return null;
        }

        // Dispatch to the concrete implementation
        if (field is System.Reflection.RuntimeFieldInfo rfi)
        {
            ProtonOS.Platform.DebugConsole.WriteLine("[FieldInfoGetFieldType] -> RuntimeFieldInfo");
            var result = rfi.FieldType;
            if (result is null)
            {
                ProtonOS.Platform.DebugConsole.WriteLine("[FieldInfoGetFieldType] result is null");
            }
            else
            {
                ProtonOS.Platform.DebugConsole.Write("[FieldInfoGetFieldType] result=");
                var name = result.Name;
                if (name != null)
                {
                    ProtonOS.Platform.DebugConsole.Write(name);
                }
                ProtonOS.Platform.DebugConsole.WriteLine();
            }
            return result;
        }

        ProtonOS.Platform.DebugConsole.WriteLine("[FieldInfoGetFieldType] not RuntimeFieldInfo");
        // Fallback - shouldn't happen in practice
        return null;
    }

    /// <summary>
    /// Get the PropertyType of a PropertyInfo.
    /// Bypasses vtable dispatch since korlib reflection types don't have properly set up vtables.
    /// </summary>
    public static Type? PropertyInfoGetPropertyType(System.Reflection.PropertyInfo prop)
    {
        if (prop is null)
            return null;

        // Dispatch to the concrete implementation
        if (prop is System.Reflection.RuntimePropertyInfo rpi)
        {
            return rpi.PropertyType;
        }

        // Fallback - shouldn't happen in practice
        return null;
    }

    /// <summary>
    /// Get the parameters of a MethodInfo.
    /// Bypasses vtable dispatch since korlib reflection types don't have properly set up vtables.
    /// </summary>
    public static System.Reflection.ParameterInfo[]? MethodInfoGetParameters(System.Reflection.MethodInfo method)
    {
        if (method is null)
            return null;

        // Dispatch to the concrete implementation
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            // Get parameter count via kernel export
            uint assemblyId = rmi.AssemblyId;
            uint methodToken = (uint)rmi.MetadataToken;
            int count = Reflection.ReflectionRuntime.GetMethodParameterCount(assemblyId, methodToken);

            if (count <= 0)
                return new System.Reflection.ParameterInfo[0];

            // Create parameters manually to avoid vtable dispatch issues
            var parameters = new System.Reflection.ParameterInfo[count];
            for (int i = 0; i < count; i++)
            {
                parameters[i] = new System.Reflection.RuntimeParameterInfo(assemblyId, methodToken, i);
            }
            return parameters;
        }

        // Fallback - return empty array
        return new System.Reflection.ParameterInfo[0];
    }

    /// <summary>
    /// Get the return type of a MethodInfo.
    /// </summary>
    public static unsafe Type? MethodInfoGetReturnType(System.Reflection.MethodInfo method)
    {
        if (method is null)
            return null;

        // Dispatch to the concrete implementation
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            uint assemblyId = rmi.AssemblyId;
            uint methodToken = (uint)rmi.MetadataToken;
            void* mt = Reflection.ReflectionRuntime.GetMethodReturnTypeMethodTable(assemblyId, methodToken);
            if (mt != null)
                return Type.GetTypeFromHandle(new RuntimeTypeHandle((nint)mt));
        }

        // Fallback - return null (caller should handle)
        return null;
    }

    /// <summary>
    /// Get the base definition of a MethodInfo.
    /// </summary>
    public static System.Reflection.MethodInfo MethodInfoGetBaseDefinition(System.Reflection.MethodInfo method)
    {
        // For our minimal implementation, just return self
        return method;
    }

    /// <summary>
    /// Get the parameters of a MethodBase (called when IL references MethodBase::GetParameters).
    /// Dispatches to appropriate handler based on runtime type.
    /// </summary>
    public static System.Reflection.ParameterInfo[]? MethodBaseGetParameters(System.Reflection.MethodBase method)
    {
        if (method is null)
            return null;

        // Dispatch based on runtime type
        if (method is System.Reflection.MethodInfo mi)
        {
            return MethodInfoGetParameters(mi);
        }
        if (method is System.Reflection.ConstructorInfo ci)
        {
            return ConstructorInfoGetParameters(ci);
        }

        // Fallback - return empty array
        return new System.Reflection.ParameterInfo[0];
    }

    /// <summary>
    /// Get the Attributes of a MethodBase.
    /// </summary>
    public static System.Reflection.MethodAttributes MethodBaseGetAttributes(System.Reflection.MethodBase method)
    {
        if (method is null)
            return System.Reflection.MethodAttributes.PrivateScope;

        // Dispatch to the concrete implementation
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            return rmi.Attributes;
        }
        if (method is System.Reflection.RuntimeConstructorInfo rci)
        {
            return rci.Attributes;
        }

        // Fallback
        return System.Reflection.MethodAttributes.Public;
    }

    /// <summary>
    /// Get the IsPublic property of a MethodBase.
    /// </summary>
    public static bool MethodBaseGetIsPublic(System.Reflection.MethodBase method)
    {
        var attrs = MethodBaseGetAttributes(method);
        return (attrs & System.Reflection.MethodAttributes.MemberAccessMask) == System.Reflection.MethodAttributes.Public;
    }

    /// <summary>
    /// Get the IsStatic property of a MethodBase.
    /// </summary>
    public static bool MethodBaseGetIsStatic(System.Reflection.MethodBase method)
    {
        if (method is null)
            return false;

        // Dispatch to the concrete implementation - RuntimeMethodInfo has its own IsStatic
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            return rmi.IsStatic;
        }

        // Use Attributes for other types
        var attrs = MethodBaseGetAttributes(method);
        return (attrs & System.Reflection.MethodAttributes.Static) != 0;
    }

    /// <summary>
    /// Get the IsVirtual property of a MethodBase.
    /// </summary>
    public static bool MethodBaseGetIsVirtual(System.Reflection.MethodBase method)
    {
        if (method is null)
            return false;

        // Dispatch to the concrete implementation - RuntimeMethodInfo has its own IsVirtual
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            return rmi.IsVirtual;
        }

        // Use Attributes for other types
        var attrs = MethodBaseGetAttributes(method);
        return (attrs & System.Reflection.MethodAttributes.Virtual) != 0;
    }

    /// <summary>
    /// Get the IsAbstract property of a MethodBase.
    /// </summary>
    public static bool MethodBaseGetIsAbstract(System.Reflection.MethodBase method)
    {
        var attrs = MethodBaseGetAttributes(method);
        return (attrs & System.Reflection.MethodAttributes.Abstract) != 0;
    }

    /// <summary>
    /// Get the MethodBody of a MethodBase. Returns null for minimal implementation.
    /// </summary>
    public static System.Reflection.MethodBody? MethodBaseGetMethodBody(System.Reflection.MethodBase method)
    {
        // Our minimal implementation returns null
        return null;
    }

    /// <summary>
    /// Get the parameters of a ConstructorInfo.
    /// Bypasses vtable dispatch since korlib reflection types don't have properly set up vtables.
    /// </summary>
    public static System.Reflection.ParameterInfo[]? ConstructorInfoGetParameters(System.Reflection.ConstructorInfo ctor)
    {
        if (ctor is null)
            return null;

        // Dispatch to the concrete implementation
        if (ctor is System.Reflection.RuntimeConstructorInfo rci)
        {
            return rci.GetParameters();
        }

        // Fallback - return empty array
        return new System.Reflection.ParameterInfo[0];
    }

    /// <summary>
    /// Get the type of a ParameterInfo.
    /// Bypasses vtable dispatch since korlib reflection types don't have properly set up vtables.
    /// </summary>
    public static unsafe Type? ParameterInfoGetParameterType(System.Reflection.ParameterInfo param)
    {
        if (param is null)
            return null;

        // Dispatch to the concrete implementation
        if (param is System.Reflection.RuntimeParameterInfo rpi)
        {
            // Get parameter type MT via kernel export
            uint assemblyId = rpi.AssemblyId;
            uint methodToken = rpi.MethodToken;
            int position = rpi.Position;

            void* mt = Reflection.ReflectionRuntime.GetMethodParameterTypeMethodTable(assemblyId, methodToken, position);
            if (mt == null)
                return null;

            // Create RuntimeType from the MethodTable
            return Type.GetTypeFromHandle(new RuntimeTypeHandle((nint)mt));
        }

        // Fallback - return null
        return null;
    }

    /// <summary>
    /// Wrapper for MethodBase.Invoke that dispatches to the appropriate runtime implementation.
    /// This handles the virtual dispatch for RuntimeMethodInfo and RuntimeConstructorInfo.
    /// </summary>
    public static object? MethodBaseInvoke(System.Reflection.MethodBase method, object? target, object?[]? args)
    {
        if (method == null)
            return null;

        // Get the method token and assembly ID
        uint methodToken = (uint)method.MetadataToken;
        uint assemblyId = 0;

        // Get assembly ID from the concrete runtime type
        if (method is System.Reflection.RuntimeMethodInfo rmi)
        {
            assemblyId = rmi.AssemblyId;
        }
        else if (method is System.Reflection.RuntimeConstructorInfo rci)
        {
            assemblyId = rci.AssemblyId;
        }

        // Call the kernel's InvokeMethod implementation with assembly ID
        return Runtime.Reflection.ReflectionRuntime.InvokeMethod(assemblyId, methodToken, target, args);
    }
}

/// <summary>
/// Helper methods for MemberInfo operations.
/// Provides polymorphic dispatch for MemberInfo virtual methods.
/// </summary>
public static unsafe class MemberInfoHelpers
{
    /// <summary>
    /// Get the Name of any MemberInfo (Type, MethodInfo, FieldInfo, PropertyInfo, etc.).
    /// This is called when JIT code invokes callvirt MemberInfo.get_Name.
    /// Since the JIT doesn't do proper vtable dispatch, we need to check the runtime type
    /// and dispatch to the correct implementation.
    /// </summary>
    public static string? GetName(System.Reflection.MemberInfo member)
    {
        if (member == null)
            return null;

        ProtonOS.Platform.DebugConsole.Write("[MemberInfoHelpers.GetName] called");
        ProtonOS.Platform.DebugConsole.WriteLine();

        // Try to determine the actual type and dispatch accordingly
        // We check the types in order of likely frequency

        // Check if it's a RuntimeMethodInfo
        if (member is System.Reflection.RuntimeMethodInfo rmi)
        {
            ProtonOS.Platform.DebugConsole.Write("[MemberInfoHelpers.GetName] -> RuntimeMethodInfo");
            ProtonOS.Platform.DebugConsole.WriteLine();
            return rmi.Name;
        }

        // Check if it's a RuntimeFieldInfo
        if (member is System.Reflection.RuntimeFieldInfo rfi)
        {
            return rfi.Name;
        }

        // Check if it's a RuntimePropertyInfo
        if (member is System.Reflection.RuntimePropertyInfo rpi)
        {
            return rpi.Name;
        }

        // Check if it's a RuntimeConstructorInfo
        if (member is System.Reflection.RuntimeConstructorInfo rci)
        {
            return rci.Name;
        }

        // Check if it's a Type (RuntimeType)
        if (member is Type type)
        {
            ProtonOS.Platform.DebugConsole.Write("[MemberInfoHelpers.GetName] -> Type");
            ProtonOS.Platform.DebugConsole.WriteLine();
            return TypeMethodHelpers.GetName(type);
        }

        // Fallback - try to read name from the object directly
        // This is a last resort if we can't identify the type
        ProtonOS.Platform.DebugConsole.Write("[MemberInfoHelpers] Unknown MemberInfo type at 0x");
        ProtonOS.Platform.DebugConsole.WriteHex((ulong)System.Runtime.CompilerServices.Unsafe.AsPointer(ref member));
        ProtonOS.Platform.DebugConsole.WriteLine();
        return null;
    }
}

/// <summary>
/// Helper methods for Type operations.
/// These provide implementations for Type virtual methods that the JIT can call directly,
/// bypassing the broken vtable dispatch for RuntimeType.
/// </summary>
public static unsafe class TypeMethodHelpers
{
    /// <summary>
    /// Get the Name of a Type.
    /// </summary>
    public static string? GetName(Type type)
    {
        if (type == null)
            return null;

        // Use our GetTypeHandle helper to get the _pMethodTable
        nint typeHandleValue = GetTypeHandle(type);
        if (typeHandleValue == 0)
            return "RuntimeType";

        // Look up the type info from the reflection runtime using TypeHandle
        void* storedMT = (void*)typeHandleValue;
        uint asmId = 0, token = 0;
        ReflectionRuntime.GetTypeInfo(storedMT, &asmId, &token);

        if (token == 0)
            return "RuntimeType";

        // Get the type name from metadata
        byte* namePtr = ReflectionRuntime.GetTypeName(asmId, token);
        if (namePtr == null)
            return "RuntimeType";

        return BytePtrToString(namePtr);
    }

    /// <summary>
    /// Get the FullName of a Type (Namespace.Name).
    /// </summary>
    public static string? GetFullName(Type type)
    {
        if (type == null)
            return null;

        string? ns = GetNamespace(type);
        string? name = GetName(type);

        if (string.IsNullOrEmpty(ns))
            return name;

        return ns + "." + name;
    }

    /// <summary>
    /// Get the Namespace of a Type.
    /// </summary>
    public static string? GetNamespace(Type type)
    {
        if (type == null)
            return null;

        // Use our GetTypeHandle helper to get the _pMethodTable
        nint typeHandleValue = GetTypeHandle(type);
        if (typeHandleValue == 0)
            return null;

        // Look up the type info from the reflection runtime
        void* storedMT = (void*)typeHandleValue;
        uint asmId = 0, token = 0;
        ReflectionRuntime.GetTypeInfo(storedMT, &asmId, &token);

        if (token == 0)
            return null;

        // Get the namespace from metadata
        byte* nsPtr = ReflectionRuntime.GetTypeNamespace(asmId, token);
        if (nsPtr == null || *nsPtr == 0)
            return null;

        return BytePtrToString(nsPtr);
    }

    /// <summary>
    /// Convert a null-terminated UTF-8 byte pointer to a string.
    /// </summary>
    private static string BytePtrToString(byte* ptr)
    {
        if (ptr == null)
            return string.Empty;

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
    /// Get the TypeHandle (RuntimeTypeHandle) for a Type.
    /// Returns the internal _pMethodTable as an nint.
    /// </summary>
    public static nint GetTypeHandle(Type type)
    {
        if (type == null)
            return 0;

        // Based on empirical testing, RuntimeType's _pMethodTable is at offset 0x18 (24 bytes)
        // This accounts for: Object header (8) + some padding/other fields (16) = 24

        // Get the object pointer
        void* typePtr = *(void**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref type);
        if (typePtr == null)
            return 0;

        // Read _pMethodTable at offset 0x18 (24)
        void* storedMT = *(void**)((byte*)typePtr + 0x18);
        return (nint)storedMT;
    }

    /// <summary>
    /// Create a Type from a RuntimeTypeHandle (MethodTable pointer).
    /// This is the implementation for typeof() operator support.
    /// The handle parameter is the MethodTable pointer from ldtoken.
    /// </summary>
    public static Type? GetTypeFromHandle(nint handle)
    {
        ProtonOS.Platform.DebugConsole.Write("[GetTypeFromHandle] handle=0x");
        ProtonOS.Platform.DebugConsole.WriteHex((ulong)handle);
        ProtonOS.Platform.DebugConsole.WriteLine();

        if (handle == 0)
            return null;

        // Create RuntimeType directly from MethodTable pointer
        // Cast to System.Runtime.MethodTable* which RuntimeType expects
        var result = new RuntimeType((System.Runtime.MethodTable*)handle);

        // Debug: check what got stored via TypeHandle property
        nint typeHandleValue = result.TypeHandle.Value;
        ProtonOS.Platform.DebugConsole.Write("[GetTypeFromHandle] TypeHandle.Value=0x");
        ProtonOS.Platform.DebugConsole.WriteHex((ulong)typeHandleValue);
        ProtonOS.Platform.DebugConsole.WriteLine();

        return result;
    }

    /// <summary>
    /// Create a MethodBase from a RuntimeMethodHandle.
    /// The handle contains (assemblyId &lt;&lt; 32) | methodToken.
    /// </summary>
    public static System.Reflection.MethodBase? GetMethodFromHandle(nint handle)
    {
        if (handle == 0)
            return null;
        // Decode: high 32 bits = assemblyId, low 32 bits = token
        ulong value = (ulong)handle;
        uint assemblyId = (uint)(value >> 32);
        uint token = (uint)(value & 0xFFFFFFFF);
        if (assemblyId == 0 || token == 0)
            return null;
        // Create RuntimeMethodInfo - declaringType will be resolved later if needed
        return new System.Reflection.RuntimeMethodInfo(assemblyId, token, null!);
    }

    /// <summary>
    /// Create a FieldInfo from a RuntimeFieldHandle.
    /// The handle contains (assemblyId &lt;&lt; 32) | fieldToken.
    /// </summary>
    public static System.Reflection.FieldInfo? GetFieldFromHandle(nint handle)
    {
        if (handle == 0)
            return null;
        // Decode: high 32 bits = assemblyId, low 32 bits = token
        ulong value = (ulong)handle;
        uint assemblyId = (uint)(value >> 32);
        uint token = (uint)(value & 0xFFFFFFFF);
        if (assemblyId == 0 || token == 0)
            return null;
        // Create RuntimeFieldInfo - field details will be resolved via reflection exports
        return new System.Reflection.RuntimeFieldInfo(assemblyId, token, null!, 0, 0, false);
    }

    /// <summary>
    /// Get all public methods of a Type.
    /// Wrapper for Type.GetMethods() virtual call.
    /// </summary>
    public static System.Reflection.MethodInfo[] GetMethods(Type type)
    {
        if (type == null)
            return System.Array.Empty<System.Reflection.MethodInfo>();

        // Cast to RuntimeType and call directly to bypass virtual dispatch issues
        if (type is RuntimeType rt)
        {
            // Debug: get the internal state
            var asmId = rt.GetAssemblyIdInternal();
            var token = rt.GetTypeTokenInternal();
            DebugConsole.Write("[GetMethods] asmId=");
            DebugConsole.WriteDecimal(asmId);
            DebugConsole.Write(" token=0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();

            var result = rt.GetMethodsInternal();
            DebugConsole.Write("[GetMethods] Got ");
            DebugConsole.WriteDecimal((uint)result.Length);
            DebugConsole.WriteLine(" methods");
            return result;
        }

        // Fallback for other Type implementations
        return type.GetMethods();
    }

    /// <summary>
    /// Get all public fields of a Type.
    /// Wrapper for Type.GetFields() virtual call.
    /// </summary>
    public static System.Reflection.FieldInfo[] GetFields(Type type)
    {
        if (type == null)
            return System.Array.Empty<System.Reflection.FieldInfo>();

        // Cast to RuntimeType and call directly to bypass virtual dispatch issues
        if (type is RuntimeType rt)
            return rt.GetFieldsInternal();

        return type.GetFields();
    }

    /// <summary>
    /// Get all public constructors of a Type.
    /// Wrapper for Type.GetConstructors() virtual call.
    /// </summary>
    public static System.Reflection.ConstructorInfo[] GetConstructors(Type type)
    {
        if (type == null)
            return System.Array.Empty<System.Reflection.ConstructorInfo>();

        // Cast to RuntimeType and call directly to bypass virtual dispatch issues
        if (type is RuntimeType rt)
            return rt.GetConstructorsInternal();

        return type.GetConstructors();
    }

    /// <summary>
    /// Get a method by name.
    /// Wrapper for Type.GetMethod(string) virtual call.
    /// </summary>
    public static System.Reflection.MethodInfo? GetMethod(Type type, string? name)
    {
        ProtonOS.Platform.DebugConsole.Write("[AOT.GetMethod] type=");
        ProtonOS.Platform.DebugConsole.Write(type == null ? "null" : "ok");
        ProtonOS.Platform.DebugConsole.Write(" name=");
        ProtonOS.Platform.DebugConsole.Write(name ?? "null");
        ProtonOS.Platform.DebugConsole.WriteLine();

        if (type == null || name == null)
            return null;

        // Cast to RuntimeType and call directly to bypass virtual dispatch issues
        System.Reflection.MethodInfo? result;
        if (type is RuntimeType rt)
            result = rt.GetMethodInternal(name);
        else
            result = type.GetMethod(name);

        ProtonOS.Platform.DebugConsole.Write("[AOT.GetMethod] result=");
        ProtonOS.Platform.DebugConsole.Write(result == null ? "null" : "found");
        ProtonOS.Platform.DebugConsole.WriteLine();

        return result;
    }

    /// <summary>
    /// Get a field by name.
    /// Wrapper for Type.GetField(string) virtual call.
    /// </summary>
    public static System.Reflection.FieldInfo? GetField(Type type, string? name)
    {
        if (type == null || name == null)
            return null;

        // Cast to RuntimeType and call directly to bypass virtual dispatch issues
        if (type is RuntimeType rt)
            return rt.GetFieldInternal(name);

        return type.GetField(name);
    }
}

/// <summary>
/// Factory methods for Exception types.
/// These are used by JIT code to create exception instances via newobj.
/// The JIT transforms newobj Exception::.ctor to a call to these factory methods.
/// </summary>
public static class ExceptionHelpers
{
    // Exception
    public static Exception Ctor_Exception() => new Exception();
    public static Exception Ctor_Exception_String(string? message) => new Exception(message);
    public static Exception Ctor_Exception_String_Exception(string? message, Exception? innerException) => new Exception(message, innerException);

    // ArgumentException
    public static ArgumentException Ctor_ArgumentException() => new ArgumentException();
    public static ArgumentException Ctor_ArgumentException_String(string? message) => new ArgumentException(message);

    // ArgumentNullException
    public static ArgumentNullException Ctor_ArgumentNullException() => new ArgumentNullException();
    public static ArgumentNullException Ctor_ArgumentNullException_String(string? paramName) => new ArgumentNullException(paramName);

    // InvalidOperationException
    public static InvalidOperationException Ctor_InvalidOperationException() => new InvalidOperationException();
    public static InvalidOperationException Ctor_InvalidOperationException_String(string? message) => new InvalidOperationException(message);

    // NotSupportedException
    public static NotSupportedException Ctor_NotSupportedException() => new NotSupportedException();
    public static NotSupportedException Ctor_NotSupportedException_String(string? message) => new NotSupportedException(message);

    // NotImplementedException
    public static NotImplementedException Ctor_NotImplementedException() => new NotImplementedException();
    public static NotImplementedException Ctor_NotImplementedException_String(string? message) => new NotImplementedException(message);

    // IndexOutOfRangeException
    public static IndexOutOfRangeException Ctor_IndexOutOfRangeException() => new IndexOutOfRangeException();
    public static IndexOutOfRangeException Ctor_IndexOutOfRangeException_String(string? message) => new IndexOutOfRangeException(message);

    // NullReferenceException
    public static NullReferenceException Ctor_NullReferenceException() => new NullReferenceException();
    public static NullReferenceException Ctor_NullReferenceException_String(string? message) => new NullReferenceException(message);

    // InvalidCastException
    public static InvalidCastException Ctor_InvalidCastException() => new InvalidCastException();
    public static InvalidCastException Ctor_InvalidCastException_String(string? message) => new InvalidCastException(message);

    // FormatException
    public static FormatException Ctor_FormatException() => new FormatException();
    public static FormatException Ctor_FormatException_String(string? message) => new FormatException(message);

    // AggregateException
    public static AggregateException Ctor_AggregateException() => new AggregateException();
    public static AggregateException Ctor_AggregateException_String(string? message) => new AggregateException(message);
    public static AggregateException Ctor_AggregateException_String_Exception(string? message, Exception innerException)
    {
        return new AggregateException(message, innerException);
    }
    public static AggregateException Ctor_AggregateException_String_ExceptionArray(string? message, Exception[] innerExceptions) => new AggregateException(message, innerExceptions);
    public static AggregateException Ctor_AggregateException_String_ListException(string? message, System.Collections.Generic.List<Exception> innerExceptions) => new AggregateException(message, innerExceptions);
    public static System.Collections.ObjectModel.ReadOnlyCollection<Exception> AggregateException_get_InnerExceptions(AggregateException ex) => ex.InnerExceptions;
    public static AggregateException AggregateException_Flatten(AggregateException ex) => ex.Flatten();

    // Exception property getters
    public static string? Exception_get_Message(Exception ex) => ex.Message;
    public static Exception? Exception_get_InnerException(Exception ex) => ex.InnerException;

    // TaskCanceledException
    public static System.Threading.Tasks.TaskCanceledException Ctor_TaskCanceledException() => new System.Threading.Tasks.TaskCanceledException();
    public static System.Threading.Tasks.TaskCanceledException Ctor_TaskCanceledException_String(string? message) => new System.Threading.Tasks.TaskCanceledException(message);
    public static System.Threading.Tasks.TaskCanceledException Ctor_TaskCanceledException_String_Exception(string? message, Exception? innerException) => new System.Threading.Tasks.TaskCanceledException(message, innerException);
}

/// <summary>
/// Wrapper methods for Delegate operations.
/// These forward to the actual Delegate.Combine/Remove methods in korlib.
/// </summary>
public static class DelegateHelpers
{
    /// <summary>
    /// Wrapper for Delegate.Combine(Delegate?, Delegate?).
    /// Combines two delegates into a multicast delegate.
    /// </summary>
    public static Delegate? Combine(Delegate? a, Delegate? b)
    {
        return Delegate.Combine(a, b);
    }

    /// <summary>
    /// Wrapper for Delegate.Remove(Delegate?, Delegate?).
    /// Removes a delegate from a multicast delegate.
    /// </summary>
    public static Delegate? Remove(Delegate? source, Delegate? value)
    {
        return Delegate.Remove(source, value);
    }

    /// <summary>
    /// Wrapper for MulticastDelegate.CombineImpl for vtable slot population.
    /// This is called through the vtable when combining delegates.
    /// </summary>
    public static Delegate? CombineImplWrapper(MulticastDelegate self, Delegate? d)
    {
        return self.InvokeCombineImpl(d);
    }

    /// <summary>
    /// Wrapper for MulticastDelegate.RemoveImpl for vtable slot population.
    /// This is called through the vtable when removing delegates.
    /// </summary>
    public static Delegate? RemoveImplWrapper(MulticastDelegate self, Delegate d)
    {
        return self.InvokeRemoveImpl(d);
    }
}

/// <summary>
/// Helper methods for Array operations.
/// These provide AOT-compiled implementations of System.Array methods
/// that JIT-compiled code can call.
/// </summary>
public static unsafe class ArrayHelpers
{
    /// <summary>
    /// Get the total number of elements in the array.
    /// Works for both single-dimension (SZARRAY) and multi-dimension (ARRAY) types.
    /// The length is stored at offset 8 from the array pointer (after the MethodTable pointer).
    /// </summary>
    public static int GetLength(Array array)
    {
        if (array == null)
            return 0;

        // Length is stored at offset 8 from the object pointer
        // This works for both 1D arrays and MD arrays
        // Note: Unsafe.AsPointer(ref array) gives us the address of the local variable,
        // so we dereference it to get the actual object pointer
        byte* ptr = *(byte**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref array);
        return *(int*)(ptr + 8);
    }

    /// <summary>
    /// Get the total number of elements as a 64-bit integer.
    /// </summary>
    public static long GetLongLength(Array array)
    {
        return GetLength(array);
    }

    /// <summary>
    /// Get the rank (number of dimensions) of the array.
    /// For single-dimension arrays (SZARRAY), returns 1.
    /// For multi-dimension arrays (ARRAY), returns the stored rank.
    /// </summary>
    public static int GetRank(Array array)
    {
        if (array == null)
            return 0;

        // Check if this is an MD array by examining the MethodTable's BaseSize
        // MD arrays have BaseSize = 16 + 8 * rank, so rank = (BaseSize - 16) / 8
        // 1D arrays have BaseSize = 16 (MT* + Length), which gives (16-16)/8 = 0
        // But 1D arrays should return 1, so we use: rank > 0 ? rank : 1
        // Note: Dereference to get the object pointer from the local variable
        byte* ptr = *(byte**)System.Runtime.CompilerServices.Unsafe.AsPointer(ref array);
        MethodTable* mt = *(MethodTable**)ptr;
        int baseSize = (int)mt->BaseSize;

        // For MD arrays, calculate rank from BaseSize
        // BaseSize = 16 + 8 * rank
        int rank = (baseSize - 16) / 8;
        return rank > 0 ? rank : 1;  // 1D arrays return 1
    }

    /// <summary>
    /// Copy elements from source array to destination array.
    /// This is the 3-argument overload (copies from start of both arrays).
    /// </summary>
    public static void Copy3(Array sourceArray, Array destinationArray, int length)
    {
        System.Array.Copy(sourceArray, 0, destinationArray, 0, length);
    }

    /// <summary>
    /// Copy elements from source array to destination array with indices.
    /// This is the 5-argument overload.
    /// </summary>
    public static void Copy5(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
    {
        System.Array.Copy(sourceArray, sourceIndex, destinationArray, destinationIndex, length);
    }

    /// <summary>
    /// Copy elements (64-bit indices).
    /// </summary>
    public static void Copy5Long(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length)
    {
        System.Array.Copy(sourceArray, sourceIndex, destinationArray, destinationIndex, length);
    }

    /// <summary>
    /// Clear elements in an array (set to default values).
    /// This is the 3-argument overload (array, index, length).
    /// </summary>
    public static void Clear3(Array array, int index, int length)
    {
        System.Array.Clear(array, index, length);
    }

    /// <summary>
    /// Clear all elements in an array (set to default values).
    /// This is the 1-argument overload (clear entire array).
    /// </summary>
    public static void Clear1(Array array)
    {
        System.Array.Clear(array);
    }
}

/// <summary>
/// Helper methods for ArgIterator operations.
/// ArgIterator is a ref struct with a single _current pointer field (8 bytes).
/// Varargs are stored as TypedReference entries (16 bytes each: value ptr + type ptr).
/// The list ends with a sentinel TypedReference (type = 0).
/// </summary>
public static unsafe class ArgIteratorHelpers
{
    /// <summary>
    /// Initialize ArgIterator from RuntimeArgumentHandle.
    /// 'thisPtr' points to the ArgIterator struct on the stack.
    /// 'handle' is the RuntimeArgumentHandle value (a pointer to varargs).
    /// </summary>
    public static void Ctor(nint thisPtr, nint handle)
    {
        // ArgIterator._current is at offset 0
        *(nint*)thisPtr = handle;
    }

    /// <summary>
    /// Get the next TypedReference from the vararg list.
    /// Uses hidden buffer return: RCX=retBuf, RDX=thisPtr
    /// (Registered with ReturnStructSize=17 to force hidden buffer mode)
    /// </summary>
    public static void GetNextArg(nint retBuf, nint thisPtr)
    {
        // Read current position from ArgIterator._current (offset 0)
        byte* current = *(byte**)thisPtr;

        // Copy TypedReference (16 bytes) to return buffer
        nint* src = (nint*)current;
        nint* dst = (nint*)retBuf;
        dst[0] = src[0];  // _value
        dst[1] = src[1];  // _type

        // Advance current pointer by 16 bytes
        *(byte**)thisPtr = current + 16;
    }

    /// <summary>
    /// Count remaining varargs by scanning for sentinel (type = 0).
    /// 'thisPtr' points to the ArgIterator struct.
    /// </summary>
    public static int GetRemainingCount(nint thisPtr)
    {
        // Read current position
        byte* current = *(byte**)thisPtr;

        int count = 0;
        // TypedReference layout: +0 = value (nint), +8 = type (nint)
        // Count until type == 0 (sentinel)
        while (*(nint*)(current + 8) != 0)
        {
            count++;
            current += 16;
        }
        return count;
    }

    /// <summary>
    /// End iteration. No-op in our implementation.
    /// </summary>
    public static void End(nint thisPtr)
    {
        // Nothing to clean up
    }
}

/// <summary>
/// Helper methods for Span operations.
/// Span<T> is a ref struct with layout: [0..7] = pointer to data, [8..11] = length
/// These helpers operate on the raw memory layout to support JIT-compiled code.
/// </summary>
public static unsafe class SpanHelpers
{
    /// <summary>
    /// Initialize a Span<byte> from a byte array.
    /// spanPtr points to a 16-byte stack allocation for the Span struct.
    /// </summary>
    public static void InitByteSpanFromArray(nint spanPtr, byte[]? array)
    {
        byte* spanBytes = (byte*)spanPtr;

        if (array == null || array.Length == 0)
        {
            // Default span - null pointer and zero length
            *(nint*)spanBytes = 0;
            *(int*)(spanBytes + 8) = 0;
            return;
        }

        // Get pointer to array data (skip MT and length at offsets 0 and 8)
        byte* arrayPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref array[0]);
        *(nint*)spanBytes = (nint)arrayPtr;
        *(int*)(spanBytes + 8) = array.Length;
    }

    /// <summary>
    /// Initialize a Span<int> from an int array.
    /// </summary>
    public static void InitIntSpanFromArray(nint spanPtr, int[]? array)
    {
        byte* spanBytes = (byte*)spanPtr;

        if (array == null || array.Length == 0)
        {
            *(nint*)spanBytes = 0;
            *(int*)(spanBytes + 8) = 0;
            return;
        }

        byte* arrayPtr = (byte*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref array[0]);
        *(nint*)spanBytes = (nint)arrayPtr;
        *(int*)(spanBytes + 8) = array.Length;
    }

    /// <summary>
    /// Initialize a Span from a pointer and length.
    /// Works for any T since layout is the same.
    /// </summary>
    public static void InitSpanFromPointer(nint spanPtr, void* pointer, int length)
    {
        byte* spanBytes = (byte*)spanPtr;
        *(nint*)spanBytes = (nint)pointer;
        *(int*)(spanBytes + 8) = length;
    }

    /// <summary>
    /// Get the length of a Span. Works for any T since layout is the same.
    /// </summary>
    public static int GetLength(nint spanPtr)
    {
        return *(int*)((byte*)spanPtr + 8);
    }

    /// <summary>
    /// Get the data pointer from a Span. Works for any T.
    /// </summary>
    public static nint GetPointer(nint spanPtr)
    {
        return *(nint*)spanPtr;
    }

    /// <summary>
    /// Get a byte from Span<byte> at the specified index.
    /// </summary>
    public static byte GetByte(nint spanPtr, int index)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        if ((uint)index >= (uint)length)
            Environment.FailFast(null);

        byte* data = (byte*)*(nint*)spanPtr;
        return data[index];
    }

    /// <summary>
    /// Set a byte in Span<byte> at the specified index.
    /// </summary>
    public static void SetByte(nint spanPtr, int index, byte value)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        if ((uint)index >= (uint)length)
            Environment.FailFast(null);

        byte* data = (byte*)*(nint*)spanPtr;
        data[index] = value;
    }

    /// <summary>
    /// Get an int from Span<int> at the specified index.
    /// </summary>
    public static int GetInt(nint spanPtr, int index)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        if ((uint)index >= (uint)length)
            Environment.FailFast(null);

        int* data = (int*)*(nint*)spanPtr;
        return data[index];
    }

    /// <summary>
    /// Set an int in Span<int> at the specified index.
    /// </summary>
    public static void SetInt(nint spanPtr, int index, int value)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        if ((uint)index >= (uint)length)
            Environment.FailFast(null);

        int* data = (int*)*(nint*)spanPtr;
        data[index] = value;
    }

    /// <summary>
    /// Clear a Span<byte> (set all bytes to 0).
    /// </summary>
    public static void ClearByteSpan(nint spanPtr)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        byte* data = (byte*)*(nint*)spanPtr;

        for (int i = 0; i < length; i++)
            data[i] = 0;
    }

    /// <summary>
    /// Fill a Span<byte> with a value.
    /// </summary>
    public static void FillByteSpan(nint spanPtr, byte value)
    {
        int length = *(int*)((byte*)spanPtr + 8);
        byte* data = (byte*)*(nint*)spanPtr;

        for (int i = 0; i < length; i++)
            data[i] = value;
    }

    /// <summary>
    /// Check if a Span is empty (length == 0).
    /// </summary>
    public static bool IsEmpty(nint spanPtr)
    {
        return *(int*)((byte*)spanPtr + 8) == 0;
    }
}

/// <summary>
/// Helper methods for collection types.
/// </summary>
public static unsafe class CollectionHelpers
{
    /// <summary>
    /// Get the Count of a ReadOnlyCollection&lt;T&gt;.
    /// ReadOnlyCollection layout: [MT 8] [_list 8] [_concreteList 8]
    /// The _concreteList is a List&lt;T&gt; which has layout: [MT 8] [_items 8] [_size 4] ...
    /// </summary>
    public static int ReadOnlyCollectionException_get_Count(System.Collections.ObjectModel.ReadOnlyCollection<Exception> collection)
    {
        // Access the underlying list directly via object layout
        // ReadOnlyCollection has: [MT 8] [_list 8] [_concreteList 8]
        // _concreteList at offset 16 is the concrete List<T>
        byte* objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.As<System.Collections.ObjectModel.ReadOnlyCollection<Exception>, nint>(ref collection);
        nint concreteListPtr = *(nint*)(objPtr + 16);  // Skip MT (8) + _list (8), get _concreteList

        if (concreteListPtr == 0)
        {
            // Fall back to _list at offset 8
            concreteListPtr = *(nint*)(objPtr + 8);
        }

        // The list is a List<T>
        // List<T> layout: [MT 8] [_items 8] [_size 4] ...
        byte* listBytes = (byte*)concreteListPtr;
        int size = *(int*)(listBytes + 16);  // Skip MT (8) + _items (8), get _size (4)
        return size;
    }

    /// <summary>
    /// Get an item from a ReadOnlyCollection&lt;T&gt; by index.
    /// ReadOnlyCollection layout: [MT 8] [_list 8] [_concreteList 8]
    /// We access the underlying _concreteList (List&lt;T&gt;) directly via memory layout.
    /// </summary>
    public static Exception? ReadOnlyCollectionException_get_Item(System.Collections.ObjectModel.ReadOnlyCollection<Exception> collection, int index)
    {
        // Access the underlying list directly via object layout
        // ReadOnlyCollection has: [MT 8] [_list 8] [_concreteList 8]
        // _concreteList at offset 16 is the concrete List<T> (or null if IList was passed)
        byte* objPtr = (byte*)System.Runtime.CompilerServices.Unsafe.As<System.Collections.ObjectModel.ReadOnlyCollection<Exception>, nint>(ref collection);
        nint concreteListPtr = *(nint*)(objPtr + 16);  // Skip MT (8) + _list (8), get _concreteList

        if (concreteListPtr == 0)
        {
            // Fall back to _list (IList<T>) at offset 8
            concreteListPtr = *(nint*)(objPtr + 8);
        }

        // The list is a List<T>
        // List<T> layout: [MT 8] [_items 8] [_size 4] ...
        byte* listBytes = (byte*)concreteListPtr;
        nint itemsArrayPtr = *(nint*)(listBytes + 8);  // Skip MT, get _items array

        // Array layout: [MT 8] [Length 8] [Elements...]
        // For reference type arrays, elements are pointers at offset 16
        byte* arrayBytes = (byte*)itemsArrayPtr;
        nint* elements = (nint*)(arrayBytes + 16);  // Skip MT + Length

        return System.Runtime.CompilerServices.Unsafe.As<nint, Exception?>(ref elements[index]);
    }

    /// <summary>
    /// Create a new ReadOnlyCollection&lt;Exception&gt; wrapping the given list.
    /// </summary>
    public static System.Collections.ObjectModel.ReadOnlyCollection<Exception> ReadOnlyCollectionException_Ctor(System.Collections.Generic.IList<Exception> list)
    {
        // Simply create a new ReadOnlyCollection wrapping the list
        return new System.Collections.ObjectModel.ReadOnlyCollection<Exception>(list);
    }
}

/// <summary>
/// Helper methods for GC operations.
/// These are no-ops in bare-metal environment but need to be resolvable by JIT.
/// </summary>
public static class GCHelpers
{
    /// <summary>
    /// Suppress finalization for an object.
    /// In bare-metal environment, this is a no-op since we don't have a GC finalizer thread.
    /// </summary>
    public static void SuppressFinalize(object? obj)
    {
        // No-op - no finalizer thread in bare-metal environment
    }

    /// <summary>
    /// Keep an object alive until this point.
    /// In bare-metal environment, this is a no-op since we don't have GC.
    /// </summary>
    public static void KeepAlive(object? obj)
    {
        // No-op - objects stay alive as long as they're referenced
    }

    /// <summary>
    /// Force a garbage collection.
    /// In bare-metal environment, this is a no-op since we don't have GC.
    /// </summary>
    public static void Collect()
    {
        // No-op - no garbage collector in bare-metal environment
    }
}

/// <summary>
/// Helper methods for System.Threading.Thread AOT registration.
/// </summary>
public static class ThreadHelpers
{
    // Thread-local storage for current Thread object
    // We create one Thread object per kernel thread and cache it
    private static System.Threading.Thread? s_cachedThread;
    private static uint s_cachedThreadId;

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    public static void Sleep_Int(int millisecondsTimeout)
    {
        if (millisecondsTimeout < -1)
            throw new ArgumentOutOfRangeException(nameof(millisecondsTimeout));

        if (millisecondsTimeout == -1)
        {
            // Infinite sleep - sleep in chunks
            while (true)
                ProtonOS.Threading.Scheduler.Sleep(uint.MaxValue);
        }

        ProtonOS.Threading.Scheduler.Sleep((uint)millisecondsTimeout);
    }

    /// <summary>
    /// Sleep for the specified TimeSpan.
    /// </summary>
    public static void Sleep_TimeSpan(TimeSpan timeout)
    {
        long ms = (long)timeout.TotalMilliseconds;
        if (ms < -1 || ms > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        Sleep_Int((int)ms);
    }

    /// <summary>
    /// Yield execution to another thread.
    /// </summary>
    public static bool Yield()
    {
        ProtonOS.Threading.Scheduler.Yield();
        return true;
    }

    /// <summary>
    /// Spin wait for the specified number of iterations.
    /// </summary>
    public static void SpinWait(int iterations)
    {
        for (int i = 0; i < iterations; i++)
        {
            // Just a tight loop
        }
    }

    /// <summary>
    /// Get the current thread.
    /// </summary>
    public static System.Threading.Thread? get_CurrentThread()
    {
        uint currentId = ProtonOS.Threading.Scheduler.GetCurrentThreadId();

        // Check if we already have a cached Thread for this kernel thread
        if (s_cachedThread != null && s_cachedThreadId == currentId)
            return s_cachedThread;

        // Create a new Thread object for this kernel thread
        // Note: We use a simple approach - create a Thread with just the ID
        s_cachedThread = CreateThreadForCurrentKernelThread(currentId);
        s_cachedThreadId = currentId;
        return s_cachedThread;
    }

    /// <summary>
    /// Get the ManagedThreadId of a Thread.
    /// </summary>
    public static int get_ManagedThreadId(System.Threading.Thread thread)
    {
        return thread.ManagedThreadId;
    }

    /// <summary>
    /// Create a Thread object representing the current kernel thread.
    /// </summary>
    private static System.Threading.Thread CreateThreadForCurrentKernelThread(uint threadId)
    {
        // Use the internal factory method in Thread class
        return System.Threading.Thread.CreateForKernelThread((int)threadId);
    }
}
