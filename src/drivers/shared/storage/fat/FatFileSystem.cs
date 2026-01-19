// ProtonOS FAT Filesystem Driver
// Implements IFileSystem for FAT12, FAT16, and FAT32

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Fat;

/// <summary>
/// FAT filesystem driver supporting FAT12, FAT16, and FAT32.
/// </summary>
public unsafe class FatFileSystem : IFileSystem
{
    // Block device
    private IBlockDevice? _device;
    private bool _mounted;
    private bool _readOnly;

    // BPB data
    private FatType _fatType;
    private uint _bytesPerSector;
    private uint _sectorsPerCluster;
    private uint _bytesPerCluster;
    private uint _reservedSectors;
    private uint _numFats;
    private uint _rootEntryCount;       // FAT12/16 only
    private uint _totalSectors;
    private uint _fatSizeSectors;
    private uint _rootDirSectors;       // FAT12/16 only
    private uint _firstDataSector;
    private uint _dataSectors;
    private uint _countOfClusters;
    private uint _rootCluster;          // FAT32 only (cluster of root dir)

    // Cached sectors
    private byte[]? _fatBuffer;         // First FAT
    private ulong _fatStartSector;

    // Volume info
    private string _volumeLabel = "";
    private uint _volumeId;

    #region IDriver Implementation

    public string DriverName => "fat";
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

    public string FilesystemName => _fatType switch
    {
        FatType.Fat12 => "FAT12",
        FatType.Fat16 => "FAT16",
        FatType.Fat32 => "FAT32",
        _ => "FAT"
    };

    public FilesystemCapabilities Capabilities =>
        FilesystemCapabilities.Read |
        FilesystemCapabilities.Write |
        FilesystemCapabilities.CasePreserving |
        FilesystemCapabilities.Timestamps;

    public bool IsMounted => _mounted;

    public string? VolumeLabel => _volumeLabel.Length > 0 ? _volumeLabel : null;

    public ulong TotalBytes => _dataSectors * _bytesPerSector;

    public ulong FreeBytes
    {
        get
        {
            if (!_mounted || _fatBuffer == null)
                return 0;

            uint freeClusters = 0;
            for (uint i = 2; i < _countOfClusters + 2; i++)
            {
                if (GetFatEntry(i) == 0)
                    freeClusters++;
            }
            return (ulong)freeClusters * _bytesPerCluster;
        }
    }

    public bool Probe(IBlockDevice device)
    {
        if (device == null || device.BlockSize == 0)
        {
            Debug.Write("[FAT Probe] device null or blockSize 0");
            Debug.WriteLine();
            return false;
        }

        Debug.Write("[FAT Probe] BlockSize=");
        Debug.WriteHex(device.BlockSize);
        Debug.WriteLine();

        // Read boot sector
        byte* buffer = stackalloc byte[(int)device.BlockSize];
        int result = device.Read(0, 1, buffer);
        if (result != 1)
        {
            Debug.Write("[FAT Probe] Read failed, result=");
            Debug.WriteDecimal(result);
            Debug.WriteLine();
            return false;
        }

        Debug.Write("[FAT Probe] Boot sig: ");
        Debug.WriteHex(buffer[510]);
        Debug.Write(" ");
        Debug.WriteHex(buffer[511]);
        Debug.WriteLine();

        // Check boot signature
        if (buffer[510] != 0x55 || buffer[511] != 0xAA)
        {
            Debug.WriteLine("[FAT Probe] Invalid boot signature");
            return false;
        }

        // Check BPB
        var bpb = (FatBpb*)buffer;
        Debug.Write("[FAT Probe] BytsPerSec=");
        Debug.WriteDecimal(bpb->BytsPerSec);
        Debug.Write(" SecPerClus=");
        Debug.WriteDecimal(bpb->SecPerClus);
        Debug.Write(" NumFATs=");
        Debug.WriteDecimal(bpb->NumFATs);
        Debug.WriteLine();

        if (bpb->BytsPerSec == 0 || bpb->SecPerClus == 0 || bpb->NumFATs == 0)
        {
            Debug.WriteLine("[FAT Probe] Invalid BPB fields");
            return false;
        }

        // Check valid bytes per sector
        ushort bps = bpb->BytsPerSec;
        if (bps != 512 && bps != 1024 && bps != 2048 && bps != 4096)
        {
            Debug.Write("[FAT Probe] Invalid bytes per sector: ");
            Debug.WriteDecimal(bps);
            Debug.WriteLine();
            return false;
        }

        Debug.WriteLine("[FAT Probe] Valid FAT filesystem detected");
        return true;
    }

