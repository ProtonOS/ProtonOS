// ProtonOS kernel - Simple Binary Loader
// Loads raw binary executables without ELF support.
// Binary format: header followed by code/data.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Process;

/// <summary>
/// Simple binary header format for user programs
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BinaryHeader
{
    /// <summary>
    /// Magic number: "PBIN" (0x4E494250)
    /// </summary>
    public uint Magic;

    /// <summary>
    /// Header version (currently 1)
    /// </summary>
    public uint Version;

    /// <summary>
    /// Entry point offset from start of code section
    /// </summary>
    public ulong EntryPoint;

    /// <summary>
    /// Size of code section (executable, read-only)
    /// </summary>
    public ulong CodeSize;

    /// <summary>
    /// Size of data section (read-write)
    /// </summary>
    public ulong DataSize;

    /// <summary>
    /// Size of BSS section (zero-initialized, not in file)
    /// </summary>
    public ulong BssSize;

    /// <summary>
    /// Requested stack size (0 for default)
    /// </summary>
    public ulong StackSize;

    /// <summary>
    /// Flags (reserved)
    /// </summary>
    public ulong Flags;

    /// <summary>
    /// Reserved for future use
    /// </summary>
    public fixed ulong Reserved[4];

    public const uint MagicValue = 0x4E494250; // "PBIN"
    public const uint CurrentVersion = 1;
    public const int HeaderSize = 80; // Size of this header
}

/// <summary>
/// Binary loader for simple executables
/// </summary>
public static unsafe class BinaryLoader
{
    /// <summary>
    /// Load a binary into a process's address space
    /// </summary>
    /// <param name="proc">Target process</param>
    /// <param name="binaryData">Pointer to binary data (including header)</param>
    /// <param name="binarySize">Total size of binary data</param>
    /// <returns>Entry point virtual address, or 0 on failure</returns>
    public static ulong LoadBinary(Process* proc, byte* binaryData, ulong binarySize)
    {
        if (proc == null || binaryData == null)
            return 0;

        if (binarySize < (ulong)BinaryHeader.HeaderSize)
        {
            DebugConsole.WriteLine("[BinaryLoader] Binary too small for header");
            return 0;
        }

        // Parse header
        var header = (BinaryHeader*)binaryData;

        if (header->Magic != BinaryHeader.MagicValue)
        {
            DebugConsole.WriteLine("[BinaryLoader] Invalid magic number");
            return 0;
        }

        if (header->Version != BinaryHeader.CurrentVersion)
        {
            DebugConsole.WriteLine("[BinaryLoader] Unsupported version");
            return 0;
        }

        DebugConsole.Write("[BinaryLoader] Code: ");
        DebugConsole.WriteDecimal((int)header->CodeSize);
        DebugConsole.Write(" bytes, Data: ");
        DebugConsole.WriteDecimal((int)header->DataSize);
        DebugConsole.Write(" bytes, BSS: ");
        DebugConsole.WriteDecimal((int)header->BssSize);
        DebugConsole.WriteLine(" bytes");

        // Validate sizes
        ulong totalFileSize = (ulong)BinaryHeader.HeaderSize + header->CodeSize + header->DataSize;
        if (totalFileSize > binarySize)
        {
            DebugConsole.WriteLine("[BinaryLoader] Binary truncated");
            return 0;
        }

        // Calculate memory layout
        ulong codeBase = UserLayout.UserImageBase;
        ulong codePages = (header->CodeSize + UserLayout.PageSize - 1) / UserLayout.PageSize;
        if (codePages == 0) codePages = 1;

        ulong dataBase = codeBase + codePages * UserLayout.PageSize;
        ulong dataPages = (header->DataSize + header->BssSize + UserLayout.PageSize - 1) / UserLayout.PageSize;
        if (dataPages == 0 && (header->DataSize > 0 || header->BssSize > 0)) dataPages = 1;

        ulong totalPages = codePages + dataPages;

        DebugConsole.Write("[BinaryLoader] Loading at 0x");
        DebugConsole.WriteHex(codeBase);
        DebugConsole.Write(", ");
        DebugConsole.WriteDecimal((int)totalPages);
        DebugConsole.WriteLine(" pages");

        // Allocate and map code pages (executable, read-only for user)
        byte* codeData = binaryData + BinaryHeader.HeaderSize;
        if (!LoadSection(proc->PageTableRoot, codeBase, codeData, header->CodeSize,
                        PageFlags.Present | PageFlags.User))
        {
            DebugConsole.WriteLine("[BinaryLoader] Failed to load code section");
            return 0;
        }

        // Allocate and map data pages (read-write)
        if (dataPages > 0)
        {
            byte* dataData = codeData + header->CodeSize;
            if (!LoadSection(proc->PageTableRoot, dataBase, dataData, header->DataSize,
                            PageFlags.Present | PageFlags.Writable | PageFlags.User | PageFlags.NoExecute))
            {
                DebugConsole.WriteLine("[BinaryLoader] Failed to load data section");
                // TODO: Cleanup code pages
                return 0;
            }

            // Zero BSS (already zeroed by LoadSection, but explicit)
            if (header->BssSize > 0)
            {
                ulong bssStart = dataBase + header->DataSize;
                // BSS is after data, already zero from page allocation
            }
        }

        // Update process image info
        proc->ImageBase = codeBase;
        proc->ImageSize = totalPages * UserLayout.PageSize;

        // Set up heap after image
        proc->HeapStart = codeBase + proc->ImageSize;
        proc->HeapEnd = proc->HeapStart;

        // Calculate entry point
        ulong entryPoint = codeBase + header->EntryPoint;

        DebugConsole.Write("[BinaryLoader] Entry point: 0x");
        DebugConsole.WriteHex(entryPoint);
        DebugConsole.WriteLine();

        return entryPoint;
    }

