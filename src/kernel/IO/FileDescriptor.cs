// ProtonOS kernel - File Descriptor Infrastructure
// Per-process file descriptor tables and file operations.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Threading;

namespace ProtonOS.IO;

/// <summary>
/// File descriptor type
/// </summary>
public enum FileType : byte
{
    None = 0,       // Unused/invalid FD
    Regular,        // Regular file
    Directory,      // Directory
    Pipe,           // Pipe (read or write end)
    Socket,         // Unix domain socket or network socket
    Device,         // Device file (character or block)
    Terminal,       // Terminal/PTY
    EventFd,        // Event file descriptor
    TimerFd,        // Timer file descriptor
    SignalFd,       // Signal file descriptor
}

/// <summary>
/// File descriptor flags (O_* flags)
/// </summary>
[Flags]
public enum FileFlags : uint
{
    None = 0,

    // Access modes (mutually exclusive)
    ReadOnly = 0x0000,      // O_RDONLY
    WriteOnly = 0x0001,     // O_WRONLY
    ReadWrite = 0x0002,     // O_RDWR
    AccessMask = 0x0003,    // Mask for access mode

    // File creation flags
    Create = 0x0040,        // O_CREAT - Create file if it doesn't exist
    Exclusive = 0x0080,     // O_EXCL - Fail if file exists (with O_CREAT)
    NoCtty = 0x0100,        // O_NOCTTY - Don't make this the controlling terminal
    Truncate = 0x0200,      // O_TRUNC - Truncate file to zero length
    Append = 0x0400,        // O_APPEND - Writes append to end

    // File status flags
    NonBlock = 0x0800,      // O_NONBLOCK - Non-blocking I/O
    Sync = 0x1000,          // O_SYNC - Synchronous writes
    Async = 0x2000,         // O_ASYNC - Signal-driven I/O
    Direct = 0x4000,        // O_DIRECT - Direct I/O (bypass page cache)
    NoFollow = 0x8000,      // O_NOFOLLOW - Don't follow symlinks

    // Close-on-exec flag
    CloseOnExec = 0x80000,  // O_CLOEXEC - Close on exec

    // Directory flag
    IsDirectory = 0x10000,  // O_DIRECTORY - Must be a directory
}

/// <summary>
/// Seek origins for lseek
/// </summary>
public enum SeekOrigin : int
{
    Set = 0,        // SEEK_SET - From beginning of file
    Current = 1,    // SEEK_CUR - From current position
    End = 2,        // SEEK_END - From end of file
}

/// <summary>
/// File descriptor entry in per-process FD table
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FileDescriptor
{
    /// <summary>
    /// Type of this file descriptor
    /// </summary>
    public FileType Type;

    /// <summary>
    /// File flags (O_* flags)
    /// </summary>
    public FileFlags Flags;

    /// <summary>
    /// Reference count (for dup/fork sharing)
    /// </summary>
    public int RefCount;

    /// <summary>
    /// Current file offset for read/write
    /// </summary>
    public long Offset;

    /// <summary>
    /// Pointer to underlying file object (INode*, Pipe*, Socket*, etc.)
    /// </summary>
    public void* Data;

    /// <summary>
    /// File operations table
    /// </summary>
    public FileOps* Ops;
}

/// <summary>
/// File operations function table
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FileOps
{
    /// <summary>
    /// Read from file: int Read(FileDescriptor* fd, byte* buf, int count)
    /// Returns bytes read, 0 for EOF, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, byte*, int, int> Read;

    /// <summary>
    /// Write to file: int Write(FileDescriptor* fd, byte* buf, int count)
    /// Returns bytes written, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, byte*, int, int> Write;

    /// <summary>
    /// Seek in file: long Seek(FileDescriptor* fd, long offset, int whence)
    /// Returns new offset, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, long, int, long> Seek;

    /// <summary>
    /// Close file: int Close(FileDescriptor* fd)
    /// Returns 0 on success, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, int> Close;

    /// <summary>
    /// IO control: int Ioctl(FileDescriptor* fd, uint cmd, void* arg)
    /// Returns 0 on success, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, uint, void*, int> Ioctl;

    /// <summary>
    /// Poll for events: int Poll(FileDescriptor* fd, PollEvents events, int timeout)
    /// Returns events that occurred, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, int, int, int> Poll;

    /// <summary>
    /// Duplicate file descriptor (for fork): int Dup(FileDescriptor* fd)
    /// Called when reference count increases
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, int> Dup;

    /// <summary>
    /// Get file status: int Fstat(FileDescriptor* fd, Stat* buf)
    /// Returns 0 on success, negative for error
    /// </summary>
    public delegate* unmanaged<FileDescriptor*, void*, int> Fstat;
}

