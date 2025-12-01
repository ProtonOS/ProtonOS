# ProtonJIT Implementation Checklist

This document tracks implementation progress for Phase 6 (JIT Compiler).

**Location**: `src/kernel/Runtime/JIT/`

**Reference**: `dotnet/src/coreclr/jit/` (RyuJIT source for reference)

**Strategy**: Same as metadata reader - implement our own clean version using RyuJIT
as reference code, not integrating it directly.

---

## Phase 6.0: CPU Feature Enablement (Prerequisites)

Before generating JIT code, we must ensure the CPU features we'll use are enabled.
This is a kernel-level prerequisite.

### Research

- [x] Review current CR0/CR4 settings in `Arch.Init()`
- [x] Understand x64 FPU/SSE/AVX state saving requirements
- [x] XSAVE area layout for context switches
- [x] CPUID feature detection

**Key Control Register Bits:**
- CR0.EM (bit 2) - Must be 0 for FPU/SSE
- CR0.TS (bit 3) - Task switched, triggers #NM for lazy FPU save
- CR0.MP (bit 1) - Monitor coprocessor
- CR0.NE (bit 5) - Numeric error (native FPU exceptions)
- CR4.OSFXSR (bit 9) - OS supports FXSAVE/FXRSTOR
- CR4.OSXMMEXCPT (bit 10) - OS supports SSE exceptions
- CR4.OSXSAVE (bit 18) - OS supports XSAVE

### Implementation

**File**: `src/kernel/x64/Arch.cs` or new `src/kernel/x64/CPUFeatures.cs`

#### 6.0.1 Basic FPU/SSE Setup
- [x] Clear CR0.EM (enable FPU) - verified CR0.EM=0 (UEFI sets this)
- [x] Set CR0.MP (monitor coprocessor) - verified CR0.MP=1
- [x] Set CR0.NE (native FPU exceptions) - verified CR0.NE=1
- [x] Set CR4.OSFXSR (enable FXSAVE/FXRSTOR) - verified CR4.OSFXSR=1
- [x] Set CR4.OSXMMEXCPT (enable SSE exceptions) - verified CR4.OSXMMEXCPT=1
- [x] Execute FNINIT to initialize FPU - implemented in CPUFeatures.Init()

#### 6.0.2 CPUID Feature Detection
- [x] Implement `CPUID` wrapper in native.asm (cpuid_ex with subleaf support)
- [x] Detect SSE/SSE2/SSE3/SSSE3/SSE4.1/SSE4.2 - implemented in CPUFeatures.cs
- [x] Detect AVX/AVX2 support - implemented (not available in QEMU by default)
- [x] Detect XSAVE support - implemented
- [x] Store detected features for JIT to query - CPUFeatures.HasFeature()

#### 6.0.3 XSAVE Setup (if supported)
- [x] Set CR4.OSXSAVE (enable XSAVE) - implemented, skipped if not supported
- [x] Set XCR0 to enable desired state components - xsetbv implemented
- [x] Calculate XSAVE area size for context switches - CPUFeatures.ExtendedStateSize (832 bytes with AVX)
- [x] Update thread context structure for extended state - Thread.ExtendedState, Thread.ExtendedStateSize

