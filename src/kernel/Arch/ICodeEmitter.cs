// ProtonOS Architecture Abstraction - Code Emitter Interface
// High-level interface for JIT code generation.
// Uses C# 11 static abstract members for compile-time polymorphism.

using ProtonOS.Runtime.JIT;

namespace ProtonOS.Arch;

/// <summary>
/// High-level code emitter interface for JIT compilation.
/// Static abstract methods enable compile-time polymorphism with zero runtime overhead.
///
/// Each architecture implements this interface with its specific instruction encoding.
/// ILCompiler uses this interface generically: ILCompiler&lt;TEmitter&gt; where TEmitter : ICodeEmitter&lt;TEmitter&gt;
///
/// Design notes:
/// - Methods take ref CodeBuffer to allow in-place emission
/// - Uses VReg for architecture-neutral register references
/// - Calling convention details exposed via properties
/// </summary>
public unsafe interface ICodeEmitter<TSelf> where TSelf : ICodeEmitter<TSelf>
{
    // ==================== Calling Convention Info ====================

    /// <summary>
    /// Number of arguments passed in registers.
    /// x64: 4 (RCX, RDX, R8, R9)
    /// ARM64: 8 (X0-X7)
    /// </summary>
    static abstract int ArgRegisterCount { get; }

    /// <summary>
    /// Size of shadow/home space in bytes.
    /// x64: 32 bytes (for 4 register args)
    /// ARM64: 0 bytes
    /// </summary>
    static abstract int ShadowSpaceSize { get; }

    /// <summary>
    /// Required stack alignment in bytes.
    /// Both x64 and ARM64: 16
    /// </summary>
    static abstract int StackAlignment { get; }

    // ==================== Prologue / Epilogue ====================

    /// <summary>
    /// Emit method prologue. Sets up stack frame with space for locals.
    /// Returns the total stack adjustment (for epilogue).
    /// </summary>
    /// <param name="code">Code buffer to emit into</param>
    /// <param name="localBytes">Space needed for local variables</param>
    /// <returns>Stack adjustment value for EmitEpilogue</returns>
    static abstract int EmitPrologue(ref CodeBuffer code, int localBytes);

    /// <summary>
    /// Emit method epilogue. Restores stack and returns.
    /// </summary>
    /// <param name="code">Code buffer to emit into</param>
    /// <param name="stackAdjust">Stack adjustment from EmitPrologue</param>
    static abstract void EmitEpilogue(ref CodeBuffer code, int stackAdjust);

    /// <summary>
    /// Home (spill) argument registers to stack shadow space.
    /// Call after prologue to access arguments from stack.
    /// </summary>
    /// <param name="code">Code buffer to emit into</param>
    /// <param name="argCount">Number of arguments to home</param>
    static abstract void HomeArguments(ref CodeBuffer code, int argCount);

    // ==================== Register Operations ====================

