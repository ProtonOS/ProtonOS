# korlib Expansion & System.Runtime.dll Implementation Plan

## Overview

Expand korlib with compiler-required types and create System.Runtime.dll as a JIT-compiled assembly providing collections, async/await, and reflection support for DDK driver development.

## Architecture Decision

| Layer | Location | Compilation | Contents |
|-------|----------|-------------|----------|
| **korlib** | `src/korlib/` | AOT with kernel | Compiler intrinsics, interfaces, primitives, core attributes |
| **System.Runtime.dll** | `src/SystemRuntime/` | JIT at runtime | Collections, async/await, reflection, utilities |

---

## Phase 1: Compiler-Required Attributes (korlib)

**Files to create/modify:**
- `src/korlib/System/Index.cs` (new)
- `src/korlib/System/Range.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/IsExternalInit.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/NullableAttribute.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/CallerMemberNameAttribute.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/CallerFilePathAttribute.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/CallerLineNumberAttribute.cs` (new)
- `src/korlib/System/Runtime/CompilerServices/CallerArgumentExpressionAttribute.cs` (new)

**Types:**
- [x] `Index` - readonly struct for `^1` syntax
- [x] `Range` - readonly struct for `1..3` syntax
- [x] `IsExternalInit` - empty class for `init` setters
- [x] `NullableAttribute` - for nullable annotations
- [x] `NullableContextAttribute` - for nullable context
- [x] `NullablePublicOnlyAttribute` - for nullable public only
- [x] `CallerMemberNameAttribute`, `CallerFilePathAttribute`, `CallerLineNumberAttribute`, `CallerArgumentExpressionAttribute`
- [x] `TupleElementNamesAttribute` - for named tuple support
- [x] `FlagsAttribute` - for enum flags
- [x] `AttributeTargets` enum - expanded with all values

**Additional fixes:**
- [x] `Object.Equals()`, `Object.GetHashCode()`, `Object.ToString()` - virtual methods added
- [x] `Object.GetType()` - returns RuntimeType
- [x] `Type` abstract class with `FullName`, `Name`, `Namespace` properties
- [x] `RuntimeType` with MethodTable* constructor

---

## Phase 2: Core Interfaces (korlib)

**Files to create:**
- `src/korlib/System/IDisposable.cs`
- `src/korlib/System/ICloneable.cs`
- `src/korlib/System/IEquatable.cs`
- `src/korlib/System/IComparable.cs`
- `src/korlib/System/IFormattable.cs`
- `src/korlib/System/IFormatProvider.cs`
- `src/korlib/System/IAsyncDisposable.cs`
- `src/korlib/System/Collections/IEnumerable.cs`
- `src/korlib/System/Collections/IEnumerator.cs`
- `src/korlib/System/Collections/ICollection.cs`
- `src/korlib/System/Collections/IList.cs`
- `src/korlib/System/Collections/IDictionary.cs`
- `src/korlib/System/Collections/IComparer.cs`
- `src/korlib/System/Collections/IEqualityComparer.cs`
- `src/korlib/System/Collections/Generic/IEnumerable.cs`
- `src/korlib/System/Collections/Generic/IEnumerator.cs`
- `src/korlib/System/Collections/Generic/ICollection.cs`
- `src/korlib/System/Collections/Generic/IList.cs`
- `src/korlib/System/Collections/Generic/IReadOnlyCollection.cs`
- `src/korlib/System/Collections/Generic/IReadOnlyList.cs`
- `src/korlib/System/Collections/Generic/IDictionary.cs`
- `src/korlib/System/Collections/Generic/IReadOnlyDictionary.cs`
- `src/korlib/System/Collections/Generic/ISet.cs`
- `src/korlib/System/Collections/Generic/IComparer.cs`
- `src/korlib/System/Collections/Generic/IEqualityComparer.cs`
- `src/korlib/System/Collections/Generic/KeyValuePair.cs`

**Status: COMPLETE** - All interfaces created and building successfully.

---

## Phase 3: ValueTuple Expansion (korlib)

**Status: COMPLETE**

**File modified:**
- `src/korlib/System/ValueTuple.cs`

**Types added:**
- [x] `ValueTuple` (0-tuple)
- [x] `ValueTuple<T1>`
- [x] `ValueTuple<T1,T2>` (enhanced with ITuple)
- [x] `ValueTuple<T1,T2,T3>` through `ValueTuple<T1-T8>`
- [x] `ITuple` interface

