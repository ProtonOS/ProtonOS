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

    // SRW lock test variables
    private static KernelSRWLock _testSRW;
    private static int _srwSharedData;
    private static int _srwReaderCount;
    private static bool _srwWriterDone;

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

        // Initialize SRW lock for testing
        fixed (KernelSRWLock* srwPtr = &_testSRW)
        {
            KernelSRWLockOps.InitializeSRWLock(srwPtr);
        }
        _srwSharedData = 0;
        _srwReaderCount = 0;
        _srwWriterDone = false;
        DebugConsole.WriteLine("[Test] Initialized SRW lock");

        // Create test threads
        uint id1, id2, id3, id4, id5, id6, id7, id8, id9;
        var thread1 = KernelScheduler.CreateThread(&TestThread1, null, 0, 0, out id1);
        var thread2 = KernelScheduler.CreateThread(&TestThread2, null, 0, 0, out id2);
        var thread3 = KernelScheduler.CreateThread(&SyncTestWaiter, null, 0, 0, out id3);
        var thread4 = KernelScheduler.CreateThread(&TlsTestThread, null, 0, 0, out id4);
        var thread5 = KernelScheduler.CreateThread(&CondVarTestConsumer, null, 0, 0, out id5);
        var thread6 = KernelScheduler.CreateThread(&SRWTestWriter, null, 0, 0, out id6);
        var thread7 = KernelScheduler.CreateThread(&SRWTestReader, (void*)1, 0, 0, out id7);
        var thread8 = KernelScheduler.CreateThread(&SRWTestReader, (void*)2, 0, 0, out id8);
        var thread9 = KernelScheduler.CreateThread(&MemoryTestThread, null, 0, 0, out id9);

        if (thread1 != null && thread2 != null && thread3 != null && thread4 != null &&
            thread5 != null && thread6 != null && thread7 != null && thread8 != null && thread9 != null)
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
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id6);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id7);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id8);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id9);
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

    /// <summary>
    /// SRW lock writer test - acquires exclusive lock and updates shared data
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SRWTestWriter(void* param)
    {
        DebugConsole.WriteLine("[SRW Writer] Starting SRW lock writer test...");

        fixed (KernelSRWLock* srwPtr = &_testSRW)
        {
            for (int i = 0; i < 3; i++)
            {
                // Busy wait a bit before acquiring
                for (ulong j = 0; j < 3_000_000; j++)
                {
                    Cpu.Pause();
                }

                DebugConsole.WriteLine("[SRW Writer] Acquiring exclusive lock...");
                KernelSRWLockOps.AcquireSRWLockExclusive(srwPtr);

                // Check no readers are active (they shouldn't be while we hold exclusive)
                int readers = _srwReaderCount;
                if (readers != 0)
                {
                    DebugConsole.Write("[SRW Writer] ERROR: Readers active during write: ");
                    DebugConsole.WriteHex((ushort)readers);
                    DebugConsole.WriteLine();
                }

                _srwSharedData++;
                DebugConsole.Write("[SRW Writer] Wrote value: ");
                DebugConsole.WriteHex((ushort)_srwSharedData);
                DebugConsole.WriteLine();

                // Hold lock briefly
                for (ulong j = 0; j < 1_000_000; j++)
                {
                    Cpu.Pause();
                }

                KernelSRWLockOps.ReleaseSRWLockExclusive(srwPtr);
                DebugConsole.WriteLine("[SRW Writer] Released exclusive lock");
            }
        }

        _srwWriterDone = true;
        DebugConsole.WriteLine("[SRW Writer] Done");
        return 0;
    }

    /// <summary>
    /// SRW lock reader test - acquires shared lock and reads shared data
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SRWTestReader(void* param)
    {
        int readerId = (int)(ulong)param;
        DebugConsole.Write("[SRW Reader ");
        DebugConsole.WriteHex((ushort)readerId);
        DebugConsole.WriteLine("] Starting SRW lock reader test...");

        fixed (KernelSRWLock* srwPtr = &_testSRW)
        {
            int readCount = 0;
            while (!_srwWriterDone || readCount < 3)
            {
                // Busy wait a bit before acquiring
                for (ulong j = 0; j < 2_000_000; j++)
                {
                    Cpu.Pause();
                }

                KernelSRWLockOps.AcquireSRWLockShared(srwPtr);

                // Increment reader count atomically
                Cpu.AtomicIncrement(ref _srwReaderCount);

                int value = _srwSharedData;
                DebugConsole.Write("[SRW Reader ");
                DebugConsole.WriteHex((ushort)readerId);
                DebugConsole.Write("] Read value: ");
                DebugConsole.WriteHex((ushort)value);
                DebugConsole.Write(" (");
                DebugConsole.WriteHex((ushort)_srwReaderCount);
                DebugConsole.WriteLine(" readers active)");

                // Hold lock briefly
                for (ulong j = 0; j < 500_000; j++)
                {
                    Cpu.Pause();
                }

                // Decrement reader count atomically
                Cpu.AtomicDecrement(ref _srwReaderCount);

                KernelSRWLockOps.ReleaseSRWLockShared(srwPtr);
                readCount++;

                if (readCount >= 5)
                    break;
            }
        }

        DebugConsole.Write("[SRW Reader ");
        DebugConsole.WriteHex((ushort)readerId);
        DebugConsole.WriteLine("] SRW test SUCCESS!");
        return 0;
    }

    /// <summary>
    /// Memory test thread - tests HeapAlloc/HeapFree and VirtualAlloc/VirtualFree
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint MemoryTestThread(void* param)
    {
        DebugConsole.WriteLine("[Memory Test] Starting memory API tests...");

        // Test 1: HeapAlloc/HeapFree
        var heap = KernelMemoryOps.GetProcessHeap();
        if (heap == null)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: GetProcessHeap returned null");
            return 1;
        }

        DebugConsole.Write("[Memory Test] Process heap at 0x");
        DebugConsole.WriteHex((ulong)heap);
        DebugConsole.WriteLine();

        // Allocate some memory
        byte* ptr1 = (byte*)KernelMemoryOps.HeapAlloc(heap, 0, 256);
        if (ptr1 == null)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: HeapAlloc returned null");
            return 1;
        }

        DebugConsole.Write("[Memory Test] HeapAlloc(256) = 0x");
        DebugConsole.WriteHex((ulong)ptr1);
        DebugConsole.WriteLine();

        // Write some data
        for (int i = 0; i < 256; i++)
            ptr1[i] = (byte)i;

        // Verify data
        bool dataOk = true;
        for (int i = 0; i < 256; i++)
        {
            if (ptr1[i] != (byte)i)
            {
                dataOk = false;
                break;
            }
        }

        if (!dataOk)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: Data verification failed");
            return 1;
        }

        DebugConsole.WriteLine("[Memory Test] HeapAlloc data write/read OK");

        // Allocate with zero flag
        byte* ptr2 = (byte*)KernelMemoryOps.HeapAlloc(heap, HeapFlags.HEAP_ZERO_MEMORY, 128);
        if (ptr2 == null)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: HeapAlloc(ZERO) returned null");
            return 1;
        }

        // Verify zeroed
        bool isZeroed = true;
        for (int i = 0; i < 128; i++)
        {
            if (ptr2[i] != 0)
            {
                isZeroed = false;
                break;
            }
        }

        if (!isZeroed)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: HEAP_ZERO_MEMORY not zeroed");
            return 1;
        }

        DebugConsole.WriteLine("[Memory Test] HeapAlloc HEAP_ZERO_MEMORY OK");

        // Free memory
        KernelMemoryOps.HeapFree(heap, 0, ptr1);
        KernelMemoryOps.HeapFree(heap, 0, ptr2);
        DebugConsole.WriteLine("[Memory Test] HeapFree OK");

        // Test 2: VirtualAlloc/VirtualFree
        void* vaddr = KernelMemoryOps.VirtualAlloc(
            null,
            4096,
            MemoryAllocationType.MEM_COMMIT | MemoryAllocationType.MEM_RESERVE,
            MemoryProtection.PAGE_READWRITE);

        if (vaddr == null)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualAlloc returned null");
            return 1;
        }

        DebugConsole.Write("[Memory Test] VirtualAlloc(4096) = 0x");
        DebugConsole.WriteHex((ulong)vaddr);
        DebugConsole.WriteLine();

        // Write to virtual memory
        byte* vptr = (byte*)vaddr;
        for (int i = 0; i < 4096; i++)
            vptr[i] = (byte)(i & 0xFF);

        // Verify
        bool vdataOk = true;
        for (int i = 0; i < 4096; i++)
        {
            if (vptr[i] != (byte)(i & 0xFF))
            {
                vdataOk = false;
                break;
            }
        }

        if (!vdataOk)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualAlloc data verification failed");
            return 1;
        }

        DebugConsole.WriteLine("[Memory Test] VirtualAlloc data write/read OK");

        // Free virtual memory
        if (!KernelMemoryOps.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE))
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualFree returned false");
            return 1;
        }

        DebugConsole.WriteLine("[Memory Test] VirtualFree OK");
        DebugConsole.WriteLine("[Memory Test] All memory tests PASSED!");
        return 0;
    }
}
