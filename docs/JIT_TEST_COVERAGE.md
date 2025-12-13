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
- ✅ Overflow checking (add.ovf, sub.ovf, mul.ovf, conv.ovf.*) - INT 4 handler triggers OverflowException

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
- ✅ conv.ovf.* (overflow checking) - tested and working

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
- ✅ calli (indirect call through function pointer)

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
- ✅ Nullable<T> boxing (special semantics - box null = null, see Section 10)

### Type Checks
- ✅ isinst (safe cast) - tested with classes and interfaces
- ✅ castclass (throwing cast) - tested with interfaces
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
- ✅ Struct with reference type fields (tested with StructWithRef containing string)
- ✅ Explicit layout structs ([StructLayout(LayoutKind.Explicit)] with [FieldOffset])
- ✅ Fixed-size buffers (6 tests: byte/int buffers, device registers, pointer access)

---

## 6. Generics

### Generic Classes
- ✅ Simple generic class (Box<T>)
- ✅ Generic class instantiation
- ✅ Generic fields

### Generic Methods
- ✅ Generic method on non-generic class
- ✅ Generic method on generic class (tested with GenericContainer<T>.Convert<TResult>)
- ✅ Generic method with multiple type parameters (Combine<T1, T2>)

### Generic Constraints
- ✅ where T : class (reference type constraint)
- ✅ where T : struct (value type constraint)
- ✅ where T : new() (Activator.CreateInstance<T> JIT intrinsic)
- ✅ where T : SomeBase (base class constraint)
- ✅ where T : ISomeInterface (interface constraint)

### Variance
- ✅ Covariance (out T) - ICovariant<Derived> assignable to ICovariant<Base>
- ✅ Contravariance (in T) - IContravariant<Base> assignable to IContravariant<Derived>

### Complex Scenarios
- ✅ Generic arrays (T[])
- ✅ Nested generic types (Outer<T>.Inner<U>) - fixed compressed int parsing in TypeSpec
- ✅ Generic interfaces (IContainer<int>, IContainer<string>) - TypeSpec handling in interface dispatch
- ✅ Generic delegates (Transformer<TIn, TOut>) - TypeSpec handling in delegate ctor/Invoke

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
- ✅ Multiple catch blocks - tested with 2 typed catches + catch-all
- ✅ Nested try/catch - tested with inner/outer try-catch

### Finally
- ✅ try { } finally { } - finally handler executes on normal and exceptional paths
- ✅ try { } catch { } finally { } - two-pass exception handling
- ✅ finally with return/break - leave instruction handling in finally (tested with break in loop)

### Throw
- ✅ throw new Exception() - exception allocation and dispatch
- ✅ rethrow (re-raise current exception)
- ✅ throw in catch block (new exception from catch handler)

### Filter
- ✅ catch when (condition) - tested with filter true and filter false cases

