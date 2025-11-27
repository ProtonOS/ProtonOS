// netos mernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls zerolib's EfiMain, which calls Main()

using Mernel.X64;

namespace Mernel;

public static unsafe class Mernel
{
    // Static buffer for memory map (8KB should be enough for most systems)
    private static MemoryMapBuffer _memMapBuffer;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private unsafe struct MemoryMapBuffer
    {
        public fixed byte Data[8192];
    }

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

        // Initialize page allocator (must be done before ExitBootServices)
        InitPageAllocator();

        // Initialize architecture-specific code
        // NOTE: Using direct static calls instead of interfaces because
        // stdlib:zero doesn't support 'new' for reference types
#if ARCH_X64
        Arch.Init();
#elif ARCH_ARM64
        // TODO: Arch.Init();
#endif

        // Initialize virtual memory
        VirtualMemory.Init();

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
    }

    private static void InitPageAllocator()
    {
        if (!UefiBoot.BootServicesAvailable)
        {
            DebugConsole.WriteLine("[UEFI] Boot services not available");
            return;
        }

        fixed (byte* buffer = _memMapBuffer.Data)
        {
            var status = UefiBoot.GetMemoryMap(
                buffer,
                8192,
                out ulong mapKey,
                out ulong descriptorSize,
                out int entryCount);

            if (status != EfiStatus.Success)
            {
                DebugConsole.Write("[UEFI] GetMemoryMap failed: 0x");
                DebugConsole.WriteHex((ulong)status);
                DebugConsole.WriteLine();
                return;
            }

            DebugConsole.Write("[UEFI] Memory map: ");
            DebugConsole.WriteHex((ushort)entryCount);
            DebugConsole.Write(" entries, descriptor size ");
            DebugConsole.WriteHex((ushort)descriptorSize);
            DebugConsole.WriteLine();

            // Find kernel extent from LoaderCode/LoaderData regions
            ulong kernelBase = 0xFFFFFFFFFFFFFFFF;
            ulong kernelTop = 0;

            for (int i = 0; i < entryCount; i++)
            {
                var desc = UefiBoot.GetDescriptor(buffer, descriptorSize, i);
                if (desc->Type == EfiMemoryType.LoaderCode ||
                    desc->Type == EfiMemoryType.LoaderData)
                {
                    ulong start = desc->PhysicalStart;
                    ulong end = start + desc->NumberOfPages * 4096;

                    if (start < kernelBase)
                        kernelBase = start;
                    if (end > kernelTop)
                        kernelTop = end;
                }
            }

            ulong kernelSize = (kernelTop > kernelBase) ? kernelTop - kernelBase : 0;

            DebugConsole.Write("[UEFI] Kernel at 0x");
            DebugConsole.WriteHex(kernelBase);
            DebugConsole.Write(" size ");
            DebugConsole.WriteHex(kernelSize / 1024);
            DebugConsole.WriteLine(" KB");

            // Initialize page allocator
            if (!PageAllocator.Init(buffer, descriptorSize, entryCount, kernelBase, kernelSize))
            {
                DebugConsole.WriteLine("[PageAlloc] Initialization failed!");
            }
        }
    }
}
