// ProtonOS DDK - Proc Filesystem Implementation
// Virtual filesystem that exposes kernel and system information.

using System;
using System.Collections.Generic;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Storage.Proc;

/// <summary>
/// Virtual /proc filesystem that exposes kernel and system information.
/// Files are dynamically generated when opened.
/// </summary>
public class ProcFileSystem : IFileSystem
{
    // Path to generator mapping
    private readonly Dictionary<string, IProcContentGenerator> _generators;

    // Set of directory paths (for enumeration)
    private readonly HashSet<string> _directories;

    private bool _isMounted;

    /// <summary>
    /// Create a new proc filesystem instance.
    /// </summary>
    public ProcFileSystem()
    {
        _generators = new Dictionary<string, IProcContentGenerator>();
        _directories = new HashSet<string>();
        _isMounted = false;

        // Root directory always exists
        _directories.Add("/");
    }

    /// <summary>
    /// Register a proc entry.
    /// </summary>
    /// <param name="path">Path relative to /proc (e.g., "/cpuinfo", "/net/dev").</param>
    /// <param name="generator">Content generator for this entry.</param>
    public void Register(string path, IProcContentGenerator generator)
    {
        // Normalize path
        if (!path.StartsWith("/"))
            path = "/" + path;

        _generators[path] = generator;

        // Ensure parent directories exist
        EnsureDirectories(path);
    }

    /// <summary>
    /// Ensure all parent directories for a path exist in our directory set.
    /// </summary>
    private void EnsureDirectories(string path)
    {
        // Walk up the path and add each directory
        int idx = path.LastIndexOf('/');
        while (idx > 0)
        {
            string dir = path.Substring(0, idx);
            _directories.Add(dir);
            idx = dir.LastIndexOf('/');
        }
    }

    #region IFileSystem Implementation

    /// <inheritdoc/>
    public string FilesystemName => "procfs";

    /// <inheritdoc/>
    public FilesystemCapabilities Capabilities => FilesystemCapabilities.Read | FilesystemCapabilities.CaseSensitive;

    /// <inheritdoc/>
    public bool IsMounted => _isMounted;

    /// <inheritdoc/>
    public string? VolumeLabel => "proc";

    /// <inheritdoc/>
    public ulong TotalBytes => 0; // Virtual filesystem has no fixed size

    /// <inheritdoc/>
    public ulong FreeBytes => 0;

    /// <inheritdoc/>
    public FileResult Mount(IBlockDevice? device, bool readOnly = false)
    {
        // Proc filesystem doesn't need a block device
        _isMounted = true;
        return FileResult.Success;
    }

    /// <inheritdoc/>
    public FileResult Unmount()
    {
        _isMounted = false;
        return FileResult.Success;
    }

    /// <inheritdoc/>
    public bool Probe(IBlockDevice device)
    {
        // Proc filesystem doesn't use block devices
        return false;
    }

    /// <inheritdoc/>
    public FileResult GetInfo(string path, out FileInfo? info)
    {
        info = null;

        // Normalize path
        if (string.IsNullOrEmpty(path) || path == "/")
        {
            info = new FileInfo
            {
                Name = "",
                Path = "/",
                Type = FileEntryType.Directory,
                Size = 0,
                Attributes = FileAttributes.ReadOnly
            };
            return FileResult.Success;
        }

        if (!path.StartsWith("/"))
            path = "/" + path;

        // Check if it's a registered file
        if (_generators.ContainsKey(path))
        {
            string name = path;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
                name = path.Substring(lastSlash + 1);

            info = new FileInfo
            {
                Name = name,
                Path = path,
                Type = FileEntryType.File,
                Size = 0, // Size unknown until content is generated
                Attributes = FileAttributes.ReadOnly
            };
            return FileResult.Success;
        }

        // Check if it's a directory
        if (_directories.Contains(path))
        {
            string name = path;
            int lastSlash = path.LastIndexOf('/');
            if (lastSlash >= 0)
                name = path.Substring(lastSlash + 1);

            info = new FileInfo
            {
                Name = name,
                Path = path,
                Type = FileEntryType.Directory,
                Size = 0,
                Attributes = FileAttributes.ReadOnly
            };
            return FileResult.Success;
        }

        return FileResult.NotFound;
    }

