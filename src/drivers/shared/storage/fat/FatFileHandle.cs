// ProtonOS FAT Filesystem Driver - File Handle
// Implements IFileHandle for FAT file operations

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Fat;

/// <summary>
/// File handle for FAT filesystem.
/// </summary>
public unsafe class FatFileHandle : IFileHandle
{
    private readonly FatFileSystem _fs;
    private readonly FileAccess _access;
    private uint _parentCluster;
    private int _entryIndex;

    private uint _firstCluster;
    private long _fileSize;
    private long _position;
    private bool _isOpen;

    // Cached current cluster info for sequential reads
    private uint _currentCluster;
    private ulong _currentClusterOffset;  // Byte offset of current cluster start

    /// <summary>
    /// Create a new file handle. Use Init() to set write-related parameters.
    /// </summary>
    public FatFileHandle(FatFileSystem fs, uint firstCluster, uint fileSize, FileAccess access)
    {
        _fs = fs;
        _firstCluster = firstCluster;
        _fileSize = fileSize;
        _access = access;
        _isOpen = true;

        _currentCluster = firstCluster;
        _currentClusterOffset = 0;
    }

    /// <summary>
    /// Initialize write-related parameters after construction.
    /// </summary>
    public void Init(uint parentCluster, int entryIndex, bool seekToEnd)
    {
        _parentCluster = parentCluster;
        _entryIndex = entryIndex;
        if (seekToEnd)
            _position = _fileSize;
    }

    public bool IsOpen => _isOpen;

    public long Position
    {
        get => _position;
        set
        {
            if (value < 0)
                value = 0;
            if (value > _fileSize)
                value = _fileSize;
            _position = value;

            // Invalidate cluster cache - will be recalculated on next read
            _currentCluster = _firstCluster;
            _currentClusterOffset = 0;
        }
    }

    public long Length => _fileSize;

    public FileAccess Access => _access;

