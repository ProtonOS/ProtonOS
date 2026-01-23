// ProtonOS JIT - x64 Instruction Emitter
// Emits x64 machine code with proper REX prefix and ModR/M encoding.
// Implements ICodeEmitter for architecture-neutral JIT support.

using ProtonOS.Arch;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// x64 register encoding
/// </summary>
public enum Reg64 : byte
{
    RAX = 0, RCX = 1, RDX = 2, RBX = 3,
    RSP = 4, RBP = 5, RSI = 6, RDI = 7,
    R8 = 8, R9 = 9, R10 = 10, R11 = 11,
    R12 = 12, R13 = 13, R14 = 14, R15 = 15
}

/// <summary>
/// XMM register encoding for SSE operations
/// </summary>
public enum RegXMM : byte
{
    XMM0 = 0, XMM1 = 1, XMM2 = 2, XMM3 = 3,
    XMM4 = 4, XMM5 = 5, XMM6 = 6, XMM7 = 7,
    XMM8 = 8, XMM9 = 9, XMM10 = 10, XMM11 = 11,
    XMM12 = 12, XMM13 = 13, XMM14 = 14, XMM15 = 15
}

/// <summary>
/// x64 instruction emitter for JIT compilation.
/// Builds on CodeBuffer to emit properly encoded x64 instructions.
/// Implements ICodeEmitter for architecture-neutral JIT support.
/// </summary>
public unsafe struct X64Emitter : ICodeEmitter<X64Emitter>
{
    // REX prefix constants (also used by static interface methods)
    private const byte REX = 0x40;
    private const byte REX_W = 0x08;  // 64-bit operand size
    private const byte REX_R = 0x04;  // Extend ModR/M reg
    private const byte REX_X = 0x02;  // Extend SIB index
    private const byte REX_B = 0x01;  // Extend ModR/M r/m or SIB base

    /// <summary>
    /// Build ModR/M byte
    /// mod: 00 = [r/m], 01 = [r/m+disp8], 10 = [r/m+disp32], 11 = r/m is register
    /// reg: register operand or opcode extension (3 bits, low bits only)
    /// rm: register or memory operand (3 bits, low bits only)
    /// </summary>
    private static byte ModRM(byte mod, byte reg, byte rm)
    {
        return (byte)((mod << 6) | ((reg & 7) << 3) | (rm & 7));
    }

    // Condition codes for Jcc (used by ILCompiler for branch conditions)
    public const byte CC_O = 0x0;   // Overflow
    public const byte CC_NO = 0x1;  // Not overflow
    public const byte CC_B = 0x2;   // Below (unsigned <)
    public const byte CC_AE = 0x3;  // Above or equal (unsigned >=)
    public const byte CC_E = 0x4;   // Equal / Zero
    public const byte CC_NE = 0x5;  // Not equal / Not zero
    public const byte CC_BE = 0x6;  // Below or equal (unsigned <=)
    public const byte CC_A = 0x7;   // Above (unsigned >)
    public const byte CC_S = 0x8;   // Sign (negative)
    public const byte CC_NS = 0x9;  // Not sign
    public const byte CC_L = 0xC;   // Less (signed <)
    public const byte CC_GE = 0xD;  // Greater or equal (signed >=)
    public const byte CC_LE = 0xE;  // Less or equal (signed <=)
    public const byte CC_G = 0xF;   // Greater (signed >)

    // ==================== ICodeEmitter Static Interface Implementation ====================
    // These static methods implement the architecture-neutral interface.
    // They use static helper methods that directly emit to the CodeBuffer.

    // Helper methods for x64 instruction encoding
    private static void EmitRex(ref CodeBuffer code, bool w, Reg64 reg, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)reg >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitRexSingle(ref CodeBuffer code, bool w, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitModRMReg(ref CodeBuffer code, Reg64 reg, Reg64 rm)
    {
        code.EmitByte(ModRM(0b11, (byte)reg, (byte)rm));
    }

    private static void EmitModRMMem(ref CodeBuffer code, Reg64 reg, Reg64 baseReg)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (baseEnc == 4)
        {
            code.EmitByte(ModRM(0b00, (byte)reg, 0b100));
            code.EmitByte(0x24);
        }
        else if (baseEnc == 5)
        {
            code.EmitByte(ModRM(0b01, (byte)reg, baseEnc));
            code.EmitByte(0);
        }
        else
        {
            code.EmitByte(ModRM(0b00, (byte)reg, baseEnc));
        }
    }

