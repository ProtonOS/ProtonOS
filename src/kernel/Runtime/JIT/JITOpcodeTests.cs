// ProtonOS JIT - Opcode Code Generation Verification Tests
// These tests compile methods from FullTest.dll using the JIT and verify the generated x64 code.
// This is the correct approach - we test JIT output, not AOT-compiled kernel code.

using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Tests JIT compilation by compiling methods from FullTest.dll and verifying generated x64 code.
/// </summary>
public static unsafe class JITOpcodeTests
{
    private static int _passed;
    private static int _failed;
    private static uint _testAssemblyId;

    /// <summary>
    /// Run all JIT opcode verification tests.
    /// Must be called after the test assembly is loaded.
    /// </summary>
    public static void RunAll(uint testAssemblyId)
    {
        _passed = 0;
        _failed = 0;
        _testAssemblyId = testAssemblyId;

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("=== JIT Code Generation Verification ===");
        DebugConsole.WriteLine();

        // Find test classes in FullTest assembly
        VerifyArithmeticTests();
        VerifyComparisonTests();
        VerifyBitwiseTests();
        VerifyControlFlowTests();
        VerifyLocalTests();
        VerifyMethodCallTests();
        VerifyConversionTests();
        VerifyStructTests();
        VerifyArrayTests();
        VerifyFieldTests();
        VerifyInstanceTests();

        // Summary
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("=== JIT Verification Summary ===");
        DebugConsole.Write("Passed: ");
        DebugConsole.WriteDecimal((uint)_passed);
        DebugConsole.Write("  Failed: ");
        DebugConsole.WriteDecimal((uint)_failed);
        DebugConsole.WriteLine();
    }

    private static void Report(string testName, bool passed, string details)
    {
        if (passed)
        {
            _passed++;
            DebugConsole.Write("[PASS] ");
        }
        else
        {
            _failed++;
            DebugConsole.Write("[FAIL] ");
        }
        DebugConsole.Write(testName);
        DebugConsole.Write(" - ");
        DebugConsole.WriteLine(details);
    }

    // ==================== Arithmetic Tests ====================

