# PAL Reorganization Checklist

## Decision

**Full reorganization** with hybrid naming:
- `Reflection_*` prefix for reflection/type system exports
- `Kernel_*` prefix for architecture/system exports

---

## Current State Analysis

### What PAL Actually Does

PAL provides **Win32-compatible APIs** used internally by the AOT-compiled kernel code:

| Category | Files | Used By |
|----------|-------|---------|
| Memory | Memory.cs | GC, heap allocation, NativeAOT runtime |
| Threading | Thread.cs, TLS.cs | Scheduler, kernel threads |
| Synchronization | Sync.cs, CriticalSection.cs, SRWLock.cs, ConditionVariable.cs | Internal locking |
| Exception Handling | Exception.cs | SEH, stack unwinding |
| System Info | System.cs, Environment.cs | Timing, version checks |
| Interlocked | Interlocked.cs | Atomic operations |
| String | String.cs | UTF-8/UTF-16 conversion |
| Console | Console.cs | Debug output |
| XState | XState.cs | SSE/AVX context (stub) |
| COM | COM.cs | **DEAD CODE** - COM not supported |

### Export Mechanisms (The Confusion)

There are **three different export patterns** in the kernel:

1. **`kernel/PAL/*.cs`** - Win32 APIs with `public` methods
   - Called internally by AOT-compiled kernel code (statically linked)
   - Only 2 exports to korlib: `PalAllocObject`, `PalFailFast`

2. **`kernel/Runtime/Reflection/ReflectionRuntime.cs`** - Uses `[RuntimeExport("PalXxx")]`
   - 20+ functions: `PalGetTypeInfo`, `PalInvokeMethod`, `PalGetFieldValue`, etc.
   - Called by korlib via `DllImport("*")`
   - Named "Pal" but lives in Runtime, not PAL

3. **`kernel/Exports/*.cs`** - Uses `[UnmanagedCallersOnly(EntryPoint = "Kernel_Xxx")]`
   - DDK APIs: `Kernel_GetCpuCount`, `Kernel_SetThreadAffinity`, etc.
   - Called by JIT-compiled drivers

### The Naming Problem

The reflection exports in `ReflectionRuntime.cs` use `Pal` prefix but aren't in PAL. This creates confusion about what "PAL" means:
- Is PAL = "Win32 compatibility layer"?
- Or is PAL = "any kernel export for managed code"?

---

## Findings

### 1. PAL Is Necessary (Not RyuJIT-Specific)

PAL provides essential services that ANY runtime needs:
- Memory allocation (VirtualAlloc, HeapAlloc)
- Threading (CreateThread, Sleep, TLS)
- Synchronization (CriticalSection, Events, Mutexes)
- Exception handling (stack unwinding, context capture)

These are used by the NativeAOT runtime that bflat generates. Without them, the kernel wouldn't boot.

### 2. Dead/Unused Code in PAL

| File | Status | Recommendation |
|------|--------|----------------|
| COM.cs | Completely unused | **DELETE** |
| XState.cs | Stub only (SSE-only) | Keep for now, but mark as incomplete |

### 3. Organizational Issues

The current structure has overlap and naming confusion:

```
kernel/
  PAL/              <- Win32 APIs (internal kernel use)
  Runtime/
    Reflection/
      ReflectionRuntime.cs  <- Has "Pal" exports (external korlib use)
  Exports/          <- DDK APIs (external driver use)
  Platform/         <- Hardware discovery (internal)
```

---

## Reorganization Plan

### New Directory Structure

