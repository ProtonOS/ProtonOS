// netos mernel - PAL Critical Sections
// Win32-style critical section implementation for PAL compatibility.
// Lightweight mutex with spin-first behavior before blocking.

using System.Runtime.InteropServices;
using Kernel.Threading;
using Kernel.Memory;
using Kernel.X64;

namespace Kernel.PAL;

/// <summary>
/// PAL Critical Section - lightweight mutex with spin-wait optimization.
/// Unlike Mutex, this is designed for short lock durations.
/// Spins briefly before blocking to avoid context switch overhead.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct CriticalSection
{
    // Debug info (for debugging deadlocks)
    public void* DebugInfo;

    // Lock count - negative when locked, 0 when unlocked
    public int LockCount;

    // Recursion count (same owner can enter multiple times)
    public int RecursionCount;

    // Owner thread
    public Thread* OwningThread;

    // Event for blocking waiters (created on first contention)
    public Event* LockSemaphore;

    // Spin count before blocking
    public uint SpinCount;

    // Padding to ensure cache line alignment
    public fixed byte Reserved[8];
}

/// <summary>
/// PAL Critical Section management APIs - matching Win32 for PAL compatibility.
/// </summary>
public static unsafe class CriticalSectionOps
{
    // Default spin count - tuned for typical x86-64 systems
    private const uint DefaultSpinCount = 4000;

    private static SpinLock _globalLock;

    /// <summary>
    /// Initialize a critical section.
    /// </summary>
    public static void InitializeCriticalSection(CriticalSection* cs)
    {
        InitializeCriticalSectionAndSpinCount(cs, 0);
    }

    /// <summary>
    /// Initialize a critical section with a specific spin count.
    /// </summary>
    public static bool InitializeCriticalSectionAndSpinCount(CriticalSection* cs, uint spinCount)
    {
        if (cs == null)
            return false;

        cs->DebugInfo = null;
        cs->LockCount = -1;          // -1 means unlocked
        cs->RecursionCount = 0;
        cs->OwningThread = null;
        cs->LockSemaphore = null;    // Created on first contention
        cs->SpinCount = spinCount > 0 ? spinCount : DefaultSpinCount;

        return true;
    }

    /// <summary>
    /// Set the spin count for a critical section.
    /// Returns the previous spin count.
    /// </summary>
    public static uint SetCriticalSectionSpinCount(CriticalSection* cs, uint spinCount)
    {
        if (cs == null)
            return 0;

        uint previous = cs->SpinCount;
        cs->SpinCount = spinCount;
        return previous;
    }

    /// <summary>
    /// Delete a critical section (free resources).
    /// </summary>
    public static void DeleteCriticalSection(CriticalSection* cs)
    {
        if (cs == null)
            return;

        // Free the lock semaphore if it was created
        if (cs->LockSemaphore != null)
        {
            Sync.CloseHandle(cs->LockSemaphore);
            cs->LockSemaphore = null;
        }

        cs->LockCount = -1;
        cs->RecursionCount = 0;
        cs->OwningThread = null;
    }

    /// <summary>
    /// Enter a critical section (blocking).
    /// </summary>
    public static void EnterCriticalSection(CriticalSection* cs)
    {
        if (cs == null)
            return;

        var current = Scheduler.CurrentThread;

        // Fast path: try to acquire without contention
        // Increment LockCount atomically - if it was -1, we got the lock
        int oldCount = CPU.AtomicIncrement(ref cs->LockCount);
        if (oldCount == -1)
        {
            // We got the lock (LockCount went from -1 to 0)
            cs->OwningThread = current;
            cs->RecursionCount = 1;
            return;
        }

        // Check for recursive entry
        if (cs->OwningThread == current)
        {
            // Same thread - just increment recursion
            cs->RecursionCount++;
            return;
        }

        // Contention - spin first
        if (cs->SpinCount > 0)
        {
            for (uint i = 0; i < cs->SpinCount; i++)
            {
                CPU.Pause();

                // Try to acquire - look for LockCount == 0 (unlocked after decrement)
                if (cs->LockCount < 0)
                {
                    oldCount = CPU.AtomicIncrement(ref cs->LockCount);
                    if (oldCount == -1)
                    {
                        cs->OwningThread = current;
                        cs->RecursionCount = 1;
                        return;
                    }
                }
            }
        }

        // Spin failed - need to block
        // Ensure we have a lock semaphore
        EnsureLockSemaphore(cs);

        // Wait on the semaphore
        Sync.WaitForSingleObject(cs->LockSemaphore, 0xFFFFFFFF);

        // We were woken - we own the lock now
        cs->OwningThread = current;
        cs->RecursionCount = 1;
    }

    /// <summary>
    /// Try to enter a critical section (non-blocking).
    /// Returns true if the lock was acquired.
    /// </summary>
    public static bool TryEnterCriticalSection(CriticalSection* cs)
    {
        if (cs == null)
            return false;

        var current = Scheduler.CurrentThread;

        // Try to acquire without contention
        int oldCount = CPU.AtomicIncrement(ref cs->LockCount);
        if (oldCount == -1)
        {
            cs->OwningThread = current;
            cs->RecursionCount = 1;
            return true;
        }

        // Check for recursive entry
        if (cs->OwningThread == current)
        {
            cs->RecursionCount++;
            return true;
        }

        // Contention - restore lock count and fail
        CPU.AtomicDecrement(ref cs->LockCount);
        return false;
    }

    /// <summary>
    /// Leave a critical section.
    /// </summary>
    public static void LeaveCriticalSection(CriticalSection* cs)
    {
        if (cs == null)
            return;

        // Decrement recursion count
        cs->RecursionCount--;
        if (cs->RecursionCount > 0)
        {
            // Still held recursively - just decrement LockCount
            CPU.AtomicDecrement(ref cs->LockCount);
            return;
        }

        // Releasing the lock
        cs->OwningThread = null;

        // Decrement LockCount
        int newCount = CPU.AtomicDecrement(ref cs->LockCount);

        // If newCount >= 0, there are waiters
        if (newCount >= 0 && cs->LockSemaphore != null)
        {
            // Wake one waiter
            Sync.SetEvent(cs->LockSemaphore);
        }
    }

    /// <summary>
    /// Ensure the lock semaphore exists (creates it on first contention).
    /// </summary>
    private static void EnsureLockSemaphore(CriticalSection* cs)
    {
        if (cs->LockSemaphore != null)
            return;

        _globalLock.Acquire();

        // Double-check after acquiring lock
        if (cs->LockSemaphore == null)
        {
            // Create an auto-reset event (semaphore-like behavior)
            cs->LockSemaphore = Sync.CreateEvent(false, false);
        }

        _globalLock.Release();
    }
}
