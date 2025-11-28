// netos mernel - PAL Synchronization Primitives
// Win32-style synchronization objects for PAL compatibility.
// Supports: Events (auto/manual reset), Mutexes, Semaphores
// All objects support WaitForSingleObject/WaitForMultipleObjects patterns.

using System.Runtime.InteropServices;
using Kernel.Threading;
using Kernel.Memory;
using Kernel.X64;

namespace Kernel.PAL;

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
        if (obj == null)
            return WaitResult.Failed;

        var header = (ObjectHeader*)obj;

        _lock.Acquire();

        // Check if object is already signaled
        if (TryAcquireObject(header))
        {
            _lock.Release();
            return WaitResult.Object0;
        }

        // If timeout is 0, return immediately
        if (timeoutMs == 0)
        {
            _lock.Release();
            return WaitResult.Timeout;
        }

        // Block current thread
        var current = Scheduler.CurrentThread;
        if (current == null)
        {
            _lock.Release();
            return WaitResult.Failed;
        }

        // Set up wait
        current->WaitObject = obj;
        current->WaitResult = WaitResult.Timeout;

        // Calculate wake time for timeout
        if (timeoutMs != 0xFFFFFFFF)
        {
            current->WakeTime = Apic.TickCount + (timeoutMs / 10);  // 10ms per tick
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

        // Remove from wait list if still there (timeout case)
        RemoveFromWaitList(header, current);
        current->WaitObject = null;

        var result = current->WaitResult;

        _lock.Release();

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
}
