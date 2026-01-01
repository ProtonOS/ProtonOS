// ProtonOS EXT2 Filesystem Driver
// Implements IFileSystem for EXT2 filesystems

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Ext2;

/// <summary>
/// EXT2 filesystem driver.
/// </summary>
public unsafe class Ext2FileSystem : IFileSystem
{
    // Block device
    private IBlockDevice? _device;
    private bool _mounted;
    private bool _readOnly;

    // Superblock data
    private uint _blockSize;
    private uint _blocksPerGroup;
    private uint _inodesPerGroup;
    private uint _inodeSize;
    private uint _firstDataBlock;
    private uint _groupCount;
    private uint _totalInodes;
    private uint _totalBlocks;

    // Cached superblock values (avoid storing the full 1024-byte struct)
    private uint _freeBlocksCount;
    private uint _featureRoCompat;
    private string? _volumeLabel;
    private Ext2GroupDesc[]? _groupDescs;

    // Sector size from device
    private uint _sectorSize;
    private uint _sectorsPerBlock;

    #region IDriver Implementation

    public string DriverName => "ext2";
    public Version DriverVersion => new Version(1, 0, 0);
    public DriverType Type => DriverType.Filesystem;
    public DriverState State { get; private set; } = DriverState.Loaded;

    public bool Initialize()
    {
        State = DriverState.Running;
        return true;
    }

    public void Shutdown()
    {
        if (_mounted)
            Unmount();
        State = DriverState.Stopped;
    }

    public void Suspend() { }
    public void Resume() { }

    #endregion

    #region IFileSystem Implementation

    public string FilesystemName => "EXT2";

    public FilesystemCapabilities Capabilities =>
        FilesystemCapabilities.Read |
        FilesystemCapabilities.Write |
        FilesystemCapabilities.CaseSensitive |
        FilesystemCapabilities.HardLinks |
        FilesystemCapabilities.SymLinks |
        FilesystemCapabilities.Timestamps;

    public bool IsMounted => _mounted;

    public string? VolumeLabel => _volumeLabel;

    public ulong TotalBytes => (ulong)_totalBlocks * _blockSize;

    public ulong FreeBytes => _mounted ? (ulong)_freeBlocksCount * _blockSize : 0;

    public bool Probe(IBlockDevice device)
    {
        if (device == null || device.BlockSize == 0)
            return false;

        // Superblock is at byte offset 1024
        // We need to read starting from sector that contains offset 1024
        uint sectorSize = device.BlockSize;
        ulong sbOffset = 1024;
        ulong sbSector = sbOffset / sectorSize;
        uint offsetInSector = (uint)(sbOffset % sectorSize);

        // Allocate buffer for sectors containing superblock
        uint sectorsNeeded = (offsetInSector + 1024 + sectorSize - 1) / sectorSize;
        ulong pageCount = ((ulong)sectorsNeeded * sectorSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            int result = device.Read(sbSector, sectorsNeeded, buffer);
            if (result != (int)sectorsNeeded)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Check magic number at offset 56 in superblock
            var sb = (Ext2Superblock*)(buffer + offsetInSector);
            bool isExt2 = sb->Magic == Ext2Magic.MAGIC;

            Memory.FreePages(bufferPhys, pageCount);
            return isExt2;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    public FileResult Mount(IBlockDevice device, bool readOnly = false)
    {
        if (_mounted)
            return FileResult.AlreadyExists;

        if (device == null)
            return FileResult.InvalidPath;

        _device = device;
        _readOnly = readOnly;
        _sectorSize = device.BlockSize;

        // Read superblock at offset 1024
        ulong sbOffset = 1024;
        ulong sbSector = sbOffset / _sectorSize;
        uint offsetInSector = (uint)(sbOffset % _sectorSize);

        uint sectorsNeeded = (offsetInSector + 1024 + _sectorSize - 1) / _sectorSize;
        ulong pageCount = ((ulong)sectorsNeeded * _sectorSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return FileResult.IoError;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            int result = device.Read(sbSector, sectorsNeeded, buffer);
            if (result != (int)sectorsNeeded)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.IoError;
            }

            // Copy superblock
            var sb = (Ext2Superblock*)(buffer + offsetInSector);
            if (sb->Magic != Ext2Magic.MAGIC)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.InvalidPath;
            }

            // Extract filesystem parameters directly from sb pointer (avoid large struct copy)
            _blockSize = 1024u << (int)sb->LogBlockSize;
            _blocksPerGroup = sb->BlocksPerGroup;
            _inodesPerGroup = sb->InodesPerGroup;
            _firstDataBlock = sb->FirstDataBlock;
            _totalInodes = sb->InodesCount;
            _totalBlocks = sb->BlocksCount;
            _sectorsPerBlock = _blockSize / _sectorSize;

            // Inode size - use 128 for rev 0, otherwise from superblock
            uint revLevel = sb->RevLevel;
            if (revLevel >= 1)
                _inodeSize = sb->InodeSize;
            else
                _inodeSize = 128;

            // Calculate group count
            _groupCount = (_totalBlocks + _blocksPerGroup - 1) / _blocksPerGroup;

            // Store additional values needed later
            _freeBlocksCount = sb->FreeBlocksCount;
            _featureRoCompat = sb->FeatureRoCompat;

            // Extract volume name (null-terminated string, max 16 chars)
            int nameLen = 0;
            while (nameLen < 16 && sb->VolumeName[nameLen] != 0)
                nameLen++;
            if (nameLen > 0)
            {
                var nameChars = new char[nameLen];
                for (int i = 0; i < nameLen; i++)
                    nameChars[i] = (char)sb->VolumeName[i];
                _volumeLabel = new string(nameChars);
            }
            else
            {
                _volumeLabel = null;
            }

            Memory.FreePages(bufferPhys, pageCount);

            // Load block group descriptors
            if (!LoadGroupDescriptors())
                return FileResult.IoError;

            _mounted = true;
            return FileResult.Success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return FileResult.IoError;
        }
    }

    public FileResult Unmount()
    {
        if (!_mounted)
            return FileResult.NotFound;

        _groupDescs = null;
        _device = null;
        _mounted = false;
        return FileResult.Success;
    }

