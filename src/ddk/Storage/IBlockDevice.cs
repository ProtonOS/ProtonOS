// ProtonOS DDK - Block Device Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Storage;

/// <summary>
/// Block device capabilities flags.
/// </summary>
[Flags]
public enum BlockDeviceCapabilities
{
    None = 0,

    /// <summary>Device supports read operations.</summary>
    Read = 1 << 0,

    /// <summary>Device supports write operations.</summary>
    Write = 1 << 1,

    /// <summary>Device supports flush/sync operations.</summary>
    Flush = 1 << 2,

    /// <summary>Device supports discard/trim operations.</summary>
    Discard = 1 << 3,

    /// <summary>Device is removable (USB, optical).</summary>
    Removable = 1 << 4,

    /// <summary>Device supports hot-plug events.</summary>
    HotPlug = 1 << 5,

    /// <summary>Device supports command queuing.</summary>
    QueuedCommands = 1 << 6,

    /// <summary>Read and write.</summary>
    ReadWrite = Read | Write,
}

/// <summary>
/// Result of a block I/O operation.
/// </summary>
public enum BlockResult
{
    /// <summary>Operation completed successfully.</summary>
    Success = 0,

    /// <summary>Invalid parameters (null buffer, bad block number).</summary>
    InvalidParameter = -1,

    /// <summary>Device not ready or not initialized.</summary>
    NotReady = -2,

    /// <summary>I/O error during operation.</summary>
    IoError = -3,

    /// <summary>Media has changed (removable device).</summary>
    MediaChanged = -4,

    /// <summary>No media present (removable device).</summary>
    NoMedia = -5,

    /// <summary>Write attempted on read-only device.</summary>
    WriteProtected = -6,

    /// <summary>Operation timed out.</summary>
    Timeout = -7,

    /// <summary>Device was removed during operation.</summary>
    DeviceRemoved = -8,
}

/// <summary>
/// Interface for block storage devices (NVMe, AHCI, virtio-blk, etc.).
/// </summary>
public unsafe interface IBlockDevice : IDriver
{
    /// <summary>
    /// Device name for identification.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Total number of blocks on the device.
    /// </summary>
    ulong BlockCount { get; }

    /// <summary>
    /// Size of each block in bytes (typically 512 or 4096).
    /// </summary>
    uint BlockSize { get; }

    /// <summary>
    /// Device capabilities.
    /// </summary>
    BlockDeviceCapabilities Capabilities { get; }

    /// <summary>
    /// True if the device is read-only.
    /// </summary>
    bool IsReadOnly => (Capabilities & BlockDeviceCapabilities.Write) == 0;

    /// <summary>
    /// Total device size in bytes.
    /// </summary>
    ulong TotalBytes => BlockCount * BlockSize;

    /// <summary>
    /// Read blocks from the device.
    /// </summary>
    /// <param name="startBlock">First block to read (0-indexed)</param>
    /// <param name="blockCount">Number of blocks to read</param>
    /// <param name="buffer">Buffer to receive data (must be at least blockCount * BlockSize bytes)</param>
    /// <returns>Number of blocks read, or negative BlockResult on error</returns>
    int Read(ulong startBlock, uint blockCount, byte* buffer);

    /// <summary>
    /// Write blocks to the device.
    /// </summary>
    /// <param name="startBlock">First block to write (0-indexed)</param>
    /// <param name="blockCount">Number of blocks to write</param>
    /// <param name="buffer">Buffer containing data to write</param>
    /// <returns>Number of blocks written, or negative BlockResult on error</returns>
    int Write(ulong startBlock, uint blockCount, byte* buffer);

    /// <summary>
    /// Flush any pending writes to the device.
    /// </summary>
    /// <returns>BlockResult indicating success or failure</returns>
    BlockResult Flush();

    /// <summary>
    /// Discard/trim blocks (SSD optimization).
    /// </summary>
    /// <param name="startBlock">First block to discard</param>
    /// <param name="blockCount">Number of blocks to discard</param>
    /// <returns>BlockResult indicating success or failure</returns>
    BlockResult Discard(ulong startBlock, uint blockCount) => BlockResult.Success;
}

/// <summary>
/// Interface for partitioned block devices.
/// </summary>
public interface IPartitionedDevice : IBlockDevice
{
    /// <summary>
    /// Number of partitions on the device.
    /// </summary>
    int PartitionCount { get; }

    /// <summary>
    /// Get information about a partition.
    /// </summary>
    PartitionInfo GetPartition(int index);
}

/// <summary>
/// Information about a partition.
/// </summary>
public class PartitionInfo
{
    /// <summary>Partition index (0-based).</summary>
    public int Index;

    /// <summary>Partition type GUID (GPT) or type byte (MBR).</summary>
    public Guid TypeGuid;

    /// <summary>Partition unique GUID (GPT only).</summary>
    public Guid UniqueGuid;

    /// <summary>Starting block of the partition.</summary>
    public ulong StartBlock;

    /// <summary>Number of blocks in the partition.</summary>
    public ulong BlockCount;

    /// <summary>Partition name (GPT only).</summary>
    public string? Name;

    /// <summary>Partition attributes/flags.</summary>
    public ulong Attributes;

    /// <summary>Is this the boot partition?</summary>
    public bool IsBootable => (Attributes & 0x01) != 0;

    /// <summary>Size in bytes.</summary>
    public ulong SizeBytes(uint blockSize) => BlockCount * blockSize;

    // Well-known partition type GUIDs
    public static readonly Guid EfiSystemPartition = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    public static readonly Guid MicrosoftBasicData = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    public static readonly Guid LinuxFilesystem = new("0FC63DAF-8483-4772-8E79-3D69D8477DE4");
    public static readonly Guid LinuxSwap = new("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F");
}
