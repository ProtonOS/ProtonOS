// ProtonOS kernel - Kernel Scheduler
// Round-robin preemptive scheduler with support for thread creation and context switching.
// Designed for future Win32 PAL compatibility (CreateThread, WaitForSingleObject, etc.)
// Uses heap allocation for thread structures - no artificial thread limits.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Threading;

/// <summary>
/// Kernel thread scheduler - manages thread lifecycle and context switching.
/// Thread structures are heap-allocated for unlimited thread count.
/// </summary>
public static unsafe class Scheduler
{
    private const nuint DefaultStackSize = 64 * 1024;  // 64KB default stack

    private static SpinLock _lock;

    private static Thread* _currentThread;        // Currently running thread
    private static Thread* _readyQueueHead;       // Head of ready queue
    private static Thread* _readyQueueTail;       // Tail of ready queue
    private static Thread* _allThreadsHead;       // All threads (for iteration)
    private static uint _nextThreadId;                  // Next thread ID to assign
    private static int _threadCount;                    // Number of active threads
    private static bool _initialized;
    private static bool _schedulingEnabled;

    /// <summary>
    /// Whether the scheduler is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Whether scheduling/preemption is enabled
    /// </summary>
    public static bool IsSchedulingEnabled => _schedulingEnabled;

    /// <summary>
    /// Current thread (null if none)
    /// </summary>
    public static Thread* CurrentThread => _currentThread;

    /// <summary>
    /// Initialize the scheduler.
    /// Creates thread 0 for the current (boot) execution context.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[Sched] Initializing scheduler...");

        _currentThread = null;
        _readyQueueHead = null;
        _readyQueueTail = null;
        _allThreadsHead = null;
        _nextThreadId = 1;
        _threadCount = 0;
        _schedulingEnabled = false;

        // Create thread 0 for the boot/idle thread (current execution context)
        // This thread doesn't need a separate stack - it uses the boot stack
        var bootThread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (bootThread == null)
        {
            DebugConsole.WriteLine("[Sched] Failed to allocate boot thread!");
            return;
        }

        bootThread->Id = _nextThreadId++;
        bootThread->State = ThreadState.Running;
        bootThread->Priority = ThreadPriority.Normal;
        bootThread->Next = null;
        bootThread->Prev = null;
        bootThread->NextReady = null;
        bootThread->PrevReady = null;
        bootThread->NextAll = null;

        // Add to all-threads list
        _allThreadsHead = bootThread;

        _currentThread = bootThread;
        _threadCount = 1;

        _initialized = true;
        DebugConsole.Write("[Sched] Initialized, boot thread ID: ");
        DebugConsole.WriteHex((ushort)bootThread->Id);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Enable preemptive scheduling.
    /// Call this after timer is set up.
    /// </summary>
    public static void EnableScheduling()
    {
        _schedulingEnabled = true;
        DebugConsole.WriteLine("[Sched] Preemptive scheduling enabled");
    }

    /// <summary>
    /// Disable preemptive scheduling.
    /// </summary>
    public static void DisableScheduling()
    {
        _schedulingEnabled = false;
    }

