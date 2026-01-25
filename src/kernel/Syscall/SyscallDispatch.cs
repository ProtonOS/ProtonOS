// ProtonOS kernel - System Call Dispatch
// Dispatch table and basic syscall implementations.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Platform;
using ProtonOS.IO;
using ProtonOS.Process;
using ProtonOS.Memory;
using ProtonOS.X64;

namespace ProtonOS.Syscall;

/// <summary>
/// Syscall dispatch table and handler registry
/// </summary>
public static unsafe class SyscallDispatch
{
    /// <summary>
    /// Syscall handler function type
    /// </summary>
    public delegate long SyscallFunc(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread);

    // Handler table
    private static SyscallFunc[] _handlers = null!;
    private static bool _initialized;

    // Ring 3 test exit handler (for testing user mode)
    private static delegate* unmanaged<int, void> _ring3TestExitHandler;

    /// <summary>
    /// Set a custom exit handler for Ring 3 testing.
    /// The handler is called when exit() syscall is invoked.
    /// </summary>
    public static void SetRing3TestExitHandler(delegate* unmanaged<int, void> handler)
    {
        _ring3TestExitHandler = handler;
    }

    /// <summary>
    /// Get the Ring 3 test exit handler
    /// </summary>
    public static delegate* unmanaged<int, void> GetRing3TestExitHandler()
    {
        return _ring3TestExitHandler;
    }

    /// <summary>
    /// Clear the Ring 3 test exit handler
    /// </summary>
    public static void ClearRing3TestExitHandler()
    {
        _ring3TestExitHandler = null;
    }

    /// <summary>
    /// Initialize the syscall dispatch table
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        DebugConsole.WriteLine("[SyscallDispatch] Initializing syscall table...");

        _handlers = new SyscallFunc[SyscallNumbers.SYS_MAX];

        // Register basic syscalls
        RegisterProcessSyscalls();
        RegisterFileSyscalls();
        RegisterIdentitySyscalls();
        RegisterMemorySyscalls();

