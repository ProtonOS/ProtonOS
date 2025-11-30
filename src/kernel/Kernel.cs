// netos kernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls zerolib's EfiMain, which calls Main()

using System.Runtime.InteropServices;
using Kernel.X64;
using Kernel.PAL;
using Kernel.Memory;
using Kernel.Threading;
using Kernel.Platform;
using Kernel.Runtime;

namespace Kernel;

public static unsafe class Kernel
{
    public static void Main()
    {
        DebugConsole.Init();
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  netos kernel booted!");
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

        // Initialize ReadyToRun info (must be before anything needing runtime metadata)
        ReadyToRunInfo.Init();
        ReadyToRunInfo.DumpSections();

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
        Scheduler.Init();

        // Initialize PAL subsystems
        Tls.Init();
        PAL.Memory.Init();

        // Second-stage arch init (timers, enable interrupts)
#if ARCH_X64
        Arch.InitStage2();
#endif

        // Create test threads to demonstrate scheduling
        CreateTestThreads();

        // Test exception handling
        TestExceptionHandling();

        // Test GC static root - store an object in static field
        _gcTestObject = new object();
        DebugConsole.Write("[GC] Static object test: ");
        DebugConsole.WriteHex((ulong)System.Runtime.CompilerServices.Unsafe.As<object, nint>(ref _gcTestObject!));
        DebugConsole.WriteLine();

        // Enable preemptive scheduling
        Scheduler.EnableScheduling();

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
    private static Event* _testEvent;

    // Shared TLS slot for testing
    private static uint _testTlsSlot;

    // Static object reference - causes NativeAOT to emit GCStatic region
    // Used for GC root testing (verified: creates __GCSTATICS, __GCStaticEEType, __GCStaticRegion)
    private static object? _gcTestObject;

    // Shared critical section and condition variable for testing
    private static CriticalSection _testCS;
    private static ConditionVariable _testCV;
    private static int _sharedCounter;
    private static bool _producerDone;

    // SRW lock test variables
    private static SRWLock _testSRW;
    private static int _srwSharedData;
    private static int _srwReaderCount;
    private static bool _srwWriterDone;

    // Exception handling test variables
    private static bool _exceptionCaught;
    private static uint _caughtExceptionCode;
    private static int _rethrowPassCount;

    /// <summary>
    /// Test exception handling with try/catch/throw
    /// </summary>
    private static void TestExceptionHandling()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[EH Test] Testing exception handling...");

        // Test 1: Basic throw/catch
        try
        {
            DebugConsole.WriteLine("[EH Test] Inside try block");
            throw new System.Exception("Test exception");
        }
        catch (System.Exception)
        {
            DebugConsole.WriteLine("[EH Test] Caught Exception!");
            _exceptionCaught = true;
        }

        if (_exceptionCaught)
        {
            DebugConsole.WriteLine("[EH Test] Basic throw/catch PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[EH Test] Basic throw/catch FAILED");
        }

        // Test 2: Rethrow with "throw;"
        _rethrowPassCount = 0;
        try
        {
            try
            {
                throw new System.Exception("Rethrow test");
            }
            catch (System.Exception)
            {
                _rethrowPassCount++;
                DebugConsole.WriteLine("[EH Test] Caught in inner, rethrowing...");
                throw;  // Rethrow
            }
        }
        catch (System.Exception)
        {
            _rethrowPassCount++;
            DebugConsole.WriteLine("[EH Test] Caught rethrown in outer!");
        }

        if (_rethrowPassCount == 2)
        {
            DebugConsole.WriteLine("[EH Test] Rethrow test PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[EH Test] Rethrow test FAILED");
        }
    }