#### 6.0.4 Context Switch Updates
- [x] Save FPU/SSE state on context switch (FXSAVE or XSAVE) - CPU.SaveExtendedState()
- [x] Restore FPU/SSE state on context switch (FXRSTOR or XRSTOR) - CPU.RestoreExtendedState()
- [ ] Consider lazy FPU save optimization (use CR0.TS + #NM handler) - deferred, eager save works

### Test
- [x] Dynamic code execution works (4 test cases passing)
- [ ] JIT'd code using float/double works correctly
- [x] Context switches preserve FPU state between threads - XSAVE/XRSTOR integrated in Scheduler.Schedule()
- [x] SSE instructions execute without #UD (verified via CR0/CR4 state)

---

## Phase 6.1: Naive JIT (Correctness First)

Goal: Generate correct code with no optimization. Simple 1:1 IL to x64 translation.

### Research

- [ ] IL opcode reference (ECMA-335 Partition III)
- [ ] x64 calling conventions (Microsoft x64 ABI)
- [ ] RyuJIT importer (`dotnet/src/coreclr/jit/importer.cpp`)
- [ ] RyuJIT emitter (`dotnet/src/coreclr/jit/emit*.cpp`)

**Key RyuJIT Files:**
| File | Purpose |
|------|---------|
| `importer.cpp` | IL import to GenTree IR |
| `codegenxarch.cpp` | x64 code generation |
| `emitxarch.cpp` | x64 instruction encoding |
| `compiler.h` | Main compiler data structures |
| `gentree.h` | GenTree IR node definitions |

### Infrastructure

**File**: `src/kernel/Runtime/JIT/`

#### 6.1.1 Code Buffer
- [ ] Executable memory allocation from kernel heap
- [ ] Write pointer with bounds checking
- [ ] Emit helper methods: `EmitByte`, `EmitInt32`, `EmitInt64`
- [ ] Relocation tracking for fixups

#### 6.1.2 x64 Instruction Encoding
- [ ] REX prefix generation
- [ ] ModR/M and SIB byte encoding
- [ ] Displacement encoding (8/32 bit)
- [ ] Immediate encoding
- [ ] Common instructions: MOV, ADD, SUB, CMP, JMP, JCC, CALL, RET, PUSH, POP

#### 6.1.3 Method Prologue/Epilogue
- [ ] Stack frame setup (push rbp, mov rbp rsp, sub rsp N)
- [ ] Callee-saved register preservation
- [ ] Local variable space allocation
- [ ] Argument handling (rcx, rdx, r8, r9, stack)
- [ ] Return value handling (rax)
- [ ] Stack frame teardown

### IL Opcodes (Basic)

Implement opcodes needed for simple methods first:

#### 6.1.4 Constants and Locals
- [ ] `ldc.i4.*` - Load int32 constant
- [ ] `ldc.i8` - Load int64 constant
- [ ] `ldc.r4` - Load float32 constant
- [ ] `ldc.r8` - Load float64 constant
- [ ] `ldnull` - Load null reference
- [ ] `ldloc.*` - Load local variable
- [ ] `stloc.*` - Store local variable
- [ ] `ldloca.*` - Load local variable address

#### 6.1.5 Arguments
- [ ] `ldarg.*` - Load argument
- [ ] `starg.*` - Store argument
- [ ] `ldarga.*` - Load argument address

#### 6.1.6 Arithmetic
- [ ] `add`, `sub`, `mul`, `div`, `rem`
- [ ] `neg` - Negate
- [ ] `and`, `or`, `xor`, `not`
- [ ] `shl`, `shr`, `shr.un`
- [ ] Overflow variants (`.ovf`)

#### 6.1.7 Comparison and Branch
- [ ] `ceq`, `cgt`, `clt` (and unsigned variants)
- [ ] `br.*` - Unconditional branch
- [ ] `beq`, `bne`, `blt`, `bgt`, `ble`, `bge` (and unsigned)
- [ ] `brfalse`, `brtrue`
- [ ] `switch` - Jump table

#### 6.1.8 Conversion
- [ ] `conv.i1`, `conv.i2`, `conv.i4`, `conv.i8`
- [ ] `conv.u1`, `conv.u2`, `conv.u4`, `conv.u8`
- [ ] `conv.r4`, `conv.r8`
- [ ] `conv.ovf.*` variants

#### 6.1.9 Method Calls
- [ ] `call` - Direct call
- [ ] `callvirt` - Virtual call (requires vtable lookup)
- [ ] `ret` - Return
- [ ] Argument marshaling per x64 ABI
- [ ] Return value handling

#### 6.1.10 Object Operations
- [ ] `newobj` - Allocate and call constructor
- [ ] `ldfld`, `stfld` - Instance field access
- [ ] `ldsfld`, `stsfld` - Static field access
- [ ] `ldflda`, `ldsflda` - Field address
- [ ] `ldlen` - Array length
- [ ] `ldelem.*`, `stelem.*` - Array element access
- [ ] `ldelema` - Array element address

#### 6.1.11 Type Operations
- [ ] `castclass` - Cast with exception on failure
- [ ] `isinst` - Cast returning null on failure
- [ ] `box`, `unbox`, `unbox.any`
- [ ] `ldtoken` - Load metadata token
- [ ] `sizeof` - Type size

#### 6.1.12 Indirect Operations
- [ ] `ldind.*` - Load indirect
- [ ] `stind.*` - Store indirect
- [ ] `ldobj`, `stobj` - Load/store value type
- [ ] `cpobj`, `initobj` - Copy/initialize value type
- [ ] `cpblk`, `initblk` - Block copy/initialize

### Test

- [ ] Compile simple `int Add(int a, int b)` method
- [ ] Verify correct result when called
- [ ] Test all basic arithmetic opcodes
- [ ] Test branches and loops
- [ ] Test method calls between JIT'd methods

---

## Phase 6.2: Exception Handling

JIT'd code needs to support try/catch/finally.

### Research

- [ ] How NativeAOT EH works (our current implementation)
- [ ] EH clause format in method body
- [ ] Funclet-based exception handling
- [ ] Stack unwinding

### Implementation

- [ ] Parse EH clauses from method body
- [ ] Generate try region code
- [ ] Generate catch handler funclets
- [ ] Generate finally handler funclets
- [ ] Hook into existing exception dispatch
- [ ] Generate appropriate GCInfo for handlers

---

## Phase 6.3: GC Integration

JIT'd code must be GC-safe.

### Research

- [ ] GCInfo format we already decode (from Phase 3)
- [ ] How to emit GCInfo for JIT'd code
- [ ] Safe point identification (call sites)
- [ ] Stack slot liveness tracking

### Implementation

- [ ] Track which locals contain GC references
- [ ] Emit GCInfo describing stack layout
- [ ] Mark safe points (call sites)
- [ ] Register compiled method with code manager
- [ ] Verify GC can walk JIT'd frames

---

## Phase 6.4: Basic Optimizations

Low-hanging fruit after correctness is achieved.

### Constant Folding
- [ ] Fold constant arithmetic at compile time
- [ ] Fold constant comparisons
- [ ] Dead branch elimination from constant conditions

### Peephole Optimizations
- [ ] Eliminate redundant loads/stores
- [ ] Strength reduction (mul by power of 2 → shift)
- [ ] Combine adjacent stack operations

### Simple Register Allocation
- [ ] Keep frequently-used values in registers
- [ ] Avoid unnecessary spills to stack
- [ ] Track register liveness

---

## Phase 6.5: SSA Transform (Future)

Foundation for advanced optimizations.

- [ ] Build control flow graph
- [ ] Compute dominators and dominance frontiers
- [ ] Insert phi nodes
- [ ] Rename variables to SSA form

---

## Phase 6.6: Advanced Optimizations (Future)

Major performance wins, implement as needed.

### Inlining
- [ ] Identify inline candidates (small methods, hot paths)
- [ ] Inline method body at call site
- [ ] Handle inlined method's locals and EH

### Common Subexpression Elimination
- [ ] Identify repeated computations
- [ ] Compute once, reuse result

### Loop Optimizations
- [ ] Loop invariant code motion
- [ ] Loop unrolling (simple cases)
- [ ] Strength reduction in loops

---

## Phase 6.7: Better Register Allocation (Future)

- [ ] Linear scan register allocation
- [ ] Live range computation
- [ ] Spill cost estimation
- [ ] Register coalescing

---

## Testing Strategy

### Unit Tests (per opcode)
Each IL opcode should have a minimal test method:
```csharp
// Test: ldc.i4 + add + ret
static int TestAdd() => 1 + 2;  // Should return 3

// Test: ldarg + add + ret
static int TestAddArgs(int a, int b) => a + b;

// Test: branch
static int TestBranch(int x) => x > 0 ? 1 : -1;
```

### Integration Tests
- [ ] Compile and run MetadataTest.dll methods
- [ ] Verify all test methods produce correct results
- [ ] Test interop between AOT kernel code and JIT'd code

### Validation
- [ ] Compare JIT output to expected x64 assembly
- [ ] Verify GC can collect while JIT'd code is on stack
- [ ] Stress test with many compiled methods

---

## Reference Files in RyuJIT

Key files to study:

| File | Purpose |
|------|---------|
| `compiler.cpp` | Main compilation driver |
| `importer.cpp` | IL → IR translation |
| `morph.cpp` | Tree transformations |
| `liveness.cpp` | Liveness analysis |
| `lower.cpp` | IR → machine lowering |
| `codegenxarch.cpp` | x64 code generation |
| `emitxarch.cpp` | x64 instruction encoding |
| `gcinfo.cpp` | GC info emission |
| `unwind.cpp` | Unwind info emission |

---

## Notes

### x64 Calling Convention (Microsoft)
- Arguments: RCX, RDX, R8, R9, then stack (right to left)
- Return: RAX (integer/pointer), XMM0 (float/double)
- Caller-saved: RAX, RCX, RDX, R8-R11, XMM0-XMM5
- Callee-saved: RBX, RBP, RDI, RSI, R12-R15, XMM6-XMM15
- Stack aligned to 16 bytes at call site
- 32-byte shadow space for first 4 args

### Stack Frame Layout
```
[Higher addresses]
+------------------------+
| Return address         | ← RSP at function entry
+------------------------+
| Saved RBP              | ← After push rbp
+------------------------+ ← RBP points here (after mov rbp, rsp)
| Local variable N       |
| ...                    |
| Local variable 0       |
+------------------------+
| Spill slots            |
+------------------------+
| Outgoing args (shadow) | ← 32 bytes minimum
+------------------------+ ← RSP during function body
[Lower addresses]
```

### IL Evaluation Stack
IL is stack-based. Naive JIT can mirror this with actual stack:
- Push = `push reg` or `sub rsp, 8; mov [rsp], reg`
- Pop = `pop reg` or `mov reg, [rsp]; add rsp, 8`
- Peek = `mov reg, [rsp]`

Better approach: track stack state, map to registers when possible.
