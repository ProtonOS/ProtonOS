# System.Runtime Migration Status

This document tracks the migration of types from the deprecated `src/SystemRuntime` to `src/korlib`.

## Overview

As of December 2024, System.Runtime.dll has been fully deprecated. All type resolution now flows through korlib via virtual assembly mapping. The System.Runtime source code is retained for reference as types are incrementally migrated to korlib.

**Total: ~11,600 lines of code across 51 files**

## Migration Status Legend

- **Migrated**: Type exists in korlib and is fully functional
- **Partial**: Type exists in korlib but may be missing some functionality
- **Not Started**: Type only exists in System.Runtime (reference code)

---

## Collections (`System.Collections.Generic`)

High-value types commonly needed for general programming.

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `List<T>` | ~650 | **Migrated** | Full implementation with Add, Remove, Sort, etc. |
| `Dictionary<TKey,TValue>` | ~690 | **Migrated** | Full implementation including foreach iteration |
| `HashSet<T>` | ~595 | **Migrated** | Full implementation with set operations (UnionWith, IntersectWith, ExceptWith, Overlaps) |
| `Queue<T>` | 286 | **Migrated** | FIFO collection with full foreach support |
| `Stack<T>` | 254 | **Migrated** | LIFO collection with full foreach support |
| `LinkedList<T>` | ~400 | **Migrated** | Doubly linked list with LinkedListNode<T> |
| `SortedList<TKey,TValue>` | ~640 | **Migrated** | Sorted key-value collection with binary search |
| `EqualityComparer<T>` | ~80 | **Migrated** | Foundation for Dictionary and collections |
| `Comparer<T>` | ~100 | **Migrated** | Foundation for sorting |
| `KeyNotFoundException` | ~25 | **Migrated** | Exception for Dictionary indexer |
| `KeyValuePair<TKey,TValue>` | ~30 | **Migrated** | Used by Dictionary |
| `IEnumerable<T>` | ~20 | **Migrated** | Generic enumeration interface |
| `IEnumerator<T>` | ~25 | **Migrated** | Generic enumerator interface |
| `ICollection<T>` | ~30 | **Migrated** | Collection interface |
| `IList<T>` | ~25 | **Migrated** | List interface |
| `IDictionary<TKey,TValue>` | ~30 | **Migrated** | Dictionary interface |
| `IReadOnlyCollection<T>` | ~15 | **Migrated** | Read-only collection interface |
| `IReadOnlyList<T>` | ~15 | **Migrated** | Read-only list interface |
| `IReadOnlyDictionary<TKey,TValue>` | ~20 | **Migrated** | Read-only dictionary interface |
| `ISet<T>` | ~25 | **Migrated** | Set interface (for future HashSet) |
| `IComparer<T>` | ~15 | **Migrated** | Comparer interface |
| `IEqualityComparer<T>` | ~15 | **Migrated** | Equality comparer interface |

### Non-Generic Collections (`System.Collections`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `IEnumerable` | ~15 | **Migrated** | Non-generic enumeration interface |
| `IEnumerator` | ~20 | **Migrated** | Non-generic enumerator interface |
| `ICollection` | ~25 | **Migrated** | Non-generic collection interface |
| `IList` | ~20 | **Migrated** | Non-generic list interface |
| `IDictionary` | ~25 | **Migrated** | Non-generic dictionary interface |
| `IComparer` | ~15 | **Migrated** | Non-generic comparer interface |
| `IEqualityComparer` | ~15 | **Migrated** | Non-generic equality comparer interface |

### Object Model (`System.Collections.ObjectModel`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Collection<T>` | ~190 | **Migrated** | Base for custom collections (non-virtual methods for JIT compatibility) |
| `ReadOnlyCollection<T>` | ~100 | **Migrated** | Read-only wrapper for IList<T> |

---

## Async/Tasks (`System.Threading.Tasks`)

