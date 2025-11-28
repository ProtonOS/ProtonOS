// netos mernel - Kernel Scheduler
// Round-robin preemptive scheduler with support for thread creation and context switching.
// Designed for future Win32 PAL compatibility (CreateThread, WaitForSingleObject, etc.)
// Uses heap allocation for thread structures - no artificial thread limits.

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

/// <summary>
/// Kernel thread scheduler - manages thread lifecycle and context switching.
/// Thread structures are heap-allocated for unlimited thread count.
/// </summary>
public static unsafe class KernelScheduler
{
    private const nuint DefaultStackSize = 64 * 1024;  // 64KB default stack

    private static KernelSpinLock _lock;

    private static KernelThread* _currentThread;        // Currently running thread
    private static KernelThread* _readyQueueHead;       // Head of ready queue
    private static KernelThread* _readyQueueTail;       // Tail of ready queue
    private static KernelThread* _allThreadsHead;       // All threads (for iteration)
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
    public static KernelThread* CurrentThread => _currentThread;

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
        var bootThread = (KernelThread*)HeapAllocator.AllocZeroed((ulong)sizeof(KernelThread));
        if (bootThread == null)
        {
            DebugConsole.WriteLine("[Sched] Failed to allocate boot thread!");
            return;
        }

        bootThread->Id = _nextThreadId++;
        bootThread->State = KernelThreadState.Running;
        bootThread->Priority = KernelThreadPriority.Normal;
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
    public static KernelThread* CreateThread(
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
        var thread = (KernelThread*)HeapAllocator.AllocZeroed((ulong)sizeof(KernelThread));
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
        thread->State = (flags & KernelThreadFlags.CreateSuspended) != 0
            ? KernelThreadState.Suspended
            : KernelThreadState.Ready;
        thread->Priority = KernelThreadPriority.Normal;
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
        thread->Context.Cs = GdtSelectors.KernelCode;
        thread->Context.Ss = GdtSelectors.KernelData;

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
        if (thread->State == KernelThreadState.Ready)
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
            Cpu.HaltForever();
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
            Cpu.HaltForever();
            return;
        }

        thread->ExitCode = exitCode;
        thread->State = KernelThreadState.Terminated;

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
        Cpu.HaltForever();
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
        if (!_initialized || milliseconds == 0)
            return;

        _lock.Acquire();

        var thread = _currentThread;
        if (thread == null)
        {
            _lock.Release();
            return;
        }

        // Calculate wake time
        thread->WakeTime = Apic.TickCount + (milliseconds / 10);  // 10ms per tick
        thread->State = KernelThreadState.Blocked;

        _lock.Release();

        Schedule();
    }

    /// <summary>
    /// Add a thread to the ready queue.
    /// Caller must hold the lock.
    /// </summary>
    private static void AddToReadyQueue(KernelThread* thread)
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
    private static void RemoveFromReadyQueue(KernelThread* thread)
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
        ulong now = Apic.TickCount;
        for (var t = _allThreadsHead; t != null; t = t->NextAll)
        {
            if (t->State == KernelThreadState.Blocked &&
                t->WakeTime > 0 &&
                t->WakeTime <= now)
            {
                t->WakeTime = 0;
                t->State = KernelThreadState.Ready;
                AddToReadyQueue(t);
            }
        }

        // If current thread is still running, move it to ready queue
        if (current != null && current->State == KernelThreadState.Running)
        {
            current->State = KernelThreadState.Ready;
            AddToReadyQueue(current);
        }

        // Pick next thread from ready queue
        var next = _readyQueueHead;
        if (next == null)
        {
            // No ready threads - continue with current or idle
            if (current != null && current->State == KernelThreadState.Ready)
            {
                RemoveFromReadyQueue(current);
                current->State = KernelThreadState.Running;
                _lock.Release();
                return;
            }
            // No runnable threads at all - this shouldn't happen
            _lock.Release();
            return;
        }

        // Remove from ready queue
        RemoveFromReadyQueue(next);
        next->State = KernelThreadState.Running;

        // Context switch if different thread
        if (next != current)
        {
            var oldThread = current;
            _currentThread = next;

            _lock.Release();

            // Perform context switch
            if (oldThread != null)
            {
                Cpu.SwitchContext(&oldThread->Context, &next->Context);
            }
            else
            {
                // First switch - just load new context
                Cpu.LoadContext(&next->Context);
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
        // With 10ms timer, this gives 100ms time slices
        if (Apic.TickCount % 10 == 0)
        {
            Schedule();
        }
    }

    /// <summary>
    /// Make a thread ready to run (add to ready queue).
    /// Called by synchronization primitives when waking threads.
    /// </summary>
    public static void MakeReady(KernelThread* thread)
    {
        if (thread == null)
            return;

        _lock.Acquire();

        if (thread->State == KernelThreadState.Ready)
        {
            // Already in ready queue
            _lock.Release();
            return;
        }

        thread->State = KernelThreadState.Ready;
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
    /// Get the head of the all-threads list for enumeration.
    /// Caller should acquire SchedulerLock before iterating.
    /// </summary>
    public static KernelThread* AllThreadsHead => _allThreadsHead;

    /// <summary>
    /// Get the scheduler lock for safe thread enumeration.
    /// </summary>
    public static ref KernelSpinLock SchedulerLock => ref _lock;
}
