# Tier 0 JIT Optimization Plan

**Status:** Phases 1-4 Complete
**Started:** December 2024

---

## Overview

The current Tier 0 JIT uses a pure stack-based evaluation model where every IL operation results in push/pop to memory. This plan outlines incremental optimizations that maintain correctness while reducing memory traffic.

### Goals
- Reduce push/pop memory operations by 50%+
- Maintain Tier 0 simplicity (no full register allocation)
- Keep code maintainable and easy to debug
- All existing tests must continue to pass

### Current Baseline (Pre-Optimization)

```
ldc.i4 5     →  mov rax, 5; push rax       (2 ops, 1 mem write)
ldc.i4 3     →  mov rax, 3; push rax       (2 ops, 1 mem write)
add          →  pop rdx; pop rax; add rax, rdx; push rax  (4 ops, 2 mem reads, 1 mem write)
stloc.0      →  pop rax; mov [rbp-8], rax  (2 ops, 1 mem read, 1 mem write)
```

Total: 10 operations, 6 memory accesses for `local = 5 + 3`

---

## Phase 1: Small Constant Optimization

**Status:** [x] COMPLETE

### Description
Optimize loading of small constants to use smaller instruction encodings.

### Changes
- [x] `ldc.i4 0` → `xor eax, eax` (2 bytes vs 10 bytes for mov rax, 0)
- [x] `ldc.i4 positive` → `mov eax, imm32` with implicit zero-extend (5 bytes vs 10)
- [x] `ldc.i4 negative` → `mov rax, imm64` (keep for sign-extension correctness)
- [x] `ldc.i8 0` → `xor eax, eax`
- [x] `ldc.i8 small positive` → `mov eax, imm32`

### Files Modified
- `src/kernel/Runtime/JIT/ILCompiler.cs` - `CompileLdcI4()`, `CompileLdcI8()`

### Testing
- [x] All 58 FullTest tests pass
- [x] All kernel tests pass

---

## Phase 2: Register Top-of-Stack (TOS) Caching

**Status:** [x] COMPLETE

### Description
Keep the top value of the evaluation stack in RAX instead of always pushing to memory. Only spill to memory when a second value needs to be pushed.

### Design

```
State Machine:
  TOS_EMPTY  - RAX does not contain a cached value (_tosCached = false)
  TOS_CACHED - RAX contains the top of eval stack (_tosCached = true)

Transitions:
  Push value (TOS_EMPTY)  → mov rax, value; state = TOS_CACHED
  Push value (TOS_CACHED) → push rax; mov rax, value; state = TOS_CACHED
  Pop to reg (TOS_CACHED) → mov reg, rax; state = TOS_EMPTY
  Pop to reg (TOS_EMPTY)  → pop reg; state = TOS_EMPTY
```

### Implementation

Helper methods added to ILCompiler:
- `SpillTOSIfCached()` - Push RAX to memory if cached, clear flag
- `MarkR0AsTOS(type)` - Set _tosCached = true, push type to type stack
- `FlushTOS()` - Ensure TOS is in memory (calls SpillTOSIfCached)
- `PopToR0()` - Pop using TOS cache (free operation when cached)
- `PopToReg(dst)` - Pop to specific register using TOS cache

Critical rule: Any operation using direct `X64Emitter.Pop()` must call `FlushTOS()` first.

### Changes
- [x] Add `_tosCached` boolean field to ILCompiler
- [x] Add `_tosType` byte field for float tracking
- [x] Add helper methods: SpillTOSIfCached, MarkR0AsTOS, FlushTOS, PopToR0, PopToReg
- [x] Update `CompileLdcI4`, `CompileLdloc`, `CompileLdarg` to use TOS
- [x] Update `CompileAdd`, `CompileSub`, `CompileMul`, etc. to use TOS
- [x] Update `CompileStloc`, `CompileStarg` to use TOS
- [x] Handle `dup` specially (TOS stays, push copy)
- [x] Flush TOS before branches (state must be consistent at join points)
- [x] Flush TOS before calls (RAX is caller-saved)
- [x] Add FlushTOS to all operations with direct Pop: fields, arrays, memory ops, etc.

### Files Modified
- `src/kernel/Runtime/JIT/ILCompiler.cs` - Core compilation logic

### Testing
- [x] All 58 FullTest tests pass
- [x] All kernel tests pass

---

## Phase 3: Peephole Optimization

**Status:** [x] COMPLETE

### Description
Eliminate redundant push/pop sequences by leveraging TOS caching in operations that previously used FlushTOS.

### Implementation

Instead of implementing a full deferred emission buffer, we use the existing TOS caching mechanism. We replaced `FlushTOS(); Pop(); Pop();` patterns with `PopToReg(); PopToR0();`, which:

1. If TOS is cached: `mov rdx, rax` instead of `push rax; pop rdx`
2. If TOS not cached: regular `pop rdx; pop rax`

This eliminates push/pop pairs when TOS is cached, achieving peephole benefits with minimal code changes.

