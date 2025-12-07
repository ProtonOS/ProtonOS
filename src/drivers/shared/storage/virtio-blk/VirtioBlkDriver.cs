// ProtonOS Virtio Block Driver
// Implements IBlockDevice using virtio-blk protocol

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Storage;
using ProtonOS.DDK.Platform;
using ProtonOS.Drivers.Virtio;

namespace ProtonOS.Drivers.Storage.VirtioBlk;

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

    /// <summary>Cache flush command support.</summary>
    Flush = 1UL << 9,

    /// <summary>Device exports information on optimal I/O alignment.</summary>
    Topology = 1UL << 10,

    /// <summary>Device can toggle its cache between writeback and writethrough modes.</summary>
    ConfigWce = 1UL << 11,

    /// <summary>Device supports multiqueue.</summary>
    Mq = 1UL << 12,

    /// <summary>Device can support discard command.</summary>
    Discard = 1UL << 13,

    /// <summary>Device can support write zeroes command.</summary>
    WriteZeroes = 1UL << 14,

    /// <summary>Device supports providing storage lifetime information.</summary>
    Lifetime = 1UL << 15,

    /// <summary>Device can support the secure erase command.</summary>
    SecureErase = 1UL << 16,
}

/// <summary>
/// Virtio block request types.
/// </summary>
public enum VirtioBlkRequestType : uint
{
    In = 0,        // Read
    Out = 1,       // Write
    Flush = 4,     // Flush
    GetId = 8,     // Get device ID
    Discard = 11,  // Discard
    WriteZeroes = 13,
    SecureErase = 14,
}

/// <summary>
/// Virtio block request status.
/// </summary>
public enum VirtioBlkStatus : byte
{
    Ok = 0,
    IoError = 1,
    Unsupported = 2,
}

/// <summary>
/// Virtio block request header.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VirtioBlkRequestHeader
{
    public VirtioBlkRequestType Type;
    public uint Reserved;
    public ulong Sector;
}

/// <summary>
/// Virtio block device configuration.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VirtioBlkConfig
{
    public ulong Capacity;      // Number of 512-byte sectors
    public uint SizeMax;        // Max segment size
    public uint SegMax;         // Max segments per request
    public VirtioBlkGeometry Geometry;
    public uint BlkSize;        // Block size
    // More fields exist but these are the important ones
}

[StructLayout(LayoutKind.Sequential)]
public struct VirtioBlkGeometry
{
    public ushort Cylinders;
    public byte Heads;
    public byte Sectors;
}

/// <summary>
/// Virtio block driver implementation.
/// </summary>
public unsafe class VirtioBlkDriver : VirtioDevice, IBlockDevice, IPciDriver
{
    private const int RequestQueueIndex = 0;
    private const uint SectorSize = 512;

    // Device configuration
    private VirtioBlkConfig _config;
    private bool _readOnly;
    private bool _flushSupported;

    // Driver state
    private DriverState _state = DriverState.Loaded;
    private string _deviceName = "virtio-blk";

    // Request tracking
    private DMABuffer _requestBuffer;

    #region IDriver Implementation

    public string DriverName => "VirtIO Block Driver";
    public Version DriverVersion => new Version(1, 0, 0);
    public DriverType Type => DriverType.Storage;
    public DriverState State => _state;

    public bool Initialize()
    {
        _state = DriverState.Initializing;

        // Set up the request queue
        if (!SetupQueue(RequestQueueIndex))
        {
            _state = DriverState.Failed;
            return false;
        }

        // Allocate request buffer for headers/status
        _requestBuffer = DMA.Allocate(4096);
        if (!_requestBuffer.IsValid)
        {
            _state = DriverState.Failed;
            return false;
        }

        // Mark driver ready
        SetDriverOk();

        _state = DriverState.Running;
        return true;
    }

    public void Shutdown()
    {
        _state = DriverState.Stopping;
        DMA.Free(ref _requestBuffer);
        Dispose();
        _state = DriverState.Stopped;
    }

    public void Suspend() => _state = DriverState.Suspended;
    public void Resume() => _state = DriverState.Running;

    #endregion

    #region IPciDriver Implementation

