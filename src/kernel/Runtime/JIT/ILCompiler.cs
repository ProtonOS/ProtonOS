// ProtonOS JIT - IL Compiler
// Compiles IL bytecode to x64 machine code using naive stack-based approach.

using ProtonOS.Platform;
using ProtonOS.X64;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// IL opcode values (ECMA-335 Partition III)
/// </summary>
public static class ILOpcode
{
    // Constants
    public const byte Nop = 0x00;
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

    // Value type operations
    public const byte Cpobj = 0x70;
    public const byte Ldobj = 0x71;
    public const byte Stobj = 0x81;

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
    public const byte Ldarg_2byte = 0x09;
    public const byte Starg_2byte = 0x0A;
    public const byte Ldloca_2byte = 0x0D;
    public const byte Ldloc_2byte = 0x0C;
    public const byte Stloc_2byte = 0x0E;
    public const byte Ldarga_2byte = 0x0A;  // Same as Starg_2byte - context matters
    public const byte Cpblk_2 = 0x17;
    public const byte Initblk_2 = 0x18;
    public const byte Initobj_2 = 0x15;
    public const byte Sizeof_2 = 0x1C;

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
}

/// <summary>
/// Delegate type for resolving method tokens to native addresses.
/// </summary>
/// <param name="token">Method token from IL</param>
/// <param name="result">Output: resolved method information</param>
/// <returns>True if resolution successful</returns>
public unsafe delegate bool MethodResolver(uint token, out ResolvedMethod result);

/// <summary>
/// Naive IL to x64 compiler.
/// Uses a stack-based approach where the IL evaluation stack is
/// simulated using x64 registers and memory.
/// </summary>
public unsafe struct ILCompiler
{
    private X64Emitter _emit;
    private byte* _il;
    private int _ilLength;
    private int _ilOffset;

    // Method info
    private int _argCount;
    private int _localCount;
    private int _stackAdjust;

    // Evaluation stack tracking (naive approach: just track depth)
    // For a more sophisticated approach, track register allocation
    private int _evalStackDepth;

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

