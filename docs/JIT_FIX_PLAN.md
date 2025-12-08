# JIT Compiler Comprehensive Fix Plan

## Executive Summary

The JIT compiler has systematic issues preventing proper driver development. This plan addresses all known bugs and gaps in a logical dependency order, with comprehensive tests for each fix.

### Completed Since Plan Draft
- Fixed large struct `stelem`/array store crash by correcting x64 immediate multiply encoding (`ImulRI` now emits full REX, enabling use of R8+). This unblocked `TestLargeStructArrayStore`.
- Added large value type handling for `stloc`: multi-slot structs are copied from the eval stack into locals, enabling large struct `ldelem` + `stloc` sequences. FullTest now passes 74 checks with `TestLargeStructArrayLoad` and `TestLargeStructArrayCopy` enabled.
- Enabled the struct object-initializer-out test (`TestObjectInitializerOut`) which now passes, raising FullTest to 75/75.
- **All large struct array tests now pass**: `TestLargeStructArrayStore`, `TestLargeStructArrayLoad`, `TestLargeStructArrayCopy` verified working with proper stack layout handling in `CompileStelemToken` and `CompileLdelemToken`.
- Added runtime debug helper `DebugStelemStack` to trace large struct stelem operations with correct x64 ABI register mappings (VReg.R3→R8, VReg.R4→R9).
- Fixed large struct `ldobj` to allocate eval stack space and copy multi-slot data.
- FullTest now passes all tests including large struct operations.

---

## Phase 1: Foundation Fixes (No Dependencies)

These fixes are isolated and don't depend on other changes.

### 1.1 Fix TOS Caching with `dup` + `initobj` Sequence
**Priority:** HIGH | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Object initializer syntax (`new Type { Field = value }`) generates `dup` before `initobj`, causing eval stack depth to be off by 1.

**Root Cause:**
- `dup` when TOS is cached pushes R0 to stack AND increments depth
- `initobj` calls `FlushTOS()` which pushes R0 again but doesn't adjust depth
- Result: Extra phantom item on eval stack

**Fix:**
- [x] In `CompileDup()`: When TOS is cached, track that the memory copy is the "real" value
- [x] In `FlushTOS()`: Don't push if value already has a memory copy from `dup`
- [x] Alternative: Make `initobj` aware of preceding `dup` and adjust stack tracking

**Tests to Add:**
```csharp
// Test object initializer on struct
public static int TestStructObjectInitializer()
{
    SimpleStruct s = new SimpleStruct { X = 10, Y = 20 };
    return s.X + s.Y;  // Expected: 30
}

// Test object initializer with out parameter
public static int TestOutParamObjectInitializer()
{
    InitStruct(out var result);
    return result.X;  // Expected: 42
}
private static void InitStruct(out SimpleStruct s)
{
    s = new SimpleStruct { X = 42, Y = 0 };
}
```
**Status:** Implemented and covered by `TestObjectInitializer` and `TestObjectInitializerOut` in FullTest (both passing).

---

### 1.2 Fix `ldfld` on Value Types from Stack
**Priority:** CRITICAL | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** When accessing fields of a value type loaded via `ldloc` or `ldarg`, JIT treats value as pointer and dereferences garbage.

**Root Cause:**
- `CompileLdfld` always emits `mov reg, [reg + offset]` (dereference)
- For value types ON the stack, should emit `mov reg, [RSP + stackOffset + fieldOffset]`
- Stack metadata tracks `StackType_ValueType` but `ldfld` doesn't check it

**Fix:**
- [ ] In `CompileLdfld()`: Check if TOS type is `StackType_ValueType`
- [ ] If value type: Calculate field offset from current stack position, not dereference
- [ ] Track struct size in stack metadata (not just classification)
- [ ] Handle nested field access (value type containing value type)

