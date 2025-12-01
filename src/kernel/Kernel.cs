// ProtonOS kernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls korlib's EfiMain, which calls Main()

using System.Runtime.InteropServices;
using ProtonOS.X64;
using ProtonOS.PAL;
using ProtonOS.Memory;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Runtime;

namespace ProtonOS;

public static unsafe class Kernel
{
    // Loaded test assembly (persists after ExitBootServices)
    private static byte* _testAssembly;
    private static ulong _testAssemblySize;

    public static void Main()
    {
        DebugConsole.Init();
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  ProtonOS kernel booted!");
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine();

        // Verify we have access to UEFI system table
        var systemTable = UEFIBoot.SystemTable;
        DebugConsole.Write("[UEFI] SystemTable at 0x");
        DebugConsole.WriteHex((ulong)systemTable);
        if (systemTable != null && UEFIBoot.BootServicesAvailable)
        {
            DebugConsole.Write(" BootServices at 0x");
            DebugConsole.WriteHex((ulong)systemTable->BootServices);
        }
        DebugConsole.WriteLine();

        // Initialize ReadyToRun info (must be before anything needing runtime metadata)
        ReadyToRunInfo.Init();
        // ReadyToRunInfo.DumpSections();

        // Test GCDesc parsing with frozen objects
        // GCDescHelper.TestWithFrozenObjects();

        // Load test assembly from boot device (must be before PageAllocator.Init
        // so the memory map snapshot includes our allocation)
        LoadTestAssembly();

        // Initialize page allocator (requires UEFI boot services)
        PageAllocator.Init();

        // Initialize ACPI (requires UEFI - must be before ExitBootServices)
        ACPI.Init();

        // Exit UEFI boot services - we now own the hardware
        UEFIBoot.ExitBootServices();

        // Initialize architecture-specific code (GDT, IDT, virtual memory)
#if ARCH_X64
        Arch.Init();
#elif ARCH_ARM64
        // TODO: Arch.Init();
#endif

        // Initialize kernel heap
        HeapAllocator.Init();

        // Initialize GC heap (managed object heap with proper object headers)
        GCHeap.Init();

        // Initialize static GC fields (must be after GC heap, before using any static object fields)
        InitializeStatics.Init();

        // Initialize garbage collector (must be after GCHeap and PageAllocator)
        GC.Init();

        // Initialize code heap for JIT (must be after VirtualMemory)
        CodeHeap.Init();

        // Test GCDesc with heap-allocated object that has references
        // GCDescHelper.TestWithHeapObject();

        // Initialize scheduler (creates boot thread)
        Scheduler.Init();

        // Initialize PAL subsystems
        TLS.Init();
        PAL.Memory.Init();

        // Second-stage arch init (timers, enable interrupts)
#if ARCH_X64
        Arch.InitStage2();
#endif

        // Tests disabled for clean logs - call Tests.Run() to enable
        // Tests.Run();

        // Test CPU features and dynamic code execution (JIT prerequisites)
        Tests.TestCPUFeatures();
        Tests.TestDynamicCodeExecution();

        // Test IL JIT compiler
        Tests.TestILCompiler();

        // Enable preemptive scheduling
        Scheduler.EnableScheduling();

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[OK] Kernel initialization complete");
        DebugConsole.WriteLine("[OK] Boot thread entering idle loop...");

        // Boot thread becomes idle thread - wait for interrupts
        while (true)
        {
            CPU.Halt();
        }
    }

    /// <summary>
    /// Load the test assembly from the boot device filesystem.
    /// Must be called before ExitBootServices.
    /// </summary>
    private static void LoadTestAssembly()
    {
        DebugConsole.WriteLine("[Kernel] Loading test assembly...");

        _testAssembly = UEFIFS.ReadFileAscii("\\MetadataTest.dll", out _testAssemblySize);

        if (_testAssembly == null)
        {
            DebugConsole.WriteLine("[Kernel] Failed to load test assembly");
            return;
        }

        DebugConsole.Write("[Kernel] Test assembly loaded at 0x");
        DebugConsole.WriteHex((ulong)_testAssembly);
        DebugConsole.Write(", size ");
        DebugConsole.WriteHex(_testAssemblySize);
        DebugConsole.WriteLine();

        // Verify it looks like a PE file (MZ header)
        if (_testAssemblySize >= 2 && _testAssembly[0] == 'M' && _testAssembly[1] == 'Z')
        {
            DebugConsole.WriteLine("[Kernel] PE signature verified (MZ)");

            // Parse CLI header and metadata
            ParseAssemblyMetadata();
        }
        else
        {
            DebugConsole.WriteLine("[Kernel] WARNING: Not a valid PE file");
        }
    }

