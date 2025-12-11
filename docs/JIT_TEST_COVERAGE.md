# JIT Test Coverage Checklist

This document tracks test coverage for JIT compiler features. Each area should have targeted tests to ensure correctness before moving to complex driver code.

## Status Legend
- ✅ Tested and working
- ⚠️ Partially tested / likely works
- ❌ Not tested / likely broken
- 🔲 Not implemented

---

## 1. Basic Operations

### Arithmetic
- ✅ add, sub, mul, div, rem (signed/unsigned)
- ✅ neg, not
- ✅ and, or, xor
- ✅ shl, shr, shr.un
- ✅ Integer overflow (unchecked)
- ⚠️ Overflow checking (add.ovf, etc.) - opcodes exist, untested

### Comparisons & Branches
- ✅ ceq, cgt, clt (signed/unsigned)
- ✅ beq, bne, blt, bgt, ble, bge
- ✅ br, br.s (unconditional)
- ✅ brfalse, brtrue
- ✅ switch

### Conversions
- ✅ conv.i1, conv.i2, conv.i4, conv.i8
- ✅ conv.u1, conv.u2, conv.u4, conv.u8
- ✅ conv.r4, conv.r8
- ⚠️ conv.ovf.* (overflow checking) - untested

---

## 2. Method Calls

### Static Calls
- ✅ Basic static method calls
- ✅ Static methods with parameters
- ✅ Static methods with return values
- ✅ Cross-assembly static calls

### Instance Calls
- ✅ Instance method calls
- ✅ `this` pointer handling
- ✅ Instance methods on structs (byref this)

### Virtual Calls
- ✅ Virtual method dispatch (callvirt)
- ✅ Override resolution
- ✅ Constrained calls on value types (ToString, etc.)
- ✅ Interface dispatch (callvirt on interface type)

### Special Calls
- ⚠️ Tail calls (tail. prefix parsed but ignored)
- ✅ Indirect calls through delegates (Delegate.Invoke)
- ❌ calli (indirect call through function pointer)

---

## 3. Object Model

### Object Creation
- ✅ newobj for reference types
- ✅ newobj for value types
- ✅ Object initialization

### Fields
- ✅ ldfld, stfld (instance fields)
- ✅ ldsfld, stsfld (static fields)
- ✅ ldflda, ldsflda (field address)
- ✅ Fields on generic types

### Boxing/Unboxing
- ✅ box (value type to object)
- ✅ unbox.any (object to value type)
- ✅ unbox (get pointer to boxed value)
- ❌ Nullable<T> boxing (special semantics - box null = null)

### Type Checks
- ⚠️ isinst (safe cast)
- ⚠️ castclass (throwing cast)
- ❌ typeof / ldtoken for types

---

## 4. Arrays

### Single-Dimension Arrays (SZARRAY)
- ✅ newarr (create array)
- ✅ ldlen (get length)
- ✅ ldelem.* (load element - primitives)
- ✅ stelem.* (store element - primitives)
- ✅ ldelem / stelem with type token (structs)
- ✅ ldelema (element address)
- ✅ Generic arrays (T[])

### Multi-Dimension Arrays (ARRAY)
- 🔲 newobj for multi-dim arrays
- 🔲 Array.Get, Array.Set, Array.Address
- 🔲 ldelem/stelem for multi-dim

### Bounds Checking
- ❌ Array bounds checks (currently no bounds checking)

---

## 5. Structs (Value Types)

### Basic Operations
- ✅ Struct creation and field access
- ✅ Struct copy (ldobj/stobj)
- ✅ Struct initialization (initobj)
- ✅ Struct parameters (by value, by ref)
- ✅ Struct return values (small, medium, large)

### Size Categories
- ✅ Small structs (≤8 bytes) - returned in RAX
- ✅ Medium structs (9-16 bytes) - returned in RAX:RDX
- ✅ Large structs (>16 bytes) - hidden buffer pointer

### Special Cases
- ✅ Nested structs
- ✅ Struct arrays
- ⚠️ Struct with reference type fields
- ❌ Explicit layout structs ([StructLayout])
- ❌ Fixed-size buffers

---

## 6. Generics

### Generic Classes
- ✅ Simple generic class (Box<T>)
- ✅ Generic class instantiation
- ✅ Generic fields

### Generic Methods
- ✅ Generic method on non-generic class
- ⚠️ Generic method on generic class
- ❌ Generic method with multiple type parameters

### Generic Constraints
- ❌ where T : class
- ❌ where T : struct
- ❌ where T : new()
- ❌ where T : SomeBase
- ❌ where T : ISomeInterface

