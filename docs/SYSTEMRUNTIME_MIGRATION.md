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
| `Dictionary<TKey,TValue>` | ~690 | **Migrated** | Full implementation with KeyCollection, ValueCollection |
| `HashSet<T>` | 595 | Not Started | Commonly used |
| `Queue<T>` | 286 | Not Started | |
| `Stack<T>` | 254 | Not Started | |
| `LinkedList<T>` | 549 | Not Started | Less commonly used |
| `SortedList<TKey,TValue>` | 773 | Not Started | Less commonly used |
| `EqualityComparer<T>` | ~80 | **Migrated** | Foundation for Dictionary and collections |
| `Comparer<T>` | ~100 | **Migrated** | Foundation for sorting |
| `KeyNotFoundException` | ~25 | **Migrated** | Exception for Dictionary indexer |

### Object Model (`System.Collections.ObjectModel`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Collection<T>` | 259 | Not Started | Base for custom collections |
| `ReadOnlyCollection<T>` | 91 | Not Started | Read-only wrapper |

---

## Async/Tasks (`System.Threading.Tasks`)

Required for async/await support. Lower priority until async is needed.

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Task` / `Task<T>` | 460 | Not Started | Core async support |
| `ValueTask` / `ValueTask<T>` | 219 | Not Started | Allocation-free async |
| `TaskCompletionSource<T>` | 86 | Not Started | Manual task completion |
| `TaskStatus` | 35 | Not Started | Enum |
| `TaskCanceledException` | 42 | Not Started | Exception |

### Async Infrastructure (`System.Runtime.CompilerServices`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `AsyncMethodBuilder` | 326 | Not Started | State machine builder |
| `TaskAwaiter` | 94 | Not Started | Awaiter pattern |
| `ValueTaskAwaiter` | 203 | Not Started | ValueTask awaiter |
| `ConfiguredTaskAwaitable` | 130 | Not Started | ConfigureAwait support |
| `IAsyncStateMachine` | 21 | Not Started | Interface |
| `INotifyCompletion` | 27 | Not Started | Interface |
| `DefaultInterpolatedStringHandler` | 184 | Migrated | Already in korlib |

### Cancellation (`System.Threading`)

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `CancellationToken` | 101 | Not Started | |
| `CancellationTokenSource` | 191 | Not Started | |

---

## Date/Time

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `DateTime` | 283 | Not Started | Date and time handling |
| `DateTimeKind` | 26 | Not Started | Enum (UTC/Local/Unspecified) |
| `TimeSpan` | 168 | Not Started | Duration representation |

---

## String/Text

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `StringBuilder` | ~490 | **Migrated** | Full implementation with Append, Insert, Remove, Replace |
| `StringComparison` | 23 | Not Started | Enum |

---

## Utilities

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Guid` | 211 | Not Started | Unique identifiers |
| `BitConverter` | 234 | Not Started | Byte/primitive conversions |
| `HashCode` | 193 | Not Started | Hash combining |
| `ArraySegment<T>` | 231 | Not Started | Array view/slice |

---

## Reflection

korlib already has partial reflection support. These types may need merging or replacement.

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Assembly` | 617 | Partial | korlib has `RuntimeAssembly.cs` |
| `MemberInfo` | 251 | Partial | korlib has `MemberInfo.cs` |
| `MethodBase` | 351 | Not Started | |
| `MethodInfo` | 584 | Not Started | |
| `BindingFlags` | 69 | Not Started | Enum |
| `MemberTypes` | 428 | Not Started | Enum (large due to docs) |

---

## Exceptions

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `SystemException` | 65 | Not Started | Base exception type |
| `AggregateException` | 137 | Not Started | Multiple exceptions |
| `OperationCanceledException` | 50 | Not Started | Cancellation |

---

## Runtime/System

| Type | Lines | Status | Notes |
|------|-------|--------|-------|
| `Activator` | 88 | Partial | korlib has `Activator.CreateInstance<T>()` |
| `GC` | 84 | Not Started | GC control (stubs) |
| `IAsyncResult` | 68 | Not Started | Legacy async pattern |
| `RuntimeHandles` | 91 | Partial | korlib has `RuntimeHandles.cs` |
| `Nullable` helpers | 30 | Migrated | korlib has `Nullable.cs` |

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

### High Priority (commonly needed)
1. `List<T>`, `Dictionary<TKey,TValue>` - Most commonly used collections
2. `StringBuilder` - Essential for string manipulation
3. `EqualityComparer<T>`, `Comparer<T>` - Required by collections

### Medium Priority
4. `HashSet<T>`, `Queue<T>`, `Stack<T>` - Useful collections
5. `DateTime`, `TimeSpan` - Time handling
6. `Guid` - Unique identifiers

### Lower Priority (migrate when needed)
7. Async/Tasks infrastructure - Only when async/await support is required
8. `LinkedList<T>`, `SortedList<TKey,TValue>` - Less commonly used
9. Reflection types - Only for advanced scenarios

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

## History

- **2024-12**: System.Runtime.dll deprecated, virtual assembly mapping implemented
- **2024-12**: All 470+ tests passing without System.Runtime.dll on boot image
