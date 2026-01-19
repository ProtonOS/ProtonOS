// ProtonOS DDK - Proc Filesystem Directory Handle
// Enumerates registered proc entries in a directory.

using System;
using System.Collections.Generic;

namespace ProtonOS.DDK.Storage.Proc;

/// <summary>
/// Directory handle for proc filesystem directories.
/// Enumerates the registered entries within a directory path.
/// </summary>
public class ProcDirectoryHandle : IDirectoryHandle
{
    private readonly List<FileInfo> _entries;
    private int _index;
    private bool _isOpen;

    /// <summary>
    /// Create a new proc directory handle.
    /// </summary>
    /// <param name="entries">List of entries in this directory.</param>
    public ProcDirectoryHandle(List<FileInfo> entries)
    {
        _entries = entries ?? new List<FileInfo>();
        _index = 0;
        _isOpen = true;
    }

    /// <inheritdoc/>
    public bool IsOpen => _isOpen;

    /// <inheritdoc/>
    public FileInfo? ReadNext()
    {
        if (!_isOpen)
            return null;

        if (_index >= _entries.Count)
            return null;

        return _entries[_index++];
    }

    /// <inheritdoc/>
    public void Rewind()
    {
        _index = 0;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isOpen = false;
    }
}
