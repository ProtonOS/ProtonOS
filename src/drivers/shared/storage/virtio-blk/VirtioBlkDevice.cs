// ProtonOS VirtioBlk Driver - Block Device Implementation
// Implements IBlockDevice using virtio block device protocol.

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;
using ProtonOS.Drivers.Virtio;

namespace ProtonOS.Drivers.Storage.VirtioBlk;

/// <summary>
/// Virtio block request header.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct VirtioBlkReqHeader
{
    public VirtioBlkReqType Type;   // Request type
    public uint Reserved;            // Reserved (was ioprio)
    public ulong Sector;             // Starting sector for read/write
}

/// <summary>
/// Virtio block device implementing IBlockDevice.
/// </summary>
public unsafe class VirtioBlkDevice : VirtioDevice, IBlockDevice
{
    // Device configuration
    private ulong _capacity;      // In 512-byte sectors
    private uint _blkSize;        // Block size (default 512)
    private bool _readOnly;
    private bool _supportsFlush;

    // Request queue (queue 0)
    private const int RequestQueue = 0;

    // DMA buffers for request header and status
    private DMABuffer _headerBuffer;
    private DMABuffer _statusBuffer;

    // Device name
    private string _deviceName = "virtio-blk0";
    private static int _deviceCounter;
    private DriverState _state = DriverState.Loaded;

    #region IDriver Implementation

    public string DriverName => "virtio-blk";
    public Version DriverVersion => new Version(1, 0, 0);
    public DriverType Type => DriverType.Storage;
    public DriverState State => _state;

    public bool Initialize()
    {
        _state = DriverState.Initializing;

        // Allocate DMA buffers for request header and status
        _headerBuffer = DMA.Allocate(64);  // Room for header
        _statusBuffer = DMA.Allocate(4);   // Status byte + padding

        if (!_headerBuffer.IsValid || !_statusBuffer.IsValid)
        {
            _state = DriverState.Failed;
            return false;
        }

        _state = DriverState.Running;
        return true;
    }

    public void Shutdown()
    {
        _state = DriverState.Stopping;
        DMA.Free(ref _headerBuffer);
        DMA.Free(ref _statusBuffer);
        _state = DriverState.Stopped;
    }

    public void Suspend() { }
    public void Resume() { }

    #endregion

    #region IBlockDevice Implementation

    public string DeviceName => _deviceName;

    public ulong BlockCount => _capacity;

    public uint BlockSize => _blkSize;

    public BlockDeviceCapabilities Capabilities
    {
        get
        {
            var caps = BlockDeviceCapabilities.Read;
            if (!_readOnly)
                caps |= BlockDeviceCapabilities.Write;
            if (_supportsFlush)
                caps |= BlockDeviceCapabilities.Flush;
            return caps;
        }
    }