    /// <summary>
    /// Create an IL compiler for a method.
    /// </summary>
    public static ILCompiler Create(byte* il, int ilLength, int argCount, int localCount)
    {
        ILCompiler compiler;
        compiler._il = il;
        compiler._ilLength = ilLength;
        compiler._ilOffset = 0;
        compiler._argCount = argCount;
        compiler._localCount = localCount;
        compiler._stackAdjust = 0;
        compiler._evalStackDepth = 0;
        compiler._branchCount = 0;
        compiler._labelCount = 0;
        compiler._resolver = null;

        // Create code buffer (4KB should be plenty for simple methods)
        var code = CodeBuffer.Create(4096);
        compiler._emit = X64Emitter.Create(ref code);

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
    /// Get the emitter (for accessing the code buffer)
    /// </summary>
    public ref X64Emitter Emitter => ref _emit;

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
    /// Compile the IL method to native code.
    /// Returns function pointer or null on failure.
    /// </summary>
    public void* Compile()
    {
        // Emit prologue
        // Calculate local space: localCount * 8 + evalStack space
        int localBytes = _localCount * 8 + 64;  // 64 bytes for eval stack
        _stackAdjust = _emit.EmitPrologue(localBytes, false);

        // Home arguments to shadow space (so we can load them later)
        if (_argCount > 0)
            _emit.HomeArguments(_argCount);

        // Process IL
        while (_ilOffset < _ilLength)
        {
            // Record label for this IL offset
            RecordLabel(_ilOffset, _emit.Position);

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

        return _emit.Code.GetFunctionPointer();
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
                _emit.Code.PatchInt32(patchOffset, rel);
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
        // Push constant onto eval stack
        // For naive implementation: mov rax, imm; push rax
        _emit.MovRI32(Reg64.RAX, value);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdarg(int index)
    {
        // Load argument from shadow space
        // Use sign-extension for 32-bit int arguments to handle negative values correctly
        // (Microsoft x64 ABI: 32-bit args are zero-extended in 64-bit registers)
        _emit.LoadArgFromHomeI32(Reg64.RAX, index);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdloc(int index)
    {
        // Locals are at negative offsets from RBP
        // After prologue: [rbp-8] = local0, [rbp-16] = local1, etc.
        // But we need to account for shadow space + stack adjustment
        int offset = X64Emitter.GetLocalOffset(index);
        _emit.MovRM(Reg64.RAX, Reg64.RBP, offset);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileStloc(int index)
    {
        // Pop from eval stack, store to local
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;
        int offset = X64Emitter.GetLocalOffset(index);
        _emit.MovMR(Reg64.RBP, offset, Reg64.RAX);
        return true;
    }

    private bool CompileDup()
    {
        // Duplicate top of stack
        _emit.MovRM(Reg64.RAX, Reg64.RSP, 0);  // Read top without popping
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompilePop()
    {
        // Discard top of stack
        _emit.AddRI(Reg64.RSP, 8);
        _evalStackDepth--;
        return true;
    }

    private bool CompileAdd()
    {
        // Pop two values, add, push result
        _emit.Pop(Reg64.RDX);  // Second operand
        _emit.Pop(Reg64.RAX);  // First operand
        _evalStackDepth -= 2;
        _emit.AddRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileSub()
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.SubRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileMul()
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.ImulRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    // Overflow-checking arithmetic
    // For signed: check OF (overflow flag) with JO (0x70)
    // For unsigned: check CF (carry flag) with JC (0x72)

    private bool CompileAddOvf(bool unsigned)
    {
        _emit.Pop(Reg64.RDX);  // Second operand
        _emit.Pop(Reg64.RAX);  // First operand
        _evalStackDepth -= 2;
        _emit.AddRR(Reg64.RAX, Reg64.RDX);
        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileSubOvf(bool unsigned)
    {
        _emit.Pop(Reg64.RDX);  // Second operand (subtrahend)
        _emit.Pop(Reg64.RAX);  // First operand (minuend)
        _evalStackDepth -= 2;
        _emit.SubRR(Reg64.RAX, Reg64.RDX);
        // Check for overflow: JO for signed (OF=1), JC for unsigned (CF=1 = borrow)
        EmitJccToInt3(unsigned ? (byte)0x72 : (byte)0x70);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileMulOvf(bool unsigned)
    {
        _emit.Pop(Reg64.RDX);  // Second operand
        _emit.Pop(Reg64.RAX);  // First operand
        _evalStackDepth -= 2;

        if (unsigned)
        {
            // mul rdx: unsigned RAX * RDX -> RDX:RAX
            // If RDX != 0 after, overflow occurred
            // Encoding: REX.W + F7 /4 (mul r/m64)
            _emit.Code.EmitByte(0x48);  // REX.W
            _emit.Code.EmitByte(0xF7);  // MUL
            _emit.Code.EmitByte(0xE2);  // ModRM: /4 rdx
            // mul sets CF=OF=1 if high half is non-zero
            EmitJccToInt3(0x72);  // JC overflow
        }
        else
        {
            // imul rax, rdx: signed, result in RAX, sets OF if overflow
            _emit.ImulRR(Reg64.RAX, Reg64.RDX);
            EmitJccToInt3(0x70);  // JO overflow
        }

        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileNeg()
    {
        _emit.Pop(Reg64.RAX);
        _emit.Neg(Reg64.RAX);
        _emit.Push(Reg64.RAX);
        return true;
    }

    private bool CompileAnd()
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.AndRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileOr()
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.OrRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileXor()
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.XorRR(Reg64.RAX, Reg64.RDX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileNot()
    {
        _emit.Pop(Reg64.RAX);
        _emit.Not(Reg64.RAX);
        _emit.Push(Reg64.RAX);
        return true;
    }

    private bool CompileDiv(bool signed)
    {
        // Division: dividend / divisor
        // IL stack: [..., dividend, divisor] -> [..., quotient]
        _emit.Pop(Reg64.RCX);  // Divisor to RCX (preserving RDX)
        _emit.Pop(Reg64.RAX);  // Dividend to RAX
        _evalStackDepth -= 2;

        if (signed)
        {
            // Sign-extend RAX into RDX:RAX
            _emit.Cqo();
            // Signed divide
            _emit.Idiv(Reg64.RCX);
        }
        else
        {
            // For unsigned 32-bit division, we need to zero-extend the operands
            // to ensure they're treated as unsigned 32-bit values, not sign-extended 64-bit
            _emit.ZeroExtend32(Reg64.RAX);
            _emit.ZeroExtend32(Reg64.RCX);
            // Zero RDX for unsigned division
            _emit.XorRR(Reg64.RDX, Reg64.RDX);
            // Unsigned divide
            _emit.Div(Reg64.RCX);
        }

        // Quotient is in RAX
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileRem(bool signed)
    {
        // Remainder: dividend % divisor
        // IL stack: [..., dividend, divisor] -> [..., remainder]
        _emit.Pop(Reg64.RCX);  // Divisor to RCX
        _emit.Pop(Reg64.RAX);  // Dividend to RAX
        _evalStackDepth -= 2;

        if (signed)
        {
            _emit.Cqo();
            _emit.Idiv(Reg64.RCX);
        }
        else
        {
            // For unsigned 32-bit remainder, zero-extend operands
            _emit.ZeroExtend32(Reg64.RAX);
            _emit.ZeroExtend32(Reg64.RCX);
            _emit.XorRR(Reg64.RDX, Reg64.RDX);
            _emit.Div(Reg64.RCX);
        }

        // Remainder is in RDX
        _emit.Push(Reg64.RDX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileShl()
    {
        // Shift left: value << shiftAmount
        // IL stack: [..., value, shiftAmount] -> [..., result]
        _emit.Pop(Reg64.RCX);  // Shift amount to CL
        _emit.Pop(Reg64.RAX);  // Value to shift
        _evalStackDepth -= 2;
        _emit.ShlCL(Reg64.RAX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileShr(bool signed)
    {
        // Shift right: value >> shiftAmount
        _emit.Pop(Reg64.RCX);  // Shift amount to CL
        _emit.Pop(Reg64.RAX);  // Value to shift
        _evalStackDepth -= 2;

        if (signed)
        {
            _emit.SarCL(Reg64.RAX);  // Arithmetic shift (preserves sign)
        }
        else
        {
            // For unsigned 32-bit shift, zero-extend the value first
            // so logical shift fills with zeros from the correct upper bit
            _emit.ZeroExtend32(Reg64.RAX);
            _emit.ShrCL(Reg64.RAX);  // Logical shift (zero-fill)
        }

        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileConv(int targetBytes, bool signed)
    {
        // Convert top of stack to different size
        // For naive JIT, we just mask/sign-extend as needed
        _emit.Pop(Reg64.RAX);

        switch (targetBytes)
        {
            case 1:
                if (signed)
                {
                    // MOVSX RAX, AL - sign-extend byte to qword
                    _emit.Code.EmitByte(0x48);  // REX.W
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xBE);
                    _emit.Code.EmitByte(0xC0);  // ModRM: RAX, AL
                }
                else
                {
                    // MOVZX EAX, AL - zero-extend byte (clears upper bits)
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xB6);
                    _emit.Code.EmitByte(0xC0);  // ModRM: EAX, AL
                }
                break;

            case 2:
                if (signed)
                {
                    // MOVSX RAX, AX - sign-extend word to qword
                    _emit.Code.EmitByte(0x48);  // REX.W
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xBF);
                    _emit.Code.EmitByte(0xC0);  // ModRM: RAX, AX
                }
                else
                {
                    // MOVZX EAX, AX - zero-extend word
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xB7);
                    _emit.Code.EmitByte(0xC0);  // ModRM: EAX, AX
                }
                break;

            case 4:
                if (signed)
                {
                    // MOVSXD RAX, EAX - sign-extend dword to qword
                    _emit.Code.EmitByte(0x48);  // REX.W
                    _emit.Code.EmitByte(0x63);
                    _emit.Code.EmitByte(0xC0);  // ModRM: RAX, EAX
                }
                else
                {
                    // MOV EAX, EAX - writing to 32-bit reg zeros upper 32 bits
                    _emit.Code.EmitByte(0x89);
                    _emit.Code.EmitByte(0xC0);  // ModRM: EAX, EAX
                }
                break;

            case 8:
                // For conv.i (signed), nothing to do - values are already sign-extended
                // For conv.u (unsigned), need to zero-extend the low 32 bits
                // because ldc.i4 sign-extends to 64-bit, so we need to mask
                if (!signed)
                {
                    // MOV EAX, EAX - writing to 32-bit reg zeros upper 32 bits
                    _emit.Code.EmitByte(0x89);
                    _emit.Code.EmitByte(0xC0);  // ModRM: EAX, EAX
                }
                break;
        }

        _emit.Push(Reg64.RAX);
        return true;
    }

    /// <summary>
    /// Compile overflow-checking conversions (conv.ovf.* opcodes).
    /// If the value doesn't fit in the target type, triggers INT3 (debug break).
    /// </summary>
    private bool CompileConvOvf(int targetBytes, bool targetSigned, bool sourceUnsigned)
    {
        // Pop value from stack
        _emit.Pop(Reg64.RAX);

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
                    _emit.CmpRI(Reg64.RAX, -128);
                    EmitJccToInt3(0x7C);  // JL overflow
                    _emit.CmpRI(Reg64.RAX, 127);
                    EmitJccToInt3(0x7F);  // JG overflow

                    // Sign-extend AL to RAX
                    _emit.Code.EmitByte(0x48);
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xBE);
                    _emit.Code.EmitByte(0xC0);
                }
                else
                {
                    // conv.ovf.u1: range 0..255
                    if (!sourceUnsigned)
                    {
                        // Check for negative: test rax, rax; js overflow
                        _emit.Code.EmitByte(0x48);
                        _emit.Code.EmitByte(0x85);
                        _emit.Code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Check: cmp rax, 255; ja overflow
                    _emit.CmpRI(Reg64.RAX, 255);
                    EmitJccToInt3(0x77);  // JA overflow

                    // Zero-extend AL to RAX
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xB6);
                    _emit.Code.EmitByte(0xC0);
                }
                break;

            case 2:
                if (targetSigned)
                {
                    // conv.ovf.i2: range -32768..32767
                    _emit.CmpRI(Reg64.RAX, -32768);
                    EmitJccToInt3(0x7C);  // JL overflow
                    _emit.CmpRI(Reg64.RAX, 32767);
                    EmitJccToInt3(0x7F);  // JG overflow

                    // Sign-extend AX to RAX
                    _emit.Code.EmitByte(0x48);
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xBF);
                    _emit.Code.EmitByte(0xC0);
                }
                else
                {
                    // conv.ovf.u2: range 0..65535
                    if (!sourceUnsigned)
                    {
                        // Check for negative
                        _emit.Code.EmitByte(0x48);
                        _emit.Code.EmitByte(0x85);
                        _emit.Code.EmitByte(0xC0);
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    _emit.CmpRI(Reg64.RAX, 65535);
                    EmitJccToInt3(0x77);  // JA overflow

                    // Zero-extend AX to RAX
                    _emit.Code.EmitByte(0x0F);
                    _emit.Code.EmitByte(0xB7);
                    _emit.Code.EmitByte(0xC0);
                }
                break;

            case 4:
                if (targetSigned)
                {
                    // conv.ovf.i4: value must fit in int32
                    // Use MOVSXD to sign-extend, then compare back
                    // If original != sign-extended, overflow
                    _emit.MovRR(Reg64.RDX, Reg64.RAX);  // Save original
                    _emit.Code.EmitByte(0x48);
                    _emit.Code.EmitByte(0x63);
                    _emit.Code.EmitByte(0xC0);  // MOVSXD RAX, EAX
                    _emit.CmpRR(Reg64.RAX, Reg64.RDX);
                    EmitJccToInt3(0x75);  // JNE overflow
                }
                else
                {
                    // conv.ovf.u4: range 0..0xFFFFFFFF
                    if (!sourceUnsigned)
                    {
                        // Check for negative
                        _emit.Code.EmitByte(0x48);
                        _emit.Code.EmitByte(0x85);
                        _emit.Code.EmitByte(0xC0);
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Check upper 32 bits are zero
                    _emit.MovRI64(Reg64.RDX, 0xFFFFFFFF00000000);
                    _emit.Code.EmitByte(0x48);
                    _emit.Code.EmitByte(0x85);
                    _emit.Code.EmitByte(0xD0);  // TEST RAX, RDX
                    EmitJccToInt3(0x75);  // JNE overflow

                    // Zero-extend EAX
                    _emit.Code.EmitByte(0x89);
                    _emit.Code.EmitByte(0xC0);
                }
                break;

            case 8:
                if (targetSigned)
                {
                    // conv.ovf.i8: if source is unsigned, it must be < 2^63
                    if (sourceUnsigned)
                    {
                        _emit.Code.EmitByte(0x48);
                        _emit.Code.EmitByte(0x85);
                        _emit.Code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow (sign bit set means >= 2^63)
                    }
                    // Otherwise no overflow possible
                }
                else
                {
                    // conv.ovf.u8: value must be >= 0
                    if (!sourceUnsigned)
                    {
                        _emit.Code.EmitByte(0x48);
                        _emit.Code.EmitByte(0x85);
                        _emit.Code.EmitByte(0xC0);  // TEST RAX, RAX
                        EmitJccToInt3(0x78);  // JS overflow
                    }
                    // Otherwise no overflow possible from unsigned source
                }
                break;
        }

        _emit.Push(Reg64.RAX);
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
        _emit.Code.EmitByte(invertedJcc);  // Jcc_not (skip INT3)
        _emit.Code.EmitByte(1);            // rel8 = +1 (skip 1 byte)
        _emit.Code.EmitByte(0xCC);         // INT3
    }

    private bool CompileLdcI8(long value)
    {
        // Load 64-bit constant
        _emit.MovRI64(Reg64.RAX, (ulong)value);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdcR4(uint bits)
    {
        // Load float constant - store bit pattern and push to stack
        // Float is 4 bytes, but we push 8 bytes (zero-extended)
        _emit.MovRI32(Reg64.RAX, (int)bits);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdcR8(ulong bits)
    {
        // Load double constant - push 64-bit pattern to stack
        _emit.MovRI64(Reg64.RAX, bits);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileConvR4()
    {
        // Convert integer to float (single precision)
        // Pop 64-bit integer, convert to float, push back as 32-bit pattern (zero-extended)
        _emit.Pop(Reg64.RAX);
        // CVTSI2SS xmm0, rax - convert 64-bit signed int to single float
        _emit.Cvtsi2ssXmmR64(RegXMM.XMM0, Reg64.RAX);
        // MOVD eax, xmm0 - move float bits to integer reg
        _emit.MovdR32Xmm(Reg64.RAX, RegXMM.XMM0);
        _emit.Push(Reg64.RAX);
        return true;
    }

    private bool CompileConvR8()
    {
        // Convert integer to double precision
        // Pop 64-bit integer, convert to double, push back as 64-bit pattern
        _emit.Pop(Reg64.RAX);
        // CVTSI2SD xmm0, rax - convert 64-bit signed int to double
        _emit.Cvtsi2sdXmmR64(RegXMM.XMM0, Reg64.RAX);
        // MOVQ rax, xmm0 - move double bits to integer reg
        _emit.MovqR64Xmm(Reg64.RAX, RegXMM.XMM0);
        _emit.Push(Reg64.RAX);
        return true;
    }

    private bool CompileLdnull()
    {
        // Load null reference (0)
        _emit.XorRR(Reg64.RAX, Reg64.RAX);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileStarg(int index)
    {
        // Pop from eval stack, store to argument home location
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;
        int offset = 16 + index * 8;  // Shadow space offset
        _emit.MovMR(Reg64.RBP, offset, Reg64.RAX);
        return true;
    }

    private bool CompileLdarga(int index)
    {
        // Load address of argument
        int offset = 16 + index * 8;
        _emit.Lea(Reg64.RAX, Reg64.RBP, offset);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileLdloca(int index)
    {
        // Load address of local variable
        int offset = X64Emitter.GetLocalOffset(index);
        _emit.Lea(Reg64.RAX, Reg64.RBP, offset);
        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileRet()
    {
        // If there's a return value, it should be on eval stack
        if (_evalStackDepth > 0)
        {
            _emit.Pop(Reg64.RAX);  // Return value in RAX
            _evalStackDepth--;
        }

        // Emit epilogue
        _emit.EmitEpilogue(_stackAdjust, false);
        return true;
    }

    private bool CompileBr(int targetIL)
    {
        int patchOffset = _emit.JmpRel32();
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBrfalse(int targetIL)
    {
        // Pop value, branch if zero
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;
        _emit.TestRR(Reg64.RAX, Reg64.RAX);
        int patchOffset = _emit.Je();  // Jump if equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBrtrue(int targetIL)
    {
        // Pop value, branch if non-zero
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;
        _emit.TestRR(Reg64.RAX, Reg64.RAX);
        int patchOffset = _emit.Jne();  // Jump if not equal (to zero)
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileBranchCmp(byte cc, int targetIL)
    {
        // Pop two values, compare, branch based on condition
        _emit.Pop(Reg64.RDX);  // Second operand
        _emit.Pop(Reg64.RAX);  // First operand
        _evalStackDepth -= 2;
        _emit.CmpRR(Reg64.RAX, Reg64.RDX);
        int patchOffset = _emit.JccRel32(cc);
        RecordBranch(_ilOffset, targetIL, patchOffset);
        return true;
    }

    private bool CompileCeq()
    {
        // Compare equal: pop two values, push 1 if equal, 0 otherwise
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.CmpRR(Reg64.RAX, Reg64.RDX);

        // SETE sets AL to 1 if equal, 0 otherwise
        // setcc r/m8: 0F 94 /0 (mod=11, reg=0, r/m=AL)
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(0x94);
        _emit.Code.EmitByte(0xC0);  // ModRM: mod=11, reg=0, r/m=0 (AL)

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _emit.Code.EmitByte(0x48);  // REX.W
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(0xB6);
        _emit.Code.EmitByte(0xC0);  // ModRM: mod=11, reg=RAX, r/m=AL

        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileCgt(bool signed)
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.CmpRR(Reg64.RAX, Reg64.RDX);

        // SETG (signed) or SETA (unsigned)
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(signed ? (byte)0x9F : (byte)0x97);  // SETG / SETA
        _emit.Code.EmitByte(0xC0);  // AL

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _emit.Code.EmitByte(0x48);
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(0xB6);
        _emit.Code.EmitByte(0xC0);

        _emit.Push(Reg64.RAX);
        _evalStackDepth++;
        return true;
    }

    private bool CompileClt(bool signed)
    {
        _emit.Pop(Reg64.RDX);
        _emit.Pop(Reg64.RAX);
        _evalStackDepth -= 2;
        _emit.CmpRR(Reg64.RAX, Reg64.RDX);

        // SETL (signed) or SETB (unsigned)
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(signed ? (byte)0x9C : (byte)0x92);  // SETL / SETB
        _emit.Code.EmitByte(0xC0);  // AL

        // Zero-extend AL to RAX using MOVZX RAX, AL (REX.W + 0F B6 /r)
        _emit.Code.EmitByte(0x48);
        _emit.Code.EmitByte(0x0F);
        _emit.Code.EmitByte(0xB6);
        _emit.Code.EmitByte(0xC0);

        _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;

        // Compare value with n: if value >= n, fall through (jump past all cases)
        _emit.CmpRI(Reg64.RAX, (int)n);
        int fallThroughPatch = _emit.JccRel32(X64Emitter.CC_AE);  // JAE (unsigned >=)

        // For each target, emit: cmp rax, i; je target[i]
        // This is naive but correct; a jump table would be faster
        for (uint i = 0; i < n; i++)
        {
            int targetOffset = *(int*)(_il + _ilOffset);
            _ilOffset += 4;

            int targetIL = switchEndIL + targetOffset;

            // Compare RAX with case index i
            _emit.CmpRI(Reg64.RAX, (int)i);

            // Jump if equal to this case
            int patchOffset = _emit.JccRel32(X64Emitter.CC_E);
            RecordBranch(_ilOffset, targetIL, patchOffset);
        }

        // Fall-through case: patch the fall-through jump to here
        _emit.Code.PatchRelative32(fallThroughPatch);

        return true;
    }

    /// <summary>
    /// Compile ldind.* - Load value indirectly from address on stack
    /// Stack: ..., addr -> ..., value
    /// </summary>
    private bool CompileLdind(int size, bool signExtend)
    {
        // Pop address from stack into RAX
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;

        // Load value from [RAX] into RAX
        // mov rax, [rax] (with appropriate size)
        switch (size)
        {
            case 1:
                if (signExtend)
                {
                    // movsx rax, byte [rax] - sign extend byte to 64-bit
                    _emit.MovsxByte(Reg64.RAX, Reg64.RAX, 0);
                }
                else
                {
                    // movzx eax, byte [rax] - zero extend byte (clears upper 32 bits)
                    _emit.MovzxByte(Reg64.RAX, Reg64.RAX, 0);
                }
                break;

            case 2:
                if (signExtend)
                {
                    // movsx rax, word [rax] - sign extend word to 64-bit
                    _emit.MovsxWord(Reg64.RAX, Reg64.RAX, 0);
                }
                else
                {
                    // movzx eax, word [rax] - zero extend word (clears upper 32 bits)
                    _emit.MovzxWord(Reg64.RAX, Reg64.RAX, 0);
                }
                break;

            case 4:
                if (signExtend)
                {
                    // movsxd rax, dword [rax] - sign extend dword to 64-bit
                    _emit.MovsxdRM(Reg64.RAX, Reg64.RAX, 0);
                }
                else
                {
                    // mov eax, [rax] - zero extend dword (clears upper 32 bits)
                    _emit.MovRM32(Reg64.RAX, Reg64.RAX, 0);
                }
                break;

            case 8:
                // mov rax, [rax] - full 64-bit load
                _emit.MovRM(Reg64.RAX, Reg64.RAX, 0);
                break;

            default:
                return false;
        }

        // Push result back onto stack
        _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.RDX);
        _evalStackDepth--;

        // Pop address into RAX
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;

        // Store value to [RAX] with appropriate size
        switch (size)
        {
            case 1:
                // mov byte [rax], dl
                _emit.MovMR8(Reg64.RAX, 0, Reg64.RDX);
                break;

            case 2:
                // mov word [rax], dx
                _emit.MovMR16(Reg64.RAX, 0, Reg64.RDX);
                break;

            case 4:
                // mov dword [rax], edx
                _emit.MovMR32(Reg64.RAX, 0, Reg64.RDX);
                break;

            case 8:
                // mov qword [rax], rdx
                _emit.MovMR(Reg64.RAX, 0, Reg64.RDX);
                break;

            default:
                return false;
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
            _emit.Pop(Reg64.RCX);
            _evalStackDepth--;
        }
        else if (totalArgs == 2)
        {
            // Two args: pop to RDX (arg1), then RCX (arg0)
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
            _evalStackDepth -= 2;
        }
        else if (totalArgs == 3)
        {
            // Three args: pop to R8, RDX, RCX
            _emit.Pop(Reg64.R8);    // arg2
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
            _evalStackDepth -= 3;
        }
        else if (totalArgs == 4)
        {
            // Four args: pop to R9, R8, RDX, RCX
            _emit.Pop(Reg64.R9);    // arg3
            _emit.Pop(Reg64.R8);    // arg2
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
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
            _emit.MovRM(Reg64.RCX, Reg64.RSP, (totalArgs - 1) * 8);  // arg0
            _emit.MovRM(Reg64.RDX, Reg64.RSP, (totalArgs - 2) * 8);  // arg1
            _emit.MovRM(Reg64.R8, Reg64.RSP, (totalArgs - 3) * 8);   // arg2
            _emit.MovRM(Reg64.R9, Reg64.RSP, (totalArgs - 4) * 8);   // arg3

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
                _emit.MovRM(Reg64.R10, Reg64.RSP, srcOffset);
                _emit.MovMR(Reg64.RSP, dstOffset, Reg64.R10);
            }

            // Now adjust RSP to point to the call frame
            // Final RSP = current RSP + totalArgs*8 - extraStackSpace
            int rspAdjust = totalArgs * 8 - extraStackSpace;
            if (rspAdjust > 0)
            {
                _emit.AddRI(Reg64.RSP, rspAdjust);
            }
            else if (rspAdjust < 0)
            {
                _emit.SubRI(Reg64.RSP, -rspAdjust);
            }

            _evalStackDepth -= totalArgs;
        }

        // Load target address and call
        // We use an indirect call through RAX to support any address
        _emit.MovRI64(Reg64.RAX, (ulong)method.NativeCode);
        _emit.CallR(Reg64.RAX);

        // Clean up extra stack space if we allocated any
        if (stackArgs > 0)
        {
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            _emit.AddRI(Reg64.RSP, extraStackSpace);
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
                _emit.Code.EmitByte(0x48);  // REX.W
                _emit.Code.EmitByte(0x63);  // MOVSXD
                _emit.Code.EmitByte(0xC0);  // ModRM: RAX, EAX
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                // Return value in RAX - push directly
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Float32:
                // Return value in XMM0 - move to RAX and push
                // movd eax, xmm0
                _emit.MovdR32Xmm(Reg64.RAX, RegXMM.XMM0);
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Float64:
                // Return value in XMM0 - move to RAX and push
                // movq rax, xmm0
                _emit.MovqR64Xmm(Reg64.RAX, RegXMM.XMM0);
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Struct:
                // Struct returns are complex - for now, treat as pointer in RAX
                _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.R11);  // ftnPtr
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
            _emit.Pop(Reg64.RCX);
            _evalStackDepth--;
        }
        else if (argCount == 2)
        {
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
            _evalStackDepth -= 2;
        }
        else if (argCount == 3)
        {
            _emit.Pop(Reg64.R8);    // arg2
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
            _evalStackDepth -= 3;
        }
        else if (argCount == 4)
        {
            _emit.Pop(Reg64.R9);    // arg3
            _emit.Pop(Reg64.R8);    // arg2
            _emit.Pop(Reg64.RDX);   // arg1
            _emit.Pop(Reg64.RCX);   // arg0
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
            _emit.MovRM(Reg64.RCX, Reg64.RSP, (argCount - 1) * 8);  // arg0
            _emit.MovRM(Reg64.RDX, Reg64.RSP, (argCount - 2) * 8);  // arg1
            _emit.MovRM(Reg64.R8, Reg64.RSP, (argCount - 3) * 8);   // arg2
            _emit.MovRM(Reg64.R9, Reg64.RSP, (argCount - 4) * 8);   // arg3

            // Copy stack args to their final locations (relative to current RSP)
            // arg(4+i) source: [RSP + (stackArgs-1-i)*8]
            // arg(4+i) dest: [RSP + argCount*8 - extraStackSpace + 32 + i*8]
            for (int i = 0; i < stackArgs; i++)
            {
                int srcOffset = (stackArgs - 1 - i) * 8;
                int dstOffset = argCount * 8 - extraStackSpace + 32 + i * 8;
                _emit.MovRM(Reg64.R10, Reg64.RSP, srcOffset);
                _emit.MovMR(Reg64.RSP, dstOffset, Reg64.R10);
            }

            // Now adjust RSP to point to the call frame
            // Final RSP = current RSP + argCount*8 - extraStackSpace
            int rspAdjust = argCount * 8 - extraStackSpace;
            if (rspAdjust > 0)
            {
                _emit.AddRI(Reg64.RSP, rspAdjust);
            }
            else if (rspAdjust < 0)
            {
                _emit.SubRI(Reg64.RSP, -rspAdjust);
            }

            _evalStackDepth -= argCount;
        }

        // Call through the function pointer in R11
        _emit.CallR(Reg64.R11);

        // Clean up extra stack space if we allocated any
        if (stackArgs > 0)
        {
            int extraStackSpace = ((stackArgs * 8) + 15) & ~15;
            _emit.AddRI(Reg64.RSP, extraStackSpace);
        }

        // Handle return value (same as CompileCall)
        switch (returnKind)
        {
            case ReturnKind.Void:
                // No return value
                break;

            case ReturnKind.Int32:
                // Sign-extend EAX to RAX
                _emit.Code.EmitByte(0x48);  // REX.W
                _emit.Code.EmitByte(0x63);  // MOVSXD
                _emit.Code.EmitByte(0xC0);  // ModRM: RAX, EAX
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Int64:
            case ReturnKind.IntPtr:
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Float32:
                _emit.MovdR32Xmm(Reg64.RAX, RegXMM.XMM0);
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Float64:
                _emit.MovqR64Xmm(Reg64.RAX, RegXMM.XMM0);
                _emit.Push(Reg64.RAX);
                _evalStackDepth++;
                break;

            case ReturnKind.Struct:
                _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.R8);
        _evalStackDepth--;

        // Pop srcAddr into RDX (second arg)
        _emit.Pop(Reg64.RDX);
        _evalStackDepth--;

        // Pop destAddr into RCX (first arg)
        _emit.Pop(Reg64.RCX);
        _evalStackDepth--;

        // Call CPU.MemCopy(dest, src, count)
        delegate*<void*, void*, ulong, void*> memcopyFn = &CPU.MemCopy;
        _emit.MovRI64(Reg64.RAX, (ulong)memcopyFn);
        _emit.CallR(Reg64.RAX);

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
        _emit.Pop(Reg64.R8);
        _evalStackDepth--;

        // Pop value into RDX (second arg) - note: IL has int32, MemSet takes byte
        // We just pass it through - the native function will use the low byte
        _emit.Pop(Reg64.RDX);
        _evalStackDepth--;

        // Pop addr into RCX (first arg)
        _emit.Pop(Reg64.RCX);
        _evalStackDepth--;

        // Call CPU.MemSet(dest, value, count)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        _emit.MovRI64(Reg64.RAX, (ulong)memsetFn);
        _emit.CallR(Reg64.RAX);

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
        _emit.Pop(Reg64.RCX);
        _evalStackDepth--;

        // Value = 0 (second arg)
        _emit.XorRR(Reg64.RDX, Reg64.RDX);

        // Size in R8 (third arg)
        _emit.MovRI64(Reg64.R8, (ulong)size);

        // Call CPU.MemSet(addr, 0, size)
        delegate*<void*, byte, ulong, void*> memsetFn = &CPU.MemSet;
        _emit.MovRI64(Reg64.RAX, (ulong)memsetFn);
        _emit.CallR(Reg64.RAX);

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
        _emit.MovRI32(Reg64.RAX, size);
        _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;

        // Load value based on size
        switch (size)
        {
            case 1:
                _emit.MovzxByte(Reg64.RAX, Reg64.RAX, 0);
                break;
            case 2:
                _emit.MovzxWord(Reg64.RAX, Reg64.RAX, 0);
                break;
            case 4:
                _emit.MovRM32(Reg64.RAX, Reg64.RAX, 0);
                break;
            case 8:
                _emit.MovRM(Reg64.RAX, Reg64.RAX, 0);
                break;
            default:
                // For larger structs, we'd need to copy to stack
                // For now, just handle common small sizes
                DebugConsole.Write("[JIT] ldobj: unsupported size ");
                DebugConsole.WriteDecimal((uint)size);
                DebugConsole.WriteLine();
                return false;
        }

        _emit.Push(Reg64.RAX);
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
        _emit.Pop(Reg64.RDX);
        _evalStackDepth--;

        // Pop addr into RAX
        _emit.Pop(Reg64.RAX);
        _evalStackDepth--;

        // Store value based on size
        switch (size)
        {
            case 1:
                _emit.MovMR8(Reg64.RAX, 0, Reg64.RDX);
                break;
            case 2:
                _emit.MovMR16(Reg64.RAX, 0, Reg64.RDX);
                break;
            case 4:
                _emit.MovMR32(Reg64.RAX, 0, Reg64.RDX);
                break;
            case 8:
                _emit.MovMR(Reg64.RAX, 0, Reg64.RDX);
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
        _emit.Pop(Reg64.RDX);
        _evalStackDepth--;

        // Pop destAddr into RCX (first arg for MemCopy)
        _emit.Pop(Reg64.RCX);
        _evalStackDepth--;

        // Size in R8 (third arg)
        _emit.MovRI64(Reg64.R8, (ulong)size);

        // Call CPU.MemCopy(dest, src, size)
        delegate*<void*, void*, ulong, void*> memcopyFn = &CPU.MemCopy;
        _emit.MovRI64(Reg64.RAX, (ulong)memcopyFn);
        _emit.CallR(Reg64.RAX);

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
        int patchOffset = _emit.JmpRel32();
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
        _emit.Pop(Reg64.RCX);
        _evalStackDepth--;

        // Call RhpThrowEx (defined in native.asm)
        // This captures context and dispatches the exception
        // It does not return normally - it unwinds to a handler
        var throwFn = CPU.GetThrowExFuncPtr();
        _emit.MovRI64(Reg64.RAX, (ulong)throwFn);
        _emit.CallR(Reg64.RAX);

        // RhpThrowEx does not return, but we add int3 for safety
        _emit.Int3();

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
        _emit.MovRI64(Reg64.RAX, (ulong)rethrowFn);
        _emit.CallR(Reg64.RAX);

        // RhpRethrow does not return
        _emit.Int3();

        return true;
    }

    /// <summary>
    /// Compile endfinally (also endfault) - Return from finally/fault handler.
    /// Stack must be empty. Control returns to the runtime which decides
    /// where to continue (either continue unwinding or resume execution).
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

        // Call RhpCallFinallyFunclet return sequence
        // In a simple implementation, we can just emit a ret
        // The runtime arranges for finally handlers to be called as functions
        // that return to the unwinding code
        _emit.EmitEpilogue(_stackAdjust);

        return true;
    }
}
