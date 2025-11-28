// netos mernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls zerolib's EfiMain, which calls Main()

using System.Runtime.InteropServices;
using Mernel.X64;

namespace Mernel;

public static unsafe class Mernel
{
    public static void Main()
    {
        DebugConsole.Init();
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  netos mernel booted!");
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine();

        // Verify we have access to UEFI system table
        var systemTable = UefiBoot.SystemTable;
        DebugConsole.Write("[UEFI] SystemTable at 0x");
        DebugConsole.WriteHex((ulong)systemTable);
        if (systemTable != null && UefiBoot.BootServicesAvailable)
        {
            DebugConsole.Write(" BootServices at 0x");
            DebugConsole.WriteHex((ulong)systemTable->BootServices);
        }
        DebugConsole.WriteLine();

        // Initialize page allocator (requires UEFI boot services)
        PageAllocator.Init();

        // Initialize ACPI (requires UEFI - must be before ExitBootServices)
        Acpi.Init();

        // Exit UEFI boot services - we now own the hardware
        UefiBoot.ExitBootServices();

        // Initialize architecture-specific code (GDT, IDT, virtual memory)
#if ARCH_X64
        Arch.Init();
#elif ARCH_ARM64
        // TODO: Arch.Init();
#endif

        // Initialize kernel heap
        HeapAllocator.Init();

        // Initialize scheduler (creates boot thread)
        KernelScheduler.Init();

        // Initialize Thread Local Storage
        KernelTls.Init();

        // Second-stage arch init (timers, enable interrupts)
#if ARCH_X64
        Arch.InitStage2();
#endif

        // Create test threads to demonstrate scheduling
        CreateTestThreads();

        // Enable preemptive scheduling
        KernelScheduler.EnableScheduling();

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
        DebugConsole.WriteLine("[OK] Boot thread entering idle loop...");

        // Boot thread becomes idle thread - wait for interrupts
        while (true)
        {
            Cpu.Halt();
        }
    }

    // Shared event for synchronization test
    private static KernelEvent* _testEvent;

    // Shared TLS slot for testing
    private static uint _testTlsSlot;

    // Shared critical section and condition variable for testing
    private static KernelCriticalSection _testCS;
    private static KernelConditionVariable _testCV;
    private static int _sharedCounter;
    private static bool _producerDone;

