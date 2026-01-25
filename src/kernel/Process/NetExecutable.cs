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
        // 1. Calls Main()
        // 2. Calls exit(return_value)
        //
        // Layout:
        //   UserCodeBase + 0:    startup stub (16 bytes)
        //   UserCodeBase + 16:   JIT'd Main() code

        const int StubSize = 16;
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

        // Write startup stub to first page
        // Stub: call Main; mov edi,eax; mov eax,60; syscall; ud2
        byte* stubPage = (byte*)VirtualMemory.PhysToVirt(physPages[0]);

        // call rel32 (E8 xx xx xx xx) - call Main at offset 16
        // Displacement is from end of instruction (offset 5) to target (offset 16) = 11
        stubPage[0] = 0xE8;
        stubPage[1] = 0x0B; // 11 = StubSize - 5
        stubPage[2] = 0x00;
        stubPage[3] = 0x00;
        stubPage[4] = 0x00;

        // mov edi, eax (89 C7) - move return value to first arg of exit
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

        // ud2 (0F 0B) - trap if syscall returns (shouldn't happen)
        stubPage[14] = 0x0F;
        stubPage[15] = 0x0B;

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

        // Map all code pages into user space
        ulong userCodeAddr = UserCodeBase;
        for (ulong i = 0; i < codePages; i++)
        {
            // Map into user space with execute permission (no NoExecute flag)
            AddressSpace.MapUserPage(proc->PageTableRoot, userCodeAddr + i * 4096, physPages[i],
                PageFlags.Present | PageFlags.User);
        }

        // Set up user stack
        ulong userRsp = SetupUserStack(proc, argv, envp);
        if (userRsp == 0)
        {
            DebugConsole.WriteLine("[NetExec] Failed to set up stack");
            return -Errno.ENOMEM;
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
    /// Set up user stack with argc, argv, envp
    /// </summary>
    private static ulong SetupUserStack(Process* proc, byte** argv, byte** envp)
    {
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

        // Set up initial stack pointer
        ulong rsp = stackTop - 8; // Leave room for alignment

        // Count argc and copy strings to stack
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

        // For simplicity, set up minimal stack:
        // RSP -> argc (8 bytes)
        //        argv[0] pointer
        //        argv[1] pointer
        //        ...
        //        NULL
        //        envp[0] pointer
        //        ...
        //        NULL
        //        string data...

        // For now, just set up a simple stack with argc=0
        // More complex argv/envp handling can be added later
        // We need to write to the stack through the physical mapping
        // but the stack is mapped into user space, not kernel space yet
        // So we write via physical address

        // Align to 16 bytes
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
}