    /// <summary>
    /// Load a raw code section (no header) - for testing
    /// </summary>
    /// <param name="proc">Target process</param>
    /// <param name="code">Raw code bytes</param>
    /// <param name="codeSize">Size of code</param>
    /// <returns>Entry point (base address), or 0 on failure</returns>
    public static ulong LoadRawCode(Process* proc, byte* code, ulong codeSize)
    {
        if (proc == null || code == null || codeSize == 0)
            return 0;

        ulong codeBase = UserLayout.UserImageBase;
        ulong codePages = (codeSize + UserLayout.PageSize - 1) / UserLayout.PageSize;
        if (codePages == 0) codePages = 1;

        DebugConsole.Write("[BinaryLoader] Loading raw code at 0x");
        DebugConsole.WriteHex(codeBase);
        DebugConsole.Write(", ");
        DebugConsole.WriteDecimal((int)codeSize);
        DebugConsole.WriteLine(" bytes");

        // Allocate and map code pages
        if (!LoadSection(proc->PageTableRoot, codeBase, code, codeSize,
                        PageFlags.Present | PageFlags.User))
        {
            DebugConsole.WriteLine("[BinaryLoader] Failed to load code");
            return 0;
        }

        // Update process image info
        proc->ImageBase = codeBase;
        proc->ImageSize = codePages * UserLayout.PageSize;

        // Set up heap after image
        proc->HeapStart = codeBase + proc->ImageSize;
        proc->HeapEnd = proc->HeapStart;

        return codeBase;
    }

