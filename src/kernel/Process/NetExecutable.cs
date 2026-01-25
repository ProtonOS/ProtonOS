// ProtonOS kernel - .NET Executable Loader
// Loads and executes .NET assemblies in user mode.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Runtime;
using ProtonOS.Runtime.JIT;
using ProtonOS.IO;
using ProtonOS.Threading;
using ProtonOS.X64;

namespace ProtonOS.Process;

/// <summary>
/// Loads and executes .NET assemblies as user-mode processes
/// </summary>
public static unsafe class NetExecutable
{
    // Code region for user-mode JIT'd code
    private const ulong UserCodeBase = 0x10000000;
    private const ulong UserCodeSize = 0x1000000; // 16MB for code

    // Data region for user-mode data (args, strings, etc.)
    private const ulong UserDataBase = 0x11000000;
    private const ulong UserDataSize = 0x100000; // 1MB for data

    // Element types from ECMA-335
    private const byte ELEMENT_TYPE_VOID = 0x01;
    private const byte ELEMENT_TYPE_I4 = 0x08;
    private const byte ELEMENT_TYPE_STRING = 0x0E;
    private const byte ELEMENT_TYPE_SZARRAY = 0x1D;

    // Assembly helper to jump to user mode
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void jump_to_ring3(ulong userRip, ulong userRsp);