---

## Phase 4: Action/Func Delegates (korlib)

**Status: COMPLETE**

**Files created:**
- `src/korlib/System/Action.cs`
- `src/korlib/System/Func.cs`
- `src/korlib/System/Predicate.cs`
- `src/korlib/System/EventHandler.cs`

**Types:**
- [x] `Action` through `Action<T1-T16>`
- [x] `Func<TResult>` through `Func<T1-T16, TResult>`
- [x] `Predicate<T>`, `Comparison<T>`, `Converter<TInput, TOutput>`
- [x] `EventHandler`, `EventHandler<TEventArgs>`
- [x] `EventArgs` base class

---

## Phase 5: Enhanced Primitives (korlib)

**Status: COMPLETE**

**File modified:**
- `src/korlib/System/Primitives.cs`

**Enhancements per primitive type (Int32, Int64, Byte, etc.):**
- [x] Implement `IEquatable<T>`, `IComparable<T>`, `IComparable`
- [x] Add `Parse(string)`, `TryParse(string, out T)`
- [x] Add `ToString()` (basic, no formatting)
- [x] Add `Equals(object)`, `Equals(T)`, `GetHashCode()`
- [x] Add `CompareTo(object)`, `CompareTo(T)`
- [x] Add `MinValue`, `MaxValue` constants

**Boolean enhancements:**
- [x] `Parse`, `TryParse`, `ToString`
- [x] `TrueString`, `FalseString` constants

**Char enhancements:**
- [x] `IsDigit`, `IsLetter`, `IsWhiteSpace`, `IsUpper`, `IsLower`
- [x] `ToUpper`, `ToLower`

**Float/Double enhancements:**
- [x] `IsNaN`, `IsInfinity`, `IsFinite`
- [x] `NaN`, `PositiveInfinity`, `NegativeInfinity`, `Epsilon`

---

## Phase 6: String Enhancement (korlib)

**Status: COMPLETE**

**File modified:**
- `src/korlib/System/String.cs`

**Methods added:**
- [x] `static Empty` property
- [x] `IsNullOrEmpty(string?)`, `IsNullOrWhiteSpace(string?)`
- [x] `IndexOf(char)`, `IndexOf(char, int)`, `IndexOf(string)`, `LastIndexOf(char)`
- [x] `Contains(char)`, `Contains(string)`
- [x] `StartsWith(char)`, `StartsWith(string)`, `EndsWith(char)`, `EndsWith(string)`
- [x] `Substring(int)`, `Substring(int, int)`
- [x] `Trim()`, `TrimStart()`, `TrimEnd()`
- [x] `static Concat(string?, string?)`, `Concat(string?, string?, string?)`, `Concat(string?, string?, string?, string?)`
- [x] `static Join(char, string?[])`, `Join(string?, string?[])`
- [x] `Split(char)`
- [x] `Replace(char, char)`
- [x] `ToLower()`, `ToUpper()`
- [x] `Equals(string)`, `GetHashCode()`, `CompareTo(string)`, `CompareTo(object)`
- [x] Implement `IEquatable<string>`, `IComparable<string>`, `IComparable`
- [x] `operator ==`, `operator !=` for string comparison
- [x] Additional constructors: `(char, int)`, `(char*, int, int)`, `(char[])`, `(char[], int, int)`

---

## Phase 7: Math Enhancement (korlib)

**Status: COMPLETE**

**File modified:**
- `src/korlib/System/Math.cs`

**Methods added:**
- [x] `Min(int, int)`, `Min(long, long)`, `Min(float, float)`, `Min(double, double)` + unsigned variants
- [x] `Max(int, int)`, `Max(long, long)`, `Max(float, float)`, `Max(double, double)` + unsigned variants
- [x] `Abs(int)`, `Abs(long)`, `Abs(float)`, `Abs(double)`
- [x] `Sign(int)`, `Sign(long)`, `Sign(float)`, `Sign(double)`
- [x] `Clamp(int, int, int)`, `Clamp(long, long, long)`, `Clamp(float)`, `Clamp(double)`
- [x] `Floor(double)`, `Ceiling(double)`, `Round(double)`, `Truncate(double)`
- [x] `Sqrt(double)` - Newton-Raphson implementation
- [x] `Pow(double, double)` - integer and fractional powers
- [x] `Sin(double)`, `Cos(double)`, `Tan(double)` - Taylor series
- [x] `Asin(double)`, `Acos(double)`, `Atan(double)`, `Atan2(double, double)`
- [x] `Log(double)`, `Log10(double)`, `Log2(double)`, `Log(double, double)`
- [x] `Exp(double)` - Taylor series
- [x] Constants: `PI`, `E`, `Tau`
- [x] `DivRem(int)`, `DivRem(long)`, `BigMul(int, int)`, `BigMul(long, long)`

