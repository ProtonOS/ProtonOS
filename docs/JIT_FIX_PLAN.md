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
- **Struct Return Values (2.1) COMPLETE** (Dec 2025):
  - Small structs (≤8 bytes): Return in RAX - `TestSmallStructReturn` passes
  - Medium structs (9-16 bytes): Return in RAX:RDX pair - `TestMediumStructReturn` passes
  - Large structs (>16 bytes): Hidden buffer pointer in RCX - `TestLargeStructReturn` passes
  - Implementation includes: return type tracking in `Tier0JIT.cs`, callee-side `CompileRet` for multi-register/buffer returns, caller-side `CompileCall` for buffer allocation and return value handling
  - Fixed 16-byte alignment consistency between `ldelem` and `stloc` for large structs
- **FullTest now passes 78/78 tests** including all struct return scenarios.
- **ref/out Parameter Handling (2.3) COMPLETE** (Dec 2025):
  - Simple `out int` parameters work via `ldloca` + `stind.i4`
  - `ref int` parameters work via `ldloca` + `ldind.i4`/`stind.i4`
  - Struct out parameters already working (tested by `TestStructOutParam`, `TestObjectInitializerOut`)
  - Added `TestSimpleOutParam`, `TestRefParam`, `TestRefParamMultiple` tests
- **FullTest now passes 81/81 tests** including ref/out parameter scenarios.
- Added nested field out/ref tests (`class.struct.field` pattern) - critical for DDK development
  - `TestNestedFieldOut` - passes value through `out c.Inner.Value`
  - `TestNestedFieldRef` - modifies value through `ref c.Inner.Value`
- **FullTest now passes 83/83 tests** including nested field out/ref scenarios.
- **Phase 3 Control Flow Fixes COMPLETE** (Dec 2025):
  - All control flow tests pass without requiring JIT fixes - implementation already robust
  - 3.1 If-Else Chain: `TestIfElseChain`, `TestIfElseChainWithReturns` pass
  - 3.2 Break/Continue: `TestBreakInLoop`, `TestContinueInLoop`, `TestNestedBreak` pass
  - 3.3 Complex Loop Conditions: `TestWhileWithAndCondition` passes
- **FullTest now passes 89/89 tests** including all control flow scenarios.
- **Phase 4 Object/Boxing Operations COMPLETE** (Dec 2025):
  - 4.1 newobj with >4 constructor args: `TestNewObjManyArgs` passes (5-arg struct constructor works)
  - 4.2 box/unbox for int: `TestBoxInt` passes (int boxing/unboxing works)
  - 4.2 box/unbox for struct: `TestBoxStruct` passes (small 8-byte struct boxing works)
  - 4.2 box/unbox for medium struct: `TestBoxMediumStruct` passes (16-byte struct boxing works)
  - 4.2 box/unbox for large struct: `TestBoxLargeStruct` passes (24-byte struct boxing works)
  - Key fix: Token table-based size calculation distinguishes external types (TypeRef, BaseSize includes MT overhead) from local types (TypeDef, BaseSize is value size)
- **FullTest now passes 94/94 tests** including all boxing scenarios.
- **Phase 5 Instance Member Support COMPLETE** (Dec 2025):
  - 5.1 Instance field read/write: `TestInstanceFieldReadWrite`, `TestMultipleInstanceFields` pass
  - 5.2 Instance method calls: `TestInstanceMethodCall`, `TestInstanceMethodWithThis` pass
  - Implementation already worked correctly - no JIT fixes required
- **FullTest now passes 98/98 tests** including all instance member scenarios.

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

**Status (Dec 2025): IMPLEMENTED AND WORKING**
- Small value types (<=8 bytes): `ldloc` marks TOS as `StackType_ValueType`, `ldfld` extracts field by bit-shifting
- Large value types (>8 bytes): `ldloc` loads ADDRESS and marks as `StackType_Int`, `ldfld` dereferences correctly
- All existing tests pass: TestStructLocalFieldWrite, TestStructLocalFieldSum, TestStructPassByValue, TestLargeStructFields

