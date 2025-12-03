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

    // ==================== Instance Methods (Legacy - for reference during migration) ====================
    // NOTE: These instance methods use _code field. All new code should use the static
    // ICodeEmitter interface methods instead (see section at end of file).

    private CodeBuffer _code;

    /// <summary>
    /// Create an emitter for the given code buffer (legacy instance pattern)
    /// </summary>
    public static X64Emitter Create(ref CodeBuffer code)
    {
        X64Emitter emit = default;
        emit._code = code;
        return emit;
    }

    // Instance helper methods for legacy code
    private void EmitRex(bool w, Reg64 reg, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)reg >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            _code.EmitByte(rex);
    }

    private void EmitRexSingle(bool w, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            _code.EmitByte(rex);
    }

    private void EmitModRMReg(Reg64 reg, Reg64 rm)
    {
        _code.EmitByte(ModRM(0b11, (byte)reg, (byte)rm));
    }

    private void EmitModRMMem(Reg64 reg, Reg64 baseReg)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (baseEnc == 4) // RSP/R12 needs SIB
        {
            _code.EmitByte(ModRM(0b00, (byte)reg, 0b100));
            _code.EmitByte(0x24); // SIB: scale=0, index=none, base=RSP
        }
        else if (baseEnc == 5) // RBP/R13 needs disp
        {
            _code.EmitByte(ModRM(0b01, (byte)reg, baseEnc));
            _code.EmitByte(0); // disp8 = 0
        }
        else
        {
            _code.EmitByte(ModRM(0b00, (byte)reg, baseEnc));
        }
    }

    private void EmitModRMMemDisp8(Reg64 reg, Reg64 baseReg, sbyte disp)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (baseEnc == 4) // RSP/R12 needs SIB
        {
            _code.EmitByte(ModRM(0b01, (byte)reg, 0b100));
            _code.EmitByte(0x24);
        }
        else
        {
            _code.EmitByte(ModRM(0b01, (byte)reg, baseEnc));
        }
        _code.EmitByte((byte)disp);
    }

    private void EmitModRMMemDisp32(Reg64 reg, Reg64 baseReg, int disp)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (baseEnc == 4) // RSP/R12 needs SIB
        {
            _code.EmitByte(ModRM(0b10, (byte)reg, 0b100));
            _code.EmitByte(0x24);
        }
        else
        {
            _code.EmitByte(ModRM(0b10, (byte)reg, baseEnc));
        }
        _code.EmitInt32(disp);
    }

    /// <summary>
    /// MOV reg64, reg64
    /// </summary>
    public void MovRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x89);  // MOV r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// MOV reg64, imm64
    /// </summary>
    public void MovRI64(Reg64 dst, ulong imm)
    {
        EmitRexSingle(true, dst);
        _code.EmitByte((byte)(0xB8 + ((byte)dst & 7)));  // MOV r64, imm64
        _code.EmitQword(imm);
    }

    /// <summary>
    /// MOV reg64, imm32 (sign-extended)
    /// Uses the more compact MOV r/m64, imm32 encoding
    /// </summary>
    public void MovRI32(Reg64 dst, int imm)
    {
        EmitRexSingle(true, dst);
        _code.EmitByte(0xC7);  // MOV r/m64, imm32
        EmitModRMReg(Reg64.RAX, dst);  // /0 opcode extension
        _code.EmitInt32(imm);
    }

    /// <summary>
    /// MOV reg64, [base+disp]
    /// </summary>
    public void MovRM(Reg64 dst, Reg64 baseReg, int disp)
    {
        EmitRex(true, dst, baseReg);
        _code.EmitByte(0x8B);  // MOV r64, r/m64

        if (disp == 0 && ((byte)baseReg & 7) != 5)  // RBP/R13 always needs disp
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOV [base+disp], reg64
    /// </summary>
    public void MovMR(Reg64 baseReg, int disp, Reg64 src)
    {
        EmitRex(true, src, baseReg);
        _code.EmitByte(0x89);  // MOV r/m64, r64

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(src, baseReg, disp);
    }

    // ==================== Stack Operations ====================

    /// <summary>
    /// PUSH reg64
    /// </summary>
    public void Push(Reg64 reg)
    {
        if ((byte)reg >= 8)
            _code.EmitByte((byte)(REX | REX_B));
        _code.EmitByte((byte)(0x50 + ((byte)reg & 7)));
    }

    /// <summary>
    /// POP reg64
    /// </summary>
    public void Pop(Reg64 reg)
    {
        if ((byte)reg >= 8)
            _code.EmitByte((byte)(REX | REX_B));
        _code.EmitByte((byte)(0x58 + ((byte)reg & 7)));
    }

    // ==================== Arithmetic ====================

    /// <summary>
    /// ADD reg64, reg64
    /// </summary>
    public void AddRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x01);  // ADD r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// ADD reg64, imm32 (sign-extended)
    /// </summary>
    public void AddRI(Reg64 dst, int imm)
    {
        EmitRexSingle(true, dst);
        if (imm >= -128 && imm <= 127)
        {
            _code.EmitByte(0x83);  // ADD r/m64, imm8
            EmitModRMReg(Reg64.RAX, dst);  // /0 opcode extension
            _code.EmitByte((byte)imm);
        }
        else
        {
            _code.EmitByte(0x81);  // ADD r/m64, imm32
            EmitModRMReg(Reg64.RAX, dst);  // /0 opcode extension
            _code.EmitInt32(imm);
        }
    }

    /// <summary>
    /// SUB reg64, reg64
    /// </summary>
    public void SubRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x29);  // SUB r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// SUB reg64, imm32 (sign-extended)
    /// </summary>
    public void SubRI(Reg64 dst, int imm)
    {
        EmitRexSingle(true, dst);
        if (imm >= -128 && imm <= 127)
        {
            _code.EmitByte(0x83);  // SUB r/m64, imm8
            EmitModRMReg(Reg64.RBP, dst);  // /5 opcode extension
            _code.EmitByte((byte)imm);
        }
        else
        {
            _code.EmitByte(0x81);  // SUB r/m64, imm32
            EmitModRMReg(Reg64.RBP, dst);  // /5 opcode extension
            _code.EmitInt32(imm);
        }
    }

    /// <summary>
    /// IMUL reg64, reg64
    /// </summary>
    public void ImulRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0xAF);  // IMUL r64, r/m64
        EmitModRMReg(dst, src);
    }

    /// <summary>
    /// NEG reg64
    /// </summary>
    public void Neg(Reg64 reg)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xF7);  // NEG r/m64
        EmitModRMReg(Reg64.RBX, reg);  // /3 opcode extension
    }

    /// <summary>
    /// NOT reg64 (bitwise NOT)
    /// </summary>
    public void Not(Reg64 reg)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xF7);  // NOT r/m64
        EmitModRMReg(Reg64.RDX, reg);  // /2 opcode extension
    }

    /// <summary>
    /// IDIV r/m64 - Signed divide RDX:RAX by r/m64, quotient in RAX, remainder in RDX
    /// Caller must sign-extend RAX to RDX:RAX with CQO before calling
    /// </summary>
    public void Idiv(Reg64 divisor)
    {
        EmitRexSingle(true, divisor);
        _code.EmitByte(0xF7);  // IDIV r/m64
        EmitModRMReg(Reg64.RDI, divisor);  // /7 opcode extension
    }

    /// <summary>
    /// DIV r/m64 - Unsigned divide RDX:RAX by r/m64, quotient in RAX, remainder in RDX
    /// Caller must zero RDX before calling
    /// </summary>
    public void Div(Reg64 divisor)
    {
        EmitRexSingle(true, divisor);
        _code.EmitByte(0xF7);  // DIV r/m64
        EmitModRMReg(Reg64.RSI, divisor);  // /6 opcode extension
    }

    /// <summary>
    /// CQO - Sign-extend RAX into RDX:RAX (for signed division)
    /// </summary>
    public void Cqo()
    {
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x99);  // CQO
    }

    /// <summary>
    /// SHL reg64, CL - Shift left by CL
    /// </summary>
    public void ShlCL(Reg64 reg)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xD3);  // SHL r/m64, CL
        EmitModRMReg(Reg64.RSP, reg);  // /4 opcode extension
    }

    /// <summary>
    /// SHL reg64, imm8 - Shift left by immediate
    /// </summary>
    public void ShlImm(Reg64 reg, byte imm)
    {
        EmitRexSingle(true, reg);
        if (imm == 1)
        {
            _code.EmitByte(0xD1);  // SHL r/m64, 1
            EmitModRMReg(Reg64.RSP, reg);  // /4
        }
        else
        {
            _code.EmitByte(0xC1);  // SHL r/m64, imm8
            EmitModRMReg(Reg64.RSP, reg);  // /4
            _code.EmitByte(imm);
        }
    }

    /// <summary>
    /// SAR reg64, CL - Arithmetic shift right by CL (preserves sign)
    /// </summary>
    public void SarCL(Reg64 reg)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xD3);  // SAR r/m64, CL
        EmitModRMReg(Reg64.RDI, reg);  // /7 opcode extension
    }

    /// <summary>
    /// SHR reg64, CL - Logical shift right by CL (zero-fill)
    /// </summary>
    public void ShrCL(Reg64 reg)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xD3);  // SHR r/m64, CL
        EmitModRMReg(Reg64.RBP, reg);  // /5 opcode extension
    }

    /// <summary>
    /// SHR reg64, imm8 - Logical shift right by immediate (zero-fill)
    /// </summary>
    public void ShrImm8(Reg64 reg, byte imm)
    {
        EmitRexSingle(true, reg);
        _code.EmitByte(0xC1);  // SHR r/m64, imm8
        EmitModRMReg(Reg64.RBP, reg);  // /5 opcode extension
        _code.EmitByte(imm);
    }

    /// <summary>
    /// Zero-extend 32-bit value in register to 64-bit.
    /// Uses MOV r32, r32 (without REX.W) which clears upper 32 bits.
    /// </summary>
    public void ZeroExtend32(Reg64 reg)
    {
        // For R8-R15, we need REX.R and REX.B but not REX.W
        // For RAX-RDI (0-7), no REX prefix needed
        if ((byte)reg >= 8)
        {
            // REX prefix: 0x45 = REX.R + REX.B (for extended registers as both src and dst)
            _code.EmitByte(0x45);
        }
        _code.EmitByte(0x89);  // MOV r/m32, r32
        EmitModRMReg(reg, reg);
    }

    /// <summary>
    /// XOR reg64, reg64 (useful for zeroing a register)
    /// </summary>
    public void XorRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x31);  // XOR r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// AND reg64, reg64
    /// </summary>
    public void AndRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x21);  // AND r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// AND reg64, imm32 (sign-extended)
    /// </summary>
    public void AndRI(Reg64 reg, int imm)
    {
        EmitRexSingle(true, reg);
        if (imm >= -128 && imm <= 127)
        {
            _code.EmitByte(0x83);  // AND r/m64, imm8
            EmitModRMReg(Reg64.RSP, reg);  // /4 opcode extension
            _code.EmitByte((byte)imm);
        }
        else
        {
            _code.EmitByte(0x81);  // AND r/m64, imm32
            EmitModRMReg(Reg64.RSP, reg);  // /4 opcode extension
            _code.EmitInt32(imm);
        }
    }

    /// <summary>
    /// OR reg64, reg64
    /// </summary>
    public void OrRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, src, dst);
        _code.EmitByte(0x09);  // OR r/m64, r64
        EmitModRMReg(src, dst);
    }

    /// <summary>
    /// CMP reg64, reg64
    /// </summary>
    public void CmpRR(Reg64 left, Reg64 right)
    {
        EmitRex(true, right, left);
        _code.EmitByte(0x39);  // CMP r/m64, r64
        EmitModRMReg(right, left);
    }

    /// <summary>
    /// CMP reg32, reg32 (32-bit compare for IL int32 operations)
    /// </summary>
    public void CmpRR32(Reg64 left, Reg64 right)
    {
        // No REX.W prefix = 32-bit operation
        // This is critical for signed comparisons of int32 values
        EmitRex(false, right, left);
        _code.EmitByte(0x39);  // CMP r/m32, r32
        EmitModRMReg(right, left);
    }

    /// <summary>
    /// CMP reg64, imm32
    /// </summary>
    public void CmpRI(Reg64 reg, int imm)
    {
        EmitRexSingle(true, reg);
        if (imm >= -128 && imm <= 127)
        {
            _code.EmitByte(0x83);  // CMP r/m64, imm8
            EmitModRMReg(Reg64.RDI, reg);  // /7 opcode extension
            _code.EmitByte((byte)imm);
        }
        else
        {
            _code.EmitByte(0x81);  // CMP r/m64, imm32
            EmitModRMReg(Reg64.RDI, reg);  // /7 opcode extension
            _code.EmitInt32(imm);
        }
    }

    /// <summary>
    /// TEST reg64, reg64
    /// </summary>
    public void TestRR(Reg64 left, Reg64 right)
    {
        EmitRex(true, right, left);
        _code.EmitByte(0x85);  // TEST r/m64, r64
        EmitModRMReg(right, left);
    }

    // ==================== Control Flow ====================

    /// <summary>
    /// RET
    /// </summary>
    public void Ret()
    {
        _code.EmitByte(0xC3);
    }

    /// <summary>
    /// CALL reg64
    /// </summary>
    public void CallR(Reg64 reg)
    {
        if ((byte)reg >= 8)
            _code.EmitByte((byte)(REX | REX_B));
        _code.EmitByte(0xFF);
        _code.EmitByte(ModRM(0b11, 2, (byte)reg));  // /2 opcode extension
    }

    /// <summary>
    /// CALL rel32 - returns offset of the rel32 for patching
    /// </summary>
    public int CallRel32()
    {
        _code.EmitByte(0xE8);
        int patchOffset = _code.Position;
        _code.EmitDword(0);  // Placeholder
        return patchOffset;
    }

    /// <summary>
    /// JMP rel32 - returns offset of the rel32 for patching
    /// </summary>
    public int JmpRel32()
    {
        _code.EmitByte(0xE9);
        int patchOffset = _code.Position;
        _code.EmitDword(0);  // Placeholder
        return patchOffset;
    }

    /// <summary>
    /// JMP rel8 - short jump, returns offset of the rel8 for patching
    /// </summary>
    public int JmpRel8()
    {
        _code.EmitByte(0xEB);
        int patchOffset = _code.Position;
        _code.EmitByte(0);  // Placeholder
        return patchOffset;
    }

    /// <summary>
    /// JMP r64 - indirect jump through register
    /// Encoding: FF /4 (ModRM with reg=4 and r/m=register)
    /// </summary>
    public void JmpR(Reg64 reg)
    {
        // REX prefix if needed (for R8-R15)
        if ((int)reg >= 8)
        {
            _code.EmitByte((byte)(0x40 | 0x01));  // REX.B
            _code.EmitByte(0xFF);
            _code.EmitByte((byte)(0xE0 | ((int)reg & 7)));  // ModRM: mod=11, reg=4, r/m=reg
        }
        else
        {
            _code.EmitByte(0xFF);
            _code.EmitByte((byte)(0xE0 | (int)reg));  // ModRM: mod=11, reg=4, r/m=reg
        }
    }

    /// <summary>
    /// Jcc rel32 - conditional jump, returns offset of the rel32 for patching
    /// </summary>
    public int JccRel32(byte cc)
    {
        _code.EmitByte(0x0F);
        _code.EmitByte((byte)(0x80 + cc));
        int patchOffset = _code.Position;
        _code.EmitDword(0);  // Placeholder
        return patchOffset;
    }

    // Condition codes for Jcc
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

    /// <summary>
    /// JE/JZ rel32
    /// </summary>
    public int Je() => JccRel32(CC_E);

    /// <summary>
    /// JNE/JNZ rel32
    /// </summary>
    public int Jne() => JccRel32(CC_NE);

    /// <summary>
    /// JL rel32 (signed less than)
    /// </summary>
    public int Jl() => JccRel32(CC_L);

    /// <summary>
    /// JLE rel32 (signed less than or equal)
    /// </summary>
    public int Jle() => JccRel32(CC_LE);

    /// <summary>
    /// JG rel32 (signed greater than)
    /// </summary>
    public int Jg() => JccRel32(CC_G);

    /// <summary>
    /// JGE rel32 (signed greater than or equal)
    /// </summary>
    public int Jge() => JccRel32(CC_GE);

    // ==================== Miscellaneous ====================

    /// <summary>
    /// NOP
    /// </summary>
    public void Nop()
    {
        _code.EmitByte(0x90);
    }

    /// <summary>
    /// INT3 (breakpoint)
    /// </summary>
    public void Int3()
    {
        _code.EmitByte(0xCC);
    }

    /// <summary>
    /// MOVSXD reg64, reg32 - Sign-extend 32-bit register to 64-bit
    /// </summary>
    public void MovsxdRR(Reg64 dst, Reg64 src)
    {
        EmitRex(true, dst, src);
        _code.EmitByte(0x63);  // MOVSXD r64, r/m32
        EmitModRMReg(dst, src);
    }

    /// <summary>
    /// MOVSXD reg64, [base+disp] - Load 32-bit from memory and sign-extend to 64-bit
    /// </summary>
    public void MovsxdRM(Reg64 dst, Reg64 baseReg, int disp)
    {
        EmitRex(true, dst, baseReg);
        _code.EmitByte(0x63);  // MOVSXD r64, r/m32

        if (disp == 0 && ((byte)baseReg & 7) != 5)  // RBP/R13 always needs disp
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    // ==================== Sized Memory Operations (for ldind/stind) ====================

    /// <summary>
    /// MOVSX r64, byte [base+disp] - Sign-extend byte from memory to 64-bit
    /// Opcode: 0F BE /r (MOVSX r32, r/m8 with REX.W for 64-bit dest)
    /// </summary>
    public void MovsxByte(Reg64 dst, Reg64 baseReg, int disp)
    {
        EmitRex(true, dst, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0xBE);  // MOVSX r, r/m8

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOVZX r64, byte [base+disp] - Zero-extend byte from memory to 64-bit
    /// Opcode: 0F B6 /r (MOVZX r32, r/m8 - upper 32 bits cleared automatically)
    /// </summary>
    public void MovzxByte(Reg64 dst, Reg64 baseReg, int disp)
    {
        // No REX.W needed - MOVZX to 32-bit clears upper 32 bits
        // But we do need REX if registers are extended (R8-R15)
        byte rex = REX;
        if ((byte)dst >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        if (rex != REX)
            _code.EmitByte(rex);

        _code.EmitByte(0x0F);
        _code.EmitByte(0xB6);  // MOVZX r32, r/m8

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOVSX r64, word [base+disp] - Sign-extend word from memory to 64-bit
    /// Opcode: 0F BF /r (MOVSX r32, r/m16 with REX.W for 64-bit dest)
    /// </summary>
    public void MovsxWord(Reg64 dst, Reg64 baseReg, int disp)
    {
        EmitRex(true, dst, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0xBF);  // MOVSX r, r/m16

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOVZX r64, word [base+disp] - Zero-extend word from memory to 64-bit
    /// Opcode: 0F B7 /r (MOVZX r32, r/m16 - upper 32 bits cleared automatically)
    /// </summary>
    public void MovzxWord(Reg64 dst, Reg64 baseReg, int disp)
    {
        // No REX.W needed - MOVZX to 32-bit clears upper 32 bits
        byte rex = REX;
        if ((byte)dst >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        if (rex != REX)
            _code.EmitByte(rex);

        _code.EmitByte(0x0F);
        _code.EmitByte(0xB7);  // MOVZX r32, r/m16

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOV r32, [base+disp] - Load 32-bit value (zero-extended to 64-bit)
    /// No REX.W prefix means 32-bit operation, which clears upper 32 bits
    /// </summary>
    public void MovRM32(Reg64 dst, Reg64 baseReg, int disp)
    {
        // No REX.W - 32-bit operation
        byte rex = REX;
        if ((byte)dst >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        if (rex != REX)
            _code.EmitByte(rex);

        _code.EmitByte(0x8B);  // MOV r32, r/m32

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    /// <summary>
    /// MOV byte [base+disp], r8 - Store low byte of register to memory
    /// </summary>
    public void MovMR8(Reg64 baseReg, int disp, Reg64 src)
    {
        // For registers RSP, RBP, RSI, RDI as source, we need REX prefix
        // to access the low byte (SPL, BPL, SIL, DIL instead of AH, CH, DH, BH)
        byte rex = REX;
        if ((byte)src >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        // Force REX for SPL/BPL/SIL/DIL access (regs 4-7 low byte)
        if ((byte)src >= 4 && (byte)src <= 7)
            rex |= 0;  // Just having REX prefix is enough

        if (rex != REX || ((byte)src >= 4 && (byte)src <= 7))
            _code.EmitByte(rex);

        _code.EmitByte(0x88);  // MOV r/m8, r8

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(src, baseReg, disp);
    }

    /// <summary>
    /// MOV word [base+disp], r16 - Store low word of register to memory
    /// </summary>
    public void MovMR16(Reg64 baseReg, int disp, Reg64 src)
    {
        _code.EmitByte(0x66);  // Operand size prefix for 16-bit

        byte rex = REX;
        if ((byte)src >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        if (rex != REX)
            _code.EmitByte(rex);

        _code.EmitByte(0x89);  // MOV r/m16, r16

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(src, baseReg, disp);
    }

    /// <summary>
    /// MOV dword [base+disp], r32 - Store low dword of register to memory
    /// </summary>
    public void MovMR32(Reg64 baseReg, int disp, Reg64 src)
    {
        // No REX.W - 32-bit operation
        byte rex = REX;
        if ((byte)src >= 8) rex |= REX_R;
        if ((byte)baseReg >= 8) rex |= REX_B;
        if (rex != REX)
            _code.EmitByte(rex);

        _code.EmitByte(0x89);  // MOV r/m32, r32

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(src, baseReg, disp);
    }

    /// <summary>
    /// LEA reg64, [base+disp]
    /// </summary>
    public void Lea(Reg64 dst, Reg64 baseReg, int disp)
    {
        EmitRex(true, dst, baseReg);
        _code.EmitByte(0x8D);  // LEA r64, m

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem(dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8(dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32(dst, baseReg, disp);
    }

    // ==================== Method Prologue/Epilogue ====================

    /// <summary>
    /// Microsoft x64 ABI:
    /// - Args: RCX, RDX, R8, R9, then stack (right to left)
    /// - Return: RAX (int/ptr), XMM0 (float/double)
    /// - Caller-saved: RAX, RCX, RDX, R8-R11, XMM0-XMM5
    /// - Callee-saved: RBX, RBP, RDI, RSI, R12-R15, XMM6-XMM15
    /// - Stack aligned to 16 bytes at CALL instruction
    /// - 32-byte shadow space for first 4 args
    /// </summary>

    /// <summary>
    /// Emit standard method prologue.
    /// Sets up stack frame with specified local space.
    /// Returns the size of stack adjustment for epilogue.
    /// </summary>
    /// <param name="localBytes">Space needed for local variables</param>
    /// <param name="saveCalleeSaved">Save RBX, RBP, RDI, RSI</param>
    /// <returns>Total stack frame size (for epilogue)</returns>
    public int EmitPrologue(int localBytes, bool saveCalleeSaved = false)
    {
        // push rbp
        Push(Reg64.RBP);

        // mov rbp, rsp
        MovRR(Reg64.RBP, Reg64.RSP);

        int frameSize = localBytes;

        if (saveCalleeSaved)
        {
            // Save callee-saved registers
            Push(Reg64.RBX);
            Push(Reg64.RDI);
            Push(Reg64.RSI);
            Push(Reg64.R12);
            Push(Reg64.R13);
            Push(Reg64.R14);
            Push(Reg64.R15);
            frameSize += 7 * 8;  // 7 registers * 8 bytes
        }

        // Align stack to 16 bytes
        // After push rbp, RSP is 8-aligned (return addr + push = 16 bytes)
        // We need total frame size to maintain 16-byte alignment
        // Round up localBytes to multiple of 16
        int alignedLocals = (localBytes + 15) & ~15;

        // Allocate locals + shadow space (32 bytes for outgoing calls)
        int totalAlloc = alignedLocals + 32;

        if (totalAlloc > 0)
        {
            SubRI(Reg64.RSP, totalAlloc);
        }

        return totalAlloc;
    }

    /// <summary>
    /// Emit minimal prologue (no frame pointer, just stack adjustment)
    /// Use for leaf functions with small local space.
    /// </summary>
    /// <param name="stackAdjust">Bytes to allocate on stack (must be 16-aligned)</param>
    public void EmitLeafPrologue(int stackAdjust)
    {
        if (stackAdjust > 0)
        {
            SubRI(Reg64.RSP, stackAdjust);
        }
    }

    /// <summary>
    /// Emit standard method epilogue.
    /// Restores frame and returns.
    /// </summary>
    /// <param name="stackAdjust">Stack adjustment from prologue</param>
    /// <param name="savedCalleeSaved">Did prologue save callee-saved registers</param>
    public void EmitEpilogue(int stackAdjust, bool savedCalleeSaved = false)
    {
        if (stackAdjust > 0)
        {
            AddRI(Reg64.RSP, stackAdjust);
        }

        if (savedCalleeSaved)
        {
            // Restore in reverse order
            Pop(Reg64.R15);
            Pop(Reg64.R14);
            Pop(Reg64.R13);
            Pop(Reg64.R12);
            Pop(Reg64.RSI);
            Pop(Reg64.RDI);
            Pop(Reg64.RBX);
        }

        // mov rsp, rbp
        MovRR(Reg64.RSP, Reg64.RBP);

        // pop rbp
        Pop(Reg64.RBP);

        // ret
        Ret();
    }

    /// <summary>
    /// Emit minimal epilogue for leaf functions.
    /// </summary>
    public void EmitLeafEpilogue(int stackAdjust)
    {
        if (stackAdjust > 0)
        {
            AddRI(Reg64.RSP, stackAdjust);
        }
        Ret();
    }

    /// <summary>
    /// Home (spill) argument registers to shadow space.
    /// Call after prologue if you need to access arguments from stack.
    /// Shadow space is at [rbp+16], [rbp+24], [rbp+32], [rbp+40] for args 0-3.
    /// </summary>
    public void HomeArguments(int argCount)
    {
        // Shadow space is above return address and saved rbp
        // [rbp+0]  = saved rbp
        // [rbp+8]  = return address
        // [rbp+16] = shadow space for arg0 (RCX)
        // [rbp+24] = shadow space for arg1 (RDX)
        // [rbp+32] = shadow space for arg2 (R8)
        // [rbp+40] = shadow space for arg3 (R9)
        if (argCount > 0) MovMR(Reg64.RBP, 16, Reg64.RCX);
        if (argCount > 1) MovMR(Reg64.RBP, 24, Reg64.RDX);
        if (argCount > 2) MovMR(Reg64.RBP, 32, Reg64.R8);
        if (argCount > 3) MovMR(Reg64.RBP, 40, Reg64.R9);
    }

    /// <summary>
    /// Load argument from its home location (stack) as 64-bit.
    /// Works for both register args (in shadow space) and stack args.
    /// </summary>
    /// <param name="dst">Destination register</param>
    /// <param name="argIndex">0-based argument index</param>
    public void LoadArgFromHome(Reg64 dst, int argIndex)
    {
        // Args 0-3 are in shadow space, args 4+ are on stack
        int offset = 16 + argIndex * 8;  // [rbp+16+argIndex*8]
        MovRM(dst, Reg64.RBP, offset);
    }

    /// <summary>
    /// Load 32-bit argument from its home location and sign-extend to 64-bit.
    /// Use this for int/int32 arguments to properly handle negative values.
    /// </summary>
    /// <param name="dst">Destination register</param>
    /// <param name="argIndex">0-based argument index</param>
    public void LoadArgFromHomeI32(Reg64 dst, int argIndex)
    {
        // Args 0-3 are in shadow space, args 4+ are on stack
        int offset = 16 + argIndex * 8;  // [rbp+16+argIndex*8]
        MovsxdRM(dst, Reg64.RBP, offset);
    }

    /// <summary>
    /// Get the register for argument by index (Microsoft x64 ABI).
    /// Returns the register that holds the argument at function entry.
    /// </summary>
    public static Reg64 GetArgRegister(int argIndex)
    {
        return argIndex switch
        {
            0 => Reg64.RCX,
            1 => Reg64.RDX,
            2 => Reg64.R8,
            3 => Reg64.R9,
            _ => Reg64.RAX  // Error: args 4+ are on stack
        };
    }

    /// <summary>
    /// Get the offset from RBP for a local variable.
    /// Locals are below RBP (negative offsets).
    /// </summary>
    /// <param name="localIndex">0-based local index</param>
    /// <param name="localSize">Size of each local (typically 8 for 64-bit)</param>
    public static int GetLocalOffset(int localIndex, int localSize = 8)
    {
        // Locals are at [rbp-8], [rbp-16], etc.
        return -(localIndex + 1) * localSize;
    }

    // ==================== SSE Instructions ====================

    /// <summary>
    /// Emit REX prefix for XMM operations if needed
    /// </summary>
    private void EmitRexXmm(bool w, RegXMM reg, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)reg >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;

        if (rex != REX || w)
            _code.EmitByte(rex);
    }

    /// <summary>
    /// Emit REX prefix for XMM to XMM operations if needed
    /// </summary>
    private void EmitRexXmmXmm(RegXMM reg, RegXMM rm)
    {
        byte rex = REX;
        if ((byte)reg >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;

        if (rex != REX)
            _code.EmitByte(rex);
    }

    /// <summary>
    /// MOVSS xmm, [base+disp] - Load single-precision float from memory
    /// </summary>
    public void MovssRM(RegXMM dst, Reg64 baseReg, int disp)
    {
        _code.EmitByte(0xF3);  // SSE prefix for scalar single
        EmitRexXmm(false, dst, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x10);  // MOVSS xmm, xmm/m32

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem((Reg64)dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8((Reg64)dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32((Reg64)dst, baseReg, disp);
    }

    /// <summary>
    /// MOVSS [base+disp], xmm - Store single-precision float to memory
    /// </summary>
    public void MovssMR(Reg64 baseReg, int disp, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix for scalar single
        EmitRexXmm(false, src, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x11);  // MOVSS xmm/m32, xmm

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem((Reg64)src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8((Reg64)src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32((Reg64)src, baseReg, disp);
    }

    /// <summary>
    /// MOVSD xmm, [base+disp] - Load double-precision float from memory
    /// </summary>
    public void MovsdRM(RegXMM dst, Reg64 baseReg, int disp)
    {
        _code.EmitByte(0xF2);  // SSE prefix for scalar double
        EmitRexXmm(false, dst, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x10);  // MOVSD xmm, xmm/m64

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem((Reg64)dst, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8((Reg64)dst, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32((Reg64)dst, baseReg, disp);
    }

    /// <summary>
    /// MOVSD [base+disp], xmm - Store double-precision float to memory
    /// </summary>
    public void MovsdMR(Reg64 baseReg, int disp, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE prefix for scalar double
        EmitRexXmm(false, src, baseReg);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x11);  // MOVSD xmm/m64, xmm

        if (disp == 0 && ((byte)baseReg & 7) != 5)
            EmitModRMMem((Reg64)src, baseReg);
        else if (disp >= -128 && disp <= 127)
            EmitModRMMemDisp8((Reg64)src, baseReg, (sbyte)disp);
        else
            EmitModRMMemDisp32((Reg64)src, baseReg, disp);
    }

    /// <summary>
    /// MOVD xmm, r32 - Move 32-bit integer to XMM (zero-extended to 128-bit)
    /// </summary>
    public void MovdXmmR32(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0x66);  // Operand size prefix
        EmitRexXmm(false, dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x6E);  // MOVD xmm, r/m32
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// MOVD r32, xmm - Move low 32-bits of XMM to integer register
    /// </summary>
    public void MovdR32Xmm(Reg64 dst, RegXMM src)
    {
        _code.EmitByte(0x66);  // Operand size prefix
        EmitRexXmm(false, src, dst);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x7E);  // MOVD r/m32, xmm
        EmitModRMReg((Reg64)src, dst);
    }

    /// <summary>
    /// MOVQ xmm, r64 - Move 64-bit integer to XMM (zero-extended to 128-bit)
    /// </summary>
    public void MovqXmmR64(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0x66);  // Operand size prefix
        EmitRexXmm(true, dst, src);  // REX.W for 64-bit
        _code.EmitByte(0x0F);
        _code.EmitByte(0x6E);  // MOVQ xmm, r/m64
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// MOVQ r64, xmm - Move low 64-bits of XMM to integer register
    /// </summary>
    public void MovqR64Xmm(Reg64 dst, RegXMM src)
    {
        _code.EmitByte(0x66);  // Operand size prefix
        EmitRexXmm(true, src, dst);  // REX.W for 64-bit
        _code.EmitByte(0x0F);
        _code.EmitByte(0x7E);  // MOVQ r/m64, xmm
        EmitModRMReg((Reg64)src, dst);
    }

    /// <summary>
    /// CVTSI2SS xmm, r64 - Convert 64-bit signed integer to single-precision float
    /// </summary>
    public void Cvtsi2ssXmmR64(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmm(true, dst, src);  // REX.W for 64-bit source
        _code.EmitByte(0x0F);
        _code.EmitByte(0x2A);  // CVTSI2SS xmm, r/m64
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// CVTSI2SS xmm, r32 - Convert 32-bit signed integer to single-precision float
    /// Use this for IL int32 values to properly handle negative numbers
    /// </summary>
    public void Cvtsi2ssXmmR32(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmm(false, dst, src);  // No REX.W = 32-bit source
        _code.EmitByte(0x0F);
        _code.EmitByte(0x2A);  // CVTSI2SS xmm, r/m32
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// CVTSI2SD xmm, r64 - Convert 64-bit signed integer to double-precision float
    /// </summary>
    public void Cvtsi2sdXmmR64(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmm(true, dst, src);  // REX.W for 64-bit source
        _code.EmitByte(0x0F);
        _code.EmitByte(0x2A);  // CVTSI2SD xmm, r/m64
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// CVTSI2SD xmm, r32 - Convert 32-bit signed integer to double-precision float
    /// Use this for IL int32 values to properly handle negative numbers
    /// </summary>
    public void Cvtsi2sdXmmR32(RegXMM dst, Reg64 src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmm(false, dst, src);  // No REX.W = 32-bit source
        _code.EmitByte(0x0F);
        _code.EmitByte(0x2A);  // CVTSI2SD xmm, r/m32
        EmitModRMReg((Reg64)dst, src);
    }

    /// <summary>
    /// CVTSS2SD xmm, xmm - Convert single to double precision
    /// </summary>
    public void Cvtss2sdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5A);  // CVTSS2SD xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// CVTSD2SS xmm, xmm - Convert double to single precision
    /// </summary>
    public void Cvtsd2ssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5A);  // CVTSD2SS xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    // ==================== SSE Arithmetic ====================

    /// <summary>
    /// ADDSS xmm, xmm - Add scalar single-precision
    /// </summary>
    public void AddssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x58);  // ADDSS xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// ADDSD xmm, xmm - Add scalar double-precision
    /// </summary>
    public void AddsdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x58);  // ADDSD xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// SUBSS xmm, xmm - Subtract scalar single-precision
    /// </summary>
    public void SubssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5C);  // SUBSS xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// SUBSD xmm, xmm - Subtract scalar double-precision
    /// </summary>
    public void SubsdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5C);  // SUBSD xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// MULSS xmm, xmm - Multiply scalar single-precision
    /// </summary>
    public void MulssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x59);  // MULSS xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// MULSD xmm, xmm - Multiply scalar double-precision
    /// </summary>
    public void MulsdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x59);  // MULSD xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// DIVSS xmm, xmm - Divide scalar single-precision
    /// </summary>
    public void DivssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5E);  // DIVSS xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// DIVSD xmm, xmm - Divide scalar double-precision
    /// </summary>
    public void DivsdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x5E);  // DIVSD xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// MOVSS xmm, xmm - Move scalar single-precision (register to register)
    /// </summary>
    public void MovssXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF3);  // SSE prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x10);  // MOVSS xmm, xmm/m32
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    /// <summary>
    /// MOVSD xmm, xmm - Move scalar double-precision (register to register)
    /// </summary>
    public void MovsdXmmXmm(RegXMM dst, RegXMM src)
    {
        _code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmXmm(dst, src);
        _code.EmitByte(0x0F);
        _code.EmitByte(0x10);  // MOVSD xmm, xmm/m64
        EmitModRMReg((Reg64)dst, (Reg64)src);
    }

    // ==================== Patch Helpers ====================

    /// <summary>
    /// Patch a rel32 offset to jump to the current position
    /// </summary>
    public void PatchRel32(int patchOffset)
    {
        _code.PatchRelative32(patchOffset);
    }

    /// <summary>
    /// Patch a rel8 offset to jump to the current position
    /// </summary>
    public void PatchRel8(int patchOffset)
    {
        int rel = _code.Position - (patchOffset + 1);
        if (rel < -128 || rel > 127)
        {
            // Error: rel8 out of range - caller should use rel32
            return;
        }
        // Direct byte patch
        _code.Code[patchOffset] = (byte)rel;
    }

    // ==================== ICodeEmitter Static Interface Implementation ====================
    // These static methods implement the architecture-neutral interface.
    // They use static helper methods that directly emit to the CodeBuffer.

    // Static versions of the helper methods for use by interface implementation
    private static void EmitRexS(ref CodeBuffer code, bool w, Reg64 reg, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)reg >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitRexSingleS(ref CodeBuffer code, bool w, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitModRMRegS(ref CodeBuffer code, Reg64 reg, Reg64 rm)
    {
        code.EmitByte(ModRM(0b11, (byte)reg, (byte)rm));
    }

    private static void EmitModRMMemS(ref CodeBuffer code, Reg64 reg, Reg64 baseReg)
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

    private static void EmitModRMMemDispS(ref CodeBuffer code, Reg64 reg, Reg64 baseReg, int disp)
    {
        byte baseEnc = (byte)((byte)baseReg & 7);
        if (disp == 0 && baseEnc != 5) // RBP/R13 always need disp
        {
            EmitModRMMemS(ref code, reg, baseReg);
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
    public static int EmitPrologue(ref CodeBuffer code, int localBytes)
    {
        // push rbp
        code.EmitByte(0x55);
        // mov rbp, rsp
        code.EmitByte(0x48);
        code.EmitByte(0x89);
        code.EmitByte(0xE5);

        // Align stack to 16 bytes (include saved rbp)
        int frameSize = localBytes + ShadowSpaceSize;
        frameSize = (frameSize + 15) & ~15;

        if (frameSize > 0)
        {
            // sub rsp, frameSize
            code.EmitByte(0x48);
            code.EmitByte(0x81);
            code.EmitByte(0xEC);
            code.EmitInt32(frameSize);
        }
        return frameSize;
    }

    public static void EmitEpilogue(ref CodeBuffer code, int stackAdjust)
    {
        // Use leave instruction: mov rsp, rbp; pop rbp
        // This properly restores RSP regardless of any modifications during the function
        // (e.g., localloc, push/pop imbalances, etc.)
        // The stackAdjust parameter is no longer needed when using frame pointer epilogue
        _ = stackAdjust; // Unused - we restore from RBP instead
        code.EmitByte(0xC9);  // leave
        // ret
        code.EmitByte(0xC3);
    }

    public static void HomeArguments(ref CodeBuffer code, int argCount)
    {
        // Home register arguments to shadow space
        // RCX -> [rbp+16], RDX -> [rbp+24], R8 -> [rbp+32], R9 -> [rbp+40]
        if (argCount > 0)
        {
            // mov [rbp+16], rcx
            code.EmitByte(0x48);
            code.EmitByte(0x89);
            code.EmitByte(0x4D);
            code.EmitByte(0x10);
        }
        if (argCount > 1)
        {
            // mov [rbp+24], rdx
            code.EmitByte(0x48);
            code.EmitByte(0x89);
            code.EmitByte(0x55);
            code.EmitByte(0x18);
        }
        if (argCount > 2)
        {
            // mov [rbp+32], r8
            code.EmitByte(0x4C);
            code.EmitByte(0x89);
            code.EmitByte(0x45);
            code.EmitByte(0x20);
        }
        if (argCount > 3)
        {
            // mov [rbp+40], r9
            code.EmitByte(0x4C);
            code.EmitByte(0x89);
            code.EmitByte(0x4D);
            code.EmitByte(0x28);
        }
    }

    // === Register Operations ===
    public static void MovRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x89);  // MOV r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void MovRI64(ref CodeBuffer code, VReg dst, ulong imm)
    {
        var d = Map(dst);
        EmitRexSingleS(ref code, true, d);
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
        EmitModRMRegS(ref code, r, r);
    }

    // === Memory Operations ===
    public static void Load64(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRexS(ref code, true, d, b);
        code.EmitByte(0x8B);  // MOV r64, r/m64
        EmitModRMMemDispS(ref code, d, b, disp);
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
        EmitModRMMemDispS(ref code, d, b, disp);
    }

    public static void Load32Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRexS(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x63);  // MOVSXD r64, r/m32
        EmitModRMMemDispS(ref code, d, b, disp);
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
        EmitModRMMemDispS(ref code, d, b, disp);
    }

    public static void Load16Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRexS(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBF);  // MOVSX r64, r/m16
        EmitModRMMemDispS(ref code, d, b, disp);
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
        EmitModRMMemDispS(ref code, d, b, disp);
    }

    public static void Load8Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRexS(ref code, true, d, b);  // REX.W for 64-bit destination
        code.EmitByte(0x0F);
        code.EmitByte(0xBE);  // MOVSX r64, r/m8
        EmitModRMMemDispS(ref code, d, b, disp);
    }

    public static void Store64(ref CodeBuffer code, VReg baseReg, int disp, VReg src)
    {
        var b = Map(baseReg);
        var s = Map(src);
        EmitRexS(ref code, true, s, b);
        code.EmitByte(0x89);  // MOV r/m64, r64
        EmitModRMMemDispS(ref code, s, b, disp);
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
        EmitModRMMemDispS(ref code, s, b, disp);
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
        EmitModRMMemDispS(ref code, s, b, disp);
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
        EmitModRMMemDispS(ref code, s, b, disp);
    }

    public static void LoadAddress(ref CodeBuffer code, VReg dst, VReg baseReg, int disp)
    {
        var d = Map(dst);
        var b = Map(baseReg);
        EmitRexS(ref code, true, d, b);
        code.EmitByte(0x8D);  // LEA r64, m
        EmitModRMMemDispS(ref code, d, b, disp);
    }

    // === Arithmetic ===
    public static void Add(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x01);  // ADD r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void AddImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingleS(ref code, true, d);
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
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x29);  // SUB r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void SubImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingleS(ref code, true, d);
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
        EmitRexS(ref code, true, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0xAF);  // IMUL r64, r/m64
        EmitModRMRegS(ref code, d, s);
    }

    public static void DivSigned(ref CodeBuffer code, VReg divisor)
    {
        var r = Map(divisor);
        EmitRexSingleS(ref code, true, r);
        code.EmitByte(0xF7);  // IDIV r/m64
        code.EmitByte(ModRM(0b11, 7, (byte)r));
    }

    public static void DivUnsigned(ref CodeBuffer code, VReg divisor)
    {
        var r = Map(divisor);
        EmitRexSingleS(ref code, true, r);
        code.EmitByte(0xF7);  // DIV r/m64
        code.EmitByte(ModRM(0b11, 6, (byte)r));
    }

    public static void Neg(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        EmitRexSingleS(ref code, true, r);
        code.EmitByte(0xF7);  // NEG r/m64
        code.EmitByte(ModRM(0b11, 3, (byte)r));
    }

    // === Bitwise Operations ===
    public static void And(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x21);  // AND r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void AndImm(ref CodeBuffer code, VReg dst, int imm)
    {
        var d = Map(dst);
        EmitRexSingleS(ref code, true, d);
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
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x09);  // OR r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void Xor(ref CodeBuffer code, VReg dst, VReg src)
    {
        var d = Map(dst);
        var s = Map(src);
        EmitRexS(ref code, true, s, d);
        code.EmitByte(0x31);  // XOR r/m64, r64
        EmitModRMRegS(ref code, s, d);
    }

    public static void Not(ref CodeBuffer code, VReg reg)
    {
        var r = Map(reg);
        EmitRexSingleS(ref code, true, r);
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
            EmitRexS(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);  // MOV
            EmitModRMRegS(ref code, s, Reg64.RCX);
        }
        EmitRexSingleS(ref code, true, v);
        code.EmitByte(0xD3);  // SHL r/m64, CL
        code.EmitByte(ModRM(0b11, 4, (byte)v));
    }

    public static void ShiftLeftImm(ref CodeBuffer code, VReg value, byte imm)
    {
        var v = Map(value);
        EmitRexSingleS(ref code, true, v);
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
            EmitRexS(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);
            EmitModRMRegS(ref code, s, Reg64.RCX);
        }
        EmitRexSingleS(ref code, true, v);
        code.EmitByte(0xD3);  // SAR r/m64, CL
        code.EmitByte(ModRM(0b11, 7, (byte)v));
    }

    public static void ShiftRightUnsigned(ref CodeBuffer code, VReg value, VReg shiftAmount)
    {
        var v = Map(value);
        var s = Map(shiftAmount);
        if (s != Reg64.RCX)
        {
            EmitRexS(ref code, true, Reg64.RCX, s);
            code.EmitByte(0x89);
            EmitModRMRegS(ref code, s, Reg64.RCX);
        }
        EmitRexSingleS(ref code, true, v);
        code.EmitByte(0xD3);  // SHR r/m64, CL
        code.EmitByte(ModRM(0b11, 5, (byte)v));
    }

    // === Comparison ===
    public static void Compare(ref CodeBuffer code, VReg left, VReg right)
    {
        var l = Map(left);
        var r = Map(right);
        EmitRexS(ref code, true, r, l);
        code.EmitByte(0x39);  // CMP r/m64, r64
        EmitModRMRegS(ref code, r, l);
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
        EmitModRMRegS(ref code, r, l);
    }

    public static void CompareImm(ref CodeBuffer code, VReg reg, int imm)
    {
        var r = Map(reg);
        EmitRexSingleS(ref code, true, r);
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
        EmitRexS(ref code, true, r, l);
        code.EmitByte(0x85);  // TEST r/m64, r64
        EmitModRMRegS(ref code, r, l);
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
        // Locals are at [rbp-8], [rbp-16], etc.
        return GetLocalOffset(localIndex, 8);
    }

    public static void LoadArg(ref CodeBuffer code, VReg dst, int argIndex)
    {
        // Load from homed location: [rbp+16+argIndex*8]
        int offset = GetArgOffset(argIndex);
        Load64(ref code, dst, VReg.FP, offset);
    }

    public static void LoadLocal(ref CodeBuffer code, VReg dst, int localIndex)
    {
        int offset = GetLocalOffset(localIndex, 8);
        Load64(ref code, dst, VReg.FP, offset);
    }

    public static void StoreLocal(ref CodeBuffer code, int localIndex, VReg src)
    {
        int offset = GetLocalOffset(localIndex, 8);
        Store64(ref code, VReg.FP, offset, src);
    }

    // === Floating Point ===
    // Static helper for XMM REX prefix
    private static void EmitRexXmmS(ref CodeBuffer code, bool w, RegXMM xmm, Reg64 rm)
    {
        byte rex = REX;
        if (w) rex |= REX_W;
        if ((byte)xmm >= 8) rex |= REX_R;
        if ((byte)rm >= 8) rex |= REX_B;
        if (rex != REX || w)
            code.EmitByte(rex);
    }

    private static void EmitRexXmmXmmS(ref CodeBuffer code, RegXMM dst, RegXMM src)
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
        EmitRexXmmS(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSS xmm, m32
        EmitModRMMemDispS(ref code, (Reg64)xmm, b, disp);
    }

    public static void LoadFloat64(ref CodeBuffer code, VRegF dst, VReg baseReg, int disp)
    {
        var xmm = MapF(dst);
        var b = Map(baseReg);
        code.EmitByte(0xF2);  // SSE2 prefix
        EmitRexXmmS(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSD xmm, m64
        EmitModRMMemDispS(ref code, (Reg64)xmm, b, disp);
    }

    public static void StoreFloat32(ref CodeBuffer code, VReg baseReg, int disp, VRegF src)
    {
        var xmm = MapF(src);
        var b = Map(baseReg);
        code.EmitByte(0xF3);
        EmitRexXmmS(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x11);  // MOVSS m32, xmm
        EmitModRMMemDispS(ref code, (Reg64)xmm, b, disp);
    }

    public static void StoreFloat64(ref CodeBuffer code, VReg baseReg, int disp, VRegF src)
    {
        var xmm = MapF(src);
        var b = Map(baseReg);
        code.EmitByte(0xF2);
        EmitRexXmmS(ref code, false, xmm, b);
        code.EmitByte(0x0F);
        code.EmitByte(0x11);  // MOVSD m64, xmm
        EmitModRMMemDispS(ref code, (Reg64)xmm, b, disp);
    }

    public static void MovFF(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x10);  // MOVSS/MOVSD xmm, xmm
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
    }

    public static void AddFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x58);  // ADDSS/ADDSD
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
    }

    public static void SubFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5C);  // SUBSS/SUBSD
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
    }

    public static void MulFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x59);  // MULSS/MULSD
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
    }

    public static void DivFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(isDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5E);  // DIVSS/DIVSD
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
    }

    // === Conversions ===
    public static void ConvertInt32ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble)
    {
        var xmm = MapF(dst);
        var r = Map(src);
        code.EmitByte(toDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmS(ref code, false, xmm, r);  // No REX.W = 32-bit source
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);  // CVTSI2SS/CVTSI2SD
        EmitModRMRegS(ref code, (Reg64)xmm, r);
    }

    public static void ConvertInt64ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble)
    {
        var xmm = MapF(dst);
        var r = Map(src);
        code.EmitByte(toDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmS(ref code, true, xmm, r);  // REX.W for 64-bit source
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);  // CVTSI2SS/CVTSI2SD
        EmitModRMRegS(ref code, (Reg64)xmm, r);
    }

    public static void ConvertFloatToInt64(ref CodeBuffer code, VReg dst, VRegF src, bool fromDouble)
    {
        var r = Map(dst);
        var xmm = MapF(src);
        code.EmitByte(fromDouble ? (byte)0xF2 : (byte)0xF3);
        EmitRexXmmS(ref code, true, (RegXMM)r, (Reg64)xmm);  // REX.W for 64-bit dest
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);  // CVTTSS2SI/CVTTSD2SI (truncate)
        EmitModRMRegS(ref code, r, (Reg64)xmm);
    }

    public static void ConvertFloatPrecision(ref CodeBuffer code, VRegF dst, VRegF src, bool toDouble)
    {
        var d = MapF(dst);
        var s = MapF(src);
        code.EmitByte(toDouble ? (byte)0xF3 : (byte)0xF2);  // F3 for CVTSS2SD, F2 for CVTSD2SS
        EmitRexXmmXmmS(ref code, d, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x5A);  // CVTSS2SD or CVTSD2SS
        EmitModRMRegS(ref code, (Reg64)d, (Reg64)s);
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
        EmitModRMRegS(ref code, r, r);
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
    /// Add register to register: dst = dst + src
    /// </summary>
    public static void AddRR(ref CodeBuffer code, VReg dst, VReg src)
    {
        Add(ref code, dst, src);
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
        EmitRexS(ref code, true, (Reg64)xmm, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x6E);
        EmitModRMRegS(ref code, (Reg64)xmm, s);
    }

    /// <summary>
    /// Move qword from XMM to GPR: r64 = xmm (bits)
    /// </summary>
    public static void MovqR64Xmm(ref CodeBuffer code, VReg dst, RegXMM xmm)
    {
        var d = Map(dst);
        // 66 REX.W 0F 7E /r - MOVQ r/m64, xmm
        code.EmitByte(0x66);
        EmitRexS(ref code, true, (Reg64)xmm, d);
        code.EmitByte(0x0F);
        code.EmitByte(0x7E);
        EmitModRMRegS(ref code, (Reg64)xmm, d);
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
        EmitModRMRegS(ref code, (Reg64)xmm, s);
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
        EmitModRMRegS(ref code, (Reg64)xmm, d);
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
        EmitModRMMemDispS(ref code, (Reg64)dst, b, disp);
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
        EmitModRMMemDispS(ref code, (Reg64)src, b, disp);
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
        EmitModRMMemDispS(ref code, (Reg64)dst, b, disp);
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
        EmitModRMMemDispS(ref code, (Reg64)src, b, disp);
    }

    /// <summary>
    /// CVTSI2SD - Convert signed int64 to double: xmm = (double)r64
    /// </summary>
    public static void Cvtsi2sdXmmR64(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F2 REX.W 0F 2A /r
        code.EmitByte(0xF2);
        EmitRexS(ref code, true, (Reg64)dst, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMRegS(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// CVTSI2SS - Convert signed int64 to float: xmm = (float)r64
    /// </summary>
    public static void Cvtsi2ssXmmR64(ref CodeBuffer code, RegXMM dst, VReg src)
    {
        var s = Map(src);
        // F3 REX.W 0F 2A /r
        code.EmitByte(0xF3);
        EmitRexS(ref code, true, (Reg64)dst, s);
        code.EmitByte(0x0F);
        code.EmitByte(0x2A);
        EmitModRMRegS(ref code, (Reg64)dst, s);
    }

    /// <summary>
    /// CVTTSD2SI - Convert double to signed int64 (truncate): r64 = (int64)xmm
    /// </summary>
    public static void Cvttsd2siR64Xmm(ref CodeBuffer code, VReg dst, RegXMM src)
    {
        var d = Map(dst);
        // F2 REX.W 0F 2C /r
        code.EmitByte(0xF2);
        EmitRexS(ref code, true, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMRegS(ref code, d, (Reg64)src);
    }

    /// <summary>
    /// CVTTSS2SI - Convert float to signed int64 (truncate): r64 = (int64)xmm
    /// </summary>
    public static void Cvttss2siR64Xmm(ref CodeBuffer code, VReg dst, RegXMM src)
    {
        var d = Map(dst);
        // F3 REX.W 0F 2C /r
        code.EmitByte(0xF3);
        EmitRexS(ref code, true, d, (Reg64)src);
        code.EmitByte(0x0F);
        code.EmitByte(0x2C);
        EmitModRMRegS(ref code, d, (Reg64)src);
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
        EmitRexS(ref code, true, Reg64.RAX, r);
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
        EmitModRMRegS(ref code, (Reg64)dst, s);
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
        EmitModRMRegS(ref code, (Reg64)dst, s);
    }
}