---

## Phase 8: Exception Types (korlib)

**Status: COMPLETE**

**File modified:**
- `src/korlib/System/Exception.cs`

**Exceptions added:**
- [x] `FormatException`
- [x] `TypeLoadException`
- [x] `MissingMemberException`, `MissingMethodException`, `MissingFieldException`
- [x] `TypeInitializationException`
- [x] `ObjectDisposedException`
- [x] `TimeoutException`
- [x] `KeyNotFoundException`

**Note:** `AggregateException`, `OperationCanceledException`, `TaskCanceledException` deferred to Phase 12 (async/await)

---

## Phase 9: System.Runtime.dll Project Setup

**Status: COMPLETE**

**Files created:**
- `src/SystemRuntime/System.Runtime.csproj`
- `src/SystemRuntime/SystemRuntimeMarker.cs`

**Build integration:**
- [x] Created csproj targeting net10.0
- [x] Added to Makefile to compile after kernel
- [x] Copies to boot image automatically
- [x] Build verified working (55 tests pass)

---

## Phase 10: Utility Types (System.Runtime.dll)

**Status: COMPLETE**

**Files created:**
- `src/SystemRuntime/System/BitConverter.cs`
- `src/SystemRuntime/System/Guid.cs`
- `src/SystemRuntime/System/TimeSpan.cs`
- `src/SystemRuntime/System/DateTime.cs`
- `src/SystemRuntime/System/DateTimeKind.cs`
- `src/SystemRuntime/System/HashCode.cs`
- `src/SystemRuntime/System/ArraySegment.cs`
- `src/SystemRuntime/System/StringComparison.cs`

**Types:**
- [x] `BitConverter` - byte array conversions
- [x] `Guid` - 16-byte identifier
- [x] `TimeSpan` - time duration
- [x] `DateTime` - date/time (basic)
- [x] `HashCode` - hash combining utility
- [x] `ArraySegment<T>` - array slice wrapper
- [x] `StringComparison` enum

---

## Phase 11: Collections (System.Runtime.dll)

**Status: COMPLETE**

**Files created:**
- `src/SystemRuntime/System/Collections/Generic/List.cs`
- `src/SystemRuntime/System/Collections/Generic/Dictionary.cs`
- `src/SystemRuntime/System/Collections/Generic/HashSet.cs`
- `src/SystemRuntime/System/Collections/Generic/Queue.cs`
- `src/SystemRuntime/System/Collections/Generic/Stack.cs`
- `src/SystemRuntime/System/Collections/Generic/EqualityComparer.cs`
- `src/SystemRuntime/System/Collections/Generic/Comparer.cs`
- `src/SystemRuntime/System/Collections/ObjectModel/ReadOnlyCollection.cs`

**Key implementations:**
- [x] `List<T>` - dynamic array with Add, Remove, Insert, IndexOf, Contains, Sort, etc.
- [x] `Dictionary<TKey, TValue>` - hash table with Add, Remove, TryGetValue, ContainsKey, etc.
- [x] `HashSet<T>` - unique set with Add, Remove, Contains, UnionWith, IntersectWith
- [x] `Queue<T>` - FIFO with Enqueue, Dequeue, Peek
- [x] `Stack<T>` - LIFO with Push, Pop, Peek
- [x] `EqualityComparer<T>.Default`, `Comparer<T>.Default`
- [x] `ReadOnlyCollection<T>` - read-only wrapper

**Deferred (lower priority):**
- LinkedList<T>, SortedList<TKey,TValue>, Collection<T>

---

## Phase 12: Async/Await Infrastructure (System.Runtime.dll)

**Status: COMPLETE**