    /// <summary>
    /// Create a new kernel thread.
    /// Returns thread pointer, or null on failure.
    /// </summary>
    /// <param name="entryPoint">Thread entry point (returns exit code)</param>
    /// <param name="parameter">Parameter passed to entry point</param>
    /// <param name="stackSize">Stack size (0 = default)</param>
    /// <param name="flags">Creation flags</param>
    /// <param name="threadId">Output: assigned thread ID</param>
    /// <returns>Thread pointer, or null on failure</returns>
    public static Thread* CreateThread(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        out uint threadId)
    {
        threadId = 0;

        if (!_initialized)
            return null;

        if (stackSize == 0)
            stackSize = DefaultStackSize;

        _lock.Acquire();

        // Allocate thread structure
        var thread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (thread == null)
        {
            _lock.Release();
            DebugConsole.WriteLine("[Sched] Failed to allocate thread structure!");
            return null;
        }

        // Allocate stack
        ulong stackBase = PageAllocator.AllocatePages((stackSize + 4095) / 4096);
        if (stackBase == 0)
        {
            HeapAllocator.Free(thread);
            _lock.Release();
            DebugConsole.WriteLine("[Sched] Failed to allocate thread stack!");
            return null;
        }

        // Stack grows down, so base is at high address
        ulong stackTop = stackBase + stackSize;
        ulong stackLimit = stackBase;

        // Initialize thread
        thread->Id = _nextThreadId++;
        bool createSuspended = (flags & ThreadFlags.CreateSuspended) != 0;
        thread->State = createSuspended ? ThreadState.Suspended : ThreadState.Ready;
        thread->SuspendCount = createSuspended ? 1 : 0;
        thread->Priority = ThreadPriority.Normal;
        thread->StackBase = stackTop;
        thread->StackLimit = stackLimit;
        thread->StackSize = stackSize;
        thread->EntryPoint = entryPoint;
        thread->Parameter = parameter;
        thread->ExitCode = 0;
        thread->Next = null;
        thread->Prev = null;
        thread->NextReady = null;
        thread->PrevReady = null;
        thread->WaitObject = null;
        thread->WaitResult = 0;

        // Set up initial context
        thread->Context = default;
        thread->Context.Rip = (ulong)(delegate* unmanaged<void>)&ThreadEntryWrapper;
        thread->Context.Rsp = stackTop;
        thread->Context.Rflags = 0x202;  // IF=1 (interrupts enabled), reserved bit 1
        thread->Context.Cs = GDTSelectors.KernelCode;
        thread->Context.Ss = GDTSelectors.KernelData;

        // Align stack to 16 bytes (required by ABI)
        thread->Context.Rsp &= ~0xFUL;
        // Stack must be 16-byte aligned BEFORE call, so subtract 8 for the "return address"
        thread->Context.Rsp -= 8;

        threadId = thread->Id;
        _threadCount++;

        // Add to all-threads list
        thread->NextAll = _allThreadsHead;
        _allThreadsHead = thread;

        // Add to ready queue if not suspended
        if (thread->State == ThreadState.Ready)
        {
            AddToReadyQueue(thread);
        }

        _lock.Release();

        DebugConsole.Write("[Sched] Created thread ");
        DebugConsole.WriteHex((ushort)threadId);
        DebugConsole.Write(" stack 0x");
        DebugConsole.WriteHex(stackBase);
        DebugConsole.WriteLine();

        return thread;
    }

    /// <summary>
    /// Thread entry point wrapper - called when a new thread starts.
    /// Sets up the environment and calls the actual entry point.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void ThreadEntryWrapper()
    {
        var thread = _currentThread;
        if (thread == null)
        {
            CPU.HaltForever();
            return;
        }

        // Call the actual thread entry point
        uint exitCode = 0;
        if (thread->EntryPoint != null)
        {
            exitCode = thread->EntryPoint(thread->Parameter);
        }

        // Thread is done - exit
        ExitThread(exitCode);
    }

    /// <summary>
    /// Exit the current thread.
    /// </summary>
    public static void ExitThread(uint exitCode)
    {
        _lock.Acquire();

        var thread = _currentThread;
        if (thread == null)
        {
            _lock.Release();
            CPU.HaltForever();
            return;
        }

        thread->ExitCode = exitCode;
        thread->State = ThreadState.Terminated;

        DebugConsole.Write("[Sched] Thread ");
        DebugConsole.WriteHex((ushort)thread->Id);
        DebugConsole.Write(" exited with code ");
        DebugConsole.WriteHex(exitCode);
        DebugConsole.WriteLine();

        // TODO: Free stack memory
        // TODO: Wake any threads waiting on this thread
        // TODO: Free thread structure (need to defer until after context switch)

        _threadCount--;
        _lock.Release();

        // Schedule another thread
        Schedule();

        // Should never reach here
        CPU.HaltForever();
    }

    /// <summary>
    /// Yield the current thread's time slice.
    /// </summary>
    public static void Yield()
    {
        if (!_initialized || !_schedulingEnabled)
            return;

        Schedule();
    }

    /// <summary>
    /// Sleep for specified milliseconds.
    /// </summary>
    public static void Sleep(uint milliseconds)
    {
        SleepEx(milliseconds, false);
    }

    /// <summary>
    /// Sleep for specified milliseconds with optional alertable state.
    /// If alertable is true, the sleep can be interrupted by APCs.
    /// </summary>
    /// <param name="milliseconds">Time to sleep in milliseconds</param>
    /// <param name="alertable">If true, can be woken by APCs</param>
    /// <returns>0 if timeout elapsed, WAIT_IO_COMPLETION (0xC0) if woken by APC</returns>
    public static uint SleepEx(uint milliseconds, bool alertable)
    {
        if (!_initialized)
            return 0;

        var thread = _currentThread;
        if (thread == null)
            return 0;

        // If alertable and APCs are already pending, deliver them immediately
        if (alertable && HasPendingApc(thread))
        {
            DeliverApcs();
            return WaitResult.IoCompletion;
        }

        // Sleep(0) just yields
        if (milliseconds == 0)
        {
            Schedule();
            return 0;
        }

        _lock.Acquire();

        // Calculate wake time
        thread->WakeTime = APIC.TickCount + milliseconds;  // 1ms per tick
        thread->State = ThreadState.Blocked;
        thread->Alertable = alertable;
        thread->WaitResult = 0;  // Will be set to IoCompletion if woken by APC

        _lock.Release();

        Schedule();

        // We've been woken up - check why
        _lock.Acquire();
        thread->Alertable = false;
        uint result = thread->WaitResult;
        thread->WaitResult = 0;
        _lock.Release();

        // If woken by APC, deliver all pending APCs
        if (result == WaitResult.IoCompletion)
        {
            DeliverApcs();
        }

        return result;
    }

