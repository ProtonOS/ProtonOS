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
/// Supports per-CPU queues with work stealing for SMP systems.
/// </summary>
public static unsafe class Scheduler
{
    private const nuint DefaultStackSize = 64 * 1024;  // 64KB default stack

    // Global lock for thread list and creation
    private static SpinLock _globalLock;

    // Legacy single-CPU mode (used before SMP init)
    private static Thread* _bspCurrentThread;     // BSP's current thread (before SMP)
    private static Thread* _bspReadyQueueHead;    // BSP ready queue head (before SMP)
    private static Thread* _bspReadyQueueTail;    // BSP ready queue tail (before SMP)

    // Global thread tracking
    private static Thread* _allThreadsHead;       // All threads (for iteration)
    private static uint _nextThreadId;            // Next thread ID to assign
    private static int _threadCount;              // Number of active threads
    private static bool _initialized;
    private static bool _schedulingEnabled;
    private static bool _smpEnabled;              // Using per-CPU queues

    /// <summary>
    /// Whether the scheduler is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Whether scheduling/preemption is enabled
    /// </summary>
    public static bool IsSchedulingEnabled => _schedulingEnabled;

    /// <summary>
    /// Whether SMP mode is active (using per-CPU queues)
    /// </summary>
    public static bool IsSmpEnabled => _smpEnabled;

    /// <summary>
    /// Current thread (null if none)
    /// Uses per-CPU state when SMP is enabled, otherwise global BSP thread.
    /// </summary>
    public static Thread* CurrentThread
    {
        get
        {
            if (_smpEnabled && PerCpu.IsInitialized)
                return PerCpu.CurrentThread;
            return _bspCurrentThread;
        }
    }

    /// <summary>
    /// Initialize the scheduler.
    /// Creates thread 0 for the current (boot) execution context.
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[Sched] Initializing scheduler...");

        _bspCurrentThread = null;
        _bspReadyQueueHead = null;
        _bspReadyQueueTail = null;
        _allThreadsHead = null;
        _nextThreadId = 1;
        _threadCount = 0;
        _schedulingEnabled = false;
        _smpEnabled = false;

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
        bootThread->CpuAffinity = 0;  // Can run on any CPU
        bootThread->LastCpu = 0;
        bootThread->PreferredCpu = 0;
        bootThread->Next = null;
        bootThread->Prev = null;
        bootThread->NextReady = null;
        bootThread->PrevReady = null;
        bootThread->NextAll = null;

        // Allocate extended state area for boot thread (FPU/SSE/AVX)
        AllocateExtendedState(bootThread);

        // Add to all-threads list
        _allThreadsHead = bootThread;

        _bspCurrentThread = bootThread;
        _threadCount = 1;

