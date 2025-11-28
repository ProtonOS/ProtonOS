// netos mernel - Slim Reader/Writer Lock
// Win32-style SRW lock implementation for PAL compatibility.
// Allows multiple concurrent readers OR a single exclusive writer.

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

/// <summary>
/// Slim Reader/Writer Lock - allows multiple readers or single writer.
/// Writer-preferring to prevent writer starvation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KernelSRWLock
{
    // State encoding (single 32-bit word for lock-free fast path):
    // Bits 0-15:  Reader count (0-65535 concurrent readers)
    // Bit 16:     Writer active flag
    // Bit 17:     Writer waiting flag (gives writers priority)
    public int State;

    // Wait queues for blocked threads
    public KernelThread* ReaderWaitHead;
    public KernelThread* ReaderWaitTail;
    public KernelThread* WriterWaitHead;
    public KernelThread* WriterWaitTail;

    // Spin lock for protecting wait queues
    public KernelSpinLock QueueLock;

    private const int ReaderMask = 0xFFFF;        // Bits 0-15
    private const int WriterActive = 0x10000;     // Bit 16
    private const int WriterWaiting = 0x20000;    // Bit 17
}

/// <summary>
/// SRW Lock management APIs - matching Win32 for PAL compatibility.
/// </summary>
public static unsafe class KernelSRWLockOps
{
    private const int ReaderMask = 0xFFFF;
    private const int WriterActive = 0x10000;
    private const int WriterWaiting = 0x20000;

    /// <summary>
    /// Initialize an SRW lock.
    /// </summary>
    public static void InitializeSRWLock(KernelSRWLock* srw)
    {
        if (srw == null)
            return;

        srw->State = 0;
        srw->ReaderWaitHead = null;
        srw->ReaderWaitTail = null;
        srw->WriterWaitHead = null;
        srw->WriterWaitTail = null;
        srw->QueueLock = default;
    }

    /// <summary>
    /// Acquire the lock for shared (read) access.
    /// Multiple threads can hold shared access simultaneously.
    /// </summary>
    public static void AcquireSRWLockShared(KernelSRWLock* srw)
    {
        if (srw == null)
            return;

        while (true)
        {
            int state = ReadState(srw);

            // Fast path: no writer active or waiting, just increment reader count
            if ((state & (WriterActive | WriterWaiting)) == 0)
            {
                int newState = state + 1;
                if ((newState & ReaderMask) == 0)
                {
                    // Reader overflow - shouldn't happen in practice
                    continue;
                }

                if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
                    return; // Got the lock

                continue; // CAS failed, retry
            }

            // Slow path: writer active or waiting, need to block
            var current = KernelScheduler.CurrentThread;
            if (current == null)
                return; // No thread context, just spin

            srw->QueueLock.Acquire();

            // Re-check state after acquiring queue lock
            state = ReadState(srw);
            if ((state & (WriterActive | WriterWaiting)) == 0)
            {
                // Writer released while we were waiting for queue lock
                srw->QueueLock.Release();
                continue;
            }

            // Add to reader wait queue
            current->Next = null;
            current->Prev = srw->ReaderWaitTail;
            if (srw->ReaderWaitTail != null)
                srw->ReaderWaitTail->Next = current;
            else
                srw->ReaderWaitHead = current;
            srw->ReaderWaitTail = current;

            current->State = KernelThreadState.Blocked;
            current->WaitResult = 0;

            srw->QueueLock.Release();

            // Yield to scheduler
            KernelScheduler.Schedule();

            // We were woken - try again from the top
        }
    }

    /// <summary>
    /// Try to acquire the lock for shared (read) access without blocking.
    /// </summary>
    public static bool TryAcquireSRWLockShared(KernelSRWLock* srw)
    {
        if (srw == null)
            return false;

        int state = ReadState(srw);

        // Can only acquire if no writer active or waiting
        if ((state & (WriterActive | WriterWaiting)) != 0)
            return false;

        int newState = state + 1;
        if ((newState & ReaderMask) == 0)
            return false; // Overflow

        return Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state;
    }

    /// <summary>
    /// Release shared (read) access to the lock.
    /// </summary>
    public static void ReleaseSRWLockShared(KernelSRWLock* srw)
    {
        if (srw == null)
            return;

        while (true)
        {
            int state = ReadState(srw);
            int readerCount = state & ReaderMask;

            if (readerCount == 0)
                return; // Not holding the lock

            int newState = state - 1;

            if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
            {
                // Successfully decremented reader count
                // If we were the last reader and a writer is waiting, wake it
                if ((newState & ReaderMask) == 0 && (newState & WriterWaiting) != 0)
                {
                    WakeOneWriter(srw);
                }
                return;
            }
            // CAS failed, retry
        }
    }

