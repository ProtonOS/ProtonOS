# JIT Optimization Removal Plan

**Status:** Not Started
**Goal:** Remove all Tier 0 JIT optimizations to restore a pure naive stack-based JIT

---

## Background

The Tier 0 JIT compiler currently has several optimization passes that were added in commits:
- `4dba103` - "Implement Tier 0 JIT optimizations: TOS caching and constant folding"
- `6e06bc8` - "Complete Tier 0 JIT optimizations with full constant folding"

These optimizations are causing a crash (Page Fault at CR2=0xA) in `TestLargeStructFields`. The value 0xA (10) is supposed to be stored in a field, but instead it's being used as a destination address. This indicates a bug in the TOS caching or constant folding logic.

Rather than continue debugging the complex optimization interactions, we're removing all optimizations to restore the naive stack-based JIT that worked correctly. Optimizations can be re-implemented later in a Tier 1 JIT.

---

## Reference Commit

The pre-optimization code is available at commit `482be73` ("Update README with current project status").

To view the original naive implementations:
```bash
git show 482be73:src/kernel/Runtime/JIT/ILCompiler.cs | grep -n "CompileLdcI4\|CompileLdloc\|CompileLdloca" -A 15
```

---

## Optimization Passes to Remove

### Phase 1: Small Constant Encoding (Minor Impact)
- **What it does:** Uses `xor eax, eax` for 0, `mov eax, imm32` for positive values
- **Why remove:** Ties into TOS caching state machine
- **Impact:** Code size slightly larger, but simpler

### Phase 2: TOS (Top-of-Stack) Caching (Major Impact)
- **What it does:** Keeps the top eval stack value in RAX instead of pushing to memory
- **State fields:**
  - `_tosCached` - True if RAX contains the TOS value
  - `_tosType` - Type of cached value
- **Helper methods:**
  - `SpillTOSIfCached()` - Push RAX to memory if cached
  - `MarkR0AsTOS()` - Mark RAX as containing TOS
  - `FlushTOS()` - Ensure TOS is in memory
  - `PopToR0()` - Pop using TOS cache
  - `PopToReg()` - Pop to specific register using TOS cache
- **Why remove:** Complex state machine with many interaction points, suspected source of bug

### Phase 3: Peephole Optimization (Moderate Impact)
- **What it does:** Uses TOS caching in float ops and shifts to eliminate push/pop pairs
- **Why remove:** Depends on Phase 2 TOS caching

### Phase 4: Constant Folding (Major Impact)
- **What it does:** Defers constant emission, folds unary/binary ops at compile time
- **State fields:**
  - `_tosIsConst` - True if TOS is a known constant (not yet emitted)
  - `_tosConstValue` - The constant value
  - `_tos2IsConst` - True if second-from-top is also a constant
  - `_tos2ConstValue` - The second constant value
  - `_tos2Type` - Type of second constant
- **Helper methods:**
  - `MarkConstAsTOS()` - Mark TOS as a deferred constant
  - `MaterializeConstToR0()` - Emit code to put constant in RAX
- **Why remove:** Most complex optimization, interacts with TOS caching

### X64Emitter Additions
- **Added methods:**
  - `ShlRI()` - Shift left by immediate
  - `SarRI()` - Arithmetic shift right by immediate
  - `ShrRI()` - Logical shift right by immediate
- **Keep or remove:** KEEP - these are useful general-purpose instructions not tied to optimizations

---

## Implementation Checklist

### Step 1: Remove State Fields from ILCompiler
File: `src/kernel/Runtime/JIT/ILCompiler.cs`

- [ ] Remove `_tosCached` (line ~470)
- [ ] Remove `_tosType` (line ~471)
- [ ] Remove `_tosIsConst` (line ~475)
- [ ] Remove `_tosConstValue` (line ~476)
- [ ] Remove `_tos2IsConst` (line ~480)
- [ ] Remove `_tos2ConstValue` (line ~481)
- [ ] Remove `_tos2Type` (line ~482)

### Step 2: Remove Initialization in CreateWithGCInfo
File: `src/kernel/Runtime/JIT/ILCompiler.cs`, in `CreateWithGCInfo()` method

