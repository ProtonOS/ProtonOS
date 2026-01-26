// ProtonOS DDK - Syscall Bridge for Filesystem Operations
// Provides function pointers that kernel syscalls can call into DDK VFS.

using System.Runtime.InteropServices;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Storage;

/// <summary>
/// Bridges kernel syscalls to DDK VFS filesystem operations.
/// </summary>
public static unsafe class SyscallBridge
{
    private static bool _initialized;

    // Kernel imports for registering syscall handlers
    [DllImport("*", EntryPoint = "Kernel_RegisterMkdirHandler")]
    private static extern void Kernel_RegisterMkdirHandler(delegate* unmanaged<byte*, int, int> handler);

    [DllImport("*", EntryPoint = "Kernel_RegisterRmdirHandler")]
    private static extern void Kernel_RegisterRmdirHandler(delegate* unmanaged<byte*, int> handler);

    [DllImport("*", EntryPoint = "Kernel_RegisterUnlinkHandler")]
    private static extern void Kernel_RegisterUnlinkHandler(delegate* unmanaged<byte*, int> handler);

    /// <summary>
    /// Initialize the syscall bridge by registering handlers with the kernel.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Register handlers with kernel syscall dispatch via kernel exports
        Kernel_RegisterMkdirHandler(&MkdirHandler);
        Kernel_RegisterRmdirHandler(&RmdirHandler);
        Kernel_RegisterUnlinkHandler(&UnlinkHandler);

        _initialized = true;
        Debug.WriteLine("[SyscallBridge] Filesystem syscall handlers registered");
    }

    /// <summary>
    /// Handle mkdir syscall by calling DDK VFS.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int MkdirHandler(byte* path, int mode)
    {
        if (path == null)
            return -14; // EFAULT

        string pathStr = BytePtrToString(path);
        var result = VFS.CreateDirectory(pathStr);
        return FileResultToErrno(result);
    }

    /// <summary>
    /// Handle rmdir syscall by calling DDK VFS.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int RmdirHandler(byte* path)
    {
        if (path == null)
            return -14; // EFAULT

        string pathStr = BytePtrToString(path);
        var result = VFS.DeleteDirectory(pathStr);
        return FileResultToErrno(result);
    }

    /// <summary>
    /// Handle unlink syscall by calling DDK VFS.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int UnlinkHandler(byte* path)
    {
        if (path == null)
            return -14; // EFAULT

        string pathStr = BytePtrToString(path);
        var result = VFS.DeleteFile(pathStr);
        return FileResultToErrno(result);
    }

    /// <summary>
    /// Convert null-terminated byte* path to managed string.
    /// </summary>
    private static string BytePtrToString(byte* path)
    {
        if (path == null)
            return "";

        int len = 0;
        while (len < 4095 && path[len] != 0)
            len++;

        if (len == 0)
            return "";

        var chars = new char[len];
        for (int i = 0; i < len; i++)
            chars[i] = (char)path[i];

        return new string(chars);
    }

    /// <summary>
    /// Convert FileResult to negative errno value.
    /// </summary>
    private static int FileResultToErrno(FileResult result)
    {
        return result switch
        {
            FileResult.Success => 0,
            FileResult.NotFound => -2,        // ENOENT
            FileResult.AlreadyExists => -17,  // EEXIST
            FileResult.AccessDenied => -13,   // EACCES
            FileResult.InvalidPath => -22,    // EINVAL
            FileResult.NotEmpty => -39,       // ENOTEMPTY
            FileResult.NoSpace => -28,        // ENOSPC
            FileResult.IoError => -5,         // EIO
            FileResult.ReadOnly => -30,       // EROFS
            FileResult.NotADirectory => -20,  // ENOTDIR
            FileResult.IsADirectory => -21,   // EISDIR
            FileResult.TooManyOpenFiles => -24, // EMFILE
            FileResult.NameTooLong => -36,    // ENAMETOOLONG
            FileResult.InvalidHandle => -9,   // EBADF
            FileResult.NotSupported => -38,   // ENOSYS
            _ => -5,                          // EIO (default)
        };
    }
}