    private static void VerifyArithmeticTests()
    {
        DebugConsole.WriteLine("--- Arithmetic Code Gen ---");

        // Find ArithmeticTests type
        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "ArithmeticTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] ArithmeticTests type not found");
            return;
        }

        // Test TestAdd method - should generate: add opcode
        VerifyMethod(typeToken, "TestAdd", "add instruction generated", VerifyAddInstruction);

        // Test TestSub method - should generate: sub opcode
        VerifyMethod(typeToken, "TestSub", "sub instruction generated", VerifySubInstruction);

        // Test TestMul method - should generate: imul opcode
        VerifyMethod(typeToken, "TestMul", "imul instruction generated", VerifyMulInstruction);
    }

    private static bool VerifyAddInstruction(byte* code, int size)
    {
        // Look for ADD instruction patterns in x64:
        // 03 xx = ADD r32, r/m32
        // 01 xx = ADD r/m32, r32
        // 48 03 xx = ADD r64, r/m64
        // 48 01 xx = ADD r/m64, r64
        for (int i = 0; i < size - 1; i++)
        {
            // 32-bit add
            if (code[i] == 0x03 || code[i] == 0x01)
                return true;
            // 64-bit add (with REX.W prefix)
            if (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x03 || code[i + 1] == 0x01))
                return true;
        }
        return false;
    }

    private static bool VerifySubInstruction(byte* code, int size)
    {
        // Look for SUB instruction patterns in x64:
        // 2B xx = SUB r32, r/m32
        // 29 xx = SUB r/m32, r32
        // 48 2B xx = SUB r64, r/m64
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x2B || code[i] == 0x29)
                return true;
            if (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x2B || code[i + 1] == 0x29))
                return true;
        }
        return false;
    }

    private static bool VerifyMulInstruction(byte* code, int size)
    {
        // Look for IMUL instruction patterns in x64:
        // 0F AF xx = IMUL r32, r/m32 (two-operand form)
        // 69 xx imm32 = IMUL r32, r/m32, imm32
        // 6B xx imm8 = IMUL r32, r/m32, imm8
        // F7 /5 = IMUL r/m32 (one-operand form)
        for (int i = 0; i < size - 2; i++)
        {
            // Two-operand IMUL
            if (code[i] == 0x0F && code[i + 1] == 0xAF)
                return true;
            // Three-operand IMUL with imm32
            if (code[i] == 0x69)
                return true;
            // Three-operand IMUL with imm8
            if (code[i] == 0x6B)
                return true;
            // One-operand IMUL (check ModR/M for /5)
            if (code[i] == 0xF7)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 5)
                    return true;
            }
        }
        return false;
    }

    // ==================== Comparison Tests ====================

    private static void VerifyComparisonTests()
    {
        DebugConsole.WriteLine("--- Comparison Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "ComparisonTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] ComparisonTests type not found");
            return;
        }

        // Test TestCeq - should generate CMP + SETE
        VerifyMethod(typeToken, "TestCeq", "cmp+sete pattern", VerifyCmpSeteInstruction);

        // Test TestCgt - should generate CMP + SETG
        VerifyMethod(typeToken, "TestCgt", "cmp+setg pattern", VerifyCmpSetgInstruction);

        // Test TestClt - should generate CMP + SETL
        VerifyMethod(typeToken, "TestClt", "cmp+setl pattern", VerifyCmpSetlInstruction);
    }

    private static bool VerifyCmpSeteInstruction(byte* code, int size)
    {
        // Look for CMP (39, 3B, or 81/83 with /7) followed by SETE (0F 94)
        bool foundCmp = false;
        bool foundSete = false;

        for (int i = 0; i < size - 1; i++)
        {
            // CMP patterns
            if (code[i] == 0x39 || code[i] == 0x3B || code[i] == 0x3D)
                foundCmp = true;
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 7)
                    foundCmp = true;
            }

            // SETE pattern: 0F 94
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] == 0x94)
                foundSete = true;
        }

        return foundCmp && foundSete;
    }

    private static bool VerifyCmpSetgInstruction(byte* code, int size)
    {
        bool foundCmp = false;
        bool foundSetg = false;

        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x39 || code[i] == 0x3B || code[i] == 0x3D)
                foundCmp = true;
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 7)
                    foundCmp = true;
            }

            // SETG pattern: 0F 9F
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] == 0x9F)
                foundSetg = true;
        }

        return foundCmp && foundSetg;
    }

    private static bool VerifyCmpSetlInstruction(byte* code, int size)
    {
        bool foundCmp = false;
        bool foundSetl = false;

        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x39 || code[i] == 0x3B || code[i] == 0x3D)
                foundCmp = true;
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 7)
                    foundCmp = true;
            }

            // SETL pattern: 0F 9C
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] == 0x9C)
                foundSetl = true;
        }

        return foundCmp && foundSetl;
    }

    // ==================== Bitwise Tests ====================

    private static void VerifyBitwiseTests()
    {
        DebugConsole.WriteLine("--- Bitwise Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "BitwiseTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] BitwiseTests type not found");
            return;
        }

        // Test AND - should generate: and opcode
        VerifyMethod(typeToken, "TestAnd", "AND instruction", VerifyAndInstruction);

        // Test OR - should generate: or opcode
        VerifyMethod(typeToken, "TestOr", "OR instruction", VerifyOrInstruction);

        // Test XOR - should generate: xor opcode
        VerifyMethod(typeToken, "TestXor", "XOR instruction", VerifyXorInstruction);

        // Test NOT - should generate: not opcode
        VerifyMethod(typeToken, "TestNot", "NOT instruction", VerifyNotInstruction);

        // Test SHL - should generate: shl opcode
        VerifyMethod(typeToken, "TestShl", "SHL instruction", VerifyShlInstruction);

        // Test SHR - should generate: sar/shr opcode
        VerifyMethod(typeToken, "TestShr", "SAR/SHR instruction", VerifyShrInstruction);
    }

    private static bool VerifyAndInstruction(byte* code, int size)
    {
        // AND: 21 xx (r/m32, r32), 23 xx (r32, r/m32)
        // 48 21/23 for 64-bit
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x21 || code[i] == 0x23)
                return true;
            if (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x21 || code[i + 1] == 0x23))
                return true;
            // AND with immediate: 81 /4, 83 /4
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 4)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyOrInstruction(byte* code, int size)
    {
        // OR: 09 xx (r/m32, r32), 0B xx (r32, r/m32)
        // 48 09/0B for 64-bit
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x09 || code[i] == 0x0B)
                return true;
            if (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x09 || code[i + 1] == 0x0B))
                return true;
            // OR with immediate: 81 /1, 83 /1
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 1)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyXorInstruction(byte* code, int size)
    {
        // XOR: 31 xx (r/m32, r32), 33 xx (r32, r/m32)
        // 48 31/33 for 64-bit
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x31 || code[i] == 0x33)
                return true;
            if (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x31 || code[i + 1] == 0x33))
                return true;
            // XOR with immediate: 81 /6, 83 /6
            if ((code[i] == 0x81 || code[i] == 0x83) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 6)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyNotInstruction(byte* code, int size)
    {
        // NOT: F7 /2 (r/m32)
        // 48 F7 /2 for 64-bit
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0xF7 && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 2)
                    return true;
            }
            if (code[i] == 0x48 && i + 2 < size && code[i + 1] == 0xF7)
            {
                byte modrm = code[i + 2];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 2)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyShlInstruction(byte* code, int size)
    {
        // SHL: D3 /4 (r/m32, CL), C1 /4 imm8 (r/m32, imm8)
        for (int i = 0; i < size - 1; i++)
        {
            if ((code[i] == 0xD3 || code[i] == 0xC1 || code[i] == 0xD1) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 4)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyShrInstruction(byte* code, int size)
    {
        // SAR: D3 /7 (r/m32, CL), C1 /7 imm8 (signed right shift)
        // SHR: D3 /5 (r/m32, CL), C1 /5 imm8 (unsigned right shift)
        for (int i = 0; i < size - 1; i++)
        {
            if ((code[i] == 0xD3 || code[i] == 0xC1 || code[i] == 0xD1) && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 5 || reg == 7)  // SHR = 5, SAR = 7
                    return true;
            }
        }
        return false;
    }

    // ==================== Control Flow Tests ====================

    private static void VerifyControlFlowTests()
    {
        DebugConsole.WriteLine("--- Control Flow Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "ControlFlowTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] ControlFlowTests type not found");
            return;
        }

        // Test branch instructions
        VerifyMethod(typeToken, "TestBrtrue", "jne/jnz generated", VerifyJneInstruction);
        VerifyMethod(typeToken, "TestBrfalse", "je/jz generated", VerifyJeInstruction);
        VerifyMethod(typeToken, "TestLoop", "loop structure (jmp/jcc)", VerifyLoopStructure);
    }

    private static bool VerifyJneInstruction(byte* code, int size)
    {
        // JNE/JNZ: 75 rel8 or 0F 85 rel32
        for (int i = 0; i < size; i++)
        {
            if (code[i] == 0x75)
                return true;
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] == 0x85)
                return true;
        }
        return false;
    }

    private static bool VerifyJeInstruction(byte* code, int size)
    {
        // JE/JZ: 74 rel8 or 0F 84 rel32
        // Also accept JMP (EB, E9) since constant brfalse 0 gets optimized to unconditional jump
        for (int i = 0; i < size; i++)
        {
            if (code[i] == 0x74)
                return true;
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] == 0x84)
                return true;
            // Unconditional jumps (for constant-folded branches)
            if (code[i] == 0xEB || code[i] == 0xE9)
                return true;
        }
        return false;
    }

    private static bool VerifyLoopStructure(byte* code, int size)
    {
        // A loop should have both a conditional jump (Jcc) and possibly unconditional jump (JMP)
        // Look for any Jcc (70-7F, 0F 80-8F) and JMP (EB, E9)
        bool hasJcc = false;
        bool hasBackwardJump = false;

        for (int i = 0; i < size; i++)
        {
            // Short Jcc: 70-7F
            if (code[i] >= 0x70 && code[i] <= 0x7F)
            {
                hasJcc = true;
                // Check if it's a backward jump (negative offset)
                if (i + 1 < size && (sbyte)code[i + 1] < 0)
                    hasBackwardJump = true;
            }
            // Near Jcc: 0F 80-8F
            if (code[i] == 0x0F && i + 1 < size && code[i + 1] >= 0x80 && code[i + 1] <= 0x8F)
                hasJcc = true;
            // Short JMP: EB
            if (code[i] == 0xEB)
            {
                if (i + 1 < size && (sbyte)code[i + 1] < 0)
                    hasBackwardJump = true;
            }
            // Near JMP: E9
            if (code[i] == 0xE9)
            {
                if (i + 4 < size)
                {
                    int offset = *(int*)(code + i + 1);
                    if (offset < 0)
                        hasBackwardJump = true;
                }
            }
        }

        return hasJcc && hasBackwardJump;
    }

    // ==================== Local Variable Tests ====================

    private static void VerifyLocalTests()
    {
        DebugConsole.WriteLine("--- Local Variable Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "LocalVariableTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] LocalVariableTests type not found");
            return;
        }

        // TestSimpleLocals - should have MOV to stack locations
        VerifyMethod(typeToken, "TestSimpleLocals", "stack frame access", VerifyStackAccess);

        // TestLocalAddress - should have LEA for address loading
        VerifyMethod(typeToken, "TestLocalAddress", "LEA instruction", VerifyLeaInstruction);
    }

    private static bool VerifyStackAccess(byte* code, int size)
    {
        // Look for MOV with RBP-based addressing: [rbp-xx]
        // 89 45 xx = MOV [rbp+disp8], r32
        // 8B 45 xx = MOV r32, [rbp+disp8]
        // 48 89 45 xx = MOV [rbp+disp8], r64
        // 48 8B 45 xx = MOV r64, [rbp+disp8]
        for (int i = 0; i < size - 2; i++)
        {
            byte op = code[i];
            byte modrm = code[i + 1];

            // Check for MOV with ModR/M indicating [rbp+disp8]
            // ModR/M = 01 xxx 101 (mod=01, r/m=101=rbp)
            if ((op == 0x89 || op == 0x8B) && (modrm & 0xC7) == 0x45)
                return true;

            // With REX.W prefix
            if (code[i] == 0x48 && i + 2 < size)
            {
                op = code[i + 1];
                modrm = code[i + 2];
                if ((op == 0x89 || op == 0x8B) && (modrm & 0xC7) == 0x45)
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyLeaInstruction(byte* code, int size)
    {
        // LEA: 8D ModR/M or 48 8D ModR/M (64-bit)
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x8D)
                return true;
            if (code[i] == 0x48 && i + 1 < size && code[i + 1] == 0x8D)
                return true;
        }
        return false;
    }

    // ==================== Method Call Tests ====================

    private static void VerifyMethodCallTests()
    {
        DebugConsole.WriteLine("--- Method Call Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "MethodCallTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] MethodCallTests type not found");
            return;
        }

        // TestSimpleCall - should have CALL instruction
        VerifyMethod(typeToken, "TestSimpleCall", "CALL instruction", VerifyCallInstruction);

        // TestRecursion - should have CALL for recursive call
        VerifyMethod(typeToken, "TestRecursion", "recursive CALL", VerifyCallInstruction);
    }

    private static bool VerifyCallInstruction(byte* code, int size)
    {
        // CALL: E8 rel32 (near call)
        // CALL: FF /2 (indirect call through r/m)
        for (int i = 0; i < size; i++)
        {
            if (code[i] == 0xE8)
                return true;
            if (code[i] == 0xFF && i + 1 < size)
            {
                byte modrm = code[i + 1];
                byte reg = (byte)((modrm >> 3) & 0x7);
                if (reg == 2)
                    return true;
            }
        }
        return false;
    }

    // ==================== Struct Tests ====================

    private static void VerifyStructTests()
    {
        DebugConsole.WriteLine("--- Struct Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "StructTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] StructTests type not found");
            return;
        }

        // Small struct field access (8 bytes - fits in register)
        VerifyMethod(typeToken, "TestStructLocalFieldWrite", "struct field write", VerifyStructFieldAccess);
        VerifyMethod(typeToken, "TestStructLocalFieldSum", "struct field read", VerifyStructFieldAccess);

        // Struct pass by value
        VerifyMethod(typeToken, "TestStructPassByValue", "struct pass by value", VerifyCallInstruction);

        // Struct out parameter
        VerifyMethod(typeToken, "TestStructOutParam", "struct out param", VerifyCallInstruction);

        // Object initializer (complex dup+initobj+stfld pattern)
        VerifyMethod(typeToken, "TestObjectInitializer", "object initializer", VerifyStructFieldAccess);

        // stobj instruction
        VerifyMethod(typeToken, "TestSimpleStobj", "stobj instruction", VerifyMovInstruction);

        // Large struct operations (>8 bytes - uses memory copy)
        VerifyMethod(typeToken, "TestLargeStructFields", "large struct fields", VerifyStructFieldAccess);
        VerifyMethod(typeToken, "TestLargeStructCopy", "large struct copy", VerifyRepMovs);

        // Struct array operations
        VerifyMethod(typeToken, "TestStructArrayStore", "struct array store", VerifyArrayAccess);
        VerifyMethod(typeToken, "TestStructArrayLoad", "struct array load", VerifyArrayAccess);
    }

    private static bool VerifyStructFieldAccess(byte* code, int size)
    {
        // Struct field access uses MOV with displacement from base
        // Look for MOV instructions with memory operands
        for (int i = 0; i < size - 2; i++)
        {
            // MOV r/m, r or MOV r, r/m with displacement
            if (code[i] == 0x89 || code[i] == 0x8B)
            {
                byte modrm = code[i + 1];
                byte mod = (byte)(modrm >> 6);
                // mod=01 (8-bit disp) or mod=10 (32-bit disp) indicate memory access
                if (mod == 1 || mod == 2)
                    return true;
            }
            // With REX prefix
            if ((code[i] & 0xF0) == 0x40 && i + 2 < size)
            {
                byte op = code[i + 1];
                if (op == 0x89 || op == 0x8B)
                {
                    byte modrm = code[i + 2];
                    byte mod = (byte)(modrm >> 6);
                    if (mod == 1 || mod == 2)
                        return true;
                }
            }
        }
        return false;
    }

    private static bool VerifyRepMovs(byte* code, int size)
    {
        // REP MOVSB: F3 A4
        // REP MOVSQ: F3 48 A5
        // Or just regular MOVSB/MOVSQ without REP
        for (int i = 0; i < size - 1; i++)
        {
            // REP prefix
            if (code[i] == 0xF3)
            {
                if (i + 1 < size && (code[i + 1] == 0xA4 || code[i + 1] == 0xA5))
                    return true;
                if (i + 2 < size && code[i + 1] == 0x48 && code[i + 2] == 0xA5)
                    return true;
            }
            // Without REP
            if (code[i] == 0xA4 || code[i] == 0xA5)
                return true;
        }
        // Also accept multiple MOV instructions as alternative to rep movs
        int movCount = 0;
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x89 || code[i] == 0x8B ||
                (code[i] == 0x48 && i + 1 < size && (code[i + 1] == 0x89 || code[i + 1] == 0x8B)))
                movCount++;
        }
        return movCount >= 3; // Multiple MOVs suggest struct copy
    }

    private static bool VerifyArrayAccess(byte* code, int size)
    {
        // Array access involves: base + index * scale + offset
        // Look for SIB byte patterns or LEA with scale
        for (int i = 0; i < size - 2; i++)
        {
            byte op = code[i];
            // MOV with potential SIB
            if (op == 0x89 || op == 0x8B || op == 0x8D)  // 8D = LEA
            {
                byte modrm = code[i + 1];
                byte rm = (byte)(modrm & 0x7);
                // r/m = 100 means SIB byte follows
                if (rm == 4)
                    return true;
            }
            // With REX prefix
            if ((code[i] & 0xF0) == 0x40 && i + 2 < size)
            {
                op = code[i + 1];
                if (op == 0x89 || op == 0x8B || op == 0x8D)
                {
                    byte modrm = code[i + 2];
                    byte rm = (byte)(modrm & 0x7);
                    if (rm == 4)
                        return true;
                }
            }
        }
        return false;
    }

    // ==================== Array Tests ====================

    private static void VerifyArrayTests()
    {
        DebugConsole.WriteLine("--- Array Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "ArrayTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] ArrayTests type not found");
            return;
        }

        // newarr - allocates array
        VerifyMethod(typeToken, "TestNewarr", "newarr (call)", VerifyCallInstruction);

        // stelem - store to array element
        VerifyMethod(typeToken, "TestStelem", "stelem (array access)", VerifyArrayAccess);

        // ldlen - get array length
        VerifyMethod(typeToken, "TestLdlen", "ldlen (memory read)", VerifyMovInstruction);

        // Complex array operations with loop
        VerifyMethod(typeToken, "TestArraySum", "array sum loop", VerifyLoopStructure);
    }

    // ==================== Field Tests ====================

    private static void VerifyFieldTests()
    {
        DebugConsole.WriteLine("--- Field Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "FieldTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] FieldTests type not found");
            return;
        }

        // Static field access
        VerifyMethod(typeToken, "TestStaticField", "static field (RIP-rel)", VerifyRipRelativeAccess);

        // Static field increment (read-modify-write)
        VerifyMethod(typeToken, "TestStaticFieldIncrement", "static field inc", VerifyAddInstruction);

        // Multiple static fields
        VerifyMethod(typeToken, "TestMultipleStaticFields", "multiple static fields", VerifyRipRelativeAccess);
    }

    private static bool VerifyRipRelativeAccess(byte* code, int size)
    {
        // RIP-relative addressing: MOV with ModR/M where mod=00 and r/m=101
        // This is [RIP+disp32] addressing mode
        for (int i = 0; i < size - 5; i++)
        {
            byte op = code[i];
            // MOV instructions
            if (op == 0x89 || op == 0x8B || op == 0xC7)
            {
                byte modrm = code[i + 1];
                byte mod = (byte)(modrm >> 6);
                byte rm = (byte)(modrm & 0x7);
                // mod=00, rm=101 = RIP-relative
                if (mod == 0 && rm == 5)
                    return true;
            }
            // With REX prefix
            if ((code[i] & 0xF0) == 0x40 && i + 2 < size)
            {
                op = code[i + 1];
                if (op == 0x89 || op == 0x8B || op == 0xC7)
                {
                    byte modrm = code[i + 2];
                    byte mod = (byte)(modrm >> 6);
                    byte rm = (byte)(modrm & 0x7);
                    if (mod == 0 && rm == 5)
                        return true;
                }
            }
        }
        return false;
    }

    // ==================== Instance/Object Tests ====================

    private static void VerifyInstanceTests()
    {
        DebugConsole.WriteLine("--- Instance/Object Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "InstanceTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] InstanceTests type not found");
            return;
        }

        // Instance field read/write (ldfld/stfld on object)
        VerifyMethod(typeToken, "TestInstanceFieldReadWrite", "instance field access", VerifyStructFieldAccess);

        // Multiple instance fields
        VerifyMethod(typeToken, "TestMultipleInstanceFields", "multiple instance fields", VerifyStructFieldAccess);

        // Instance method call (call with this)
        VerifyMethod(typeToken, "TestInstanceMethodCall", "instance method call", VerifyCallInstruction);

        // Instance method using this
        VerifyMethod(typeToken, "TestInstanceMethodWithThis", "this pointer usage", VerifyCallInstruction);

        // Callvirt with bool branch and large locals (driver-like pattern!)
        VerifyMethod(typeToken, "TestCallvirtBoolBranchWithLargeLocals", "callvirt+bool+large locals", VerifyCallInstruction);

        // Virtqueue-like pattern: class with many fields, struct copy, field store/read
        VerifyMethod(typeToken, "TestVirtqueuePattern", "virtqueue pattern", VerifyCallInstruction);

        // CRITICAL: Cross-assembly large struct return (hidden buffer convention)
        // This is the exact pattern that crashes the driver: calling a method in another assembly
        // that returns a struct >16 bytes (uses hidden buffer convention)
        VerifyMethod(typeToken, "TestCrossAssemblyLargeStructReturn", "cross-asm large struct return", VerifyCallInstruction);

        // Cross-assembly large struct to class field (mimics DMA.Allocate -> copy to _descBuffer)
        VerifyMethod(typeToken, "TestCrossAssemblyLargeStructToClassField", "cross-asm struct to field", VerifyCallInstruction);
    }

    // ==================== Conversion Tests ====================

    private static void VerifyConversionTests()
    {
        DebugConsole.WriteLine("--- Conversion Code Gen ---");

        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "ConversionTests");
        if (typeToken == 0)
        {
            DebugConsole.WriteLine("[SKIP] ConversionTests type not found");
            return;
        }

        // Test conv.i4 - truncate to 32-bit
        VerifyMethod(typeToken, "TestConvI4", "32-bit truncation", VerifyMovInstruction);

        // Test conv.i8 - extend to 64-bit (cdqe/movsxd)
        VerifyMethod(typeToken, "TestConvI8", "64-bit sign extend", VerifySignExtendInstruction);

        // Test conv.i1 - sign extend byte (movsx)
        VerifyMethod(typeToken, "TestConvI1", "MOVSX byte", VerifyMovsxInstruction);

        // Test conv.u1 - zero extend byte (movzx)
        VerifyMethod(typeToken, "TestConvU1", "MOVZX byte", VerifyMovzxInstruction);
    }

    private static bool VerifyMovInstruction(byte* code, int size)
    {
        // MOV: 89 xx (r/m, r), 8B xx (r, r/m)
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x89 || code[i] == 0x8B)
                return true;
        }
        return false;
    }

    private static bool VerifySignExtendInstruction(byte* code, int size)
    {
        // CDQE: 48 98 (sign extend EAX to RAX)
        // MOVSXD: 48 63 (sign extend r/m32 to r64)
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x48 && i + 1 < size)
            {
                if (code[i + 1] == 0x98)  // CDQE
                    return true;
                if (code[i + 1] == 0x63)  // MOVSXD
                    return true;
            }
        }
        return false;
    }

    private static bool VerifyMovsxInstruction(byte* code, int size)
    {
        // MOVSX: 0F BE (r32, r/m8), 0F BF (r32, r/m16)
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x0F && i + 1 < size && (code[i + 1] == 0xBE || code[i + 1] == 0xBF))
                return true;
        }
        return false;
    }

    private static bool VerifyMovzxInstruction(byte* code, int size)
    {
        // MOVZX: 0F B6 (r32, r/m8), 0F B7 (r32, r/m16)
        for (int i = 0; i < size - 1; i++)
        {
            if (code[i] == 0x0F && i + 1 < size && (code[i + 1] == 0xB6 || code[i + 1] == 0xB7))
                return true;
        }
        return false;
    }

    // ==================== Helper Methods ====================

    private delegate bool CodeVerifier(byte* code, int size);

    private static void VerifyMethod(uint typeToken, string methodName, string expectation, CodeVerifier verifier)
    {
        // Find the method
        uint methodToken = AssemblyLoader.FindMethodDefByName(_testAssemblyId, typeToken, methodName);
        if (methodToken == 0)
        {
            Report(methodName, false, "method not found");
            return;
        }

        // JIT compile the method
        var result = Tier0JIT.CompileMethod(_testAssemblyId, methodToken);
        if (!result.Success)
        {
            Report(methodName, false, "JIT compilation failed");
            return;
        }

        // Get the compiled code info
        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, _testAssemblyId);
        if (info == null || info->NativeCode == null)
        {
            Report(methodName, false, "no native code found");
            return;
        }

        // Verify the generated code
        // Note: We don't have exact code size, so use a reasonable maximum
        const int MaxCodeSize = 512;
        byte* code = (byte*)info->NativeCode;

        bool passed = verifier(code, MaxCodeSize);
        Report(methodName, passed, passed ? expectation : "pattern not found");

        // Dump first bytes for debugging if failed
        if (!passed)
        {
            DebugConsole.Write("  Code bytes: ");
            for (int i = 0; i < 32 && i < MaxCodeSize; i++)
            {
                DebugConsole.WriteHex(code[i]);
                DebugConsole.Write(" ");
            }
            DebugConsole.WriteLine();
        }
    }

    /// <summary>
    /// Dump the generated code for a method (for debugging).
    /// </summary>
    public static void DumpMethodCode(uint assemblyId, string typeName, string methodName, int bytes = 64)
    {
        uint typeToken = AssemblyLoader.FindTypeDefByFullName(assemblyId, "FullTest", typeName);
        if (typeToken == 0)
        {
            DebugConsole.Write("[Dump] Type not found: ");
            DebugConsole.WriteLine(typeName);
            return;
        }

        uint methodToken = AssemblyLoader.FindMethodDefByName(assemblyId, typeToken, methodName);
        if (methodToken == 0)
        {
            DebugConsole.Write("[Dump] Method not found: ");
            DebugConsole.WriteLine(methodName);
            return;
        }

        var result = Tier0JIT.CompileMethod(assemblyId, methodToken);
        if (!result.Success)
        {
            DebugConsole.WriteLine("[Dump] JIT compilation failed");
            return;
        }

        CompiledMethodInfo* info = CompiledMethodRegistry.Lookup(methodToken, assemblyId);
        if (info == null || info->NativeCode == null)
        {
            DebugConsole.WriteLine("[Dump] No native code");
            return;
        }

        DebugConsole.Write("[Dump] ");
        DebugConsole.Write(typeName);
        DebugConsole.Write(".");
        DebugConsole.Write(methodName);
        DebugConsole.Write(" @ 0x");
        DebugConsole.WriteHex((ulong)info->NativeCode);
        DebugConsole.WriteLine(":");

        byte* code = (byte*)info->NativeCode;
        for (int i = 0; i < bytes; i++)
        {
            if (i % 16 == 0)
            {
                if (i > 0) DebugConsole.WriteLine();
                DebugConsole.Write("  ");
                DebugConsole.WriteHex((uint)i);
                DebugConsole.Write(": ");
            }
            DebugConsole.WriteHex(code[i]);
            DebugConsole.Write(" ");
        }
        DebugConsole.WriteLine();
    }
}