        _initialized = true;
        DebugConsole.WriteLine("[SyscallDispatch] Initialized");
    }

    /// <summary>
    /// Register a syscall handler
    /// </summary>
    public static void Register(int number, SyscallFunc handler)
    {
        if (number >= 0 && number < SyscallNumbers.SYS_MAX)
        {
            _handlers[number] = handler;
        }
    }

    /// <summary>
    /// Dispatch a syscall to the appropriate handler
    /// </summary>
    public static long Dispatch(long number, long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        if (!_initialized)
            Init();

        if (number < 0 || number >= SyscallNumbers.SYS_MAX)
            return -Errno.ENOSYS;

        var handler = _handlers[number];
        if (handler == null)
            return -Errno.ENOSYS;

        return handler(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    /// <summary>
    /// Register process control syscalls
    /// </summary>
    private static void RegisterProcessSyscalls()
    {
        _handlers[SyscallNumbers.SYS_EXIT] = SysExit;
        _handlers[SyscallNumbers.SYS_EXIT_GROUP] = SysExitGroup;
        _handlers[SyscallNumbers.SYS_GETPID] = SysGetpid;
        _handlers[SyscallNumbers.SYS_GETPPID] = SysGetppid;
        _handlers[SyscallNumbers.SYS_FORK] = SysFork;
        _handlers[SyscallNumbers.SYS_VFORK] = SysVfork;
        _handlers[SyscallNumbers.SYS_WAIT4] = SysWait4;
        _handlers[SyscallNumbers.SYS_KILL] = SysKill;
        _handlers[SyscallNumbers.SYS_GETPGID] = SysGetpgid;
        _handlers[SyscallNumbers.SYS_SETPGID] = SysSetpgid;
        _handlers[SyscallNumbers.SYS_GETSID] = SysGetsid;
        _handlers[SyscallNumbers.SYS_SETSID] = SysSetsid;
    }

    /// <summary>
    /// Register file operation syscalls
    /// </summary>
    private static void RegisterFileSyscalls()
    {
        _handlers[SyscallNumbers.SYS_READ] = SysRead;
        _handlers[SyscallNumbers.SYS_WRITE] = SysWrite;
        _handlers[SyscallNumbers.SYS_OPEN] = SysOpen;
        _handlers[SyscallNumbers.SYS_CLOSE] = SysClose;
        _handlers[SyscallNumbers.SYS_LSEEK] = SysLseek;
        _handlers[SyscallNumbers.SYS_PIPE] = SysPipe;
        _handlers[SyscallNumbers.SYS_DUP] = SysDup;
        _handlers[SyscallNumbers.SYS_DUP2] = SysDup2;
        _handlers[SyscallNumbers.SYS_GETCWD] = SysGetcwd;
        _handlers[SyscallNumbers.SYS_CHDIR] = SysChdir;
    }

    /// <summary>
    /// Register user/group identity syscalls
    /// </summary>
    private static void RegisterIdentitySyscalls()
    {
        _handlers[SyscallNumbers.SYS_GETUID] = SysGetuid;
        _handlers[SyscallNumbers.SYS_GETGID] = SysGetgid;
        _handlers[SyscallNumbers.SYS_GETEUID] = SysGeteuid;
        _handlers[SyscallNumbers.SYS_GETEGID] = SysGetegid;
        _handlers[SyscallNumbers.SYS_SETUID] = SysSetuid;
        _handlers[SyscallNumbers.SYS_SETGID] = SysSetgid;
    }

    /// <summary>
    /// Register memory management syscalls
    /// </summary>
    private static void RegisterMemorySyscalls()
    {
        _handlers[SyscallNumbers.SYS_BRK] = SysBrk;
        _handlers[SyscallNumbers.SYS_MMAP] = SysMmap;
        _handlers[SyscallNumbers.SYS_MPROTECT] = SysMprotect;
        _handlers[SyscallNumbers.SYS_MUNMAP] = SysMunmap;
        _handlers[SyscallNumbers.SYS_MSYNC] = SysMsync;
    }

    // ==================== Process Control ====================

    private static long SysExit(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int exitCode = (int)arg0;
        DebugConsole.Write("[Syscall] exit(");
        DebugConsole.WriteDecimal(exitCode);
        DebugConsole.WriteLine(")");

        // Check if Ring 3 test handler is installed
        if (_ring3TestExitHandler != null)
        {
            DebugConsole.WriteLine("[Syscall] Calling Ring 3 test exit handler");
            var handler = _ring3TestExitHandler;
            _ring3TestExitHandler = null;  // Clear handler to prevent re-entry
            handler(exitCode);
            // Handler should not return, but if it does, continue to normal exit
        }

        // Mark process as zombie and store exit code
        ProcessTable.MarkZombie(proc, exitCode, 0);

        // Terminate the current thread
        Scheduler.ExitThread((uint)exitCode);

        // Should never reach here
        return 0;
    }

    private static long SysExitGroup(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                      Process.Process* proc, Thread* thread)
    {
        // exit_group terminates all threads in the process
        return SysExit(arg0, arg1, arg2, arg3, arg4, arg5, proc, thread);
    }

    private static long SysGetpid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Pid;
    }

    private static long SysGetppid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->ParentPid;
    }

    private static long SysFork(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        // Fork implementation will be in ProcessSyscalls.cs
        return ProcessSyscalls.Fork(proc, thread);
    }

    private static long SysVfork(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // vfork is similar to fork but shares address space until exec
        // For now, implement as regular fork
        return ProcessSyscalls.Fork(proc, thread);
    }

    private static long SysWait4(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int* status = (int*)arg1;
        int options = (int)arg2;

        return ProcessSyscalls.Wait4(proc, pid, status, options);
    }

    private static long SysKill(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int sig = (int)arg1;

        if (pid <= 0)
        {
            // Process group signals not yet implemented
            return -Errno.ENOSYS;
        }

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        // Check permissions (can only signal processes we own, or be root)
        if (proc->Euid != 0 && proc->Euid != target->Uid && proc->Uid != target->Uid)
            return -Errno.EPERM;

        if (!ProcessTable.SignalProcess(target, sig))
            return -Errno.EINVAL;

        return 0;
    }

    private static long SysGetpgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        if (pid == 0)
            return proc->ProcessGroupId;

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        return target->ProcessGroupId;
    }

    private static long SysSetpgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        int pgid = (int)arg1;

        var target = pid == 0 ? proc : ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        // Can only change own or children's process group
        if (target != proc && target->ParentPid != proc->Pid)
            return -Errno.ESRCH;

        if (pgid == 0)
            pgid = target->Pid;

        target->ProcessGroupId = pgid;
        return 0;
    }

    private static long SysGetsid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        int pid = (int)arg0;
        if (pid == 0)
            return proc->SessionId;

        var target = ProcessTable.GetByPid(pid);
        if (target == null)
            return -Errno.ESRCH;

        return target->SessionId;
    }

    private static long SysSetsid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        // Process must not already be a session leader
        if (proc->Pid == proc->SessionId)
            return -Errno.EPERM;

        // Create new session
        proc->SessionId = proc->Pid;
        proc->ProcessGroupId = proc->Pid;
        proc->ControllingTerminal = null;

        return proc->Pid;
    }

    // ==================== File Operations ====================

    private static long SysRead(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For stdin, read from debug console (serial port)
        if (fd == StdFd.Stdin)
        {
            if (count <= 0)
                return 0;

            // Read characters until newline or buffer full
            // This provides line-buffered input like a terminal
            int bytesRead = 0;
            while (bytesRead < count)
            {
                byte b = DebugConsole.ReadByte();

                // Echo the character back
                DebugConsole.WriteByte(b);

                // Handle backspace
                if (b == 0x7F || b == 0x08)
                {
                    if (bytesRead > 0)
                    {
                        bytesRead--;
                        // Erase character on terminal: backspace, space, backspace
                        DebugConsole.WriteByte(0x08);
                        DebugConsole.WriteByte((byte)' ');
                        DebugConsole.WriteByte(0x08);
                    }
                    continue;
                }

                // Store the byte
                buf[bytesRead++] = b;

                // Return on newline (CR or LF)
                if (b == '\r' || b == '\n')
                {
                    // Echo newline if we got CR
                    if (b == '\r')
                        DebugConsole.WriteByte((byte)'\n');
                    // Convert CR to LF for Unix convention
                    buf[bytesRead - 1] = (byte)'\n';
                    break;
                }
            }
            return bytesRead;
        }

        if (fdEntry->Ops == null || fdEntry->Ops->Read == null)
            return -Errno.ENOTSUP;

        return fdEntry->Ops->Read(fdEntry, buf, count);
    }

    private static long SysWrite(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        byte* buf = (byte*)arg1;
        int count = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        // For stdout/stderr, write to debug console
        if (fd == StdFd.Stdout || fd == StdFd.Stderr)
        {
            for (int i = 0; i < count; i++)
            {
                DebugConsole.WriteChar((char)buf[i]);
            }
            return count;
        }

        if (fdEntry->Ops == null || fdEntry->Ops->Write == null)
            return -Errno.ENOTSUP;

        return fdEntry->Ops->Write(fdEntry, buf, count);
    }

    // VFS file operations table (static to keep it alive)
    private static FileOps _vfsFileOps;
    private static bool _vfsOpsInitialized;

    private static void InitVfsFileOps()
    {
        if (_vfsOpsInitialized)
            return;

        _vfsFileOps.Read = &VfsFileRead;
        _vfsFileOps.Write = &VfsFileWrite;
        _vfsFileOps.Seek = &VfsFileSeek;
        _vfsFileOps.Close = &VfsFileClose;
        _vfsOpsInitialized = true;
    }

    [UnmanagedCallersOnly]
    private static int VfsFileRead(FileDescriptor* fd, byte* buf, int count)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Read(vfsHandle, buf, count);
    }

    [UnmanagedCallersOnly]
    private static int VfsFileWrite(FileDescriptor* fd, byte* buf, int count)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Write(vfsHandle, buf, count);
    }

    [UnmanagedCallersOnly]
    private static long VfsFileSeek(FileDescriptor* fd, long offset, int whence)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Seek(vfsHandle, offset, whence);
    }

    [UnmanagedCallersOnly]
    private static int VfsFileClose(FileDescriptor* fd)
    {
        int vfsHandle = (int)(long)fd->Data;
        return VFS.Close(vfsHandle);
    }

    private static long SysOpen(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;
        int flags = (int)arg1;
        int mode = (int)arg2;

        if (path == null)
            return -Errno.EFAULT;

        // Initialize VFS file operations if needed
        InitVfsFileOps();

        // Open through VFS
        int vfsHandle = VFS.Open(path, flags, mode);
        if (vfsHandle < 0)
            return vfsHandle;  // Return error code

        // Allocate file descriptor
        int fd = FdTable.Allocate(proc->FdTable, proc->FdTableSize, 0);
        if (fd < 0)
        {
            VFS.Close(vfsHandle);
            return fd;
        }

        // Set up file descriptor
        var fdEntry = &proc->FdTable[fd];
        fdEntry->Type = FileType.Regular;
        fdEntry->Flags = (FileFlags)flags;
        fdEntry->RefCount = 1;
        fdEntry->Offset = 0;
        fdEntry->Data = (void*)(long)vfsHandle;  // Store VFS handle
        fdEntry->Ops = (FileOps*)System.Runtime.CompilerServices.Unsafe.AsPointer(ref _vfsFileOps);

        return fd;
    }

    private static long SysClose(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        return FdTable.Close(fdEntry);
    }

    private static long SysLseek(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        int fd = (int)arg0;
        long offset = arg1;
        int whence = (int)arg2;

        if (fd < 0 || fd >= proc->FdTableSize)
            return -Errno.EBADF;

        var fdEntry = &proc->FdTable[fd];
        if (fdEntry->Type == FileType.None)
            return -Errno.EBADF;

        if (fdEntry->Ops == null || fdEntry->Ops->Seek == null)
            return -Errno.ESPIPE;

        return fdEntry->Ops->Seek(fdEntry, offset, whence);
    }

    private static long SysPipe(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int* pipefd = (int*)arg0;

        // TODO: Implement pipe creation
        return -Errno.ENOSYS;
    }

    private static long SysDup(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                Process.Process* proc, Thread* thread)
    {
        int oldfd = (int)arg0;
        return FdTable.Dup(proc->FdTable, proc->FdTableSize, oldfd);
    }

    private static long SysDup2(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        int oldfd = (int)arg0;
        int newfd = (int)arg1;
        return FdTable.Dup2(proc->FdTable, proc->FdTableSize, oldfd, newfd);
    }

    private static long SysGetcwd(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        byte* buf = (byte*)arg0;
        int size = (int)arg1;

        if (buf == null || size < 1)
            return -Errno.EINVAL;

        // Copy cwd to buffer
        int len = 0;
        while (len < 255 && proc->Cwd[len] != 0)
            len++;

        if (len + 1 > size)
            return -Errno.ERANGE;

        for (int i = 0; i <= len; i++)
            buf[i] = proc->Cwd[i];

        return (long)buf;
    }

    private static long SysChdir(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        byte* path = (byte*)arg0;

        // TODO: Validate path exists and is a directory
        // For now, just copy the path
        int len = 0;
        while (len < 255 && path[len] != 0)
        {
            proc->Cwd[len] = path[len];
            len++;
        }
        proc->Cwd[len] = 0;

        return 0;
    }

    // ==================== User/Group Identity ====================

    private static long SysGetuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Uid;
    }

    private static long SysGetgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        return proc->Gid;
    }

    private static long SysGeteuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->Euid;
    }

    private static long SysGetegid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                    Process.Process* proc, Thread* thread)
    {
        return proc->Egid;
    }

    private static long SysSetuid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        uint uid = (uint)arg0;

        // Root can set any UID
        if (proc->Euid == 0)
        {
            proc->Uid = uid;
            proc->Euid = uid;
            proc->Suid = uid;
            return 0;
        }

        // Non-root can only set UID to real, effective, or saved UID
        if (uid == proc->Uid || uid == proc->Euid || uid == proc->Suid)
        {
            proc->Euid = uid;
            return 0;
        }

        return -Errno.EPERM;
    }

    private static long SysSetgid(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        uint gid = (uint)arg0;

        // Root can set any GID
        if (proc->Euid == 0)
        {
            proc->Gid = gid;
            proc->Egid = gid;
            proc->Sgid = gid;
            return 0;
        }

        // Non-root can only set GID to real, effective, or saved GID
        if (gid == proc->Gid || gid == proc->Egid || gid == proc->Sgid)
        {
            proc->Egid = gid;
            return 0;
        }

        return -Errno.EPERM;
    }

    // ==================== Memory Management ====================

    private static long SysBrk(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                Process.Process* proc, Thread* thread)
    {
        ulong newBrk = (ulong)arg0;

        // If arg is 0, return current brk
        if (newBrk == 0)
            return (long)proc->HeapEnd;

        // Can't shrink below heap start
        if (newBrk < proc->HeapStart)
            return (long)proc->HeapEnd;

        // TODO: Allocate/deallocate pages as needed
        proc->HeapEnd = newBrk;
        return (long)proc->HeapEnd;
    }

    private static long SysMmap(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                 Process.Process* proc, Thread* thread)
    {
        // mmap(addr, length, prot, flags, fd, offset)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int prot = (int)arg2;
        int flags = (int)arg3;
        int fd = (int)arg4;
        long offset = arg5;

        const ulong PageSize = 4096;

        // Validate length
        if (length == 0)
            return -Errno.EINVAL;

        // Page-align length
        ulong alignedLength = (length + PageSize - 1) & ~(PageSize - 1);

        bool isAnonymous = (flags & MmapFlags.Anonymous) != 0;
        FileDescriptor* fdEntry = null;

        // Validate file descriptor for file-backed mappings
        if (!isAnonymous)
        {
            if (fd < 0 || fd >= proc->FdTableSize)
                return -Errno.EBADF;

            fdEntry = &proc->FdTable[fd];
            if (fdEntry->Type == FileType.None)
                return -Errno.EBADF;

            // Check that fd supports read (for mapping)
            if (fdEntry->Ops == null || fdEntry->Ops->Read == null)
                return -Errno.ENODEV;

            // Offset must be page-aligned
            if ((offset & (long)(PageSize - 1)) != 0)
                return -Errno.EINVAL;
        }

        // Determine mapping address
        ulong mapAddr;
        if ((flags & MmapFlags.Fixed) != 0)
        {
            // Use exact address requested
            if (addr == 0 || (addr & (PageSize - 1)) != 0)
                return -Errno.EINVAL;
            mapAddr = addr;
        }
        else
        {
            // Allocate from mmap region (below stack)
            if (proc->MmapBase == 0)
            {
                // Start mmap region below stack with some gap
                proc->MmapBase = proc->StackBottom - 0x10000000; // 256MB below stack
            }

            // Allocate from top down
            proc->MmapBase -= alignedLength;
            proc->MmapBase &= ~(PageSize - 1);
            mapAddr = proc->MmapBase;
        }

        // Convert protection to page flags
        ulong pageFlags = PageFlags.Present | PageFlags.User;
        if ((prot & MmapProt.Write) != 0)
            pageFlags |= PageFlags.Writable;
        if ((prot & MmapProt.Exec) == 0)
            pageFlags |= PageFlags.NoExecute;

        // Allocate and map pages
        ulong numPages = alignedLength / PageSize;
        for (ulong i = 0; i < numPages; i++)
        {
            ulong pagePhys = PageAllocator.AllocatePage();
            if (pagePhys == 0)
            {
                // Rollback: unmap already-mapped pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = mapAddr + j * PageSize;
                    ulong rollbackPhys = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, rollbackAddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, rollbackAddr);
                    if (rollbackPhys != 0)
                        PageAllocator.FreePage(rollbackPhys);
                }
                return -Errno.ENOMEM;
            }

            // Zero the page first
            CPU.MemZero((void*)pagePhys, PageSize);

            // Map the page
            ulong vaddr = mapAddr + i * PageSize;
            if (!AddressSpace.MapUserPage(proc->PageTableRoot, vaddr, pagePhys, pageFlags))
            {
                PageAllocator.FreePage(pagePhys);
                // Rollback previous pages
                for (ulong j = 0; j < i; j++)
                {
                    ulong rollbackAddr = mapAddr + j * PageSize;
                    ulong rollbackPhys = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, rollbackAddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, rollbackAddr);
                    if (rollbackPhys != 0)
                        PageAllocator.FreePage(rollbackPhys);
                }
                return -Errno.ENOMEM;
            }
        }

        // For file-backed mappings, read file content into the mapped pages
        if (!isAnonymous && fdEntry != null)
        {
            // Save current file position
            long savedPos = -1;
            if (fdEntry->Ops->Seek != null)
            {
                savedPos = fdEntry->Ops->Seek(fdEntry, 0, 1); // SEEK_CUR
                fdEntry->Ops->Seek(fdEntry, offset, 0); // SEEK_SET to mapping offset
            }

            // Read file content into mapped memory
            ulong bytesToRead = length; // Use original length, not aligned
            ulong bytesRead = 0;

            while (bytesRead < bytesToRead)
            {
                ulong pageOffset = bytesRead / PageSize;
                ulong offsetInPage = bytesRead % PageSize;
                ulong vaddr = mapAddr + pageOffset * PageSize + offsetInPage;

                // Get physical address for this virtual address
                ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                if (physAddr == 0)
                    break;

                // Calculate how much to read (up to end of page or end of file region)
                ulong remainingInPage = PageSize - offsetInPage;
                ulong remainingTotal = bytesToRead - bytesRead;
                int toRead = (int)(remainingInPage < remainingTotal ? remainingInPage : remainingTotal);

                // Read directly into physical memory
                int result = fdEntry->Ops->Read(fdEntry, (byte*)physAddr, toRead);
                if (result <= 0)
                    break; // EOF or error

                bytesRead += (ulong)result;
            }

            // Restore file position
            if (savedPos >= 0 && fdEntry->Ops->Seek != null)
            {
                fdEntry->Ops->Seek(fdEntry, savedPos, 0); // SEEK_SET
            }
        }

        // Track the mapping
        var region = (MmapRegion*)HeapAllocator.AllocZeroed((ulong)sizeof(MmapRegion));
        if (region != null)
        {
            region->Start = mapAddr;
            region->Length = alignedLength;
            region->Prot = prot;
            region->Flags = flags;
            region->Fd = isAnonymous ? -1 : fd;
            region->FileOffset = offset;
            region->Next = proc->MmapRegions;
            proc->MmapRegions = region;
        }

        return (long)mapAddr;
    }

    private static long SysMunmap(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                   Process.Process* proc, Thread* thread)
    {
        // munmap(addr, length)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return -Errno.EINVAL;

        ulong unmapEnd = addr + length;

        // Find and handle overlapping regions
        MmapRegion** prevPtr = &proc->MmapRegions;
        MmapRegion* region = proc->MmapRegions;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps with the unmap range
            if (region->Start < unmapEnd && regionEnd > addr)
            {
                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < unmapEnd ? regionEnd : unmapEnd;

                // Unmap pages in the overlap and free physical memory
                for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                {
                    ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                    AddressSpace.UnmapUserPage(proc->PageTableRoot, vaddr);
                    if (physAddr != 0)
                        PageAllocator.FreePage(physAddr);
                }

                // Case 1: Entire region unmapped - remove it
                if (overlapStart == region->Start && overlapEnd == regionEnd)
                {
                    MmapRegion* toFree = region;
                    *prevPtr = region->Next;
                    region = region->Next;
                    HeapAllocator.Free(toFree);
                    continue;
                }
                // Case 2: Unmap from start - shrink region
                else if (overlapStart == region->Start)
                {
                    ulong shrinkBy = overlapEnd - region->Start;
                    region->Start = overlapEnd;
                    region->Length -= shrinkBy;
                    if (region->Fd >= 0)
                        region->FileOffset += (long)shrinkBy;
                }
                // Case 3: Unmap from end - shrink region
                else if (overlapEnd == regionEnd)
                {
                    region->Length = overlapStart - region->Start;
                }
                // Case 4: Unmap from middle - split into two regions
                else
                {
                    // Create new region for the part after the hole
                    var newRegion = (MmapRegion*)HeapAllocator.AllocZeroed((ulong)sizeof(MmapRegion));
                    if (newRegion != null)
                    {
                        newRegion->Start = overlapEnd;
                        newRegion->Length = regionEnd - overlapEnd;
                        newRegion->Prot = region->Prot;
                        newRegion->Flags = region->Flags;
                        newRegion->Fd = region->Fd;
                        newRegion->FileOffset = region->FileOffset + (long)(overlapEnd - region->Start);
                        newRegion->Next = region->Next;
                        region->Next = newRegion;
                    }
                    // Shrink original region to part before the hole
                    region->Length = overlapStart - region->Start;
                }
            }

            prevPtr = &region->Next;
            region = region->Next;
        }

        return 0;
    }

    private static long SysMprotect(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                     Process.Process* proc, Thread* thread)
    {
        // mprotect(addr, length, prot)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int prot = (int)arg2;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return 0; // Nothing to do

        ulong addrEnd = addr + length;

        // Convert protection to page flags
        ulong newFlags = PageFlags.Present | PageFlags.User;
        if ((prot & MmapProt.Write) != 0)
            newFlags |= PageFlags.Writable;
        if ((prot & MmapProt.Exec) == 0)
            newFlags |= PageFlags.NoExecute;

        // Find and update matching regions
        MmapRegion* region = proc->MmapRegions;
        bool foundRegion = false;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps with the protection range
            if (region->Start < addrEnd && regionEnd > addr)
            {
                foundRegion = true;

                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < addrEnd ? regionEnd : addrEnd;

                // Update page table entries for the overlap
                for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                {
                    // Get current physical address
                    ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                    if (physAddr != 0)
                    {
                        // Remap with new flags
                        AddressSpace.UnmapUserPage(proc->PageTableRoot, vaddr);
                        AddressSpace.MapUserPage(proc->PageTableRoot, vaddr, physAddr, newFlags);
                    }
                }

                // Update region protection if entire region is affected
                if (overlapStart == region->Start && overlapEnd == regionEnd)
                {
                    region->Prot = prot;
                }
                // TODO: Split region if partial protection change
            }

            region = region->Next;
        }

        // Linux mprotect succeeds even if no mapping exists
        return 0;
    }

    private static long SysMsync(long arg0, long arg1, long arg2, long arg3, long arg4, long arg5,
                                  Process.Process* proc, Thread* thread)
    {
        // msync(addr, length, flags)
        ulong addr = (ulong)arg0;
        ulong length = (ulong)arg1;
        int flags = (int)arg2;

        const ulong PageSize = 4096;

        // Validate address alignment
        if ((addr & (PageSize - 1)) != 0)
            return -Errno.EINVAL;

        // Page-align length
        length = (length + PageSize - 1) & ~(PageSize - 1);

        if (length == 0)
            return 0;

        // Must specify either MS_ASYNC or MS_SYNC (but not both)
        bool async = (flags & MsyncFlags.Async) != 0;
        bool sync = (flags & MsyncFlags.Sync) != 0;
        if (async && sync)
            return -Errno.EINVAL;
        if (!async && !sync)
            return -Errno.EINVAL;

        ulong addrEnd = addr + length;

        // Find overlapping MAP_SHARED regions with file backing
        MmapRegion* region = proc->MmapRegions;

        while (region != null)
        {
            ulong regionEnd = region->Start + region->Length;

            // Check if this region overlaps and is MAP_SHARED with file backing
            if (region->Start < addrEnd && regionEnd > addr &&
                (region->Flags & MmapFlags.Shared) != 0 &&
                region->Fd >= 0)
            {
                ulong overlapStart = region->Start > addr ? region->Start : addr;
                ulong overlapEnd = regionEnd < addrEnd ? regionEnd : addrEnd;

                // Get the file descriptor
                if (region->Fd >= 0 && region->Fd < proc->FdTableSize)
                {
                    var fdEntry = &proc->FdTable[region->Fd];
                    if (fdEntry->Type != FileType.None && fdEntry->Ops != null && fdEntry->Ops->Write != null)
                    {
                        // Calculate file offset for the overlap start
                        long fileOffset = region->FileOffset + (long)(overlapStart - region->Start);

                        // Seek to position if possible
                        if (fdEntry->Ops->Seek != null)
                        {
                            fdEntry->Ops->Seek(fdEntry, fileOffset, 0); // SEEK_SET
                        }

                        // Write each page back to the file
                        for (ulong vaddr = overlapStart; vaddr < overlapEnd; vaddr += PageSize)
                        {
                            ulong physAddr = AddressSpace.GetPhysicalAddress(proc->PageTableRoot, vaddr);
                            if (physAddr != 0)
                            {
                                // Calculate bytes to write (may be partial page at end)
                                ulong remaining = overlapEnd - vaddr;
                                int toWrite = remaining > PageSize ? (int)PageSize : (int)remaining;

                                fdEntry->Ops->Write(fdEntry, (byte*)physAddr, toWrite);
                            }
                        }
                    }
                }
            }

            region = region->Next;
        }

        return 0;
    }
}