- [ ] Remove `compiler._tosCached = false;` (line ~561)
- [ ] Remove `compiler._tosType = 0;` (line ~562)
- [ ] Remove `compiler._tosIsConst = false;` (line ~563)
- [ ] Remove `compiler._tosConstValue = 0;` (line ~564)
- [ ] Remove `compiler._tos2IsConst = false;` (line ~565)
- [ ] Remove `compiler._tos2ConstValue = 0;` (line ~566)
- [ ] Remove `compiler._tos2Type = 0;` (line ~567)

### Step 3: Remove Helper Methods
File: `src/kernel/Runtime/JIT/ILCompiler.cs`

- [ ] Remove `MaterializeConstToR0()` (lines ~2275-2294)
- [ ] Remove `SpillTOSIfCached()` (lines ~2296-2315)
- [ ] Remove `MarkR0AsTOS()` (lines ~2317-2327)
- [ ] Remove `MarkConstAsTOS()` (lines ~2329-2361)
- [ ] Remove `FlushTOS()` (lines ~2363-2371)
- [ ] Remove `PopToR0()` (lines ~2373-2412)
- [ ] Remove `PopToReg()` (lines ~2414-2460)

### Step 4: Restore Naive CompileLdcI4
File: `src/kernel/Runtime/JIT/ILCompiler.cs`

Current optimized version uses `SpillTOSIfCached`, `MarkConstAsTOS`, small constant encoding.

Restore to naive version:
```csharp
private bool CompileLdcI4(int value)
{
    // Push constant onto eval stack (sign-extended to 64-bit)
    X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)(long)value);
    X64Emitter.Push(ref _code, VReg.R0);
    _evalStackDepth++;
    PushStackType(StackType_Int);
    return true;
}
```

### Step 5: Restore Naive CompileLdcI8
Similar to CompileLdcI4:
```csharp
private bool CompileLdcI8(long value)
{
    X64Emitter.MovRI64(ref _code, VReg.R0, (ulong)value);
    X64Emitter.Push(ref _code, VReg.R0);
    _evalStackDepth++;
    PushStackType(StackType_Int);
    return true;
}
```

### Step 6: Restore Naive CompileLdloc
```csharp
private bool CompileLdloc(int index)
{
    int offset = X64Emitter.GetLocalOffset(index);
    X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
    X64Emitter.Push(ref _code, VReg.R0);
    _evalStackDepth++;
    // Note: May need type tracking logic here
    return true;
}
```

### Step 7: Restore Naive CompileLdloca
```csharp
private bool CompileLdloca(int index)
{
    int offset = X64Emitter.GetLocalOffset(index);
    X64Emitter.Lea(ref _code, VReg.R0, VReg.FP, offset);
    X64Emitter.Push(ref _code, VReg.R0);
    _evalStackDepth++;
    return true;
}
```

### Step 8: Restore Naive CompileStloc
```csharp
private bool CompileStloc(int index)
{
    X64Emitter.Pop(ref _code, VReg.R0);
    _evalStackDepth--;
    PopStackType();
    int offset = X64Emitter.GetLocalOffset(index);
    X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
    return true;
}
```

### Step 9: Restore Naive CompileLdarg
```csharp
private bool CompileLdarg(int index)
{
    int offset = X64Emitter.GetArgOffset(index);
    X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
    X64Emitter.Push(ref _code, VReg.R0);
    _evalStackDepth++;
    return true;
}
```

### Step 10: Restore Naive CompileStarg
```csharp
private bool CompileStarg(int index)
{
    X64Emitter.Pop(ref _code, VReg.R0);
    _evalStackDepth--;
    PopStackType();
    int offset = X64Emitter.GetArgOffset(index);
    X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
    return true;
}
```

### Step 11: Fix Arithmetic Ops - Remove TOS/Constant Folding