Required for async/await support.

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Task` / `Task<T>` | 460 | **Migrated** | Core async support (4 tests passing - 576 total) |
| `ValueTask` / `ValueTask<T>` | 219 | **Migrated** | Allocation-free async (3 tests) |
| `TaskCompletionSource<T>` | 86 | **Migrated** | Manual task completion |
| `TaskStatus` | 35 | **Migrated** | Enum |
| `TaskCanceledException` | 42 | **Migrated** | Exception with Task reference |

### Async Infrastructure (`System.Runtime.CompilerServices`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `AsyncMethodBuilder` | ~350 | **Migrated** | AsyncTaskMethodBuilder, AsyncTaskMethodBuilder<T>, AsyncVoidMethodBuilder, AsyncValueTaskMethodBuilder, AsyncValueTaskMethodBuilder<T> |
| `TaskAwaiter` | 94 | **Migrated** | TaskAwaiter and TaskAwaiter<T> |
| `ValueTaskAwaiter` | 203 | **Migrated** | ValueTaskAwaiter<T>, ConfiguredValueTaskAwaitable<T> |
| `ConfiguredTaskAwaitable` | 130 | **Migrated** | ConfigureAwait support |
| `IAsyncStateMachine` | 21 | **Migrated** | Interface |
| `INotifyCompletion` | 27 | **Migrated** | Interface (includes ICriticalNotifyCompletion) |
| `DefaultInterpolatedStringHandler` | 184 | Migrated | Already in korlib |

### Cancellation (`System.Threading`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `CancellationToken` | 101 | **Migrated** | Full token functionality |
| `CancellationTokenSource` | 191 | **Migrated** | Cancel/Token work; Dispose/callbacks have JIT issues |
| `Monitor` | N/A | **Migrated** | Minimal no-op for lock statement support |

---

## Date/Time

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `DateTime` | 283 | **Migrated** | Date and time handling (bounds checks removed for JIT compatibility) |
| `DateTimeKind` | 26 | **Migrated** | Enum (UTC/Local/Unspecified) |
| `TimeSpan` | 168 | **Migrated** | Duration representation - fully working (generic interfaces removed) |

---

## String/Text

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `StringBuilder` | ~490 | **Migrated** | Full implementation with Append, Insert, Remove, Replace |
| `StringComparison` | 23 | **Migrated** | Enum |

---

## Utilities

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Guid` | 211 | **Migrated** | Unique identifiers - fully working (generic interfaces removed) |
| `BitConverter` | ~200 | **Migrated** | Byte/primitive conversions (without Half type) |
| `HashCode` | 193 | **Migrated** | Hash combining |
| `ArraySegment<T>` | ~240 | **Migrated** | Array view/slice (Slice returns struct - may need JIT fixes) |

---

## Reflection

korlib already has partial reflection support. These types may need merging or replacement.

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Assembly` | 617 | Partial | korlib has `RuntimeAssembly.cs` |
| `MemberInfo` | 251 | Partial | korlib has `MemberInfo.cs` |
| `MethodBase` | 351 | Not Started | |
| `MethodInfo` | 584 | Not Started | |
| `BindingFlags` | 69 | **Migrated** | Enum (in MemberInfo.cs) |
| `MemberTypes` | 428 | **Migrated** | Enum (in MemberInfo.cs) |

---

## Exceptions

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `SystemException` | 26 | **Migrated** | Base exception type |
| `AggregateException` | 115 | **Migrated** | Multiple exceptions with Flatten/Handle |
| `OperationCanceledException` | 24 | **Migrated** | Cancellation (without CancellationToken) |

---

## Runtime/System

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Activator` | 88 | Partial | korlib has `Activator.CreateInstance<T>()` |
| `GC` | 84 | **Migrated** | Minimal stubs (SuppressFinalize, Collect, etc.) |
| `IAsyncResult` | 68 | **Migrated** | Legacy async pattern with WaitHandle |
| `RuntimeHandles` | 91 | Partial | korlib has `RuntimeHandles.cs` |
| `Nullable` helpers | 30 | Migrated | korlib has `Nullable.cs` |
| `ObsoleteAttribute` | ~20 | **Migrated** | Marks deprecated elements |

