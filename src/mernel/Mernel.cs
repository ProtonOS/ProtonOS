// netos mernel - Managed kernel entry point
// bflat's zerolib EfiMain captures the UEFI system table, then calls Main()

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

        // Initialize architecture-specific code
        // NOTE: Using direct static calls instead of interfaces because
        // stdlib:zero doesn't support 'new' for reference types
#if ARCH_X64
        Arch.Init();
#elif ARCH_ARM64
        // TODO: Arch.Init();
#endif

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
    }
}