    public int Read(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (buffer == null || blockCount == 0)
            return (int)BlockResult.InvalidParameter;

        if (startBlock + blockCount > _capacity)
            return (int)BlockResult.InvalidParameter;

        if (!_initialized)
            return (int)BlockResult.NotReady;

        // Get request queue
        var queue = GetQueue(RequestQueue);
        if (queue == null)
            return (int)BlockResult.NotReady;

        // Calculate total bytes
        uint totalBytes = blockCount * _blkSize;

        // Allocate DMA buffer for data
        var dataBuffer = DMA.Allocate(totalBytes);
        if (!dataBuffer.IsValid)
            return (int)BlockResult.IoError;

        try
        {
            // Setup request header
            var header = (VirtioBlkReqHeader*)_headerBuffer.VirtualAddress;
            header->Type = VirtioBlkReqType.In;
            header->Reserved = 0;
            header->Sector = startBlock;

            // Clear status
            *_statusBuffer.AsBytes = 0xFF;

            // Allocate 3 descriptors: header (device-read), data (device-write), status (device-write)
            int descHead = queue.AllocateDescriptors(3);
            if (descHead < 0)
            {
                DMA.Free(ref dataBuffer);
                return (int)BlockResult.IoError;
            }

            // Set up descriptor chain
            // Descriptor 0: Header (device reads)
            queue.SetDescriptor(descHead, _headerBuffer.PhysicalAddress, 16,
                VirtqDescFlags.Next, (ushort)(descHead + 1));

            // Descriptor 1: Data buffer (device writes)
            queue.SetDescriptor(descHead + 1, dataBuffer.PhysicalAddress, totalBytes,
                VirtqDescFlags.Write | VirtqDescFlags.Next, (ushort)(descHead + 2));

            // Descriptor 2: Status (device writes)
            queue.SetDescriptor(descHead + 2, _statusBuffer.PhysicalAddress, 1,
                VirtqDescFlags.Write, 0);

            // Submit to available ring
            queue.SubmitAvailable(descHead);

            // Notify device
            NotifyQueue(RequestQueue);

            // Poll for completion
            int timeout = 10000000;
            while (!queue.HasUsedBuffers() && --timeout > 0)
            {
                // Busy wait
            }

            if (timeout <= 0)
            {
                queue.FreeDescriptors(descHead);
                DMA.Free(ref dataBuffer);
                return (int)BlockResult.Timeout;
            }

            // Get completion
            uint len;
            int usedDesc = queue.PopUsed(out len);
            queue.FreeDescriptors(usedDesc);

            // Check status
            byte status = *_statusBuffer.AsBytes;
            if (status != (byte)VirtioBlkStatus.Ok)
            {
                DMA.Free(ref dataBuffer);
                return (int)BlockResult.IoError;
            }

            // Copy data to user buffer
            DMA.CopyFrom(dataBuffer, buffer, totalBytes);
            DMA.Free(ref dataBuffer);

            return (int)blockCount;
        }
        catch
        {
            DMA.Free(ref dataBuffer);
            return (int)BlockResult.IoError;
        }
    }

    public int Write(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (buffer == null || blockCount == 0)
            return (int)BlockResult.InvalidParameter;

        if (startBlock + blockCount > _capacity)
            return (int)BlockResult.InvalidParameter;

        if (!_initialized)
            return (int)BlockResult.NotReady;

        if (_readOnly)
            return (int)BlockResult.WriteProtected;

        // Get request queue
        var queue = GetQueue(RequestQueue);
        if (queue == null)
            return (int)BlockResult.NotReady;

        // Calculate total bytes
        uint totalBytes = blockCount * _blkSize;

        // Allocate DMA buffer for data
        var dataBuffer = DMA.Allocate(totalBytes);
        if (!dataBuffer.IsValid)
            return (int)BlockResult.IoError;

        try
        {
            // Copy data to DMA buffer
            DMA.CopyTo(dataBuffer, buffer, totalBytes);

            // Setup request header
            var header = (VirtioBlkReqHeader*)_headerBuffer.VirtualAddress;
            header->Type = VirtioBlkReqType.Out;
            header->Reserved = 0;
            header->Sector = startBlock;

            // Clear status
            *_statusBuffer.AsBytes = 0xFF;

            // Allocate 3 descriptors: header (device-read), data (device-read), status (device-write)
            int descHead = queue.AllocateDescriptors(3);
            if (descHead < 0)
            {
                DMA.Free(ref dataBuffer);
                return (int)BlockResult.IoError;
            }

            // Set up descriptor chain
            // Descriptor 0: Header (device reads)
            queue.SetDescriptor(descHead, _headerBuffer.PhysicalAddress, 16,
                VirtqDescFlags.Next, (ushort)(descHead + 1));

            // Descriptor 1: Data buffer (device reads for write)
            queue.SetDescriptor(descHead + 1, dataBuffer.PhysicalAddress, totalBytes,
                VirtqDescFlags.Next, (ushort)(descHead + 2));

            // Descriptor 2: Status (device writes)
            queue.SetDescriptor(descHead + 2, _statusBuffer.PhysicalAddress, 1,
                VirtqDescFlags.Write, 0);

            // Submit to available ring
            queue.SubmitAvailable(descHead);

            // Notify device
            NotifyQueue(RequestQueue);

            // Poll for completion
            int timeout = 10000000;
            while (!queue.HasUsedBuffers() && --timeout > 0)
            {
                // Busy wait
            }

            if (timeout <= 0)
            {
                queue.FreeDescriptors(descHead);
                DMA.Free(ref dataBuffer);
                return (int)BlockResult.Timeout;
            }

            // Get completion
            uint len;
            int usedDesc = queue.PopUsed(out len);
            queue.FreeDescriptors(usedDesc);

            // Check status
            byte status = *_statusBuffer.AsBytes;
            DMA.Free(ref dataBuffer);

            if (status != (byte)VirtioBlkStatus.Ok)
                return (int)BlockResult.IoError;

            return (int)blockCount;
        }
        catch
        {
            DMA.Free(ref dataBuffer);
            return (int)BlockResult.IoError;
        }
    }