        _initialized = true;
        DebugConsole.WriteLine(string.Format("[Sched] Initialized, boot thread ID: 0x{0}",
            bootThread->Id.ToString("X4", null)));
    }

    /// <summary>
    /// Enable SMP mode - switch to per-CPU queues.
    /// Called after SMP.Init() has set up per-CPU state.
    /// </summary>
    public static void EnableSmp()
    {
        if (!_initialized || _smpEnabled)
            return;

        // Move boot thread to BSP's per-CPU state
        if (PerCpu.IsInitialized)
        {
            var perCpu = PerCpu.Current;
            perCpu->CurrentThread = _bspCurrentThread;
            perCpu->ReadyQueueHead = _bspReadyQueueHead;
            perCpu->ReadyQueueTail = _bspReadyQueueTail;

            // Count threads in ready queue
            int count = 0;
            for (var t = perCpu->ReadyQueueHead; t != null; t = t->NextReady)
                count++;
            perCpu->ReadyQueueCount = count;

            _smpEnabled = true;
            DebugConsole.WriteLine("[Sched] SMP mode enabled (per-CPU queues active)");
        }
    }

    /// <summary>
    /// Initialize scheduler on a secondary CPU (AP).
    /// Creates idle thread for this CPU.
    /// </summary>
    public static void InitSecondaryCpu()
    {
        if (!_smpEnabled || !PerCpu.IsInitialized)
            return;

        var perCpu = PerCpu.Current;

        // Create idle thread for this CPU
        uint idleThreadId;
        var idleThread = CreateIdleThread(out idleThreadId);
        if (idleThread != null)
        {
            perCpu->IdleThread = idleThread;
            perCpu->CurrentThread = idleThread;
            idleThread->State = ThreadState.Running;
            idleThread->LastCpu = perCpu->CpuIndex;
        }
    }

    /// <summary>
    /// Create an idle thread for a CPU.
    /// </summary>
    private static Thread* CreateIdleThread(out uint threadId)
    {
        return CreateThread(
            (delegate* unmanaged<void*, uint>)&IdleThreadEntry,
            null,
            8192, // Small stack for idle thread
            0,
            out threadId);
    }

    /// <summary>
    /// Idle thread entry point - runs when no other threads are ready.
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint IdleThreadEntry(void* param)
    {
        while (true)
        {
            // Halt until interrupt
            CPU.Halt();
        }
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

        _globalLock.Acquire();

        // Allocate thread structure
        var thread = (Thread*)HeapAllocator.AllocZeroed((ulong)sizeof(Thread));
        if (thread == null)
        {
            _globalLock.Release();
            DebugConsole.WriteLine("[Sched] Failed to allocate thread structure!");
            return null;
        }

        // Allocate stack
        ulong stackBase = PageAllocator.AllocatePages((stackSize + 4095) / 4096);
        if (stackBase == 0)
        {
            HeapAllocator.Free(thread);
            _globalLock.Release();
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

        // SMP support - initialize affinity
        thread->CpuAffinity = 0;  // Can run on any CPU
        thread->LastCpu = _smpEnabled ? PerCpu.CpuIndex : 0;
        thread->PreferredCpu = thread->LastCpu;

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

        // Allocate extended state area (FPU/SSE/AVX) with 64-byte alignment
        AllocateExtendedState(thread);

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

        _globalLock.Release();

        DebugConsole.WriteLine(string.Format("[Sched] Created thread 0x{0} stack 0x{1}",
            threadId.ToString("X4", null), stackBase.ToString("X", null)));

        return thread;
    }

    /// <summary>
    /// Thread entry point wrapper - called when a new thread starts.
    /// Sets up the environment and calls the actual entry point.
    /// </summary>
    [UnmanagedCallersOnly]
    private static void ThreadEntryWrapper()
    {
        var thread = CurrentThread;
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
        _globalLock.Acquire();

        var thread = CurrentThread;
        if (thread == null)
        {
            _globalLock.Release();
            CPU.HaltForever();
            return;
        }

        thread->ExitCode = exitCode;
        thread->State = ThreadState.Terminated;

        DebugConsole.WriteLine(string.Format("[Sched] Thread 0x{0} exited with code 0x{1}",
            thread->Id.ToString("X4", null), exitCode.ToString("X8", null)));

        // TODO: Free stack memory
        // TODO: Wake any threads waiting on this thread
        // TODO: Free thread structure (need to defer until after context switch)

        _threadCount--;
        _globalLock.Release();

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

        var thread = CurrentThread;
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

        _globalLock.Acquire();

        // Calculate wake time
        thread->WakeTime = APIC.TickCount + milliseconds;  // 1ms per tick
        thread->State = ThreadState.Blocked;
        thread->Alertable = alertable;
        thread->WaitResult = 0;  // Will be set to IoCompletion if woken by APC

        _globalLock.Release();

        Schedule();

        // We've been woken up - check why
        _globalLock.Acquire();
        thread->Alertable = false;
        uint result = thread->WaitResult;
        thread->WaitResult = 0;
        _globalLock.Release();

        // If woken by APC, deliver all pending APCs
        if (result == WaitResult.IoCompletion)
        {
            DeliverApcs();
        }

        return result;
    }

    /// <summary>
    /// Add a thread to the ready queue.
    /// Uses per-CPU queue when SMP is enabled, otherwise BSP queue.
    /// </summary>
    private static void AddToReadyQueue(Thread* thread)
    {
        thread->NextReady = null;

        if (_smpEnabled && PerCpu.IsInitialized)
        {
            // SMP mode: Add to current CPU's queue (or preferred CPU)
            var perCpu = PerCpu.Current;
            perCpu->SchedulerLock.Acquire();

            thread->PrevReady = perCpu->ReadyQueueTail;

            if (perCpu->ReadyQueueTail != null)
            {
                perCpu->ReadyQueueTail->NextReady = thread;
            }
            else
            {
                perCpu->ReadyQueueHead = thread;
            }
            perCpu->ReadyQueueTail = thread;
            perCpu->ReadyQueueCount++;

            perCpu->SchedulerLock.Release();
        }
        else
        {
            // BSP-only mode
            thread->PrevReady = _bspReadyQueueTail;

            if (_bspReadyQueueTail != null)
            {
                _bspReadyQueueTail->NextReady = thread;
            }
            else
            {
                _bspReadyQueueHead = thread;
            }
            _bspReadyQueueTail = thread;
        }
    }

    /// <summary>
    /// Remove a thread from its current ready queue.
    /// Handles both SMP and non-SMP modes.
    /// </summary>
    private static void RemoveFromReadyQueue(Thread* thread)
    {
        if (_smpEnabled && PerCpu.IsInitialized)
        {
            // SMP mode: Remove from per-CPU queue
            var perCpu = PerCpu.Current;
            perCpu->SchedulerLock.Acquire();

            if (thread->PrevReady != null)
                thread->PrevReady->NextReady = thread->NextReady;
            else
                perCpu->ReadyQueueHead = thread->NextReady;

            if (thread->NextReady != null)
                thread->NextReady->PrevReady = thread->PrevReady;
            else
                perCpu->ReadyQueueTail = thread->PrevReady;

            perCpu->ReadyQueueCount--;
            perCpu->SchedulerLock.Release();
        }
        else
        {
            // BSP-only mode
            if (thread->PrevReady != null)
                thread->PrevReady->NextReady = thread->NextReady;
            else
                _bspReadyQueueHead = thread->NextReady;

            if (thread->NextReady != null)
                thread->NextReady->PrevReady = thread->PrevReady;
            else
                _bspReadyQueueTail = thread->PrevReady;
        }

        thread->NextReady = null;
        thread->PrevReady = null;
    }

    /// <summary>
    /// Allocate extended processor state area (FPU/SSE/AVX) for a thread.
    /// The area must be 64-byte aligned for XSAVE or 16-byte aligned for FXSAVE.
    /// </summary>
    private static void AllocateExtendedState(Thread* thread)
    {
        uint size = CPUFeatures.ExtendedStateSize;
        if (size == 0)
        {
            // CPU features not initialized yet or no SSE support
            thread->ExtendedState = null;
            thread->ExtendedStateSize = 0;
            return;
        }

        // Allocate with 64-byte alignment (XSAVE requirement, also covers FXSAVE's 16-byte)
        // We allocate extra bytes to ensure alignment
        ulong rawPtr = (ulong)HeapAllocator.AllocZeroed(size + 64);
        if (rawPtr == 0)
        {
            DebugConsole.WriteLine("[Sched] WARNING: Failed to allocate extended state area!");
            thread->ExtendedState = null;
            thread->ExtendedStateSize = 0;
            return;
        }

        // Align to 64-byte boundary
        ulong alignedPtr = (rawPtr + 63) & ~63UL;
        thread->ExtendedState = (byte*)alignedPtr;
        thread->ExtendedStateSize = size;

        // Initialize the XSAVE header for XRSTOR (all zeros is valid initial state)
        // The XSAVE area is already zeroed from AllocZeroed
    }

    /// <summary>
    /// Pick the next thread to run.
    /// Called from timer interrupt or yield.
    /// </summary>
    public static void Schedule()
    {
        if (!_initialized)
            return;

        if (_smpEnabled && PerCpu.IsInitialized)
        {
            ScheduleSmp();
        }
        else
        {
            ScheduleBsp();
        }
    }

    /// <summary>
    /// Schedule for BSP-only mode (before SMP init)
    /// </summary>
    private static void ScheduleBsp()
    {
        _globalLock.Acquire();

        var current = _bspCurrentThread;

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
        var next = _bspReadyQueueHead;
        if (next == null)
        {
            // No ready threads - continue with current or idle
            if (current != null && current->State == ThreadState.Ready)
            {
                RemoveFromReadyQueue(current);
                current->State = ThreadState.Running;
                _globalLock.Release();
                return;
            }
            // No runnable threads at all - this shouldn't happen
            _globalLock.Release();
            return;
        }

        // Remove from ready queue
        RemoveFromReadyQueue(next);
        next->State = ThreadState.Running;

        // Context switch if different thread
        if (next != current)
        {
            var oldThread = current;
            _bspCurrentThread = next;

            _globalLock.Release();

            // Perform context switch with FPU/SSE/AVX state
            if (oldThread != null)
            {
                if (oldThread->ExtendedState != null)
                    CPU.SaveExtendedState(oldThread->ExtendedState);

                if (next->ExtendedState != null)
                    CPU.RestoreExtendedState(next->ExtendedState);

                CPU.SwitchContext(&oldThread->Context, &next->Context);
            }
            else
            {
                if (next->ExtendedState != null)
                    CPU.RestoreExtendedState(next->ExtendedState);
                CPU.LoadContext(&next->Context);
            }
        }
        else
        {
            _globalLock.Release();
        }
    }

    /// <summary>
    /// Schedule for SMP mode (per-CPU queues with work stealing)
    /// </summary>
    private static void ScheduleSmp()
    {
        var perCpu = PerCpu.Current;
        perCpu->SchedulerLock.Acquire();

        var current = perCpu->CurrentThread;

        // Wake up any sleeping threads whose time has come (check global list)
        _globalLock.Acquire();
        ulong now = APIC.TickCount;
        for (var t = _allThreadsHead; t != null; t = t->NextAll)
        {
            if (t->State == ThreadState.Blocked &&
                t->WakeTime > 0 &&
                t->WakeTime <= now)
            {
                t->WakeTime = 0;
                t->State = ThreadState.Ready;
                // Add to appropriate CPU's queue (prefer last CPU for cache)
                // For now, add to current CPU's queue
                t->PrevReady = perCpu->ReadyQueueTail;
                t->NextReady = null;
                if (perCpu->ReadyQueueTail != null)
                    perCpu->ReadyQueueTail->NextReady = t;
                else
                    perCpu->ReadyQueueHead = t;
                perCpu->ReadyQueueTail = t;
                perCpu->ReadyQueueCount++;
            }
        }
        _globalLock.Release();

        // If current thread is still running, move it to ready queue
        if (current != null && current->State == ThreadState.Running)
        {
            current->State = ThreadState.Ready;
            current->PrevReady = perCpu->ReadyQueueTail;
            current->NextReady = null;
            if (perCpu->ReadyQueueTail != null)
                perCpu->ReadyQueueTail->NextReady = current;
            else
                perCpu->ReadyQueueHead = current;
            perCpu->ReadyQueueTail = current;
            perCpu->ReadyQueueCount++;
        }

        // Pick next thread from local ready queue
        var next = perCpu->ReadyQueueHead;

        // Try work stealing if local queue is empty
        if (next == null)
        {
            next = StealWork(perCpu);
        }

        // Fall back to idle thread
        if (next == null)
        {
            next = perCpu->IdleThread;
            if (next == null)
            {
                // No idle thread yet - continue with current
                if (current != null && current->State == ThreadState.Ready)
                {
                    // Remove from queue and continue
                    perCpu->ReadyQueueHead = current->NextReady;
                    if (perCpu->ReadyQueueHead == null)
                        perCpu->ReadyQueueTail = null;
                    else
                        perCpu->ReadyQueueHead->PrevReady = null;
                    perCpu->ReadyQueueCount--;
                    current->NextReady = null;
                    current->PrevReady = null;
                    current->State = ThreadState.Running;
                }
                perCpu->SchedulerLock.Release();
                return;
            }
        }
        else
        {
            // Remove from local queue
            perCpu->ReadyQueueHead = next->NextReady;
            if (perCpu->ReadyQueueHead == null)
                perCpu->ReadyQueueTail = null;
            else
                perCpu->ReadyQueueHead->PrevReady = null;
            perCpu->ReadyQueueCount--;
            next->NextReady = null;
            next->PrevReady = null;
        }

        next->State = ThreadState.Running;
        next->LastCpu = perCpu->CpuIndex;

        // Context switch if different thread
        if (next != current)
        {
            var oldThread = current;
            perCpu->CurrentThread = next;

            PerCpu.IncrementContextSwitchCount();
            perCpu->SchedulerLock.Release();

            // Perform context switch with FPU/SSE/AVX state
            if (oldThread != null)
            {
                if (oldThread->ExtendedState != null)
                    CPU.SaveExtendedState(oldThread->ExtendedState);

                if (next->ExtendedState != null)
                    CPU.RestoreExtendedState(next->ExtendedState);

                CPU.SwitchContext(&oldThread->Context, &next->Context);
            }
            else
            {
                if (next->ExtendedState != null)
                    CPU.RestoreExtendedState(next->ExtendedState);
                CPU.LoadContext(&next->Context);
            }
        }
        else
        {
            perCpu->SchedulerLock.Release();
        }
    }

    /// <summary>
    /// Try to steal work from another CPU's queue
    /// </summary>
    private static Thread* StealWork(PerCpuState* thisCpu)
    {
        int cpuCount = PerCpu.CpuCount;
        if (cpuCount <= 1)
            return null;

        // Round-robin through other CPUs
        for (int i = 1; i < cpuCount; i++)
        {
            int targetIndex = ((int)thisCpu->CpuIndex + i) % cpuCount;
            var targetCpu = PerCpu.GetCpu(targetIndex);
            if (targetCpu == null)
                continue;

            // Try to acquire target's lock
            if (!targetCpu->SchedulerLock.TryAcquire())
                continue;

            // Steal from tail (opposite end from local dequeue)
            var stolen = targetCpu->ReadyQueueTail;
            if (stolen != null)
            {
                // Remove from target queue
                if (stolen->PrevReady != null)
                    stolen->PrevReady->NextReady = null;
                else
                    targetCpu->ReadyQueueHead = null;
                targetCpu->ReadyQueueTail = stolen->PrevReady;
                targetCpu->ReadyQueueCount--;
                targetCpu->WorkStolenCount++;

                stolen->NextReady = null;
                stolen->PrevReady = null;

                targetCpu->SchedulerLock.Release();

                thisCpu->WorkStealCount++;
                return stolen;
            }

            targetCpu->SchedulerLock.Release();
        }

        return null;
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

        _globalLock.Acquire();

        if (thread->State == ThreadState.Ready)
        {
            // Already in ready queue
            _globalLock.Release();
            return;
        }

        thread->State = ThreadState.Ready;
        AddToReadyQueue(thread);

        // In SMP mode, send IPI to wake target CPU if it's idle
        if (_smpEnabled && PerCpu.IsInitialized)
        {
            // Could send reschedule IPI here if thread has affinity to another CPU
            // For now, we just add to local queue - work stealing handles load balancing
        }

        _globalLock.Release();
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
        var thread = CurrentThread;
        if (!_initialized || thread == null)
            return 0;
        return thread->Id;
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

        _globalLock.Acquire();

        // Can't queue to terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _globalLock.Release();
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

        _globalLock.Release();
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
        var thread = CurrentThread;
        if (!_initialized || thread == null)
            return 0;

        int count = 0;

        // Process APCs until queue is empty
        while (true)
        {
            _globalLock.Acquire();

            var apc = thread->APCQueueHead;
            if (apc == null)
            {
                _globalLock.Release();
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

            _globalLock.Release();

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
    /// Caller should acquire GlobalLock before iterating.
    /// </summary>
    public static Thread* AllThreadsHead => _allThreadsHead;

    /// <summary>
    /// Get the global scheduler lock for safe thread enumeration.
    /// </summary>
    public static ref SpinLock GlobalLock => ref _globalLock;

    /// <summary>
    /// Get the global scheduler lock (legacy name for compatibility).
    /// </summary>
    public static ref SpinLock SchedulerLock => ref _globalLock;

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

        _globalLock.Acquire();

        // Can't suspend terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _globalLock.Release();
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
            if (thread == CurrentThread)
            {
                _globalLock.Release();
                Schedule();
                return previousCount;
            }
        }

        _globalLock.Release();
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

        _globalLock.Acquire();

        // Can't resume terminated threads
        if (thread->State == ThreadState.Terminated)
        {
            _globalLock.Release();
            return -1;
        }

        // Can't resume if not suspended
        if (thread->SuspendCount == 0)
        {
            _globalLock.Release();
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

        _globalLock.Release();
        return previousCount;
    }
}