    /// <summary>
    /// Add a thread to the ready queue.
    /// Caller must hold the lock.
    /// </summary>
    private static void AddToReadyQueue(Thread* thread)
    {
        thread->NextReady = null;
        thread->PrevReady = _readyQueueTail;

        if (_readyQueueTail != null)
        {
            _readyQueueTail->NextReady = thread;
        }
        else
        {
            _readyQueueHead = thread;
        }
        _readyQueueTail = thread;
    }

    /// <summary>
    /// Remove a thread from the ready queue.
    /// Caller must hold the lock.
    /// </summary>
    private static void RemoveFromReadyQueue(Thread* thread)
    {
        if (thread->PrevReady != null)
            thread->PrevReady->NextReady = thread->NextReady;
        else
            _readyQueueHead = thread->NextReady;

        if (thread->NextReady != null)
            thread->NextReady->PrevReady = thread->PrevReady;
        else
            _readyQueueTail = thread->PrevReady;

        thread->NextReady = null;
        thread->PrevReady = null;
    }

    /// <summary>
    /// Pick the next thread to run.
    /// Called from timer interrupt or yield.
    /// </summary>
    public static void Schedule()
    {
        if (!_initialized)
            return;

        _lock.Acquire();

        var current = _currentThread;

        // Wake up any sleeping threads whose time has come
        ulong now = APIC.TickCount;
        for (var t = _allThreadsHead; t != null; t = t->NextAll)
        {
            if (t->State == ThreadState.Blocked &&
                t->WakeTime > 0 &&
                t->WakeTime <= now)
            {
                t->WakeTime = 0;
                t->State = ThreadState.Ready;
                AddToReadyQueue(t);
            }
        }

        // If current thread is still running, move it to ready queue
        if (current != null && current->State == ThreadState.Running)
        {
            current->State = ThreadState.Ready;
            AddToReadyQueue(current);
        }

        // Pick next thread from ready queue
        var next = _readyQueueHead;
        if (next == null)
        {
            // No ready threads - continue with current or idle
            if (current != null && current->State == ThreadState.Ready)
            {
                RemoveFromReadyQueue(current);
                current->State = ThreadState.Running;
                _lock.Release();
                return;
            }
            // No runnable threads at all - this shouldn't happen
            _lock.Release();
            return;
        }

        // Remove from ready queue
        RemoveFromReadyQueue(next);
        next->State = ThreadState.Running;

        // Context switch if different thread
        if (next != current)
        {
            var oldThread = current;
            _currentThread = next;

            _lock.Release();

            // Perform context switch
            if (oldThread != null)
            {
                CPU.SwitchContext(&oldThread->Context, &next->Context);
            }
            else
            {
                // First switch - just load new context
                CPU.LoadContext(&next->Context);
            }
        }
        else
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Timer tick handler - called from APIC timer interrupt.
    /// </summary>
    public static void TimerTick()
    {
        if (!_initialized || !_schedulingEnabled)
            return;

        // Only reschedule every N ticks to avoid too much overhead
        // With 1ms timer, this gives 10ms time slices
        if (APIC.TickCount % 10 == 0)
        {
            Schedule();
        }
    }

    /// <summary>
    /// Make a thread ready to run (add to ready queue).
    /// Called by synchronization primitives when waking threads.
    /// </summary>
    public static void MakeReady(Thread* thread)
    {
        if (thread == null)
            return;

        _lock.Acquire();

        if (thread->State == ThreadState.Ready)
        {
            // Already in ready queue
            _lock.Release();
            return;
        }

        thread->State = ThreadState.Ready;
        AddToReadyQueue(thread);

        _lock.Release();
    }

    /// <summary>
    /// Get thread count
    /// </summary>
    public static int ThreadCount => _threadCount;

    /// <summary>
    /// Get current thread ID
    /// </summary>
    public static uint GetCurrentThreadId()
    {
        if (!_initialized || _currentThread == null)
            return 0;
        return _currentThread->Id;
    }

    /// <summary>
    /// Queue an APC to a thread.
    /// </summary>
    /// <param name="thread">Target thread</param>
    /// <param name="function">APC function pointer</param>
    /// <param name="parameter">Parameter to pass to function</param>
    /// <returns>True if APC was queued successfully</returns>
    public static bool QueueApc(Thread* thread, delegate* unmanaged<nuint, void> function, nuint parameter)
    {
        if (thread == null || function == null || !_initialized)
            return false;

        // Allocate APC entry
        var apc = (APC*)HeapAllocator.AllocZeroed((ulong)sizeof(APC));
        if (apc == null)
            return false;

        apc->Function = function;
        apc->Parameter = parameter;
        apc->Next = null;

        _lock.Acquire();

        // Can't queue to terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _lock.Release();
            HeapAllocator.Free(apc);
            return false;
        }

        // Add to tail of APC queue (FIFO order)
        if (thread->APCQueueTail != null)
        {
            thread->APCQueueTail->Next = apc;
        }
        else
        {
            thread->APCQueueHead = apc;
        }
        thread->APCQueueTail = apc;

        // If thread is in alertable wait, wake it up
        if (thread->Alertable && thread->State == ThreadState.Blocked)
        {
            thread->WaitResult = WaitResult.IoCompletion;
            thread->WakeTime = 0;
            thread->State = ThreadState.Ready;
            AddToReadyQueue(thread);
        }

        _lock.Release();
        return true;
    }