    private static void EmitModRMMemDisp(ref CodeBuffer code, Reg64 reg, Reg64 baseReg, int disp)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (disp == 0 && baseEnc != 5) // RBP/R13 always need disp
        {
            EmitModRMMem(ref code, reg, baseReg);
        }
        else if (disp >= -128 && disp <= 127)
        {
            if (baseEnc == 4)
            {
                code.EmitByte(ModRM(0b01, (byte)reg, 0b100));
                code.EmitByte(0x24);
            }
            else
            {
                code.EmitByte(ModRM(0b01, (byte)reg, baseEnc));
            }
            code.EmitByte((byte)disp);
        }
        else
        {
            if (baseEnc == 4)
            {
                code.EmitByte(ModRM(0b10, (byte)reg, 0b100));
                code.EmitByte(0x24);
            }
            else
            {
                code.EmitByte(ModRM(0b10, (byte)reg, baseEnc));
            }
            code.EmitInt32(disp);
        }
    }

    /// <summary>
    /// Map VReg to x64 Reg64.
    /// </summary>
    public static Reg64 Map(VReg vreg) => vreg switch
    {
        VReg.R0 => Reg64.RAX,   // Return value + scratch
        VReg.R1 => Reg64.RCX,   // Arg0 (MS x64 ABI)
        VReg.R2 => Reg64.RDX,   // Arg1
        VReg.R3 => Reg64.R8,    // Arg2
        VReg.R4 => Reg64.R9,    // Arg3
        VReg.R5 => Reg64.R10,   // Scratch
        VReg.R6 => Reg64.R11,   // Scratch
        VReg.R7 => Reg64.RBX,   // Callee-saved
        VReg.R8 => Reg64.R12,   // Callee-saved
        VReg.R9 => Reg64.R13,   // Callee-saved
        VReg.R10 => Reg64.R14,  // Callee-saved
        VReg.R11 => Reg64.R15,  // Callee-saved
        VReg.SP => Reg64.RSP,
        VReg.FP => Reg64.RBP,
        _ => Reg64.RAX,
    };

    /// <summary>
    /// Map VRegF to x64 RegXMM.
    /// </summary>
    private static RegXMM MapF(VRegF vreg) => (RegXMM)(byte)vreg;

    /// <summary>
    /// Map Condition to x64 condition code.
    /// </summary>
    private static byte MapCondition(Condition cond) => cond switch
    {
        Condition.Equal => CC_E,
        Condition.NotEqual => CC_NE,
        Condition.LessThan => CC_L,
        Condition.LessOrEqual => CC_LE,
        Condition.GreaterThan => CC_G,
        Condition.GreaterOrEqual => CC_GE,
        Condition.Below => CC_B,
        Condition.BelowOrEqual => CC_BE,
        Condition.Above => CC_A,
        Condition.AboveOrEqual => CC_AE,
        _ => CC_E,
    };

    // === Calling Convention Info ===
    public static int ArgRegisterCount => 4;
    public static int ShadowSpaceSize => 32;
    public static int StackAlignment => 16;

    // === Prologue / Epilogue ===
    // Callee-saved registers used by JIT: RBX (R7), R12 (R8), R13 (R9), R14 (R10), R15 (R11)
    // These are saved at [RBP-8] through [RBP-40] to avoid corrupting the caller's values.
    public const int CalleeSaveSize = 40;  // 5 registers * 8 bytes

    public static int EmitPrologue(ref CodeBuffer code, int localBytes)
    {
        // push rbp
        code.EmitByte(0x55);
        // mov rbp, rsp
        code.EmitByte(0x48);
        code.EmitByte(0x89);
        code.EmitByte(0xE5);

        // Align stack to 16 bytes (include saved rbp + callee-saves + locals)
        int frameSize = localBytes + ShadowSpaceSize + CalleeSaveSize;
        frameSize = (frameSize + 15) & ~15;

        if (frameSize > 0)
        {
            // sub rsp, frameSize
            code.EmitByte(0x48);
            code.EmitByte(0x81);
            code.EmitByte(0xEC);
            code.EmitInt32(frameSize);
        }

        // Save callee-saved registers to known offsets from RBP
        // mov [rbp-8], rbx
        code.EmitByte(0x48);
        code.EmitByte(0x89);
        code.EmitByte(0x5D);
        code.EmitByte(0xF8);  // -8

        // mov [rbp-16], r12
        code.EmitByte(0x4C);
        code.EmitByte(0x89);
        code.EmitByte(0x65);
        code.EmitByte(0xF0);  // -16

        // mov [rbp-24], r13
        code.EmitByte(0x4C);
        code.EmitByte(0x89);
        code.EmitByte(0x6D);
        code.EmitByte(0xE8);  // -24

        // mov [rbp-32], r14
        code.EmitByte(0x4C);
        code.EmitByte(0x89);
        code.EmitByte(0x75);
        code.EmitByte(0xE0);  // -32

        // mov [rbp-40], r15
        code.EmitByte(0x4C);
        code.EmitByte(0x89);
        code.EmitByte(0x7D);
        code.EmitByte(0xD8);  // -40

        // Note: Arguments are homed separately via HomeArguments() call in ILCompiler.cs
        // We must NOT home arguments here because:
        // 1. For vararg methods with 0 declared args, shadow space contains the sentinel TypedReference
        // 2. Homing garbage register values would overwrite the caller's data in shadow space
        // HomeArguments is called after EmitPrologue with the correct argument count.

        return frameSize;
    }

    public static void EmitEpilogue(ref CodeBuffer code, int stackAdjust)
    {
        // The stackAdjust parameter is no longer needed when using frame pointer epilogue
        _ = stackAdjust; // Unused - we restore from RBP instead

        // Restore callee-saved registers from known offsets
        // mov rbx, [rbp-8]
        code.EmitByte(0x48);
        code.EmitByte(0x8B);
        code.EmitByte(0x5D);
        code.EmitByte(0xF8);  // -8

        // mov r12, [rbp-16]
        code.EmitByte(0x4C);
        code.EmitByte(0x8B);
        code.EmitByte(0x65);
        code.EmitByte(0xF0);  // -16

        // mov r13, [rbp-24]
        code.EmitByte(0x4C);
        code.EmitByte(0x8B);
        code.EmitByte(0x6D);
        code.EmitByte(0xE8);  // -24

        // mov r14, [rbp-32]
        code.EmitByte(0x4C);
        code.EmitByte(0x8B);
        code.EmitByte(0x75);
        code.EmitByte(0xE0);  // -32

        // mov r15, [rbp-40]
        code.EmitByte(0x4C);
        code.EmitByte(0x8B);
        code.EmitByte(0x7D);
        code.EmitByte(0xD8);  // -40

        // Use leave instruction: mov rsp, rbp; pop rbp
        // This properly restores RSP regardless of any modifications during the function
        code.EmitByte(0xC9);  // leave
        // ret
        code.EmitByte(0xC3);
    }

    public static void HomeArguments(ref CodeBuffer code, int argCount)
    {
        // Home register arguments to shadow space (no float info - all integer)
        HomeArgumentsWithFloats(ref code, argCount, null);
    }

    public static unsafe void HomeArgumentsWithFloats(ref CodeBuffer code, int argCount, byte* floatKinds)
    {
        // Home register arguments to shadow space
        // Integer args: RCX -> [rbp+16], RDX -> [rbp+24], R8 -> [rbp+32], R9 -> [rbp+40]
        // Float args: XMM0 -> [rbp+16], XMM1 -> [rbp+24], XMM2 -> [rbp+32], XMM3 -> [rbp+40]
        // floatKinds: 0=integer, 4=float32, 8=float64

        if (argCount > 0)
        {
            byte fk0 = (floatKinds != null) ? floatKinds[0] : (byte)0;
            if (fk0 == 8)
            {
                // movq [rbp+16], xmm0
                MovqMemRbpXmm(ref code, 0x10, RegXMM.XMM0);
            }
            else if (fk0 == 4)
            {
                // movd [rbp+16], xmm0 (store 32-bit float)
                MovdMemRbpXmm(ref code, 0x10, RegXMM.XMM0);
            }
            else
            {
                // mov [rbp+16], rcx
                code.EmitByte(0x48);
                code.EmitByte(0x89);
                code.EmitByte(0x4D);
                code.EmitByte(0x10);
            }
        }
        if (argCount > 1)
        {
            byte fk1 = (floatKinds != null && argCount > 1) ? floatKinds[1] : (byte)0;
            if (fk1 == 8)
            {
                // movq [rbp+24], xmm1
                MovqMemRbpXmm(ref code, 0x18, RegXMM.XMM1);
            }
            else if (fk1 == 4)
            {
                // movd [rbp+24], xmm1
                MovdMemRbpXmm(ref code, 0x18, RegXMM.XMM1);
            }
            else
            {
                // mov [rbp+24], rdx
                code.EmitByte(0x48);
                code.EmitByte(0x89);
                code.EmitByte(0x55);
                code.EmitByte(0x18);
            }
        }
        if (argCount > 2)
        {
            byte fk2 = (floatKinds != null && argCount > 2) ? floatKinds[2] : (byte)0;
            if (fk2 == 8)
            {
                // movq [rbp+32], xmm2
                MovqMemRbpXmm(ref code, 0x20, RegXMM.XMM2);
            }
            else if (fk2 == 4)
            {
                // movd [rbp+32], xmm2
                MovdMemRbpXmm(ref code, 0x20, RegXMM.XMM2);
            }
            else
            {
                // mov [rbp+32], r8
                code.EmitByte(0x4C);
                code.EmitByte(0x89);
                code.EmitByte(0x45);
                code.EmitByte(0x20);
            }
        }
        if (argCount > 3)
        {
            byte fk3 = (floatKinds != null && argCount > 3) ? floatKinds[3] : (byte)0;
            if (fk3 == 8)
            {
                // movq [rbp+40], xmm3
                MovqMemRbpXmm(ref code, 0x28, RegXMM.XMM3);
            }
            else if (fk3 == 4)
            {
                // movd [rbp+40], xmm3
                MovdMemRbpXmm(ref code, 0x28, RegXMM.XMM3);
            }
            else
            {
                // mov [rbp+40], r9
                code.EmitByte(0x4C);
                code.EmitByte(0x89);
                code.EmitByte(0x4D);
                code.EmitByte(0x28);
            }
        }
    }

    // === Register Operations ===
    public static void MovRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x89);  // MOV r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void MovRI64(ref CodeBuffer code, VReg dst, ulong imm)
    {
        var d = Map(dst);
        EmitRexSingle(ref code, true, d);
        code.EmitByte((byte)(0xB8 + ((byte)d & 7)));  // MOV r64, imm64
        code.EmitQword(imm);
    }

    public static void MovRI32(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        if ((byte)d >= 8)
            code.EmitByte((byte)(REX | REX_B));
        code.EmitByte((byte)(0xB8 + ((byte)d & 7)));  // MOV r32, imm32
        code.EmitInt32(imm);
    }

    public static void ZeroReg(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        // XOR r32, r32 clears upper 32 bits and is shorter
        if ((byte)r >= 8)
        {
            code.EmitByte((byte)(REX | REX_R | REX_B));
        }
        code.EmitByte(0x31);  // XOR r/m32, r32
        EmitModRMReg(ref code, r, r);
    }

    // === Memory Operations ===
    public static void Load64(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRex(ref code, true, d, b);
        code.EmitByte(0x8B);  // MOV r64, r/m64
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load32(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        // 32-bit load zero-extends to 64-bit, no REX.W needed
        if ((byte)d >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)d >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x8B);  // MOV r32, r/m32
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load32Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRex(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x63);  // MOVSXD r64, r/m32
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load16(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        if ((byte)d >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)d >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0xB7);  // MOVZX r32, r/m16
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load16Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRex(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBF);  // MOVSX r64, r/m16
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load8(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        if ((byte)d >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)d >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0xB6);  // MOVZX r32, r/m8
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Load8Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRex(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBE);  // MOVSX r64, r/m8
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    public static void Store64(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        var b = Map(baseReg);
        var s = Map(src);
        EmitRex(ref code, true, s, b);
        code.EmitByte(0x89);  // MOV r/m64, r64
        EmitModRMMemDisp(ref code, s, b, disp);
    }

    public static void Store32(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        var b = Map(baseReg);
        var s = Map(src);
        if ((byte)s >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)s >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x89);  // MOV r/m32, r32
        EmitModRMMemDisp(ref code, s, b, disp);
    }

    public static void Store16(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        var b = Map(baseReg);
        var s = Map(src);
        code.EmitByte(0x66);  // Operand size prefix
        if ((byte)s >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)s >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x89);  // MOV r/m16, r16
        EmitModRMMemDisp(ref code, s, b, disp);
    }

    public static void Store8(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        var b = Map(baseReg);
        var s = Map(src);
        // Need REX prefix for R8-R15 or to access SPL/BPL/SIL/DIL
        if ((byte)s >= 8 || (byte)b >= 8 || (byte)s >= 4)
        {
            byte rex = REX;
            if ((byte)s >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x88);  // MOV r/m8, r8
        EmitModRMMemDisp(ref code, s, b, disp);
    }

    public static void LoadAddress(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRex(ref code, true, d, b);
        code.EmitByte(0x8D);  // LEA r64, m
        EmitModRMMemDisp(ref code, d, b, disp);
    }

    // === Arithmetic ===
    public static void Add(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x01);  // ADD r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void AddImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingle(ref code, true, d);
        if (imm >= -128 && imm <= 127)
        {
            code.EmitByte(0x83);  // ADD r/m64, imm8
            code.EmitByte(ModRM(0b11, 0, (byte)d));
            code.EmitByte((byte)imm);
        }
        else
        {
            code.EmitByte(0x81);  // ADD r/m64, imm32
            code.EmitByte(ModRM(0b11, 0, (byte)d));
            code.EmitInt32(imm);
        }
    }

    public static void Sub(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x29);  // SUB r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void SubImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingle(ref code, true, d);
        if (imm >= -128 && imm <= 127)
        {
            code.EmitByte(0x83);  // SUB r/m64, imm8
            code.EmitByte(ModRM(0b11, 5, (byte)d));
            code.EmitByte((byte)imm);
        }
        else
        {
            code.EmitByte(0x81);  // SUB r/m64, imm32
            code.EmitByte(ModRM(0b11, 5, (byte)d));
            code.EmitInt32(imm);
        }
    }

    public static void Mul(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0xAF);  // IMUL r64, r/m64
        EmitModRMReg(ref code, d, s);
    }

    public static void DivSigned(ref CodeBuffer code, VReg divisor)
    {
        var r = Map(divisor);
        EmitRexSingle(ref code, true, r);
        code.EmitByte(0xF7);  // IDIV r/m64
        code.EmitByte(ModRM(0b11, 7, (byte)r));
    }

    public static void DivUnsigned(ref CodeBuffer code, VReg divisor)
    {
        var r = Map(divisor);
        EmitRexSingle(ref code, true, r);
        code.EmitByte(0xF7);  // DIV r/m64
        code.EmitByte(ModRM(0b11, 6, (byte)r));
    }

    public static void Neg(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        EmitRexSingle(ref code, true, r);
        code.EmitByte(0xF7);  // NEG r/m64
        code.EmitByte(ModRM(0b11, 3, (byte)r));
    }

    // === Bitwise Operations ===
    public static void And(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x21);  // AND r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void AndImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingle(ref code, true, d);
        if (imm >= -128 && imm <= 127)
        {
            code.EmitByte(0x83);  // AND r/m64, imm8
            code.EmitByte(ModRM(0b11, 4, (byte)d));
            code.EmitByte((byte)imm);
        }
        else
        {
            code.EmitByte(0x81);  // AND r/m64, imm32
            code.EmitByte(ModRM(0b11, 4, (byte)d));
            code.EmitInt32(imm);
        }
    }

    public static void Or(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x09);  // OR r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void Xor(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, s, d);
        code.EmitByte(0x31);  // XOR r/m64, r64
        EmitModRMReg(ref code, s, d);
    }

    public static void Not(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        EmitRexSingle(ref code, true, r);
        code.EmitByte(0xF7);  // NOT r/m64
        code.EmitByte(ModRM(0b11, 2, (byte)r));
    }

    public static void ShiftLeft(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        // x64 requires shift amount in CL
        if (s != Reg64.RCX)
        {
            EmitRex(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);  // MOV
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        EmitRexSingle(ref code, true, v);
        code.EmitByte(0xD3);  // SHL r/m64, CL
        code.EmitByte(ModRM(0b11, 4, (byte)v));
    }

    public static void ShiftLeftImm(ref CodeBuffer code, VReg value, byte imm)
    {
        var v = Map(value);
        EmitRexSingle(ref code, true, v);
        if (imm == 1)
        {
            code.EmitByte(0xD1);  // SHL r/m64, 1
            code.EmitByte(ModRM(0b11, 4, (byte)v));
        }
        else
        {
            code.EmitByte(0xC1);  // SHL r/m64, imm8
            code.EmitByte(ModRM(0b11, 4, (byte)v));
            code.EmitByte(imm);
        }
    }

    public static void ShiftRightSigned(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            EmitRex(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        EmitRexSingle(ref code, true, v);
        code.EmitByte(0xD3);  // SAR r/m64, CL
        code.EmitByte(ModRM(0b11, 7, (byte)v));
    }

    public static void ShiftRightSigned32(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            // Move shift amount to CL (32-bit move is fine)
            if ((byte)s >= 8)
            {
                code.EmitByte(0x44);  // REX.R for source register
            }
            code.EmitByte(0x89);
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        // 32-bit SAR: no REX.W prefix (or only REX.B if register >= R8)
        if ((byte)v >= 8)
        {
            code.EmitByte(0x41);  // REX.B
        }
        code.EmitByte(0xD3);  // SAR r/m32, CL
        code.EmitByte(ModRM(0b11, 7, (byte)((byte)v & 7)));
    }

    public static void ShiftRightUnsigned(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            EmitRex(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        EmitRexSingle(ref code, true, v);
        code.EmitByte(0xD3);  // SHR r/m64, CL
        code.EmitByte(ModRM(0b11, 5, (byte)v));
    }

    public static void ShiftRightUnsigned32(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            // Move shift amount to CL (32-bit move is fine)
            if ((byte)s >= 8)
            {
                code.EmitByte(0x44);  // REX.R for source register
            }
            code.EmitByte(0x89);
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        // 32-bit SHR: no REX.W prefix (or only REX.B if register >= R8)
        if ((byte)v >= 8)
        {
            code.EmitByte(0x41);  // REX.B
        }
        code.EmitByte(0xD3);  // SHR r/m32, CL
        code.EmitByte(ModRM(0b11, 5, (byte)((byte)v & 7)));
    }

    // === Comparison ===
    public static void Compare(ref CodeBuffer code, VReg left, VReg right)
    {
        var l = Map(left);
        var r = Map(right);
        EmitRex(ref code, true, r, l);
        code.EmitByte(0x39);  // CMP r/m64, r64
        EmitModRMReg(ref code, r, l);
    }

    public static void Compare32(ref CodeBuffer code, VReg left, VReg right)
    {
        var l = Map(left);
        var r = Map(right);
        if ((byte)l >= 8 || (byte)r >= 8)
        {
            byte rex = REX;
            if ((byte)r >= 8) rex |= REX_R;
            if ((byte)l >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x39);  // CMP r/m32, r32
        EmitModRMReg(ref code, r, l);
    }

    public static void CompareImm(ref CodeBuffer code, VReg reg, int imm)
    {
        var r = Map(reg);
        EmitRexSingle(ref code, true, r);
        if (imm >= -128 && imm <= 127)
        {
            code.EmitByte(0x83);  // CMP r/m64, imm8
            code.EmitByte(ModRM(0b11, 7, (byte)r));
            code.EmitByte((byte)imm);
        }
        else
        {
            code.EmitByte(0x81);  // CMP r/m64, imm32
            code.EmitByte(ModRM(0b11, 7, (byte)r));
            code.EmitInt32(imm);
        }
    }

    public static void Test(ref CodeBuffer code, VReg left, VReg right)
    {
        var l = Map(left);
        var r = Map(right);
        EmitRex(ref code, true, r, l);
        code.EmitByte(0x85);  // TEST r/m64, r64
        EmitModRMReg(ref code, r, l);
    }

    // === Control Flow ===
    public static void Ret(ref CodeBuffer code)
    {
        code.EmitByte(0xC3);  // RET
    }

    public static void CallReg(ref CodeBuffer code, VReg target)
    {
        var t = Map(target);
        if ((byte)t >= 8)
            code.EmitByte((byte)(REX | REX_B));
        code.EmitByte(0xFF);  // CALL r/m64
        code.EmitByte(ModRM(0b11, 2, (byte)t));
    }

    public static int CallRel32(ref CodeBuffer code)
    {
        code.EmitByte(0xE8);  // CALL rel32
        int patchOffset = code.Position;
        code.EmitInt32(0);  // Placeholder
        return patchOffset;
    }

    public static int JumpRel32(ref CodeBuffer code)
    {
        code.EmitByte(0xE9);  // JMP rel32
        int patchOffset = code.Position;
        code.EmitInt32(0);  // Placeholder
        return patchOffset;
    }

    public static void JumpReg(ref CodeBuffer code, VReg target)
    {
        var t = Map(target);
        if ((byte)t >= 8)
            code.EmitByte((byte)(REX | REX_B));
        code.EmitByte(0xFF);  // JMP r/m64
        code.EmitByte(ModRM(0b11, 4, (byte)t));
    }

    public static int JumpConditional(ref CodeBuffer code, Condition cond)
    {
        code.EmitByte(0x0F);
        code.EmitByte((byte)(0x80 + MapCondition(cond)));  // Jcc rel32
        int patchOffset = code.Position;
        code.EmitInt32(0);  // Placeholder
        return patchOffset;
    }

    public static void PatchRel32(ref CodeBuffer code, int patchOffset)
    {
        code.PatchRelative32(patchOffset);
    }

    // === Stack Operations ===
    public static void Push(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        if ((byte)r >= 8)
            code.EmitByte((byte)(REX | REX_B));
        code.EmitByte((byte)(0x50 + ((byte)r & 7)));  // PUSH r64
    }

    public static void Pop(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        if ((byte)r >= 8)
            code.EmitByte((byte)(REX | REX_B));
        code.EmitByte((byte)(0x58 + ((byte)r & 7)));  // POP r64
    }

    // === Argument/Local Access ===
    public static int GetArgOffset(int argIndex)
    {
        // Args are at [rbp+16+argIndex*8] after prologue
        return 16 + argIndex * 8;
    }

    public static int GetLocalOffset(int localIndex)
    {
        // Locals must be below the callee-save area (RBP-8 to RBP-40).
        // Callee-saved registers occupy 40 bytes (5 * 8), so locals start at RBP-48 or lower.
        // Each local gets 64 bytes to support value types up to 64 bytes.
        // Structs are stored growing UPWARD from the base offset, so we need to ensure
        // the entire 64-byte slot is below the callee-save area.
        // Local 0 starts at -(CalleeSaveSize + 64) = -(40 + 64) = -104
        // Local 1 starts at -(CalleeSaveSize + 128) = -168, etc.
        return -(CalleeSaveSize + (localIndex + 1) * 64);
    }

    public static void LoadArg(ref CodeBuffer code, VReg dst, int argIndex)
    {
        // Load from homed location: [rbp+16+argIndex*8]
        int offset = GetArgOffset(argIndex);
        Load64(ref code, dst, VReg.FP, offset);
    }

    public static void LoadLocal(ref CodeBuffer code, VReg dst, int localIndex)
    {
        int offset = GetLocalOffset(localIndex);
        Load64(ref code, dst, VReg.FP, offset);
    }

    public static void StoreLocal(ref CodeBuffer code, int localIndex, VReg src)
    {
        int offset = GetLocalOffset(localIndex);
        Store64(ref code, VReg.FP, offset, src);
    }

    // === Floating Point ===
    // Static helper for XMM REX prefix
    private static void EmitRexXmm(ref CodeBuffer code, bool w, RegXMM xmm, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)xmm >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitRexXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
    }

    public static void LoadFloat32(ref CodeBuffer code, VRegF dst, VReg baseReg, int disp)
    {
        var xmm = MapF(dst);
        var b = Map(baseReg);
        code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmm(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSS xmm, m32
        EmitModRMMemDisp(ref code, (Reg64)xmm, b, disp);
    }

    public static void LoadFloat64(ref CodeBuffer code, VRegF dst, VReg baseReg, int disp)
    {
        var xmm = MapF(dst);
        var b = Map(baseReg);
        code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmm(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSD xmm, m64
        EmitModRMMemDisp(ref code, (Reg64)xmm, b, disp);
    }

    public static void StoreFloat32(ref CodeBuffer code, VReg baseReg, int disp, VRegF src)
    {
        var xmm = MapF(src);
        var b = Map(baseReg);
        code.EmitByte(0xF3);
        EmitRexXmm(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x11);  // MOVSS m32, xmm
        EmitModRMMemDisp(ref code, (Reg64)xmm, b, disp);
    }

    public static void StoreFloat64(ref CodeBuffer code, VReg baseReg, int disp, VRegF src)
    {
        var xmm = MapF(src);
        var b = Map(baseReg);
        code.EmitByte(0xF2);
        EmitRexXmm(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x11);  // MOVSD m64, xmm
        EmitModRMMemDisp(ref code, (Reg64)xmm, b, disp);
    }

    public static void MovFF(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSS/MOVSD xmm, xmm
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    public static void AddFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x58);  // ADDSS/ADDSD
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    public static void SubFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5C);  // SUBSS/SUBSD
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    public static void MulFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x59);  // MULSS/MULSD
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    public static void DivFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5E);  // DIVSS/DIVSD
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    // === Conversions ===
    public static void ConvertInt32ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble)
    {
        var xmm = MapF(dst);
        var r = Map(src);
        code.EmitByte(toDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmm(ref code, false, xmm, r);  // No REX.W = 32-bit source
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);  // CVTSI2SS/CVTSI2SD
        EmitModRMReg(ref code, (Reg64)xmm, r);
    }

    public static void ConvertInt64ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble)
    {
        var xmm = MapF(dst);
        var r = Map(src);
        code.EmitByte(toDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmm(ref code, true, xmm, r);  // REX.W for 64-bit source
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);  // CVTSI2SS/CVTSI2SD
        EmitModRMReg(ref code, (Reg64)xmm, r);
    }

    public static void ConvertFloatToInt64(ref CodeBuffer code, VReg dst, VRegF src, bool fromDouble)
    {
        var r = Map(dst);
        var xmm = MapF(src);
        code.EmitByte(fromDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmm(ref code, true, (RegXMM)r, (Reg64)xmm);  // REX.W for 64-bit dest
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);  // CVTTSS2SI/CVTTSD2SI (truncate)
        EmitModRMReg(ref code, r, (Reg64)xmm);
    }

    public static void ConvertFloatPrecision(ref CodeBuffer code, VRegF dst, VRegF src, bool toDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(toDouble ? (byte)0xF3 : (byte)0xF2);  // F3 for CVTSS2SD, F2 for CVTSD2SS
        EmitRexXmmXmm(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5A);  // CVTSS2SD or CVTSD2SS
        EmitModRMReg(ref code, (Reg64)d, (Reg64)s);
    }

    // === Miscellaneous ===
    public static void Nop(ref CodeBuffer code)
    {
        code.EmitByte(0x90);  // NOP
    }

    public static void Breakpoint(ref CodeBuffer code)
    {
        code.EmitByte(0xCC);  // INT 3
    }

    public static void SignExtendForDivision(ref CodeBuffer code)
    {
        // CQO - sign-extend RAX to RDX:RAX for 64-bit division
        code.EmitByte(0x48);  // REX.W
        code.EmitByte(0x99);  // CQO
    }

    public static void ZeroExtend32(ref CodeBuffer code, VReg reg)
    {
        // Move 32-bit to 32-bit clears upper 32 bits
        var r = Map(reg);
        if ((byte)r >= 8)
        {
            code.EmitByte((byte)(REX | REX_R | REX_B));
        }
        code.EmitByte(0x89);  // MOV r32, r32
        EmitModRMReg(ref code, r, r);
    }

    // ==================== Additional Static Methods for ILCompiler ====================
    // These provide VReg-based static methods matching the instance method signatures

    /// <summary>
    /// Move from memory to register (64-bit): dst = [baseReg + disp]
    /// </summary>
    public static void MovRM(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load64(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move from register to memory (64-bit): [baseReg + disp] = src
    /// </summary>
    public static void MovMR(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        Store64(ref code, baseReg, disp, src);
    }

    /// <summary>
    /// Move from memory to register (32-bit zero-extended): dst = [baseReg + disp]
    /// </summary>
    public static void MovRM32(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load32(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move from register to memory (32-bit): [baseReg + disp] = src
    /// </summary>
    public static void MovMR32(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        Store32(ref code, baseReg, disp, src);
    }

    /// <summary>
    /// Move from register to memory (16-bit): [baseReg + disp] = src
    /// </summary>
    public static void MovMR16(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        Store16(ref code, baseReg, disp, src);
    }

    /// <summary>
    /// Move from register to memory (8-bit): [baseReg + disp] = src
    /// </summary>
    public static void MovMR8(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        Store8(ref code, baseReg, disp, src);
    }

    /// <summary>
    /// Move from memory to register (16-bit zero-extended): dst = [baseReg + disp]
    /// </summary>
    public static void MovRM16(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load16(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move from memory to register (8-bit zero-extended): dst = [baseReg + disp]
    /// </summary>
    public static void MovRM8(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load8(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Add register to register: dst = dst + src
    /// </summary>
    public static void AddRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Add(ref code, dst, src);
    }

    /// <summary>
    /// 32-bit add register to register: dst = dst + src (uses EAX/EDX, sets OF correctly for 32-bit)
    /// </summary>
    public static void Add32RR(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        // No REX.W prefix - use 32-bit operand size
        // Still need REX if using R8-R15 registers
        byte rex = REX;
        if ((byte)s >= 8) rex |= REX_R;
        if ((byte)d >= 8) rex |= REX_B;
        if (rex != REX)
            code.EmitByte(rex);
        code.EmitByte(0x01);  // ADD r/m32, r32
        code.EmitByte(ModRM(0b11, (byte)((int)s & 7), (byte)((int)d & 7)));
    }

    /// <summary>
    /// Add immediate to register: dst = dst + imm
    /// </summary>
    public static void AddRI(ref CodeBuffer code, VReg dst, int imm)
    {
        AddImm(ref code, dst, imm);
    }

    /// <summary>
    /// Subtract register from register: dst = dst - src
    /// </summary>
    public static void SubRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Sub(ref code, dst, src);
    }

    /// <summary>
    /// 32-bit subtract register from register: dst = dst - src (uses EAX/EDX, sets OF correctly for 32-bit)
    /// </summary>
    public static void Sub32RR(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        // No REX.W prefix - use 32-bit operand size
        byte rex = REX;
        if ((byte)s >= 8) rex |= REX_R;
        if ((byte)d >= 8) rex |= REX_B;
        if (rex != REX)
            code.EmitByte(rex);
        code.EmitByte(0x29);  // SUB r/m32, r32
        code.EmitByte(ModRM(0b11, (byte)((int)s & 7), (byte)((int)d & 7)));
    }

    /// <summary>
    /// Subtract immediate from register: dst = dst - imm
    /// </summary>
    public static void SubRI(ref CodeBuffer code, VReg dst, int imm)
    {
        SubImm(ref code, dst, imm);
    }

    /// <summary>
    /// Signed multiply: dst = dst * src
    /// </summary>
    public static void ImulRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Mul(ref code, dst, src);
    }

    /// <summary>
    /// 32-bit signed multiply: dst = dst * src (uses EAX/EDX, sets OF correctly for 32-bit)
    /// </summary>
    public static void Imul32RR(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        // No REX.W prefix - use 32-bit operand size
        byte rex = REX;
        if ((byte)d >= 8) rex |= REX_R;
        if ((byte)s >= 8) rex |= REX_B;
        if (rex != REX)
            code.EmitByte(rex);
        code.EmitByte(0x0F);
        code.EmitByte(0xAF);  // IMUL r32, r/m32
        code.EmitByte(ModRM(0b11, (byte)((int)d & 7), (byte)((int)s & 7)));
    }

    /// <summary>
    /// Signed multiply by immediate: dst = dst * imm
    /// Encodes: imul r64, r64, imm32 (REX.W + 69 /r id)
    /// </summary>
    public static void ImulRI(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        // Need full REX encoding because the register appears in both reg and r/m fields
        EmitRex(ref code, true, d, d);
        code.EmitByte(0x69);  // imul r64, r/m64, imm32
        code.EmitByte(ModRM(0b11, (byte)d, (byte)d));  // dst is both dest and source
        code.EmitInt32(imm);
    }

    /// <summary>
    /// XOR register with register: dst = dst ^ src
    /// </summary>
    public static void XorRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Xor(ref code, dst, src);
    }

    /// <summary>
    /// OR register with register: dst = dst | src
    /// </summary>
    public static void OrRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Or(ref code, dst, src);
    }

    /// <summary>
    /// AND register with register: dst = dst & src
    /// </summary>
    public static void AndRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        And(ref code, dst, src);
    }

    /// <summary>
    /// Shift left by immediate.
    /// </summary>
    public static void ShlImm(ref CodeBuffer code, VReg reg, byte imm)
    {
        ShiftLeftImm(ref code, reg, imm);
    }

    /// <summary>
    /// Shift left by immediate (alias for ShlImm).
    /// </summary>
    public static void ShlRI(ref CodeBuffer code, VReg reg, byte imm)
    {
        ShiftLeftImm(ref code, reg, imm);
    }

    /// <summary>
    /// Shift right (arithmetic/signed) by immediate.
    /// </summary>
    public static void SarRI(ref CodeBuffer code, VReg reg, byte imm)
    {
        var v = Map(reg);
        EmitRexSingle(ref code, true, v);
        if (imm == 1)
        {
            code.EmitByte(0xD1);  // SAR r/m64, 1
            code.EmitByte(ModRM(0b11, 7, (byte)v));
        }
        else
        {
            code.EmitByte(0xC1);  // SAR r/m64, imm8
            code.EmitByte(ModRM(0b11, 7, (byte)v));
            code.EmitByte(imm);
        }
    }

    /// <summary>
    /// Shift right (logical/unsigned) by immediate.
    /// </summary>
    public static void ShrRI(ref CodeBuffer code, VReg reg, byte imm)
    {
        var v = Map(reg);
        EmitRexSingle(ref code, true, v);
        if (imm == 1)
        {
            code.EmitByte(0xD1);  // SHR r/m64, 1
            code.EmitByte(ModRM(0b11, 5, (byte)v));
        }
        else
        {
            code.EmitByte(0xC1);  // SHR r/m64, imm8
            code.EmitByte(ModRM(0b11, 5, (byte)v));
            code.EmitByte(imm);
        }
    }

    /// <summary>
    /// Shift right (arithmetic/signed) by register.
    /// </summary>
    public static void SarCL(ref CodeBuffer code, VReg reg)
    {
        // Shift amount in CL (R1)
        ShiftRightSigned(ref code, reg, VReg.R1);
    }

    /// <summary>
    /// Shift right (logical/unsigned) by register.
    /// </summary>
    public static void ShrCL(ref CodeBuffer code, VReg reg)
    {
        ShiftRightUnsigned(ref code, reg, VReg.R1);
    }

    /// <summary>
    /// Shift left by register.
    /// </summary>
    public static void ShlCL(ref CodeBuffer code, VReg reg)
    {
        ShiftLeft(ref code, reg, VReg.R1);
    }

    /// <summary>
    /// Shift left 32-bit by register (CL). Result is zero-extended to 64 bits.
    /// </summary>
    public static void Shl32CL(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            // Move shift amount to CL
            if ((byte)s >= 8)
            {
                code.EmitByte(0x44);  // REX.R for source register
            }
            code.EmitByte(0x89);
            EmitModRMReg(ref code, s, Reg64.RCX);
        }
        // 32-bit SHL: no REX.W prefix (or only REX.B if register >= R8)
        if ((byte)v >= 8)
        {
            code.EmitByte(0x41);  // REX.B
        }
        code.EmitByte(0xD3);  // SHL r/m32, CL
        code.EmitByte(ModRM(0b11, 4, (byte)((byte)v & 7)));
    }

    /// <summary>
    /// Compare register with register (64-bit).
    /// </summary>
    public static void CmpRR(ref CodeBuffer code, VReg left, VReg right)
    {
        Compare(ref code, left, right);
    }

    /// <summary>
    /// Compare register with register (32-bit).
    /// </summary>
    public static void CmpRR32(ref CodeBuffer code, VReg left, VReg right)
    {
        Compare32(ref code, left, right);
    }

    /// <summary>
    /// Compare register with immediate.
    /// </summary>
    public static void CmpRI(ref CodeBuffer code, VReg reg, int imm)
    {
        CompareImm(ref code, reg, imm);
    }

    /// <summary>
    /// Test register with register (AND without storing result).
    /// </summary>
    public static void TestRR(ref CodeBuffer code, VReg left, VReg right)
    {
        Test(ref code, left, right);
    }

    /// <summary>
    /// Conditional move if zero (ZF=1): dst = src if ZF is set.
    /// CMOVZ r64, r64 is encoded as: REX.W 0F 44 /r
    /// </summary>
    public static void CmovzRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        byte dstReg = (byte)Map(dst);
        byte srcReg = (byte)Map(src);

        // REX prefix: always need REX.W for 64-bit
        byte rex = 0x48;
        if (dstReg >= 8) rex |= 0x04;  // REX.R
        if (srcReg >= 8) rex |= 0x01;  // REX.B
        code.EmitByte(rex);

        // Opcode: 0F 44 (CMOVZ)
        code.EmitByte(0x0F);
        code.EmitByte(0x44);

        // ModR/M: 11 dst src (register direct)
        byte modrm = (byte)(0xC0 | ((dstReg & 7) << 3) | (srcReg & 7));
        code.EmitByte(modrm);
    }

    /// <summary>
    /// Call through register.
    /// </summary>
    public static void CallR(ref CodeBuffer code, VReg target)
    {
        CallReg(ref code, target);
    }

    /// <summary>
    /// INT 3 breakpoint.
    /// </summary>
    public static void Int3(ref CodeBuffer code)
    {
        Breakpoint(ref code);
    }

    /// <summary>
    /// Load effective address: dst = baseReg + disp
    /// </summary>
    public static void Lea(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        LoadAddress(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Jump relative 32-bit. Returns patch offset.
    /// </summary>
    public static int JmpRel32(ref CodeBuffer code)
    {
        return JumpRel32(ref code);
    }

    /// <summary>
    /// Conditional jump. Returns patch offset.
    /// </summary>
    public static int JccRel32(ref CodeBuffer code, byte cc)
    {
        // Map x64 condition codes to Condition enum
        Condition cond = cc switch
        {
            CC_E => Condition.Equal,
            CC_NE => Condition.NotEqual,
            CC_L => Condition.LessThan,
            CC_LE => Condition.LessOrEqual,
            CC_G => Condition.GreaterThan,
            CC_GE => Condition.GreaterOrEqual,
            CC_B => Condition.Below,
            CC_BE => Condition.BelowOrEqual,
            CC_A => Condition.Above,
            CC_AE => Condition.AboveOrEqual,
            _ => Condition.Equal,
        };
        return JumpConditional(ref code, cond);
    }

    /// <summary>
    /// Jump if equal (ZF=1).
    /// </summary>
    public static int Je(ref CodeBuffer code)
    {
        return JumpConditional(ref code, Condition.Equal);
    }

    /// <summary>
    /// Jump if not equal (ZF=0).
    /// </summary>
    public static int Jne(ref CodeBuffer code)
    {
        return JumpConditional(ref code, Condition.NotEqual);
    }

    /// <summary>
    /// Patch a jump instruction at patchOffset to jump to targetOffset.
    /// Works for both JMP rel32 and Jcc rel32 instructions.
    /// </summary>
    public static void PatchJump(ref CodeBuffer code, int patchOffset, int targetOffset)
    {
        // rel32 is calculated from the end of the rel32 field (patchOffset + 4)
        int rel = targetOffset - (patchOffset + 4);
        code.PatchInt32(patchOffset, rel);
    }

    /// <summary>
    /// Move with zero-extend from byte: dst = zero-extend([baseReg + disp])
    /// </summary>
    public static void MovzxByte(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load8(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move with sign-extend from byte: dst = sign-extend([baseReg + disp])
    /// </summary>
    public static void MovsxByte(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load8Signed(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move with zero-extend from word: dst = zero-extend([baseReg + disp])
    /// </summary>
    public static void MovzxWord(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load16(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move with sign-extend from word: dst = sign-extend([baseReg + disp])
    /// </summary>
    public static void MovsxWord(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load16Signed(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Move with sign-extend from dword: dst = sign-extend([baseReg + disp])
    /// </summary>
    public static void MovsxdRM(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        Load32Signed(ref code, dst, baseReg, disp);
    }

    /// <summary>
    /// Zero-extend byte register: dst = zero-extend(src low byte)
    /// movzx r64, r8 (0x0F 0xB6 with ModRM mod=11)
    /// </summary>
    public static void MovzxByteReg(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        // Need REX for 64-bit destination or if using extended registers
        EmitRex(ref code, true, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0xB6);  // MOVZX r64, r/m8
        // ModRM: mod=11 (register), reg=dst, r/m=src
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// Sign-extend byte register: dst = sign-extend(src low byte)
    /// movsx r64, r8 (REX.W + 0x0F 0xBE with ModRM mod=11)
    /// </summary>
    public static void MovsxByteReg(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, d, s);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBE);  // MOVSX r64, r/m8
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// Zero-extend word register: dst = zero-extend(src low word)
    /// movzx r64, r16 (0x0F 0xB7 with ModRM mod=11)
    /// </summary>
    public static void MovzxWordReg(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0xB7);  // MOVZX r64, r/m16
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// Sign-extend word register: dst = sign-extend(src low word)
    /// movsx r64, r16 (REX.W + 0x0F 0xBF with ModRM mod=11)
    /// </summary>
    public static void MovsxWordReg(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, d, s);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBF);  // MOVSX r64, r/m16
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// Sign-extend dword register: dst = sign-extend(src low dword)
    /// movsxd r64, r32 (REX.W + 0x63 with ModRM mod=11)
    /// </summary>
    public static void MovsxdReg(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRex(ref code, true, d, s);  // REX.W for 64-bit destination
        code.EmitByte(0x63);  // MOVSXD r64, r/m32
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// 32-bit move (zero-extends to 64-bit): dst = src (low 32 bits)
    /// mov r32, r32 (0x8B with ModRM mod=11, no REX.W)
    /// </summary>
    public static void MovRR32(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        // Only emit REX if we need extended registers (no REX.W - 32-bit operation)
        if ((byte)d >= 8 || (byte)s >= 8)
        {
            byte rex = REX;
            if ((byte)d >= 8) rex |= REX_R;
            if ((byte)s >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x8B);  // MOV r32, r/m32
        code.EmitByte((byte)(0xC0 | (((byte)d & 7) << 3) | ((byte)s & 7)));
    }

    /// <summary>
    /// Load argument from home/shadow space.
    /// </summary>
    public static void LoadArgFromHome(ref CodeBuffer code, VReg dst, int argIndex)
    {
        LoadArg(ref code, dst, argIndex);
    }

    /// <summary>
    /// Negate register: reg = -reg
    /// </summary>
    public static void NegR(ref CodeBuffer code, VReg reg)
    {
        Neg(ref code, reg);
    }

    /// <summary>
    /// NOT register: reg = ~reg
    /// </summary>
    public static void NotR(ref CodeBuffer code, VReg reg)
    {
        Not(ref code, reg);
    }

    /// <summary>
    /// CQO - sign-extend RAX to RDX:RAX for 64-bit signed division.
    /// </summary>
    public static void Cqo(ref CodeBuffer code)
    {
        SignExtendForDivision(ref code);
    }

    /// <summary>
    /// IDIV - signed divide RDX:RAX by register.
    /// </summary>
    public static void IdivR(ref CodeBuffer code, VReg divisor)
    {
        DivSigned(ref code, divisor);
    }

    /// <summary>
    /// DIV - unsigned divide RDX:RAX by register.
    /// </summary>
    public static void DivR(ref CodeBuffer code, VReg divisor)
    {
        DivUnsigned(ref code, divisor);
    }

    // ==================== XMM Static Methods ====================

    /// <summary>
    /// Move qword from GPR to XMM: xmm = r64 (bits)
    /// </summary>
    public static void MovqXmmR64(ref CodeBuffer code, RegXMM xmm, VReg src)
    {
        var s = Map(src);
        // 66 REX.W 0F 6E /r - MOVQ xmm, r/m64
        code.EmitByte(0x66);
        EmitRex(ref code, true, (Reg64)xmm, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x6E);
        EmitModRMReg(ref code, (Reg64)xmm, s);
    }

    /// <summary>
    /// Move qword from XMM to GPR: r64 = xmm (bits)
    /// </summary>
    public static void MovqR64Xmm(ref CodeBuffer code, VReg dst, RegXMM xmm)
    {
        var d = Map(dst);
        // 66 REX.W 0F 7E /r - MOVQ r/m64, xmm
        code.EmitByte(0x66);
        EmitRex(ref code, true, (Reg64)xmm, d);
        code.EmitByte(0x0F);
        code.EmitByte(0x7E);
        EmitModRMReg(ref code, (Reg64)xmm, d);
    }

    /// <summary>
    /// Move dword from GPR to XMM: xmm = r32 (bits)
    /// </summary>
    public static void MovdXmmR32(ref CodeBuffer code, RegXMM xmm, VReg src)
    {
        var s = Map(src);
        // 66 0F 6E /r - MOVD xmm, r/m32
        code.EmitByte(0x66);
        if ((byte)xmm >= 8 || (byte)s >= 8)
        {
            byte rex = REX;
            if ((byte)xmm >= 8) rex |= REX_R;
            if ((byte)s >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x6E);
        EmitModRMReg(ref code, (Reg64)xmm, s);
    }

    /// <summary>
    /// Move dword from XMM to GPR: r32 = xmm (bits)
    /// </summary>
    public static void MovdR32Xmm(ref CodeBuffer code, VReg dst, RegXMM xmm)
    {
        var d = Map(dst);
        // 66 0F 7E /r - MOVD r/m32, xmm
        code.EmitByte(0x66);
        if ((byte)xmm >= 8 || (byte)d >= 8)
        {
            byte rex = REX;
            if ((byte)xmm >= 8) rex |= REX_R;
            if ((byte)d >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x7E);
        EmitModRMReg(ref code, (Reg64)xmm, d);
    }

    /// <summary>
    /// Store qword from XMM to memory: [rbp+disp8] = xmm (64 bits)
    /// Used for homing double arguments in prologue.
    /// </summary>
    public static void MovqMemRbpXmm(ref CodeBuffer code, int disp8, RegXMM xmm)
    {
        // 66 0F D6 /r - MOVQ m64, xmm (with mod=01 for [rbp+disp8])
        // REX.W is NOT needed for memory operand
        code.EmitByte(0x66);
        if ((byte)xmm >= 8)
        {
            code.EmitByte(REX | REX_R);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0xD6);
        // ModR/M: mod=01 (disp8), reg=xmm, rm=101 (rbp)
        code.EmitByte(ModRM(0b01, (byte)((byte)xmm & 7), 0b101));
        code.EmitByte((byte)disp8);
    }

    /// <summary>
    /// Store dword from XMM to memory: [rbp+disp8] = xmm (32 bits)
    /// Used for homing float arguments in prologue.
    /// Note: We actually use MOVD to store the full qword slot for ABI compliance.
    /// </summary>
    public static void MovdMemRbpXmm(ref CodeBuffer code, int disp8, RegXMM xmm)
    {
        // 66 0F 7E /r - MOVD m32, xmm (with mod=01 for [rbp+disp8])
        code.EmitByte(0x66);
        if ((byte)xmm >= 8)
        {
            code.EmitByte(REX | REX_R);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x7E);
        // ModR/M: mod=01 (disp8), reg=xmm, rm=101 (rbp)
        code.EmitByte(ModRM(0b01, (byte)((byte)xmm & 7), 0b101));
        code.EmitByte((byte)disp8);
    }

    /// <summary>
    /// ADDSD - Add scalar double.
    /// </summary>
    public static void AddsdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F2 0F 58 /r
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x58);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// ADDSS - Add scalar single.
    /// </summary>
    public static void AddssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F3 0F 58 /r
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x58);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// SUBSD - Subtract scalar double.
    /// </summary>
    public static void SubsdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F2 0F 5C /r
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5C);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// SUBSS - Subtract scalar single.
    /// </summary>
    public static void SubssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F3 0F 5C /r
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5C);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// MULSD - Multiply scalar double.
    /// </summary>
    public static void MulsdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F2 0F 59 /r
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x59);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// MULSS - Multiply scalar single.
    /// </summary>
    public static void MulssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F3 0F 59 /r
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x59);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// DIVSD - Divide scalar double.
    /// </summary>
    public static void DivsdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F2 0F 5E /r
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5E);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// DIVSS - Divide scalar single.
    /// </summary>
    public static void DivssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F3 0F 5E /r
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5E);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// COMISS - Compare scalar single (sets EFLAGS).
    /// </summary>
    public static void ComissXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // 0F 2F /r
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2F);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// COMISD - Compare scalar double (sets EFLAGS).
    /// </summary>
    public static void ComisdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // 66 0F 2F /r
        code.EmitByte(0x66);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2F);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// MOVSS from memory: xmm = [baseReg + disp]
    /// </summary>
    public static void MovssRM(ref CodeBuffer code, RegXMM dst, VReg baseReg, int disp)
    {
        var b = Map(baseReg);
        // F3 0F 10 /r - MOVSS xmm, m32
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x10);
        EmitModRMMemDisp(ref code, (Reg64)dst, b, disp);
    }

    /// <summary>
    /// MOVSS to memory: [baseReg + disp] = xmm
    /// </summary>
    public static void MovssMR(ref CodeBuffer code, VReg baseReg, int disp, RegXMM src)
    {
        var b = Map(baseReg);
        // F3 0F 11 /r - MOVSS m32, xmm
        code.EmitByte(0xF3);
        if ((byte)src >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)src >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x11);
        EmitModRMMemDisp(ref code, (Reg64)src, b, disp);
    }

    /// <summary>
    /// MOVSD from memory: xmm = [baseReg + disp]
    /// </summary>
    public static void MovsdRM(ref CodeBuffer code, RegXMM dst, VReg baseReg, int disp)
    {
        var b = Map(baseReg);
        // F2 0F 10 /r - MOVSD xmm, m64
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x10);
        EmitModRMMemDisp(ref code, (Reg64)dst, b, disp);
    }

    /// <summary>
    /// MOVSD to memory: [baseReg + disp] = xmm
    /// </summary>
    public static void MovsdMR(ref CodeBuffer code, VReg baseReg, int disp, RegXMM src)
    {
        var b = Map(baseReg);
        // F2 0F 11 /r - MOVSD m64, xmm
        code.EmitByte(0xF2);
        if ((byte)src >= 8 || (byte)b >= 8)
        {
            byte rex = REX;
            if ((byte)src >= 8) rex |= REX_R;
            if ((byte)b >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x11);
        EmitModRMMemDisp(ref code, (Reg64)src, b, disp);
    }

    /// <summary>
    /// CVTSI2SD - Convert signed int64 to double: xmm = (double)r64
    /// </summary>
    public static void Cvtsi2sdXmmR64(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F2 REX.W 0F 2A /r
        code.EmitByte(0xF2);
        EmitRex(ref code, true, (Reg64)dst, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMReg(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// CVTSI2SS - Convert signed int64 to float: xmm = (float)r64
    /// </summary>
    public static void Cvtsi2ssXmmR64(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F3 REX.W 0F 2A /r
        code.EmitByte(0xF3);
        EmitRex(ref code, true, (Reg64)dst, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMReg(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// CVTTSD2SI - Convert double to signed int64 (truncate): r64 = (int64)xmm
    /// </summary>
    public static void Cvttsd2siR64Xmm(ref CodeBuffer code, VReg dst, RegXMM src)
    {
        var d = Map(dst);
        // F2 REX.W 0F 2C /r
        code.EmitByte(0xF2);
        EmitRex(ref code, true, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMReg(ref code, d, (Reg64)src);
    }

    /// <summary>
    /// CVTTSS2SI - Convert float to signed int64 (truncate): r64 = (int64)xmm
    /// </summary>
    public static void Cvttss2siR64Xmm(ref CodeBuffer code, VReg dst, RegXMM src)
    {
        var d = Map(dst);
        // F3 REX.W 0F 2C /r
        code.EmitByte(0xF3);
        EmitRex(ref code, true, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMReg(ref code, d, (Reg64)src);
    }

    /// <summary>
    /// CVTTSD2SI - Convert double to signed integer (truncate): r32/r64 = (int32/int64)xmm
    /// </summary>
    public static void Cvttsd2si(ref CodeBuffer code, VReg dst, RegXMM src, bool is64Bit)
    {
        var d = Map(dst);
        // F2 [REX.W] 0F 2C /r
        code.EmitByte(0xF2);
        EmitRex(ref code, is64Bit, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMReg(ref code, d, (Reg64)src);
    }

    /// <summary>
    /// CVTTSS2SI - Convert float to signed integer (truncate): r32/r64 = (int32/int64)xmm
    /// </summary>
    public static void Cvttss2si(ref CodeBuffer code, VReg dst, RegXMM src, bool is64Bit)
    {
        var d = Map(dst);
        // F3 [REX.W] 0F 2C /r
        code.EmitByte(0xF3);
        EmitRex(ref code, is64Bit, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMReg(ref code, d, (Reg64)src);
    }

    /// <summary>
    /// CVTSS2SD - Convert float to double: dst = (double)src
    /// </summary>
    public static void Cvtss2sdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F3 0F 5A /r
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5A);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// CVTSD2SS - Convert double to float: dst = (float)src
    /// </summary>
    public static void Cvtsd2ssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // F2 0F 5A /r
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x5A);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// UCOMISD - Compare unordered scalar double.
    /// </summary>
    public static void UcomisdXmmXmm(ref CodeBuffer code, RegXMM left, RegXMM right)
    {
        // 66 0F 2E /r
        code.EmitByte(0x66);
        if ((byte)left >= 8 || (byte)right >= 8)
        {
            byte rex = REX;
            if ((byte)left >= 8) rex |= REX_R;
            if ((byte)right >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2E);
        code.EmitByte(ModRM(0b11, (byte)left, (byte)right));
    }

    /// <summary>
    /// UCOMISS - Compare unordered scalar single.
    /// </summary>
    public static void UcomissXmmXmm(ref CodeBuffer code, RegXMM left, RegXMM right)
    {
        // 0F 2E /r
        if ((byte)left >= 8 || (byte)right >= 8)
        {
            byte rex = REX;
            if ((byte)left >= 8) rex |= REX_R;
            if ((byte)right >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2E);
        code.EmitByte(ModRM(0b11, (byte)left, (byte)right));
    }

    /// <summary>
    /// XORPS - XOR packed single (for zeroing XMM registers).
    /// </summary>
    public static void XorpsXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // 0F 57 /r
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x57);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// Shift right by immediate (logical/unsigned).
    /// </summary>
    public static void ShrImm8(ref CodeBuffer code, VReg reg, byte imm)
    {
        var r = Map(reg);
        // REX.W C1 /5 ib - SHR r64, imm8
        EmitRex(ref code, true, Reg64.RAX, r);
        code.EmitByte(0xC1);
        code.EmitByte(ModRM(0b11, 5, (byte)((byte)r & 7)));
        code.EmitByte(imm);
    }

    /// <summary>
    /// CVTSI2SS - Convert signed int32 to float: xmm = (float)r32
    /// </summary>
    public static void Cvtsi2ssXmmR32(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F3 0F 2A /r - CVTSI2SS xmm, r32
        code.EmitByte(0xF3);
        if ((byte)dst >= 8 || (byte)s >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)s >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMReg(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// CVTSI2SD - Convert signed int32 to double: xmm = (double)r32
    /// </summary>
    public static void Cvtsi2sdXmmR32(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F2 0F 2A /r - CVTSI2SD xmm, r32
        code.EmitByte(0xF2);
        if ((byte)dst >= 8 || (byte)s >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)s >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMReg(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// MOVAPS - Move aligned packed single-precision (copy XMM register).
    /// </summary>
    public static void MovapsXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // 0F 28 /r - MOVAPS xmm1, xmm2
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x28);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// MOVAPD - Move aligned packed double-precision (copy XMM register).
    /// </summary>
    public static void MovapdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src)
    {
        // 66 0F 28 /r - MOVAPD xmm1, xmm2
        code.EmitByte(0x66);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x28);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
    }

    /// <summary>
    /// ROUNDSS - Round scalar single-precision.
    /// imm8: 0=round nearest, 1=floor, 2=ceil, 3=truncate toward zero
    /// </summary>
    public static void RoundssXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src, byte imm8)
    {
        // 66 0F 3A 0A /r imm8 - ROUNDSS xmm1, xmm2, imm8
        code.EmitByte(0x66);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x3A);
        code.EmitByte(0x0A);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
        code.EmitByte(imm8);
    }

    /// <summary>
    /// ROUNDSD - Round scalar double-precision.
    /// imm8: 0=round nearest, 1=floor, 2=ceil, 3=truncate toward zero
    /// </summary>
    public static void RoundsdXmmXmm(ref CodeBuffer code, RegXMM dst, RegXMM src, byte imm8)
    {
        // 66 0F 3A 0B /r imm8 - ROUNDSD xmm1, xmm2, imm8
        code.EmitByte(0x66);
        if ((byte)dst >= 8 || (byte)src >= 8)
        {
            byte rex = REX;
            if ((byte)dst >= 8) rex |= REX_R;
            if ((byte)src >= 8) rex |= REX_B;
            code.EmitByte(rex);
        }
        code.EmitByte(0x0F);
        code.EmitByte(0x3A);
        code.EmitByte(0x0B);
        code.EmitByte(ModRM(0b11, (byte)dst, (byte)src));
        code.EmitByte(imm8);
    }
}