**Tests to Add:**
```csharp
// Test field access on local struct
public static int TestLocalStructFieldAccess()
{
    SimpleStruct s;
    s.X = 42;
    s.Y = 10;
    return s.X;  // Expected: 42
}

// Test field access on struct parameter
public static int TestParamStructFieldAccess()
{
    SimpleStruct s = new SimpleStruct { X = 100, Y = 200 };
    return GetX(s);
}
private static int GetX(SimpleStruct s)
{
    return s.X;  // Expected: 100
}

// Test nested struct field access
public static int TestNestedStructField()
{
    OuterStruct outer;
    outer.Inner.Value = 55;
    return outer.Inner.Value;  // Expected: 55
}
```

---

### 1.3 Fix Large Struct Support in `ldobj`
**Priority:** HIGH | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** `ldobj` only supports structs <= 8 bytes, returns error for larger.

**Current Code (line ~4987):**
```csharp
// For larger structs, we'd need to copy to stack
// For now, just handle common small sizes
```

**Fix:**
- [x] For structs > 8 bytes: Allocate stack space, emit inline copy loop
- [ ] Use rep movsq for large copies (>= 64 bytes) (optimization, not required)
- [x] Track struct size on eval stack for subsequent operations

**Status:** Implemented and tested - large structs up to 24+ bytes work correctly.

**Tests to Add:**
```csharp
public struct LargeStruct  // 48 bytes
{
    public long A, B, C, D, E, F;
}

public static int TestLargeStructLoad()
{
    LargeStruct* ptr = stackalloc LargeStruct[1];
    ptr->A = 1; ptr->B = 2; ptr->C = 3;
    LargeStruct copy = *ptr;  // ldobj
    return (int)(copy.A + copy.B + copy.C);  // Expected: 6
}
```

---

## Phase 2: Struct Parameter/Return Fixes

These depend on Phase 1 stack tracking improvements.

### 2.1 Fix Struct Return Values (Small Structs)
**Priority:** HIGH | **Complexity:** High | **Files:** `ILCompiler.cs`, `X64Emitter.cs`

**Problem:** All struct returns treated as 64-bit pointer in RAX, no size checking.

**x64 ABI Rules:**
- Structs <= 8 bytes: Return in RAX
- Structs 9-16 bytes: Return in RAX:RDX
- Structs > 16 bytes: Caller provides hidden buffer pointer in RCX

**Fix:**
- [ ] Track return type size in method signature parsing
- [ ] For <= 8 bytes: Current behavior (RAX)
- [ ] For 9-16 bytes: Use RAX:RDX pair
- [ ] For > 16 bytes: Add hidden first parameter for return buffer

**Tests to Add:**
```csharp
public struct Small8 { public long Value; }  // 8 bytes - RAX
public struct Medium16 { public long A, B; }  // 16 bytes - RAX:RDX
public struct Large32 { public long A, B, C, D; }  // 32 bytes - hidden buffer

public static int TestSmallStructReturn()
{
    Small8 s = GetSmall8();
    return (int)s.Value;  // Expected: 42
}
private static Small8 GetSmall8() => new Small8 { Value = 42 };

public static int TestMediumStructReturn()
{
    Medium16 m = GetMedium16();
    return (int)(m.A + m.B);  // Expected: 30
}
private static Medium16 GetMedium16() => new Medium16 { A = 10, B = 20 };

public static int TestLargeStructReturn()
{
    Large32 l = GetLarge32();
    return (int)(l.A + l.B + l.C + l.D);  // Expected: 100
}
private static Large32 GetLarge32() => new Large32 { A = 10, B = 20, C = 30, D = 40 };
```

---

### 2.2 Fix Struct Copying from Arrays to Locals
**Priority:** HIGH | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** `var bar = bars[i]` produces broken code - struct not properly copied.

**Root Cause:** `ldelem` for value types needs to copy entire struct, not just pointer.

**Fix:**
- [x] In `CompileLdelem()`: Detect value type element
- [x] For value types: Allocate stack space, copy struct data
- [x] Track copied struct on eval stack with proper size

**Status:** Implemented - `TestLargeStructArrayLoad` and `TestLargeStructArrayCopy` both pass.

