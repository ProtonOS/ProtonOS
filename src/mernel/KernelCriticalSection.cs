// netos mernel - Critical Sections
// Win32-style critical section implementation for PAL compatibility.
// Lightweight mutex with spin-first behavior before blocking.

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

/// <summary>
/// Critical Section - lightweight mutex with spin-wait optimization.
/// Unlike KernelMutex, this is designed for short lock durations.
/// Spins briefly before blocking to avoid context switch overhead.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KernelCriticalSection
{
    // Debug info (for debugging deadlocks)
    public void* DebugInfo;

    // Lock count - negative when locked, 0 when unlocked
    public int LockCount;

    // Recursion count (same owner can enter multiple times)
    public int RecursionCount;

    // Owner thread
    public KernelThread* OwningThread;

    // Event for blocking waiters (created on first contention)
    public KernelEvent* LockSemaphore;

    // Spin count before blocking
    public uint SpinCount;

    // Padding to ensure cache line alignment
    public fixed byte Reserved[8];
}

/// <summary>
/// Critical Section management APIs - matching Win32 for PAL compatibility.
/// </summary>
public static unsafe class KernelCriticalSectionOps
{
    // Default spin count - tuned for typical x86-64 systems
    private const uint DefaultSpinCount = 4000;

    private static KernelSpinLock _globalLock;

    /// <summary>
    /// Initialize a critical section.
    /// </summary>
    public static void InitializeCriticalSection(KernelCriticalSection* cs)
    {
        InitializeCriticalSectionAndSpinCount(cs, 0);
    }

    /// <summary>
    /// Initialize a critical section with a specific spin count.
    /// </summary>
    public static bool InitializeCriticalSectionAndSpinCount(KernelCriticalSection* cs, uint spinCount)
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
    public static uint SetCriticalSectionSpinCount(KernelCriticalSection* cs, uint spinCount)
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
    public static void DeleteCriticalSection(KernelCriticalSection* cs)
    {
        if (cs == null)
            return;

        // Free the lock semaphore if it was created
        if (cs->LockSemaphore != null)
        {
            KernelSync.CloseHandle(cs->LockSemaphore);
            cs->LockSemaphore = null;
        }

        cs->LockCount = -1;
        cs->RecursionCount = 0;
        cs->OwningThread = null;
    }

    /// <summary>
    /// Enter a critical section (blocking).
    /// </summary>
    public static void EnterCriticalSection(KernelCriticalSection* cs)
    {
        if (cs == null)
            return;

        var current = KernelScheduler.CurrentThread;

        // Fast path: try to acquire without contention
        // Increment LockCount atomically - if it was -1, we got the lock
        int oldCount = Cpu.AtomicIncrement(ref cs->LockCount);
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
                Cpu.Pause();

                // Try to acquire - look for LockCount == 0 (unlocked after decrement)
                if (cs->LockCount < 0)
                {
                    oldCount = Cpu.AtomicIncrement(ref cs->LockCount);
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
        KernelSync.WaitForSingleObject(cs->LockSemaphore, 0xFFFFFFFF);

        // We were woken - we own the lock now
        cs->OwningThread = current;
        cs->RecursionCount = 1;
    }

    /// <summary>
    /// Try to enter a critical section (non-blocking).
    /// Returns true if the lock was acquired.
    /// </summary>
    public static bool TryEnterCriticalSection(KernelCriticalSection* cs)
    {
        if (cs == null)
            return false;

        var current = KernelScheduler.CurrentThread;

        // Try to acquire without contention
        int oldCount = Cpu.AtomicIncrement(ref cs->LockCount);
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
        Cpu.AtomicDecrement(ref cs->LockCount);
        return false;
    }

    /// <summary>
    /// Leave a critical section.
    /// </summary>
    public static void LeaveCriticalSection(KernelCriticalSection* cs)
    {
        if (cs == null)
            return;

        // Decrement recursion count
        cs->RecursionCount--;
        if (cs->RecursionCount > 0)
        {
            // Still held recursively - just decrement LockCount
            Cpu.AtomicDecrement(ref cs->LockCount);
            return;
        }

        // Releasing the lock
        cs->OwningThread = null;

        // Decrement LockCount
        int newCount = Cpu.AtomicDecrement(ref cs->LockCount);

        // If newCount >= 0, there are waiters
        if (newCount >= 0 && cs->LockSemaphore != null)
        {
            // Wake one waiter
            KernelSync.SetEvent(cs->LockSemaphore);
        }
    }

    /// <summary>
    /// Ensure the lock semaphore exists (creates it on first contention).
    /// </summary>
    private static void EnsureLockSemaphore(KernelCriticalSection* cs)
    {
        if (cs->LockSemaphore != null)
            return;

        _globalLock.Acquire();

        // Double-check after acquiring lock
        if (cs->LockSemaphore == null)
        {
            // Create an auto-reset event (semaphore-like behavior)
            cs->LockSemaphore = KernelSync.CreateEvent(false, false);
        }

        _globalLock.Release();
    }
}
