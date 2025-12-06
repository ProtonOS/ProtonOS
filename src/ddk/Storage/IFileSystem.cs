// ProtonOS DDK - Filesystem Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Storage;

/// <summary>
/// Filesystem capabilities flags.
/// </summary>
[Flags]
public enum FilesystemCapabilities
{
    None = 0,

    /// <summary>Filesystem supports read operations.</summary>
    Read = 1 << 0,

    /// <summary>Filesystem supports write operations.</summary>
    Write = 1 << 1,

    /// <summary>Filesystem supports symbolic links.</summary>
    SymLinks = 1 << 2,

    /// <summary>Filesystem supports hard links.</summary>
    HardLinks = 1 << 3,

    /// <summary>Filesystem is case-sensitive.</summary>
    CaseSensitive = 1 << 4,

    /// <summary>Filesystem preserves case but is case-insensitive.</summary>
    CasePreserving = 1 << 5,

    /// <summary>Filesystem supports extended attributes.</summary>
    ExtendedAttributes = 1 << 6,

    /// <summary>Filesystem supports access control lists.</summary>
    ACLs = 1 << 7,

    /// <summary>Filesystem supports timestamps.</summary>
    Timestamps = 1 << 8,

    /// <summary>Filesystem supports sparse files.</summary>
    SparseFiles = 1 << 9,

    /// <summary>Filesystem supports compression.</summary>
    Compression = 1 << 10,

    /// <summary>Read and write.</summary>
    ReadWrite = Read | Write,
}

/// <summary>
/// File/directory entry type.
/// </summary>
public enum FileEntryType
{
    Unknown = 0,
    File,
    Directory,
    SymLink,
    BlockDevice,
    CharDevice,
    Fifo,
    Socket,
}

/// <summary>
/// File/directory attributes.
/// </summary>
[Flags]
public enum FileAttributes
{
    None = 0,
    ReadOnly = 1 << 0,
    Hidden = 1 << 1,
    System = 1 << 2,
    Archive = 1 << 3,
}

/// <summary>
/// File access mode for open operations.
/// </summary>
[Flags]
public enum FileAccess
{
    Read = 1 << 0,
    Write = 1 << 1,
    ReadWrite = Read | Write,
}

/// <summary>
/// File open mode.
/// </summary>
public enum FileMode
{
    /// <summary>Open existing file. Fails if doesn't exist.</summary>
    Open,

    /// <summary>Create new file. Fails if exists.</summary>
    CreateNew,

    /// <summary>Create or open existing file.</summary>
    OpenOrCreate,

    /// <summary>Create or truncate existing file.</summary>
    Create,

    /// <summary>Open existing file and truncate. Fails if doesn't exist.</summary>
    Truncate,

    /// <summary>Open existing file and seek to end for appending.</summary>
    Append,
}

/// <summary>
/// Result of filesystem operations.
/// </summary>
public enum FileResult
{
    Success = 0,
    NotFound = -1,
    AlreadyExists = -2,
    AccessDenied = -3,
    InvalidPath = -4,
    NotEmpty = -5,
    NoSpace = -6,
    IoError = -7,
    ReadOnly = -8,
    NotADirectory = -9,
    IsADirectory = -10,
    TooManyOpenFiles = -11,
    NameTooLong = -12,
    InvalidHandle = -13,
    NotSupported = -14,
}

/// <summary>
/// File or directory information.
/// </summary>
public class FileInfo
{
    /// <summary>Entry name (without path).</summary>
    public string Name = "";

    /// <summary>Full path.</summary>
    public string Path = "";

    /// <summary>Entry type.</summary>
    public FileEntryType Type;

    /// <summary>File size in bytes (0 for directories).</summary>
    public ulong Size;

    /// <summary>Entry attributes.</summary>
    public FileAttributes Attributes;

    /// <summary>Creation time (UTC ticks).</summary>
    public long CreationTime;

    /// <summary>Last modification time (UTC ticks).</summary>
    public long ModificationTime;

    /// <summary>Last access time (UTC ticks).</summary>
    public long AccessTime;

    /// <summary>True if this is a directory.</summary>
    public bool IsDirectory => Type == FileEntryType.Directory;

    /// <summary>True if this is a regular file.</summary>
    public bool IsFile => Type == FileEntryType.File;
}

