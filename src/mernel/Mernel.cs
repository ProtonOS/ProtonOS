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

    /// <summary>
    /// Create test threads to demonstrate scheduling
    /// </summary>
    private static void CreateTestThreads()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Test] Creating test threads...");

        // Create two test threads
        uint id1, id2;
        var thread1 = KernelScheduler.CreateThread(&TestThread1, null, 0, 0, out id1);
        var thread2 = KernelScheduler.CreateThread(&TestThread2, null, 0, 0, out id2);

        if (thread1 != null && thread2 != null)
        {
            DebugConsole.Write("[Test] Created threads ");
            DebugConsole.WriteHex((ushort)id1);
            DebugConsole.Write(" and ");
            DebugConsole.WriteHex((ushort)id2);
            DebugConsole.WriteLine();
        }
    }

    /// <summary>
    /// Test thread 1 - prints 'A' periodically
    /// </summary>
    [UnmanagedCallersOnly]
    private static uint TestThread1(void* param)
    {
        uint count = 0;
        while (count < 10)
        {
            DebugConsole.Write("A");
            count++;

            // Busy wait to consume time slice
            for (ulong i = 0; i < 10_000_000; i++)
            {
                Cpu.Pause();
            }
        }

        DebugConsole.WriteLine();
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
        while (count < 10)
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
}
