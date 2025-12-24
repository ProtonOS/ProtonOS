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

    public FileResult Mount(IBlockDevice device, bool readOnly = false)
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

        // TODO: Implement directory creation
        return FileResult.NotSupported;
    }

    public FileResult DeleteFile(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        // TODO: Implement file deletion
        return FileResult.NotSupported;
    }

    public FileResult DeleteDirectory(string path)
    {
        if (!_mounted || _device == null)
            return FileResult.IoError;
        if (_readOnly)
            return FileResult.ReadOnly;

        // TODO: Implement directory deletion
        return FileResult.NotSupported;
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
        // TODO: Implement file creation
        return FileResult.NotSupported;
    }

    /// <summary>
    /// Truncate a file to zero length.
    /// </summary>
    private void TruncateFile(ref FatDirEntry entry, uint parentCluster, int entryIndex)
    {
        // TODO: Free cluster chain and update entry
    }

    // Properties for internal use
    internal FatType FatVariant => _fatType;
    internal uint BytesPerCluster => _bytesPerCluster;
    internal uint SectorsPerCluster => _sectorsPerCluster;
    internal uint BytesPerSector => _bytesPerSector;
    internal uint RootEntryCount => _rootEntryCount;
    internal uint RootCluster => _rootCluster;
    internal IBlockDevice? Device => _device;

    #endregion
}
