// ProtonOS kernel - PAL Synchronization Primitives
// Win32-style synchronization objects for PAL compatibility.
// Supports: Events (auto/manual reset), Mutexes, Semaphores
// All objects support WaitForSingleObject/WaitForMultipleObjects patterns.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.PAL;

/// <summary>
/// Type of waitable PAL object
/// </summary>
public enum ObjectType : uint
{
    Event = 0,
    Mutex = 1,
    Semaphore = 2,
    Thread = 3,
}

/// <summary>
/// Base header for all waitable objects
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ObjectHeader
{
    public ObjectType Type;
    public uint RefCount;
    public Thread* WaitListHead;  // Threads waiting on this object
    public Thread* WaitListTail;
}

/// <summary>
/// PAL Event - signalable synchronization object.
/// Supports both auto-reset and manual-reset modes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Event
{
    public ObjectHeader Header;
    public bool Signaled;           // Current signal state
    public bool ManualReset;        // If true, stays signaled until ResetEvent
}

/// <summary>
/// PAL Mutex - mutual exclusion lock with ownership tracking.
/// Supports recursive acquisition by the same thread.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Mutex
{
    public ObjectHeader Header;
    public Thread* Owner;     // Thread that owns the mutex
    public uint RecursionCount;     // For recursive acquisition
}

/// <summary>
/// PAL Semaphore - counting synchronization object.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct Semaphore
{
    public ObjectHeader Header;
    public int Count;               // Current count
    public int MaxCount;            // Maximum count
}

/// <summary>
/// Static class for PAL synchronization operations.
/// Provides Win32-style CreateEvent, SetEvent, WaitForSingleObject, etc.
/// </summary>
public static unsafe class Sync
{
    private static SpinLock _lock;

    /// <summary>
    /// Create an event object.
    /// </summary>
    /// <param name="manualReset">If true, event stays signaled until Reset is called</param>
    /// <param name="initialState">If true, event starts signaled</param>
    /// <returns>Event pointer, or null on failure</returns>
    public static Event* CreateEvent(bool manualReset, bool initialState)
    {
        var evt = (Event*)HeapAllocator.AllocZeroed((ulong)sizeof(Event));
        if (evt == null)
            return null;

        evt->Header.Type = ObjectType.Event;
        evt->Header.RefCount = 1;
        evt->Header.WaitListHead = null;
        evt->Header.WaitListTail = null;
        evt->ManualReset = manualReset;
        evt->Signaled = initialState;

        return evt;
    }

    /// <summary>
    /// Event creation flags for CreateEventEx
    /// </summary>
    public static class EventFlags
    {
        public const uint CREATE_EVENT_INITIAL_SET = 0x00000002;
        public const uint CREATE_EVENT_MANUAL_RESET = 0x00000001;
    }

    /// <summary>
    /// Create an event object with extended options.
    /// Named objects are not supported - lpName must be null.
    /// </summary>
    /// <param name="lpEventAttributes">Security attributes (ignored)</param>
    /// <param name="lpName">Event name - must be null (named objects not supported)</param>
    /// <param name="dwFlags">CREATE_EVENT_MANUAL_RESET (0x1) and/or CREATE_EVENT_INITIAL_SET (0x2)</param>
    /// <param name="dwDesiredAccess">Desired access (ignored)</param>
    /// <returns>Event pointer, or null on failure or if lpName is not null</returns>
    public static Event* CreateEventEx(void* lpEventAttributes, char* lpName, uint dwFlags, uint dwDesiredAccess)
    {
        // Named objects not supported yet - fail if name provided
        if (lpName != null)
            return null;

        bool manualReset = (dwFlags & EventFlags.CREATE_EVENT_MANUAL_RESET) != 0;
        bool initialState = (dwFlags & EventFlags.CREATE_EVENT_INITIAL_SET) != 0;
        return CreateEvent(manualReset, initialState);
    }