**Tests to Add:**
```csharp
public static int TestArrayStructCopy()
{
    SimpleStruct[] arr = new SimpleStruct[3];
    arr[0] = new SimpleStruct { X = 10, Y = 20 };
    arr[1] = new SimpleStruct { X = 30, Y = 40 };

    SimpleStruct copy = arr[0];  // Should copy, not reference
    copy.X = 999;  // Modify copy

    return arr[0].X;  // Expected: 10 (original unchanged)
}

public static int TestArrayStructFieldAccess()
{
    SimpleStruct[] arr = new SimpleStruct[2];
    arr[0].X = 42;
    arr[0].Y = 58;

    SimpleStruct s = arr[0];
    return s.X + s.Y;  // Expected: 100
}
```

---

### 2.3 Fix `ref`/`out` Parameter Handling
**Priority:** HIGH | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Passing `class.struct.field` as out parameter fails due to chained address computation.

**Fix:**
- [ ] For `ldarga`/`ldloca`: Properly compute address for nested access
- [ ] Track that result is a managed pointer (byref)
- [ ] In `stind`/`ldind`: Properly handle byref to struct fields

**Tests to Add:**
```csharp
public static int TestSimpleOutParam()
{
    int result;
    SetValue(out result);
    return result;  // Expected: 42
}
private static void SetValue(out int x) { x = 42; }

public static int TestStructOutParam()
{
    SimpleStruct s;
    InitializeStruct(out s);
    return s.X + s.Y;  // Expected: 30
}
private static void InitializeStruct(out SimpleStruct s)
{
    s.X = 10;
    s.Y = 20;
}

public static int TestRefParam()
{
    int value = 10;
    AddTen(ref value);
    return value;  // Expected: 20
}
private static void AddTen(ref int x) { x += 10; }

public static int TestNestedFieldOut()
{
    Container c = new Container();
    SetInnerValue(out c.Inner.Value);
    return c.Inner.Value;  // Expected: 99
}
```

---

## Phase 3: Control Flow Fixes

### 3.1 Fix If-Else Chain Execution
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Multiple branches of if-else chain may execute when they shouldn't.

**Root Cause:** Branch target resolution or conditional jump encoding issues.

**Fix:**
- [ ] Audit `beq`, `bne`, `bgt`, `blt`, `bge`, `ble` implementations
- [ ] Verify branch offset calculations for forward jumps
- [ ] Check that fall-through doesn't skip to wrong location

**Tests to Add:**
```csharp
public static int TestIfElseChain()
{
    int result = 0;
    int x = 2;

    if (x == 1) result = 10;
    else if (x == 2) result = 20;
    else if (x == 3) result = 30;
    else result = 40;

    return result;  // Expected: 20
}

public static int TestIfElseChainWithReturns()
{
    return GetValueForCode(3);  // Expected: 30
}
private static int GetValueForCode(int code)
{
    if (code == 1) return 10;
    if (code == 2) return 20;
    if (code == 3) return 30;
    return 0;
}
```

---

### 3.2 Fix Break/Continue in Loops
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** `break` and `continue` statements may not work correctly.

**Fix:**
- [ ] Track loop entry and exit points during compilation
- [ ] Ensure `br` to loop exit (break) works correctly
- [ ] Ensure `br` to loop header (continue) works correctly

**Tests to Add:**
```csharp
public static int TestBreakInLoop()
{
    int sum = 0;
    for (int i = 0; i < 100; i++)
    {
        if (i == 5) break;
        sum += i;
    }
    return sum;  // Expected: 0+1+2+3+4 = 10
}

public static int TestContinueInLoop()
{
    int sum = 0;
    for (int i = 0; i < 10; i++)
    {
        if (i % 2 == 0) continue;  // Skip evens
        sum += i;
    }
    return sum;  // Expected: 1+3+5+7+9 = 25
}

public static int TestNestedBreak()
{
    int count = 0;
    for (int i = 0; i < 10; i++)
    {
        for (int j = 0; j < 10; j++)
        {
            if (j == 3) break;  // Inner break
            count++;
        }
    }
    return count;  // Expected: 10 * 3 = 30
}
```