    public FileResult GetInfo(string path, out FileInfo? info)
    {
        info = null;
        if (!_mounted || _device == null)
            return FileResult.IoError;

        path = NormalizePath(path);

        // Root directory
        if (path == "/" || path.Length == 0)
        {
            info = new FileInfo
            {
                Name = "",
                Path = "/",
                Type = FileEntryType.Directory,
                Size = 0,
                Attributes = FileAttributes.None,
            };
            return FileResult.Success;
        }

        // Find inode for path
        uint inodeNum;
        var result = FindInode(path, out inodeNum);
        if (result != FileResult.Success)
            return result;

        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return FileResult.IoError;

        info = InodeToFileInfo(inode, path);
        return FileResult.Success;
    }

    public FileResult OpenFile(string path, FileMode mode, FileAccess access, out IFileHandle? handle)
    {
        handle = null;
        if (!_mounted || _device == null)
            return FileResult.IoError;

        if (_readOnly && (access & FileAccess.Write) != 0)
            return FileResult.ReadOnly;

        path = NormalizePath(path);

        uint inodeNum;
        var findResult = FindInode(path, out inodeNum);

        switch (mode)
        {
            case FileMode.Open:
                if (findResult != FileResult.Success)
                    return FileResult.NotFound;
                break;

            case FileMode.CreateNew:
                if (findResult == FileResult.Success)
                    return FileResult.AlreadyExists;
                // Create new file
                var createResult = CreateFileInternal(path, out inodeNum);
                if (createResult != FileResult.Success)
                    return createResult;
                findResult = FileResult.Success;
                break;

            case FileMode.OpenOrCreate:
            case FileMode.Create:
                if (findResult != FileResult.Success)
                {
                    // Create new file
                    var createResult2 = CreateFileInternal(path, out inodeNum);
                    if (createResult2 != FileResult.Success)
                        return createResult2;
                    findResult = FileResult.Success;
                }
                break;

            case FileMode.Truncate:
            case FileMode.Append:
                if (findResult != FileResult.Success)
                    return FileResult.NotFound;
                break;

            default:
                return FileResult.NotSupported;
        }

        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return FileResult.IoError;

        // Check if it's a regular file
        if ((inode.Mode & Ext2FileMode.S_IFMT) != Ext2FileMode.S_IFREG)
            return FileResult.IsADirectory;

        ulong fileSize = inode.Size;
        if ((_featureRoCompat & Ext2FeatureRoCompat.LARGE_FILE) != 0)
            fileSize |= ((ulong)inode.DirAcl << 32);

        handle = new Ext2FileHandle(this, inodeNum, inode, fileSize, access);
        return FileResult.Success;
    }

    public FileResult OpenDirectory(string path, out IDirectoryHandle? handle)
    {
        handle = null;
        if (!_mounted || _device == null)
            return FileResult.IoError;

        path = NormalizePath(path);

        uint inodeNum;
        if (path == "/" || path.Length == 0)
        {
            inodeNum = Ext2Inodes.ROOT_INO;
        }
        else
        {
            var result = FindInode(path, out inodeNum);
            if (result != FileResult.Success)
                return result;
        }

        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return FileResult.IoError;

        // Check if it's a directory
        if ((inode.Mode & Ext2FileMode.S_IFMT) != Ext2FileMode.S_IFDIR)
            return FileResult.NotADirectory;

        handle = new Ext2DirectoryHandle(this, inodeNum, inode, path);
        return FileResult.Success;
    }