    /// <summary>
    /// Move register to register (64-bit).
    /// </summary>
    static abstract void MovRR(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Load 64-bit immediate into register.
    /// </summary>
    static abstract void MovRI64(ref CodeBuffer code, VReg dst, ulong imm);

    /// <summary>
    /// Load 32-bit immediate into register (sign-extended on x64).
    /// </summary>
    static abstract void MovRI32(ref CodeBuffer code, VReg dst, int imm);

    /// <summary>
    /// Zero a register (XOR optimization).
    /// </summary>
    static abstract void ZeroReg(ref CodeBuffer code, VReg reg);

    // ==================== Memory Operations ====================

    /// <summary>
    /// Load 64-bit value from memory: dst = [baseReg + disp]
    /// </summary>
    static abstract void Load64(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 32-bit value from memory (zero-extended): dst = [baseReg + disp]
    /// </summary>
    static abstract void Load32(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 32-bit value from memory (sign-extended to 64-bit).
    /// </summary>
    static abstract void Load32Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 16-bit value from memory (zero-extended).
    /// </summary>
    static abstract void Load16(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 16-bit value from memory (sign-extended).
    /// </summary>
    static abstract void Load16Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 8-bit value from memory (zero-extended).
    /// </summary>
    static abstract void Load8(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Load 8-bit value from memory (sign-extended).
    /// </summary>
    static abstract void Load8Signed(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    /// <summary>
    /// Store 64-bit value to memory: [baseReg + disp] = src
    /// </summary>
    static abstract void Store64(ref CodeBuffer code, VReg baseReg, int disp, VReg src);

    /// <summary>
    /// Store 32-bit value to memory.
    /// </summary>
    static abstract void Store32(ref CodeBuffer code, VReg baseReg, int disp, VReg src);

    /// <summary>
    /// Store 16-bit value to memory.
    /// </summary>
    static abstract void Store16(ref CodeBuffer code, VReg baseReg, int disp, VReg src);

    /// <summary>
    /// Store 8-bit value to memory.
    /// </summary>
    static abstract void Store8(ref CodeBuffer code, VReg baseReg, int disp, VReg src);

    /// <summary>
    /// Load effective address: dst = baseReg + disp
    /// </summary>
    static abstract void LoadAddress(ref CodeBuffer code, VReg dst, VReg baseReg, int disp);

    // ==================== Arithmetic ====================

    /// <summary>
    /// Add: dst = dst + src
    /// </summary>
    static abstract void Add(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Add immediate: dst = dst + imm
    /// </summary>
    static abstract void AddImm(ref CodeBuffer code, VReg dst, int imm);

    /// <summary>
    /// Subtract: dst = dst - src
    /// </summary>
    static abstract void Sub(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Subtract immediate: dst = dst - imm
    /// </summary>
    static abstract void SubImm(ref CodeBuffer code, VReg dst, int imm);

    /// <summary>
    /// Multiply: dst = dst * src
    /// </summary>
    static abstract void Mul(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Signed divide. Puts quotient in Return register.
    /// Caller must set up dividend and clear/sign-extend as needed.
    /// </summary>
    static abstract void DivSigned(ref CodeBuffer code, VReg divisor);

    /// <summary>
    /// Unsigned divide. Puts quotient in Return register.
    /// </summary>
    static abstract void DivUnsigned(ref CodeBuffer code, VReg divisor);

    /// <summary>
    /// Negate: reg = -reg
    /// </summary>
    static abstract void Neg(ref CodeBuffer code, VReg reg);

    // ==================== Bitwise Operations ====================

    /// <summary>
    /// Bitwise AND: dst = dst &amp; src
    /// </summary>
    static abstract void And(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Bitwise AND with immediate: dst = dst &amp; imm
    /// </summary>
    static abstract void AndImm(ref CodeBuffer code, VReg dst, int imm);

    /// <summary>
    /// Bitwise OR: dst = dst | src
    /// </summary>
    static abstract void Or(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Bitwise XOR: dst = dst ^ src
    /// </summary>
    static abstract void Xor(ref CodeBuffer code, VReg dst, VReg src);

    /// <summary>
    /// Bitwise NOT: reg = ~reg
    /// </summary>
    static abstract void Not(ref CodeBuffer code, VReg reg);

    /// <summary>
    /// Shift left: value = value &lt;&lt; shiftAmount (lower 6 bits)
    /// </summary>
    static abstract void ShiftLeft(ref CodeBuffer code, VReg value, VReg shiftAmount);

    /// <summary>
    /// Shift left by immediate: value = value &lt;&lt; imm
    /// </summary>
    static abstract void ShiftLeftImm(ref CodeBuffer code, VReg value, byte imm);

    /// <summary>
    /// Arithmetic shift right (preserves sign): value = value &gt;&gt; shiftAmount
    /// </summary>
    static abstract void ShiftRightSigned(ref CodeBuffer code, VReg value, VReg shiftAmount);

    /// <summary>
    /// Logical shift right (zero-fill): value = value &gt;&gt;&gt; shiftAmount
    /// </summary>
    static abstract void ShiftRightUnsigned(ref CodeBuffer code, VReg value, VReg shiftAmount);

    // ==================== Comparison ====================

    /// <summary>
    /// Compare 64-bit registers (sets flags for subsequent Jcc).
    /// </summary>
    static abstract void Compare(ref CodeBuffer code, VReg left, VReg right);

    /// <summary>
    /// Compare 32-bit registers (for int32 comparisons).
    /// </summary>
    static abstract void Compare32(ref CodeBuffer code, VReg left, VReg right);

    /// <summary>
    /// Compare register with immediate.
    /// </summary>
    static abstract void CompareImm(ref CodeBuffer code, VReg reg, int imm);

    /// <summary>
    /// Test (AND without storing result): sets ZF based on left &amp; right
    /// </summary>
    static abstract void Test(ref CodeBuffer code, VReg left, VReg right);

    // ==================== Control Flow ====================

    /// <summary>
    /// Return from function.
    /// </summary>
    static abstract void Ret(ref CodeBuffer code);

    /// <summary>
    /// Call through register.
    /// </summary>
    static abstract void CallReg(ref CodeBuffer code, VReg target);

    /// <summary>
    /// Call with rel32 displacement. Returns offset of rel32 for patching.
    /// </summary>
    static abstract int CallRel32(ref CodeBuffer code);

    /// <summary>
    /// Unconditional jump with rel32 displacement. Returns offset for patching.
    /// </summary>
    static abstract int JumpRel32(ref CodeBuffer code);

    /// <summary>
    /// Jump through register.
    /// </summary>
    static abstract void JumpReg(ref CodeBuffer code, VReg target);

    /// <summary>
    /// Conditional jump. Returns offset for patching.
    /// </summary>
    static abstract int JumpConditional(ref CodeBuffer code, Condition cond);

    /// <summary>
    /// Patch a rel32 offset to jump to current position.
    /// </summary>
    static abstract void PatchRel32(ref CodeBuffer code, int patchOffset);

    // ==================== Stack Operations ====================

    /// <summary>
    /// Push register onto stack.
    /// </summary>
    static abstract void Push(ref CodeBuffer code, VReg reg);

    /// <summary>
    /// Pop from stack into register.
    /// </summary>
    static abstract void Pop(ref CodeBuffer code, VReg reg);

    // ==================== Argument/Local Access ====================

    /// <summary>
    /// Get stack offset for argument by index (from frame pointer).
    /// </summary>
    static abstract int GetArgOffset(int argIndex);

    /// <summary>
    /// Get stack offset for local variable by index (from frame pointer).
    /// </summary>
    static abstract int GetLocalOffset(int localIndex);

    /// <summary>
    /// Load argument into register.
    /// </summary>
    static abstract void LoadArg(ref CodeBuffer code, VReg dst, int argIndex);

    /// <summary>
    /// Load local variable into register.
    /// </summary>
    static abstract void LoadLocal(ref CodeBuffer code, VReg dst, int localIndex);

    /// <summary>
    /// Store register to local variable.
    /// </summary>
    static abstract void StoreLocal(ref CodeBuffer code, int localIndex, VReg src);

    // ==================== Floating Point ====================

    /// <summary>
    /// Load float32 from memory into floating-point register.
    /// </summary>
    static abstract void LoadFloat32(ref CodeBuffer code, VRegF dst, VReg baseReg, int disp);

    /// <summary>
    /// Load float64 from memory into floating-point register.
    /// </summary>
    static abstract void LoadFloat64(ref CodeBuffer code, VRegF dst, VReg baseReg, int disp);

    /// <summary>
    /// Store float32 from floating-point register to memory.
    /// </summary>
    static abstract void StoreFloat32(ref CodeBuffer code, VReg baseReg, int disp, VRegF src);

    /// <summary>
    /// Store float64 from floating-point register to memory.
    /// </summary>
    static abstract void StoreFloat64(ref CodeBuffer code, VReg baseReg, int disp, VRegF src);

    /// <summary>
    /// Move float register to float register.
    /// </summary>
    static abstract void MovFF(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble);

    /// <summary>
    /// Add float: dst = dst + src
    /// </summary>
    static abstract void AddFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble);

    /// <summary>
    /// Subtract float: dst = dst - src
    /// </summary>
    static abstract void SubFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble);

    /// <summary>
    /// Multiply float: dst = dst * src
    /// </summary>
    static abstract void MulFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble);

    /// <summary>
    /// Divide float: dst = dst / src
    /// </summary>
    static abstract void DivFloat(ref CodeBuffer code, VRegF dst, VRegF src, bool isDouble);

    // ==================== Conversions ====================

    /// <summary>
    /// Convert signed int32 to float.
    /// </summary>
    static abstract void ConvertInt32ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble);

    /// <summary>
    /// Convert signed int64 to float.
    /// </summary>
    static abstract void ConvertInt64ToFloat(ref CodeBuffer code, VRegF dst, VReg src, bool toDouble);

    /// <summary>
    /// Convert float to signed int64 (truncate).
    /// </summary>
    static abstract void ConvertFloatToInt64(ref CodeBuffer code, VReg dst, VRegF src, bool fromDouble);

    /// <summary>
    /// Convert between float and double.
    /// </summary>
    static abstract void ConvertFloatPrecision(ref CodeBuffer code, VRegF dst, VRegF src, bool toDouble);

    // ==================== Miscellaneous ====================

    /// <summary>
    /// No operation (for alignment or padding).
    /// </summary>
    static abstract void Nop(ref CodeBuffer code);

    /// <summary>
    /// Debug breakpoint.
    /// </summary>
    static abstract void Breakpoint(ref CodeBuffer code);

    /// <summary>
    /// Sign-extend 32-bit register to 64-bit for division.
    /// On x64, this is CQO (sign-extend RAX to RDX:RAX).
    /// </summary>
    static abstract void SignExtendForDivision(ref CodeBuffer code);

    /// <summary>
    /// Zero the upper 32 bits of a register (for unsigned 32-bit values).
    /// On x64, uses 32-bit mov which clears upper bits.
    /// </summary>
    static abstract void ZeroExtend32(ref CodeBuffer code, VReg reg);
}
