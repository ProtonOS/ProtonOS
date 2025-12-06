// ProtonOS DDK - Virtual Filesystem Layer
// Provides unified filesystem access across mounted filesystems.

using System;
using System.Collections.Generic;

namespace ProtonOS.DDK.Storage;

/// <summary>
/// Mount point information.
/// </summary>
public class MountPoint
{
    /// <summary>Mount path (e.g., "/" or "/boot").</summary>
    public string Path { get; }

    /// <summary>Filesystem mounted at this point.</summary>
    public IFileSystem FileSystem { get; }

    /// <summary>Block device containing the filesystem.</summary>
    public IBlockDevice Device { get; }

    /// <summary>Is this mount read-only?</summary>
    public bool IsReadOnly { get; }

    public MountPoint(string path, IFileSystem fileSystem, IBlockDevice device, bool readOnly)
    {
        Path = path;
        FileSystem = fileSystem;
        Device = device;
        IsReadOnly = readOnly;
    }
}

/// <summary>
/// Virtual Filesystem - provides unified filesystem access.
/// </summary>
public static class VFS
{
    private static List<MountPoint> _mounts = new();
    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>
    /// All active mount points.
    /// </summary>
    public static IReadOnlyList<MountPoint> MountPoints => _mounts;

    /// <summary>
    /// Initialize the VFS.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _mounts = new List<MountPoint>();
        _initialized = true;
    }

    /// <summary>
    /// Mount a filesystem at a path.
    /// </summary>
    /// <param name="path">Mount point path</param>
    /// <param name="fileSystem">Filesystem to mount</param>
    /// <param name="device">Block device containing the filesystem</param>
    /// <param name="readOnly">Mount as read-only</param>
    public static FileResult Mount(string path, IFileSystem fileSystem, IBlockDevice device, bool readOnly = false)
    {
        if (!_initialized)
            Initialize();

        // Normalize path
        path = NormalizePath(path);

        // Check if already mounted
        foreach (var m in _mounts)
        {
            if (m.Path == path)
                return FileResult.AlreadyExists;
        }

        // Mount the filesystem
        var result = fileSystem.Mount(device, readOnly);
        if (result != FileResult.Success)
            return result;

        // Add mount point
        lock (_lock)
        {
            _mounts.Add(new MountPoint(path, fileSystem, device, readOnly));

            // Sort by path length (longest first) for correct matching
            _mounts.Sort((a, b) => b.Path.Length.CompareTo(a.Path.Length));
        }

        return FileResult.Success;
    }

    /// <summary>
    /// Unmount a filesystem.
    /// </summary>
    public static FileResult Unmount(string path)
    {
        path = NormalizePath(path);

        lock (_lock)
        {
            for (int i = 0; i < _mounts.Count; i++)
            {
                if (_mounts[i].Path == path)
                {
                    var mount = _mounts[i];
                    var result = mount.FileSystem.Unmount();
                    if (result != FileResult.Success)
                        return result;

                    _mounts.RemoveAt(i);
                    return FileResult.Success;
                }
            }
        }

        return FileResult.NotFound;
    }

    /// <summary>
    /// Find the mount point for a path.
    /// </summary>
    public static MountPoint? FindMount(string path)
    {
        path = NormalizePath(path);

        // Mounts are sorted longest-first, so first match is most specific
        foreach (var mount in _mounts)
        {
            if (path.StartsWith(mount.Path))
            {
                // Make sure it's a complete path component match
                if (path.Length == mount.Path.Length ||
                    mount.Path == "/" ||
                    path[mount.Path.Length] == '/')
                {
                    return mount;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get the path relative to a mount point.
    /// </summary>
    public static string GetRelativePath(string fullPath, MountPoint mount)
    {
        fullPath = NormalizePath(fullPath);

        if (mount.Path == "/")
            return fullPath;

        if (fullPath.Length <= mount.Path.Length)
            return "/";

        return fullPath.Substring(mount.Path.Length);
    }

    /// <summary>
    /// Get information about a file or directory.
    /// </summary>
    public static FileResult GetInfo(string path, out FileInfo? info)
    {
        info = null;
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.GetInfo(relativePath, out info);
    }

    /// <summary>
    /// Open a file.
    /// </summary>
    public static FileResult OpenFile(string path, FileMode mode, FileAccess access, out IFileHandle? handle)
    {
        handle = null;
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        // Check write access on read-only mounts
        if (mount.IsReadOnly && (access & FileAccess.Write) != 0)
            return FileResult.ReadOnly;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.OpenFile(relativePath, mode, access, out handle);
    }

    /// <summary>
    /// Open a directory for enumeration.
    /// </summary>
    public static FileResult OpenDirectory(string path, out IDirectoryHandle? handle)
    {
        handle = null;
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.OpenDirectory(relativePath, out handle);
    }

    /// <summary>
    /// Create a directory.
    /// </summary>
    public static FileResult CreateDirectory(string path)
    {
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        if (mount.IsReadOnly)
            return FileResult.ReadOnly;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.CreateDirectory(relativePath);
    }

    /// <summary>
    /// Delete a file.
    /// </summary>
    public static FileResult DeleteFile(string path)
    {
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        if (mount.IsReadOnly)
            return FileResult.ReadOnly;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.DeleteFile(relativePath);
    }

    /// <summary>
    /// Delete an empty directory.
    /// </summary>
    public static FileResult DeleteDirectory(string path)
    {
        var mount = FindMount(path);
        if (mount == null)
            return FileResult.NotFound;

        if (mount.IsReadOnly)
            return FileResult.ReadOnly;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.DeleteDirectory(relativePath);
    }

    /// <summary>
    /// Rename/move a file or directory.
    /// </summary>
    public static FileResult Rename(string oldPath, string newPath)
    {
        var oldMount = FindMount(oldPath);
        var newMount = FindMount(newPath);

        if (oldMount == null || newMount == null)
            return FileResult.NotFound;

        // Can't move across filesystems (would need copy + delete)
        if (oldMount != newMount)
            return FileResult.NotSupported;

        if (oldMount.IsReadOnly)
            return FileResult.ReadOnly;

        var oldRelative = GetRelativePath(oldPath, oldMount);
        var newRelative = GetRelativePath(newPath, oldMount);
        return oldMount.FileSystem.Rename(oldRelative, newRelative);
    }

    /// <summary>
    /// Check if a path exists.
    /// </summary>
    public static bool Exists(string path)
    {
        var mount = FindMount(path);
        if (mount == null)
            return false;

        var relativePath = GetRelativePath(path, mount);
        return mount.FileSystem.Exists(relativePath);
    }

    /// <summary>
    /// Normalize a path (remove double slashes, resolve . and .., etc.).
    /// </summary>
    public static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return "/";

        // Replace backslashes
        path = path.Replace('\\', '/');

        // Ensure starts with /
        if (!path.StartsWith("/"))
            path = "/" + path;

        // Remove trailing slash (except for root)
        while (path.Length > 1 && path.EndsWith("/"))
            path = path.Substring(0, path.Length - 1);

        // Remove double slashes
        while (path.Contains("//"))
            path = path.Replace("//", "/");

        // TODO: Resolve . and .. components

        return path;
    }

    /// <summary>
    /// Get the directory portion of a path.
    /// </summary>
    public static string GetDirectory(string path)
    {
        path = NormalizePath(path);
        int lastSlash = path.LastIndexOf('/');
        if (lastSlash <= 0)
            return "/";
        return path.Substring(0, lastSlash);
    }

    /// <summary>
    /// Get the filename portion of a path.
    /// </summary>
    public static string GetFileName(string path)
    {
        path = NormalizePath(path);
        int lastSlash = path.LastIndexOf('/');
        if (lastSlash < 0 || lastSlash == path.Length - 1)
            return "";
        return path.Substring(lastSlash + 1);
    }

    /// <summary>
    /// Combine path components.
    /// </summary>
    public static string Combine(string path1, string path2)
    {
        if (string.IsNullOrEmpty(path1))
            return NormalizePath(path2);
        if (string.IsNullOrEmpty(path2))
            return NormalizePath(path1);

        // If path2 is absolute, return it
        if (path2.StartsWith("/") || path2.StartsWith("\\"))
            return NormalizePath(path2);

        path1 = NormalizePath(path1);
        if (!path1.EndsWith("/"))
            path1 += "/";

        return NormalizePath(path1 + path2);
    }
}