    public FileResult Mount(IBlockDevice? device, bool readOnly = false)
    {
        if (_mounted)
            return FileResult.AlreadyExists;

        if (device == null)
            return FileResult.InvalidPath;

        _device = device;
        _readOnly = readOnly;

        // Allocate buffer for boot sector
        uint sectorSize = device.BlockSize;
        ulong pageCount = (sectorSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return FileResult.IoError;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Read boot sector
            int result = device.Read(0, 1, buffer);
            if (result != 1)
            {
                Debug.WriteLine("[FAT] Failed to read boot sector");
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.IoError;
            }

            // Parse BPB
            var bpb = (FatBpb*)buffer;

            _bytesPerSector = bpb->BytsPerSec;
            _sectorsPerCluster = bpb->SecPerClus;
            _bytesPerCluster = _bytesPerSector * _sectorsPerCluster;
            _reservedSectors = bpb->RsvdSecCnt;
            _numFats = bpb->NumFATs;
            _rootEntryCount = bpb->RootEntCnt;

            // Total sectors
            _totalSectors = bpb->TotSec16 != 0 ? bpb->TotSec16 : bpb->TotSec32;

            // FAT size
            _fatSizeSectors = bpb->FATSz16;
            if (_fatSizeSectors == 0)
            {
                // FAT32
                var ebr32 = (Fat32Ebr*)(buffer + 36);
                _fatSizeSectors = ebr32->FATSz32;
                _rootCluster = ebr32->RootClus;

                // Volume info
                _volumeId = ebr32->VolID;
                _volumeLabel = ExtractString(ebr32->VolLab, 11);
            }
            else
            {
                // FAT12/16
                var ebr16 = (Fat16Ebr*)(buffer + 36);
                _volumeId = ebr16->VolID;
                _rootCluster = 0;
                _volumeLabel = ExtractString(ebr16->VolLab, 11);
            }

            // Calculate derived values
            _rootDirSectors = ((_rootEntryCount * 32) + (_bytesPerSector - 1)) / _bytesPerSector;
            _firstDataSector = _reservedSectors + (_numFats * _fatSizeSectors) + _rootDirSectors;
            _dataSectors = _totalSectors - _firstDataSector;
            _countOfClusters = _dataSectors / _sectorsPerCluster;

            // Determine FAT type based on cluster count
            if (_countOfClusters < 4085)
                _fatType = FatType.Fat12;
            else if (_countOfClusters < 65525)
                _fatType = FatType.Fat16;
            else
                _fatType = FatType.Fat32;

            Debug.Write("[FAT] Mounted ");
            Debug.Write(FilesystemName);
            Debug.Write(" volume: ");
            Debug.Write(_volumeLabel);
            Debug.Write(", ");
            Debug.WriteHex(_countOfClusters);
            Debug.WriteLine(" clusters");

            // Load FAT into memory
            _fatStartSector = _reservedSectors;
            uint fatBytes = _fatSizeSectors * _bytesPerSector;
            _fatBuffer = new byte[fatBytes];

            // Read FAT
            if (!ReadFat())
            {
                Debug.WriteLine("[FAT] Failed to read FAT");
                _fatBuffer = null;
                Memory.FreePages(bufferPhys, pageCount);
                return FileResult.IoError;
            }

            _mounted = true;
            Memory.FreePages(bufferPhys, pageCount);
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

        // TODO: Flush any dirty buffers
        _fatBuffer = null;
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
        if (path == "/" || path == "")
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

        // Find entry
        FatDirEntry entry;
        uint parentCluster;
        int entryIndex;
        var result = FindEntry(path, out entry, out parentCluster, out entryIndex);
        if (result != FileResult.Success)
            return result;

        info = DirEntryToFileInfo(entry, path);
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

        FatDirEntry entry;
        uint parentCluster;
        int entryIndex;
        var findResult = FindEntry(path, out entry, out parentCluster, out entryIndex);

        switch (mode)
        {
            case FileMode.Open:
                if (findResult != FileResult.Success)
                    return FileResult.NotFound;
                if ((entry.Attr & (byte)FatAttr.Directory) != 0)
                    return FileResult.IsADirectory;
                break;

            case FileMode.CreateNew:
                if (findResult == FileResult.Success)
                    return FileResult.AlreadyExists;
                // Create new file
                return CreateFile(path, access, out handle);

            case FileMode.OpenOrCreate:
            case FileMode.Create:
                if (findResult != FileResult.Success)
                    return CreateFile(path, access, out handle);
                if ((entry.Attr & (byte)FatAttr.Directory) != 0)
                    return FileResult.IsADirectory;
                if (mode == FileMode.Create)
                {
                    // Truncate
                    TruncateFile(ref entry, parentCluster, entryIndex);
                }
                break;

            case FileMode.Truncate:
                if (findResult != FileResult.Success)
                    return FileResult.NotFound;
                if ((entry.Attr & (byte)FatAttr.Directory) != 0)
                    return FileResult.IsADirectory;
                TruncateFile(ref entry, parentCluster, entryIndex);
                break;

            case FileMode.Append:
                if (findResult != FileResult.Success)
                    return FileResult.NotFound;
                if ((entry.Attr & (byte)FatAttr.Directory) != 0)
                    return FileResult.IsADirectory;
                // Handle will seek to end
                break;

            default:
                return FileResult.NotSupported;
        }

        uint firstCluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;
        var fileHandle = new FatFileHandle(this, firstCluster, entry.FileSize, access);
        fileHandle.Init(parentCluster, entryIndex, mode == FileMode.Append);
        handle = fileHandle;
        return FileResult.Success;
    }

    public FileResult OpenDirectory(string path, out IDirectoryHandle? handle)
    {
        handle = null;
        if (!_mounted || _device == null)
            return FileResult.IoError;

        path = NormalizePath(path);

        uint cluster;
        if (path == "/" || path.Length == 0)
        {
            // Root directory
            cluster = _fatType == FatType.Fat32 ? _rootCluster : 0;
        }
        else
        {
            FatDirEntry entry;
            uint parentCluster;
            int entryIndex;
            var result = FindEntry(path, out entry, out parentCluster, out entryIndex);
            if (result != FileResult.Success)
                return result;

            if ((entry.Attr & (byte)FatAttr.Directory) == 0)
                return FileResult.NotADirectory;

            cluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;
        }

        Debug.Write("[FAT OpenDir] cluster=");
        Debug.WriteHex(cluster);
        Debug.WriteLine();
        handle = new FatDirectoryHandle(this, cluster, path);
        return FileResult.Success;
    }

    public FileResult CreateDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);

        // Get parent directory and dirname
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string dirName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        if (dirName.Length == 0)
            return FileResult.InvalidPath;

        // Find parent directory
        uint parentCluster;
        if (parentPath == "/" || parentPath.Length == 0)
        {
            parentCluster = _fatType == FatType.Fat32 ? _rootCluster : 0;
        }
        else
        {
            FatDirEntry parentEntry;
            uint grandParent;
            int parentIndex;
            var result = FindEntry(parentPath, out parentEntry, out grandParent, out parentIndex);
            if (result != FileResult.Success)
                return result;

            if ((parentEntry.Attr & (byte)FatAttr.Directory) == 0)
                return FileResult.NotADirectory;

            parentCluster = ((uint)parentEntry.FstClusHI << 16) | parentEntry.FstClusLO;
        }

        // Check if already exists
        FatDirEntry existingEntry;
        uint existingParent;
        int existingIndex;
        if (FindEntry(path, out existingEntry, out existingParent, out existingIndex) == FileResult.Success)
            return FileResult.AlreadyExists;