    /// <summary>
    /// Execute a .NET assembly, replacing the current process
    /// </summary>
    /// <param name="proc">Current process to replace</param>
    /// <param name="path">Path to the .dll file</param>
    /// <param name="argv">Argument vector (null-terminated array of string pointers)</param>
    /// <param name="envp">Environment vector (null-terminated array of string pointers)</param>
    /// <returns>Does not return on success, negative errno on failure</returns>
    public static long Exec(Process* proc, byte* path, byte** argv, byte** envp)
    {
        if (proc == null || path == null)
            return -Errno.EINVAL;

        // Get path length
        int pathLen = 0;
        while (path[pathLen] != 0 && pathLen < 4096)
            pathLen++;

        if (pathLen == 0)
            return -Errno.ENOENT;

        DebugConsole.Write("[NetExec] Loading: ");
        for (int i = 0; i < pathLen; i++)
            DebugConsole.WriteChar((char)path[i]);
        DebugConsole.WriteLine();

        // Try to load from boot image first (for early testing)
        byte* assemblyData = null;
        ulong assemblySize = 0;

        // Extract filename from path for boot image lookup
        int lastSlash = -1;
        for (int i = 0; i < pathLen; i++)
            if (path[i] == '/')
                lastSlash = i;

        byte* filename = lastSlash >= 0 ? path + lastSlash + 1 : path;
        int filenameLen = pathLen - (lastSlash >= 0 ? lastSlash + 1 : 0);

        // Try boot image first
        assemblyData = TryLoadFromBootImage(filename, filenameLen, out assemblySize);

        if (assemblyData == null)
        {
            // Try VFS
            assemblyData = TryLoadFromVfs(path, pathLen, out assemblySize);
        }

        if (assemblyData == null)
        {
            DebugConsole.WriteLine("[NetExec] Assembly not found");
            return -Errno.ENOENT;
        }

        DebugConsole.Write("[NetExec] Loaded assembly, size=");
        DebugConsole.WriteDecimal((int)assemblySize);
        DebugConsole.WriteLine();

        // Load the assembly
        uint assemblyId = AssemblyLoader.Load(assemblyData, assemblySize);
        if (assemblyId == 0)
        {
            DebugConsole.WriteLine("[NetExec] Failed to parse assembly");
            return -Errno.ENOEXEC;
        }

        // Get entry point from CLI header
        var asm = AssemblyLoader.GetAssembly(assemblyId);
        if (asm == null)
        {
            DebugConsole.WriteLine("[NetExec] Failed to get assembly");
            return -Errno.ENOEXEC;
        }

        uint entryPointToken = GetEntryPointToken(asm);
        if (entryPointToken == 0)
        {
            DebugConsole.WriteLine("[NetExec] No entry point found");
            return -Errno.ENOEXEC;
        }

        DebugConsole.Write("[NetExec] Entry point token: 0x");
        DebugConsole.WriteHex(entryPointToken);
        DebugConsole.WriteLine();

        // Analyze Main signature to see if it takes string[] args
        var mainSigInfo = GetMainSignatureInfo(asm, entryPointToken);

        // JIT compile the entry point
        var jitResult = Tier0JIT.CompileMethod(assemblyId, entryPointToken);
        if (!jitResult.Success || jitResult.CodeAddress == null)
        {
            DebugConsole.WriteLine("[NetExec] JIT compilation failed");
            return -Errno.ENOEXEC;
        }

        DebugConsole.Write("[NetExec] JIT'd code at 0x");
        DebugConsole.WriteHex((ulong)jitResult.CodeAddress);
        DebugConsole.Write(", size=");
        DebugConsole.WriteDecimal(jitResult.CodeSize);
        DebugConsole.WriteLine();

        // Set up user address space
        // We need to create a startup stub that:
        // 1. If Main takes args: load args into RCX
        // 2. Calls Main()
        // 3. Calls exit(return_value)
        //
        // Layout:
        //   UserCodeBase + 0:    startup stub (32 bytes with args, 16 bytes without)
        //   UserCodeBase + StubSize: JIT'd Main() code

        // Stub size depends on whether we need to pass args
        int StubSize = mainSigInfo.TakesStringArrayArg ? 32 : 16;
        ulong totalCodeSize = (ulong)StubSize + (ulong)jitResult.CodeSize;
        ulong codePages = (totalCodeSize + 4095) / 4096;

        // Create new address space if needed or reuse existing
        if (proc->PageTableRoot == 0)
        {
            proc->PageTableRoot = AddressSpace.CreateUserSpace();
            if (proc->PageTableRoot == 0)
            {
                DebugConsole.WriteLine("[NetExec] Failed to create address space");
                return -Errno.ENOMEM;
            }
        }
        // Note: For a full exec, we'd want to clear existing mappings
        // For now, we're creating a fresh process

        // Allocate all code pages first
        ulong* physPages = stackalloc ulong[(int)codePages];
        for (ulong i = 0; i < codePages; i++)
        {
            physPages[i] = PageAllocator.AllocatePage();
            if (physPages[i] == 0)
            {
                DebugConsole.WriteLine("[NetExec] Out of memory for code");
                return -Errno.ENOMEM;
            }

            // Zero the page
            byte* page = (byte*)VirtualMemory.PhysToVirt(physPages[i]);
            for (int j = 0; j < 4096; j++)
                page[j] = 0;
        }

        // Map all code pages into user space first (before creating data region)
        ulong userCodeAddr = UserCodeBase;
        for (ulong i = 0; i < codePages; i++)
        {
            // Map into user space with execute permission (no NoExecute flag)
            AddressSpace.MapUserPage(proc->PageTableRoot, userCodeAddr + i * 4096, physPages[i],
                PageFlags.Present | PageFlags.User);
        }

        // Initialize user data region for managed objects (strings, arrays)
        UserDataAllocator dataAlloc;
        if (!InitUserDataRegion(proc, out dataAlloc))
        {
            DebugConsole.WriteLine("[NetExec] Failed to init user data region");
            return -Errno.ENOMEM;
        }

        // Set up user stack (and create args array if needed)
        ulong argsArrayAddr;
        ulong userRsp = SetupUserStack(proc, argv, envp, mainSigInfo.TakesStringArrayArg,
                                        ref dataAlloc, out argsArrayAddr);
        if (userRsp == 0)
        {
            DebugConsole.WriteLine("[NetExec] Failed to set up stack");
            return -Errno.ENOMEM;
        }

        // Write startup stub to first page
        byte* stubPage = (byte*)VirtualMemory.PhysToVirt(physPages[0]);

        if (mainSigInfo.TakesStringArrayArg)
        {
            // Stub with args (32 bytes):
            // movabs rcx, <args_addr>  ; 48 B9 <imm64>  - load args array into first param
            // call rel32               ; E8 <rel32>     - call Main
            // mov edi, eax             ; 89 C7          - move return value to exit arg
            // mov eax, 60              ; B8 3C 00 00 00 - SYS_EXIT
            // syscall                  ; 0F 05
            // ud2                      ; 0F 0B

            int pos = 0;

            // movabs rcx, imm64 (48 B9 + 8-byte immediate)
            stubPage[pos++] = 0x48;
            stubPage[pos++] = 0xB9;
            *(ulong*)(stubPage + pos) = argsArrayAddr;
            pos += 8;

            // call rel32 (E8 xx xx xx xx)
            // Displacement from end of call instruction to Main
            // call is at position 10-14, ends at 15, Main is at StubSize (32)
            // displacement = 32 - 15 = 17
            stubPage[pos++] = 0xE8;
            int callDisp = StubSize - (pos + 4);  // displacement = target - (current + 4)
            *(int*)(stubPage + pos) = callDisp;
            pos += 4;

            // mov edi, eax (89 C7)
            stubPage[pos++] = 0x89;
            stubPage[pos++] = 0xC7;

            // mov eax, 60 (B8 3C 00 00 00)
            stubPage[pos++] = 0xB8;
            stubPage[pos++] = 0x3C;
            stubPage[pos++] = 0x00;
            stubPage[pos++] = 0x00;
            stubPage[pos++] = 0x00;

            // syscall (0F 05)
            stubPage[pos++] = 0x0F;
            stubPage[pos++] = 0x05;

            // ud2 (0F 0B)
            stubPage[pos++] = 0x0F;
            stubPage[pos++] = 0x0B;
        }
        else
        {
            // Stub without args (16 bytes):
            // call rel32               ; E8 <rel32>     - call Main
            // mov edi, eax             ; 89 C7          - move return value to exit arg
            // mov eax, 60              ; B8 3C 00 00 00 - SYS_EXIT
            // syscall                  ; 0F 05
            // ud2                      ; 0F 0B

            // call rel32 (E8 xx xx xx xx) - call Main at offset 16
            // Displacement is from end of instruction (offset 5) to target (offset 16) = 11
            stubPage[0] = 0xE8;
            stubPage[1] = 0x0B; // 11 = StubSize - 5
            stubPage[2] = 0x00;
            stubPage[3] = 0x00;
            stubPage[4] = 0x00;

            // mov edi, eax (89 C7)
            stubPage[5] = 0x89;
            stubPage[6] = 0xC7;

            // mov eax, 60 (B8 3C 00 00 00) - SYS_EXIT
            stubPage[7] = 0xB8;
            stubPage[8] = 0x3C;
            stubPage[9] = 0x00;
            stubPage[10] = 0x00;
            stubPage[11] = 0x00;

            // syscall (0F 05)
            stubPage[12] = 0x0F;
            stubPage[13] = 0x05;

            // ud2 (0F 0B)
            stubPage[14] = 0x0F;
            stubPage[15] = 0x0B;
        }

        // Copy JIT'd Main code after stub
        byte* src = (byte*)jitResult.CodeAddress;
        for (int offset = 0; offset < jitResult.CodeSize; offset++)
        {
            int destOffset = StubSize + offset;
            int pageIndex = destOffset / 4096;
            int pageOffset = destOffset % 4096;
            byte* dstPage = (byte*)VirtualMemory.PhysToVirt(physPages[pageIndex]);
            dstPage[pageOffset] = src[offset];
        }

        // Update process name from filename
        for (int i = 0; i < 15 && i < filenameLen; i++)
            proc->Name[i] = filename[i];
        proc->Name[filenameLen < 15 ? filenameLen : 15] = 0;

        // Initialize memory regions for the new exec'd image
        proc->ImageBase = userCodeAddr;
        proc->ImageSize = codePages * 4096;
        proc->MmapBase = 0x20000000; // Start mmap region after code
        proc->MmapRegions = null;
        proc->HeapStart = 0x30000000; // Heap starts here
        proc->HeapEnd = proc->HeapStart;

        DebugConsole.Write("[NetExec] Jumping to user mode at 0x");
        DebugConsole.WriteHex(userCodeAddr);
        DebugConsole.WriteLine();

        // Associate current thread with process
        var currentThread = Scheduler.CurrentThread;
        if (currentThread != null)
        {
            currentThread->Process = proc;
            currentThread->IsUserMode = true;
        }

        // Switch to process address space and jump to user mode
        AddressSpace.SwitchTo(proc->PageTableRoot);
        jump_to_ring3(userCodeAddr, userRsp);

        // Should never return
        return -Errno.ENOEXEC;
    }

