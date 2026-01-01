// ProtonOS EXT2 Filesystem Driver - Directory Handle
// Implements IDirectoryHandle for EXT2 directory enumeration

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Ext2;

/// <summary>
/// Directory handle for EXT2 filesystem enumeration.
/// </summary>
public unsafe class Ext2DirectoryHandle : IDirectoryHandle
{
    private readonly Ext2FileSystem _fs;
    private readonly uint _inodeNum;
    private Ext2Inode _inode;
    private readonly string _basePath;

    private uint _currentBlockIndex;
    private uint _offsetInBlock;
    private bool _isOpen;

    public Ext2DirectoryHandle(Ext2FileSystem fs, uint inodeNum, Ext2Inode inode, string path)
    {
        _fs = fs;
        _inodeNum = inodeNum;
        _inode = inode;
        _basePath = path;
        _currentBlockIndex = 0;
        _offsetInBlock = 0;
        _isOpen = true;
    }

    public bool IsOpen => _isOpen;

    public FileInfo? ReadNext()
    {
        if (!_isOpen)
            return null;

        uint blockSize = _fs.BlockSize;
        ulong dirSize = _inode.Size;

        // Allocate block buffer
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return null;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            while (true)
            {
                // Check if we've read past the directory size
                ulong currentOffset = (ulong)_currentBlockIndex * blockSize + _offsetInBlock;
                if (currentOffset >= dirSize)
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return null;
                }

                // Get block number
                uint blockNum;
                fixed (Ext2Inode* inodePtr = &_inode)
                {
                    blockNum = _fs.GetBlockNumber(inodePtr, _currentBlockIndex);
                }

                if (blockNum == 0)
                {
                    // No more blocks
                    Memory.FreePages(bufferPhys, pageCount);
                    return null;
                }

                // Read block if we're at the start of it
                if (_offsetInBlock == 0 || _offsetInBlock >= blockSize)
                {
                    if (!_fs.ReadBlock(blockNum, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;
                    }
                    if (_offsetInBlock >= blockSize)
                    {
                        _currentBlockIndex++;
                        _offsetInBlock = 0;
                        continue;
                    }
                }
                else
                {
                    // Re-read current block
                    if (!_fs.ReadBlock(blockNum, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;
                    }
                }

                // Parse directory entry
                var entry = (Ext2DirEntry*)(buffer + _offsetInBlock);

                if (entry->RecLen == 0)
                {
                    // Invalid entry, move to next block
                    _currentBlockIndex++;
                    _offsetInBlock = 0;
                    continue;
                }

                // Move to next entry
                _offsetInBlock += entry->RecLen;

                // Skip deleted entries (inode = 0)
                if (entry->Inode == 0)
                    continue;

                // Skip . and ..
                byte* entryName = (byte*)entry + 8;
                if (entry->NameLen == 1 && entryName[0] == '.')
                    continue;
                if (entry->NameLen == 2 && entryName[0] == '.' && entryName[1] == '.')
                    continue;

                // Build file name
                var nameChars = new char[entry->NameLen];
                for (int i = 0; i < entry->NameLen; i++)
                    nameChars[i] = (char)entryName[i];
                string name = new string(nameChars);

                // Build full path
                string fullPath;
                if (_basePath == "/" || _basePath.Length == 0)
                    fullPath = "/" + name;
                else
                    fullPath = _basePath + "/" + name;

                // Determine file type
                FileEntryType fileType;
                switch (entry->FileType)
                {
                    case Ext2FileType.FT_DIR:
                        fileType = FileEntryType.Directory;
                        break;
                    case Ext2FileType.FT_SYMLINK:
                        fileType = FileEntryType.SymLink;
                        break;
                    default:
                        fileType = FileEntryType.File;
                        break;
                }

                // Get file size from inode
                ulong fileSize = 0;
                Ext2Inode fileInode;
                if (_fs.ReadInode(entry->Inode, out fileInode))
                {
                    fileSize = fileInode.Size;
                }

                var info = new FileInfo();
                info.Name = name;
                info.Path = fullPath;
                info.Type = fileType;
                info.Size = fileSize;
                info.Attributes = FileAttributes.None;

                Memory.FreePages(bufferPhys, pageCount);
                return info;
            }
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return null;
        }
    }

    public void Rewind()
    {
        _currentBlockIndex = 0;
        _offsetInBlock = 0;
    }

    public void Dispose()
    {
        _isOpen = false;
    }
}