### Variance
- ❌ Covariance (out T)
- ❌ Contravariance (in T)

### Complex Scenarios
- ✅ Generic arrays (T[])
- ❌ Nested generic types (Outer<T>.Inner<U>)
- ❌ Generic interfaces
- ❌ Generic delegates

---

## 7. Exception Handling

### Infrastructure (implemented)
- ✅ EH clause parsing (ILMethodParser.ParseEHClauses)
- ✅ Funclet-based compilation (CompileWithFunclets)
- ✅ JIT method registration (JITMethodRegistry)
- ✅ EH clause detection in Tier0JIT
- ✅ Exception type resolution (well-known types forwarded from System.Runtime to korlib)
- ✅ Unwind codes for all JIT methods (AddStandardUnwindCodes)
- ✅ LeaveTargetOffset encoding/decoding for funclet return addresses

### Basic Try/Catch
- ✅ try { } catch { } - basic catch-all
- ✅ try { } catch (Exception) { } - typed catch clause
- ⚠️ Multiple catch blocks - infrastructure ready, untested
- ⚠️ Nested try/catch - infrastructure ready, untested

### Finally
- ✅ try { } finally { } - finally handler executes on normal and exceptional paths
- ✅ try { } catch { } finally { } - two-pass exception handling
- ⚠️ finally with return/break - leave instruction handling in finally

### Throw
- ✅ throw new Exception() - exception allocation and dispatch
- ✅ rethrow (re-raise current exception)
- ✅ throw in catch block (new exception from catch handler)

### Filter
- ⚠️ catch when (condition) - infrastructure exists (ExecuteFilterFunclet), untested

### Fault
- ⚠️ fault blocks - treated like finally, untested

### Implementation Details
- Two-pass exception dispatch: Pass 1 finds handler, Pass 2 executes finally handlers
- Funclet calling convention: RCX = exception object, RDX = parent frame pointer
- Funclet prologue: `push rbp; mov rbp, rdx` - establishes frame from parent
- Leave in funclet: `pop rbp; ret` - returns to LeaveTargetOffset (code after try/catch)
- Stack setup for funclet: RSP-16 with return address at RSP+8

---

## 8. Delegates and Events

### Delegate Creation
- ✅ ldftn (load function pointer)
- ⚠️ ldvirtftn (load virtual function pointer) - infrastructure ready, untested
- ✅ newobj for delegate (static delegates)
- ✅ Delegate.Invoke (inline dispatch code generation)

### Delegate Invocation Details
- ✅ Static delegate (target=null, function pointer called directly)
- ⚠️ Instance delegate (target=object, function pointer called with target as first arg) - untested
- ✅ Single-argument delegates
- ✅ Multi-argument delegates (2+ args)
- ✅ Void-returning delegates
- ✅ Value-returning delegates
- ✅ Delegate reassignment

### Multicast Delegates
- ❌ Delegate.Combine
- ❌ Delegate.Remove

### Anonymous Methods / Lambdas
- ❌ Closure capture
- ❌ Display classes

---

## 9. Interfaces

### Interface Dispatch
- ✅ callvirt on interface method (GetInterfaceMethod runtime helper)
- ✅ Interface method resolution (InterfaceMap in MethodTable)
- ✅ Interface map population from InterfaceImpl metadata table
- ✅ Lazy JIT compilation of interface implementations
- ⚠️ Explicit interface implementation (same mechanism, untested)
- ⚠️ Multiple interfaces on same type (map populated, untested)

### Interface Casting
- ❌ Cast to interface type
- ❌ isinst with interface