    /// <summary>
    /// Check if a thread has pending APCs.
    /// </summary>
    public static bool HasPendingApc(Thread* thread)
    {
        if (thread == null)
            return false;
        return thread->APCQueueHead != null;
    }

    /// <summary>
    /// Deliver all pending APCs for the current thread.
    /// Called after returning from an alertable wait.
    /// Returns the number of APCs delivered.
    /// </summary>
    public static int DeliverApcs()
    {
        if (!_initialized || _currentThread == null)
            return 0;

        int count = 0;
        var thread = _currentThread;

        // Process APCs until queue is empty
        while (true)
        {
            _lock.Acquire();

            var apc = thread->APCQueueHead;
            if (apc == null)
            {
                _lock.Release();
                break;
            }

            // Dequeue this APC
            thread->APCQueueHead = apc->Next;
            if (thread->APCQueueHead == null)
            {
                thread->APCQueueTail = null;
            }

            // Get function and parameter before releasing lock
            var function = apc->Function;
            var parameter = apc->Parameter;

            _lock.Release();

            // Free the APC entry
            HeapAllocator.Free(apc);

            // Call the APC function (outside the lock)
            if (function != null)
            {
                function(parameter);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Get the head of the all-threads list for enumeration.
    /// Caller should acquire SchedulerLock before iterating.
    /// </summary>
    public static Thread* AllThreadsHead => _allThreadsHead;

    /// <summary>
    /// Get the scheduler lock for safe thread enumeration.
    /// </summary>
    public static ref SpinLock SchedulerLock => ref _lock;

    /// <summary>
    /// Suspend a thread.
    /// Increments the suspend count and removes the thread from the ready queue.
    /// </summary>
    /// <param name="thread">Thread to suspend</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int SuspendThread(Thread* thread)
    {
        if (thread == null || !_initialized)
            return -1;

        _lock.Acquire();

        // Can't suspend terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _lock.Release();
            return -1;
        }

        int previousCount = thread->SuspendCount;
        thread->SuspendCount++;

        // If this is the first suspend (count was 0), actually suspend the thread
        if (previousCount == 0)
        {
            if (thread->State == ThreadState.Ready)
            {
                // Remove from ready queue
                RemoveFromReadyQueue(thread);
            }
            thread->State = ThreadState.Suspended;

            // If suspending the current thread, need to reschedule
            if (thread == _currentThread)
            {
                _lock.Release();
                Schedule();
                return previousCount;
            }
        }

        _lock.Release();
        return previousCount;
    }

    /// <summary>
    /// Resume a suspended thread.
    /// Decrements the suspend count and makes the thread ready if count reaches 0.
    /// </summary>
    /// <param name="thread">Thread to resume</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int ResumeThread(Thread* thread)
    {
        if (thread == null || !_initialized)
            return -1;

        _lock.Acquire();

        // Can't resume terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _lock.Release();
            return -1;
        }

        // Can't resume if not suspended
        if (thread->SuspendCount == 0)
        {
            _lock.Release();
            return 0;  // Already not suspended
        }

        int previousCount = thread->SuspendCount;
        thread->SuspendCount--;

        // If suspend count reaches 0, make thread ready
        if (thread->SuspendCount == 0 && thread->State == ThreadState.Suspended)
        {
            thread->State = ThreadState.Ready;
            AddToReadyQueue(thread);
        }

        _lock.Release();
        return previousCount;
    }
}