    /// <inheritdoc/>
    public FileResult OpenFile(string path, FileMode mode, FileAccess access, out IFileHandle? handle)
    {
        handle = null;

        // Only support read access
        if ((access & FileAccess.Write) != 0)
            return FileResult.ReadOnly;

        // Only support opening existing files
        if (mode != FileMode.Open)
            return FileResult.ReadOnly;

        // Normalize path
        if (!path.StartsWith("/"))
            path = "/" + path;

        // Find the generator
        if (!_generators.TryGetValue(path, out var generator))
            return FileResult.NotFound;

        // Create file handle with generated content
        handle = new ProcFileHandle(generator);
        return FileResult.Success;
    }

    /// <inheritdoc/>
    public FileResult OpenDirectory(string path, out IDirectoryHandle? handle)
    {
        handle = null;

        // Normalize path
        if (string.IsNullOrEmpty(path))
            path = "/";
        if (!path.StartsWith("/"))
            path = "/" + path;

        // Remove trailing slash (except for root)
        while (path.Length > 1 && path.EndsWith("/"))
            path = path.Substring(0, path.Length - 1);

        // Check if directory exists
        if (path != "/" && !_directories.Contains(path))
            return FileResult.NotFound;

        // Build list of entries in this directory
        var entries = new List<FileInfo>();
        string prefix = path == "/" ? "/" : path + "/";

        // Add subdirectories
        foreach (var dir in _directories)
        {
            if (dir == path)
                continue;

            if (dir.StartsWith(prefix))
            {
                // Check if this is a direct child
                string remainder = dir.Substring(prefix.Length);
                if (!remainder.Contains("/"))
                {
                    entries.Add(new FileInfo
                    {
                        Name = remainder,
                        Path = dir,
                        Type = FileEntryType.Directory,
                        Size = 0,
                        Attributes = FileAttributes.ReadOnly
                    });
                }
            }
        }

        // Add files
        foreach (var kvp in _generators)
        {
            string filePath = kvp.Key;

            if (filePath.StartsWith(prefix) || (path == "/" && filePath.StartsWith("/")))
            {
                string remainder;
                if (path == "/")
                    remainder = filePath.Substring(1);
                else
                    remainder = filePath.Substring(prefix.Length);

                // Check if this is a direct child
                if (!remainder.Contains("/"))
                {
                    entries.Add(new FileInfo
                    {
                        Name = remainder,
                        Path = filePath,
                        Type = FileEntryType.File,
                        Size = 0,
                        Attributes = FileAttributes.ReadOnly
                    });
                }
            }
        }

        handle = new ProcDirectoryHandle(entries);
        return FileResult.Success;
    }

    /// <inheritdoc/>
    public FileResult CreateDirectory(string path)
    {
        return FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public FileResult DeleteFile(string path)
    {
        return FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public FileResult DeleteDirectory(string path)
    {
        return FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public FileResult Rename(string oldPath, string newPath)
    {
        return FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public bool Exists(string path)
    {
        // Normalize path
        if (string.IsNullOrEmpty(path) || path == "/")
            return true;

        if (!path.StartsWith("/"))
            path = "/" + path;

        return _generators.ContainsKey(path) || _directories.Contains(path);
    }

    #endregion

    #region IDriver Implementation

    /// <inheritdoc/>
    public string DriverName => "procfs";

    /// <inheritdoc/>
    public Version DriverVersion => new Version(1, 0, 0, 0);

    /// <inheritdoc/>
    public DriverType Type => DriverType.Filesystem;

    /// <inheritdoc/>
    public DriverState State => _isMounted ? DriverState.Running : DriverState.Stopped;

    /// <inheritdoc/>
    public bool Initialize()
    {
        return true;
    }

    /// <inheritdoc/>
    public void Shutdown()
    {
        Unmount();
    }

    /// <inheritdoc/>
    public void Suspend()
    {
    }

    /// <inheritdoc/>
    public void Resume()
    {
    }

    #endregion
}