/// <summary>
/// Poll event flags
/// </summary>
[Flags]
public enum PollEvents : short
{
    None = 0,
    In = 0x0001,        // POLLIN - Data available to read
    Pri = 0x0002,       // POLLPRI - Priority data available
    Out = 0x0004,       // POLLOUT - Writing possible
    Err = 0x0008,       // POLLERR - Error condition
    Hup = 0x0010,       // POLLHUP - Hang up
    Nval = 0x0020,      // POLLNVAL - Invalid FD
    RdNorm = 0x0040,    // POLLRDNORM - Normal data available
    RdBand = 0x0080,    // POLLRDBAND - Priority band data available
    WrNorm = 0x0100,    // POLLWRNORM - Writing normal data possible
    WrBand = 0x0200,    // POLLWRBAND - Writing priority data possible
}

/// <summary>
/// Standard errno values
/// </summary>
public static class Errno
{
    public const int SUCCESS = 0;
    public const int EPERM = 1;         // Operation not permitted
    public const int ENOENT = 2;        // No such file or directory
    public const int ESRCH = 3;         // No such process
    public const int EINTR = 4;         // Interrupted system call
    public const int EIO = 5;           // I/O error
    public const int ENXIO = 6;         // No such device or address
    public const int E2BIG = 7;         // Argument list too long
    public const int ENOEXEC = 8;       // Exec format error
    public const int EBADF = 9;         // Bad file descriptor
    public const int ECHILD = 10;       // No child processes
    public const int EAGAIN = 11;       // Resource temporarily unavailable
    public const int ENOMEM = 12;       // Out of memory
    public const int EACCES = 13;       // Permission denied
    public const int EFAULT = 14;       // Bad address
    public const int ENOTBLK = 15;      // Block device required
    public const int EBUSY = 16;        // Device or resource busy
    public const int EEXIST = 17;       // File exists
    public const int EXDEV = 18;        // Cross-device link
    public const int ENODEV = 19;       // No such device
    public const int ENOTDIR = 20;      // Not a directory
    public const int EISDIR = 21;       // Is a directory
    public const int EINVAL = 22;       // Invalid argument
    public const int ENFILE = 23;       // Too many open files in system
    public const int EMFILE = 24;       // Too many open files
    public const int ENOTTY = 25;       // Inappropriate ioctl
    public const int ETXTBSY = 26;      // Text file busy
    public const int EFBIG = 27;        // File too large
    public const int ENOSPC = 28;       // No space left on device
    public const int ESPIPE = 29;       // Illegal seek
    public const int EROFS = 30;        // Read-only file system
    public const int EMLINK = 31;       // Too many links
    public const int EPIPE = 32;        // Broken pipe
    public const int EDOM = 33;         // Math argument out of domain
    public const int ERANGE = 34;       // Math result not representable
    public const int EDEADLK = 35;      // Resource deadlock avoided
    public const int ENAMETOOLONG = 36; // File name too long
    public const int ENOLCK = 37;       // No locks available
    public const int ENOSYS = 38;       // Function not implemented
    public const int ENOTEMPTY = 39;    // Directory not empty
    public const int ELOOP = 40;        // Too many symbolic links
    public const int EWOULDBLOCK = EAGAIN;
    public const int ENOMSG = 42;       // No message of desired type
    public const int EIDRM = 43;        // Identifier removed
    public const int ENOSTR = 60;       // Not a STREAMS device
    public const int ENODATA = 61;      // No data available
    public const int ETIME = 62;        // Timer expired
    public const int ENOSR = 63;        // Out of streams resources
    public const int ENOLINK = 67;      // Link has been severed
    public const int EPROTO = 71;       // Protocol error
    public const int EOVERFLOW = 75;    // Value too large for type
    public const int EBADFD = 77;       // File descriptor in bad state
    public const int ENOTSOCK = 88;     // Socket operation on non-socket
    public const int EDESTADDRREQ = 89; // Destination address required
    public const int EMSGSIZE = 90;     // Message too long
    public const int EPROTOTYPE = 91;   // Wrong protocol type for socket
    public const int ENOPROTOOPT = 92;  // Protocol not available
    public const int EPROTONOSUPPORT = 93; // Protocol not supported
    public const int ESOCKTNOSUPPORT = 94; // Socket type not supported
    public const int EOPNOTSUPP = 95;   // Operation not supported
    public const int ENOTSUP = EOPNOTSUPP;
    public const int EPFNOSUPPORT = 96; // Protocol family not supported
    public const int EAFNOSUPPORT = 97; // Address family not supported
    public const int EADDRINUSE = 98;   // Address already in use
    public const int EADDRNOTAVAIL = 99; // Can't assign requested address
    public const int ENETDOWN = 100;    // Network is down
    public const int ENETUNREACH = 101; // Network is unreachable
    public const int ENETRESET = 102;   // Network dropped connection on reset
    public const int ECONNABORTED = 103; // Software caused connection abort
    public const int ECONNRESET = 104;  // Connection reset by peer
    public const int ENOBUFS = 105;     // No buffer space available
    public const int EISCONN = 106;     // Socket is already connected
    public const int ENOTCONN = 107;    // Socket is not connected
    public const int ESHUTDOWN = 108;   // Cannot send after socket shutdown
    public const int ETOOMANYREFS = 109; // Too many references
    public const int ETIMEDOUT = 110;   // Connection timed out
    public const int ECONNREFUSED = 111; // Connection refused
    public const int EHOSTDOWN = 112;   // Host is down
    public const int EHOSTUNREACH = 113; // No route to host
    public const int EALREADY = 114;    // Operation already in progress
    public const int EINPROGRESS = 115; // Operation now in progress
    public const int ESTALE = 116;      // Stale file handle
    public const int EDQUOT = 122;      // Disk quota exceeded
}

