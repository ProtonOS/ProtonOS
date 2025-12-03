// ProtonOS JIT - IL Compiler
// Compiles IL bytecode to x64 machine code using naive stack-based approach.

using ProtonOS.Platform;
using ProtonOS.X64;
using ProtonOS.Arch;

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
}

/// <summary>
/// Delegate for resolving field tokens (ldfld, stfld, ldsfld, stsfld).
/// </summary>
/// <param name="token">Field token (FieldDef 0x04, MemberRef 0x0A)</param>
/// <param name="result">Output: resolved field information</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool FieldResolver(uint token, out ResolvedField result);

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

    // Method info
    private int _argCount;
    private int _localCount;
    private int _stackAdjust;

    // GC tracking: bitmask for which locals/args are GC references
    // Bit i set = local i is GC reference (for i < localCount)
    // Bit (localCount + i) set = arg i is GC reference
    private ulong _gcRefMask;

    // GC info builder for stack root enumeration
    private JITGCInfo _gcInfo;

    // Evaluation stack tracking (naive approach: just track depth)
    // For a more sophisticated approach, track register allocation
    private int _evalStackDepth;

    // Stack type tracking for float support (0=int, 1=float32, 2=float64)
    private const int MaxStackDepth = 32;
    private fixed byte _evalStackTypes[MaxStackDepth];

    // Fixed-size array for branch targets
    private const int MaxBranches = 64;
    private fixed int _branchSources[MaxBranches];  // IL offset where branch was emitted
    private fixed int _branchTargetIL[MaxBranches]; // IL offset branch targets
    private fixed int _branchPatchOffset[MaxBranches]; // Code offset to patch
    private int _branchCount;

    // Label mapping: IL offset -> code offset
    private const int MaxLabels = 128;
    private fixed int _labelILOffset[MaxLabels];
    private fixed int _labelCodeOffset[MaxLabels];
    private int _labelCount;

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
        compiler._argCount = argCount;
        compiler._localCount = localCount;
        compiler._stackAdjust = 0;
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

        // Initialize funclet support
        compiler._ehClauseCount = 0;
        compiler._funcletCount = 0;
        compiler._compilingFunclet = false;

        // Create code buffer (4KB should be plenty for simple methods)
        compiler._code = CodeBuffer.Create(4096);

        return compiler;
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
        fixed (int* ilOffsets = _labelILOffset)
        fixed (int* nativeOffsets = _labelCodeOffset)
        {
            return EHClauseConverter.ConvertClauses(
                ref ilClauses,
                out nativeClauses,
                ilOffsets,
                nativeOffsets,
                _labelCount);
        }
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
    /// Compile the IL method to native code.
    /// Returns function pointer or null on failure.
    /// </summary>
    public void* Compile()
    {
        // Emit prologue
        // Calculate local space: localCount * 8 + evalStack space
        int localBytes = _localCount * 8 + 64;  // 64 bytes for eval stack
        _stackAdjust = X64Emitter.EmitPrologue(ref _code, localBytes);

        // Home arguments to shadow space (so we can load them later)
        if (_argCount > 0)
            X64Emitter.HomeArguments(ref _code, _argCount);

        // Process IL
        while (_ilOffset < _ilLength)
        {
            // Record label for this IL offset
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

        // Patch forward branches
        PatchBranches();

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
            _branchCount++;
        }
    }

    private void PatchBranches()
    {
        for (int i = 0; i < _branchCount; i++)
        {
            int targetIL = _branchTargetIL[i];
            int codeOffset = FindCodeOffset(targetIL);
            if (codeOffset >= 0)
            {
                int patchOffset = _branchPatchOffset[i];
                // Calculate relative offset: target - (patch + 4)
                int rel = codeOffset - (patchOffset + 4);
                _code.PatchInt32(patchOffset, rel);
            }
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
                    sbyte offset = (sbyte)_il[_ilOffset++];
                    return CompileLeave(_ilOffset + offset);
                }

            case ILOpcode.Leave:
                {
                    int offset = *(int*)(_il + _ilOffset);
                    _ilOffset += 4;
                    return CompileLeave(_ilOffset + offset);
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
                    uint token = *(uint*)(_il + _ilOffset);
                    _ilOffset += 4;
                    // For Tier 0 JIT, we ignore the constraint and let the next callvirt handle it
                    // The constrained prefix is an optimization hint that allows devirtualization
                    return true;
                }
            case ILOpcode.Readonly_2:
                // readonly. prefix - indicates ldelema result won't be used for writes
                // For naive JIT, this is a no-op (just an optimization hint)
                return true;
            case ILOpcode.Tail_2:
                // tail. prefix - indicates a tail call follows
                // For naive JIT, we ignore this and do a normal call
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
        // For naive implementation: mov rax, imm64; push rax
        // We sign-extend to 64-bit so that comparisons with long work correctly
        // and so that returning negative i4 values works properly
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)(long)value);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdarg(int index)
    {
        // Load argument from shadow space - use full 64-bit load
        // This handles both native int (pointer) and int32 arguments correctly
        // For int32 args, the caller already zero-extends per Microsoft x64 ABI
        X64Emitter.LoadArgFromHome(ref _code, VReg.R0, index);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdloc(int index)
    {
        // Locals are at negative offsets from RBP
        // After prologue: [rbp-8] = local0, [rbp-16] = local1, etc.
        // But we need to account for shadow space + stack adjustment
        int offset = X64Emitter.GetLocalOffset(index);
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileStloc(int index)
    {
        // Pop from eval stack, store to local
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;
        int offset = X64Emitter.GetLocalOffset(index);
        X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
        return true;
    }

    private bool CompileDup()
    {
        // Duplicate top of stack
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);  // Read top without popping
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompilePop()
    {
        // Discard top of stack
        X64Emitter.AddRI(ref _code, VReg.SP, 8);
        PopStackType();
        return true;
    }

    // Stack type constants
    private const byte StackType_Int = 0;
    private const byte StackType_Float32 = 1;
    private const byte StackType_Float64 = 2;

    // Helper to push a type onto the type stack
    private void PushStackType(byte type)
    {
        if (_evalStackDepth < MaxStackDepth)
        {
            fixed (byte* types = _evalStackTypes)
            {
                types[_evalStackDepth] = type;
            }
        }
        _evalStackDepth++;
    }

    // Helper to pop a type from the type stack
    private byte PopStackType()
    {
        if (_evalStackDepth > 0)
        {
            _evalStackDepth--;
            fixed (byte* types = _evalStackTypes)
            {
                return types[_evalStackDepth];
            }
        }
        return StackType_Int;
    }

    // Helper to peek at the top type
    private byte PeekStackType()
    {
        if (_evalStackDepth > 0)
        {
            fixed (byte* types = _evalStackTypes)
            {
                return types[_evalStackDepth - 1];
            }
        }
        return StackType_Int;
    }

    // Check if type is float (single or double)
    private bool IsFloatType(byte type) => type == StackType_Float32 || type == StackType_Float64;

    private bool CompileAdd()
    {
        // Pop two values, add, push result
        // Peek at operand types before popping
        byte type2 = PopStackType();
        byte type1 = PopStackType();

        if (IsFloatType(type1) || IsFloatType(type2))
        {
            // Float arithmetic - use SSE
            // Pop both operands from memory stack
            X64Emitter.Pop(ref _code, VReg.R2);  // Second operand (bits)
            X64Emitter.Pop(ref _code, VReg.R0);  // First operand (bits)

            // Determine if single or double precision (use widest type)
            bool isDouble = (type1 == StackType_Float64 || type2 == StackType_Float64);

            if (isDouble)
            {
                // Move bit patterns to XMM registers
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                // ADDSD xmm0, xmm1
                X64Emitter.AddsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                // Move result bits back
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float64);
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
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float32);
            }
        }
        else
        {
            // Integer arithmetic
            X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
            X64Emitter.Pop(ref _code, VReg.R0);  // First operand
            X64Emitter.AddRR(ref _code, VReg.R0, VReg.R2);
            X64Emitter.Push(ref _code, VReg.R0);
            PushStackType(StackType_Int);
        }
        return true;
    }

    private bool CompileSub()
    {
        // Pop two values, subtract, push result
        byte type2 = PopStackType();
        byte type1 = PopStackType();

        if (IsFloatType(type1) || IsFloatType(type2))
        {
            // Float arithmetic - use SSE
            X64Emitter.Pop(ref _code, VReg.R2);  // Second operand (bits)
            X64Emitter.Pop(ref _code, VReg.R0);  // First operand (bits)

            bool isDouble = (type1 == StackType_Float64 || type2 == StackType_Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.SubsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.SubssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float32);
            }
        }
        else
        {
            // Integer arithmetic
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R0);
            X64Emitter.SubRR(ref _code, VReg.R0, VReg.R2);
            X64Emitter.Push(ref _code, VReg.R0);
            PushStackType(StackType_Int);
        }
        return true;
    }

    private bool CompileMul()
    {
        // Pop two values, multiply, push result
        byte type2 = PopStackType();
        byte type1 = PopStackType();

        if (IsFloatType(type1) || IsFloatType(type2))
        {
            // Float arithmetic - use SSE
            X64Emitter.Pop(ref _code, VReg.R2);  // Second operand (bits)
            X64Emitter.Pop(ref _code, VReg.R0);  // First operand (bits)

            bool isDouble = (type1 == StackType_Float64 || type2 == StackType_Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.MulsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R2);
                X64Emitter.MulssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float32);
            }
        }
        else
        {
            // Integer arithmetic
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R0);
            X64Emitter.ImulRR(ref _code, VReg.R0, VReg.R2);
            X64Emitter.Push(ref _code, VReg.R0);
            PushStackType(StackType_Int);
        }
        return true;
    }

    // Overflow-checking arithmetic
    // For signed: check OF (overflow flag) with JO (0x70)
    // For unsigned: check CF (carry flag) with JC (0x72)

    private bool CompileAddOvf(bool unsigned)
    {
        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand
        _evalStackDepth -= 2;
        X64Emitter.AddRR(ref _code, VReg.R0, VReg.R2);
        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileSubOvf(bool unsigned)
    {
        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand (subtrahend)
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand (minuend)
        _evalStackDepth -= 2;
        X64Emitter.SubRR(ref _code, VReg.R0, VReg.R2);
        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1 = borrow)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileMulOvf(bool unsigned)
    {
        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand
        _evalStackDepth -= 2;

        if (unsigned)
        {
            // mul rdx: unsigned RAX * RDX -> RDX:RAX
            // If RDX != 0 after, overflow occurred
            // Encoding: REX.W + F7 /4 (mul r/m64)
            _code.EmitByte(0x48);  // REX.W
            _code.EmitByte(0xF7);  // MUL
            _code.EmitByte(0xE2);  // ModRM: /4 rdx
            // mul sets CF=OF=1 if high half is non-zero
            EmitJccToInt3(0x72);  // JC overflow
        }
        else
        {
            // imul rax, rdx: signed, result in RAX, sets OF if overflow
            X64Emitter.ImulRR(ref _code, VReg.R0, VReg.R2);
            EmitJccToInt3(0x70);  // JO overflow
        }

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
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
        _evalStackDepth--;

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
        _evalStackDepth++;
        return true;
    }

    private bool CompileNeg()
    {
        X64Emitter.Pop(ref _code, VReg.R0);
        X64Emitter.Neg(ref _code, VReg.R0);
        X64Emitter.Push(ref _code, VReg.R0);
        return true;
    }

    private bool CompileAnd()
    {
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
        X64Emitter.AndRR(ref _code, VReg.R0, VReg.R2);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileOr()
    {
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
        X64Emitter.OrRR(ref _code, VReg.R0, VReg.R2);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileXor()
    {
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R2);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileNot()
    {
        X64Emitter.Pop(ref _code, VReg.R0);
        X64Emitter.Not(ref _code, VReg.R0);
        X64Emitter.Push(ref _code, VReg.R0);
        return true;
    }

    private bool CompileDiv(bool signed)
    {
        // Division: dividend / divisor
        // IL stack: [..., dividend, divisor] -> [..., quotient]
        byte type2 = PopStackType();
        byte type1 = PopStackType();

        if (IsFloatType(type1) || IsFloatType(type2))
        {
            // Float division - use SSE
            X64Emitter.Pop(ref _code, VReg.R1);  // Divisor (bits)
            X64Emitter.Pop(ref _code, VReg.R0);  // Dividend (bits)

            bool isDouble = (type1 == StackType_Float64 || type2 == StackType_Float64);

            if (isDouble)
            {
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovqXmmR64(ref _code, RegXMM.XMM1, VReg.R1);
                X64Emitter.DivsdXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float64);
            }
            else
            {
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
                X64Emitter.MovdXmmR32(ref _code, RegXMM.XMM1, VReg.R1);
                X64Emitter.DivssXmmXmm(ref _code, RegXMM.XMM0, RegXMM.XMM1);
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                PushStackType(StackType_Float32);
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
            PushStackType(StackType_Int);
        }
        return true;
    }

    private bool CompileRem(bool signed)
    {
        // Remainder: dividend % divisor
        // IL stack: [..., dividend, divisor] -> [..., remainder]
        X64Emitter.Pop(ref _code, VReg.R1);  // Divisor to RCX
        X64Emitter.Pop(ref _code, VReg.R0);  // Dividend to RAX
        _evalStackDepth -= 2;

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
        _evalStackDepth++;
        return true;
    }

    private bool CompileShl()
    {
        // Shift left: value << shiftAmount
        // IL stack: [..., value, shiftAmount] -> [..., result]
        X64Emitter.Pop(ref _code, VReg.R1);  // Shift amount to CL
        X64Emitter.Pop(ref _code, VReg.R0);  // Value to shift
        _evalStackDepth -= 2;
        X64Emitter.ShlCL(ref _code, VReg.R0);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileShr(bool signed)
    {
        // Shift right: value >> shiftAmount
        X64Emitter.Pop(ref _code, VReg.R1);  // Shift amount to CL
        X64Emitter.Pop(ref _code, VReg.R0);  // Value to shift
        _evalStackDepth -= 2;

        if (signed)
        {
            X64Emitter.SarCL(ref _code, VReg.R0);  // Arithmetic shift (preserves sign)
        }
        else
        {
            // For unsigned 32-bit shift, zero-extend the value first
            // so logical shift fills with zeros from the correct upper bit
            X64Emitter.ZeroExtend32(ref _code, VReg.R0);
            X64Emitter.ShrCL(ref _code, VReg.R0);  // Logical shift (zero-fill)
        }

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileConv(int targetBytes, bool signed)
    {
        // Convert top of stack to different size
        // For naive JIT, we just mask/sign-extend as needed
        X64Emitter.Pop(ref _code, VReg.R0);

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
                // conv.i (signed): sign-extend from 32-bit to 64-bit
                // conv.u (unsigned): zero-extend from 32-bit to 64-bit
                // Note: ldc.i4 uses MOV r32, imm32 which zero-extends, so we need
                // to explicitly sign-extend for conv.i
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
        }

        X64Emitter.Push(ref _code, VReg.R0);
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
        _code.EmitByte(invertedJcc);  // Jcc_not (skip INT3)
        _code.EmitByte(1);            // rel8 = +1 (skip 1 byte)
        _code.EmitByte(0xCC);         // INT3
    }

    private bool CompileLdcI8(long value)
    {
        // Load 64-bit constant
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)value);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdcR4(uint bits)
    {
        // Load float constant - store bit pattern and push to stack
        // Float is 4 bytes, but we push 8 bytes (zero-extended)
        X64Emitter.MovRI32(ref _code, VReg.R0, (int)bits);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Float32);
        return true;
    }

    private bool CompileLdcR8(ulong bits)
    {
        // Load double constant - push 64-bit pattern to stack
        X64Emitter.MovRI64(ref _code, VReg.R0, bits);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Float64);
        return true;
    }

    private bool CompileConvR4()
    {
        // Convert integer to float (single precision)
        // Pop value, convert to float, push back as 32-bit pattern (zero-extended)
        X64Emitter.Pop(ref _code, VReg.R0);
        PopStackType();  // Pop whatever type was there
        // Use 32-bit source for proper signed int32 semantics
        // This ensures -10 is treated as signed -10, not as unsigned 0xFFFFFFF6
        X64Emitter.Cvtsi2ssXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
        // MOVD eax, xmm0 - move float bits to integer reg
        X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Float32);
        return true;
    }

    private bool CompileConvR8()
    {
        // Convert integer to double precision
        // Pop value, convert to double, push back as 64-bit pattern
        X64Emitter.Pop(ref _code, VReg.R0);
        PopStackType();  // Pop whatever type was there
        // Use 32-bit source for proper signed int32 semantics
        // This ensures -10 is treated as signed -10, not as unsigned 0xFFFFFFF6
        X64Emitter.Cvtsi2sdXmmR32(ref _code, RegXMM.XMM0, VReg.R0);
        // MOVQ rax, xmm0 - move double bits to integer reg
        X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Float64);
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
        PopStackType();

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
        PushStackType(StackType_Float64);
        return true;
    }

    private bool CompileLdnull()
    {
        // Load null reference (0)
        X64Emitter.XorRR(ref _code, VReg.R0, VReg.R0);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileStarg(int index)
    {
        // Pop from eval stack, store to argument home location
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;
        int offset = 16 + index * 8;  // Shadow space offset
        X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
        return true;
    }

    private bool CompileLdarga(int index)
    {
        // Load address of argument
        int offset = 16 + index * 8;
        X64Emitter.Lea(ref _code, VReg.R0, VReg.FP, offset);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdloca(int index)
    {
        // Load address of local variable
        int offset = X64Emitter.GetLocalOffset(index);
        X64Emitter.Lea(ref _code, VReg.R0, VReg.FP, offset);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileRet()
    {
        // If there's a return value, it should be on eval stack
        if (_evalStackDepth > 0)
        {
            X64Emitter.Pop(ref _code, VReg.R0);  // Return value in RAX
            _evalStackDepth--;
        }

        // Emit epilogue
        X64Emitter.EmitEpilogue(ref _code, _stackAdjust);
        return true;
    }

    private bool CompileBr(int targetIL)
    {
        int patchOffset = X64Emitter.JmpRel32(ref _code);
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBrfalse(int targetIL)
    {
        // Pop value, branch if zero
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int patchOffset = X64Emitter.Je(ref _code);  // Jump if equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBrtrue(int targetIL)
    {
        // Pop value, branch if non-zero
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;
        X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
        int patchOffset = X64Emitter.Jne(ref _code);  // Jump if not equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBranchCmp(byte cc, int targetIL)
    {
        // Pop two values, compare, branch based on condition
        X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
        X64Emitter.Pop(ref _code, VReg.R0);  // First operand
        _evalStackDepth -= 2;
        // Use 32-bit comparison for proper signed int32 semantics
        // This ensures -5 < 0 works correctly (64-bit would see 0x00000000FFFFFFFB as positive)
        X64Emitter.CmpRR32(ref _code, VReg.R0, VReg.R2);
        int patchOffset = X64Emitter.JccRel32(ref _code, cc);
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileCeq()
    {
        // Compare equal: pop two values, push 1 if equal, 0 otherwise
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
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

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileCgt(bool signed)
    {
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
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

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        return true;
    }

    private bool CompileClt(bool signed)
    {
        X64Emitter.Pop(ref _code, VReg.R2);
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth -= 2;
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

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
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
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;

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
        _evalStackDepth--;

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
        _evalStackDepth++;

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
        _evalStackDepth--;

        // Pop address into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;

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
        _evalStackDepth--;

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
        _evalStackDepth++;

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
        _evalStackDepth--;

        // Pop address into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;

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
            DebugConsole.Write("[JIT] Method token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(" not found in registry");
            return false;
        }

        if (!info->IsCompiled)
        {
            DebugConsole.Write("[JIT] Method token 0x");
            DebugConsole.WriteHex(token);
            DebugConsole.WriteLine(" not yet compiled");
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
        // Resolve the method
        if (!ResolveMethod(token, out ResolvedMethod method))
        {
            return false;
        }

        int totalArgs = method.ArgCount;
        if (method.HasThis)
            totalArgs++;  // Instance methods have implicit 'this' as first arg

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalArgs)
        {
            DebugConsole.Write("[JIT] Call: insufficient stack depth ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" for ");
            DebugConsole.WriteDecimal((uint)totalArgs);
            DebugConsole.WriteLine(" args");
            return false;
        }

        // Calculate extra stack space needed for args beyond the first 4
        // The first 4 args go in registers (RCX, RDX, R8, R9)
        // Args 5+ go on the stack at [RSP+32], [RSP+40], etc.
        int stackArgs = totalArgs > 4 ? totalArgs - 4 : 0;

        // Pop arguments from eval stack into registers or temp storage
        // IL stack has args in order: arg0 at bottom, argN-1 at top
        // We need to pop in reverse order (top first)

        if (totalArgs == 0)
        {
            // No arguments - just call
        }
        else if (totalArgs == 1)
        {
            // Single arg: pop to RCX
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth--;
        }
        else if (totalArgs == 2)
        {
            // Two args: pop to RDX (arg1), then RCX (arg0)
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 2;
        }
        else if (totalArgs == 3)
        {
            // Three args: pop to R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 3;
        }
        else if (totalArgs == 4)
        {
            // Four args: pop to R9, R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R4);    // arg3
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 4;
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

            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;

            // Load register args from eval stack (before RSP changes)
            // arg0 at [RSP + (totalArgs-1)*8], arg1 at [RSP + (totalArgs-2)*8], etc.
            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, (totalArgs - 1) * 8);  // arg0
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, (totalArgs - 2) * 8);  // arg1
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, (totalArgs - 3) * 8);   // arg2
            X64Emitter.MovRM(ref _code, VReg.R4, VReg.SP, (totalArgs - 4) * 8);   // arg3

            // WORKING APPROACH:
            // 1. Compute final RSP position and work backwards
            // 2. Current RSP has eval stack, we need to end up at RSP + totalArgs*8 - extraStackSpace
            //
            // Let finalRSP = RSP + totalArgs*8 - extraStackSpace
            // Stack args need to be at [finalRSP + 32], [finalRSP + 40], etc.
            // In terms of current RSP: [RSP + totalArgs*8 - extraStackSpace + 32], etc.
            //
            // So we can copy stack args to their final locations BEFORE adjusting RSP!

            // Copy stack args to their final locations (relative to current RSP)
            for (int i = 0; i < stackArgs; i++)
            {
                // arg(4+i) source: [RSP + (stackArgs-1-i)*8]
                // arg(4+i) dest: [RSP + totalArgs*8 - extraStackSpace + 32 + i*8]
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = totalArgs * 8 - extraStackSpace + 32 + i * 8;
                X64Emitter.MovRM(ref _code, VReg.R5, VReg.SP, srcOffset);
                X64Emitter.MovMR(ref _code, VReg.SP, dstOffset, VReg.R5);
            }

            // Now adjust RSP to point to the call frame
            // Final RSP = current RSP + totalArgs*8 - extraStackSpace
            int rspAdjust = totalArgs * 8 - extraStackSpace;
            if (rspAdjust > 0)
            {
                X64Emitter.AddRI(ref _code, VReg.SP, rspAdjust);
            }
            else if (rspAdjust < 0)
            {
                X64Emitter.SubRI(ref _code, VReg.SP, -rspAdjust);
            }

            _evalStackDepth -= totalArgs;
        }

        // Load target address and call
        // We use an indirect call through RAX to support any address
        if (method.NativeCode != null)
        {
            // Direct call - we already know the target address
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
        }
        else if (method.RegistryEntry != null)
        {
            // Indirect call through registry entry (for recursive/pending methods)
            // The NativeCode will be filled in by the time the call actually executes
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

        // Clean up extra stack space if we allocated any
        if (stackArgs > 0)
        {
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            X64Emitter.AddRI(ref _code, VReg.SP, extraStackSpace);
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
                _evalStackDepth++;
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                // Return value in RAX - push directly
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float32:
                // Return value in XMM0 - move to RAX and push
                // movd eax, xmm0
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float64:
                // Return value in XMM0 - move to RAX and push
                // movq rax, xmm0
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Struct:
                // Struct returns are complex - for now, treat as pointer in RAX
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;
        }

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
            return false;
        }

        // Callvirt always has 'this' as the first argument
        // Even if method.HasThis is false (shouldn't happen), we treat it as instance
        int totalArgs = method.ArgCount;
        if (method.HasThis || true) // callvirt always has 'this'
            totalArgs++;

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalArgs)
        {
            DebugConsole.Write("[JIT] Callvirt: insufficient stack depth ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" for ");
            DebugConsole.WriteDecimal((uint)totalArgs);
            DebugConsole.WriteLine(" args");
            return false;
        }

        // The rest is identical to CompileCall - pop args, set up registers, call, handle return
        int stackArgs = totalArgs > 4 ? totalArgs - 4 : 0;

        if (totalArgs == 0)
        {
            // No arguments - just call (shouldn't happen for callvirt)
        }
        else if (totalArgs == 1)
        {
            // Single arg (this): pop to RCX
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth--;
        }
        else if (totalArgs == 2)
        {
            // Two args: pop to RDX (arg1), then RCX (this)
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth -= 2;
        }
        else if (totalArgs == 3)
        {
            // Three args: pop to R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R3);
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth -= 3;
        }
        else if (totalArgs == 4)
        {
            // Four args: pop to R9, R8, RDX, RCX
            X64Emitter.Pop(ref _code, VReg.R4);
            X64Emitter.Pop(ref _code, VReg.R3);
            X64Emitter.Pop(ref _code, VReg.R2);
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth -= 4;
        }
        else
        {
            // More than 4 args - handle stack args (same as CompileCall)
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;

            X64Emitter.MovRM(ref _code, VReg.R1, VReg.SP, (totalArgs - 1) * 8);
            X64Emitter.MovRM(ref _code, VReg.R2, VReg.SP, (totalArgs - 2) * 8);
            X64Emitter.MovRM(ref _code, VReg.R3, VReg.SP, (totalArgs - 3) * 8);
            X64Emitter.MovRM(ref _code, VReg.R4, VReg.SP, (totalArgs - 4) * 8);

            for (int i = 0; i < stackArgs; i++)
            {
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = totalArgs * 8 - extraStackSpace + 32 + i * 8;
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

            _evalStackDepth -= totalArgs;
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
            // 1. Load MethodTable* from [RCX] (object header is the MT pointer)
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R1, 0);  // RAX = *this = MethodTable*

            // 2. Load vtable slot at offset HeaderSize + slot*8
            // MethodTable.HeaderSize = 24 bytes, each vtable slot is 8 bytes
            int vtableOffset = ProtonOS.Runtime.MethodTable.HeaderSize + (method.VtableSlot * 8);
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, vtableOffset);  // RAX = vtable[slot]

            // 3. Call through the vtable slot
            X64Emitter.CallR(ref _code, VReg.R0);
        }
        else
        {
            // Direct call (devirtualized or non-virtual)
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)method.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
        }

        // Record safe point after call
        RecordSafePoint();

        // Clean up extra stack space if we allocated any
        if (stackArgs > 0)
        {
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            X64Emitter.AddRI(ref _code, VReg.SP, extraStackSpace);
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
                _evalStackDepth++;
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float32:
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float64:
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Struct:
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
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
        // Decode the signature token
        // For our test purposes, we encode: (ReturnKind << 8) | ArgCount
        int argCount = (int)(sigToken & 0xFF);
        ReturnKind returnKind = (ReturnKind)((sigToken >> 8) & 0xFF);

        // Total stack items needed: argCount args + 1 function pointer
        int totalStackItems = argCount + 1;

        // Verify we have enough values on the eval stack
        if (_evalStackDepth < totalStackItems)
        {
            DebugConsole.Write("[JIT] Calli: insufficient stack depth ");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.Write(" for ");
            DebugConsole.WriteDecimal((uint)totalStackItems);
            DebugConsole.WriteLine(" items (args + ftnPtr)");
            return false;
        }

        // First, pop the function pointer from the top of the stack into a safe register
        // We'll use R11 since it's caller-saved and won't be clobbered by arg setup
        X64Emitter.Pop(ref _code, VReg.R6);  // ftnPtr
        _evalStackDepth--;

        // Calculate stack args needed (args beyond the first 4 go on stack)
        int stackArgs = argCount > 4 ? argCount - 4 : 0;

        // Now pop arguments and set up the call (same logic as CompileCall)
        if (argCount == 0)
        {
            // No arguments - just call
        }
        else if (argCount == 1)
        {
            X64Emitter.Pop(ref _code, VReg.R1);
            _evalStackDepth--;
        }
        else if (argCount == 2)
        {
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 2;
        }
        else if (argCount == 3)
        {
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 3;
        }
        else if (argCount == 4)
        {
            X64Emitter.Pop(ref _code, VReg.R4);    // arg3
            X64Emitter.Pop(ref _code, VReg.R3);    // arg2
            X64Emitter.Pop(ref _code, VReg.R2);   // arg1
            X64Emitter.Pop(ref _code, VReg.R1);   // arg0
            _evalStackDepth -= 4;
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
            // arg(4+i) source: [RSP + (stackArgs-1-i)*8]
            // arg(4+i) dest: [RSP + argCount*8 - extraStackSpace + 32 + i*8]
            for (int i = 0; i < stackArgs; i++)
            {
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = argCount * 8 - extraStackSpace + 32 + i * 8;
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

            _evalStackDepth -= argCount;
        }

        // Call through the function pointer in R11
        X64Emitter.CallR(ref _code, VReg.R6);

        // Record safe point after call (GC can happen during callee execution)
        RecordSafePoint();

        // Clean up extra stack space if we allocated any
        if (stackArgs > 0)
        {
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            X64Emitter.AddRI(ref _code, VReg.SP, extraStackSpace);
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
                _evalStackDepth++;
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float32:
                X64Emitter.MovdR32Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Float64:
                X64Emitter.MovqR64Xmm(ref _code, VReg.R0, RegXMM.XMM0);
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
                break;

            case ReturnKind.Struct:
                X64Emitter.Push(ref _code, VReg.R0);
                _evalStackDepth++;
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
            DebugConsole.WriteLine("[JIT] cpblk: insufficient stack depth");
            return false;
        }

        // Pop size into R8 (third arg)
        X64Emitter.Pop(ref _code, VReg.R3);
        _evalStackDepth--;

        // Pop srcAddr into RDX (second arg)
        X64Emitter.Pop(ref _code, VReg.R2);
        _evalStackDepth--;

        // Pop destAddr into RCX (first arg)
        X64Emitter.Pop(ref _code, VReg.R1);
        _evalStackDepth--;

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
            DebugConsole.WriteLine("[JIT] initblk: insufficient stack depth");
            return false;
        }

        // Pop size into R8 (third arg)
        X64Emitter.Pop(ref _code, VReg.R3);
        _evalStackDepth--;

        // Pop value into RDX (second arg) - note: IL has int32, MemSet takes byte
        // We just pass it through - the native function will use the low byte
        X64Emitter.Pop(ref _code, VReg.R2);
        _evalStackDepth--;

        // Pop addr into RCX (first arg)
        X64Emitter.Pop(ref _code, VReg.R1);
        _evalStackDepth--;

        // Call CPU.MemSet(dest, value, count)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memsetFn);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Return value (dest pointer) is discarded - initblk has no return on IL stack

        return true;
    }

    // ==================== Value Type Operations ====================
    // These opcodes take a type token. For testing, we encode the size directly in the token.
    // In a full implementation, we'd resolve the type token to get the size.

    /// <summary>
    /// Get size from a type token. For testing, we encode size directly in the low 16 bits.
    /// A real implementation would look up the type in metadata.
    /// </summary>
    private static int GetTypeSizeFromToken(uint token)
    {
        // For testing: token encodes the size directly
        return (int)(token & 0xFFFF);
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
            DebugConsole.WriteLine("[JIT] initobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop addr into RCX (first arg for MemSet)
        X64Emitter.Pop(ref _code, VReg.R1);
        _evalStackDepth--;

        // Value = 0 (second arg)
        X64Emitter.XorRR(ref _code, VReg.R2, VReg.R2);

        // Size in R8 (third arg)
        X64Emitter.MovRI64(ref _code, VReg.R3, (ulong)size);

        // Call CPU.MemSet(addr, 0, size)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)memsetFn);
        X64Emitter.CallR(ref _code, VReg.R0);

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
        _evalStackDepth++;

        return true;
    }

    /// <summary>
    /// Compile ldobj - Load value type from address.
    /// Stack: ..., addr -> ..., value
    /// For small sizes, we can load directly. For larger sizes, we'd need stack space.
    /// Currently supports sizes up to 8 bytes (fits in a register).
    /// </summary>
    private bool CompileLdobj(uint token)
    {
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] ldobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop addr into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;

        // Load value based on size
        switch (size)
        {
            case 1:
                X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 2:
                X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 4:
                X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, 0);
                break;
            case 8:
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 0);
                break;
            default:
                // For larger structs, we'd need to copy to stack
                // For now, just handle common small sizes
                DebugConsole.Write("[JIT] ldobj: unsupported size ");
                DebugConsole.WriteDecimal((uint)size);
                DebugConsole.WriteLine();
                return false;
        }

        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
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
            DebugConsole.WriteLine("[JIT] stobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop value into RDX
        X64Emitter.Pop(ref _code, VReg.R2);
        _evalStackDepth--;

        // Pop addr into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;

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
                X64Emitter.MovMR(ref _code, VReg.R0, 0, VReg.R2);
                break;
            default:
                DebugConsole.Write("[JIT] stobj: unsupported size ");
                DebugConsole.WriteDecimal((uint)size);
                DebugConsole.WriteLine();
                return false;
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
            DebugConsole.WriteLine("[JIT] cpobj: insufficient stack depth");
            return false;
        }

        int size = GetTypeSizeFromToken(token);

        // Pop srcAddr into RDX (second arg for MemCopy)
        X64Emitter.Pop(ref _code, VReg.R2);
        _evalStackDepth--;

        // Pop destAddr into RCX (first arg for MemCopy)
        X64Emitter.Pop(ref _code, VReg.R1);
        _evalStackDepth--;

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
    /// If leaving a try block with a finally handler, the finally is executed first
    /// (handled by the runtime, not by this instruction).
    /// </summary>
    private bool CompileLeave(int targetIL)
    {
        // Leave empties the evaluation stack (reset to 0)
        // We don't need to emit pops since we're jumping away and the
        // target IL expects an empty stack
        _evalStackDepth = 0;

        // Emit unconditional jump to target
        // This is the same as br but conceptually different - it's for exiting protected regions
        int patchOffset = X64Emitter.JmpRel32(ref _code);
        RecordBranch(_ilOffset, targetIL, patchOffset);

        return true;
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
            DebugConsole.WriteLine("[JIT] throw: insufficient stack depth");
            return false;
        }

        // Pop exception object into RCX (first arg for RhpThrowEx)
        X64Emitter.Pop(ref _code, VReg.R1);
        _evalStackDepth--;

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
            DebugConsole.Write("[JIT] endfinally: stack not empty (depth=");
            DebugConsole.WriteDecimal((uint)_evalStackDepth);
            DebugConsole.WriteLine(")");
            return false;
        }

        // When compiling a funclet, we need to emit the full funclet epilog:
        // pop rbp; ret - to restore the saved rbp and return to EH runtime.
        // When compiling inline handler, just emit 'ret'.
        if (_compilingFunclet)
        {
            // Funclet epilog: pop rbp (0x5D), ret (0xC3)
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

        // Try field resolver first, fall back to test token encoding
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
        {
            offset = field.Offset;
            size = field.Size;
            signed = field.IsSigned;
        }
        else
        {
            DecodeFieldToken(token, out offset, out size, out signed);
        }

        // Pop object reference
        X64Emitter.Pop(ref _code, VReg.R0);
        PopStackType();

        // Load field at obj + offset
        // mov RAX, [RAX + offset]
        switch (size)
        {
            case 1:
                if (signed)
                    X64Emitter.MovsxByte(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovzxByte(ref _code, VReg.R0, VReg.R0, offset);
                break;
            case 2:
                if (signed)
                    X64Emitter.MovsxWord(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovzxWord(ref _code, VReg.R0, VReg.R0, offset);
                break;
            case 4:
                if (signed)
                    X64Emitter.MovsxdRM(ref _code, VReg.R0, VReg.R0, offset);
                else
                    X64Emitter.MovRM32(ref _code, VReg.R0, VReg.R0, offset); // Zero-extends to 64-bit
                break;
            case 8:
            default:
                X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, offset);
                break;
        }

        // Push result
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);

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
        PopStackType();

        // Compute field address: obj + offset
        if (offset != 0)
        {
            X64Emitter.AddRI(ref _code, VReg.R0, offset);
        }

        // Push address
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);

        return true;
    }

    /// <summary>
    /// Compile stfld - Store instance field.
    /// Stack: ..., obj, value -> ...
    /// </summary>
    private bool CompileStfld(uint token)
    {
        int offset;
        int size;

        // Try field resolver first, fall back to test token encoding
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
        {
            offset = field.Offset;
            size = field.Size;
        }
        else
        {
            DecodeFieldToken(token, out offset, out size, out _);
        }

        // Pop value and object reference
        X64Emitter.Pop(ref _code, VReg.R2);  // value
        X64Emitter.Pop(ref _code, VReg.R0);  // obj
        PopStackType();
        PopStackType();

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
    /// Compile ldsfld - Load static field.
    /// For testing: token is direct address of static.
    /// Stack: ... -> ..., value
    /// </summary>
    private bool CompileLdsfld(uint token)
    {
        ulong staticAddr;
        int size;
        bool signed;

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
            size = field.Size;
            signed = field.IsSigned;
        }
        else
        {
            // For testing, token is address of static field (assume 8-byte)
            staticAddr = token;
            size = 8;
            signed = false;
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
        PushStackType(StackType_Int);
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

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
        }
        else
        {
            // For testing, token is address of static
            staticAddr = token;
        }

        X64Emitter.MovRI64(ref _code, VReg.R0, staticAddr);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);
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

        // Try field resolver first, fall back to test token (direct address)
        if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid && field.IsStatic)
        {
            staticAddr = (ulong)field.StaticAddress;
            size = field.Size;
        }
        else
        {
            // For testing, token is address of static (assume 8-byte)
            staticAddr = token;
            size = 8;
        }

        // Pop value
        X64Emitter.Pop(ref _code, VReg.R0);
        PopStackType();

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
        PopStackType();

        // Load length from array+8
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, ArrayLengthOffset);

        // Push length
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);

        return true;
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
        PopStackType();
        PopStackType();

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
        PushStackType(StackType_Int);

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
        PopStackType();
        PopStackType();
        PopStackType();

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
        PopStackType();
        PopStackType();

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
        PushStackType(elemSize == 4 ? StackType_Float32 : StackType_Float64);

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
        PopStackType();
        PopStackType();
        PopStackType();

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
    /// For testing: token encodes element size.
    /// </summary>
    private bool CompileLdelema(uint token)
    {
        int elemSize = (int)(token & 0xFF);
        if (elemSize == 0) elemSize = 8; // Default to pointer size

        // Pop index and array
        X64Emitter.Pop(ref _code, VReg.R1);  // index
        X64Emitter.Pop(ref _code, VReg.R0);  // array
        PopStackType();
        PopStackType();

        // Compute element address: array + 16 + index * elemSize
        switch (elemSize)
        {
            case 1:
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                break;
            case 2:
                X64Emitter.ShlImm(ref _code, VReg.R1, 1);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                break;
            case 4:
                X64Emitter.ShlImm(ref _code, VReg.R1, 2);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                break;
            case 8:
            default:
                X64Emitter.ShlImm(ref _code, VReg.R1, 3);
                X64Emitter.AddRR(ref _code, VReg.R0, VReg.R1);
                break;
        }
        X64Emitter.AddRI(ref _code, VReg.R0, ArrayDataOffset);

        // Push address
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);

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
        if (ResolveMethod(token, out ResolvedMethod ctor) && ctor.IsValid && ctor.MethodTable != null)
        {
            // We have a constructor with MethodTable - full newobj implementation
            // Stack before: ..., arg1, ..., argN (constructor args, not including 'this')
            // Stack after: ..., obj

            int ctorArgs = ctor.ArgCount;  // Not including 'this'

            // Step 1: Pop constructor arguments from eval stack into temporary storage
            // We need to save them because we'll call RhpNewFast first
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
                PopStackType();
                _evalStackDepth--;
            }

            // Step 2: Allocate the object via RhpNewFast
            X64Emitter.MovRI64(ref _code, VReg.R1, (ulong)ctor.MethodTable);
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
            X64Emitter.CallR(ref _code, VReg.R0);
            RecordSafePoint();

            // RAX now contains the new object pointer
            // Save it to R15 (callee-saved) so constructor call doesn't clobber it
            X64Emitter.MovRR(ref _code, VReg.R11, VReg.R0);

            // Step 3: Call the constructor with 'this' = new object
            // x64 calling convention: RCX=this, RDX=arg0, R8=arg1, R9=arg2, stack=arg3+
            // Move 'this' (R15) to RCX
            X64Emitter.MovRR(ref _code, VReg.R1, VReg.R11);

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

            // Call the constructor
            X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)ctor.NativeCode);
            X64Emitter.CallR(ref _code, VReg.R0);
            RecordSafePoint();

            // Push the object reference (from R15) onto eval stack
            X64Emitter.Push(ref _code, VReg.R11);
            PushStackType(StackType_Int);
            _evalStackDepth++;

            return true;
        }

        // Fallback: token is directly the MethodTable* address (simplified testing mode)
        ulong mtAddress = token;

        // Load MethodTable* into RCX (first arg for RhpNewFast)
        X64Emitter.MovRI64(ref _code, VReg.R1, mtAddress);

        // Call RhpNewFast(MethodTable* pMT) -> returns object pointer in RAX
        // RhpNewFast allocates the object and sets the MT pointer
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during allocation)
        RecordSafePoint();

        // Push the allocated object reference onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Objects are 64-bit pointers
        _evalStackDepth++;

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
        // Resolve token to MethodTable* address
        ulong mtAddress = token;  // Fallback: use token directly (for testing)

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                mtAddress = (ulong)resolved;
            }
        }

        // Pop numElements from stack into RDX (second arg)
        X64Emitter.Pop(ref _code, VReg.R2);
        PopStackType();
        _evalStackDepth--;

        // Load MethodTable* into RCX (first arg for RhpNewArray)
        X64Emitter.MovRI64(ref _code, VReg.R1, mtAddress);

        // Call RhpNewArray(MethodTable* pMT, int numElements) -> returns array pointer in RAX
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewArray);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during allocation)
        RecordSafePoint();

        // Push the allocated array reference onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Arrays are 64-bit pointers
        _evalStackDepth++;

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
    /// </summary>
    private bool CompileBox(uint token)
    {
        // Resolve token to MethodTable* address
        // The value type size is derived from BaseSize - 8 (minus MT pointer)
        ulong mtAddress = token;  // Fallback: use token directly (for testing)

        // If we have a type resolver, use it to get the real MethodTable
        if (_typeResolver != null)
        {
            void* resolved;
            if (_typeResolver(token, out resolved) && resolved != null)
            {
                mtAddress = (ulong)resolved;
            }
        }

        // Pop value from stack into R8 (save it temporarily)
        // For simplicity, we assume 64-bit value on stack
        X64Emitter.Pop(ref _code, VReg.R3);
        PopStackType();
        _evalStackDepth--;

        // Load MethodTable* into RCX (first arg for RhpNewFast)
        X64Emitter.MovRI64(ref _code, VReg.R1, mtAddress);

        // Save RCX (MT address) in R9 for later use
        X64Emitter.MovRR(ref _code, VReg.R4, VReg.R1);

        // Call RhpNewFast(MethodTable* pMT) -> returns object pointer in RAX
        X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)_rhpNewFast);
        X64Emitter.CallR(ref _code, VReg.R0);

        // Record safe point after call (GC can happen during allocation)
        RecordSafePoint();

        // Now RAX = pointer to boxed object
        // Copy the value (in R8) to offset 8 in the object (after MT pointer)
        // mov [RAX + 8], R8
        X64Emitter.MovMR(ref _code, VReg.R0, 8, VReg.R3);

        // Push the boxed object reference onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Objects are 64-bit pointers
        _evalStackDepth++;

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
        PopStackType();
        _evalStackDepth--;

        // Calculate pointer to value: obj + 8 (skip MT pointer)
        // lea RAX, [RAX + 8]
        X64Emitter.Lea(ref _code, VReg.R0, VReg.R0, 8);

        // Push the value pointer onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Managed pointers are 64-bit
        _evalStackDepth++;

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
    /// </summary>
    private bool CompileUnboxAny(uint token)
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
        PopStackType();
        _evalStackDepth--;

        // Load value from offset 8: mov RAX, [RAX + 8]
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.R0, 8);

        // Push the value onto eval stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Assuming 64-bit value
        _evalStackDepth++;

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
        PopStackType();
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
        PushStackType(StackType_Int);  // Objects are 64-bit pointers

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
        // For testing: token is the function pointer address
        // In production: would use method resolver to get address
        ulong fnPtr = token;

        // If we have a method resolver, try to resolve the token
        if (_resolver != null)
        {
            ResolvedMethod resolved;
            if (_resolver(token, out resolved) && resolved.IsValid && resolved.NativeCode != null)
            {
                fnPtr = (ulong)resolved.NativeCode;
            }
            // Otherwise fall back to treating token as direct address
        }

        // Push the function pointer onto the stack
        X64Emitter.MovRI64(ref _code, VReg.R0, fnPtr);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Function pointers are native int
        _evalStackDepth++;

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
        // Verify we have an object on the stack
        if (_evalStackDepth < 1)
        {
            DebugConsole.WriteLine("[JIT] ldvirtftn: no object on stack");
            return false;
        }

        // Pop the object reference into RAX
        X64Emitter.Pop(ref _code, VReg.R0);
        _evalStackDepth--;
        PopStackType();

        // For testing: token is the function pointer address
        // In production: would use method resolver to get address, then vtable lookup
        ulong fnPtr = token;
        bool useVtable = false;
        int vtableSlot = -1;

        // Use ResolveMethod to get method info (supports registry fallback when _resolver is null)
        ResolvedMethod resolved;
        if (ResolveMethod(token, out resolved) && resolved.IsValid && resolved.NativeCode != null)
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

        if (useVtable)
        {
            // Virtual dispatch: load function pointer from vtable
            // RAX already contains the object reference from pop above
            DebugConsole.Write("[JIT] ldvirtftn: vtable dispatch slot=");
            DebugConsole.WriteDecimal((uint)vtableSlot);

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
            X64Emitter.MovRI64(ref _code, VReg.R0, fnPtr);
        }

        // Push the function pointer onto the stack
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(StackType_Int);  // Function pointers are native int
        _evalStackDepth++;

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
        PopStackType();
        _evalStackDepth--;

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
        PushStackType(StackType_Int);  // Pointer type
        _evalStackDepth++;

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
        _evalStackDepth++;

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
        // For testing purposes, treat the token as a direct pointer value
        // In production, this would resolve to:
        // - For types: the MethodTable pointer (RuntimeTypeHandle)
        // - For methods: the method descriptor (RuntimeMethodHandle)
        // - For fields: the field descriptor (RuntimeFieldHandle)
        //
        // The loaded value can then be used with:
        // - Type.GetTypeFromHandle() for types
        // - MethodBase.GetMethodFromHandle() for methods
        // - FieldInfo.GetFieldFromHandle() for fields

        ulong handleValue = token;

        // If we have a type resolver, try to resolve the token
        if (_typeResolver != null)
        {
            void* mtPtr;
            if (_typeResolver(token, out mtPtr) && mtPtr != null)
            {
                handleValue = (ulong)mtPtr;
            }
        }

        // Push the handle onto the stack
        X64Emitter.MovRI64(ref _code, VReg.R0, handleValue);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;

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
        // Decode element size from token (for testing, use low byte)
        int elemSize = (int)(token & 0xFF);
        if (elemSize == 0) elemSize = 8;  // Default to pointer size

        // Delegate to the typed version (signed for value types)
        return CompileLdelem(elemSize, signed: true);
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
        // Decode element size from token (for testing, use low byte)
        int elemSize = (int)(token & 0xFF);
        if (elemSize == 0) elemSize = 8;  // Default to pointer size

        // Delegate to the typed version
        return CompileStelem(elemSize);
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
        _evalStackDepth--;
        PopStackType();

        // Return from the filter funclet
        X64Emitter.EmitEpilogue(ref _code, _stackAdjust);

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
        _evalStackDepth--;
        PopStackType();

        // TypedReference is 16 bytes - we push both parts onto the stack
        // The layout is: [Value pointer][Type pointer]
        // We push Type first (higher address), then Value (lower address)

        // Push type pointer
        X64Emitter.MovRI64(ref _code, VReg.R1, typePtr);
        X64Emitter.Push(ref _code, VReg.R1);
        // Push value pointer (already in RAX)
        X64Emitter.Push(ref _code, VReg.R0);

        // TypedReference takes 2 stack slots
        _evalStackDepth += 2;
        PushStackType(StackType_Int);  // Value part
        PushStackType(StackType_Int);  // Type part

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
        if (_evalStackDepth < 2)
        {
            DebugConsole.WriteLine("[JIT] refanyval: stack underflow");
            return false;
        }

        // TypedReference is 16 bytes (2 stack slots)
        // mkrefany pushed: Type first, Value second (Value is TOS)
        // So we pop: Value first, then Type
        X64Emitter.Pop(ref _code, VReg.R0);  // RAX = value pointer (TOS)
        _evalStackDepth--;
        PopStackType();
        X64Emitter.Pop(ref _code, VReg.R1);  // RCX = type pointer (currently unused for validation)
        _evalStackDepth--;
        PopStackType();

        // Push just the value pointer
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        PushStackType(StackType_Int);

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
        if (_evalStackDepth < 2)
        {
            DebugConsole.WriteLine("[JIT] refanytype: stack underflow");
            return false;
        }

        // TypedReference is 16 bytes (2 stack slots)
        // mkrefany pushed: Type first, Value second (Value is TOS)
        // So we pop: Value first (discard), then Type
        X64Emitter.Pop(ref _code, VReg.R1);  // RCX = value pointer (discarded, TOS)
        _evalStackDepth--;
        PopStackType();
        X64Emitter.Pop(ref _code, VReg.R0);  // RAX = type pointer
        _evalStackDepth--;
        PopStackType();

        // Push the type pointer as RuntimeTypeHandle
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        PushStackType(StackType_Int);

        return true;
    }

    /// <summary>
    /// Compile arglist - Get handle to argument list (for varargs methods).
    /// Stack: ... -> ..., argHandle
    ///
    /// Returns a RuntimeArgumentHandle pointing to the varargs portion of the arguments.
    /// </summary>
    private bool CompileArglist()
    {
        // In a varargs method, after the declared parameters, there may be additional
        // arguments on the stack. The arglist instruction returns a handle that allows
        // iterating over those extra arguments using ArgIterator.
        //
        // For our implementation:
        // - The handle is a pointer to where the varargs begin on the stack
        // - In x64 calling convention, args after the first 4 are on the stack
        // - The varargs start after the declared parameters

        // Calculate the address where varargs would begin
        // This is: RBP + 16 + (argCount * 8)
        // (RBP points to saved RBP, +8 is return address, +16 is start of shadow space/args)
        int varargOffset = 16 + (_argCount * 8);

        X64Emitter.MovRR(ref _code, VReg.R0, VReg.FP);
        X64Emitter.AddRI(ref _code, VReg.R0, varargOffset);
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        PushStackType(StackType_Int);

        return true;
    }

    // Function pointers for runtime helpers (set by caller)
    private void* _rhpNewFast;
    private void* _rhpNewArray;
    private void* _isAssignableTo;
    private void* _getInterfaceMethod;

    // ==================== Funclet Compilation Support ====================
    // EH clause storage for funclet-based exception handling
    private const int MaxEHClauses = 16;
    private int _ehClauseCount;

    // Each clause uses 5 ints: flags, tryStart, tryEnd, handlerStart, handlerEnd (IL offsets)
    private fixed int _ehClauseData[MaxEHClauses * 5];

    // Funclet compilation results (filled by CompileWithFunclets)
    // Each funclet has: nativeStart (code offset), nativeSize, clauseIndex
    private fixed int _funcletInfo[MaxEHClauses * 3];
    private int _funcletCount;

    // Flag to indicate we're compiling a funclet (not main method)
    private bool _compilingFunclet;

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

        fixed (int* data = _ehClauseData)
        {
            for (int i = 0; i < count; i++)
            {
                var clause = clauses.GetClause(i);
                int idx = i * 5;
                data[idx + 0] = (int)clause.Flags;
                data[idx + 1] = (int)clause.TryOffset;
                data[idx + 2] = (int)(clause.TryOffset + clause.TryLength);
                data[idx + 3] = (int)clause.HandlerOffset;
                data[idx + 4] = (int)(clause.HandlerOffset + clause.HandlerLength);
                _ehClauseCount++;
            }
        }
    }

    /// <summary>
    /// Check if an IL offset is inside a handler region (for skipping during main method compilation).
    /// </summary>
    private bool IsInsideHandler(int ilOffset)
    {
        fixed (int* data = _ehClauseData)
        {
            for (int i = 0; i < _ehClauseCount; i++)
            {
                int idx = i * 5;
                int handlerStart = data[idx + 3];
                int handlerEnd = data[idx + 4];
                if (ilOffset >= handlerStart && ilOffset < handlerEnd)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Get the next IL offset after skipping all handler regions starting at current offset.
    /// Returns the IL offset just past the handler(s), or -1 if not in a handler.
    /// </summary>
    private int SkipHandler(int ilOffset)
    {
        fixed (int* data = _ehClauseData)
        {
            for (int i = 0; i < _ehClauseCount; i++)
            {
                int idx = i * 5;
                int handlerStart = data[idx + 3];
                int handlerEnd = data[idx + 4];
                if (ilOffset == handlerStart)
                    return handlerEnd;
            }
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
        int localBytes = _localCount * 8 + 64;
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
                // Record a label for the handler start (needed for leave instructions)
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

        fixed (int* ehData = _ehClauseData)
        fixed (int* funcletData = _funcletInfo)
        {
            for (int i = 0; i < _ehClauseCount; i++)
            {
                int idx = i * 5;
                int handlerStart = ehData[idx + 3];
                int handlerEnd = ehData[idx + 4];
                int flags = ehData[idx + 0];

                // Record funclet start
                int funcletCodeStart = _code.Position;

                // Emit funclet prolog (4 bytes):
                // push rbp       (0x55)
                // mov rbp, rdx   (0x48 0x89 0xD5) - RDX contains parent frame pointer
                _code.EmitByte(0x55);       // push rbp
                _code.EmitByte(0x48);       // REX.W
                _code.EmitByte(0x89);       // mov r/m64, r64
                _code.EmitByte(0xD5);       // rbp, rdx

                // Reset label tracking for funclet
                // (branches within funclet are relative to funclet)
                int saveLabelCount = _labelCount;
                _labelCount = 0;
                int saveBranchCount = _branchCount;
                _branchCount = 0;

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

                // Emit funclet epilog:
                // pop rbp  (0x5D)
                // ret      (0xC3)
                // Note: endfinally/ret in handler should have already emitted appropriate code
                // We add a safety epilog in case control falls through
                _code.EmitByte(0x5D);  // pop rbp
                _code.EmitByte(0xC3);  // ret

                int funcletCodeEnd = _code.Position;
                int funcletCodeSize = funcletCodeEnd - funcletCodeStart;

                // Store funclet info
                int funcIdx = _funcletCount * 3;
                funcletData[funcIdx + 0] = funcletCodeStart;
                funcletData[funcIdx + 1] = funcletCodeSize;
                funcletData[funcIdx + 2] = i;  // clause index
                _funcletCount++;

                // Restore label/branch tracking
                _labelCount = saveLabelCount;
                _branchCount = saveBranchCount;
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

        // Allocate funclets in method info
        if (_funcletCount > 0)
        {
            methodInfo.AllocateFunclets(_funcletCount);

            fixed (int* funcletData = _funcletInfo)
            fixed (int* ehData = _ehClauseData)
            {
                for (int i = 0; i < _funcletCount; i++)
                {
                    int funcIdx = i * 3;
                    int funcletCodeStart = funcletData[funcIdx + 0];
                    int funcletCodeSize = funcletData[funcIdx + 1];
                    int clauseIdx = funcletData[funcIdx + 2];

                    int ehIdx = clauseIdx * 5;
                    int flags = ehData[ehIdx + 0];
                    bool isFilter = (flags == (int)ILExceptionClauseFlags.Filter);

                    ulong funcletAddr = codeBase + (ulong)funcletCodeStart;
                    methodInfo.AddFunclet(funcletAddr, (uint)funcletCodeSize, isFilter, clauseIdx);
                }
            }
        }

        // Build native EH clauses
        fixed (int* ehData = _ehClauseData)
        fixed (int* funcletData = _funcletInfo)
        {
            for (int i = 0; i < _ehClauseCount; i++)
            {
                int ehIdx = i * 5;
                int flags = ehData[ehIdx + 0];
                int tryStartIL = ehData[ehIdx + 1];
                int tryEndIL = ehData[ehIdx + 2];

                // Get native try offsets
                uint nativeTryStart = (uint)GetNativeOffset(tryStartIL);
                uint nativeTryEnd = (uint)GetNativeOffset(tryEndIL);

                // Handler offset is the funclet's code offset
                uint nativeHandlerStart = 0;
                uint nativeHandlerEnd = 0;

                // Find the funclet for this clause
                for (int f = 0; f < _funcletCount; f++)
                {
                    int funcIdx = f * 3;
                    if (funcletData[funcIdx + 2] == i)
                    {
                        nativeHandlerStart = (uint)funcletData[funcIdx + 0];
                        nativeHandlerEnd = nativeHandlerStart + (uint)funcletData[funcIdx + 1];
                        break;
                    }
                }

                JITExceptionClause clause;
                clause.Flags = (ILExceptionClauseFlags)flags;
                clause.TryStartOffset = nativeTryStart;
                clause.TryEndOffset = nativeTryEnd;
                clause.HandlerStartOffset = nativeHandlerStart;
                clause.HandlerEndOffset = nativeHandlerEnd;
                clause.ClassTokenOrFilterOffset = 0;
                clause.IsValid = true;

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
}
