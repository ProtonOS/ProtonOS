# Full stdlib Integration Checklist

This document tracks the requirements for switching from `--stdlib:zero` to full stdlib
in bflat, enabling use of System.Reflection.Metadata, GC, and other BCL features.

## Overview

Currently ProtonOS uses `--stdlib:zero` which provides only primitive types. Switching to
full stdlib requires implementing PAL (Platform Abstraction Layer) functions that the
NativeAOT runtime expects from the OS.

**Goal:** Enable full .NET stdlib for JIT/runtime integration while maintaining
bare-metal UEFI operation.

## References

- [bflat](https://github.com/bflattened/bflat) - C# AOT compiler with UEFI support
- [bflat BuildCommand.cs](https://github.com/bflattened/bflat/blob/master/src/bflat/BuildCommand.cs) - Build linking logic
- [NativeAOT Runtime](https://github.com/dotnet/runtime/tree/main/src/coreclr/nativeaot/Runtime)
- [PalMinWin.cpp](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/Runtime/windows/PalMinWin.cpp) - Windows PAL reference

---

## IMPORTANT: Native Runtime Libraries

When building with full stdlib (not `--stdlib:zero`), bflat links these native libraries:

| Library | Purpose | UEFI Implications |
|---------|---------|-------------------|
| `Runtime.WorkstationGC.lib` | Garbage collector | Contains PAL calls - we must provide |
| `System.IO.Compression.Native.Aot.lib` | zlib compression | Can likely stub or exclude |
| `System.Globalization.Native.Aot.lib` | ICU globalization | Use `--no-globalization` to exclude |
| Bootstrap object files | Runtime init | Contains startup code |

**Key Insight:** The GC library (`Runtime.WorkstationGC.lib`) contains calls to Windows PAL
functions. When we provide implementations for those functions, the GC should work.

For UEFI with `--no-globalization` and `--no-stacktrace-data`, the main dependencies are:
1. The GC runtime (which needs our PAL)
2. Bootstrap/startup code
3. Base type system

---

## Legend

- [x] Complete and tested
- [~] Partial implementation (see notes)
- [ ] Not implemented
- **CRITICAL** - Required for runtime initialization
- **GC** - Required for garbage collector
- **THREAD** - Required for threading
- **OPT** - Optional, can stub

---

## 1. Memory Management (CRITICAL)

### Virtual Memory
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| VirtualAlloc | [x] | Memory.cs | GC heap, code allocation |
| VirtualFree | [x] | Memory.cs | Memory release |
| VirtualProtect | [x] | Memory.cs | Make code executable (W^X) |
| VirtualQuery | [x] | Memory.cs | Stack bounds detection |

### Heap Management
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetProcessHeap | [x] | Memory.cs | Default heap access |
| HeapCreate | [x] | Memory.cs | Private heaps |
| HeapDestroy | [x] | Memory.cs | Heap cleanup |
| HeapAlloc | [x] | Memory.cs | Small allocations |
| HeapFree | [x] | Memory.cs | Deallocation |
| HeapReAlloc | [x] | Memory.cs | Resize allocations |
| HeapSize | [x] | Memory.cs | Query allocation size |

### Cache Control
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| FlushInstructionCache | [x] | Thread.cs | JIT code generation |

**Status: COMPLETE**

---

## 2. Threading (CRITICAL for GC)

### Thread Lifecycle
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| CreateThread | [x] | Thread.cs | Create managed threads |
| ExitThread | [x] | Thread.cs | Thread termination |
| TerminateThread | [~] | Thread.cs | Stub (dangerous API) |
| GetCurrentThreadId | [x] | Thread.cs | Thread identification |
| GetCurrentThread | [x] | Thread.cs | Pseudo-handle |
| CloseHandle (thread) | [x] | Thread.cs | Handle cleanup |

### Thread Control (GC CRITICAL)
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| SuspendThread | [x] | Thread.cs | **GC thread suspension** |
| ResumeThread | [x] | Thread.cs | **GC thread resume** |
| SwitchToThread | [x] | Thread.cs | Yield CPU |
| Sleep | [x] | Thread.cs | Timed wait |
| SleepEx | [x] | Thread.cs | Alertable sleep |

### Thread Priority
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetThreadPriority | [x] | Thread.cs | Priority query |
| SetThreadPriority | [x] | Thread.cs | Priority adjustment |

### Thread Context (CRITICAL for SEH/GC)
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetThreadContext | [x] | Thread.cs | **GC stack scanning** |
| SetThreadContext | [x] | Thread.cs | Exception handling |
| RtlCaptureContext | [x] | Thread.cs | Context capture |
| RtlRestoreContext | [x] | Thread.cs | Context restore |

### Thread Description (OPT)
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| SetThreadDescription | [x] | Thread.cs | Debug thread names (optional) |

**Status: COMPLETE**

---

## 3. Fiber/Thread Local Storage (CRITICAL)

NativeAOT uses Fiber Local Storage (FLS) on Windows. Our TLS implementation serves the same purpose.

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| FlsAlloc | [x] | Tls.cs | Per-thread data (via TlsAlloc) |
| FlsGetValue | [x] | Tls.cs | Get thread-local (via TlsGetValue) |
| FlsSetValue | [x] | Tls.cs | Set thread-local (via TlsSetValue) |
| FlsFree | [x] | Tls.cs | Free slot (via TlsFree) |

**Note:** We map FLS to TLS since we don't support fibers. This is fine for kernel use.

**Status: COMPLETE**

---

## 4. Synchronization (CRITICAL for GC)

### Events
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| CreateEventW | [x] | Sync.cs | GC signaling |
| CreateEventExW | [x] | Sync.cs | Extended flags |
| SetEvent | [x] | Sync.cs | Signal event |
| ResetEvent | [x] | Sync.cs | Clear signal |
| CloseHandle (event) | [x] | Sync.cs | Cleanup |

### Wait Functions (GC CRITICAL)
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| WaitForSingleObject | [x] | Sync.cs | Basic wait |
| WaitForSingleObjectEx | [x] | Sync.cs | **Alertable wait for GC** |
| WaitForMultipleObjects | [x] | Sync.cs | Multi-wait |
| WaitForMultipleObjectsEx | [x] | Sync.cs | **Alertable multi-wait** |

### Mutexes and Semaphores
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| CreateMutexW | [x] | Sync.cs | Mutex creation |
| CreateMutexExW | [x] | Sync.cs | Extended flags |
| ReleaseMutex | [x] | Sync.cs | Unlock |
| CreateSemaphoreW | [x] | Sync.cs | Semaphore creation |
| CreateSemaphoreExW | [x] | Sync.cs | Extended flags |
| ReleaseSemaphore | [x] | Sync.cs | Increment count |

### Critical Sections
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| InitializeCriticalSection | [x] | CriticalSection.cs | Fast mutex |
| InitializeCriticalSectionEx | [x] | CriticalSection.cs | With spin count |
| EnterCriticalSection | [x] | CriticalSection.cs | Lock |
| TryEnterCriticalSection | [x] | CriticalSection.cs | Try lock |
| LeaveCriticalSection | [x] | CriticalSection.cs | Unlock |
| DeleteCriticalSection | [x] | CriticalSection.cs | Cleanup |

### SRW Locks
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| InitializeSRWLock | [x] | SRWLock.cs | Reader-writer lock |
| AcquireSRWLockShared | [x] | SRWLock.cs | Reader lock |
| AcquireSRWLockExclusive | [x] | SRWLock.cs | Writer lock |
| TryAcquireSRWLockShared | [x] | SRWLock.cs | Try reader |
| TryAcquireSRWLockExclusive | [x] | SRWLock.cs | Try writer |
| ReleaseSRWLockShared | [x] | SRWLock.cs | Release reader |
| ReleaseSRWLockExclusive | [x] | SRWLock.cs | Release writer |

### Condition Variables
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| InitializeConditionVariable | [x] | ConditionVariable.cs | CV init |
| SleepConditionVariableCS | [x] | ConditionVariable.cs | Wait with CS |
| SleepConditionVariableSRW | [x] | ConditionVariable.cs | Wait with SRW |
| WakeConditionVariable | [x] | ConditionVariable.cs | Wake one |
| WakeAllConditionVariable | [x] | ConditionVariable.cs | Wake all |

### COM Wait (OPT - can stub)
| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| CoWaitForMultipleHandles | [x] | Com.cs | COM interop (stub ok) |
| CoInitializeEx | [x] | Com.cs | COM init (stub ok) |

**Status: COMPLETE**

---

## 5. Async Procedure Calls (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| QueueUserAPC | [x] | Thread.cs | Standard APC |
| QueueUserAPC2 | [x] | Thread.cs | Enhanced APC (Windows 10+) |

**Status: COMPLETE**

---

## 6. Process Information (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetCurrentProcess | [x] | System.cs | Process pseudo-handle |
| GetCurrentProcessId | [x] | System.cs | Process ID |
| GetProcessAffinityMask | [x] | System.cs | CPU affinity |
| QueryInformationJobObject | [x] | System.cs | Job objects (stub ok) |

**Status: COMPLETE**

---

## 7. System Information (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetSystemInfo | [x] | System.cs | Page size, CPU count |
| GetNativeSystemInfo | [x] | System.cs | Same on 64-bit |
| IsWow64Process2 | [x] | System.cs | 32/64-bit detection |
| IsWindowsVersionOrGreater | [x] | System.cs | Version checks |

**Status: COMPLETE**

---

## 8. Environment Variables (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetEnvironmentVariableW | [x] | Environment.cs | Config/tuning |
| SetEnvironmentVariableW | [x] | Environment.cs | Set variables |
| GetEnvironmentStringsW | [x] | Environment.cs | Get all |
| FreeEnvironmentStringsW | [x] | Environment.cs | Free block |
| ExpandEnvironmentStringsW | [~] | Environment.cs | Stub (no expansion) |

**Status: COMPLETE**

---

## 9. String Conversion (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| MultiByteToWideChar | [x] | String.cs | UTF-8 to UTF-16 |
| WideCharToMultiByte | [x] | String.cs | UTF-16 to UTF-8 |
| GetACP | [x] | String.cs | Active code page |
| GetCPInfo | [x] | String.cs | Code page info |

**Status: COMPLETE**

---

## 10. Console/Debug Output (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetStdHandle | [x] | Console.cs | Console handles |
| WriteFile | [x] | Console.cs | Console output |
| OutputDebugStringA | [x] | System.cs | Debug output |
| OutputDebugStringW | [x] | System.cs | Debug output (wide) |
| IsDebuggerPresent | [x] | System.cs | Debugger check |
| DebugBreak | [x] | System.cs | Break into debugger |

**Status: COMPLETE**

---

## 11. Exception Handling (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| SetUnhandledExceptionFilter | [x] | Exception.cs | Global filter |
| RaiseException | [x] | Exception.cs | Throw exceptions |
| RtlCaptureStackBackTrace | [x] | Exception.cs | Stack traces (via VirtualUnwind) |

**Status: COMPLETE**

---

## 12. Stack Unwinding (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| RtlLookupFunctionEntry | [x] | Exception.cs | Find unwind info |
| RtlVirtualUnwind | [x] | Exception.cs | Unwind one frame |
| RtlAddFunctionTable | [x] | Exception.cs | Register JIT code |
| RtlDeleteFunctionTable | [x] | Exception.cs | Unregister |
| PAL_VirtualUnwind | [x] | Exception.cs | PAL-style unwind |

**Status: COMPLETE**

---

## 13. Interlocked Operations (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| InterlockedIncrement | [x] | Interlocked.cs | Atomic increment |
| InterlockedDecrement | [x] | Interlocked.cs | Atomic decrement |
| InterlockedExchange | [x] | Interlocked.cs | Atomic swap |
| InterlockedCompareExchange | [x] | Interlocked.cs | CAS |
| InterlockedExchangeAdd | [x] | Interlocked.cs | Atomic add |
| InterlockedIncrement64 | [x] | Interlocked.cs | 64-bit versions |
| InterlockedDecrement64 | [x] | Interlocked.cs | 64-bit versions |
| InterlockedExchange64 | [x] | Interlocked.cs | 64-bit versions |
| InterlockedCompareExchange64 | [x] | Interlocked.cs | 64-bit versions |
| InterlockedExchangeAdd64 | [x] | Interlocked.cs | 64-bit versions |
| InterlockedExchangePointer | [x] | Interlocked.cs | Pointer swap |
| InterlockedCompareExchangePointer | [x] | Interlocked.cs | Pointer CAS |
| MemoryBarrier | [x] | Interlocked.cs | Full fence |

**Status: COMPLETE**

---

## 14. Module Loading (FUTURE - for dynamic modules)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| LoadLibraryExW | [ ] | - | Load DLL |
| GetModuleHandleW | [ ] | - | Get module handle |
| GetModuleHandleExW | [ ] | - | Extended flags |
| GetProcAddress | [ ] | - | Get function address |
| FreeLibrary | [ ] | - | Unload module |
| GetModuleFileNameW | [ ] | - | Module path |

**Note:** These are only needed for dynamic module loading. For static linking
they can be stubbed initially.

**Status: NOT NEEDED FOR INITIAL STDLIB**

---

## 15. Extended CPU State (XState) - COMPLETE (SSE-only)

For SSE/AVX register preservation during context switches.

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| GetEnabledXStateFeatures | [x] | XState.cs | Query XSAVE features |
| InitializeContext | [x] | XState.cs | Create CONTEXT struct |
| InitializeContext2 | [x] | XState.cs | With compaction mask |
| SetXStateFeaturesMask | [x] | XState.cs | Set features in context |
| LocateXStateFeature | [x] | XState.cs | Find feature in context |

**Note:** Currently returns SSE-only support (no AVX). Can be extended later.

**Status: COMPLETE (SSE-only)**

---

## 16. NativeAOT-Specific PAL Functions (COMPLETE)

These are internal runtime functions, not Win32 APIs:

| Function | Status | Notes |
|----------|--------|-------|
| PalGetModuleBounds | [x] | NativeAotPal.cs - Get module VA range |
| PalGetMaximumStackBounds | [x] | NativeAotPal.cs - Get thread stack range |
| PalGetModuleFileName | [x] | NativeAotPal.cs - Get module path |
| PalGetProcessCpuCount | [x] | NativeAotPal.cs - CPU count via GetSystemInfo |
| PalGetPDBInfo | [x] | NativeAotPal.cs - Stub (no PDBs) |

**Status: COMPLETE**

---

## 17. Time Functions (COMPLETE)

| API | Status | ProtonOS File | NativeAOT Usage |
|-----|--------|------------|-----------------|
| QueryPerformanceCounter | [x] | System.cs | High-res timing |
| QueryPerformanceFrequency | [x] | System.cs | Timer frequency |
| GetTickCount | [x] | System.cs | Uptime (32-bit) |
| GetTickCount64 | [x] | System.cs | Uptime (64-bit) |
| GetSystemTimeAsFileTime | [x] | System.cs | Wall clock |
| GetSystemTime | [x] | System.cs | Broken-down time |

**Status: COMPLETE**

---

## Summary

| Category | Complete | Partial | Missing | Priority |
|----------|----------|---------|---------|----------|
| Memory Management | 9 | 0 | 0 | COMPLETE |
| Threading | 17 | 0 | 0 | COMPLETE |
| Fiber/TLS | 4 | 0 | 0 | COMPLETE |
| Synchronization | 29 | 0 | 0 | COMPLETE |
| APC | 2 | 0 | 0 | COMPLETE |
| Process Info | 4 | 0 | 0 | COMPLETE |
| System Info | 4 | 0 | 0 | COMPLETE |
| Environment | 4 | 1 | 0 | COMPLETE |
| String Conversion | 4 | 0 | 0 | COMPLETE |
| Console/Debug | 6 | 0 | 0 | COMPLETE |
| Exception Handling | 3 | 0 | 0 | COMPLETE |
| Stack Unwinding | 5 | 0 | 0 | COMPLETE |
| Interlocked | 13 | 0 | 0 | COMPLETE |
| Module Loading | 0 | 0 | 6 | FUTURE |
| XState | 5 | 0 | 0 | COMPLETE |
| NativeAOT PAL | 5 | 0 | 0 | COMPLETE |
| Time | 6 | 0 | 0 | COMPLETE |
| **TOTAL** | **120** | **1** | **6** | - |

**Current Coverage: 94% complete, 1% partial, 5% missing (Module Loading only)**

The only missing APIs are Module Loading (LoadLibraryExW, GetModuleHandleW, etc.) which are
not needed for initial stdlib enablement. These will be implemented when JIT/dynamic module
support is added.

---

## Implementation Status for stdlib Enablement

### Phase 1: Critical Stubs ✅ COMPLETE

All critical stubs have been implemented:

1. **Console I/O** ✅
   - `GetStdHandle` - Console.cs
   - `WriteFile` - Console.cs

2. **Process/System Stubs** ✅
   - `GetProcessAffinityMask` - System.cs
   - `QueryInformationJobObject` - System.cs
   - `IsWow64Process2` - System.cs
   - `IsWindowsVersionOrGreater` - System.cs

3. **COM Stubs** ✅
   - `CoWaitForMultipleHandles` - Com.cs
   - `CoInitializeEx` - Com.cs

4. **NativeAOT PAL** ✅
   - `PalGetModuleBounds` - NativeAotPal.cs
   - `PalGetMaximumStackBounds` - NativeAotPal.cs
   - `PalGetModuleFileName` - NativeAotPal.cs
   - `PalGetPDBInfo` - NativeAotPal.cs

### Phase 2: FLS/TLS Mapping ✅ COMPLETE

- FlsAlloc → TlsAlloc (Tls.cs)
- FlsGetValue → TlsGetValue (Tls.cs)
- FlsSetValue → TlsSetValue (Tls.cs)
- FlsFree → TlsFree (Tls.cs)

### Phase 3: XState Support ✅ COMPLETE (SSE-only)

- XState.cs provides SSE-only support
- GetEnabledXStateFeatures, InitializeContext, etc.

### Phase 4: Module Loading (Future)

- Only needed when we want to load dynamic modules
- Can be deferred until JIT integration

---

## Build Configuration Changes

Current Makefile uses:
```makefile
BFLAT_FLAGS := \
    --os:uefi \
    --arch:$(ARCH) \
    --stdlib:zero \          # <- CHANGE THIS
    --no-stacktrace-data \
    --no-globalization \
    --no-reflection \
    --no-exception-messages
```

For full stdlib:
```makefile
BFLAT_FLAGS := \
    --os:uefi \
    --arch:$(ARCH) \
    # Remove --stdlib:zero
    --no-stacktrace-data \   # Keep to reduce size
    --no-globalization \     # Keep - we don't need culture-specific
    --no-reflection \        # Consider removing for System.Reflection.Metadata
    --no-exception-messages  # Keep to reduce size
```

---

## Testing Strategy

1. **Add stubs first** - Implement missing functions as stubs
2. **Remove --stdlib:zero** - Attempt build with full stdlib
3. **Fix linker errors** - Add missing exports one by one
4. **Boot test** - Verify kernel still boots with GC/runtime
5. **Memory test** - Verify GC doesn't corrupt kernel state
6. **Threading test** - Verify GC suspension works correctly

---

## Appendix: Full PalMinWin.cpp API List

For reference, these are ALL Windows APIs called by NativeAOT's Windows PAL:

### Memory
- VirtualAlloc, VirtualFree, VirtualProtect, FlushInstructionCache

### Threading
- GetCurrentThreadId, CreateThread, SuspendThread, ResumeThread
- SetThreadPriority, GetThreadContext, SetThreadContext
- CloseHandle, SwitchToThread

### FLS/TLS
- FlsAlloc, FlsGetValue, FlsSetValue

### Sync
- CreateEventW, SetEvent, ResetEvent
- WaitForMultipleObjectsEx, WaitForSingleObjectEx
- CoWaitForMultipleHandles

### Process
- GetCurrentProcess, GetProcessAffinityMask, GetCurrentProcessId
- QueryInformationJobObject

### Module
- LoadLibraryExW, GetModuleHandleW, GetModuleHandleExW
- GetProcAddress, PalGetModuleFileName

### Context/XState
- GetEnabledXStateFeatures, InitializeContext, InitializeContext2
- RtlRestoreContext, RtlCaptureContext, RtlCaptureStackBackTrace
- SetXStateFeaturesMask, LocateXStateFeature

### APC
- QueueUserAPC2, SetThreadDescription

### Environment/Config
- GetEnvironmentVariableW, CoInitializeEx
- IsWindowsVersionOrGreater, IsWow64Process2

### I/O
- WriteFile, GetStdHandle, MultiByteToWideChar, WideCharToMultiByte
