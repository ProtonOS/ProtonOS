// netos mernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls zerolib's EfiMain, which calls Main()

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

        // Initialize architecture-specific code (GDT, IDT)
#if ARCH_X64
        Arch.Init();
#elif ARCH_ARM64
        // TODO: Arch.Init();
#endif

        // Initialize virtual memory (our own page tables)
        VirtualMemory.Init();

        // Initialize kernel heap
        HeapAllocator.Init();

        // Second-stage arch init (timers, enable interrupts)
#if ARCH_X64
        Arch.InitStage2();
#endif

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
        DebugConsole.WriteLine("[OK] Entering idle loop...");

        // Idle loop - wait for interrupts
        while (true)
        {
            Cpu.Halt();
        }
    }
}
