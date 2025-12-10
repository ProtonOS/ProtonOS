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
    // Loaded assembly binaries (persist after ExitBootServices)
    // Note: PE bytes are loaded via UEFI before ExitBootServices,
    // then registered with AssemblyLoader after HeapAllocator.Init
    private static byte* _testAssemblyBytes;
    private static ulong _testAssemblySize;
    private static byte* _systemRuntimeBytes;
    private static ulong _systemRuntimeSize;
    private static byte* _ddkBytes;
    private static ulong _ddkSize;

    // Driver assembly data
    private static byte* _virtioDriverBytes;
    private static ulong _virtioDriverSize;
    private static byte* _virtioBlkDriverBytes;
    private static ulong _virtioBlkDriverSize;

    // Assembly IDs from AssemblyLoader (assigned after registration)
    private static uint _testAssemblyId;
    private static uint _systemRuntimeId;
    private static uint _ddkId;
    private static uint _virtioDriverId;
    private static uint _virtioBlkDriverId;

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

        // Run kernel tests
        Tests.Run();

        // Initialize String MethodTable for JIT ldstr support
        Runtime.MetadataReader.InitStringMethodTable();

        // Set up the MetadataRoot for string resolution (ldstr)
        Runtime.MetadataReader.SetMetadataRoot(GetTestMetadataRoot());

        // Initialize runtime helpers for JIT (allocation, MD array, etc.)
        Runtime.RuntimeHelpers.Init();

        // Initialize AOT method registry for well-known types (String, etc.)
        Runtime.AotMethodRegistry.Init();

        // Initialize string pool for interning and ldstr caching (requires HeapAllocator)
        Runtime.StringPool.Init();

        // Initialize assembly loader (requires HeapAllocator)
        AssemblyLoader.Initialize();

        // Register System.Runtime.dll first (dependency for other assemblies)
        if (_systemRuntimeBytes != null)
        {
            _systemRuntimeId = AssemblyLoader.Load(_systemRuntimeBytes, _systemRuntimeSize);
            if (_systemRuntimeId != AssemblyLoader.InvalidAssemblyId)
            {
                DebugConsole.Write("[Kernel] System.Runtime registered with ID ");
                DebugConsole.WriteDecimal(_systemRuntimeId);
                DebugConsole.WriteLine();
            }
        }

        // Register ProtonOS.DDK.dll (depends on System.Runtime)
        if (_ddkBytes != null)
        {
            _ddkId = AssemblyLoader.Load(_ddkBytes, _ddkSize);
            if (_ddkId != AssemblyLoader.InvalidAssemblyId)
            {
                DebugConsole.Write("[Kernel] ProtonOS.DDK registered with ID ");
                DebugConsole.WriteDecimal(_ddkId);
                DebugConsole.WriteLine();
            }
        }

        // Register driver assemblies (depend on System.Runtime and DDK)
        if (_virtioDriverBytes != null)
        {
            _virtioDriverId = AssemblyLoader.Load(_virtioDriverBytes, _virtioDriverSize);
            if (_virtioDriverId != AssemblyLoader.InvalidAssemblyId)
            {
                DebugConsole.Write("[Kernel] Virtio driver registered with ID ");
                DebugConsole.WriteDecimal(_virtioDriverId);
                DebugConsole.WriteLine();
            }
        }

        if (_virtioBlkDriverBytes != null)
        {
            _virtioBlkDriverId = AssemblyLoader.Load(_virtioBlkDriverBytes, _virtioBlkDriverSize);
            if (_virtioBlkDriverId != AssemblyLoader.InvalidAssemblyId)
            {
                DebugConsole.Write("[Kernel] VirtioBlk driver registered with ID ");
                DebugConsole.WriteDecimal(_virtioBlkDriverId);
                DebugConsole.WriteLine();
            }
        }

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

        // Initialize DDK (Driver Development Kit)
        RunDDKInit();

        // Initialize kernel exports for PInvoke resolution
        Runtime.KernelExportInit.Initialize();

        // Initialize PCI subsystem and enumerate devices
        Platform.PCI.Initialize();
        Platform.PCI.EnumerateAndPrint();

        // Bind drivers to detected PCI devices
        BindDrivers();

        // Run JIT code generation verification tests
        // These tests compile methods from FullTest.dll and verify the generated x64 code
        if (_testAssemblyId != AssemblyLoader.InvalidAssemblyId)
        {
            Runtime.JIT.JITOpcodeTests.RunAll(_testAssemblyId);
        }

        // Run FullTest assembly via JIT
        // DISABLED: RunFullTestAssembly();

        // Run GC tests (after runtime is fully initialized)
        // DISABLED: Tests.RunGCTests();

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
        DebugConsole.WriteLine("[Kernel] Loading assemblies from UEFI FS...");

        // Load System.Runtime.dll first (dependency for all assemblies)
        _systemRuntimeBytes = UEFIFS.ReadFileAscii("\\System.Runtime.dll", out _systemRuntimeSize);
        if (_systemRuntimeBytes != null)
        {
            DebugConsole.Write("[Kernel] System.Runtime.dll loaded, size ");
            DebugConsole.WriteHex(_systemRuntimeSize);
            DebugConsole.WriteLine();
        }
        else
        {
            DebugConsole.WriteLine("[Kernel] System.Runtime.dll not found (optional)");
        }

        // Load ProtonOS.DDK.dll (Driver Development Kit)
        _ddkBytes = UEFIFS.ReadFileAscii("\\ProtonOS.DDK.dll", out _ddkSize);
        if (_ddkBytes != null)
        {
            DebugConsole.Write("[Kernel] ProtonOS.DDK.dll loaded, size ");
            DebugConsole.WriteHex(_ddkSize);
            DebugConsole.WriteLine();
        }
        else
        {
            DebugConsole.WriteLine("[Kernel] ProtonOS.DDK.dll not found (optional)");
        }

        // Load driver assemblies from /drivers/
        _virtioDriverBytes = UEFIFS.ReadFileAscii("\\drivers\\ProtonOS.Drivers.Virtio.dll", out _virtioDriverSize);
        if (_virtioDriverBytes != null)
        {
            DebugConsole.Write("[Kernel] Virtio driver loaded, size ");
            DebugConsole.WriteHex(_virtioDriverSize);
            DebugConsole.WriteLine();
        }

        _virtioBlkDriverBytes = UEFIFS.ReadFileAscii("\\drivers\\ProtonOS.Drivers.VirtioBlk.dll", out _virtioBlkDriverSize);
        if (_virtioBlkDriverBytes != null)
        {
            DebugConsole.Write("[Kernel] VirtioBlk driver loaded, size ");
            DebugConsole.WriteHex(_virtioBlkDriverSize);
            DebugConsole.WriteLine();
        }

        // Load FullTest.dll
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

    /// <summary>
    /// Initialize the DDK via JIT compilation.
    /// Finds DDKInit.Initialize() and executes it.
    /// </summary>
    private static void RunDDKInit()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine("  Initializing DDK");
        DebugConsole.WriteLine("==============================");
        DebugConsole.WriteLine();

        if (_ddkId == AssemblyLoader.InvalidAssemblyId)
        {
            DebugConsole.WriteLine("[DDK] No DDK assembly loaded, skipping initialization");
            return;
        }

        DebugConsole.Write("[DDK] Assembly ID: ");
        DebugConsole.WriteDecimal(_ddkId);
        DebugConsole.WriteLine();

        // Find DDKInit type
        uint ddkInitToken = AssemblyLoader.FindTypeDefByFullName(_ddkId, "ProtonOS.DDK", "DDKInit");
        if (ddkInitToken == 0)
        {
            DebugConsole.WriteLine("[DDK] ERROR: Could not find ProtonOS.DDK.DDKInit type");
            return;
        }

        DebugConsole.Write("[DDK] Found DDKInit type, token: 0x");
        DebugConsole.WriteHex(ddkInitToken);
        DebugConsole.WriteLine();

        // Find Initialize method
        uint initializeToken = AssemblyLoader.FindMethodDefByName(_ddkId, ddkInitToken, "Initialize");
        if (initializeToken == 0)
        {
            DebugConsole.WriteLine("[DDK] ERROR: Could not find Initialize method");
            return;
        }

        DebugConsole.Write("[DDK] Found Initialize method, token: 0x");
        DebugConsole.WriteHex(initializeToken);
        DebugConsole.WriteLine();

        // JIT compile the method
        DebugConsole.WriteLine("[DDK] JIT compiling DDKInit.Initialize...");

        var jitResult = Runtime.JIT.Tier0JIT.CompileMethod(_ddkId, initializeToken);
        if (jitResult.Success)
        {
            DebugConsole.Write("[DDK] JIT compilation successful, code at 0x");
            DebugConsole.WriteHex((ulong)jitResult.CodeAddress);
            DebugConsole.WriteLine();

            // Execute the compiled method (returns bool)
            DebugConsole.WriteLine("[DDK] Executing DDKInit.Initialize()...");

            var funcPtr = (delegate* unmanaged<bool>)jitResult.CodeAddress;
            bool success = funcPtr();

            if (success)
            {
                DebugConsole.WriteLine("[DDK] Initialization successful");
            }
            else
            {
                DebugConsole.WriteLine("[DDK] Initialization failed");
            }
        }
        else
        {
            DebugConsole.WriteLine("[DDK] ERROR: JIT compilation failed");
        }
    }

    /// <summary>
    /// Bind drivers to detected PCI devices.
    /// Iterates through PCI devices and tries to match them with loaded drivers.
    /// </summary>
    private static void BindDrivers()
    {
        DebugConsole.WriteLine();
        DebugConsole.WriteLine("[Drivers] Binding drivers to PCI devices...");

        if (_virtioBlkDriverId == AssemblyLoader.InvalidAssemblyId)
        {
            DebugConsole.WriteLine("[Drivers] No VirtioBlk driver loaded, skipping binding");
            return;
        }

        // Find VirtioBlkEntry type
        uint virtioBlkEntryToken = AssemblyLoader.FindTypeDefByFullName(
            _virtioBlkDriverId, "ProtonOS.Drivers.Storage.VirtioBlk", "VirtioBlkEntry");

        if (virtioBlkEntryToken == 0)
        {
            DebugConsole.WriteLine("[Drivers] ERROR: Could not find VirtioBlkEntry type");
            return;
        }

        // Find Probe method
        uint probeToken = AssemblyLoader.FindMethodDefByName(_virtioBlkDriverId, virtioBlkEntryToken, "Probe");
        if (probeToken == 0)
        {
            DebugConsole.WriteLine("[Drivers] ERROR: Could not find Probe method");
            return;
        }

        // Find Bind method
        uint bindToken = AssemblyLoader.FindMethodDefByName(_virtioBlkDriverId, virtioBlkEntryToken, "Bind");
        if (bindToken == 0)
        {
            DebugConsole.WriteLine("[Drivers] ERROR: Could not find Bind method");
            return;
        }

        DebugConsole.Write("[Drivers] Found VirtioBlkEntry type (0x");
        DebugConsole.WriteHex(virtioBlkEntryToken);
        DebugConsole.Write("), Probe (0x");
        DebugConsole.WriteHex(probeToken);
        DebugConsole.Write("), Bind (0x");
        DebugConsole.WriteHex(bindToken);
        DebugConsole.WriteLine(")");

        // JIT compile Probe method
        DebugConsole.WriteLine("[Drivers] JIT compiling VirtioBlkEntry.Probe...");
        var probeResult = Runtime.JIT.Tier0JIT.CompileMethod(_virtioBlkDriverId, probeToken);
        if (!probeResult.Success)
        {
            DebugConsole.WriteLine("[Drivers] ERROR: Failed to JIT compile Probe");
            return;
        }

        // JIT compile Bind method
        DebugConsole.WriteLine("[Drivers] JIT compiling VirtioBlkEntry.Bind...");
        var bindResult = Runtime.JIT.Tier0JIT.CompileMethod(_virtioBlkDriverId, bindToken);
        if (!bindResult.Success)
        {
            DebugConsole.WriteLine("[Drivers] ERROR: Failed to JIT compile Bind");
            return;
        }

        DebugConsole.Write("[Drivers] Probe at 0x");
        DebugConsole.WriteHex((ulong)probeResult.CodeAddress);
        DebugConsole.Write(", Bind at 0x");
        DebugConsole.WriteHex((ulong)bindResult.CodeAddress);
        DebugConsole.WriteLine();

        // Create function pointers
        var probeFunc = (delegate* unmanaged<ushort, ushort, bool>)probeResult.CodeAddress;
        var bindFunc = (delegate* unmanaged<byte, byte, byte, bool>)bindResult.CodeAddress;

        // Iterate through detected PCI devices
        int deviceCount = Platform.PCI.DeviceCount;
        int boundCount = 0;

        DebugConsole.Write("[Drivers] Checking ");
        DebugConsole.WriteDecimal(deviceCount);
        DebugConsole.WriteLine(" PCI device(s)...");

        for (int i = 0; i < deviceCount; i++)
        {
            var device = Platform.PCI.GetDevice(i);
            if (device == null)
                continue;

            // Try VirtioBlk driver
            bool probeSuccess = probeFunc(device->VendorId, device->DeviceId);

            if (probeSuccess)
            {
                DebugConsole.Write("[Drivers] VirtioBlk matched ");
                DebugConsole.WriteHex(device->Bus);
                DebugConsole.Write(":");
                DebugConsole.WriteHex(device->Device);
                DebugConsole.Write(".");
                DebugConsole.WriteHex(device->Function);
                DebugConsole.Write(" (Vendor:");
                DebugConsole.WriteHex(device->VendorId);
                DebugConsole.Write(" Device:");
                DebugConsole.WriteHex(device->DeviceId);
                DebugConsole.WriteLine(")");

                // Bind the driver
                bool bindSuccess = bindFunc(device->Bus, device->Device, device->Function);
                if (bindSuccess)
                {
                    DebugConsole.WriteLine("[Drivers]   Bind successful");
                    boundCount++;
                }
                else
                {
                    DebugConsole.WriteLine("[Drivers]   Bind failed");
                }
            }
        }

        DebugConsole.Write("[Drivers] Bound ");
        DebugConsole.WriteDecimal(boundCount);
        DebugConsole.WriteLine(" driver(s)");
    }
}