/// <summary>
/// Handle to an open file.
/// </summary>
public interface IFileHandle : IDisposable
{
    /// <summary>True if the file is open.</summary>
    bool IsOpen { get; }

    /// <summary>Current read/write position.</summary>
    long Position { get; set; }

    /// <summary>File size in bytes.</summary>
    long Length { get; }

    /// <summary>Access mode.</summary>
    FileAccess Access { get; }

    /// <summary>
    /// Read data from the file at current position.
    /// </summary>
    /// <param name="buffer">Buffer to receive data</param>
    /// <param name="count">Maximum bytes to read</param>
    /// <returns>Bytes read, or negative FileResult on error</returns>
    unsafe int Read(byte* buffer, int count);

    /// <summary>
    /// Write data to the file at current position.
    /// </summary>
    /// <param name="buffer">Buffer containing data to write</param>
    /// <param name="count">Bytes to write</param>
    /// <returns>Bytes written, or negative FileResult on error</returns>
    unsafe int Write(byte* buffer, int count);

    /// <summary>
    /// Flush pending writes to storage.
    /// </summary>
    FileResult Flush();

    /// <summary>
    /// Set file length (truncate or extend).
    /// </summary>
    FileResult SetLength(long length);
}

/// <summary>
/// Handle to an open directory for enumeration.
/// </summary>
public interface IDirectoryHandle : IDisposable
{
    /// <summary>True if the directory is open.</summary>
    bool IsOpen { get; }

    /// <summary>
    /// Read the next entry in the directory.
    /// </summary>
    /// <returns>FileInfo or null if no more entries</returns>
    FileInfo? ReadNext();

    /// <summary>
    /// Reset enumeration to the beginning.
    /// </summary>
    void Rewind();
}

/// <summary>
/// Interface for filesystem drivers (FAT32, ext4, etc.).
/// </summary>
public interface IFileSystem : IDriver
{
    /// <summary>
    /// Filesystem name for identification (e.g., "FAT32", "ext4").
    /// </summary>
    string FilesystemName { get; }

    /// <summary>
    /// Filesystem capabilities.
    /// </summary>
    FilesystemCapabilities Capabilities { get; }

    /// <summary>
    /// True if filesystem is mounted.
    /// </summary>
    bool IsMounted { get; }

    /// <summary>
    /// Volume label (if any).
    /// </summary>
    string? VolumeLabel { get; }

    /// <summary>
    /// Total filesystem size in bytes.
    /// </summary>
    ulong TotalBytes { get; }

    /// <summary>
    /// Free space in bytes.
    /// </summary>
    ulong FreeBytes { get; }

    /// <summary>
    /// Mount the filesystem on a block device.
    /// </summary>
    /// <param name="device">Block device containing the filesystem</param>
    /// <param name="readOnly">Mount as read-only</param>
    /// <returns>FileResult indicating success or failure</returns>
    FileResult Mount(IBlockDevice device, bool readOnly = false);

    /// <summary>
    /// Unmount the filesystem.
    /// </summary>
    FileResult Unmount();

    /// <summary>
    /// Check if a filesystem signature is present on a block device.
    /// </summary>
    bool Probe(IBlockDevice device);

    /// <summary>
    /// Get information about a file or directory.
    /// </summary>
    FileResult GetInfo(string path, out FileInfo? info);

    /// <summary>
    /// Open a file.
    /// </summary>
    FileResult OpenFile(string path, FileMode mode, FileAccess access, out IFileHandle? handle);

    /// <summary>
    /// Open a directory for enumeration.
    /// </summary>
    FileResult OpenDirectory(string path, out IDirectoryHandle? handle);

    /// <summary>
    /// Create a directory.
    /// </summary>
    FileResult CreateDirectory(string path);

    /// <summary>
    /// Delete a file.
    /// </summary>
    FileResult DeleteFile(string path);

    /// <summary>
    /// Delete an empty directory.
    /// </summary>
    FileResult DeleteDirectory(string path);

    /// <summary>
    /// Rename/move a file or directory.
    /// </summary>
    FileResult Rename(string oldPath, string newPath);

    /// <summary>
    /// Check if a path exists.
    /// </summary>
    bool Exists(string path);
}