---

### 3.3 Fix Complex Loop Conditions
**Priority:** MEDIUM | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Problem:** `while (cond1 && cond2)` may be miscompiled.

**Fix:**
- [ ] Verify short-circuit evaluation in `and`/`or` with branches
- [ ] Check `brfalse`/`brtrue` after comparison sequences

**Tests to Add:**
```csharp
public static int TestWhileWithAndCondition()
{
    int i = 0;
    int sum = 0;
    while (i < 10 && sum < 20)
    {
        sum += i;
        i++;
    }
    return sum;  // Expected: 0+1+2+3+4+5 = 15, then 15+6=21 > 20, so 21
}

public static int TestWhileWithOrCondition()
{
    int x = 0;
    while (x < 5 || x == 7)
    {
        x++;
        if (x == 10) break;  // Safety
    }
    return x;  // Expected: 5 (first condition false at 5, second false)
}
```

---

## Phase 4: Object/Boxing Operations

### 4.1 Fix `newobj` for Value Types with >4 Constructor Args
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Value type constructors limited to 4 arguments.

**Fix:**
- [ ] Extend newobj to handle 5+ args using stack-based passing
- [ ] Reuse the >4 args infrastructure from regular calls

**Tests to Add:**
```csharp
public struct MultiFieldStruct
{
    public int A, B, C, D, E;
    public MultiFieldStruct(int a, int b, int c, int d, int e)
    {
        A = a; B = b; C = c; D = d; E = e;
    }
}

public static int TestNewObjManyArgs()
{
    var s = new MultiFieldStruct(1, 2, 3, 4, 5);
    return s.A + s.B + s.C + s.D + s.E;  // Expected: 15
}
```

---

### 4.2 Fix `box`/`unbox` for Various Sizes
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Box/unbox assume 64-bit values, don't handle arbitrary struct sizes.

**Fix:**
- [ ] Get actual type size from metadata
- [ ] Allocate correct size on heap for box
- [ ] Copy correct number of bytes

**Tests to Add:**
```csharp
public static int TestBoxInt()
{
    int x = 42;
    object boxed = x;
    int unboxed = (int)boxed;
    return unboxed;  // Expected: 42
}

public static int TestBoxStruct()
{
    SimpleStruct s = new SimpleStruct { X = 10, Y = 20 };
    object boxed = s;
    SimpleStruct unboxed = (SimpleStruct)boxed;
    return unboxed.X + unboxed.Y;  // Expected: 30
}

public static int TestBoxLargeStruct()
{
    LargeStruct l = new LargeStruct { A = 1, B = 2, C = 3, D = 4, E = 5, F = 6 };
    object boxed = l;
    LargeStruct unboxed = (LargeStruct)boxed;
    return (int)(unboxed.A + unboxed.F);  // Expected: 7
}
```

---

## Phase 5: Instance Member Support

### 5.1 Verify Instance Field Access
**Priority:** HIGH | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Current Status:** Marked as implemented but needs verification with tests.

**Tests to Add:**
```csharp
public static int TestInstanceFieldReadWrite()
{
    SimpleClass obj = new SimpleClass(0);
    obj.SetValue(42);
    return obj.GetValue();  // Expected: 42
}

public static int TestMultipleInstanceFields()
{
    var obj = new MultiFieldClass();
    obj.A = 10;
    obj.B = 20;
    obj.C = 30;
    return obj.A + obj.B + obj.C;  // Expected: 60
}
```

---

### 5.2 Verify Instance Method Calls
**Priority:** HIGH | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Tests to Add:**
```csharp
public static int TestInstanceMethodCall()
{
    var calc = new Calculator();
    return calc.Add(10, 20);  // Expected: 30
}

public static int TestInstanceMethodChain()
{
    var builder = new ValueBuilder();
    builder.Add(10).Add(20).Add(30);
    return builder.GetValue();  // Expected: 60
}
```

