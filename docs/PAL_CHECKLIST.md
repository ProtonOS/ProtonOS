# PAL Integration Checklist for JIT Support

This document tracks the Platform Abstraction Layer (PAL) APIs required for CoreCLR/RyuJIT integration.
Based on [CoreCLR PAL header](https://github.com/dotnet/coreclr/blob/master/src/pal/inc/pal.h).

## Legend
- [x] Complete and tested
- [~] Partial implementation (see notes)
- [ ] Not implemented

---

## 1. Memory Management (HIGH PRIORITY)

### Virtual Memory
| API | Status | File | Notes |
|-----|--------|------|-------|
| VirtualAlloc | [x] | Memory.cs | Full implementation with protection flags |
| VirtualFree | [x] | Memory.cs | MEM_RELEASE and MEM_DECOMMIT supported |
| VirtualProtect | [x] | Memory.cs | Protection changes work, tested |
| VirtualQuery | [ ] | - | **NEEDED**: Query memory region info (MEMORY_BASIC_INFORMATION) |

### Heap Management
| API | Status | File | Notes |
|-----|--------|------|-------|
| GetProcessHeap | [x] | Memory.cs | Returns default kernel heap |
| HeapCreate | [x] | Memory.cs | Creates private heaps |
| HeapDestroy | [x] | Memory.cs | Frees private heaps |
| HeapAlloc | [x] | Memory.cs | HEAP_ZERO_MEMORY supported |
| HeapFree | [x] | Memory.cs | Works for process heap |
| HeapReAlloc | [~] | Memory.cs | Basic impl, doesn't track old sizes |
| HeapSize | [ ] | Memory.cs | Stub returns 0 - needs allocation tracking |

### Other Memory
| API | Status | File | Notes |
|-----|--------|------|-------|
| GlobalMemoryStatusEx | [ ] | - | Low priority - system memory stats |
| LocalAlloc/LocalFree | [ ] | - | Low priority - can use HeapAlloc |

---

## 2. Threading (HIGH PRIORITY)

### Thread Lifecycle
| API | Status | File | Notes |
|-----|--------|------|-------|
| CreateThread | [x] | Thread.cs | Full implementation |
| ExitThread | [x] | Thread.cs | Terminates current thread |
| TerminateThread | [~] | Thread.cs | Stub returns false - dangerous API |
| GetCurrentThreadId | [x] | Thread.cs | Works |
| GetCurrentThread | [x] | Thread.cs | Returns actual thread ptr (not pseudo-handle) |
| CloseHandle (thread) | [x] | Thread.cs | No-op in our impl |

### Thread Control
| API | Status | File | Notes |
|-----|--------|------|-------|
| Sleep | [x] | Thread.cs | Uses scheduler Sleep() |
| SleepEx | [ ] | - | Alertable sleep - needed for APC |
| SwitchToThread | [x] | Thread.cs | Yields to scheduler |
| SuspendThread | [~] | Thread.cs | **STUB** - returns -1, needs impl |
| ResumeThread | [~] | Thread.cs | **STUB** - returns -1, needs impl |

### Thread Priority
| API | Status | File | Notes |
|-----|--------|------|-------|
| GetThreadPriority | [x] | Thread.cs | Maps to kernel priority |
| SetThreadPriority | [x] | Thread.cs | Maps Win32 priorities |

### Thread Context (CRITICAL FOR SEH)
| API | Status | File | Notes |
|-----|--------|------|-------|
| GetThreadContext | [ ] | - | **CRITICAL**: Read registers for stack walking |
| SetThreadContext | [ ] | - | **CRITICAL**: Modify registers for exception handling |

### Thread Info
| API | Status | File | Notes |
|-----|--------|------|-------|
| GetExitCodeThread | [x] | Thread.cs | Returns exit code or STILL_ACTIVE |
| GetThreadTimes | [ ] | - | Low priority - timing info |
| QueryThreadCycleTime | [ ] | - | Low priority - CPU cycles |
| SetThreadDescription | [ ] | - | Low priority - debug names |

### Async Procedure Calls
| API | Status | File | Notes |
|-----|--------|------|-------|
| QueueUserAPC | [ ] | - | Needed for async operations |

---

## 3. Synchronization (HIGH PRIORITY)

### Critical Sections
| API | Status | File | Notes |
|-----|--------|------|-------|
| InitializeCriticalSection | [x] | CriticalSection.cs | With spin count option |
| InitializeCriticalSectionEx | [x] | CriticalSection.cs | Same as above |
| EnterCriticalSection | [x] | CriticalSection.cs | Blocking acquire |
| TryEnterCriticalSection | [x] | CriticalSection.cs | Non-blocking attempt |
| LeaveCriticalSection | [x] | CriticalSection.cs | Release lock |
| DeleteCriticalSection | [x] | CriticalSection.cs | Cleanup |

### SRW Locks
| API | Status | File | Notes |
|-----|--------|------|-------|
| InitializeSRWLock | [x] | SRWLock.cs | Reader-writer lock |
| AcquireSRWLockShared | [x] | SRWLock.cs | Reader lock |
| AcquireSRWLockExclusive | [x] | SRWLock.cs | Writer lock |
| ReleaseSRWLockShared | [x] | SRWLock.cs | Release reader |
| ReleaseSRWLockExclusive | [x] | SRWLock.cs | Release writer |
| TryAcquireSRWLockShared | [x] | SRWLock.cs | Non-blocking reader |
| TryAcquireSRWLockExclusive | [x] | SRWLock.cs | Non-blocking writer |

### Condition Variables
| API | Status | File | Notes |
|-----|--------|------|-------|
| InitializeConditionVariable | [x] | ConditionVariable.cs | Works |
| SleepConditionVariableCS | [x] | ConditionVariable.cs | Wait on CV with CS |
| SleepConditionVariableSRW | [x] | ConditionVariable.cs | Wait on CV with SRW |
| WakeConditionVariable | [x] | ConditionVariable.cs | Wake one |
| WakeAllConditionVariable | [x] | ConditionVariable.cs | Wake all |

### Events
| API | Status | File | Notes |
|-----|--------|------|-------|
| CreateEvent | [x] | Sync.cs | Auto/manual reset events |
| CreateEventEx | [ ] | - | Extended version with security |
| OpenEvent | [ ] | - | Open existing named event |
| SetEvent | [x] | Sync.cs | Signal event |
| ResetEvent | [x] | Sync.cs | Clear signal |
| PulseEvent | [ ] | - | Low priority - deprecated in Windows |

### Mutexes
| API | Status | File | Notes |
|-----|--------|------|-------|
| CreateMutex | [x] | Sync.cs | Recursive mutex |
| CreateMutexEx | [ ] | - | Extended version |
| OpenMutex | [ ] | - | Open named mutex |
| ReleaseMutex | [x] | Sync.cs | Release ownership |

### Semaphores
| API | Status | File | Notes |
|-----|--------|------|-------|
| CreateSemaphore | [x] | Sync.cs | Counting semaphore |
| CreateSemaphoreEx | [ ] | - | Extended version |
| OpenSemaphore | [ ] | - | Open named semaphore |
| ReleaseSemaphore | [x] | Sync.cs | Increment count |

### Wait Functions
| API | Status | File | Notes |
|-----|--------|------|-------|
| WaitForSingleObject | [x] | Sync.cs | Event, Mutex, Semaphore, Thread |
| WaitForSingleObjectEx | [ ] | - | Alertable wait |
| WaitForMultipleObjects | [ ] | - | **IMPORTANT**: Wait on multiple handles |
| WaitForMultipleObjectsEx | [ ] | - | Alertable multi-wait |
| SignalObjectAndWait | [ ] | - | Atomic signal + wait |

---

## 4. Time & Performance (COMPLETE)

| API | Status | File | Notes |
|-----|--------|------|-------|
| QueryPerformanceCounter | [x] | System.cs | Uses HPET |
| QueryPerformanceFrequency | [x] | System.cs | Returns HPET frequency (~100MHz) |
| GetTickCount | [x] | System.cs | Milliseconds since boot (32-bit) |
| GetTickCount64 | [x] | System.cs | Milliseconds since boot (64-bit) |
| GetSystemTimeAsFileTime | [ ] | - | Current time as FILETIME |
| GetSystemTime | [ ] | - | Current time as SYSTEMTIME |

---

## 5. System Information (MOSTLY COMPLETE)

| API | Status | File | Notes |
|-----|--------|------|-------|
| GetSystemInfo | [x] | System.cs | Processor, page size, address range |
| GetNativeSystemInfo | [x] | System.cs | Same as GetSystemInfo on x64 |
| FlushInstructionCache | [x] | Thread.cs | Memory barrier for JIT |

---

## 6. Exception Handling (PARTIAL)

| API | Status | File | Notes |
|-----|--------|------|-------|
| SetUnhandledExceptionFilter | [x] | Exception.cs | Global exception filter |
| RaiseException | [ ] | - | **NEEDED**: Throw Win32 exceptions |
| RtlCaptureContext | [ ] | - | Capture current CPU context |
| RtlRestoreContext | [ ] | - | Restore CPU context |

---

## 7. Interlocked Operations (COMPLETE)

### 32-bit Operations
| API | Status | File | Notes |
|-----|--------|------|-------|
| InterlockedIncrement | [x] | Interlocked.cs | Returns new value |
| InterlockedDecrement | [x] | Interlocked.cs | Returns new value |
| InterlockedExchange | [x] | Interlocked.cs | Atomic swap |
| InterlockedCompareExchange | [x] | Interlocked.cs | CAS |
| InterlockedExchangeAdd | [x] | Interlocked.cs | Atomic add |

### 64-bit Operations
| API | Status | File | Notes |
|-----|--------|------|-------|
| InterlockedIncrement64 | [x] | Interlocked.cs | 64-bit increment |
| InterlockedDecrement64 | [x] | Interlocked.cs | 64-bit decrement |
| InterlockedExchange64 | [x] | Interlocked.cs | 64-bit swap |
| InterlockedCompareExchange64 | [x] | Interlocked.cs | 64-bit CAS |
| InterlockedExchangeAdd64 | [x] | Interlocked.cs | 64-bit add |

### Pointer Operations
| API | Status | File | Notes |
|-----|--------|------|-------|
| InterlockedExchangePointer | [x] | Interlocked.cs | Pointer swap |
| InterlockedCompareExchangePointer | [x] | Interlocked.cs | Pointer CAS |

### Memory Barriers
| API | Status | File | Notes |
|-----|--------|------|-------|
| MemoryBarrier | [x] | Interlocked.cs | Full fence (mfence) |

---

## 8. Thread Local Storage (COMPLETE)

| API | Status | File | Notes |
|-----|--------|------|-------|
| TlsAlloc | [x] | Tls.cs | Allocate TLS slot |
| TlsFree | [x] | Tls.cs | Free TLS slot |
| TlsGetValue | [x] | Tls.cs | Get per-thread value |
| TlsSetValue | [x] | Tls.cs | Set per-thread value |

---

## 9. Debug Support (NOT STARTED)

| API | Status | File | Notes |
|-----|--------|------|-------|
| OutputDebugStringA | [ ] | - | **USEFUL**: Debug output |
| OutputDebugStringW | [ ] | - | Wide char version |
| IsDebuggerPresent | [ ] | - | **USEFUL**: Check for debugger |
| DebugBreak | [x] | Cpu.cs | INT3 instruction |

---

## 10. Environment (NOT STARTED)

| API | Status | File | Notes |
|-----|--------|------|-------|
| GetEnvironmentVariableW | [ ] | - | Read env var |
| SetEnvironmentVariableW | [ ] | - | Write env var |
| GetEnvironmentStringsW | [ ] | - | Get all env vars |
| FreeEnvironmentStringsW | [ ] | - | Free env block |

---

## 11. Handle Management (PARTIAL)

| API | Status | File | Notes |
|-----|--------|------|-------|
| CloseHandle | [~] | Sync.cs, Thread.cs | Works for events, no-op for threads |
| DuplicateHandle | [ ] | - | Low priority |

---

## 12. File/IO (LOW PRIORITY FOR JIT)

| API | Status | File | Notes |
|-----|--------|------|-------|
| CreateFileW | [ ] | - | Not needed for basic JIT |
| ReadFile | [ ] | - | Not needed for basic JIT |
| WriteFile | [ ] | - | Not needed for basic JIT |
| ... | [ ] | - | Entire file subsystem deferred |

---

## 13. Process Management (LOW PRIORITY FOR JIT)

| API | Status | File | Notes |
|-----|--------|------|-------|
| GetCurrentProcessId | [ ] | - | Easy to add |
| GetCurrentProcess | [ ] | - | Easy to add |
| ... | [ ] | - | Full process API deferred |

---

## Priority Implementation Order

### Phase 1 - Critical for JIT (Do First)
1. [ ] GetThreadContext / SetThreadContext - Required for SEH stack walking
2. [ ] VirtualQuery - Memory region queries
3. [ ] RaiseException - Throw runtime exceptions
4. [ ] WaitForMultipleObjects - Common sync pattern

### Phase 2 - Important for Runtime
5. [ ] SuspendThread / ResumeThread - Thread control
6. [ ] GetEnvironmentVariableW - Config/tuning
7. [ ] OutputDebugString / IsDebuggerPresent - Debug support
8. [ ] GetSystemTimeAsFileTime - Timestamps

### Phase 3 - Nice to Have
9. [ ] QueueUserAPC - Async operations
10. [ ] SleepEx - Alertable sleep
11. [ ] Extended sync APIs (CreateEventEx, etc.)
12. [ ] HeapSize - Allocation tracking

### Phase 4 - Future (After JIT Works)
13. [ ] File I/O subsystem
14. [ ] Process management
15. [ ] Named objects (OpenEvent, OpenMutex, etc.)

---

## Summary

| Category | Complete | Partial | Missing | Total |
|----------|----------|---------|---------|-------|
| Memory | 6 | 1 | 3 | 10 |
| Threading | 8 | 3 | 6 | 17 |
| Synchronization | 22 | 0 | 9 | 31 |
| Time/Performance | 4 | 0 | 2 | 6 |
| System Info | 3 | 0 | 0 | 3 |
| Exception Handling | 1 | 0 | 3 | 4 |
| Interlocked | 13 | 0 | 0 | 13 |
| TLS | 4 | 0 | 0 | 4 |
| Debug | 1 | 0 | 3 | 4 |
| Environment | 0 | 0 | 4 | 4 |
| **TOTAL** | **62** | **4** | **30** | **96** |

**Coverage: 65% complete, 4% partial, 31% missing**

The missing APIs are mostly in categories that aren't critical for initial JIT integration (file I/O, process management). The key gaps are GetThreadContext/SetThreadContext which are essential for proper exception handling and stack walking.