    /// <summary>
    /// Parse the CLI header and metadata from the loaded test assembly.
    /// The assembly is loaded as raw file bytes, not memory-mapped.
    /// </summary>
    private static void ParseAssemblyMetadata()
    {
        // Get CLI header (using file-based RVA translation)
        var corHeader = PEHelper.GetCorHeaderFromFile(_testAssembly);
        if (corHeader == null)
        {
            DebugConsole.WriteLine("[Kernel] No CLI header found");
            return;
        }

        DebugConsole.Write("[Kernel] CLI header: runtime ");
        DebugConsole.WriteDecimal(corHeader->MajorRuntimeVersion);
        DebugConsole.Write(".");
        DebugConsole.WriteDecimal(corHeader->MinorRuntimeVersion);
        DebugConsole.Write(", flags 0x");
        DebugConsole.WriteHex(corHeader->Flags);
        DebugConsole.WriteLine();

        DebugConsole.Write("[Kernel] Metadata RVA 0x");
        DebugConsole.WriteHex(corHeader->MetaData.VirtualAddress);
        DebugConsole.Write(", size 0x");
        DebugConsole.WriteHex(corHeader->MetaData.Size);
        DebugConsole.WriteLine();

        // Get metadata root (using file-based RVA translation)
        var metadataRoot = (byte*)PEHelper.GetMetadataRootFromFile(_testAssembly);
        if (metadataRoot == null)
        {
            DebugConsole.WriteLine("[Kernel] Failed to locate metadata");
            return;
        }

        // Verify BSJB signature
        uint signature = *(uint*)metadataRoot;
        if (signature == PEConstants.METADATA_SIGNATURE)
        {
            DebugConsole.WriteLine("[Kernel] Metadata signature verified (BSJB)");
        }
        else
        {
            DebugConsole.Write("[Kernel] Invalid metadata signature: 0x");
            DebugConsole.WriteHex(signature);
            DebugConsole.WriteLine();
            return;
        }

        // Parse metadata streams
        if (MetadataReader.Init(metadataRoot, corHeader->MetaData.Size, out var mdRoot))
        {
            MetadataReader.Dump(ref mdRoot);

            // Parse #~ (tables) stream header
            if (MetadataReader.ParseTablesHeader(ref mdRoot, out var tablesHeader))
            {
                MetadataReader.DumpTablesHeader(ref tablesHeader);

                // Test heap access by reading Module and TypeDef tables
                MetadataReader.DumpModuleTable(ref mdRoot, ref tablesHeader);
                MetadataReader.DumpTypeDefTable(ref mdRoot, ref tablesHeader);

                // Test new table accessors with TypeRef, MethodDef, MemberRef, AssemblyRef
                MetadataReader.DumpTypeRefTable(ref mdRoot, ref tablesHeader);
                MetadataReader.DumpMethodDefTable(ref mdRoot, ref tablesHeader);
                MetadataReader.DumpMemberRefTable(ref mdRoot, ref tablesHeader);
                MetadataReader.DumpAssemblyRefTable(ref mdRoot, ref tablesHeader);

                // Test IL method body parsing
                DumpMethodBodies(ref mdRoot, ref tablesHeader);

                // Test type resolution (Phase 5.9)
                var sizes = TableSizes.Calculate(ref tablesHeader);
                MetadataReader.TestTypeResolution(ref mdRoot, ref tablesHeader, ref sizes);

                // Test assembly identity (Phase 5.10)
                MetadataReader.TestAssemblyIdentity(ref mdRoot, ref tablesHeader, ref sizes);
            }
            else
            {
                DebugConsole.WriteLine("[Kernel] Failed to parse #~ header");
            }
        }
        else
        {
            DebugConsole.WriteLine("[Kernel] Failed to parse metadata");
        }
    }

    /// <summary>
    /// Parse and dump IL method bodies for all methods in the assembly
    /// </summary>
    private static void DumpMethodBodies(ref MetadataRoot mdRoot, ref TablesHeader tablesHeader)
    {
        uint methodCount = tablesHeader.RowCounts[(int)MetadataTableId.MethodDef];
        if (methodCount == 0)
            return;

        var sizes = TableSizes.Calculate(ref tablesHeader);

        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Kernel] Parsing method bodies and signatures:");

        for (uint i = 1; i <= methodCount; i++)
        {
            uint rva = MetadataReader.GetMethodDefRva(ref tablesHeader, ref sizes, i);
            uint nameIdx = MetadataReader.GetMethodDefName(ref tablesHeader, ref sizes, i);
            uint sigIdx = MetadataReader.GetMethodDefSignature(ref tablesHeader, ref sizes, i);

            DebugConsole.Write("[Kernel]   Method ");
            MetadataReader.PrintString(ref mdRoot, nameIdx);
            DebugConsole.WriteLine(":");

            // Parse and display method signature
            byte* sigBlob = MetadataReader.GetBlob(ref mdRoot, sigIdx, out uint sigLen);
            if (sigBlob != null && sigLen > 0)
            {
                DebugConsole.Write("[Kernel]     Sig: ");
                if (SignatureReader.ReadMethodSignature(sigBlob, sigLen, out var methodSig))
                {
                    SignatureReader.PrintMethodSignature(ref methodSig);
                    DebugConsole.WriteLine();
                }
                else
                {
                    DebugConsole.WriteLine("failed to parse");
                }
            }

            if (rva == 0)
            {
                // Abstract, extern, or runtime-implemented method (no IL body)
                DebugConsole.WriteLine("[Kernel]     no IL body");
                continue;
            }

            // Convert RVA to file pointer
            byte* methodBodyPtr = (byte*)PEHelper.RvaToFilePointer(_testAssembly, rva);
            if (methodBodyPtr == null)
            {
                DebugConsole.WriteLine("[Kernel]     failed to resolve RVA");
                continue;
            }

            // Parse the method body
            if (MetadataReader.ReadMethodBody(methodBodyPtr, out var body))
            {
                MetadataReader.DumpMethodBody(ref body);
            }
            else
            {
                DebugConsole.WriteLine("[Kernel]     failed to parse body");
            }
        }
    }
}