### Default Interface Methods
- 🔲 Not planned (C# 8+ feature)

---

## 10. Nullable<T>

- ✅ Nullable<T> creation (via constructor)
- ✅ HasValue property
- ✅ Value property
- ✅ GetValueOrDefault()
- ✅ GetValueOrDefault(defaultValue)
- ✅ Implicit conversion from T to Nullable<T>
- ✅ Assign null to Nullable<T> (initobj)
- ✅ Nullable<T> as method parameter
- ✅ Nullable<T> as method return value
- ✅ Nullable boxing (null value boxes to null reference)
- ✅ Nullable unboxing (null reference unboxes to Nullable with HasValue=false)
- ✅ Lifted operators (int? + int?) - compiler generates inline code using existing support

---

## 11. Static Constructors

- ❌ Type initializer (.cctor) invocation
- ❌ beforefieldinit semantics
- ❌ Circular static initialization

---

## 12. Reflection (if needed)

- ❌ ldtoken (type/method/field handle)
- ❌ Type.GetTypeFromHandle
- ❌ GetType() on objects
- ❌ typeof(T) in generic context

---

## 13. Miscellaneous IL

### Pointer Operations
- ⚠️ ldind.* (load indirect)
- ⚠️ stind.* (store indirect)
- ⚠️ localloc (stack allocation)
- ❌ cpblk (memory copy)
- ❌ initblk (memory init)

### Prefix Opcodes
- ✅ constrained. (for value type virtcalls)
- ✅ readonly. (no-op, optimization hint)
- ✅ tail. (no-op, not implemented)
- ✅ volatile. (no-op in naive JIT)
- ✅ unaligned. (no-op on x64)

### Rare Opcodes
- ❌ arglist (varargs)
- ❌ mkrefany / refanyval / refanytype (TypedReference)
- ❌ sizeof (should be easy)

---

## Priority Order for Testing

### P0 - Critical for Drivers
1. ✅ Exception handling (try/catch/finally)
2. ✅ Interface dispatch
3. ⚠️ Nullable<T> (basic operations work; passing/returning structs needs work)

### P1 - Important for Robustness
4. Delegates (for callbacks)
5. Static constructors
6. Complex generics

### P2 - Nice to Have
7. Multi-dimensional arrays
8. Reflection basics
9. Overflow checking

---

## Test File Locations

Tests should be added to `src/FullTest/Program.cs` in appropriate test classes:

- `ExceptionTests` - Exception handling
- `InterfaceTests` - Interface dispatch
- `DelegateTests` - Delegates and function pointers
- `NullableTests` - Nullable<T> operations
- `StaticCtorTests` - Static constructor behavior
- `AdvancedGenericTests` - Complex generic scenarios

---

## Notes

- Current test count: 142 passing
- Target: Add ~50-100 more targeted tests before driver work
- Focus on failure isolation - each test should test ONE thing

## Recent Updates

### Nullable<T> Lifted Operators (2024-12)
Verified lifted operators work without additional JIT changes:
- C# compiler generates inline code using HasValue, GetValueOrDefault(), and newobj
- 11 new tests added for lifted operators:
  - Addition with both values, first null, second null, both null
  - Subtraction, multiplication, division
  - Equality comparisons (same values, different values, both null, one null)

### Nullable<T> Boxing/Unboxing (2024-12)
Completed Nullable<T> boxing/unboxing support:
- Nullable boxing: if HasValue is false, box returns null; if true, boxes inner T value
- Nullable unboxing: null reference creates Nullable with HasValue=false; non-null creates HasValue=true
- Added `IsNullable` flag (0x00010000) to MTFlags and MethodTable
- Added `IsNullableName()` and `IsNullableGenericDef()` helpers to AssemblyLoader
- `CompileBox` detects Nullable<T> and calls `CompileNullableBox` for special handling
- `CompileUnboxAny` detects Nullable<T> and calls `CompileNullableUnbox` for special handling
- Fixed multi-slot struct handling in box: LEA RSP instead of POP for structs >8 bytes
- 7 new tests added:
  - Boxing with value, boxing null, boxing default-constructed
  - Unboxing from boxed int, unboxing from null
  - Round-trip boxing/unboxing with value and null

### Nullable<T> Support (2024-12)
Added Nullable<T> support:
- Added `GetValueOrDefault()` and `GetValueOrDefault(T)` to korlib Nullable<T>
- Added `Nullable<T>` struct to System.Runtime to match korlib
- Fixed `newobj` for value types: properly allocate/zero large structs (>8 bytes)
- Fixed generic instantiation size calculation: compute Nullable<T> as 8 + sizeof(T)
- 13 tests passing (up from 9):
  - Constructor, HasValue, Value properties
  - GetValueOrDefault with/without custom default
  - Implicit conversion, null assignment (initobj)
  - Parameter passing and return values (fixed)

### Interface Dispatch (2024-12)
Implemented interface dispatch via `callvirt` on interface types:
- Added `IsInterfaceMethod()` / `IsMethodDefInterfaceMethod()` to detect interface method calls
- Added `CountInterfacesForType()` / `PopulateInterfaceMap()` to build interface maps in MethodTables
- Added `RegisterNewVirtualMethodsForLazyJit()` to register interface implementations for lazy JIT
- Modified `GetInterfaceMethod()` to call `EnsureVtableSlotCompiled()` for lazy compilation
- Test: `InterfaceTests.TestSimpleInterface()` - IValue interface with ValueImpl implementation
