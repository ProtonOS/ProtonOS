// netos mernel - Managed kernel entry point
// bflat's zerolib EfiMain captures the UEFI system table, then calls Main()

namespace Mernel;

public static unsafe class Entry
{
    public static void Main()
    {
        DebugConsole.Init();
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  netos mernel booted!");
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine();

        // Initialize GDT with our own descriptors
        Gdt.Init();

        // Initialize IDT with interrupt handlers
        Idt.Init();

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
    }
}