    /// <summary>
    /// Create test threads to demonstrate scheduling and synchronization
    /// </summary>
    private static void CreateTestThreads()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Test] Creating test threads...");

        // Create an auto-reset event for synchronization test
        _testEvent = KernelSync.CreateEvent(false, false);  // Auto-reset, initially non-signaled
        if (_testEvent != null)
        {
            DebugConsole.WriteLine("[Test] Created auto-reset event");
        }

        // Allocate TLS slot for testing
        _testTlsSlot = KernelTls.TlsAlloc();
        if (_testTlsSlot != 0xFFFFFFFF)
        {
            DebugConsole.Write("[Test] Allocated TLS slot: ");
            DebugConsole.WriteHex((ushort)_testTlsSlot);
            DebugConsole.WriteLine();
        }
        else
        {
            DebugConsole.WriteLine("[Test] FAILED to allocate TLS slot!");
        }

        // Initialize critical section and condition variable
        fixed (KernelCriticalSection* csPtr = &_testCS)
        fixed (KernelConditionVariable* cvPtr = &_testCV)
        {
            KernelCriticalSectionOps.InitializeCriticalSectionAndSpinCount(csPtr, 1000);
            KernelConditionVariableOps.InitializeConditionVariable(cvPtr);
        }
        _sharedCounter = 0;
        _producerDone = false;
        DebugConsole.WriteLine("[Test] Initialized critical section and condition variable");

        // Create test threads
        uint id1, id2, id3, id4, id5;
        var thread1 = KernelScheduler.CreateThread(&TestThread1, null, 0, 0, out id1);
        var thread2 = KernelScheduler.CreateThread(&TestThread2, null, 0, 0, out id2);
        var thread3 = KernelScheduler.CreateThread(&SyncTestWaiter, null, 0, 0, out id3);
        var thread4 = KernelScheduler.CreateThread(&TlsTestThread, null, 0, 0, out id4);
        var thread5 = KernelScheduler.CreateThread(&CondVarTestConsumer, null, 0, 0, out id5);

        if (thread1 != null && thread2 != null && thread3 != null && thread4 != null && thread5 != null)
        {
            DebugConsole.Write("[Test] Created threads ");
            DebugConsole.WriteHex((ushort)id1);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id2);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id3);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id4);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id5);
            DebugConsole.WriteLine();
        }
    }

    /// <summary>
    /// Test thread 1 - prints 'A' periodically, then signals event
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint TestThread1(void* param)
    {
        uint count = 0;
        while (count < 5)
        {
            DebugConsole.Write("A");
            count++;

            // Busy wait to consume time slice
            for (ulong i = 0; i < 10_000_000; i++)
            {
                Cpu.Pause();
            }
        }

        // Signal the event to wake the waiter
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Thread1] Signaling event");
        KernelSync.SetEvent(_testEvent);

        DebugConsole.WriteLine("[Thread1] Done");
        return 0;
    }

    /// <summary>
    /// Test thread 2 - prints 'B' periodically
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint TestThread2(void* param)
    {
        uint count = 0;
        while (count < 5)
        {
            DebugConsole.Write("B");
            count++;

            // Busy wait to consume time slice
            for (ulong i = 0; i < 10_000_000; i++)
            {
                Cpu.Pause();
            }
        }

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Thread2] Done");
        return 0;
    }

    /// <summary>
    /// Test thread 3 - waits for event before printing
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SyncTestWaiter(void* param)
    {
        DebugConsole.WriteLine("[Waiter] Waiting for event...");

        // Wait for the event (infinite timeout)
        uint result = KernelSync.WaitForSingleObject(_testEvent, 0xFFFFFFFF);

        if (result == KernelWaitResult.Object0)
        {
            DebugConsole.WriteLine("[Waiter] Event received! Sync works!");
        }
        else
        {
            DebugConsole.Write("[Waiter] Wait failed: ");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
        }

        return 0;
    }

    /// <summary>
    /// Test thread 4 - tests TLS (Thread Local Storage)
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint TlsTestThread(void* param)
    {
        DebugConsole.WriteLine("[TLS Test] Starting TLS test...");

        // Store thread ID in TLS slot
        uint threadId = KernelScheduler.GetCurrentThreadId();
        bool setResult = KernelTls.TlsSetValue(_testTlsSlot, (void*)(ulong)threadId);
        if (!setResult)
        {
            DebugConsole.WriteLine("[TLS Test] FAILED to set TLS value!");
            return 1;
        }

        // Read it back
        void* value = KernelTls.TlsGetValue(_testTlsSlot);
        uint readBack = (uint)(ulong)value;

        if (readBack == threadId)
        {
            DebugConsole.Write("[TLS Test] SUCCESS! Stored and retrieved thread ID: ");
            DebugConsole.WriteHex((ushort)readBack);
            DebugConsole.WriteLine();
        }
        else
        {
            DebugConsole.Write("[TLS Test] FAILED! Expected ");
            DebugConsole.WriteHex((ushort)threadId);
            DebugConsole.Write(" but got ");
            DebugConsole.WriteHex((ushort)readBack);
            DebugConsole.WriteLine();
            return 1;
        }

        // Now act as producer for condition variable test
        DebugConsole.WriteLine("[TLS Test] Acting as producer for CondVar test...");

        fixed (KernelCriticalSection* csPtr = &_testCS)
        fixed (KernelConditionVariable* cvPtr = &_testCV)
        {
            for (int i = 0; i < 3; i++)
            {
                // Busy wait a bit
                for (ulong j = 0; j < 5_000_000; j++)
                {
                    Cpu.Pause();
                }

                // Enter critical section, update counter, signal consumer
                KernelCriticalSectionOps.EnterCriticalSection(csPtr);
                _sharedCounter++;
                DebugConsole.Write("[Producer] Counter = ");
                DebugConsole.WriteHex((ushort)_sharedCounter);
                DebugConsole.WriteLine();
                KernelConditionVariableOps.WakeConditionVariable(cvPtr);
                KernelCriticalSectionOps.LeaveCriticalSection(csPtr);
            }

            // Signal done
            KernelCriticalSectionOps.EnterCriticalSection(csPtr);
            _producerDone = true;
            KernelConditionVariableOps.WakeConditionVariable(cvPtr);
            KernelCriticalSectionOps.LeaveCriticalSection(csPtr);
        }

        DebugConsole.WriteLine("[TLS Test/Producer] Done");
        return 0;
    }

    /// <summary>
    /// Test thread 5 - tests Condition Variable (consumer)
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint CondVarTestConsumer(void* param)
    {
        DebugConsole.WriteLine("[Consumer] Starting CondVar consumer...");

        int lastSeen = 0;

        fixed (KernelCriticalSection* csPtr = &_testCS)
        fixed (KernelConditionVariable* cvPtr = &_testCV)
        {
            while (true)
            {
                KernelCriticalSectionOps.EnterCriticalSection(csPtr);

                // Wait for counter to change or producer to finish
                while (_sharedCounter == lastSeen && !_producerDone)
                {
                    DebugConsole.WriteLine("[Consumer] Waiting on condition variable...");
                    bool woken = KernelConditionVariableOps.SleepConditionVariableCS(cvPtr, csPtr, 0xFFFFFFFF);
                    if (!woken)
                    {
                        DebugConsole.WriteLine("[Consumer] Timeout waiting (unexpected)");
                    }
                }

                if (_sharedCounter != lastSeen)
                {
                    lastSeen = _sharedCounter;
                    DebugConsole.Write("[Consumer] Saw counter update: ");
                    DebugConsole.WriteHex((ushort)lastSeen);
                    DebugConsole.WriteLine();
                }

                if (_producerDone && _sharedCounter == lastSeen)
                {
                    KernelCriticalSectionOps.LeaveCriticalSection(csPtr);
                    break;
                }

                KernelCriticalSectionOps.LeaveCriticalSection(csPtr);
            }
        }

        DebugConsole.WriteLine("[Consumer] CondVar test SUCCESS!");
        return 0;
    }
}