**Files created:**
- `src/SystemRuntime/System/Threading/Tasks/Task.cs` (includes Task<TResult>)
- `src/SystemRuntime/System/Threading/Tasks/ValueTask.cs`
- `src/SystemRuntime/System/Threading/Tasks/TaskCompletionSource.cs`
- `src/SystemRuntime/System/Threading/Tasks/TaskStatus.cs`
- `src/SystemRuntime/System/Threading/Tasks/TaskCanceledException.cs`
- `src/SystemRuntime/System/Threading/CancellationToken.cs`
- `src/SystemRuntime/System/Threading/CancellationTokenSource.cs`
- `src/SystemRuntime/System/Runtime/CompilerServices/TaskAwaiter.cs`
- `src/SystemRuntime/System/Runtime/CompilerServices/ValueTaskAwaiter.cs`
- `src/SystemRuntime/System/Runtime/CompilerServices/AsyncMethodBuilder.cs` (includes ValueTask builders)
- `src/SystemRuntime/System/Runtime/CompilerServices/IAsyncStateMachine.cs`
- `src/SystemRuntime/System/Runtime/CompilerServices/INotifyCompletion.cs` (includes ICriticalNotifyCompletion)
- `src/SystemRuntime/System/Runtime/CompilerServices/ConfiguredTaskAwaitable.cs`
- `src/SystemRuntime/System/AggregateException.cs`
- `src/SystemRuntime/System/OperationCanceledException.cs`

**Types:**
- [x] `Task`, `Task<TResult>` - async operation representation
- [x] `ValueTask`, `ValueTask<TResult>` - allocation-free async
- [x] `TaskCompletionSource<T>` - manual task completion
- [x] `CancellationToken`, `CancellationTokenSource` - cancellation support
- [x] `TaskAwaiter`, `TaskAwaiter<T>` - awaiter pattern
- [x] `AsyncTaskMethodBuilder`, `AsyncTaskMethodBuilder<T>` - state machine builders
- [x] `AsyncValueTaskMethodBuilder`, `AsyncValueTaskMethodBuilder<T>` - ValueTask builders
- [x] `IAsyncStateMachine` - compiler-generated state machine interface
- [x] `INotifyCompletion`, `ICriticalNotifyCompletion` - completion callbacks
- [x] `AggregateException`, `OperationCanceledException`, `TaskCanceledException`

---

## Phase 13: Reflection Support (System.Runtime.dll + JIT)

**Status: PARTIAL (stub types complete, deep reflection deferred)**

**Files created (System.Runtime.dll):**
- `src/SystemRuntime/System/Reflection/Assembly.cs`
- `src/SystemRuntime/System/Reflection/MemberInfo.cs`
- `src/SystemRuntime/System/Reflection/MethodBase.cs`
- `src/SystemRuntime/System/Reflection/MethodInfo.cs` (includes ConstructorInfo, FieldInfo, PropertyInfo, EventInfo, ParameterInfo)
- `src/SystemRuntime/System/Reflection/BindingFlags.cs`
- `src/SystemRuntime/System/Reflection/MemberTypes.cs`
- `src/SystemRuntime/System/Activator.cs`

**Implemented:**
- [x] `BindingFlags` enum
- [x] `MemberTypes` enum
- [x] `MemberInfo` abstract base class
- [x] `MethodBase` abstract class
- [x] `MethodInfo`, `ConstructorInfo`, `FieldInfo`, `PropertyInfo`, `EventInfo`, `ParameterInfo` stub classes
- [x] `Assembly` stub class
- [x] `Activator.CreateInstance<T>()` (basic)

**Deferred (requires JIT integration):**
- [ ] `MethodInfo.Invoke()` dynamic invocation
- [ ] `FieldInfo.GetValue()`, `SetValue()` dynamic access
- [ ] `PropertyInfo.GetValue()`, `SetValue()` dynamic access
- [ ] `Type.GetMembers()`, `GetMethods()`, `GetFields()`, `GetProperties()` metadata queries
- [ ] `Assembly.GetTypes()`, `GetType(string)` type enumeration

---

## Phase 14: StringBuilder (System.Runtime.dll)

**Status: COMPLETE**

**File created:**
- `src/SystemRuntime/System/Text/StringBuilder.cs`

**Methods:**
- [x] `Append(char)`, `Append(string)`, `Append(int)`, `Append(bool)`, `Append(object)`, etc.
- [x] `AppendLine()`, `AppendLine(string)`
- [x] `Insert(int, string)`, `Insert(int, char)`
- [x] `Remove(int, int)`
- [x] `Replace(char, char)`, `Replace(string, string)`
- [x] `Clear()`
- [x] `ToString()`, `ToString(int, int)`
- [x] `Length`, `Capacity`, `MaxCapacity` properties
- [x] `EnsureCapacity(int)`
- [x] Indexer `this[int]` with get/set

