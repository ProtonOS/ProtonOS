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

        // TODO: Implement write
        return (int)FileResult.NotSupported;
    }

    public FileResult Flush()
    {
        if (!_isOpen)
            return FileResult.InvalidHandle;

        // TODO: Flush dirty buffers
        return FileResult.Success;
    }

    public FileResult SetLength(long length)
    {
        if (!_isOpen)
            return FileResult.InvalidHandle;

        if ((_access & FileAccess.Write) == 0)
            return FileResult.AccessDenied;

        // TODO: Implement truncate/extend
        return FileResult.NotSupported;
    }

    public void Dispose()
    {
        _isOpen = false;
    }
}