    public BlockResult Flush()
    {
        if (!_initialized)
            return BlockResult.NotReady;

        if (!_supportsFlush)
            return BlockResult.Success; // No-op if not supported

        var queue = GetQueue(RequestQueue);
        if (queue == null)
            return BlockResult.NotReady;

        // Setup flush request
        var header = (VirtioBlkReqHeader*)_headerBuffer.VirtualAddress;
        header->Type = VirtioBlkReqType.Flush;
        header->Reserved = 0;
        header->Sector = 0;

        *_statusBuffer.AsBytes = 0xFF;

        // Allocate 2 descriptors: header and status
        int descHead = queue.AllocateDescriptors(2);
        if (descHead < 0)
            return BlockResult.IoError;

        queue.SetDescriptor(descHead, _headerBuffer.PhysicalAddress, 16,
            VirtqDescFlags.Next, (ushort)(descHead + 1));
        queue.SetDescriptor(descHead + 1, _statusBuffer.PhysicalAddress, 1,
            VirtqDescFlags.Write, 0);

        queue.SubmitAvailable(descHead);
        NotifyQueue(RequestQueue);

        // Poll for completion
        int timeout = 10000000;
        while (!queue.HasUsedBuffers() && --timeout > 0) { }

        if (timeout <= 0)
        {
            queue.FreeDescriptors(descHead);
            return BlockResult.Timeout;
        }

        uint len;
        int usedDesc = queue.PopUsed(out len);
        queue.FreeDescriptors(usedDesc);

        return *_statusBuffer.AsBytes == (byte)VirtioBlkStatus.Ok
            ? BlockResult.Success
            : BlockResult.IoError;
    }

    #endregion

    #region VirtioDevice Implementation

    protected override ulong DeviceFeatures =>
        (ulong)(VirtioBlkFeatures.BlkSize |
                VirtioBlkFeatures.Flush |
                VirtioBlkFeatures.SizeMax |
                VirtioBlkFeatures.SegMax);

    /// <summary>
    /// Complete device initialization after VirtioDevice.Initialize().
    /// </summary>
    public bool InitializeBlockDevice()
    {
        if (_deviceCfg == null)
            return false;

        // Read capacity (in 512-byte sectors)
        _capacity = *(ulong*)(_deviceCfg + VirtioBlkCfgOffsets.Capacity);

        // Read block size if supported
        if (((ulong)_features & (ulong)VirtioBlkFeatures.BlkSize) != 0)
        {
            _blkSize = *(uint*)(_deviceCfg + VirtioBlkCfgOffsets.BlkSize);
        }
        else
        {
            _blkSize = 512; // Default sector size
        }

        // Check if read-only
        _readOnly = ((ulong)_features & (ulong)VirtioBlkFeatures.ReadOnly) != 0;

        // Check flush support
        _supportsFlush = ((ulong)_features & (ulong)VirtioBlkFeatures.Flush) != 0;

        // Set up request queue
        if (!SetupQueue(RequestQueue))
            return false;

        // Initialize driver resources
        if (!Initialize())
            return false;

        // Mark device ready
        SetDriverOk();

        // Assign device name
        _deviceName = "virtio-blk" + _deviceCounter++;

        return true;
    }

    public override void Dispose()
    {
        Shutdown();
        base.Dispose();
    }

    #endregion
}