**Fix:**
- [x] In `CompileLdfld()`: Check if TOS type is `StackType_ValueType`
- [x] If value type: Calculate field offset from current stack position, not dereference
- [x] Track struct size in stack metadata (not just classification) - partial, works for arrays
- [ ] Handle nested field access (value type containing value type) - NOT TESTED

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

### 2.1 Fix Struct Return Values (All Sizes) ✅ COMPLETE
**Priority:** HIGH | **Complexity:** High | **Files:** `ILCompiler.cs`, `Tier0JIT.cs`, `X64Emitter.cs`

**Problem:** All struct returns treated as 64-bit pointer in RAX, no size checking.

**x64 ABI Rules:**
- Structs <= 8 bytes: Return in RAX
- Structs 9-16 bytes: Return in RAX:RDX
- Structs > 16 bytes: Caller provides hidden buffer pointer in RCX

**Status (Dec 2025): COMPLETE - All 78 tests pass**
- [x] Small structs (<=8 bytes) - `TestSmallStructReturn` passes
- [x] Medium structs (9-16 bytes) - `TestMediumStructReturn` passes (RAX:RDX return)
- [x] Large structs (>16 bytes) - `TestLargeStructReturn` passes (hidden buffer)

**Implementation Details:**
- [x] Track return type size in method signature parsing (`ReturnKind`, `ReturnStructSize` in `ResolvedMethod`)
- [x] For <= 8 bytes: Return in RAX (existing behavior)
- [x] For 9-16 bytes: `CompileRet` writes to RAX:RDX, `CompileCall` pushes RDX:RAX to stack
- [x] For > 16 bytes: `CompileCall` allocates 16-byte aligned buffer, passes address in RCX; `CompileRet` copies to buffer via hidden first arg
- [x] Fixed `ldelem` to use 16-byte alignment for structs >16 bytes (consistency with call return buffer)
- [x] Fixed `stloc` to use 16-byte alignment cleanup for structs >16 bytes

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

### 2.3 Fix `ref`/`out` Parameter Handling ✅ COMPLETE
**Priority:** HIGH | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Problem:** Passing `class.struct.field` as out parameter fails due to chained address computation.

**Status (Dec 2025): FULLY COMPLETE - All ref/out scenarios working**
- [x] Simple `out int` parameters - `TestSimpleOutParam` passes
- [x] `ref int` parameters - `TestRefParam` passes
- [x] Multiple ref modifications - `TestRefParamMultiple` passes
- [x] Struct out parameters - `TestStructOutParam` and `TestObjectInitializerOut` already passing
- [x] Nested field out (`class.struct.field`) - `TestNestedFieldOut` passes
- [x] Nested field ref (`class.struct.field`) - `TestNestedFieldRef` passes

**Implementation Notes:**
- `ldloca`/`ldarga` correctly compute local/arg addresses
- `stind.i4`/`ldind.i4` correctly handle pointer dereferencing
- `ldflda` chain correctly computes nested field addresses for objects
- The `class.struct.field` pattern (critical for DDK development) works correctly
- **FullTest now passes 83/83 tests** with all ref/out parameter tests

**Tests Added:**
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

### 3.1 Fix If-Else Chain Execution ✅ COMPLETE
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - No JIT fixes required, implementation already correct**
- [x] Audit `beq`, `bne`, `bgt`, `blt`, `bge`, `ble` implementations - Working correctly
- [x] Verify branch offset calculations for forward jumps - Working correctly
- [x] Check that fall-through doesn't skip to wrong location - Working correctly
- `TestIfElseChain` passes (tests if-else chain with assignments)
- `TestIfElseChainWithReturns` passes (tests if-else chain with early returns)

**Tests Added:**
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

