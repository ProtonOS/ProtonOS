// ProtonOS kernel - Virtual File System
// Manages mount points and routes file operations to filesystem drivers.

using System;
using ProtonOS.Platform;
using ProtonOS.Memory;

namespace ProtonOS.IO;

/// <summary>
/// Filesystem operations function pointers (registered by drivers)
/// </summary>
public unsafe struct FilesystemOps
{
    /// <summary>
    /// Open a file: int Open(void* fsContext, byte* path, int flags, int mode, out void* fileHandle)
    /// Returns 0 on success, negative errno on error
    /// </summary>
    public delegate* unmanaged<void*, byte*, int, int, void**, int> Open;

    /// <summary>
    /// Read from file: int Read(void* fileHandle, byte* buf, int count)
    /// Returns bytes read, 0 for EOF, negative errno on error
    /// </summary>
    public delegate* unmanaged<void*, byte*, int, int> Read;

    /// <summary>
    /// Write to file: int Write(void* fileHandle, byte* buf, int count)
    /// Returns bytes written, negative errno on error
    /// </summary>
    public delegate* unmanaged<void*, byte*, int, int> Write;

    /// <summary>
    /// Seek in file: long Seek(void* fileHandle, long offset, int whence)
    /// Returns new position, negative errno on error
    /// </summary>
    public delegate* unmanaged<void*, long, int, long> Seek;

    /// <summary>
    /// Close file: int Close(void* fileHandle)
    /// Returns 0 on success, negative errno on error
    /// </summary>
    public delegate* unmanaged<void*, int> Close;

    /// <summary>
    /// Check if path exists: bool Exists(void* fsContext, byte* path)
    /// </summary>
    public delegate* unmanaged<void*, byte*, bool> Exists;
}

/// <summary>
/// Mount point entry
/// </summary>
public unsafe struct MountPoint
{
    /// <summary>
    /// Mount path (e.g., "/", "/mnt/disk")
    /// </summary>
    public fixed byte Path[256];

    /// <summary>
    /// Path length
    /// </summary>
    public int PathLength;

    /// <summary>
    /// Filesystem operations
    /// </summary>
    public FilesystemOps Ops;

    /// <summary>
    /// Filesystem-specific context (passed to operations)
    /// </summary>
    public void* FsContext;

    /// <summary>
    /// Is this mount point in use
    /// </summary>
    public bool InUse;
}

/// <summary>
/// Open file tracking for VFS
/// </summary>
public unsafe struct VfsFileHandle
{
    /// <summary>
    /// Driver's file handle
    /// </summary>
    public void* DriverHandle;

    /// <summary>
    /// Pointer to the mount point's operations
    /// </summary>
    public FilesystemOps* Ops;

    /// <summary>
    /// Is this handle in use
    /// </summary>
    public bool InUse;
}

/// <summary>
/// Virtual File System - routes file operations to mounted filesystems
/// </summary>
public static unsafe class VFS
{
    private const int MaxMounts = 16;
    private const int MaxOpenFiles = 256;

    private static MountPoint* _mounts;
    private static VfsFileHandle* _openFiles;
    private static bool _initialized;

    /// <summary>
    /// Initialize the VFS
    /// </summary>
    public static void Init()
    {
        if (_initialized)
            return;

        _mounts = (MountPoint*)HeapAllocator.AllocZeroed((ulong)(sizeof(MountPoint) * MaxMounts));
        _openFiles = (VfsFileHandle*)HeapAllocator.AllocZeroed((ulong)(sizeof(VfsFileHandle) * MaxOpenFiles));

        if (_mounts == null || _openFiles == null)
        {
            DebugConsole.WriteLine("[VFS] Failed to allocate memory!");
            return;
        }

        _initialized = true;
        DebugConsole.WriteLine("[VFS] Initialized");
    }

