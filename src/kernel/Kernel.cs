// ProtonOS kernel - Managed kernel entry point
// EfiEntry (native.asm) saves UEFI params, then calls korlib's EfiMain, which calls Main()

using System.Runtime.InteropServices;
using ProtonOS.X64;
using ProtonOS.PAL;
using ProtonOS.Memory;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Runtime;
using ProtonOS.Runtime.JIT;

namespace ProtonOS;

public static unsafe class Kernel
{
    // Loaded test assembly binary (persists after ExitBootServices)
    // Note: PE bytes are loaded via UEFI before ExitBootServices,
    // then registered with AssemblyLoader after HeapAllocator.Init
    private static byte* _testAssemblyBytes;
    private static ulong _testAssemblySize;

    // Assembly ID from AssemblyLoader (assigned after registration)
    private static uint _testAssemblyId;

    // Cached MetadataRoot for the test assembly (for string resolution)
    // TODO: Migrate to use LoadedAssembly.Metadata instead
    private static Runtime.MetadataRoot _testMetadataRoot;

    // Cached TablesHeader and TableSizes for the test assembly (for token resolution)
    // TODO: Migrate to use LoadedAssembly.Tables/Sizes instead
    private static Runtime.TablesHeader _testTablesHeader;
    private static Runtime.TableSizes _testTableSizes;

    /// <summary>
    /// Get a pointer to the test assembly's MetadataRoot.
    /// Returns null if no assembly has been loaded/parsed.
    /// </summary>
    public static Runtime.MetadataRoot* GetTestMetadataRoot()
    {
        // Static fields have fixed addresses in the managed heap
        // This is safe because _testMetadataRoot is a static field
        fixed (Runtime.MetadataRoot* ptr = &_testMetadataRoot)
        {
            return ptr;
        }
    }

    /// <summary>
    /// Get a pointer to the test assembly's TablesHeader.
    /// </summary>
    public static Runtime.TablesHeader* GetTestTablesHeader()
    {
        fixed (Runtime.TablesHeader* ptr = &_testTablesHeader)
        {
            return ptr;
        }
    }

    /// <summary>
    /// Get a pointer to the test assembly's TableSizes.
    /// </summary>
    public static Runtime.TableSizes* GetTestTableSizes()
    {
        fixed (Runtime.TableSizes* ptr = &_testTableSizes)
        {
            return ptr;
        }
    }

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
        CurrentArch.InitStage1();

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
        CurrentArch.InitStage2();

        // Tests disabled for clean logs - call Tests.Run() to enable
        // Tests.Run();

        // Initialize String MethodTable for JIT ldstr support
        Runtime.MetadataReader.InitStringMethodTable();

        // Set up the MetadataRoot for string resolution (ldstr)
        Runtime.MetadataReader.SetMetadataRoot(GetTestMetadataRoot());

        // Initialize runtime helpers for JIT (allocation, MD array, etc.)
        Runtime.RuntimeHelpers.Init();

        // Initialize string pool for interning and ldstr caching (requires HeapAllocator)
        Runtime.StringPool.Init();

        // Initialize assembly loader (requires HeapAllocator)
        AssemblyLoader.Initialize();

        // Register the test assembly with AssemblyLoader (PE bytes were loaded from UEFI FS)
        if (_testAssemblyBytes != null)
        {
            _testAssemblyId = AssemblyLoader.Load(_testAssemblyBytes, _testAssemblySize);
            if (_testAssemblyId != AssemblyLoader.InvalidAssemblyId)
            {
                DebugConsole.Write("[Kernel] Test assembly registered with ID ");
                DebugConsole.WriteDecimal(_testAssemblyId);
                DebugConsole.WriteLine();
            }
        }

        // Initialize metadata integration layer (requires HeapAllocator)
        MetadataIntegration.Initialize();

        // Wire up metadata context for token resolution (backward compatibility)
        // TODO: Migrate to use AssemblyLoader.GetAssembly(_testAssemblyId) instead
        MetadataIntegration.SetMetadataContext(
            GetTestMetadataRoot(),
            GetTestTablesHeader(),
            GetTestTableSizes());

        // Register well-known AOT types (System.String, etc.)
        MetadataIntegration.RegisterWellKnownTypes();

        // Test CPU features and dynamic code execution (JIT prerequisites)
        // Tests.TestCPUFeatures();
        // Tests.TestDynamicCodeExecution();

        // Test IL JIT compiler (legacy tests - replaced by FullTest assembly)
        // Tests.TestILCompiler();

        // Run FullTest assembly via JIT
        RunFullTestAssembly();

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
        DebugConsole.WriteLine("[Kernel] Loading test assembly from UEFI FS...");

        _testAssemblyBytes = UEFIFS.ReadFileAscii("\\FullTest.dll", out _testAssemblySize);

        if (_testAssemblyBytes == null)
        {
            DebugConsole.WriteLine("[Kernel] Failed to load test assembly");
            return;
        }

        DebugConsole.Write("[Kernel] Test assembly loaded at 0x");
        DebugConsole.WriteHex((ulong)_testAssemblyBytes);
        DebugConsole.Write(", size ");
        DebugConsole.WriteHex(_testAssemblySize);
        DebugConsole.WriteLine();