### 3.2 Fix Break/Continue in Loops ✅ COMPLETE
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - No JIT fixes required, implementation already correct**
- [x] Track loop entry and exit points during compilation - Working correctly
- [x] Ensure `br` to loop exit (break) works correctly - Working correctly
- [x] Ensure `br` to loop header (continue) works correctly - Working correctly
- `TestBreakInLoop` passes (breaks at i==5, sum=0+1+2+3+4=10)
- `TestContinueInLoop` passes (skips evens, sum=1+3+5+7+9=25)
- `TestNestedBreak` passes (inner break at j==3, count=10*3=30)

**Tests Added:**
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

### 3.3 Fix Complex Loop Conditions ✅ COMPLETE
**Priority:** MEDIUM | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - No JIT fixes required, implementation already correct**
- [x] Verify short-circuit evaluation in `and`/`or` with branches - Working correctly
- [x] Check `brfalse`/`brtrue` after comparison sequences - Working correctly
- `TestWhileWithAndCondition` passes (i<10 && sum<20, result=21)

**Tests Added:**
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

### 4.1 Fix `newobj` for Value Types with >4 Constructor Args ✅ COMPLETE
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - Already works correctly**
- [x] newobj with 5+ args tested and working
- `TestNewObjManyArgs` passes (1+2+3+4+5 = 15)

**Tests Added:**
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

### 4.2 Fix `box`/`unbox` for Various Sizes ✅ COMPLETE
**Priority:** MEDIUM | **Complexity:** Medium | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - All struct sizes work**
- [x] Box/unbox for int - `TestBoxInt` passes (4 bytes)
- [x] Box/unbox for small structs - `TestBoxStruct` passes (8 bytes)
- [x] Box/unbox for medium structs - `TestBoxMediumStruct` passes (16 bytes)
- [x] Box/unbox for large structs - `TestBoxLargeStruct` passes (24 bytes)
- [x] Get actual type size from metadata using token table approach
- [x] Copy correct number of bytes for struct boxing/unboxing

**Implementation Details:**
- Token table byte determines type source: 0x01=TypeRef (external), 0x02=TypeDef (local)
- External types (System.Runtime): BaseSize includes 8-byte MT pointer overhead, so `valueSize = baseSize - 8`
- Local types (user assembly): BaseSize IS the value size, so `valueSize = baseSize`
- `CompileBox` handles small (≤8 bytes) vs large (>8 bytes) structs differently:
  - Small: R3 contains value directly, single 8-byte copy
  - Large: R3 contains address, multi-qword copy loop with remainder handling
- `CompileUnboxAny` mirrors the same logic for extracting values from boxed objects

**Tests Added:**
```csharp
public static int TestBoxInt()
{
    int x = 42;
    object boxed = x;
    int unboxed = (int)boxed;
    return unboxed;  // Expected: 42 - PASSES
}

public static int TestBoxStruct()
{
    SimpleStruct s;
    s.X = 10;
    s.Y = 20;
    object boxed = s;
    SimpleStruct unboxed = (SimpleStruct)boxed;
    return unboxed.X + unboxed.Y;  // Expected: 30 - PASSES
}

public static int TestBoxMediumStruct()
{
    MediumStruct m;
    m.A = 100;
    m.B = 200;
    object boxed = m;
    MediumStruct unboxed = (MediumStruct)boxed;
    return (int)(unboxed.A + unboxed.B);  // Expected: 300 - PASSES
}

public static int TestBoxLargeStruct()
{
    LargeStruct l;
    l.A = 10;
    l.B = 20;
    l.C = 30;
    object boxed = l;
    LargeStruct unboxed = (LargeStruct)boxed;
    return (int)(unboxed.A + unboxed.B + unboxed.C);  // Expected: 60 - PASSES
}
```

---

## Phase 5: Instance Member Support ✅ COMPLETE

### 5.1 Verify Instance Field Access ✅ COMPLETE
**Priority:** HIGH | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - Implementation already worked correctly**
- [x] `TestInstanceFieldReadWrite` passes - read/write via instance methods
- [x] `TestMultipleInstanceFields` passes - direct field access on class instance