    public bool Probe(PciDeviceInfo device)
    {
        // Check for virtio block device
        if (device.VendorId != VirtioPciIds.VendorId)
            return false;

        return device.DeviceId == VirtioPciIds.LegacyBlockDeviceId ||
               device.DeviceId == VirtioPciIds.BlockDeviceId;
    }

    public void Bind(PciDeviceInfo device)
    {
        // Initialize virtio device
        if (!base.Initialize(device))
        {
            _state = DriverState.Failed;
            return;
        }

        // Read device config
        ReadDeviceConfig();

        // Update device name based on capacity
        ulong sizeGB = (_config.Capacity * SectorSize) / (1024 * 1024 * 1024);
        _deviceName = $"virtio-blk ({sizeGB} GB)";
    }

    public void Unbind()
    {
        Shutdown();
    }

    #endregion

    #region IBlockDevice Implementation

    string IBlockDevice.DeviceName => _deviceName;

    public ulong BlockCount => _config.Capacity;

    public uint BlockSize => _config.BlkSize > 0 ? _config.BlkSize : SectorSize;

    public BlockDeviceCapabilities Capabilities
    {
        get
        {
            var caps = BlockDeviceCapabilities.Read;
            if (!_readOnly)
                caps |= BlockDeviceCapabilities.Write;
            if (_flushSupported)
                caps |= BlockDeviceCapabilities.Flush;
            return caps;
        }
    }

    public int Read(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (_state != DriverState.Running || buffer == null)
            return (int)BlockResult.NotReady;

        if (startBlock + blockCount > _config.Capacity)
            return (int)BlockResult.InvalidParameter;

        // For simplicity, process one block at a time
        // A production driver would batch requests
        for (uint i = 0; i < blockCount; i++)
        {
            var result = DoBlockRequest(VirtioBlkRequestType.In, startBlock + i, buffer + (i * BlockSize), BlockSize);
            if (result != BlockResult.Success)
                return (int)result;
        }

        return (int)blockCount;
    }

    public int Write(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (_state != DriverState.Running || buffer == null)
            return (int)BlockResult.NotReady;

        if (_readOnly)
            return (int)BlockResult.WriteProtected;

        if (startBlock + blockCount > _config.Capacity)
            return (int)BlockResult.InvalidParameter;

        for (uint i = 0; i < blockCount; i++)
        {
            var result = DoBlockRequest(VirtioBlkRequestType.Out, startBlock + i, buffer + (i * BlockSize), BlockSize);
            if (result != BlockResult.Success)
                return (int)result;
        }

        return (int)blockCount;
    }

    public BlockResult Flush()
    {
        if (_state != DriverState.Running)
            return BlockResult.NotReady;

        if (!_flushSupported)
            return BlockResult.Success; // No-op if not supported

        return DoBlockRequest(VirtioBlkRequestType.Flush, 0, null, 0);
    }

    public BlockResult Discard(ulong startBlock, uint blockCount)
    {
        // Not implemented yet
        return BlockResult.Success;
    }

    #endregion

    #region Protected Overrides

    protected override ulong DeviceFeatures =>
        (ulong)(VirtioBlkFeatures.SizeMax |
                VirtioBlkFeatures.SegMax |
                VirtioBlkFeatures.BlkSize |
                VirtioBlkFeatures.Flush |
                VirtioBlkFeatures.ReadOnly);

    #endregion

    #region Private Methods

    private void ReadDeviceConfig()
    {
        if (_deviceCfg == null)
            return;

        // Read capacity
        _config.Capacity = *(ulong*)_deviceCfg;

        // Read size_max if feature negotiated
        if (((ulong)Features & (ulong)VirtioBlkFeatures.SizeMax) != 0)
            _config.SizeMax = *(uint*)(_deviceCfg + 8);

        // Read seg_max if feature negotiated
        if (((ulong)Features & (ulong)VirtioBlkFeatures.SegMax) != 0)
            _config.SegMax = *(uint*)(_deviceCfg + 12);

        // Read blk_size if feature negotiated
        if (((ulong)Features & (ulong)VirtioBlkFeatures.BlkSize) != 0)
            _config.BlkSize = *(uint*)(_deviceCfg + 20);

        // Check features
        _readOnly = ((ulong)Features & (ulong)VirtioBlkFeatures.ReadOnly) != 0;
        _flushSupported = ((ulong)Features & (ulong)VirtioBlkFeatures.Flush) != 0;
    }