```
kernel/
  PAL/                <- Keep name (now correctly just Win32 compatibility layer)
    Memory.cs         <- VirtualAlloc, HeapAlloc, etc.
    Thread.cs         <- CreateThread, Sleep, etc.
    TLS.cs            <- Thread-local storage
    Sync.cs           <- Events, Mutexes, Semaphores
    CriticalSection.cs
    SRWLock.cs
    ConditionVariable.cs
    Interlocked.cs
    Exception.cs      <- SEH/stack unwinding
    System.cs         <- QueryPerformanceCounter, GetSystemInfo
    Environment.cs    <- Environment variables
    String.cs         <- UTF-8/UTF-16 conversion
    Console.cs        <- Debug output
    XState.cs         <- SSE/AVX context
    (DELETE COM.cs)   <- Dead code

  Exports/            <- External APIs for JIT-compiled code
    DDK/              <- Move existing exports here
      CPU.cs          <- Kernel_GetCpuCount, etc.
      NUMA.cs         <- Kernel_GetNumaNodeCount, etc.
    Reflection/       <- NEW: Move from Runtime/Reflection
      ReflectionExports.cs  <- Reflection_GetTypeInfo, etc.

    NOTE: PalAllocObject and PalFailFast stay in PAL (internal AOT linkage, not JIT exports)

  Runtime/            <- JIT & managed code execution (unchanged)
    JIT/
    Reflection/       <- Keep ReflectionRuntime.cs for internal logic
    AssemblyLoader.cs
    MetadataReader.cs
    ...

  Platform/           <- Hardware discovery (unchanged)
  Memory/             <- GC, heap (unchanged)
  Threading/          <- Scheduler (unchanged)
  Arch/               <- Architecture abstractions (unchanged)
  x64/                <- x64 implementation (unchanged)
```

### Export Naming Convention (24 exports in ReflectionRuntime.cs)

| Current Name | New Name | Category |
|--------------|----------|----------|
| `PalGetTypeInfo` | `Reflection_GetTypeInfo` | Type metadata |
| `PalInvokeMethod` | `Reflection_InvokeMethod` | Method invocation |
| `PalGetFieldValue` | `Reflection_GetFieldValue` | Field access |
| `PalSetFieldValue` | `Reflection_SetFieldValue` | Field access |
| `PalGetFieldValueRaw` | `Reflection_GetFieldValueRaw` | Field access |
| `PalGetFieldElementType` | `Reflection_GetFieldElementType` | Field metadata |
| `PalGetMethodName` | `Reflection_GetMethodName` | Method metadata |
| `PalGetMethodCount` | `Reflection_GetMethodCount` | Type enumeration |
| `PalGetMethodToken` | `Reflection_GetMethodToken` | Method metadata |
| `PalGetFieldCount` | `Reflection_GetFieldCount` | Type enumeration |
| `PalGetFieldToken` | `Reflection_GetFieldToken` | Field metadata |
| `PalGetFieldName` | `Reflection_GetFieldName` | Field metadata |
| `PalGetTypeName` | `Reflection_GetTypeName` | Type metadata |
| `PalGetTypeNamespace` | `Reflection_GetTypeNamespace` | Type metadata |
| `PalIsMethodStatic` | `Reflection_IsMethodStatic` | Method metadata |
| `PalIsMethodVirtual` | `Reflection_IsMethodVirtual` | Method metadata |
| `PalGetAssemblyCount` | `Reflection_GetAssemblyCount` | Assembly enumeration |
| `PalGetAssemblyIdByIndex` | `Reflection_GetAssemblyIdByIndex` | Assembly enumeration |
| `PalGetTypeCount` | `Reflection_GetTypeCount` | Type enumeration |
| `PalGetTypeTokenByIndex` | `Reflection_GetTypeTokenByIndex` | Type enumeration |
| `PalFindTypeByName` | `Reflection_FindTypeByName` | Type lookup |
| `PalGetTypeMethodTable` | `Reflection_GetTypeMethodTable` | Type metadata |
| `PalGetTypeFlags` | `Reflection_GetTypeFlags` | Type metadata |
| `PalGetTypeBaseType` | `Reflection_GetTypeBaseType` | Type metadata |

**Staying as-is (internal AOT linkage, not JIT exports):**

| Current Name | New Name | Location |
|--------------|----------|----------|
| `PalAllocObject` | No change | PAL (internal AOT) |
| `PalFailFast` | No change | PAL (internal AOT) |

---

## Implementation Checklist

### Phase 1: Clean Up PAL Directory
- [x] Keep `src/kernel/PAL/` and namespace `ProtonOS.PAL` (no rename needed)
- [x] Delete `COM.cs` (dead code)
- [x] Extract exports to `kernel/Exports/` (see Phase 3)