**Tests Added:**
```csharp
public static int TestInstanceFieldReadWrite()
{
    SimpleClass obj = new SimpleClass(0);
    obj.SetValue(42);
    return obj.GetValue();  // Expected: 42 - PASSES
}

public static int TestMultipleInstanceFields()
{
    MultiFieldClass obj = new MultiFieldClass();
    obj.A = 10;
    obj.B = 20;
    obj.C = 30;
    return obj.A + obj.B + obj.C;  // Expected: 60 - PASSES
}
```

---

### 5.2 Verify Instance Method Calls ✅ COMPLETE
**Priority:** HIGH | **Complexity:** Low | **Files:** `ILCompiler.cs`

**Status (Dec 2025): COMPLETE - Implementation already worked correctly**
- [x] `TestInstanceMethodCall` passes - instance method with parameters
- [x] `TestInstanceMethodWithThis` passes - instance method using `this.field`

**Tests Added:**
```csharp
public static int TestInstanceMethodCall()
{
    Calculator calc = new Calculator();
    return calc.Add(10, 20);  // Expected: 30 - PASSES
}

public static int TestInstanceMethodWithThis()
{
    Calculator calc = new Calculator();
    calc.Value = 30;
    return calc.AddToValue(20);  // Expected: 50 - PASSES
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
- [x] 1.2 Fix `ldfld` on value types from stack (TESTED - all existing tests pass)
- [x] 1.1 Fix TOS caching with `dup` + `initobj`
- [x] Add basic struct tests to FullTest

### Week 2: Struct Operations (COMPLETE)
- [x] 1.3 Fix large struct support in `ldobj`
- [x] 2.2 Fix struct copying from arrays
- [x] Large struct array store (`stelem.any` for >8 byte structs)
- [x] Large struct array load (`ldelem.any` for >8 byte structs)
- [x] Large struct local store (`stloc` for multi-slot structs)
- [ ] 6.1 Add struct size tracking to eval stack (partial - works for arrays)

### Week 3: Parameter Handling (COMPLETE)
- [x] 2.1 Fix struct return values - ALL SIZES NOW WORK (≤8 bytes RAX, 9-16 bytes RAX:RDX, >16 bytes hidden buffer)
- [x] 2.3 Fix ref/out parameter handling - All tests pass including nested field out/ref (`class.struct.field` pattern)
- [x] Add parameter tests to FullTest (TestSmallStructReturn, TestMediumStructReturn, TestLargeStructReturn, TestNestedFieldOut, TestNestedFieldRef all pass)

### Week 4: Control Flow (COMPLETE)
- [x] 3.1 Fix if-else chain execution - Already working, `TestIfElseChain`, `TestIfElseChainWithReturns` pass
- [x] 3.2 Fix break/continue in loops - Already working, `TestBreakInLoop`, `TestContinueInLoop`, `TestNestedBreak` pass
- [x] 3.3 Fix complex loop conditions - Already working, `TestWhileWithAndCondition` passes

### Week 5: Objects and Boxing (COMPLETE)
- [x] 4.1 Fix newobj for >4 constructor args - Already working (`TestNewObjManyArgs` passes)
- [x] 4.2 Fix box/unbox for various sizes - All sizes work (int, 8-byte, 16-byte, 24-byte structs)
- [x] 5.1/5.2 Verify instance member support - All 4 tests pass (fields and methods)

### Week 6: Polish - COMPLETE
- [x] Audit VirtioBlk/VirtioDevice workarounds - All underlying JIT bugs are FIXED
  - VirtioDevice.cs: ldfld on value types, struct array copy, if-else chains, while loops - ALL FIXED
  - VirtioBlkEntry.cs: struct returns, object initializers - ALL FIXED
- [x] Remove workaround code from drivers
  - Cleaned up VirtioDevice.cs: removed JIT bug comments, simplified struct handling, converted to switch statement
  - Cleaned up VirtioBlkEntry.cs: removed JIT bug comments, simplified comments
- [x] Full regression test pass - **98/98 tests pass**

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