    /// <summary>
    /// Acquire the lock for exclusive (write) access.
    /// Only one thread can hold exclusive access, and no readers allowed.
    /// </summary>
    public static void AcquireSRWLockExclusive(KernelSRWLock* srw)
    {
        if (srw == null)
            return;

        while (true)
        {
            int state = ReadState(srw);

            // Fast path: no readers and no writer, acquire directly
            if ((state & (ReaderMask | WriterActive)) == 0)
            {
                int newState = state | WriterActive;
                // Clear writer waiting if we set it
                newState &= ~WriterWaiting;

                if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
                    return; // Got the lock

                continue; // CAS failed, retry
            }

            // Slow path: readers active or another writer, need to block
            var current = KernelScheduler.CurrentThread;
            if (current == null)
                return; // No thread context

            srw->QueueLock.Acquire();

            // Set writer waiting flag to block new readers
            while (true)
            {
                state = ReadState(srw);
                if ((state & WriterWaiting) != 0)
                    break; // Already set

                int newState = state | WriterWaiting;
                if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
                    break;
            }

            // Re-check if we can acquire now
            state = ReadState(srw);
            if ((state & (ReaderMask | WriterActive)) == 0)
            {
                // Lock is free now
                int newState = (state | WriterActive) & ~WriterWaiting;
                if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
                {
                    srw->QueueLock.Release();
                    return; // Got the lock
                }
            }

            // Add to writer wait queue
            current->Next = null;
            current->Prev = srw->WriterWaitTail;
            if (srw->WriterWaitTail != null)
                srw->WriterWaitTail->Next = current;
            else
                srw->WriterWaitHead = current;
            srw->WriterWaitTail = current;

            current->State = KernelThreadState.Blocked;
            current->WaitResult = 0;

            srw->QueueLock.Release();

            // Yield to scheduler
            KernelScheduler.Schedule();

            // We were woken - try again from the top
        }
    }

    /// <summary>
    /// Try to acquire the lock for exclusive (write) access without blocking.
    /// </summary>
    public static bool TryAcquireSRWLockExclusive(KernelSRWLock* srw)
    {
        if (srw == null)
            return false;

        int state = ReadState(srw);

        // Can only acquire if no readers and no writer
        if ((state & (ReaderMask | WriterActive)) != 0)
            return false;

        int newState = state | WriterActive;
        return Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state;
    }

    /// <summary>
    /// Release exclusive (write) access to the lock.
    /// </summary>
    public static void ReleaseSRWLockExclusive(KernelSRWLock* srw)
    {
        if (srw == null)
            return;

        while (true)
        {
            int state = ReadState(srw);

            if ((state & WriterActive) == 0)
                return; // Not holding exclusive lock

            // Clear writer active flag
            int newState = state & ~WriterActive;

            // Check if we should clear writer waiting flag
            srw->QueueLock.Acquire();
            bool hasWaitingWriters = srw->WriterWaitHead != null;
            if (!hasWaitingWriters)
                newState &= ~WriterWaiting;
            srw->QueueLock.Release();

            if (Cpu.AtomicCompareExchange(ref srw->State, newState, state) == state)
            {
                // Successfully released
                // Wake waiters: prefer writers (writer-preferring lock)
                if (hasWaitingWriters)
                {
                    WakeOneWriter(srw);
                }
                else
                {
                    WakeAllReaders(srw);
                }
                return;
            }
            // CAS failed, retry
        }
    }

    /// <summary>
    /// Wake one waiting writer.
    /// </summary>
    private static void WakeOneWriter(KernelSRWLock* srw)
    {
        srw->QueueLock.Acquire();

        var thread = srw->WriterWaitHead;
        if (thread != null)
        {
            // Remove from wait queue
            srw->WriterWaitHead = thread->Next;
            if (srw->WriterWaitHead != null)
                srw->WriterWaitHead->Prev = null;
            else
                srw->WriterWaitTail = null;

            thread->Next = null;
            thread->Prev = null;

            // Make ready
            if (thread->State == KernelThreadState.Blocked)
            {
                KernelScheduler.MakeReady(thread);
            }
        }

        srw->QueueLock.Release();
    }

    /// <summary>
    /// Wake all waiting readers.
    /// </summary>
    private static void WakeAllReaders(KernelSRWLock* srw)
    {
        srw->QueueLock.Acquire();

        while (srw->ReaderWaitHead != null)
        {
            var thread = srw->ReaderWaitHead;

            // Remove from wait queue
            srw->ReaderWaitHead = thread->Next;
            if (srw->ReaderWaitHead != null)
                srw->ReaderWaitHead->Prev = null;
            else
                srw->ReaderWaitTail = null;

            thread->Next = null;
            thread->Prev = null;

            // Make ready
            if (thread->State == KernelThreadState.Blocked)
            {
                KernelScheduler.MakeReady(thread);
            }
        }

        srw->QueueLock.Release();
    }

    /// <summary>
    /// Read current state with volatile semantics.
    /// </summary>
    private static int ReadState(KernelSRWLock* srw)
    {
        // Read with memory barrier - prevents reordering
        int* ptr = &srw->State;
        int value = *ptr;
        Cpu.MemoryBarrier();
        return value;
    }
}