    /// <summary>
    /// Information about the Main method signature
    /// </summary>
    private struct MainSignatureInfo
    {
        public bool TakesStringArrayArg;  // Main(string[] args)
        public bool ReturnsInt;           // int Main() vs void Main()
    }

    /// <summary>
    /// Analyze the entry point's signature to determine if it takes string[] args
    /// </summary>
    private static MainSignatureInfo GetMainSignatureInfo(LoadedAssembly* asm, uint entryPointToken)
    {
        var info = new MainSignatureInfo { TakesStringArrayArg = false, ReturnsInt = true };

        if (asm == null)
            return info;

        // Get method RID from token
        uint methodRid = entryPointToken & 0x00FFFFFF;
        if (methodRid == 0)
            return info;

        // Get signature blob index
        uint sigIdx = MetadataReader.GetMethodDefSignature(ref asm->Tables, ref asm->Sizes, methodRid);
        if (sigIdx == 0)
            return info;

        // Get the signature blob
        uint sigLen;
        byte* sig = MetadataReader.GetBlob(ref asm->Metadata, sigIdx, out sigLen);
        if (sig == null || sigLen < 3)
            return info;

        int pos = 0;

        // Read calling convention (byte 0)
        // 0x00 = default (static), 0x20 = hasthis (instance)
        byte callingConv = sig[pos++];

        // Read parameter count (compressed uint, usually 1 byte for small counts)
        uint paramCount = 0;
        if (sig[pos] < 0x80)
        {
            paramCount = sig[pos++];
        }
        else if (sig[pos] < 0xC0)
        {
            paramCount = (uint)(((sig[pos] & 0x3F) << 8) | sig[pos + 1]);
            pos += 2;
        }
        else
        {
            // 4-byte encoding (unlikely for Main)
            paramCount = (uint)(((sig[pos] & 0x1F) << 24) | (sig[pos + 1] << 16) | (sig[pos + 2] << 8) | sig[pos + 3]);
            pos += 4;
        }

        // Read return type
        byte retType = sig[pos++];
        info.ReturnsInt = (retType == ELEMENT_TYPE_I4);

        // Check if Main takes string[] args (1 parameter that is SZARRAY of STRING)
        if (paramCount == 1 && pos + 1 < sigLen)
        {
            // Parameter type should be ELEMENT_TYPE_SZARRAY followed by ELEMENT_TYPE_STRING
            if (sig[pos] == ELEMENT_TYPE_SZARRAY && sig[pos + 1] == ELEMENT_TYPE_STRING)
            {
                info.TakesStringArrayArg = true;
            }
        }

        DebugConsole.Write("[NetExec] Main signature: params=");
        DebugConsole.WriteDecimal(paramCount);
        DebugConsole.Write(", returnsInt=");
        DebugConsole.WriteDecimal(info.ReturnsInt ? 1u : 0u);
        DebugConsole.Write(", takesArgs=");
        DebugConsole.WriteDecimal(info.TakesStringArrayArg ? 1u : 0u);
        DebugConsole.WriteLine();

        return info;
    }

