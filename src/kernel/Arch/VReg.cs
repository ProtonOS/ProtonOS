// ProtonOS Architecture Abstraction - Virtual Registers
// Architecture-neutral register representation for JIT compilation.

namespace ProtonOS.Arch;

/// <summary>
/// Architecture-neutral virtual registers.
/// Each architecture maps these to physical registers.
///
/// x64 mapping:
///   R0 = RAX (return value, scratch)
///   R1 = RCX (arg0, scratch)
///   R2 = RDX (arg1, scratch)
///   R3 = R8  (arg2, scratch)
///   R4 = R9  (arg3, scratch)
///   R5 = R10 (scratch)
///   R6 = R11 (scratch)
///   R7 = RBX (callee-saved)
///   R8 = R12 (callee-saved)
///   R9 = R13 (callee-saved)
///   R10 = R14 (callee-saved)
///   R11 = R15 (callee-saved)
///   SP = RSP
///   FP = RBP
///
/// ARM64 mapping (future):
///   R0 = X0 (return value, arg0)
///   R1 = X1 (arg1)
///   ...
///   R8-R11 = X19-X22 (callee-saved)
///   SP = SP
///   FP = FP (X29)
/// </summary>
public enum VReg : byte
{
    // Scratch registers (caller-saved, for temporaries)
    R0 = 0,   // Return value + primary scratch (x64: RAX)
    R1 = 1,   // Arg0 (x64: RCX)
    R2 = 2,   // Arg1 (x64: RDX)
    R3 = 3,   // Arg2 (x64: R8)
    R4 = 4,   // Arg3 (x64: R9)
    R5 = 5,   // Scratch (x64: R10)
    R6 = 6,   // Scratch (x64: R11)

    // Callee-saved registers (must be preserved across calls)
    R7 = 7,   // Callee-saved (x64: RBX)
    R8 = 8,   // Callee-saved (x64: R12)
    R9 = 9,   // Callee-saved (x64: R13)
    R10 = 10, // Callee-saved (x64: R14)
    R11 = 11, // Callee-saved (x64: R15)

    // Frame/stack registers
    SP = 16,  // Stack pointer
    FP = 17,  // Frame pointer

    // Semantic aliases (for clarity in IL compilation)
    Return = R0,
    Arg0 = R1,
    Arg1 = R2,
    Arg2 = R3,
    Arg3 = R4,
}

/// <summary>
/// Floating-point/SIMD registers (architecture-neutral).
///
/// x64: XMM0-XMM7
/// ARM64: V0-V7 (NEON)
/// </summary>
public enum VRegF : byte
{
    F0 = 0,
    F1 = 1,
    F2 = 2,
    F3 = 3,
    F4 = 4,
    F5 = 5,
    F6 = 6,
    F7 = 7,

    // Semantic aliases
    ReturnF = F0,
    Arg0F = F0,
    Arg1F = F1,
    Arg2F = F2,
    Arg3F = F3,
}

/// <summary>
/// Condition codes for conditional branches.
/// Architecture-neutral - each arch maps to its native flags.
/// </summary>
public enum Condition : byte
{
    // Equality
    Equal = 0,        // ZF=1 on x64, EQ on ARM64
    NotEqual = 1,     // ZF=0 on x64, NE on ARM64

    // Signed comparisons
    LessThan = 2,         // SF!=OF on x64, LT on ARM64
    LessOrEqual = 3,      // ZF=1 || SF!=OF on x64, LE on ARM64
    GreaterThan = 4,      // ZF=0 && SF==OF on x64, GT on ARM64
    GreaterOrEqual = 5,   // SF==OF on x64, GE on ARM64

    // Unsigned comparisons
    Below = 6,            // CF=1 on x64, LO/CC on ARM64
    BelowOrEqual = 7,     // CF=1 || ZF=1 on x64, LS on ARM64
    Above = 8,            // CF=0 && ZF=0 on x64, HI on ARM64
    AboveOrEqual = 9,     // CF=0 on x64, HS/CS on ARM64
}