/// <summary>
/// mmap protection flags
/// </summary>
public static class MmapProt
{
    public const int None = 0x0;      // Page cannot be accessed
    public const int Read = 0x1;      // Page can be read
    public const int Write = 0x2;     // Page can be written
    public const int Exec = 0x4;      // Page can be executed
}

/// <summary>
/// mmap flags
/// </summary>
public static class MmapFlags
{
    public const int Shared = 0x01;       // Share changes
    public const int Private = 0x02;      // Changes are private (copy-on-write)
    public const int Fixed = 0x10;        // Interpret addr exactly
    public const int Anonymous = 0x20;    // Don't use a file
    public const int GrowsDown = 0x100;   // Stack-like segment
    public const int Populate = 0x8000;   // Populate (prefault) page tables
    public const int NonBlock = 0x10000;  // Don't block on IO
}

/// <summary>
/// msync flags
/// </summary>
public static class MsyncFlags
{
    public const int Async = 1;       // Sync memory asynchronously
    public const int Invalidate = 2;  // Invalidate cached data
    public const int Sync = 4;        // Synchronous memory sync
}

/// <summary>
/// Standard file descriptor numbers
/// </summary>
public static class StdFd
{
    public const int Stdin = 0;
    public const int Stdout = 1;
    public const int Stderr = 2;
}