    /// <summary>
    /// Get entry point token from assembly's CLI header
    /// </summary>
    private static uint GetEntryPointToken(LoadedAssembly* asm)
    {
        if (asm == null || asm->ImageBase == null)
            return 0;

        // Parse PE to find CLI header
        byte* image = asm->ImageBase;

        // DOS header
        if (image[0] != 'M' || image[1] != 'Z')
            return 0;

        uint peOffset = *(uint*)(image + 0x3C);
        byte* peHeader = image + peOffset;

        // PE signature
        if (peHeader[0] != 'P' || peHeader[1] != 'E')
            return 0;

        // COFF header starts at PE + 4
        ushort numSections = *(ushort*)(peHeader + 6);
        ushort optHeaderSize = *(ushort*)(peHeader + 20);

        // Optional header
        byte* optHeader = peHeader + 24;
        ushort magic = *(ushort*)optHeader;

        // Get CLI header RVA from data directories
        uint cliHeaderRva;
        if (magic == 0x20B) // PE32+
        {
            // Data directories start at offset 112 in PE32+ optional header
            // CLI header is directory index 14
            cliHeaderRva = *(uint*)(optHeader + 112 + 14 * 8);
        }
        else // PE32
        {
            // Data directories start at offset 96 in PE32 optional header
            cliHeaderRva = *(uint*)(optHeader + 96 + 14 * 8);
        }

        if (cliHeaderRva == 0)
            return 0;

        // Convert RVA to file offset using section headers
        byte* sectionTable = optHeader + optHeaderSize;
        uint cliHeaderOffset = 0;

        for (int i = 0; i < numSections; i++)
        {
            byte* section = sectionTable + i * 40;
            uint virtualAddr = *(uint*)(section + 12);
            uint virtualSize = *(uint*)(section + 8);
            uint rawDataPtr = *(uint*)(section + 20);

            if (cliHeaderRva >= virtualAddr && cliHeaderRva < virtualAddr + virtualSize)
            {
                cliHeaderOffset = rawDataPtr + (cliHeaderRva - virtualAddr);
                break;
            }
        }

        if (cliHeaderOffset == 0)
            return 0;

        // Read entry point token from CLI header
        // EntryPointTokenOrRVA is at offset 20 in ImageCor20Header
        uint entryPoint = *(uint*)(image + cliHeaderOffset + 20);

        return entryPoint;
    }