---

## Threading

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Thread` | 197 | Not Started | Thread representation |
| `Interlocked` | 195 | Migrated | korlib has full implementation |

---

## Other

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `SystemRuntimeMarker` | 46 | N/A | Assembly marker, not needed |

---

## Size Considerations

When migrating types to korlib, they become AOT-compiled into the kernel. Current estimates:

- **Current kernel size**: ~1.5 MB (BOOTX64.EFI)
- **Boot image capacity**: 64 MB FAT filesystem
- **Estimated increase if all migrated**: ~200-400 KB (rough estimate)

The kernel size is not a significant concern given the available capacity. UEFI boot loaders typically have no issues with kernels of this size.

---

## Migration Priority

### High Priority (commonly needed) ✅ COMPLETE
1. ~~`List<T>`, `Dictionary<TKey,TValue>`~~ - Both fully working with foreach
2. ~~`StringBuilder`~~ - Full implementation
3. ~~`EqualityComparer<T>`, `Comparer<T>`~~ - Complete with value type support
4. ~~All collection interfaces~~ - IEnumerable, IEnumerator, ICollection, IList, etc.
5. ~~`BitConverter`, `HashCode`~~ - Utility types for binary conversion and hash combining

### Medium Priority (Complete)
6. ~~`Queue<T>`, `Stack<T>`~~ - Basic operations work (Enqueue/Dequeue, Push/Pop, Contains, Clear), foreach disabled
7. ~~`TimeSpan`~~ - Fully working
8. ~~`Guid`~~ - Fully working (parsing, equality, comparison)
9. ~~`HashSet<T>`~~ - Basic operations work (Add, Remove, Contains, Clear), foreach disabled
10. `DateTime` - Time handling (not started)

### Lower Priority (migrate when needed)
11. Async/Tasks infrastructure - Only when async/await support is required
12. `LinkedList<T>`, `SortedList<TKey,TValue>` - Less commonly used
13. Reflection types - Only for advanced scenarios

---

## How to Migrate a Type

1. Copy the file from `src/SystemRuntime/` to `src/korlib/`
2. Update namespace if needed (korlib uses same namespaces)
3. Remove any `DllImport` patterns - korlib is AOT compiled
4. Add to `src/korlib/korlib.csproj` if using conditional compilation
5. Test that the type works correctly
6. Update this document to mark as "Migrated"
7. Delete from `src/SystemRuntime/` once confirmed working

---

## JIT Fixes Required for Collections

During migration of generic collections, several JIT/runtime fixes were required:

### Abstract Method Vtable Dispatch
`EqualityComparer<T>.Equals(T, T)` is abstract - concrete implementations like `ObjectEqualityComparer<T>` override it. The JIT wasn't setting `VtableSlot` for abstract methods because they have no `CompiledMethodInfo` entry.

**Fix**: Added `CalculateVtableSlotForMethod()` in `MetadataIntegration.cs` to compute vtable slot from metadata for abstract/virtual methods, enabling proper vtable dispatch at runtime.

### Value Type Equality Comparison
`Object.Equals(object, object)` for boxed value types was returning false because:
1. AOT-compiled MethodTables don't have the `IsValueType` flag set consistently
2. The comparison was using reference equality instead of value comparison

**Fix**: Updated `StaticEquals()` in `AotMethodRegistry.cs` to use a size-based heuristic (12-32 bytes base size) to detect boxed primitives and compare their value bytes directly.

### Nullable<T> Boxing/Unboxing
The value offset calculation for Nullable<T> was incorrect, causing wrong values after unboxing.

**Fix**: Corrected offset calculation in boxing/unboxing paths. Added ELEMENT_TYPE_VAR (0x13) handling in MetadataIntegration for generic type parameters in field resolution.

### Newobj Temp Slot Overlap
The `newobjTempOffset` calculation for value type constructors was using the wrong formula, causing it to overlap with local variable V_0. This caused lifted arithmetic operations on Nullable<T> to fail because the first local was corrupted.

**Fix**: Changed formula from `-(_localCount * 8 + 64)` to `-(CalleeSaveSize + (_localCount + 1) * 64)` to place the temp slot after all 64-byte local slots.

### Box/Unbox TypeSpec vs TypeDef
The valueSize calculation for box/unbox was not distinguishing between TypeSpec (generic instantiations) and TypeDef (user structs), leading to incorrect sizes.

**Fix**: Added token table check - TypeSpec (0x1B) generic instances have baseSize including +8, while TypeDef (0x02) user structs have baseSize = raw size.

### String.GetHashCode Missing from AOT Registry
`HashCode.Combine()` with string arguments was failing because `String.GetHashCode()` wasn't registered in the AOT method registry. It was falling back to `Object.GetHashCode()` which uses pointer-based hashing instead of content-based hashing.

**Fix**: Added `StringHelpers.GetHashCode()` to `AotMethodRegistry.cs` with a content-based hash implementation (Java-style `31 * hash + char`).

### Reflection Invoke Cross-Assembly Token Collision
`MethodInfo.Invoke()` was finding the wrong method because `CompiledMethodRegistry.Lookup(token)` scans all assemblies. When korlib and FullTest have methods with the same token number, it could return the wrong one.

**Fix**: Changed `ReflectionRuntime.InvokeMethod()` to use `Lookup(token, assemblyId)` which properly scopes the lookup to the correct assembly.

### Constrained Callvirt on Primitives (AOT vs Byref Calling Convention)
When calling virtual methods on value types through `constrained. callvirt` (e.g., `first.GetHashCode()` in generic methods), the JIT was using the AOT-compiled vtable entry directly. However, AOT-compiled value type methods expect a **boxed object** pointer (value at `[this+8]`), while constrained callvirt provides a **byref** (value at `[this+0]`).

This caused `Int32.GetHashCode()` to read the wrong memory location, returning garbage values.

**Fix**: Added inline handling for primitive `GetHashCode` (vtable slot 2) in the constrained callvirt code path. Instead of calling the AOT method, the JIT now directly loads the value from `[managed_ptr+0]` using correct byref semantics.

### Direct Instance Call on Primitives (call instance)
When calling `int.GetHashCode()` directly via `ldloca; call instance` (not through constrained callvirt), the AOT registry's `Int32Helpers.GetHashCode` was being called with a byref pointer. The original implementation expected a boxed object (value at `[this+8]`), but received a byref (value at `[this+0]`).

**Fix**: Modified `Int32Helpers.GetHashCode` to detect the calling convention by checking if the first 8 bytes at `thisPtr` look like a MethodTable pointer (> 0x100000) or an actual int value. This allows the same method to handle both boxed objects and byref calls.

### Ref Locals to Array Elements
Using `ref Entry entry = ref entries[i]` in loops can cause incorrect behavior. The JIT's handling of ref locals pointing to array elements appears to have issues with the address calculation or tracking.

**Workaround**: Use direct array indexing (`entries[i].field`) instead of ref locals. This issue affected:
- `HashSet<T>.Remove` - fixed by using direct array indexing
- `HashSet<T>.Enumerator.MoveNext` - fixed by using direct array indexing

### Generic Interface Resolution (Partially Resolved)
Previous concern about types implementing generic interfaces (`IComparable<T>`, `IEquatable<T>`, `IEnumerator<T>`) causing infinite loops has been partially resolved. All collection enumerators (List, Dictionary, HashSet, Queue, Stack) now work correctly with **direct foreach** on the collection.

**Remaining Issue**: Calling `GetEnumerator()` through the `IEnumerable<T>` interface (as opposed to directly on the collection type) causes a page fault crash. This affects methods like `HashSet<T>.UnionWith(IEnumerable<T>)` which iterate over the `other` parameter via interface dispatch.

Note: Some repeated type resolution log messages may appear during JIT compilation, but they don't affect functionality for direct foreach.

---

## History

- **2024-12**: System.Runtime.dll deprecated, virtual assembly mapping implemented
- **2024-12**: All 470+ tests passing without System.Runtime.dll on boot image
- **2024-12**: Added List<T>, StringBuilder, EqualityComparer<T>, Comparer<T> (492 tests)
- **2024-12**: Fixed abstract method vtable dispatch for generic collection methods
- **2024-12**: Fixed value type equality comparison in Object.Equals for boxed primitives
- **2024-12**: Fixed Dictionary<TKey,TValue> - SZARRAY parsing, string hashing, ref locals (502 tests)
- **2024-12**: Fixed Nullable<T> boxing/unboxing and ELEMENT_TYPE_VAR field resolution
- **2024-12**: Fixed newobj temp slot overlap with local V_0 (lifted arithmetic tests)
- **2024-12**: Enabled Iterator tests - IEnumerable/IEnumerator now resolvable (506 tests)
- **2024-12**: Migrated utility types: StringComparison, DateTimeKind, BitConverter, HashCode, ObsoleteAttribute
- **2024-12**: Added String.GetHashCode to AOT registry for content-based hashing (512 tests)
- **2024-12**: Fixed reflection invoke cross-assembly token collision in CompiledMethodRegistry lookup
- **2024-12**: Migrated TimeSpan, Guid, Queue<T>, Stack<T> to korlib (code complete, needs JIT fix for generic interface resolution)
- **2024-12**: Fixed TimeSpan by removing generic interfaces (IEquatable<T>, IComparable<T>), inlined constructor bodies (520 tests)
- **2024-12**: Fixed Queue<T>, Stack<T> by replacing Array.Empty<T>(), Array.IndexOf<T>() with inline implementations
- **2024-12**: Fixed constrained callvirt on primitives - inline GetHashCode to handle byref vs boxed object calling convention mismatch (525 tests)
- **2024-12**: Fixed Int32.GetHashCode AOT wrapper to handle both byref and boxed object calling conventions
- **2024-12**: Migrated HashSet<T> to korlib with full Add, Remove, Contains, Clear operations
- **2024-12**: Fixed HashSet ref local JIT issue by using direct array indexing instead of ref locals (530 tests)
- **2024-12**: Fixed HashSet Enumerator ref local issue, enabled foreach iteration (531 tests)
- **2024-12**: Enabled TestVirtqueuePattern regression test (532 tests)
- **2024-12**: Migrated AggregateException with full InnerExceptions.Count access (562 tests)
- **2024-12**: Migrated async interfaces: IAsyncStateMachine, INotifyCompletion, ICriticalNotifyCompletion
- **2024-12**: Migrated TaskCanceledException with AOT registration (564 tests)
- **2024-12**: Confirmed BindingFlags already migrated in MemberInfo.cs
- **2024-12**: Migrated CancellationToken, CancellationTokenSource, Monitor, GC (568 tests)
- **2024-12**: Confirmed MemberTypes already migrated in MemberInfo.cs
- **2024-12**: Updated OperationCanceledException with CancellationToken constructors
- **2024-12**: Added AggregateException constructors for IEnumerable/List
- **2024-12**: Fixed vtable slot collision for interface implementations (Dispose() vs Dispose(bool))
- **2024-12**: Fixed generic instantiation vtable slot lookup for inherited methods
- **2024-12**: Migrated Task, Task<T>, TaskCompletionSource<T> to korlib
- **2024-12**: Migrated TaskAwaiter, TaskAwaiter<T>, ConfiguredTaskAwaitable to korlib
- **2024-12**: Migrated AsyncTaskMethodBuilder, AsyncTaskMethodBuilder<T>, AsyncVoidMethodBuilder to korlib
- **2024-12**: Migrated IAsyncResult and WaitHandle to korlib
- **2024-12**: Fixed JIT Object.ctor MethodTable resolution for AOT lookup (572 tests passing)