### Phase 2: Create Exports Structure
- [x] Create `src/kernel/Exports/DDK/` directory
- [x] Move `src/kernel/Exports/CPU.cs` to `src/kernel/Exports/DDK/CPU.cs`
- [x] Move `src/kernel/Exports/NUMA.cs` to `src/kernel/Exports/DDK/NUMA.cs`
- [x] Update namespace to `ProtonOS.Exports.DDK`

### Phase 3: Create Reflection Exports
- [x] Create `src/kernel/Exports/Reflection/ReflectionExports.cs`
- [x] Move export functions from `Runtime/Reflection/ReflectionRuntime.cs`
- [x] Rename `PalXxx` to `Reflection_Xxx`
- [x] Use namespace `ProtonOS.Exports.Reflection`
- [x] Keep internal helper code in `ReflectionRuntime.cs`

### Phase 4: Update korlib
- [x] Update all `DllImport` declarations in korlib to use new Reflection_ names:
  - [x] `src/korlib/System/Type.cs` (PalXxx -> Reflection_Xxx)
  - [x] `src/korlib/System/Reflection/RuntimeAssembly.cs` (PalXxx -> Reflection_Xxx)
  - [x] `src/korlib/System/Reflection/RuntimeReflection.cs` (PalXxx -> Reflection_Xxx)
- [x] Keep `src/korlib/Internal/Stubs.cs` unchanged (PalAllocObject stays as-is)

### Phase 5: Documentation
- [x] Update `docs/PAL_CHECKLIST.md` header to clarify PAL is now pure Win32 compatibility
- [x] Create `docs/KERNEL_EXPORTS.md` documenting the export structure
- [ ] Update `docs/ARCHITECTURE.md` with new organization (optional)

### Phase 6: Build & Test
- [x] Run `./build.sh` and fix any compilation errors
- [x] Run `./dev.sh ./run.sh` to verify kernel boots
- [x] Commit changes

---

## Files to Modify

### Move:
- `src/kernel/Exports/CPU.cs` -> `src/kernel/Exports/DDK/CPU.cs`
- `src/kernel/Exports/NUMA.cs` -> `src/kernel/Exports/DDK/NUMA.cs`

### Delete:
- `src/kernel/PAL/COM.cs` (dead code)

### Create:
- `src/kernel/Exports/Reflection/ReflectionExports.cs`
- `docs/KERNEL_EXPORTS.md`

### Modify:
- `src/kernel/Runtime/Reflection/ReflectionRuntime.cs` (extract exports to Exports/Reflection/)
- `src/korlib/System/Type.cs` (PalXxx -> Reflection_Xxx)
- `src/korlib/System/Reflection/RuntimeAssembly.cs` (PalXxx -> Reflection_Xxx)
- `src/korlib/System/Reflection/RuntimeReflection.cs` (PalXxx -> Reflection_Xxx)
- `docs/PAL_CHECKLIST.md` (update header/description)

### Unchanged:
- `src/kernel/PAL/Memory.cs` (PalAllocObject stays - internal AOT linkage)
- `src/kernel/PAL/Environment.cs` (PalFailFast stays - internal AOT linkage)
- `src/korlib/Internal/Stubs.cs` (PalAllocObject call stays)

---

## Summary

| Aspect | Current | After Reorganization |
|--------|---------|---------------------|
| Win32 APIs | `kernel/PAL/` (mixed with exports) | `kernel/PAL/` (pure Win32 + internal AOT linkage) |
| DDK exports | `kernel/Exports/` (flat) | `kernel/Exports/DDK/` |
| Reflection exports | In `Runtime/Reflection/` with `Pal` prefix | `kernel/Exports/Reflection/` with `Reflection_` prefix |
| AOT linkage | `PalAllocObject`, `PalFailFast` in PAL | **Unchanged** - stays in PAL |
| Naming | Mixed `Pal` for everything | `Pal` = internal AOT, `Reflection_`/`Kernel_` = JIT exports |
| PAL namespace | `ProtonOS.Kernel.PAL` | No change |