    public int Read(byte* buffer, int count)
    {
        if (!_isOpen)
            return (int)FileResult.InvalidHandle;

        if ((_access & FileAccess.Read) == 0)
            return (int)FileResult.AccessDenied;

        if (buffer == null || count < 0)
            return (int)FileResult.InvalidPath;

        if (count == 0)
            return 0;

        // Clamp to end of file
        long remaining = _fileSize - _position;
        if (remaining <= 0)
            return 0;
        if (count > remaining)
            count = (int)remaining;

        int totalRead = 0;
        uint bytesPerCluster = _fs.BytesPerCluster;

        // Allocate cluster buffer
        ulong pageCount = (bytesPerCluster + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return (int)FileResult.IoError;

        byte* clusterBuffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Seek to current position's cluster if needed
            ulong pos = (ulong)_position;
            if (_currentClusterOffset > pos || _currentCluster == 0)
            {
                _currentCluster = _firstCluster;
                _currentClusterOffset = 0;
            }

            while (_currentClusterOffset + bytesPerCluster <= pos)
            {
                if (FatCluster.IsEndOfChain(_currentCluster, _fs.FatVariant))
                    break;
                _currentCluster = _fs.GetFatEntry(_currentCluster);
                _currentClusterOffset += bytesPerCluster;
            }

            while (totalRead < count)
            {
                if (_currentCluster == 0 || FatCluster.IsEndOfChain(_currentCluster, _fs.FatVariant))
                    break;

                // Read current cluster
                if (!_fs.ReadCluster(_currentCluster, clusterBuffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return totalRead > 0 ? totalRead : (int)FileResult.IoError;
                }

                // Calculate offset within cluster
                uint offsetInCluster = (uint)((pos + (ulong)totalRead) - _currentClusterOffset);
                uint bytesAvailable = bytesPerCluster - offsetInCluster;
                uint bytesToCopy = (uint)(count - totalRead);
                if (bytesToCopy > bytesAvailable)
                    bytesToCopy = bytesAvailable;

                // Copy to output buffer
                for (uint i = 0; i < bytesToCopy; i++)
                {
                    buffer[totalRead + i] = clusterBuffer[offsetInCluster + i];
                }

                totalRead += (int)bytesToCopy;

                // Move to next cluster if needed
                if (offsetInCluster + bytesToCopy >= bytesPerCluster)
                {
                    _currentCluster = _fs.GetFatEntry(_currentCluster);
                    _currentClusterOffset += bytesPerCluster;
                }
            }

            _position += totalRead;
            Memory.FreePages(bufferPhys, pageCount);
            return totalRead;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return (int)FileResult.IoError;
        }
    }

    public int Write(byte* buffer, int count)
    {
        if (!_isOpen)
            return (int)FileResult.InvalidHandle;

        if ((_access & FileAccess.Write) == 0)
            return (int)FileResult.AccessDenied;

        if (_fs.IsReadOnly)
            return (int)FileResult.ReadOnly;

        if (buffer == null || count < 0)
            return (int)FileResult.InvalidPath;

        if (count == 0)
            return 0;

        int totalWritten = 0;
        uint bytesPerCluster = _fs.BytesPerCluster;

        // Allocate cluster buffer
        ulong pageCount = (bytesPerCluster + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return (int)FileResult.IoError;

        byte* clusterBuffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            ulong pos = (ulong)_position;

            // Position to correct cluster
            if (_currentClusterOffset > pos || (_currentCluster == 0 && _firstCluster == 0))
            {
                _currentCluster = _firstCluster;
                _currentClusterOffset = 0;
            }

            // Skip to position cluster
            while (_currentCluster != 0 && !FatCluster.IsEndOfChain(_currentCluster, _fs.FatVariant) &&
                   _currentClusterOffset + bytesPerCluster <= pos)
            {
                _currentCluster = _fs.GetFatEntry(_currentCluster);
                _currentClusterOffset += bytesPerCluster;
            }

            while (totalWritten < count)
            {
                // Need to allocate first cluster?
                if (_firstCluster == 0)
                {
                    _firstCluster = _fs.AllocateCluster();
                    if (_firstCluster == 0)
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return totalWritten > 0 ? totalWritten : (int)FileResult.NoSpace;
                    }
                    _currentCluster = _firstCluster;
                    _currentClusterOffset = 0;

                    // Zero the new cluster
                    for (uint i = 0; i < bytesPerCluster; i++)
                        clusterBuffer[i] = 0;
                    _fs.WriteCluster(_currentCluster, clusterBuffer);
                }

                // Need new cluster?
                if (_currentCluster == 0 || FatCluster.IsEndOfChain(_currentCluster, _fs.FatVariant))
                {
                    // Extend cluster chain
                    uint lastCluster = _currentCluster;
                    if (lastCluster == 0 || FatCluster.IsEndOfChain(lastCluster, _fs.FatVariant))
                    {
                        // Find the last cluster in chain
                        lastCluster = _firstCluster;
                        while (lastCluster != 0 && !FatCluster.IsEndOfChain(lastCluster, _fs.FatVariant))
                        {
                            uint next = _fs.GetFatEntry(lastCluster);
                            if (FatCluster.IsEndOfChain(next, _fs.FatVariant))
                                break;
                            lastCluster = next;
                        }
                    }

                    uint newCluster = _fs.ExtendClusterChain(lastCluster);
                    if (newCluster == 0)
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return totalWritten > 0 ? totalWritten : (int)FileResult.NoSpace;
                    }

                    _currentCluster = newCluster;
                    _currentClusterOffset = ((ulong)_position + (ulong)totalWritten) / bytesPerCluster * bytesPerCluster;

                    // Zero the new cluster
                    for (uint i = 0; i < bytesPerCluster; i++)
                        clusterBuffer[i] = 0;
                    _fs.WriteCluster(_currentCluster, clusterBuffer);
                }

                // Read current cluster content (for partial writes)
                if (!_fs.ReadCluster(_currentCluster, clusterBuffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return totalWritten > 0 ? totalWritten : (int)FileResult.IoError;
                }

                // Calculate offset within cluster
                uint offsetInCluster = (uint)((pos + (ulong)totalWritten) - _currentClusterOffset);
                uint bytesAvailable = bytesPerCluster - offsetInCluster;
                uint bytesToCopy = (uint)(count - totalWritten);
                if (bytesToCopy > bytesAvailable)
                    bytesToCopy = bytesAvailable;

                // Copy to cluster buffer
                for (uint i = 0; i < bytesToCopy; i++)
                {
                    clusterBuffer[offsetInCluster + i] = buffer[totalWritten + i];
                }

                // Write cluster back
                if (!_fs.WriteCluster(_currentCluster, clusterBuffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return totalWritten > 0 ? totalWritten : (int)FileResult.IoError;
                }

                totalWritten += (int)bytesToCopy;

                // Move to next cluster if needed
                if (offsetInCluster + bytesToCopy >= bytesPerCluster)
                {
                    uint nextCluster = _fs.GetFatEntry(_currentCluster);
                    _currentCluster = nextCluster;
                    _currentClusterOffset += bytesPerCluster;
                }
            }

            _position += totalWritten;

            // Update file size if we wrote past end
            if (_position > _fileSize)
                _fileSize = _position;

            _dirty = true;
            Memory.FreePages(bufferPhys, pageCount);
            return totalWritten;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return (int)FileResult.IoError;
        }
    }

    public FileResult Flush()
    {
        if (!_isOpen)
            return FileResult.InvalidHandle;

        if (_dirty)
        {
            // Update directory entry with new size and first cluster
            FatDirEntry entry = default;
            entry.FileSize = (uint)_fileSize;
            entry.FstClusLO = (ushort)(_firstCluster & 0xFFFF);
            entry.FstClusHI = (ushort)((_firstCluster >> 16) & 0xFFFF);
            entry.Attr = (byte)FatAttr.Archive;

            // Read existing entry to preserve other fields
            // For simplicity, just update size and cluster
            _fs.UpdateDirectoryEntryPartial(_parentCluster, _entryIndex, _firstCluster, (uint)_fileSize);

            // Write FAT to disk
            _fs.WriteFat();

            _dirty = false;
        }

        return FileResult.Success;
    }

    public FileResult SetLength(long length)
    {
        if (!_isOpen)
            return FileResult.InvalidHandle;

        if ((_access & FileAccess.Write) == 0)
            return FileResult.AccessDenied;

        if (length < 0)
            return FileResult.InvalidPath;

        if (length == _fileSize)
            return FileResult.Success;

        if (length < _fileSize)
        {
            // Truncate - free clusters beyond new length
            if (length == 0)
            {
                // Free entire chain
                if (_firstCluster >= 2)
                {
                    _fs.FreeClusterChain(_firstCluster);
                    _firstCluster = 0;
                }
            }
            else
            {
                // Find cluster containing new end
                uint bytesPerCluster = _fs.BytesPerCluster;
                uint clustersNeeded = (uint)((length + bytesPerCluster - 1) / bytesPerCluster);

                uint cluster = _firstCluster;
                for (uint i = 1; i < clustersNeeded && cluster >= 2; i++)
                {
                    uint next = _fs.GetFatEntry(cluster);
                    if (FatCluster.IsEndOfChain(next, _fs.FatVariant))
                        break;
                    cluster = next;
                }

                // Free clusters after this one
                if (cluster >= 2)
                {
                    uint next = _fs.GetFatEntry(cluster);
                    if (!FatCluster.IsEndOfChain(next, _fs.FatVariant))
                    {
                        _fs.FreeClusterChain(next);
                    }
                    // Mark this as end of chain
                    uint endMarker = _fs.FatVariant switch
                    {
                        FatType.Fat12 => FatCluster.EndOfChain12,
                        FatType.Fat16 => FatCluster.EndOfChain16,
                        FatType.Fat32 => FatCluster.EndOfChain32,
                        _ => 0xFFFFFFFF
                    };
                    _fs.SetFatEntry(cluster, endMarker);
                }
            }
        }
        // else: extending - clusters will be allocated on write

        _fileSize = length;
        if (_position > _fileSize)
            _position = _fileSize;

        _dirty = true;
        return FileResult.Success;
    }

    private bool _dirty;

    public void Dispose()
    {
        if (_isOpen)
        {
            Flush();
            _isOpen = false;
        }
    }
}