        // Verify it looks like a PE file (MZ header)
        if (_testAssemblySize >= 2 && _testAssemblyBytes[0] == 'M' && _testAssemblyBytes[1] == 'Z')
        {
            DebugConsole.WriteLine("[Kernel] PE signature verified (MZ)");

            // Parse CLI header and metadata (still needed before ExitBootServices for dump output)
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
        var corHeader = PEHelper.GetCorHeaderFromFile(_testAssemblyBytes);
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
        var metadataRoot = (byte*)PEHelper.GetMetadataRootFromFile(_testAssemblyBytes);
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
            // Save the MetadataRoot for later use (e.g., string resolution)
            _testMetadataRoot = mdRoot;

            MetadataReader.Dump(ref mdRoot);

            // Parse #~ (tables) stream header
            if (MetadataReader.ParseTablesHeader(ref mdRoot, out var tablesHeader))
            {
                // Save for later use (token resolution)
                _testTablesHeader = tablesHeader;
                _testTableSizes = TableSizes.Calculate(ref tablesHeader);

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
                MetadataReader.TestTypeResolution(ref mdRoot, ref tablesHeader, ref _testTableSizes);

                // Test assembly identity (Phase 5.10)
                MetadataReader.TestAssemblyIdentity(ref mdRoot, ref tablesHeader, ref _testTableSizes);
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
            byte* methodBodyPtr = (byte*)PEHelper.RvaToFilePointer(_testAssemblyBytes, rva);
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

    /// <summary>
    /// Run the FullTest assembly via JIT compilation.
    /// Finds TestRunner.RunAllTests() and executes it.
    /// </summary>
    private static void RunFullTestAssembly()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  Running FullTest Assembly");
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine();

        if (_testAssemblyId == AssemblyLoader.InvalidAssemblyId)
        {
            DebugConsole.WriteLine("[FullTest] ERROR: No test assembly loaded");
            return;
        }

        DebugConsole.Write("[FullTest] Assembly ID: ");
        DebugConsole.WriteDecimal(_testAssemblyId);
        DebugConsole.WriteLine();

        // Find TestRunner type using AssemblyLoader
        uint testRunnerToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "TestRunner");
        if (testRunnerToken == 0)
        {
            DebugConsole.WriteLine("[FullTest] ERROR: Could not find FullTest.TestRunner type");
            return;
        }

        DebugConsole.Write("[FullTest] Found TestRunner type, token: 0x");
        DebugConsole.WriteHex(testRunnerToken);
        DebugConsole.WriteLine();

        // Find RunAllTests method using AssemblyLoader
        uint runAllTestsToken = AssemblyLoader.FindMethodDefByName(_testAssemblyId, testRunnerToken, "RunAllTests");
        if (runAllTestsToken == 0)
        {
            DebugConsole.WriteLine("[FullTest] ERROR: Could not find RunAllTests method");
            return;
        }

        DebugConsole.Write("[FullTest] Found RunAllTests method, token: 0x");
        DebugConsole.WriteHex(runAllTestsToken);
        DebugConsole.WriteLine();

        // JIT compile the method
        DebugConsole.WriteLine("[FullTest] JIT compiling RunAllTests...");

        // Diagnostic: dump page table walk for the crash address
        DebugConsole.WriteLine("[FullTest] Checking page table before JIT...");
        X64.VirtualMemory.DumpPageTableWalk(0xFFFF800000100020);

        // Try to read from the address to verify mapping
        unsafe
        {
            DebugConsole.Write("[FullTest] Reading from 0xFFFF800000100020: ");
            byte* ptr = (byte*)0xFFFF800000100020;
            byte val = *ptr;
            DebugConsole.WriteHex(val);
            DebugConsole.WriteLine(" (OK)");
        }

        var jitResult = Runtime.JIT.Tier0JIT.CompileMethod(_testAssemblyId, runAllTestsToken);
        if (jitResult.Success)
        {
            DebugConsole.Write("[FullTest] JIT compilation successful, code at 0x");
            DebugConsole.WriteHex((ulong)jitResult.CodeAddress);
            DebugConsole.WriteLine();

            // Execute the compiled method
            DebugConsole.WriteLine("[FullTest] Executing RunAllTests...");
            DebugConsole.WriteLine();

            // Call the compiled method (returns int)
            var funcPtr = (delegate* unmanaged<int>)jitResult.CodeAddress;
            int result = funcPtr();

            // Parse result: (passCount << 16) | failCount
            int passCount = (result >> 16) & 0xFFFF;
            int failCount = result & 0xFFFF;

            DebugConsole.WriteLine();
            DebugConsole.WriteLine("==============================");
            DebugConsole.WriteLine("  FullTest Results");
            DebugConsole.WriteLine("==============================");
            DebugConsole.Write("[FullTest] Passed: ");
            DebugConsole.WriteDecimal(passCount);
            DebugConsole.WriteLine();
            DebugConsole.Write("[FullTest] Failed: ");
            DebugConsole.WriteDecimal(failCount);
            DebugConsole.WriteLine();

            if (failCount == 0)
            {
                DebugConsole.WriteLine("[FullTest] ALL TESTS PASSED!");
            }
            else
            {
                DebugConsole.WriteLine("[FullTest] SOME TESTS FAILED");
            }
        }
        else
        {
            DebugConsole.WriteLine("[FullTest] ERROR: JIT compilation failed");
        }
    }
}