    public FileResult CreateDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);
        return CreateDirectoryInternal(path);
    }

    public FileResult DeleteFile(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);
        return DeleteFileInternal(path);
    }

    public FileResult DeleteDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);
        return DeleteDirectoryInternal(path);
    }

    public FileResult Rename(string oldPath, string newPath)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;
        return FileResult.NotSupported;
    }

    public bool Exists(string path)
    {
        FileInfo? info;
        return GetInfo(path, out info) == FileResult.Success;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// Load block group descriptors from disk.
    /// </summary>
    private bool LoadGroupDescriptors()
    {
        if (_device == null)
            return false;

        // Group descriptors start at block (firstDataBlock + 1)
        uint gdBlock = _firstDataBlock + 1;
        uint gdSizeBytes = _groupCount * 32; // 32 bytes per descriptor
        uint gdBlocks = (gdSizeBytes + _blockSize - 1) / _blockSize;

        ulong pageCount = ((ulong)gdBlocks * _blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Read group descriptor blocks
            ulong sector = (ulong)gdBlock * _sectorsPerBlock;
            uint sectorsToRead = gdBlocks * _sectorsPerBlock;
            int result = _device.Read(sector, sectorsToRead, buffer);
            if (result != (int)sectorsToRead)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Copy to managed array
            _groupDescs = new Ext2GroupDesc[_groupCount];
            var gd = (Ext2GroupDesc*)buffer;
            for (uint i = 0; i < _groupCount; i++)
            {
                _groupDescs[i] = gd[i];
            }

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Read an inode from disk.
    /// </summary>
    internal bool ReadInode(uint inodeNum, out Ext2Inode inode)
    {
        inode = default;
        if (_device == null || _groupDescs == null || inodeNum == 0)
            return false;

        // Calculate which group and index within group
        uint group = (inodeNum - 1) / _inodesPerGroup;
        uint index = (inodeNum - 1) % _inodesPerGroup;

        if (group >= _groupCount)
            return false;

        // Get inode table block from group descriptor
        uint inodeTableBlock = _groupDescs[group].InodeTable;

        // Calculate offset within inode table
        uint inodeOffset = index * _inodeSize;
        uint blockInTable = inodeOffset / _blockSize;
        uint offsetInBlock = inodeOffset % _blockSize;

        // Read the block containing the inode
        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            ulong sector = (ulong)(inodeTableBlock + blockInTable) * _sectorsPerBlock;
            int result = _device.Read(sector, _sectorsPerBlock, buffer);
            if (result != (int)_sectorsPerBlock)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Copy inode
            var inodePtr = (Ext2Inode*)(buffer + offsetInBlock);
            inode = *inodePtr;

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Read a block from the filesystem.
    /// </summary>
    internal bool ReadBlock(uint blockNum, byte* buffer)
    {
        if (_device == null || blockNum == 0)
            return false;

        ulong sector = (ulong)blockNum * _sectorsPerBlock;
        int result = _device.Read(sector, _sectorsPerBlock, buffer);
        return result == (int)_sectorsPerBlock;
    }

    /// <summary>
    /// Write a block to the filesystem.
    /// </summary>
    internal bool WriteBlock(uint blockNum, byte* buffer)
    {
        if (_device == null || blockNum == 0 || _readOnly)
            return false;

        ulong sector = (ulong)blockNum * _sectorsPerBlock;
        int result = _device.Write(sector, _sectorsPerBlock, buffer);
        return result == (int)_sectorsPerBlock;
    }

    /// <summary>
    /// Write an inode to disk.
    /// </summary>
    internal bool WriteInode(uint inodeNum, ref Ext2Inode inode)
    {
        if (_device == null || _groupDescs == null || inodeNum == 0 || _readOnly)
            return false;

        // Calculate which group and index within group
        uint group = (inodeNum - 1) / _inodesPerGroup;
        uint index = (inodeNum - 1) % _inodesPerGroup;

        if (group >= _groupCount)
            return false;

        // Get inode table block from group descriptor
        uint inodeTableBlock = _groupDescs[group].InodeTable;

        // Calculate offset within inode table
        uint inodeOffset = index * _inodeSize;
        uint blockInTable = inodeOffset / _blockSize;
        uint offsetInBlock = inodeOffset % _blockSize;

        // Read the block containing the inode
        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Read block first
            uint targetBlock = inodeTableBlock + blockInTable;
            if (!ReadBlock(targetBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Update inode in buffer
            var inodePtr = (Ext2Inode*)(buffer + offsetInBlock);
            *inodePtr = inode;

            // Write block back
            bool success = WriteBlock(targetBlock, buffer);
            Memory.FreePages(bufferPhys, pageCount);
            return success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Get block number for a given file offset.
    /// Handles direct, indirect, double indirect, and triple indirect blocks.
    /// </summary>
    internal uint GetBlockNumber(Ext2Inode* inode, uint fileBlockIndex)
    {
        uint idx = fileBlockIndex;

        uint ptrsPerBlock = _blockSize / 4;

        // Direct blocks (0-11)
        if (idx < Ext2BlockPtrs.NDIR_BLOCKS)
        {
            return inode->Block[idx];
        }

        idx -= Ext2BlockPtrs.NDIR_BLOCKS;

        // Single indirect (12 - 12+ptrsPerBlock-1)
        if (idx < ptrsPerBlock)
        {
            uint indBlock = inode->Block[Ext2BlockPtrs.IND_BLOCK];
            if (indBlock == 0)
                return 0;
            return ReadBlockPointer(indBlock, idx);
        }

        idx -= ptrsPerBlock;

        // Double indirect
        if (idx < ptrsPerBlock * ptrsPerBlock)
        {
            uint dindBlock = inode->Block[Ext2BlockPtrs.DIND_BLOCK];
            if (dindBlock == 0)
                return 0;

            uint indIndex = idx / ptrsPerBlock;
            uint ptrIndex = idx % ptrsPerBlock;

            uint indBlock = ReadBlockPointer(dindBlock, indIndex);
            if (indBlock == 0)
                return 0;

            return ReadBlockPointer(indBlock, ptrIndex);
        }

        idx -= ptrsPerBlock * ptrsPerBlock;

        // Triple indirect
        uint tindBlock = inode->Block[Ext2BlockPtrs.TIND_BLOCK];
        if (tindBlock == 0)
            return 0;

        uint dindIndex = idx / (ptrsPerBlock * ptrsPerBlock);
        uint remainder = idx % (ptrsPerBlock * ptrsPerBlock);
        uint indIndex2 = remainder / ptrsPerBlock;
        uint ptrIndex2 = remainder % ptrsPerBlock;

        uint dindBlock2 = ReadBlockPointer(tindBlock, dindIndex);
        if (dindBlock2 == 0)
            return 0;

        uint indBlock2 = ReadBlockPointer(dindBlock2, indIndex2);
        if (indBlock2 == 0)
            return 0;

        return ReadBlockPointer(indBlock2, ptrIndex2);
    }

    /// <summary>
    /// Read a block pointer from an indirect block.
    /// </summary>
    private uint ReadBlockPointer(uint blockNum, uint index)
    {
        if (_device == null)
            return 0;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return 0;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(blockNum, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return 0;
            }

            uint* ptrs = (uint*)buffer;
            uint result = ptrs[index];

            Memory.FreePages(bufferPhys, pageCount);
            return result;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
    }

    /// <summary>
    /// Find an inode by path.
    /// </summary>
    internal FileResult FindInode(string path, out uint inodeNum)
    {
        inodeNum = Ext2Inodes.ROOT_INO;

        if (string.IsNullOrEmpty(path) || path == "/")
            return FileResult.Success;

        // Parse path components
        int pathStart = 0;
        while (pathStart < path.Length)
        {
            // Skip leading slashes
            while (pathStart < path.Length && path[pathStart] == '/')
                pathStart++;
            if (pathStart >= path.Length)
                break;

            // Find end of component
            int pathEnd = pathStart;
            while (pathEnd < path.Length && path[pathEnd] != '/')
                pathEnd++;

            string component = path.Substring(pathStart, pathEnd - pathStart);
            pathStart = pathEnd;

            if (component.Length == 0)
                continue;

            // Read current directory inode
            Ext2Inode dirInode;
            if (!ReadInode(inodeNum, out dirInode))
                return FileResult.IoError;

            // Search directory for component
            uint foundInode;
            if (!SearchDirectory(dirInode, component, out foundInode))
                return FileResult.NotFound;

            inodeNum = foundInode;
        }

        return FileResult.Success;
    }

    /// <summary>
    /// Search a directory for an entry with the given name.
    /// </summary>
    private bool SearchDirectory(Ext2Inode dirInode, string name, out uint foundInode)
    {
        foundInode = 0;
        if (_device == null)
            return false;

        ulong dirSize = dirInode.Size;
        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            uint blockIndex = 0;
            ulong offset = 0;

            while (offset < dirSize)
            {
                // Get block number for this offset
                Ext2Inode localInode = dirInode;
                uint blockNum = GetBlockNumber(&localInode, blockIndex);

                if (blockNum == 0)
                    break;

                // Read directory block
                if (!ReadBlock(blockNum, buffer))
                    break;

                // Parse directory entries in this block
                uint blockOffset = 0;
                while (blockOffset < _blockSize && offset + blockOffset < dirSize)
                {
                    var entry = (Ext2DirEntry*)(buffer + blockOffset);

                    if (entry->RecLen == 0)
                        break; // Invalid entry

                    if (entry->Inode != 0 && entry->NameLen > 0)
                    {
                        // Compare name
                        if (entry->NameLen == name.Length)
                        {
                            bool match = true;
                            byte* entryName = (byte*)entry + 8;
                            for (int i = 0; i < name.Length && match; i++)
                            {
                                if (entryName[i] != (byte)name[i])
                                    match = false;
                            }
                            if (match)
                            {
                                foundInode = entry->Inode;
                                Memory.FreePages(bufferPhys, pageCount);
                                return true;
                            }
                        }
                    }

                    blockOffset += entry->RecLen;
                }

                offset += _blockSize;
                blockIndex++;
            }

            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Convert inode to FileInfo.
    /// </summary>
    private FileInfo InodeToFileInfo(Ext2Inode inode, string path)
    {
        var info = new FileInfo();
        info.Name = VFS.GetFileName(path);
        info.Path = path;

        ushort fileType = (ushort)(inode.Mode & Ext2FileMode.S_IFMT);
        switch (fileType)
        {
            case Ext2FileMode.S_IFDIR:
                info.Type = FileEntryType.Directory;
                break;
            case Ext2FileMode.S_IFLNK:
                info.Type = FileEntryType.SymLink;
                break;
            default:
                info.Type = FileEntryType.File;
                break;
        }

        ulong size = inode.Size;
        if ((_featureRoCompat & Ext2FeatureRoCompat.LARGE_FILE) != 0)
            size |= ((ulong)inode.DirAcl << 32);
        info.Size = size;

        info.Attributes = FileAttributes.None;
        return info;
    }

    /// <summary>
    /// Normalize a path.
    /// </summary>
    private static string NormalizePath(string path)
    {
        if (path == null || path.Length == 0)
            return "/";
        if (path[0] != '/')
            return "/" + path;
        return path;
    }

    // Properties for internal use
    internal uint BlockSize => _blockSize;
    internal uint SectorsPerBlock => _sectorsPerBlock;
    internal IBlockDevice? Device => _device;

    #endregion

    #region Block/Inode Allocation

    /// <summary>
    /// Allocate a new block, preferring the specified group.
    /// Returns 0 on failure.
    /// </summary>
    internal uint AllocateBlock(uint preferredGroup = 0)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return 0;

        if (_freeBlocksCount == 0)
            return 0;

        // Try preferred group first, then search all groups
        for (uint i = 0; i < _groupCount; i++)
        {
            uint group = (preferredGroup + i) % _groupCount;
            if (_groupDescs[group].FreeBlocksCount == 0)
                continue;

            uint block = AllocateBlockInGroup(group);
            if (block != 0)
                return block;
        }

        return 0;
    }

    /// <summary>
    /// Allocate a block from a specific group.
    /// </summary>
    private uint AllocateBlockInGroup(uint group)
    {
        if (_device == null || _groupDescs == null)
            return 0;

        uint bitmapBlock = _groupDescs[group].BlockBitmap;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return 0;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return 0;
            }

            // Search for a free bit
            uint blocksInGroup = (group == _groupCount - 1)
                ? (_totalBlocks - 1) % _blocksPerGroup + 1
                : _blocksPerGroup;

            for (uint i = 0; i < blocksInGroup; i++)
            {
                uint byteIndex = i / 8;
                uint bitIndex = i % 8;

                if ((buffer[byteIndex] & (1 << (int)bitIndex)) == 0)
                {
                    // Found free block - mark as used
                    buffer[byteIndex] |= (byte)(1 << (int)bitIndex);

                    // Write bitmap back
                    if (!WriteBlock(bitmapBlock, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return 0;
                    }

                    // Update group descriptor
                    _groupDescs[group].FreeBlocksCount--;

                    // Update superblock free count
                    _freeBlocksCount--;

                    Memory.FreePages(bufferPhys, pageCount);

                    // Calculate absolute block number
                    uint blockNum = group * _blocksPerGroup + i + _firstDataBlock;
                    return blockNum;
                }
            }

            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
    }

    /// <summary>
    /// Free a block.
    /// </summary>
    internal bool FreeBlock(uint blockNum)
    {
        if (_device == null || _groupDescs == null || _readOnly || blockNum == 0)
            return false;

        // Calculate group and index within group
        uint blockInFs = blockNum - _firstDataBlock;
        uint group = blockInFs / _blocksPerGroup;
        uint indexInGroup = blockInFs % _blocksPerGroup;

        if (group >= _groupCount)
            return false;

        uint bitmapBlock = _groupDescs[group].BlockBitmap;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            uint byteIndex = indexInGroup / 8;
            uint bitIndex = indexInGroup % 8;

            // Clear the bit
            buffer[byteIndex] &= (byte)~(1 << (int)bitIndex);

            // Write bitmap back
            if (!WriteBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Update group descriptor
            _groupDescs[group].FreeBlocksCount++;

            // Update superblock free count
            _freeBlocksCount++;

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Set a block pointer for a file at the given file block index.
    /// Allocates indirect blocks as needed.
    /// </summary>
    internal bool SetBlockNumber(Ext2Inode* inode, uint fileBlockIndex, uint blockNum)
    {
        uint idx = fileBlockIndex;
        uint ptrsPerBlock = _blockSize / 4;

        // Direct blocks (0-11)
        if (idx < Ext2BlockPtrs.NDIR_BLOCKS)
        {
            inode->Block[idx] = blockNum;
            return true;
        }

        idx -= Ext2BlockPtrs.NDIR_BLOCKS;

        // Single indirect
        if (idx < ptrsPerBlock)
        {
            // Allocate indirect block if needed
            if (inode->Block[Ext2BlockPtrs.IND_BLOCK] == 0)
            {
                uint indBlock = AllocateBlock();
                if (indBlock == 0)
                    return false;
                inode->Block[Ext2BlockPtrs.IND_BLOCK] = indBlock;
                // Zero the new indirect block
                if (!ZeroBlock(indBlock))
                    return false;
            }
            return WriteBlockPointer(inode->Block[Ext2BlockPtrs.IND_BLOCK], idx, blockNum);
        }

        idx -= ptrsPerBlock;

        // Double indirect
        if (idx < ptrsPerBlock * ptrsPerBlock)
        {
            // Allocate double indirect block if needed
            if (inode->Block[Ext2BlockPtrs.DIND_BLOCK] == 0)
            {
                uint dindBlock = AllocateBlock();
                if (dindBlock == 0)
                    return false;
                inode->Block[Ext2BlockPtrs.DIND_BLOCK] = dindBlock;
                if (!ZeroBlock(dindBlock))
                    return false;
            }

            uint indIndex = idx / ptrsPerBlock;
            uint ptrIndex = idx % ptrsPerBlock;

            // Get/allocate indirect block
            uint indBlock = ReadBlockPointer(inode->Block[Ext2BlockPtrs.DIND_BLOCK], indIndex);
            if (indBlock == 0)
            {
                indBlock = AllocateBlock();
                if (indBlock == 0)
                    return false;
                if (!WriteBlockPointer(inode->Block[Ext2BlockPtrs.DIND_BLOCK], indIndex, indBlock))
                    return false;
                if (!ZeroBlock(indBlock))
                    return false;
            }

            return WriteBlockPointer(indBlock, ptrIndex, blockNum);
        }

        // Triple indirect (not implemented for now)
        return false;
    }

    /// <summary>
    /// Write a block pointer to an indirect block.
    /// </summary>
    private bool WriteBlockPointer(uint indBlockNum, uint index, uint value)
    {
        if (_device == null)
            return false;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(indBlockNum, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            uint* ptrs = (uint*)buffer;
            ptrs[index] = value;

            bool success = WriteBlock(indBlockNum, buffer);
            Memory.FreePages(bufferPhys, pageCount);
            return success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Zero a block.
    /// </summary>
    private bool ZeroBlock(uint blockNum)
    {
        if (_device == null)
            return false;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Zero the buffer
        for (uint i = 0; i < _blockSize; i++)
            buffer[i] = 0;

        bool success = WriteBlock(blockNum, buffer);
        Memory.FreePages(bufferPhys, pageCount);
        return success;
    }

    /// <summary>
    /// Allocate a new inode, preferring the specified group.
    /// Returns 0 on failure.
    /// </summary>
    internal uint AllocateInode(uint preferredGroup = 0)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return 0;

        // Try preferred group first, then search all groups
        for (uint i = 0; i < _groupCount; i++)
        {
            uint group = (preferredGroup + i) % _groupCount;
            if (_groupDescs[group].FreeInodesCount == 0)
                continue;

            uint inodeNum = AllocateInodeInGroup(group);
            if (inodeNum != 0)
                return inodeNum;
        }

        return 0;
    }

    /// <summary>
    /// Allocate an inode from a specific group.
    /// </summary>
    private uint AllocateInodeInGroup(uint group)
    {
        if (_device == null || _groupDescs == null)
            return 0;

        uint bitmapBlock = _groupDescs[group].InodeBitmap;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return 0;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return 0;
            }

            // Search for a free bit
            for (uint i = 0; i < _inodesPerGroup; i++)
            {
                uint byteIndex = i / 8;
                uint bitIndex = i % 8;

                if ((buffer[byteIndex] & (1 << (int)bitIndex)) == 0)
                {
                    // Found free inode - mark as used
                    buffer[byteIndex] |= (byte)(1 << (int)bitIndex);

                    // Write bitmap back
                    if (!WriteBlock(bitmapBlock, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return 0;
                    }

                    // Update group descriptor
                    _groupDescs[group].FreeInodesCount--;
                    WriteGroupDescriptor(group);

                    Memory.FreePages(bufferPhys, pageCount);

                    // Calculate absolute inode number (1-based)
                    uint inodeNum = group * _inodesPerGroup + i + 1;
                    return inodeNum;
                }
            }

            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
    }

    /// <summary>
    /// Free an inode.
    /// </summary>
    internal bool FreeInode(uint inodeNum)
    {
        if (_device == null || _groupDescs == null || _readOnly || inodeNum == 0)
            return false;

        // Calculate group and index within group
        uint group = (inodeNum - 1) / _inodesPerGroup;
        uint indexInGroup = (inodeNum - 1) % _inodesPerGroup;

        if (group >= _groupCount)
            return false;

        uint bitmapBlock = _groupDescs[group].InodeBitmap;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            uint byteIndex = indexInGroup / 8;
            uint bitIndex = indexInGroup % 8;

            // Clear the bit
            buffer[byteIndex] &= (byte)~(1 << (int)bitIndex);

            // Write bitmap back
            if (!WriteBlock(bitmapBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Update group descriptor
            _groupDescs[group].FreeInodesCount++;
            WriteGroupDescriptor(group);

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Write a group descriptor back to disk.
    /// </summary>
    private bool WriteGroupDescriptor(uint group)
    {
        if (_device == null || _groupDescs == null || group >= _groupCount)
            return false;

        // Group descriptors start at block (firstDataBlock + 1)
        uint gdBlock = _firstDataBlock + 1;

        // Calculate which block contains this group descriptor
        uint descsPerBlock = _blockSize / 32; // 32 bytes per descriptor
        uint blockIndex = group / descsPerBlock;
        uint offsetInBlock = (group % descsPerBlock) * 32;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Read the block containing this descriptor
            if (!ReadBlock(gdBlock + blockIndex, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Update the descriptor
            var gdPtr = (Ext2GroupDesc*)(buffer + offsetInBlock);
            *gdPtr = _groupDescs[group];

            // Write back
            bool success = WriteBlock(gdBlock + blockIndex, buffer);
            Memory.FreePages(bufferPhys, pageCount);
            return success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    #endregion

    #region Directory Entry Management

    /// <summary>
    /// Add a directory entry to a directory.
    /// </summary>
    internal bool AddDirectoryEntry(uint dirInodeNum, string name, uint targetInodeNum, byte fileType)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return false;

        if (name.Length == 0 || name.Length > 255)
            return false;

        Ext2Inode dirInode;
        if (!ReadInode(dirInodeNum, out dirInode))
            return false;

        // Check it's actually a directory
        if ((dirInode.Mode & Ext2FileMode.S_IFMT) != Ext2FileMode.S_IFDIR)
            return false;

        // Calculate required size for new entry (8 bytes header + name, rounded to 4-byte boundary)
        uint requiredSize = (uint)((8 + name.Length + 3) & ~3);

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            uint dirSize = dirInode.Size;
            uint blockIndex = 0;

            // Search existing blocks for space
            while (blockIndex * _blockSize < dirSize)
            {
                Ext2Inode localInode = dirInode;
                uint blockNum = GetBlockNumber(&localInode, blockIndex);
                if (blockNum == 0)
                    break;

                if (!ReadBlock(blockNum, buffer))
                    break;

                // Parse entries in this block looking for space
                uint offset = 0;
                while (offset < _blockSize)
                {
                    var entry = (Ext2DirEntry*)(buffer + offset);
                    if (entry->RecLen == 0)
                        break;

                    // Calculate actual size this entry needs
                    uint actualSize = entry->Inode != 0
                        ? (uint)((8 + entry->NameLen + 3) & ~3)
                        : 0;

                    // Space available = RecLen - actualSize
                    uint available = entry->RecLen - actualSize;

                    if (available >= requiredSize)
                    {
                        // Found space - split this entry
                        if (entry->Inode != 0)
                        {
                            // Shrink existing entry
                            ushort newRecLen = (ushort)actualSize;
                            ushort remainingLen = (ushort)(entry->RecLen - actualSize);
                            entry->RecLen = newRecLen;

                            // Create new entry after it
                            var newEntry = (Ext2DirEntry*)(buffer + offset + actualSize);
                            newEntry->Inode = targetInodeNum;
                            newEntry->RecLen = remainingLen;
                            newEntry->NameLen = (byte)name.Length;
                            newEntry->FileType = fileType;
                            byte* newEntryName = (byte*)newEntry + 8;
                            for (int i = 0; i < name.Length; i++)
                                newEntryName[i] = (byte)name[i];
                        }
                        else
                        {
                            // Reuse deleted entry
                            entry->Inode = targetInodeNum;
                            entry->NameLen = (byte)name.Length;
                            entry->FileType = fileType;
                            byte* entryName = (byte*)entry + 8;
                            for (int i = 0; i < name.Length; i++)
                                entryName[i] = (byte)name[i];
                        }

                        // Write block back
                        if (!WriteBlock(blockNum, buffer))
                        {
                            Memory.FreePages(bufferPhys, pageCount);
                            return false;
                        }

                        Memory.FreePages(bufferPhys, pageCount);
                        return true;
                    }

                    offset += entry->RecLen;
                }

                blockIndex++;
            }

            // No space in existing blocks - allocate new block
            uint group = (dirInodeNum - 1) / _inodesPerGroup;
            uint newBlockNum = AllocateBlock(group);
            if (newBlockNum == 0)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Set block pointer in inode
            Ext2Inode* inodePtr = &dirInode;
            if (!SetBlockNumber(inodePtr, blockIndex, newBlockNum))
            {
                FreeBlock(newBlockNum);
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Zero the new block
            for (uint i = 0; i < _blockSize; i++)
                buffer[i] = 0;

            // Create single entry spanning entire block
            var dirEntry = (Ext2DirEntry*)buffer;
            dirEntry->Inode = targetInodeNum;
            dirEntry->RecLen = (ushort)_blockSize;
            dirEntry->NameLen = (byte)name.Length;
            dirEntry->FileType = fileType;
            byte* dirEntryName = (byte*)dirEntry + 8;
            for (int i = 0; i < name.Length; i++)
                dirEntryName[i] = (byte)name[i];

            // Write new block
            if (!WriteBlock(newBlockNum, buffer))
            {
                FreeBlock(newBlockNum);
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Update directory inode size
            dirInode.Size += _blockSize;
            dirInode.Blocks += _blockSize / 512;
            if (!WriteInode(dirInodeNum, ref dirInode))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Remove a directory entry from a directory.
    /// </summary>
    internal bool RemoveDirectoryEntry(uint dirInodeNum, string name)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return false;

        if (name.Length == 0 || name.Length > 255)
            return false;

        Ext2Inode dirInode;
        if (!ReadInode(dirInodeNum, out dirInode))
            return false;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            uint dirSize = dirInode.Size;
            uint blockIndex = 0;

            while (blockIndex * _blockSize < dirSize)
            {
                Ext2Inode localInode = dirInode;
                uint blockNum = GetBlockNumber(&localInode, blockIndex);
                if (blockNum == 0)
                    break;

                if (!ReadBlock(blockNum, buffer))
                    break;

                // Track previous entry for merging
                Ext2DirEntry* prevEntry = null;
                uint offset = 0;

                while (offset < _blockSize)
                {
                    var entry = (Ext2DirEntry*)(buffer + offset);
                    if (entry->RecLen == 0)
                        break;

                    if (entry->Inode != 0 && entry->NameLen == name.Length)
                    {
                        // Compare name
                        bool match = true;
                        byte* entryName = (byte*)entry + 8;
                        for (int i = 0; i < name.Length && match; i++)
                        {
                            if (entryName[i] != (byte)name[i])
                                match = false;
                        }

                        if (match)
                        {
                            // Found it - mark as deleted
                            if (prevEntry != null)
                            {
                                // Merge with previous entry
                                prevEntry->RecLen += entry->RecLen;
                            }
                            else
                            {
                                // First entry - just clear inode
                                entry->Inode = 0;
                            }

                            // Write block back
                            if (!WriteBlock(blockNum, buffer))
                            {
                                Memory.FreePages(bufferPhys, pageCount);
                                return false;
                            }

                            Memory.FreePages(bufferPhys, pageCount);
                            return true;
                        }
                    }

                    prevEntry = entry;
                    offset += entry->RecLen;
                }

                blockIndex++;
            }

            Memory.FreePages(bufferPhys, pageCount);
            return false; // Entry not found
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    #endregion

    #region File/Directory Creation and Deletion

    /// <summary>
    /// Create a new file.
    /// </summary>
    internal FileResult CreateFileInternal(string path, out uint newInodeNum)
    {
        newInodeNum = 0;

        if (_device == null || _groupDescs == null || _readOnly)
            return FileResult.IoError;

        // Get parent directory and filename
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        if (fileName.Length == 0 || fileName.Length > 255)
            return FileResult.InvalidPath;

        // Find parent directory inode
        uint parentInodeNum;
        var result = FindInode(parentPath, out parentInodeNum);
        if (result != FileResult.Success)
            return result;

        // Check if file already exists
        uint existingInode;
        if (SearchDirectoryByInode(parentInodeNum, fileName, out existingInode))
            return FileResult.AlreadyExists;

        // Allocate new inode in same group as parent
        uint group = (parentInodeNum - 1) / _inodesPerGroup;
        newInodeNum = AllocateInode(group);
        if (newInodeNum == 0)
            return FileResult.NoSpace;

        // Initialize inode
        Ext2Inode inode = default;
        inode.Mode = (ushort)(Ext2FileMode.S_IFREG | 0x1B6); // Regular file, rw-rw-rw-
        inode.Uid = 0;
        inode.Gid = 0;
        inode.Size = 0;
        inode.LinksCount = 1;
        inode.Blocks = 0;
        // Set timestamps (would need RTC support for proper values)
        inode.Atime = inode.Ctime = inode.Mtime = 0;

        if (!WriteInode(newInodeNum, ref inode))
        {
            FreeInode(newInodeNum);
            newInodeNum = 0;
            return FileResult.IoError;
        }

        // Add directory entry
        if (!AddDirectoryEntry(parentInodeNum, fileName, newInodeNum, Ext2FileType.FT_REG_FILE))
        {
            FreeInode(newInodeNum);
            newInodeNum = 0;
            return FileResult.IoError;
        }

        return FileResult.Success;
    }

    /// <summary>
    /// Create a new directory.
    /// </summary>
    internal FileResult CreateDirectoryInternal(string path)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return FileResult.IoError;

        // Get parent directory and dirname
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string dirName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        if (dirName.Length == 0 || dirName.Length > 255)
            return FileResult.InvalidPath;

        // Find parent directory inode
        uint parentInodeNum;
        var result = FindInode(parentPath, out parentInodeNum);
        if (result != FileResult.Success)
            return result;

        // Check if already exists
        uint existingInode;
        if (SearchDirectoryByInode(parentInodeNum, dirName, out existingInode))
            return FileResult.AlreadyExists;

        // Allocate new inode
        uint group = (parentInodeNum - 1) / _inodesPerGroup;
        uint newInodeNum = AllocateInode(group);
        if (newInodeNum == 0)
            return FileResult.NoSpace;

        // Allocate block for directory entries (. and ..)
        uint dataBlock = AllocateBlock(group);
        if (dataBlock == 0)
        {
            FreeInode(newInodeNum);
            return FileResult.NoSpace;
        }

        // Initialize directory block with . and .. entries
        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            FreeBlock(dataBlock);
            FreeInode(newInodeNum);
            return FileResult.IoError;
        }

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Zero the buffer
            for (uint i = 0; i < _blockSize; i++)
                buffer[i] = 0;

            // Create "." entry
            var dotEntry = (Ext2DirEntry*)buffer;
            dotEntry->Inode = newInodeNum;
            dotEntry->RecLen = 12; // Minimum for "." (8 header + 1 name + 3 padding)
            dotEntry->NameLen = 1;
            dotEntry->FileType = Ext2FileType.FT_DIR;
            byte* dotName = (byte*)dotEntry + 8;
            dotName[0] = (byte)'.';

            // Create ".." entry
            var dotdotEntry = (Ext2DirEntry*)(buffer + 12);
            dotdotEntry->Inode = parentInodeNum;
            dotdotEntry->RecLen = (ushort)(_blockSize - 12); // Rest of block
            dotdotEntry->NameLen = 2;
            dotdotEntry->FileType = Ext2FileType.FT_DIR;
            byte* dotdotName = (byte*)dotdotEntry + 8;
            dotdotName[0] = (byte)'.';
            dotdotName[1] = (byte)'.';

            // Write directory block
            if (!WriteBlock(dataBlock, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                FreeBlock(dataBlock);
                FreeInode(newInodeNum);
                return FileResult.IoError;
            }

            Memory.FreePages(bufferPhys, pageCount);

            // Initialize inode
            Ext2Inode inode = default;
            inode.Mode = (ushort)(Ext2FileMode.S_IFDIR | 0x1FF); // Directory, rwxrwxrwx
            inode.Uid = 0;
            inode.Gid = 0;
            inode.Size = _blockSize;
            inode.LinksCount = 2; // . and parent's entry
            inode.Blocks = _blockSize / 512;
            inode.Block[0] = dataBlock;

            if (!WriteInode(newInodeNum, ref inode))
            {
                FreeBlock(dataBlock);
                FreeInode(newInodeNum);
                return FileResult.IoError;
            }

            // Add entry in parent directory
            if (!AddDirectoryEntry(parentInodeNum, dirName, newInodeNum, Ext2FileType.FT_DIR))
            {
                FreeBlock(dataBlock);
                FreeInode(newInodeNum);
                return FileResult.IoError;
            }

            // Increment parent's link count (for ..)
            Ext2Inode parentInode;
            if (ReadInode(parentInodeNum, out parentInode))
            {
                parentInode.LinksCount++;
                WriteInode(parentInodeNum, ref parentInode);
            }

            // Update group descriptor used directories count
            _groupDescs[group].UsedDirsCount++;
            WriteGroupDescriptor(group);

            return FileResult.Success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            FreeBlock(dataBlock);
            FreeInode(newInodeNum);
            return FileResult.IoError;
        }
    }

    /// <summary>
    /// Delete a file.
    /// </summary>
    internal FileResult DeleteFileInternal(string path)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return FileResult.IoError;

        // Find the inode
        uint inodeNum;
        var result = FindInode(path, out inodeNum);
        if (result != FileResult.Success)
            return result;

        // Read inode
        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return FileResult.IoError;

        // Verify it's a regular file
        if ((inode.Mode & Ext2FileMode.S_IFMT) != Ext2FileMode.S_IFREG)
            return FileResult.IsADirectory;

        // Get parent directory and filename
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        uint parentInodeNum;
        result = FindInode(parentPath, out parentInodeNum);
        if (result != FileResult.Success)
            return result;

        // Remove directory entry
        if (!RemoveDirectoryEntry(parentInodeNum, fileName))
            return FileResult.IoError;

        // Decrement link count
        inode.LinksCount--;

        if (inode.LinksCount == 0)
        {
            // Free all data blocks
            FreeFileBlocks(&inode);

            // Free inode
            FreeInode(inodeNum);
        }
        else
        {
            // Just update inode
            WriteInode(inodeNum, ref inode);
        }

        return FileResult.Success;
    }

    /// <summary>
    /// Delete a directory (must be empty).
    /// </summary>
    internal FileResult DeleteDirectoryInternal(string path)
    {
        if (_device == null || _groupDescs == null || _readOnly)
            return FileResult.IoError;

        // Can't delete root
        if (path == "/" || path.Length == 0)
            return FileResult.InvalidPath;

        // Find the inode
        uint inodeNum;
        var result = FindInode(path, out inodeNum);
        if (result != FileResult.Success)
            return result;

        // Read inode
        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return FileResult.IoError;

        // Verify it's a directory
        if ((inode.Mode & Ext2FileMode.S_IFMT) != Ext2FileMode.S_IFDIR)
            return FileResult.NotADirectory;

        // Check if empty (only . and .. entries)
        if (!IsDirectoryEmpty(inodeNum))
            return FileResult.NotEmpty;

        // Get parent directory and dirname
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string dirName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        uint parentInodeNum;
        result = FindInode(parentPath, out parentInodeNum);
        if (result != FileResult.Success)
            return result;

        // Remove directory entry from parent
        if (!RemoveDirectoryEntry(parentInodeNum, dirName))
            return FileResult.IoError;

        // Decrement parent's link count (for ..)
        Ext2Inode parentInode;
        if (ReadInode(parentInodeNum, out parentInode))
        {
            parentInode.LinksCount--;
            WriteInode(parentInodeNum, ref parentInode);
        }

        // Free directory's data blocks
        FreeFileBlocks(&inode);

        // Free inode
        FreeInode(inodeNum);

        // Update group descriptor
        uint group = (inodeNum - 1) / _inodesPerGroup;
        _groupDescs[group].UsedDirsCount--;
        WriteGroupDescriptor(group);

        return FileResult.Success;
    }

    /// <summary>
    /// Check if a directory is empty (only contains . and ..).
    /// </summary>
    private bool IsDirectoryEmpty(uint inodeNum)
    {
        Ext2Inode inode;
        if (!ReadInode(inodeNum, out inode))
            return false;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            uint blockIndex = 0;
            uint dirSize = inode.Size;

            while (blockIndex * _blockSize < dirSize)
            {
                Ext2Inode localInode = inode;
                uint blockNum = GetBlockNumber(&localInode, blockIndex);
                if (blockNum == 0)
                    break;

                if (!ReadBlock(blockNum, buffer))
                    break;

                uint offset = 0;
                while (offset < _blockSize)
                {
                    var entry = (Ext2DirEntry*)(buffer + offset);
                    if (entry->RecLen == 0)
                        break;

                    if (entry->Inode != 0)
                    {
                        // Check if it's . or ..
                        byte* entryName = (byte*)entry + 8;
                        if (entry->NameLen == 1 && entryName[0] == '.')
                        {
                            // . entry, ok
                        }
                        else if (entry->NameLen == 2 && entryName[0] == '.' && entryName[1] == '.')
                        {
                            // .. entry, ok
                        }
                        else
                        {
                            // Found a real entry - not empty
                            Memory.FreePages(bufferPhys, pageCount);
                            return false;
                        }
                    }

                    offset += entry->RecLen;
                }

                blockIndex++;
            }

            Memory.FreePages(bufferPhys, pageCount);
            return true;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Free all data blocks associated with an inode.
    /// </summary>
    private void FreeFileBlocks(Ext2Inode* inode)
    {
        uint ptrsPerBlock = _blockSize / 4;

        // Free direct blocks
        for (int i = 0; i < Ext2BlockPtrs.NDIR_BLOCKS; i++)
        {
            if (inode->Block[i] != 0)
            {
                FreeBlock(inode->Block[i]);
                inode->Block[i] = 0;
            }
        }

        // Free single indirect
        if (inode->Block[Ext2BlockPtrs.IND_BLOCK] != 0)
        {
            FreeIndirectBlocks(inode->Block[Ext2BlockPtrs.IND_BLOCK], 1, ptrsPerBlock);
            FreeBlock(inode->Block[Ext2BlockPtrs.IND_BLOCK]);
            inode->Block[Ext2BlockPtrs.IND_BLOCK] = 0;
        }

        // Free double indirect
        if (inode->Block[Ext2BlockPtrs.DIND_BLOCK] != 0)
        {
            FreeIndirectBlocks(inode->Block[Ext2BlockPtrs.DIND_BLOCK], 2, ptrsPerBlock);
            FreeBlock(inode->Block[Ext2BlockPtrs.DIND_BLOCK]);
            inode->Block[Ext2BlockPtrs.DIND_BLOCK] = 0;
        }

        // Free triple indirect
        if (inode->Block[Ext2BlockPtrs.TIND_BLOCK] != 0)
        {
            FreeIndirectBlocks(inode->Block[Ext2BlockPtrs.TIND_BLOCK], 3, ptrsPerBlock);
            FreeBlock(inode->Block[Ext2BlockPtrs.TIND_BLOCK]);
            inode->Block[Ext2BlockPtrs.TIND_BLOCK] = 0;
        }
    }

    /// <summary>
    /// Recursively free indirect blocks.
    /// </summary>
    private void FreeIndirectBlocks(uint blockNum, int level, uint ptrsPerBlock)
    {
        if (blockNum == 0 || level < 1)
            return;

        ulong pageCount = (_blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadBlock(blockNum, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return;
            }

            uint* ptrs = (uint*)buffer;
            for (uint i = 0; i < ptrsPerBlock; i++)
            {
                if (ptrs[i] != 0)
                {
                    if (level > 1)
                        FreeIndirectBlocks(ptrs[i], level - 1, ptrsPerBlock);
                    FreeBlock(ptrs[i]);
                }
            }

            Memory.FreePages(bufferPhys, pageCount);
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
        }
    }

    /// <summary>
    /// Search directory by inode number.
    /// </summary>
    private bool SearchDirectoryByInode(uint dirInodeNum, string name, out uint foundInode)
    {
        foundInode = 0;
        Ext2Inode dirInode;
        if (!ReadInode(dirInodeNum, out dirInode))
            return false;
        return SearchDirectory(dirInode, name, out foundInode);
    }

    #endregion
}