/// <summary>
/// File descriptor table operations
/// </summary>
public static unsafe class FdTable
{
    /// <summary>
    /// Allocate a new file descriptor in a process
    /// </summary>
    /// <param name="fds">File descriptor table</param>
    /// <param name="maxFds">Maximum number of file descriptors</param>
    /// <param name="minFd">Minimum FD number to allocate</param>
    /// <returns>File descriptor number, or -EMFILE if table is full</returns>
    public static int Allocate(FileDescriptor* fds, int maxFds, int minFd = 0)
    {
        for (int i = minFd; i < maxFds; i++)
        {
            if (fds[i].Type == FileType.None)
            {
                return i;
            }
        }
        return -Errno.EMFILE;
    }

    /// <summary>
    /// Duplicate a file descriptor
    /// </summary>
    /// <param name="fds">File descriptor table</param>
    /// <param name="maxFds">Maximum number of file descriptors</param>
    /// <param name="oldFd">Original FD to duplicate</param>
    /// <returns>New file descriptor number, or negative error</returns>
    public static int Dup(FileDescriptor* fds, int maxFds, int oldFd)
    {
        if (oldFd < 0 || oldFd >= maxFds || fds[oldFd].Type == FileType.None)
            return -Errno.EBADF;

        int newFd = Allocate(fds, maxFds);
        if (newFd < 0)
            return newFd;

        // Copy the file descriptor
        fds[newFd] = fds[oldFd];
        fds[newFd].RefCount++;

        // Call dup operation if defined
        if (fds[oldFd].Ops != null && fds[oldFd].Ops->Dup != null)
        {
            int result = fds[oldFd].Ops->Dup(&fds[oldFd]);
            if (result < 0)
            {
                fds[newFd] = default;
                return result;
            }
        }

        return newFd;
    }

    /// <summary>
    /// Duplicate a file descriptor to a specific number
    /// </summary>
    public static int Dup2(FileDescriptor* fds, int maxFds, int oldFd, int newFd)
    {
        if (oldFd < 0 || oldFd >= maxFds || fds[oldFd].Type == FileType.None)
            return -Errno.EBADF;

        if (newFd < 0 || newFd >= maxFds)
            return -Errno.EBADF;

        if (oldFd == newFd)
            return newFd;

        // Close existing FD at newFd if open
        if (fds[newFd].Type != FileType.None)
        {
            Close(&fds[newFd]);
        }

        // Copy the file descriptor
        fds[newFd] = fds[oldFd];
        fds[newFd].RefCount++;

        // Call dup operation if defined
        if (fds[oldFd].Ops != null && fds[oldFd].Ops->Dup != null)
        {
            fds[oldFd].Ops->Dup(&fds[oldFd]);
        }

        return newFd;
    }

    /// <summary>
    /// Close a file descriptor
    /// </summary>
    public static int Close(FileDescriptor* fd)
    {
        if (fd == null || fd->Type == FileType.None)
            return -Errno.EBADF;

        int result = 0;

        // Call close operation if defined
        if (fd->Ops != null && fd->Ops->Close != null)
        {
            result = fd->Ops->Close(fd);
        }

        // Clear the descriptor
        *fd = default;

        return result;
    }

    /// <summary>
    /// Clone file descriptor table (for fork)
    /// </summary>
    public static bool CloneTable(FileDescriptor* src, FileDescriptor* dst, int count)
    {
        if (src == null || dst == null || count <= 0)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (src[i].Type != FileType.None)
            {
                dst[i] = src[i];
                dst[i].RefCount++;

                // Call dup for each open FD
                if (src[i].Ops != null && src[i].Ops->Dup != null)
                {
                    src[i].Ops->Dup(&src[i]);
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Close all FDs with close-on-exec flag (for exec)
    /// </summary>
    public static void CloseOnExec(FileDescriptor* fds, int count)
    {
        for (int i = 0; i < count; i++)
        {
            if (fds[i].Type != FileType.None &&
                (fds[i].Flags & FileFlags.CloseOnExec) != 0)
            {
                Close(&fds[i]);
            }
        }
    }
}