---

## Phase 6: Stack Metadata Enhancement

### 6.1 Track Struct Sizes on Eval Stack
**Priority:** HIGH | **Complexity:** High | **Files:** `ILCompiler.cs`

**Problem:** Stack metadata only tracks type classification, not size.

**Fix:**
- [ ] Add `_evalStackSizes[]` array alongside `_evalStackTypes[]`
- [ ] Set size when pushing value types
- [ ] Use size information in `ldfld`, `stfld`, `ldobj`, `stobj`

---

### 6.2 Improve Local Variable Allocation
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `Tier0JIT.cs`

**Problem:** All locals get 64 bytes regardless of actual size.

**Fix:**
- [ ] Parse local signature to get actual types and sizes
- [ ] Allocate appropriate space per local
- [ ] Track offsets in a local offset table

---

## Implementation Order Checklist

### Week 1: Critical Path
- [ ] 1.2 Fix `ldfld` on value types from stack (CRITICAL - unblocks driver)
- [x] 1.1 Fix TOS caching with `dup` + `initobj`
- [x] Add basic struct tests to FullTest

### Week 2: Struct Operations (COMPLETE)
- [x] 1.3 Fix large struct support in `ldobj`
- [x] 2.2 Fix struct copying from arrays
- [x] Large struct array store (`stelem.any` for >8 byte structs)
- [x] Large struct array load (`ldelem.any` for >8 byte structs)
- [x] Large struct local store (`stloc` for multi-slot structs)
- [ ] 6.1 Add struct size tracking to eval stack (partial - works for arrays)

### Week 3: Parameter Handling
- [ ] 2.1 Fix struct return values
- [ ] 2.3 Fix ref/out parameter handling
- [ ] Add parameter tests to FullTest

### Week 4: Control Flow
- [ ] 3.1 Fix if-else chain execution
- [ ] 3.2 Fix break/continue in loops
- [ ] 3.3 Fix complex loop conditions

### Week 5: Objects and Boxing
- [ ] 4.1 Fix newobj for >4 constructor args
- [ ] 4.2 Fix box/unbox for various sizes
- [ ] 5.1/5.2 Verify instance member support

### Week 6: Polish
- [ ] 6.2 Improve local variable allocation
- [ ] Remove all workarounds from VirtioBlk driver
- [ ] Full regression test pass

---

## Test Infrastructure Additions

### New Test Classes to Add to FullTest:

```csharp
// StructTests - struct operations
// RefOutTests - ref/out parameters
// ObjectInitializerTests - object initializers
// NestedFieldTests - x.y.z access patterns
// LargeStructTests - structs > 8 bytes
// ControlFlowAdvancedTests - break/continue/complex conditions
// BoxingTests - box/unbox operations
// InstanceMemberTests - instance fields and methods
```

### Test Naming Convention:
- `Test<Feature><Variant>` e.g., `TestStructFieldAccess`, `TestStructFieldNested`
- Each test returns expected value or uses assertion pattern
- Group related tests in dedicated classes

---

## Files to Modify

| File | Changes |
|------|---------|
| `src/kernel/Runtime/JIT/ILCompiler.cs` | Core fixes for ldfld, initobj, ldobj, stobj, newobj |
| `src/kernel/Runtime/JIT/X64Emitter.cs` | Struct return value ABI support |
| `src/kernel/Runtime/JIT/Tier0JIT.cs` | Local allocation improvements |
| `src/test/FullTest/Program.cs` | Comprehensive new tests |
| `src/drivers/shared/storage/virtio-blk/VirtioBlkEntry.cs` | Remove workarounds |
| `src/drivers/shared/virtio/VirtioDevice.cs` | Remove workarounds |

---

## Success Criteria

1. All new tests pass in FullTest
2. VirtioBlk driver works without any workarounds
3. Object initializer syntax works naturally
4. Struct returns work for all sizes
5. ref/out parameters work for nested fields
6. Control flow (break/continue/if-else) works correctly
7. No regressions in existing tests
