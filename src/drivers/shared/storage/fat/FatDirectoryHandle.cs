// ProtonOS FAT Filesystem Driver - Directory Handle
// Implements IDirectoryHandle for FAT directory enumeration

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Fat;

/// <summary>
/// Directory handle for FAT filesystem enumeration.
/// </summary>
public unsafe class FatDirectoryHandle : IDirectoryHandle
{
    private readonly FatFileSystem _fs;
    private readonly uint _startCluster;
    private readonly string _basePath;

    private uint _currentCluster;
    private uint _entryIndex;           // Index within current cluster
    private bool _isOpen;
    private bool _isRootDir;            // FAT12/16 root directory special case

    // Root directory info for FAT12/16
    private uint _rootEntriesRead;

    public FatDirectoryHandle(FatFileSystem fs, uint cluster, string path)
    {
        _fs = fs;
        _startCluster = cluster;
        _basePath = path;
        _currentCluster = cluster;
        _entryIndex = 0;
        _isOpen = true;
        _rootEntriesRead = 0;

        // FAT12/16 root directory has cluster=0
        _isRootDir = (fs.FatVariant != FatType.Fat32 && cluster == 0);
    }

    public bool IsOpen => _isOpen;

    public FileInfo? ReadNext()
    {
        if (!_isOpen)
            return null;

        uint bytesPerCluster = _fs.BytesPerCluster;
        uint entriesPerCluster = bytesPerCluster / 32;

        // Allocate cluster buffer
        ulong pageCount = (_isRootDir ? (_fs.RootEntryCount * 32 + 4095) : (bytesPerCluster + 4095)) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return null;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        try
        {
            while (true)
            {
                if (_isRootDir)
                {
                    // FAT12/16 root directory
                    if (_rootEntriesRead >= _fs.RootEntryCount)
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;  // End of directory
                    }

                    // Read root directory
                    uint rootBytes = _fs.RootEntryCount * 32;
                    if (!_fs.ReadRootDirectory(buffer, rootBytes))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;
                    }

                    var dirEntry = (FatDirEntry*)buffer;

                    while (_rootEntriesRead < _fs.RootEntryCount)
                    {
                        var entry = dirEntry[_rootEntriesRead];
                        _rootEntriesRead++;

                        if (entry.Name[0] == 0)
                        {
                            // End of directory
                            Memory.FreePages(bufferPhys, pageCount);
                            return null;
                        }

                        if (entry.Name[0] == 0xE5)
                            continue;  // Deleted entry

                        if ((entry.Attr & (byte)FatAttr.LongName) == (byte)FatAttr.LongName)
                            continue;  // Skip LFN entries

                        if ((entry.Attr & (byte)FatAttr.VolumeId) != 0)
                            continue;  // Skip volume label

                        // Skip . and ..
                        if (entry.Name[0] == '.')
                            continue;

                        string name = FatFileSystem.GetShortName(&entry);
                        // Simple path combine to avoid VFS.Combine issues
                        string fullPath;
                        if (_basePath == "/" || _basePath.Length == 0)
                            fullPath = "/" + name;
                        else
                            fullPath = _basePath + "/" + name;

                        var info = new FileInfo();
                        info.Name = name;
                        info.Path = fullPath;
                        info.Type = (entry.Attr & (byte)FatAttr.Directory) != 0
                            ? FileEntryType.Directory
                            : FileEntryType.File;
                        info.Size = entry.FileSize;
                        info.Attributes = ConvertAttributes(entry.Attr);

                        Memory.FreePages(bufferPhys, pageCount);
                        return info;
                    }
                }
                else
                {
                    // Regular cluster-based directory
                    if (_currentCluster == 0 || FatCluster.IsEndOfChain(_currentCluster, _fs.FatVariant))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;  // End of directory
                    }

                    // Read current cluster
                    if (!_fs.ReadCluster(_currentCluster, buffer))
                    {
                        Memory.FreePages(bufferPhys, pageCount);
                        return null;
                    }

                    var dirEntry = (FatDirEntry*)buffer;

                    while (_entryIndex < entriesPerCluster)
                    {
                        var entry = dirEntry[_entryIndex];
                        _entryIndex++;

                        if (entry.Name[0] == 0)
                        {
                            // End of directory
                            Memory.FreePages(bufferPhys, pageCount);
                            return null;
                        }

                        if (entry.Name[0] == 0xE5)
                            continue;  // Deleted entry

                        if ((entry.Attr & (byte)FatAttr.LongName) == (byte)FatAttr.LongName)
                            continue;  // Skip LFN entries

                        if ((entry.Attr & (byte)FatAttr.VolumeId) != 0)
                            continue;  // Skip volume label

                        // Skip . and ..
                        if (entry.Name[0] == '.')
                            continue;

                        string name = FatFileSystem.GetShortName(&entry);

                        // Simple path combine to avoid VFS.Combine issues
                        string fullPath;
                        if (_basePath == "/" || _basePath.Length == 0)
                            fullPath = "/" + name;
                        else
                            fullPath = _basePath + "/" + name;

                        var info = new FileInfo();
                        info.Name = name;
                        info.Path = fullPath;
                        info.Type = (entry.Attr & (byte)FatAttr.Directory) != 0
                            ? FileEntryType.Directory
                            : FileEntryType.File;
                        info.Size = entry.FileSize;
                        info.Attributes = ConvertAttributes(entry.Attr);

                        Memory.FreePages(bufferPhys, pageCount);
                        return info;
                    }

                    // Move to next cluster
                    _currentCluster = _fs.GetFatEntry(_currentCluster);
                    _entryIndex = 0;
                }
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
        _currentCluster = _startCluster;
        _entryIndex = 0;
        _rootEntriesRead = 0;
    }

    public void Dispose()
    {
        _isOpen = false;
    }

    private static FileAttributes ConvertAttributes(byte attr)
    {
        FileAttributes result = FileAttributes.None;
        if ((attr & (byte)FatAttr.ReadOnly) != 0)
            result |= FileAttributes.ReadOnly;
        if ((attr & (byte)FatAttr.Hidden) != 0)
            result |= FileAttributes.Hidden;
        if ((attr & (byte)FatAttr.System) != 0)
            result |= FileAttributes.System;
        if ((attr & (byte)FatAttr.Archive) != 0)
            result |= FileAttributes.Archive;
        return result;
    }
}