---

## Phase 15: Additional Compiler Support (korlib)

**Status: COMPLETE**

**Files created:**
- `src/korlib/System/Runtime/CompilerServices/AsyncStateMachineAttribute.cs` (includes IteratorStateMachineAttribute, AsyncIteratorStateMachineAttribute, StateMachineAttribute, CompilerGeneratedAttribute)
- `src/korlib/System/Runtime/CompilerServices/TupleElementNamesAttribute.cs` (in CompilerAttributes.cs)

**Types:**
- [x] `AsyncStateMachineAttribute` - marks async methods
- [x] `IteratorStateMachineAttribute` - marks iterator methods
- [x] `AsyncIteratorStateMachineAttribute` - marks async iterator methods
- [x] `StateMachineAttribute` - base class for state machine attributes
- [x] `CompilerGeneratedAttribute` - marks compiler-generated code
- [x] `TupleElementNamesAttribute` - for named tuple elements

---

## Dependency Order

```
Phase 1: Compiler Attributes (no dependencies)
    ↓
Phase 2: Core Interfaces (no dependencies)
    ↓
Phase 3: ValueTuple (ITuple from Phase 2)
    ↓
Phase 4: Action/Func (Delegate base exists)
    ↓
Phase 5: Enhanced Primitives (interfaces from Phase 2)
    ↓
Phase 6: String Enhancement (primitives from Phase 5)
    ↓
Phase 7: Math Enhancement (primitives from Phase 5)
    ↓
Phase 8: Exception Types (no dependencies)
    ↓
Phase 9: System.Runtime.dll Setup (korlib phases complete)
    ↓
Phase 10: Utility Types (interfaces from Phase 2)
    ↓
Phase 11: Collections (interfaces, comparers from Phase 10)
    ↓
Phase 12: Async/Await (delegates from Phase 4, exceptions from Phase 8)
    ↓
Phase 13: Reflection (collections from Phase 11, async from Phase 12)
    ↓
Phase 14: StringBuilder (string from Phase 6)
    ↓
Phase 15: Additional Compiler Support (async types from Phase 12)
```

---

## Estimated Scope

| Phase | New Files | Lines Est. |
|-------|-----------|------------|
| 1 | 9 | ~150 |
| 2 | 26 | ~400 |
| 3 | 1 (expand) | ~300 |
| 4 | 6 | ~250 |
| 5 | 1 (expand) | ~600 |
| 6 | 1 (expand) | ~500 |
| 7 | 1 (expand) | ~300 |
| 8 | 1 (expand) | ~200 |
| 9 | 2 | ~50 |
| 10 | 8 | ~800 |
| 11 | 11 | ~2500 |
| 12 | 16 | ~1500 |
| 13 | 14 | ~1500 |
| 14 | 1 | ~300 |
| 15 | 6 | ~150 |
| **Total** | **~104 files** | **~9500 lines** |

---

## Critical Files Summary

**korlib modifications:**
- `src/korlib/System/Primitives.cs` - add interface implementations
- `src/korlib/System/String.cs` - add string methods
- `src/korlib/System/Math.cs` - add math functions
- `src/korlib/System/ValueTuple.cs` - expand to T1-T8
- `src/korlib/System/Exception.cs` - add exception types

**New korlib files (most important):**
- `src/korlib/System/Index.cs`
- `src/korlib/System/Range.cs`
- `src/korlib/System/IDisposable.cs`
- `src/korlib/System/Collections/Generic/IEnumerable.cs`
- `src/korlib/System/Action.cs`
- `src/korlib/System/Func.cs`

**System.Runtime.dll (most important):**
- `src/SystemRuntime/System/Collections/Generic/List.cs`
- `src/SystemRuntime/System/Collections/Generic/Dictionary.cs`
- `src/SystemRuntime/System/Threading/Tasks/Task.cs`
- `src/SystemRuntime/System/Reflection/MethodInfo.cs`
- `src/SystemRuntime/System/Text/StringBuilder.cs`

---

## Notes

- Phases 1-8 must be completed before System.Runtime.dll can reference korlib types
- Collections (Phase 11) are the highest-value addition for DDK
- Async (Phase 12) enables non-blocking driver patterns
- Reflection (Phase 13) is complex and may require JIT modifications
- Can implement phases incrementally and test after each
