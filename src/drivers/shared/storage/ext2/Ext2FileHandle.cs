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
    private ulong _fileSize;  // Mutable for write support
    private long _position;
    private bool _isOpen;
    private bool _dirty;  // Track if inode needs to be written

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
            // Allow seeking past end for write operations
            // (will extend file on write)
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

        if (buffer == null || count < 0)
            return (int)FileResult.InvalidPath;

        if (count == 0)
            return 0;

        int totalWritten = 0;
        uint blockSize = _fs.BlockSize;

        // Allocate block buffer
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return (int)FileResult.IoError;

        byte* blockBuffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            while (totalWritten < count)
            {
                // Calculate current block and offset within block
                ulong pos = (ulong)_position + (ulong)totalWritten;
                uint blockIndex = (uint)(pos / blockSize);
                uint offsetInBlock = (uint)(pos % blockSize);

                // Get or allocate block
                uint blockNum;
                fixed (Ext2Inode* inodePtr = &_inode)
                {
                    blockNum = _fs.GetBlockNumber(inodePtr, blockIndex);
                }

                if (blockNum == 0)
                {
                    // Need to allocate a new block
                    blockNum = _fs.AllocateBlock();
                    if (blockNum == 0)
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return totalWritten > 0 ? totalWritten : (int)FileResult.NoSpace;
                    }

                    // Set block pointer in inode
                    fixed (Ext2Inode* inodePtr = &_inode)
                    {
                        if (!_fs.SetBlockNumber(inodePtr, blockIndex, blockNum))
                        {
                            _fs.FreeBlock(blockNum);
                            Memory.FreePages(bufferPhys, pageCount);
                            return totalWritten > 0 ? totalWritten : (int)FileResult.IoError;
                        }
                    }
                    _dirty = true;

                    // Zero the new block first
                    for (uint i = 0; i < blockSize; i++)
                        blockBuffer[i] = 0;
                }
                else if (offsetInBlock != 0 || (count - totalWritten) < (int)blockSize)
                {
                    // Partial block write - read existing data first
                    if (!_fs.ReadBlock(blockNum, blockBuffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return totalWritten > 0 ? totalWritten : (int)FileResult.IoError;
                    }
                }

                // Copy data to block buffer
                uint bytesAvailable = blockSize - offsetInBlock;
                uint bytesToCopy = (uint)(count - totalWritten);
                if (bytesToCopy > bytesAvailable)
                    bytesToCopy = bytesAvailable;

                for (uint i = 0; i < bytesToCopy; i++)
                    blockBuffer[offsetInBlock + i] = buffer[totalWritten + i];

                // Write block back
                if (!_fs.WriteBlock(blockNum, blockBuffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return totalWritten > 0 ? totalWritten : (int)FileResult.IoError;
                }

                totalWritten += (int)bytesToCopy;
            }

            _position += totalWritten;

            // Update file size if we wrote past the end
            if ((ulong)_position > _fileSize)
            {
                _fileSize = (ulong)_position;
                _inode.Size = (uint)_fileSize;
                // High 32 bits in DirAcl for large files (not implemented yet)
                _dirty = true;
            }

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

        // Write inode if dirty
        if (_dirty)
        {
            if (!_fs.WriteInode(_inodeNum, ref _inode))
                return FileResult.IoError;
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

        ulong newSize = (ulong)length;

        if (newSize == _fileSize)
            return FileResult.Success;

        if (newSize < _fileSize)
        {
            // Truncate - free blocks beyond new size
            // For now, just update the size (blocks remain allocated but unused)
            _fileSize = newSize;
            _inode.Size = (uint)newSize;
            _dirty = true;
        }
        else
        {
            // Extend - allocate blocks as needed
            uint blockSize = _fs.BlockSize;
            uint newBlockCount = (uint)((newSize + blockSize - 1) / blockSize);
            uint currentBlockCount = (uint)((_fileSize + blockSize - 1) / blockSize);

            // Allocate new blocks
            for (uint blockIndex = currentBlockCount; blockIndex < newBlockCount; blockIndex++)
            {
                uint blockNum = _fs.AllocateBlock();
                if (blockNum == 0)
                    return FileResult.NoSpace;

                fixed (Ext2Inode* inodePtr = &_inode)
                {
                    if (!_fs.SetBlockNumber(inodePtr, blockIndex, blockNum))
                    {
                        _fs.FreeBlock(blockNum);
                        return FileResult.IoError;
                    }
                }
            }

            _fileSize = newSize;
            _inode.Size = (uint)newSize;
            _dirty = true;
        }

        return FileResult.Success;
    }

    public void Dispose()
    {
        if (_isOpen && _dirty)
        {
            // Best effort flush on close
            _fs.WriteInode(_inodeNum, ref _inode);
        }
        _isOpen = false;
    }
}