### Changes Applied
- [x] CompileAdd (float path): Use PopToReg + PopToR0 + MarkR0AsTOS
- [x] CompileSub (float path): Use PopToReg + PopToR0 + MarkR0AsTOS
- [x] CompileMul (float path): Use PopToReg + PopToR0 + MarkR0AsTOS
- [x] CompileShr: Use PopToReg + PopToR0 + MarkR0AsTOS
- [x] CompileLdsfld: Added SpillTOSIfCached before using R0

### Key Fix
The initial implementation of shift operations caused test failures. The root cause was that `CompileLdsfld` was using R0 directly without first spilling any cached TOS value. Adding `SpillTOSIfCached()` to `CompileLdsfld` resolved the issue.

### Files Modified
- `src/kernel/Runtime/JIT/ILCompiler.cs` - Float arithmetic, shift ops, static field loads

### Testing
- [x] All 58 FullTest tests pass
- [x] All kernel tests pass

---

## Phase 4: Constant Folding

**Status:** [x] COMPLETE

### Description
Defer constant materialization and fold unary operations at compile time.

### Implementation

Track whether TOS is a known compile-time constant:
```csharp
_tosIsConst: bool      // True if TOS is a constant (not yet materialized)
_tosConstValue: long   // The constant value
```

When a constant is loaded (`ldc.i4`), we don't emit code immediately. Instead, we mark TOS as a constant. When the value is actually needed (for arithmetic, storage, etc.), we materialize it.

For unary operations like `neg` and `not`, if TOS is a constant, we compute the result at compile time and keep it as a constant - no code emitted at all.

### Changes Applied
- [x] Add `_tosIsConst` and `_tosConstValue` fields
- [x] Add `MarkConstAsTOS(value, type)` helper to defer constant emission
- [x] Add `MaterializeConstToR0()` helper for when constant needs to be in register
- [x] Update `SpillTOSIfCached()` to handle constants
- [x] Update `PopToR0()` and `PopToReg()` to handle constants
- [x] Update `CompileLdcI4` to use deferred constant emission
- [x] Update `CompileNeg` to fold constants at compile time
- [x] Update `CompileNot` to fold constants at compile time

### Example Optimization

```
; Before (ldc.i4 5; neg):
mov eax, 5
push rax
pop rax
neg rax
push rax

; After (constant folded):
; No code until value is used, then:
mov eax, -5  ; or xor eax,eax if result is 0
```

### Files Modified
- `src/kernel/Runtime/JIT/ILCompiler.cs` - Constant tracking and unary op folding

### Testing
- [x] All 58 FullTest tests pass
- [x] All kernel tests pass

---

## Phase 5: Memory Operand Fusion (Future)

**Status:** [ ] Not Started (Deferred)

### Description
Use x64 memory operands directly instead of load-to-register-then-operate.

```
; Current:
ldloc.0         →  mov rax, [rbp-8]; push rax
add             →  pop rdx; pop rax; add rax, rdx; push rax

; Fused:
ldloc.0; add    →  pop rax; add rax, [rbp-8]; push rax
```

### Notes
- More complex: requires look-ahead or pattern matching
- Deferred to later phase
- May conflict with TOS caching (need careful design)

---

## Testing Strategy

### Regression Tests
1. Run `./build.sh` - must complete without errors
2. Run `./dev.sh ./run.sh` - all tests must pass
3. Specifically verify:
   - Arithmetic tests (add, sub, mul, div)
   - Branch tests (forward and backward)
   - Method call tests (including recursive)
   - Exception handling tests
   - Float/double tests

### Performance Validation
- Count emitted instructions (before/after)
- Measure code size (before/after)
- Boot time should not regress

---

## Implementation Order

1. **Phase 1: Small Constants** - Quick win, minimal risk
2. **Phase 2: TOS Caching** - Biggest impact, moderate complexity
3. **Phase 3: Peephole** - Good cleanup, can skip if TOS works well
4. **Phase 4: Constant Folding** - Nice to have, lower priority
5. **Phase 5: Memory Fusion** - Future work

---

## Progress Log

| Date | Phase | Status | Notes |
|------|-------|--------|-------|
| 2025-12-06 | Phase 4 | COMPLETE | Constant folding: deferred constant materialization, compile-time folding for neg/not. All 58 tests pass. |
| 2025-12-06 | Phase 3 | COMPLETE | Peephole optimization: float arithmetic and shift ops use TOS caching. Fixed CompileLdsfld to spill TOS. All 58 tests pass. |
| 2025-12-06 | Phase 1 | COMPLETE | Small constant opt: xor for 0, mov eax for positive, mov rax for negative. All 58 tests pass. |
| 2025-12-05 | Phase 2 | COMPLETE | TOS caching: keep top eval stack value in RAX. Added SpillTOSIfCached, MarkR0AsTOS, FlushTOS, PopToR0, PopToReg helpers. All 58 tests pass. |

---

*This document will be updated as implementation progresses.*