    /// <summary>
    /// Perform a block I/O request.
    /// </summary>
    private BlockResult DoBlockRequest(VirtioBlkRequestType type, ulong sector, byte* buffer, uint length)
    {
        var queue = GetQueue(RequestQueueIndex);
        if (queue == null)
            return BlockResult.NotReady;

        // Allocate DMA buffer for this request
        // Layout: [header (16 bytes)][data][status (1 byte)]
        uint totalSize = 16 + length + 1;
        var reqBuffer = DMA.Allocate(totalSize);
        if (!reqBuffer.IsValid)
            return BlockResult.IoError;

        try
        {
            // Set up header
            var header = (VirtioBlkRequestHeader*)reqBuffer.VirtualAddress;
            header->Type = type;
            header->Reserved = 0;
            header->Sector = sector;

            // Copy data for write
            byte* dataPtr = (byte*)reqBuffer.VirtualAddress + 16;
            if (type == VirtioBlkRequestType.Out && buffer != null)
            {
                for (uint i = 0; i < length; i++)
                    dataPtr[i] = buffer[i];
            }

            // Status byte at end
            byte* statusPtr = (byte*)reqBuffer.VirtualAddress + 16 + length;
            *statusPtr = 0xFF; // Invalid initially

            // Build descriptor chain
            // For read: header (device-readable), data (device-writable), status (device-writable)
            // For write: header (device-readable), data (device-readable), status (device-writable)

            int numDescs = (length > 0) ? 3 : 2;
            int head = queue.AllocateDescriptors(numDescs);
            if (head < 0)
            {
                DMA.Free(ref reqBuffer);
                return BlockResult.IoError;
            }

            int descIdx = head;

            // Header descriptor (always device-readable)
            queue.SetDescriptor(descIdx, reqBuffer.PhysicalAddress, 16,
                               VirtqDescFlags.Next, (ushort)(descIdx + 1));

            if (length > 0)
            {
                // Data descriptor
                descIdx++;
                VirtqDescFlags dataFlags = VirtqDescFlags.Next;
                if (type == VirtioBlkRequestType.In)
                    dataFlags |= VirtqDescFlags.Write; // Device writes to buffer

                queue.SetDescriptor(descIdx, reqBuffer.PhysicalAddress + 16, length,
                                   dataFlags, (ushort)(descIdx + 1));
            }

            // Status descriptor (always device-writable)
            descIdx++;
            queue.SetDescriptor(descIdx, reqBuffer.PhysicalAddress + 16 + length, 1,
                               VirtqDescFlags.Write, 0);

            // Store request buffer for cleanup
            queue.SetDescriptorData(head, reqBuffer.VirtualAddress);

            // Submit to available ring
            queue.SubmitAvailable(head);

            // Notify device
            NotifyQueue(RequestQueueIndex);

            // Wait for completion (polling)
            int timeout = 1000000; // Arbitrary timeout
            while (!queue.HasUsedBuffers() && timeout-- > 0)
            {
                // Spin wait
            }

            if (timeout <= 0)
            {
                queue.FreeDescriptors(head);
                DMA.Free(ref reqBuffer);
                return BlockResult.Timeout;
            }

            // Get result
            uint writtenLen;
            int usedHead = queue.PopUsed(out writtenLen);

            // Check status
            VirtioBlkStatus status = (VirtioBlkStatus)(*statusPtr);

            // Copy data for read
            if (type == VirtioBlkRequestType.In && buffer != null && status == VirtioBlkStatus.Ok)
            {
                for (uint i = 0; i < length; i++)
                    buffer[i] = dataPtr[i];
            }

            // Free descriptors
            queue.FreeDescriptors(head);
            DMA.Free(ref reqBuffer);

            return status == VirtioBlkStatus.Ok ? BlockResult.Success : BlockResult.IoError;
        }
        catch
        {
            DMA.Free(ref reqBuffer);
            return BlockResult.IoError;
        }
    }

    #endregion
}
