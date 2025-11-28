// netos mernel - Condition Variables
// Win32-style condition variable implementation for PAL compatibility.
// Used with Critical Sections for producer/consumer and wait patterns.

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

/// <summary>
/// Condition Variable - allows threads to atomically release a lock
/// and wait for a condition, then re-acquire the lock.
/// Used with Critical Sections for complex synchronization patterns.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KernelConditionVariable
{
    // Wait list of threads waiting on this condition
    public KernelThread* WaitListHead;
    public KernelThread* WaitListTail;

    // Lock for protecting the wait list
    public KernelSpinLock Lock;
}

/// <summary>
/// Condition Variable management APIs - matching Win32 for PAL compatibility.
/// </summary>
public static unsafe class KernelConditionVariableOps
{
    /// <summary>
    /// Initialize a condition variable.
    /// </summary>
    public static void InitializeConditionVariable(KernelConditionVariable* cv)
    {
        if (cv == null)
            return;

        cv->WaitListHead = null;
        cv->WaitListTail = null;
        cv->Lock = default;
    }

    /// <summary>
    /// Sleep on a condition variable and release the critical section.
    /// When woken, re-acquires the critical section before returning.
    /// </summary>
    /// <param name="cv">Condition variable to wait on</param>
    /// <param name="cs">Critical section to release while waiting</param>
    /// <param name="dwMilliseconds">Timeout in milliseconds (INFINITE = 0xFFFFFFFF)</param>
    /// <returns>True if signaled, false if timed out</returns>
    public static bool SleepConditionVariableCS(
        KernelConditionVariable* cv,
        KernelCriticalSection* cs,
        uint dwMilliseconds)
    {
        if (cv == null || cs == null)
            return false;

        var current = KernelScheduler.CurrentThread;
        if (current == null)
            return false;

        // Add thread to wait list
        cv->Lock.Acquire();

        current->Next = null;
        current->Prev = cv->WaitListTail;

        if (cv->WaitListTail != null)
            cv->WaitListTail->Next = current;
        else
            cv->WaitListHead = current;

        cv->WaitListTail = current;

        // Set up wait timeout
        if (dwMilliseconds != 0xFFFFFFFF)
        {
            current->WakeTime = Apic.TickCount + (dwMilliseconds / 10);  // 10ms per tick
        }
        else
        {
            current->WakeTime = 0;  // Infinite wait
        }

        current->WaitResult = KernelWaitResult.Timeout;  // Default to timeout
        current->State = KernelThreadState.Blocked;

        cv->Lock.Release();

        // Release the critical section
        KernelCriticalSectionOps.LeaveCriticalSection(cs);

        // Yield to scheduler
        KernelScheduler.Schedule();

        // We're back - re-acquire the critical section
        KernelCriticalSectionOps.EnterCriticalSection(cs);

        // Check if we were signaled or timed out
        cv->Lock.Acquire();

        // Remove from wait list if still there (timeout case)
        RemoveFromWaitList(cv, current);

        bool signaled = current->WaitResult == KernelWaitResult.Object0;
        current->WaitResult = 0;
        current->WakeTime = 0;

        cv->Lock.Release();

        return signaled;
    }

    /// <summary>
    /// Sleep on a condition variable and release a slim reader/writer lock.
    /// Note: SRW locks not yet implemented, this is a placeholder.
    /// </summary>
    public static bool SleepConditionVariableSRW(
        KernelConditionVariable* cv,
        void* srwLock,
        uint dwMilliseconds,
        uint flags)
    {
        // TODO: Implement when SRW locks are added
        return false;
    }

    /// <summary>
    /// Wake one thread waiting on a condition variable.
    /// </summary>
    public static void WakeConditionVariable(KernelConditionVariable* cv)
    {
        if (cv == null)
            return;

        cv->Lock.Acquire();

        var thread = cv->WaitListHead;
        if (thread != null)
        {
            // Remove from wait list
            cv->WaitListHead = thread->Next;
            if (cv->WaitListHead != null)
                cv->WaitListHead->Prev = null;
            else
                cv->WaitListTail = null;

            thread->Next = null;
            thread->Prev = null;

            // Signal success
            thread->WaitResult = KernelWaitResult.Object0;
            thread->WakeTime = 0;

            // Make thread ready
            if (thread->State == KernelThreadState.Blocked)
            {
                KernelScheduler.MakeReady(thread);
            }
        }

        cv->Lock.Release();
    }

    /// <summary>
    /// Wake all threads waiting on a condition variable.
    /// </summary>
    public static void WakeAllConditionVariable(KernelConditionVariable* cv)
    {
        if (cv == null)
            return;

        cv->Lock.Acquire();

        while (cv->WaitListHead != null)
        {
            var thread = cv->WaitListHead;

            // Remove from wait list
            cv->WaitListHead = thread->Next;
            if (cv->WaitListHead != null)
                cv->WaitListHead->Prev = null;
            else
                cv->WaitListTail = null;

            thread->Next = null;
            thread->Prev = null;

            // Signal success
            thread->WaitResult = KernelWaitResult.Object0;
            thread->WakeTime = 0;

            // Make thread ready
            if (thread->State == KernelThreadState.Blocked)
            {
                KernelScheduler.MakeReady(thread);
            }
        }

        cv->Lock.Release();
    }

    /// <summary>
    /// Remove a thread from the condition variable's wait list.
    /// Safe to call even if thread was already removed.
    /// Caller must hold cv->Lock.
    /// </summary>
    private static void RemoveFromWaitList(KernelConditionVariable* cv, KernelThread* thread)
    {
        // Check if thread is still in wait list
        bool inList = thread->Prev != null || thread->Next != null ||
                      cv->WaitListHead == thread || cv->WaitListTail == thread;
        if (!inList)
            return;

        if (thread->Prev != null)
            thread->Prev->Next = thread->Next;
        else
            cv->WaitListHead = thread->Next;

        if (thread->Next != null)
            thread->Next->Prev = thread->Prev;
        else
            cv->WaitListTail = thread->Prev;

        thread->Next = null;
        thread->Prev = null;
    }
}