    /// <summary>
    /// Mount a filesystem at the specified path
    /// </summary>
    /// <param name="path">Mount point path</param>
    /// <param name="ops">Filesystem operations</param>
    /// <param name="fsContext">Filesystem-specific context</param>
    public static bool Mount(string path, FilesystemOps ops, void* fsContext)
    {
        if (!_initialized || path == null)
            return false;

        // Find free mount slot
        for (int i = 0; i < MaxMounts; i++)
        {
            if (!_mounts[i].InUse)
            {
                // Copy path
                int len = path.Length;
                if (len >= 255) len = 255;

                for (int j = 0; j < len; j++)
                    _mounts[i].Path[j] = (byte)path[j];
                _mounts[i].Path[len] = 0;
                _mounts[i].PathLength = len;
                _mounts[i].Ops = ops;
                _mounts[i].FsContext = fsContext;
                _mounts[i].InUse = true;

                DebugConsole.Write("[VFS] Mounted filesystem at ");
                DebugConsole.WriteLine(path);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Unmount a filesystem
    /// </summary>
    public static bool Unmount(string path)
    {
        if (!_initialized || path == null)
            return false;

        for (int i = 0; i < MaxMounts; i++)
        {
            if (_mounts[i].InUse && PathEquals(_mounts[i].Path, _mounts[i].PathLength, path))
            {
                _mounts[i].FsContext = null;
                _mounts[i].InUse = false;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Open a file
    /// </summary>
    /// <param name="path">File path</param>
    /// <param name="flags">Open flags (O_* flags)</param>
    /// <param name="mode">Creation mode</param>
    /// <returns>VFS file handle index, or negative error</returns>
    public static int Open(byte* path, int flags, int mode)
    {
        if (!_initialized || path == null)
            return -Errno.EINVAL;

        // Get path length
        int pathLen = 0;
        while (path[pathLen] != 0 && pathLen < 4095)
            pathLen++;

        if (pathLen == 0)
            return -Errno.EINVAL;

        // Find the filesystem for this path
        MountPoint* mount = null;
        int mountPrefixLen = 0;

        for (int i = 0; i < MaxMounts; i++)
        {
            if (!_mounts[i].InUse)
                continue;

            // Check if this mount point is a prefix of the path
            if (PathStartsWith(path, pathLen, _mounts[i].Path, _mounts[i].PathLength))
            {
                // Use the longest matching mount point
                if (_mounts[i].PathLength > mountPrefixLen)
                {
                    mount = &_mounts[i];
                    mountPrefixLen = _mounts[i].PathLength;
                }
            }
        }

        if (mount == null || mount->Ops.Open == null)
            return -Errno.ENOENT;

        // Get relative path
        byte* relativePath = GetRelativePath(path, pathLen, mountPrefixLen);

        // Open through filesystem driver
        void* driverHandle = null;
        int result = mount->Ops.Open(mount->FsContext, relativePath, flags, mode, &driverHandle);

        if (result < 0)
            return result;

        // Allocate VFS handle
        int vfsHandle = AllocateHandle(driverHandle, &mount->Ops);
        if (vfsHandle < 0)
        {
            if (mount->Ops.Close != null)
                mount->Ops.Close(driverHandle);
            return -Errno.EMFILE;
        }

        return vfsHandle;
    }

    /// <summary>
    /// Read from an open file
    /// </summary>
    public static int Read(int vfsHandle, byte* buf, int count)
    {
        if (!_initialized || vfsHandle < 0 || vfsHandle >= MaxOpenFiles)
            return -Errno.EBADF;

        var vfs = &_openFiles[vfsHandle];
        if (!vfs->InUse || vfs->Ops == null || vfs->Ops->Read == null)
            return -Errno.EBADF;

        return vfs->Ops->Read(vfs->DriverHandle, buf, count);
    }

    /// <summary>
    /// Write to an open file
    /// </summary>
    public static int Write(int vfsHandle, byte* buf, int count)
    {
        if (!_initialized || vfsHandle < 0 || vfsHandle >= MaxOpenFiles)
            return -Errno.EBADF;

        var vfs = &_openFiles[vfsHandle];
        if (!vfs->InUse || vfs->Ops == null || vfs->Ops->Write == null)
            return -Errno.EBADF;

        return vfs->Ops->Write(vfs->DriverHandle, buf, count);
    }

    /// <summary>
    /// Seek in an open file
    /// </summary>
    public static long Seek(int vfsHandle, long offset, int whence)
    {
        if (!_initialized || vfsHandle < 0 || vfsHandle >= MaxOpenFiles)
            return -Errno.EBADF;

        var vfs = &_openFiles[vfsHandle];
        if (!vfs->InUse || vfs->Ops == null || vfs->Ops->Seek == null)
            return -Errno.EBADF;

        return vfs->Ops->Seek(vfs->DriverHandle, offset, whence);
    }

    /// <summary>
    /// Close an open file
    /// </summary>
    public static int Close(int vfsHandle)
    {
        if (!_initialized || vfsHandle < 0 || vfsHandle >= MaxOpenFiles)
            return -Errno.EBADF;

        var vfs = &_openFiles[vfsHandle];
        if (!vfs->InUse)
            return -Errno.EBADF;

        int result = 0;
        if (vfs->Ops != null && vfs->Ops->Close != null)
            result = vfs->Ops->Close(vfs->DriverHandle);

        vfs->DriverHandle = null;
        vfs->Ops = null;
        vfs->InUse = false;

        return result;
    }

    // Helper methods

    private static int AllocateHandle(void* driverHandle, FilesystemOps* ops)
    {
        for (int i = 0; i < MaxOpenFiles; i++)
        {
            if (!_openFiles[i].InUse)
            {
                _openFiles[i].DriverHandle = driverHandle;
                _openFiles[i].Ops = ops;
                _openFiles[i].InUse = true;
                return i;
            }
        }
        return -1;
    }

    private static bool PathEquals(byte* a, int aLen, string b)
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

    private static bool PathStartsWith(byte* path, int pathLen, byte* prefix, int prefixLen)
    {
        if (pathLen < prefixLen)
            return false;

        // Root mount matches everything
        if (prefixLen == 1 && prefix[0] == '/')
            return path[0] == '/';

        for (int i = 0; i < prefixLen; i++)
        {
            if (path[i] != prefix[i])
                return false;
        }

        // Ensure we're at a path boundary
        if (pathLen > prefixLen && path[prefixLen] != '/')
            return false;

        return true;
    }

    // Allocated root path buffer "/"
    private static byte* _rootPath;

    private static byte* GetRelativePath(byte* path, int pathLen, int prefixLen)
    {
        // Skip the mount prefix
        int start = prefixLen;

        // If path is exactly the mount point, return "/"
        if (start >= pathLen)
        {
            // Lazy initialize root path
            if (_rootPath == null)
            {
                _rootPath = (byte*)HeapAllocator.Alloc(2);
                _rootPath[0] = (byte)'/';
                _rootPath[1] = 0;
            }
            return _rootPath;
        }

        // Return pointer into original path after mount prefix
        return path + start;
    }
}
