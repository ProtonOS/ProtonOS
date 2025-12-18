// ProtonOS JIT - IL Compiler
// Compiles IL bytecode to x64 machine code using naive stack-based approach.

using ProtonOS.Platform;
using ProtonOS.X64;
using ProtonOS.Arch;
using ProtonOS.Memory;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// IL opcode values (ECMA-335 Partition III)
/// </summary>
public static class ILOpcode
{
    // Constants
    public const byte Nop = 0x00;
    public const byte Break = 0x01;
    public const byte Ldnull = 0x14;
    public const byte Ldc_I4_M1 = 0x15;
    public const byte Ldc_I4_0 = 0x16;
    public const byte Ldc_I4_1 = 0x17;
    public const byte Ldc_I4_2 = 0x18;
    public const byte Ldc_I4_3 = 0x19;
    public const byte Ldc_I4_4 = 0x1A;
    public const byte Ldc_I4_5 = 0x1B;
    public const byte Ldc_I4_6 = 0x1C;
    public const byte Ldc_I4_7 = 0x1D;
    public const byte Ldc_I4_8 = 0x1E;
    public const byte Ldc_I4_S = 0x1F;
    public const byte Ldc_I4 = 0x20;
    public const byte Ldc_I8 = 0x21;
    public const byte Ldc_R4 = 0x22;
    public const byte Ldc_R8 = 0x23;

    // Locals
    public const byte Ldloc_0 = 0x06;
    public const byte Ldloc_1 = 0x07;
    public const byte Ldloc_2 = 0x08;
    public const byte Ldloc_3 = 0x09;
    public const byte Ldloc_S = 0x11;
    public const byte Stloc_0 = 0x0A;
    public const byte Stloc_1 = 0x0B;
    public const byte Stloc_2 = 0x0C;
    public const byte Stloc_3 = 0x0D;
    public const byte Stloc_S = 0x13;

    // Arguments
    public const byte Ldarg_0 = 0x02;
    public const byte Ldarg_1 = 0x03;
    public const byte Ldarg_2 = 0x04;
    public const byte Ldarg_3 = 0x05;
    public const byte Ldarg_S = 0x0E;
    public const byte Starg_S = 0x10;

    // Stack operations
    public const byte Dup = 0x25;
    public const byte Pop = 0x26;
    public const byte Jmp = 0x27;  // Tail jump (rare)

    // Arithmetic
    public const byte Add = 0x58;
    public const byte Sub = 0x59;
    public const byte Mul = 0x5A;
    public const byte Div = 0x5B;
    public const byte Div_Un = 0x5C;
    public const byte Rem = 0x5D;
    public const byte Rem_Un = 0x5E;
    public const byte And = 0x5F;
    public const byte Or = 0x60;
    public const byte Xor = 0x61;
    public const byte Shl = 0x62;
    public const byte Shr = 0x63;
    public const byte Shr_Un = 0x64;
    public const byte Neg = 0x65;
    public const byte Not = 0x66;

    // Conversion
    public const byte Conv_I1 = 0x67;
    public const byte Conv_I2 = 0x68;
    public const byte Conv_I4 = 0x69;
    public const byte Conv_I8 = 0x6A;
    public const byte Conv_R4 = 0x6B;
    public const byte Conv_R8 = 0x6C;
    public const byte Conv_U4 = 0x6D;
    public const byte Conv_U8 = 0x6E;
    public const byte Conv_R_Un = 0x76;  // Convert unsigned integer to floating point
    public const byte Conv_U2 = 0xD1;
    public const byte Conv_U1 = 0xD2;
    public const byte Conv_I = 0xD3;
    public const byte Conv_U = 0xE0;

    // Arithmetic with overflow checking
    public const byte Add_Ovf = 0xD6;
    public const byte Add_Ovf_Un = 0xD7;
    public const byte Mul_Ovf = 0xD8;
    public const byte Mul_Ovf_Un = 0xD9;
    public const byte Sub_Ovf = 0xDA;
    public const byte Sub_Ovf_Un = 0xDB;

    // Floating point check
    public const byte Ckfinite = 0xC3;  // Check if value is finite (not NaN or infinity)

    // Typed references (rare)
    public const byte Refanyval = 0xC2;  // Get value address from typed reference
    public const byte Mkrefany = 0xC6;   // Make typed reference

    // Conversion with overflow checking (signed source)
    public const byte Conv_Ovf_I1 = 0xB3;
    public const byte Conv_Ovf_U1 = 0xB4;
    public const byte Conv_Ovf_I2 = 0xB5;
    public const byte Conv_Ovf_U2 = 0xB6;
    public const byte Conv_Ovf_I4 = 0xB7;
    public const byte Conv_Ovf_U4 = 0xB8;
    public const byte Conv_Ovf_I8 = 0xB9;
    public const byte Conv_Ovf_U8 = 0xBA;
    public const byte Conv_Ovf_I = 0xD4;
    public const byte Conv_Ovf_U = 0xD5;

    // Comparison
    public const byte Ceq = 0xFE;  // 0xFE 0x01 two-byte opcode
    public const byte Cgt = 0xFE;  // 0xFE 0x02
    public const byte Cgt_Un = 0xFE;  // 0xFE 0x03
    public const byte Clt = 0xFE;  // 0xFE 0x04
    public const byte Clt_Un = 0xFE;  // 0xFE 0x05

    // Branches
    public const byte Br_S = 0x2B;
    public const byte Brfalse_S = 0x2C;
    public const byte Brtrue_S = 0x2D;
    public const byte Beq_S = 0x2E;
    public const byte Bge_S = 0x2F;
    public const byte Bgt_S = 0x30;
    public const byte Ble_S = 0x31;
    public const byte Blt_S = 0x32;
    public const byte Bne_Un_S = 0x33;
    public const byte Bge_Un_S = 0x34;
    public const byte Bgt_Un_S = 0x35;
    public const byte Ble_Un_S = 0x36;
    public const byte Blt_Un_S = 0x37;

    public const byte Br = 0x38;
    public const byte Brfalse = 0x39;
    public const byte Brtrue = 0x3A;
    public const byte Beq = 0x3B;
    public const byte Bge = 0x3C;
    public const byte Bgt = 0x3D;
    public const byte Ble = 0x3E;
    public const byte Blt = 0x3F;
    public const byte Bne_Un = 0x40;
    public const byte Bge_Un = 0x41;
    public const byte Bgt_Un = 0x42;
    public const byte Ble_Un = 0x43;
    public const byte Blt_Un = 0x44;
    public const byte Switch = 0x45;

    // Exception handling
    public const byte Leave = 0xDD;
    public const byte Leave_S = 0xDE;
    public const byte Throw = 0x7A;
    public const byte Rethrow = 0xFE;  // 0xFE 0x1A two-byte opcode
    public const byte Rethrow_2 = 0x1A;
    public const byte Endfinally = 0xDC;

    // Indirect load/store
    public const byte Ldind_I1 = 0x46;
    public const byte Ldind_U1 = 0x47;
    public const byte Ldind_I2 = 0x48;
    public const byte Ldind_U2 = 0x49;
    public const byte Ldind_I4 = 0x4A;
    public const byte Ldind_U4 = 0x4B;
    public const byte Ldind_I8 = 0x4C;
    public const byte Ldind_I = 0x4D;
    public const byte Ldind_R4 = 0x4E;
    public const byte Ldind_R8 = 0x4F;
    public const byte Ldind_Ref = 0x50;
    public const byte Stind_Ref = 0x51;
    public const byte Stind_I1 = 0x52;
    public const byte Stind_I2 = 0x53;
    public const byte Stind_I4 = 0x54;
    public const byte Stind_I8 = 0x55;
    public const byte Stind_R4 = 0x56;
    public const byte Stind_R8 = 0x57;
    public const byte Stind_I = 0xDF;  // Note: Stind_I is at 0xDF per ECMA

    // String loading
    public const byte Ldstr = 0x72;      // Load string literal from #US heap

    // Value type operations
    public const byte Cpobj = 0x70;
    public const byte Ldobj = 0x71;
    public const byte Stobj = 0x81;

    // Field access
    public const byte Ldfld = 0x7B;      // Load instance field
    public const byte Ldflda = 0x7C;     // Load field address
    public const byte Stfld = 0x7D;      // Store instance field
    public const byte Ldsfld = 0x7E;     // Load static field
    public const byte Ldsflda = 0x7F;    // Load static field address
    public const byte Stsfld = 0x80;     // Store static field

    // Object/array allocation
    public const byte Newobj = 0x73;     // Create new object
    public const byte Newarr = 0x8D;     // Create new array

    // Type operations
    public const byte Castclass = 0x74;  // Cast with exception on failure
    public const byte Isinst = 0x75;     // Cast returning null on failure

    // Boxing/Unboxing
    public const byte Box = 0x8C;        // Box value type to object
    public const byte Unbox = 0x79;      // Unbox object to managed pointer
    public const byte Unbox_Any = 0xA5;  // Unbox object to value

    // Token loading
    public const byte Ldtoken = 0xD0;    // Load metadata token (TypeDef, TypeRef, TypeSpec, MethodDef, MemberRef, FieldDef)

    // Array operations
    public const byte Ldlen = 0x8E;      // Load array length
    public const byte Ldelema = 0x8F;    // Load element address
    public const byte Ldelem_I1 = 0x90;
    public const byte Ldelem_U1 = 0x91;
    public const byte Ldelem_I2 = 0x92;
    public const byte Ldelem_U2 = 0x93;
    public const byte Ldelem_I4 = 0x94;
    public const byte Ldelem_U4 = 0x95;
    public const byte Ldelem_I8 = 0x96;
    public const byte Ldelem_I = 0x97;
    public const byte Ldelem_R4 = 0x98;
    public const byte Ldelem_R8 = 0x99;
    public const byte Ldelem_Ref = 0x9A;
    public const byte Stelem_I = 0x9B;
    public const byte Stelem_I1 = 0x9C;
    public const byte Stelem_I2 = 0x9D;
    public const byte Stelem_I4 = 0x9E;
    public const byte Stelem_I8 = 0x9F;
    public const byte Stelem_R4 = 0xA0;
    public const byte Stelem_R8 = 0xA1;
    public const byte Stelem_Ref = 0xA2;
    public const byte Ldelem = 0xA3;     // Load element (with type token)
    public const byte Stelem = 0xA4;     // Store element (with type token)

    // Method calls
    public const byte Call = 0x28;
    public const byte Calli = 0x29;
    public const byte Callvirt = 0x6F;
    public const byte Ret = 0x2A;

    // Two-byte opcodes (0xFE prefix)
    public const byte Prefix_FE = 0xFE;
    public const byte Ceq_2 = 0x01;
    public const byte Cgt_2 = 0x02;
    public const byte Cgt_Un_2 = 0x03;
    public const byte Clt_2 = 0x04;
    public const byte Clt_Un_2 = 0x05;
    public const byte Ldarg_2byte = 0x09;   // FE09: ldarg <uint16>
    public const byte Ldarga_2byte = 0x0A;  // FE0A: ldarga <uint16>
    public const byte Starg_2byte = 0x0B;   // FE0B: starg <uint16>
    public const byte Ldloc_2byte = 0x0C;   // FE0C: ldloc <uint16>
    public const byte Ldloca_2byte = 0x0D;  // FE0D: ldloca <uint16>
    public const byte Stloc_2byte = 0x0E;   // FE0E: stloc <uint16>
    public const byte Cpblk_2 = 0x17;
    public const byte Initblk_2 = 0x18;
    public const byte Initobj_2 = 0x15;
    public const byte Sizeof_2 = 0x1C;

    // Method pointer loading (two-byte opcodes)
    public const byte Ldftn_2 = 0x06;      // Load function pointer
    public const byte Ldvirtftn_2 = 0x07;  // Load virtual function pointer
    public const byte Localloc_2 = 0x0F;   // Allocate space on stack

    // Conversion with overflow checking (unsigned source) - two-byte opcodes
    public const byte Conv_Ovf_I1_Un_2 = 0x82;
    public const byte Conv_Ovf_U1_Un_2 = 0x83;
    public const byte Conv_Ovf_I2_Un_2 = 0x84;
    public const byte Conv_Ovf_U2_Un_2 = 0x85;
    public const byte Conv_Ovf_I4_Un_2 = 0x86;
    public const byte Conv_Ovf_U4_Un_2 = 0x87;
    public const byte Conv_Ovf_I8_Un_2 = 0x88;
    public const byte Conv_Ovf_U8_Un_2 = 0x89;
    public const byte Conv_Ovf_I_Un_2 = 0x8A;
    public const byte Conv_Ovf_U_Un_2 = 0x8B;

    // Exception handling (two-byte opcodes)
    public const byte Endfilter_2 = 0x11;  // FE11: End exception filter

    // Prefix opcodes (two-byte) - for constrained callvirt
    public const byte Constrained_2 = 0x16;  // FE16: constrained. <type token>
    public const byte Readonly_2 = 0x1E;     // FE1E: readonly. prefix for ldelema
    public const byte Tail_2 = 0x14;         // FE14: tail. prefix for call
    public const byte Volatile_2 = 0x13;     // FE13: volatile. prefix
    public const byte Unaligned_2 = 0x12;    // FE12: unaligned. prefix
    public const byte No_2 = 0x19;           // FE19: no. prefix (typecheck/rangecheck/nullcheck)

    // Rare opcodes (two-byte)
    public const byte Arglist_2 = 0x00;      // FE00: arglist (varargs)
    public const byte Refanytype_2 = 0x1D;   // FE1D: refanytype (typed references)

    // Address loading (short forms)
    public const byte Ldloca_S = 0x12;
    public const byte Ldarga_S = 0x0F;
}

/// <summary>
/// Method resolution result for call compilation.
/// </summary>
public unsafe struct ResolvedMethod
{
    /// <summary>Pointer to native code (null if not yet compiled).</summary>
    public void* NativeCode;

    /// <summary>Number of arguments expected.</summary>
    public byte ArgCount;

    /// <summary>Return type classification.</summary>
    public ReturnKind ReturnKind;

    /// <summary>Size of return struct in bytes (only valid when ReturnKind is Struct).</summary>
    public ushort ReturnStructSize;

    /// <summary>True if instance method (has implicit 'this' parameter).</summary>
    public bool HasThis;

    /// <summary>True if resolution was successful.</summary>
    public bool IsValid;

    /// <summary>True if this is a virtual method requiring vtable dispatch.</summary>
    public bool IsVirtual;

    /// <summary>
    /// Vtable slot index for virtual methods.
    /// Only valid if IsVirtual is true.
    /// -1 means not a virtual method or slot not determined.
    /// </summary>
    public short VtableSlot;

    /// <summary>
    /// MethodTable pointer for the declaring type.
    /// Used by newobj to allocate the correct type before calling the constructor.
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
    /// Pointer to the CompiledMethodInfo registry entry.
    /// Used for indirect calls when NativeCode is not yet known (recursive methods).
    /// </summary>
    public void* RegistryEntry;

    /// <summary>True if this is a delegate Invoke method (runtime-provided).</summary>
    public bool IsDelegateInvoke;

    /// <summary>True if this is a delegate constructor (runtime-provided).</summary>
    public bool IsDelegateCtor;

    /// <summary>True if this is Activator.CreateInstance&lt;T&gt;() (JIT intrinsic).</summary>
    public bool IsActivatorCreateInstance;

    /// <summary>MethodTable pointer for the type argument T in Activator.CreateInstance&lt;T&gt;().</summary>
    public void* ActivatorTypeArgMT;

    /// <summary>True if this is RuntimeHelpers.InitializeArray (JIT intrinsic).</summary>
    public bool IsInitializeArray;

    /// <summary>True if this is an MD array method (Get, Set, Address, .ctor).</summary>
    public bool IsMDArrayMethod;

    /// <summary>Kind of MD array method (only valid if IsMDArrayMethod).</summary>
    public MDArrayMethodKind MDArrayKind;

    /// <summary>Rank of the MD array (only valid if IsMDArrayMethod).</summary>
    public byte MDArrayRank;

    /// <summary>Element size of the MD array (only valid if IsMDArrayMethod).</summary>
    public ushort MDArrayElemSize;

    /// <summary>True if the target method accepts varargs (has VARARG calling convention).</summary>
    public bool IsVarargMethod;

    /// <summary>True if this is a varargs call with varargs at the call site.</summary>
    public bool IsVarargCall;

    /// <summary>Number of varargs passed at this call site.</summary>
    public byte VarargCount;

    /// <summary>Pointer to array of MethodTable pointers for vararg types (or null).</summary>
    public void** VarargMTs;
}

/// <summary>Kind of multi-dimensional array method.</summary>
public enum MDArrayMethodKind : byte
{
    None = 0,
    Ctor = 1,    // .ctor(int, int, ...) - constructor
    Get = 2,     // Get(int, int, ...) -> T - get element
    Set = 3,     // Set(int, int, ..., T) - set element
    Address = 4  // Address(int, int, ...) -> T& - get element address
}

/// <summary>
/// Delegate type for resolving method tokens to native addresses.
/// </summary>
/// <param name="token">Method token from IL</param>
/// <param name="result">Output: resolved method information</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool MethodResolver(uint token, out ResolvedMethod result);

/// <summary>
/// Delegate for resolving user string tokens (ldstr).
/// </summary>
/// <param name="token">User string token (0x70xxxxxx where xxxxxx is #US heap offset)</param>
/// <param name="stringPtr">Output: pointer to managed String object</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool StringResolver(uint token, out void* stringPtr);

/// <summary>
/// Delegate for resolving type tokens (ldtoken, newarr, etc.).
/// </summary>
/// <param name="token">Type token (TypeDef 0x02, TypeRef 0x01, TypeSpec 0x1B)</param>
/// <param name="methodTablePtr">Output: pointer to MethodTable</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool TypeResolver(uint token, out void* methodTablePtr);

/// <summary>
/// Result of resolving a field token.
/// Contains all information needed to generate field access code.
/// </summary>
public unsafe struct ResolvedField
{
    /// <summary>Byte offset of field within object instance (after MethodTable*).</summary>
    public int Offset;

    /// <summary>Size of field in bytes (1, 2, 4, or 8).</summary>
    public byte Size;

    /// <summary>True if field should be sign-extended when loaded.</summary>
    public bool IsSigned;

    /// <summary>True if this is a static field.</summary>
    public bool IsStatic;

    /// <summary>
    /// For static fields: the address of the static storage.
    /// For instance fields: unused (null).
    /// </summary>
    public void* StaticAddress;

    /// <summary>True if this is a GC reference type (object, string, array).</summary>
    public bool IsGCRef;

    /// <summary>True if resolution was successful.</summary>
    public bool IsValid;

    /// <summary>True if the declaring type is a value type (struct/enum).</summary>
    public bool IsDeclaringTypeValueType;

    /// <summary>Size of the declaring type in bytes (for value types).</summary>
    public int DeclaringTypeSize;

    /// <summary>True if the field type itself is a value type (for propagating through ldfld chains).</summary>
    public bool IsFieldTypeValueType;

    /// <summary>Declaring type token (TypeDef 0x02) for the field.</summary>
    public uint DeclaringTypeToken;

    /// <summary>Assembly ID containing the declaring type.</summary>
    public uint DeclaringTypeAssemblyId;
}

/// <summary>
/// Delegate for resolving field tokens (ldfld, stfld, ldsfld, stsfld).
/// </summary>
/// <param name="token">Field token (FieldDef 0x04, MemberRef 0x0A)</param>
/// <param name="result">Output: resolved field information</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool FieldResolver(uint token, out ResolvedField result);

/// <summary>
/// Stack entry kinds for the JIT eval stack.
/// These represent the CLI type system categories plus additional distinctions
/// useful for correct code generation.
/// </summary>
public enum EvalStackKind : byte
{
    /// <summary>Unknown or uninitialized entry.</summary>
    Unknown = 0,

    /// <summary>32-bit integer (int32, bool, char, etc). Always 8 bytes on stack (zero-extended).</summary>
    Int32 = 1,

    /// <summary>64-bit integer (int64, native int). 8 bytes on stack.</summary>
    Int64 = 2,

    /// <summary>Native-sized integer (IntPtr, UIntPtr). 8 bytes on x64.</summary>
    NativeInt = 3,

    /// <summary>32-bit float. 8 bytes on stack (stored in low 32 bits or XMM).</summary>
    Float32 = 4,

    /// <summary>64-bit float. 8 bytes on stack.</summary>
    Float64 = 5,

    /// <summary>Object reference (class instance, array, string). 8 bytes on x64. GC-tracked.</summary>
    ObjectRef = 6,

    /// <summary>Managed pointer (ref, out parameters, ldloca, ldarga). 8 bytes. Points to GC-tracked memory.</summary>
    ManagedPtr = 7,

    /// <summary>Unmanaged pointer (void*, int*, etc). 8 bytes. Not GC-tracked.</summary>
    UnmanagedPtr = 8,

    /// <summary>
    /// Value type stored inline on the eval stack. Size varies.
    /// The ByteSize field contains the actual size of the struct.
    /// </summary>
    ValueType = 9,
}

/// <summary>
/// Represents a single entry on the JIT evaluation stack.
/// Tracks both the semantic type and the physical byte size on the stack.
/// </summary>
public struct EvalStackEntry
{
    /// <summary>The kind of value stored in this entry.</summary>
    public EvalStackKind Kind;

    /// <summary>
    /// The size in bytes this entry occupies on the physical RSP-based stack.
    /// For most types this is 8. For ValueType, this is the actual struct size (8-byte aligned).
    /// </summary>
    public ushort ByteSize;

    /// <summary>
    /// The actual semantic size of the value (before alignment).
    /// Int32 = 4, Int64 = 8, Float32 = 4, etc.
    /// For ValueType: the original struct size.
    /// </summary>
    public ushort RawSize;

    /// <summary>
    /// Create an entry with explicit sizes.
    /// </summary>
    public static EvalStackEntry Create(EvalStackKind kind, int rawSize, int alignedSize)
    {
        EvalStackEntry e;
        e.Kind = kind;
        e.RawSize = (ushort)rawSize;
        e.ByteSize = (ushort)alignedSize;
        return e;
    }

    /// <summary>
    /// Create an entry for a value type with the given size.
    /// ByteSize is aligned to 8 bytes for stack alignment.
    /// </summary>
    public static EvalStackEntry Struct(int rawSize)
    {
        EvalStackEntry e;
        e.Kind = EvalStackKind.ValueType;
        e.RawSize = (ushort)rawSize;
        e.ByteSize = (ushort)((rawSize + 7) & ~7);  // 8-byte aligned
        return e;
    }

    /// <summary>Create an entry for a 32-bit integer. Raw size 4, stack size 8.</summary>
    public static EvalStackEntry Int32 => Create(EvalStackKind.Int32, 4, 8);

    /// <summary>Create an entry for a 64-bit integer. Raw size 8, stack size 8.</summary>
    public static EvalStackEntry Int64 => Create(EvalStackKind.Int64, 8, 8);

    /// <summary>Create an entry for a native integer (IntPtr). Raw and stack size 8 on x64.</summary>
    public static EvalStackEntry NativeInt => Create(EvalStackKind.NativeInt, 8, 8);

    /// <summary>Create an entry for a 32-bit float. Raw size 4, stack size 8.</summary>
    public static EvalStackEntry Float32 => Create(EvalStackKind.Float32, 4, 8);

    /// <summary>Create an entry for a 64-bit float. Raw size 8, stack size 8.</summary>
    public static EvalStackEntry Float64 => Create(EvalStackKind.Float64, 8, 8);

    /// <summary>Create an entry for an object reference. Raw and stack size 8.</summary>
    public static EvalStackEntry ObjRef => Create(EvalStackKind.ObjectRef, 8, 8);

    /// <summary>Create an entry for a managed pointer (ref/out/ldloca). Raw and stack size 8.</summary>
    public static EvalStackEntry ByRef => Create(EvalStackKind.ManagedPtr, 8, 8);

    /// <summary>Create an entry for an unmanaged pointer. Raw and stack size 8.</summary>
    public static EvalStackEntry Ptr => Create(EvalStackKind.UnmanagedPtr, 8, 8);

    /// <summary>Returns true if this is a GC-trackable reference (object or managed pointer).</summary>
    public bool IsGCRef => Kind == EvalStackKind.ObjectRef || Kind == EvalStackKind.ManagedPtr;

    /// <summary>Returns true if this is a floating-point type.</summary>
    public bool IsFloat => Kind == EvalStackKind.Float32 || Kind == EvalStackKind.Float64;

    /// <summary>Returns true if this is a pointer type (managed or unmanaged).</summary>
    public bool IsPointer => Kind == EvalStackKind.ManagedPtr || Kind == EvalStackKind.UnmanagedPtr;

    /// <summary>Returns true if this is a value type taking multiple slots (>8 bytes).</summary>
    public bool IsMultiSlotValueType => Kind == EvalStackKind.ValueType && ByteSize > 8;

    /// <summary>Returns true if this is an integer type (32-bit, 64-bit, or native).</summary>
    public bool IsInteger => Kind == EvalStackKind.Int32 || Kind == EvalStackKind.Int64 || Kind == EvalStackKind.NativeInt;

    /// <summary>Returns true if this is any reference/pointer type (object, managed ptr, or unmanaged ptr).</summary>
    public bool IsRefOrPtr => Kind == EvalStackKind.ObjectRef || Kind == EvalStackKind.ManagedPtr || Kind == EvalStackKind.UnmanagedPtr;
}

/// <summary>
/// Naive IL to x64 compiler.
/// Uses a stack-based approach where the IL evaluation stack is
/// simulated using x64 registers and memory.
/// </summary>
public unsafe struct ILCompiler
{
    private CodeBuffer _code;
    private byte* _il;
    private int _ilLength;
    private int _ilOffset;

    // Prefix opcodes state - cleared after each instruction
    private uint _constrainedTypeToken;  // Non-zero if constrained. prefix is pending
    private bool _tailPrefix;            // True if tail. prefix is pending

    // Method info
    private int _argCount;
    private int _localCount;
    private int _stackAdjust;
    private int _structReturnTempOffset;  // Offset from RBP for 16-byte struct return temp

    // GC tracking: bitmask for which locals/args are GC references
    // Bit i set = local i is GC reference (for i < localCount)
    // Bit (localCount + i) set = arg i is GC reference
    private ulong _gcRefMask;

    // GC info builder for stack root enumeration
    private JITGCInfo _gcInfo;

    // Evaluation stack tracking with rich type information
    // Each entry tracks both the semantic type and physical byte size on stack
    private const int MaxStackDepth = 32;
    private int _evalStackDepth;                  // Number of entries (not bytes!)
    private EvalStackEntry* _evalStack;           // heap-allocated [MaxStackDepth] - rich type info per entry
    private int _evalStackByteSize;               // Total bytes currently on physical RSP stack

    // Arrays for branch targets (heap-allocated to reduce stack usage during nested JIT)
    private const int MaxBranches = 64;
    private int* _branchSources;    // IL offset where branch was emitted [MaxBranches]
    private int* _branchTargetIL;   // IL offset branch targets [MaxBranches]
    private int* _branchPatchOffset; // Code offset to patch [MaxBranches]
    private byte* _branchTargetStackDepth; // Expected stack depth at target [MaxBranches]
    private int _branchCount;

    // Label mapping: IL offset -> code offset (heap-allocated)
    private const int MaxLabels = 512;
    private int* _labelILOffset;    // [MaxLabels]
    private int* _labelCodeOffset;  // [MaxLabels]
    private int _labelCount;

    // Heap buffer for all compiler arrays (freed after compilation)
    private byte* _heapBuffers;

    // Local and argument type tracking for value type handling in ldfld
    private const int MaxLocals = 64;
    private const int MaxArgs = 32;
    private bool* _localIsValueType;  // heap-allocated [MaxLocals] - true if local is a value type
    private bool* _argIsValueType;    // heap-allocated [MaxArgs] - true if arg is a value type
    private ushort* _localTypeSize;   // heap-allocated [MaxLocals] - size of local's type (for value types)
    private ushort* _argTypeSize;     // heap-allocated [MaxArgs] - size of arg's type (for value types)
    private byte* _argFloatKind;      // heap-allocated [MaxArgs] - 0=not float, 4=float32, 8=float64

    // Return type tracking for struct return handling
    private bool _returnIsValueType;  // true if return type is a value type
    private ushort _returnTypeSize;   // size of return type (for value types)

    // Method resolution callback (optional - if null, uses CompiledMethodRegistry)
    private MethodResolver _resolver;

    // String resolution callback for ldstr (optional)
    private StringResolver _stringResolver;

    // Type resolution callback for ldtoken/newarr (optional)
    private TypeResolver _typeResolver;

    // Field resolution callback for ldfld/stfld/ldsfld/stsfld (optional)
    private FieldResolver _fieldResolver;

    /// <summary>
    /// Create an IL compiler for a method.
    /// </summary>
    public static ILCompiler Create(byte* il, int ilLength, int argCount, int localCount)
    {
        return CreateWithGCInfo(il, ilLength, argCount, localCount, 0);
    }

    // Buffer size constants for heap allocation
    // EvalStackEntry is 5 bytes (Kind:1 + ByteSize:2 + RawSize:2) but we use 8 for alignment
    private const int EvalStackEntrySize = 8;  // sizeof(EvalStackEntry) with padding
    // Layout: evalStack[256] + branchSources[256] + branchTargetIL[256] + branchPatchOffset[256]
    //       + branchTargetStackDepth[64] + labelILOffset[2048] + labelCodeOffset[2048] + ehClauseData[384] + funcletInfo[512]
    //       + localIsValueType[64] + argIsValueType[32] + localTypeSize[128] + argTypeSize[64] + finallyCallPatches[128]
    //       + argFloatKind[32]
    // Note: evalStack = MaxStackDepth(32) * 8 bytes = 256 bytes for rich type tracking
    // funcletInfo = 32 funclets * 4 ints * 4 bytes = 512 bytes (doubled for filter clause support)
    // Finally call tracking: each entry is 2 ints (patchOffset, ehClauseIndex)
    private const int MaxFinallyCalls = 16;
    private const int HeapBufferSize = (MaxStackDepth * EvalStackEntrySize) + 256 + 256 + 256 + 64 + 2048 + 2048 + 384 + 512 + 64 + 32 + 128 + 64 + (MaxFinallyCalls * 8) + 32; // 6528 bytes

    /// <summary>
    /// Create an IL compiler with GC reference tracking.
    /// </summary>
    /// <param name="gcRefMask">Bitmask: bit i = local i is GC ref, bit (localCount+i) = arg i is GC ref</param>
    public static ILCompiler CreateWithGCInfo(byte* il, int ilLength, int argCount, int localCount, ulong gcRefMask)
    {
        ILCompiler compiler;
        compiler._il = il;
        compiler._ilLength = ilLength;
        compiler._ilOffset = 0;
        compiler._constrainedTypeToken = 0;  // Initialize prefix state
        compiler._tailPrefix = false;         // Initialize prefix state
        compiler._argCount = argCount;
        compiler._localCount = localCount;
        compiler._stackAdjust = 0;
        compiler._structReturnTempOffset = 0;
        compiler._returnIsValueType = false;
        compiler._returnTypeSize = 0;
        compiler._gcRefMask = gcRefMask;
        compiler._gcInfo = default;
        compiler._evalStackDepth = 0;
        compiler._branchCount = 0;
        compiler._labelCount = 0;
        compiler._resolver = null;
        compiler._stringResolver = null;
        compiler._typeResolver = null;
        compiler._fieldResolver = null;
        // Use default allocation helpers from RuntimeHelpers
        compiler._rhpNewFast = RuntimeHelpers.GetRhpNewFastPtr();
        compiler._rhpNewArray = RuntimeHelpers.GetRhpNewArrayPtr();
        // Use default type helpers from RuntimeHelpers
        compiler._isAssignableTo = RuntimeHelpers.GetIsAssignableToPtr();
        compiler._getInterfaceMethod = RuntimeHelpers.GetInterfaceMethodPtr();
        // Debug helpers - DISABLED due to function pointer bug (addresses off by 16 bytes)
        // The function pointers obtained via &Method point to padding before the actual code
        compiler._debugStfld = null;  // RuntimeHelpers.GetDebugStfldPtr();
        compiler._debugLdfld = null;  // RuntimeHelpers.GetDebugLdfldPtr();
        compiler._debugLdfldInt = null;  // RuntimeHelpers.GetDebugLdfldIntPtr();
        compiler._debugStelemStack = null;  // RuntimeHelpers.GetDebugStelemStackPtr();
        compiler._debugVtableDispatch = null;  // RuntimeHelpers.GetDebugVtableDispatchPtr();
        compiler._debugAssemblyId = 0;
        compiler._debugMethodToken = 0;
        // JIT stub helpers
        compiler._ensureVtableSlotCompiled = (void*)JitStubs.EnsureVtableSlotCompiledAddress;

        // Initialize funclet support
        compiler._ehClauseCount = 0;
        compiler._funcletCount = 0;
        compiler._compilingFunclet = false;
        compiler._funcletCatchHandlerEntry = false;

        // Initialize all pointer fields to null first (required for struct initialization)
        compiler._heapBuffers = null;
        compiler._evalStack = null;
        compiler._evalStackByteSize = 0;
        compiler._branchSources = null;
        compiler._branchTargetIL = null;
        compiler._branchPatchOffset = null;
        compiler._branchTargetStackDepth = null;
        compiler._labelILOffset = null;
        compiler._labelCodeOffset = null;
        compiler._ehClauseData = null;
        compiler._funcletInfo = null;
        compiler._localIsValueType = null;
        compiler._argIsValueType = null;
        compiler._localTypeSize = null;
        compiler._argTypeSize = null;
        compiler._argFloatKind = null;
        compiler._finallyCallPatches = null;
        compiler._finallyCallCount = 0;

        // Allocate heap buffer for all arrays (reduces stack usage during nested JIT)
        compiler._heapBuffers = (byte*)HeapAllocator.AllocZeroed(HeapBufferSize);
        if (compiler._heapBuffers != null)
        {
            // Set up pointers into the heap buffer
            // Layout with evalStack at offset 0
            byte* p = compiler._heapBuffers;
            compiler._evalStack = (EvalStackEntry*)p;          // offset 0, 256 bytes (32 * 8)
            compiler._branchSources = (int*)(p + 256);         // offset 256, 256 bytes
            compiler._branchTargetIL = (int*)(p + 512);        // offset 512, 256 bytes
            compiler._branchPatchOffset = (int*)(p + 768);     // offset 768, 256 bytes
            compiler._branchTargetStackDepth = p + 1024;       // offset 1024, 64 bytes
            compiler._labelILOffset = (int*)(p + 1088);        // offset 1088, 2048 bytes
            compiler._labelCodeOffset = (int*)(p + 3136);      // offset 3136, 2048 bytes
            compiler._ehClauseData = (int*)(p + 5184);         // offset 5184, 384 bytes (16 * 6 * 4)
            compiler._funcletInfo = (int*)(p + 5568);          // offset 5568, 512 bytes (32 funclets * 4 ints * 4 bytes)
            compiler._localIsValueType = (bool*)(p + 6080);    // offset 6080, 64 bytes
            compiler._argIsValueType = (bool*)(p + 6144);      // offset 6144, 32 bytes
            compiler._localTypeSize = (ushort*)(p + 6176);     // offset 6176, 128 bytes
            compiler._argTypeSize = (ushort*)(p + 6304);       // offset 6304, 64 bytes (MaxArgs * 2)
            compiler._finallyCallPatches = (int*)(p + 6368);   // offset 6368, 128 bytes (MaxFinallyCalls * 8)
            compiler._argFloatKind = (byte*)(p + 6496);        // offset 6496, 32 bytes (MaxArgs * 1)
        }

        // Create code buffer sized based on IL length
        // The naive stack-based JIT generates ~8-16 bytes of x64 per IL byte
        // (push/pop pairs, 10-byte movabs for addresses, etc.)
        // Use 16x multiplier + 512 bytes overhead for prologue/epilogue
        int estimatedCodeSize = (ilLength * 16) + 512;
        // Minimum 4KB, round up to 4KB boundary for efficient allocation
        if (estimatedCodeSize < 4096)
            estimatedCodeSize = 4096;
        else
            estimatedCodeSize = (estimatedCodeSize + 4095) & ~4095;
        compiler._code = CodeBuffer.Create(estimatedCodeSize);

        return compiler;
    }

    /// <summary>
    /// Free heap buffers allocated during creation.
    /// Must be called after compilation completes.
    /// </summary>
    public void FreeBuffers()
    {
        if (_heapBuffers != null)
        {
            HeapAllocator.Free(_heapBuffers);
            _heapBuffers = null;
        }
    }

    /// <summary>
    /// Create an IL compiler with a custom method resolver.
    /// </summary>
    public static ILCompiler CreateWithResolver(byte* il, int ilLength, int argCount, int localCount, MethodResolver resolver)
    {
        var compiler = Create(il, ilLength, argCount, localCount);
        compiler._resolver = resolver;
        return compiler;
    }

    /// <summary>
    /// Set the string resolver for ldstr instructions.
    /// </summary>
    public void SetStringResolver(StringResolver resolver)
    {
        _stringResolver = resolver;
    }

    /// <summary>
    /// Set the type resolver for ldtoken and newarr instructions.
    /// </summary>
    public void SetTypeResolver(TypeResolver resolver)
    {
        _typeResolver = resolver;
    }

    /// <summary>
    /// Set the field resolver for ldfld/stfld/ldsfld/stsfld instructions.
    /// </summary>
    public void SetFieldResolver(FieldResolver resolver)
    {
        _fieldResolver = resolver;
    }

    /// <summary>
    /// Set the method resolver for call instructions.
    /// This enables lazy JIT compilation of called methods.
    /// </summary>
    public void SetMethodResolver(MethodResolver resolver)
    {
        _resolver = resolver;
    }

    /// <summary>
    /// Set local variable type information for value type handling in ldfld.
    /// </summary>
    /// <param name="localTypes">Array of booleans, true if local is a value type</param>
    /// <param name="count">Number of locals</param>
    public void SetLocalTypes(bool* localTypes, int count)
    {
        // Copy the value type flags for each local
        // Use the minimum of count and _localCount (already set during Create)
        int copyCount = count < _localCount ? count : _localCount;
        if (copyCount > MaxLocals)
            copyCount = MaxLocals;

        if (_localIsValueType != null && localTypes != null)
        {
            for (int i = 0; i < copyCount; i++)
            {
                _localIsValueType[i] = localTypes[i];
            }
        }
    }

    /// <summary>
    /// Set local variable type sizes for large value type handling in ldloc.
    /// </summary>
    /// <param name="localSizes">Array of sizes for each local (for value types)</param>
    /// <param name="count">Number of locals</param>
    public void SetLocalTypeSizes(ushort* localSizes, int count)
    {
        // Copy the type sizes for each local
        int copyCount = count < _localCount ? count : _localCount;
        if (copyCount > MaxLocals)
            copyCount = MaxLocals;

        if (_localTypeSize != null && localSizes != null)
        {
            for (int i = 0; i < copyCount; i++)
            {
                _localTypeSize[i] = localSizes[i];
            }
        }
    }

    /// <summary>
    /// Set which arguments are value types (affects ldfld behavior on args).
    /// </summary>
    /// <param name="argTypes">Array of bools, true if arg at that index is a value type</param>
    /// <param name="count">Number of args</param>
    public void SetArgTypes(bool* argTypes, int count)
    {
        // Copy the value type flags for each arg
        int copyCount = count < _argCount ? count : _argCount;
        if (copyCount > MaxArgs)
            copyCount = MaxArgs;

        if (_argIsValueType != null && argTypes != null)
        {
            for (int i = 0; i < copyCount; i++)
            {
                _argIsValueType[i] = argTypes[i];
            }
        }
    }

    /// <summary>
    /// Set argument type sizes for value type handling in ldarg/ldfld.
    /// </summary>
    /// <param name="argSizes">Array of sizes for each arg (0 for non-value types)</param>
    /// <param name="count">Number of args</param>
    public void SetArgTypeSizes(ushort* argSizes, int count)
    {
        // Copy the type sizes for each arg
        int copyCount = count < _argCount ? count : _argCount;
        if (copyCount > MaxArgs)
            copyCount = MaxArgs;

        if (_argTypeSize != null && argSizes != null)
        {
            for (int i = 0; i < copyCount; i++)
            {
                _argTypeSize[i] = argSizes[i];
            }
        }
    }

    /// <summary>
    /// Set float kind for each argument (0=not float, 4=float32, 8=float64).
    /// </summary>
    /// <param name="floatKinds">Array of float kinds for each arg</param>
    /// <param name="count">Number of args</param>
    public void SetArgFloatKinds(byte* floatKinds, int count)
    {
        int copyCount = count < _argCount ? count : _argCount;
        if (copyCount > MaxArgs)
            copyCount = MaxArgs;

        if (_argFloatKind != null && floatKinds != null)
        {
            for (int i = 0; i < copyCount; i++)
            {
                _argFloatKind[i] = floatKinds[i];
            }
        }
    }

    /// <summary>
    /// Set return type info for struct return handling.
    /// </summary>
    /// <param name="isValueType">true if return type is a value type</param>
    /// <param name="size">size of the return type (for value types)</param>
    public void SetReturnType(bool isValueType, ushort size)
    {
        _returnIsValueType = isValueType;
        _returnTypeSize = size;
    }

    /// <summary>
    /// Set debug context for targeted logging during compilation.
    /// </summary>
    public void SetDebugContext(uint assemblyId, uint methodToken)
    {
        _debugAssemblyId = assemblyId;
        _debugMethodToken = methodToken;
    }

    /// <summary>
    /// Get the code buffer.
    /// </summary>
    public ref CodeBuffer Code => ref _code;

    /// <summary>
    /// Get the number of IL->native offset mappings recorded.
    /// </summary>
    public int LabelCount => _labelCount;

    /// <summary>
    /// Convert IL exception clauses to native exception clauses using recorded offset mappings.
    /// Must be called after Compile() succeeds.
    /// </summary>
    /// <param name="ilClauses">IL exception clauses parsed from method body</param>
    /// <param name="nativeClauses">Output: converted native clauses</param>
    /// <returns>True if all clauses converted successfully</returns>
    public bool ConvertEHClauses(ref ILExceptionClauses ilClauses, out JITExceptionClauses nativeClauses)
    {
        return EHClauseConverter.ConvertClauses(
            ref ilClauses,
            out nativeClauses,
            _labelILOffset,
            _labelCodeOffset,
            _labelCount,
            MetadataIntegration.GetCurrentAssemblyId());
    }

    /// <summary>
    /// Find the native code offset for a given IL offset.
    /// Returns -1 if not found.
    /// </summary>
    public int GetNativeOffset(int ilOffset)
    {
        for (int i = 0; i < _labelCount; i++)
        {
            if (_labelILOffset[i] == ilOffset)
                return _labelCodeOffset[i];
        }
        return -1;
    }

    /// <summary>
    /// Record a GC safe point at the current code position.
    /// Safe points are where GC can safely scan stack roots.
    /// Called after emitting CALL instructions.
    /// </summary>
    private void RecordSafePoint()
    {
        if (_gcRefMask != 0)
        {
            _gcInfo.AddSafePoint((uint)_code.Position);
        }
    }

    /// <summary>
    /// Initialize GC slots based on gcRefMask.
    /// Must be called after prologue is emitted.
    /// </summary>
    private void InitializeGCSlots()
    {
        if (_gcRefMask == 0)
            return;

        // Register locals that are GC references
        for (int i = 0; i < _localCount && i < 64; i++)
        {
            if ((_gcRefMask & (1UL << i)) != 0)
            {
                // Local i is a GC reference at [rbp - (i+1)*8]
                int offset = X64Emitter.GetLocalOffset(i);
                _gcInfo.AddStackSlot(offset);
            }
        }

        // Register args that are GC references (bit localCount+i = arg i)
        for (int i = 0; i < _argCount && (_localCount + i) < 64; i++)
        {
            if ((_gcRefMask & (1UL << (_localCount + i))) != 0)
            {
                // Arg i is a GC reference - homed to shadow space at [rbp + 16 + i*8]
                int offset = 16 + i * 8;
                _gcInfo.AddStackSlot(offset);
            }
        }
    }

    /// <summary>
    /// Finalize the GCInfo and build the encoded data.
    /// Must be called after Compile() succeeds.
    /// </summary>
    /// <param name="buffer">Buffer to write GCInfo to.</param>
    /// <param name="outSize">Output: size of generated GCInfo in bytes.</param>
    /// <returns>True if successful, false if no GC refs or error.</returns>
    public bool FinalizeGCInfo(byte* buffer, out int outSize)
    {
        outSize = 0;
        if (_gcRefMask == 0)
            return false;

        // Initialize with final code length
        _gcInfo.Init((uint)_code.Position, true);

        // Re-add the GC slots (Init clears them)
        InitializeGCSlots();

        return _gcInfo.BuildGCInfo(buffer, out outSize);
    }

    /// <summary>
    /// Get the maximum size needed for GCInfo buffer.
    /// </summary>
    public int MaxGCInfoSize => _gcInfo.MaxGCInfoSize();

    /// <summary>
    /// Check if this method has any GC reference slots.
    /// </summary>
    public bool HasGCRefs => _gcRefMask != 0;

    /// <summary>
    /// Get the number of safe points recorded.
    /// </summary>
    public int SafePointCount => _gcInfo.NumSafePoints;

    /// <summary>
    /// Get the total code size in bytes (after Compile()).
    /// </summary>
    public uint CodeSize => (uint)_code.Position;

    /// <summary>
    /// Get the prologue size in bytes.
    /// The prologue is the code before IL offset 0 starts executing.
    /// </summary>
    public byte PrologSize => _labelCount > 0 ? (byte)_labelCodeOffset[0] : (byte)0;

    /// <summary>
    /// Get the stack space allocated in the prologue (for unwind codes).
    /// </summary>
    public int StackAdjust => _stackAdjust;

    /// <summary>
    /// Compile the IL method to native code.
    /// Returns function pointer or null on failure.
    /// </summary>
    public void* Compile()
    {
        // Emit prologue
        // Calculate local space: localCount * 64 + evalStack space + struct return temp
        // Use 64 bytes per local to support value type locals up to 64 bytes
        // Note: Local sizes are parsed and stored in _localTypeSize but we use fixed
        // 64-byte slots for simplicity. Variable-sized slots would save stack space
        // but require refactoring GetLocalOffset and all call sites.
        int localBytes = _localCount * 64 + 64;  // 64 bytes per local + eval stack
        int structReturnTempBytes = 32;           // 32 bytes for struct return temps
        _structReturnTempOffset = -(localBytes + 16);  // First 16 bytes of the temp area
        _stackAdjust = X64Emitter.EmitPrologue(ref _code, localBytes + structReturnTempBytes);

        // Home arguments to shadow space (so we can load them later)
        // When returning a large struct (>16 bytes), the caller passes a hidden buffer
        // pointer in RCX, shifting all IL arguments by 1 in the physical registers.
        // We need to home _argCount + 1 physical arguments to include the hidden buffer.
        int physicalArgCount = _argCount;
        if (_returnIsValueType && _returnTypeSize > 16)
            physicalArgCount = _argCount + 1;
        if (physicalArgCount > 0)
            X64Emitter.HomeArguments(ref _code, physicalArgCount);

        // Process IL
        while (_ilOffset < _ilLength)
        {
            // Record label for this IL offset
            RecordLabel(_ilOffset, _code.Position);

            // Check if this is a branch target - if so, restore the expected stack depth
            // This handles the case where a conditional branch skips over a pop/br.s sequence
            // and the linear fall-through leaves us with a different stack depth
            int branchDepth = FindBranchTargetDepth(_ilOffset);
            if (branchDepth >= 0)
            {
                _evalStackDepth = branchDepth;
            }

            byte opcode = _il[_ilOffset++];

            // if (IsDebugBoolBugMethod())
            // {
            //     // DebugConsole.Write("[BindDbg] IL op il=0x");
            //     DebugConsole.WriteHex((ulong)(_ilOffset - 1));
            //     DebugConsole.Write(" opcode=0x");
            //     DebugConsole.WriteHex(opcode);
            //     DebugConsole.Write(" depth=");
            //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
            //     DebugConsole.WriteLine();
            // }

            if (!CompileOpcode(opcode))
            {
                DebugConsole.Write("[JIT] Unknown opcode 0x");
                DebugConsole.WriteHex(opcode);
                DebugConsole.Write(" at IL offset ");
                DebugConsole.WriteDecimal((uint)(_ilOffset - 1));
                DebugConsole.WriteLine();
                return null;
            }
        }

        // Patch forward branches
        PatchBranches();

        // Check for code buffer overflow
        if (_code.HasOverflow)
        {
            DebugConsole.Write("[JIT] FATAL: Code buffer overflow! IL size=");
            DebugConsole.WriteDecimal((uint)_ilLength);
            DebugConsole.Write(" Buffer capacity=");
            DebugConsole.WriteDecimal((uint)_code.Capacity);
            DebugConsole.Write(" Used=");
            DebugConsole.WriteDecimal((uint)_code.Position);
            DebugConsole.WriteLine();
            return null;
        }

        return _code.GetFunctionPointer();
    }

    private void RecordLabel(int ilOffset, int codeOffset)
    {
        if (_labelCount < MaxLabels)
        {
            _labelILOffset[_labelCount] = ilOffset;
            _labelCodeOffset[_labelCount] = codeOffset;
            _labelCount++;
        }
    }

    private int FindCodeOffset(int ilOffset)
    {
        for (int i = 0; i < _labelCount; i++)
        {
            if (_labelILOffset[i] == ilOffset)
                return _labelCodeOffset[i];
        }
        return -1;  // Not found (forward reference)
    }

    private void RecordBranch(int ilOffset, int targetIL, int patchOffset)
    {
        if (_branchCount < MaxBranches)
        {
            _branchSources[_branchCount] = ilOffset;
            _branchTargetIL[_branchCount] = targetIL;
            _branchPatchOffset[_branchCount] = patchOffset;
            // Record expected stack depth at the branch target
            _branchTargetStackDepth[_branchCount] = (byte)_evalStackDepth;
            _branchCount++;
        }
    }

    /// <summary>
    /// Find the expected stack depth at a branch target.
    /// Returns -1 if this IL offset is not a branch target.
    /// </summary>
    private int FindBranchTargetDepth(int ilOffset)
    {
        for (int i = 0; i < _branchCount; i++)
        {
            if (_branchTargetIL[i] == ilOffset)
                return _branchTargetStackDepth[i];
        }
        return -1;
    }

    private void PatchBranches()
    {
        // bool debug = IsDebugBoolBugMethod() || IsDebugBarMethod();
        // if (debug)
        // {
        //     // DebugConsole.Write("[BindDbg] patch branches count=");
        //     DebugConsole.WriteDecimal((uint)_branchCount);
        //     DebugConsole.WriteLine();
        // }
        for (int i = 0; i < _branchCount; i++)
        {
            int targetIL = _branchTargetIL[i];
            int patchOffset = _branchPatchOffset[i];
            int codeOffset = FindCodeOffset(targetIL);

            // if (debug)
            // {
            //     // DebugConsole.Write("[BindDbg] patch idx=");
            //     DebugConsole.WriteDecimal((uint)i);
            //     DebugConsole.Write(" tgtIL=0x");
            //     DebugConsole.WriteHex((ulong)targetIL);
            //     DebugConsole.Write(" pOff=0x");
            //     DebugConsole.WriteHex((ulong)patchOffset);
            //     DebugConsole.Write(" cOff=");
            //     DebugConsole.WriteDecimal((uint)codeOffset);
            // }

            if (codeOffset >= 0)
            {
                // Calculate relative offset: target - (patch + 4)
                int rel = codeOffset - (patchOffset + 4);
                _code.PatchInt32(patchOffset, rel);
                // if (debug)
                // {
                //     DebugConsole.Write(" rel=0x");
                //     DebugConsole.WriteHex((ulong)(uint)rel);
                //     DebugConsole.WriteLine();
                // }
            }
            // else if (debug)
            // {
            //     DebugConsole.WriteLine(" UNRESOLVED");
            // }
        }
    }

    /// <summary>
    /// Compile a single IL opcode
    /// </summary>
    private bool CompileOpcode(byte opcode)
    {
        switch (opcode)
        {
            case ILOpcode.Nop:
                return true;

            case ILOpcode.Break:
                X64Emitter.Int3(ref _code);  // Emit x86 INT 3 breakpoint instruction
                return true;

            // === Constants ===
            case ILOpcode.Ldc_I4_M1: return CompileLdcI4(-1);
            case ILOpcode.Ldc_I4_0: return CompileLdcI4(0);
            case ILOpcode.Ldc_I4_1: return CompileLdcI4(1);
            case ILOpcode.Ldc_I4_2: return CompileLdcI4(2);
            case ILOpcode.Ldc_I4_3: return CompileLdcI4(3);
            case ILOpcode.Ldc_I4_4: return CompileLdcI4(4);
            case ILOpcode.Ldc_I4_5: return CompileLdcI4(5);
            case ILOpcode.Ldc_I4_6: return CompileLdcI4(6);
            case ILOpcode.Ldc_I4_7: return CompileLdcI4(7);
            case ILOpcode.Ldc_I4_8: return CompileLdcI4(8);

            case ILOpcode.Ldc_I4_S:
                {
                    sbyte val = (sbyte)_il[_ilOffset++];
                    return CompileLdcI4(val);
                }

            case ILOpcode.Ldc_I4:
                {
                    int val = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdcI4(val);
                }

            case ILOpcode.Ldc_I8:
                {
                    long val = *(long*)(_il + _ilOffset);
                    _ilOffset += 8;
                    return CompileLdcI8(val);
                }

            case ILOpcode.Ldnull:
                return CompileLdnull();

            case ILOpcode.Ldc_R4:
                {
                    uint val = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdcR4(val);
                }

            case ILOpcode.Ldc_R8:
                {
                    ulong val = *(ulong*)(_il + _ilOffset);
                    _ilOffset += 8;
                    return CompileLdcR8(val);
                }

            // === Arguments ===
            case ILOpcode.Ldarg_0: return CompileLdarg(0);
            case ILOpcode.Ldarg_1: return CompileLdarg(1);
            case ILOpcode.Ldarg_2: return CompileLdarg(2);
            case ILOpcode.Ldarg_3: return CompileLdarg(3);

            case ILOpcode.Ldarg_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileLdarg(idx);
                }

            case ILOpcode.Starg_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileStarg(idx);
                }

            case ILOpcode.Ldarga_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileLdarga(idx);
                }

            // === Locals ===
            case ILOpcode.Ldloc_0: return CompileLdloc(0);
            case ILOpcode.Ldloc_1: return CompileLdloc(1);
            case ILOpcode.Ldloc_2: return CompileLdloc(2);
            case ILOpcode.Ldloc_3: return CompileLdloc(3);

            case ILOpcode.Ldloc_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileLdloc(idx);
                }

            case ILOpcode.Stloc_0: return CompileStloc(0);
            case ILOpcode.Stloc_1: return CompileStloc(1);
            case ILOpcode.Stloc_2: return CompileStloc(2);
            case ILOpcode.Stloc_3: return CompileStloc(3);

            case ILOpcode.Stloc_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileStloc(idx);
                }

            case ILOpcode.Ldloca_S:
                {
                    byte idx = _il[_ilOffset++];
                    return CompileLdloca(idx);
                }

            // === Stack ===
            case ILOpcode.Dup:
                return CompileDup();

            case ILOpcode.Pop:
                return CompilePop();

            case ILOpcode.Jmp:
                {
                    // jmp <method token> - tail jump to method
                    // This replaces the current stack frame with the target method's frame.
                    // The current method's arguments become the target method's arguments.
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileJmp(token);
                }

            // === Arithmetic ===
            case ILOpcode.Add: return CompileAdd();
            case ILOpcode.Sub: return CompileSub();
            case ILOpcode.Mul: return CompileMul();
            case ILOpcode.Div: return CompileDiv(signed: true);
            case ILOpcode.Div_Un: return CompileDiv(signed: false);
            case ILOpcode.Rem: return CompileRem(signed: true);
            case ILOpcode.Rem_Un: return CompileRem(signed: false);
            case ILOpcode.Neg: return CompileNeg();
            case ILOpcode.Not: return CompileNot();
            case ILOpcode.And: return CompileAnd();
            case ILOpcode.Or: return CompileOr();
            case ILOpcode.Xor: return CompileXor();
            case ILOpcode.Shl: return CompileShl();
            case ILOpcode.Shr: return CompileShr(signed: true);
            case ILOpcode.Shr_Un: return CompileShr(signed: false);

            // === Overflow-checking Arithmetic ===
            case ILOpcode.Add_Ovf: return CompileAddOvf(unsigned: false);
            case ILOpcode.Add_Ovf_Un: return CompileAddOvf(unsigned: true);
            case ILOpcode.Sub_Ovf: return CompileSubOvf(unsigned: false);
            case ILOpcode.Sub_Ovf_Un: return CompileSubOvf(unsigned: true);
            case ILOpcode.Mul_Ovf: return CompileMulOvf(unsigned: false);
            case ILOpcode.Mul_Ovf_Un: return CompileMulOvf(unsigned: true);

            // === Conversion ===
            case ILOpcode.Conv_I1: return CompileConv(1, signed: true);
            case ILOpcode.Conv_I2: return CompileConv(2, signed: true);
            case ILOpcode.Conv_I4: return CompileConv(4, signed: true);
            case ILOpcode.Conv_I8: return CompileConv(8, signed: true);
            case ILOpcode.Conv_U1: return CompileConv(1, signed: false);
            case ILOpcode.Conv_U2: return CompileConv(2, signed: false);
            case ILOpcode.Conv_U4: return CompileConv(4, signed: false);
            case ILOpcode.Conv_U8: return CompileConv(8, signed: false);
            case ILOpcode.Conv_I: return CompileConv(8, signed: true);   // Native int = 64-bit
            case ILOpcode.Conv_U: return CompileConv(8, signed: false);  // Native uint = 64-bit
            case ILOpcode.Conv_R4: return CompileConvR4();
            case ILOpcode.Conv_R8: return CompileConvR8();
            case ILOpcode.Conv_R_Un: return CompileConvR_Un();

            // === Floating Point Check ===
            case ILOpcode.Ckfinite: return CompileCkfinite();

            // === Typed references (rare) ===
            case ILOpcode.Refanyval:
                {
                    // refanyval <type token> - get value pointer from typed reference
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileRefanyval(token);
                }
            case ILOpcode.Mkrefany:
                {
                    // mkrefany <type token> - make typed reference
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileMkrefany(token);
                }

            // === Overflow-checking Conversion (signed source) ===
            case ILOpcode.Conv_Ovf_I1: return CompileConvOvf(1, targetSigned: true, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_U1: return CompileConvOvf(1, targetSigned: false, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_I2: return CompileConvOvf(2, targetSigned: true, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_U2: return CompileConvOvf(2, targetSigned: false, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_I4: return CompileConvOvf(4, targetSigned: true, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_U4: return CompileConvOvf(4, targetSigned: false, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_I8: return CompileConvOvf(8, targetSigned: true, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_U8: return CompileConvOvf(8, targetSigned: false, sourceUnsigned: false);
            case ILOpcode.Conv_Ovf_I: return CompileConvOvf(8, targetSigned: true, sourceUnsigned: false);  // Native int = 64-bit
            case ILOpcode.Conv_Ovf_U: return CompileConvOvf(8, targetSigned: false, sourceUnsigned: false); // Native uint = 64-bit

            // === Control Flow ===
            case ILOpcode.Ret:
                return CompileRet();

            case ILOpcode.Br_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBr(_ilOffset + offset);
                }

            case ILOpcode.Br:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBr(_ilOffset + offset);
                }

            case ILOpcode.Brfalse_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBrfalse(_ilOffset + offset);
                }

            case ILOpcode.Brtrue_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBrtrue(_ilOffset + offset);
                }

            case ILOpcode.Beq_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_E, _ilOffset + offset);
                }

            case ILOpcode.Bne_Un_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_NE, _ilOffset + offset);
                }

            case ILOpcode.Blt_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_L, _ilOffset + offset);
                }

            case ILOpcode.Ble_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_LE, _ilOffset + offset);
                }

            case ILOpcode.Bgt_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_G, _ilOffset + offset);
                }

            case ILOpcode.Bge_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_GE, _ilOffset + offset);
                }

            // Unsigned short branches
            case ILOpcode.Bge_Un_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_AE, _ilOffset + offset);
                }

            case ILOpcode.Bgt_Un_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_A, _ilOffset + offset);
                }

            case ILOpcode.Ble_Un_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_BE, _ilOffset + offset);
                }

            case ILOpcode.Blt_Un_S:
                {
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileBranchCmp(X64Emitter.CC_B, _ilOffset + offset);
                }

            // Long branches (4-byte offset)
            case ILOpcode.Brfalse:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBrfalse(_ilOffset + offset);
                }

            case ILOpcode.Brtrue:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBrtrue(_ilOffset + offset);
                }

            case ILOpcode.Beq:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_E, _ilOffset + offset);
                }

            case ILOpcode.Bne_Un:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_NE, _ilOffset + offset);
                }

            case ILOpcode.Blt:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_L, _ilOffset + offset);
                }

            case ILOpcode.Ble:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_LE, _ilOffset + offset);
                }

            case ILOpcode.Bgt:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_G, _ilOffset + offset);
                }

            case ILOpcode.Bge:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_GE, _ilOffset + offset);
                }

            case ILOpcode.Blt_Un:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_B, _ilOffset + offset);
                }

            case ILOpcode.Ble_Un:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_BE, _ilOffset + offset);
                }

            case ILOpcode.Bgt_Un:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_A, _ilOffset + offset);
                }

            case ILOpcode.Bge_Un:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBranchCmp(X64Emitter.CC_AE, _ilOffset + offset);
                }

            case ILOpcode.Switch:
                return CompileSwitch();

            // === Exception handling ===
            case ILOpcode.Leave_S:
                {
                    int leaveILOffset = _ilOffset - 1;  // IL offset of the leave.s opcode
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileLeave(_ilOffset + offset, leaveILOffset);
                }

            case ILOpcode.Leave:
                {
                    int leaveILOffset = _ilOffset - 1;  // IL offset of the leave opcode
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLeave(_ilOffset + offset, leaveILOffset);
                }

            case ILOpcode.Throw:
                return CompileThrow();

            case ILOpcode.Endfinally:
                return CompileEndfinally();

            // === Indirect load/store ===
            case ILOpcode.Ldind_I1:
                return CompileLdind(1, signExtend: true);
            case ILOpcode.Ldind_U1:
                return CompileLdind(1, signExtend: false);
            case ILOpcode.Ldind_I2:
                return CompileLdind(2, signExtend: true);
            case ILOpcode.Ldind_U2:
                return CompileLdind(2, signExtend: false);
            case ILOpcode.Ldind_I4:
                return CompileLdind(4, signExtend: true);
            case ILOpcode.Ldind_U4:
                return CompileLdind(4, signExtend: false);
            case ILOpcode.Ldind_I8:
                return CompileLdind(8, signExtend: false);  // 8 bytes = full 64-bit, no extension
            case ILOpcode.Ldind_I:
                return CompileLdind(8, signExtend: false);  // Native int = 64-bit on x64
            case ILOpcode.Ldind_R4:
                return CompileLdindFloat(4);  // Load float32 indirect
            case ILOpcode.Ldind_R8:
                return CompileLdindFloat(8);  // Load float64 indirect
            case ILOpcode.Ldind_Ref:
                return CompileLdind(8, signExtend: false);  // Object ref = pointer = 64-bit

            case ILOpcode.Stind_Ref:
            case ILOpcode.Stind_I:
                return CompileStind(8);  // Object ref / native int = 64-bit
            case ILOpcode.Stind_I1:
                return CompileStind(1);
            case ILOpcode.Stind_I2:
                return CompileStind(2);
            case ILOpcode.Stind_I4:
                return CompileStind(4);
            case ILOpcode.Stind_I8:
                return CompileStind(8);
            case ILOpcode.Stind_R4:
                return CompileStindFloat(4);  // Store float32 indirect
            case ILOpcode.Stind_R8:
                return CompileStindFloat(8);  // Store float64 indirect

            // === Value type operations ===
            case ILOpcode.Cpobj:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileCpobj(token);
                }

            case ILOpcode.Ldobj:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdobj(token);
                }

            case ILOpcode.Stobj:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileStobj(token);
                }

            // === Method calls ===
            case ILOpcode.Call:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileCall(token);
                }

            case ILOpcode.Calli:
                {
                    uint sigToken = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileCalli(sigToken);
                }

            case ILOpcode.Callvirt:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileCallvirt(token);
                }

            // === Field access ===
            case ILOpcode.Ldfld:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdfld(token);
                }
            case ILOpcode.Ldflda:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdflda(token);
                }
            case ILOpcode.Stfld:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileStfld(token);
                }
            case ILOpcode.Ldsfld:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdsfld(token);
                }
            case ILOpcode.Ldsflda:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdsflda(token);
                }
            case ILOpcode.Stsfld:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileStsfld(token);
                }

            // === Array operations ===
            case ILOpcode.Ldlen:
                return CompileLdlen();
            case ILOpcode.Ldelem_I1:
                return CompileLdelem(1, signed: true);
            case ILOpcode.Ldelem_U1:
                return CompileLdelem(1, signed: false);
            case ILOpcode.Ldelem_I2:
                return CompileLdelem(2, signed: true);
            case ILOpcode.Ldelem_U2:
                return CompileLdelem(2, signed: false);
            case ILOpcode.Ldelem_I4:
                return CompileLdelem(4, signed: true);
            case ILOpcode.Ldelem_U4:
                return CompileLdelem(4, signed: false);
            case ILOpcode.Ldelem_I8:
                return CompileLdelem(8, signed: true);
            case ILOpcode.Ldelem_I:
                return CompileLdelem(8, signed: true);  // Native int = 64-bit
            case ILOpcode.Ldelem_Ref:
                return CompileLdelem(8, signed: false); // Pointer = 64-bit
            case ILOpcode.Ldelem_R4:
                return CompileLdelemFloat(4);
            case ILOpcode.Ldelem_R8:
                return CompileLdelemFloat(8);
            case ILOpcode.Stelem_I:
                return CompileStelem(8);
            case ILOpcode.Stelem_I1:
                return CompileStelem(1);
            case ILOpcode.Stelem_I2:
                return CompileStelem(2);
            case ILOpcode.Stelem_I4:
                return CompileStelem(4);
            case ILOpcode.Stelem_I8:
                return CompileStelem(8);
            case ILOpcode.Stelem_Ref:
                return CompileStelem(8);  // Pointer = 64-bit
            case ILOpcode.Stelem_R4:
                return CompileStelemFloat(4);
            case ILOpcode.Stelem_R8:
                return CompileStelemFloat(8);
            case ILOpcode.Ldelema:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdelema(token);
                }

            // === Object allocation ===
            case ILOpcode.Newobj:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileNewobj(token);
                }
            case ILOpcode.Newarr:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileNewarr(token);
                }

            // === Boxing/Unboxing ===
            case ILOpcode.Box:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileBox(token);
                }
            case ILOpcode.Unbox:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileUnbox(token);
                }
            case ILOpcode.Unbox_Any:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileUnboxAny(token);
                }

            // === Type operations ===
            case ILOpcode.Castclass:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileCastclass(token);
                }
            case ILOpcode.Isinst:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileIsinst(token);
                }

            // === String loading ===
            case ILOpcode.Ldstr:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdstr(token);
                }

            // === Token loading ===
            case ILOpcode.Ldtoken:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdtoken(token);
                }

            // === Array element with type token ===
            case ILOpcode.Ldelem:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdelemToken(token);
                }
            case ILOpcode.Stelem:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileStelemToken(token);
                }

            // === Two-byte opcodes ===
            case ILOpcode.Prefix_FE:
                {
                    byte op2 = _il[_ilOffset++];
                    return CompileTwoByteOpcode(op2);
                }

            default:
                return false;  // Unsupported opcode
        }
    }

    private bool CompileTwoByteOpcode(byte op2)
    {
        switch (op2)
        {
            case ILOpcode.Ceq_2:
                return CompileCeq();
            case ILOpcode.Cgt_2:
                return CompileCgt(signed: true);
            case ILOpcode.Cgt_Un_2:
                return CompileCgt(signed: false);
            case ILOpcode.Clt_2:
                return CompileClt(signed: true);
            case ILOpcode.Clt_Un_2:
                return CompileClt(signed: false);
            case ILOpcode.Cpblk_2:
                return CompileCpblk();
            case ILOpcode.Initblk_2:
                return CompileInitblk();
            case ILOpcode.Initobj_2:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileInitobj(token);
                }
            case ILOpcode.Sizeof_2:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileSizeof(token);
                }

            case ILOpcode.Rethrow_2:
                return CompileRethrow();

            // Function pointer loading
            case ILOpcode.Ldftn_2:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdftn(token);
                }

            case ILOpcode.Ldvirtftn_2:
                {
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLdvirtftn(token);
                }

            // Stack allocation
            case ILOpcode.Localloc_2:
                return CompileLocalloc();

            // Two-byte arg/local opcodes (16-bit index for >255 args/locals)
            case ILOpcode.Ldarg_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileLdarg(idx);
                }
            case ILOpcode.Ldarga_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileLdarga(idx);
                }
            case ILOpcode.Starg_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileStarg(idx);
                }
            case ILOpcode.Ldloc_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileLdloc(idx);
                }
            case ILOpcode.Ldloca_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileLdloca(idx);
                }
            case ILOpcode.Stloc_2byte:
                {
                    ushort idx = *(ushort*)(_il + _ilOffset);
                    _ilOffset += 2;
                    return CompileStloc(idx);
                }

            // Overflow-checking conversions (unsigned source)
            case ILOpcode.Conv_Ovf_I1_Un_2: return CompileConvOvf(1, targetSigned: true, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_U1_Un_2: return CompileConvOvf(1, targetSigned: false, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_I2_Un_2: return CompileConvOvf(2, targetSigned: true, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_U2_Un_2: return CompileConvOvf(2, targetSigned: false, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_I4_Un_2: return CompileConvOvf(4, targetSigned: true, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_U4_Un_2: return CompileConvOvf(4, targetSigned: false, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_I8_Un_2: return CompileConvOvf(8, targetSigned: true, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_U8_Un_2: return CompileConvOvf(8, targetSigned: false, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_I_Un_2: return CompileConvOvf(8, targetSigned: true, sourceUnsigned: true);
            case ILOpcode.Conv_Ovf_U_Un_2: return CompileConvOvf(8, targetSigned: false, sourceUnsigned: true);

            // === Exception handling ===
            case ILOpcode.Endfilter_2:
                return CompileEndfilter();

            // === Prefix opcodes ===
            // These prefixes modify the behavior of the following opcode
            case ILOpcode.Constrained_2:
                {
                    // constrained. <type token> - prefix for callvirt
                    // Save the constraint type token - the next callvirt will use it
                    _constrainedTypeToken = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    // NOTE: For value types, constrained. is REQUIRED, not just an optimization
                    // When calling a virtual method on a value type through constrained.:
                    // - Stack has managed pointer to the value (not boxed object)
                    // - For value types that override the method: direct call
                    // - For value types that don't override: box and call
                    return true;
                }
            case ILOpcode.Readonly_2:
                // readonly. prefix - indicates ldelema result won't be used for writes
                // For naive JIT, this is a no-op (just an optimization hint)
                return true;
            case ILOpcode.Tail_2:
                // tail. prefix - indicates a tail call follows
                // Set flag to be consumed by the next call instruction
                _tailPrefix = true;
                return true;
            case ILOpcode.Volatile_2:
                // volatile. prefix - indicates next load/store is volatile
                // For naive JIT with no optimizations, memory accesses are already ordered
                return true;
            case ILOpcode.Unaligned_2:
                {
                    // unaligned. <alignment> - indicates next load/store may be unaligned
                    byte alignment = _il[_ilOffset++];
                    _ = alignment; // Unused in naive JIT
                    return true;
                }
            case ILOpcode.No_2:
                {
                    // no. <flags> - suppress runtime checks
                    byte flags = _il[_ilOffset++];
                    _ = flags; // Unused in naive JIT
                    return true;
                }

            // === Rare opcodes ===
            case ILOpcode.Arglist_2:
                // arglist - get handle to argument list (varargs)
                return CompileArglist();
            case ILOpcode.Refanytype_2:
                // refanytype - get type from typed reference
                return CompileRefanytype();

            default:
                DebugConsole.Write("[JIT] Unknown 0xFE opcode: 0x");
                DebugConsole.WriteHex(op2);
                DebugConsole.WriteLine();
                return false;
        }
    }

    // === Opcode implementations ===

    // Naive stack approach: use RAX as top of stack for simple cases.
    // Push/pop to actual stack for deeper stack operations.

    private bool CompileLdcI4(int value)
    {
        // Push constant onto eval stack (sign-extended to 64-bit)
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)(long)value);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Int32);
        return true;
    }

    private bool CompileLdarg(int index)
    {
        // Load argument from shadow space - use full 64-bit load
        // When the method returns a large struct (>16 bytes), the caller passes
        // a hidden buffer pointer as the first argument (in RCX). This shifts all
        // IL arguments by 1 in the calling convention:
        //   - Hidden buffer pointer: RCX -> [FP+16] (arg slot 0)
        //   - IL arg0: RDX -> [FP+24] (arg slot 1)
        //   - IL arg1: R8 -> [FP+32] (arg slot 2)
        //   - etc.
        // We need to adjust the IL index to account for this shift.
        int physicalArgIndex = index;
        if (_returnIsValueType && _returnTypeSize > 16)
        {
            physicalArgIndex = index + 1;
        }

        X64Emitter.LoadArgFromHome(ref _code, VReg.R0, physicalArgIndex);

        // Check if this arg is a value type - affects ldfld behavior
        // In x64 calling convention:
        // - Small value types (<=8 bytes) are passed BY VALUE in registers - the actual data
        // - Large value types (>8 bytes) are passed by pointer
        // So for small VT args, we're loading the VALUE directly, not a pointer.
        // For large VT args, we're loading a pointer to the data.
        EvalStackEntry entry = EvalStackEntry.NativeInt;
        bool isArgVT = _argIsValueType != null && index >= 0 && index < _argCount && _argIsValueType[index];
        ushort argSize = (_argTypeSize != null && index >= 0 && index < _argCount) ? _argTypeSize[index] : (ushort)0;
        byte floatKind = (_argFloatKind != null && index >= 0 && index < _argCount) ? _argFloatKind[index] : (byte)0;

        // Check for float argument type first
        if (floatKind == 4)
        {
            entry = EvalStackEntry.Float32;
        }
        else if (floatKind == 8)
        {
            entry = EvalStackEntry.Float64;
        }
        else if (isArgVT && argSize > 0 && argSize <= 8)
        {
            // Small value type passed by value - the VALUE is in R0, not a pointer
            // Mark as Struct so ldfld uses bit extraction instead of memory dereference
            entry = EvalStackEntry.Struct(argSize);
        }
        else if (isArgVT && argSize > 8)
        {
            // Large value type passed by pointer - R0 contains a pointer to the data
            // We need to push the actual VALUE onto the eval stack, not the pointer
            // This matches what stobj/stelem.any expects.
            int alignedSize = (argSize + 7) & ~7;

            // Make room on eval stack
            X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);

            // R0 has the pointer to struct data - copy from [R0] to [RSP]
            int copyOffset = 0;
            while (copyOffset + 8 <= argSize)
            {
                X64Emitter.MovRM(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 8;
            }
            if (copyOffset + 4 <= argSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= argSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 2;
            }
            if (copyOffset < argSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.SP, copyOffset, VReg.R1);
            }

            // Track as ONE entry with actual byte size
            PushEntry(EvalStackEntry.Struct(argSize));
            return true;
        }
        // else: non-VT arg or unknown size - treat as NativeInt (reference/pointer)

        PushR0(entry);
        return true;
    }

    private bool CompileLdloc(int index)
    {
        // Locals are at negative offsets from RBP
        int offset = X64Emitter.GetLocalOffset(index);

        // Check if this local is a value type - affects stack type marking
        bool isValueType = _localIsValueType != null && index >= 0 && index < _localCount && _localIsValueType[index];
        ushort typeSize = (_localTypeSize != null && index >= 0 && index < _localCount) ? _localTypeSize[index] : (ushort)0;
        // bool debugBind = IsDebugBindMethod() && index == 4;
        //
        // // Debug: Log large struct ldloc
        // if (typeSize > 8)
        // {
        //     // DebugConsole.Write("[ldloc] idx=");
        //     DebugConsole.WriteHex((ulong)index);
        //     DebugConsole.Write(" isVT=");
        //     DebugConsole.WriteHex(isValueType ? 1UL : 0UL);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteHex(typeSize);
        //     DebugConsole.WriteLine();
        // }
        //
        // if (debugBind)
        // {
        //     // DebugConsole.Write("[BindDbg] ldloc.4 il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" codePos=");
        //     DebugConsole.WriteHex((ulong)_code.Position);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }

        byte stackType;
        if (isValueType && typeSize > 16)
        {
            // Large value type (>16 bytes): push the full struct value onto the stack
            // This matches what stobj expects (a source VALUE, not a pointer).
            // For stelem.any, we need special handling in CompileStelemToken.
            int alignedSize = (typeSize + 7) & ~7;
            int slots = alignedSize / 8;

            // DebugConsole.Write("[ldloc VT] idx=");
            // DebugConsole.WriteDecimal((uint)index);
            // DebugConsole.Write(" size=");
            // DebugConsole.WriteDecimal((uint)typeSize);
            // DebugConsole.Write(" src=[FP-");
            // DebugConsole.WriteDecimal((uint)(-offset));
            // DebugConsole.Write("] depth=");
            // DebugConsole.WriteDecimal((uint)_evalStackDepth);
            // DebugConsole.WriteLine();

            // Make room on eval stack
            X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);

            // Copy the struct from local to eval stack
            int copyOffset = 0;
            while (copyOffset + 8 <= typeSize)
            {
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset + copyOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 8;
            }
            if (copyOffset + 4 <= typeSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.FP, offset + copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= typeSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R0, VReg.FP, offset + copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 2;
            }
            if (copyOffset < typeSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R0, VReg.FP, offset + copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.SP, copyOffset, VReg.R0);
            }

            // Track as ONE entry with actual byte size (not N slots!)
            PushEntry(EvalStackEntry.Struct(typeSize));
            return true;
        }
        else if (isValueType && typeSize > 8)
        {
            // Medium value type (9-16 bytes): push as 16 bytes (aligned)
            int alignedSize = (typeSize + 7) & ~7;
            X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
            X64Emitter.MovMR(ref _code, VReg.SP, 0, VReg.R0);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset + 8);
            X64Emitter.MovMR(ref _code, VReg.SP, 8, VReg.R0);
            // Track as ONE entry with actual byte size
            PushEntry(EvalStackEntry.Struct(typeSize));
            return true;
        }
        else if (isValueType && typeSize > 0)
        {
            // Small value type (1-8 bytes): load VALUE directly, push with size
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(EvalStackEntry.Struct(typeSize));
            return true;
        }
        else
        {
            // Type info not available: primitive, reference type
            // Load VALUE directly (8 bytes)
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
        }

        // Default: push as Int32 (primitives, references)
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Int32);
        // if (debugBind)
        // {
        //     // DebugConsole.Write("[BindDbg] ldloc.4 cached depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }
        return true;
    }

    private bool CompileStloc(int index)
    {
        bool isValueType = _localIsValueType != null && index >= 0 && index < _localCount && _localIsValueType[index];
        ushort typeSize = (_localTypeSize != null && index >= 0 && index < _localCount) ? _localTypeSize[index] : (ushort)0;

        // Debug: trace stloc.0 in TestDictForeach
        if (_debugAssemblyId == 6 && _debugMethodToken == 0x060002E7 && index == 0)
        {
            EvalStackEntry dbgTos = PeekEntry();
            DebugConsole.Write("[DF-stloc0] isVT=");
            DebugConsole.WriteDecimal(isValueType ? 1U : 0U);
            DebugConsole.Write(" typeSz=");
            DebugConsole.WriteDecimal(typeSize);
            DebugConsole.Write(" tosByteSz=");
            DebugConsole.WriteDecimal((uint)dbgTos.ByteSize);
            DebugConsole.Write(" tosKind=");
            DebugConsole.WriteDecimal((uint)dbgTos.Kind);
            DebugConsole.WriteLine();
        }
        // bool debugBind = IsDebugBindMethod();
        //
        // // Debug: trace stloc for AllocateBuffers (asm 3, method 0x0600001D)
        // if (_debugAssemblyId == 3 && _debugMethodToken == 0x0600001D)
        // {
        //     // DebugConsole.Write("[AllocBuf stloc] idx=");
        //     DebugConsole.WriteDecimal((uint)index);
        //     DebugConsole.Write(" isVT=");
        //     DebugConsole.WriteDecimal(isValueType ? 1U : 0U);
        //     DebugConsole.Write(" sz=");
        //     DebugConsole.WriteDecimal(typeSize);
        //     DebugConsole.Write(" _localIsVT=");
        //     DebugConsole.WriteHex((ulong)(_localIsValueType != null ? 1 : 0));
        //     DebugConsole.Write(" _localSz=");
        //     DebugConsole.WriteHex((ulong)(_localTypeSize != null ? 1 : 0));
        //     DebugConsole.Write(" cnt=");
        //     DebugConsole.WriteDecimal((uint)_localCount);
        //     DebugConsole.WriteLine();
        // }
        //
        // if (debugBind)
        // {
        //     // DebugConsole.Write("[BindDbg] stloc.");
        //     DebugConsole.WriteDecimal((uint)index);
        //     DebugConsole.Write(" il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" codePos=");
        //     DebugConsole.WriteHex((ulong)_code.Position);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.Write(" isVT=");
        //     DebugConsole.WriteDecimal(isValueType ? 1U : 0U);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal(typeSize);
        //     DebugConsole.WriteLine();
        // }

        // Use new type system: PeekEntry tells us the actual byte size on stack
        EvalStackEntry tosEntry = PeekEntry();
        int stackByteSize = tosEntry.ByteSize;  // Actual bytes this entry occupies on RSP

        // Determine the size to copy into the local
        // Prefer metadata size if available, otherwise use stack entry size
        int effectiveSize = (int)typeSize;
        if (effectiveSize == 0 && tosEntry.Kind == EvalStackKind.ValueType)
        {
            // TypeRef resolution failed - use stack entry's tracked size
            effectiveSize = tosEntry.RawSize;
            // DebugConsole.Write("[stloc WARN] VT local idx=");
            // DebugConsole.WriteDecimal((uint)index);
            // DebugConsole.Write(" typeSize=0, using stack rawSize=");
            // DebugConsole.WriteDecimal((uint)effectiveSize);
            // DebugConsole.WriteLine();
        }

        // Large value type (>8 bytes): copy full struct from eval stack into local
        if (effectiveSize > 8 || stackByteSize > 8)
        {
            int destOffset = X64Emitter.GetLocalOffset(index);
            int copySize = effectiveSize > 0 ? effectiveSize : stackByteSize;
            // DebugConsole.Write("[stloc VT] idx=");
            // DebugConsole.WriteDecimal((uint)index);
            // DebugConsole.Write(" off=-");
            // DebugConsole.WriteDecimal((uint)(-destOffset));
            // DebugConsole.Write(" cp=");
            // DebugConsole.WriteDecimal((uint)copySize);
            // DebugConsole.Write(" st=");
            // DebugConsole.WriteDecimal((uint)stackByteSize);
            // DebugConsole.WriteLine();

            // Copy from [RSP] (top of eval stack) into the local slot
            int copyOffset = 0;
            while (copyOffset + 8 <= copySize)
            {
                X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, copyOffset);
                X64Emitter.MovMR(ref _code, VReg.FP, destOffset + copyOffset, VReg.R3);
                copyOffset += 8;
            }

            if (copyOffset + 4 <= copySize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R3, VReg.SP, copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.FP, destOffset + copyOffset, VReg.R3);
                copyOffset += 4;
            }

            if (copyOffset + 2 <= copySize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R3, VReg.SP, copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.FP, destOffset + copyOffset, VReg.R3);
                copyOffset += 2;
            }

            if (copyOffset < copySize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R3, VReg.SP, copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.FP, destOffset + copyOffset, VReg.R3);
            }

            // Pop the entry off the eval stack (uses tracked ByteSize)
            if (stackByteSize > 0)
            {
                X64Emitter.AddRI(ref _code, VReg.SP, stackByteSize);
            }
            PopEntry();
            return true;
        }

        // Small value (≤8 bytes): pop single slot, store to local
        EvalStackEntry entry = PopEntry();
        X64Emitter.Pop(ref _code, VReg.R0);
        // Adjust RSP if entry was larger than 8 bytes (shouldn't happen here but safe)
        if (entry.ByteSize > 8)
        {
            X64Emitter.AddRI(ref _code, VReg.SP, entry.ByteSize - 8);
        }
        int offset = X64Emitter.GetLocalOffset(index);
        X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);

        // if (debugBind)
        // {
        //     // DebugConsole.Write("[BindDbg] stloc.");
        //     DebugConsole.WriteDecimal((uint)index);
        //     DebugConsole.Write(" stored offset=");
        //     DebugConsole.WriteDecimal((uint)offset);
        //     DebugConsole.Write(" depthNow=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }
        return true;
    }

    private bool CompileDup()
    {
        // Use new type system: PeekEntry tells us actual byte size
        EvalStackEntry tosEntry = PeekEntry();
        int byteSize = tosEntry.ByteSize;

        // Make room for the duplicate
        X64Emitter.SubRI(ref _code, VReg.SP, byteSize);

        // Copy bytes from [SP + byteSize] to [SP]
        int copyOffset = 0;
        while (copyOffset + 8 <= byteSize)
        {
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, byteSize + copyOffset);
            X64Emitter.MovMR(ref _code, VReg.SP, copyOffset, VReg.R0);
            copyOffset += 8;
        }
        // Handle trailing bytes (4, 2, 1) if any
        if (copyOffset + 4 <= byteSize)
        {
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.SP, byteSize + copyOffset);
            X64Emitter.MovMR32(ref _code, VReg.SP, copyOffset, VReg.R0);
            copyOffset += 4;
        }
        if (copyOffset + 2 <= byteSize)
        {
            X64Emitter.MovRM16(ref _code, VReg.R0, VReg.SP, byteSize + copyOffset);
            X64Emitter.MovMR16(ref _code, VReg.SP, copyOffset, VReg.R0);
            copyOffset += 2;
        }
        if (copyOffset < byteSize)
        {
            X64Emitter.MovRM8(ref _code, VReg.R0, VReg.SP, byteSize + copyOffset);
            X64Emitter.MovMR8(ref _code, VReg.SP, copyOffset, VReg.R0);
        }

        // Push duplicate entry (same type as original)
        PushEntry(tosEntry);
        return true;
    }

    private bool CompilePop()
    {
        // Special case: at catch handler funclet entry, the exception object is in RCX, not on stack.
        // The IL 'pop' to discard the exception should be a no-op (don't adjust RSP).
        if (_funcletCatchHandlerEntry)
        {
            _funcletCatchHandlerEntry = false;  // Clear flag - only first pop is special
            // Don't emit any code - the exception in RCX is just ignored
            return true;
        }

        // Use new type system: PopEntry tells us actual byte size to deallocate
        EvalStackEntry entry = PopEntry();
        int byteSize = entry.ByteSize;
        X64Emitter.AddRI(ref _code, VReg.SP, byteSize);
        return true;
    }

    // === Rich Eval Stack Type System ===
    // Each entry is ONE value with tracked byte size.
    // A 32-byte struct is ONE entry with ByteSize=32, not 4 separate entries.

    /// <summary>
    /// Push an entry onto the eval stack.
    /// This does NOT emit any machine code - use with machine code emission helpers.
    /// </summary>
    private void PushEntry(EvalStackEntry entry)
    {
        if (_evalStackDepth < MaxStackDepth && _evalStack != null)
            _evalStack[_evalStackDepth] = entry;
        _evalStackDepth++;
        _evalStackByteSize += entry.ByteSize;
    }

    /// <summary>
    /// Pop an entry from the rich eval stack.
    /// Returns the entry that was popped. Does NOT emit machine code.
    /// </summary>
    private EvalStackEntry PopEntry()
    {
        if (_evalStackDepth > 0)
        {
            _evalStackDepth--;
            if (_evalStack != null)
            {
                var entry = _evalStack[_evalStackDepth];
                _evalStackByteSize -= entry.ByteSize;
                return entry;
            }
        }
        // Return a default Int32 entry if stack is empty
        return EvalStackEntry.Int32;
    }

    /// <summary>
    /// Peek at the top entry without removing it.
    /// </summary>
    private EvalStackEntry PeekEntry()
    {
        if (_evalStackDepth > 0 && _evalStack != null)
            return _evalStack[_evalStackDepth - 1];
        return EvalStackEntry.Int32;
    }

    /// <summary>
    /// Peek at entry at a specific depth from top (0 = top, 1 = second from top, etc.)
    /// </summary>
    private EvalStackEntry PeekEntryAt(int depthFromTop)
    {
        int idx = _evalStackDepth - 1 - depthFromTop;
        if (idx >= 0 && _evalStack != null)
            return _evalStack[idx];
        return EvalStackEntry.Int32;
    }

    /// <summary>
    /// Get the total byte size of all entries on the eval stack.
    /// </summary>
    private int GetEvalStackByteSize() => _evalStackByteSize;

    /// <summary>
    /// Get the byte offset from RSP to a specific entry (0 = top of stack).
    /// This is the actual physical offset accounting for all entry sizes.
    /// </summary>
    private int GetEntryRspOffset(int depthFromTop)
    {
        int offset = 0;
        for (int i = 0; i < depthFromTop && i < _evalStackDepth; i++)
        {
            offset += _evalStack[_evalStackDepth - 1 - i].ByteSize;
        }
        return offset;
    }

    private bool IsDebugBindMethod() => _debugAssemblyId == 4 && _debugMethodToken == 0x0600002A;
    private bool IsDebugBoolBugMethod() =>
        (_debugAssemblyId == 4 && _debugMethodToken == 0x0600002A) ||
        (_debugAssemblyId == 3 && _debugMethodToken == 0x06000006);
    // Debug ReadAndProgramBar: assembly 5 (VirtioBlk), find by IL size ~0x410
    private bool IsDebugBarMethod() => _debugAssemblyId == 5 && _ilLength > 0x400;

    // === Stack Operations ===

    /// <summary>
    /// Push R0 onto the eval stack and track the type.
    /// </summary>
    private void PushR0(EvalStackEntry entry)
    {
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(entry);
    }

    /// <summary>
    /// Pop a value from the eval stack into R0.
    /// </summary>
    private void PopR0()
    {
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();
    }

    /// <summary>
    /// Pop a value from the eval stack into a specific register.
    /// </summary>
    private void PopReg(VReg dst)
    {
        X64Emitter.Pop(ref _code, dst);
        PopEntry();
    }

    /// <summary>
    /// Load a constant into R0 and push it onto the eval stack.
    /// </summary>
    private void PushConst(long value, EvalStackEntry entry)
    {
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)value);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(entry);
    }

    private bool CompileAdd()
    {
        // Pop two values, add, push result
        // Peek at types to determine float vs int (without popping yet)
        EvalStackEntry entry2 = PeekEntryAt(0);  // Top
        EvalStackEntry entry1 = PeekEntryAt(1);  // Second from top

        if (IsFloatEntry(entry1) || IsFloatEntry(entry2))
        {
            // Float arithmetic with peephole optimization
            // Pop operands into registers
            PopReg(VReg.R2);  // Second operand (bits) -> R2
            PopR0();          // First operand (bits) -> R0

            // Determine if single or double precision (use widest type)
            bool isDouble = (entry1.Kind == EvalStackKind.Float64 || entry2.Kind == EvalStackKind.Float64);

            if (isDouble)
            {
                // Move bit patterns to XMM registers
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                // ADDSD xmm0, xmm1
                X64Emitter.AddsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                // Move result bits back
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float64);
            }
            else
            {
                // Single precision
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                // ADDSS xmm0, xmm1
                X64Emitter.AddssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                // Move result bits back
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float32);
            }
        }
        else
        {
            // Integer add
            PopReg(VReg.R2);  // Second operand -> R2
            PopR0();          // First operand -> R0
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R2);
            PushR0(EvalStackEntry.NativeInt);
        }
        return true;
    }

    /// <summary>
    /// Check if an entry is a floating-point type
    /// </summary>
    private bool IsFloatEntry(EvalStackEntry entry) =>
        entry.Kind == EvalStackKind.Float32 || entry.Kind == EvalStackKind.Float64;

    private bool CompileSub()
    {
        // Pop two values, subtract, push result
        // Peek at types to determine float vs int (without popping yet)
        EvalStackEntry entry2 = PeekEntryAt(0);  // Top
        EvalStackEntry entry1 = PeekEntryAt(1);  // Second from top

        if (IsFloatEntry(entry1) || IsFloatEntry(entry2))
        {
            // Float arithmetic with peephole optimization
            PopReg(VReg.R2);  // Second operand (bits) -> R2
            PopR0();          // First operand (bits) -> R0

            bool isDouble = (entry1.Kind == EvalStackKind.Float64 || entry2.Kind == EvalStackKind.Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.SubsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.SubssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float32);
            }
        }
        else
        {
            // Integer subtract
            PopReg(VReg.R2);  // Second operand -> R2
            PopR0();          // First operand -> R0
            X64Emitter.SubRR(ref _code, VReg.R0, VReg.R2);
            PushR0(EvalStackEntry.NativeInt);
        }
        return true;
    }

    private bool CompileMul()
    {
        // Pop two values, multiply, push result
        // Peek at types to determine float vs int (without popping yet)
        EvalStackEntry entry2 = PeekEntryAt(0);  // Top
        EvalStackEntry entry1 = PeekEntryAt(1);  // Second from top

        if (IsFloatEntry(entry1) || IsFloatEntry(entry2))
        {
            // Float arithmetic with peephole optimization
            PopReg(VReg.R2);  // Second operand (bits) -> R2
            PopR0();          // First operand (bits) -> R0

            bool isDouble = (entry1.Kind == EvalStackKind.Float64 || entry2.Kind == EvalStackKind.Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.MulsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.MulssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                PushR0(EvalStackEntry.Float32);
            }
        }
        else
        {
            // Integer multiply
            PopReg(VReg.R2);  // Second operand -> R2
            PopR0();          // First operand -> R0
            X64Emitter.ImulRR(ref _code, VReg.R0, VReg.R2);
            PushR0(EvalStackEntry.NativeInt);
        }
        return true;
    }

    /// <summary>
    /// Check if a value is a power of 2 (and > 0).
    /// </summary>
    private static bool IsPowerOf2(ulong value)
    {
        return value != 0 && (value & (value - 1)) == 0;
    }

    /// <summary>
    /// Get the log base 2 of a power of 2 value (bit position).
    /// </summary>
    private static int Log2(ulong value)
    {
        int result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }
        return result;
    }

    // Overflow-checking arithmetic
    // For signed: check OF (overflow flag) with JO (0x70)
    // For unsigned: check CF (carry flag) with JC (0x72)

    /// <summary>
    /// Check if an eval stack entry represents a 32-bit integer (Int32 or a small ValueType).
    /// </summary>
    private bool IsInt32Like(EvalStackEntry entry)
    {
        // Explicit Int32
        if (entry.Kind == EvalStackKind.Int32)
            return true;
        // ValueType with 4-byte size (primitives like int loaded from locals)
        if (entry.Kind == EvalStackKind.ValueType && entry.RawSize == 4)
            return true;
        return false;
    }

    private bool CompileAddOvf(bool unsigned)
    {
        // Check operand types before popping - use 32-bit ops for Int32-like operands
        var entry1 = PeekEntryAt(1);  // First operand (deeper in stack)
        var entry2 = PeekEntry();     // Second operand (top of stack)
        bool use32Bit = IsInt32Like(entry1) && IsInt32Like(entry2);

        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand
        PopEntry(); PopEntry();

        if (use32Bit)
        {
            // 32-bit add sets OF correctly for 32-bit overflow
            X64Emitter.Add32RR(ref _code, VReg.R0, VReg.R2);
        }
        else
        {
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R2);
        }

        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(use32Bit ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileSubOvf(bool unsigned)
    {
        // Check operand types before popping - use 32-bit ops for Int32-like operands
        var entry1 = PeekEntryAt(1);  // First operand (deeper in stack)
        var entry2 = PeekEntry();     // Second operand (top of stack)
        bool use32Bit = IsInt32Like(entry1) && IsInt32Like(entry2);

        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand (subtrahend)
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand (minuend)
        PopEntry(); PopEntry();

        if (use32Bit)
        {
            // 32-bit sub sets OF correctly for 32-bit overflow
            X64Emitter.Sub32RR(ref _code, VReg.R0, VReg.R2);
        }
        else
        {
            X64Emitter.SubRR(ref _code, VReg.R0, VReg.R2);
        }

        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1 = borrow)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(use32Bit ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileMulOvf(bool unsigned)
    {
        // Check operand types before popping - use 32-bit ops for Int32-like operands
        var entry1 = PeekEntryAt(1);  // First operand (deeper in stack)
        var entry2 = PeekEntry();     // Second operand (top of stack)
        bool use32Bit = IsInt32Like(entry1) && IsInt32Like(entry2);

        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand
        PopEntry(); PopEntry();

        if (unsigned)
        {
            if (use32Bit)
            {
                // 32-bit unsigned mul: eax * edx -> edx:eax
                // Encoding: F7 /4 (mul r/m32, no REX.W)
                _code.EmitByte(0xF7);  // MUL
                _code.EmitByte(0xE2);  // ModRM: /4 edx
            }
            else
            {
                // 64-bit unsigned mul: rax * rdx -> rdx:rax
                // Encoding: REX.W + F7 /4 (mul r/m64)
                _code.EmitByte(0x48);  // REX.W
                _code.EmitByte(0xF7);  // MUL
                _code.EmitByte(0xE2);  // ModRM: /4 rdx
            }
            // mul sets CF=OF=1 if high half is non-zero
            EmitJccToInt3(0x72);  // JC overflow
        }
        else
        {
            if (use32Bit)
            {
                // 32-bit signed mul sets OF correctly for 32-bit overflow
                X64Emitter.Imul32RR(ref _code, VReg.R0, VReg.R2);
            }
            else
            {
                // 64-bit imul rax, rdx: signed, result in RAX, sets OF if overflow
                X64Emitter.ImulRR(ref _code, VReg.R0, VReg.R2);
            }
            EmitJccToInt3(0x70);  // JO overflow
        }

        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(use32Bit ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);
        return true;
    }

    /// <summary>
    /// Compile ckfinite - check if floating-point value is finite (not NaN or infinity).
    /// For doubles: NOT finite if exponent bits (52-62) == 0x7FF
    /// The value is left on the stack; throws ArithmeticException if not finite.
    /// </summary>
    private bool CompileCkfinite()
    {
        // Pop value into RAX (it stays on the logical stack, we just peek and check)
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // For double: exponent is bits 52-62 (11 bits)
        // Value is NOT finite if exponent == 0x7FF (all 1s)
        // Mask to extract exponent: 0x7FF0_0000_0000_0000
        // After shift right by 52: 0x7FF means not finite

        // mov rdx, rax  (save value)
        X64Emitter.MovRR(ref _code, VReg.R2, VReg.R0);

        // shr rax, 52  (shift to get exponent in low 11 bits)
        // REX.W + C1 /5 ib
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0xC1);  // SHR r/m64, imm8
        _code.EmitByte(0xE8);  // ModRM: /5 rax
        _code.EmitByte(52);    // shift by 52

        // and rax, 0x7FF  (mask to get just exponent bits, ignoring sign)
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x25);  // AND RAX, imm32
        _code.EmitByte(0xFF);  // 0x7FF
        _code.EmitByte(0x07);
        _code.EmitByte(0x00);
        _code.EmitByte(0x00);

        // cmp rax, 0x7FF  (if exponent == 0x7FF, value is not finite)
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x3D);  // CMP RAX, imm32
        _code.EmitByte(0xFF);  // 0x7FF
        _code.EmitByte(0x07);
        _code.EmitByte(0x00);
        _code.EmitByte(0x00);

        // JE -> INT3 (if equal, not finite, trigger exception)
        EmitJccToInt3(0x74);  // JE (ZF=1)

        // Restore original value and push back
        X64Emitter.Push(ref _code, VReg.R2);
        PushEntry(EvalStackEntry.Float64);
        return true;
    }

    private bool CompileNeg()
    {
        // Unary op: pop one, negate, push result
        EvalStackEntry entry = PeekEntry();

        if (IsFloatEntry(entry))
        {
            PopR0();
            if (entry.Kind == EvalStackKind.Float64)
            {
                // Double negation: XOR sign bit (bit 63)
                X64Emitter.MovRI64(ref _code, VReg.R2, 0x8000000000000000UL);
                X64Emitter.XorRR(ref _code, VReg.R0, VReg.R2);
                PushR0(EvalStackEntry.Float64);
            }
            else
            {
                // Float negation: XOR sign bit (bit 31)
                X64Emitter.MovRI64(ref _code, VReg.R2, 0x80000000UL);
                X64Emitter.XorRR(ref _code, VReg.R0, VReg.R2);
                PushR0(EvalStackEntry.Float32);
            }
        }
        else
        {
            // Integer negation
            PopR0();
            X64Emitter.Neg(ref _code, VReg.R0);
            PushR0(EvalStackEntry.NativeInt);
        }
        return true;
    }

    private bool CompileAnd()
    {
        // Binary op: pop two, AND, push result
        PopReg(VReg.R2);  // Second operand
        PopR0();          // First operand
        X64Emitter.AndRR(ref _code, VReg.R0, VReg.R2);
        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileOr()
    {
        // Binary op: pop two, OR, push result
        PopReg(VReg.R2);  // Second operand
        PopR0();          // First operand
        X64Emitter.OrRR(ref _code, VReg.R0, VReg.R2);
        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileXor()
    {
        // Binary op: pop two, XOR, push result
        PopReg(VReg.R2);  // Second operand
        PopR0();          // First operand
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R2);
        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileNot()
    {
        // Unary op: pop one, NOT, push result
        PopR0();
        X64Emitter.Not(ref _code, VReg.R0);
        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileDiv(bool signed)
    {
        // Division: dividend / divisor
        // IL stack: [..., dividend, divisor] -> [..., quotient]
        EvalStackEntry entry2 = PeekEntryAt(0);  // Top (divisor)
        EvalStackEntry entry1 = PeekEntryAt(1);  // Second from top (dividend)
        PopEntry();  // entry2
        PopEntry();  // entry1

        if (IsFloatEntry(entry1) || IsFloatEntry(entry2))
        {
            // Float division - use SSE
            X64Emitter.Pop(ref _code, VReg.R1);  // Divisor (bits)
            X64Emitter.Pop(ref _code, VReg.R0);  // Dividend (bits)

            bool isDouble = (entry1.Kind == EvalStackKind.Float64 || entry2.Kind == EvalStackKind.Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R1);
                X64Emitter.DivsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R1);
                X64Emitter.DivssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float32);
            }
        }
        else
        {
            // Integer division
            X64Emitter.Pop(ref _code, VReg.R1);  // Divisor to RCX (preserving RDX)
            X64Emitter.Pop(ref _code, VReg.R0);  // Dividend to RAX

            if (signed)
            {
                // Sign-extend RAX into RDX:RAX
                X64Emitter.Cqo(ref _code);
                // Signed divide
                X64Emitter.IdivR(ref _code, VReg.R1);
            }
            else
            {
                // For unsigned 32-bit division, we need to zero-extend the operands
                // to ensure they're treated as unsigned 32-bit values, not sign-extended 64-bit
                X64Emitter.ZeroExtend32(ref _code, VReg.R0);
                X64Emitter.ZeroExtend32(ref _code, VReg.R1);
                // Zero RDX for unsigned division
                X64Emitter.XorRR(ref _code, VReg.R2, VReg.R2);
                // Unsigned divide
                X64Emitter.DivR(ref _code, VReg.R1);
            }

            // Quotient is in RAX
            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(EvalStackEntry.NativeInt);
        }
        return true;
    }

    private bool CompileRem(bool signed)
    {
        // Remainder: dividend % divisor
        // IL stack: [..., dividend, divisor] -> [..., remainder]
        X64Emitter.Pop(ref _code, VReg.R1);  // Divisor to RCX
        X64Emitter.Pop(ref _code, VReg.R0);  // Dividend to RAX
        PopEntry(); PopEntry();

        if (signed)
        {
            X64Emitter.Cqo(ref _code);
            X64Emitter.IdivR(ref _code, VReg.R1);
        }
        else
        {
            // For unsigned 32-bit remainder, zero-extend operands
            X64Emitter.ZeroExtend32(ref _code, VReg.R0);
            X64Emitter.ZeroExtend32(ref _code, VReg.R1);
            X64Emitter.XorRR(ref _code, VReg.R2, VReg.R2);
            X64Emitter.DivR(ref _code, VReg.R1);
        }

        // Remainder is in RDX
        X64Emitter.Push(ref _code, VReg.R2);
        PushEntry(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileShl()
    {
        // Shift left: value << shiftAmount
        // IL stack: [..., value, shiftAmount] -> [..., result]
        PopReg(VReg.R1);  // Shift amount to CL (part of RCX)
        PopR0();          // Value to shift
        X64Emitter.ShlCL(ref _code, VReg.R0);
        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileShr(bool signed)
    {
        // Shift right: value >> shiftAmount
        // IL stack: [..., value, shiftAmount] -> [..., result]

        // Check the type of the value being shifted (second from top, under shiftAmount)
        EvalStackEntry valueEntry = PeekEntryAt(1);
        bool is32Bit = (valueEntry.Kind == EvalStackKind.Int32);

        PopReg(VReg.R1);  // Shift amount to CL (part of RCX)
        PopR0();          // Value to shift

        if (signed)
        {
            if (is32Bit)
                X64Emitter.ShiftRightSigned32(ref _code, VReg.R0, VReg.R1);
            else
                X64Emitter.SarCL(ref _code, VReg.R0);  // Arithmetic shift (preserves sign)
        }
        else
        {
            if (is32Bit)
                X64Emitter.ShiftRightUnsigned32(ref _code, VReg.R0, VReg.R1);
            else
                X64Emitter.ShrCL(ref _code, VReg.R0);  // Logical shift (zero-fill)
        }

        // Result type matches input type
        PushR0(is32Bit ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileConv(int targetBytes, bool signed)
    {
        // Convert top of stack to different size
        // Check source type to determine proper extension
        EvalStackEntry srcEntry = PeekEntryAt(0);
        bool srcIs32Bit = (srcEntry.Kind == EvalStackKind.Int32);
        bool srcIsFloat = IsFloatEntry(srcEntry);

        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Handle float-to-integer conversion
        if (srcIsFloat && targetBytes <= 8)
        {
            bool srcIsDouble = (srcEntry.Kind == EvalStackKind.Float64);
            bool targetIs64 = (targetBytes == 8);

            if (srcIsDouble)
            {
                // CVTTSD2SI - Convert double to integer with truncation
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.Cvttsd2si(ref _code, VReg.R0, RegXMM.XMM0, targetIs64);
            }
            else
            {
                // CVTTSS2SI - Convert float to integer with truncation
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.Cvttss2si(ref _code, VReg.R0, RegXMM.XMM0, targetIs64);
            }

            // Sign/zero extend to proper size if needed
            if (targetBytes < 4)
            {
                if (signed)
                {
                    if (targetBytes == 1)
                    {
                        // MOVSX RAX, AL
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0xBE);
                        _code.EmitByte(0xC0);
                    }
                    else // targetBytes == 2
                    {
                        // MOVSX RAX, AX
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0xBF);
                        _code.EmitByte(0xC0);
                    }
                }
                else
                {
                    if (targetBytes == 1)
                    {
                        // MOVZX EAX, AL
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0xB6);
                        _code.EmitByte(0xC0);
                    }
                    else // targetBytes == 2
                    {
                        // MOVZX EAX, AX
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0xB7);
                        _code.EmitByte(0xC0);
                    }
                }
            }

            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(targetBytes <= 4 ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);
            return true;
        }

        switch (targetBytes)
        {
            case 1:
                if (signed)
                {
                    // MOVSX RAX, AL - sign-extend byte to qword
                    _code.EmitByte(0x48);  // REX.W
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xBE);
                    _code.EmitByte(0xC0);  // ModRM: RAX, AL
                }
                else
                {
                    // MOVZX EAX, AL - zero-extend byte (clears upper bits)
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xB6);
                    _code.EmitByte(0xC0);  // ModRM: EAX, AL
                }
                break;

            case 2:
                if (signed)
                {
                    // MOVSX RAX, AX - sign-extend word to qword
                    _code.EmitByte(0x48);  // REX.W
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xBF);
                    _code.EmitByte(0xC0);  // ModRM: RAX, AX
                }
                else
                {
                    // MOVZX EAX, AX - zero-extend word
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xB7);
                    _code.EmitByte(0xC0);  // ModRM: EAX, AX
                }
                break;

            case 4:
                if (signed)
                {
                    // MOVSXD RAX, EAX - sign-extend dword to qword
                    _code.EmitByte(0x48);  // REX.W
                    _code.EmitByte(0x63);
                    _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                }
                else
                {
                    // MOV EAX, EAX - writing to 32-bit reg zeros upper 32 bits
                    _code.EmitByte(0x89);
                    _code.EmitByte(0xC0);  // ModRM: EAX, EAX
                }
                break;

            case 8:
                // conv.i8/conv.i (signed): sign-extend from 32-bit to 64-bit if needed
                // conv.u8/conv.u (unsigned): zero-extend from 32-bit to 64-bit if needed
                if (srcIs32Bit)
                {
                    if (signed)
                    {
                        // MOVSXD RAX, EAX - sign-extend dword to qword
                        _code.EmitByte(0x48);  // REX.W
                        _code.EmitByte(0x63);
                        _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                    }
                    else
                    {
                        // conv.u8 from Int32: zero-extend by clearing upper 32 bits
                        // MOV EAX, EAX - writing to 32-bit reg zeros upper 32 bits
                        _code.EmitByte(0x89);
                        _code.EmitByte(0xC0);  // ModRM: EAX, EAX
                    }
                }
                // For 64-bit source: no-op, preserve full value
                break;
        }

        X64Emitter.Push(ref _code, VReg.R0);
        // Determine result type based on target size
        EvalStackEntry resultType = targetBytes <= 4 ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt;
        PushEntry(resultType);
        return true;
    }

    /// <summary>
    /// Compile overflow-checking conversions (conv.ovf.* opcodes).
    /// If the value doesn't fit in the target type, triggers INT3 (debug break).
    /// </summary>
    private bool CompileConvOvf(int targetBytes, bool targetSigned, bool sourceUnsigned)
    {
        // Pop value from stack
        X64Emitter.Pop(ref _code, VReg.R0);

        // We need to check if the value fits in the target range
        // then do the conversion.
        //
        // For each target type, we need range checks:
        // - conv.ovf.i1:  -128 to 127
        // - conv.ovf.u1:  0 to 255
        // - conv.ovf.i2:  -32768 to 32767
        // - conv.ovf.u2:  0 to 65535
        // - conv.ovf.i4:  -2147483648 to 2147483647
        // - conv.ovf.u4:  0 to 4294967295
        // - conv.ovf.i8:  signed 64-bit (no overflow possible from signed source)
        // - conv.ovf.u8:  value must be >= 0 (no overflow from unsigned source)
        //
        // The .un suffix indicates the SOURCE is treated as unsigned.

        // Strategy: Compare against bounds and jump to INT3 on overflow
        // For simplicity, we use JO (jump on overflow) where possible,
        // or explicit range comparisons.

        switch (targetBytes)
        {
            case 1:
                if (targetSigned)
                {
                    // conv.ovf.i1: range -128..127
                    // Check: cmp rax, -128; jl overflow
                    // Check: cmp rax, 127; jg overflow
                    X64Emitter.CmpRI(ref _code, VReg.R0, -128);
                    EmitJccToInt3(0x7C);  // JL overflow
                    X64Emitter.CmpRI(ref _code, VReg.R0, 127);
                    EmitJccToInt3(0x7F);  // JG overflow

                    // Sign-extend AL to RAX
                    _code.EmitByte(0x48);
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xBE);
                    _code.EmitByte(0xC0);
                }
                else
                {
                    // conv.ovf.u1: range 0..255
                    if (!sourceUnsigned)
                    {
                        // Check for negative: test rax, rax; js overflow
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x85);
                        _code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Check: cmp rax, 255; ja overflow
                    X64Emitter.CmpRI(ref _code, VReg.R0, 255);
                    EmitJccToInt3(0x77);  // JA overflow

                    // Zero-extend AL to RAX
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xB6);
                    _code.EmitByte(0xC0);
                }
                break;

            case 2:
                if (targetSigned)
                {
                    // conv.ovf.i2: range -32768..32767
                    X64Emitter.CmpRI(ref _code, VReg.R0, -32768);
                    EmitJccToInt3(0x7C);  // JL overflow
                    X64Emitter.CmpRI(ref _code, VReg.R0, 32767);
                    EmitJccToInt3(0x7F);  // JG overflow

                    // Sign-extend AX to RAX
                    _code.EmitByte(0x48);
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xBF);
                    _code.EmitByte(0xC0);
                }
                else
                {
                    // conv.ovf.u2: range 0..65535
                    if (!sourceUnsigned)
                    {
                        // Check for negative
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x85);
                        _code.EmitByte(0xC0);
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    X64Emitter.CmpRI(ref _code, VReg.R0, 65535);
                    EmitJccToInt3(0x77);  // JA overflow

                    // Zero-extend AX to RAX
                    _code.EmitByte(0x0F);
                    _code.EmitByte(0xB7);
                    _code.EmitByte(0xC0);
                }
                break;

            case 4:
                if (targetSigned)
                {
                    // conv.ovf.i4: value must fit in int32
                    // Use MOVSXD to sign-extend, then compare back
                    // If original != sign-extended, overflow
                    X64Emitter.MovRR(ref _code, VReg.R2, VReg.R0);  // Save original
                    _code.EmitByte(0x48);
                    _code.EmitByte(0x63);
                    _code.EmitByte(0xC0);  // MOVSXD RAX, EAX
                    X64Emitter.CmpRR(ref _code, VReg.R0, VReg.R2);
                    EmitJccToInt3(0x75);  // JNE overflow
                }
                else
                {
                    // conv.ovf.u4: range 0..0xFFFFFFFF
                    if (!sourceUnsigned)
                    {
                        // Check for negative
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x85);
                        _code.EmitByte(0xC0);
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Check upper 32 bits are zero
                    X64Emitter.MovRI64(ref _code, VReg.R2, 0xFFFFFFFF00000000);
                    _code.EmitByte(0x48);
                    _code.EmitByte(0x85);
                    _code.EmitByte(0xD0);  // TEST RAX, RDX
                    EmitJccToInt3(0x75);  // JNE overflow

                    // Zero-extend EAX
                    _code.EmitByte(0x89);
                    _code.EmitByte(0xC0);
                }
                break;

            case 8:
                if (targetSigned)
                {
                    // conv.ovf.i8: if source is unsigned, it must be < 2^63
                    if (sourceUnsigned)
                    {
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x85);
                        _code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow (sign bit set means >= 2^63)
                    }
                    // Otherwise no overflow possible
                }
                else
                {
                    // conv.ovf.u8: value must be >= 0
                    if (!sourceUnsigned)
                    {
                        _code.EmitByte(0x48);
                        _code.EmitByte(0x85);
                        _code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Otherwise no overflow possible from unsigned source
                }
                break;
        }

        X64Emitter.Push(ref _code, VReg.R0);
        return true;
    }

    /// <summary>
    /// Emit a conditional jump to INT3 for overflow handling.
    /// Pattern: Jcc +2; JMP +1; INT3
    /// </summary>
    private void EmitJccToInt3(byte jccOpcode)
    {
        // Jcc skip (skip over INT3 if condition is FALSE)
        // We want to execute INT3 if condition is TRUE, so we invert the condition
        // Actually, easier: Jcc to INT3, then skip over it
        // Pattern: Jcc +0; JMP +1; INT3  -- no, that's wrong
        //
        // What we want:
        // - If overflow, INT3
        // - If no overflow, continue
        //
        // So: Jcc overflow_label; continue...; overflow_label: INT3
        // But INT3 is just 1 byte, so we can do:
        // Jcc +2 (jump to INT3 if overflow)
        // JMP +1 (skip INT3)
        // INT3
        //
        // Actually simpler: Use the forward condition directly
        // Jcc +1 means: if condition true, jump over next 1 byte (skip the jmp +1)
        // Wait, let me think...
        //
        // If condition means "overflow", we want:
        // Jcc not_overflow (+1 to skip INT3); INT3; continue
        //
        // But we're given the "overflow" condition opcode.
        // The simplest approach is:
        // Jcc_not skip_int3  (2 bytes: opcode + rel8)
        // INT3              (1 byte)
        // skip_int3:        continue
        //
        // But mapping overflow condition to not-overflow is tedious.
        // Let's just do: Jcc overflow_target (+1); JMP skip_int3 (+1); INT3; skip_int3: ...
        // No, that's wrong too.
        //
        // Simplest: Jcc +0 followed by... no.
        //
        // OK, let me be clear:
        // We have a condition that's true when there IS overflow.
        // We want to execute INT3 if overflow.
        // So: JNcc +1 (skip INT3 if NOT overflow); INT3
        // This requires inverting the condition.
        //
        // JL (0x7C) -> JGE (0x7D)
        // JG (0x7F) -> JLE (0x7E)
        // JS (0x78) -> JNS (0x79)
        // JA (0x77) -> JBE (0x76)
        // JNE (0x75) -> JE (0x74)
        //
        // The pattern: toggle the low bit of the opcode

        byte invertedJcc = (byte)(jccOpcode ^ 1);
        _code.EmitByte(invertedJcc);  // Jcc_not (skip INT 4)
        _code.EmitByte(2);            // rel8 = +2 (skip 2 bytes for INT 4)
        _code.EmitByte(0xCD);         // INT imm8
        _code.EmitByte(0x04);         // 4 = overflow interrupt (throws OverflowException)
    }

    private bool CompileLdcI8(long value)
    {
        // Load 64-bit constant
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)value);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileLdcR4(uint bits)
    {
        // Load float constant - store bit pattern and push to stack
        // Float is 4 bytes, but we push 8 bytes (zero-extended)
        X64Emitter.MovRI32(ref _code, VReg.R0, (int)bits);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Float32);
        return true;
    }

    private bool CompileLdcR8(ulong bits)
    {
        // Load double constant - push 64-bit pattern to stack
        X64Emitter.MovRI64(ref _code, VReg.R0, bits);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Float64);
        return true;
    }

    private bool CompileConvR4()
    {
        // Convert to float (single precision)
        EvalStackEntry srcEntry = PeekEntry();
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        if (srcEntry.Kind == EvalStackKind.Float64)
        {
            // Double to float: CVTSD2SS
            X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
            X64Emitter.Cvtsd2ssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM0);
            X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }
        else if (srcEntry.Kind == EvalStackKind.Float32)
        {
            // Float to float: no conversion needed
        }
        else
        {
            // Integer to float: CVTSI2SS
            // Use 32-bit source for proper signed int32 semantics
            X64Emitter.Cvtsi2ssXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
            X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }

        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Float32);
        return true;
    }

    private bool CompileConvR8()
    {
        // Convert to double precision
        EvalStackEntry srcEntry = PeekEntry();
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        if (srcEntry.Kind == EvalStackKind.Float32)
        {
            // Float to double: CVTSS2SD
            X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
            X64Emitter.Cvtss2sdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM0);
            X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }
        else if (srcEntry.Kind == EvalStackKind.Float64)
        {
            // Double to double: no conversion needed
        }
        else
        {
            // Integer to double: CVTSI2SD
            // Use 32-bit source for proper signed int32 semantics
            X64Emitter.Cvtsi2sdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
            X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }

        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Float64);
        return true;
    }

    private bool CompileConvR_Un()
    {
        // Convert unsigned integer to float (F - the native float type, which is double for IL)
        // This is tricky because CVTSI2SD only handles signed integers.
        // For unsigned 64-bit integers with the high bit set, we need special handling.
        //
        // Algorithm:
        // 1. Test if high bit is set (value is negative as signed)
        // 2. If not set, use normal CVTSI2SD (value fits in signed range)
        // 3. If set: shift right by 1, convert, then double the result and add 1 if odd
        //
        // Simplified approach for naive JIT: use the following technique:
        //   If (value >= 0 as signed): CVTSI2SD directly
        //   Else: Convert as (value >> 1) | (value & 1), multiply by 2
        //
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // TEST rax, rax (check sign bit)
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);

        // JS label_negative (jump if sign bit set)
        int patchNeg = X64Emitter.JccRel32(ref _code, X64Emitter.CC_S);

        // Positive path: simple convert
        X64Emitter.Cvtsi2sdXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
        // JMP done
        int patchDone = X64Emitter.JmpRel32(ref _code);

        // Patch jump to negative path
        X64Emitter.PatchRel32(ref _code, patchNeg);

        // Negative path (high bit set):
        // Save lowest bit in RCX: mov rcx, rax; and rcx, 1
        X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);
        X64Emitter.AndImm(ref _code, VReg.R1, 1);

        // Shift right by 1: shr rax, 1
        X64Emitter.ShrImm8(ref _code, VReg.R0, 1);

        // OR in the lowest bit to avoid losing precision: or rax, rcx
        X64Emitter.OrRR(ref _code, VReg.R0, VReg.R1);

        // Convert shifted value
        X64Emitter.Cvtsi2sdXmmR64(ref _code, RegXMM.XMM0, VReg.R0);

        // Double the result: addsd xmm0, xmm0
        X64Emitter.AddsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM0);

        // Patch done label
        X64Emitter.PatchRel32(ref _code, patchDone);

        // Move result to integer register for stack
        X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Float64);
        return true;
    }

    private bool CompileLdnull()
    {
        // Load null reference (0)
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ObjRef);
        return true;
    }

    private bool CompileStarg(int index)
    {
        // Pop from eval stack, store to argument home location
        PopR0();
        // Adjust for hidden buffer parameter (see CompileLdarg for explanation)
        int physicalArgIndex = index;
        if (_returnIsValueType && _returnTypeSize > 16)
        {
            physicalArgIndex = index + 1;
        }

        int offset = 16 + physicalArgIndex * 8;  // Shadow space offset
        X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
        return true;
    }

    private bool CompileLdarga(int index)
    {
        // Load address of argument

        // Adjust for hidden buffer parameter (see CompileLdarg for explanation)
        int physicalArgIndex = index;
        if (_returnIsValueType && _returnTypeSize > 16)
        {
            physicalArgIndex = index + 1;
        }

        // Check if this is a large value type argument (>8 bytes)
        // For large VTs, the arg slot contains a POINTER to the struct data (passed by caller)
        // So ldarga should return that pointer value directly, not the address of the slot
        bool isLargeVT = false;
        ushort argSize = 0;
        if (_argIsValueType != null && index >= 0 && index < _argCount && _argIsValueType[index])
        {
            argSize = (_argTypeSize != null && index < _argCount) ? _argTypeSize[index] : (ushort)0;
            if (argSize > 8)
                isLargeVT = true;
        }

        int offset = 16 + physicalArgIndex * 8;
        if (isLargeVT)
        {
            // Large VT: arg slot contains pointer to struct - load the pointer value
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
        }
        else
        {
            // Small VT or ref type: return address of the arg slot
            X64Emitter.Lea(ref _code, VReg.R0, VReg.FP, offset);
        }
        PushR0(EvalStackEntry.NativeInt);  // Address is treated as integer
        return true;
    }

    private bool CompileLdloca(int index)
    {
        // Load address of local variable
        int offset = X64Emitter.GetLocalOffset(index);

        // Debug: trace ldloca in TestDictForeach (token 0x060002E7)
        bool isDebugMethod = _debugAssemblyId == 6 && _debugMethodToken == 0x060002E7;
        if (isDebugMethod)
        {
            DebugConsole.Write("[DF-ldloca] idx=");
            DebugConsole.WriteDecimal((uint)index);
            DebugConsole.Write(" off=");
            DebugConsole.WriteDecimal((uint)(offset < 0 ? -offset : offset));
            DebugConsole.Write(offset < 0 ? "(-)" : "(+)");
            DebugConsole.Write(" codePos=");
            DebugConsole.WriteHex((ulong)_code.Position);
            DebugConsole.WriteLine();
        }

        // Capture position before LEA
        int leaStart = _code.Position;
        X64Emitter.Lea(ref _code, VReg.R0, VReg.FP, offset);
        int leaEnd = _code.Position;

        // Debug: dump LEA bytes for TestDictForeach
        if (isDebugMethod)
        {
            DebugConsole.Write("[DF-LEA] bytes: ");
            byte* codePtr = _code.Code;
            for (int i = leaStart; i < leaEnd; i++)
            {
                DebugConsole.WriteHex((ulong)codePtr[i]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
        }

        // Capture position before PUSH
        int pushStart = _code.Position;
        X64Emitter.Push(ref _code, VReg.R0);
        int pushEnd = _code.Position;

        // Debug: dump PUSH bytes for TestDictForeach
        if (isDebugMethod)
        {
            DebugConsole.Write("[DF-PUSH] bytes: ");
            byte* codePtr = _code.Code;
            for (int i = pushStart; i < pushEnd; i++)
            {
                DebugConsole.WriteHex((ulong)codePtr[i]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
        }

        PushEntry(EvalStackEntry.NativeInt);  // Address is treated as integer
        return true;
    }

    private bool CompileRet()
    {
        // if (IsDebugBoolBugMethod())
        // {
        //     DebugConsole.Write("[BindDbg] ret il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.Write(" returnVT=");
        //     DebugConsole.WriteDecimal(_returnIsValueType ? 1U : 0U);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal(_returnTypeSize);
        //     DebugConsole.WriteLine();
        // }

        // If there's a return value, it should be on eval stack
        if (_evalStackDepth > 0)
        {
            // Check if returning a large value type (>8 bytes)
            // For large structs, TOS contains the ADDRESS of the value, not the value itself
            if (_returnIsValueType && _returnTypeSize > 8)
            {
                // Use new type system: PeekEntry tells us exact byte size on stack
                EvalStackEntry tosEntry = PeekEntry();
                int stackSize = tosEntry.ByteSize;  // Actual bytes this entry occupies

                // If ByteSize seems wrong (8 for multi-byte struct), fall back to metadata
                if (stackSize <= 8 && _returnTypeSize > 8)
                {
                    int alignedSize = (_returnTypeSize + 7) & ~7;
                    stackSize = _returnTypeSize > 16 ? ((alignedSize + 15) & ~15) : alignedSize;
                }

                if (_returnTypeSize <= 16)
                {
                    // Medium struct (9-16 bytes): value is sitting on the stack
                    // Load first 8 bytes to RAX and second 8 (if present) to RDX
                    X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);
                    if (_returnTypeSize > 8)
                        X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, 8);

                    // Pop the struct value from the eval stack
                    X64Emitter.AddRI(ref _code, VReg.SP, stackSize);
                    PopEntry();  // ONE entry, not multiple slots
                }
                else
                {
                    // Large struct (>16 bytes): copy VALUE from [RSP] to hidden buffer (arg0)
                    // DebugConsole.Write("[ret>16] size=");
                    // DebugConsole.WriteDecimal((uint)_returnTypeSize);
                    // DebugConsole.Write(" argCnt=");
                    // DebugConsole.WriteDecimal((uint)_argCount);
                    // DebugConsole.Write(" depth=");
                    // DebugConsole.WriteDecimal((uint)_evalStackDepth);
                    // DebugConsole.Write(" stackBytes=");
                    // DebugConsole.WriteDecimal((uint)stackSize);
                    // DebugConsole.WriteLine();

                    X64Emitter.LoadArg(ref _code, VReg.R5, 0);  // R5 = hidden buffer pointer

                    int bytesToCopy = _returnTypeSize;
                    int offset = 0;
                    while (bytesToCopy >= 8)
                    {
                        X64Emitter.MovRM(ref _code, VReg.R6, VReg.SP, offset);
                        X64Emitter.MovMR(ref _code, VReg.R5, offset, VReg.R6);
                        offset += 8;
                        bytesToCopy -= 8;
                    }
                    if (bytesToCopy >= 4)
                    {
                        X64Emitter.MovRM32(ref _code, VReg.R6, VReg.SP, offset);
                        X64Emitter.MovMR32(ref _code, VReg.R5, offset, VReg.R6);
                        offset += 4;
                        bytesToCopy -= 4;
                    }

                    // Pop the struct value (including any padding we reserved)
                    X64Emitter.AddRI(ref _code, VReg.SP, stackSize);
                    PopEntry();  // ONE entry represents entire struct in new type system

                    // Return the buffer address (same as what was passed by caller)
                    X64Emitter.MovRR(ref _code, VReg.R0, VReg.R5);  // RAX = buffer address
                }
            }
            else
            {
                // Small return value (<=8 bytes) or non-struct: just pop to RAX
                PopR0();  // Return value in RAX (uses TOS cache if available)
            }
        }

        // Emit epilogue
        X64Emitter.EmitEpilogue(ref _code, _stackAdjust);
        return true;
    }

    private bool CompileBr(int targetIL)
    {
        // bool debugBind = IsDebugBarMethod();
        // if (debugBind)
        // {
        //     // DebugConsole.Write("[BarDbg] br il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" target=0x");
        //     DebugConsole.WriteHex((ulong)targetIL);
        //     DebugConsole.Write(" codeOff=0x");
        //     DebugConsole.WriteHex((ulong)_code.Position);
        //     DebugConsole.WriteLine();
        // }
        int patchOffset = X64Emitter.JmpRel32(ref _code);
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBrfalse(int targetIL)
    {
        // bool debugBind = IsDebugBindMethod() || IsDebugBarMethod();
        // if (debugBind)
        // {
        //     DebugConsole.Write("[BindDbg] brfalse il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" target=0x");
        //     DebugConsole.WriteHex((ulong)targetIL);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }

        // Pop value, branch if zero
        PopR0();        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int patchOffset = X64Emitter.Je(ref _code);  // Jump if equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        // if (debugBind)
        // {
        //     DebugConsole.Write("[BindDbg] brfalse patchOff=0x");
        //     DebugConsole.WriteHex((ulong)patchOffset);
        //     DebugConsole.Write(" codePos=0x");
        //     DebugConsole.WriteHex((ulong)_code.Position);
        //     DebugConsole.WriteLine();
        // }
        return true;
    }

    private bool CompileBrtrue(int targetIL)
    {
        // Pop value, branch if non-zero
        PopR0();        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int patchOffset = X64Emitter.Jne(ref _code);  // Jump if not equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBranchCmp(byte cc, int targetIL)
    {
        // Peek at types to determine float vs int
        EvalStackEntry entry2 = PeekEntryAt(0);  // Top
        EvalStackEntry entry1 = PeekEntryAt(1);  // Second from top

        if (IsFloatEntry(entry1) || IsFloatEntry(entry2))
        {
            // Float comparison
            PopReg(VReg.R2);  // Second operand
            PopR0();          // First operand

            bool isDouble = (entry1.Kind == EvalStackKind.Float64 || entry2.Kind == EvalStackKind.Float64);

            if (isDouble)
            {
                // COMISD xmm0, xmm1
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.ComisdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
            }
            else
            {
                // COMISS xmm0, xmm1
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.ComissXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
            }

            // COMISS/COMISD set CF and ZF (not SF/OF), so we need to translate
            // signed condition codes to unsigned equivalents for floats
            cc = TranslateConditionCodeForFloat(cc);
        }
        else
        {
            // Pop two values, compare, branch based on condition
            PopReg(VReg.R2);  // Second operand
            PopR0();  // First operand
            // Use 32-bit comparison for proper signed int32 semantics
            // This ensures -5 < 0 works correctly (64-bit would see 0x00000000FFFFFFFB as positive)
            X64Emitter.CmpRR32(ref _code, VReg.R0, VReg.R2);
        }

        int patchOffset = X64Emitter.JccRel32(ref _code, cc);
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    /// <summary>
    /// Translate signed integer condition codes to float comparison codes.
    /// COMISS/COMISD set CF/ZF (like unsigned compare), not SF/OF.
    /// </summary>
    private static byte TranslateConditionCodeForFloat(byte cc)
    {
        // Signed integer uses SF/OF, float uses CF/ZF
        // CC_L (less, SF!=OF)     -> CC_B (below, CF=1)
        // CC_LE (less/equal)      -> CC_BE (below/equal, CF=1 or ZF=1)
        // CC_G (greater, SF=OF)   -> CC_A (above, CF=0 and ZF=0)
        // CC_GE (greater/equal)   -> CC_AE (above/equal, CF=0)
        // CC_E, CC_NE - same (use ZF)
        // CC_B, CC_BE, CC_A, CC_AE - already correct
        return cc switch
        {
            X64Emitter.CC_L => X64Emitter.CC_B,
            X64Emitter.CC_LE => X64Emitter.CC_BE,
            X64Emitter.CC_G => X64Emitter.CC_A,
            X64Emitter.CC_GE => X64Emitter.CC_AE,
            _ => cc  // E, NE, B, BE, A, AE already correct
        };
    }

    private bool CompileCeq()
    {
        // Compare equal: pop two values, push 1 if equal, 0 otherwise

        // Debug: Log ceq for the bool bug method - check IL offset
        bool debugThis = IsDebugBoolBugMethod();
        if (debugThis)
        {
            // DebugConsole.Write("[ceq-pre] IL=0x");
            DebugConsole.WriteHex((ulong)_ilOffset);
            DebugConsole.Write(" depth=");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.WriteLine();
        }

        PopReg(VReg.R2);  // Second operand
        PopR0();  // First operand

        if (debugThis)
        {
            // DebugConsole.Write("[ceq-post] depth=");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.WriteLine();
        }

        X64Emitter.CmpRR(ref _code, VReg.R0, VReg.R2);

        // SETE sets AL to 1 if equal, 0 otherwise
        // setcc r/m8: 0F 94 /0 (mod=11, reg=0, r/m=AL)
        _code.EmitByte(0x0F);
        _code.EmitByte(0x94);
        _code.EmitByte(0xC0);  // ModRM: mod=11, reg=0, r/m=0 (AL)

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x0F);
        _code.EmitByte(0xB6);
        _code.EmitByte(0xC0);  // ModRM: mod=11, reg=RAX, r/m=AL

        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileCgt(bool signed)
    {
        PopReg(VReg.R2);  // Second operand
        PopR0();  // First operand
        // Use 32-bit comparison for proper signed int32 semantics
        X64Emitter.CmpRR32(ref _code, VReg.R0, VReg.R2);

        // SETG (signed) or SETA (unsigned)
        _code.EmitByte(0x0F);
        _code.EmitByte(signed ? (byte)0x9F : (byte)0x97);  // SETG / SETA
        _code.EmitByte(0xC0);  // AL

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _code.EmitByte(0x48);
        _code.EmitByte(0x0F);
        _code.EmitByte(0xB6);
        _code.EmitByte(0xC0);

        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileClt(bool signed)
    {
        PopReg(VReg.R2);  // Second operand
        PopR0();  // First operand
        // Use 32-bit comparison for proper signed int32 semantics
        X64Emitter.CmpRR32(ref _code, VReg.R0, VReg.R2);

        // SETL (signed) or SETB (unsigned)
        _code.EmitByte(0x0F);
        _code.EmitByte(signed ? (byte)0x9C : (byte)0x92);  // SETL / SETB
        _code.EmitByte(0xC0);  // AL

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _code.EmitByte(0x48);
        _code.EmitByte(0x0F);
        _code.EmitByte(0xB6);
        _code.EmitByte(0xC0);

        PushR0(EvalStackEntry.NativeInt);
        return true;
    }

    private bool CompileSwitch()
    {
        // switch instruction format:
        // 0x45 (opcode, already consumed)
        // uint32 n - number of targets
        // n x int32 offsets - relative to end of switch instruction
        //
        // Pop value from stack, if value < n, jump to targets[value]
        // Otherwise fall through to next instruction

        // Read number of targets
        uint n = *(uint*)(_il + _ilOffset);
        _ilOffset += 4;

        // Calculate IL offset at end of switch (where offsets are relative to)
        int switchEndIL = _ilOffset + (int)(n * 4);

        // Pop value into RAX
        PopR0();

        // Compare value with n: if value >= n, fall through (jump past all cases)
        X64Emitter.CmpRI(ref _code, VReg.R0, (int)n);
        int fallThroughPatch = X64Emitter.JccRel32(ref _code, X64Emitter.CC_AE);  // JAE (unsigned >=)

        // For each target, emit: cmp rax, i; je target[i]
        // This is naive but correct; a jump table would be faster
        for (uint i = 0; i < n; i++)
        {
            int targetOffset = *(int*)(_il + _ilOffset);
            _ilOffset += 4;

            int targetIL = switchEndIL + targetOffset;

            // Compare RAX with case index i
            X64Emitter.CmpRI(ref _code, VReg.R0, (int)i);

            // Jump if equal to this case
            int patchOffset = X64Emitter.JccRel32(ref _code, X64Emitter.CC_E);
            RecordBranch(_ilOffset, targetIL, patchOffset);
        }

        // Fall-through case: patch the fall-through jump to here
        _code.PatchRelative32(fallThroughPatch);

        return true;
    }

    /// <summary>
    /// Compile ldind.* - Load value indirectly from address on stack
    /// Stack: ..., addr -> ..., value
    /// </summary>
    private bool CompileLdind(int size, bool signExtend)
    {
        // Pop address from stack into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Load value from [RAX] into RAX
        // mov rax, [rax] (with appropriate size)
        switch (size)
        {
            case 1:
                if (signExtend)
                {
                    // movsx rax, byte [rax] - sign extend byte to 64-bit
                    X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.R0, 0);
                }
                else
                {
                    // movzx eax, byte [rax] - zero extend byte (clears upper 32 bits)
                    X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, 0);
                }
                break;

            case 2:
                if (signExtend)
                {
                    // movsx rax, word [rax] - sign extend word to 64-bit
                    X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.R0, 0);
                }
                else
                {
                    // movzx eax, word [rax] - zero extend word (clears upper 32 bits)
                    X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, 0);
                }
                break;

            case 4:
                if (signExtend)
                {
                    // movsxd rax, dword [rax] - sign extend dword to 64-bit
                    X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.R0, 0);
                }
                else
                {
                    // mov eax, [rax] - zero extend dword (clears upper 32 bits)
                    X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, 0);
                }
                break;

            case 8:
                // mov rax, [rax] - full 64-bit load
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
                break;

            default:
                return false;
        }

        // Push result back onto stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(size <= 4 ? EvalStackEntry.Int32 : EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// Compile stind.* - Store value indirectly to address on stack
    /// Stack: ..., addr, value -> ...
    /// </summary>
    private bool CompileStind(int size)
    {
        // Pop value into RDX
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Pop address into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Store value to [RAX] with appropriate size
        switch (size)
        {
            case 1:
                // mov byte [rax], dl
                X64Emitter.MovMR8(ref _code, VReg.R0, 0, VReg.R2);
                break;

            case 2:
                // mov word [rax], dx
                X64Emitter.MovMR16(ref _code, VReg.R0, 0, VReg.R2);
                break;

            case 4:
                // mov dword [rax], edx
                X64Emitter.MovMR32(ref _code, VReg.R0, 0, VReg.R2);
                break;

            case 8:
                // mov qword [rax], rdx
                X64Emitter.MovMR(ref _code, VReg.R0, 0, VReg.R2);
                break;

            default:
                return false;
        }

        return true;
    }

    /// <summary>
    /// Compile ldind.r4/ldind.r8 - Load float value indirectly from address on stack
    /// Stack: ..., addr -> ..., value
    /// </summary>
    private bool CompileLdindFloat(int size)
    {
        // Pop address from stack into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Load float value from [RAX] into XMM0
        if (size == 4)
        {
            // movss xmm0, [rax] - load float32
            X64Emitter.MovssRM(ref _code, RegXMM.XMM0, VReg.R0, 0);
            // movd eax, xmm0 - move 32 bits to integer register
            X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }
        else // size == 8
        {
            // movsd xmm0, [rax] - load float64
            X64Emitter.MovsdRM(ref _code, RegXMM.XMM0, VReg.R0, 0);
            // movq rax, xmm0 - move 64 bits to integer register
            X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }

        // Push result back onto stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(size == 4 ? EvalStackEntry.Float32 : EvalStackEntry.Float64);

        return true;
    }

    /// <summary>
    /// Compile stind.r4/stind.r8 - Store float value indirectly to address on stack
    /// Stack: ..., addr, value -> ...
    /// </summary>
    private bool CompileStindFloat(int size)
    {
        // Pop value into RDX
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Pop address into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Move value to XMM0 and store to memory
        if (size == 4)
        {
            // movd xmm0, edx - move 32 bits to XMM
            X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R2);
            // movss [rax], xmm0 - store float32
            X64Emitter.MovssMR(ref _code, VReg.R0, 0, RegXMM.XMM0);
        }
        else // size == 8
        {
            // movq xmm0, rdx - move 64 bits to XMM
            X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R2);
            // movsd [rax], xmm0 - store float64
            X64Emitter.MovsdMR(ref _code, VReg.R0, 0, RegXMM.XMM0);
        }

        return true;
    }

    // ==================== Method Call Support ====================

    /// <summary>
    /// Resolve a method token to its native address and signature info.
    /// Uses custom resolver if provided, otherwise falls back to CompiledMethodRegistry.
    /// </summary>
    private bool ResolveMethod(uint token, out ResolvedMethod result)
    {
        result = default;

        // Try custom resolver first
        if (_resolver != null)
        {
            return _resolver(token, out result);
        }

        // Fall back to CompiledMethodRegistry
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(token);
        if (info == null)
        {
            // DebugConsole.Write("[JIT] Method token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(" not found in registry");
            return false;
        }

        // Fill in method information from registry
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

        if (info->IsCompiled)
        {
            // Method is fully compiled - use direct call
            result.NativeCode = info->NativeCode;
            result.RegistryEntry = null;
        }
        else if (info->IsBeingCompiled)
        {
            // Method is being compiled (recursive call) - use indirect call through registry
            // The NativeCode will be filled in by the time the call actually executes
            result.NativeCode = null;
            result.RegistryEntry = info;
        }
        else
        {
            // Method is reserved but not being compiled - this shouldn't happen
            // DebugConsole.Write("[JIT] Method token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(" not yet compiled");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Compile a call instruction.
    /// Handles argument marshaling per Microsoft x64 ABI and return value handling.
    ///
    /// Microsoft x64 ABI:
    /// - First 4 integer/pointer args: RCX, RDX, R8, R9
    /// - Additional args: pushed right-to-left on stack
    /// - Caller must allocate 32-byte shadow space (already done in prologue)
    /// - Stack must be 16-byte aligned at call site
    /// - Return value: RAX (integer/pointer), XMM0 (float/double)
    ///
    /// IL eval stack before call: [..., arg0, arg1, ..., argN-1] (argN-1 on top)
    /// IL eval stack after call:  [..., returnValue] (if non-void)
    /// </summary>
    private bool CompileCall(uint token)
    {

        // DEBUG: Track stack depth before resolve
        int depthBefore = _evalStackDepth;

        // Resolve the method
        if (!ResolveMethod(token, out ResolvedMethod method))
        {
            // DebugConsole.Write("[JIT] ResolveMethod failed for call 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // DEBUG: Check if stack depth changed
        if (_evalStackDepth != depthBefore)
        {
            DebugConsole.Write("[JIT] CORRUPTION: eval stack depth changed from ");
            DebugConsole.WriteDecimal((uint)depthBefore);
            DebugConsole.Write(" to ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" after resolving 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
        }

        // DEBUG: Check if Activator detection worked
        if ((token >> 24) == 0x2B)  // MethodSpec token
        {
            DebugConsole.Write("[JIT] MethodSpec 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" IsActivator=");
            DebugConsole.Write(method.IsActivatorCreateInstance ? "true" : "false");
            DebugConsole.Write(" NativeCode=0x");
            DebugConsole.WriteHex((ulong)method.NativeCode);
            DebugConsole.WriteLine();
        }

        // Handle Activator.CreateInstance<T>() as JIT intrinsic
        if (method.IsActivatorCreateInstance)
        {
            return CompileActivatorCreateInstance((MethodTable*)method.ActivatorTypeArgMT);
        }

        // Handle RuntimeHelpers.InitializeArray as JIT intrinsic
        if (method.IsInitializeArray)
        {
            return CompileInitializeArray();
        }

        // Handle MD array methods (Get, Set, Address) as JIT intrinsic
        if (method.IsMDArrayMethod)
        {
            return CompileMDArrayMethod(method);
        }

        // Handle vararg calls with TypedReference entries
        // Note: Even when VarargCount == 0, we need to emit the sentinel TypedReference
        // Also handle the case when calling a vararg method with 0 varargs (direct MethodDef call)
        if (method.IsVarargCall || method.IsVarargMethod)
        {
            _tailPrefix = false;  // Clear prefix - vararg calls don't support tail call
            return CompileVarargCall(token, method);
        }

        // Handle tail call optimization for self-recursive calls
        bool isTailCall = _tailPrefix;
        _tailPrefix = false;  // Clear prefix - must be consumed

        if (isTailCall)
        {
            // Check if this is a self-recursive call (same method token)
            // We compare against _debugMethodToken which is set to the current method's token
            if (token == _debugMethodToken && !method.HasThis)
            {
                // Self-recursive tail call - optimize to a jump
                return CompileSelfRecursiveTailCall(method);
            }
            // For non-self-recursive tail calls, fall through to regular call
            // (full tail call optimization would require more complex stack manipulation)
        }

        int totalArgs = method.ArgCount;
        if (method.HasThis)
            totalArgs++;  // Instance methods have implicit 'this' as first arg

        // Debug: trace ALL calls from TestDictForeach method (token 0x060002E7)
        if (_debugAssemblyId == 6 && _debugMethodToken == 0x060002E7)
        {
            DebugConsole.Write("[DF-Call] il=0x");
            DebugConsole.WriteHex((ulong)(_ilOffset - 5));  // approx IL offset
            DebugConsole.Write(" tok=0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" hasThis=");
            DebugConsole.Write(method.HasThis ? "Y" : "N");
            DebugConsole.Write(" argc=");
            DebugConsole.WriteDecimal((uint)method.ArgCount);
            DebugConsole.Write(" total=");
            DebugConsole.WriteDecimal((uint)totalArgs);
            DebugConsole.Write(" ret=");
            DebugConsole.WriteDecimal((uint)method.ReturnKind);
            DebugConsole.Write(" depth=");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.WriteLine();

            // DEBUG: trace struct returns (like GetEnumerator)
            if (method.ReturnKind == ReturnKind.Struct)
            {
                DebugConsole.Write("[DF-StructRet] size=");
                DebugConsole.WriteDecimal((uint)method.ReturnStructSize);
                DebugConsole.Write(" needsHidden=");
                DebugConsole.Write(method.ReturnStructSize > 16 ? "Y" : "N");
                DebugConsole.WriteLine();
            }

            // DEBUG: For MoveNext call (hasThis=Y, argc=0, ret=Int32 for bool), trace the call
            if (method.HasThis && method.ArgCount == 0 && method.ReturnKind == ReturnKind.Int32)
            {
                DebugConsole.Write("[DF] MoveNext call at code offset 0x");
                DebugConsole.WriteHex((ulong)_code.Position);
                DebugConsole.WriteLine();
            }
        }

        // Check if method returns a large struct (>16 bytes) - needs hidden buffer parameter
        bool needsHiddenBuffer = method.ReturnKind == ReturnKind.Struct && method.ReturnStructSize > 16;
        int hiddenBufferSize = 0;
        if (needsHiddenBuffer)
        {
            // Align buffer size to 8 bytes
            hiddenBufferSize = (method.ReturnStructSize + 7) & ~7;
        }

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalArgs)
        {
            // DebugConsole.Write("[JIT] Call 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" at IL ");
            DebugConsole.WriteDecimal((uint)(_ilOffset - 4));  // -4 because token was already read
            DebugConsole.Write("/");
            DebugConsole.WriteDecimal((uint)_ilLength);
            DebugConsole.Write(": insufficient stack depth ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" for ");
            DebugConsole.WriteDecimal((uint)totalArgs);
            DebugConsole.Write(" args (ArgCount=");
            DebugConsole.WriteDecimal((uint)method.ArgCount);
            DebugConsole.Write(", HasThis=");
            DebugConsole.Write(method.HasThis ? "true" : "false");
            DebugConsole.WriteLine(")");
            return false;
        }

        // Calculate extra stack space needed for args beyond the first 4
        // The first 4 args go in registers (RCX, RDX, R8, R9)
        // Args 5+ go on the stack at [RSP+32], [RSP+40], etc.
        int stackArgs = totalArgs > 4 ? totalArgs - 4 : 0;

        // Pop arguments from eval stack into registers or temp storage
        // IL stack has args in order: arg0 at bottom, argN-1 at top
        // We need to pop in reverse order (top first)

        // Track whether we need to allocate shadow space
        // The x64 ABI ALWAYS requires 32 bytes of shadow space for the callee to home register args
        bool needsShadowSpace = true;

        // Track bytes of large struct args left on stack (need cleanup after call)
        int largeStructArgBytes = 0;

        // Track whether we need to set up RCX or RDX with struct pointer after shadow space allocation
        bool needsLargeStructPtrInRcx = false;
        bool needsLargeStructPtrInRdx = false;

        // Track special allocation for 4-args + hidden buffer case
        int fourArgsHiddenBufferCleanup = 0;

        if (totalArgs == 0 && !needsHiddenBuffer)
        {
            // No arguments - just call (shadow space allocated below)
        }
        else if (totalArgs == 0 && needsHiddenBuffer)
        {
            // No normal args but hidden buffer needed
            // Buffer address will be set up after shadow space allocation
        }
        else if (totalArgs == 1 && !needsHiddenBuffer)
        {
            // Single arg: check if it's a multi-slot value type using new type system
            EvalStackEntry argEntry = PeekEntry();

            // Debug: trace single-arg call in TestDictForeach
            bool isDebugDFMoveNext = _debugAssemblyId == 6 && _debugMethodToken == 0x060002E7 &&
                                     method.HasThis && method.ArgCount == 0;
            if (isDebugDFMoveNext)
            {
                DebugConsole.Write("[DF-Call1] argEntry.ByteSize=");
                DebugConsole.WriteDecimal((uint)argEntry.ByteSize);
                DebugConsole.Write(" kind=");
                DebugConsole.WriteDecimal((uint)argEntry.Kind);
                DebugConsole.Write(" codePos=");
                DebugConsole.WriteHex((ulong)_code.Position);
                DebugConsole.WriteLine();
            }

            if (argEntry.ByteSize > 8)
            {
                // Large struct: pass pointer to stack data in RCX
                // The struct is at [RSP] now, but after shadow space allocation it will be at [RSP + 32]
                // So we defer setting RCX until after shadow space is allocated
                // Track that we need to clean up the struct after the call
                largeStructArgBytes = argEntry.ByteSize;
                needsLargeStructPtrInRcx = true;
                // Don't pop the stack yet - callee needs access to the struct data
                // But do remove from tracking
                PopEntry();
            }
            else
            {
                // Single slot: pop to RCX
                int popStart = _code.Position;
                X64Emitter.Pop(ref _code, VReg.R1);
                int popEnd = _code.Position;

                // Debug: dump POP RCX bytes for TestDictForeach
                if (isDebugDFMoveNext)
                {
                    DebugConsole.Write("[DF-POP] bytes: ");
                    byte* codePtr = _code.Code;
                    for (int i = popStart; i < popEnd; i++)
                    {
                        DebugConsole.WriteHex((ulong)codePtr[i]);
                        DebugConsole.Write(" ");
                    }
                    DebugConsole.WriteLine();
                }

                PopEntry();
            }
        }
        else if (totalArgs == 1 && needsHiddenBuffer)
        {
            // One normal arg + hidden buffer: pop arg to RDX (shifted), buffer goes in RCX
            X64Emitter.Pop(ref _code, VReg.R2);   // arg0 -> RDX
            PopEntry();
        }
        else if (totalArgs == 2 && !needsHiddenBuffer)
        {
            // Two args: check if arg1 (TOS) is a large struct
            EvalStackEntry arg1Entry = PeekEntryAt(0);  // TOS = arg1
            EvalStackEntry arg0Entry = PeekEntryAt(1);  // TOS-1 = arg0

            if (arg1Entry.ByteSize > 8)
            {
                // arg1 is a large struct: pass by pointer
                // Stack layout: [RSP] = struct (24 bytes), [RSP+structSize] = arg0 (8 bytes)
                // After shadow space: RCX = arg0, RDX = pointer to struct

                // Load arg0 from [RSP + structSize] into R1 (RCX)
                X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, arg1Entry.ByteSize);

                // Now move the struct data down by 8 bytes to close the gap left by arg0
                // This way, after removing 8 bytes from stack, struct is at correct position
                // Actually, easier: keep struct in place, remove arg0, point RDX to struct after shadow
                //
                // Layout now: [RSP]=struct, [RSP+structSize]=arg0
                // We want to remove arg0 but it's at the bottom. Instead:
                // - Keep everything, track total size as structSize + 8
                // - After shadow space allocation, RCX is already set, LEA RDX to struct
                // - Clean up structSize + 8 after call

                largeStructArgBytes = arg1Entry.ByteSize + 8;  // struct + arg0
                needsLargeStructPtrInRdx = true;

                // Don't remove anything from physical stack yet
                PopEntry(); PopEntry();
            }
            else if (arg0Entry.ByteSize > 8)
            {
                // arg0 is a large struct: pass by pointer
                // This is unusual but handle it: struct in RCX by pointer, arg1 in RDX
                // arg1 at TOS, struct below it
                X64Emitter.Pop(ref _code, VReg.R2);   // RDX = arg1
                PopEntry();

                // Now struct is at TOS - pass pointer in RCX
                largeStructArgBytes = arg0Entry.ByteSize;
                needsLargeStructPtrInRcx = true;
                PopEntry();
            }
            else
            {
                // Both args are small: pop to RDX (arg1), then RCX (arg0)
                X64Emitter.Pop(ref _code, VReg.R2);   // arg1
                X64Emitter.Pop(ref _code, VReg.R1);   // arg0
                PopEntry(); PopEntry();
            }
        }
        else if (totalArgs == 2 && needsHiddenBuffer)
        {
            // Two normal args + hidden buffer: shift args by 1
            X64Emitter.Pop(ref _code, VReg.R3);   // arg1 -> R8
            X64Emitter.Pop(ref _code, VReg.R2);   // arg0 -> RDX
            PopEntry(); PopEntry();
        }
        else if (totalArgs == 3 && !needsHiddenBuffer)
        {
            // Three args: pop to R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            PopEntry(); PopEntry(); PopEntry();
        }
        else if (totalArgs == 3 && needsHiddenBuffer)
        {
            // Three normal args + hidden buffer: shift args by 1
            X64Emitter.Pop(ref _code, VReg.R4);    // arg2 -> R9
            X64Emitter.Pop(ref _code, VReg.R3);    // arg1 -> R8
            X64Emitter.Pop(ref _code, VReg.R2);   // arg0 -> RDX
            PopEntry(); PopEntry(); PopEntry();
        }
        else if (totalArgs == 4 && !needsHiddenBuffer)
        {
            // Four args: pop to R9, R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R4);    // arg3
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            PopEntry(); PopEntry(); PopEntry(); PopEntry();
        }
        else if (totalArgs == 4 && needsHiddenBuffer)
        {
            // Four normal args + hidden buffer: 5 physical args total
            // RCX = hidden buffer (set up after shadow space allocation)
            // RDX = arg0, R8 = arg1, R9 = arg2, [RSP+32] = arg3
            //
            // Pop args in reverse order from eval stack
            // arg3 goes to R10 temporarily, will be stored to stack after shadow space
            X64Emitter.Pop(ref _code, VReg.R5);    // arg3 -> R10 (temp)
            X64Emitter.Pop(ref _code, VReg.R4);    // arg2 -> R9
            X64Emitter.Pop(ref _code, VReg.R3);    // arg1 -> R8
            X64Emitter.Pop(ref _code, VReg.R2);    // arg0 -> RDX
            PopEntry(); PopEntry(); PopEntry(); PopEntry();

            // We need to handle this specially: allocate extra stack space for arg3
            // Calculate total allocation: 32 (shadow) + 8 (arg3) + hiddenBufferSize
            // But this needs to be 16-byte aligned, and we need to store arg3 at [RSP+32]
            // Allocate: shadow (32) + stack arg slot (8) + hidden buffer (rounded to 16)
            int bufferRounded = (hiddenBufferSize + 15) & ~15;
            int totalAlloc = 32 + 8 + bufferRounded;
            // Round up to 16-byte boundary
            totalAlloc = (totalAlloc + 15) & ~15;

            X64Emitter.SubRI(ref _code, VReg.SP, totalAlloc);

            // Store arg3 at [RSP+32]
            X64Emitter.MovMR(ref _code, VReg.SP, 32, VReg.R5);

            // Set RCX = buffer address (after shadow+stack args)
            // Buffer is at RSP + 32 + 8 = RSP + 40
            X64Emitter.Lea(ref _code, VReg.R1, VReg.SP, 40);

            // Track cleanup amount: shadow (32) + stack arg (8) = 40
            // The hidden buffer stays on stack for the return value
            fourArgsHiddenBufferCleanup = 40;

            // Mark that we handled shadow space allocation
            needsShadowSpace = false;
        }
        else
        {
            // More than 4 args - need to handle stack args
            //
            // Eval stack layout (before any modifications):
            //   [RSP + 0]               = argN-1 (top of stack, last pushed)
            //   [RSP + 8]               = argN-2
            //   ...
            //   [RSP + (N-1)*8]         = arg0 (bottom, first pushed)
            //
            // x64 ABI requires:
            //   RCX = arg0, RDX = arg1, R8 = arg2, R9 = arg3
            //   [RSP+32] = arg4, [RSP+40] = arg5, etc.
            //
            // Strategy: First consume the eval stack, then allocate outgoing space
            // 1. Load register args from eval stack
            // 2. Save stack args to scratch regs (R10, R11) or push them
            // 3. Add totalArgs*8 to RSP (consume eval stack)
            // 4. Sub extraStackSpace from RSP (allocate call frame)
            // 5. Store stack args to [RSP+32], [RSP+40], etc.

            // SIMPLER APPROACH:
            // 1. First save all stack args to scratch registers (R10, R11, etc.)
            // 2. Load register args (arg0-3) into RCX, RDX, R8, R9
            // 3. Pop the eval stack (ADD RSP, totalArgs*8)
            // 4. Allocate call frame (SUB RSP, 32 + extraStackSpace)
            // 5. Store stack args at [RSP+32], [RSP+40], etc.

            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;

            // Stack args debug (verbose - commented out)
            // DebugConsole.WriteDecimal((uint)totalArgs);
            // DebugConsole.Write(" stackArgs=");
            // DebugConsole.WriteDecimal((uint)stackArgs);
            // DebugConsole.Write(" extraStackSpace=");
            // DebugConsole.WriteDecimal((uint)extraStackSpace);
            // DebugConsole.WriteLine();

            // Step 1: Save stack args to scratch registers (R10, R11)
            // Stack args are at [RSP+0], [RSP+8], ... (top of eval stack)
            // VReg.R5 = R10 (scratch), VReg.R6 = R11 (scratch)
            // For now, support up to 6 total args (2 on stack).
            if (stackArgs >= 1)
            {
                // arg4 is at [RSP+0] (top of stack)
                X64Emitter.MovRM(ref _code, VReg.R5, VReg.SP, 0);  // R10 = arg4 (VReg.R5 = R10)
                // DebugConsole.WriteLine("[JIT]   arg4 saved to R10 from [RSP+0]");
            }
            if (stackArgs >= 2)
            {
                // arg5 is at [RSP+8]
                X64Emitter.MovRM(ref _code, VReg.R6, VReg.SP, 8);  // R11 = arg5 (VReg.R6 = R11)
                // DebugConsole.WriteLine("[JIT]   arg5 saved to R11 from [RSP+8]");
            }

            // Step 2: Load register args from eval stack
            // arg0 at [RSP + (totalArgs-1)*8], arg1 at [RSP + (totalArgs-2)*8], etc.
            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, (totalArgs - 1) * 8);  // RCX = arg0
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, (totalArgs - 2) * 8);  // RDX = arg1
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, (totalArgs - 3) * 8);  // R8 = arg2
            X64Emitter.MovRM(ref _code, VReg.R4, VReg.SP, (totalArgs - 4) * 8);  // R9 = arg3

            // Step 3: Pop eval stack
            X64Emitter.AddRI(ref _code, VReg.SP, totalArgs * 8);

            // Step 4: Allocate call frame (shadow space + extra for stack args)
            int callFrameSize = 32 + extraStackSpace;
            X64Emitter.SubRI(ref _code, VReg.SP, callFrameSize);

            // Step 5: Store stack args to their proper locations
            // arg4 at [RSP+32], arg5 at [RSP+40], etc.
            if (stackArgs >= 1)
            {
                X64Emitter.MovMR(ref _code, VReg.SP, 32, VReg.R5);  // [RSP+32] = R10 (arg4)
                // DebugConsole.WriteLine("[JIT]   arg4 stored to [RSP+32] from R10");
            }
            if (stackArgs >= 2)
            {
                X64Emitter.MovMR(ref _code, VReg.SP, 40, VReg.R6);  // [RSP+40] = R11 (arg5)
                // DebugConsole.WriteLine("[JIT]   arg5 stored to [RSP+40] from R11");
            }

            for (int i = 0; i < totalArgs; i++) PopEntry();
            // For >4 args, we already allocated shadow space as part of callFrameSize
            needsShadowSpace = false;
        }

        // Allocate shadow space for calls with 0-4 args
        // x64 ABI requires 32 bytes for the callee to home register arguments
        // For large struct returns, also allocate buffer space
        if (needsShadowSpace)
        {
            if (needsHiddenBuffer)
            {
                // Allocate shadow space + buffer space (aligned to 16 bytes)
                int totalAlloc = 32 + ((hiddenBufferSize + 15) & ~15);
                X64Emitter.SubRI(ref _code, VReg.SP, totalAlloc);

                // Set RCX = buffer address (RSP + 32, after shadow space)
                // lea rcx, [rsp + 32]
                X64Emitter.Lea(ref _code, VReg.R1, VReg.SP, 32);
            }
            else
            {
                X64Emitter.SubRI(ref _code, VReg.SP, 32);

                // If we deferred setting up RCX for a large struct pointer, do it now
                // The struct data is at [RSP + 32] (just after shadow space)
                if (needsLargeStructPtrInRcx)
                {
                    X64Emitter.Lea(ref _code, VReg.R1, VReg.SP, 32);
                }
                // If we deferred setting up RDX for a large struct pointer (2-arg case), do it now
                if (needsLargeStructPtrInRdx)
                {
                    X64Emitter.Lea(ref _code, VReg.R2, VReg.SP, 32);
                }
            }
        }

        // Load target address and call
        // We use an indirect call through RAX to support any address
        if (method.NativeCode != null)
        {
            // Direct call - we already know the target address
            if (_debugAssemblyId == 1)  // korlib
            {
                DebugConsole.Write("[JIT call] direct tok=0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" native=0x");
                DebugConsole.WriteHex((ulong)method.NativeCode);
                DebugConsole.WriteLine();
            }
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
        }
        else if (method.RegistryEntry != null)
        {
            // Indirect call through registry entry (for recursive/pending methods)
            // IMPORTANT: We must ensure the method is compiled before loading NativeCode!
            // Otherwise NativeCode might still be null.

            // First, call EnsureCompiled to trigger lazy compilation if needed
            // EnsureCompiled(uint methodToken, uint assemblyId) - uses RCX, RDX
            // We need to save any args that might be in those registers

            // Save the args we just set up in R10/R11 (caller-saved but not used for args)
            // Only need to save RCX and RDX if we have args in them
            bool savedRcx = totalArgs >= 1;
            bool savedRdx = totalArgs >= 2;
            bool savedR8 = totalArgs >= 3;
            bool savedR9 = totalArgs >= 4;

            if (savedRcx)
                X64Emitter.MovRR(ref _code, VReg.R10, VReg.R1);  // R10 = RCX
            if (savedRdx)
                X64Emitter.MovRR(ref _code, VReg.R11, VReg.R2);  // R11 = RDX
            // R8 and R9 need to be saved to stack if used
            if (savedR8)
                X64Emitter.Push(ref _code, VReg.R3);
            if (savedR9)
                X64Emitter.Push(ref _code, VReg.R4);

            // Get assembly ID from registry entry
            // CompiledMethodInfo layout: Token(4) + NativeCode(8) + ArgCount(1) + ReturnKind(1) + ... + AssemblyId at offset 32
            CompiledMethodInfo* info = (CompiledMethodInfo*)method.RegistryEntry;
            uint methodToken = info->Token;
            uint assemblyId = info->AssemblyId;

            // Set up args for EnsureCompiled(methodToken, assemblyId)
            X64Emitter.MovRI32(ref _code, VReg.R1, (int)methodToken);    // RCX = methodToken
            X64Emitter.MovRI32(ref _code, VReg.R2, (int)assemblyId);     // RDX = assemblyId

            // Allocate shadow space and call EnsureCompiled
            X64Emitter.SubRI(ref _code, VReg.SP, 32);
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)JitStubs.EnsureCompiledAddress);
            X64Emitter.CallR(ref _code, VReg.R0);
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // Restore saved args
            if (savedR9)
                X64Emitter.Pop(ref _code, VReg.R4);
            if (savedR8)
                X64Emitter.Pop(ref _code, VReg.R3);
            if (savedRdx)
                X64Emitter.MovRR(ref _code, VReg.R2, VReg.R11);  // RDX = R11
            if (savedRcx)
                X64Emitter.MovRR(ref _code, VReg.R1, VReg.R10);  // RCX = R10

            // Now load NativeCode from registry entry
            // Layout: CompiledMethodInfo { uint Token; void* NativeCode; ... }
            // NativeCode is at offset 8 (after 4-byte Token + 4 bytes padding for alignment)
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.RegistryEntry);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 8);  // Load [RAX+8] = NativeCode
        }
        else
        {
            DebugConsole.WriteLine("[JIT] ERROR: No native code and no registry entry");
            return false;
        }
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during callee execution)
        RecordSafePoint();

        // Clean up stack space if we allocated any
        if (needsShadowSpace)
        {
            if (needsHiddenBuffer)
            {
                // Only deallocate shadow space - keep the buffer on the stack!
                // RAX points to the buffer which is now at RSP after this cleanup.
                // The buffer will be tracked as part of evalStackDepth.
                X64Emitter.AddRI(ref _code, VReg.SP, 32 + largeStructArgBytes);
            }
            else
            {
                // Deallocate the 32-byte shadow space we allocated for 0-4 args
                // Plus any large struct args that were passed by pointer
                X64Emitter.AddRI(ref _code, VReg.SP, 32 + largeStructArgBytes);
            }
        }
        else if (stackArgs > 0)
        {
            // Deallocate the full call frame (shadow space + extra stack args space)
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            int callFrameSize = 32 + extraStackSpace + largeStructArgBytes;
            X64Emitter.AddRI(ref _code, VReg.SP, callFrameSize);
        }
        else if (largeStructArgBytes > 0)
        {
            // Only large struct args to clean up (no shadow space, no stack args)
            X64Emitter.AddRI(ref _code, VReg.SP, largeStructArgBytes);
        }
        else if (fourArgsHiddenBufferCleanup > 0)
        {
            // Special case: 4 args + hidden buffer - clean up shadow space + stack arg slot
            // The hidden buffer stays on stack as the return value
            X64Emitter.AddRI(ref _code, VReg.SP, fourArgsHiddenBufferCleanup);
        }

        // Handle return value
        switch (method.ReturnKind)
        {
            case ReturnKind.Void:
                // No return value - don't push anything
                break;

            case ReturnKind.Int32:
                // Return value in EAX (zero-extended in RAX)
                // Sign-extend to maintain IL semantics for signed int32
                // movsxd rax, eax
                _code.EmitByte(0x48);  // REX.W
                _code.EmitByte(0x63);  // MOVSXD
                _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                // Return value in RAX - push directly
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;

            case ReturnKind.Float32:
                // Return value in XMM0 - move to RAX and push
                // movd eax, xmm0
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float32);
                break;

            case ReturnKind.Float64:
                // Return value in XMM0 - move to RAX and push
                // movq rax, xmm0
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float64);
                break;

            case ReturnKind.Struct:
                // Handle struct returns based on size
                // DebugConsole.Write("[JIT Call] structRet=");
                // DebugConsole.WriteDecimal(method.ReturnStructSize);
                // DebugConsole.WriteLine();
                if (method.ReturnStructSize <= 8)
                {
                    // Small struct (1-8 bytes): value in RAX, push directly
                    X64Emitter.Push(ref _code, VReg.R0);
                    PushEntry(EvalStackEntry.Struct(8));
                }
                else if (method.ReturnStructSize <= 16)
                {
                    // Medium struct (9-16 bytes): RAX has first 8 bytes, RDX has second
                    // Push the struct data directly onto eval stack for stloc to copy
                    // Push in reverse order so memory layout is correct:
                    // push RDX  -> [RSP] = bytes 8-15
                    // push RAX  -> [RSP] = bytes 0-7, [RSP+8] = bytes 8-15
                    X64Emitter.Push(ref _code, VReg.R2);  // RDX = bytes 8-15 (pushed first, ends up at RSP+8)
                    X64Emitter.Push(ref _code, VReg.R0);  // RAX = bytes 0-7 (pushed second, ends up at RSP)
                    // Track as ONE entry with 16-byte size so stloc copies all bytes
                    PushEntry(EvalStackEntry.Struct(16));
                }
                else
                {
                    // Large struct (>16 bytes): caller passed hidden buffer, address in RAX
                    // After shadow space cleanup, the buffer data is now at RSP
                    // The buffer contains the struct VALUE - just like medium struct has
                    // RAX:RDX at RSP, we have the full struct data at RSP.
                    // DON'T push anything - the data is already in the right place.
                    // stloc will copy from RSP to the local variable.

                    // Track eval stack: buffer space (struct value on stack)
                    // Buffer was allocated as ((size+7)&~7) rounded to 16 bytes
                    int bufferSize8 = (method.ReturnStructSize + 7) & ~7;
                    int allocatedBuffer = (bufferSize8 + 15) & ~15;
                    // Push ONE entry with the full buffer size so stloc pops the correct amount
                    PushEntry(EvalStackEntry.Struct(allocatedBuffer));
                }
                break;
        }

        return true;
    }

    /// <summary>
    /// Compile a self-recursive tail call.
    /// Instead of call/ret, we:
    /// 1. Pop new arguments from eval stack
    /// 2. Store them in shadow space (where original args were homed)
    /// 3. Jump back to IL offset 0
    /// </summary>
    private bool CompileSelfRecursiveTailCall(ResolvedMethod method)
    {
        int argCount = method.ArgCount;

        // For now, only support up to 4 arguments (register args in shadow space)
        // Extending to more args requires stack manipulation beyond shadow space
        if (argCount > 4)
        {
            // Fall back: do a regular call (not optimized, but correct)
            // This is handled by returning false and letting the caller proceed normally
            // But we've already consumed the prefix, so we need to do the call here
            // Actually, we should just proceed with regular call path - caller will handle it
            // For now, just emit a regular call
            DebugConsole.Write("[JIT] Tail call fallback: ");
            DebugConsole.WriteDecimal((uint)argCount);
            DebugConsole.WriteLine(" args");

            // Cannot fall back easily since we're in a separate method
            // For correctness, just don't optimize - do a regular call
            return CompileRegularCall(method);
        }

        // Pop arguments in reverse order and store in shadow space
        // Shadow space layout: [RBP+16]=arg0, [RBP+24]=arg1, [RBP+32]=arg2, [RBP+40]=arg3

        // Pop all args first (they're in IL order: arg0 bottom, argN-1 top)
        // We need to pop in reverse and store in reverse, so final order is correct

        // Strategy: Pop to temp locations, then store to shadow space
        // Use RBX, R12, R13, R14 as temps (callee-saved, we can use them temporarily)

        // Pop args to temp registers in reverse order (TOS = argN-1)
        // VReg mapping: R7=RBX, R8=R12, R9=R13, R10=R14 (all callee-saved)
        // For 4 args: pop -> R7 (arg3), pop -> R8 (arg2), pop -> R9 (arg1), pop -> R10 (arg0)
        if (argCount >= 4)
        {
            X64Emitter.Pop(ref _code, VReg.R7);  // arg3 -> R7 (RBX) temp
            PopEntry();
        }
        if (argCount >= 3)
        {
            X64Emitter.Pop(ref _code, VReg.R8);  // arg2 -> R8 (R12) temp
            PopEntry();
        }
        if (argCount >= 2)
        {
            X64Emitter.Pop(ref _code, VReg.R9);  // arg1 -> R9 (R13) temp
            PopEntry();
        }
        if (argCount >= 1)
        {
            X64Emitter.Pop(ref _code, VReg.R10); // arg0 -> R10 (R14) temp
            PopEntry();
        }

        // Now store temps to shadow space
        // [RBP+16] = arg0, [RBP+24] = arg1, [RBP+32] = arg2, [RBP+40] = arg3
        if (argCount >= 1)
        {
            // mov [rbp+16], r14  (arg0)
            X64Emitter.MovMR(ref _code, VReg.FP, 16, VReg.R10);
        }
        if (argCount >= 2)
        {
            // mov [rbp+24], r13  (arg1)
            X64Emitter.MovMR(ref _code, VReg.FP, 24, VReg.R9);
        }
        if (argCount >= 3)
        {
            // mov [rbp+32], r12  (arg2)
            X64Emitter.MovMR(ref _code, VReg.FP, 32, VReg.R8);
        }
        if (argCount >= 4)
        {
            // mov [rbp+40], rbx  (arg3)
            X64Emitter.MovMR(ref _code, VReg.FP, 40, VReg.R7);
        }

        // Jump to IL offset 0 (start of method body after prologue)
        // Use RecordBranch to handle forward/backward reference
        int targetIL = 0;
        int codeOffset = FindCodeOffset(targetIL);

        if (codeOffset >= 0)
        {
            // Backward jump - we know the target already
            // jmp rel32
            _code.EmitByte(0xE9);
            int rel = codeOffset - (_code.Position + 4);
            _code.EmitInt32(rel);
        }
        else
        {
            // IL offset 0 should always be known since we record labels as we compile
            // This case shouldn't happen for self-recursion
            DebugConsole.WriteLine("[JIT] ERROR: Tail call target IL 0 not found");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Helper for CompileSelfRecursiveTailCall fallback - emit a regular call
    /// </summary>
    private bool CompileRegularCall(ResolvedMethod method)
    {
        int totalArgs = method.ArgCount;

        // Pop arguments and set up registers for call
        // This is a simplified version - just pop and call
        if (totalArgs >= 4)
        {
            X64Emitter.Pop(ref _code, VReg.R4);  // arg3 -> R9
            PopEntry();
        }
        if (totalArgs >= 3)
        {
            X64Emitter.Pop(ref _code, VReg.R3);  // arg2 -> R8
            PopEntry();
        }
        if (totalArgs >= 2)
        {
            X64Emitter.Pop(ref _code, VReg.R2);  // arg1 -> RDX
            PopEntry();
        }
        if (totalArgs >= 1)
        {
            X64Emitter.Pop(ref _code, VReg.R1);  // arg0 -> RCX
            PopEntry();
        }

        // Allocate shadow space and call
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call the method
        if (method.NativeCode != null)
        {
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
        }
        else
        {
            DebugConsole.WriteLine("[JIT] ERROR: No native code for tail call fallback");
            return false;
        }

        // Deallocate shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Handle return value
        if (method.ReturnKind != ReturnKind.Void)
        {
            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(EvalStackEntry.NativeInt);
        }
        // For void, nothing to push

        return true;
    }

    /// <summary>
    /// Compile a callvirt instruction (virtual call).
    ///
    /// For non-virtual methods, this is identical to 'call' but always passes 'this'.
    /// For virtual methods, we need vtable lookup:
    ///   1. Get MethodTable pointer from object (first 8 bytes)
    ///   2. Look up vtable slot from MethodTable
    ///   3. Call through the vtable slot
    ///
    /// IL eval stack before call: [..., this, arg0, arg1, ..., argN-1] (argN-1 on top)
    /// IL eval stack after call:  [..., returnValue] (if non-void)
    ///
    /// For now, we implement simple devirtualization: resolve to the direct method
    /// like 'call'. True virtual dispatch requires vtable slot index info.
    /// </summary>
    private bool CompileCallvirt(uint token)
    {
        // Check for constrained. prefix - this changes how we handle 'this'
        uint constrainedToken = _constrainedTypeToken;
        _constrainedTypeToken = 0;  // Clear it - must be consumed

        // For constrained. prefix on value types:
        // - Stack has a managed pointer to the value type (NOT a boxed object)
        // - We need to box the value first, then do the virtual call
        // - The constrained token tells us the type to box
        if (constrainedToken != 0 && _typeResolver != null)
        {
            DebugConsole.Write("[constrained] token=0x");
            DebugConsole.WriteHex(constrainedToken);

            // Resolve the constraint type to get its MethodTable
            void* resolvedPtr;
            bool resolveResult = _typeResolver(constrainedToken, out resolvedPtr);
            DebugConsole.Write(" res=");
            DebugConsole.Write(resolveResult ? "T" : "F");
            DebugConsole.Write(" ptr=0x");
            DebugConsole.WriteHex((ulong)resolvedPtr);
            if (resolveResult && resolvedPtr != null)
            {
                MethodTable* constraintMT = (MethodTable*)resolvedPtr;
                DebugConsole.Write(" mt=0x");
                DebugConsole.WriteHex((ulong)constraintMT);
                DebugConsole.Write(" isVT=");
                DebugConsole.Write(constraintMT->IsValueType ? "Y" : "N");
                DebugConsole.WriteLine();

                if (constraintMT->IsValueType)
                {
                    // Value type with constrained prefix - need to box before virtual call
                    // Stack currently has: [..., managed_ptr_to_value, arg1, arg2, ...]
                    // where the managed pointer is at position (method.ArgCount) from top.
                    //
                    // We need to:
                    // 1. First resolve the method to know how many args are on top of the managed ptr
                    // 2. Save the args, box the managed ptr, restore the args
                    // 3. Continue with virtual call
                    //
                    // After boxing, the vtable dispatch will work normally since primitive
                    // MethodTables now include proper vtable entries (ToString, Equals, GetHashCode).

                    // We need to resolve the method FIRST to know the arg count
                    // This is a bit ugly but necessary for correct stack manipulation
                    if (!ResolveMethod(token, out ResolvedMethod tempMethod))
                    {
                        DebugConsole.Write("[JIT] ResolveMethod failed for constrained callvirt 0x");
                        DebugConsole.WriteHex(token);
                        DebugConsole.WriteLine();
                        return false;
                    }

                    int numArgs = tempMethod.ArgCount;  // Args NOT including 'this'

                    // Get the value size from the MethodTable
                    uint baseSize = constraintMT->_uBaseSize;
                    int valueSize = (int)(baseSize - 8);  // Subtract MethodTable pointer (8 bytes)
                    if (valueSize <= 0) valueSize = 8;  // Minimum

                    // OPTIMIZATION: For primitive types, inline Equals comparison instead of boxing
                    // This avoids the boxing overhead and correctly implements value equality
                    // (Object.Equals does reference equality which doesn't work for boxed primitives)
                    if (numArgs == 1 && tempMethod.IsVirtual && tempMethod.VtableSlot == 1 &&
                        MetadataIntegration.IsPrimitiveMT(constraintMT))
                    {
                        DebugConsole.Write("[JIT] Inline primitive Equals: MT=0x");
                        DebugConsole.WriteHex((ulong)constraintMT);
                        DebugConsole.Write(" valueSize=");
                        DebugConsole.WriteDecimal((uint)valueSize);
                        DebugConsole.WriteLine();

                        // Stack has: [managed_ptr_to_value, arg0_object_ref]
                        // Pop arg0 (other object) and managed_ptr
                        X64Emitter.Pop(ref _code, VReg.R5);   // R10 = other (object ref)
                        X64Emitter.Pop(ref _code, VReg.R0);   // RAX = managed_ptr
                        PopEntry(); PopEntry();

                        // Load "this" value from managed_ptr into RCX
                        if (valueSize <= 4)
                            X64Emitter.MovRM32(ref _code, VReg.R1, VReg.R0, 0);  // ECX = *managed_ptr
                        else
                            X64Emitter.MovRM(ref _code, VReg.R1, VReg.R0, 0);    // RCX = *managed_ptr

                        // Check if other is null -> return false
                        X64Emitter.TestRR(ref _code, VReg.R5, VReg.R5);
                        int nullCheckPatch = X64Emitter.JccRel32(ref _code, X64Emitter.CC_E);  // JE return_false

                        // Check if other's MT matches our primitive MT -> if not, return false
                        X64Emitter.MovRM(ref _code, VReg.R2, VReg.R5, 0);  // RDX = [other] = other's MT
                        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)constraintMT);  // RAX = expected MT
                        X64Emitter.CmpRR(ref _code, VReg.R2, VReg.R0);
                        int mtCheckPatch = X64Emitter.JccRel32(ref _code, X64Emitter.CC_NE);  // JNE return_false

                        // Unbox other: value is at [other + 8]
                        if (valueSize <= 4)
                            X64Emitter.MovRM32(ref _code, VReg.R2, VReg.R5, 8);  // EDX = [other + 8]
                        else
                            X64Emitter.MovRM(ref _code, VReg.R2, VReg.R5, 8);    // RDX = [other + 8]

                        // Compare values
                        if (valueSize <= 4)
                            X64Emitter.CmpRR32(ref _code, VReg.R1, VReg.R2);
                        else
                            X64Emitter.CmpRR(ref _code, VReg.R1, VReg.R2);

                        // Set result: AL = 1 if equal, 0 otherwise
                        // SETE AL: 0F 94 C0
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0x94);
                        _code.EmitByte(0xC0);

                        // Zero-extend AL to RAX: MOVZX RAX, AL (REX.W + 0F B6 /r)
                        _code.EmitByte(0x48);  // REX.W
                        _code.EmitByte(0x0F);
                        _code.EmitByte(0xB6);
                        _code.EmitByte(0xC0);  // ModRM: mod=11, reg=RAX, r/m=AL

                        // Jump to done
                        int donePatch = X64Emitter.JmpRel32(ref _code);

                        // return_false:
                        int returnFalseOffset = _code.Position;
                        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);  // RAX = 0 (false)

                        // done:
                        int doneOffset = _code.Position;

                        // Patch jump targets
                        X64Emitter.PatchJump(ref _code, nullCheckPatch, returnFalseOffset);
                        X64Emitter.PatchJump(ref _code, mtCheckPatch, returnFalseOffset);
                        X64Emitter.PatchJump(ref _code, donePatch, doneOffset);

                        // Push result
                        X64Emitter.Push(ref _code, VReg.R0);
                        PushEntry(EvalStackEntry.Int32);

                        return true;  // Don't fall through to normal callvirt
                    }

                    // The managed pointer is at stack position [numArgs] (0-indexed from top)
                    // We need to save the args, get the managed ptr, box it, then restore args

                    if (numArgs == 0)
                    {
                        // No args on top - managed ptr is on top
                        // Pop managed pointer to RAX
                        X64Emitter.Pop(ref _code, VReg.R0);
                        PopEntry();
                    }
                    else if (numArgs == 1)
                    {
                        // One arg on top: [managed_ptr, arg0]
                        // Save arg0, get managed_ptr
                        X64Emitter.Pop(ref _code, VReg.R5);   // R10 = arg0
                        X64Emitter.Pop(ref _code, VReg.R0);   // RAX = managed_ptr
                        PopEntry(); PopEntry();
                    }
                    else if (numArgs == 2)
                    {
                        // Two args on top: [managed_ptr, arg0, arg1]
                        X64Emitter.Pop(ref _code, VReg.R6);   // R11 = arg1
                        X64Emitter.Pop(ref _code, VReg.R5);   // R10 = arg0
                        X64Emitter.Pop(ref _code, VReg.R0);   // RAX = managed_ptr
                        PopEntry(); PopEntry(); PopEntry();
                    }
                    else
                    {
                        // Three+ args on top: [managed_ptr, arg0, arg1, arg2]
                        X64Emitter.Pop(ref _code, VReg.R7);   // R12 = arg2
                        X64Emitter.Pop(ref _code, VReg.R6);   // R11 = arg1
                        X64Emitter.Pop(ref _code, VReg.R5);   // R10 = arg0
                        X64Emitter.Pop(ref _code, VReg.R0);   // RAX = managed_ptr
                        PopEntry(); PopEntry(); PopEntry(); PopEntry();
                    }

                    // Read the value from the pointer
                    // For small values (<=8 bytes), we can read directly
                    if (valueSize <= 4)
                    {
                        // mov eax, [rax] - read 4 bytes
                        X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, 0);
                    }
                    else
                    {
                        // mov rax, [rax] - read 8 bytes
                        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
                    }

                    // Now box it - inline the boxing logic
                    // Need to allocate: baseSize bytes
                    // Layout: [MethodTable*][value]

                    // Save value on the stack and the args we saved in registers
                    X64Emitter.Push(ref _code, VReg.R0);  // Save value on stack
                    if (numArgs >= 1) X64Emitter.Push(ref _code, VReg.R5);   // Save arg0
                    if (numArgs >= 2) X64Emitter.Push(ref _code, VReg.R6);   // Save arg1
                    if (numArgs >= 3) X64Emitter.Push(ref _code, VReg.R7);   // Save arg2

                    // Call RhpNewFast(MT*) - returns pointer in RAX
                    X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)constraintMT);  // RCX = MT*
                    X64Emitter.SubRI(ref _code, VReg.SP, 32);  // Shadow space
                    X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
                    X64Emitter.CallR(ref _code, VReg.R0);
                    X64Emitter.AddRI(ref _code, VReg.SP, 32);

                    // RAX = allocated object pointer (MT* is already set by RhpNewFast)
                    // Restore saved args and value
                    if (numArgs >= 3) X64Emitter.Pop(ref _code, VReg.R7);   // Restore arg2
                    if (numArgs >= 2) X64Emitter.Pop(ref _code, VReg.R6);   // Restore arg1
                    if (numArgs >= 1) X64Emitter.Pop(ref _code, VReg.R5);   // Restore arg0
                    X64Emitter.Pop(ref _code, VReg.R2);  // RDX = saved value

                    // Store value at [RAX + 8]
                    if (valueSize <= 4)
                    {
                        X64Emitter.MovMR32(ref _code, VReg.R0, 8, VReg.R2);  // [RAX+8] = value (32-bit)
                    }
                    else
                    {
                        X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R2);  // [RAX+8] = value (64-bit)
                    }

                    // Now push args back in correct order, then the boxed object
                    // Stack should end up as: [..., boxed_object, arg0, arg1, ...]
                    X64Emitter.Push(ref _code, VReg.R0);  // Push boxed object
                    PushEntry(EvalStackEntry.NativeInt);  // Object reference
                    if (numArgs >= 1)
                    {
                        X64Emitter.Push(ref _code, VReg.R5);  // Push arg0
                        PushEntry(EvalStackEntry.NativeInt);
                    }
                    if (numArgs >= 2)
                    {
                        X64Emitter.Push(ref _code, VReg.R6);  // Push arg1
                        PushEntry(EvalStackEntry.NativeInt);
                    }
                    if (numArgs >= 3)
                    {
                        X64Emitter.Push(ref _code, VReg.R7);  // Push arg2
                        PushEntry(EvalStackEntry.NativeInt);
                    }

                    // Now fall through to callvirt handling with correct stack order
                    // Virtual dispatch will work via the vtable entries in the MethodTable
                }
                else
                {
                    // Reference type with constrained prefix
                    // Stack has: managed pointer to a reference (not the reference itself)
                    // We need to dereference it to get the actual object reference
                    //
                    // Example: ldflda StringField; constrained. string; callvirt ToString
                    // Stack has: &(this.StringField) which is a pointer to a string reference
                    // We need: the string reference itself

                    // Pop managed pointer to RAX
                    X64Emitter.Pop(ref _code, VReg.R0);
                    PopEntry();

                    // Dereference: RAX = [RAX] - read the object reference from the pointer
                    X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);

                    // Push the actual object reference back on stack
                    X64Emitter.Push(ref _code, VReg.R0);
                    PushEntry(EvalStackEntry.NativeInt);

                    // Now fall through to callvirt handling with the reference on stack
                }
            }
            else
            {
                // Type resolution failed for constrained token
                // This can happen for generic type parameters (T) when we can't resolve them.
                // We assume it's a reference type and dereference the managed pointer.
                // This is safe because:
                // 1. Value types would crash anyway without proper boxing
                // 2. Generic parameters used with constrained callvirt are usually reference types
                //    when the constraint cannot be resolved at JIT time
                DebugConsole.WriteLine(" [FALLBACK] assuming ref type");

                // Pop managed pointer to RAX
                X64Emitter.Pop(ref _code, VReg.R0);
                PopEntry();

                // Dereference: RAX = [RAX] - read the object reference from the pointer
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);

                // Push the actual object reference back on stack
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
            }
        }

        // For now, callvirt behaves like call but always has 'this'.
        // The resolved method should have HasThis=true.
        //
        // True virtual dispatch would:
        // 1. Load 'this' from stack without consuming it
        // 2. Load MethodTable* from [this]
        // 3. Load vtable slot from [MT + vtableSlotOffset]
        // 4. Call through the slot
        //
        // For now, we resolve to the specific method like 'call'.
        // This is correct for sealed types, final methods, and non-virtual methods.

        if (!ResolveMethod(token, out ResolvedMethod method))
        {
            // DebugConsole.Write("[JIT] ResolveMethod failed for callvirt 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // Special case: delegate Invoke
        // Delegate.Invoke is a runtime-provided method - we emit inline dispatch code
        if (method.IsDelegateInvoke)
        {
            return CompileCallvirtDelegateInvoke(method);
        }

        // Callvirt always has 'this' as the first argument
        // Even if method.HasThis is false (shouldn't happen), we treat it as instance
        int totalArgs = method.ArgCount;
        if (method.HasThis || true) // callvirt always has 'this'
            totalArgs++;

        // bool debugBind = IsDebugBindMethod();
        // if (debugBind)
        // {
        //     DebugConsole.Write("[BindDbg] callvirt il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" token=0x");
        //     DebugConsole.WriteHex(token);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.Write(" totalArgs=");
        //     DebugConsole.WriteDecimal((uint)totalArgs);
        //     DebugConsole.WriteLine();
        // }

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalArgs)
        {
            // DebugConsole.Write("[JIT] Callvirt: insufficient stack depth ");
            // DebugConsole.WriteDecimal((uint)_evalStackDepth);
            // DebugConsole.Write(" for ");
            // DebugConsole.WriteDecimal((uint)totalArgs);
            // DebugConsole.WriteLine(" args");
            return false;
        }

        // The rest is identical to CompileCall - pop args, set up registers, call, handle return
        int stackArgs = totalArgs > 4 ? totalArgs - 4 : 0;

        // x64 ABI ALWAYS requires 32 bytes shadow space, even for >4 args
        bool needsShadowSpace = true;

        if (totalArgs == 0)
        {
            // No arguments - just call (shouldn't happen for callvirt)
        }
        else if (totalArgs == 1)
        {
            // Single arg (this): pop to RCX
            X64Emitter.Pop(ref _code, VReg.R1);
            PopEntry();
        }
        else if (totalArgs == 2)
        {
            // Two args: pop to RDX (arg1), then RCX (this)
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            PopEntry(); PopEntry();
        }
        else if (totalArgs == 3)
        {
            // Three args: pop to R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R3);
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            PopEntry(); PopEntry(); PopEntry();
        }
        else if (totalArgs == 4)
        {
            // Four args: pop to R9, R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R4);
            X64Emitter.Pop(ref _code, VReg.R3);
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            PopEntry(); PopEntry(); PopEntry(); PopEntry();
        }
        else
        {
            // More than 4 args - handle stack args (same as CompileCall)
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;

            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, (totalArgs - 1) * 8);
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, (totalArgs - 2) * 8);
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, (totalArgs - 3) * 8);
            X64Emitter.MovRM(ref _code, VReg.R4, VReg.SP, (totalArgs - 4) * 8);

            // Copy stack args to their final locations (relative to current RSP)
            // Note: shadow space (32 bytes) is allocated AFTER rspAdjust, so positions are
            // calculated relative to the pre-shadow RSP. After shadow space allocation,
            // these will be at [finalRSP + 32], [finalRSP + 40], etc. as required.
            for (int i = 0; i < stackArgs; i++)
            {
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = totalArgs * 8 - extraStackSpace + i * 8;
                X64Emitter.MovRM(ref _code, VReg.R5, VReg.SP, srcOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, dstOffset, VReg.R5);
            }

            int rspAdjust = totalArgs * 8 - extraStackSpace;
            if (rspAdjust > 0)
            {
                X64Emitter.AddRI(ref _code, VReg.SP, rspAdjust);
            }
            else if (rspAdjust < 0)
            {
                X64Emitter.SubRI(ref _code, VReg.SP, -rspAdjust);
            }

            for (int i = 0; i < totalArgs; i++) PopEntry();
        }

        // Check if we need a hidden return buffer for large struct returns
        bool needsHiddenBuffer = method.ReturnKind == ReturnKind.Struct && method.ReturnStructSize > 16;
        int hiddenBufferSize = needsHiddenBuffer ? (int)((method.ReturnStructSize + 7) & ~7) : 0;

        // Debug: trace callvirt in TestDictForeach
        if (_debugAssemblyId == 6 && _debugMethodToken == 0x060002E7)
        {
            DebugConsole.Write("[DF-CVirt] tok=0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" ret=");
            DebugConsole.WriteDecimal((uint)method.ReturnKind);
            DebugConsole.Write(" retSz=");
            DebugConsole.WriteDecimal((uint)method.ReturnStructSize);
            DebugConsole.Write(" hidden=");
            DebugConsole.Write(needsHiddenBuffer ? "Y" : "N");
            DebugConsole.Write(" bufSz=");
            DebugConsole.WriteDecimal((uint)hiddenBufferSize);
            DebugConsole.WriteLine();
        }

        // Allocate shadow space (x64 ABI ALWAYS requires 32 bytes)
        // x64 ABI requires 32 bytes for the callee to home register arguments
        // For large struct returns, also allocate buffer space
        if (needsShadowSpace)
        {
            if (needsHiddenBuffer)
            {
                // Allocate shadow space + buffer space (aligned to 16 bytes)
                int totalAlloc = 32 + ((hiddenBufferSize + 15) & ~15);
                X64Emitter.SubRI(ref _code, VReg.SP, totalAlloc);

                // For callvirt, 'this' is already in RCX, we need to shift args
                // Hidden buffer pointer becomes RCX, this goes to RDX, etc.
                // Save current args first
                if (totalArgs >= 4)
                    X64Emitter.MovRR(ref _code, VReg.R11, VReg.R4);  // Save R9
                if (totalArgs >= 3)
                    X64Emitter.MovRR(ref _code, VReg.R4, VReg.R3);   // R9 = R8 (arg2)
                if (totalArgs >= 2)
                    X64Emitter.MovRR(ref _code, VReg.R3, VReg.R2);   // R8 = RDX (arg1)
                X64Emitter.MovRR(ref _code, VReg.R2, VReg.R1);       // RDX = RCX (this)

                // Set RCX = buffer address (RSP + 32, after shadow space)
                X64Emitter.Lea(ref _code, VReg.R1, VReg.SP, 32);
            }
            else
            {
                X64Emitter.SubRI(ref _code, VReg.SP, 32);
            }
        }

        // Load target address and call
        if (method.IsInterfaceMethod)
        {
            // Interface dispatch: call GetInterfaceMethod helper at runtime
            // This is needed because the vtable slot depends on the concrete object type.
            //
            // GetInterfaceMethod signature: void* GetInterfaceMethod(void* obj, MethodTable* interfaceMT, int methodIndex)
            //
            // At this point:
            //   RCX = 'this' (the object)
            //   RDX, R8, R9 = arg1, arg2, arg3 (if any)
            //
            // We need to:
            //   1. Save current args to callee-saved registers
            //   2. Call GetInterfaceMethod(obj, interfaceMT, methodIndex)
            //   3. Save function pointer result
            //   4. Restore args
            //   5. Call through function pointer

            // Save args to callee-saved registers (R12-R15)
            // We only need to save what we'll overwrite
            X64Emitter.MovRR(ref _code, VReg.R8, VReg.R1);  // Save this
            if (totalArgs >= 2)
                X64Emitter.MovRR(ref _code, VReg.R9, VReg.R2);  // Save arg1
            if (totalArgs >= 3)
                X64Emitter.MovRR(ref _code, VReg.R10, VReg.R3);   // Save arg2
            if (totalArgs >= 4)
                X64Emitter.MovRR(ref _code, VReg.R11, VReg.R4);   // Save arg3

            // Set up call to GetInterfaceMethod(obj, interfaceMT, methodIndex)
            // RCX already has 'this' (obj), no change needed
            X64Emitter.MovRI64(ref _code, VReg.R2, (ulong)method.InterfaceMT);  // interfaceMT
            X64Emitter.MovRI32(ref _code, VReg.R3, method.InterfaceMethodSlot);  // methodIndex

            // Call GetInterfaceMethod
            X64Emitter.SubRI(ref _code, VReg.SP, 32);  // Shadow space
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_getInterfaceMethod);
            X64Emitter.CallR(ref _code, VReg.R0);
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // RAX now contains the function pointer
            // Save it to R10 (caller-saved but we control this)
            X64Emitter.MovRR(ref _code, VReg.R5, VReg.R0);

            // Restore args from callee-saved registers
            X64Emitter.MovRR(ref _code, VReg.R1, VReg.R8);  // Restore this
            if (totalArgs >= 2)
                X64Emitter.MovRR(ref _code, VReg.R2, VReg.R9);  // Restore arg1
            if (totalArgs >= 3)
                X64Emitter.MovRR(ref _code, VReg.R3, VReg.R10);   // Restore arg2
            if (totalArgs >= 4)
                X64Emitter.MovRR(ref _code, VReg.R4, VReg.R11);   // Restore arg3

            // Call through the resolved function pointer
            X64Emitter.CallR(ref _code, VReg.R5);
        }
        else if (method.IsVirtual && method.VtableSlot >= 0)
        {
            // Virtual dispatch: load function pointer from vtable
            // RCX contains 'this' at this point
            DebugConsole.Write("[JIT] vtable dispatch: slot=");
            DebugConsole.WriteDecimal((uint)method.VtableSlot);
            DebugConsole.Write(" offset=0x");
            int vtableOffset = ProtonOS.Runtime.MethodTable.HeaderSize + (method.VtableSlot * 8);
            DebugConsole.WriteHex((uint)vtableOffset);
            DebugConsole.WriteLine();

            // Call EnsureVtableSlotCompiled(objPtr, vtableSlot) to ensure the method is compiled
            // This stub returns the method address to call, which handles out-of-bounds vtable slots
            // (e.g., for sealed types like String where NativeAOT optimized away vtable slots)
            if (_ensureVtableSlotCompiled != null)
            {
                // Save RBX first (we'll use it to hold the method address)
                X64Emitter.Push(ref _code, VReg.R7);  // Save original RBX

                // Save args to stack (we need them after the stub call)
                // Stack layout: [RBX_orig][arg3][arg2][arg1][this] <- RSP points to this
                if (totalArgs >= 4)
                    X64Emitter.Push(ref _code, VReg.R4);  // Push R9 (arg3)
                if (totalArgs >= 3)
                    X64Emitter.Push(ref _code, VReg.R3);  // Push R8 (arg2)
                if (totalArgs >= 2)
                    X64Emitter.Push(ref _code, VReg.R2);  // Push RDX (arg1)
                X64Emitter.Push(ref _code, VReg.R1);      // Push RCX (this)

                // RCX still has 'this' (objPtr), just need RDX = slot
                X64Emitter.MovRI32(ref _code, VReg.R2, method.VtableSlot);

                // Call stub helper: nint EnsureVtableSlotCompiled(nint objPtr, short vtableSlot)
                X64Emitter.SubRI(ref _code, VReg.SP, 32);  // Shadow space
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_ensureVtableSlotCompiled);
                X64Emitter.CallR(ref _code, VReg.R0);
                X64Emitter.AddRI(ref _code, VReg.SP, 32);  // Remove shadow space

                // Save method address to RBX
                X64Emitter.MovRR(ref _code, VReg.R7, VReg.R0);  // RBX = RAX (method address)

                // Pop args from stack (reverse order)
                X64Emitter.Pop(ref _code, VReg.R1);       // Pop RCX (this)
                if (totalArgs >= 2)
                    X64Emitter.Pop(ref _code, VReg.R2);   // Pop RDX (arg1)
                if (totalArgs >= 3)
                    X64Emitter.Pop(ref _code, VReg.R3);   // Pop R8 (arg2)
                if (totalArgs >= 4)
                    X64Emitter.Pop(ref _code, VReg.R4);   // Pop R9 (arg3)

                // Move method address to RAX
                X64Emitter.MovRR(ref _code, VReg.R0, VReg.R7);  // RAX = RBX (method address)

                // Restore original RBX
                X64Emitter.Pop(ref _code, VReg.R7);  // Restore RBX

                // Call through the method address (now in RAX)
                X64Emitter.CallR(ref _code, VReg.R0);  // call RAX
            }
            else
            {
                // Fallback: load from vtable directly (used when stub is not available)
                // 1. Load MethodTable* from [RCX] (object header is the MT pointer)
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R1, 0);  // RAX = *this = MethodTable*

                // 2. Load vtable slot at offset HeaderSize + slot*8
                // MethodTable.HeaderSize = 24 bytes, each vtable slot is 8 bytes
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, vtableOffset);  // RAX = vtable[slot]

                // 3. Call through the vtable slot
                X64Emitter.CallR(ref _code, VReg.R0);
            }
        }
        else
        {
            // Direct call (devirtualized or non-virtual)
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
        }

        // Record safe point after call
        RecordSafePoint();

        // Clean up stack space if we allocated any
        if (needsShadowSpace)
        {
            if (needsHiddenBuffer)
            {
                // Only deallocate shadow space, keep the buffer on stack for struct return
                X64Emitter.AddRI(ref _code, VReg.SP, 32);
            }
            else
            {
                // Deallocate the 32-byte shadow space we allocated for 0-4 args
                X64Emitter.AddRI(ref _code, VReg.SP, 32);
            }
        }
        else if (stackArgs > 0)
        {
            // Deallocate the full call frame (shadow space + extra stack args space)
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            int callFrameSize = 32 + extraStackSpace;
            X64Emitter.AddRI(ref _code, VReg.SP, callFrameSize);
        }

        // Handle return value (same as CompileCall)
        switch (method.ReturnKind)
        {
            case ReturnKind.Void:
                break;

            case ReturnKind.Int32:
                _code.EmitByte(0x48);  // REX.W
                _code.EmitByte(0x63);  // MOVSXD
                _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;

            case ReturnKind.Float32:
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float32);
                break;

            case ReturnKind.Float64:
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float64);
                break;

            case ReturnKind.Struct:
                // Handle struct returns based on size
                if (method.ReturnStructSize <= 8)
                {
                    // Small struct (1-8 bytes): value in RAX, push directly
                    X64Emitter.Push(ref _code, VReg.R0);
                    PushEntry(EvalStackEntry.Struct(8));
                }
                else if (method.ReturnStructSize <= 16)
                {
                    // Medium struct (9-16 bytes): RAX has first 8 bytes, RDX has second
                    X64Emitter.Push(ref _code, VReg.R2);  // RDX = bytes 8-15
                    X64Emitter.Push(ref _code, VReg.R0);  // RAX = bytes 0-7
                    PushEntry(EvalStackEntry.Struct(16));
                }
                else
                {
                    // Large struct (>16 bytes): caller passed hidden buffer, address in RAX
                    // The buffer contains the struct value at RSP (after shadow space was cleaned up)
                    int bufferSize8 = (method.ReturnStructSize + 7) & ~7;
                    int allocatedBuffer = (bufferSize8 + 15) & ~15;
                    PushEntry(EvalStackEntry.Struct(allocatedBuffer));
                }
                break;
        }

        // if (debugBind)
        // {
        //     DebugConsole.Write("[BindDbg] callvirt done il=0x");
        //     DebugConsole.WriteHex((ulong)_ilOffset);
        //     DebugConsole.Write(" codePos=");
        //     DebugConsole.WriteHex((ulong)_code.Position);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }

        return true;
    }

    /// <summary>
    /// Compile delegate Invoke call.
    /// Stack: ..., arg0, ..., argN-1, delegate -> ..., result (if non-void)
    ///
    /// Delegate layout:
    /// - Offset 8:  _firstParameter (target object for instance, null for static)
    /// - Offset 32: _functionPointer (actual method to call)
    ///
    /// For instance delegates: call func(_firstParameter, arg0, ..., argN-1)
    /// For static delegates: call func(arg0, ..., argN-1)
    /// </summary>
    private bool CompileCallvirtDelegateInvoke(ResolvedMethod method)
    {
        // Total args including 'this' (delegate reference)
        // The delegate itself is on the stack as 'this'
        int delegateArgCount = method.ArgCount;  // Args to pass to target function (not including delegate)
        int totalStackArgs = delegateArgCount + 1;  // +1 for delegate reference

        if (totalStackArgs > 5)  // Max 4 real args + delegate
        {
            DebugConsole.Write("[JIT] delegate Invoke: too many args ");
            DebugConsole.WriteDecimal((uint)delegateArgCount);
            DebugConsole.WriteLine();
            return false;
        }

        // Pop all arguments from stack including delegate reference
        // Stack order: ..., arg0, arg1, delegate (delegate on top for callvirt)
        // Actually for callvirt: delegate is pushed first, then args
        // Wait - need to check. Stack: ..., delegate, arg0, arg1, ... (delegate pushed first, args after)
        // When popping: first pop arg (N-1), then arg (N-2), ..., then arg0, then delegate

        // Save args to temp registers, then delegate
        VReg[] tempRegs = { VReg.R7, VReg.R8, VReg.R9, VReg.R10, VReg.R11 };

        // Pop in reverse order: argN-1, ..., arg0, delegate
        for (int i = delegateArgCount - 1; i >= 0; i--)
        {
            X64Emitter.Pop(ref _code, tempRegs[i + 1]);  // args go to R8-R11
            PopEntry();
        }
        // Pop delegate reference
        X64Emitter.Pop(ref _code, tempRegs[0]);  // R7 = delegate
        PopEntry();

        // Reserve shadow space
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Load _firstParameter from delegate (offset 8)
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R7, 8);  // RAX = delegate._firstParameter

        // Load _functionPointer from delegate (offset 32)
        X64Emitter.MovRM(ref _code, VReg.R6, VReg.R7, 32);  // RSI = delegate._functionPointer

        // Check if this is instance or static delegate
        // Instance: _firstParameter != null, call as func(target, arg0, arg1, ...)
        // Static:   _firstParameter == null, call as func(arg0, arg1, ...)
        //
        // We emit code that handles both cases at runtime:
        // 1. Test RAX (== null for static, != null for instance)
        // 2. If null: RCX=arg0, RDX=arg1, R8=arg2, R9=arg3
        // 3. If non-null: RCX=target, RDX=arg0, R8=arg1, R9=arg2
        //
        // For now, emit simpler code: check at runtime and branch

        // test rax, rax (check if _firstParameter is null)
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x85);  // TEST
        _code.EmitByte(0xC0);  // RAX, RAX

        // jz static_path (jump if null - static delegate)
        _code.EmitByte(0x74);  // JZ rel8
        int jmpOffset = _code.Position;
        _code.EmitByte(0x00);  // Placeholder for offset

        // === Instance delegate path ===
        // RCX = _firstParameter (target)
        // RDX = arg0, R8 = arg1, R9 = arg2
        X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);  // RCX = _firstParameter
        if (delegateArgCount >= 1)
            X64Emitter.MovRR(ref _code, VReg.R2, VReg.R8);  // RDX = arg0
        if (delegateArgCount >= 2)
            X64Emitter.MovRR(ref _code, VReg.R3, VReg.R9);  // R8 = arg1
        if (delegateArgCount >= 3)
            X64Emitter.MovRR(ref _code, VReg.R4, VReg.R10);  // R9 = arg2
        // jmp to call
        _code.EmitByte(0xEB);  // JMP rel8
        int jmpToCall = _code.Position;
        _code.EmitByte(0x00);  // Placeholder

        // === Static delegate path ===
        int staticPath = _code.Position;
        // Patch the jz offset
        _code.PatchByte(jmpOffset, (byte)(staticPath - jmpOffset - 1));

        // For static: RCX = arg0, RDX = arg1, R8 = arg2, R9 = arg3
        if (delegateArgCount >= 1)
            X64Emitter.MovRR(ref _code, VReg.R1, VReg.R8);  // RCX = arg0
        if (delegateArgCount >= 2)
            X64Emitter.MovRR(ref _code, VReg.R2, VReg.R9);  // RDX = arg1
        if (delegateArgCount >= 3)
            X64Emitter.MovRR(ref _code, VReg.R3, VReg.R10);  // R8 = arg2
        if (delegateArgCount >= 4)
            X64Emitter.MovRR(ref _code, VReg.R4, VReg.R11);  // R9 = arg3

        // === Call site ===
        int callSite = _code.Position;
        // Patch jmp to call offset
        _code.PatchByte(jmpToCall, (byte)(callSite - jmpToCall - 1));

        // Call through function pointer (in R6/RSI)
        X64Emitter.CallR(ref _code, VReg.R6);
        RecordSafePoint();

        // Restore shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Handle return value
        switch (method.ReturnKind)
        {
            case ReturnKind.Void:
                break;

            case ReturnKind.Int32:
                _code.EmitByte(0x48);  // REX.W
                _code.EmitByte(0x63);  // MOVSXD
                _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;

            case ReturnKind.Float32:
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float32);
                break;

            case ReturnKind.Float64:
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float64);
                break;

            default:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;
        }

        return true;
    }

    /// <summary>
    /// Compile a calli instruction (indirect call via function pointer).
    ///
    /// The calli instruction takes a StandAloneSig token that describes the call signature.
    /// IL eval stack before call: [..., arg0, arg1, ..., argN-1, ftnPtr]
    /// IL eval stack after call:  [..., returnValue] (if non-void)
    ///
    /// For now, we use a simplified signature encoding where the token encodes:
    /// - Low byte: argument count
    /// - Bits 8-15: return kind (ReturnKind enum value)
    /// </summary>
    private bool CompileCalli(uint sigToken)
    {
        int argCount;
        ReturnKind returnKind;
        bool hasThis;

        // Check if this is a real StandAloneSig token (0x11xxxxxx)
        uint tableId = (sigToken >> 24) & 0xFF;
        if (tableId == 0x11)
        {
            // Parse the real StandAloneSig signature
            if (!MetadataIntegration.ParseCalliSignature(sigToken, out argCount, out returnKind, out hasThis))
            {
                DebugConsole.Write("[JIT] Calli: failed to parse StandAloneSig 0x");
                DebugConsole.WriteHex(sigToken);
                DebugConsole.WriteLine();
                return false;
            }

            // For instance calls (hasThis), the 'this' pointer is the first argument
            // but it's already counted in the IL arg count on the stack
            // So we don't need to adjust argCount here
        }
        else
        {
            // Legacy test encoding: (ReturnKind << 8) | ArgCount
            argCount = (int)(sigToken & 0xFF);
            returnKind = (ReturnKind)((sigToken >> 8) & 0xFF);
            hasThis = false;
        }

        // Total stack items needed: argCount args + 1 function pointer
        int totalStackItems = argCount + 1;

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalStackItems)
        {
            // DebugConsole.Write("[JIT] Calli: insufficient stack depth ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" for ");
            DebugConsole.WriteDecimal((uint)totalStackItems);
            DebugConsole.WriteLine(" items (args + ftnPtr)");
            return false;
        }

        // First, pop the function pointer from the top of the stack into a safe register
        // We'll use R11 since it's caller-saved and won't be clobbered by arg setup
        X64Emitter.Pop(ref _code, VReg.R6);  // ftnPtr
        PopEntry();

        // Calculate stack args needed (args beyond the first 4 go on stack)
        int stackArgs = argCount > 4 ? argCount - 4 : 0;

        // x64 ABI ALWAYS requires 32 bytes shadow space, even for >4 args
        bool needsShadowSpace = true;

        // Now pop arguments and set up the call (same logic as CompileCall)
        if (argCount == 0)
        {
            // No arguments - just call
        }
        else if (argCount == 1)
        {
            X64Emitter.Pop(ref _code, VReg.R1);
            PopEntry();
        }
        else if (argCount == 2)
        {
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            PopEntry(); PopEntry();
        }
        else if (argCount == 3)
        {
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            PopEntry(); PopEntry(); PopEntry();
        }
        else if (argCount == 4)
        {
            X64Emitter.Pop(ref _code, VReg.R4);    // arg3
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            PopEntry(); PopEntry(); PopEntry(); PopEntry();
        }
        else
        {
            // More than 4 args - need to handle stack args
            // Same strategy as CompileCall: copy args to final positions before adjusting RSP
            //
            // Eval stack layout (after popping ftnPtr):
            //   [RSP + 0]               = argN-1 (top of stack)
            //   [RSP + 8]               = argN-2
            //   ...
            //   [RSP + (N-1)*8]         = arg0 (bottom)
            //
            // x64 ABI requires:
            //   RCX = arg0, RDX = arg1, R8 = arg2, R9 = arg3
            //   [RSP+32] = arg4, [RSP+40] = arg5, etc.

            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;

            // Load register args from eval stack (before RSP changes)
            // arg0 at [RSP + (argCount-1)*8], arg1 at [RSP + (argCount-2)*8], etc.
            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, (argCount - 1) * 8);  // arg0
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, (argCount - 2) * 8);  // arg1
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, (argCount - 3) * 8);   // arg2
            X64Emitter.MovRM(ref _code, VReg.R4, VReg.SP, (argCount - 4) * 8);   // arg3

            // Copy stack args to their final locations (relative to current RSP)
            // Note: shadow space (32 bytes) is allocated AFTER rspAdjust, so positions are
            // calculated relative to the pre-shadow RSP. After shadow space allocation,
            // these will be at [finalRSP + 32], [finalRSP + 40], etc. as required.
            for (int i = 0; i < stackArgs; i++)
            {
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = argCount * 8 - extraStackSpace + i * 8;
                X64Emitter.MovRM(ref _code, VReg.R5, VReg.SP, srcOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, dstOffset, VReg.R5);
            }

            // Now adjust RSP to point to the call frame
            // Final RSP = current RSP + argCount*8 - extraStackSpace
            int rspAdjust = argCount * 8 - extraStackSpace;
            if (rspAdjust > 0)
            {
                X64Emitter.AddRI(ref _code, VReg.SP, rspAdjust);
            }
            else if (rspAdjust < 0)
            {
                X64Emitter.SubRI(ref _code, VReg.SP, -rspAdjust);
            }

            for (int i = 0; i < argCount; i++) PopEntry();
        }

        // Allocate shadow space for calls with 0-4 args
        // x64 ABI requires 32 bytes for the callee to home register arguments
        if (needsShadowSpace)
        {
            X64Emitter.SubRI(ref _code, VReg.SP, 32);
        }

        // Call through the function pointer in R11
        X64Emitter.CallR(ref _code, VReg.R6);

        // Record safe point after call (GC can happen during callee execution)
        RecordSafePoint();

        // Clean up stack space if we allocated any
        if (needsShadowSpace)
        {
            // Deallocate the 32-byte shadow space we allocated for 0-4 args
            X64Emitter.AddRI(ref _code, VReg.SP, 32);
        }
        else if (stackArgs > 0)
        {
            // Deallocate the full call frame (shadow space + extra stack args space)
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            int callFrameSize = 32 + extraStackSpace;
            X64Emitter.AddRI(ref _code, VReg.SP, callFrameSize);
        }

        // Handle return value (same as CompileCall)
        switch (returnKind)
        {
            case ReturnKind.Void:
                // No return value
                break;

            case ReturnKind.Int32:
                // Sign-extend EAX to RAX
                _code.EmitByte(0x48);  // REX.W
                _code.EmitByte(0x63);  // MOVSXD
                _code.EmitByte(0xC0);  // ModRM: RAX, EAX
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;

            case ReturnKind.Float32:
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float32);
                break;

            case ReturnKind.Float64:
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Float64);
                break;

            case ReturnKind.Struct:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Struct(8));
                break;
        }

        return true;
    }

    /// <summary>
    /// Compile cpblk - Copy block of memory.
    /// Stack: ..., destAddr, srcAddr, size -> ...
    /// Calls CPU.MemCopy(dest, src, count).
    /// </summary>
    private bool CompileCpblk()
    {
        if (_evalStackDepth < 3)
        {
            // DebugConsole.WriteLine("[JIT] cpblk: insufficient stack depth");
            return false;
        }

        // Pop size into R8 (third arg)
        X64Emitter.Pop(ref _code, VReg.R3);
        PopEntry();

        // Pop srcAddr into RDX (second arg)
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Pop destAddr into RCX (first arg)
        X64Emitter.Pop(ref _code, VReg.R1);
        PopEntry();

        // Call CPU.MemCopy(dest, src, count)
        delegate*<void*, void*, ulong, void*> memcopyFn = &CPU.MemCopy;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memcopyFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Return value (dest pointer) is discarded - cpblk has no return on IL stack

        return true;
    }

    /// <summary>
    /// Compile initblk - Initialize block of memory.
    /// Stack: ..., addr, value, size -> ...
    /// Calls CPU.MemSet(dest, value, count).
    /// </summary>
    private bool CompileInitblk()
    {
        if (_evalStackDepth < 3)
        {
            // DebugConsole.WriteLine("[JIT] initblk: insufficient stack depth");
            return false;
        }

        // Pop size into R8 (third arg)
        X64Emitter.Pop(ref _code, VReg.R3);
        PopEntry();

        // Pop value into RDX (second arg) - note: IL has int32, MemSet takes byte
        // We just pass it through - the native function will use the low byte
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Pop addr into RCX (first arg)
        X64Emitter.Pop(ref _code, VReg.R1);
        PopEntry();

        // Call CPU.MemSet(dest, value, count)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memsetFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Return value (dest pointer) is discarded - initblk has no return on IL stack

        return true;
    }

    // ==================== Value Type Operations ====================

    /// <summary>
    /// Get size from a type token by resolving it via metadata.
    /// </summary>
    private static int GetTypeSizeFromToken(uint token)
    {
        uint size = MetadataIntegration.GetTypeSize(token);
        return (int)size;
    }

    /// <summary>
    /// Compile initobj - Initialize value type to zero.
    /// Stack: ..., addr -> ...
    /// Zeros 'size' bytes at the address.
    /// </summary>
    private bool CompileInitobj(uint token)
    {
        if (_evalStackDepth < 1)
        {
            // DebugConsole.WriteLine("[JIT] initobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop addr into RCX (first arg for MemSet)
        X64Emitter.Pop(ref _code, VReg.R1);
        PopEntry();

        // Value = 0 (second arg)
        X64Emitter.XorRR(ref _code, VReg.R2, VReg.R2);

        // Size in R8 (third arg)
        X64Emitter.MovRI64(ref _code, VReg.R3, (ulong)size);

        // Reserve shadow space for call (Windows x64 ABI requires 32 bytes)
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call CPU.MemSet(addr, 0, size)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memsetFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Restore shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        return true;
    }

    /// <summary>
    /// Compile sizeof - Push the size of a type onto the stack.
    /// Stack: ... -> ..., size
    /// </summary>
    private bool CompileSizeof(uint token)
    {
        int size = GetTypeSizeFromToken(token);

        // Push size as int32 constant
        X64Emitter.MovRI32(ref _code, VReg.R0, size);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Int32);

        return true;
    }

    /// <summary>
    /// Compile ldobj - Load value type from address.
    /// Stack: ..., addr -> ..., value
    /// For small sizes, we can load directly into a register.
    /// For larger sizes, we copy to the eval stack.
    /// </summary>
    private bool CompileLdobj(uint token)
    {
        if (_evalStackDepth < 1)
        {
            // DebugConsole.WriteLine("[JIT] ldobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop addr into R2 (we'll use R0/R3 for the copy)
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Load value based on size
        switch (size)
        {
            case 1:
                X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R2, 0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;
            case 2:
                X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R2, 0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;
            case 4:
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R2, 0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.Int32);
                break;
            case 8:
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R2, 0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;
            default:
                // Large struct: allocate stack space and copy data
                // Round size up to 8-byte alignment for stack
                int alignedSize = (size + 7) & ~7;

                // Allocate stack space (decrement RSP)
                X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);

                // Copy from [R2] to [RSP]
                // Use R0 as dest (RSP) and R3 for temp
                X64Emitter.MovRR(ref _code, VReg.R0, VReg.SP);

                // Copy 8 bytes at a time
                int offset = 0;
                while (offset + 8 <= size)
                {
                    X64Emitter.MovRM(ref _code, VReg.R3, VReg.R2, offset);
                    X64Emitter.MovMR(ref _code, VReg.R0, offset, VReg.R3);
                    offset += 8;
                }

                // Copy remaining 4 bytes if present
                if (offset + 4 <= size)
                {
                    X64Emitter.MovRM32(ref _code, VReg.R3, VReg.R2, offset);
                    X64Emitter.MovMR32(ref _code, VReg.R0, offset, VReg.R3);
                    offset += 4;
                }

                // Copy remaining 2 bytes if present
                if (offset + 2 <= size)
                {
                    X64Emitter.MovRM16(ref _code, VReg.R3, VReg.R2, offset);
                    X64Emitter.MovMR16(ref _code, VReg.R0, offset, VReg.R3);
                    offset += 2;
                }

                // Copy remaining 1 byte if present
                if (offset < size)
                {
                    X64Emitter.MovRM8(ref _code, VReg.R3, VReg.R2, offset);
                    X64Emitter.MovMR8(ref _code, VReg.R0, offset, VReg.R3);
                }

                // NEW TYPE SYSTEM: Push ONE entry with the actual byte size
                PushEntry(EvalStackEntry.Struct(alignedSize));
                break;
        }

        return true;
    }

    /// <summary>
    /// Compile stobj - Store value type to address.
    /// Stack: ..., addr, value -> ...
    /// </summary>
    private bool CompileStobj(uint token)
    {
        if (_evalStackDepth < 2)
        {
            // DebugConsole.WriteLine("[JIT] stobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Debug: log stobj size
        // DebugConsole.Write("[stobj] size=");
        // DebugConsole.WriteHex((ulong)size);
        // DebugConsole.Write(" depth=");
        // DebugConsole.WriteDecimal((uint)_evalStackDepth);
        // DebugConsole.WriteLine();

        // CLI says stobj expects: ..., dest, src -> ...
        // In CLI notation, rightmost item (src) is TOS, dest is TOS-1
        // First pop (TOS) = src value, Second pop (TOS-1) = dest address
        //
        // IMPORTANT: ldloc handles structs with three cases that we must match:
        // - Small (<=8 bytes): 1 slot containing the value
        // - Medium (9-16 bytes): ALWAYS 2 slots (16 bytes on stack)
        // - Large (>16 bytes): N slots where N = alignedSize/8

        // NEW TYPE SYSTEM: Use PeekEntry to get byte sizes from the rich type stack
        EvalStackEntry srcEntry = PeekEntryAt(0);   // TOS = src value
        EvalStackEntry destEntry = PeekEntryAt(1);  // TOS-1 = dest address
        int srcByteSize = srcEntry.ByteSize;

        if (size > 16)
        {
            // Large struct (>16 bytes): struct data takes alignedSize bytes on physical stack
            int alignedSize = (size + 7) & ~7;

            // RSP points to struct data, dest addr is below it
            X64Emitter.MovRR(ref _code, VReg.R2, VReg.SP);  // R2 = source address
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, srcByteSize);  // R0 = dest address (use actual stack size)

            // Copy from [R2] (src on stack) to [R0] (dest)
            int offset = 0;
            while (offset + 8 <= size)
            {
                X64Emitter.MovRM(ref _code, VReg.R3, VReg.R2, offset);
                X64Emitter.MovMR(ref _code, VReg.R0, offset, VReg.R3);
                offset += 8;
            }
            if (offset + 4 <= size)
            {
                X64Emitter.MovRM32(ref _code, VReg.R3, VReg.R2, offset);
                X64Emitter.MovMR32(ref _code, VReg.R0, offset, VReg.R3);
                offset += 4;
            }
            if (offset + 2 <= size)
            {
                X64Emitter.MovRM16(ref _code, VReg.R3, VReg.R2, offset);
                X64Emitter.MovMR16(ref _code, VReg.R0, offset, VReg.R3);
                offset += 2;
            }
            if (offset < size)
            {
                X64Emitter.MovRM8(ref _code, VReg.R3, VReg.R2, offset);
                X64Emitter.MovMR8(ref _code, VReg.R0, offset, VReg.R3);
            }

            // Pop struct data + dest address from physical stack
            X64Emitter.AddRI(ref _code, VReg.SP, srcByteSize + 8);
            // Pop 2 ENTRIES from eval stack (not slots!)
            PopEntry();  // src value (1 entry)
            PopEntry();  // dest addr (1 entry)
        }
        else if (size > 8)
        {
            // Medium struct (9-16 bytes): 16 bytes on stack
            // RSP points to 16 bytes of struct data, dest addr is at RSP+16
            X64Emitter.MovRR(ref _code, VReg.R2, VReg.SP);  // R2 = source address
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, srcByteSize);  // R0 = dest address (use actual stack size)

            // Copy 16 bytes (even if struct is smaller, ldloc pushed 16)
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.R2, 0);
            X64Emitter.MovMR(ref _code, VReg.R0, 0, VReg.R3);
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.R2, 8);
            X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R3);

            // Pop struct data + dest address from physical stack
            X64Emitter.AddRI(ref _code, VReg.SP, srcByteSize + 8);
            // Pop 2 ENTRIES from eval stack (not slots!)
            PopEntry();  // src value (1 entry)
            PopEntry();  // dest addr (1 entry)
        }
        else
        {
            // Small struct (<=8 bytes): value fits in a register
            // Pop src value into R2
            X64Emitter.Pop(ref _code, VReg.R2);
            PopEntry();

            // Pop dest address into R0
            X64Emitter.Pop(ref _code, VReg.R0);
            PopEntry();

            // Store value based on size
            switch (size)
            {
                case 1:
                    X64Emitter.MovMR8(ref _code, VReg.R0, 0, VReg.R2);
                    break;
                case 2:
                    X64Emitter.MovMR16(ref _code, VReg.R0, 0, VReg.R2);
                    break;
                case 4:
                    X64Emitter.MovMR32(ref _code, VReg.R0, 0, VReg.R2);
                    break;
                case 8:
                default:
                    X64Emitter.MovMR(ref _code, VReg.R0, 0, VReg.R2);
                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Compile cpobj - Copy value type from src to dest.
    /// Stack: ..., destAddr, srcAddr -> ...
    /// </summary>
    private bool CompileCpobj(uint token)
    {
        if (_evalStackDepth < 2)
        {
            // DebugConsole.WriteLine("[JIT] cpobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop srcAddr into RDX (second arg for MemCopy)
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Pop destAddr into RCX (first arg for MemCopy)
        X64Emitter.Pop(ref _code, VReg.R1);
        PopEntry();

        // Size in R8 (third arg)
        X64Emitter.MovRI64(ref _code, VReg.R3, (ulong)size);

        // Call CPU.MemCopy(dest, src, size)
        delegate*<void*, void*, ulong, void*> memcopyFn = &CPU.MemCopy;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memcopyFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        return true;
    }

    // ==================== Exception Handling Operations ====================

    /// <summary>
    /// Compile leave/leave.s - Exit a try or catch block.
    /// The leave instruction empties the evaluation stack and branches to the target.
    /// If leaving a try block with a finally handler, the finally is executed first.
    /// </summary>
    /// <param name="targetIL">IL offset to branch to after leaving</param>
    /// <param name="leaveILOffset">IL offset of the leave instruction itself</param>
    private bool CompileLeave(int targetIL, int leaveILOffset)
    {
        // Leave empties the evaluation stack (reset to 0)
        // We don't need to emit pops since we're jumping away and the
        // target IL expects an empty stack
        _evalStackDepth = 0;

        if (_compilingFunclet)
        {
            // In a funclet, leave exits by returning from the funclet.
            // The exception dispatch code sets up the return address to be
            // the leave target address.
            // Funclet prolog did: push rbp; mov rbp, rdx
            // We must NOT pop rbp because the leave target expects RBP to be
            // the parent frame pointer (which was set from RDX). Instead, we
            // skip over the saved rbp on the stack.
            // Emit: add rsp, 8; ret
            _code.EmitByte(0x48);  // REX.W
            _code.EmitByte(0x83);  // add r/m64, imm8
            _code.EmitByte(0xC4);  // ModRM: reg=0 (add), r/m=4 (RSP)
            _code.EmitByte(0x08);  // imm8 = 8
            _code.EmitByte(0xC3);  // ret
        }
        else
        {
            // In main method, check if we're leaving a try block with a finally handler
            // If so, we need to call the finally funclet first
            int finallyClauseIdx = FindEnclosingFinallyClause(leaveILOffset);

            if (finallyClauseIdx >= 0)
            {
                // We're leaving a try block that has a finally handler.
                // Emit a call to the finally funclet. The funclet expects:
                //   RDX = parent frame pointer (RBP)
                // We'll emit: sub rsp, 32; mov rdx, rbp; call <funclet>; add rsp, 32
                // The call target will be patched after funclet compilation.

                // CRITICAL: Allocate 32-byte shadow space before call
                // Without this, the callee's shadow space corrupts caller's stack data!
                X64Emitter.SubRI(ref _code, VReg.SP, 32);

                // mov rdx, rbp  (48 89 EA)
                _code.EmitByte(0x48);
                _code.EmitByte(0x89);
                _code.EmitByte(0xEA);

                // call rel32 (E8 xx xx xx xx)
                // Record patch location for later
                _code.EmitByte(0xE8);
                int patchOffset = _code.Position;
                _code.EmitInt32(0);  // Placeholder for rel32

                // Record this call for patching after funclet compilation
                if (_finallyCallPatches != null && _finallyCallCount < MaxFinallyCalls)
                {
                    int idx = _finallyCallCount * 2;
                    _finallyCallPatches[idx + 0] = patchOffset;
                    _finallyCallPatches[idx + 1] = finallyClauseIdx;
                    _finallyCallCount++;
                }

                // Deallocate shadow space
                X64Emitter.AddRI(ref _code, VReg.SP, 32);
            }

            // Jump to target (after finally has run, if any)
            int jmpPatchOffset = X64Emitter.JmpRel32(ref _code);
            RecordBranch(_ilOffset, targetIL, jmpPatchOffset);
        }

        return true;
    }

    /// <summary>
    /// Find a Finally clause that encloses the given IL offset.
    /// Returns the clause index, or -1 if not found.
    /// </summary>
    private int FindEnclosingFinallyClause(int ilOffset)
    {
        if (_ehClauseData == null) return -1;

        for (int i = 0; i < _ehClauseCount; i++)
        {
            int idx = i * 6;
            int flags = _ehClauseData[idx + 0];
            int tryStart = _ehClauseData[idx + 1];
            int tryEnd = _ehClauseData[idx + 2];

            // Check if this is a Finally clause
            if (flags != (int)ILExceptionClauseFlags.Finally)
                continue;

            // Check if ilOffset is within the try block
            if (ilOffset >= tryStart && ilOffset < tryEnd)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Compile throw - Throw an exception.
    /// Stack: ..., exception -> ...
    /// Pops the exception object and calls the runtime exception thrower.
    /// Control does not return from this instruction (unless the exception is caught).
    /// </summary>
    private bool CompileThrow()
    {
        if (_evalStackDepth < 1)
        {
            // DebugConsole.WriteLine("[JIT] throw: insufficient stack depth");
            return false;
        }

        // Pop exception object into RCX (first arg for RhpThrowEx)
        X64Emitter.Pop(ref _code, VReg.R1);
        PopEntry();

        // Debug: Print the exception object address and its MT before throwing
        // mov rax, rcx (save exception ptr)
        // X64Emitter.MovRR(ref _code, VReg.R0, VReg.R1);
        // Actually, let's call a debug helper that prints the object details
        // For now, skip this - can use GDB instead

        // Call RhpThrowEx (defined in native.asm)
        // This captures context and dispatches the exception
        // It does not return normally - it unwinds to a handler
        var throwFn = CPU.GetThrowExFuncPtr();
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)throwFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // RhpThrowEx does not return, but we add int3 for safety
        X64Emitter.Int3(ref _code);

        return true;
    }

    /// <summary>
    /// Compile rethrow - Rethrow the current exception.
    /// This is only valid inside a catch handler.
    /// Stack: ... -> ... (no change - current exception is rethrown)
    /// </summary>
    private bool CompileRethrow()
    {
        // Call RhpRethrow (defined in native.asm)
        // This rethrows the current exception that's being handled
        var rethrowFn = CPU.GetRethrowFuncPtr();
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)rethrowFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // RhpRethrow does not return
        X64Emitter.Int3(ref _code);

        return true;
    }

    /// <summary>
    /// Compile endfinally (also endfault) - Return from finally/fault handler.
    /// Stack must be empty. Control returns to the runtime which decides
    /// where to continue (either continue unwinding or resume execution).
    ///
    /// For inline finally handlers called during exception handling:
    /// - The EH runtime calls the handler with RBP set up to access locals
    /// - endfinally just does 'ret' to return to the EH runtime
    /// - The EH runtime then continues to the catch handler
    ///
    /// For normal finally execution (no exception):
    /// - The try block completes, finally runs inline
    /// - endfinally just falls through (the ret is never hit because leave jumps over it)
    /// </summary>
    private bool CompileEndfinally()
    {
        if (_evalStackDepth != 0)
        {
            // DebugConsole.Write("[JIT] endfinally: stack not empty (depth=");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.WriteLine(")");
            return false;
        }

        // When compiling a funclet, we need to emit the funclet epilog:
        // pop rbp; ret - to restore RBP and return.
        // When compiling inline handler, just emit 'ret'.
        if (_compilingFunclet)
        {
            // Funclet epilog: pop rbp; ret
            // (matches prolog: push rbp; mov rbp, rdx)
            _code.EmitByte(0x5D);  // pop rbp
        }

        // Emit ret - either as part of funclet epilog or for inline handler
        X64Emitter.Ret(ref _code);

        return true;
    }

    // ==================== Field Access Operations ====================
    //
    // For JIT tests, field tokens encode:
    //   Bits 0-15:  Field offset in bytes
    //   Bits 16-23: Field size (1/2/4/8)
    //   Bits 24-31: Flags (bit 0 = signed for load)
    //
    // For static fields, the token IS the direct address of the static.

    /// <summary>
    /// Decode a test field token into offset and size.
    /// </summary>
    private static void DecodeFieldToken(uint token, out int offset, out int size, out bool signed)
    {
        offset = (int)(token & 0xFFFF);
        size = (int)((token >> 16) & 0xFF);
        signed = ((token >> 24) & 1) != 0;
    }

    /// <summary>
    /// Compile ldfld - Load instance field.
    /// Stack: ..., obj -> ..., value
    /// </summary>
    private bool CompileLdfld(uint token)
    {
        int offset;
        int size;
        bool signed;
        bool isValueType = false;
        int declaringTypeSize = 0;
        bool fieldTypeIsValueType = false;
        bool isDebugMapBars = _debugAssemblyId == 3 && _debugMethodToken == 0x06000009;

        // Try field resolver first, fall back to test token encoding
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
        {
            offset = field.Offset;
            size = field.Size;
            signed = field.IsSigned;
            isValueType = field.IsDeclaringTypeValueType;
            declaringTypeSize = field.DeclaringTypeSize;
            fieldTypeIsValueType = field.IsFieldTypeValueType;
            // ldfld field resolver debug (verbose - commented out)
            // if (isDebugMapBars)
            // {
            //     DebugConsole.Write("[LdfldDbg] tok=0x");
            //     DebugConsole.WriteHex(token);
            //     DebugConsole.Write(" off=");
            //     DebugConsole.WriteDecimal((uint)offset);
            //     DebugConsole.Write(" size=");
            //     DebugConsole.WriteDecimal((uint)size);
            //     DebugConsole.Write(" declVT=");
            //     DebugConsole.WriteDecimal(isValueType ? 1U : 0U);
            //     DebugConsole.Write(" declSize=");
            //     DebugConsole.WriteDecimal((uint)declaringTypeSize);
            //     DebugConsole.Write(" fieldVT=");
            //     DebugConsole.WriteDecimal(fieldTypeIsValueType ? 1U : 0U);
            //     DebugConsole.WriteLine();
            // }
        }
        else
        {
            DecodeFieldToken(token, out offset, out size, out signed);
        }

        // Check if TOS is a value type (pushed by ldloc on a struct)
        // Use new type system for accurate size tracking
        EvalStackEntry tosEntry = PeekEntry();
        bool tosIsValueType = tosEntry.Kind == EvalStackKind.ValueType;

        // ldfld TOS debug (verbose - commented out)
        // if (isDebugMapBars)
        // {
        //     DebugConsole.WriteDecimal(tosIsValueType ? 1U : 0U);
        //     DebugConsole.Write(" tosBytes=");
        //     DebugConsole.WriteDecimal((uint)tosEntry.ByteSize);
        //     DebugConsole.Write(" depth=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }

        // Handle large value types (>8 bytes) that are on the stack from ldloc
        // For these, the struct data is spread across multiple 8-byte slots on the eval stack.
        // We need to access the field directly from the stack, then clean up all slots.
        // IMPORTANT: Only take this path if TOS is actually a value type (from ldloc on struct),
        // NOT if TOS is a pointer (from ldarg.0 which passes 'this' by reference for structs).

        // Debug: log all ldfld calls for assembly 3 (verbose - commented out)
        // if (_debugAssemblyId == 3)
        // {
        //     DebugConsole.WriteDecimal(isValueType ? 1U : 0U);
        //     DebugConsole.Write(" declSz=");
        //     DebugConsole.WriteDecimal((uint)declaringTypeSize);
        //     DebugConsole.Write(" fldSz=");
        //     DebugConsole.WriteDecimal((uint)size);
        //     DebugConsole.Write(" fldVT=");
        //     DebugConsole.WriteDecimal(fieldTypeIsValueType ? 1U : 0U);
        //     DebugConsole.Write(" tosKind=");
        //     DebugConsole.WriteDecimal((byte)tosEntry.Kind);
        //     DebugConsole.Write(" tosBytes=");
        //     DebugConsole.WriteDecimal((uint)tosEntry.ByteSize);
        //     DebugConsole.Write(" off=");
        //     DebugConsole.WriteDecimal((uint)offset);
        //     DebugConsole.Write(" d=");
        //     DebugConsole.WriteDecimal((uint)_evalStackDepth);
        //     DebugConsole.WriteLine();
        // }

        // Check if we have a multi-slot value type on the stack using new type system
        // The entry's ByteSize tells us directly - no need to iterate slots
        int structByteSize = 0;

        if (tosEntry.Kind == EvalStackKind.ValueType && tosEntry.ByteSize > 8)
        {
            // New type system tells us directly
            structByteSize = tosEntry.ByteSize;
        }
        else if (isValueType && declaringTypeSize > 8 && tosEntry.Kind == EvalStackKind.ValueType)
        {
            // Metadata gives us declaring type size (legacy fallback)
            structByteSize = (declaringTypeSize + 7) & ~7;
        }

        if (structByteSize > 8)
        {
            // Debug: log when taking the inline large VT path (verbose - commented out)
            // if (_debugAssemblyId == 3)
            // {
            //     DebugConsole.WriteDecimal((uint)offset);
            //     DebugConsole.Write(" sz=");
            //     DebugConsole.WriteDecimal((uint)size);
            //     DebugConsole.Write(" declSz=");
            //     DebugConsole.WriteDecimal((uint)declaringTypeSize);
            //     DebugConsole.Write(" stackBytes=");
            //     DebugConsole.WriteDecimal((uint)structByteSize);
            //     DebugConsole.WriteLine();
            // }

            // The field is at [RSP + fieldOffset], not requiring a dereference of a pointer
            // Load the field value directly from the stack
            switch (size)
            {
                case 1:
                    if (signed)
                        X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.SP, offset);
                    else
                        X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.SP, offset);
                    break;
                case 2:
                    if (signed)
                        X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.SP, offset);
                    else
                        X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.SP, offset);
                    break;
                case 4:
                    if (signed)
                        X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.SP, offset);
                    else
                        X64Emitter.MovRM32(ref _code, VReg.R0, VReg.SP, offset);
                    break;
                case 8:
                default:
                    X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, offset);
                    break;
            }

            // Clean up the entire struct from the stack using tracked ByteSize
            X64Emitter.AddRI(ref _code, VReg.SP, tosEntry.ByteSize);

            // Pop the struct entry (ONE entry, not multiple slots)
            PopEntry();

            // Push the field value result
            X64Emitter.Push(ref _code, VReg.R0);
            if (fieldTypeIsValueType && size > 0 && size <= 8)
                PushEntry(EvalStackEntry.Struct(size));
            else
                PushEntry(EvalStackEntry.Int32);

            return true;
        }

        // Pop the value/reference into R0
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Removed verbose ldfld debug output - bug was in callvirt argument preservation

        // Save struct pointer for debug tracing (only for ref fields from value types)
        bool needDebugLdfld = _debugLdfld != null && isValueType && !fieldTypeIsValueType && size == 8;
        if (needDebugLdfld)
        {
            // Save original pointer to R9 (VReg.R9 = x64 R13, callee-saved)
            X64Emitter.MovRR(ref _code, VReg.R9, VReg.R0);
        }

        // ldfld conditions debug (verbose - commented out)
        // if (_debugAssemblyId == 3)
        // {
        //     DebugConsole.WriteDecimal(fieldTypeIsValueType ? 1U : 0U);
        //     DebugConsole.Write(" size=");
        //     DebugConsole.WriteDecimal((uint)size);
        //     DebugConsole.Write(" tosIsVT=");
        //     DebugConsole.WriteDecimal(tosIsValueType ? 1U : 0U);
        //     DebugConsole.Write(" off=");
        //     DebugConsole.WriteDecimal((uint)offset);
        //     DebugConsole.WriteLine();
        // }

        // If the field itself is a large value type (>8 bytes) and we have an object/pointer on the stack,
        // copy the entire struct to the eval stack (similar to ldobj).
        if (fieldTypeIsValueType && size > 8 && !tosIsValueType)
        {
            // Large struct field debug (verbose - commented out)
            // DebugConsole.WriteDecimal((uint)offset);
            // DebugConsole.Write(" size=");
            // DebugConsole.WriteDecimal((uint)size);
            // DebugConsole.Write(" aligned=");
            // DebugConsole.WriteDecimal((uint)((size + 7) & ~7));
            // DebugConsole.WriteLine();
            int alignedFieldSize = (size + 7) & ~7;
            int fieldSlots = alignedFieldSize / 8;

            // R0 currently points to the object/struct; add field offset
            if (offset != 0)
                X64Emitter.AddRI(ref _code, VReg.R0, offset);

            // Make room on the eval stack and copy the struct
            X64Emitter.SubRI(ref _code, VReg.SP, alignedFieldSize);

            int copyOffset = 0;
            while (copyOffset + 8 <= size)
            {
                X64Emitter.MovRM(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 8;
            }
            if (copyOffset + 4 <= size)
            {
                X64Emitter.MovRM32(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= size)
            {
                X64Emitter.MovRM16(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.SP, copyOffset, VReg.R1);
                copyOffset += 2;
            }
            if (copyOffset < size)
            {
                X64Emitter.MovRM8(ref _code, VReg.R1, VReg.R0, copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.SP, copyOffset, VReg.R1);
            }

            // Push ONE entry with the actual byte size (not N slots!)
            // This is the key fix: large struct is one logical value with known size.
            PushEntry(EvalStackEntry.Struct(size));

            return true;
        }

        // Handle value types that fit in a register (<=8 bytes)
        // When a small struct is loaded with ldloc, the VALUE is on the stack, not a pointer.
        // We need to extract the field by bit shifting, not by memory dereference.
        //
        // Detection: TOS is explicitly a ValueType (from ldloc on a value type).
        // If TOS is a pointer/ref type (from ldloca, ldarg for byref, etc), we dereference.
        bool treatAsInlineValue = tosIsValueType &&
                                   isValueType && declaringTypeSize > 0 && declaringTypeSize <= 8;

        if (treatAsInlineValue)
        {
            // Value type is inline in R0 - extract field by shifting
            // In little-endian, field at offset N starts at bit (N * 8)
            if (offset > 0)
            {
                // shr rax, (offset * 8)
                X64Emitter.ShrRI(ref _code, VReg.R0, (byte)(offset * 8));
            }

            // Mask/extend to field size
            switch (size)
            {
                case 1:
                    if (signed)
                        X64Emitter.MovsxByteReg(ref _code, VReg.R0, VReg.R0);  // movsx rax, al
                    else
                        X64Emitter.MovzxByteReg(ref _code, VReg.R0, VReg.R0);  // movzx rax, al
                    break;
                case 2:
                    if (signed)
                        X64Emitter.MovsxWordReg(ref _code, VReg.R0, VReg.R0);  // movsx rax, ax
                    else
                        X64Emitter.MovzxWordReg(ref _code, VReg.R0, VReg.R0);  // movzx rax, ax
                    break;
                case 4:
                    if (signed)
                        X64Emitter.MovsxdReg(ref _code, VReg.R0, VReg.R0);  // movsxd rax, eax
                    else
                    {
                        // mov eax, eax zero-extends to 64-bit
                        X64Emitter.MovRR32(ref _code, VReg.R0, VReg.R0);
                    }
                    break;
                case 8:
                default:
                    // Already have the full 8 bytes
                    break;
            }
        }
        else
        {
            // Standard case: R0 contains a pointer, dereference to get field
            // Load field at obj + offset
            // mov RAX, [RAX + offset]
            // For sizes that don't match a native load size, use the next larger one.
            // Sizes 3 should use 4-byte load; sizes 5,6,7 should use 8-byte load.
            // The extra bytes loaded are garbage but won't affect the value type since
            // subsequent field accesses use the correct byte offsets.

            // Debug ldfld for reference type fields from value types
            if (isValueType && !fieldTypeIsValueType && size == 8)
            {
                DebugConsole.Write("[ldfld] ref field from VT: tok=0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" off=");
                DebugConsole.WriteDecimal((uint)offset);
                DebugConsole.Write(" declIsVT=Y declSize=");
                DebugConsole.WriteDecimal((uint)declaringTypeSize);
                DebugConsole.WriteLine();
            }

            // Removed verbose ldfld offset 16 debug tracing

            if (size == 1)
            {
                if (signed)
                    X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, offset);
            }
            else if (size == 2)
            {
                if (signed)
                    X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, offset);
            }
            else if (size <= 4)
            {
                // Sizes 3 and 4 use 4-byte load
                if (signed)
                    X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, offset); // Zero-extends to 64-bit
            }
            else
            {
                // Sizes 5, 6, 7, 8 use 8-byte load
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, offset);
            }

            // Runtime debug: trace ldfld for reference fields from value types (like DefaultInterpolatedStringHandler._builder)
            if (needDebugLdfld)
            {
                // Save R0 (loaded value) - we need it for both the debug call and the result
                X64Emitter.Push(ref _code, VReg.R0);

                // Allocate shadow space
                X64Emitter.SubRI(ref _code, VReg.SP, 32);

                // Set up args: RCX = original ptr (saved in R9/x64-R13), RDX = offset, R8 = loaded value
                X64Emitter.MovRR(ref _code, VReg.R1, VReg.R9);  // RCX = original struct ptr
                X64Emitter.MovRI32(ref _code, VReg.R2, offset);  // RDX = offset
                X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, 32);  // R8 (VReg.R3) = value (from saved R0)

                // Call DebugLdfld
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_debugLdfld);
                X64Emitter.CallR(ref _code, VReg.R0);

                // Deallocate shadow space
                X64Emitter.AddRI(ref _code, VReg.SP, 32);

                // Restore R0
                X64Emitter.Pop(ref _code, VReg.R0);
            }

        }

        // Push result
        X64Emitter.Push(ref _code, VReg.R0);

        // If loading a small value type field, mark TOS as value type so subsequent ldfld
        // knows to treat it as inline data, not a pointer
        if (fieldTypeIsValueType && size > 0 && size <= 8)
            PushEntry(EvalStackEntry.Struct(size));
        else
            PushEntry(EvalStackEntry.Int32);

        return true;
    }

    /// <summary>
    /// Compile ldflda - Load field address.
    /// Stack: ..., obj -> ..., &field
    /// </summary>
    private bool CompileLdflda(uint token)
    {
        int offset;

        // Try field resolver first, fall back to test token encoding
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
        {
            offset = field.Offset;
        }
        else
        {
            DecodeFieldToken(token, out offset, out _, out _);
        }

        // Pop object reference
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Compute field address: obj + offset
        if (offset != 0)
        {
            X64Emitter.AddRI(ref _code, VReg.R0, offset);
        }

        // Push address (pointer type)
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Ptr);

        return true;
    }

    /// <summary>
    /// Compile stfld - Store instance field.
    /// Stack: ..., obj, value -> ...
    /// For large structs (>8 bytes), value occupies multiple eval stack slots.
    /// </summary>
    private bool CompileStfld(uint token)
    {
        int offset;
        int size;
        bool isFieldTypeValueType = true;  // Default to true (safer)

        // Try field resolver first, fall back to test token encoding
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
        {
            offset = field.Offset;
            size = field.Size;
            isFieldTypeValueType = field.IsFieldTypeValueType;

            // DEBUG: trace stfld resolution
            DebugConsole.Write("[stfld] token=0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" off=");
            DebugConsole.WriteDecimal(offset);
            DebugConsole.Write(" size=");
            DebugConsole.WriteDecimal(size);
            DebugConsole.Write(" isValType=");
            DebugConsole.Write(isFieldTypeValueType ? "Y" : "N");
            DebugConsole.WriteLine();
        }
        else
        {
            DecodeFieldToken(token, out offset, out size, out _);
        }

        // CRITICAL: For reference-type fields (class fields), force size=8 (pointer)
        // regardless of what the field resolver returned. Reference types are always
        // stored as 8-byte pointers, never as inline data.
        if (!isFieldTypeValueType && size > 8)
        {
            DebugConsole.Write("[stfld] FIXING ref type field size ");
            DebugConsole.WriteDecimal(size);
            DebugConsole.WriteLine(" -> 8");
            size = 8;
        }

        // For large structs, the value takes multiple stack slots
        // Stack layout for stfld of 32-byte struct:
        //   [RSP+0]  = struct bytes 0-7
        //   [RSP+8]  = struct bytes 8-15
        //   [RSP+16] = struct bytes 16-23
        //   [RSP+24] = struct bytes 24-31
        //   [RSP+32] = obj pointer
        // We need to copy the struct to [obj+offset] and clean up all slots

        if (size > 16)
        {
            // Large struct: value is on stack (TOS), obj is below it (TOS-1)
            // Use new type system to get actual sizes
            var valueEntry = PeekEntry();     // TOS = struct value
            var objEntry = PeekEntryAt(1);    // TOS-1 = object pointer
            int valueBytes = valueEntry.ByteSize;  // Actual bytes from type system

            // Load obj pointer (it's below the struct value)
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, valueBytes);  // RAX = obj

            // Optional runtime trace for large struct stfld
            if (_debugStfld != null)
            {
                // Preserve R0 (obj) across the call
                X64Emitter.Push(ref _code, VReg.R0);

                // RCX = obj, RDX = offset
                X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);
                X64Emitter.MovRI32(ref _code, VReg.R2, offset);

                // Shadow space
                X64Emitter.SubRI(ref _code, VReg.SP, 32);
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_debugStfld);
                X64Emitter.CallR(ref _code, VReg.R0);
                X64Emitter.AddRI(ref _code, VReg.SP, 32);

                X64Emitter.Pop(ref _code, VReg.R0);
            }

            // Copy struct value to [obj+offset]
            // For efficiency, copy 8 bytes at a time
            int valueSlots = valueBytes / 8;
            for (int i = 0; i < valueSlots; i++)
            {
                X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, i * 8);  // load 8 bytes
                X64Emitter.MovMR(ref _code, VReg.R0, offset + i * 8, VReg.R2);  // store to field
            }

            // Clean up stack: struct value + obj pointer
            int totalStackBytes = valueBytes + objEntry.ByteSize;
            X64Emitter.AddRI(ref _code, VReg.SP, totalStackBytes);

            // Pop both entries from type system
            PopEntry();  // struct value
            PopEntry();  // obj pointer

            return true;
        }
        else if (size > 8)
        {
            // Medium struct (9-16 bytes): value on stack + obj below
            // Use new type system for accurate sizes
            var valueEntry = PeekEntry();
            var objEntry = PeekEntryAt(1);
            int valueBytes = valueEntry.ByteSize;

            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, valueBytes);  // RAX = obj

            // Copy first 8 bytes
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, 0);
            X64Emitter.MovMR(ref _code, VReg.R0, offset, VReg.R2);

            // Copy second 8 bytes (or partial for 9-15 byte structs)
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, 8);
            X64Emitter.MovMR(ref _code, VReg.R0, offset + 8, VReg.R2);

            // Clean up stack using actual entry sizes
            int totalStackBytes = valueBytes + objEntry.ByteSize;
            X64Emitter.AddRI(ref _code, VReg.SP, totalStackBytes);

            PopEntry();  // struct value
            PopEntry();  // obj pointer

            return true;
        }

        // Small values (1-8 bytes): single slot
        // Pop value and object reference
        X64Emitter.Pop(ref _code, VReg.R2);  // value
        X64Emitter.Pop(ref _code, VReg.R0);  // obj
        PopEntry();
        PopEntry();

        // === RUNTIME DEBUG: Emit call to DebugStfld(objPtr, offset) ===
        // Trace all stfld operations to debug null pointer crash
        if (_debugStfld != null)
        {
            // Allocate shadow space + save area FIRST to avoid corruption
            // Layout: [RSP+0..31] = shadow space, [RSP+32] = saved value, [RSP+40] = saved obj
            X64Emitter.SubRI(ref _code, VReg.SP, 48);  // 32 shadow + 16 save area
            X64Emitter.MovMR(ref _code, VReg.SP, 40, VReg.R0);  // save obj at [RSP+40]
            X64Emitter.MovMR(ref _code, VReg.SP, 32, VReg.R2);  // save value at [RSP+32]

            // Set up args: RCX = objPtr (from R0), RDX = offset
            X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);  // RCX = RAX (obj)
            X64Emitter.MovRI32(ref _code, VReg.R2, offset);  // RDX = offset

            // Call DebugStfld (shadow space already allocated)
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_debugStfld);
            X64Emitter.CallR(ref _code, VReg.R0);

            // Restore saved values and deallocate
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, 32);  // restore value
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 40);  // restore obj
            X64Emitter.AddRI(ref _code, VReg.SP, 48);
        }
        // === END RUNTIME DEBUG ===

        // Store value at obj + offset
        switch (size)
        {
            case 1:
                X64Emitter.MovMR8(ref _code, VReg.R0, offset, VReg.R2);
                break;
            case 2:
                X64Emitter.MovMR16(ref _code, VReg.R0, offset, VReg.R2);
                break;
            case 4:
                X64Emitter.MovMR32(ref _code, VReg.R0, offset, VReg.R2);
                break;
            case 8:
            default:
                X64Emitter.MovMR(ref _code, VReg.R0, offset, VReg.R2);
                break;
        }

        return true;
    }

    /// <summary>
    /// Emit inline code to check and run the static constructor for a type if needed.
    /// Called at JIT compile time; emits code that runs at runtime.
    ///
    /// Generated code:
    ///   mov r8, contextAddress
    ///   mov r9, [r8]           ; Load cctorMethodAddress
    ///   test r9, r9
    ///   jz skip_cctor          ; Already run (address is 0)
    ///   mov qword [r8], 0      ; Mark as running/complete
    ///   call r9                ; Call the cctor
    /// skip_cctor:
    /// </summary>
    /// <param name="contextAddress">Address of the StaticClassConstructionContext.</param>
    private void EmitCctorCheck(nint contextAddress)
    {
        // Load context address into R8
        X64Emitter.MovRI64(ref _code, VReg.R8, (ulong)contextAddress);

        // Load cctorMethodAddress from context into R9
        X64Emitter.MovRM(ref _code, VReg.R9, VReg.R8, 0);

        // Test if cctorMethodAddress is zero (already run)
        X64Emitter.TestRR(ref _code, VReg.R9, VReg.R9);

        // Jump to skip if zero (JE = jump if equal/zero)
        int jzPatchOffset = X64Emitter.JccRel32(ref _code, X64Emitter.CC_E);

        // Clear the context (mark as complete) before calling
        // mov qword [R8], 0
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);
        X64Emitter.MovMR(ref _code, VReg.R8, 0, VReg.R0);

        // Call the cctor (address is in R9)
        X64Emitter.CallR(ref _code, VReg.R9);

        // Patch the jz to jump here (skip_cctor)
        int skipOffset = _code.Position;
        X64Emitter.PatchJump(ref _code, jzPatchOffset, skipOffset);
    }

    /// <summary>
    /// Compile ldsfld - Load static field.
    /// For testing: token is direct address of static.
    /// Stack: ... -> ..., value
    /// </summary>
    private bool CompileLdsfld(uint token)
    {
        ulong staticAddr;
        int size;
        bool signed;
        uint declaringTypeToken = 0;
        uint declaringTypeAsmId = 0;

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
            size = field.Size;
            signed = field.IsSigned;
            declaringTypeToken = field.DeclaringTypeToken;
            declaringTypeAsmId = field.DeclaringTypeAssemblyId;
        }
        else
        {
            // For testing, token is address of static field (assume 8-byte)
            staticAddr = token;
            size = 8;
            signed = false;
        }

        // Emit cctor check if the declaring type has a static constructor
        if (declaringTypeToken != 0)
        {
            nint* cctorContext = MetadataIntegration.EnsureCctorContextRegistered(declaringTypeAsmId, declaringTypeToken);
            if (cctorContext != null)
            {
                EmitCctorCheck((nint)cctorContext);
            }
        }

        // Load static address
        X64Emitter.MovRI64(ref _code, VReg.R0, staticAddr);

        // Load value based on size
        switch (size)
        {
            case 1:
                if (signed)
                    X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 2:
                if (signed)
                    X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 4:
                if (signed)
                    X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 8:
            default:
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
                break;
        }

        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);
        return true;
    }

    /// <summary>
    /// Compile ldsflda - Load static field address.
    /// For testing: token is direct address of static.
    /// Stack: ... -> ..., &static
    /// </summary>
    private bool CompileLdsflda(uint token)
    {
        ulong staticAddr;
        uint declaringTypeToken = 0;
        uint declaringTypeAsmId = 0;

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
            declaringTypeToken = field.DeclaringTypeToken;
            declaringTypeAsmId = field.DeclaringTypeAssemblyId;
        }
        else
        {
            // For testing, token is address of static
            staticAddr = token;
        }

        // Emit cctor check if the declaring type has a static constructor
        if (declaringTypeToken != 0)
        {
            nint* cctorContext = MetadataIntegration.EnsureCctorContextRegistered(declaringTypeAsmId, declaringTypeToken);
            if (cctorContext != null)
            {
                EmitCctorCheck((nint)cctorContext);
            }
        }

        X64Emitter.MovRI64(ref _code, VReg.R0, staticAddr);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);
        return true;
    }

    /// <summary>
    /// Compile stsfld - Store static field.
    /// For testing: token is direct address of static.
    /// Stack: ..., value -> ...
    /// </summary>
    private bool CompileStsfld(uint token)
    {
        ulong staticAddr;
        int size;
        uint declaringTypeToken = 0;
        uint declaringTypeAsmId = 0;

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
            size = field.Size;
            declaringTypeToken = field.DeclaringTypeToken;
            declaringTypeAsmId = field.DeclaringTypeAssemblyId;
        }
        else
        {
            // For testing, token is address of static (assume 8-byte)
            staticAddr = token;
            size = 8;
        }

        // Emit cctor check if the declaring type has a static constructor
        // Note: Must happen BEFORE we pop the value from the stack, since cctor may throw
        if (declaringTypeToken != 0)
        {
            nint* cctorContext = MetadataIntegration.EnsureCctorContextRegistered(declaringTypeAsmId, declaringTypeToken);
            if (cctorContext != null)
            {
                EmitCctorCheck((nint)cctorContext);
            }
        }

        // Pop value
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Load static address and store
        X64Emitter.MovRI64(ref _code, VReg.R2, staticAddr);

        switch (size)
        {
            case 1:
                X64Emitter.MovMR8(ref _code, VReg.R2, 0, VReg.R0);
                break;
            case 2:
                X64Emitter.MovMR16(ref _code, VReg.R2, 0, VReg.R0);
                break;
            case 4:
                X64Emitter.MovMR32(ref _code, VReg.R2, 0, VReg.R0);
                break;
            case 8:
            default:
                X64Emitter.MovMR(ref _code, VReg.R2, 0, VReg.R0);
                break;
        }

        return true;
    }

    // ==================== MD Array Operations ====================
    //
    // MD Array layout:
    //   +0:  MethodTable*
    //   +8:  Length (total element count, 4 bytes)
    //   +12: Rank (4 bytes)
    //   +16: Bounds[0], Bounds[1], ... (4 bytes each)
    //   +16+4*rank: LoBounds[0], LoBounds[1], ... (4 bytes each)
    //   +16+8*rank: Elements[0], Elements[1], ...
    //
    // HeaderSize = 16 + 8 * rank (32 for 2D, 40 for 3D)

    /// <summary>
    /// Compile MD array method (Get, Set, Address).
    /// Emits inline code for element access using the RuntimeHelpers functions.
    /// </summary>
    private bool CompileMDArrayMethod(ResolvedMethod method)
    {
        byte rank = method.MDArrayRank;
        ushort elemSize = method.MDArrayElemSize;

        // Currently only support 2D and 3D
        if (rank < 2 || rank > 3)
        {
            DebugConsole.Write("[JIT] MD array: unsupported rank ");
            DebugConsole.WriteDecimal((uint)rank);
            DebugConsole.WriteLine();
            return false;
        }

        switch (method.MDArrayKind)
        {
            case MDArrayMethodKind.Get:
                return CompileMDArrayGet(rank, elemSize);
            case MDArrayMethodKind.Set:
                return CompileMDArraySet(rank, elemSize);
            case MDArrayMethodKind.Address:
                return CompileMDArrayAddress(rank, elemSize);
            default:
                DebugConsole.Write("[JIT] MD array: unexpected method kind ");
                DebugConsole.WriteDecimal((uint)method.MDArrayKind);
                DebugConsole.WriteLine();
                return false;
        }
    }

    /// <summary>
    /// Compile MD array Get method.
    /// Stack (2D): ..., array, i, j -> ..., value
    /// Stack (3D): ..., array, i, j, k -> ..., value
    /// </summary>
    private bool CompileMDArrayGet(byte rank, ushort elemSize)
    {
        int headerSize = 16 + 8 * rank;

        if (rank == 2)
        {
            // Pop: j (top), i, array
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = i * dim1 + j
            // dim1 is at array + 20 (Bounds[1])
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            // Element address = array + headerSize + index * elemSize
            // For now, use multiply for all sizes
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = array + header + index*elemSize

            // Load element value
            EmitMDArrayLoad(VReg.R1, elemSize);
        }
        else // rank == 3
        {
            // Pop: k (top), j, i, array
            X64Emitter.Pop(ref _code, VReg.R4);   // R9 = k
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = (i * dim1 + j) * dim2 + k
            // dim1 is at array + 20, dim2 is at array + 24
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 24);  // RAX = dim2
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = (i*dim1+j) * dim2
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R4);         // RDX = index + k

            // Element address = array + headerSize + index * elemSize
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = array + header + index*elemSize

            // Load element value
            EmitMDArrayLoad(VReg.R1, elemSize);
        }

        return true;
    }

    /// <summary>
    /// Emit code to load an MD array element and push onto eval stack.
    /// </summary>
    private void EmitMDArrayLoad(VReg addrReg, ushort elemSize)
    {
        switch (elemSize)
        {
            case 1:
                X64Emitter.MovzxByte(ref _code, VReg.R0, addrReg, 0);
                break;
            case 2:
                X64Emitter.MovzxWord(ref _code, VReg.R0, addrReg, 0);
                break;
            case 4:
                X64Emitter.MovRM32(ref _code, VReg.R0, addrReg, 0);
                break;
            case 8:
            default:
                X64Emitter.MovRM(ref _code, VReg.R0, addrReg, 0);
                break;
        }
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);
    }

    /// <summary>
    /// Compile MD array Set method.
    /// Stack (2D): ..., array, i, j, value -> ...
    /// Stack (3D): ..., array, i, j, k, value -> ...
    /// </summary>
    private bool CompileMDArraySet(byte rank, ushort elemSize)
    {
        int headerSize = 16 + 8 * rank;

        if (rank == 2)
        {
            // Pop: value (top), j, i, array
            // Use callee-saved registers to preserve values across calculations
            X64Emitter.Pop(ref _code, VReg.R8);   // R12 = value
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = i * dim1 + j
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            // Element address = array + headerSize + index * elemSize
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = element address

            // Store value
            EmitMDArrayStore(VReg.R1, VReg.R8, elemSize);
        }
        else // rank == 3
        {
            // Pop: value (top), k, j, i, array
            X64Emitter.Pop(ref _code, VReg.R8);   // R12 = value
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R4);   // R9 = k
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = (i * dim1 + j) * dim2 + k
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 24);  // RAX = dim2
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = (i*dim1+j) * dim2
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R4);         // RDX = index + k

            // Element address = array + headerSize + index * elemSize
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = element address

            // Store value
            EmitMDArrayStore(VReg.R1, VReg.R8, elemSize);
        }

        return true;
    }

    /// <summary>
    /// Emit code to store a value to an MD array element.
    /// </summary>
    private void EmitMDArrayStore(VReg addrReg, VReg valueReg, ushort elemSize)
    {
        switch (elemSize)
        {
            case 1:
                X64Emitter.MovMR8(ref _code, addrReg, 0, valueReg);
                break;
            case 2:
                X64Emitter.MovMR16(ref _code, addrReg, 0, valueReg);
                break;
            case 4:
                X64Emitter.MovMR32(ref _code, addrReg, 0, valueReg);
                break;
            case 8:
            default:
                X64Emitter.MovMR(ref _code, addrReg, 0, valueReg);
                break;
        }
    }

    /// <summary>
    /// Compile MD array Address method.
    /// Stack (2D): ..., array, i, j -> ..., &element
    /// Stack (3D): ..., array, i, j, k -> ..., &element
    /// </summary>
    private bool CompileMDArrayAddress(byte rank, ushort elemSize)
    {
        int headerSize = 16 + 8 * rank;

        if (rank == 2)
        {
            // Pop: j (top), i, array
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = i * dim1 + j
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            // Element address = array + headerSize + index * elemSize
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = element address

            // Push address
            X64Emitter.Push(ref _code, VReg.R1);
            PushEntry(EvalStackEntry.NativeInt);
        }
        else // rank == 3
        {
            // Pop: k (top), j, i, array
            X64Emitter.Pop(ref _code, VReg.R4);   // R9 = k
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R3);   // R8 = j
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R2);   // RDX = i
            PopEntry();
            X64Emitter.Pop(ref _code, VReg.R1);   // RCX = array
            PopEntry();

            // Calculate linear index: index = (i * dim1 + j) * dim2 + k
            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 20);  // RAX = dim1
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = i * dim1
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R3);         // RDX = i * dim1 + j

            X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 24);  // RAX = dim2
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = (i*dim1+j) * dim2
            X64Emitter.AddRR(ref _code, VReg.R2, VReg.R4);         // RDX = index + k

            // Element address = array + headerSize + index * elemSize
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)elemSize);
            X64Emitter.ImulRR(ref _code, VReg.R2, VReg.R0);        // RDX = index * elemSize
            X64Emitter.AddRR(ref _code, VReg.R1, VReg.R2);         // RCX = array + index*elemSize
            X64Emitter.AddRI(ref _code, VReg.R1, headerSize);      // RCX = element address

            // Push address
            X64Emitter.Push(ref _code, VReg.R1);
            PushEntry(EvalStackEntry.NativeInt);
        }

        return true;
    }

    // ==================== Array Operations ====================
    //
    // Array layout (NativeAOT):
    //   +0:  MethodTable*
    //   +8:  Length (native int = 8 bytes)
    //   +16: Elements[0], Elements[1], ...

    private const int ArrayLengthOffset = 8;
    private const int ArrayDataOffset = 16;

    /// <summary>
    /// Compile ldlen - Load array length.
    /// Stack: ..., array -> ..., length
    /// </summary>
    private bool CompileLdlen()
    {
        // Pop array reference
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Load length from array+8
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, ArrayLengthOffset);

        // Push length
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// Emit bounds check for array access.
    /// Compares index (unsigned) against array length and triggers INT 5 if out of bounds.
    /// Uses R3 as scratch register.
    /// </summary>
    /// <param name="arrayReg">Register containing array reference</param>
    /// <param name="indexReg">Register containing index</param>
    private void EmitArrayBoundsCheck(VReg arrayReg, VReg indexReg)
    {
        // Load array length from array+8
        X64Emitter.MovRM(ref _code, VReg.R3, arrayReg, ArrayLengthOffset);

        // Compare index with length (unsigned comparison)
        X64Emitter.CmpRR(ref _code, indexReg, VReg.R3);

        // JB skip_int5 (Jump if Below, unsigned) - index < length means OK
        // JB rel8 = 0x72 rel8
        // If index >= length (unsigned), we fall through to INT 5
        _code.EmitByte(0x72);  // JB
        _code.EmitByte(0x02);  // rel8 = +2 (skip INT 5)

        // INT 5 - triggers IndexOutOfRangeException
        _code.EmitByte(0xCD);  // INT
        _code.EmitByte(0x05);  // imm8 = 5 (array bounds exceeded)
    }

    /// <summary>
    /// Compile ldelem.* - Load array element.
    /// Stack: ..., array, index -> ..., value
    /// </summary>
    private bool CompileLdelem(int elemSize, bool signed)
    {
        // Pop index and array
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + 16 + index * elemSize
        // For now, use shift for power-of-2 sizes
        switch (elemSize)
        {
            case 1:
                // RAX = array + 16 + index
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                if (signed)
                    X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 2:
                // RAX = array + 16 + index * 2
                X64Emitter.ShlImm(ref _code, VReg.R1, 1);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                if (signed)
                    X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 4:
                // RAX = array + 16 + index * 4
                X64Emitter.ShlImm(ref _code, VReg.R1, 2);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                if (signed)
                    X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.R0, 0);
                else
                    X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 8:
            default:
                // RAX = array + 16 + index * 8
                X64Emitter.ShlImm(ref _code, VReg.R1, 3);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
                break;
        }

        // Push value
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// Compile stelem.* - Store array element.
    /// Stack: ..., array, index, value -> ...
    /// </summary>
    private bool CompileStelem(int elemSize)
    {
        // Pop value, index, array
        X64Emitter.Pop(ref _code, VReg.R2);  // value
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + 16 + index * elemSize
        switch (elemSize)
        {
            case 1:
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                X64Emitter.MovMR8(ref _code, VReg.R0, 0, VReg.R2);
                break;
            case 2:
                X64Emitter.ShlImm(ref _code, VReg.R1, 1);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                X64Emitter.MovMR16(ref _code, VReg.R0, 0, VReg.R2);
                break;
            case 4:
                X64Emitter.ShlImm(ref _code, VReg.R1, 2);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                X64Emitter.MovMR32(ref _code, VReg.R0, 0, VReg.R2);
                break;
            case 8:
            default:
                X64Emitter.ShlImm(ref _code, VReg.R1, 3);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
                X64Emitter.MovMR(ref _code, VReg.R0, 0, VReg.R2);
                break;
        }

        return true;
    }

    /// <summary>
    /// Compile ldelem.r4/r8 - Load float/double array element.
    /// Stack: ..., array, index -> ..., value
    /// </summary>
    private bool CompileLdelemFloat(int elemSize)
    {
        // Pop index and array
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + 16 + index * elemSize
        if (elemSize == 4)
        {
            // RAX = array + 16 + index * 4
            X64Emitter.ShlImm(ref _code, VReg.R1, 2);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
            X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
            // Load float into XMM0, then move to RAX for push
            X64Emitter.MovssRM(ref _code, RegXMM.XMM0, VReg.R0, 0);
            // Move XMM0 to RAX (as 32-bit, zero-extended)
            X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }
        else
        {
            // RAX = array + 16 + index * 8
            X64Emitter.ShlImm(ref _code, VReg.R1, 3);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
            X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
            // Load double into XMM0, then move to RAX for push
            X64Emitter.MovsdRM(ref _code, RegXMM.XMM0, VReg.R0, 0);
            // Move XMM0 to RAX (as 64-bit)
            X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        }

        // Push value
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(elemSize == 4 ? EvalStackEntry.Float32 : EvalStackEntry.Float64);

        return true;
    }

    /// <summary>
    /// Compile stelem.r4/r8 - Store float/double array element.
    /// Stack: ..., array, index, value -> ...
    /// </summary>
    private bool CompileStelemFloat(int elemSize)
    {
        // Pop value, index, array
        X64Emitter.Pop(ref _code, VReg.R2);  // value (float/double bit pattern)
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + 16 + index * elemSize
        if (elemSize == 4)
        {
            // RAX = array + 16 + index * 4
            X64Emitter.ShlImm(ref _code, VReg.R1, 2);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
            X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
            // Move RDX to XMM0, then store as float
            X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R2);
            X64Emitter.MovssMR(ref _code, VReg.R0, 0, RegXMM.XMM0);
        }
        else
        {
            // RAX = array + 16 + index * 8
            X64Emitter.ShlImm(ref _code, VReg.R1, 3);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
            X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
            // Move RDX to XMM0, then store as double
            X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R2);
            X64Emitter.MovsdMR(ref _code, VReg.R0, 0, RegXMM.XMM0);
        }

        return true;
    }

    /// <summary>
    /// Compile ldelema - Load element address.
    /// Stack: ..., array, index -> ..., &elem
    /// Token is a TypeSpec/TypeDef/TypeRef that encodes the element type.
    /// </summary>
    private bool CompileLdelema(uint token)
    {

        // Resolve the element size from the type token using metadata
        uint elemSize = MetadataIntegration.GetTypeSize(token);

        // Debug: trace ldelema element size
        DebugConsole.Write("[ldelema] token=0x");
        DebugConsole.WriteHex(token);
        DebugConsole.Write(" elemSize=");
        DebugConsole.WriteDecimal(elemSize);
        DebugConsole.WriteLine();

        // Pop index and array
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + ArrayDataOffset + index * elemSize
        // For generic sizes, use imul: R1 = index * elemSize
        if (elemSize == 1)
        {
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
        }
        else if (elemSize == 2)
        {
            X64Emitter.ShlImm(ref _code, VReg.R1, 1);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
        }
        else if (elemSize == 4)
        {
            X64Emitter.ShlImm(ref _code, VReg.R1, 2);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
        }
        else if (elemSize == 8)
        {
            X64Emitter.ShlImm(ref _code, VReg.R1, 3);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
        }
        else
        {
            // General case: use imul for arbitrary element sizes
            // imul R1, R1, elemSize -> R1 = index * elemSize
            X64Emitter.ImulRI(ref _code, VReg.R1, (int)elemSize);
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
        }
        X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);

        // Push address
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    // === Object allocation operations ===

    /// <summary>
    /// newobj - Allocate new object and call constructor.
    /// Stack: ..., arg1, ..., argN -> ..., obj
    ///
    /// Two modes of operation:
    /// 1. Constructor token registered via RegisterConstructor:
    ///    - Resolves constructor to get MethodTable and native code
    ///    - Allocates via RhpNewFast using the MethodTable
    ///    - Calls constructor with allocated object as 'this'
    ///
    /// 2. Fallback (simplified testing):
    ///    - Token = MethodTable* (direct pointer to MT)
    ///    - Just allocates via RhpNewFast, no constructor call
    /// </summary>
    private bool CompileNewobj(uint token)
    {
        // Try to resolve as a registered constructor
        ResolvedMethod ctor;
        bool resolved = ResolveMethod(token, out ctor);

        // Special case: Factory-style constructors (like String..ctor)
        // These have NativeCode but no MethodTable - they allocate and return the object themselves
        if (resolved && ctor.IsValid && ctor.NativeCode != null && ctor.MethodTable == null && !ctor.HasThis)
        {
            // Factory method: just call it with the args and it returns the new object
            int factoryArgs = ctor.ArgCount;

            if (factoryArgs > 4)
            {
                DebugConsole.WriteLine("[JIT] newobj factory: too many args (max 4)");
                return false;
            }

            // Pop args from eval stack into argument registers
            // x64: RCX, RDX, R8, R9 for first 4 args
            VReg[] argRegs = { VReg.R1, VReg.R2, VReg.R3, VReg.R4 };
            VReg[] tempRegs = { VReg.R7, VReg.R8, VReg.R9, VReg.R10 };

            // Pop in reverse order (last arg on top of stack)
            for (int i = factoryArgs - 1; i >= 0; i--)
            {
                X64Emitter.Pop(ref _code, tempRegs[i]);
                PopEntry();
            }

            // Reserve shadow space
            X64Emitter.SubRI(ref _code, VReg.SP, 32);

            // Move saved args to their calling convention positions
            for (int i = 0; i < factoryArgs; i++)
            {
                X64Emitter.MovRR(ref _code, argRegs[i], tempRegs[i]);
            }

            // Call the factory method
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)ctor.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
            RecordSafePoint();

            // Restore shadow space
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // Push return value (in RAX) onto eval stack
            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(EvalStackEntry.NativeInt);

            return true;
        }

        if (resolved && ctor.IsValid && ctor.MethodTable != null)
        {
            // Check if this is a value type constructor
            MethodTable* mt = (MethodTable*)ctor.MethodTable;
            bool isValueType = mt->IsValueType;

            // newobj value type debug (verbose - commented out)
            // DebugConsole.Write(isValueType ? "Y" : "N");
            // DebugConsole.Write(" baseSize=");
            // DebugConsole.WriteDecimal(mt->BaseSize);
            // DebugConsole.WriteLine();

            // Special case: Delegate constructor
            // Delegates have "runtime managed" constructors with no IL body
            // Stack: ..., target, functionPointer
            // The runtime provides the implementation: allocate delegate, set _firstParameter and _functionPointer
            if (mt->IsDelegate)
            {
                return CompileNewobjDelegate(mt, ctor.ArgCount);
            }

            // Special case: MD array constructor
            // MD arrays are allocated by runtime helpers, not regular constructors
            if (ctor.IsMDArrayMethod && ctor.MDArrayKind == MDArrayMethodKind.Ctor)
            {
                return CompileNewobjMDArray(mt, ctor.MDArrayRank, ctor.MDArrayElemSize);
            }

            // We have a constructor with MethodTable - full newobj implementation
            // Stack before: ..., arg1, ..., argN (constructor args, not including 'this')
            // Stack after: ..., obj (reference type) or value (value type)

            int ctorArgs = ctor.ArgCount;  // Not including 'this'

            // Step 1: Pop constructor arguments from eval stack into temporary storage
            // We need to save them because we'll call RhpNewFast first (for ref types)
            // For simplicity, move them to callee-saved registers (we saved them in prologue)
            // RBX, R12, R13, R14 are callee-saved
            // Max 4 args for now (beyond that would need stack spill)

            if (ctorArgs > 4)
            {
                DebugConsole.WriteLine("[JIT] newobj: too many constructor args (max 4)");
                return false;
            }

            // Pop args into temp registers (in reverse order since last arg is on top)
            // Args will go: arg0->RBX, arg1->R12, arg2->R13, arg3->R14
            VReg[] tempRegs = { VReg.R7, VReg.R8, VReg.R9, VReg.R10 };
            for (int i = ctorArgs - 1; i >= 0; i--)
            {
                X64Emitter.Pop(ref _code, tempRegs[i]);
                PopEntry();
            }

            // CRITICAL: Reserve shadow space to protect any live eval stack data.
            // After popping args, there may still be items on the RSP-based eval stack
            // (e.g., from 'dup' in object initializers). These items are at [RSP] and above.
            // When we call RhpNewFast/constructor, the callee's shadow space overlaps this!
            // Fix: move RSP down 32 bytes to put live data safely below shadow space.
            X64Emitter.SubRI(ref _code, VReg.SP, 32);

            int newobjTempOffset = -(_localCount * 8 + 64);

            if (isValueType)
            {
                // VALUE TYPE: Allocate space on stack, call constructor
                // Value types don't use RhpNewFast - they're stack allocated
                // We'll use a temp slot in the frame for the value

                // Get the actual size of the value type from its MethodTable
                uint vtSize = mt->BaseSize;
                int alignedVtSize = (int)((vtSize + 7) & ~7UL);  // Round up to 8-byte alignment

                // Adjust newobjTempOffset to account for larger structs
                // newobjTempOffset is the start of the temp area, growing toward lower addresses
                // We need space for the full struct, so adjust the base if needed
                int vtBaseOffset = newobjTempOffset - (alignedVtSize - 8);

                // Zero the entire value type temp slot
                // Fields are at [base + 0], [base + 8], etc.
                X64Emitter.MovRI64(ref _code, VReg.R0, 0);
                for (int off = 0; off < alignedVtSize; off += 8)
                {
                    X64Emitter.MovMR(ref _code, VReg.FP, vtBaseOffset + off, VReg.R0);
                }

                // RCX = pointer to the value (for 'this' parameter)
                // LEA RCX, [RBP + vtBaseOffset]
                X64Emitter.Lea(ref _code, VReg.R1, VReg.FP, vtBaseOffset);
            }
            else
            {
                // REFERENCE TYPE: Allocate on heap via RhpNewFast
                // newobj reference type debug (verbose - commented out)
                // DebugConsole.Write("[JIT newobj] refType: RhpNewFast=0x");
                // DebugConsole.WriteHex((ulong)_rhpNewFast);
                // DebugConsole.Write(" MT=0x");
                // DebugConsole.WriteHex((ulong)ctor.MethodTable);
                // DebugConsole.WriteLine();

                X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)ctor.MethodTable);
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
                X64Emitter.CallR(ref _code, VReg.R0);
                RecordSafePoint();

                // RAX now contains the new object pointer
                // Save to temp slot (nested newobj would clobber registers)
                X64Emitter.MovMR(ref _code, VReg.FP, newobjTempOffset, VReg.R0);

                // RCX = 'this' pointer (the new object)
                X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);
            }

            // Step 3: Call the constructor with 'this' in RCX
            // x64 calling convention: RCX=this, RDX=arg0, R8=arg1, R9=arg2, stack=arg3+

            // Move saved args to their calling convention positions
            // arg0 in RBX -> RDX
            // arg1 in R12 -> R8
            // arg2 in R13 -> R9
            // arg3 in R14 -> stack (push before call)
            if (ctorArgs >= 1)
                X64Emitter.MovRR(ref _code, VReg.R2, VReg.R7);
            if (ctorArgs >= 2)
                X64Emitter.MovRR(ref _code, VReg.R3, VReg.R8);
            if (ctorArgs >= 3)
                X64Emitter.MovRR(ref _code, VReg.R4, VReg.R9);
            if (ctorArgs >= 4)
            {
                // Push 4th arg to stack (shadow space slot)
                // Actually for x64, we use stack slot at RSP+32 for 5th arg
                // But since constructor has 'this' as hidden first arg,
                // arg3 is the 5th param, goes to stack
                X64Emitter.MovMR(ref _code, VReg.SP, 32, VReg.R10);  // shadow space [rsp+32]
            }

            // newobj ctor call debug (verbose - commented out)
            // DebugConsole.WriteHex((ulong)ctor.NativeCode);
            // DebugConsole.Write(" args=");
            // DebugConsole.WriteDecimal((uint)ctorArgs);
            // DebugConsole.WriteLine();

            if (ctor.NativeCode == null)
            {
                // DebugConsole.WriteLine("[JIT newobj] ERROR: ctor.NativeCode is null!");
                return false;
            }

            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)ctor.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
            RecordSafePoint();

            // Restore RSP after the shadow space reservation
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // Push result onto eval stack
            if (isValueType)
            {
                // For value types, push the entire struct onto the eval stack
                uint vtSize = mt->BaseSize;
                int alignedVtSize = (int)((vtSize + 7) & ~7UL);
                // Calculate the base offset (same formula as above)
                int vtBaseOffset = newobjTempOffset - (alignedVtSize - 8);

                if (alignedVtSize <= 8)
                {
                    // Small struct: fits in one register/stack slot
                    X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, vtBaseOffset);
                    X64Emitter.Push(ref _code, VReg.R0);
                    PushEntry(EvalStackEntry.NativeInt);
                }
                else
                {
                    // Larger struct: push all 8-byte chunks in reverse order
                    // (so first chunk ends up at lowest address on stack)
                    // Fields are at [vtBaseOffset + 0], [vtBaseOffset + 8], etc.
                    for (int off = alignedVtSize - 8; off >= 0; off -= 8)
                    {
                        X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, vtBaseOffset + off);
                        X64Emitter.Push(ref _code, VReg.R0);
                    }
                    // Track as one entry with the struct size (consistent with ldloc for structs)
                    PushEntry(EvalStackEntry.Struct(alignedVtSize));
                }
            }
            else
            {
                // Reference type: push the object pointer
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, newobjTempOffset);
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
            }

            return true;
        }

        // Failed to resolve newobj token - this is a compilation error
        DebugConsole.Write("[JIT newobj] FAIL: unresolved token 0x");
        DebugConsole.WriteHex(token);
        DebugConsole.WriteLine();
        return false;
    }

    /// <summary>
    /// Compile delegate constructor (runtime-provided).
    /// Stack: ..., target, functionPointer -> ..., delegate
    ///
    /// Delegate constructors are "runtime managed" with no IL body.
    /// The runtime provides: allocate delegate, set _firstParameter and _functionPointer.
    ///
    /// Delegate layout (from Delegate base class):
    /// - Offset 0: MethodTable*
    /// - Offset 8: _firstParameter (object) - target for instance delegates, null/this for static
    /// - Offset 16: _helperObject (object)
    /// - Offset 24: _extraFunctionPointerOrData (nint)
    /// - Offset 32: _functionPointer (IntPtr) - the actual method to invoke
    /// </summary>
    private bool CompileNewobjDelegate(MethodTable* mt, int ctorArgCount)
    {
        // Delegate.ctor(object target, IntPtr functionPointer)
        // Should have exactly 2 arguments
        if (ctorArgCount != 2)
        {
            DebugConsole.Write("[JIT] newobj delegate: unexpected arg count ");
            DebugConsole.WriteDecimal((uint)ctorArgCount);
            DebugConsole.WriteLine();
            return false;
        }

        // Pop args from eval stack: functionPointer first, then target
        // Stack order: ..., target, functionPointer (functionPointer on top)
        // VReg.R8 = x64 R12 (callee-saved), VReg.R9 = x64 R13 (callee-saved)
        X64Emitter.Pop(ref _code, VReg.R8);   // R12 = functionPointer
        PopEntry();
        X64Emitter.Pop(ref _code, VReg.R9);   // R13 = target
        PopEntry();

        // Reserve shadow space
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Allocate delegate object: RhpNewFast(MethodTable* mt)
        X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)mt);  // RCX = MT
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
        X64Emitter.CallR(ref _code, VReg.R0);
        RecordSafePoint();
        // RAX = new delegate object

        // Store _firstParameter (offset 8) = target
        // For instance delegates, this is the target object
        // For static delegates, this could be null or the delegate itself
        // The C# compiler pushes null for static methods
        X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R9);   // [RAX+8] = R13

        // Store _functionPointer (offset 32) = functionPointer
        X64Emitter.MovMR(ref _code, VReg.R0, 32, VReg.R8);  // [RAX+32] = R12

        // Restore shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Push delegate object onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// Compile newobj for multi-dimensional arrays.
    /// Stack: ..., dim0, dim1, ... dimN-1 -> ..., array
    /// Calls NewMDArray2D or NewMDArray3D runtime helper.
    /// </summary>
    private bool CompileNewobjMDArray(MethodTable* mt, byte rank, ushort elemSize)
    {
        // Currently support 2D and 3D arrays
        if (rank < 2 || rank > 3)
        {
            DebugConsole.Write("[JIT] newobj MD array: unsupported rank ");
            DebugConsole.WriteDecimal((uint)rank);
            DebugConsole.WriteLine();
            return false;
        }

        // Pop dimension args into temp registers (in reverse order)
        // Stack order: ..., dim0, dim1 (dim1 on top for 2D)
        VReg[] tempRegs = { VReg.R7, VReg.R8, VReg.R9 };  // RBX, R12, R13
        for (int i = rank - 1; i >= 0; i--)
        {
            X64Emitter.Pop(ref _code, tempRegs[i]);
            PopEntry();
        }

        // Reserve shadow space
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Set up call to NewMDArrayND helper
        // Signature: void* NewMDArrayND(MethodTable* pMT, int dim0, int dim1, ...)
        X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)mt);  // RCX = MT
        X64Emitter.MovRR(ref _code, VReg.R2, tempRegs[0]);  // RDX = dim0
        X64Emitter.MovRR(ref _code, VReg.R3, tempRegs[1]);  // R8 = dim1

        void* helperPtr;
        if (rank == 2)
        {
            helperPtr = RuntimeHelpers.GetMDArray2DHelperPtr();
        }
        else // rank == 3
        {
            X64Emitter.MovRR(ref _code, VReg.R4, tempRegs[2]);  // R9 = dim2
            helperPtr = RuntimeHelpers.GetMDArray3DHelperPtr();
        }

        // Call the helper
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)helperPtr);
        X64Emitter.CallR(ref _code, VReg.R0);
        RecordSafePoint();

        // Restore shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Push array reference onto eval stack (in RAX)
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// newarr - Allocate new array.
    /// Stack: ..., numElements -> ..., array
    ///
    /// For simplified testing:
    /// - Token encodes: bits 0-15 = element size, bits 16-31 = array element MT address (high bits)
    /// - Actually for testing, token = array MethodTable* address directly
    ///
    /// Calls RhpNewArray(MethodTable* pMT, int numElements).
    /// </summary>
    private bool CompileNewarr(uint token)
    {
        // Resolve token to ARRAY MethodTable* address
        // The newarr token is for the ELEMENT type, but we need an ARRAY MethodTable
        // with proper ComponentSize for RhpNewArray to allocate correctly.
        ulong mtAddress = token;  // Fallback: use token directly (for testing)

        // Use the array element type resolver to get an array MethodTable
        // This resolves the element type and creates an array MT with correct ComponentSize
        void* resolved;
        if (MetadataIntegration.ResolveArrayElementType(token, out resolved) && resolved != null)
        {
            mtAddress = (ulong)resolved;
        }
        else if (_typeResolver != null)
        {
            // Fallback to old behavior if ResolveArrayElementType fails
            // This may not work correctly for struct arrays but maintains backward compatibility
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                mtAddress = (ulong)resolved;
            }
        }

        // Pop numElements from stack into RDX (second arg)
        X64Emitter.Pop(ref _code, VReg.R2);
        PopEntry();

        // Load MethodTable* into RCX (first arg for RhpNewArray)
        X64Emitter.MovRI64(ref _code, VReg.R1, mtAddress);

        // CRITICAL: Reserve shadow space to protect any live eval stack data.
        // After popping numElements, there may still be items on the RSP-based eval stack
        // (e.g., 'this' in a constructor doing: ldarg.0, ldc.i4 N, newarr, stfld).
        // When we call RhpNewArray, the callee's shadow space overlaps this!
        // Fix: move RSP down 32 bytes to put live data safely below shadow space.
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call RhpNewArray(MethodTable* pMT, int numElements) -> returns array pointer in RAX
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewArray);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during allocation)
        RecordSafePoint();

        // Restore RSP after the shadow space reservation
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Push the allocated array reference onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    // === Boxing/Unboxing ===

    /// <summary>
    /// box - Box a value type to an object reference.
    /// Stack: ..., value -> ..., obj
    ///
    /// For simplified testing:
    /// - Token encodes value type size in low 16 bits, boxed MT address must be provided via helper
    /// - Actually for testing, token = MethodTable* address directly, value size from MT._uBaseSize - 8
    ///
    /// Allocates boxed object via RhpNewFast, then copies value into it.
    /// Boxed layout: [MT*][value data] where value starts at offset 8.
    ///
    /// Special handling for Nullable&lt;T&gt;:
    /// - If HasValue is false, box returns null
    /// - If HasValue is true, box just the inner value using T's MethodTable
    /// </summary>
    private bool CompileBox(uint token)
    {
        // Resolve token to MethodTable* address
        // The value type size is derived from BaseSize - 8 (minus MT pointer)
        ulong mtAddress = token;  // Fallback: use token directly (for testing)
        uint valueSize = 8;  // Default: 8 bytes
        bool isNullable = false;
        ulong innerMtAddress = 0;
        uint innerValueSize = 0;

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolvedPtr;
            if (_typeResolver(token, out resolvedPtr) && resolvedPtr != null)
            {
                mtAddress = (ulong)resolvedPtr;
                MethodTable* mt = (MethodTable*)resolvedPtr;
                uint baseSize = mt->BaseSize;
                uint combinedFlags = mt->CombinedFlags;

                DebugConsole.Write("[box] token=0x");
                DebugConsole.WriteHex(token);
                DebugConsole.Write(" mt=0x");
                DebugConsole.WriteHex((uint)mtAddress);
                DebugConsole.Write(" baseSize=");
                DebugConsole.WriteHex(baseSize);
                DebugConsole.Write(" flags=0x");
                DebugConsole.WriteHex(combinedFlags);
                DebugConsole.Write(" isVT=");
                DebugConsole.Write(mt->IsValueType ? "Y" : "N");

                // Check for Nullable<T> - requires special boxing semantics
                if (mt->IsNullable)
                {
                    isNullable = true;
                    DebugConsole.Write(" NULLABLE");

                    // Get the inner type's MethodTable from _relatedType
                    MethodTable* innerMt = mt->_relatedType;
                    if (innerMt != null)
                    {
                        innerMtAddress = (ulong)innerMt;
                        innerValueSize = innerMt->BaseSize - 8;  // Inner value size

                        DebugConsole.Write(" innerMT=0x");
                        DebugConsole.WriteHex((uint)innerMtAddress);
                        DebugConsole.Write(" innerSize=");
                        DebugConsole.WriteHex(innerValueSize);
                    }
                }

                // For value types, we need the raw value size (bytes to copy when boxing).
                // - AOT primitives: ComponentSize is raw size, BaseSize is boxed size (includes +8)
                // - JIT-created structs: ComponentSize is 0, BaseSize IS the raw size (no +8)
                // For reference types (boxing a ref type is a no-op), just return - leave value on stack.
                if (mt->IsValueType)
                {
                    ushort componentSize = mt->_usComponentSize;
                    if (componentSize > 0)
                    {
                        // AOT primitive: ComponentSize is the raw value size
                        valueSize = componentSize;
                    }
                    else
                    {
                        // JIT-created struct: BaseSize IS the raw value size
                        valueSize = baseSize;
                    }
                    DebugConsole.Write(" valueSize=");
                    DebugConsole.WriteHex(valueSize);
                    DebugConsole.WriteLine("");
                }
                else
                {
                    // Reference type - box is a no-op, just leave value on stack
                    DebugConsole.WriteLine(" REF-NOOP");
                    return true;
                }
            }
            else
            {
                DebugConsole.Write("[box] token=0x");
                DebugConsole.WriteHex(token);
                DebugConsole.WriteLine(" RESOLUTION FAILED!");
            }
        }

        // Check if the top entry is a multi-slot struct (ByteSize > 8)
        EvalStackEntry topEntry = PeekEntry();
        bool isMultiSlotStruct = topEntry.Kind == EvalStackKind.ValueType && topEntry.ByteSize > 8;
        int structByteSize = topEntry.ByteSize;

        if (isMultiSlotStruct)
        {
            // Multi-slot struct: data is on the stack, get its address into R3
            // LEA R3, [RSP] - the struct data starts at current RSP
            X64Emitter.Lea(ref _code, VReg.R3, VReg.SP, 0);
            // Don't pop yet - we need the data to stay on the stack while we read it
            // We'll adjust RSP after copying
        }
        else
        {
            // Small value: pop the value itself into R3
            X64Emitter.Pop(ref _code, VReg.R3);
        }
        PopEntry();

        // Handle Nullable<T> boxing specially
        if (isNullable && innerMtAddress != 0)
        {
            bool result = CompileNullableBox(innerMtAddress, innerValueSize, valueSize, isMultiSlotStruct, structByteSize);
            return result;
        }

        // For non-Nullable multi-slot structs, adjust RSP after getting the address
        if (isMultiSlotStruct)
        {
            // Don't adjust RSP yet - normal box path handles this via the copy loop
            // which expects R3 to be the ADDRESS of the value
        }

        // Save R3 (the value/address to box) on the stack - R8 is caller-saved and will be clobbered
        X64Emitter.Push(ref _code, VReg.R3);

        // Load MethodTable* into RCX (first arg for RhpNewFast)
        X64Emitter.MovRI64(ref _code, VReg.R1, mtAddress);

        // Reserve shadow space (32 bytes) for the call
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call RhpNewFast(MethodTable* pMT) -> returns object pointer in RAX
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during allocation)
        RecordSafePoint();

        // Restore RSP after the shadow space reservation
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Restore the value/address into R3
        X64Emitter.Pop(ref _code, VReg.R3);

        // Now RAX = pointer to boxed object
        // Copy the value to offset 8 in the object (after MT pointer)
        if (valueSize <= 8)
        {
            // Small value type: R3 contains the value directly
            // Use correctly-sized store to avoid corrupting neighboring memory
            if (valueSize == 8)
            {
                // 64-bit value (long, ulong, double, pointer)
                X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R3);
            }
            else if (valueSize == 4)
            {
                // 32-bit value (int, uint, float)
                X64Emitter.MovMR32(ref _code, VReg.R0, 8, VReg.R3);
            }
            else if (valueSize == 2)
            {
                // 16-bit value (short, ushort, char)
                X64Emitter.MovMR16(ref _code, VReg.R0, 8, VReg.R3);
            }
            else if (valueSize == 1)
            {
                // 8-bit value (byte, sbyte, bool)
                X64Emitter.MovMR8(ref _code, VReg.R0, 8, VReg.R3);
            }
            else
            {
                // Unusual size (3, 5, 6, 7 bytes) - use byte-by-byte copy from address
                // R3 is expected to be an address in this case
                int copyOffset = 0;
                while (copyOffset + 4 <= (int)valueSize)
                {
                    X64Emitter.MovRM32(ref _code, VReg.R2, VReg.R3, copyOffset);
                    X64Emitter.MovMR32(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                    copyOffset += 4;
                }
                while (copyOffset + 2 <= (int)valueSize)
                {
                    X64Emitter.MovRM16(ref _code, VReg.R2, VReg.R3, copyOffset);
                    X64Emitter.MovMR16(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                    copyOffset += 2;
                }
                while (copyOffset < (int)valueSize)
                {
                    X64Emitter.MovRM8(ref _code, VReg.R2, VReg.R3, copyOffset);
                    X64Emitter.MovMR8(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                    copyOffset += 1;
                }
            }
        }
        else
        {
            // Large struct: R3 contains the source address
            // Need to copy valueSize bytes from [R3] to [RAX + 8]
            // Use R2 (RDX) as scratch register for copying
            int copyOffset = 0;
            while (copyOffset + 8 <= (int)valueSize)
            {
                // mov R2, [R3 + copyOffset]
                X64Emitter.MovRM(ref _code, VReg.R2, VReg.R3, copyOffset);
                // mov [RAX + 8 + copyOffset], R2
                X64Emitter.MovMR(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 8;
            }
            // Handle remaining bytes (4, 2, 1)
            if (copyOffset + 4 <= (int)valueSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= (int)valueSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 2;
            }
            if (copyOffset < (int)valueSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
            }
        }

        // Clean up the original struct data from the stack (for multi-slot structs)
        if (isMultiSlotStruct && structByteSize > 0)
        {
            X64Emitter.AddRI(ref _code, VReg.SP, structByteSize);
        }

        // Push the boxed object reference onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ObjRef);

        return true;
    }

    /// <summary>
    /// Special boxing for Nullable&lt;T&gt;.
    /// - R3 contains address of Nullable&lt;T&gt; struct
    /// - If hasValue (at offset 0) is false, push null
    /// - If hasValue is true, box the inner value (at offset 8) using innerMT
    /// - If isMultiSlotStruct is true, the struct data is on the stack at RSP and needs to be cleaned up
    /// </summary>
    private bool CompileNullableBox(ulong innerMtAddress, uint innerValueSize, uint nullableSize, bool isMultiSlotStruct, int structByteSize)
    {
        // The value offset depends on the nullable size on the eval stack:
        // - For large Nullable (>8 bytes on stack): uses 8-byte aligned layout, value at offset 8
        // - For small Nullable (<=8 bytes on stack): uses struct alignment, value at alignment offset
        int valueOffset;
        if (nullableSize > 8)
        {
            // Large Nullable uses uniform 8-byte aligned layout
            valueOffset = 8;
        }
        else
        {
            // Small Nullable uses natural struct alignment
            if (innerValueSize <= 1) valueOffset = 1;
            else if (innerValueSize <= 2) valueOffset = 2;
            else if (innerValueSize <= 4) valueOffset = 4;
            else valueOffset = 8;
        }

        // For small Nullable<T> (<=8 bytes on stack), R3 contains the VALUE, not an address
        // We need to push it to stack to get a memory address
        bool pushedToStack = false;
        if (!isMultiSlotStruct)
        {
            // Push R3 value to stack so we can access it by address
            X64Emitter.Push(ref _code, VReg.R3);
            // Now RSP points to the Nullable struct data
            X64Emitter.MovRR(ref _code, VReg.R3, VReg.SP);
            pushedToStack = true;
        }

        // Now R3 points to the Nullable<T> struct in memory
        // Load hasValue byte into R2 (RDX): movzx edx, byte [R3]
        X64Emitter.MovRM8(ref _code, VReg.R2, VReg.R3, 0);

        // Test hasValue: test rdx, rdx (test full register to set flags)
        X64Emitter.TestRR(ref _code, VReg.R2, VReg.R2);

        // If hasValue is false (zero), jump to push null
        // je pushNull (Je = Jz when ZF=1)
        int jzPatchOffset = X64Emitter.Je(ref _code);

        // hasValue is true - box the inner value
        // Inner value is at calculated valueOffset in the Nullable struct

        // Save address of inner value (R3 + valueOffset) for after allocation
        X64Emitter.Lea(ref _code, VReg.R3, VReg.R3, valueOffset);

        // Save R3 on stack (inner value address)
        X64Emitter.Push(ref _code, VReg.R3);

        // Load inner type's MethodTable* into RCX
        X64Emitter.MovRI64(ref _code, VReg.R1, innerMtAddress);

        // Reserve shadow space for call
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call RhpNewFast(innerMT) to allocate boxed object
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call
        RecordSafePoint();

        // Restore RSP
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Restore inner value address into R3
        X64Emitter.Pop(ref _code, VReg.R3);

        // RAX = boxed object, R3 = pointer to inner value
        // Copy inner value to offset 8 in boxed object
        if (innerValueSize <= 8)
        {
            // Small value - load and store directly
            if (innerValueSize == 8)
            {
                X64Emitter.MovRM(ref _code, VReg.R2, VReg.R3, 0);
                X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R2);
            }
            else if (innerValueSize == 4)
            {
                X64Emitter.MovRM32(ref _code, VReg.R2, VReg.R3, 0);
                X64Emitter.MovMR32(ref _code, VReg.R0, 8, VReg.R2);
            }
            else if (innerValueSize == 2)
            {
                X64Emitter.MovRM16(ref _code, VReg.R2, VReg.R3, 0);
                X64Emitter.MovMR16(ref _code, VReg.R0, 8, VReg.R2);
            }
            else if (innerValueSize == 1)
            {
                X64Emitter.MovRM8(ref _code, VReg.R2, VReg.R3, 0);
                X64Emitter.MovMR8(ref _code, VReg.R0, 8, VReg.R2);
            }
        }
        else
        {
            // Large value - copy in chunks
            int copyOffset = 0;
            while (copyOffset + 8 <= (int)innerValueSize)
            {
                X64Emitter.MovRM(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 8;
            }
            if (copyOffset + 4 <= (int)innerValueSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= (int)innerValueSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
                copyOffset += 2;
            }
            if (copyOffset < (int)innerValueSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R2, VReg.R3, copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.R0, 8 + copyOffset, VReg.R2);
            }
        }

        // Jump past the null case
        int jmpPatchOffset = X64Emitter.JmpRel32(ref _code);

        // Patch the je (jz) to jump here (pushNull label)
        int pushNullOffset = _code.Position;
        X64Emitter.PatchJump(ref _code, jzPatchOffset, pushNullOffset);

        // hasValue is false - push null
        // xor rax, rax
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);

        // Patch the jmp to jump here (end label)
        int endOffset = _code.Position;
        X64Emitter.PatchJump(ref _code, jmpPatchOffset, endOffset);

        // Clean up the struct data from the stack
        if (isMultiSlotStruct && structByteSize > 0)
        {
            // Multi-slot struct data was on stack
            X64Emitter.AddRI(ref _code, VReg.SP, structByteSize);
        }
        else if (pushedToStack)
        {
            // We pushed R3 (8 bytes) for small Nullable<T>
            X64Emitter.AddRI(ref _code, VReg.SP, 8);
        }

        // Push result (RAX = boxed object or null) onto stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ObjRef);

        return true;
    }

    /// <summary>
    /// Special unboxing for Nullable&lt;T&gt;.
    /// - R3 contains the source object reference (boxed T or null)
    /// - If R3 is null, create Nullable&lt;T&gt; with HasValue=false
    /// - If R3 is non-null, create Nullable&lt;T&gt; with HasValue=true and copy the value
    /// </summary>
    private bool CompileNullableUnbox(ulong innerMtAddress, uint innerValueSize, uint nullableSize)
    {
        // The value offset depends on the nullable size on the eval stack:
        // - For large Nullable (>8 bytes): uses 8-byte aligned layout, value at offset 8
        // - For small Nullable (<=8 bytes): uses struct alignment, value at alignment offset
        int valueOffset;
        if (nullableSize > 8)
        {
            // Large Nullable uses uniform 8-byte aligned layout
            valueOffset = 8;
        }
        else
        {
            // Small Nullable uses natural struct alignment
            if (innerValueSize <= 1) valueOffset = 1;
            else if (innerValueSize <= 2) valueOffset = 2;
            else if (innerValueSize <= 4) valueOffset = 4;
            else valueOffset = 8;
        }

        int alignedNullableSize = ((int)nullableSize + 7) & ~7;
        if (alignedNullableSize < 8)
            alignedNullableSize = 8;  // Minimum 8 bytes for Nullable<T> on stack

        // Allocate space on stack for the Nullable<T> result
        X64Emitter.SubRI(ref _code, VReg.SP, alignedNullableSize);

        // Test if R3 (source object) is null
        X64Emitter.TestRR(ref _code, VReg.R3, VReg.R3);

        // If null, jump to create null Nullable
        int jzPatchOffset = X64Emitter.Je(ref _code);

        // Non-null path: create Nullable with HasValue=true and copy the value
        // Zero the entire struct first
        X64Emitter.MovRI64(ref _code, VReg.R0, 0);
        X64Emitter.MovMR(ref _code, VReg.SP, 0, VReg.R0);  // Zero bytes 0-7
        if (alignedNullableSize > 8)
        {
            // Zero remaining bytes for larger Nullable types
            for (int i = 8; i < alignedNullableSize; i += 8)
            {
                X64Emitter.MovMR(ref _code, VReg.SP, i, VReg.R0);
            }
        }

        // Set hasValue = 1 at [RSP + 0]
        X64Emitter.MovRI64(ref _code, VReg.R0, 1);
        X64Emitter.MovMR8(ref _code, VReg.SP, 0, VReg.R0);

        // Copy the inner value from boxed object [R3 + 8] to Nullable [RSP + valueOffset]
        // Source is always at offset 8 (after MT pointer in boxed object)
        if (innerValueSize <= 8)
        {
            if (innerValueSize == 8)
            {
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R3, 8);
                X64Emitter.MovMR(ref _code, VReg.SP, valueOffset, VReg.R0);
            }
            else if (innerValueSize == 4)
            {
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R3, 8);
                X64Emitter.MovMR32(ref _code, VReg.SP, valueOffset, VReg.R0);
            }
            else if (innerValueSize == 2)
            {
                X64Emitter.MovRM16(ref _code, VReg.R0, VReg.R3, 8);
                X64Emitter.MovMR16(ref _code, VReg.SP, valueOffset, VReg.R0);
            }
            else if (innerValueSize == 1)
            {
                X64Emitter.MovRM8(ref _code, VReg.R0, VReg.R3, 8);
                X64Emitter.MovMR8(ref _code, VReg.SP, valueOffset, VReg.R0);
            }
        }
        else
        {
            // Large inner value - copy in chunks
            int copyOffset = 0;
            while (copyOffset + 8 <= (int)innerValueSize)
            {
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, valueOffset + copyOffset, VReg.R0);
                copyOffset += 8;
            }
            if (copyOffset + 4 <= (int)innerValueSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.SP, valueOffset + copyOffset, VReg.R0);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= (int)innerValueSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.SP, valueOffset + copyOffset, VReg.R0);
                copyOffset += 2;
            }
            if (copyOffset < (int)innerValueSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.SP, valueOffset + copyOffset, VReg.R0);
            }
        }

        // Jump past the null path
        int jmpPatchOffset = X64Emitter.JmpRel32(ref _code);

        // Null path: create Nullable with HasValue=false (all zeros)
        int nullPathOffset = _code.Position;
        X64Emitter.PatchJump(ref _code, jzPatchOffset, nullPathOffset);

        // Zero the entire Nullable struct
        X64Emitter.MovRI64(ref _code, VReg.R0, 0);
        for (int off = 0; off < alignedNullableSize; off += 8)
        {
            X64Emitter.MovMR(ref _code, VReg.SP, off, VReg.R0);
        }

        // End label
        int endOffset = _code.Position;
        X64Emitter.PatchJump(ref _code, jmpPatchOffset, endOffset);

        // Track as a struct entry with the Nullable size
        PushEntry(EvalStackEntry.Struct(alignedNullableSize));

        return true;
    }

    /// <summary>
    /// unbox - Get a managed pointer to the value inside a boxed object.
    /// Stack: ..., obj -> ..., valuePtr
    ///
    /// For simplified testing:
    /// - Token = expected MethodTable* address (for type checking)
    /// - Returns pointer to value at offset 8 in boxed object
    ///
    /// Note: unbox returns a pointer, not the value itself.
    /// </summary>
    private bool CompileUnbox(uint token)
    {
        // Resolve token to MethodTable* address for type validation
        ulong expectedMT = token;  // Fallback: use token directly (for testing)

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                expectedMT = (ulong)resolved;
            }
        }

        // Note: In a full implementation, we'd verify obj->MT matches expectedMT
        _ = expectedMT;  // Currently unused - type validation not implemented

        // Pop object reference from stack
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Calculate pointer to value: obj + 8 (skip MT pointer)
        // lea RAX, [RAX + 8]
        X64Emitter.Lea(ref _code, VReg.R0, VReg.R0, 8);

        // Push the value pointer onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ByRef);

        return true;
    }

    /// <summary>
    /// unbox.any - Unbox to a value (combines unbox + ldobj).
    /// Stack: ..., obj -> ..., value
    ///
    /// For simplified testing:
    /// - Token = expected MethodTable* address
    /// - Loads value from offset 8 in boxed object
    /// - Assumes 64-bit value for simplicity
    ///
    /// This is equivalent to unbox followed by ldobj.
    ///
    /// Special handling for Nullable&lt;T&gt;:
    /// - If target type is Nullable&lt;T&gt; and source is null, create Nullable with HasValue=false
    /// - If target type is Nullable&lt;T&gt; and source is non-null, create Nullable with HasValue=true and the value
    /// </summary>
    private bool CompileUnboxAny(uint token)
    {
        // Resolve token to MethodTable* address for type validation
        ulong expectedMT = token;  // Fallback: use token directly (for testing)
        uint valueSize = 8;  // Default: 8 bytes
        bool isNullable = false;
        ulong innerMtAddress = 0;
        uint innerValueSize = 0;
        uint nullableSize = 0;

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                expectedMT = (ulong)resolved;
                MethodTable* mt = (MethodTable*)resolved;
                // Read BaseSize from MethodTable (offset 4: _uBaseSize)
                uint baseSize = mt->BaseSize;

                // Check for Nullable<T> - requires special unboxing semantics
                if (mt->IsNullable)
                {
                    isNullable = true;

                    // Get the Nullable struct size - must match how boxing determines size
                    // For AOT types: ComponentSize is raw size
                    // For JIT types: BaseSize IS the raw size (no overhead)
                    ushort componentSize = mt->_usComponentSize;
                    if (componentSize > 0)
                        nullableSize = componentSize;
                    else
                        nullableSize = baseSize;

                    // Get the inner type's MethodTable from _relatedType
                    MethodTable* innerMt = mt->_relatedType;
                    if (innerMt != null)
                    {
                        innerMtAddress = (ulong)innerMt;
                        // Inner value size - also depends on type source
                        ushort innerComponentSize = innerMt->_usComponentSize;
                        if (innerComponentSize > 0)
                            innerValueSize = innerComponentSize;
                        else
                            innerValueSize = innerMt->BaseSize;
                    }
                }

                // Token table is in high byte:
                // 0x01 = TypeRef (external type, like System.Int32 from System.Runtime)
                // 0x02 = TypeDef (local type defined in the user assembly)
                // External types have BaseSize that includes 8-byte MT pointer overhead
                // Local types have BaseSize = actual value size (no overhead)
                bool isExternalType = (token >> 24) == 0x01;
                if (isExternalType)
                {
                    // External type: BaseSize includes MT pointer overhead
                    valueSize = baseSize - 8;
                }
                else
                {
                    // Local type: BaseSize IS the value size
                    valueSize = baseSize;
                }
            }
        }

        // Note: In a full implementation, we'd verify obj->MT matches expectedMT
        _ = expectedMT;  // Currently unused - type validation not implemented

        // Pop object reference from stack into R3 (source address)
        X64Emitter.Pop(ref _code, VReg.R3);
        PopEntry();

        // Handle Nullable<T> unboxing specially
        if (isNullable && innerMtAddress != 0)
        {
            return CompileNullableUnbox(innerMtAddress, innerValueSize, nullableSize);
        }

        // Value data is at [R3 + 8] (after MT pointer)
        if (valueSize <= 8)
        {
            // Small struct: load value directly into R0 and push
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R3, 8);
            X64Emitter.Push(ref _code, VReg.R0);
            PushEntry(EvalStackEntry.Struct(8));
        }
        else
        {
            // Large struct: allocate space on eval stack and copy the full data
            // Calculate aligned size for stack (8-byte aligned)
            int alignedSize = ((int)valueSize + 7) & ~7;

            // Allocate space on the stack
            X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);

            // Copy from [R3 + 8] to [RSP]
            int copyOffset = 0;
            while (copyOffset + 8 <= (int)valueSize)
            {
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 8;
            }
            // Handle remaining bytes
            if (copyOffset + 4 <= (int)valueSize)
            {
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR32(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 4;
            }
            if (copyOffset + 2 <= (int)valueSize)
            {
                X64Emitter.MovRM16(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR16(ref _code, VReg.SP, copyOffset, VReg.R0);
                copyOffset += 2;
            }
            if (copyOffset < (int)valueSize)
            {
                X64Emitter.MovRM8(ref _code, VReg.R0, VReg.R3, 8 + copyOffset);
                X64Emitter.MovMR8(ref _code, VReg.SP, copyOffset, VReg.R0);
            }

            // Track as a single struct entry (not multiple slots)
            PushEntry(EvalStackEntry.Struct(alignedSize));
        }

        return true;
    }

    // === Type operations ===

    /// <summary>
    /// castclass - Cast with InvalidCastException on failure.
    /// Stack: ..., obj -> ..., obj
    ///
    /// For simplified testing:
    /// - Token = target MethodTable* address
    /// - Just compares object's MT to target MT
    /// - Throws if mismatch (calls RhpThrowEx)
    ///
    /// Full implementation would check type hierarchy.
    /// </summary>
    private bool CompileCastclass(uint token)
    {
        // Resolve token to MethodTable* address
        ulong targetMT = token;  // Fallback: use token directly (for testing)

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                targetMT = (ulong)resolved;
            }
        }

        // Peek at object on stack (don't pop - result is same object)
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);  // Load obj from stack top

        // Check for null - null passes castclass
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int skipNullCheckPatch = X64Emitter.Je(ref _code);  // Jump if null (ZF=1)

        // Load object's MethodTable (at offset 0) into RCX (arg1)
        X64Emitter.MovRM(ref _code, VReg.R1, VReg.R0, 0);  // obj->MT

        // Load target MT into RDX (arg2)
        X64Emitter.MovRI64(ref _code, VReg.R2, targetMT);

        // Call IsAssignableTo(objectMT, targetMT) -> bool in RAX
        // First: allocate shadow space
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call the helper
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_isAssignableTo);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Deallocate shadow space
        X64Emitter.AddRI(ref _code, VReg.SP, 32);

        // Check result - AL contains bool (0 = false, 1 = true)
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int skipThrowPatch = X64Emitter.Jne(ref _code);  // Jump if result != 0 (cast succeeds)

        // Cast failed - throw InvalidCastException
        // For now, just trap (INT3)
        X64Emitter.Int3(ref _code);  // Debug break on cast failure

        // Patch the jumps to current position
        X64Emitter.PatchRel32(ref _code, skipNullCheckPatch);
        X64Emitter.PatchRel32(ref _code, skipThrowPatch);

        // Object remains on stack unchanged
        return true;
    }

    /// <summary>
    /// isinst - Cast returning null on failure.
    /// Stack: ..., obj -> ..., result (obj or null)
    ///
    /// Token = target MethodTable* address
    /// Calls IsAssignableTo to check type hierarchy.
    /// Returns obj if assignable, null otherwise.
    /// </summary>
    private bool CompileIsinst(uint token)
    {
        // Resolve token to MethodTable* address
        ulong targetMT = token;  // Fallback: use token directly (for testing)

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                targetMT = (ulong)resolved;
            }
        }

        // Pop object from stack into R12 (callee-saved, survives call)
        X64Emitter.Pop(ref _code, VReg.R0);  // Load obj
        PopEntry();
        X64Emitter.MovRR(ref _code, VReg.R8, VReg.R0);  // Save obj in R12

        // Result in R13, start with null
        X64Emitter.XorRR(ref _code, VReg.R9, VReg.R9);

        // Check for null - null returns null
        X64Emitter.TestRR(ref _code, VReg.R8, VReg.R8);
        int skipCheckPatch = X64Emitter.Je(ref _code);  // Jump if null (ZF=1)

        // Load object's MethodTable (at offset 0) into RCX (arg1)
        X64Emitter.MovRM(ref _code, VReg.R1, VReg.R8, 0);  // obj->MT

        // Load target MT into RDX (arg2)
        X64Emitter.MovRI64(ref _code, VReg.R2, targetMT);

        // Call IsAssignableTo(objectMT, targetMT) -> bool in RAX
        X64Emitter.SubRI(ref _code, VReg.SP, 32);  // Shadow space
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_isAssignableTo);
        X64Emitter.CallR(ref _code, VReg.R0);
        X64Emitter.AddRI(ref _code, VReg.SP, 32);  // Pop shadow space

        // Check result - AL contains bool (0 = false, 1 = true)
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int skipSetPatch = X64Emitter.Je(ref _code);  // Jump if result == 0 (not assignable)

        // Assignable - result is obj
        X64Emitter.MovRR(ref _code, VReg.R9, VReg.R8);

        // Patch jumps to current position
        X64Emitter.PatchRel32(ref _code, skipCheckPatch);
        X64Emitter.PatchRel32(ref _code, skipSetPatch);

        // Push result (obj or null in R13)
        X64Emitter.Push(ref _code, VReg.R9);
        PushEntry(EvalStackEntry.NativeInt);  // Objects are 64-bit pointers

        return true;
    }

    /// <summary>
    /// ldftn - Load function pointer for a method.
    /// Stack: ... -> ..., ftnPtr
    ///
    /// For simplified testing:
    /// - Token encodes the function pointer address directly
    ///   (in production would resolve method token to address)
    ///
    /// This allows creating delegates and indirect calls via calli.
    /// </summary>
    private bool CompileLdftn(uint token)
    {
        // Resolve the method token using the method resolver
        if (_resolver == null)
        {
            DebugConsole.Write("[JIT] ldftn: no method resolver for token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        ResolvedMethod resolved;
        if (!_resolver(token, out resolved) || !resolved.IsValid)
        {
            DebugConsole.Write("[JIT] ldftn: failed to resolve token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // For ldftn, we need a direct function pointer, not an indirect reference.
        // If the method isn't compiled yet, we need to emit code that loads
        // from the registry entry at runtime (since it may be compiled by then).
        ulong fnPtr = 0;

        if (resolved.NativeCode != null)
        {
            // Method is already compiled - use direct address
            fnPtr = (ulong)resolved.NativeCode;
            DebugConsole.Write("[ldftn] token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" -> fnPtr=0x");
            DebugConsole.WriteHex(fnPtr);
            DebugConsole.WriteLine();
            X64Emitter.MovRI64(ref _code, VReg.R0, fnPtr);
        }
        else if (resolved.RegistryEntry != null)
        {
            // Method is registered but not yet compiled.
            // For delegates, we need the actual function pointer at delegate creation time.
            // Emit code to load from the registry entry at runtime.
            // By the time the ldftn executes, the target method should be compiled.
            CompiledMethodInfo* entry = (CompiledMethodInfo*)resolved.RegistryEntry;
            DebugConsole.Write("[ldftn] token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.Write(" -> INDIRECT via registry 0x");
            DebugConsole.WriteHex((ulong)entry);
            DebugConsole.WriteLine();

            // Emit: mov rax, [registry + 8]  ; Load NativeCode from registry
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)entry);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 8);  // RAX = entry->NativeCode
        }
        else
        {
            DebugConsole.Write("[JIT] ldftn: no native code or registry for token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // Push the function pointer onto the stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);  // Function pointers are native int

        return true;
    }

    /// <summary>
    /// ldvirtftn - Load virtual function pointer for a method.
    /// Stack: ..., obj -> ..., ftnPtr
    ///
    /// This opcode loads a function pointer for a virtual method, using the
    /// runtime type of the object to determine the actual method implementation.
    ///
    /// For virtual methods with vtable slot info:
    /// 1. Pop object reference into RAX
    /// 2. Load MethodTable* from [obj]
    /// 3. Load function pointer from [MT + vtableSlotOffset]
    /// 4. Push the function pointer
    ///
    /// For non-virtual methods or devirtualized calls:
    /// - Use the direct method address
    /// </summary>
    private bool CompileLdvirtftn(uint token)
    {
        DebugConsole.Write("[ldvirtftn] token=0x");
        DebugConsole.WriteHex(token);
        DebugConsole.Write(" stackDepth=");
        DebugConsole.WriteDecimal((uint)_evalStackDepth);
        DebugConsole.WriteLine();

        // Verify we have an object on the stack
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] ldvirtftn: no object on stack");
            return false;
        }

        // Pop the object reference into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // For testing: token is the function pointer address
        // In production: would use method resolver to get address, then vtable lookup
        ulong fnPtr = token;
        bool useVtable = false;
        int vtableSlot = -1;

        // Use ResolveMethod to get method info (supports registry fallback when _resolver is null)
        ResolvedMethod resolved;
        bool resolveOk = ResolveMethod(token, out resolved);
        DebugConsole.Write("[ldvirtftn] resolveOk=");
        DebugConsole.WriteDecimal(resolveOk ? 1u : 0u);
        DebugConsole.Write(" IsValid=");
        DebugConsole.WriteDecimal(resolved.IsValid ? 1u : 0u);
        DebugConsole.Write(" NativeCode=0x");
        DebugConsole.WriteHex((ulong)resolved.NativeCode);
        DebugConsole.Write(" IsVirtual=");
        DebugConsole.WriteDecimal(resolved.IsVirtual ? 1u : 0u);
        DebugConsole.Write(" VtableSlot=");
        DebugConsole.WriteDecimal((uint)(resolved.VtableSlot >= 0 ? resolved.VtableSlot : 0));
        if (resolved.VtableSlot < 0) DebugConsole.Write("(neg)");
        DebugConsole.WriteLine();

        if (resolveOk && resolved.IsValid && resolved.NativeCode != null)
        {
            fnPtr = (ulong)resolved.NativeCode;

            // Check if this is a virtual method requiring vtable dispatch
            if (resolved.IsVirtual && resolved.VtableSlot >= 0)
            {
                useVtable = true;
                vtableSlot = resolved.VtableSlot;
            }
        }
        // Otherwise fall back to treating token as direct address

        DebugConsole.Write("[ldvirtftn] useVtable=");
        DebugConsole.WriteDecimal(useVtable ? 1u : 0u);
        DebugConsole.Write(" fnPtr=0x");
        DebugConsole.WriteHex(fnPtr);
        DebugConsole.WriteLine();

        if (useVtable)
        {
            // Virtual dispatch: load function pointer from vtable
            // RAX already contains the object reference from pop above
            DebugConsole.Write("[ldvirtftn] vtable dispatch slot=");
            DebugConsole.WriteDecimal((uint)vtableSlot);

            // 0. Call EnsureVtableSlotCompiled(objPtr, vtableSlot) to make sure the slot is populated
            //    This is needed because the slot might be 0 (lazy JIT not yet triggered)
            //    Save RAX (object ptr) to R7 (RBX = callee-saved) since we'll clobber RCX/RDX
            X64Emitter.MovRR(ref _code, VReg.R7, VReg.R0);  // RBX = obj ptr (callee-saved)

            // Set up args using Microsoft x64 ABI: RCX = objPtr, RDX = vtableSlot
            X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);  // RCX = objPtr (Arg0)
            X64Emitter.MovRI32(ref _code, VReg.R2, vtableSlot);  // RDX = vtableSlot (Arg1)

            // Call EnsureVtableSlotCompiled
            ulong ensureAddr = (ulong)JitStubs.EnsureVtableSlotCompiledAddress;
            X64Emitter.MovRI64(ref _code, VReg.R0, ensureAddr);  // RAX = function address
            X64Emitter.CallR(ref _code, VReg.R0);

            // Restore object pointer from RBX
            X64Emitter.MovRR(ref _code, VReg.R0, VReg.R7);  // RAX = obj ptr

            // 1. Load MethodTable* from [RAX] (object header is the MT pointer)
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);  // RAX = *obj = MethodTable*

            // 2. Load vtable slot at offset HeaderSize + slot*8
            int vtableOffset = ProtonOS.Runtime.MethodTable.HeaderSize + (vtableSlot * 8);
            DebugConsole.Write(" offset=");
            DebugConsole.WriteDecimal((uint)vtableOffset);
            DebugConsole.WriteLine();
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, vtableOffset);  // RAX = vtable[slot]
        }
        else
        {
            // Devirtualized: use direct address
            DebugConsole.WriteLine("[ldvirtftn] using direct fnPtr (no vtable)");
            X64Emitter.MovRI64(ref _code, VReg.R0, fnPtr);
        }

        // Push the function pointer onto the stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);  // Function pointers are native int

        return true;
    }

    /// <summary>
    /// Compile localloc opcode - allocate memory on stack.
    /// Stack: ..., size -> ..., ptr
    ///
    /// Challenge: The evaluation stack uses the machine stack (via push/pop).
    /// After allocating N bytes, we need to push the result pointer.
    /// But if we just do sub rsp, N then push ptr, the push would write into
    /// the allocated space!
    ///
    /// Solution: Allocate size+16 bytes (aligned), use the first 8 for the
    /// pushed pointer, return a pointer to the rest. Or simpler: after sub,
    /// do lea rax, [rsp+8] then push rax - the push writes to [rsp], but
    /// we return rsp+8 as the buffer.
    ///
    /// Wait, that's still wrong. Let me think again:
    /// - We allocate N bytes (aligned to 16)
    /// - We need to push 8 bytes for the result pointer
    /// - So we allocate N+16 bytes (to stay 16-aligned after the push)
    /// - The result pointer is RSP+8 (skipping the slot used by push)
    /// </summary>
    private bool CompileLocalloc()
    {
        // Pop the size from the evaluation stack
        X64Emitter.Pop(ref _code, VReg.R1);  // RCX = size in bytes
        PopEntry();

        // Add 8 for the result pointer we need to push, then align to 16
        // RCX = ((RCX + 8) + 15) & ~15 = (RCX + 23) & ~15
        X64Emitter.AddRI(ref _code, VReg.R1, 23);
        X64Emitter.AndImm(ref _code, VReg.R1, unchecked((int)0xFFFFFFF0));

        // Subtract from RSP to allocate space (includes room for the push)
        X64Emitter.SubRR(ref _code, VReg.SP, VReg.R1);

        // Compute the result pointer = RSP + 8 (skipping space for the push)
        // lea rax, [rsp+8]
        X64Emitter.MovRR(ref _code, VReg.R0, VReg.SP);
        X64Emitter.AddRI(ref _code, VReg.R0, 8);

        // Push the result pointer - this writes to [RSP-8] then decrements RSP
        // So the allocated buffer starts at what was RSP+8, now at RSP+16
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.Ptr);  // Pointer type

        return true;
    }

    // ==================== String and Token Loading ====================

    /// <summary>
    /// Compile ldstr - Load a string literal from the #US heap.
    /// Stack: ... -> ..., string
    ///
    /// The token is a user string token (0x70xxxxxx) that references the #US heap.
    ///
    /// Per ECMA-335 III.4.16, ldstr "pushes a new string object, created from the
    /// metadata string literal". The StringResolver callback is responsible for:
    /// 1. Looking up the UTF-16 string data in the #US heap using the token
    /// 2. Allocating a System.String object on the GC heap (using RhpNewArray)
    /// 3. Copying the string data into the object
    /// 4. Returning the object reference (or a cached interned string)
    ///
    /// String layout in memory:
    /// - Object header (MethodTable pointer)
    /// - int _length (number of characters)
    /// - char _firstChar (first character, followed by remaining chars inline)
    ///
    /// For testing: if no string resolver is available, we push null.
    /// For production: StringResolver should call MetadataReader.GetUserString()
    /// and allocate a proper String object.
    /// </summary>
    private bool CompileLdstr(uint token)
    {
        // User string tokens have table ID 0x70 in the high byte
        // The low 24 bits are the index into the #US heap

        ulong stringPtr = 0;

        // Try to resolve the string using the delegate resolver first
        if (_stringResolver != null)
        {
            void* resolved;
            if (_stringResolver(token, out resolved) && resolved != null)
            {
                stringPtr = (ulong)resolved;
            }
        }

        // If no delegate resolver or it failed, try MetadataReader directly
        // (This is the preferred path in our minimal runtime environment
        // where delegate invocation has limitations)
        if (stringPtr == 0)
        {
            void* resolved;
            if (MetadataReader.ResolveUserString(token, out resolved) && resolved != null)
            {
                stringPtr = (ulong)resolved;
            }
        }

        // Push the string object reference (or null if unresolved)
        X64Emitter.MovRI64(ref _code, VReg.R0, stringPtr);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ObjRef);

        return true;
    }

    /// <summary>
    /// Compile ldtoken - Load a runtime handle for a type, method, or field.
    /// Stack: ... -> ..., RuntimeHandle
    ///
    /// The token references a TypeRef, TypeDef, MethodDef, MethodRef, FieldDef, or MemberRef.
    /// We load the address of the corresponding MethodTable or other runtime handle.
    ///
    /// For testing: token is treated as a direct pointer value.
    /// For production: would resolve via metadata tables.
    /// </summary>
    private bool CompileLdtoken(uint token)
    {
        // ldtoken can reference types, methods, or fields
        // - For types: return MethodTable pointer (RuntimeTypeHandle)
        // - For methods: return (assemblyId << 32) | token (RuntimeMethodHandle)
        // - For fields: return (assemblyId << 32) | token (RuntimeFieldHandle)
        //
        // The loaded value can then be used with:
        // - Type.GetTypeFromHandle() for types
        // - MethodBase.GetMethodFromHandle() for methods
        // - FieldInfo.GetFieldFromHandle() for fields

        byte tableId = (byte)(token >> 24);
        ulong handleValue = token;

        switch (tableId)
        {
            case 0x01:  // TypeRef
            case 0x02:  // TypeDef
            case 0x1B:  // TypeSpec
                // Type token - resolve to MethodTable pointer
                if (_typeResolver != null)
                {
                    void* mtPtr;
                    if (_typeResolver(token, out mtPtr) && mtPtr != null)
                    {
                        handleValue = (ulong)mtPtr;
                    }
                }
                break;

            case 0x04:  // FieldDef
                // Field token - check if it has static data (array initializer)
                // If so, return the actual data address; otherwise return token-based handle
                {
                    byte* fieldData = AssemblyLoader.GetFieldDataAddressByToken(_debugAssemblyId, token);
                    if (fieldData != null)
                    {
                        // Field has static data - return actual address
                        handleValue = (ulong)fieldData;
                        DebugConsole.Write("[JIT] ldtoken field 0x");
                        DebugConsole.WriteHex(token);
                        DebugConsole.Write(" -> data addr 0x");
                        DebugConsole.WriteHex(handleValue);
                        DebugConsole.WriteLine();
                    }
                    else
                    {
                        // Regular field - encode as (assemblyId << 32) | token
                        handleValue = ((ulong)_debugAssemblyId << 32) | token;
                    }
                }
                break;

            case 0x06:  // MethodDef
            case 0x2B:  // MethodSpec
                // Method token - encode as (assemblyId << 32) | token
                handleValue = ((ulong)_debugAssemblyId << 32) | token;
                break;

            case 0x0A:  // MemberRef - could be method or field
                // Try to resolve as field with static data first
                {
                    byte* fieldData = AssemblyLoader.GetFieldDataAddressByToken(_debugAssemblyId, token);
                    if (fieldData != null)
                    {
                        // MemberRef to field with static data - return actual address
                        handleValue = (ulong)fieldData;
                        DebugConsole.Write("[JIT] ldtoken memberref field 0x");
                        DebugConsole.WriteHex(token);
                        DebugConsole.Write(" -> data addr 0x");
                        DebugConsole.WriteHex(handleValue);
                        DebugConsole.WriteLine();
                        break;
                    }
                }
                // Try field resolver for regular fields
                if (_fieldResolver != null)
                {
                    ResolvedField field;
                    if (_fieldResolver(token, out field) && field.IsValid)
                    {
                        // It's a regular field
                        handleValue = ((ulong)_debugAssemblyId << 32) | token;
                        break;
                    }
                }
                // Assume it's a method reference
                handleValue = ((ulong)_debugAssemblyId << 32) | token;
                break;

            default:
                // Unknown token type - just use the raw token
                break;
        }

        // Push the handle onto the stack
        X64Emitter.MovRI64(ref _code, VReg.R0, handleValue);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);  // Runtime handles are native int

        return true;
    }

    /// <summary>
    /// Compile ldelem with type token - Load an array element.
    /// Stack: ..., array, index -> ..., value
    ///
    /// The token specifies the element type. We decode the element size from the token.
    /// For testing: token low byte encodes element size (1/2/4/8).
    /// For production: would look up type info from metadata.
    /// </summary>
    private bool CompileLdelemToken(uint token)
    {
        // Get element size from metadata token
        int elemSize = GetTypeSizeFromToken(token);
        if (elemSize == 0) elemSize = 8;  // Default to pointer size

        // For small primitives (1/2/4/8 bytes), use the standard ldelem path
        if (elemSize <= 8)
        {
            return CompileLdelem(elemSize, signed: true);
        }

        // For larger structs, we need to copy the entire struct to the eval stack

        // Pop index and array
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopEntry();
        PopEntry();

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + ArrayDataOffset + index * elemSize
        // R0 = array, R1 = index
        // R2 = index * elemSize
        X64Emitter.MovRR(ref _code, VReg.R2, VReg.R1);
        X64Emitter.ImulRI(ref _code, VReg.R2, elemSize);
        X64Emitter.AddRR(ref _code, VReg.R0, VReg.R2);
        X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
        // Now R0 = element address

        // Allocate stack space for the struct
        // Use 16-byte alignment for structs > 16 bytes to match call return buffer alignment
        // This ensures consistency with stloc cleanup
        int alignedSize = elemSize > 16 ? ((elemSize + 15) & ~15) : ((elemSize + 7) & ~7);
        int slots = alignedSize / 8;
        X64Emitter.SubRI(ref _code, VReg.SP, alignedSize);

        // Copy from [R0] to [RSP]
        // Use R3 for temp
        int offset = 0;
        while (offset + 8 <= elemSize)
        {
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.R0, offset);
            X64Emitter.MovMR(ref _code, VReg.SP, offset, VReg.R3);
            offset += 8;
        }

        // Copy remaining 4 bytes if present
        if (offset + 4 <= elemSize)
        {
            X64Emitter.MovRM32(ref _code, VReg.R3, VReg.R0, offset);
            X64Emitter.MovMR32(ref _code, VReg.SP, offset, VReg.R3);
            offset += 4;
        }

        // Copy remaining 2 bytes if present
        if (offset + 2 <= elemSize)
        {
            X64Emitter.MovRM16(ref _code, VReg.R3, VReg.R0, offset);
            X64Emitter.MovMR16(ref _code, VReg.SP, offset, VReg.R3);
            offset += 2;
        }

        // Copy remaining 1 byte if present
        if (offset < elemSize)
        {
            X64Emitter.MovRM8(ref _code, VReg.R3, VReg.R0, offset);
            X64Emitter.MovMR8(ref _code, VReg.SP, offset, VReg.R3);
        }

        // Track as ONE entry with the full byte size (matching ldloc behavior for large structs)
        PushEntry(EvalStackEntry.Struct(elemSize));

        return true;
    }

    /// <summary>
    /// Compile stelem with type token - Store an array element.
    /// Stack: ..., array, index, value -> ...
    ///
    /// The token specifies the element type. We decode the element size from the token.
    /// For testing: token low byte encodes element size (1/2/4/8).
    /// For production: would look up type info from metadata.
    /// </summary>
    private bool CompileStelemToken(uint token)
    {
        // Get element size from metadata token
        int elemSize = GetTypeSizeFromToken(token);
        if (elemSize == 0) elemSize = 8;  // Default to pointer size

        // For small primitives (1/2/4/8 bytes), use the standard stelem path
        if (elemSize <= 8)
        {
            return CompileStelem(elemSize);
        }

        // For larger structs (>8 bytes), the C# compiler typically generates:
        //   ldarg.0 / ldloc (object/struct ref)
        //   ldfld (array field)
        //   ldloc (index)
        //   ldloc (struct local) -- this SHOULD copy struct value, but if typeSize not known, just loads 8 bytes
        //   stelem
        //
        // The problem: ldloc for a struct without proper type info just loads 8 bytes (first part of struct).
        // This gives us garbage, not the actual struct data on the eval stack.
        //
        // SOLUTION: For large struct stelem, we need to get the struct's LOCAL VARIABLE ADDRESS
        // and copy from there. We'll look at what ldloc actually pushed (which is garbage/partial data)
        // and IGNORE it, instead using ldloca-style behavior.
        //
        // However, we don't know at stelem time which local was loaded. So we need a different approach:
        // We'll interpret what's on the stack as a POINTER to the struct (which is what ldloca would push).
        // Since ldloc without type info loads 8 bytes from the local, and structs are at negative RBP offsets,
        // that value is actually part of the struct data (not a valid pointer).
        //
        // The REAL fix is in CompileLdloc: when the local is a struct but typeSize is unknown,
        // push the ADDRESS of the local instead of trying to copy its value.
        //
        // For now, we'll use a hybrid approach:
        // Stack has: [value-or-pointer, index, array] (3 slots minimum)
        // Pop all 3, compute element address, and copy from the SOURCE.
        //
        // To determine if 'value-or-pointer' is actually struct data or a pointer:
        // - If _evalStackDepth >= structSlots + 2, we have VALUE on stack
        // - Otherwise, we have POINTER (or garbage that we treat as pointer)

        int alignedSize = (elemSize + 7) & ~7;
        int structSlots = alignedSize / 8;

        // With our ldloc fix, large structs push full VALUE onto the stack.
        // Stack layout: [struct_value (structSlots slots), index, array]
        // Total slots = structSlots + 2
        //
        // We need to:
        // 1. Get index and array from the bottom of the sequence
        // 2. Use RSP (pointing to struct data) as source
        // 3. Compute element address
        // 4. Copy struct data to element

        // RSP points to start of struct value
        // index is at RSP + alignedSize
        // array is at RSP + alignedSize + 8

        // First, save RSP as source address
        X64Emitter.MovRR(ref _code, VReg.R2, VReg.SP);  // R2 = source address (struct data on stack)

        // Load index and array from below struct data
        X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, alignedSize);      // R1 = index
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, alignedSize + 8);  // R0 = array

        // Bounds check: throws IndexOutOfRangeException if index >= length
        EmitArrayBoundsCheck(VReg.R0, VReg.R1);

        // Compute element address: array + ArrayDataOffset + index * elemSize
        X64Emitter.MovRR(ref _code, VReg.R3, VReg.R1);
        X64Emitter.ImulRI(ref _code, VReg.R3, elemSize);
        X64Emitter.AddRR(ref _code, VReg.R0, VReg.R3);
        X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);
        // Now R0 = element address (dest), R2 = source struct address (on stack)

        // Copy struct data from [R2] to element address [R0]
        int offset = 0;
        while (offset + 8 <= elemSize)
        {
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.R2, offset);
            X64Emitter.MovMR(ref _code, VReg.R0, offset, VReg.R3);
            offset += 8;
        }
        if (offset + 4 <= elemSize)
        {
            X64Emitter.MovRM32(ref _code, VReg.R3, VReg.R2, offset);
            X64Emitter.MovMR32(ref _code, VReg.R0, offset, VReg.R3);
            offset += 4;
        }
        if (offset + 2 <= elemSize)
        {
            X64Emitter.MovRM16(ref _code, VReg.R3, VReg.R2, offset);
            X64Emitter.MovMR16(ref _code, VReg.R0, offset, VReg.R3);
            offset += 2;
        }
        if (offset < elemSize)
        {
            X64Emitter.MovRM8(ref _code, VReg.R3, VReg.R2, offset);
            X64Emitter.MovMR8(ref _code, VReg.R0, offset, VReg.R3);
        }

        // Pop all slots: struct data (alignedSize) + index (8) + array (8)
        X64Emitter.AddRI(ref _code, VReg.SP, alignedSize + 16);
        // NEW TYPE SYSTEM: Pop 3 entries (struct value, index, array)
        PopEntry();  // struct value (1 entry regardless of byte size)
        PopEntry();  // index
        PopEntry();  // array

        return true;
    }

    /// <summary>
    /// Compile endfilter - End an exception filter clause.
    /// Stack: ..., result -> ...
    ///
    /// The filter returns an int32 indicating whether to handle the exception:
    /// - 0 = exception_continue_search (don't handle)
    /// - 1 = exception_execute_handler (handle)
    ///
    /// This pops the result and returns from the filter funclet.
    /// </summary>
    private bool CompileEndfilter()
    {
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] endfilter: stack empty");
            return false;
        }

        // Pop the filter result into RAX (return value)
        X64Emitter.Pop(ref _code, VReg.R0);
        PopEntry();

        // Return from the filter funclet
        // Filter funclet prolog was: push rbp; mov rbp, rdx; push rcx (exception object)
        // So the epilog is: pop rbp; ret
        // The pushed rcx has already been consumed by the eval stack operations.
        // The eval stack was initialized with depth 1 for the pushed exception,
        // and endfilter pops the final result, so RSP should be at the pushed rbp.

        // pop rbp
        _code.EmitByte(0x5D);

        // ret
        _code.EmitByte(0xC3);

        return true;
    }

    // ==================== Rare Opcodes ====================

    /// <summary>
    /// Compile jmp - Tail jump to a method.
    /// Stack: ... -> (method transfer)
    ///
    /// The jmp instruction transfers control to a method, using the current
    /// method's arguments. The evaluation stack must be empty.
    /// This is equivalent to a tail call where the arguments are unchanged.
    /// </summary>
    private bool CompileJmp(uint token)
    {
        // Evaluation stack must be empty before jmp
        if (_evalStackDepth != 0)
        {
            DebugConsole.WriteLine("[JIT] jmp: eval stack not empty");
            return false;
        }

        // Resolve the method token to get the target address
        ulong targetAddr = 0;
        if (_resolver != null)
        {
            ResolvedMethod resolved;
            if (_resolver(token, out resolved) && resolved.NativeCode != null)
            {
                targetAddr = (ulong)resolved.NativeCode;
            }
        }

        if (targetAddr == 0)
        {
            // Try the compiled method registry as fallback
            void* nativeCode = CompiledMethodRegistry.GetNativeCode(token);
            if (nativeCode != null)
            {
                targetAddr = (ulong)nativeCode;
            }
        }

        if (targetAddr == 0)
        {
            DebugConsole.Write("[JIT] jmp: cannot resolve method token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        // The jmp instruction reuses the current stack frame.
        // We need to:
        // 1. Restore callee-saved registers if we saved any
        // 2. Restore RSP/RBP
        // 3. Jump (not call) to the target

        // Emit epilogue but use JMP instead of RET
        // First, restore RSP from RBP (undo stack allocation)
        X64Emitter.MovRR(ref _code, VReg.SP, VReg.FP);
        // Pop saved RBP
        X64Emitter.Pop(ref _code, VReg.FP);
        // Jump to target (this reuses the return address already on stack)
        X64Emitter.MovRI64(ref _code, VReg.R0, targetAddr);
        X64Emitter.JumpReg(ref _code, VReg.R0);

        return true;
    }

    /// <summary>
    /// Compile mkrefany - Make a typed reference.
    /// Stack: ..., ptr -> ..., typedRef
    ///
    /// Creates a TypedReference from a pointer and type token.
    /// TypedReference is a 16-byte struct: { void* Value; void* Type }
    /// </summary>
    private bool CompileMkrefany(uint typeToken)
    {
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] mkrefany: stack underflow");
            return false;
        }

        // Resolve the type token to get the Type pointer
        ulong typePtr = 0;
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(typeToken, out resolved) && resolved != null)
            {
                typePtr = (ulong)resolved;
            }
        }

        // Pop the pointer value
        X64Emitter.Pop(ref _code, VReg.R0);  // RAX = value pointer
        PopEntry();

        // TypedReference is 16 bytes - we push both parts onto the stack
        // The layout is: [Value pointer][Type pointer]
        // We push Type first (higher address), then Value (lower address)

        // Push type pointer
        X64Emitter.MovRI64(ref _code, VReg.R1, typePtr);
        X64Emitter.Push(ref _code, VReg.R1);
        // Push value pointer (already in RAX)
        X64Emitter.Push(ref _code, VReg.R0);

        // TypedReference takes 2 stack slots - push as struct entry
        PushEntry(EvalStackEntry.Struct(16));  // TypedReference is 16 bytes (2 slots)

        return true;
    }

    /// <summary>
    /// Compile refanyval - Get value pointer from typed reference.
    /// Stack: ..., typedRef -> ..., ptr
    ///
    /// Extracts the value pointer from a TypedReference, validating the type.
    /// </summary>
    private bool CompileRefanyval(uint typeToken)
    {
        // TypedReference is tracked as 1 entry (a 16-byte struct), not 2
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] refanyval: stack underflow");
            return false;
        }

        // TypedReference is 16 bytes (2 stack slots) - tracked as single entry
        // mkrefany pushed it as one Struct entry with ByteSize=16
        // So we pop: first 8 bytes (Value), then next 8 bytes (Type)
        X64Emitter.Pop(ref _code, VReg.R0);  // RAX = value pointer (TOS)
        X64Emitter.Pop(ref _code, VReg.R1);  // RCX = type pointer (currently unused for validation)
        PopEntry();  // Pop the single TypedReference entry

        // Push just the value pointer
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.ByRef);

        // Note: In a full implementation, we would validate that typeToken matches
        // the Type field of the TypedReference and throw InvalidCastException if not.
        _ = typeToken;

        return true;
    }

    /// <summary>
    /// Compile refanytype - Get type from typed reference.
    /// Stack: ..., typedRef -> ..., typeHandle
    ///
    /// Extracts the RuntimeTypeHandle from a TypedReference.
    /// </summary>
    private bool CompileRefanytype()
    {
        // TypedReference is tracked as 1 entry (a 16-byte struct), not 2
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] refanytype: stack underflow");
            return false;
        }

        // TypedReference is 16 bytes (2 stack slots) - tracked as single entry
        // mkrefany pushed it as one Struct entry with ByteSize=16
        // So we pop: first 8 bytes (Value), then next 8 bytes (Type)
        X64Emitter.Pop(ref _code, VReg.R1);  // RCX = value pointer (discarded, TOS)
        X64Emitter.Pop(ref _code, VReg.R0);  // RAX = type pointer
        PopEntry();  // Pop the single TypedReference entry

        // Push the type pointer as RuntimeTypeHandle
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    /// <summary>
    /// Compile arglist - Get handle to argument list (for varargs methods).
    /// Stack: ... -> ..., argHandle
    ///
    /// Returns a RuntimeArgumentHandle pointing to the varargs portion of the arguments.
    /// </summary>
    /// <summary>
    /// Compile a varargs call with TypedReference entries.
    /// For each vararg, we create a TypedReference (16 bytes: value + MethodTable*).
    /// A sentinel TypedReference (0, 0) is added at the end.
    /// </summary>
    private unsafe bool CompileVarargCall(uint token, ResolvedMethod method)
    {
        // Calculate total items on eval stack: declared args + varargs
        int varargCount = method.VarargCount;
        int declaredArgs = method.ArgCount;
        if (method.HasThis)
            declaredArgs++;
        int totalArgs = declaredArgs + varargCount;

        if (_evalStackDepth < totalArgs)
        {
            DebugConsole.Write("[JIT VarargCall] Insufficient stack depth for 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine();
            return false;
        }

        MethodTable** varargMTs = (MethodTable**)method.VarargMTs;

        DebugConsole.Write("[JIT VarargCall] ");
        DebugConsole.WriteDecimal((uint)varargCount);
        DebugConsole.Write(" varargs, ");
        DebugConsole.WriteDecimal((uint)declaredArgs);
        DebugConsole.Write(" declared for 0x");
        DebugConsole.WriteHex(token);
        DebugConsole.WriteLine();

        // Stack layout on eval stack (before we start):
        //   [declared arg 0]        <- bottom
        //   [declared arg 1]
        //   ...
        //   [vararg 0]
        //   [vararg 1]
        //   ...
        //   [vararg N-1]            <- top
        //
        // Native stack has these values pushed in order.
        // We need to:
        // 1. Read the vararg values from the native stack
        // 2. Build TypedReference entries
        // 3. Pop declared args into registers
        // 4. Call with TypedReference entries on stack

        // Calculate TypedReference array size (varargs + sentinel)
        int typedRefSize = (varargCount + 1) * 16;  // +1 for sentinel
        int alignedSize = (typedRefSize + 15) & ~15;

        // The vararg values are currently at [RSP+0], [RSP+8], etc.
        // (with declared args below them)
        // Varargs: [RSP + declaredArgs*8] to [RSP + totalArgs*8 - 8]

        // Step 1: Read all vararg values from the native stack into the TypedReference array
        // We'll build the array in-place where the varargs currently are
        //
        // Current layout (native stack, RSP at top):
        //   [vararg N-1]  <- RSP
        //   [vararg N-2]
        //   ...
        //   [vararg 0]
        //   [declared args]
        //
        // We need to transform varargs into TypedReference entries.
        // Each raw value (8 bytes) becomes a TypedReference (16 bytes).
        // So we need extra space.

        // Allocate space for the full TypedReference array (varargs + sentinel)
        // Each TypedReference is 16 bytes: _value (8) + _type (8)
        // We need: varargCount * 16 (for varargs) + 16 (for sentinel) = (varargCount + 1) * 16
        int typedRefArraySize = (varargCount + 1) * 16;
        int alignedExtra = (typedRefArraySize + 15) & ~15;
        X64Emitter.SubRI(ref _code, VReg.SP, alignedExtra);

        // Now the vararg values are at [RSP + alignedExtra]
        // We need to copy them to TypedReference entries at RSP

        // Build TypedReference entries starting from index 0
        // TypedReference._value is a POINTER to the data, not the data itself
        for (int i = 0; i < varargCount; i++)
        {
            // Get address of vararg value on stack: RSP + alignedExtra + (varargCount - 1 - i) * 8
            // (reverse order because last vararg is at RSP+alignedExtra, first at higher address)
            int srcOffset = alignedExtra + (varargCount - 1 - i) * 8;

            // Store POINTER to value as _value (TypedReference._value is a pointer)
            int destOffset = i * 16;
            X64Emitter.Lea(ref _code, VReg.R0, VReg.SP, srcOffset);
            X64Emitter.MovMR(ref _code, VReg.SP, destOffset, VReg.R0);  // _value = pointer to data

            // Load and store MethodTable
            MethodTable* mt = varargMTs != null ? varargMTs[i] : null;
            if (mt != null)
            {
                X64Emitter.MovRI64(ref _code, VReg.R10, (ulong)mt);
            }
            else
            {
                X64Emitter.XorRR(ref _code, VReg.R10, VReg.R10);
            }
            X64Emitter.MovMR(ref _code, VReg.SP, destOffset + 8, VReg.R10);  // _type
        }

        // Write sentinel at the end
        int sentinelOffset = varargCount * 16;
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);
        X64Emitter.MovMR(ref _code, VReg.SP, sentinelOffset, VReg.R0);      // _value = 0
        X64Emitter.MovMR(ref _code, VReg.SP, sentinelOffset + 8, VReg.R0);  // _type = 0

        // Pop varargs from eval stack tracking (we've consumed them)
        for (int i = 0; i < varargCount; i++)
            PopEntry();

        // Now pop declared args into registers
        // They're at [RSP + alignedExtra + varargCount * 8 + ...]
        // Actually, they're still on the eval stack
        if (declaredArgs >= 4)
        {
            // Load from native stack at offset
            int offset = alignedExtra + varargCount * 8 + 3 * 8;
            X64Emitter.MovRM(ref _code, VReg.R9, VReg.SP, offset);
            PopEntry();
        }
        if (declaredArgs >= 3)
        {
            int offset = alignedExtra + varargCount * 8 + 2 * 8;
            X64Emitter.MovRM(ref _code, VReg.R8, VReg.SP, offset);
            PopEntry();
        }
        if (declaredArgs >= 2)
        {
            int offset = alignedExtra + varargCount * 8 + 1 * 8;
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, offset);
            PopEntry();
        }
        if (declaredArgs >= 1)
        {
            int offset = alignedExtra + varargCount * 8;
            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, offset);
            PopEntry();
        }

        // Adjust stack to remove old vararg values and declared args
        // The TypedReference array is at RSP, old values are above it
        // We need to keep only the TypedReference array
        // Add offset to skip over old values: alignedExtra + (varargCount + declaredArgs) * 8
        // But wait, we need the TypedReference array to stay at the top
        //
        // Current layout:
        //   [TypedReference array + sentinel]  <- RSP, size = alignedSize
        //   [original vararg values]            <- RSP + alignedSize (roughly), size = varargCount * 8
        //   [declared arg values]               <- size = declaredArgs * 8
        //
        // We need:
        //   [TypedReference array + sentinel]  <- RSP at call time
        //
        // So we need to remove the old vararg and declared arg values
        int oldValuesSize = (varargCount + declaredArgs) * 8;
        int alignedOldValues = (oldValuesSize + 15) & ~15;

        // Move RSP up past the old values, but keep TypedReference array where it is
        // Actually, the TypedReference array is already at RSP, we just need to skip the old values
        // which are above (at higher stack addresses, i.e., lower memory addresses after our sub rsp)
        //
        // Wait, I think I have the layout wrong. Let me reconsider.
        // After sub rsp, alignedExtra:
        //   RSP points to new space (lower address)
        //   Old vararg values are at RSP + alignedExtra
        //
        // We wrote TypedReference entries starting at RSP.
        // Old vararg values and declared args are above (at RSP + alignedExtra to RSP + alignedExtra + totalArgs*8)
        //
        // For the call, we need TypedReference array at RSP. We don't need the old values anymore.
        // But the old values are "below" the TypedReference array in terms of stack (higher addresses).
        // When we call, we push return address and the stack grows down.
        //
        // Actually, we want the TypedReference array to be the ONLY thing on the stack at call time.
        // We need to "remove" the old vararg/declared values.
        //
        // One way: copy the TypedReference array up to overwrite the old values.
        // But that's expensive.
        //
        // Simpler approach for now: just leave them there as dead space.
        // The callee won't access them. We'll clean them up after the call.

        // Total cleanup after call: alignedExtra + totalArgs * 8 (to remove everything we pushed)
        int totalCleanup = alignedExtra + totalArgs * 8;
        int alignedCleanup = (totalCleanup + 15) & ~15;

        // Actually wait, we did sub rsp, alignedExtra but the original args are still on the stack
        // from before. We haven't removed them. Let me trace through again:
        //
        // Before CompileVarargCall:
        //   Native stack has args pushed: [declaredArg0, declaredArg1, ..., vararg0, vararg1, ...]
        //   RSP points to top (last vararg)
        //
        // After sub rsp, alignedExtra:
        //   RSP points to new space
        //   Old args are at RSP + alignedExtra
        //
        // We build TypedReference array at RSP.
        // We load declared args from native stack into registers.
        //
        // Now for the call, we need TypedReference array at RSP.
        // The old values are dead space between RSP + alignedSize and RSP + alignedExtra + oldValuesSize
        //
        // Wait, alignedSize might be larger than alignedExtra. Let me recalculate.
        // alignedExtra = (varargCount * 8 + 16 + 15) & ~15
        // alignedSize = ((varargCount + 1) * 16 + 15) & ~15
        //
        // For varargCount = 2:
        //   alignedExtra = (16 + 16 + 15) & ~15 = 48 -> 48
        //   alignedSize = (3 * 16 + 15) & ~15 = 63 -> 48
        //
        // So they're about the same. The TypedReference array fits in the extra space we allocated.
        //
        // After we've built the TypedReference array, we just need to clean up the old values.
        // Since declared args are now in registers, and TypedReference entries are at RSP,
        // we need to remove the old vararg values (which are between RSP + alignedExtra and RSP + alignedExtra + varargCount*8)
        // and declared arg values (above that).
        //
        // But for the call to work correctly, RSP should point to the TypedReference array.
        // And it does! So we just call.
        //
        // After the call returns, we clean up: add rsp, alignedExtra + totalArgs*8
        // This removes both the TypedReference array space AND the original pushed values.

        // x64 ABI REQUIRES 32 bytes of shadow space before the call
        // The callee expects [RBP+16..47] as shadow space for homing register args
        // Without this, shadow space overlaps with our TypedReference array!
        X64Emitter.SubRI(ref _code, VReg.SP, 32);

        // Call the function
        if (method.NativeCode != null)
        {
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
        }
        else if (method.RegistryEntry != null)
        {
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.RegistryEntry);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
            X64Emitter.CallR(ref _code, VReg.R0);
        }
        else
        {
            DebugConsole.WriteLine("[JIT VarargCall] No native code or registry entry");
            return false;
        }

        // Clean up: shadow space + extra space + original args
        X64Emitter.AddRI(ref _code, VReg.SP, 32 + alignedExtra + totalArgs * 8);

        // Handle return value
        switch (method.ReturnKind)
        {
            case ReturnKind.Void:
                break;
            case ReturnKind.Int32:
            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;
            default:
                X64Emitter.Push(ref _code, VReg.R0);
                PushEntry(EvalStackEntry.NativeInt);
                break;
        }

        return true;
    }

    private bool CompileArglist()
    {
        // In a varargs method, after the declared parameters, there may be additional
        // arguments on the stack. The arglist instruction returns a handle that allows
        // iterating over those extra arguments using ArgIterator.
        //
        // For our implementation:
        // - The handle is a pointer to where the varargs begin on the stack
        // - In x64 calling convention:
        //   - First 4 args are in registers (RCX, RDX, R8, R9), not on stack
        //   - Caller reserves 32-byte shadow space at [RSP+0..31]
        //   - 5th+ args go on stack starting at [RSP+32]
        //   - Varargs TypedReference array is placed after any stack args
        //
        // Stack layout in callee frame (after prologue):
        //   [RBP+0]  = saved RBP
        //   [RBP+8]  = return address
        //   [RBP+16] = shadow space (32 bytes, [RBP+16..47])
        //   [RBP+48] = TypedReference array start (for 0-4 declared args)
        //   [RBP+48 + (argCount-4)*8] = TypedReference array start (for 5+ declared args)

        // Calculate the address where varargs TypedReference array begins
        // Base offset: 48 (16 for saved RBP + return addr, 32 for shadow space)
        // Stack args: for declared args > 4, they take space at [RBP+48], [RBP+56], etc.
        int stackArgs = _argCount > 4 ? _argCount - 4 : 0;
        int varargOffset = 48 + (stackArgs * 8);

        X64Emitter.MovRR(ref _code, VReg.R0, VReg.FP);
        X64Emitter.AddRI(ref _code, VReg.R0, varargOffset);
        X64Emitter.Push(ref _code, VReg.R0);
        PushEntry(EvalStackEntry.NativeInt);

        return true;
    }

    // Function pointers for runtime helpers (set by caller)
    private void* _rhpNewFast;
    private void* _rhpNewArray;
    private void* _isAssignableTo;
    private void* _getInterfaceMethod;
    private void* _debugStfld;
    private void* _debugLdfld;
    private void* _debugLdfldInt;
    private void* _debugStelemStack;
    private void* _debugVtableDispatch;
    private void* _ensureVtableSlotCompiled;  // JIT stub for lazy vtable slot compilation
    private uint _debugAssemblyId;   // Assembly ID for targeted debug logging
    private uint _debugMethodToken;  // Method token for targeted debug logging

    // ==================== Funclet Compilation Support ====================
    // EH clause storage for funclet-based exception handling
    private const int MaxEHClauses = 16;
    private int _ehClauseCount;

    // Each clause uses 6 ints: flags, tryStart, tryEnd, handlerStart, handlerEnd, classToken (IL offsets)
    // Heap-allocated as part of _heapBuffers to reduce stack usage
    private int* _ehClauseData;   // [MaxEHClauses * 6]

    // Funclet compilation results (filled by CompileWithFunclets)
    // Each funclet has: nativeStart (code offset), nativeSize, clauseIndex, isFilterExpr (0 or 1)
    // Filter clauses generate TWO funclets: one for filter expression, one for handler
    private int* _funcletInfo;    // [MaxEHClauses * 2 * 4] - doubled for filter clauses
    private int _funcletCount;

    // Flag to indicate we're compiling a funclet (not main method)
    private bool _compilingFunclet;

    // Flag to indicate we're at the entry of a catch handler funclet
    // The first 'pop' instruction should be a no-op (exception is in RCX, not on stack)
    private bool _funcletCatchHandlerEntry;

    // Finally call tracking (for calls from leave instructions in main body)
    // Each entry: [patchOffset, ehClauseIndex]
    private int* _finallyCallPatches;  // [MaxFinallyCalls * 2]
    private int _finallyCallCount;

    /// <summary>
    /// Set EH clauses for funclet compilation.
    /// Must be called before CompileWithFunclets().
    /// </summary>
    /// <param name="clauses">IL-offset based EH clauses</param>
    public void SetILEHClauses(ref ILExceptionClauses clauses)
    {
        _ehClauseCount = 0;
        int count = clauses.Count;
        if (count > MaxEHClauses) count = MaxEHClauses;
        if (_ehClauseData == null) return;

        for (int i = 0; i < count; i++)
        {
            var clause = clauses.GetClause(i);
            int idx = i * 6;
            _ehClauseData[idx + 0] = (int)clause.Flags;
            _ehClauseData[idx + 1] = (int)clause.TryOffset;
            _ehClauseData[idx + 2] = (int)(clause.TryOffset + clause.TryLength);
            _ehClauseData[idx + 3] = (int)clause.HandlerOffset;
            _ehClauseData[idx + 4] = (int)(clause.HandlerOffset + clause.HandlerLength);
            _ehClauseData[idx + 5] = (int)clause.ClassTokenOrFilterOffset;
            _ehClauseCount++;
        }
    }

    /// <summary>
    /// Check if an IL offset is inside a handler region (for skipping during main method compilation).
    /// </summary>
    private bool IsInsideHandler(int ilOffset)
    {
        if (_ehClauseData == null) return false;

        for (int i = 0; i < _ehClauseCount; i++)
        {
            int idx = i * 6;
            int handlerStart = _ehClauseData[idx + 3];
            int handlerEnd = _ehClauseData[idx + 4];
            if (ilOffset >= handlerStart && ilOffset < handlerEnd)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Get the next IL offset after skipping all handler regions starting at current offset.
    /// Returns the IL offset just past the handler(s), or -1 if not in a handler.
    /// </summary>
    private int SkipHandler(int ilOffset)
    {
        if (_ehClauseData == null) return -1;

        for (int i = 0; i < _ehClauseCount; i++)
        {
            int idx = i * 6;
            int handlerStart = _ehClauseData[idx + 3];
            int handlerEnd = _ehClauseData[idx + 4];
            if (ilOffset == handlerStart)
                return handlerEnd;
        }
        return -1;
    }

    /// <summary>
    /// Compile with funclet support.
    /// Main method body is compiled first (skipping handler regions).
    /// Each handler is then compiled as a separate funclet.
    /// </summary>
    /// <param name="methodInfo">Output: JITMethodInfo for the method with funclets</param>
    /// <param name="nativeClauses">Output: Native EH clauses with updated offsets</param>
    /// <returns>Function pointer to main method, or null on failure</returns>
    public void* CompileWithFunclets(out JITMethodInfo methodInfo, out JITExceptionClauses nativeClauses)
    {
        methodInfo = default;
        nativeClauses = default;

        if (_ehClauseCount == 0)
        {
            // No EH clauses - just do regular compilation
            var result = Compile();
            if (result == null) return null;

            // Create basic method info
            ulong codeStart = (ulong)result;
            methodInfo = JITMethodInfo.Create(
                codeStart,
                codeStart,
                CodeSize,
                PrologSize,
                5, // RBP
                0
            );
            return result;
        }

        // ========== Pass 1: Compile main method body (skip handlers) ==========
        _compilingFunclet = false;

        // Emit main method prologue
        // Calculate local space: localCount * 64 + eval stack space
        // Use 64 bytes per local to support value type locals up to 64 bytes
        // (MUST match GetLocalOffset which uses 64 bytes per local)
        int localBytes = _localCount * 64 + 64;  // 64 bytes per local + eval stack
        _stackAdjust = X64Emitter.EmitPrologue(ref _code, localBytes);

        if (_argCount > 0)
            X64Emitter.HomeArguments(ref _code, _argCount);

        // Process IL, skipping handler regions
        while (_ilOffset < _ilLength)
        {
            // Check if we're entering a handler region
            int skipTo = SkipHandler(_ilOffset);
            if (skipTo >= 0)
            {
                // Record label for handler start (this is tryEnd in IL terms)
                // This marks the native position where we would have compiled the handler
                RecordLabel(_ilOffset, _code.Position);
                // Skip to end of handler
                _ilOffset = skipTo;
                continue;
            }

            RecordLabel(_ilOffset, _code.Position);
            byte opcode = _il[_ilOffset++];

            if (!CompileOpcode(opcode))
            {
                DebugConsole.Write("[JIT] Unknown opcode 0x");
                DebugConsole.WriteHex(opcode);
                DebugConsole.Write(" at IL offset ");
                DebugConsole.WriteDecimal((uint)(_ilOffset - 1));
                DebugConsole.WriteLine();
                return null;
            }
        }

        // Patch forward branches for main method
        PatchBranches();

        // Record main method size
        int mainCodeSize = _code.Position;
        byte mainPrologSize = PrologSize;

        // ========== Pass 2: Compile funclets ==========
        _compilingFunclet = true;
        _funcletCount = 0;

        if (_ehClauseData != null && _funcletInfo != null)
        {
            // Save main method labels once (up to 32 to avoid huge stack usage)
            int saveLabelCount = _labelCount;
            int saveBranchCount = _branchCount;
            int savedLabelDataCount = saveLabelCount < 32 ? saveLabelCount : 32;
            int* savedLabelIL = stackalloc int[savedLabelDataCount];
            int* savedLabelCode = stackalloc int[savedLabelDataCount];
            for (int j = 0; j < savedLabelDataCount; j++)
            {
                savedLabelIL[j] = _labelILOffset[j];
                savedLabelCode[j] = _labelCodeOffset[j];
            }

            for (int i = 0; i < _ehClauseCount; i++)
            {
                int idx = i * 6;
                int handlerStart = _ehClauseData[idx + 3];
                int handlerEnd = _ehClauseData[idx + 4];
                int flags = _ehClauseData[idx + 0];
                int filterOrClassToken = _ehClauseData[idx + 5];

                // For filter clauses, compile the filter expression funclet FIRST
                if (flags == (int)ILExceptionClauseFlags.Filter)
                {
                    int filterStart = filterOrClassToken;  // IL offset of filter expression
                    int filterEnd = handlerStart;          // Filter expression ends where handler begins

                    // Record filter funclet start
                    int filterCodeStart = _code.Position;

                    // Emit filter funclet prolog
                    _code.EmitByte(0x55);              // push rbp
                    _code.EmitByte(0x48);              // mov rbp, rdx
                    _code.EmitByte(0x89);
                    _code.EmitByte(0xD5);

                    // Push exception object (in RCX) onto the eval stack
                    // The filter expression IL expects the exception on the stack for isinst
                    // push rcx (51)
                    _code.EmitByte(0x51);

                    // Reset for funclet
                    _labelCount = 0;
                    _branchCount = 0;
                    _evalStackDepth = 1;        // Exception is on the stack
                    _evalStackByteSize = 8;     // 8 bytes (object reference)

                    // DO NOT set _funcletCatchHandlerEntry for filter expressions!
                    // The exception is now on the real stack, not just in RCX
                    _funcletCatchHandlerEntry = false;

                    // Compile filter expression IL
                    _ilOffset = filterStart;
                    while (_ilOffset < filterEnd)
                    {
                        RecordLabel(_ilOffset, _code.Position);
                        byte opcode = _il[_ilOffset++];

                        if (!CompileOpcode(opcode))
                        {
                            DebugConsole.Write("[JIT] Filter funclet opcode error at IL ");
                            DebugConsole.WriteHex((uint)(_ilOffset - 1));
                            DebugConsole.WriteLine();
                            return null;
                        }
                    }

                    // Patch branches within filter funclet
                    PatchBranches();

                    // Safety epilog (endfilter should have already emitted return code)
                    _code.EmitByte(0x5D);  // pop rbp
                    _code.EmitByte(0xC3);  // ret

                    int filterCodeEnd = _code.Position;
                    int filterCodeSize = filterCodeEnd - filterCodeStart;

                    // Store filter expression funclet info
                    int funcIdx = _funcletCount * 4;
                    _funcletInfo[funcIdx + 0] = filterCodeStart;
                    _funcletInfo[funcIdx + 1] = filterCodeSize;
                    _funcletInfo[funcIdx + 2] = i;  // clause index
                    _funcletInfo[funcIdx + 3] = 1;  // isFilterExpr = 1 (this IS the filter expression)
                    _funcletCount++;
                }

                // Compile the handler funclet
                int funcletCodeStart = _code.Position;

                // Emit funclet prolog:
                // push rbp                    ; 1 byte (0x55) - save caller's RBP
                // mov rbp, rdx                ; 3 bytes (0x48 0x89 0xD5) - set RBP to parent frame pointer
                _code.EmitByte(0x55);
                _code.EmitByte(0x48);
                _code.EmitByte(0x89);
                _code.EmitByte(0xD5);

                // Reset label tracking for funclet
                _labelCount = 0;
                _branchCount = 0;

                // Reset eval stack for funclet compilation
                _evalStackDepth = 0;
                _evalStackByteSize = 0;

                // For catch handlers (Exception=0 or Filter=1), the exception object is passed in RCX.
                // The IL handler starts with 'pop' to discard the exception object.
                bool isCatchHandler = (flags == (int)ILExceptionClauseFlags.Exception ||
                                       flags == (int)ILExceptionClauseFlags.Filter);
                if (isCatchHandler)
                {
                    // Track that the first pop should be a no-op (exception is in RCX, not on stack)
                    _funcletCatchHandlerEntry = true;
                }

                // Compile handler IL
                _ilOffset = handlerStart;
                while (_ilOffset < handlerEnd)
                {
                    RecordLabel(_ilOffset, _code.Position);
                    byte opcode = _il[_ilOffset++];

                    if (!CompileOpcode(opcode))
                    {
                        DebugConsole.Write("[JIT] Funclet opcode error at ");
                        DebugConsole.WriteHex((uint)(_ilOffset - 1));
                        DebugConsole.WriteLine();
                        return null;
                    }
                }

                // Patch branches within funclet
                PatchBranches();

                // Emit funclet epilog (safety - endfinally/leave should have already emitted appropriate code)
                // For catch handlers, we use 'add rsp, 8; ret' to preserve RBP (parent frame pointer)
                // For finally handlers, we use 'pop rbp; ret' to restore caller's RBP
                if (isCatchHandler)
                {
                    // add rsp, 8; ret - skip saved rbp, keep RBP = parent frame pointer
                    _code.EmitByte(0x48);  // REX.W
                    _code.EmitByte(0x83);  // add r/m64, imm8
                    _code.EmitByte(0xC4);  // ModRM: reg=0 (add), r/m=4 (RSP)
                    _code.EmitByte(0x08);  // imm8 = 8
                    _code.EmitByte(0xC3);  // ret
                }
                else
                {
                    // pop rbp; ret - restore caller's RBP for finally handlers
                    _code.EmitByte(0x5D);  // pop rbp
                    _code.EmitByte(0xC3);  // ret
                }

                int funcletCodeEnd = _code.Position;
                int funcletCodeSize = funcletCodeEnd - funcletCodeStart;

                // Store handler funclet info
                int hFuncIdx = _funcletCount * 4;
                _funcletInfo[hFuncIdx + 0] = funcletCodeStart;
                _funcletInfo[hFuncIdx + 1] = funcletCodeSize;
                _funcletInfo[hFuncIdx + 2] = i;  // clause index
                _funcletInfo[hFuncIdx + 3] = 0;  // isFilterExpr = 0 (this is the handler)
                _funcletCount++;
            }

            // Restore main method labels from saved data
            for (int j = 0; j < savedLabelDataCount; j++)
            {
                _labelILOffset[j] = savedLabelIL[j];
                _labelCodeOffset[j] = savedLabelCode[j];
            }
            _labelCount = saveLabelCount;
            _branchCount = saveBranchCount;
        }

        // ========== Patch finally calls in main method ==========
        // Now that funclets are compiled, patch the call instructions that call finally funclets
        if (_finallyCallPatches != null && _funcletInfo != null)
        {
            for (int i = 0; i < _finallyCallCount; i++)
            {
                int patchIdx = i * 2;
                int patchOffset = _finallyCallPatches[patchIdx + 0];
                int clauseIndex = _finallyCallPatches[patchIdx + 1];

                // Find the funclet for this clause
                int funcletStart = -1;
                for (int f = 0; f < _funcletCount; f++)
                {
                    int funcIdx = f * 4;
                    int fClauseIdx = _funcletInfo[funcIdx + 2];
                    int isFilterExpr = _funcletInfo[funcIdx + 3];
                    // Skip filter expression funclets, we only want handler funclets
                    if (fClauseIdx == clauseIndex && isFilterExpr == 0)
                    {
                        funcletStart = _funcletInfo[funcIdx + 0];
                        break;
                    }
                }

                if (funcletStart >= 0)
                {
                    // Patch the call rel32 at patchOffset
                    // rel32 = target - (patchOffset + 4)
                    int callEnd = patchOffset + 4;  // Address after the call instruction
                    int rel32 = funcletStart - callEnd;
                    _code.PatchInt32(patchOffset, rel32);
                }
            }
        }

        // ========== Build results ==========
        void* code = _code.GetFunctionPointer();
        if (code == null) return null;

        ulong codeBase = (ulong)code;

        // Create method info
        methodInfo = JITMethodInfo.Create(
            codeBase,
            codeBase,
            (uint)mainCodeSize,
            mainPrologSize,
            5, // RBP
            0
        );

        // Add standard unwind codes for proper stack unwinding
        methodInfo.AddStandardUnwindCodes(_stackAdjust);

        // Allocate funclets in method info
        if (_funcletCount > 0 && _funcletInfo != null && _ehClauseData != null)
        {
            methodInfo.AllocateFunclets(_funcletCount);

            for (int i = 0; i < _funcletCount; i++)
            {
                int funcIdx = i * 4;
                int funcletCodeStart = _funcletInfo[funcIdx + 0];
                int funcletCodeSize = _funcletInfo[funcIdx + 1];
                int clauseIdx = _funcletInfo[funcIdx + 2];
                int isFilterExprFunclet = _funcletInfo[funcIdx + 3];

                int ehIdx = clauseIdx * 6;
                int flags = _ehClauseData[ehIdx + 0];
                bool isFilterClause = (flags == (int)ILExceptionClauseFlags.Filter);

                ulong funcletAddr = codeBase + (ulong)funcletCodeStart;
                // isFilter here means it's a filter expression funclet
                methodInfo.AddFunclet(funcletAddr, (uint)funcletCodeSize, isFilterExprFunclet == 1, clauseIdx);
            }
        }

        // Build native EH clauses
        if (_ehClauseData != null && _funcletInfo != null)
        {
            for (int i = 0; i < _ehClauseCount; i++)
            {
                int ehIdx = i * 6;
                int flags = _ehClauseData[ehIdx + 0];
                int tryStartIL = _ehClauseData[ehIdx + 1];
                int tryEndIL = _ehClauseData[ehIdx + 2];
                int classToken = _ehClauseData[ehIdx + 5];

                // Get native try offsets
                int nativeTryStartInt = GetNativeOffset(tryStartIL);
                int nativeTryEndInt = GetNativeOffset(tryEndIL);

                DebugConsole.Write("[CompileWithFunclets] EH clause ");
                DebugConsole.WriteDecimal((uint)i);
                DebugConsole.Write(": IL try=[");
                DebugConsole.WriteDecimal((uint)tryStartIL);
                DebugConsole.Write("-");
                DebugConsole.WriteDecimal((uint)tryEndIL);
                DebugConsole.Write("] -> native=[");
                DebugConsole.WriteDecimal((uint)nativeTryStartInt);
                DebugConsole.Write("-");
                DebugConsole.WriteDecimal((uint)nativeTryEndInt);
                DebugConsole.WriteLine("]");

                uint nativeTryStart = (uint)nativeTryStartInt;
                uint nativeTryEnd = (uint)nativeTryEndInt;

                // Handler offset is the funclet's code offset
                uint nativeHandlerStart = 0;
                uint nativeHandlerEnd = 0;
                uint nativeFilterStart = 0;

                // Find the funclets for this clause
                // For filter clauses, we need both the filter expression funclet and handler funclet
                for (int f = 0; f < _funcletCount; f++)
                {
                    int funcIdx = f * 4;
                    if (_funcletInfo[funcIdx + 2] == i)
                    {
                        int isFilterExpr = _funcletInfo[funcIdx + 3];
                        if (isFilterExpr == 1)
                        {
                            // This is the filter expression funclet
                            nativeFilterStart = (uint)_funcletInfo[funcIdx + 0];
                        }
                        else
                        {
                            // This is the handler funclet
                            nativeHandlerStart = (uint)_funcletInfo[funcIdx + 0];
                            nativeHandlerEnd = nativeHandlerStart + (uint)_funcletInfo[funcIdx + 1];
                        }
                    }
                }

                // The leave target is the first instruction after the handler block ends.
                // This is where leave instructions in the catch handler should return to.
                // We need the handler end IL offset and convert it to native offset.
                int handlerEndIL = _ehClauseData[ehIdx + 4];
                int nativeLeaveTargetInt = GetNativeOffset(handlerEndIL);
                uint nativeLeaveTarget = (uint)nativeLeaveTargetInt;

                // Resolve catch type token to MethodTable pointer for typed catch clauses
                ulong catchTypeMT = 0;
                uint filterOrClassToken = (uint)classToken;

                if (flags == (int)ILExceptionClauseFlags.Exception && classToken != 0)
                {
                    uint asmId = MetadataIntegration.GetCurrentAssemblyId();
                    if (asmId != 0)
                    {
                        MethodTable* resolved = AssemblyLoader.ResolveType(asmId, (uint)classToken);
                        if (resolved != null)
                        {
                            catchTypeMT = (ulong)resolved;
                        }
                    }
                }
                else if (flags == (int)ILExceptionClauseFlags.Filter)
                {
                    // For filter clauses, store the native filter funclet offset instead of IL offset
                    filterOrClassToken = nativeFilterStart;
                }

                JITExceptionClause clause;
                clause.Flags = (ILExceptionClauseFlags)flags;
                clause.TryStartOffset = nativeTryStart;
                clause.TryEndOffset = nativeTryEnd;
                clause.HandlerStartOffset = nativeHandlerStart;
                clause.HandlerEndOffset = nativeHandlerEnd;
                clause.ClassTokenOrFilterOffset = filterOrClassToken;
                clause.LeaveTargetOffset = nativeLeaveTarget;
                clause.IsValid = true;
                clause.CatchTypeMethodTable = catchTypeMT;

                nativeClauses.AddClause(clause);
            }
        }

        return code;
    }

    /// <summary>
    /// Set runtime helper function pointers for object allocation.
    /// Must be called before compiling code that uses newobj/newarr.
    /// </summary>
    public void SetAllocationHelpers(void* rhpNewFast, void* rhpNewArray)
    {
        _rhpNewFast = rhpNewFast;
        _rhpNewArray = rhpNewArray;
    }

    /// <summary>
    /// Compile Activator.CreateInstance&lt;T&gt;() as a JIT intrinsic.
    /// Allocates a new instance of T and calls the default constructor.
    /// </summary>
    private bool CompileActivatorCreateInstance(MethodTable* typeMT)
    {
        if (typeMT == null)
        {
            DebugConsole.WriteLine("[JIT] CompileActivatorCreateInstance: null typeMT");
            return false;
        }

        DebugConsole.Write("[JIT] CompileActivatorCreateInstance MT=0x");
        DebugConsole.WriteHex((ulong)typeMT);
        DebugConsole.Write(" RhpNewFast=0x");
        DebugConsole.WriteHex((ulong)_rhpNewFast);
        DebugConsole.WriteLine();

        bool isValueType = (typeMT->CombinedFlags & MTFlags.IsValueType) != 0;
        DebugConsole.Write("[JIT] CompileActivatorCreateInstance isValueType=");
        DebugConsole.Write(isValueType ? "true" : "false");
        DebugConsole.Write(" flags=0x");
        DebugConsole.WriteHex((ulong)typeMT->CombinedFlags);
        DebugConsole.WriteLine();

        if (isValueType)
        {
            // Reserve shadow space for calls (value type path)
            X64Emitter.SubRI(ref _code, VReg.SP, 32);

            // Value types: allocate on stack and zero-initialize
            // For simplicity, push a zeroed value onto the eval stack
            uint vtSize = typeMT->BaseSize;
            int alignedVtSize = (int)((vtSize + 7) & ~7UL);

            // Push zeroed slots for the struct
            X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);  // RAX = 0
            int slots = (alignedVtSize + 7) / 8;
            for (int i = 0; i < slots; i++)
            {
                X64Emitter.Push(ref _code, VReg.R0);
            }

            // Find and call default constructor if exists
            void* ctorNativeCode = MetadataIntegration.FindDefaultConstructor(typeMT);
            if (ctorNativeCode != null)
            {
                // LEA RCX, [RSP] - 'this' points to the stack-allocated struct
                // But we just pushed, so the struct is at RSP now
                X64Emitter.Lea(ref _code, VReg.R1, VReg.SP, 0);

                // Call constructor
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)ctorNativeCode);
                X64Emitter.CallR(ref _code, VReg.R0);
                RecordSafePoint();
            }

            // Restore shadow space
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // Struct is already on the eval stack from the push above
            if (alignedVtSize <= 8)
            {
                PushEntry(EvalStackEntry.NativeInt);
            }
            else
            {
                PushEntry(EvalStackEntry.Struct(alignedVtSize));
            }
        }
        else
        {
            // Reference type: allocate on heap via RhpNewFast

            // IMPORTANT: Find and compile the constructor FIRST, before emitting any code.
            // This ensures that nested JIT compilation happens before we emit addresses,
            // avoiding any potential corruption of already-emitted code.
            void* ctorNativeCode = MetadataIntegration.FindDefaultConstructor(typeMT);
            DebugConsole.Write("[JIT] CompileActivatorCreateInstance ctor=0x");
            DebugConsole.WriteHex((ulong)ctorNativeCode);
            DebugConsole.WriteLine();

            // Now emit all the code with the resolved addresses
            // Use R12 (callee-saved) to preserve object pointer across ctor call
            X64Emitter.Push(ref _code, VReg.R8);  // Save R12

            // Reserve shadow space for calls
            X64Emitter.SubRI(ref _code, VReg.SP, 32);

            // Load MethodTable* into RCX and call RhpNewFast
            X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)typeMT);  // RCX = MethodTable*
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
            X64Emitter.CallR(ref _code, VReg.R0);
            RecordSafePoint();

            // RAX = new object pointer, save to R12 (survives ctor call)
            X64Emitter.MovRR(ref _code, VReg.R8, VReg.R0);

            if (ctorNativeCode != null)
            {
                // RCX = object pointer ('this')
                X64Emitter.MovRR(ref _code, VReg.R1, VReg.R8);

                // Call constructor
                X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)ctorNativeCode);
                X64Emitter.CallR(ref _code, VReg.R0);
                RecordSafePoint();
            }

            // Restore shadow space - now [RSP] points to saved R12
            X64Emitter.AddRI(ref _code, VReg.SP, 32);

            // Swap R12 and saved R12 slot: store object to stack, restore R12
            // Currently [RSP] = old R12, R12 = object
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);   // RAX = old R12
            X64Emitter.MovMR(ref _code, VReg.SP, 0, VReg.R8);   // [RSP] = object
            X64Emitter.MovRR(ref _code, VReg.R8, VReg.R0);      // R12 = old R12

            // Object pointer is on the stack, track it
            PushEntry(EvalStackEntry.NativeInt);
        }

        DebugConsole.WriteLine("[JIT] CompileActivatorCreateInstance done");
        return true;
    }

    /// <summary>
    /// Compile RuntimeHelpers.InitializeArray as a JIT intrinsic.
    /// Copies static data embedded in the assembly to an array's data section.
    /// Stack: [..., Array array, RuntimeFieldHandle fldHandle] -> [...]
    /// </summary>
    private bool CompileInitializeArray()
    {
        DebugConsole.WriteLine("[JIT] CompileInitializeArray - inlining array initialization");

        // Stack has: [array, fieldHandle] with fieldHandle on top
        // The fieldHandle.Value is a pointer to the static data in the assembly

        // Pop fieldHandle (pointer to static data)
        PopReg(VReg.R2);  // RDX = fieldHandle.Value (source pointer)

        // Pop array reference
        PopReg(VReg.R1);  // RCX = array reference

        // R1 (RCX) = array object pointer
        // R2 (RDX) = source data pointer (from RuntimeFieldHandle)

        // Get MethodTable* from array object: MT = *(void**)array
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R1, 0);  // RAX = array->MethodTable

        // Get componentSize from MethodTable: componentSize = *(ushort*)MT (offset 0)
        // movzx r8d, word ptr [rax]
        _code.EmitByte(0x44);  // REX.R
        _code.EmitByte(0x0F);  // 2-byte opcode prefix
        _code.EmitByte(0xB7);  // MOVZX r32, r/m16
        _code.EmitByte(0x00);  // ModR/M: R8, [RAX]
        // R8 = componentSize

        // Get array length: length = *(uint*)(array + 8)
        // For SZ arrays, length is at offset 8 (after MT pointer)
        X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R1, 8);  // EAX = array.Length

        // Calculate total bytes: totalBytes = length * componentSize
        // imul eax, r8d
        _code.EmitByte(0x41);  // REX.B
        _code.EmitByte(0x0F);
        _code.EmitByte(0xAF);  // IMUL r32, r/m32
        _code.EmitByte(0xC0);  // ModR/M: EAX, R8D
        // RAX = total bytes to copy

        // Calculate destination: dest = array + 16 (data starts after MT + Length + padding)
        X64Emitter.Lea(ref _code, VReg.R3, VReg.R1, 16);  // R8 = array data pointer (dest)
        // Note: VReg.R3 maps to x64 R8

        // Source is already in RDX

        // Now emit a simple byte copy loop:
        // for (int i = 0; i < totalBytes; i++) dest[i] = src[i]
        //
        // Using RCX as counter (totalBytes), RSI as src, RDI as dest
        // But we can use a simpler approach with indexed addressing

        // Move count to RCX for rep movsb
        X64Emitter.MovRR(ref _code, VReg.R1, VReg.R0);  // RCX = byte count

        // Save RSI and RDI (callee-saved in Windows x64 ABI)
        // push rsi
        _code.EmitByte(0x56);
        // push rdi
        _code.EmitByte(0x57);

        // Set up RSI (source) and RDI (dest) for rep movsb
        // RSI = RDX (source), RDI = R8 (dest)
        // mov rsi, rdx
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x89);
        _code.EmitByte(0xD6);  // ModR/M: RSI, RDX

        // mov rdi, r8
        _code.EmitByte(0x4C);  // REX.WR
        _code.EmitByte(0x89);
        _code.EmitByte(0xC7);  // ModR/M: RDI, R8

        // rep movsb - copy RCX bytes from [RSI] to [RDI]
        _code.EmitByte(0xF3);  // REP prefix
        _code.EmitByte(0xA4);  // MOVSB

        // Restore RDI and RSI
        // pop rdi
        _code.EmitByte(0x5F);
        // pop rsi
        _code.EmitByte(0x5E);

        DebugConsole.WriteLine("[JIT] CompileInitializeArray done");
        return true;
    }
}
