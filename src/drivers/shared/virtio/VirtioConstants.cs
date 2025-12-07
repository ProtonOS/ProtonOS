// ProtonOS Virtio Driver - Constants and Definitions
// Based on VirtIO 1.2 specification

using System;

namespace ProtonOS.Drivers.Virtio;

/// <summary>
/// Virtio device status flags.
/// </summary>
[Flags]
public enum VirtioDeviceStatus : byte
{
    /// <summary>Reset (0 value).</summary>
    Reset = 0,

    /// <summary>Guest has found the device.</summary>
    Acknowledge = 1,

    /// <summary>Guest knows how to drive the device.</summary>
    Driver = 2,

    /// <summary>Feature negotiation complete.</summary>
    FeaturesOk = 8,

    /// <summary>Driver setup complete, device is usable.</summary>
    DriverOk = 4,

    /// <summary>Device has experienced an unrecoverable error.</summary>
    DeviceNeedsReset = 64,

    /// <summary>Something went wrong during initialization.</summary>
    Failed = 128,
}

/// <summary>
/// Common virtio feature bits (bits 24-37).
/// </summary>
[Flags]
public enum VirtioFeatures : ulong
{
    None = 0,

    /// <summary>Device supports indirect descriptors.</summary>
    RingIndirectDesc = 1UL << 28,

    /// <summary>Device supports VIRTQ_USED_F_NO_NOTIFY optimization.</summary>
    RingEventIdx = 1UL << 29,

    /// <summary>Virtio 1.0+ modern interface (required).</summary>
    Version1 = 1UL << 32,

    /// <summary>Device supports access platform.</summary>
    AccessPlatform = 1UL << 33,

    /// <summary>Device supports packed virtqueues.</summary>
    RingPacked = 1UL << 34,

    /// <summary>Device supports in-order use of descriptors.</summary>
    InOrder = 1UL << 35,

    /// <summary>Device supports order platform operations.</summary>
    OrderPlatform = 1UL << 36,

    /// <summary>Device supports single root I/O virtualization.</summary>
    SrIov = 1UL << 37,

    /// <summary>Device supports notification data.</summary>
    NotificationData = 1UL << 38,
}

/// <summary>
/// Virtio PCI vendor and device IDs.
/// </summary>
public static class VirtioPciIds
{
    /// <summary>Virtio vendor ID (Red Hat).</summary>
    public const ushort VendorId = 0x1AF4;

    // Legacy device IDs (transitional)
    public const ushort LegacyNetworkDeviceId = 0x1000;
    public const ushort LegacyBlockDeviceId = 0x1001;
    public const ushort LegacyConsoleDeviceId = 0x1003;
    public const ushort LegacyEntropyDeviceId = 0x1005;
    public const ushort LegacyBalloonDeviceId = 0x1002;
    public const ushort LegacyScsiDeviceId = 0x1004;
    public const ushort Legacy9pDeviceId = 0x1009;
    public const ushort LegacyGpuDeviceId = 0x1050;

    // Modern device IDs (1.0+)
    public const ushort NetworkDeviceId = 0x1041;
    public const ushort BlockDeviceId = 0x1042;
    public const ushort ConsoleDeviceId = 0x1043;
    public const ushort EntropyDeviceId = 0x1044;
    public const ushort BalloonDeviceId = 0x1045;
    public const ushort ScsiDeviceId = 0x1048;
    public const ushort GpuDeviceId = 0x1050;
    public const ushort InputDeviceId = 0x1052;
    public const ushort SocketDeviceId = 0x1053;

    /// <summary>
    /// Check if a device ID is a virtio device.
    /// </summary>
    public static bool IsVirtioDevice(ushort vendorId, ushort deviceId)
    {
        if (vendorId != VendorId)
            return false;

        // Legacy IDs: 0x1000-0x103F
        // Modern IDs: 0x1040-0x107F
        return deviceId >= 0x1000 && deviceId <= 0x107F;
    }

    /// <summary>
    /// Check if a device ID is a legacy (transitional) device.
    /// </summary>
    public static bool IsLegacyDevice(ushort deviceId)
    {
        return deviceId >= 0x1000 && deviceId <= 0x103F;
    }
}

/// <summary>
/// Virtio PCI capability types.
/// </summary>
public enum VirtioPciCapType : byte
{
    /// <summary>Common configuration.</summary>
    CommonCfg = 1,

    /// <summary>Notifications.</summary>
    NotifyCfg = 2,

    /// <summary>ISR status.</summary>
    IsrCfg = 3,

    /// <summary>Device-specific configuration.</summary>
    DeviceCfg = 4,