### Fault
- ✅ fault blocks - code review verified (not testable from C#)
  - Fault handlers only run during exception unwinding, NOT on normal leave
  - `CompileLeave` only calls Finally handlers (line 5968: `flags != Finally` check)
  - Exception handler dispatch calls both Finally and Fault (line 2116: `Finally || Fault` check)
  - `endfault` uses same opcode as `endfinally` (0xDC) - handled by `CompileEndfinally`

### Implementation Details
- Two-pass exception dispatch: Pass 1 finds handler, Pass 2 executes finally handlers
- Funclet calling convention: RCX = exception object, RDX = parent frame pointer
- Catch funclet prologue: `push rbp; mov rbp, rdx; push rcx` - establishes frame and pushes exception to eval stack
- Finally/fault funclet prologue: `push rbp; mov rbp, rdx` - no exception on stack
- Leave in funclet: `pop rbp; ret` - returns to LeaveTargetOffset (code after try/catch)
- Stack setup for funclet: RSP-16 with return address at RSP+8

---

## 8. Delegates and Events

### Delegate Creation
- ✅ ldftn (load function pointer)
- ✅ ldvirtftn (load virtual function pointer) - tested with virtual delegate dispatch
- ✅ newobj for delegate (static delegates)
- ✅ Delegate.Invoke (inline dispatch code generation)

### Delegate Invocation Details
- ✅ Static delegate (target=null, function pointer called directly)
- ✅ Instance delegate (target=object, function pointer called with target as first arg)
- ✅ Single-argument delegates
- ✅ Multi-argument delegates (2+ args)
- ✅ Void-returning delegates
- ✅ Value-returning delegates
- ✅ Delegate reassignment

### Multicast Delegates
- ✅ Delegate.Combine (creates new multicast delegate without mutating original)
- ✅ Delegate.Remove (removes delegate from invocation list)
- ✅ += operator (compiles to Delegate.Combine)
- ✅ -= operator (compiles to Delegate.Remove)

### Anonymous Methods / Lambdas
- ✅ Closure capture (capturing local variables and parameters)
- ✅ Display classes (compiler-generated `<>c__DisplayClass` types)
- ✅ Nested closures (inner lambda capturing outer scope)
- ✅ Reference type capture (capturing strings, objects)
- ✅ Mutation of captured variables

---

## 9. Interfaces

### Interface Dispatch
- ✅ callvirt on interface method (GetInterfaceMethod runtime helper)
- ✅ Interface method resolution (InterfaceMap in MethodTable)
- ✅ Interface map population from InterfaceImpl metadata table
- ✅ Lazy JIT compilation of interface implementations
- ✅ Multiple interfaces on same type (tested with 3 interfaces)
- ✅ Explicit interface implementation (tested with IExplicit.GetValue vs IValue.GetValue)

### Interface Casting
- ✅ isinst with interface (as T)
- ✅ castclass with interface ((T)obj)

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

- ✅ Type initializer (.cctor) invocation
- ✅ Static field access triggers cctor before first use
- ✅ Cctor runs only once (subsequent accesses skip)
- ✅ Cctor with dependencies (type A's cctor accesses type B's static field)
- ⚠️ beforefieldinit semantics (types without beforefieldinit work correctly)
- ⚠️ Circular static initialization (basic case works, complex cycles untested)

---

## 12. Reflection (if needed)

- ❌ ldtoken (type/method/field handle)
- ❌ Type.GetTypeFromHandle
- ✅ GetType() on objects - returns RuntimeType with working Name, FullName, Namespace properties
- ❌ typeof(T) in generic context

---

## 13. Miscellaneous IL

### Pointer Operations
- ✅ ldind.* (load indirect) - used in passing tests
- ✅ stind.* (store indirect) - used in passing tests
- ✅ localloc (stack allocation) - 6 dedicated tests (small/large buffers, int/long types, multiple allocations)
- ✅ cpblk (memory copy) - tested via MemoryBlockTests (small and large buffers)
- ✅ initblk (memory init) - tested via MemoryBlockTests (small and large buffers)

### Prefix Opcodes
- ✅ constrained. (for value type virtcalls)
- ✅ readonly. (no-op, optimization hint)
- ✅ tail. (no-op, not implemented)
- ✅ volatile. (no-op in naive JIT)
- ✅ unaligned. (no-op on x64)

### Rare Opcodes
- ❌ arglist (varargs)
- ❌ mkrefany / refanyval / refanytype (TypedReference)
- ✅ sizeof (primitive types and structs)

---

## Priority Order for Testing

### P0 - Critical for Drivers
1. ✅ Exception handling (try/catch/finally)
2. ✅ Interface dispatch
3. ✅ Nullable<T> (full support including boxing/unboxing)

### P1 - Important for Robustness
4. ✅ Delegates (for callbacks) - static and instance delegates working
5. ✅ Static constructors - cctor invocation working
6. Complex generics

### P2 - Nice to Have
7. Multi-dimensional arrays
8. Reflection basics
9. ✅ Overflow checking - implemented via INT 4 interrupt handler

---

## Test File Locations

Tests should be added to `src/FullTest/Program.cs` in appropriate test classes:

- `ExceptionTests` - Exception handling
- `InterfaceTests` - Interface dispatch
- `DelegateTests` - Delegates and function pointers
- `CalliTests` - Indirect calls through function pointers (calli)
- `MulticastDelegateTests` - Delegate.Combine and Delegate.Remove
- `ClosureTests` - Lambdas with captured variables
- `StackallocTests` - Stack allocation (localloc opcode)
- `FixedBufferTests` - Fixed-size buffers in structs
- `OverflowTests` - Overflow checking (checked arithmetic) operations
- `NullableTests` - Nullable<T> operations
- `StaticCtorTests` - Static constructor behavior
- `AdvancedGenericTests` - Complex generic scenarios

---

## Notes

- Current test count: 282 passing
- Target: Add ~50-100 more targeted tests before driver work
- Focus on failure isolation - each test should test ONE thing

### Known Limitations

*No known critical limitations remaining.*

## Recent Updates

### Object.GetType() Support (2025-12)
Implemented full `Object.GetType()` support with type forwarding:
- **AOT method registration**: Added `System.Object.GetType` to AotMethodRegistry returning RuntimeType
- **RuntimeType creation**: GetType implementation dereferences object to get MethodTable pointer, wraps in RuntimeType
- **korlib changes**: Made `System.Type` inherit from `System.Reflection.MemberInfo` to match .NET hierarchy
- **String operators**: Added `String.op_Equality` and `String.op_Inequality` to AOT registry
- **Type method helpers**: Added TypeMethodHelpers class with GetName, GetFullName, GetNamespace implementations
- **Type forwarding**: Added System.Type and System.RuntimeType as well-known types (0xF0000030, 0xF0000031)
- **Well-known type lookup**: Added Type, RuntimeType, MemberInfo to GetWellKnownTypeToken and IsWellKnownAotType
- **Runtime type registration**: RegisterReflectionTypes extracts RuntimeType MethodTable and parent Type MethodTable from AOT
- **AOT method routing**: Type property accessors (get_Name, get_FullName, get_Namespace) routed through AOT registry to TypeMethodHelpers
- **6 tests**: TestGetTypeNotNull, TestGetTypeName, TestGetTypeBoxedInt, TestGetTypeSameType, TestGetTypeFullName, TestGetTypeNamespace
- Test count increased from 276 to 282

### Overflow Exception Support (2025-12)
Implemented full overflow exception handling for checked arithmetic operations:
- **INT 4 (Overflow Interrupt) handler**: x86 overflow operations (`jo`, `into`, `add.ovf`, etc.) trigger INT 4
  - IDT handler converts vector 4 → OverflowException using NativeAOT exception dispatch
  - Exception is caught by normal try/catch handlers
- **Two critical fixes**:
  1. **Funclet epilog**: Changed from `pop rbp; ret` to `add rsp, 8; ret` to preserve parent frame pointer
     - Funclet prolog does `push rbp; mov rbp, rdx` (RDX = parent frame pointer)
     - Old epilog restored wrong RBP; new epilog skips saved RBP and keeps RBP correct for leave target
  2. **RSP calculation**: Changed from `searchContext.Rsp - 16` to `catchSearchContext.Rbp - 0x100`
     - RSP calculation was overlapping with interrupt frame's SS field
     - SS was being set to leave target address instead of 0x10 (kernel data segment)
     - New calculation uses RBP-based offset safely within function's stack frame
- **10 new tests** in OverflowTests:
  - TestCheckedAddNoOverflow, TestCheckedAddOverflow
  - TestCheckedSubNoOverflow, TestCheckedSubOverflow
  - TestCheckedMulNoOverflow, TestCheckedMulOverflow
  - TestCheckedConvNoOverflow, TestCheckedConvOverflow
  - TestCheckedAddUnsignedNoOverflow, TestCheckedAddUnsignedOverflow
- Test count increased from 266 to 276

### Fixed-Size Buffer Support (2025-12)
Added support and tests for fixed-size buffers (`fixed` arrays in structs):
- Added `FixedBufferAttribute` to korlib's CompilerAttributes.cs
- Fixed buffers work out-of-the-box (no JIT changes needed)
- 6 new tests:
  - TestFixedByteBuffer - basic byte array access
  - TestFixedIntBuffer - int array access
  - TestFixedBufferLoop - loop access pattern
  - TestDeviceRegisters - device register struct pattern
  - TestFixedBufferAsParameter - passing struct by ref
  - TestFixedBufferPointer - pointer arithmetic
- Test count increased from 260 to 266

### Memory Block Tests (2025-12)
Added tests for memory operations:
- **MemoryBlockTests** expanded with large buffer tests:
  - TestInitBlockLarge - 64-byte initialization
  - TestCopyBlockLarge - 64-byte copy
- cpblk/initblk now marked as ✅ (previously ⚠️)
- Test count increased from 258 to 260

### Stackalloc (localloc) Tests (2025-12)
Added dedicated tests for stack allocation (`stackalloc` / `localloc` IL opcode):
- Already implemented, but only indirectly tested via MemoryBlockTests
- 6 new dedicated tests:
  - TestStackallocSmall - 4-byte buffer
  - TestStackallocLarge - 64-byte buffer (tests alignment)
  - TestStackallocInt - int* buffer (4-byte elements)
  - TestStackallocLong - long* buffer (8-byte elements)
  - TestStackallocMultiple - multiple stackallocs in same method
  - TestStackallocComputation - using stackalloc for temp computation
- Test count increased from 252 to 258

### Closure/Lambda Support (2025-12)
Added full support for closures and lambdas that capture local variables:
- **Display classes**: Compiler-generated `<>c__DisplayClass*` types work as regular reference types
- **Variable capture**: Captured variables stored as fields on display class instances
- **Instance delegates**: Lambda methods on display classes use instance delegate dispatch (target = display class instance)
- **No JIT changes required**: Closures work out-of-the-box because:
  1. Display classes are regular reference types (`newobj` creates them)
  2. Field access (`ldfld`/`stfld`) works on display class fields
  3. Instance delegates already supported (target object passed as first argument)
- 8 new tests:
  - TestSimpleClosure - capture single local variable
  - TestMultipleCaptures - capture multiple local variables
  - TestMutateCaptured - modify captured variable (count++)
  - TestCaptureParameter - capture method parameter
  - TestCaptureReferenceType - capture string (reference type)
  - TestNestedClosure - inner closure captures outer scope
  - TestClosureInLoop - closure chain in loop
  - TestRepeatedAccess - multiple accesses to captured variable
- Test count increased from 244 to 252

### Multicast Delegate Support (2025-12)
Implemented `Delegate.Combine()` and `Delegate.Remove()` for event-like patterns:
- **Delegate.Combine**: Creates a NEW multicast delegate (doesn't mutate original - important for delegate caching)
- **Delegate.Remove**: Removes delegate from invocation list, returns remaining delegate(s)
- **Key implementation challenges**:
  1. ABI mismatch: AOT-compiled korlib uses NativeAOT's internal field layout, but JIT delegates use korlib's declared layout
     - Fixed by using explicit pointer offsets (40 for _invocationList, 48 for _invocationCount)
  2. Delegate caching: C# compiler caches delegates for static methods in `<>O` static fields
     - Original mutation approach corrupted cached delegates across tests
     - Fixed by allocating NEW delegate object using `PalAllocObject`
  3. Delegate size: JIT was allocating 40-byte delegates (Delegate fields only)
     - MulticastDelegate needs 56 bytes to include _invocationList and _invocationCount fields
     - Fixed by updating `ComputeInstanceSize()` to use 56 bytes for delegate types
- **AotMethodRegistry additions**:
  - `DelegateHelpers.Combine()` wrapper forwarding to `Delegate.Combine()`
  - `DelegateHelpers.Remove()` wrapper forwarding to `Delegate.Remove()`
  - `CombineImplWrapper` and `RemoveImplWrapper` for vtable slot population
- 9 new tests:
  - TestCombineTwo, TestCombineThree - basic combining
  - TestCombineNullFirst, TestCombineNullSecond - null handling
  - TestRemoveFromTwo, TestRemoveNonExistent, TestRemoveAll - removal
  - TestPlusEqualsOperator, TestMinusEqualsOperator - operator sugar
- Test count increased from 235 to 244

### Calli Instruction Support (2025-12)
Implemented proper `calli` IL opcode for indirect calls through function pointers:
- **StandAloneSig parsing**: Added `ParseCalliSignature()` to parse real StandAloneSig tokens (0x11xxxxxx)
  - Reads signature blob from StandAloneSig metadata table
  - Parses CLI compressed calling convention, parameter count, return type, and param types
  - Maps ECMA-335 element types to JIT ReturnKind enum
  - Handles HASTHIS flag (0x20) for instance method signatures
- **CompileCalli update**: Modified to detect real StandAloneSig tokens vs legacy test encoding
  - Token format 0x11xxxxxx = real StandAloneSig (parse signature blob)
  - Other format = legacy encoding ((ReturnKind << 8) | ArgCount) for backwards compatibility
- 7 new tests using C# function pointer syntax (`delegate*`):
  - TestCalliNoArgs - no arguments, int return
  - TestCalliOneArg - single int argument
  - TestCalliTwoArgs - two int arguments
  - TestCalliThreeArgs - three int arguments
  - TestCalliVoidReturn - void return type
  - TestCalliLong - long argument and return
  - TestCalliReassign - function pointer reassignment
- Test count increased from 228 to 235

### Explicit Layout Structs Support (2025-12)
Added support for `[StructLayout(LayoutKind.Explicit)]` with `[FieldOffset]` attributes:
- **FieldOffsetAttribute**: Added to korlib (`System.Runtime.InteropServices`)
- **Size calculation fix**: Both `MetadataIntegration.CalculateTypeDefSize()` and `AssemblyLoader.ComputeInstanceSize()` now:
  1. Check ClassLayout table for explicit type size
  2. Check FieldLayout table for explicit field offsets and calculate size as max(offset + fieldSize)
  3. Fall back to sequential layout for auto-layout types
- **Field offset resolution**: Was already working via `HasExplicitFieldOffset()` querying FieldLayout table
- 7 new tests:
  - TestUnionWriteIntReadBytes, TestUnionWriteBytesReadInt - int/byte union
  - TestExplicitGap - struct with gap between fields (offset 0 and 8)
  - TestExplicitOutOfOrder - fields defined out of memory order
  - TestLongIntUnionWriteLong, TestLongIntUnionWriteInts - long/int union
  - TestUnionOverwrite - verify field overlap works correctly
- Test count increased from 221 to 227

### Activator.CreateInstance<T>() Fix (2025-12)
Fixed `Activator.CreateInstance<T>()` intrinsic not calling the correct constructor:
- **Bug**: `TestConstraintNew` returned 0 instead of 42 (field initializer not running)
- **Root cause**: Token collision in CompiledMethodRegistry lookup
  - `FindAndCompileDefaultCtor()` used `GetNativeCode(ctorToken)` without assembly ID
  - Token 0x06000101 from FullTest.dll collided with same token from another assembly
  - The wrong ctor was returned (from System.Runtime or another assembly)
- **Fix**: Use assembly-aware lookup `Lookup(ctorToken, asm->AssemblyId)`
- Test count increased from 227 to 228 (TestConstraintNew now passes)

### Generic Constraints Fix (2025-12)
- Fixed `box T` for reference types - was allocating a new object instead of no-op
- For `where T : class`, boxing null should return null (not a new object)
- 4 new tests: TestConstraintClass, TestConstraintClassNotNull, TestConstraintStruct, TestConstraintInterface
- Test count increased from 211 to 215

### Nested Generic Types Fix (2025-12)
Added support for nested generic types like `Outer<T>.Inner<U>`:
- **Bug**: Field resolution for nested generic types returned garbage offsets (45, 46)
- **Root cause**: TypeSpec signature parsing used LEB128-style decoding instead of CLI compressed integer format
  - LEB128: `(b0 & 0x7F) | ((b1 & 0x7F) << 7) | ...`
  - CLI compressed: 1-byte (`< 0x80`), 2-byte (`(b0 & 0x3F) << 8 | b1`), or 4-byte
  - For `0x81 0x2C`: LEB128 = 5633 (wrong), CLI = 300 (correct)
- **Fix 1**: Use existing `DecodeCompressedUInt()` in `ResolveMemberRefField()` for TypeSpec parsing
- **Fix 2**: Handle reference types in `constrained.` prefix - must dereference managed pointer before callvirt
  - Stack has pointer to reference (from ldflda), not the reference itself
  - Added code to dereference pointer for reference types before virtual dispatch
- 3 new tests: TestNestedGenericSimple, TestNestedGenericInnerValue, TestNestedGenericMethod
- Test count increased from 208 to 211

### Generic Delegates and Interfaces Fix (2025-12)
Fixed generic delegates and generic interfaces for TypeSpec tokens:
- **Generic Delegates** (`Transformer<int, int>`, `Transformer<string, int>`):
  - Added `IsDelegateCtor()` to detect delegate constructors with TypeSpec class reference
  - Added TypeSpec handling to `IsDelegateInvoke()` for generic delegate Invoke methods
  - Uses `GetMemberRefGenericInstMT()` to resolve instantiated delegate MethodTable
  - Both `newobj` and `callvirt Invoke` now work for generic delegate types
  - 2 new tests: TestGenericDelegate, TestGenericDelegateStringToInt

- **Generic Interfaces** (`IContainer<int>`, `IContainer<string>`):
  - Fixed `PopulateGenericInstInterfaceMap()` to use correct base vtable slot count
  - Was passing `numVtableSlots` (5) instead of `baseVtableSlots` (3), causing StartSlot overflow
  - Added `GetGenericDefinitionMT()` for fallback lookup in `EnsureVtableSlotCompiled`
  - 3 new tests: TestGenericInterfaceInt, TestGenericInterfaceSetGet, TestGenericInterfaceString

- **Nullable<T> Boxing Fix**:
  - Fixed `IsNullableGenericDef()` token format - was expecting standard metadata token but received normalized `(asmId << 24) | typeDefRow`
  - This caused `IsNullable` flag to never be set, breaking special boxing/unboxing semantics
  - All 7 Nullable boxing tests now pass

- Test count increased from 201 to 208

### Generic Method Multiple Type Parameters Fix (2025-12)
Fixed `Combine<T1, T2>(T1 first, T2 second)` test failure:
- Bug: Test returned 0 instead of 42 when calling `first.GetHashCode() ^ second.GetHashCode()` with ints
- Root cause: Primitive MTs used Object.GetHashCode (returns object address) instead of type-specific GetHashCode
- Fix 1: Added `Int32Helpers.GetHashCode()` to AotMethodRegistry that reads value from boxed int
- Fix 2: Added GetHashCode lookups for all primitives in MetadataIntegration
- Fix 3: Fixed vtable slot assignment for AOT methods - Object methods get correct slots (ToString=0, Equals=1, GetHashCode=2)
- Fix 4: Updated primitive MT initialization to use type-specific GetHashCode in vtable slot 2
- Test count increased from 201 to 203

### Generic Interface Investigation (2025-12)
Investigated and fixed generic interface dispatch (`IContainer<int>.GetValue()`):
- Added TypeSpec handling to `IsInterfaceMethod()` for generic interface tokens
- Fixed MVAR resolution to use `GetMethodTypeArgMethodTable()` instead of class type args
- Initial MT deduplication issue was actually caused by wrong interface StartSlot calculation
- **FIXED**: See "Generic Delegates and Interfaces Fix" above - tests now enabled and passing

### Verified ⚠️ Items (2025-12)
Tested and verified three items that were marked as partially tested:
- ✅ Explicit interface implementation: ExplicitImpl with IValue (implicit) and IExplicit (explicit)
- ✅ Struct with reference type fields: StructWithRef containing int Value and string Name
- ✅ Generic method on generic class: GenericContainer<T>.Convert<TResult>()
- 10 new tests added, test count increased from 191 to 201

### Struct Boxing Fix (2025-12)
Fixed boxing of JIT-created structs (SimpleStruct, MediumStruct, LargeStruct):
- Bug: `RhpNewFast` allocated `BaseSize` bytes, but JIT structs have BaseSize = raw value size (no MT overhead)
- AOT primitives have BaseSize = 8 + value size (includes MT pointer overhead)
- JIT structs have BaseSize = value size only (no overhead included)
- Fix: `RhpNewFast` now detects JIT structs (ComponentSize == 0) and adds 8 bytes for MT pointer
- Also fixed `CompileBox` to use ComponentSize for AOT types, BaseSize for JIT structs
- Also fixed multi-slot struct stack cleanup in `CompileBox` (was leaking struct data on stack)
- 5 boxing tests now pass: TestBoxInt, TestBoxStruct, TestBoxMediumStruct, TestBoxLargeStruct, TestNewObjManyArgs
- 7 instance tests also enabled and passing

### Filter Handler Tests (2025-12)
- Added and verified `catch when (condition)` filter tests
- TestCatchWhenTrue: filter evaluates to true, catch block executes
- TestCatchWhenFalse: filter evaluates to false, falls through to next catch

### Catch Handler Funclet Fix (2025-12)
Fixed exception propagation from catch handlers (throw inside catch block):
- Bug: Catch funclets didn't push exception object (RCX) onto eval stack
- IL handler starts with `pop` to discard exception, but funclet had nothing to pop
- This caused `add rsp, 8` to corrupt the stack layout (shifting return address)
- Fix: Catch funclets now emit `push rcx` after prolog and track exception on eval stack
- TestNestedTryCatch now passes (throw from inner catch to outer catch)

### Nullable<T> Lifted Operators (2025-12)
Verified lifted operators work without additional JIT changes:
- C# compiler generates inline code using HasValue, GetValueOrDefault(), and newobj
- 11 new tests added for lifted operators:
  - Addition with both values, first null, second null, both null
  - Subtraction, multiplication, division
  - Equality comparisons (same values, different values, both null, one null)

### Nullable<T> Boxing/Unboxing (2025-12)
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

### Nullable<T> Support (2025-12)
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

### Interface Dispatch (2025-12)
Implemented interface dispatch via `callvirt` on interface types:
- Added `IsInterfaceMethod()` / `IsMethodDefInterfaceMethod()` to detect interface method calls
- Added `CountInterfacesForType()` / `PopulateInterfaceMap()` to build interface maps in MethodTables
- Added `RegisterNewVirtualMethodsForLazyJit()` to register interface implementations for lazy JIT
- Modified `GetInterfaceMethod()` to call `EnsureVtableSlotCompiled()` for lazy compilation
- Test: `InterfaceTests.TestSimpleInterface()` - IValue interface with ValueImpl implementation

### Multiple Interfaces Fix (2025-12)
Fixed interface method argument count parsing:
- Interface methods were hardcoded to `ArgCount = 0` causing wrong argument counts
- Added signature parsing for MemberRef (cross-assembly) and MethodDef interface methods
- `ParseMemberRefSignatureForDelegate()` now shared for both delegates and interface methods
- Added `ParseMethodDefSignature()` for MethodDef interface method tokens
- 3 new tests: TestMultipleInterfacesFirst/Second/Third with IValue, IMultiplier, IAdder interfaces

### Static Constructor Support (2025-12)
Implemented static constructor (.cctor) invocation:
- `EnsureCctorContextRegistered()` finds and compiles cctors, registers context
- Context registered BEFORE compiling cctor to prevent infinite recursion when cctor accesses own type's fields
- `EmitCctorCheck()` generates inline code: load context, check if zero, if not zero clear and call
- `FindTypeCctor()` locates .cctor method for a TypeDef token
- cctor context stored as `StaticClassConstructionContext` with single `cctorMethodAddress` field
- Address=0 means "already run" or "being run", non-zero means "needs to run"
- 4 tests added:
  - TestStaticCtorInitializesField - cctor sets field to 42
  - TestStaticCtorRunsOnce - cctor only runs once across multiple accesses
  - TestStaticCtorOnWrite - writing to static field triggers cctor first
  - TestStaticCtorWithDependency - cctor with dependency on another type's static

### Finally in Loop Fix (2025-12)
Fixed stack corruption in finally handlers called from loops:
- Bug 1: Missing shadow space allocation when calling finally funclets from `leave`
  - The finally funclet's shadow space was overwriting caller's stack data
  - Fix: Added `sub rsp, 32` before call and `add rsp, 32` after in `CompileLeave()`
- Bug 2: Wrong local allocation formula in `CompileWithFunclets()`
  - Used `_localCount * 8 + 64` instead of `_localCount * 64 + 64`
  - This caused stack underallocation for methods with 3+ locals and EH clauses
  - Fix: Changed formula to match regular `Compile()` path (64 bytes per local)
- Symptom: Loop variable `i` showed corrupted values (e.g., 1066888 instead of 0,1,2)
- Test: `TestFinallyInLoopWithBreak` - finally runs on each loop iteration including break

### Interface Casting Tests (2025-12)
Added tests for isinst/castclass with interface types:
- TestIsinstInterfaceSuccess - object implements interface, returns object
- TestIsinstInterfaceFailure - object doesn't implement interface, returns null
- TestIsinstNull - null input returns null
- TestIsinstMultipleFirst/Second - isinst with multiple interfaces, then call through result
- TestCastclassInterfaceSuccess - explicit cast to interface type