    /// <summary>
    /// Set an event to signaled state.
    /// Wakes one (auto-reset) or all (manual-reset) waiting threads.
    /// </summary>
    public static bool SetEvent(Event* evt)
    {
        if (evt == null || evt->Header.Type != ObjectType.Event)
            return false;

        _lock.Acquire();

        evt->Signaled = true;

        if (evt->ManualReset)
        {
            // Wake all waiting threads
            WakeAllWaiters(&evt->Header);
        }
        else
        {
            // Wake one waiting thread (if any)
            if (WakeOneWaiter(&evt->Header))
            {
                // Auto-reset: clear signal since a thread consumed it
                evt->Signaled = false;
            }
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Reset an event to non-signaled state.
    /// </summary>
    public static bool ResetEvent(Event* evt)
    {
        if (evt == null || evt->Header.Type != ObjectType.Event)
            return false;

        _lock.Acquire();
        evt->Signaled = false;
        _lock.Release();
        return true;
    }

    /// <summary>
    /// Create a mutex object.
    /// </summary>
    /// <param name="initialOwner">If true, creating thread owns the mutex</param>
    /// <returns>Mutex pointer, or null on failure</returns>
    public static Mutex* CreateMutex(bool initialOwner)
    {
        var mutex = (Mutex*)HeapAllocator.AllocZeroed((ulong)sizeof(Mutex));
        if (mutex == null)
            return null;

        mutex->Header.Type = ObjectType.Mutex;
        mutex->Header.RefCount = 1;
        mutex->Header.WaitListHead = null;
        mutex->Header.WaitListTail = null;
        mutex->Owner = initialOwner ? Scheduler.CurrentThread : null;
        mutex->RecursionCount = initialOwner ? 1u : 0u;

        return mutex;
    }

    /// <summary>
    /// Mutex creation flags for CreateMutexEx
    /// </summary>
    public static class MutexFlags
    {
        public const uint CREATE_MUTEX_INITIAL_OWNER = 0x00000001;
    }

    /// <summary>
    /// Create a mutex object with extended options.
    /// Named objects are not supported - lpName must be null.
    /// </summary>
    /// <param name="lpMutexAttributes">Security attributes (ignored)</param>
    /// <param name="lpName">Mutex name - must be null (named objects not supported)</param>
    /// <param name="dwFlags">CREATE_MUTEX_INITIAL_OWNER (0x1) to own mutex on creation</param>
    /// <param name="dwDesiredAccess">Desired access (ignored)</param>
    /// <returns>Mutex pointer, or null on failure or if lpName is not null</returns>
    public static Mutex* CreateMutexEx(void* lpMutexAttributes, char* lpName, uint dwFlags, uint dwDesiredAccess)
    {
        // Named objects not supported yet - fail if name provided
        if (lpName != null)
            return null;

        bool initialOwner = (dwFlags & MutexFlags.CREATE_MUTEX_INITIAL_OWNER) != 0;
        return CreateMutex(initialOwner);
    }

    /// <summary>
    /// Release a mutex.
    /// </summary>
    public static bool ReleaseMutex(Mutex* mutex)
    {
        if (mutex == null || mutex->Header.Type != ObjectType.Mutex)
            return false;

        _lock.Acquire();

        var current = Scheduler.CurrentThread;
        if (mutex->Owner != current)
        {
            // Not owned by current thread
            _lock.Release();
            return false;
        }

        mutex->RecursionCount--;
        if (mutex->RecursionCount == 0)
        {
            mutex->Owner = null;
            // Wake one waiting thread
            WakeOneWaiter(&mutex->Header);
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Create a semaphore object.
    /// </summary>
    /// <param name="initialCount">Initial count (must be >= 0 and <= maxCount)</param>
    /// <param name="maxCount">Maximum count</param>
    /// <returns>Semaphore pointer, or null on failure</returns>
    public static Semaphore* CreateSemaphore(int initialCount, int maxCount)
    {
        if (initialCount < 0 || maxCount <= 0 || initialCount > maxCount)
            return null;

        var sem = (Semaphore*)HeapAllocator.AllocZeroed((ulong)sizeof(Semaphore));
        if (sem == null)
            return null;

        sem->Header.Type = ObjectType.Semaphore;
        sem->Header.RefCount = 1;
        sem->Header.WaitListHead = null;
        sem->Header.WaitListTail = null;
        sem->Count = initialCount;
        sem->MaxCount = maxCount;

        return sem;
    }

    /// <summary>
    /// Create a semaphore object with extended options.
    /// Named objects are not supported - lpName must be null.
    /// </summary>
    /// <param name="lpSemaphoreAttributes">Security attributes (ignored)</param>
    /// <param name="lInitialCount">Initial count (must be >= 0 and <= lMaximumCount)</param>
    /// <param name="lMaximumCount">Maximum count</param>
    /// <param name="lpName">Semaphore name - must be null (named objects not supported)</param>
    /// <param name="dwFlags">Reserved, must be 0</param>
    /// <param name="dwDesiredAccess">Desired access (ignored)</param>
    /// <returns>Semaphore pointer, or null on failure or if lpName is not null</returns>
    public static Semaphore* CreateSemaphoreEx(
        void* lpSemaphoreAttributes,
        int lInitialCount,
        int lMaximumCount,
        char* lpName,
        uint dwFlags,
        uint dwDesiredAccess)
    {
        // Named objects not supported yet - fail if name provided
        if (lpName != null)
            return null;

        return CreateSemaphore(lInitialCount, lMaximumCount);
    }

    /// <summary>
    /// Release a semaphore (increment count).
    /// </summary>
    /// <param name="sem">Semaphore to release</param>
    /// <param name="releaseCount">Amount to increment (usually 1)</param>
    /// <param name="previousCount">Output: previous count</param>
    /// <returns>True on success</returns>
    public static bool ReleaseSemaphore(Semaphore* sem, int releaseCount, out int previousCount)
    {
        previousCount = 0;

        if (sem == null || sem->Header.Type != ObjectType.Semaphore)
            return false;

        if (releaseCount <= 0)
            return false;

        _lock.Acquire();

        previousCount = sem->Count;

        // Check for overflow
        if (sem->Count + releaseCount > sem->MaxCount)
        {
            _lock.Release();
            return false;
        }

        sem->Count += releaseCount;

        // Wake waiting threads (up to releaseCount)
        for (int i = 0; i < releaseCount; i++)
        {
            if (!WakeOneWaiter(&sem->Header))
                break;
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Wait for a single object.
    /// </summary>
    /// <param name="obj">Object to wait on (Event, Mutex, Semaphore, or Thread)</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0xFFFFFFFF = infinite)</param>
    /// <returns>Wait result code</returns>
    public static uint WaitForSingleObject(void* obj, uint timeoutMs)
    {
        return WaitForSingleObjectEx(obj, timeoutMs, false);
    }

    /// <summary>
    /// Wait for a single object with alertable option.
    /// If alertable is true, the wait can be interrupted by APCs.
    /// </summary>
    /// <param name="obj">Object to wait on (Event, Mutex, Semaphore, or Thread)</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0xFFFFFFFF = infinite)</param>
    /// <param name="alertable">If true, wait can be interrupted by APCs</param>
    /// <returns>Wait result code (WAIT_IO_COMPLETION if woken by APC)</returns>
    public static uint WaitForSingleObjectEx(void* obj, uint timeoutMs, bool alertable)
    {
        if (obj == null)
            return WaitResult.Failed;

        var current = Scheduler.CurrentThread;
        if (current == null)
            return WaitResult.Failed;

        var header = (ObjectHeader*)obj;

        _lock.Acquire();

        // Check if object is already signaled - this takes priority over APC delivery
        if (TryAcquireObject(header))
        {
            _lock.Release();
            return WaitResult.Object0;
        }

        _lock.Release();

        // If alertable and APCs are already pending, deliver them immediately
        // (only if we would need to block)
        if (alertable && Scheduler.HasPendingApc(current))
        {
            Scheduler.DeliverApcs();
            return WaitResult.IoCompletion;
        }

        _lock.Acquire();

        // If timeout is 0, return immediately
        if (timeoutMs == 0)
        {
            _lock.Release();
            return WaitResult.Timeout;
        }

        // Set up wait
        current->WaitObject = obj;
        current->WaitResult = WaitResult.Timeout;
        current->Alertable = alertable;

        // Calculate wake time for timeout
        if (timeoutMs != 0xFFFFFFFF)
        {
            current->WakeTime = APIC.TickCount + timeoutMs;  // 1ms per tick
        }
        else
        {
            current->WakeTime = 0;  // Infinite wait
        }

        // Add to wait list
        AddToWaitList(header, current);

        // Block the thread
        current->State = ThreadState.Blocked;

        _lock.Release();

        // Yield to scheduler
        Scheduler.Schedule();

        // We're back - check result
        _lock.Acquire();

        // Remove from wait list if still there (timeout or APC case)
        RemoveFromWaitList(header, current);
        current->WaitObject = null;
        current->Alertable = false;

        var result = current->WaitResult;
        current->WaitResult = 0;

        _lock.Release();

        // If woken by APC, deliver all pending APCs
        if (result == WaitResult.IoCompletion)
        {
            Scheduler.DeliverApcs();
        }

        return result;
    }

    /// <summary>
    /// Try to acquire an object without blocking.
    /// Caller must hold the lock.
    /// </summary>
    private static bool TryAcquireObject(ObjectHeader* header)
    {
        switch (header->Type)
        {
            case ObjectType.Event:
                var evt = (Event*)header;
                if (evt->Signaled)
                {
                    if (!evt->ManualReset)
                        evt->Signaled = false;  // Auto-reset
                    return true;
                }
                return false;

            case ObjectType.Mutex:
                var mutex = (Mutex*)header;
                var current = Scheduler.CurrentThread;
                if (mutex->Owner == null)
                {
                    mutex->Owner = current;
                    mutex->RecursionCount = 1;
                    return true;
                }
                else if (mutex->Owner == current)
                {
                    // Recursive acquisition
                    mutex->RecursionCount++;
                    return true;
                }
                return false;

            case ObjectType.Semaphore:
                var sem = (Semaphore*)header;
                if (sem->Count > 0)
                {
                    sem->Count--;
                    return true;
                }
                return false;

            case ObjectType.Thread:
                var thread = (Thread*)header;
                return thread->State == ThreadState.Terminated;

            default:
                return false;
        }
    }

    /// <summary>
    /// Add a thread to an object's wait list.
    /// Caller must hold the lock.
    /// </summary>
    private static void AddToWaitList(ObjectHeader* header, Thread* thread)
    {
        thread->Next = null;
        thread->Prev = header->WaitListTail;

        if (header->WaitListTail != null)
            header->WaitListTail->Next = thread;
        else
            header->WaitListHead = thread;

        header->WaitListTail = thread;
    }

    /// <summary>
    /// Remove a thread from an object's wait list.
    /// Caller must hold the lock.
    /// Safe to call even if thread was already removed.
    /// </summary>
    private static void RemoveFromWaitList(ObjectHeader* header, Thread* thread)
    {
        // Check if thread is still in a wait list (has prev/next or is head/tail)
        bool inList = thread->Prev != null || thread->Next != null ||
                      header->WaitListHead == thread || header->WaitListTail == thread;
        if (!inList)
            return;

        if (thread->Prev != null)
            thread->Prev->Next = thread->Next;
        else
            header->WaitListHead = thread->Next;

        if (thread->Next != null)
            thread->Next->Prev = thread->Prev;
        else
            header->WaitListTail = thread->Prev;

        thread->Next = null;
        thread->Prev = null;
    }

    /// <summary>
    /// Wake one thread from an object's wait list.
    /// Caller must hold the lock.
    /// </summary>
    /// <returns>True if a thread was woken</returns>
    private static bool WakeOneWaiter(ObjectHeader* header)
    {
        var thread = header->WaitListHead;
        if (thread == null)
            return false;

        // Remove from wait list
        header->WaitListHead = thread->Next;
        if (header->WaitListHead != null)
            header->WaitListHead->Prev = null;
        else
            header->WaitListTail = null;

        thread->Next = null;
        thread->Prev = null;

        // Wake the thread - set result before making ready
        thread->WaitObject = null;
        thread->WaitResult = WaitResult.Object0;
        thread->WakeTime = 0;

        // Make it ready - MakeReady will change state from Blocked to Ready
        // and add to ready queue
        if (thread->State == ThreadState.Blocked)
        {
            Scheduler.MakeReady(thread);
        }

        return true;
    }

    /// <summary>
    /// Wake all threads from an object's wait list.
    /// Caller must hold the lock.
    /// </summary>
    private static void WakeAllWaiters(ObjectHeader* header)
    {
        while (WakeOneWaiter(header))
        {
            // Keep waking until no more waiters
        }
    }

    /// <summary>
    /// Close a synchronization object.
    /// </summary>
    public static bool CloseHandle(void* obj)
    {
        if (obj == null)
            return false;

        var header = (ObjectHeader*)obj;

        _lock.Acquire();

        header->RefCount--;
        if (header->RefCount == 0)
        {
            // Wake any remaining waiters with failure
            var thread = header->WaitListHead;
            while (thread != null)
            {
                var next = thread->Next;
                thread->WaitObject = null;
                thread->WaitResult = WaitResult.Failed;
                thread->Next = null;
                thread->Prev = null;
                if (thread->State == ThreadState.Blocked)
                {
                    Scheduler.MakeReady(thread);
                }
                thread = next;
            }

            _lock.Release();

            // Free the object
            HeapAllocator.Free(obj);
            return true;
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Wait for multiple objects.
    /// </summary>
    /// <param name="count">Number of objects to wait on</param>
    /// <param name="handles">Array of object pointers</param>
    /// <param name="waitAll">If true, wait for ALL objects; if false, wait for ANY</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0xFFFFFFFF = infinite)</param>
    /// <returns>
    /// If waitAll is false: WAIT_OBJECT_0 + index of signaled object
    /// If waitAll is true: WAIT_OBJECT_0 when all objects are signaled
    /// WAIT_TIMEOUT on timeout, WAIT_FAILED on error
    /// </returns>
    public static uint WaitForMultipleObjects(uint count, void** handles, bool waitAll, uint timeoutMs)
    {
        return WaitForMultipleObjectsEx(count, handles, waitAll, timeoutMs, false);
    }

    /// <summary>
    /// Wait for multiple objects with alertable option.
    /// If alertable is true, the wait can be interrupted by APCs.
    /// </summary>
    /// <param name="count">Number of objects to wait on</param>
    /// <param name="handles">Array of object pointers</param>
    /// <param name="waitAll">If true, wait for ALL objects; if false, wait for ANY</param>
    /// <param name="timeoutMs">Timeout in milliseconds (0xFFFFFFFF = infinite)</param>
    /// <param name="alertable">If true, wait can be interrupted by APCs</param>
    /// <returns>
    /// If waitAll is false: WAIT_OBJECT_0 + index of signaled object
    /// If waitAll is true: WAIT_OBJECT_0 when all objects are signaled
    /// WAIT_IO_COMPLETION if woken by APC
    /// WAIT_TIMEOUT on timeout, WAIT_FAILED on error
    /// </returns>
    public static uint WaitForMultipleObjectsEx(uint count, void** handles, bool waitAll, uint timeoutMs, bool alertable)
    {
        if (count == 0 || count > 64 || handles == null)
            return WaitResult.Failed;

        // Check for null handles
        for (uint i = 0; i < count; i++)
        {
            if (handles[i] == null)
                return WaitResult.Failed;
        }

        var current = Scheduler.CurrentThread;
        if (current == null)
            return WaitResult.Failed;

        _lock.Acquire();

        // Check if objects are already signaled - this takes priority over APC delivery
        if (waitAll)
        {
            // WaitAll: all objects must be signaled
            bool allSignaled = true;
            for (uint i = 0; i < count; i++)
            {
                if (!IsObjectSignaled((ObjectHeader*)handles[i]))
                {
                    allSignaled = false;
                    break;
                }
            }

            if (allSignaled)
            {
                // Acquire all objects
                for (uint i = 0; i < count; i++)
                {
                    TryAcquireObject((ObjectHeader*)handles[i]);
                }
                _lock.Release();
                return WaitResult.Object0;
            }
        }
        else
        {
            // WaitAny: check each object
            for (uint i = 0; i < count; i++)
            {
                if (TryAcquireObject((ObjectHeader*)handles[i]))
                {
                    _lock.Release();
                    return WaitResult.Object0 + i;
                }
            }
        }

        // If timeout is 0, return immediately
        if (timeoutMs == 0)
        {
            _lock.Release();
            return WaitResult.Timeout;
        }

        _lock.Release();

        // If alertable and APCs are already pending, deliver them immediately
        // (only if we would need to block)
        if (alertable && Scheduler.HasPendingApc(current))
        {
            Scheduler.DeliverApcs();
            return WaitResult.IoCompletion;
        }

        _lock.Acquire();

        // Store the first handle as the wait object (for simple tracking)
        current->WaitObject = handles[0];
        current->WaitResult = WaitResult.Timeout;
        current->Alertable = alertable;

        // Calculate wake time for timeout
        if (timeoutMs != 0xFFFFFFFF)
        {
            current->WakeTime = APIC.TickCount + timeoutMs;  // 1ms per tick
        }
        else
        {
            current->WakeTime = 0;
        }

        // For WaitAny, we add to all wait lists
        // When one signals, we'll be woken and can check which one
        // Note: This is a simplified implementation - we only add to the first wait list
        // A full implementation would need to handle being on multiple wait lists
        AddToWaitList((ObjectHeader*)handles[0], current);

        current->State = ThreadState.Blocked;
        _lock.Release();

        // Poll loop for wait-any or wait-all
        // This is a simplified implementation - ideally we'd use proper multi-wait
        while (true)
        {
            Scheduler.Schedule();

            _lock.Acquire();

            // Check if woken by APC
            if (current->WaitResult == WaitResult.IoCompletion)
            {
                RemoveFromWaitList((ObjectHeader*)handles[0], current);
                current->WaitObject = null;
                current->Alertable = false;
                current->WaitResult = 0;
                _lock.Release();
                Scheduler.DeliverApcs();
                return WaitResult.IoCompletion;
            }

            // Check timeout
            bool timedOut = false;
            if (current->WakeTime != 0 && APIC.TickCount >= current->WakeTime)
            {
                timedOut = true;
            }

            if (waitAll)
            {
                // Check if all are signaled
                bool allSignaled = true;
                for (uint i = 0; i < count; i++)
                {
                    if (!IsObjectSignaled((ObjectHeader*)handles[i]))
                    {
                        allSignaled = false;
                        break;
                    }
                }

                if (allSignaled)
                {
                    // Acquire all
                    for (uint i = 0; i < count; i++)
                    {
                        TryAcquireObject((ObjectHeader*)handles[i]);
                    }
                    RemoveFromWaitList((ObjectHeader*)handles[0], current);
                    current->WaitObject = null;
                    current->Alertable = false;
                    _lock.Release();
                    return WaitResult.Object0;
                }
            }
            else
            {
                // Check if any is signaled
                for (uint i = 0; i < count; i++)
                {
                    if (TryAcquireObject((ObjectHeader*)handles[i]))
                    {
                        RemoveFromWaitList((ObjectHeader*)handles[0], current);
                        current->WaitObject = null;
                        current->Alertable = false;
                        _lock.Release();
                        return WaitResult.Object0 + i;
                    }
                }
            }

            if (timedOut)
            {
                RemoveFromWaitList((ObjectHeader*)handles[0], current);
                current->WaitObject = null;
                current->Alertable = false;
                _lock.Release();
                return WaitResult.Timeout;
            }

            // Still waiting - go back to blocked
            current->State = ThreadState.Blocked;
            _lock.Release();
        }
    }

    /// <summary>
    /// Atomically signal one object and wait on another.
    /// This prevents race conditions between signaling and waiting.
    /// </summary>
    /// <param name="hObjectToSignal">Object to signal (Event, Mutex, or Semaphore)</param>
    /// <param name="hObjectToWaitOn">Object to wait on</param>
    /// <param name="dwMilliseconds">Timeout in milliseconds (0xFFFFFFFF = infinite)</param>
    /// <param name="bAlertable">If true, wait can be interrupted by APCs</param>
    /// <returns>Wait result code</returns>
    public static uint SignalObjectAndWait(void* hObjectToSignal, void* hObjectToWaitOn, uint dwMilliseconds, bool bAlertable)
    {
        if (hObjectToSignal == null || hObjectToWaitOn == null)
            return WaitResult.Failed;

        var signalHeader = (ObjectHeader*)hObjectToSignal;
        var waitHeader = (ObjectHeader*)hObjectToWaitOn;

        var current = Scheduler.CurrentThread;
        if (current == null)
            return WaitResult.Failed;

        _lock.Acquire();

        // Signal the object
        if (!SignalObjectLocked(signalHeader))
        {
            _lock.Release();
            return WaitResult.Failed;
        }

        // Check if wait object is already signaled
        if (TryAcquireObject(waitHeader))
        {
            _lock.Release();
            return WaitResult.Object0;
        }

        _lock.Release();

        // If alertable and APCs are already pending, deliver them
        if (bAlertable && Scheduler.HasPendingApc(current))
        {
            Scheduler.DeliverApcs();
            return WaitResult.IoCompletion;
        }

        // If timeout is 0, return immediately
        if (dwMilliseconds == 0)
        {
            return WaitResult.Timeout;
        }

        _lock.Acquire();

        // Set up wait
        current->WaitObject = hObjectToWaitOn;
        current->WaitResult = WaitResult.Timeout;
        current->Alertable = bAlertable;

        // Calculate wake time for timeout
        if (dwMilliseconds != 0xFFFFFFFF)
        {
            current->WakeTime = APIC.TickCount + dwMilliseconds;  // 1ms per tick
        }
        else
        {
            current->WakeTime = 0;
        }

        // Add to wait list
        AddToWaitList(waitHeader, current);

        // Block the thread
        current->State = ThreadState.Blocked;

        _lock.Release();

        // Yield to scheduler
        Scheduler.Schedule();

        // We're back - check result
        _lock.Acquire();

        RemoveFromWaitList(waitHeader, current);
        current->WaitObject = null;
        current->Alertable = false;

        var result = current->WaitResult;
        current->WaitResult = 0;

        _lock.Release();

        // If woken by APC, deliver all pending APCs
        if (result == WaitResult.IoCompletion)
        {
            Scheduler.DeliverApcs();
        }

        return result;
    }

    /// <summary>
    /// Signal an object. Caller must hold the lock.
    /// </summary>
    private static bool SignalObjectLocked(ObjectHeader* header)
    {
        switch (header->Type)
        {
            case ObjectType.Event:
                var evt = (Event*)header;
                evt->Signaled = true;
                if (evt->ManualReset)
                {
                    WakeAllWaiters(&evt->Header);
                }
                else
                {
                    if (WakeOneWaiter(&evt->Header))
                        evt->Signaled = false;
                }
                return true;

            case ObjectType.Mutex:
                var mutex = (Mutex*)header;
                var current = Scheduler.CurrentThread;
                if (mutex->Owner != current)
                    return false;  // Not owned by current thread
                mutex->RecursionCount--;
                if (mutex->RecursionCount == 0)
                {
                    mutex->Owner = null;
                    WakeOneWaiter(&mutex->Header);
                }
                return true;

            case ObjectType.Semaphore:
                var sem = (Semaphore*)header;
                if (sem->Count >= sem->MaxCount)
                    return false;  // Already at max
                sem->Count++;
                WakeOneWaiter(&sem->Header);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Check if an object is signaled without acquiring it.
    /// Caller must hold the lock.
    /// </summary>
    private static bool IsObjectSignaled(ObjectHeader* header)
    {
        switch (header->Type)
        {
            case ObjectType.Event:
                return ((Event*)header)->Signaled;

            case ObjectType.Mutex:
                var mutex = (Mutex*)header;
                var current = Scheduler.CurrentThread;
                return mutex->Owner == null || mutex->Owner == current;

            case ObjectType.Semaphore:
                return ((Semaphore*)header)->Count > 0;

            case ObjectType.Thread:
                return ((Thread*)header)->State == ThreadState.Terminated;

            default:
                return false;
        }
    }
}