    /// <summary>PCI configuration access.</summary>
    PciCfg = 5,

    /// <summary>Shared memory region.</summary>
    SharedMemoryCfg = 8,

    /// <summary>Vendor-specific.</summary>
    VendorCfg = 9,
}

/// <summary>
/// PCI capability offsets for virtio.
/// </summary>
public static class VirtioPciCapOffsets
{
    public const int CapLen = 0;      // u8: Generic PCI cap
    public const int CapType = 3;     // u8: VirtioPciCapType
    public const int Bar = 4;         // u8: BAR index
    public const int Id = 5;          // u8: ID (multiple caps of same type)
    public const int Padding = 6;     // u16: padding
    public const int Offset = 8;      // u32: offset within BAR
    public const int Length = 12;     // u32: length of structure
}

/// <summary>
/// Virtio common configuration structure offsets.
/// </summary>
public static class VirtioCommonCfgOffsets
{
    public const int DeviceFeatureSelect = 0;   // u32
    public const int DeviceFeature = 4;         // u32
    public const int DriverFeatureSelect = 8;   // u32
    public const int DriverFeature = 12;        // u32
    public const int ConfigMsixVector = 16;     // u16
    public const int NumQueues = 18;            // u16
    public const int DeviceStatus = 20;         // u8
    public const int ConfigGeneration = 21;     // u8
    public const int QueueSelect = 22;          // u16
    public const int QueueSize = 24;            // u16
    public const int QueueMsixVector = 26;      // u16
    public const int QueueEnable = 28;          // u16
    public const int QueueNotifyOff = 30;       // u16
    public const int QueueDesc = 32;            // u64
    public const int QueueDriver = 40;          // u64
    public const int QueueDevice = 48;          // u64
    public const int QueueNotifyData = 56;      // u16
    public const int QueueReset = 58;           // u16
}

/// <summary>
/// Virtio block device feature bits.
/// </summary>
[Flags]
public enum VirtioBlkFeatures : ulong
{
    None = 0,

    /// <summary>Maximum size of any single segment is in size_max.</summary>
    SizeMax = 1UL << 1,

    /// <summary>Maximum number of segments in a request is in seg_max.</summary>
    SegMax = 1UL << 2,

    /// <summary>Disk-style geometry specified in geometry.</summary>
    Geometry = 1UL << 4,

    /// <summary>Device is read-only.</summary>
    ReadOnly = 1UL << 5,

    /// <summary>Block size of disk is in blk_size.</summary>
    BlkSize = 1UL << 6,

    /// <summary>Device supports flush command.</summary>
    Flush = 1UL << 9,

    /// <summary>Device exports optimal I/O topology.</summary>
    Topology = 1UL << 10,

    /// <summary>Device can toggle its cache between writeback and writethrough.</summary>
    ConfigWce = 1UL << 11,

    /// <summary>Device supports multiqueue.</summary>
    Mq = 1UL << 12,

    /// <summary>Device supports discard command.</summary>
    Discard = 1UL << 13,

    /// <summary>Device supports write zeroes command.</summary>
    WriteZeroes = 1UL << 14,
}

/// <summary>
/// Virtio block device configuration offsets.
/// </summary>
public static class VirtioBlkCfgOffsets
{
    public const int Capacity = 0;       // u64: number of 512-byte sectors
    public const int SizeMax = 8;        // u32: max segment size (if VIRTIO_BLK_F_SIZE_MAX)
    public const int SegMax = 12;        // u32: max segments (if VIRTIO_BLK_F_SEG_MAX)
    public const int Geometry = 16;      // geometry struct (if VIRTIO_BLK_F_GEOMETRY)
    public const int BlkSize = 20;       // u32: block size (if VIRTIO_BLK_F_BLK_SIZE)
}

/// <summary>
/// Virtio block request types.
/// </summary>
public enum VirtioBlkReqType : uint
{
    /// <summary>Read sectors.</summary>
    In = 0,

    /// <summary>Write sectors.</summary>
    Out = 1,

    /// <summary>Flush volatile buffers.</summary>
    Flush = 4,

    /// <summary>Get device ID.</summary>
    GetId = 8,

    /// <summary>Discard sectors.</summary>
    Discard = 11,

    /// <summary>Write zeroes.</summary>
    WriteZeroes = 13,
}

/// <summary>
/// Virtio block request status.
/// </summary>
public enum VirtioBlkStatus : byte
{
    /// <summary>Request completed successfully.</summary>
    Ok = 0,

    /// <summary>I/O error.</summary>
    IoErr = 1,

    /// <summary>Unsupported operation.</summary>
    Unsupp = 2,
}
