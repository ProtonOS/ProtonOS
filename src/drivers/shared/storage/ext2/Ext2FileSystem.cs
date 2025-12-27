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
        Debug.Write("[Ext2] Mount called");
        Debug.WriteLine();

        if (_mounted)
            return FileResult.AlreadyExists;

        if (device == null)
            return FileResult.InvalidPath;

        _device = device;
        _readOnly = readOnly;
        _sectorSize = device.BlockSize;

        Debug.Write("[Ext2] Sector size: ");
        Debug.WriteDecimal((int)_sectorSize);
        Debug.WriteLine();

        // Read superblock at offset 1024
        ulong sbOffset = 1024;
        ulong sbSector = sbOffset / _sectorSize;
        uint offsetInSector = (uint)(sbOffset % _sectorSize);

        Debug.Write("[Ext2] Reading superblock from sector ");
        Debug.WriteDecimal((int)sbSector);
        Debug.Write(" offset ");
        Debug.WriteDecimal((int)offsetInSector);
        Debug.WriteLine();

        uint sectorsNeeded = (offsetInSector + 1024 + _sectorSize - 1) / _sectorSize;
        ulong pageCount = ((ulong)sectorsNeeded * _sectorSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return FileResult.IoError;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            int result = device.Read(sbSector, sectorsNeeded, buffer);
            Debug.Write("[Ext2] Read result: ");
            Debug.WriteDecimal(result);
            Debug.Write(" expected: ");
            Debug.WriteDecimal((int)sectorsNeeded);
            Debug.WriteLine();

            if (result != (int)sectorsNeeded)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.IoError;
            }

            // Copy superblock
            var sb = (Ext2Superblock*)(buffer + offsetInSector);
            Debug.Write("[Ext2] Magic: 0x");
            Debug.WriteHex((ulong)sb->Magic);
            Debug.Write(" expected: 0x");
            Debug.WriteHex((ulong)Ext2Magic.MAGIC);
            Debug.WriteLine();

            if (sb->Magic != Ext2Magic.MAGIC)
            {
                Debug.WriteLine("[EXT2] Invalid magic number");
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.InvalidPath;
            }

            Debug.Write("[Ext2] LogBlockSize=");
            Debug.WriteDecimal((int)sb->LogBlockSize);
            Debug.WriteLine();

            Debug.Write("[Ext2] BlocksPerGroup=");
            Debug.WriteDecimal((int)sb->BlocksPerGroup);
            Debug.WriteLine();

            Debug.Write("[Ext2] InodesPerGroup=");
            Debug.WriteDecimal((int)sb->InodesPerGroup);
            Debug.WriteLine();

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

            Debug.Write("[Ext2] blockSize=");
            Debug.WriteDecimal((int)_blockSize);
            Debug.Write(" sectorsPerBlock=");
            Debug.WriteDecimal((int)_sectorsPerBlock);
            Debug.WriteLine();

            Memory.FreePages(bufferPhys, pageCount);

            Debug.Write("[Ext2] groupCount=");
            Debug.WriteDecimal((int)_groupCount);
            Debug.WriteLine();

            Debug.Write("[EXT2] Mounted: ");
            Debug.WriteDecimal((int)_blockSize);
            Debug.Write(" byte blocks, ");
            Debug.WriteDecimal((int)_groupCount);
            Debug.Write(" groups, ");
            Debug.WriteDecimal((int)_totalInodes);
            Debug.WriteLine(" inodes");

            // Load block group descriptors
            if (!LoadGroupDescriptors())
            {
                Debug.WriteLine("[EXT2] Failed to load group descriptors");
                return FileResult.IoError;
            }

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
                return FileResult.NotSupported; // TODO: implement create

            case FileMode.OpenOrCreate:
            case FileMode.Create:
                if (findResult != FileResult.Success)
                    return FileResult.NotSupported; // TODO: implement create
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
        return FileResult.NotSupported;
    }

    public FileResult DeleteFile(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;
        return FileResult.NotSupported;
    }

    public FileResult DeleteDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;
        return FileResult.NotSupported;
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
                            for (int i = 0; i < name.Length && match; i++)
                            {
                                if (entry->Name[i] != (byte)name[i])
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

    #endregion
}
