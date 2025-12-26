// ProtonOS EXT2 Filesystem Driver - File Handle
// Implements IFileHandle for EXT2 file operations

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Ext2;

/// <summary>
/// File handle for EXT2 filesystem.
/// </summary>
public unsafe class Ext2FileHandle : IFileHandle
{
    private readonly Ext2FileSystem _fs;
    private readonly FileAccess _access;
    private readonly uint _inodeNum;
    private Ext2Inode _inode;
    private readonly ulong _fileSize;
    private long _position;
    private bool _isOpen;

    public Ext2FileHandle(Ext2FileSystem fs, uint inodeNum, Ext2Inode inode, ulong fileSize, FileAccess access)
    {
        _fs = fs;
        _inodeNum = inodeNum;
        _inode = inode;
        _fileSize = fileSize;
        _access = access;
        _position = 0;
        _isOpen = true;
    }

    public bool IsOpen => _isOpen;

    public long Position
    {
        get => _position;
        set
        {
            if (value < 0)
                value = 0;
            if (value > (long)_fileSize)
                value = (long)_fileSize;
            _position = value;
        }
    }

    public long Length => (long)_fileSize;

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
        long remaining = (long)_fileSize - _position;
        if (remaining <= 0)
            return 0;
        if (count > remaining)
            count = (int)remaining;

        int totalRead = 0;
        uint blockSize = _fs.BlockSize;

        // Allocate block buffer
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return (int)FileResult.IoError;

        byte* blockBuffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            while (totalRead < count)
            {
                // Calculate current block and offset within block
                ulong pos = (ulong)_position + (ulong)totalRead;
                uint blockIndex = (uint)(pos / blockSize);
                uint offsetInBlock = (uint)(pos % blockSize);

                // Get block number
                uint blockNum;
                fixed (Ext2Inode* inodePtr = &_inode)
                {
                    blockNum = _fs.GetBlockNumber(inodePtr, blockIndex);
                }

                if (blockNum == 0)
                {
                    // Sparse file - treat as zeros
                    uint bytesAvailable = blockSize - offsetInBlock;
                    uint bytesToCopy = (uint)(count - totalRead);
                    if (bytesToCopy > bytesAvailable)
                        bytesToCopy = bytesAvailable;

                    for (uint i = 0; i < bytesToCopy; i++)
                        buffer[totalRead + i] = 0;

                    totalRead += (int)bytesToCopy;
                }
                else
                {
                    // Read block
                    if (!_fs.ReadBlock(blockNum, blockBuffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return totalRead > 0 ? totalRead : (int)FileResult.IoError;
                    }

                    // Copy data from block
                    uint bytesAvailable = blockSize - offsetInBlock;
                    uint bytesToCopy = (uint)(count - totalRead);
                    if (bytesToCopy > bytesAvailable)
                        bytesToCopy = bytesAvailable;

                    for (uint i = 0; i < bytesToCopy; i++)
                        buffer[totalRead + i] = blockBuffer[offsetInBlock + i];

                    totalRead += (int)bytesToCopy;
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