    /// <summary>
    /// Try to load assembly from boot image
    /// </summary>
    private static byte* TryLoadFromBootImage(byte* filename, int filenameLen, out ulong size)
    {
        size = 0;

        // Build a string from the filename for FindFile
        // FindFile does case-insensitive search
        if (StringEquals(filename, filenameLen, "AppTest.dll"))
        {
            return BootInfoAccess.FindFile("AppTest.dll", out size);
        }
        if (StringEquals(filename, filenameLen, "JITTest.dll"))
        {
            return BootInfoAccess.FindFile("JITTest.dll", out size);
        }
        if (StringEquals(filename, filenameLen, "korlib.dll"))
        {
            return BootInfoAccess.FindFile("korlib.dll", out size);
        }
        if (StringEquals(filename, filenameLen, "ProtonOS.DDK.dll"))
        {
            return BootInfoAccess.FindFile("ProtonOS.DDK.dll", out size);
        }
        if (StringEquals(filename, filenameLen, "HelloApp.dll"))
        {
            return BootInfoAccess.FindFile("HelloApp.dll", out size);
        }
        if (StringEquals(filename, filenameLen, "ArgsApp.dll"))
        {
            return BootInfoAccess.FindFile("ArgsApp.dll", out size);
        }

        // For other files, try using GetLoadedFiles
        uint count;
        var files = BootInfoAccess.GetLoadedFiles(out count);
        if (files == null || count == 0)
            return null;

        for (uint i = 0; i < count; i++)
        {
            var file = &files[i];
            byte* fileNamePtr = file->GetNameBytes();
            int fileNameLen = file->NameLength;

            // Compare names (case-insensitive)
            if (fileNameLen == filenameLen)
            {
                bool match = true;
                for (int j = 0; j < filenameLen; j++)
                {
                    byte c1 = fileNamePtr[j];
                    byte c2 = filename[j];
                    // Simple ASCII lowercase
                    if (c1 >= 'A' && c1 <= 'Z') c1 = (byte)(c1 + 32);
                    if (c2 >= 'A' && c2 <= 'Z') c2 = (byte)(c2 + 32);
                    if (c1 != c2)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    size = file->Size;
                    return (byte*)file->PhysicalAddress;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Try to load assembly from VFS
    /// </summary>
    private static byte* TryLoadFromVfs(byte* path, int pathLen, out ulong size)
    {
        size = 0;

        // Open file (O_RDONLY = 0)
        int vfsHandle = VFS.Open(path, 0, 0);
        if (vfsHandle < 0)
            return null;

        // Get file size by seeking to end
        long fileSize = VFS.Seek(vfsHandle, 0, 2); // SEEK_END
        if (fileSize <= 0)
        {
            VFS.Close(vfsHandle);
            return null;
        }

        // Seek back to start
        VFS.Seek(vfsHandle, 0, 0); // SEEK_SET

        // Allocate buffer
        byte* buffer = (byte*)HeapAllocator.Alloc((ulong)fileSize);
        if (buffer == null)
        {
            VFS.Close(vfsHandle);
            return null;
        }

        // Read entire file
        long bytesRead = 0;
        while (bytesRead < fileSize)
        {
            int toRead = (int)(fileSize - bytesRead);
            if (toRead > 4096) toRead = 4096;

            int read = VFS.Read(vfsHandle, buffer + bytesRead, toRead);
            if (read <= 0)
                break;
            bytesRead += read;
        }

        VFS.Close(vfsHandle);

        if (bytesRead != fileSize)
        {
            HeapAllocator.Free(buffer);
            return null;
        }

        size = (ulong)fileSize;
        return buffer;
    }

    /// <summary>
    /// State for user-space data allocation
    /// </summary>
    private struct UserDataAllocator
    {
        public ulong PageTableRoot;
        public ulong VirtualBase;
        public ulong PhysicalBase;
        public ulong CurrentOffset;
        public ulong TotalSize;
    }

    /// <summary>
    /// Initialize user-space data region for allocating managed objects
    /// </summary>
    private static bool InitUserDataRegion(Process* proc, out UserDataAllocator allocator)
    {
        allocator = default;

        // Allocate a single page for user data (strings, arrays, etc.)
        ulong physPage = PageAllocator.AllocatePage();
        if (physPage == 0)
            return false;

        // Zero the page
        byte* page = (byte*)VirtualMemory.PhysToVirt(physPage);
        for (int i = 0; i < 4096; i++)
            page[i] = 0;

        // Map into user space
        AddressSpace.MapUserPage(proc->PageTableRoot, UserDataBase, physPage,
            PageFlags.Present | PageFlags.User | PageFlags.Writable | PageFlags.NoExecute);

        allocator.PageTableRoot = proc->PageTableRoot;
        allocator.VirtualBase = UserDataBase;
        allocator.PhysicalBase = physPage;
        allocator.CurrentOffset = 0;
        allocator.TotalSize = 4096;

        return true;
    }

    /// <summary>
    /// Allocate space in user data region and return both virtual and physical addresses
    /// </summary>
    private static ulong AllocUserData(ref UserDataAllocator alloc, ulong size, out byte* physAddr)
    {
        physAddr = null;

        // Align to 8 bytes
        size = (size + 7) & ~7UL;

        if (alloc.CurrentOffset + size > alloc.TotalSize)
            return 0; // Out of space

        ulong virtAddr = alloc.VirtualBase + alloc.CurrentOffset;
        physAddr = (byte*)VirtualMemory.PhysToVirt(alloc.PhysicalBase) + alloc.CurrentOffset;
        alloc.CurrentOffset += size;

        return virtAddr;
    }

    /// <summary>
    /// Create a managed String object in user space from a null-terminated byte array (UTF-8)
    /// Returns the user-space virtual address of the String object
    /// </summary>
    private static ulong CreateUserString(ref UserDataAllocator alloc, byte* utf8Str, int utf8Len)
    {
        if (utf8Str == null)
            return 0;

        // For simplicity, assume ASCII (1:1 mapping to UTF-16)
        // String layout: [MethodTable* (8)] [int _length (4)] [char _firstChar... (2*len)] [null (2)]
        int charCount = utf8Len;
        ulong size = (ulong)(8 + 4 + (charCount + 1) * 2);

        byte* physAddr;
        ulong virtAddr = AllocUserData(ref alloc, size, out physAddr);
        if (virtAddr == 0)
            return 0;

        // Get String MethodTable
        void* stringMT = MetadataReader.GetStringMethodTable();
        if (stringMT == null)
        {
            DebugConsole.WriteLine("[NetExec] String MethodTable not initialized!");
            return 0;
        }

        // Write String object
        *(void**)physAddr = stringMT;              // MethodTable*
        *(int*)(physAddr + 8) = charCount;         // _length
        ushort* chars = (ushort*)(physAddr + 12);  // _firstChar
        for (int i = 0; i < charCount; i++)
        {
            chars[i] = utf8Str[i];  // ASCII to UTF-16
        }
        chars[charCount] = 0;  // null terminator

        return virtAddr;
    }

    /// <summary>
    /// Create a managed string[] array in user space from argv
    /// Returns the user-space virtual address of the array
    /// </summary>
    private static ulong CreateUserStringArray(ref UserDataAllocator alloc, byte** argv, int argc)
    {
        // First, create all the String objects and store their virtual addresses
        ulong* stringAddrs = stackalloc ulong[argc];
        for (int i = 0; i < argc; i++)
        {
            if (argv[i] == null)
            {
                stringAddrs[i] = 0;
                continue;
            }

            // Get string length
            int len = 0;
            while (argv[i][len] != 0 && len < 4096)
                len++;

            stringAddrs[i] = CreateUserString(ref alloc, argv[i], len);
        }

        // Now create the string[] array
        // Array layout: [MethodTable* (8)] [int _length (4)] [padding (4)] [elements (8*argc)]
        ulong arraySize = (ulong)(16 + argc * 8);

        byte* physAddr;
        ulong virtAddr = AllocUserData(ref alloc, arraySize, out physAddr);
        if (virtAddr == 0)
            return 0;

        // Get string[] MethodTable
        MethodTable* stringMT = (MethodTable*)MetadataReader.GetStringMethodTable();
        if (stringMT == null)
            return 0;

        MethodTable* arrayMT = AssemblyLoader.GetOrCreateArrayMethodTable(stringMT);
        if (arrayMT == null)
        {
            DebugConsole.WriteLine("[NetExec] Failed to get string[] MethodTable");
            return 0;
        }

        // Write array header
        *(MethodTable**)physAddr = arrayMT;        // MethodTable*
        *(int*)(physAddr + 8) = argc;              // Length
        // [12-15] is padding

        // Write element pointers
        ulong* elements = (ulong*)(physAddr + 16);
        for (int i = 0; i < argc; i++)
        {
            elements[i] = stringAddrs[i];
        }

        DebugConsole.Write("[NetExec] Created string[] at 0x");
        DebugConsole.WriteHex(virtAddr);
        DebugConsole.Write(" with ");
        DebugConsole.WriteDecimal((uint)argc);
        DebugConsole.WriteLine(" elements");

        return virtAddr;
    }

    /// <summary>
    /// Set up user stack with argc, argv, envp
    /// </summary>
    private static ulong SetupUserStack(Process* proc, byte** argv, byte** envp,
                                         bool needsArgs, ref UserDataAllocator dataAlloc, out ulong argsArrayAddr)
    {
        argsArrayAddr = 0;

        // Allocate stack pages (8MB stack)
        const ulong stackSize = 8 * 1024 * 1024;
        const ulong stackPages = stackSize / 4096;
        const ulong stackTop = 0x7FFFFFFF0000UL;
        const ulong stackBase = stackTop - stackSize;

        for (ulong i = 0; i < stackPages; i++)
        {
            ulong physPage = PageAllocator.AllocatePage();
            if (physPage == 0)
                return 0;

            // Zero the page via physical map
            byte* page = (byte*)VirtualMemory.PhysToVirt(physPage);
            for (int j = 0; j < 4096; j++)
                page[j] = 0;

            AddressSpace.MapUserPage(proc->PageTableRoot, stackBase + i * 4096, physPage,
                PageFlags.Present | PageFlags.User | PageFlags.Writable | PageFlags.NoExecute);
        }

        // Count argc
        int argc = 0;
        if (argv != null)
        {
            while (argv[argc] != null)
                argc++;
        }

        int envc = 0;
        if (envp != null)
        {
            while (envp[envc] != null)
                envc++;
        }

        // If Main needs args, create the managed string[] array
        if (needsArgs && argc > 0)
        {
            argsArrayAddr = CreateUserStringArray(ref dataAlloc, argv, argc);
            DebugConsole.Write("[NetExec] Args array at 0x");
            DebugConsole.WriteHex(argsArrayAddr);
            DebugConsole.WriteLine();
        }
        else if (needsArgs)
        {
            // Main takes args but none provided - create empty array
            argsArrayAddr = CreateUserStringArray(ref dataAlloc, null, 0);
        }

        // Set up initial stack pointer - align to 16 bytes
        ulong rsp = stackTop - 8;
        rsp &= ~0xFUL;

        proc->StackTop = stackTop;
        proc->StackBottom = stackBase;

        return rsp;
    }

    /// <summary>
    /// Compare byte array to string literal
    /// </summary>
    private static bool StringEquals(byte* a, int aLen, string b)
    {
        if (aLen != b.Length)
            return false;
        for (int i = 0; i < aLen; i++)
        {
            if (a[i] != (byte)b[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Test execve by creating a process and exec'ing HelloApp.dll
    /// </summary>
    public static void TestExecHelloApp()
    {
        DebugConsole.WriteLine("[NetExec] Testing execve with HelloApp.dll...");

        // Create a new process
        var proc = ProcessTable.Allocate();
        if (proc == null)
        {
            DebugConsole.WriteLine("[NetExec] Failed to allocate process");
            return;
        }

        DebugConsole.Write("[NetExec] Created process PID ");
        DebugConsole.WriteDecimal(proc->Pid);
        DebugConsole.WriteLine();

        // Set up init process attributes
        proc->ParentPid = 0;
        proc->Uid = 0;
        proc->Gid = 0;
        proc->Euid = 0;
        proc->Egid = 0;
        proc->State = ProcessState.Running;

        // Initialize file descriptor table with stdio
        // Allocate fd table (minimum 64 entries)
        proc->FdTableSize = 64;
        proc->FdTable = (FileDescriptor*)HeapAllocator.Alloc((ulong)(proc->FdTableSize * sizeof(FileDescriptor)));
        if (proc->FdTable != null)
        {
            // Zero the table
            for (int i = 0; i < proc->FdTableSize; i++)
            {
                proc->FdTable[i] = default;
            }

            // stdin (fd 0)
            proc->FdTable[0].Type = FileType.Terminal;
            proc->FdTable[0].Flags = FileFlags.ReadOnly;
            proc->FdTable[0].RefCount = 1;

            // stdout (fd 1)
            proc->FdTable[1].Type = FileType.Terminal;
            proc->FdTable[1].Flags = FileFlags.WriteOnly;
            proc->FdTable[1].RefCount = 1;

            // stderr (fd 2)
            proc->FdTable[2].Type = FileType.Terminal;
            proc->FdTable[2].Flags = FileFlags.WriteOnly;
            proc->FdTable[2].RefCount = 1;
        }

        // Associate current thread with process
        var currentThread = Scheduler.CurrentThread;
        if (currentThread != null)
        {
            currentThread->Process = proc;
        }

        // Build path string on stack
        byte* path = stackalloc byte[16];
        path[0] = (byte)'H';
        path[1] = (byte)'e';
        path[2] = (byte)'l';
        path[3] = (byte)'l';
        path[4] = (byte)'o';
        path[5] = (byte)'A';
        path[6] = (byte)'p';
        path[7] = (byte)'p';
        path[8] = (byte)'.';
        path[9] = (byte)'d';
        path[10] = (byte)'l';
        path[11] = (byte)'l';
        path[12] = 0;

        // Call Exec - this should not return on success
        long result = Exec(proc, path, null, null);

        // If we get here, exec failed
        DebugConsole.Write("[NetExec] Exec failed with error ");
        DebugConsole.WriteDecimal((int)(-result));
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Test execve with arguments by exec'ing ArgsApp.dll
    /// ArgsApp.Main(string[] args) returns args.Length
    /// </summary>
    public static void TestExecArgsApp()
    {
        DebugConsole.WriteLine("[NetExec] Testing execve with ArgsApp.dll (with args)...");

        // Create a new process
        var proc = ProcessTable.Allocate();
        if (proc == null)
        {
            DebugConsole.WriteLine("[NetExec] Failed to allocate process");
            return;
        }

        DebugConsole.Write("[NetExec] Created process PID ");
        DebugConsole.WriteDecimal(proc->Pid);
        DebugConsole.WriteLine();

        // Set up process attributes
        proc->ParentPid = 0;
        proc->Uid = 0;
        proc->Gid = 0;
        proc->Euid = 0;
        proc->Egid = 0;
        proc->State = ProcessState.Running;

        // Initialize file descriptor table
        proc->FdTableSize = 64;
        proc->FdTable = (FileDescriptor*)HeapAllocator.Alloc((ulong)(proc->FdTableSize * sizeof(FileDescriptor)));
        if (proc->FdTable != null)
        {
            for (int i = 0; i < proc->FdTableSize; i++)
                proc->FdTable[i] = default;

            proc->FdTable[0].Type = FileType.Terminal;
            proc->FdTable[0].Flags = FileFlags.ReadOnly;
            proc->FdTable[0].RefCount = 1;

            proc->FdTable[1].Type = FileType.Terminal;
            proc->FdTable[1].Flags = FileFlags.WriteOnly;
            proc->FdTable[1].RefCount = 1;

            proc->FdTable[2].Type = FileType.Terminal;
            proc->FdTable[2].Flags = FileFlags.WriteOnly;
            proc->FdTable[2].RefCount = 1;
        }

        // Associate current thread with process
        var currentThread = Scheduler.CurrentThread;
        if (currentThread != null)
        {
            currentThread->Process = proc;
        }

        // Build path string
        byte* path = stackalloc byte[16];
        path[0] = (byte)'A';
        path[1] = (byte)'r';
        path[2] = (byte)'g';
        path[3] = (byte)'s';
        path[4] = (byte)'A';
        path[5] = (byte)'p';
        path[6] = (byte)'p';
        path[7] = (byte)'.';
        path[8] = (byte)'d';
        path[9] = (byte)'l';
        path[10] = (byte)'l';
        path[11] = 0;

        // Build argument array with 3 arguments
        // argv[0] = "ArgsApp", argv[1] = "hello", argv[2] = "world"
        byte* arg0 = stackalloc byte[8];
        arg0[0] = (byte)'A'; arg0[1] = (byte)'r'; arg0[2] = (byte)'g'; arg0[3] = (byte)'s';
        arg0[4] = (byte)'A'; arg0[5] = (byte)'p'; arg0[6] = (byte)'p'; arg0[7] = 0;

        byte* arg1 = stackalloc byte[6];
        arg1[0] = (byte)'h'; arg1[1] = (byte)'e'; arg1[2] = (byte)'l';
        arg1[3] = (byte)'l'; arg1[4] = (byte)'o'; arg1[5] = 0;

        byte* arg2 = stackalloc byte[6];
        arg2[0] = (byte)'w'; arg2[1] = (byte)'o'; arg2[2] = (byte)'r';
        arg2[3] = (byte)'l'; arg2[4] = (byte)'d'; arg2[5] = 0;

        byte** argv = stackalloc byte*[4];
        argv[0] = arg0;
        argv[1] = arg1;
        argv[2] = arg2;
        argv[3] = null;  // NULL terminator

        // Call Exec with arguments
        // Should return 3 (args.Length) as exit code
        long result = Exec(proc, path, argv, null);

        // If we get here, exec failed
        DebugConsole.Write("[NetExec] Exec failed with error ");
        DebugConsole.WriteDecimal((int)(-result));
        DebugConsole.WriteLine();
    }
}
