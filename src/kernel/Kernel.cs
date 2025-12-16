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
    private static byte* _testSupportBytes;
    private static ulong _testSupportSize;
    private static byte* _ddkBytes;
    private static ulong _ddkSize;

    // Driver assembly data
    private static byte* _virtioDriverBytes;
    private static ulong _virtioDriverSize;
    private static byte* _virtioBlkDriverBytes;
    private static ulong _virtioBlkDriverSize;

    // korlib IL assembly (for JIT generic instantiation and token-based AOT lookup)
    private static byte* _korlibBytes;
    private static ulong _korlibSize;

    // Assembly IDs from AssemblyLoader (assigned after registration)
    private static uint _testAssemblyId;
    private static uint _testSupportId;
    private static uint _ddkId;
    private static uint _virtioDriverId;
    private static uint _virtioBlkDriverId;
    private static uint _korlibId;

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

        // Initialize String MethodTable for JIT ldstr support
        Runtime.MetadataReader.InitStringMethodTable();

        // Set up the MetadataRoot for string resolution (ldstr)
        Runtime.MetadataReader.SetMetadataRoot(GetTestMetadataRoot());

        // Initialize runtime helpers for JIT (allocation, MD array, etc.)
        Runtime.RuntimeHelpers.Init();

        // Initialize AOT method registry for well-known types (String, etc.)
        Runtime.AotMethodRegistry.Init();

        // Initialize JIT stubs for lazy method compilation
        Runtime.JIT.JitStubs.Init();

        // Initialize string pool for interning and ldstr caching (requires HeapAllocator)
        Runtime.StringPool.Init();

        // Initialize assembly loader (requires HeapAllocator)
        AssemblyLoader.Initialize();

        // Initialize ReflectionRuntime for type info reverse lookups (MT* -> assembly/token)
        Runtime.Reflection.ReflectionRuntime.Init();

        // Force bflat to keep virtual method vtable entries for reflection types.
        // Without this, DCE may remove vtable slots that JIT code needs.
        System.RuntimeType.ForceKeepVtableMethods();
        System.Reflection.ReflectionVtableKeeper.ForceKeepVtableMethods();

        // Register korlib.dll as CoreLib (provides IL for generic instantiation)
        if (_korlibBytes != null)
        {
            _korlibId = AssemblyLoader.Load(_korlibBytes, _korlibSize, AssemblyFlags.CoreLib);
            if (_korlibId != AssemblyLoader.InvalidAssemblyId)
            {
                // Build token-based AOT method registry from korlib metadata
                BuildKorlibTokenRegistry();

                // Build DDK token registry (maps korlib DDK methods to kernel exports)
                BuildDDKTokenRegistry();

                // Initialize critical interface types (IDisposable) from korlib
                AssemblyLoader.InitializeKorlibInterfaces(_korlibId);
            }
        }

        // Register TestSupport.dll (dependency for test assembly)
        if (_testSupportBytes != null)
        {
            _testSupportId = AssemblyLoader.Load(_testSupportBytes, _testSupportSize);
        }

        // Register ProtonOS.DDK.dll
        if (_ddkBytes != null)
        {
            _ddkId = AssemblyLoader.Load(_ddkBytes, _ddkSize);
        }

        // Register driver assemblies
        if (_virtioDriverBytes != null)
        {
            _virtioDriverId = AssemblyLoader.Load(_virtioDriverBytes, _virtioDriverSize);
        }

        if (_virtioBlkDriverBytes != null)
        {
            _virtioBlkDriverId = AssemblyLoader.Load(_virtioBlkDriverBytes, _virtioBlkDriverSize);
        }

        // Register the test assembly with AssemblyLoader (depends on TestSupport and DDK)
        if (_testAssemblyBytes != null)
        {
            _testAssemblyId = AssemblyLoader.Load(_testAssemblyBytes, _testAssemblySize);
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

        // Initialize DDK (Driver Development Kit)
        RunDDKInit();

        // Initialize kernel exports for PInvoke resolution
        Runtime.KernelExportInit.Initialize();

        // Initialize PCI subsystem and enumerate devices
        Platform.PCI.Initialize();
        Platform.PCI.EnumerateAndPrint();

        // Bind drivers to detected PCI devices
        BindDrivers();

        // Run the FullTest assembly to exercise JIT functionality
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
        // Load TestSupport.dll (dependency for test assembly)
        _testSupportBytes = UEFIFS.ReadFileAscii("\\TestSupport.dll", out _testSupportSize);

        // Load ProtonOS.DDK.dll (Driver Development Kit)
        _ddkBytes = UEFIFS.ReadFileAscii("\\ProtonOS.DDK.dll", out _ddkSize);

        // Load driver assemblies from /drivers/
        _virtioDriverBytes = UEFIFS.ReadFileAscii("\\drivers\\ProtonOS.Drivers.Virtio.dll", out _virtioDriverSize);
        _virtioBlkDriverBytes = UEFIFS.ReadFileAscii("\\drivers\\ProtonOS.Drivers.VirtioBlk.dll", out _virtioBlkDriverSize);

        // Load korlib.dll (IL assembly for JIT generic instantiation)
        _korlibBytes = UEFIFS.ReadFileAscii("\\korlib.dll", out _korlibSize);

        // Load FullTest.dll
        _testAssemblyBytes = UEFIFS.ReadFileAscii("\\FullTest.dll", out _testAssemblySize);

        if (_testAssemblyBytes == null)
        {
            DebugConsole.WriteLine("[Kernel] Failed to load test assembly");
            return;
        }

        // Verify it looks like a PE file (MZ header)
        if (_testAssemblySize >= 2 && _testAssemblyBytes[0] == 'M' && _testAssemblyBytes[1] == 'Z')
        {
            // Parse CLI header and metadata (still needed before ExitBootServices)
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

        // Get metadata root (using file-based RVA translation)
        var metadataRoot = (byte*)PEHelper.GetMetadataRootFromFile(_testAssemblyBytes);
        if (metadataRoot == null)
        {
            DebugConsole.WriteLine("[Kernel] Failed to locate metadata");
            return;
        }

        // Verify BSJB signature
        uint signature = *(uint*)metadataRoot;
        if (signature != PEConstants.METADATA_SIGNATURE)
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

            // MetadataReader.Dump(ref mdRoot);

            // Parse #~ (tables) stream header
            if (MetadataReader.ParseTablesHeader(ref mdRoot, out var tablesHeader))
            {
                // Save for later use (token resolution)
                _testTablesHeader = tablesHeader;
                _testTableSizes = TableSizes.Calculate(ref tablesHeader);

                // MetadataReader.DumpTablesHeader(ref tablesHeader);

                // Test heap access by reading Module and TypeDef tables
                // MetadataReader.DumpModuleTable(ref mdRoot, ref tablesHeader);
                // MetadataReader.DumpTypeDefTable(ref mdRoot, ref tablesHeader);

                // Test new table accessors with TypeRef, MethodDef, MemberRef, AssemblyRef
                // MetadataReader.DumpTypeRefTable(ref mdRoot, ref tablesHeader);
                // MetadataReader.DumpMethodDefTable(ref mdRoot, ref tablesHeader);
                // MetadataReader.DumpMemberRefTable(ref mdRoot, ref tablesHeader);
                // MetadataReader.DumpAssemblyRefTable(ref mdRoot, ref tablesHeader);

                // Test IL method body parsing
                // DumpMethodBodies(ref mdRoot, ref tablesHeader);

                // Test type resolution (Phase 5.9)
                // MetadataReader.TestTypeResolution(ref mdRoot, ref tablesHeader, ref _testTableSizes);

                // Test assembly identity (Phase 5.10)
                // MetadataReader.TestAssemblyIdentity(ref mdRoot, ref tablesHeader, ref _testTableSizes);
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

        DebugConsole.WriteLine(string.Format("[FullTest] Assembly ID: {0}", _testAssemblyId));

        // Find TestRunner type using AssemblyLoader
        uint testRunnerToken = AssemblyLoader.FindTypeDefByFullName(_testAssemblyId, "FullTest", "TestRunner");
        if (testRunnerToken == 0)
        {
            DebugConsole.WriteLine("[FullTest] ERROR: Could not find FullTest.TestRunner type");
            return;
        }

        DebugConsole.WriteLine(string.Format("[FullTest] Found TestRunner type, token: 0x{0}", testRunnerToken.ToString("X8", null)));

        // Find RunAllTests method using AssemblyLoader
        uint runAllTestsToken = AssemblyLoader.FindMethodDefByName(_testAssemblyId, testRunnerToken, "RunAllTests");
        if (runAllTestsToken == 0)
        {
            DebugConsole.WriteLine("[FullTest] ERROR: Could not find RunAllTests method");
            return;
        }

        DebugConsole.WriteLine(string.Format("[FullTest] Found RunAllTests method, token: 0x{0}", runAllTestsToken.ToString("X8", null)));

        // JIT compile the method
        DebugConsole.WriteLine("[FullTest] JIT compiling RunAllTests...");

        var jitResult = Runtime.JIT.Tier0JIT.CompileMethod(_testAssemblyId, runAllTestsToken);
        if (jitResult.Success)
        {
            DebugConsole.WriteLine(string.Format("[FullTest] JIT compilation successful, code at 0x{0}", ((ulong)jitResult.CodeAddress).ToString("X", null)));

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
            DebugConsole.WriteLine(string.Format("[FullTest] Passed: {0}", passCount));
            DebugConsole.WriteLine(string.Format("[FullTest] Failed: {0}", failCount));

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
    /// Build the token-based AOT method registry from korlib.dll metadata.
    /// Maps korlib method tokens to native code addresses.
    /// </summary>
    private static void BuildKorlibTokenRegistry()
    {
        DebugConsole.WriteLine("[Kernel] Building korlib token registry...");

        // Get korlib assembly
        LoadedAssembly* korlib = AssemblyLoader.GetAssembly(_korlibId);
        if (korlib == null)
        {
            DebugConsole.WriteLine("[Kernel] ERROR: korlib assembly not found");
            return;
        }

        // Initialize the token registry
        AotMethodRegistry.InitTokenRegistry();

        // Get MethodDef table row count
        uint methodDefCount = korlib->Tables.RowCounts[(int)MetadataTableId.MethodDef];

        int registered = 0;
        int matched = 0;

        // Iterate through all MethodDef entries
        for (uint row = 1; row <= methodDefCount; row++)
        {
            // Get method name
            uint nameIdx = MetadataReader.GetMethodDefName(ref korlib->Tables, ref korlib->Sizes, row);
            byte* methodName = MetadataReader.GetString(ref korlib->Metadata, nameIdx);

            // Get declaring type for this method
            uint typeDefRow = FindOwningTypeDef(korlib, row);
            if (typeDefRow == 0)
                continue;

            // Get type name and namespace
            uint typeNameIdx = MetadataReader.GetTypeDefName(ref korlib->Tables, ref korlib->Sizes, typeDefRow);
            uint typeNsIdx = MetadataReader.GetTypeDefNamespace(ref korlib->Tables, ref korlib->Sizes, typeDefRow);
            byte* typeName = MetadataReader.GetString(ref korlib->Metadata, typeNameIdx);
            byte* typeNs = MetadataReader.GetString(ref korlib->Metadata, typeNsIdx);

            // Build full type name (namespace.typename)
            byte* fullTypeName = stackalloc byte[256];
            int pos = 0;

            // Copy namespace
            if (typeNs != null && typeNs[0] != 0)
            {
                for (int i = 0; typeNs[i] != 0 && pos < 254; i++)
                    fullTypeName[pos++] = typeNs[i];
                fullTypeName[pos++] = (byte)'.';
            }

            // Copy type name
            if (typeName != null)
            {
                for (int i = 0; typeName[i] != 0 && pos < 255; i++)
                    fullTypeName[pos++] = typeName[i];
            }
            fullTypeName[pos] = 0;

            // Try to find this method in the hash-based AOT registry
            // Get method flags to determine if static
            ushort methodFlags = MetadataReader.GetMethodDefFlags(ref korlib->Tables, ref korlib->Sizes, row);
            bool isStatic = (methodFlags & 0x0010) != 0; // Static flag

            // Try lookup in hash-based registry
            AotMethodEntry hashEntry;
            if (AotMethodRegistry.TryLookup(fullTypeName, methodName, 0, out hashEntry, false))
            {
                // Found! Register in token registry
                uint methodToken = 0x06000000 | row;
                AotMethodFlags flags = hashEntry.Flags;

                AotMethodRegistry.RegisterByToken(_korlibId, methodToken, hashEntry.NativeCode, flags);
                registered++;
                matched++;
            }
        }

        DebugConsole.Write("[Kernel] Scanned ");
        DebugConsole.WriteDecimal((int)methodDefCount);
        DebugConsole.Write(" methods, registered ");
        DebugConsole.WriteDecimal(registered);
        DebugConsole.WriteLine(" in token registry");
    }

    /// <summary>
    /// Find the TypeDef row that owns a MethodDef.
    /// Uses the MethodList field to determine ownership.
    /// </summary>
    private static uint FindOwningTypeDef(LoadedAssembly* asm, uint methodRid)
    {
        uint typeDefCount = asm->Tables.RowCounts[(int)MetadataTableId.TypeDef];

        for (uint row = 1; row <= typeDefCount; row++)
        {
            uint methodListStart = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, row);
            uint methodListEnd;

            if (row < typeDefCount)
                methodListEnd = MetadataReader.GetTypeDefMethodList(ref asm->Tables, ref asm->Sizes, row + 1);
            else
                methodListEnd = asm->Tables.RowCounts[(int)MetadataTableId.MethodDef] + 1;

            if (methodRid >= methodListStart && methodRid < methodListEnd)
                return row;
        }

        return 0;
    }

    /// <summary>
    /// Build token registry entries for korlib DDK types.
    /// Maps korlib DDK method tokens to kernel export addresses.
    /// This enables JIT code to call korlib DDK methods directly.
    /// </summary>
    private static void BuildDDKTokenRegistry()
    {
        if (_korlibId == AssemblyLoader.InvalidAssemblyId)
            return;

        LoadedAssembly* korlib = AssemblyLoader.GetAssembly(_korlibId);
        if (korlib == null)
            return;

        int registered = 0;

        // Register Memory API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "AllocatePage",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.MemoryExports.AllocatePage);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "AllocatePages",
            (void*)(delegate* unmanaged<ulong, ulong>)&Exports.DDK.MemoryExports.AllocatePages);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "FreePage",
            (void*)(delegate* unmanaged<ulong, void>)&Exports.DDK.MemoryExports.FreePage);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "FreePages",
            (void*)(delegate* unmanaged<ulong, ulong, void>)&Exports.DDK.MemoryExports.FreePages);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "PhysToVirt",
            (void*)(delegate* unmanaged<ulong, ulong>)&Exports.DDK.MemoryExports.PhysToVirt);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "VirtToPhys",
            (void*)(delegate* unmanaged<ulong, ulong>)&Exports.DDK.MemoryExports.VirtToPhys);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "MapMMIO",
            (void*)(delegate* unmanaged<ulong, ulong, ulong>)&Exports.DDK.MemoryExports.MapMMIO);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "UnmapMMIO",
            (void*)(delegate* unmanaged<ulong, ulong, void>)&Exports.DDK.MemoryExports.UnmapMMIO);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "GetTotalMemory",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.MemoryExports.GetTotalMemory);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "GetFreeMemory",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.MemoryExports.GetFreeMemory);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Memory", "GetPageSize",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.MemoryExports.GetPageSize);

        // Register Debug API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWrite",
            (void*)(delegate* unmanaged<char*, int, void>)&Exports.DDK.DebugExports.DebugWrite);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteLine",
            (void*)(delegate* unmanaged<char*, int, void>)&Exports.DDK.DebugExports.DebugWriteLine);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteHex64",
            (void*)(delegate* unmanaged<ulong, void>)&Exports.DDK.DebugExports.DebugWriteHex64);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteHex32",
            (void*)(delegate* unmanaged<uint, void>)&Exports.DDK.DebugExports.DebugWriteHex32);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteHex16",
            (void*)(delegate* unmanaged<ushort, void>)&Exports.DDK.DebugExports.DebugWriteHex16);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteHex8",
            (void*)(delegate* unmanaged<byte, void>)&Exports.DDK.DebugExports.DebugWriteHex8);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteDecimal",
            (void*)(delegate* unmanaged<int, void>)&Exports.DDK.DebugExports.DebugWriteDecimal);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteDecimalU",
            (void*)(delegate* unmanaged<uint, void>)&Exports.DDK.DebugExports.DebugWriteDecimalU);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Debug", "Kernel_DebugWriteDecimal64",
            (void*)(delegate* unmanaged<ulong, void>)&Exports.DDK.DebugExports.DebugWriteDecimal64);

        // Register PortIO API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "InByte",
            (void*)(delegate* unmanaged<ushort, byte>)&Exports.DDK.PortIOExports.InByte);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "OutByte",
            (void*)(delegate* unmanaged<ushort, byte, void>)&Exports.DDK.PortIOExports.OutByte);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "InWord",
            (void*)(delegate* unmanaged<ushort, ushort>)&Exports.DDK.PortIOExports.InWord);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "OutWord",
            (void*)(delegate* unmanaged<ushort, ushort, void>)&Exports.DDK.PortIOExports.OutWord);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "InDword",
            (void*)(delegate* unmanaged<ushort, uint>)&Exports.DDK.PortIOExports.InDword);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PortIO", "OutDword",
            (void*)(delegate* unmanaged<ushort, uint, void>)&Exports.DDK.PortIOExports.OutDword);

        // Register CPU API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetCpuCount",
            (void*)(delegate* unmanaged<int>)&Exports.DDK.CPUExports.GetCpuCount);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetCurrentCpu",
            (void*)(delegate* unmanaged<int>)&Exports.DDK.CPUExports.GetCurrentCpu);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetCpuInfo",
            (void*)(delegate* unmanaged<int, Platform.CpuInfo*, bool>)&Exports.DDK.CPUExports.GetCpuInfo);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "SetThreadAffinity",
            (void*)(delegate* unmanaged<ulong, ulong>)&Exports.DDK.CPUExports.SetThreadAffinity);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetThreadAffinity",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.CPUExports.GetThreadAffinity);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "IsCpuOnline",
            (void*)(delegate* unmanaged<int, bool>)&Exports.DDK.CPUExports.IsCpuOnline);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetBspIndex",
            (void*)(delegate* unmanaged<int>)&Exports.DDK.CPUExports.GetBspIndex);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "CPU", "GetSystemAffinityMask",
            (void*)(delegate* unmanaged<ulong>)&Exports.DDK.CPUExports.GetSystemAffinityMask);

        // Register PCI API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "ReadConfig32",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, uint>)&Exports.DDK.PCIExports.ReadConfig32);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "ReadConfig16",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, ushort>)&Exports.DDK.PCIExports.ReadConfig16);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "ReadConfig8",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, byte>)&Exports.DDK.PCIExports.ReadConfig8);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "WriteConfig32",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, uint, void>)&Exports.DDK.PCIExports.WriteConfig32);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "WriteConfig16",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, ushort, void>)&Exports.DDK.PCIExports.WriteConfig16);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "WriteConfig8",
            (void*)(delegate* unmanaged<byte, byte, byte, byte, byte, void>)&Exports.DDK.PCIExports.WriteConfig8);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "GetBar",
            (void*)(delegate* unmanaged<byte, byte, byte, int, uint>)&Exports.DDK.PCIExports.GetBar);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "GetBarSize",
            (void*)(delegate* unmanaged<byte, byte, byte, int, uint>)&Exports.DDK.PCIExports.GetBarSize);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "EnableMemorySpace",
            (void*)(delegate* unmanaged<byte, byte, byte, void>)&Exports.DDK.PCIExports.EnableMemorySpace);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "PCI", "EnableBusMaster",
            (void*)(delegate* unmanaged<byte, byte, byte, void>)&Exports.DDK.PCIExports.EnableBusMaster);

        // Register Thread API
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "CreateThread",
            (void*)(delegate* unmanaged<delegate* unmanaged<void*, uint>, void*, nuint, uint, uint*, Threading.Thread*>)&Exports.DDK.ThreadExports.CreateThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "ExitThread",
            (void*)(delegate* unmanaged<uint, void>)&Exports.DDK.ThreadExports.ExitThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "GetCurrentThreadId",
            (void*)(delegate* unmanaged<uint>)&Exports.DDK.ThreadExports.GetCurrentThreadId);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "GetCurrentThread",
            (void*)(delegate* unmanaged<Threading.Thread*>)&Exports.DDK.ThreadExports.GetCurrentThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "Sleep",
            (void*)(delegate* unmanaged<uint, void>)&Exports.DDK.ThreadExports.Sleep);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "Yield",
            (void*)(delegate* unmanaged<void>)&Exports.DDK.ThreadExports.Yield);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "GetExitCodeThread",
            (void*)(delegate* unmanaged<Threading.Thread*, uint*, bool>)&Exports.DDK.ThreadExports.GetExitCodeThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "GetThreadState",
            (void*)(delegate* unmanaged<Threading.Thread*, int>)&Exports.DDK.ThreadExports.GetThreadState);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "SuspendThread",
            (void*)(delegate* unmanaged<Threading.Thread*, int>)&Exports.DDK.ThreadExports.SuspendThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "ResumeThread",
            (void*)(delegate* unmanaged<Threading.Thread*, int>)&Exports.DDK.ThreadExports.ResumeThread);
        registered += RegisterDDKMethod(korlib, "ProtonOS.Kernel", "Thread", "GetThreadCount",
            (void*)(delegate* unmanaged<int>)&Exports.DDK.ThreadExports.GetThreadCount);

        if (registered > 0)
        {
            DebugConsole.Write("[Kernel] Registered ");
            DebugConsole.WriteDecimal(registered);
            DebugConsole.WriteLine(" DDK methods in token registry");
        }
    }

    /// <summary>
    /// Helper to register a single DDK method in the token registry.
    /// </summary>
    private static int RegisterDDKMethod(LoadedAssembly* korlib, string ns, string typeName, string methodName, void* nativeAddr)
    {
        // Find type in korlib
        uint typeToken = AssemblyLoader.FindTypeDefByFullName(_korlibId, ns, typeName);
        if (typeToken == 0)
            return 0;

        // Find method in type
        uint methodToken = AssemblyLoader.FindMethodDefByName(_korlibId, typeToken, methodName);
        if (methodToken == 0)
            return 0;

        // Register in token registry
        AotMethodRegistry.RegisterByToken(_korlibId, methodToken, (nint)nativeAddr, AotMethodFlags.None);
        return 1;
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

        DebugConsole.WriteLine(string.Format("[DDK] Assembly ID: {0}", _ddkId));

        // Find DDKInit type
        uint ddkInitToken = AssemblyLoader.FindTypeDefByFullName(_ddkId, "ProtonOS.DDK", "DDKInit");
        if (ddkInitToken == 0)
        {
            DebugConsole.WriteLine("[DDK] ERROR: Could not find ProtonOS.DDK.DDKInit type");
            return;
        }

        DebugConsole.WriteLine(string.Format("[DDK] Found DDKInit type, token: 0x{0}", ddkInitToken.ToString("X8", null)));

        // Find Initialize method
        uint initializeToken = AssemblyLoader.FindMethodDefByName(_ddkId, ddkInitToken, "Initialize");
        if (initializeToken == 0)
        {
            DebugConsole.WriteLine("[DDK] ERROR: Could not find Initialize method");
            return;
        }

        DebugConsole.WriteLine(string.Format("[DDK] Found Initialize method, token: 0x{0}", initializeToken.ToString("X8", null)));

        // JIT compile the method
        DebugConsole.WriteLine("[DDK] JIT compiling DDKInit.Initialize...");

        var jitResult = Runtime.JIT.Tier0JIT.CompileMethod(_ddkId, initializeToken);
        if (jitResult.Success)
        {
            DebugConsole.WriteLine(string.Format("[DDK] JIT compilation successful, code at 0x{0}", ((ulong)jitResult.CodeAddress).ToString("X", null)));

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

        DebugConsole.WriteLine(string.Format("[Drivers] Found VirtioBlkEntry (0x{0}) Probe (0x{1}) Bind (0x{2})",
            virtioBlkEntryToken.ToString("X8", null), probeToken.ToString("X8", null), bindToken.ToString("X8", null)));

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

        DebugConsole.WriteLine(string.Format("[Drivers] Probe at 0x{0} Bind at 0x{1}",
            ((ulong)probeResult.CodeAddress).ToString("X", null), ((ulong)bindResult.CodeAddress).ToString("X", null)));

        // Create function pointers
        var probeFunc = (delegate* unmanaged<ushort, ushort, bool>)probeResult.CodeAddress;
        var bindFunc = (delegate* unmanaged<byte, byte, byte, bool>)bindResult.CodeAddress;

        // Iterate through detected PCI devices
        int deviceCount = Platform.PCI.DeviceCount;
        int boundCount = 0;

        DebugConsole.WriteLine(string.Format("[Drivers] Checking {0} PCI device(s)...", deviceCount));

        for (int i = 0; i < deviceCount; i++)
        {
            var device = Platform.PCI.GetDevice(i);
            if (device == null)
                continue;

            // Try VirtioBlk driver
            bool probeSuccess = probeFunc(device->VendorId, device->DeviceId);

            if (probeSuccess)
            {
                // Test string.Format with byte (Bus/Device/Function) and ushort (VendorId/DeviceId)
                DebugConsole.WriteLine(string.Format("[Drivers] VirtioBlk matched {0}:{1}.{2} (Vendor:{3} Device:{4})",
                    device->Bus.ToString("X2", null), device->Device.ToString("X2", null), device->Function.ToString("X2", null),
                    device->VendorId.ToString("X4", null), device->DeviceId.ToString("X4", null)));

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

        DebugConsole.WriteLine(string.Format("[Drivers] Bound {0} driver(s)", boundCount));
    }
}