        // Allocate cluster for new directory
        uint dirCluster = AllocateCluster();
        if (dirCluster == 0)
            return FileResult.NoSpace;

        // Initialize directory with . and .. entries
        ulong pageCount = (_bytesPerCluster + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            FreeClusterChain(dirCluster);
            return FileResult.IoError;
        }

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Zero buffer
        for (uint i = 0; i < _bytesPerCluster; i++)
            buffer[i] = 0;

        try
        {
            var entries = (FatDirEntry*)buffer;

            // "." entry
            for (int i = 0; i < 11; i++)
                entries[0].Name[i] = (byte)' ';
            entries[0].Name[0] = (byte)'.';
            entries[0].Attr = (byte)FatAttr.Directory;
            entries[0].FstClusLO = (ushort)(dirCluster & 0xFFFF);
            entries[0].FstClusHI = (ushort)((dirCluster >> 16) & 0xFFFF);

            // ".." entry
            for (int i = 0; i < 11; i++)
                entries[1].Name[i] = (byte)' ';
            entries[1].Name[0] = (byte)'.';
            entries[1].Name[1] = (byte)'.';
            entries[1].Attr = (byte)FatAttr.Directory;
            entries[1].FstClusLO = (ushort)(parentCluster & 0xFFFF);
            entries[1].FstClusHI = (ushort)((parentCluster >> 16) & 0xFFFF);

            // Write directory cluster
            if (!WriteCluster(dirCluster, buffer))
            {
                Memory.FreePages(bufferPhys, pageCount);
                FreeClusterChain(dirCluster);
                return FileResult.IoError;
            }

            Memory.FreePages(bufferPhys, pageCount);

            // Create directory entry in parent
            FatDirEntry newEntry;
            int entryIndex;
            if (!CreateDirectoryEntry(parentCluster, dirName, true, out newEntry, out entryIndex))
            {
                FreeClusterChain(dirCluster);
                return FileResult.IoError;
            }

            // Update the entry with the cluster number
            newEntry.FstClusLO = (ushort)(dirCluster & 0xFFFF);
            newEntry.FstClusHI = (ushort)((dirCluster >> 16) & 0xFFFF);
            UpdateDirectoryEntry(parentCluster, entryIndex, ref newEntry);

            WriteFat();
            return FileResult.Success;
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            FreeClusterChain(dirCluster);
            return FileResult.IoError;
        }
    }

    public FileResult DeleteFile(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);

        // Find the entry
        FatDirEntry entry;
        uint parentCluster;
        int entryIndex;
        var result = FindEntry(path, out entry, out parentCluster, out entryIndex);
        if (result != FileResult.Success)
            return result;

        // Check it's a file, not a directory
        if ((entry.Attr & (byte)FatAttr.Directory) != 0)
            return FileResult.IsADirectory;

        // Free cluster chain
        uint firstCluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;
        if (firstCluster >= 2)
        {
            FreeClusterChain(firstCluster);
        }

        // Delete directory entry
        if (!DeleteDirectoryEntry(parentCluster, entryIndex))
            return FileResult.IoError;

        WriteFat();
        return FileResult.Success;
    }

    public FileResult DeleteDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        path = NormalizePath(path);

        // Can't delete root
        if (path == "/" || path.Length == 0)
            return FileResult.InvalidPath;

        // Find the entry
        FatDirEntry entry;
        uint parentCluster;
        int entryIndex;
        var result = FindEntry(path, out entry, out parentCluster, out entryIndex);
        if (result != FileResult.Success)
            return result;

        // Check it's a directory
        if ((entry.Attr & (byte)FatAttr.Directory) == 0)
            return FileResult.NotADirectory;

        uint dirCluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;

        // Check if empty (only . and ..)
        if (!IsDirectoryEmpty(dirCluster))
            return FileResult.NotEmpty;

        // Free cluster chain
        if (dirCluster >= 2)
        {
            FreeClusterChain(dirCluster);
        }

        // Delete directory entry
        if (!DeleteDirectoryEntry(parentCluster, entryIndex))
            return FileResult.IoError;

        WriteFat();
        return FileResult.Success;
    }

    /// <summary>
    /// Check if a directory is empty (only contains . and ..).
    /// </summary>
    private bool IsDirectoryEmpty(uint dirCluster)
    {
        if (dirCluster < 2)
            return true;

        ulong pageCount = (_bytesPerCluster + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);
        uint entriesPerCluster = _bytesPerCluster / 32;

        try
        {
            uint cluster = dirCluster;
            while (!FatCluster.IsEndOfChain(cluster, _fatType))
            {
                if (!ReadCluster(cluster, buffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var entries = (FatDirEntry*)buffer;
                for (uint i = 0; i < entriesPerCluster; i++)
                {
                    if (entries[i].Name[0] == 0) // End of directory
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return true;
                    }

                    if (entries[i].Name[0] == 0xE5) // Deleted
                        continue;

                    // Check for . and ..
                    if (entries[i].Name[0] == '.')
                    {
                        if (entries[i].Name[1] == ' ' || entries[i].Name[1] == '.')
                            continue; // . or ..
                    }

                    // Found a real entry
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                cluster = GetFatEntry(cluster);
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

    public FileResult Rename(string oldPath, string newPath)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        // TODO: Implement rename
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
    /// Read the FAT into memory.
    /// </summary>
    private bool ReadFat()
    {
        if (_device == null || _fatBuffer == null)
            return false;

        uint sectorsToRead = _fatSizeSectors;
        uint bytesPerSector = _bytesPerSector;

        // Allocate temp buffer
        ulong pageCount = ((ulong)sectorsToRead * bytesPerSector + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Read FAT sectors
            int result = _device.Read(_fatStartSector, sectorsToRead, buffer);
            if (result != (int)sectorsToRead)
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            // Copy to managed array
            for (uint i = 0; i < _fatBuffer.Length; i++)
            {
                _fatBuffer[i] = buffer[i];
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
    /// Get FAT entry for a cluster.
    /// </summary>
    internal uint GetFatEntry(uint cluster)
    {
        if (_fatBuffer == null)
            return 0xFFFFFFFF;

        switch (_fatType)
        {
            case FatType.Fat12:
                {
                    uint offset = cluster + (cluster / 2);
                    if (offset + 1 >= _fatBuffer.Length)
                        return 0xFFFFFFFF;
                    uint value = (uint)_fatBuffer[offset] | ((uint)_fatBuffer[offset + 1] << 8);
                    if ((cluster & 1) != 0)
                        return value >> 4;
                    else
                        return value & 0xFFF;
                }

            case FatType.Fat16:
                {
                    uint offset = cluster * 2;
                    if (offset + 1 >= _fatBuffer.Length)
                        return 0xFFFFFFFF;
                    return (uint)_fatBuffer[offset] | ((uint)_fatBuffer[offset + 1] << 8);
                }

            case FatType.Fat32:
                {
                    uint offset = cluster * 4;
                    if (offset + 3 >= _fatBuffer.Length)
                        return 0xFFFFFFFF;
                    uint value = (uint)_fatBuffer[offset] |
                                 ((uint)_fatBuffer[offset + 1] << 8) |
                                 ((uint)_fatBuffer[offset + 2] << 16) |
                                 ((uint)_fatBuffer[offset + 3] << 24);
                    return value & 0x0FFFFFFF;
                }

            default:
                return 0xFFFFFFFF;
        }
    }

    /// <summary>
    /// Convert cluster number to sector number.
    /// </summary>
    internal ulong ClusterToSector(uint cluster)
    {
        return _firstDataSector + ((ulong)(cluster - 2) * _sectorsPerCluster);
    }

    /// <summary>
    /// Read a cluster's data.
    /// </summary>
    internal bool ReadCluster(uint cluster, byte* buffer)
    {
        if (_device == null || cluster < 2)
            return false;

        ulong sector = ClusterToSector(cluster);
        int result = _device.Read(sector, _sectorsPerCluster, buffer);
        return result == (int)_sectorsPerCluster;
    }

    /// <summary>
    /// Read root directory (FAT12/16).
    /// </summary>
    internal bool ReadRootDirectory(byte* buffer, uint maxBytes)
    {
        if (_device == null)
            return false;

        // FAT32 root dir is a cluster chain
        if (_fatType == FatType.Fat32)
            return ReadCluster(_rootCluster, buffer);

        // FAT12/16 root dir is at fixed location
        ulong rootSector = _reservedSectors + (_numFats * _fatSizeSectors);
        uint sectorsToRead = _rootDirSectors;
        if (sectorsToRead * _bytesPerSector > maxBytes)
            sectorsToRead = maxBytes / _bytesPerSector;

        int result = _device.Read(rootSector, sectorsToRead, buffer);
        return result == (int)sectorsToRead;
    }

    /// <summary>
    /// Case-insensitive string comparison helper (avoids StringComparison which needs AOT).
    /// </summary>
    private static bool EqualsIgnoreCase(string a, string b)
    {
        if (a == null || b == null)
            return a == b;
        if (a.Length != b.Length)
            return false;

        for (int i = 0; i < a.Length; i++)
        {
            char ca = a[i];
            char cb = b[i];

            // Simple ASCII case folding
            if (ca >= 'a' && ca <= 'z')
                ca = (char)(ca - 32);
            if (cb >= 'a' && cb <= 'z')
                cb = (char)(cb - 32);

            if (ca != cb)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Find a directory entry by path.
    /// </summary>
    internal FileResult FindEntry(string path, out FatDirEntry entry, out uint parentCluster, out int entryIndex)
    {
        entry = default;
        parentCluster = 0;
        entryIndex = -1;

        if (string.IsNullOrEmpty(path) || path == "/")
            return FileResult.InvalidPath;

        // Start at root
        uint currentCluster = _fatType == FatType.Fat32 ? _rootCluster : 0;
        bool isRootDir = true;

        // Parse path components without using Split
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

            // Extract component
            string part = path.Substring(pathStart, pathEnd - pathStart);
            pathStart = pathEnd;

            if (part.Length == 0)
                continue;

            parentCluster = currentCluster;

            // Search directory for entry
            bool found = false;
            int index = 0;

            if (isRootDir && _fatType != FatType.Fat32)
            {
                // FAT12/16 root directory
                uint rootBytes = _rootEntryCount * 32;
                ulong pageCount = (rootBytes + 4095) / 4096;
                ulong bufferPhys = Memory.AllocatePages(pageCount);
                if (bufferPhys == 0)
                    return FileResult.IoError;

                byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);
                if (!ReadRootDirectory(buffer, rootBytes))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return FileResult.IoError;
                }

                var dirEntry = (FatDirEntry*)buffer;
                for (uint i = 0; i < _rootEntryCount; i++)
                {
                    if (dirEntry[i].Name[0] == 0) // End of directory
                        break;
                    if (dirEntry[i].Name[0] == 0xE5) // Deleted
                        continue;
                    if ((dirEntry[i].Attr & (byte)FatAttr.VolumeId) != 0 &&
                        (dirEntry[i].Attr & (byte)FatAttr.LongName) != (byte)FatAttr.LongName)
                        continue;
                    if ((dirEntry[i].Attr & (byte)FatAttr.LongName) == (byte)FatAttr.LongName)
                        continue; // Skip LFN entries for now

                    string name = GetShortName(&dirEntry[i]);
                    if (EqualsIgnoreCase(name, part))
                    {
                        entry = dirEntry[i];
                        entryIndex = (int)i;
                        found = true;
                        break;
                    }
                    index++;
                }

                Memory.FreePages(bufferPhys, pageCount);
            }
            else
            {
                // Regular directory (cluster chain)
                uint cluster = currentCluster;
                uint entriesPerCluster = _bytesPerCluster / 32;

                ulong pageCount = (_bytesPerCluster + 4095) / 4096;
                ulong bufferPhys = Memory.AllocatePages(pageCount);
                if (bufferPhys == 0)
                    return FileResult.IoError;

                byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

                while (!FatCluster.IsEndOfChain(cluster, _fatType))
                {
                    if (!ReadCluster(cluster, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return FileResult.IoError;
                    }

                    var dirEntry = (FatDirEntry*)buffer;
                    for (uint i = 0; i < entriesPerCluster; i++)
                    {
                        if (dirEntry[i].Name[0] == 0) // End
                        {
                            Memory.FreePages(bufferPhys, pageCount);
                            return FileResult.NotFound;
                        }
                        if (dirEntry[i].Name[0] == 0xE5) // Deleted
                            continue;
                        if ((dirEntry[i].Attr & (byte)FatAttr.VolumeId) != 0 &&
                            (dirEntry[i].Attr & (byte)FatAttr.LongName) != (byte)FatAttr.LongName)
                            continue;
                        if ((dirEntry[i].Attr & (byte)FatAttr.LongName) == (byte)FatAttr.LongName)
                            continue;

                        string name = GetShortName(&dirEntry[i]);
                        if (EqualsIgnoreCase(name, part))
                        {
                            entry = dirEntry[i];
                            entryIndex = index;
                            found = true;
                            break;
                        }
                        index++;
                    }

                    if (found)
                        break;

                    cluster = GetFatEntry(cluster);
                }

                Memory.FreePages(bufferPhys, pageCount);
            }

            if (!found)
                return FileResult.NotFound;

            // Move to next directory level
            currentCluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;
            isRootDir = false;
        }

        return FileResult.Success;
    }

    /// <summary>
    /// Get short (8.3) filename from directory entry.
    /// </summary>
    internal static string GetShortName(FatDirEntry* entry)
    {
        var chars = new char[12];
        int len = 0;

        // Name part (8 chars)
        for (int i = 0; i < 8; i++)
        {
            byte c = entry->Name[i];
            if (c == ' ')
                break;
            chars[len++] = (char)c;
        }

        // Extension part (3 chars)
        if (entry->Name[8] != ' ')
        {
            chars[len++] = '.';
            for (int i = 8; i < 11; i++)
            {
                byte c = entry->Name[i];
                if (c == ' ')
                    break;
                chars[len++] = (char)c;
            }
        }

        return new string(chars, 0, len);
    }

    /// <summary>
    /// Convert directory entry to FileInfo.
    /// </summary>
    private FileInfo DirEntryToFileInfo(FatDirEntry entry, string path)
    {
        var info = new FileInfo
        {
            Name = VFS.GetFileName(path),
            Path = path,
            Type = (entry.Attr & (byte)FatAttr.Directory) != 0
                ? FileEntryType.Directory
                : FileEntryType.File,
            Size = entry.FileSize,
            Attributes = FileAttributes.None,
        };

        if ((entry.Attr & (byte)FatAttr.ReadOnly) != 0)
            info.Attributes |= FileAttributes.ReadOnly;
        if ((entry.Attr & (byte)FatAttr.Hidden) != 0)
            info.Attributes |= FileAttributes.Hidden;
        if ((entry.Attr & (byte)FatAttr.System) != 0)
            info.Attributes |= FileAttributes.System;
        if ((entry.Attr & (byte)FatAttr.Archive) != 0)
            info.Attributes |= FileAttributes.Archive;

        // TODO: Convert DOS date/time to ticks
        return info;
    }

    /// <summary>
    /// Extract string from fixed byte array.
    /// </summary>
    private static string ExtractString(byte* data, int maxLen)
    {
        var chars = new char[maxLen];
        int len = 0;
        for (int i = 0; i < maxLen; i++)
        {
            if (data[i] == 0 || data[i] == ' ')
            {
                // Trim trailing spaces
                while (len > 0 && chars[len - 1] == ' ')
                    len--;
                break;
            }
            chars[len++] = (char)data[i];
        }
        // Trim trailing spaces
        while (len > 0 && chars[len - 1] == ' ')
            len--;
        return new string(chars, 0, len);
    }

    /// <summary>
    /// Normalize a path. Simplified to avoid complex string operations.
    /// </summary>
    private static string NormalizePath(string path)
    {
        // For simple paths like "/" or "/hello.txt", just return as-is
        if (path == null || path.Length == 0)
            return "/";

        // Simple case: already starts with / and no backslashes
        // The paths we deal with in FAT are typically simple
        if (path[0] == '/')
            return path;

        // Prepend slash if needed
        return "/" + path;
    }

    /// <summary>
    /// Create a new file.
    /// </summary>
    private FileResult CreateFile(string path, FileAccess access, out IFileHandle? handle)
    {
        handle = null;

        // Get parent directory and filename
        int lastSlash = path.LastIndexOf('/');
        string parentPath = lastSlash > 0 ? path.Substring(0, lastSlash) : "/";
        string fileName = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;

        if (fileName.Length == 0)
            return FileResult.InvalidPath;

        // Find parent directory
        uint parentCluster;
        if (parentPath == "/" || parentPath.Length == 0)
        {
            parentCluster = _fatType == FatType.Fat32 ? _rootCluster : 0;
        }
        else
        {
            FatDirEntry parentEntry;
            uint grandParent;
            int parentIndex;
            var result = FindEntry(parentPath, out parentEntry, out grandParent, out parentIndex);
            if (result != FileResult.Success)
                return result;

            if ((parentEntry.Attr & (byte)FatAttr.Directory) == 0)
                return FileResult.NotADirectory;

            parentCluster = ((uint)parentEntry.FstClusHI << 16) | parentEntry.FstClusLO;
        }

        // Create directory entry
        FatDirEntry newEntry;
        int entryIndex;
        if (!CreateDirectoryEntry(parentCluster, fileName, false, out newEntry, out entryIndex))
            return FileResult.IoError;

        // Create file handle
        uint firstCluster = ((uint)newEntry.FstClusHI << 16) | newEntry.FstClusLO;
        var fileHandle = new FatFileHandle(this, firstCluster, newEntry.FileSize, access);
        fileHandle.Init(parentCluster, entryIndex, false);
        handle = fileHandle;
        return FileResult.Success;
    }

    /// <summary>
    /// Truncate a file to zero length.
    /// </summary>
    private void TruncateFile(ref FatDirEntry entry, uint parentCluster, int entryIndex)
    {
        // Free existing cluster chain
        uint firstCluster = ((uint)entry.FstClusHI << 16) | entry.FstClusLO;
        if (firstCluster >= 2)
        {
            FreeClusterChain(firstCluster);
        }

        // Update entry
        entry.FstClusHI = 0;
        entry.FstClusLO = 0;
        entry.FileSize = 0;

        // Write updated entry back
        UpdateDirectoryEntry(parentCluster, entryIndex, ref entry);
    }

    // Properties for internal use
    internal FatType FatVariant => _fatType;
    internal uint BytesPerCluster => _bytesPerCluster;
    internal uint SectorsPerCluster => _sectorsPerCluster;
    internal uint BytesPerSector => _bytesPerSector;
    internal uint RootEntryCount => _rootEntryCount;
    internal uint RootCluster => _rootCluster;
    internal IBlockDevice? Device => _device;
    internal bool IsReadOnly => _readOnly;

    #endregion

    #region FAT Table Operations

    /// <summary>
    /// Set a FAT entry value.
    /// </summary>
    internal bool SetFatEntry(uint cluster, uint value)
    {
        if (_fatBuffer == null || _readOnly)
            return false;

        switch (_fatType)
        {
            case FatType.Fat12:
                {
                    uint offset = cluster + (cluster / 2);
                    if (offset + 1 >= _fatBuffer.Length)
                        return false;

                    if ((cluster & 1) != 0)
                    {
                        // Odd cluster: value goes in high 12 bits
                        _fatBuffer[offset] = (byte)((_fatBuffer[offset] & 0x0F) | ((value & 0x0F) << 4));
                        _fatBuffer[offset + 1] = (byte)(value >> 4);
                    }
                    else
                    {
                        // Even cluster: value goes in low 12 bits
                        _fatBuffer[offset] = (byte)(value & 0xFF);
                        _fatBuffer[offset + 1] = (byte)((_fatBuffer[offset + 1] & 0xF0) | ((value >> 8) & 0x0F));
                    }
                    return true;
                }

            case FatType.Fat16:
                {
                    uint offset = cluster * 2;
                    if (offset + 1 >= _fatBuffer.Length)
                        return false;
                    _fatBuffer[offset] = (byte)(value & 0xFF);
                    _fatBuffer[offset + 1] = (byte)((value >> 8) & 0xFF);
                    return true;
                }

            case FatType.Fat32:
                {
                    uint offset = cluster * 4;
                    if (offset + 3 >= _fatBuffer.Length)
                        return false;
                    // Preserve high 4 bits
                    uint existing = ((uint)_fatBuffer[offset + 3] & 0xF0) << 24;
                    value = (value & 0x0FFFFFFF) | existing;
                    _fatBuffer[offset] = (byte)(value & 0xFF);
                    _fatBuffer[offset + 1] = (byte)((value >> 8) & 0xFF);
                    _fatBuffer[offset + 2] = (byte)((value >> 16) & 0xFF);
                    _fatBuffer[offset + 3] = (byte)((value >> 24) & 0xFF);
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Write the FAT back to disk.
    /// </summary>
    internal bool WriteFat()
    {
        if (_device == null || _fatBuffer == null || _readOnly)
            return false;

        uint sectorsToWrite = _fatSizeSectors;
        uint bytesPerSector = _bytesPerSector;

        ulong pageCount = ((ulong)sectorsToWrite * bytesPerSector + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            // Copy FAT to buffer
            for (uint i = 0; i < _fatBuffer.Length; i++)
            {
                buffer[i] = _fatBuffer[i];
            }

            // Write all FAT copies
            for (uint fat = 0; fat < _numFats; fat++)
            {
                ulong sector = _fatStartSector + (fat * _fatSizeSectors);
                int result = _device.Write(sector, sectorsToWrite, buffer);
                if (result != (int)sectorsToWrite)
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }
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
    /// Allocate a new cluster.
    /// </summary>
    internal uint AllocateCluster()
    {
        if (_fatBuffer == null || _readOnly)
            return 0;

        // Search for a free cluster
        for (uint cluster = 2; cluster < _countOfClusters + 2; cluster++)
        {
            if (GetFatEntry(cluster) == 0)
            {
                // Mark as end of chain
                uint endMarker = _fatType switch
                {
                    FatType.Fat12 => FatCluster.EndOfChain12,
                    FatType.Fat16 => FatCluster.EndOfChain16,
                    FatType.Fat32 => FatCluster.EndOfChain32,
                    _ => 0xFFFFFFFF
                };

                if (!SetFatEntry(cluster, endMarker))
                    return 0;

                return cluster;
            }
        }

        return 0; // Disk full
    }

    /// <summary>
    /// Extend a cluster chain by adding a new cluster.
    /// </summary>
    internal uint ExtendClusterChain(uint lastCluster)
    {
        if (_fatBuffer == null || _readOnly)
            return 0;

        uint newCluster = AllocateCluster();
        if (newCluster == 0)
            return 0;

        // Link previous cluster to new one
        if (lastCluster >= 2)
        {
            if (!SetFatEntry(lastCluster, newCluster))
            {
                // Rollback
                SetFatEntry(newCluster, 0);
                return 0;
            }
        }

        return newCluster;
    }

    /// <summary>
    /// Free a cluster chain starting from the given cluster.
    /// </summary>
    internal void FreeClusterChain(uint startCluster)
    {
        if (_fatBuffer == null || _readOnly)
            return;

        uint cluster = startCluster;
        while (cluster >= 2 && !FatCluster.IsEndOfChain(cluster, _fatType))
        {
            uint next = GetFatEntry(cluster);
            SetFatEntry(cluster, 0);
            cluster = next;
        }

        // Don't forget to free the last cluster
        if (cluster >= 2 && !FatCluster.IsEndOfChain(cluster, _fatType))
            SetFatEntry(cluster, 0);
    }

    /// <summary>
    /// Write data to a cluster.
    /// </summary>
    internal bool WriteCluster(uint cluster, byte* buffer)
    {
        if (_device == null || cluster < 2 || _readOnly)
            return false;

        ulong sector = ClusterToSector(cluster);
        int result = _device.Write(sector, _sectorsPerCluster, buffer);
        return result == (int)_sectorsPerCluster;
    }

    #endregion

    #region Directory Entry Operations

    /// <summary>
    /// Create a new directory entry in the specified directory.
    /// </summary>
    private bool CreateDirectoryEntry(uint dirCluster, string name, bool isDirectory, out FatDirEntry entry, out int entryIndex)
    {
        entry = default;
        entryIndex = -1;

        // Create 8.3 filename
        if (!Create83Name(name, out entry))
            return false;

        // Set attributes
        entry.Attr = isDirectory ? (byte)FatAttr.Directory : (byte)FatAttr.Archive;

        // TODO: Set timestamps when RTC is available

        // Find free entry in directory
        if (dirCluster == 0 && _fatType != FatType.Fat32)
        {
            // FAT12/16 root directory (fixed size)
            return CreateEntryInRootDir(ref entry, out entryIndex);
        }
        else
        {
            // Normal directory (cluster chain)
            return CreateEntryInDirectory(dirCluster, ref entry, out entryIndex);
        }
    }

    /// <summary>
    /// Create a directory entry in FAT12/16 root directory.
    /// </summary>
    private bool CreateEntryInRootDir(ref FatDirEntry entry, out int entryIndex)
    {
        entryIndex = -1;
        if (_device == null)
            return false;

        uint rootBytes = _rootEntryCount * 32;
        ulong pageCount = (rootBytes + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            if (!ReadRootDirectory(buffer, rootBytes))
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }

            var dirEntry = (FatDirEntry*)buffer;

            // Find free entry
            for (uint i = 0; i < _rootEntryCount; i++)
            {
                if (dirEntry[i].Name[0] == 0 || dirEntry[i].Name[0] == 0xE5)
                {
                    // Found free entry
                    dirEntry[i] = entry;
                    entryIndex = (int)i;

                    // Write back
                    ulong rootSector = _reservedSectors + (_numFats * _fatSizeSectors);
                    int result = _device.Write(rootSector, _rootDirSectors, buffer);

                    Memory.FreePages(bufferPhys, pageCount);
                    return result == (int)_rootDirSectors;
                }
            }

            Memory.FreePages(bufferPhys, pageCount);
            return false; // Directory full
        }
        catch
        {
            Memory.FreePages(bufferPhys, pageCount);
            return false;
        }
    }

    /// <summary>
    /// Create a directory entry in a normal directory (cluster chain).
    /// </summary>
    private bool CreateEntryInDirectory(uint dirCluster, ref FatDirEntry entry, out int entryIndex)
    {
        entryIndex = -1;
        if (_device == null)
            return false;

        uint entriesPerCluster = _bytesPerCluster / 32;
        ulong pageCount = (_bytesPerCluster + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return false;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            uint cluster = dirCluster;
            uint globalIndex = 0;

            while (!FatCluster.IsEndOfChain(cluster, _fatType))
            {
                if (!ReadCluster(cluster, buffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;

                for (uint i = 0; i < entriesPerCluster; i++)
                {
                    if (dirEntry[i].Name[0] == 0 || dirEntry[i].Name[0] == 0xE5)
                    {
                        // Found free entry
                        dirEntry[i] = entry;
                        entryIndex = (int)globalIndex;

                        // Write back
                        if (!WriteCluster(cluster, buffer))
                        {
                            Memory.FreePages(bufferPhys, pageCount);
                            return false;
                        }

                        Memory.FreePages(bufferPhys, pageCount);
                        return true;
                    }
                    globalIndex++;
                }

                uint nextCluster = GetFatEntry(cluster);
                if (FatCluster.IsEndOfChain(nextCluster, _fatType))
                {
                    // Allocate new cluster for directory
                    uint newCluster = ExtendClusterChain(cluster);
                    if (newCluster == 0)
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return false;
                    }

                    // Zero new cluster
                    for (uint i = 0; i < _bytesPerCluster; i++)
                        buffer[i] = 0;

                    // Add entry at start
                    dirEntry = (FatDirEntry*)buffer;
                    dirEntry[0] = entry;
                    entryIndex = (int)globalIndex;

                    if (!WriteCluster(newCluster, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return false;
                    }

                    WriteFat();
                    Memory.FreePages(bufferPhys, pageCount);
                    return true;
                }

                cluster = nextCluster;
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
    /// Update a directory entry.
    /// </summary>
    internal bool UpdateDirectoryEntry(uint dirCluster, int entryIndex, ref FatDirEntry entry)
    {
        if (_device == null || _readOnly)
            return false;

        if (dirCluster == 0 && _fatType != FatType.Fat32)
        {
            // FAT12/16 root directory
            uint rootBytes = _rootEntryCount * 32;
            ulong pageCount = (rootBytes + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadRootDirectory(buffer, rootBytes))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[entryIndex] = entry;

                ulong rootSector = _reservedSectors + (_numFats * _fatSizeSectors);
                int result = _device.Write(rootSector, _rootDirSectors, buffer);

                Memory.FreePages(bufferPhys, pageCount);
                return result == (int)_rootDirSectors;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
        else
        {
            // Normal directory - find cluster containing entry
            uint entriesPerCluster = _bytesPerCluster / 32;
            uint clusterIndex = (uint)entryIndex / entriesPerCluster;
            uint offsetInCluster = (uint)entryIndex % entriesPerCluster;

            uint cluster = dirCluster;
            for (uint i = 0; i < clusterIndex && !FatCluster.IsEndOfChain(cluster, _fatType); i++)
            {
                cluster = GetFatEntry(cluster);
            }

            if (FatCluster.IsEndOfChain(cluster, _fatType))
                return false;

            ulong pageCount = (_bytesPerCluster + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadCluster(cluster, buffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[offsetInCluster] = entry;

                bool success = WriteCluster(cluster, buffer);
                Memory.FreePages(bufferPhys, pageCount);
                return success;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
    }

    /// <summary>
    /// Create an 8.3 filename from a long filename.
    /// </summary>
    private static bool Create83Name(string name, out FatDirEntry entry)
    {
        entry = default;

        // Find extension
        int dotPos = name.LastIndexOf('.');
        string baseName = dotPos >= 0 ? name.Substring(0, dotPos) : name;
        string ext = dotPos >= 0 && dotPos < name.Length - 1 ? name.Substring(dotPos + 1) : "";

        // Validate and convert
        if (baseName.Length == 0)
            return false;

        // Fill name field with spaces
        for (int i = 0; i < 11; i++)
            entry.Name[i] = (byte)' ';

        // Copy base name (up to 8 chars)
        int len = baseName.Length > 8 ? 8 : baseName.Length;
        for (int i = 0; i < len; i++)
        {
            char c = baseName[i];
            // Convert to uppercase
            if (c >= 'a' && c <= 'z')
                c = (char)(c - 32);
            // Basic validation (simplified)
            if (c == ' ' || c == '.')
                c = '_';
            entry.Name[i] = (byte)c;
        }

        // Copy extension (up to 3 chars)
        len = ext.Length > 3 ? 3 : ext.Length;
        for (int i = 0; i < len; i++)
        {
            char c = ext[i];
            if (c >= 'a' && c <= 'z')
                c = (char)(c - 32);
            if (c == ' ' || c == '.')
                c = '_';
            entry.Name[8 + i] = (byte)c;
        }

        return true;
    }

    /// <summary>
    /// Update just the cluster and size of a directory entry.
    /// Used by FatFileHandle.Flush() to update file metadata without
    /// needing to read/preserve all entry fields.
    /// </summary>
    internal bool UpdateDirectoryEntryPartial(uint dirCluster, int entryIndex, uint firstCluster, uint fileSize)
    {
        if (_device == null || _readOnly || entryIndex < 0)
            return false;

        if (dirCluster == 0 && _fatType != FatType.Fat32)
        {
            // FAT12/16 root directory
            uint rootBytes = _rootEntryCount * 32;
            ulong pageCount = (rootBytes + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadRootDirectory(buffer, rootBytes))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[entryIndex].FstClusLO = (ushort)(firstCluster & 0xFFFF);
                dirEntry[entryIndex].FstClusHI = (ushort)((firstCluster >> 16) & 0xFFFF);
                dirEntry[entryIndex].FileSize = fileSize;
                dirEntry[entryIndex].Attr |= (byte)FatAttr.Archive;

                ulong rootSector = _reservedSectors + (_numFats * _fatSizeSectors);
                int result = _device.Write(rootSector, _rootDirSectors, buffer);

                Memory.FreePages(bufferPhys, pageCount);
                return result == (int)_rootDirSectors;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
        else
        {
            // Normal directory
            uint entriesPerCluster = _bytesPerCluster / 32;
            uint clusterIndex = (uint)entryIndex / entriesPerCluster;
            uint offsetInCluster = (uint)entryIndex % entriesPerCluster;

            uint cluster = dirCluster;
            for (uint i = 0; i < clusterIndex && !FatCluster.IsEndOfChain(cluster, _fatType); i++)
            {
                cluster = GetFatEntry(cluster);
            }

            if (FatCluster.IsEndOfChain(cluster, _fatType))
                return false;

            ulong pageCount = (_bytesPerCluster + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadCluster(cluster, buffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[offsetInCluster].FstClusLO = (ushort)(firstCluster & 0xFFFF);
                dirEntry[offsetInCluster].FstClusHI = (ushort)((firstCluster >> 16) & 0xFFFF);
                dirEntry[offsetInCluster].FileSize = fileSize;
                dirEntry[offsetInCluster].Attr |= (byte)FatAttr.Archive;

                bool success = WriteCluster(cluster, buffer);
                Memory.FreePages(bufferPhys, pageCount);
                return success;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
    }

    /// <summary>
    /// Delete a directory entry by marking it as deleted.
    /// </summary>
    private bool DeleteDirectoryEntry(uint dirCluster, int entryIndex)
    {
        if (_device == null || _readOnly)
            return false;

        if (dirCluster == 0 && _fatType != FatType.Fat32)
        {
            // FAT12/16 root directory
            uint rootBytes = _rootEntryCount * 32;
            ulong pageCount = (rootBytes + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadRootDirectory(buffer, rootBytes))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[entryIndex].Name[0] = 0xE5; // Deleted marker

                ulong rootSector = _reservedSectors + (_numFats * _fatSizeSectors);
                int result = _device.Write(rootSector, _rootDirSectors, buffer);

                Memory.FreePages(bufferPhys, pageCount);
                return result == (int)_rootDirSectors;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
        else
        {
            // Normal directory
            uint entriesPerCluster = _bytesPerCluster / 32;
            uint clusterIndex = (uint)entryIndex / entriesPerCluster;
            uint offsetInCluster = (uint)entryIndex % entriesPerCluster;

            uint cluster = dirCluster;
            for (uint i = 0; i < clusterIndex && !FatCluster.IsEndOfChain(cluster, _fatType); i++)
            {
                cluster = GetFatEntry(cluster);
            }

            if (FatCluster.IsEndOfChain(cluster, _fatType))
                return false;

            ulong pageCount = (_bytesPerCluster + 4095) / 4096;
            ulong bufferPhys = Memory.AllocatePages(pageCount);
            if (bufferPhys == 0)
                return false;

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

            try
            {
                if (!ReadCluster(cluster, buffer))
                {
                    Memory.FreePages(bufferPhys, pageCount);
                    return false;
                }

                var dirEntry = (FatDirEntry*)buffer;
                dirEntry[offsetInCluster].Name[0] = 0xE5;

                bool success = WriteCluster(cluster, buffer);
                Memory.FreePages(bufferPhys, pageCount);
                return success;
            }
            catch
            {
                Memory.FreePages(bufferPhys, pageCount);
                return false;
            }
        }
    }

    #endregion
}