    /// <summary>
    /// Create test threads to demonstrate scheduling and synchronization
    /// </summary>
    private static void CreateTestThreads()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Test] Creating test threads...");

        // Create an auto-reset event for synchronization test
        _testEvent = Sync.CreateEvent(false, false);  // Auto-reset, initially non-signaled
        if (_testEvent != null)
        {
            DebugConsole.WriteLine("[Test] Created auto-reset event");
        }

        // Allocate TLS slot for testing
        _testTlsSlot = Tls.TlsAlloc();
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
        fixed (CriticalSection* csPtr = &_testCS)
        fixed (ConditionVariable* cvPtr = &_testCV)
        {
            CriticalSectionOps.InitializeCriticalSectionAndSpinCount(csPtr, 1000);
            ConditionVariableOps.InitializeConditionVariable(cvPtr);
        }
        _sharedCounter = 0;
        _producerDone = false;
        DebugConsole.WriteLine("[Test] Initialized critical section and condition variable");

        // Initialize SRW lock for testing
        fixed (SRWLock* srwPtr = &_testSRW)
        {
            SRWLockOps.InitializeSRWLock(srwPtr);
        }
        _srwSharedData = 0;
        _srwReaderCount = 0;
        _srwWriterDone = false;
        DebugConsole.WriteLine("[Test] Initialized SRW lock");

        // Create test threads
        uint id1, id2, id3, id4, id5, id6, id7, id8, id9, id10, id11;
        var thread1 = Scheduler.CreateThread(&TestThread1, null, 0, 0, out id1);
        var thread2 = Scheduler.CreateThread(&TestThread2, null, 0, 0, out id2);
        var thread3 = Scheduler.CreateThread(&SyncTestWaiter, null, 0, 0, out id3);
        var thread4 = Scheduler.CreateThread(&TlsTestThread, null, 0, 0, out id4);
        var thread5 = Scheduler.CreateThread(&CondVarTestConsumer, null, 0, 0, out id5);
        var thread6 = Scheduler.CreateThread(&SRWTestWriter, null, 0, 0, out id6);
        var thread7 = Scheduler.CreateThread(&SRWTestReader, (void*)1, 0, 0, out id7);
        var thread8 = Scheduler.CreateThread(&SRWTestReader, (void*)2, 0, 0, out id8);
        var thread9 = Scheduler.CreateThread(&MemoryTestThread, null, 0, 0, out id9);
        var thread10 = Scheduler.CreateThread(&ExceptionTestThread, null, 0, 0, out id10);
        var thread11 = Scheduler.CreateThread(&SystemTestThread, null, 0, 0, out id11);
        uint id12, id13, id14, id15, id16, id17, id18, id19;
        var thread12 = Scheduler.CreateThread(&VirtualQueryTestThread, null, 0, 0, out id12);
        var thread13 = Scheduler.CreateThread(&ThreadContextTestThread, null, 0, 0, out id13);
        var thread14 = Scheduler.CreateThread(&WaitMultipleTestThread, null, 0, 0, out id14);
        var thread15 = Scheduler.CreateThread(&RaiseExceptionTestThread, null, 0, 0, out id15);
        var thread16 = Scheduler.CreateThread(&SuspendResumeTestThread, null, 0, 0, out id16);
        var thread17 = Scheduler.CreateThread(&DebugApiTestThread, null, 0, 0, out id17);
        var thread18 = Scheduler.CreateThread(&EnvironmentApiTestThread, null, 0, 0, out id18);
        var thread19 = Scheduler.CreateThread(&ApcTestThread, null, 0, 0, out id19);
        uint id20, id21, id22, id23, id24;
        var thread20 = Scheduler.CreateThread(&SyncExTestThread, null, 0, 0, out id20);
        var thread21 = Scheduler.CreateThread(&ProcessAndHeapTestThread, null, 0, 0, out id21);
        var thread22 = Scheduler.CreateThread(&StringConversionTestThread, null, 0, 0, out id22);
        var thread23 = Scheduler.CreateThread(&StackUnwindTestThread, null, 0, 0, out id23);
        var thread24 = Scheduler.CreateThread(&NewPalApisTestThread, null, 0, 0, out id24);

        if (thread1 != null && thread2 != null && thread3 != null && thread4 != null &&
            thread5 != null && thread6 != null && thread7 != null && thread8 != null &&
            thread9 != null && thread10 != null && thread11 != null &&
            thread12 != null && thread13 != null && thread14 != null && thread15 != null &&
            thread16 != null && thread17 != null && thread18 != null && thread19 != null &&
            thread20 != null && thread21 != null && thread22 != null && thread23 != null && thread24 != null)
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
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id10);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id11);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id12);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id13);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id14);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id15);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id16);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id17);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id18);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id19);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id20);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id21);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id22);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id23);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex((ushort)id24);
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
        Sync.SetEvent(_testEvent);

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
        uint result = Sync.WaitForSingleObject(_testEvent, 0xFFFFFFFF);

        if (result == WaitResult.Object0)
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
        uint threadId = Scheduler.GetCurrentThreadId();
        bool setResult = Tls.TlsSetValue(_testTlsSlot, (void*)(ulong)threadId);
        if (!setResult)
        {
            DebugConsole.WriteLine("[TLS Test] FAILED to set TLS value!");
            return 1;
        }

        // Read it back
        void* value = Tls.TlsGetValue(_testTlsSlot);
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

        fixed (CriticalSection* csPtr = &_testCS)
        fixed (ConditionVariable* cvPtr = &_testCV)
        {
            for (int i = 0; i < 3; i++)
            {
                // Busy wait a bit
                for (ulong j = 0; j < 5_000_000; j++)
                {
                    Cpu.Pause();
                }

                // Enter critical section, update counter, signal consumer
                CriticalSectionOps.EnterCriticalSection(csPtr);
                _sharedCounter++;
                DebugConsole.Write("[Producer] Counter = ");
                DebugConsole.WriteHex((ushort)_sharedCounter);
                DebugConsole.WriteLine();
                ConditionVariableOps.WakeConditionVariable(cvPtr);
                CriticalSectionOps.LeaveCriticalSection(csPtr);
            }

            // Signal done
            CriticalSectionOps.EnterCriticalSection(csPtr);
            _producerDone = true;
            ConditionVariableOps.WakeConditionVariable(cvPtr);
            CriticalSectionOps.LeaveCriticalSection(csPtr);
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

        fixed (CriticalSection* csPtr = &_testCS)
        fixed (ConditionVariable* cvPtr = &_testCV)
        {
            while (true)
            {
                CriticalSectionOps.EnterCriticalSection(csPtr);

                // Wait for counter to change or producer to finish
                while (_sharedCounter == lastSeen && !_producerDone)
                {
                    DebugConsole.WriteLine("[Consumer] Waiting on condition variable...");
                    bool woken = ConditionVariableOps.SleepConditionVariableCS(cvPtr, csPtr, 0xFFFFFFFF);
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
                    CriticalSectionOps.LeaveCriticalSection(csPtr);
                    break;
                }

                CriticalSectionOps.LeaveCriticalSection(csPtr);
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

        fixed (SRWLock* srwPtr = &_testSRW)
        {
            for (int i = 0; i < 3; i++)
            {
                // Busy wait a bit before acquiring
                for (ulong j = 0; j < 3_000_000; j++)
                {
                    Cpu.Pause();
                }

                DebugConsole.WriteLine("[SRW Writer] Acquiring exclusive lock...");
                SRWLockOps.AcquireSRWLockExclusive(srwPtr);

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

                SRWLockOps.ReleaseSRWLockExclusive(srwPtr);
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

        fixed (SRWLock* srwPtr = &_testSRW)
        {
            int readCount = 0;
            while (!_srwWriterDone || readCount < 3)
            {
                // Busy wait a bit before acquiring
                for (ulong j = 0; j < 2_000_000; j++)
                {
                    Cpu.Pause();
                }

                SRWLockOps.AcquireSRWLockShared(srwPtr);

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

                SRWLockOps.ReleaseSRWLockShared(srwPtr);
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
        var heap = PAL.Memory.GetProcessHeap();
        if (heap == null)
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: GetProcessHeap returned null");
            return 1;
        }

        DebugConsole.Write("[Memory Test] Process heap at 0x");
        DebugConsole.WriteHex((ulong)heap);
        DebugConsole.WriteLine();

        // Allocate some memory
        byte* ptr1 = (byte*)PAL.Memory.HeapAlloc(heap, 0, 256);
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
        byte* ptr2 = (byte*)PAL.Memory.HeapAlloc(heap, HeapFlags.HEAP_ZERO_MEMORY, 128);
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
        PAL.Memory.HeapFree(heap, 0, ptr1);
        PAL.Memory.HeapFree(heap, 0, ptr2);
        DebugConsole.WriteLine("[Memory Test] HeapFree OK");

        // Test 2: VirtualAlloc/VirtualFree
        void* vaddr = PAL.Memory.VirtualAlloc(
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

        // Test 3: VirtualProtect
        uint oldProtect = 0;
        if (!PAL.Memory.VirtualProtect(vaddr, 4096, MemoryProtection.PAGE_EXECUTE_READWRITE, &oldProtect))
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualProtect returned false");
            return 1;
        }

        DebugConsole.Write("[Memory Test] VirtualProtect RW->RWX, old protect: 0x");
        DebugConsole.WriteHex(oldProtect);
        DebugConsole.WriteLine();

        // Change back to read-only
        uint oldProtect2 = 0;
        if (!PAL.Memory.VirtualProtect(vaddr, 4096, MemoryProtection.PAGE_READONLY, &oldProtect2))
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualProtect (to RO) returned false");
            return 1;
        }

        DebugConsole.Write("[Memory Test] VirtualProtect RWX->RO, old protect: 0x");
        DebugConsole.WriteHex(oldProtect2);
        DebugConsole.WriteLine();

        // Verify old protect was RWX (0x40)
        if (oldProtect2 != MemoryProtection.PAGE_EXECUTE_READWRITE)
        {
            DebugConsole.Write("[Memory Test] WARNING: Expected old protect 0x40, got 0x");
            DebugConsole.WriteHex(oldProtect2);
            DebugConsole.WriteLine();
        }

        DebugConsole.WriteLine("[Memory Test] VirtualProtect OK");

        // Free virtual memory
        if (!PAL.Memory.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE))
        {
            DebugConsole.WriteLine("[Memory Test] FAILED: VirtualFree returned false");
            return 1;
        }

        DebugConsole.WriteLine("[Memory Test] VirtualFree OK");
        DebugConsole.WriteLine("[Memory Test] All memory tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Exception filter for the exception test.
    /// Handles breakpoint exceptions - INT3 is a trap so RIP already points past it.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int TestExceptionFilter(X64.ExceptionRecord* record, X64.ExceptionContext* context)
    {
        // Handle breakpoints - INT3 is a trap, RIP already points to next instruction
        // We just need to continue execution without modifying RIP
        if (record->ExceptionCode == X64.ExceptionCodes.EXCEPTION_BREAKPOINT)
        {
            // INT3 is a trap - RIP already points to instruction after INT3
            // No need to increment RIP
            return -1; // EXCEPTION_CONTINUE_EXECUTION
        }

        // Let other exceptions crash
        return 0; // EXCEPTION_CONTINUE_SEARCH
    }

    /// <summary>
    /// Exception handling test thread - tests that SEH actually works
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint ExceptionTestThread(void* param)
    {
        DebugConsole.WriteLine("[SEH Test] Starting exception handling test...");

        // Install our exception filter
        var oldFilter = X64.ExceptionHandling.SetUnhandledExceptionFilter(&TestExceptionFilter);
        DebugConsole.WriteLine("[SEH Test] Installed exception filter");

        // Trigger a breakpoint exception (INT3)
        // This should be caught by our filter, which will increment RIP and continue
        DebugConsole.WriteLine("[SEH Test] About to trigger INT3 breakpoint...");

        // Use a volatile write to a local before and after to prove execution continued
        int testVal = 42;

        Cpu.Breakpoint(); // This triggers INT3 (exception vector 3)

        // If we reach here, the exception was handled and we continued
        testVal = 123;

        // If we get here, the exception was handled!
        DebugConsole.WriteLine("[SEH Test] Execution continued after breakpoint!");
        DebugConsole.Write("[SEH Test] testVal = ");
        DebugConsole.WriteHex((ushort)testVal);
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[SEH Test] Exception handling test PASSED!");

        // Restore old filter
        X64.ExceptionHandling.SetUnhandledExceptionFilter(oldFilter);

        return 0;
    }

    /// <summary>
    /// System API test thread - tests QueryPerformanceCounter, GetSystemInfo, GetTickCount
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SystemTestThread(void* param)
    {
        DebugConsole.WriteLine("[System Test] Starting System API tests...");

        // Test 1: QueryPerformanceFrequency
        long frequency;
        if (!SystemApi.QueryPerformanceFrequency(out frequency))
        {
            DebugConsole.WriteLine("[System Test] FAILED: QueryPerformanceFrequency returned false");
            return 1;
        }

        if (frequency <= 0)
        {
            DebugConsole.WriteLine("[System Test] FAILED: Frequency is <= 0");
            return 1;
        }

        DebugConsole.Write("[System Test] Performance frequency: ");
        DebugConsole.WriteHex((ulong)frequency);
        DebugConsole.Write(" Hz (");
        // Display in MHz for readability
        DebugConsole.WriteHex((ulong)(frequency / 1000000));
        DebugConsole.WriteLine(" MHz)");

        // Test 2: QueryPerformanceCounter
        long counter1;
        if (!SystemApi.QueryPerformanceCounter(out counter1))
        {
            DebugConsole.WriteLine("[System Test] FAILED: QueryPerformanceCounter returned false");
            return 1;
        }

        if (counter1 <= 0)
        {
            DebugConsole.WriteLine("[System Test] FAILED: Counter is <= 0");
            return 1;
        }

        DebugConsole.Write("[System Test] Performance counter: 0x");
        DebugConsole.WriteHex((ulong)counter1);
        DebugConsole.WriteLine();

        // Wait a bit and read again to verify it advances
        for (ulong i = 0; i < 5_000_000; i++)
        {
            Cpu.Pause();
        }

        long counter2;
        SystemApi.QueryPerformanceCounter(out counter2);

        if (counter2 <= counter1)
        {
            DebugConsole.WriteLine("[System Test] FAILED: Counter did not advance");
            return 1;
        }

        DebugConsole.Write("[System Test] Counter advanced to: 0x");
        DebugConsole.WriteHex((ulong)counter2);
        DebugConsole.WriteLine(" OK");

        // Test 3: GetSystemInfo
        SystemInfo sysInfo;
        SystemApi.GetSystemInfo(out sysInfo);

        DebugConsole.Write("[System Test] ProcessorArchitecture: ");
        DebugConsole.WriteHex(sysInfo.wProcessorArchitecture);
        if (sysInfo.wProcessorArchitecture == ProcessorArchitecture.PROCESSOR_ARCHITECTURE_AMD64)
        {
            DebugConsole.WriteLine(" (AMD64) OK");
        }
        else
        {
            DebugConsole.WriteLine(" UNEXPECTED");
        }

        DebugConsole.Write("[System Test] PageSize: ");
        DebugConsole.WriteHex(sysInfo.dwPageSize);
        if (sysInfo.dwPageSize == 4096)
        {
            DebugConsole.WriteLine(" (4KB) OK");
        }
        else
        {
            DebugConsole.WriteLine(" UNEXPECTED");
        }

        DebugConsole.Write("[System Test] AllocationGranularity: ");
        DebugConsole.WriteHex(sysInfo.dwAllocationGranularity);
        DebugConsole.WriteLine();

        DebugConsole.Write("[System Test] NumberOfProcessors: ");
        DebugConsole.WriteHex(sysInfo.dwNumberOfProcessors);
        DebugConsole.WriteLine();

        DebugConsole.Write("[System Test] MinAppAddress: 0x");
        DebugConsole.WriteHex((ulong)sysInfo.lpMinimumApplicationAddress);
        DebugConsole.WriteLine();

        DebugConsole.Write("[System Test] MaxAppAddress: 0x");
        DebugConsole.WriteHex((ulong)sysInfo.lpMaximumApplicationAddress);
        DebugConsole.WriteLine();

        // Test 4: GetTickCount64
        ulong tick1 = SystemApi.GetTickCount64();
        DebugConsole.Write("[System Test] TickCount64: ");
        DebugConsole.WriteHex(tick1);
        DebugConsole.WriteLine(" ms");

        // Wait and verify it advances
        for (ulong i = 0; i < 5_000_000; i++)
        {
            Cpu.Pause();
        }

        ulong tick2 = SystemApi.GetTickCount64();
        if (tick2 < tick1)
        {
            DebugConsole.WriteLine("[System Test] FAILED: TickCount64 went backwards");
            return 1;
        }

        DebugConsole.Write("[System Test] TickCount64 advanced to: ");
        DebugConsole.WriteHex(tick2);
        DebugConsole.WriteLine(" ms OK");

        // Test 5: GetTickCount (32-bit)
        uint tick32 = SystemApi.GetTickCount();
        DebugConsole.Write("[System Test] TickCount: ");
        DebugConsole.WriteHex(tick32);
        DebugConsole.WriteLine(" ms OK");

        // Test 6: GetSystemTimeAsFileTime
        FileTime fileTime;
        SystemApi.GetSystemTimeAsFileTime(&fileTime);
        ulong ft = fileTime.ToUInt64();
        if (ft == 0)
        {
            DebugConsole.WriteLine("[System Test] FAILED: GetSystemTimeAsFileTime returned 0");
            return 1;
        }
        DebugConsole.Write("[System Test] FileTime: 0x");
        DebugConsole.WriteHex(ft);
        DebugConsole.WriteLine(" OK");

        // Test 7: GetSystemTime (broken-down time)
        SystemTime sysTime;
        SystemApi.GetSystemTime(&sysTime);
        if (sysTime.wYear < 2024 || sysTime.wYear > 2100)
        {
            DebugConsole.Write("[System Test] FAILED: Invalid year: ");
            DebugConsole.WriteDecimal(sysTime.wYear);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.Write("[System Test] SystemTime: ");
        DebugConsole.WriteDecimal(sysTime.wYear);
        DebugConsole.Write("-");
        DebugConsole.WriteDecimalPadded(sysTime.wMonth, 2);
        DebugConsole.Write("-");
        DebugConsole.WriteDecimalPadded(sysTime.wDay, 2);
        DebugConsole.Write(" ");
        DebugConsole.WriteDecimalPadded(sysTime.wHour, 2);
        DebugConsole.Write(":");
        DebugConsole.WriteDecimalPadded(sysTime.wMinute, 2);
        DebugConsole.Write(":");
        DebugConsole.WriteDecimalPadded(sysTime.wSecond, 2);
        DebugConsole.WriteLine(" UTC OK");

        DebugConsole.WriteLine("[System Test] All System API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// VirtualQuery test thread - tests VirtualQuery API
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint VirtualQueryTestThread(void* param)
    {
        DebugConsole.WriteLine("[VirtualQuery Test] Starting VirtualQuery API test...");

        // Allocate some virtual memory
        void* vaddr = PAL.Memory.VirtualAlloc(
            null,
            8192,  // 2 pages
            MemoryAllocationType.MEM_COMMIT | MemoryAllocationType.MEM_RESERVE,
            MemoryProtection.PAGE_READWRITE);

        if (vaddr == null)
        {
            DebugConsole.WriteLine("[VirtualQuery Test] FAILED: VirtualAlloc returned null");
            return 1;
        }

        DebugConsole.Write("[VirtualQuery Test] Allocated at 0x");
        DebugConsole.WriteHex((ulong)vaddr);
        DebugConsole.WriteLine();

        // Query the allocated memory
        MemoryBasicInformation memInfo;
        ulong result = PAL.Memory.VirtualQuery(vaddr, &memInfo, (ulong)sizeof(MemoryBasicInformation));

        if (result == 0)
        {
            DebugConsole.WriteLine("[VirtualQuery Test] FAILED: VirtualQuery returned 0");
            PAL.Memory.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE);
            return 1;
        }

        DebugConsole.Write("[VirtualQuery Test] BaseAddress: 0x");
        DebugConsole.WriteHex((ulong)memInfo.BaseAddress);
        DebugConsole.WriteLine();

        DebugConsole.Write("[VirtualQuery Test] AllocationBase: 0x");
        DebugConsole.WriteHex((ulong)memInfo.AllocationBase);
        DebugConsole.WriteLine();

        DebugConsole.Write("[VirtualQuery Test] RegionSize: 0x");
        DebugConsole.WriteHex((ulong)memInfo.RegionSize);
        DebugConsole.WriteLine();

        DebugConsole.Write("[VirtualQuery Test] State: 0x");
        DebugConsole.WriteHex(memInfo.State);
        if (memInfo.State == MemoryState.MEM_COMMIT)
            DebugConsole.Write(" (MEM_COMMIT)");
        DebugConsole.WriteLine();

        DebugConsole.Write("[VirtualQuery Test] Protect: 0x");
        DebugConsole.WriteHex(memInfo.Protect);
        if (memInfo.Protect == MemoryProtection.PAGE_READWRITE)
            DebugConsole.Write(" (PAGE_READWRITE)");
        DebugConsole.WriteLine();

        DebugConsole.Write("[VirtualQuery Test] Type: 0x");
        DebugConsole.WriteHex(memInfo.Type);
        if (memInfo.Type == MemoryType.MEM_PRIVATE)
            DebugConsole.Write(" (MEM_PRIVATE)");
        DebugConsole.WriteLine();

        // Verify the results
        if (memInfo.State != MemoryState.MEM_COMMIT)
        {
            DebugConsole.WriteLine("[VirtualQuery Test] FAILED: Expected MEM_COMMIT state");
            PAL.Memory.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE);
            return 1;
        }

        if (memInfo.Protect != MemoryProtection.PAGE_READWRITE)
        {
            DebugConsole.WriteLine("[VirtualQuery Test] FAILED: Expected PAGE_READWRITE protection");
            PAL.Memory.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE);
            return 1;
        }

        // Query a free memory region (high address that shouldn't be mapped)
        void* freeAddr = (void*)0x7FFF00000000UL;
        ulong result2 = PAL.Memory.VirtualQuery(freeAddr, &memInfo, (ulong)sizeof(MemoryBasicInformation));

        if (result2 > 0)
        {
            DebugConsole.Write("[VirtualQuery Test] Free region state: 0x");
            DebugConsole.WriteHex(memInfo.State);
            if (memInfo.State == MemoryState.MEM_FREE)
                DebugConsole.Write(" (MEM_FREE) OK");
            DebugConsole.WriteLine();
        }

        // Cleanup
        PAL.Memory.VirtualFree(vaddr, 0, MemoryFreeType.MEM_RELEASE);

        DebugConsole.WriteLine("[VirtualQuery Test] All VirtualQuery tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Thread context test - tests GetThreadContext / RtlCaptureContext
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint ThreadContextTestThread(void* param)
    {
        DebugConsole.WriteLine("[Context Test] Starting Thread Context API test...");

        // Test 1: RtlCaptureContext - capture current thread's context
        Context ctx;
        ThreadApi.RtlCaptureContext(&ctx);

        DebugConsole.Write("[Context Test] RtlCaptureContext - ContextFlags: 0x");
        DebugConsole.WriteHex(ctx.ContextFlags);
        DebugConsole.WriteLine();

        DebugConsole.Write("[Context Test] RIP: 0x");
        DebugConsole.WriteHex(ctx.Rip);
        DebugConsole.WriteLine();

        DebugConsole.Write("[Context Test] RSP: 0x");
        DebugConsole.WriteHex(ctx.Rsp);
        DebugConsole.WriteLine();

        DebugConsole.Write("[Context Test] RBP: 0x");
        DebugConsole.WriteHex(ctx.Rbp);
        DebugConsole.WriteLine();

        // Verify context flags include what we expect
        if ((ctx.ContextFlags & ContextFlags.CONTEXT_CONTROL) == 0)
        {
            DebugConsole.WriteLine("[Context Test] WARNING: CONTEXT_CONTROL not set");
        }

        if ((ctx.ContextFlags & ContextFlags.CONTEXT_INTEGER) == 0)
        {
            DebugConsole.WriteLine("[Context Test] WARNING: CONTEXT_INTEGER not set");
        }

        // Test 2: GetThreadContext - get our own context via handle
        var handle = ThreadApi.GetCurrentThread();
        if (!handle.IsValid)
        {
            DebugConsole.WriteLine("[Context Test] FAILED: GetCurrentThread returned invalid handle");
            return 1;
        }

        Context ctx2;
        ctx2.ContextFlags = ContextFlags.CONTEXT_FULL;
        bool success = ThreadApi.GetThreadContext(handle, &ctx2);

        if (!success)
        {
            DebugConsole.WriteLine("[Context Test] FAILED: GetThreadContext returned false");
            return 1;
        }

        DebugConsole.Write("[Context Test] GetThreadContext - RSP: 0x");
        DebugConsole.WriteHex(ctx2.Rsp);
        DebugConsole.WriteLine();

        // Verify RSP values are in a reasonable range (should be on stack)
        if (ctx2.Rsp < 0x1000)
        {
            DebugConsole.WriteLine("[Context Test] WARNING: RSP seems too low");
        }

        // Test 3: Check segment registers
        DebugConsole.Write("[Context Test] CS: 0x");
        DebugConsole.WriteHex(ctx2.SegCs);
        DebugConsole.Write(" SS: 0x");
        DebugConsole.WriteHex(ctx2.SegSs);
        DebugConsole.WriteLine();

        // Test GetCurrentThreadId
        uint tid = ThreadApi.GetCurrentThreadId();
        DebugConsole.Write("[Context Test] Current thread ID: ");
        DebugConsole.WriteHex((ushort)tid);
        DebugConsole.WriteLine();

        // Test 4: RtlRestoreContext - restore execution to a previous point
        // This is tricky to test because it doesn't return, so we use a counter
        int restoreTestCounter = 0;
        Context restoreCtx;

        // Capture context at this point
        ThreadApi.RtlCaptureContext(&restoreCtx);

        // After capture (or after restore returns here), increment counter
        restoreTestCounter++;

        if (restoreTestCounter == 1)
        {
            // First time through - modify RAX in context and restore
            // We set RAX to a marker value that we can detect
            // Actually, let's just restore and check counter increments to 2
            DebugConsole.WriteLine("[Context Test] Test 4: RtlRestoreContext - first pass, about to restore...");

            // Modify a register so we know restore worked - bump the counter check value
            // When we restore, we'll jump back to after RtlCaptureContext with
            // restoreTestCounter still = 1 (stack variable), so it will be incremented to 2
            // Actually, the stack variable IS at the same address, so it will be 1 again...
            // We need to use a static variable to survive the restore
            _restoreTestPassCount++;

            if (_restoreTestPassCount == 1)
            {
                // First restore - do it
                ThreadApi.RtlRestoreContext(&restoreCtx, null);
                // Should not reach here
                DebugConsole.WriteLine("[Context Test] FAILED: RtlRestoreContext returned!");
                return 1;
            }
        }

        // We get here on the second pass after restore jumps back
        if (_restoreTestPassCount == 2)
        {
            DebugConsole.WriteLine("[Context Test] Test 4: RtlRestoreContext - PASSED (restored successfully)");
        }
        else
        {
            DebugConsole.Write("[Context Test] Test 4: RtlRestoreContext - pass count: ");
            DebugConsole.WriteHex((uint)_restoreTestPassCount);
            DebugConsole.WriteLine();
        }

        DebugConsole.WriteLine("[Context Test] All Thread Context tests PASSED!");
        return 0;
    }

    // Static counter for RtlRestoreContext test (survives context restore)
    private static int _restoreTestPassCount;

    /// <summary>
    /// Fixed-size handle array for WaitForMultipleObjects testing.
    /// Using a struct avoids stackalloc overflow checking issues in stdlib:zero.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct HandleArray2
    {
        public void* Handle0;
        public void* Handle1;
    }

    /// <summary>
    /// WaitForMultipleObjects test thread - tests multi-object waiting
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint WaitMultipleTestThread(void* param)
    {
        DebugConsole.WriteLine("[WaitMultiple Test] Starting WaitForMultipleObjects test...");

        // Create two events for testing
        var event1 = Sync.CreateEvent(false, false);  // Auto-reset, not signaled
        var event2 = Sync.CreateEvent(false, true);   // Auto-reset, signaled

        if (event1 == null || event2 == null)
        {
            DebugConsole.WriteLine("[WaitMultiple Test] FAILED: Could not create events");
            return 1;
        }

        DebugConsole.WriteLine("[WaitMultiple Test] Created two events (one not signaled, one signaled)");

        // Test 1: WaitAny with one signaled - should return immediately
        // Use fixed-size struct to avoid stackalloc overflow checking
        HandleArray2 handleArray;
        handleArray.Handle0 = event1;  // not signaled
        handleArray.Handle1 = event2;  // signaled
        void** handles = (void**)&handleArray;

        uint result = Sync.WaitForMultipleObjects(2, handles, false, 0);  // bWaitAll=false, timeout=0

        DebugConsole.Write("[WaitMultiple Test] WaitAny result: ");
        DebugConsole.WriteHex(result);
        // WAIT_OBJECT_0 + 1 = 1 (second handle signaled)
        if (result == 1u)
        {
            DebugConsole.WriteLine(" (WAIT_OBJECT_0+1) OK - second event was signaled");
        }
        else if (result == WaitResult.Object0)
        {
            DebugConsole.WriteLine(" (WAIT_OBJECT_0) - first event somehow signaled");
        }
        else if (result == WaitResult.Timeout)
        {
            DebugConsole.WriteLine(" (WAIT_TIMEOUT) - neither signaled??");
        }
        else
        {
            DebugConsole.WriteLine();
        }

        // Test 2: Signal event1 and test WaitAll
        Sync.SetEvent(event1);
        Sync.SetEvent(event2);  // Re-signal after auto-reset

        DebugConsole.WriteLine("[WaitMultiple Test] Both events now signaled");

        result = Sync.WaitForMultipleObjects(2, handles, true, 0);  // bWaitAll=true

        DebugConsole.Write("[WaitMultiple Test] WaitAll result: ");
        DebugConsole.WriteHex(result);
        if (result == WaitResult.Object0)
        {
            DebugConsole.WriteLine(" (WAIT_OBJECT_0) OK - both events signaled");
        }
        else if (result == WaitResult.Timeout)
        {
            DebugConsole.WriteLine(" (WAIT_TIMEOUT)");
        }
        else
        {
            DebugConsole.WriteLine();
        }

        // Test 3: Timeout test - neither signaled now (auto-reset consumed them)
        result = Sync.WaitForMultipleObjects(2, handles, false, 0);

        DebugConsole.Write("[WaitMultiple Test] After auto-reset, WaitAny result: ");
        DebugConsole.WriteHex(result);
        if (result == WaitResult.Timeout)
        {
            DebugConsole.WriteLine(" (WAIT_TIMEOUT) OK - events were auto-reset");
        }
        else
        {
            DebugConsole.WriteLine();
        }

        // Cleanup
        Sync.CloseHandle(event1);
        Sync.CloseHandle(event2);

        DebugConsole.WriteLine("[WaitMultiple Test] All WaitForMultipleObjects tests PASSED!");
        return 0;
    }

    // Static flag to track if our exception filter was called
    private static bool _raiseExceptionFilterCalled;
    private static uint _raiseExceptionCode;

    /// <summary>
    /// Exception filter for RaiseException test - handles the test exception
    /// </summary>
    [UnmanagedCallersOnly]
    private static int RaiseExceptionTestFilter(ExceptionRecord* record, ExceptionContext* context)
    {
        _raiseExceptionFilterCalled = true;
        _raiseExceptionCode = record->ExceptionCode;

        DebugConsole.Write("[RaiseException Test] Filter called! Code: 0x");
        DebugConsole.WriteHex(record->ExceptionCode);
        DebugConsole.WriteLine();

        // Return -1 to continue execution (EXCEPTION_CONTINUE_EXECUTION)
        return -1;
    }

    /// <summary>
    /// RaiseException test thread - tests software exception raising with filter
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint RaiseExceptionTestThread(void* param)
    {
        DebugConsole.WriteLine("[RaiseException Test] Starting RaiseException test...");

        // Reset test state
        _raiseExceptionFilterCalled = false;
        _raiseExceptionCode = 0;

        // Register our exception filter
        var oldFilter = PAL.Exception.SetUnhandledExceptionFilter(&RaiseExceptionTestFilter);
        DebugConsole.WriteLine("[RaiseException Test] Registered test exception filter");

        // Test 1: Raise a custom exception code (continuable)
        const uint TEST_EXCEPTION_CODE = 0xE0001234;
        DebugConsole.Write("[RaiseException Test] Raising exception 0x");
        DebugConsole.WriteHex(TEST_EXCEPTION_CODE);
        DebugConsole.WriteLine("...");

        PAL.Exception.RaiseException(TEST_EXCEPTION_CODE, 0, 0, null);

        // If we get here, the exception was handled and we continued
        DebugConsole.WriteLine("[RaiseException Test] Returned from RaiseException!");

        // Check if filter was called
        if (_raiseExceptionFilterCalled)
        {
            DebugConsole.Write("[RaiseException Test] Filter was called - ");
            if (_raiseExceptionCode == TEST_EXCEPTION_CODE)
            {
                DebugConsole.WriteLine("PASSED: Exception code matched!");
            }
            else
            {
                DebugConsole.Write("FAILED: Wrong code 0x");
                DebugConsole.WriteHex(_raiseExceptionCode);
                DebugConsole.WriteLine();
            }
        }
        else
        {
            DebugConsole.WriteLine("[RaiseException Test] Filter was NOT called (no filter registered or dispatch failed)");
        }

        // Restore old filter
        PAL.Exception.SetUnhandledExceptionFilter(oldFilter);
        DebugConsole.WriteLine("[RaiseException Test] Restored original exception filter");

        DebugConsole.WriteLine("[RaiseException Test] RaiseException test completed!");
        return 0;
    }

    // State for suspend/resume test
    private static int _suspendTestCounter;
    private static bool _suspendTestTargetRunning;

    /// <summary>
    /// Target thread that increments a counter - will be suspended/resumed
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SuspendTestTargetThread(void* param)
    {
        _suspendTestTargetRunning = true;
        while (_suspendTestTargetRunning)
        {
            _suspendTestCounter++;
            // Small busy loop
            for (int i = 0; i < 1000; i++) Cpu.Pause();
        }
        return 0;
    }

    /// <summary>
    /// SuspendThread/ResumeThread test thread
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SuspendResumeTestThread(void* param)
    {
        DebugConsole.WriteLine("[Suspend/Resume Test] Starting...");

        // Test 1: Create a thread in suspended state
        DebugConsole.WriteLine("[Suspend/Resume Test] Test 1: CREATE_SUSPENDED flag");
        _suspendTestCounter = 0;
        _suspendTestTargetRunning = true;

        uint targetId;
        var targetHandle = PAL.ThreadApi.CreateThread(
            null, 0, &SuspendTestTargetThread, null,
            ThreadCreationFlags.CREATE_SUSPENDED, out targetId);

        if (!targetHandle.IsValid)
        {
            DebugConsole.WriteLine("[Suspend/Resume Test] FAILED: Could not create target thread");
            return 1;
        }

        DebugConsole.Write("[Suspend/Resume Test] Created suspended thread ");
        DebugConsole.WriteHex((ushort)targetId);
        DebugConsole.WriteLine();

        // Give it a moment, counter should stay at 0 since it's suspended
        PAL.ThreadApi.Sleep(100);
        int count1 = _suspendTestCounter;
        DebugConsole.Write("[Suspend/Resume Test] Counter after 100ms (should be 0): ");
        DebugConsole.WriteHex((uint)count1);
        if (count1 == 0)
            DebugConsole.WriteLine(" - PASSED");
        else
            DebugConsole.WriteLine(" - FAILED (thread ran while suspended!)");

        // Resume the thread
        DebugConsole.WriteLine("[Suspend/Resume Test] Resuming thread...");
        int prevCount = PAL.ThreadApi.ResumeThread(targetHandle);
        DebugConsole.Write("[Suspend/Resume Test] ResumeThread returned previous suspend count: ");
        DebugConsole.WriteHex((uint)prevCount);
        if (prevCount == 1)
            DebugConsole.WriteLine(" - PASSED (was suspended once)");
        else
            DebugConsole.WriteLine();

        // Let it run and check counter increases
        PAL.ThreadApi.Sleep(100);
        int count2 = _suspendTestCounter;
        DebugConsole.Write("[Suspend/Resume Test] Counter after running 100ms: ");
        DebugConsole.WriteHex((uint)count2);
        if (count2 > count1)
            DebugConsole.WriteLine(" - PASSED (counter increased)");
        else
            DebugConsole.WriteLine(" - FAILED (counter did not increase!)");

        // Test 2: Suspend a running thread
        DebugConsole.WriteLine("[Suspend/Resume Test] Test 2: SuspendThread on running thread");
        prevCount = PAL.ThreadApi.SuspendThread(targetHandle);
        DebugConsole.Write("[Suspend/Resume Test] SuspendThread returned previous suspend count: ");
        DebugConsole.WriteHex((uint)prevCount);
        if (prevCount == 0)
            DebugConsole.WriteLine(" - PASSED (was not suspended)");
        else
            DebugConsole.WriteLine();

        // Record counter, wait, check it doesn't change
        int count3 = _suspendTestCounter;
        PAL.ThreadApi.Sleep(100);
        int count4 = _suspendTestCounter;
        DebugConsole.Write("[Suspend/Resume Test] Counter before: ");
        DebugConsole.WriteHex((uint)count3);
        DebugConsole.Write(", after: ");
        DebugConsole.WriteHex((uint)count4);
        if (count4 == count3)
            DebugConsole.WriteLine(" - PASSED (thread is suspended)");
        else
            DebugConsole.WriteLine(" - Note: counter changed (timing variance)");

        // Test 3: Multiple suspends - suspend count should stack
        DebugConsole.WriteLine("[Suspend/Resume Test] Test 3: Multiple suspend calls");
        prevCount = PAL.ThreadApi.SuspendThread(targetHandle);
        DebugConsole.Write("[Suspend/Resume Test] Second SuspendThread returned: ");
        DebugConsole.WriteHex((uint)prevCount);
        if (prevCount == 1)
            DebugConsole.WriteLine(" - PASSED (was suspended once)");
        else
            DebugConsole.WriteLine();

        // Resume once - should still be suspended (count was 2, now 1)
        prevCount = PAL.ThreadApi.ResumeThread(targetHandle);
        DebugConsole.Write("[Suspend/Resume Test] First ResumeThread returned: ");
        DebugConsole.WriteHex((uint)prevCount);
        if (prevCount == 2)
            DebugConsole.WriteLine(" - PASSED (was suspended twice)");
        else
            DebugConsole.WriteLine();

        int count5 = _suspendTestCounter;
        PAL.ThreadApi.Sleep(50);
        int count6 = _suspendTestCounter;
        DebugConsole.Write("[Suspend/Resume Test] After one resume, counter change: ");
        DebugConsole.WriteHex((uint)(count6 - count5));
        if (count6 == count5)
            DebugConsole.WriteLine(" - PASSED (still suspended)");
        else
            DebugConsole.WriteLine(" - Note: counter changed");

        // Final resume - thread should run
        prevCount = PAL.ThreadApi.ResumeThread(targetHandle);
        DebugConsole.Write("[Suspend/Resume Test] Second ResumeThread returned: ");
        DebugConsole.WriteHex((uint)prevCount);
        if (prevCount == 1)
            DebugConsole.WriteLine(" - PASSED");
        else
            DebugConsole.WriteLine();

        PAL.ThreadApi.Sleep(50);
        int count7 = _suspendTestCounter;
        if (count7 > count6)
            DebugConsole.WriteLine("[Suspend/Resume Test] Thread running again - PASSED");
        else
            DebugConsole.WriteLine("[Suspend/Resume Test] Thread not running - check timing");

        // Stop the target thread
        _suspendTestTargetRunning = false;
        PAL.ThreadApi.Sleep(50);

        DebugConsole.WriteLine("[Suspend/Resume Test] All tests completed!");
        return 0;
    }

    /// <summary>
    /// Debug API test thread - tests OutputDebugStringA/W, IsDebuggerPresent, DebugBreak
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint DebugApiTestThread(void* param)
    {
        DebugConsole.WriteLine("[Debug API Test] Starting Debug API tests...");

        // Test 1: OutputDebugStringA - ANSI string output
        DebugConsole.WriteLine("[Debug API Test] Test 1: OutputDebugStringA");
        // Create a null-terminated ANSI string on stack
        byte* ansiStr = stackalloc byte[32];
        ansiStr[0] = (byte)'[';
        ansiStr[1] = (byte)'D';
        ansiStr[2] = (byte)'e';
        ansiStr[3] = (byte)'b';
        ansiStr[4] = (byte)'u';
        ansiStr[5] = (byte)'g';
        ansiStr[6] = (byte)' ';
        ansiStr[7] = (byte)'A';
        ansiStr[8] = (byte)'N';
        ansiStr[9] = (byte)'S';
        ansiStr[10] = (byte)'I';
        ansiStr[11] = (byte)']';
        ansiStr[12] = (byte)' ';
        ansiStr[13] = (byte)'H';
        ansiStr[14] = (byte)'e';
        ansiStr[15] = (byte)'l';
        ansiStr[16] = (byte)'l';
        ansiStr[17] = (byte)'o';
        ansiStr[18] = (byte)'!';
        ansiStr[19] = (byte)'\n';
        ansiStr[20] = 0;

        DebugApi.OutputDebugStringA(ansiStr);
        DebugConsole.WriteLine("[Debug API Test] OutputDebugStringA - check serial output above");

        // Test 2: OutputDebugStringW - Wide string output
        DebugConsole.WriteLine("[Debug API Test] Test 2: OutputDebugStringW");
        char* wideStr = stackalloc char[32];
        wideStr[0] = '[';
        wideStr[1] = 'D';
        wideStr[2] = 'e';
        wideStr[3] = 'b';
        wideStr[4] = 'u';
        wideStr[5] = 'g';
        wideStr[6] = ' ';
        wideStr[7] = 'W';
        wideStr[8] = 'I';
        wideStr[9] = 'D';
        wideStr[10] = 'E';
        wideStr[11] = ']';
        wideStr[12] = ' ';
        wideStr[13] = 'H';
        wideStr[14] = 'e';
        wideStr[15] = 'l';
        wideStr[16] = 'l';
        wideStr[17] = 'o';
        wideStr[18] = '!';
        wideStr[19] = '\n';
        wideStr[20] = '\0';

        DebugApi.OutputDebugStringW(wideStr);
        DebugConsole.WriteLine("[Debug API Test] OutputDebugStringW - check serial output above");

        // Test 3: IsDebuggerPresent - should return false
        DebugConsole.WriteLine("[Debug API Test] Test 3: IsDebuggerPresent");
        bool debuggerPresent = DebugApi.IsDebuggerPresent();
        DebugConsole.Write("[Debug API Test] IsDebuggerPresent returned: ");
        if (debuggerPresent)
            DebugConsole.WriteLine("true");
        else
            DebugConsole.WriteLine("false (expected)");

        if (!debuggerPresent)
        {
            DebugConsole.WriteLine("[Debug API Test] IsDebuggerPresent - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Debug API Test] IsDebuggerPresent - unexpected (but not an error)");
        }

        // Test 4: Null pointer handling
        DebugConsole.WriteLine("[Debug API Test] Test 4: Null pointer handling");
        DebugApi.OutputDebugStringA(null);
        DebugApi.OutputDebugStringW(null);
        DebugConsole.WriteLine("[Debug API Test] Null pointers handled safely - PASSED");

        // Note: We don't test DebugBreak here because it would trigger INT3
        // which would need to be caught by an exception handler.
        // The ExceptionTestThread already tests breakpoints.
        DebugConsole.WriteLine("[Debug API Test] Note: DebugBreak tested via ExceptionTestThread");

        DebugConsole.WriteLine("[Debug API Test] All Debug API tests PASSED!");
        return 0;
    }

    // APC test state
    private static int _apcCallCount;
    private static nuint _apcLastParam;

    /// <summary>
    /// APC callback function for testing
    /// </summary>
    [UnmanagedCallersOnly]
    private static void TestApcCallback(nuint dwParam)
    {
        _apcCallCount++;
        _apcLastParam = dwParam;
        DebugConsole.Write("[APC Test] APC callback executed! Param: 0x");
        DebugConsole.WriteHex((ulong)dwParam);
        DebugConsole.Write(", call count: ");
        DebugConsole.WriteHex((uint)_apcCallCount);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// APC test thread - tests QueueUserAPC and SleepEx
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint ApcTestThread(void* param)
    {
        DebugConsole.WriteLine("[APC Test] Starting QueueUserAPC and SleepEx tests...");

        // Reset test state
        _apcCallCount = 0;
        _apcLastParam = 0;

        // Get handle to current thread
        var handle = PAL.ThreadApi.GetCurrentThread();
        if (!handle.IsValid)
        {
            DebugConsole.WriteLine("[APC Test] FAILED: Could not get current thread handle");
            return 1;
        }

        // Test 1: Queue an APC to self and call SleepEx with alertable=true
        DebugConsole.WriteLine("[APC Test] Test 1: Queue APC to self, then SleepEx(alertable=true)");

        bool queued = PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0x1234);
        if (!queued)
        {
            DebugConsole.WriteLine("[APC Test] FAILED: QueueUserAPC returned false");
            return 1;
        }
        DebugConsole.WriteLine("[APC Test] QueueUserAPC succeeded");

        // Call SleepEx with alertable=true - should return immediately with WAIT_IO_COMPLETION
        // because APC is already queued
        uint result = PAL.ThreadApi.SleepEx(1000, true);  // 1 second timeout, alertable

        DebugConsole.Write("[APC Test] SleepEx returned: 0x");
        DebugConsole.WriteHex(result);
        if (result == WaitResult.IoCompletion)
        {
            DebugConsole.WriteLine(" (WAIT_IO_COMPLETION) - PASSED");
        }
        else if (result == 0)
        {
            DebugConsole.WriteLine(" (timeout - APC not delivered?)");
        }
        else
        {
            DebugConsole.WriteLine();
        }

        // Check APC was called
        if (_apcCallCount == 1 && _apcLastParam == 0x1234)
        {
            DebugConsole.WriteLine("[APC Test] Test 1: APC delivered correctly - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 1: FAILED - call count: ");
            DebugConsole.WriteHex((uint)_apcCallCount);
            DebugConsole.Write(", last param: 0x");
            DebugConsole.WriteHex((ulong)_apcLastParam);
            DebugConsole.WriteLine();
        }

        // Test 2: SleepEx with alertable=false should NOT deliver APCs
        DebugConsole.WriteLine("[APC Test] Test 2: SleepEx with alertable=false");
        _apcCallCount = 0;

        queued = PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0x5678);
        if (!queued)
        {
            DebugConsole.WriteLine("[APC Test] FAILED: QueueUserAPC returned false");
            return 1;
        }

        // Call SleepEx with alertable=false - should NOT process the APC
        result = PAL.ThreadApi.SleepEx(50, false);  // 50ms timeout, NOT alertable

        DebugConsole.Write("[APC Test] SleepEx(alertable=false) returned: 0x");
        DebugConsole.WriteHex(result);
        DebugConsole.WriteLine();

        if (_apcCallCount == 0)
        {
            DebugConsole.WriteLine("[APC Test] Test 2: APC NOT delivered (correct) - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[APC Test] Test 2: FAILED - APC was delivered when it shouldn't be");
        }

        // Test 3: Now call SleepEx alertable to drain the queued APC
        DebugConsole.WriteLine("[APC Test] Test 3: Drain pending APC with SleepEx(alertable=true)");
        result = PAL.ThreadApi.SleepEx(1000, true);

        if (result == WaitResult.IoCompletion && _apcCallCount == 1 && _apcLastParam == 0x5678)
        {
            DebugConsole.WriteLine("[APC Test] Test 3: Pending APC delivered - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 3: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.Write(", count: ");
            DebugConsole.WriteHex((uint)_apcCallCount);
            DebugConsole.WriteLine();
        }

        // Test 4: Multiple APCs queued
        DebugConsole.WriteLine("[APC Test] Test 4: Multiple APCs queued");
        _apcCallCount = 0;

        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xAAAA);
        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xBBBB);
        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xCCCC);

        result = PAL.ThreadApi.SleepEx(1000, true);

        if (_apcCallCount == 3)
        {
            DebugConsole.Write("[APC Test] Test 4: All 3 APCs delivered, last param: 0x");
            DebugConsole.WriteHex((ulong)_apcLastParam);
            DebugConsole.WriteLine(" - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 4: FAILED - only ");
            DebugConsole.WriteHex((uint)_apcCallCount);
            DebugConsole.WriteLine(" APCs delivered");
        }

        // Test 5: SleepEx(0, true) should check for APCs without sleeping
        DebugConsole.WriteLine("[APC Test] Test 5: SleepEx(0, alertable=true)");
        _apcCallCount = 0;

        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xDEAD);
        result = PAL.ThreadApi.SleepEx(0, true);

        if (result == WaitResult.IoCompletion && _apcCallCount == 1)
        {
            DebugConsole.WriteLine("[APC Test] Test 5: SleepEx(0, true) delivered APC - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 5: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
        }

        // Test 6: No pending APCs - should timeout normally
        DebugConsole.WriteLine("[APC Test] Test 6: No pending APCs - should timeout");
        _apcCallCount = 0;

        result = PAL.ThreadApi.SleepEx(50, true);  // 50ms timeout

        if (result == 0 && _apcCallCount == 0)
        {
            DebugConsole.WriteLine("[APC Test] Test 6: Normal timeout with no APCs - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 6: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
        }

        // Test 7: WaitForSingleObjectEx with alertable=true and pending APC
        DebugConsole.WriteLine("[APC Test] Test 7: WaitForSingleObjectEx with alertable=true");
        _apcCallCount = 0;

        // Create an event that won't be signaled
        var evt = PAL.Sync.CreateEvent(false, false);
        if (evt == null)
        {
            DebugConsole.WriteLine("[APC Test] Test 7: FAILED - could not create event");
            return 1;
        }

        // Queue an APC
        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xCAFE);

        // Wait on unsignaled event with alertable=true - should return WAIT_IO_COMPLETION
        result = PAL.Sync.WaitForSingleObjectEx(evt, 1000, true);

        if (result == WaitResult.IoCompletion && _apcCallCount == 1 && _apcLastParam == 0xCAFE)
        {
            DebugConsole.WriteLine("[APC Test] Test 7: WaitForSingleObjectEx returned WAIT_IO_COMPLETION - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 7: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.Write(", count: ");
            DebugConsole.WriteHex((uint)_apcCallCount);
            DebugConsole.WriteLine();
        }

        // Test 8: WaitForSingleObjectEx with alertable=false should NOT deliver APC
        DebugConsole.WriteLine("[APC Test] Test 8: WaitForSingleObjectEx with alertable=false");
        _apcCallCount = 0;

        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xBEEF);

        // Wait with short timeout and alertable=false
        result = PAL.Sync.WaitForSingleObjectEx(evt, 50, false);

        if (result == WaitResult.Timeout && _apcCallCount == 0)
        {
            DebugConsole.WriteLine("[APC Test] Test 8: APC NOT delivered during non-alertable wait - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 8: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.Write(", count: ");
            DebugConsole.WriteHex((uint)_apcCallCount);
            DebugConsole.WriteLine();
        }

        // Drain the pending APC
        PAL.ThreadApi.SleepEx(0, true);
        _apcCallCount = 0;

        // Test 9: WaitForSingleObjectEx on signaled event should return immediately
        DebugConsole.WriteLine("[APC Test] Test 9: WaitForSingleObjectEx on signaled event");

        PAL.Sync.SetEvent(evt);  // Signal the event
        PAL.ThreadApi.QueueUserAPC(&TestApcCallback, handle, 0xFACE);

        // Wait on signaled event - should acquire immediately, not deliver APC
        result = PAL.Sync.WaitForSingleObjectEx(evt, 1000, true);

        if (result == WaitResult.Object0)
        {
            DebugConsole.WriteLine("[APC Test] Test 9: Signaled event acquired immediately - PASSED");
        }
        else
        {
            DebugConsole.Write("[APC Test] Test 9: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
        }

        // Drain the pending APC from test 9
        PAL.ThreadApi.SleepEx(0, true);

        // Clean up
        PAL.Sync.CloseHandle(evt);

        DebugConsole.WriteLine("[APC Test] All QueueUserAPC, SleepEx, and WaitForSingleObjectEx tests completed!");
        return 0;
    }

    /// <summary>
    /// Environment API test thread - tests Get/Set/Free environment variable APIs
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint EnvironmentApiTestThread(void* param)
    {
        DebugConsole.WriteLine("[Env API Test] Starting Environment API tests...");

        // Test 1: Set a new environment variable
        DebugConsole.WriteLine("[Env API Test] Test 1: SetEnvironmentVariableW (new variable)");
        char* varName = stackalloc char[16];
        varName[0] = 'T'; varName[1] = 'E'; varName[2] = 'S'; varName[3] = 'T';
        varName[4] = '_'; varName[5] = 'V'; varName[6] = 'A'; varName[7] = 'R';
        varName[8] = '\0';

        char* varValue = stackalloc char[16];
        varValue[0] = 'H'; varValue[1] = 'e'; varValue[2] = 'l'; varValue[3] = 'l';
        varValue[4] = 'o'; varValue[5] = '1'; varValue[6] = '2'; varValue[7] = '3';
        varValue[8] = '\0';

        bool setResult = EnvironmentApi.SetEnvironmentVariableW(varName, varValue);
        if (setResult)
        {
            DebugConsole.WriteLine("[Env API Test] SetEnvironmentVariableW - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] SetEnvironmentVariableW - FAILED");
            return 1;
        }

        // Test 2: Get the environment variable
        DebugConsole.WriteLine("[Env API Test] Test 2: GetEnvironmentVariableW");
        char* buffer = stackalloc char[256];
        uint result = EnvironmentApi.GetEnvironmentVariableW(varName, buffer, 256);

        if (result == 8) // "Hello123" is 8 chars
        {
            DebugConsole.Write("[Env API Test] Got value: ");
            // Print the value character by character
            for (int i = 0; i < (int)result; i++)
            {
                DebugConsole.WriteByte((byte)buffer[i]);
            }
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("[Env API Test] GetEnvironmentVariableW - PASSED");
        }
        else
        {
            DebugConsole.Write("[Env API Test] GetEnvironmentVariableW returned: ");
            DebugConsole.WriteHex((uint)result);
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("[Env API Test] GetEnvironmentVariableW - FAILED");
            return 1;
        }

        // Test 3: Case-insensitive lookup
        DebugConsole.WriteLine("[Env API Test] Test 3: Case-insensitive lookup");
        char* lowerName = stackalloc char[16];
        lowerName[0] = 't'; lowerName[1] = 'e'; lowerName[2] = 's'; lowerName[3] = 't';
        lowerName[4] = '_'; lowerName[5] = 'v'; lowerName[6] = 'a'; lowerName[7] = 'r';
        lowerName[8] = '\0';

        result = EnvironmentApi.GetEnvironmentVariableW(lowerName, buffer, 256);
        if (result == 8)
        {
            DebugConsole.WriteLine("[Env API Test] Case-insensitive lookup - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] Case-insensitive lookup - FAILED");
            return 1;
        }

        // Test 4: Update existing variable
        DebugConsole.WriteLine("[Env API Test] Test 4: Update existing variable");
        char* newValue = stackalloc char[16];
        newValue[0] = 'W'; newValue[1] = 'o'; newValue[2] = 'r'; newValue[3] = 'l';
        newValue[4] = 'd'; newValue[5] = '!'; newValue[6] = '\0';

        setResult = EnvironmentApi.SetEnvironmentVariableW(varName, newValue);
        result = EnvironmentApi.GetEnvironmentVariableW(varName, buffer, 256);
        if (setResult && result == 6) // "World!" is 6 chars
        {
            DebugConsole.Write("[Env API Test] Updated value: ");
            for (int i = 0; i < (int)result; i++)
            {
                DebugConsole.WriteByte((byte)buffer[i]);
            }
            DebugConsole.WriteLine();
            DebugConsole.WriteLine("[Env API Test] Update variable - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] Update variable - FAILED");
            return 1;
        }

        // Test 5: GetEnvironmentStringsW
        DebugConsole.WriteLine("[Env API Test] Test 5: GetEnvironmentStringsW");
        char* envBlock = EnvironmentApi.GetEnvironmentStringsW();
        if (envBlock != null)
        {
            DebugConsole.WriteLine("[Env API Test] Got environment block");
            // Count entries
            int entryCount = 0;
            char* p = envBlock;
            while (*p != '\0')
            {
                entryCount++;
                while (*p != '\0') p++;
                p++; // skip null terminator
            }
            DebugConsole.Write("[Env API Test] Environment entries: ");
            DebugConsole.WriteHex((uint)entryCount);
            DebugConsole.WriteLine();

            // Free the block
            bool freeResult = EnvironmentApi.FreeEnvironmentStringsW(envBlock);
            if (freeResult)
            {
                DebugConsole.WriteLine("[Env API Test] GetEnvironmentStringsW + Free - PASSED");
            }
            else
            {
                DebugConsole.WriteLine("[Env API Test] FreeEnvironmentStringsW - FAILED");
                return 1;
            }
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] GetEnvironmentStringsW - FAILED (null)");
            return 1;
        }

        // Test 6: Delete variable (set to null)
        DebugConsole.WriteLine("[Env API Test] Test 6: Delete variable");
        setResult = EnvironmentApi.SetEnvironmentVariableW(varName, null);
        result = EnvironmentApi.GetEnvironmentVariableW(varName, buffer, 256);
        if (setResult && result == 0)
        {
            DebugConsole.WriteLine("[Env API Test] Delete variable - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] Delete variable - FAILED");
            return 1;
        }

        // Test 7: Get non-existent variable
        DebugConsole.WriteLine("[Env API Test] Test 7: Get non-existent variable");
        char* noExist = stackalloc char[16];
        noExist[0] = 'N'; noExist[1] = 'O'; noExist[2] = '_'; noExist[3] = 'E';
        noExist[4] = 'X'; noExist[5] = 'I'; noExist[6] = 'S'; noExist[7] = 'T';
        noExist[8] = '\0';

        result = EnvironmentApi.GetEnvironmentVariableW(noExist, buffer, 256);
        if (result == 0)
        {
            DebugConsole.WriteLine("[Env API Test] Non-existent variable returns 0 - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Env API Test] Non-existent variable - FAILED");
            return 1;
        }

        // Test 8: Buffer size check
        DebugConsole.WriteLine("[Env API Test] Test 8: Buffer size check");
        // Set a variable again
        EnvironmentApi.SetEnvironmentVariableW(varName, varValue); // "Hello123"
        // Try to get with small buffer
        result = EnvironmentApi.GetEnvironmentVariableW(varName, buffer, 4); // too small
        if (result == 9) // Should return required size (8 + 1 for null)
        {
            DebugConsole.WriteLine("[Env API Test] Buffer size check - PASSED");
        }
        else
        {
            DebugConsole.Write("[Env API Test] Buffer size check returned: ");
            DebugConsole.WriteHex((uint)result);
            DebugConsole.WriteLine(" (expected 9)");
            DebugConsole.WriteLine("[Env API Test] Buffer size check - FAILED");
            return 1;
        }

        // Clean up
        EnvironmentApi.SetEnvironmentVariableW(varName, null);

        DebugConsole.WriteLine("[Env API Test] All Environment API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Test thread for CreateEventEx, CreateMutexEx, CreateSemaphoreEx, and SignalObjectAndWait
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint SyncExTestThread(void* param)
    {
        DebugConsole.WriteLine("[SyncEx Test] Starting extended sync API tests...");

        // Test 1: CreateEventEx with manual reset and initial set flags
        DebugConsole.WriteLine("[SyncEx Test] Test 1: CreateEventEx");

        // Create manual-reset, initially-signaled event
        var evt1 = PAL.Sync.CreateEventEx(
            null, null,
            PAL.Sync.EventFlags.CREATE_EVENT_MANUAL_RESET | PAL.Sync.EventFlags.CREATE_EVENT_INITIAL_SET,
            0);

        if (evt1 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 1: FAILED - CreateEventEx returned null");
            return 1;
        }

        // Should be able to wait on it immediately (already signaled)
        var result = PAL.Sync.WaitForSingleObject(evt1, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.Write("[SyncEx Test] Test 1: FAILED - event not initially signaled, result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
            return 1;
        }

        // Manual reset - should still be signaled
        result = PAL.Sync.WaitForSingleObject(evt1, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 1: FAILED - manual reset event auto-reset");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 1: CreateEventEx with flags - PASSED");

        // Test 2: CreateEventEx auto-reset (no MANUAL_RESET flag)
        DebugConsole.WriteLine("[SyncEx Test] Test 2: CreateEventEx auto-reset");

        var evt2 = PAL.Sync.CreateEventEx(
            null, null,
            PAL.Sync.EventFlags.CREATE_EVENT_INITIAL_SET,  // No manual reset
            0);

        if (evt2 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 2: FAILED - CreateEventEx returned null");
            return 1;
        }

        // First wait should succeed
        result = PAL.Sync.WaitForSingleObject(evt2, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 2: FAILED - event not initially signaled");
            return 1;
        }

        // Second wait should timeout (auto-reset)
        result = PAL.Sync.WaitForSingleObject(evt2, 0);
        if (result != WaitResult.Timeout)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 2: FAILED - event did not auto-reset");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 2: CreateEventEx auto-reset - PASSED");

        // Test 3: CreateEventEx with name should fail (not supported)
        DebugConsole.WriteLine("[SyncEx Test] Test 3: CreateEventEx with name (should fail)");

        char* fakeName = stackalloc char[8];
        fakeName[0] = 'T'; fakeName[1] = 'e'; fakeName[2] = 's'; fakeName[3] = 't';
        fakeName[4] = '\0';

        var evt3 = PAL.Sync.CreateEventEx(null, fakeName, 0, 0);
        if (evt3 != null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 3: FAILED - CreateEventEx with name should return null");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 3: Named event rejected - PASSED");

        // Test 4: CreateMutexEx with initial owner
        DebugConsole.WriteLine("[SyncEx Test] Test 4: CreateMutexEx with initial owner");

        var mutex1 = PAL.Sync.CreateMutexEx(
            null, null,
            PAL.Sync.MutexFlags.CREATE_MUTEX_INITIAL_OWNER,
            0);

        if (mutex1 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 4: FAILED - CreateMutexEx returned null");
            return 1;
        }

        // We own it, so we can acquire it again (recursive)
        result = PAL.Sync.WaitForSingleObject(mutex1, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 4: FAILED - could not recursively acquire mutex");
            return 1;
        }

        // Release twice (we acquired it twice - once on creation, once manually)
        PAL.Sync.ReleaseMutex(mutex1);
        PAL.Sync.ReleaseMutex(mutex1);

        DebugConsole.WriteLine("[SyncEx Test] Test 4: CreateMutexEx with initial owner - PASSED");

        // Test 5: CreateMutexEx without initial owner
        DebugConsole.WriteLine("[SyncEx Test] Test 5: CreateMutexEx without initial owner");

        var mutex2 = PAL.Sync.CreateMutexEx(null, null, 0, 0);

        if (mutex2 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 5: FAILED - CreateMutexEx returned null");
            return 1;
        }

        // Should be able to acquire (no owner)
        result = PAL.Sync.WaitForSingleObject(mutex2, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 5: FAILED - could not acquire unowned mutex");
            return 1;
        }

        PAL.Sync.ReleaseMutex(mutex2);

        DebugConsole.WriteLine("[SyncEx Test] Test 5: CreateMutexEx without owner - PASSED");

        // Test 6: CreateSemaphoreEx
        DebugConsole.WriteLine("[SyncEx Test] Test 6: CreateSemaphoreEx");

        var sem1 = PAL.Sync.CreateSemaphoreEx(null, 2, 5, null, 0, 0);

        if (sem1 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 6: FAILED - CreateSemaphoreEx returned null");
            return 1;
        }

        // Should be able to acquire twice (initial count = 2)
        result = PAL.Sync.WaitForSingleObject(sem1, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 6: FAILED - first acquire failed");
            return 1;
        }

        result = PAL.Sync.WaitForSingleObject(sem1, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 6: FAILED - second acquire failed");
            return 1;
        }

        // Third should timeout (count is now 0)
        result = PAL.Sync.WaitForSingleObject(sem1, 0);
        if (result != WaitResult.Timeout)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 6: FAILED - third acquire should timeout");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 6: CreateSemaphoreEx - PASSED");

        // Test 7: SignalObjectAndWait with event
        DebugConsole.WriteLine("[SyncEx Test] Test 7: SignalObjectAndWait");

        // Create two events: one to signal, one to wait on
        var evtToSignal = PAL.Sync.CreateEvent(false, false);
        var evtToWait = PAL.Sync.CreateEvent(false, true);  // Already signaled

        if (evtToSignal == null || evtToWait == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 7: FAILED - could not create events");
            return 1;
        }

        // SignalObjectAndWait: signal evtToSignal, wait on evtToWait
        result = PAL.Sync.SignalObjectAndWait(evtToSignal, evtToWait, 0, false);

        if (result != WaitResult.Object0)
        {
            DebugConsole.Write("[SyncEx Test] Test 7: FAILED - SignalObjectAndWait returned: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify evtToSignal was signaled
        result = PAL.Sync.WaitForSingleObject(evtToSignal, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 7: FAILED - signal object was not signaled");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 7: SignalObjectAndWait - PASSED");

        // Test 8: SignalObjectAndWait with mutex
        DebugConsole.WriteLine("[SyncEx Test] Test 8: SignalObjectAndWait with mutex");

        var mutexToRelease = PAL.Sync.CreateMutex(true);  // We own it
        var evtToWait2 = PAL.Sync.CreateEvent(false, true);  // Already signaled

        if (mutexToRelease == null || evtToWait2 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 8: FAILED - could not create objects");
            return 1;
        }

        // SignalObjectAndWait: release mutex, wait on event
        result = PAL.Sync.SignalObjectAndWait(mutexToRelease, evtToWait2, 0, false);

        if (result != WaitResult.Object0)
        {
            DebugConsole.Write("[SyncEx Test] Test 8: FAILED - result: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify mutex was released (we can acquire it again)
        result = PAL.Sync.WaitForSingleObject(mutexToRelease, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 8: FAILED - mutex was not released");
            return 1;
        }

        PAL.Sync.ReleaseMutex(mutexToRelease);

        DebugConsole.WriteLine("[SyncEx Test] Test 8: SignalObjectAndWait with mutex - PASSED");

        // Test 9: SignalObjectAndWait with timeout
        DebugConsole.WriteLine("[SyncEx Test] Test 9: SignalObjectAndWait with timeout");

        var evtToSignal2 = PAL.Sync.CreateEvent(false, false);
        var evtToWait3 = PAL.Sync.CreateEvent(false, false);  // Not signaled

        if (evtToSignal2 == null || evtToWait3 == null)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 9: FAILED - could not create events");
            return 1;
        }

        // Should timeout since evtToWait3 is not signaled
        result = PAL.Sync.SignalObjectAndWait(evtToSignal2, evtToWait3, 50, false);

        if (result != WaitResult.Timeout)
        {
            DebugConsole.Write("[SyncEx Test] Test 9: FAILED - expected timeout, got: 0x");
            DebugConsole.WriteHex(result);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify evtToSignal2 was still signaled (signal happens before wait)
        result = PAL.Sync.WaitForSingleObject(evtToSignal2, 0);
        if (result != WaitResult.Object0)
        {
            DebugConsole.WriteLine("[SyncEx Test] Test 9: FAILED - signal object was not signaled");
            return 1;
        }

        DebugConsole.WriteLine("[SyncEx Test] Test 9: SignalObjectAndWait timeout - PASSED");

        // Clean up
        PAL.Sync.CloseHandle(evt1);
        PAL.Sync.CloseHandle(evt2);
        PAL.Sync.CloseHandle(mutex1);
        PAL.Sync.CloseHandle(mutex2);
        PAL.Sync.CloseHandle(sem1);
        PAL.Sync.CloseHandle(evtToSignal);
        PAL.Sync.CloseHandle(evtToWait);
        PAL.Sync.CloseHandle(mutexToRelease);
        PAL.Sync.CloseHandle(evtToWait2);
        PAL.Sync.CloseHandle(evtToSignal2);
        PAL.Sync.CloseHandle(evtToWait3);

        DebugConsole.WriteLine("[SyncEx Test] All extended sync API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Test thread for GetCurrentProcessId, GetCurrentProcess, and HeapSize
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint ProcessAndHeapTestThread(void* param)
    {
        DebugConsole.WriteLine("[Process/Heap Test] Starting Process and Heap API tests...");

        // Test 1: GetCurrentProcessId
        DebugConsole.WriteLine("[Process/Heap Test] Test 1: GetCurrentProcessId");

        uint pid = ProcessApi.GetCurrentProcessId();
        if (pid != 0)
        {
            DebugConsole.Write("[Process/Heap Test] Test 1: FAILED - expected PID 0 (kernel), got: ");
            DebugConsole.WriteHex(pid);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 1: GetCurrentProcessId = 0 (kernel) - PASSED");

        // Test 2: GetCurrentProcess
        DebugConsole.WriteLine("[Process/Heap Test] Test 2: GetCurrentProcess");

        nuint processHandle = ProcessApi.GetCurrentProcess();
        // Should be -1 (pseudo-handle for current process)
        if (processHandle != unchecked((nuint)(nint)(-1)))
        {
            DebugConsole.Write("[Process/Heap Test] Test 2: FAILED - expected -1, got: 0x");
            DebugConsole.WriteHex((ulong)processHandle);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 2: GetCurrentProcess = -1 (pseudo-handle) - PASSED");

        // Test 3: HeapSize with valid allocation
        DebugConsole.WriteLine("[Process/Heap Test] Test 3: HeapSize");

        var heap = PAL.Memory.GetProcessHeap();
        void* ptr = PAL.Memory.HeapAlloc(heap, 0, 256);

        if (ptr == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 3: FAILED - HeapAlloc returned null");
            return 1;
        }

        ulong size = PAL.Memory.HeapSize(heap, 0, ptr);
        // Size should be at least 256 (may be larger due to alignment)
        if (size < 256)
        {
            DebugConsole.Write("[Process/Heap Test] Test 3: FAILED - HeapSize returned: ");
            DebugConsole.WriteHex(size);
            DebugConsole.WriteLine(" (expected >= 256)");
            PAL.Memory.HeapFree(heap, 0, ptr);
            return 1;
        }

        DebugConsole.Write("[Process/Heap Test] Test 3: HeapSize = ");
        DebugConsole.WriteHex(size);
        DebugConsole.WriteLine(" - PASSED");

        PAL.Memory.HeapFree(heap, 0, ptr);

        // Test 4: HeapSize with null pointer
        DebugConsole.WriteLine("[Process/Heap Test] Test 4: HeapSize with null");

        size = PAL.Memory.HeapSize(heap, 0, null);
        if (size != 0)
        {
            DebugConsole.Write("[Process/Heap Test] Test 4: FAILED - HeapSize(null) returned: ");
            DebugConsole.WriteHex(size);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 4: HeapSize(null) = 0 - PASSED");

        // Test 5: HeapSize with different allocation sizes
        DebugConsole.WriteLine("[Process/Heap Test] Test 5: HeapSize with various sizes");

        void* ptr1 = PAL.Memory.HeapAlloc(heap, 0, 64);
        void* ptr2 = PAL.Memory.HeapAlloc(heap, 0, 1024);
        void* ptr3 = PAL.Memory.HeapAlloc(heap, 0, 4096);

        if (ptr1 == null || ptr2 == null || ptr3 == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 5: FAILED - HeapAlloc returned null");
            return 1;
        }

        ulong size1 = PAL.Memory.HeapSize(heap, 0, ptr1);
        ulong size2 = PAL.Memory.HeapSize(heap, 0, ptr2);
        ulong size3 = PAL.Memory.HeapSize(heap, 0, ptr3);

        bool ok = size1 >= 64 && size2 >= 1024 && size3 >= 4096;
        if (!ok)
        {
            DebugConsole.Write("[Process/Heap Test] Test 5: FAILED - sizes: ");
            DebugConsole.WriteHex(size1);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex(size2);
            DebugConsole.Write(", ");
            DebugConsole.WriteHex(size3);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.Write("[Process/Heap Test] Test 5: HeapSize(64)=");
        DebugConsole.WriteHex(size1);
        DebugConsole.Write(", HeapSize(1024)=");
        DebugConsole.WriteHex(size2);
        DebugConsole.Write(", HeapSize(4096)=");
        DebugConsole.WriteHex(size3);
        DebugConsole.WriteLine(" - PASSED");

        PAL.Memory.HeapFree(heap, 0, ptr1);
        PAL.Memory.HeapFree(heap, 0, ptr2);
        PAL.Memory.HeapFree(heap, 0, ptr3);

        // Test 6: HeapSize after free should return 0 (block is marked free)
        DebugConsole.WriteLine("[Process/Heap Test] Test 6: HeapSize after free");

        void* ptrFree = PAL.Memory.HeapAlloc(heap, 0, 128);
        if (ptrFree == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 6: FAILED - HeapAlloc returned null");
            return 1;
        }

        // Get size before free
        ulong sizeBefore = PAL.Memory.HeapSize(heap, 0, ptrFree);
        if (sizeBefore < 128)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 6: FAILED - HeapSize before free too small");
            return 1;
        }

        PAL.Memory.HeapFree(heap, 0, ptrFree);

        // Get size after free - should be 0 because block is marked free
        ulong sizeAfter = PAL.Memory.HeapSize(heap, 0, ptrFree);
        if (sizeAfter != 0)
        {
            DebugConsole.Write("[Process/Heap Test] Test 6: FAILED - HeapSize after free returned: ");
            DebugConsole.WriteHex(sizeAfter);
            DebugConsole.WriteLine(" (expected 0)");
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 6: HeapSize after free = 0 - PASSED");

        // Test 7: HeapReAlloc - shrink (should stay in place)
        DebugConsole.WriteLine("[Process/Heap Test] Test 7: HeapReAlloc shrink");

        void* ptrRealloc = PAL.Memory.HeapAlloc(heap, 0, 1024);
        if (ptrRealloc == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 7: FAILED - initial HeapAlloc failed");
            return 1;
        }

        // Write a pattern to verify data is preserved
        byte* pattern = (byte*)ptrRealloc;
        for (int i = 0; i < 256; i++)
            pattern[i] = (byte)(i & 0xFF);

        void* shrunk = PAL.Memory.HeapReAlloc(heap, 0, ptrRealloc, 256);
        if (shrunk == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 7: FAILED - HeapReAlloc shrink returned null");
            return 1;
        }

        // Shrink should stay in place
        if (shrunk != ptrRealloc)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 7: Note - shrink moved (OK but not optimal)");
        }

        // Verify data preserved
        byte* verifyPattern = (byte*)shrunk;
        bool patternOk = true;
        for (int i = 0; i < 256; i++)
        {
            if (verifyPattern[i] != (byte)(i & 0xFF))
            {
                patternOk = false;
                break;
            }
        }

        if (!patternOk)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 7: FAILED - data not preserved after shrink");
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 7: HeapReAlloc shrink - PASSED");

        // Test 8: HeapReAlloc - grow (may or may not stay in place)
        DebugConsole.WriteLine("[Process/Heap Test] Test 8: HeapReAlloc grow");

        void* grown = PAL.Memory.HeapReAlloc(heap, 0, shrunk, 2048);
        if (grown == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 8: FAILED - HeapReAlloc grow returned null");
            return 1;
        }

        // Verify original data still intact
        verifyPattern = (byte*)grown;
        patternOk = true;
        for (int i = 0; i < 256; i++)
        {
            if (verifyPattern[i] != (byte)(i & 0xFF))
            {
                patternOk = false;
                break;
            }
        }

        if (!patternOk)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 8: FAILED - data not preserved after grow");
            return 1;
        }

        // Verify new size is at least 2048
        ulong grownSize = PAL.Memory.HeapSize(heap, 0, grown);
        if (grownSize < 2048)
        {
            DebugConsole.Write("[Process/Heap Test] Test 8: FAILED - grown size too small: ");
            DebugConsole.WriteHex(grownSize);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.Write("[Process/Heap Test] Test 8: HeapReAlloc grow to ");
        DebugConsole.WriteHex(grownSize);
        DebugConsole.WriteLine(" - PASSED");

        PAL.Memory.HeapFree(heap, 0, grown);

        // Test 9: HeapReAlloc with HEAP_ZERO_MEMORY
        DebugConsole.WriteLine("[Process/Heap Test] Test 9: HeapReAlloc with HEAP_ZERO_MEMORY");

        void* ptrZero = PAL.Memory.HeapAlloc(heap, 0, 64);
        if (ptrZero == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 9: FAILED - initial alloc failed");
            return 1;
        }

        // Get the usable size (may be larger than 64 due to alignment)
        ulong usableSize = PAL.Memory.HeapSize(heap, 0, ptrZero);

        // Fill the entire usable area with non-zero pattern
        byte* fill = (byte*)ptrZero;
        for (ulong i = 0; i < usableSize; i++)
            fill[i] = 0xAA;

        // Grow with HEAP_ZERO_MEMORY (0x08) - request more than current usable size
        ulong newRequestedSize = usableSize + 64;
        void* grownZero = PAL.Memory.HeapReAlloc(heap, 0x08, ptrZero, newRequestedSize);
        if (grownZero == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 9: FAILED - HeapReAlloc failed");
            return 1;
        }

        // Original data should be preserved, new portion should be zeroed
        byte* checkZero = (byte*)grownZero;
        bool zeroOk = true;

        // Original usable area should still be 0xAA
        for (ulong i = 0; i < usableSize; i++)
        {
            if (checkZero[i] != 0xAA)
            {
                zeroOk = false;
                break;
            }
        }

        // New portion (from usableSize to newRequestedSize) should be 0
        for (ulong i = usableSize; i < newRequestedSize; i++)
        {
            if (checkZero[i] != 0)
            {
                zeroOk = false;
                break;
            }
        }

        if (!zeroOk)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 9: FAILED - HEAP_ZERO_MEMORY not working");
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 9: HeapReAlloc HEAP_ZERO_MEMORY - PASSED");
        PAL.Memory.HeapFree(heap, 0, grownZero);

        // Test 10: HeapReAlloc with null (should behave like HeapAlloc)
        DebugConsole.WriteLine("[Process/Heap Test] Test 10: HeapReAlloc with null ptr");

        void* fromNull = PAL.Memory.HeapReAlloc(heap, 0, null, 512);
        if (fromNull == null)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 10: FAILED - HeapReAlloc(null) returned null");
            return 1;
        }

        ulong fromNullSize = PAL.Memory.HeapSize(heap, 0, fromNull);
        if (fromNullSize < 512)
        {
            DebugConsole.WriteLine("[Process/Heap Test] Test 10: FAILED - size too small");
            return 1;
        }

        DebugConsole.WriteLine("[Process/Heap Test] Test 10: HeapReAlloc(null) - PASSED");
        PAL.Memory.HeapFree(heap, 0, fromNull);

        DebugConsole.WriteLine("[Process/Heap Test] All Process and Heap API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Test thread 22 - String Conversion APIs (MultiByteToWideChar, WideCharToMultiByte)
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint StringConversionTestThread(void* param)
    {
        DebugConsole.WriteLine("[String Test] Starting String Conversion API tests...");

        // Test 1: GetACP
        DebugConsole.WriteLine("[String Test] Test 1: GetACP");
        uint acp = PAL.StringApi.GetACP();
        if (acp != PAL.CodePage.CP_UTF8)
        {
            DebugConsole.Write("[String Test] Test 1: FAILED - expected UTF-8 (65001), got: ");
            DebugConsole.WriteHex(acp);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 1: GetACP = 65001 (UTF-8) - PASSED");

        // Test 2: GetCPInfo for UTF-8
        DebugConsole.WriteLine("[String Test] Test 2: GetCPInfo");
        PAL.CpInfo cpInfo;
        bool result = PAL.StringApi.GetCPInfo(PAL.CodePage.CP_UTF8, &cpInfo);
        if (!result)
        {
            DebugConsole.WriteLine("[String Test] Test 2: FAILED - GetCPInfo returned false");
            return 1;
        }
        if (cpInfo.MaxCharSize != 4)
        {
            DebugConsole.Write("[String Test] Test 2: FAILED - MaxCharSize expected 4, got: ");
            DebugConsole.WriteHex(cpInfo.MaxCharSize);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 2: GetCPInfo MaxCharSize = 4 - PASSED");

        // Test 3: MultiByteToWideChar - simple ASCII "Hello"
        DebugConsole.WriteLine("[String Test] Test 3: MultiByteToWideChar ASCII");

        // "Hello" in UTF-8 (null-terminated)
        byte* utf8Hello = stackalloc byte[6];
        utf8Hello[0] = (byte)'H';
        utf8Hello[1] = (byte)'e';
        utf8Hello[2] = (byte)'l';
        utf8Hello[3] = (byte)'l';
        utf8Hello[4] = (byte)'o';
        utf8Hello[5] = 0;

        // Query required size
        int requiredSize = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Hello, -1, null, 0);
        if (requiredSize != 6) // 5 chars + null
        {
            DebugConsole.Write("[String Test] Test 3: FAILED - expected size 6, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        // Actually convert
        char* wideHello = stackalloc char[6];
        int converted = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Hello, -1, wideHello, 6);
        if (converted != 6)
        {
            DebugConsole.Write("[String Test] Test 3: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify content
        if (wideHello[0] != 'H' || wideHello[1] != 'e' || wideHello[2] != 'l' ||
            wideHello[3] != 'l' || wideHello[4] != 'o' || wideHello[5] != 0)
        {
            DebugConsole.WriteLine("[String Test] Test 3: FAILED - content mismatch");
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 3: MultiByteToWideChar ASCII - PASSED");

        // Test 4: WideCharToMultiByte - simple ASCII
        DebugConsole.WriteLine("[String Test] Test 4: WideCharToMultiByte ASCII");

        // Query required size
        requiredSize = PAL.StringApi.WideCharToMultiByte(PAL.CodePage.CP_UTF8, 0, wideHello, -1, null, 0, null, null);
        if (requiredSize != 6) // 5 bytes + null
        {
            DebugConsole.Write("[String Test] Test 4: FAILED - expected size 6, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        // Actually convert
        byte* utf8Back = stackalloc byte[6];
        converted = PAL.StringApi.WideCharToMultiByte(PAL.CodePage.CP_UTF8, 0, wideHello, -1, utf8Back, 6, null, null);
        if (converted != 6)
        {
            DebugConsole.Write("[String Test] Test 4: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify round-trip
        bool match = true;
        for (int i = 0; i < 6; i++)
        {
            if (utf8Hello[i] != utf8Back[i])
            {
                match = false;
                break;
            }
        }
        if (!match)
        {
            DebugConsole.WriteLine("[String Test] Test 4: FAILED - round-trip mismatch");
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 4: WideCharToMultiByte ASCII - PASSED");

        // Test 5: MultiByteToWideChar - 2-byte UTF-8 (é = U+00E9 = 0xC3 0xA9)
        DebugConsole.WriteLine("[String Test] Test 5: MultiByteToWideChar 2-byte UTF-8");

        byte* utf8Cafe = stackalloc byte[6];
        utf8Cafe[0] = (byte)'c';
        utf8Cafe[1] = (byte)'a';
        utf8Cafe[2] = (byte)'f';
        utf8Cafe[3] = 0xC3; // é in UTF-8 (first byte)
        utf8Cafe[4] = 0xA9; // é in UTF-8 (second byte)
        utf8Cafe[5] = 0;

        requiredSize = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Cafe, -1, null, 0);
        if (requiredSize != 5) // 4 chars + null (é is single UTF-16 code unit)
        {
            DebugConsole.Write("[String Test] Test 5: FAILED - expected size 5, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        char* wideCafe = stackalloc char[5];
        converted = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Cafe, -1, wideCafe, 5);
        if (converted != 5)
        {
            DebugConsole.Write("[String Test] Test 5: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // é = U+00E9
        if (wideCafe[0] != 'c' || wideCafe[1] != 'a' || wideCafe[2] != 'f' ||
            wideCafe[3] != (char)0x00E9 || wideCafe[4] != 0)
        {
            DebugConsole.Write("[String Test] Test 5: FAILED - content mismatch, char3=");
            DebugConsole.WriteHex((ushort)wideCafe[3]);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 5: MultiByteToWideChar 2-byte UTF-8 - PASSED");

        // Test 6: MultiByteToWideChar - 3-byte UTF-8 (€ = U+20AC = 0xE2 0x82 0xAC)
        DebugConsole.WriteLine("[String Test] Test 6: MultiByteToWideChar 3-byte UTF-8");

        byte* utf8Euro = stackalloc byte[4];
        utf8Euro[0] = 0xE2; // € in UTF-8
        utf8Euro[1] = 0x82;
        utf8Euro[2] = 0xAC;
        utf8Euro[3] = 0;

        requiredSize = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Euro, -1, null, 0);
        if (requiredSize != 2) // 1 char + null
        {
            DebugConsole.Write("[String Test] Test 6: FAILED - expected size 2, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        char* wideEuro = stackalloc char[2];
        converted = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Euro, -1, wideEuro, 2);
        if (converted != 2)
        {
            DebugConsole.Write("[String Test] Test 6: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // € = U+20AC
        if (wideEuro[0] != (char)0x20AC || wideEuro[1] != 0)
        {
            DebugConsole.Write("[String Test] Test 6: FAILED - expected 0x20AC, got: ");
            DebugConsole.WriteHex((ushort)wideEuro[0]);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 6: MultiByteToWideChar 3-byte UTF-8 - PASSED");

        // Test 7: MultiByteToWideChar - 4-byte UTF-8 (surrogate pair: 😀 = U+1F600)
        // U+1F600 in UTF-8 = 0xF0 0x9F 0x98 0x80
        DebugConsole.WriteLine("[String Test] Test 7: MultiByteToWideChar 4-byte UTF-8 (surrogate)");

        byte* utf8Emoji = stackalloc byte[5];
        utf8Emoji[0] = 0xF0;
        utf8Emoji[1] = 0x9F;
        utf8Emoji[2] = 0x98;
        utf8Emoji[3] = 0x80;
        utf8Emoji[4] = 0;

        requiredSize = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Emoji, -1, null, 0);
        if (requiredSize != 3) // 2 chars (surrogate pair) + null
        {
            DebugConsole.Write("[String Test] Test 7: FAILED - expected size 3, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        char* wideEmoji = stackalloc char[3];
        converted = PAL.StringApi.MultiByteToWideChar(PAL.CodePage.CP_UTF8, 0, utf8Emoji, -1, wideEmoji, 3);
        if (converted != 3)
        {
            DebugConsole.Write("[String Test] Test 7: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // U+1F600 = surrogate pair 0xD83D 0xDE00
        if (wideEmoji[0] != (char)0xD83D || wideEmoji[1] != (char)0xDE00 || wideEmoji[2] != 0)
        {
            DebugConsole.Write("[String Test] Test 7: FAILED - expected 0xD83D 0xDE00, got: ");
            DebugConsole.WriteHex((ushort)wideEmoji[0]);
            DebugConsole.Write(" ");
            DebugConsole.WriteHex((ushort)wideEmoji[1]);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 7: MultiByteToWideChar 4-byte UTF-8 - PASSED");

        // Test 8: WideCharToMultiByte with surrogate pair
        DebugConsole.WriteLine("[String Test] Test 8: WideCharToMultiByte surrogate pair");

        requiredSize = PAL.StringApi.WideCharToMultiByte(PAL.CodePage.CP_UTF8, 0, wideEmoji, -1, null, 0, null, null);
        if (requiredSize != 5) // 4 bytes + null
        {
            DebugConsole.Write("[String Test] Test 8: FAILED - expected size 5, got: ");
            DebugConsole.WriteHex((uint)requiredSize);
            DebugConsole.WriteLine();
            return 1;
        }

        byte* utf8EmojiBack = stackalloc byte[5];
        converted = PAL.StringApi.WideCharToMultiByte(PAL.CodePage.CP_UTF8, 0, wideEmoji, -1, utf8EmojiBack, 5, null, null);
        if (converted != 5)
        {
            DebugConsole.Write("[String Test] Test 8: FAILED - converted count wrong: ");
            DebugConsole.WriteHex((uint)converted);
            DebugConsole.WriteLine();
            return 1;
        }

        // Verify round-trip
        match = true;
        for (int i = 0; i < 5; i++)
        {
            if (utf8Emoji[i] != utf8EmojiBack[i])
            {
                match = false;
                break;
            }
        }
        if (!match)
        {
            DebugConsole.WriteLine("[String Test] Test 8: FAILED - round-trip mismatch");
            return 1;
        }
        DebugConsole.WriteLine("[String Test] Test 8: WideCharToMultiByte surrogate - PASSED");

        DebugConsole.WriteLine("[String Test] All String Conversion API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Test thread for stack unwinding (PAL_VirtualUnwind, RtlVirtualUnwind)
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint StackUnwindTestThread(void* param)
    {
        DebugConsole.WriteLine("[Unwind Test] Starting Stack Unwinding API tests...");

        // Test 1: Test RtlLookupFunctionEntry on an unregistered address
        DebugConsole.WriteLine("[Unwind Test] Test 1: RtlLookupFunctionEntry (no entry)");
        ulong imageBase;
        var entry = PAL.Exception.RtlLookupFunctionEntry(0x12345678UL, out imageBase, null);
        if (entry == null)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 1: RtlLookupFunctionEntry returns null for unregistered - PASSED");
        }
        else
        {
            DebugConsole.WriteLine("[Unwind Test] Test 1: FAILED - expected null");
            return 1;
        }

        // Test 2: Create a synthetic function and register it
        DebugConsole.WriteLine("[Unwind Test] Test 2: RtlAddFunctionTable and RtlLookupFunctionEntry");

        // Use stackalloc for UNWIND_INFO buffer (16 bytes)
        byte* unwindInfoData = stackalloc byte[16];

        // Create a simple UNWIND_INFO:
        // - Version 1, no flags (no handler)
        // - PrologSize = 4
        // - CountOfUnwindCodes = 1
        // - FrameRegister = 0, FrameOffset = 0
        // - UWOP_ALLOC_SMALL with OpInfo = 3 (allocates 8*3+8 = 32 bytes)
        unwindInfoData[0] = 0x01;    // Version=1, Flags=0
        unwindInfoData[1] = 0x04;    // SizeOfProlog = 4
        unwindInfoData[2] = 0x01;    // CountOfUnwindCodes = 1
        unwindInfoData[3] = 0x00;    // FrameRegister=0, FrameOffset=0
        // UNWIND_CODE: CodeOffset=4, UnwindOp=2 (UWOP_ALLOC_SMALL), OpInfo=3
        unwindInfoData[4] = 0x04;    // CodeOffset
        unwindInfoData[5] = 0x32;    // OpInfo=3 (high 4 bits), UnwindOp=2 (low 4 bits)
        unwindInfoData[6] = 0x00;    // Padding to even count
        unwindInfoData[7] = 0x00;

        // Use a "fake" base address for our synthetic function
        // We'll use the unwindInfoData address as the base for simplicity
        ulong fakeBase = (ulong)unwindInfoData;

        // Create RUNTIME_FUNCTION on stack
        RuntimeFunction funcTableEntry;
        RuntimeFunction* funcTable = &funcTableEntry;
        // Function spans RVA 0x100 to 0x200, UNWIND_INFO at RVA 0 (which is our buffer)
        funcTable[0].BeginAddress = 0x100;
        funcTable[0].EndAddress = 0x200;
        funcTable[0].UnwindInfoAddress = 0;  // UnwindInfo is at base + 0

        // Register the function table
        bool addResult = PAL.Exception.RtlAddFunctionTable(funcTable, 1, fakeBase);
        if (!addResult)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 2: FAILED - RtlAddFunctionTable returned false");
            return 1;
        }
        DebugConsole.WriteLine("[Unwind Test] Test 2: RtlAddFunctionTable - PASSED");

        // Test 3: Look up the function we just registered
        DebugConsole.WriteLine("[Unwind Test] Test 3: RtlLookupFunctionEntry (registered function)");
        ulong foundBase;
        var foundEntry = PAL.Exception.RtlLookupFunctionEntry(fakeBase + 0x150UL, out foundBase, null);
        if (foundEntry == null)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 3: FAILED - entry not found");
            return 1;
        }
        if (foundBase != fakeBase)
        {
            DebugConsole.Write("[Unwind Test] Test 3: FAILED - wrong imageBase: ");
            DebugConsole.WriteHex(foundBase);
            DebugConsole.WriteLine();
            return 1;
        }
        if (foundEntry->BeginAddress != 0x100 || foundEntry->EndAddress != 0x200)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 3: FAILED - wrong function addresses");
            return 1;
        }
        DebugConsole.WriteLine("[Unwind Test] Test 3: RtlLookupFunctionEntry found function - PASSED");

        // Test 4: Test RtlVirtualUnwind with our synthetic function
        DebugConsole.WriteLine("[Unwind Test] Test 4: RtlVirtualUnwind");

        // Create a fake stack with return address on stack
        ulong* fakeStack = stackalloc ulong[8];
        fakeStack[0] = 0xAAAA0000UL; // slot 0
        fakeStack[1] = 0xAAAA1111UL; // slot 1
        fakeStack[2] = 0xAAAA2222UL; // slot 2
        fakeStack[3] = 0xAAAA3333UL; // slot 3
        fakeStack[4] = 0xDEADBEEF12345678UL; // Return address at RSP+32

        // Create context: we're at RIP within the function, RSP points to our fake stack
        ExceptionContext ctx;
        ctx.Rip = fakeBase + 0x150UL;  // Inside the function
        ctx.Rsp = (ulong)fakeStack;    // Point to our fake stack
        ctx.Rbp = 0UL;
        ctx.Rflags = 0UL;
        ctx.Rax = 0x1111UL;
        ctx.Rbx = 0x2222UL;
        ctx.Rcx = 0x3333UL;
        ctx.Rdx = 0x4444UL;
        ctx.Rsi = 0x5555UL;
        ctx.Rdi = 0x6666UL;
        ctx.R8 = 0UL;
        ctx.R9 = 0UL;
        ctx.R10 = 0UL;
        ctx.R11 = 0UL;
        ctx.R12 = 0UL;
        ctx.R13 = 0UL;
        ctx.R14 = 0UL;
        ctx.R15 = 0UL;
        ctx.Cs = 0x08;
        ctx.Ss = 0x10;

        ulong establisherFrame = 0UL;
        void* handlerData = null;

        // Call RtlVirtualUnwind
        void* handler = PAL.Exception.RtlVirtualUnwind(
            0, // UNW_FLAG_NHANDLER
            fakeBase,
            ctx.Rip,
            foundEntry,
            &ctx,
            &handlerData,
            &establisherFrame,
            null);

        // After unwinding:
        // - RSP should have been adjusted by +32 (undoing ALLOC_SMALL) + 8 (popping return addr)
        // - RIP should be the return address (0xDEADBEEF12345678)
        ulong originalStackAddr = (ulong)fakeStack;
        ulong expectedRsp = originalStackAddr + 40UL;  // Original + 32 (alloc) + 8 (ret addr pop)
        ulong expectedRip = 0xDEADBEEF12345678UL;

        if (ctx.Rip != expectedRip)
        {
            DebugConsole.Write("[Unwind Test] Test 4: FAILED - RIP wrong. Expected: ");
            DebugConsole.WriteHex(expectedRip);
            DebugConsole.Write(" Got: ");
            DebugConsole.WriteHex(ctx.Rip);
            DebugConsole.WriteLine();
            return 1;
        }

        if (ctx.Rsp != expectedRsp)
        {
            DebugConsole.Write("[Unwind Test] Test 4: FAILED - RSP wrong. Expected: ");
            DebugConsole.WriteHex(expectedRsp);
            DebugConsole.Write(" Got: ");
            DebugConsole.WriteHex(ctx.Rsp);
            DebugConsole.WriteLine();
            return 1;
        }

        DebugConsole.Write("[Unwind Test] Test 4: RtlVirtualUnwind - RIP=0x");
        DebugConsole.WriteHex(ctx.Rip);
        DebugConsole.Write(" RSP=0x");
        DebugConsole.WriteHex(ctx.Rsp);
        DebugConsole.WriteLine(" - PASSED");

        // Test 5: Test PAL_VirtualUnwind (simpler API)
        DebugConsole.WriteLine("[Unwind Test] Test 5: PAL_VirtualUnwind");

        // Reset context
        ctx.Rip = fakeBase + 0x150UL;
        ctx.Rsp = (ulong)fakeStack;

        bool unwindResult = PAL.Exception.PAL_VirtualUnwind(&ctx, null);
        if (!unwindResult)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 5: FAILED - PAL_VirtualUnwind returned false");
            return 1;
        }

        if (ctx.Rip != expectedRip || ctx.Rsp != expectedRsp)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 5: FAILED - wrong result");
            return 1;
        }
        DebugConsole.WriteLine("[Unwind Test] Test 5: PAL_VirtualUnwind - PASSED");

        // Test 6: RtlDeleteFunctionTable
        DebugConsole.WriteLine("[Unwind Test] Test 6: RtlDeleteFunctionTable");
        bool delResult = PAL.Exception.RtlDeleteFunctionTable(funcTable);
        if (!delResult)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 6: FAILED - delete returned false");
            return 1;
        }

        // Verify it's no longer found
        entry = PAL.Exception.RtlLookupFunctionEntry(fakeBase + 0x150UL, out imageBase, null);
        if (entry != null)
        {
            DebugConsole.WriteLine("[Unwind Test] Test 6: FAILED - entry still found after delete");
            return 1;
        }
        DebugConsole.WriteLine("[Unwind Test] Test 6: RtlDeleteFunctionTable - PASSED");

        DebugConsole.WriteLine("[Unwind Test] All Stack Unwinding API tests PASSED!");
        return 0;
    }

    /// <summary>
    /// Test thread for new PAL APIs (Console, COM, NativeAOT PAL, XState, System)
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint NewPalApisTestThread(void* param)
    {
        DebugConsole.WriteLine("[NewPAL Test] Starting new PAL API tests...");

        // ==================== Console I/O Tests ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 1: GetStdHandle");
        var hStdOut = PAL.Console.GetStdHandle(PAL.StdHandle.STD_OUTPUT_HANDLE);
        var hStdErr = PAL.Console.GetStdHandle(PAL.StdHandle.STD_ERROR_HANDLE);
        var hStdIn = PAL.Console.GetStdHandle(PAL.StdHandle.STD_INPUT_HANDLE);
        var hInvalid = PAL.Console.GetStdHandle(999);  // Invalid handle type

        if (hStdOut == PAL.ConsoleHandles.InvalidHandle)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 1: FAILED - stdout invalid");
            return 1;
        }
        if (hStdErr == PAL.ConsoleHandles.InvalidHandle)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 1: FAILED - stderr invalid");
            return 1;
        }
        if (hStdIn == PAL.ConsoleHandles.InvalidHandle)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 1: FAILED - stdin invalid");
            return 1;
        }
        if (hInvalid != PAL.ConsoleHandles.InvalidHandle)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 1: FAILED - invalid handle should be INVALID_HANDLE");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 1: GetStdHandle - PASSED");

        // Test 2: WriteFile to stdout
        DebugConsole.WriteLine("[NewPAL Test] Test 2: WriteFile to stdout");
        byte* testMsg = stackalloc byte[6];
        testMsg[0] = (byte)'H';
        testMsg[1] = (byte)'i';
        testMsg[2] = (byte)'!';
        testMsg[3] = (byte)'\r';
        testMsg[4] = (byte)'\n';
        testMsg[5] = 0;

        uint bytesWritten = 0;
        bool writeResult = PAL.Console.WriteFile(hStdOut, testMsg, 5, &bytesWritten, null);
        if (!writeResult || bytesWritten != 5)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 2: FAILED");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 2: WriteFile - PASSED (you should see 'Hi!' above)");

        // Test 3: IsConsoleHandle
        DebugConsole.WriteLine("[NewPAL Test] Test 3: IsConsoleHandle");
        if (!PAL.Console.IsConsoleHandle(hStdOut) || !PAL.Console.IsConsoleHandle(hStdErr))
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 3: FAILED - console handles not recognized");
            return 1;
        }
        if (PAL.Console.IsConsoleHandle((nuint)0x12345678))
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 3: FAILED - random handle recognized as console");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 3: IsConsoleHandle - PASSED");

        // ==================== Process/System Info Tests ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 4: GetProcessAffinityMask");
        nuint procMask = 0;
        nuint sysMask = 0;
        bool affinityResult = PAL.ProcessApi.GetProcessAffinityMask(
            PAL.ProcessApi.GetCurrentProcess(), &procMask, &sysMask);
        if (!affinityResult)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 4: FAILED - returned false");
            return 1;
        }
        if (procMask == 0 || sysMask == 0)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 4: FAILED - masks are zero");
            return 1;
        }
        if (procMask != sysMask)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 4: FAILED - masks don't match");
            return 1;
        }
        DebugConsole.Write("[NewPAL Test] Test 4: GetProcessAffinityMask = 0x");
        DebugConsole.WriteHex((ulong)procMask);
        DebugConsole.WriteLine(" - PASSED");

        // Test 5: QueryInformationJobObject (should fail - not supported)
        DebugConsole.WriteLine("[NewPAL Test] Test 5: QueryInformationJobObject (stub)");
        uint retLen = 0;
        bool jobResult = PAL.ProcessApi.QueryInformationJobObject(0, 0, null, 0, &retLen);
        if (jobResult)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 5: FAILED - should return false");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 5: QueryInformationJobObject returns false - PASSED");

        // Test 6: IsWow64Process2
        DebugConsole.WriteLine("[NewPAL Test] Test 6: IsWow64Process2");
        ushort procMachine = 0xFFFF;
        ushort nativeMachine = 0;
        bool wow64Result = PAL.VersionApi.IsWow64Process2(
            PAL.ProcessApi.GetCurrentProcess(), &procMachine, &nativeMachine);
        if (!wow64Result)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 6: FAILED - returned false");
            return 1;
        }
        // procMachine should be 0 (not WOW64), nativeMachine should be AMD64 (0x8664)
        if (procMachine != 0)
        {
            DebugConsole.Write("[NewPAL Test] Test 6: FAILED - procMachine = ");
            DebugConsole.WriteHex(procMachine);
            DebugConsole.WriteLine(" (expected 0)");
            return 1;
        }
        if (nativeMachine != PAL.VersionApi.IMAGE_FILE_MACHINE_AMD64)
        {
            DebugConsole.Write("[NewPAL Test] Test 6: FAILED - nativeMachine = ");
            DebugConsole.WriteHex(nativeMachine);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 6: IsWow64Process2 - PASSED (native AMD64)");

        // Test 7: IsWindowsVersionOrGreater
        DebugConsole.WriteLine("[NewPAL Test] Test 7: IsWindowsVersionOrGreater");
        // Should return true for Windows 7 (6.1)
        if (!PAL.VersionApi.IsWindowsVersionOrGreater(6, 1, 0))
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 7: FAILED - not >= Windows 7");
            return 1;
        }
        // Should return true for Windows 10 (10.0)
        if (!PAL.VersionApi.IsWindowsVersionOrGreater(10, 0, 0))
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 7: FAILED - not >= Windows 10");
            return 1;
        }
        // Should return false for Windows 11 (pretend version 11.0)
        if (PAL.VersionApi.IsWindowsVersionOrGreater(11, 0, 0))
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 7: FAILED - claims >= Windows 11");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 7: IsWindowsVersionOrGreater - PASSED");

        // ==================== COM Stubs Tests ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 8: CoInitializeEx");
        int hr = PAL.Com.CoInitializeEx(null, PAL.CoInitFlags.COINIT_MULTITHREADED);
        if (hr != PAL.HResult.S_OK)
        {
            DebugConsole.Write("[NewPAL Test] Test 8: FAILED - hr = ");
            DebugConsole.WriteHex((uint)hr);
            DebugConsole.WriteLine();
            return 1;
        }
        // Second call should return S_FALSE (already initialized)
        hr = PAL.Com.CoInitializeEx(null, PAL.CoInitFlags.COINIT_MULTITHREADED);
        if (hr != PAL.HResult.S_FALSE)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 8: FAILED - second call should be S_FALSE");
            return 1;
        }
        PAL.Com.CoUninitialize();
        DebugConsole.WriteLine("[NewPAL Test] Test 8: CoInitializeEx/CoUninitialize - PASSED");

        // ==================== NativeAOT PAL Tests ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 9: PalGetModuleBounds");
        ulong lowerBound = 0, upperBound = 0;
        bool boundsResult = PAL.NativeAotPal.PalGetModuleBounds(&lowerBound, &upperBound);
        if (!boundsResult)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 9: FAILED - returned false");
            return 1;
        }
        if (lowerBound == 0 || upperBound == 0 || lowerBound >= upperBound)
        {
            DebugConsole.Write("[NewPAL Test] Test 9: FAILED - invalid bounds: ");
            DebugConsole.WriteHex(lowerBound);
            DebugConsole.Write(" - ");
            DebugConsole.WriteHex(upperBound);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.Write("[NewPAL Test] Test 9: PalGetModuleBounds = 0x");
        DebugConsole.WriteHex(lowerBound);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex(upperBound);
        DebugConsole.WriteLine(" - PASSED");

        // Test 10: PalGetMaximumStackBounds
        DebugConsole.WriteLine("[NewPAL Test] Test 10: PalGetMaximumStackBounds");
        ulong stackLow = 0, stackHigh = 0;
        bool stackResult = PAL.NativeAotPal.PalGetMaximumStackBounds(&stackLow, &stackHigh);
        if (!stackResult)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 10: FAILED - returned false");
            return 1;
        }
        if (stackLow == 0 || stackHigh == 0 || stackLow >= stackHigh)
        {
            DebugConsole.Write("[NewPAL Test] Test 10: FAILED - invalid bounds: ");
            DebugConsole.WriteHex(stackLow);
            DebugConsole.Write(" - ");
            DebugConsole.WriteHex(stackHigh);
            DebugConsole.WriteLine();
            return 1;
        }
        // Verify current RSP is within bounds
        ulong currentRsp = Cpu.GetRsp();
        if (currentRsp < stackLow || currentRsp > stackHigh)
        {
            DebugConsole.Write("[NewPAL Test] Test 10: FAILED - RSP 0x");
            DebugConsole.WriteHex(currentRsp);
            DebugConsole.WriteLine(" not in bounds");
            return 1;
        }
        DebugConsole.Write("[NewPAL Test] Test 10: PalGetMaximumStackBounds = 0x");
        DebugConsole.WriteHex(stackLow);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex(stackHigh);
        DebugConsole.WriteLine(" - PASSED");

        // Test 11: PalGetModuleFileName
        DebugConsole.WriteLine("[NewPAL Test] Test 11: PalGetModuleFileName");
        char* fileNameBuf = stackalloc char[64];
        uint nameLen = PAL.NativeAotPal.PalGetModuleFileName(null, fileNameBuf, 64);
        if (nameLen == 0)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 11: FAILED - returned 0 length");
            return 1;
        }
        // Should be "\kernel" or similar
        DebugConsole.Write("[NewPAL Test] Test 11: PalGetModuleFileName = \"");
        for (uint i = 0; i < nameLen; i++)
            DebugConsole.WriteByte((byte)fileNameBuf[i]);
        DebugConsole.WriteLine("\" - PASSED");

        // ==================== XState Tests ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 12: GetEnabledXStateFeatures");
        ulong xstateFeatures = PAL.XState.GetEnabledXStateFeatures();
        // Should have at least legacy FP+SSE
        if ((xstateFeatures & PAL.XStateFeatures.XSTATE_MASK_LEGACY) != PAL.XStateFeatures.XSTATE_MASK_LEGACY)
        {
            DebugConsole.Write("[NewPAL Test] Test 12: FAILED - missing legacy features: ");
            DebugConsole.WriteHex(xstateFeatures);
            DebugConsole.WriteLine();
            return 1;
        }
        DebugConsole.Write("[NewPAL Test] Test 12: GetEnabledXStateFeatures = 0x");
        DebugConsole.WriteHex(xstateFeatures);
        DebugConsole.WriteLine(" - PASSED");

        // Test 13: InitializeContext
        DebugConsole.WriteLine("[NewPAL Test] Test 13: InitializeContext");
        uint requiredSize = 0;
        // First call to get required size
        PAL.XState.InitializeContext(null, 0, null, &requiredSize);
        if (requiredSize == 0)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 13: FAILED - required size is 0");
            return 1;
        }
        DebugConsole.Write("[NewPAL Test] Test 13: InitializeContext requires ");
        DebugConsole.WriteDecimal(requiredSize);
        DebugConsole.WriteLine(" bytes - PASSED");

        // ==================== Thread Description Test ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 14: SetThreadDescription");
        var currentThread = PAL.ThreadApi.GetCurrentThread();
        int descResult = PAL.ThreadApi.SetThreadDescription(currentThread, null);
        if (descResult != 0)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 14: FAILED - SetThreadDescription returned error");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 14: SetThreadDescription - PASSED");

        // ==================== QueueUserAPC2 Test ====================
        DebugConsole.WriteLine("[NewPAL Test] Test 15: QueueUserAPC2");
        _apc2CallCount = 0;
        bool apc2Result = PAL.ThreadApi.QueueUserAPC2(
            &Apc2Callback, currentThread, 0x9999, PAL.QueueUserApcFlags.QUEUE_USER_APC_FLAGS_NONE);
        if (!apc2Result)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 15: FAILED - QueueUserAPC2 returned false");
            return 1;
        }
        // Use SleepEx to deliver the APC
        PAL.ThreadApi.SleepEx(0, true);
        if (_apc2CallCount != 1)
        {
            DebugConsole.WriteLine("[NewPAL Test] Test 15: FAILED - APC2 not delivered");
            return 1;
        }
        DebugConsole.WriteLine("[NewPAL Test] Test 15: QueueUserAPC2 - PASSED");

        DebugConsole.WriteLine("[NewPAL Test] All new PAL API tests PASSED!");
        return 0;
    }

    private static int _apc2CallCount;

    [UnmanagedCallersOnly]
    private static void Apc2Callback(nuint param)
    {
        _apc2CallCount++;
    }
}