For each arithmetic operation, remove:
1. Calls to `FlushTOS()`
2. Calls to `PopToR0()` / `PopToReg()`
3. Constant folding logic (`if (_tosIsConst && _tos2IsConst)`)
4. Identity elimination (`x + 0 = x`, `x * 1 = x`, etc.)
5. Strength reduction (`x * 2^n = x << n`)

Restore to simple pattern:
```csharp
// Pop two values, operate, push result
X64Emitter.Pop(ref _code, VReg.R2);  // Second operand
X64Emitter.Pop(ref _code, VReg.R0);  // First operand
_evalStackDepth -= 2;
// ... perform operation ...
X64Emitter.Push(ref _code, VReg.R0);
_evalStackDepth++;
```

Methods to fix:
- [ ] `CompileAdd()` - Remove constant folding for `a + b`, identity for `x + 0`
- [ ] `CompileSub()` - Remove constant folding for `a - b`, identity for `x - 0`
- [ ] `CompileMul()` - Remove constant folding, identity `x * 1`, zero `x * 0`, strength reduction
- [ ] `CompileDiv()` - Remove constant folding, identity `x / 1`
- [ ] `CompileRem()` - Remove constant folding
- [ ] `CompileAnd()` - Remove constant folding, identity `x & -1`, zero `x & 0`
- [ ] `CompileOr()` - Remove constant folding, identity `x | 0`
- [ ] `CompileXor()` - Remove constant folding, identity `x ^ 0`
- [ ] `CompileShl()` - Remove constant folding, identity `x << 0`
- [ ] `CompileShr()` - Remove constant folding, identity `x >> 0`
- [ ] `CompileNeg()` - Remove constant folding for `-const`
- [ ] `CompileNot()` - Remove constant folding for `~const`

### Step 12: Fix Conv Operations
- [ ] `CompileConv()` - Remove `FlushTOS()` calls, use direct `Pop()`

### Step 13: Fix Field Operations
- [ ] `CompileLdfld()` - Remove `FlushTOS()` calls, use direct `Pop()`
- [ ] `CompileStfld()` - Remove `FlushTOS()` calls, use direct `Pop()`
- [ ] `CompileLdsfld()` - Remove `SpillTOSIfCached()` calls
- [ ] `CompileStsfld()` - Remove `FlushTOS()` calls

### Step 14: Fix Branch Operations
- [ ] Remove `FlushTOS()` before all branches (state must not carry across branches)
- [ ] Affected: `CompileBr`, `CompileBrfalse`, `CompileBrtrue`, `CompileBeq`, `CompileBne`, etc.

### Step 15: Fix Other Operations
- [ ] `CompileDup()` - Remove TOS special handling
- [ ] `CompilePop()` - Remove TOS special handling
- [ ] `CompileRet()` - Remove `FlushTOS()` calls
- [ ] `CompileCall()` - Remove `FlushTOS()` before calls
- [ ] `CompileCallvirt()` - Remove `FlushTOS()` before calls
- [ ] `CompileNewobj()` - Remove `FlushTOS()` before calls
- [ ] Array operations (`ldelem`, `stelem`, `newarr`, etc.) - Remove `FlushTOS()` calls

---

## Testing

After each step:
```bash
./build.sh 2>&1
./dev.sh ./run.sh 2>&1
./kill.sh
```

Expected result after full removal: All tests pass without Page Fault crashes.

---

## Files Modified

| File | Changes |
|------|---------|
| `src/kernel/Runtime/JIT/ILCompiler.cs` | Remove all optimization state, helpers, and restore naive implementations |

Note: `X64Emitter.cs` changes (ShlRI, SarRI, ShrRI) should be KEPT as they are useful instructions.

---

## Rollback

If needed, the optimized code can be recovered from:
- Commit `4dba103` - Initial optimization implementation
- Commit `6e06bc8` - Complete constant folding

Or simply: `git checkout 6e06bc8 -- src/kernel/Runtime/JIT/ILCompiler.cs`

---

## Future Work

Once the naive JIT is stable:
1. Re-implement optimizations in a separate Tier 1 JIT
2. Use proper SSA form or similar IR for optimization passes
3. Add comprehensive testing for each optimization
4. Consider using a proper register allocator instead of TOS caching