    /// <summary>
    /// Load a section into process address space
    /// </summary>
    private static bool LoadSection(ulong pml4Phys, ulong virtBase, byte* data, ulong dataSize, ulong flags)
    {
        ulong numPages = (dataSize + UserLayout.PageSize - 1) / UserLayout.PageSize;
        if (numPages == 0 && dataSize > 0) numPages = 1;

        for (ulong i = 0; i < numPages; i++)
        {
            // Allocate physical page
            ulong physPage = PageAllocator.AllocatePage();
            if (physPage == 0)
                return false;

            // Zero the page first
            byte* pagePtr = (byte*)physPage;
            for (int j = 0; j < (int)UserLayout.PageSize; j++)
                pagePtr[j] = 0;

            // Copy data to page (if we have data for this page)
            ulong pageOffset = i * UserLayout.PageSize;
            if (pageOffset < dataSize)
            {
                ulong copySize = dataSize - pageOffset;
                if (copySize > UserLayout.PageSize)
                    copySize = UserLayout.PageSize;

                byte* src = data + pageOffset;
                for (ulong j = 0; j < copySize; j++)
                    pagePtr[j] = src[j];
            }

            // Map into address space
            ulong virtAddr = virtBase + i * UserLayout.PageSize;
            if (!AddressSpace.MapUserPage(pml4Phys, virtAddr, physPage, flags))
            {
                PageAllocator.FreePage(physPage);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Set up the user stack for a process
    /// </summary>
    /// <param name="proc">Target process</param>
    /// <param name="stackSize">Stack size in bytes (0 for default)</param>
    /// <returns>Initial stack pointer (top of stack), or 0 on failure</returns>
    public static ulong SetupUserStack(Process* proc, ulong stackSize = 0)
    {
        if (proc == null || proc->PageTableRoot == 0)
            return 0;

        if (stackSize == 0)
            stackSize = UserLayout.UserStackSize;

        // Align to page boundary
        stackSize = (stackSize + UserLayout.PageSize - 1) & ~(UserLayout.PageSize - 1);

        // Stack grows down from UserStackTop
        ulong stackTop = UserLayout.UserStackTop;
        ulong stackBottom = stackTop - stackSize;

        ulong numPages = stackSize / UserLayout.PageSize;

        DebugConsole.Write("[BinaryLoader] Setting up stack: 0x");
        DebugConsole.WriteHex(stackBottom);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex(stackTop);
        DebugConsole.Write(" (");
        DebugConsole.WriteDecimal((int)numPages);
        DebugConsole.WriteLine(" pages)");

        // Allocate stack pages
        ulong flags = PageFlags.Present | PageFlags.Writable | PageFlags.User | PageFlags.NoExecute;

        for (ulong i = 0; i < numPages; i++)
        {
            ulong physPage = PageAllocator.AllocatePage();
            if (physPage == 0)
            {
                DebugConsole.WriteLine("[BinaryLoader] Failed to allocate stack page");
                return 0;
            }

            // Zero the page
            byte* pagePtr = (byte*)physPage;
            for (int j = 0; j < (int)UserLayout.PageSize; j++)
                pagePtr[j] = 0;

            ulong virtAddr = stackBottom + i * UserLayout.PageSize;
            if (!AddressSpace.MapUserPage(proc->PageTableRoot, virtAddr, physPage, flags))
            {
                PageAllocator.FreePage(physPage);
                DebugConsole.WriteLine("[BinaryLoader] Failed to map stack page");
                return 0;
            }
        }

        proc->StackTop = stackTop;
        proc->StackBottom = stackBottom;

        // Return initial RSP (16-byte aligned, with some room at top)
        ulong initialRsp = stackTop - 8;
        initialRsp &= ~0xFUL;

        return initialRsp;
    }

    /// <summary>
    /// Set up user stack with argc, argv, envp
    /// </summary>
    public static ulong SetupUserStackWithArgs(Process* proc, byte** argv, int argc, byte** envp, int envc)
    {
        // First set up basic stack
        ulong rsp = SetupUserStack(proc, 0);
        if (rsp == 0)
            return 0;

        // TODO: Copy argv and envp strings to user stack
        // For now, just return empty args
        // The stack layout should be:
        // [high addresses]
        //   environment strings
        //   argument strings
        //   NULL (end of envp)
        //   envp[envc-1]
        //   ...
        //   envp[0]
        //   NULL (end of argv)
        //   argv[argc-1]
        //   ...
        //   argv[0]
        //   argc
        // [rsp points here]

        // For now, just set up argc=0, argv=NULL, envp=NULL
        // Push NULL for envp terminator
        rsp -= 8;
        // Push NULL for argv terminator
        rsp -= 8;
        // Push argc=0
        rsp -= 8;

        // Note: We can't write to user memory directly since we're in kernel
        // The actual values would need to be written through the page mapping
        // For now, the user program will see uninitialized stack values

        return rsp;
    }
}
