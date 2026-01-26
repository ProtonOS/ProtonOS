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

    [DllImport("*", EntryPoint = "Kernel_RegisterGetdentsHandler")]
    private static extern void Kernel_RegisterGetdentsHandler(delegate* unmanaged<byte*, byte*, int, long*, int> handler);

    [DllImport("*", EntryPoint = "Kernel_RegisterAccessHandler")]
    private static extern void Kernel_RegisterAccessHandler(delegate* unmanaged<byte*, int, int> handler);

    [DllImport("*", EntryPoint = "Kernel_RegisterRenameHandler")]
    private static extern void Kernel_RegisterRenameHandler(delegate* unmanaged<byte*, byte*, int> handler);

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
        Kernel_RegisterGetdentsHandler(&GetdentsHandler);
        Kernel_RegisterAccessHandler(&AccessHandler);
        Kernel_RegisterRenameHandler(&RenameHandler);

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
    /// Handle getdents syscall by reading directory entries.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int GetdentsHandler(byte* path, byte* buf, int count, long* offset)
    {
        if (path == null || buf == null || offset == null)
            return -14; // EFAULT

        string pathStr = BytePtrToString(path);

        // Open the directory
        IDirectoryHandle? dirHandle;
        var result = VFS.OpenDirectory(pathStr, out dirHandle);
        if (result != FileResult.Success || dirHandle == null)
            return FileResultToErrno(result);

        try
        {
            // Skip to offset
            long currentOffset = 0;
            while (currentOffset < *offset)
            {
                var entry = dirHandle.ReadNext();
                if (entry == null)
                {
                    *offset = currentOffset;
                    return 0; // EOF
                }
                currentOffset++;
            }

            int bytesWritten = 0;
            int remaining = count;

            while (remaining > 0)
            {
                var entry = dirHandle.ReadNext();
                if (entry == null)
                    break;

                // Calculate record length (header + name + null + padding to 8-byte boundary)
                int nameLen = entry.Name.Length;
                int recLen = 8 + 8 + 2 + 1 + nameLen + 1; // d_ino + d_off + d_reclen + d_type + name + null
                recLen = (recLen + 7) & ~7; // Round up to 8-byte boundary

                if (recLen > remaining)
                    break; // Buffer full

                // Write the dirent structure
                byte* dirent = buf + bytesWritten;

                // d_ino (8 bytes) - use a hash of the name as inode
                ulong ino = 1;
                for (int i = 0; i < nameLen; i++)
                    ino = ino * 31 + (uint)entry.Name[i];
                *(ulong*)dirent = ino;

                // d_off (8 bytes) - offset to next entry
                currentOffset++;
                *(long*)(dirent + 8) = currentOffset;

                // d_reclen (2 bytes)
                *(ushort*)(dirent + 16) = (ushort)recLen;

                // d_type (1 byte)
                byte dtype = entry.Type switch
                {
                    FileEntryType.Directory => 4,  // DT_DIR
                    FileEntryType.File => 8,       // DT_REG
                    FileEntryType.SymLink => 10,   // DT_LNK
                    FileEntryType.BlockDevice => 6, // DT_BLK
                    FileEntryType.CharDevice => 2,  // DT_CHR
                    FileEntryType.Fifo => 1,        // DT_FIFO
                    FileEntryType.Socket => 12,     // DT_SOCK
                    _ => 0,                         // DT_UNKNOWN
                };
                dirent[18] = dtype;

                // d_name (variable, null-terminated)
                for (int i = 0; i < nameLen; i++)
                    dirent[19 + i] = (byte)entry.Name[i];
                dirent[19 + nameLen] = 0;

                bytesWritten += recLen;
                remaining -= recLen;
            }

            *offset = currentOffset;
            return bytesWritten;
        }
        finally
        {
            dirHandle.Dispose();
        }
    }

    /// <summary>
    /// Handle access syscall by checking file existence/permissions.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int AccessHandler(byte* path, int mode)
    {
        if (path == null)
            return -14; // EFAULT

        string pathStr = BytePtrToString(path);

        // F_OK (0) = check existence
        if (mode == 0)
        {
            if (VFS.Exists(pathStr))
                return 0;
            return -2; // ENOENT
        }

        // For other modes (R_OK, W_OK, X_OK), check if file exists first
        FileInfo? info;
        var result = VFS.GetInfo(pathStr, out info);
        if (result != FileResult.Success)
            return FileResultToErrno(result);

        // For now, assume all permissions are granted if the file exists
        // A real implementation would check against the process's UID/GID
        return 0;
    }

    /// <summary>
    /// Handle rename syscall by calling DDK VFS.
    /// </summary>
    [UnmanagedCallersOnly]
    private static int RenameHandler(byte* oldpath, byte* newpath)
    {
        if (oldpath == null || newpath == null)
            return -14; // EFAULT

        string oldPathStr = BytePtrToString(oldpath);
        string newPathStr = BytePtrToString(newpath);

        var result = VFS.Rename(oldPathStr, newPathStr);
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
