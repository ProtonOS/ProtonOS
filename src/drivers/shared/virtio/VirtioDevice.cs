// ProtonOS Virtio Driver - Base Device Implementation
// Common PCI virtio device initialization and management

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Kernel;
// Debug class provides: Debug.WriteLine(format, args...)

namespace ProtonOS.Drivers.Virtio;

/// <summary>
/// Base class for virtio PCI devices.
/// Handles device initialization, feature negotiation, and queue setup.
/// </summary>
public unsafe class VirtioDevice : IDisposable
{
    // PCI device info
    protected PciDeviceInfo _pciDevice;

    // Common config structure (modern virtio)
    protected byte* _commonCfg;
    protected uint _commonCfgBar;
    protected uint _commonCfgOffset;
    protected uint _commonCfgLength;

    // Notification structure
    protected byte* _notifyCfg;
    protected uint _notifyBar;
    protected uint _notifyOffset;
    protected uint _notifyMultiplier;

    // ISR config
    protected byte* _isrCfg;

    // Device-specific config
    protected byte* _deviceCfg;
    protected uint _deviceCfgLength;

    // BAR mappings
    protected ulong[] _barVirtAddr = new ulong[6];
    protected ulong[] _barPhysAddr = new ulong[6];
    protected ulong[] _barSize = new ulong[6];

    // Negotiated features
    protected VirtioFeatures _features;

    // Virtqueues
    protected Virtqueue[] _queues;
    protected ushort _numQueues;

    // Device status
    protected bool _initialized;
    protected bool _isLegacy;

    /// <summary>
    /// Negotiated features.
    /// </summary>
    public VirtioFeatures Features => _features;

    /// <summary>
    /// Number of queues.
    /// </summary>
    public int QueueCount => _numQueues;

    /// <summary>
    /// True if device is in legacy mode.
    /// </summary>
    public bool IsLegacy => _isLegacy;

    /// <summary>
    /// Device-specific feature bits (set by subclass before Initialize).
    /// </summary>
    protected ulong _wantedFeatures;

    /// <summary>
    /// Initialize the virtio device from PCI info.
    /// </summary>
    public bool Initialize(PciDeviceInfo pciDevice)
    {
        _pciDevice = pciDevice;

        // Determine if legacy or modern
        ushort deviceId = pciDevice.DeviceId;
        Debug.WriteLine("[virtio] Initializing device");
        _isLegacy = VirtioPciIds.IsLegacyDevice(deviceId);

        bool result = _isLegacy ? InitializeLegacy() : InitializeModern();
        return result;
    }

    /// <summary>
    /// Initialize modern virtio device (1.0+).
    /// </summary>
    private bool InitializeModern()
    {
        if (_pciDevice == null)
            return false;

        // Enable memory space and bus mastering
        byte bus = _pciDevice.Address.Bus;
        byte device = _pciDevice.Address.Device;
        byte function = _pciDevice.Address.Function;

        ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
        cmd |= PCI.PCI_CMD_MEMORY_SPACE;
        PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);

        PCI.EnableBusMaster(_pciDevice.Address);

        // Map BARs
        if (!MapBars())
        {
            Debug.WriteLine("[virtio] MapBars failed");
            return false;
        }

        // Find virtio capabilities
        if (!FindCapabilities())
        {
            Debug.WriteLine("[virtio] FindCapabilities failed");
            return false;
        }

        // Reset device
        WriteStatus(VirtioDeviceStatus.Reset);

        // Acknowledge device
        WriteStatus(VirtioDeviceStatus.Acknowledge);

        // We know how to drive it
        WriteStatus(ReadStatus() | VirtioDeviceStatus.Driver);

        // Read and negotiate features
        if (!NegotiateFeatures())
        {
            Debug.WriteLine("[virtio] NegotiateFeatures failed");
            return false;
        }

        // Features OK
        WriteStatus(ReadStatus() | VirtioDeviceStatus.FeaturesOk);

        // Check if features were accepted
        if ((ReadStatus() & VirtioDeviceStatus.FeaturesOk) == 0)
        {
            WriteStatus(VirtioDeviceStatus.Failed);
            Debug.WriteLine("[virtio] FeaturesOk not accepted");
            return false;
        }

        // Get number of queues
        _numQueues = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.NumQueues);

        // Allocate queue array
        _queues = new Virtqueue[_numQueues];

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Initialize legacy virtio device.
    /// </summary>
    private bool InitializeLegacy()
    {
        // For legacy devices, config is in BAR0 (I/O port based typically)
        // This is more complex - implement if needed for older QEMU
        // Most modern QEMU uses transitional devices which support modern interface
        return false; // Not implemented
    }

    /// <summary>
    /// Map PCI BARs to virtual memory.
    /// </summary>
    private bool MapBars()
    {
        if (_pciDevice == null)
            return false;

        var bars = _pciDevice.Bars;
        if (bars == null)
            return false;

        int mappedCount = 0;
        for (int i = 0; i < 6; i++)
        {
            PciBar bar = bars[i];
            ulong baseAddr = bar.BaseAddress;
            ulong size = bar.Size;
            bool isValid = bar.IsValid;
            bool isIO = bar.IsIO;

            if (isValid && !isIO)
            {
                _barPhysAddr[i] = baseAddr;
                _barSize[i] = size;

                _barVirtAddr[i] = Memory.MapMMIO(_barPhysAddr[i], _barSize[i]);

                if (_barVirtAddr[i] == 0)
                    return false;

                mappedCount++;
            }
        }

        return true;
    }

    /// <summary>
    /// Find virtio PCI capabilities.
    /// </summary>
    private bool FindCapabilities()
    {
        // Extract address components for convenience
        var addr = _pciDevice.Address;
        byte bus = addr.Bus;
        byte device = addr.Device;
        byte function = addr.Function;

        // Find first vendor-specific capability
        byte capPtr = PCI.FindCapability(addr, 0x09);

        // Extract BAR addresses array for convenience
        var barAddrs = _barVirtAddr;

        // Iterate through all PCI capabilities
        while (capPtr != 0)
        {
            // Read capability structure
            byte capLen = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 2));

            if (capLen < 16)
            {
                // Skip - capability too short
                capPtr = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 1));
                continue;
            }

            byte cfgType = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 3));
            byte bar = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 4));
            uint offset = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 8));
            uint length = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 12));

            // Process capability by type
            ProcessCapability(cfgType, bar, offset, length, capPtr, bus, device, function, barAddrs);

            // Get next capability pointer
            capPtr = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 1));
        }

        // Common config is required
        if (_commonCfg != null)
        {
            return true;
        }

        Debug.WriteLine("[virtio] FindCapabilities failed");
        return false;
    }

    /// <summary>
    /// Process a single virtio capability.
    /// </summary>
    private void ProcessCapability(byte cfgType, byte bar, uint offset, uint length,
                                   byte capPtr, byte bus, byte device, byte function, ulong[] barAddrs)
    {
        // Validate BAR
        if (bar >= 6)
        {
            return;
        }

        // Use direct field access instead of parameter to avoid JIT issue
        ulong barAddr = _barVirtAddr[bar];

        if (barAddr == 0)
        {
            return;
        }

        // Handle capability by type
        switch (cfgType)
        {
            case 1: // CommonCfg
                {
                    ulong virtAddr = barAddr + offset;
                    _commonCfg = (byte*)(nint)(long)virtAddr;
                    _commonCfgBar = bar;
                    _commonCfgOffset = offset;
                    _commonCfgLength = length;
                }
                break;

            case 2: // NotifyCfg
                {
                    ulong notifyAddr = barAddr + offset;
                    _notifyCfg = (byte*)(nint)(long)notifyAddr;
                    _notifyBar = bar;
                    _notifyOffset = offset;
                    _notifyMultiplier = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 16));
                }
                break;

            case 3: // IsrCfg
                {
                    ulong isrAddr = barAddr + offset;
                    _isrCfg = (byte*)(nint)(long)isrAddr;
                }
                break;

            case 4: // DeviceCfg
                {
                    ulong devAddr = barAddr + offset;
                    _deviceCfg = (byte*)(nint)(long)devAddr;
                    _deviceCfgLength = length;
                }
                break;
        }
    }

    /// <summary>
    /// Negotiate features with device.
    /// </summary>
    private bool NegotiateFeatures()
    {
        // Read device features
        ulong deviceFeatures = 0;

        // Low 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeatureSelect) = 0;
        deviceFeatures = *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeature);
        // Debug.WriteHex(0xFEA00004u); // After DeviceFeature read

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeatureSelect) = 1;
        deviceFeatures |= (ulong)*(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeature) << 32;
        // Debug.WriteHex(0xFEA00005u); // After reading both feature halves

        // Calculate features we want
        // Debug.WriteHex(0xFEA00006u); // Before reading _wantedFeatures
        ulong wantFeatures = _wantedFeatures;
        // Debug.WriteHex(0xFEA00007u); // After reading _wantedFeatures

        // Always want VERSION_1 for modern devices
        wantFeatures |= (ulong)VirtioFeatures.Version1;

        // Negotiate: only features both support
        _features = (VirtioFeatures)(deviceFeatures & wantFeatures);
        // Debug.WriteHex(0xFEA00008u); // After feature negotiation

        // Write driver features
        // Low 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 0;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)_features;
        // Debug.WriteHex(0xFEA00009u); // After writing low features

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 1;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)((ulong)_features >> 32);
        // Debug.WriteHex(0xFEA0000Au); // After writing high features

        return true;
    }

    /// <summary>
    /// Set up a virtqueue.
    /// </summary>
    protected bool SetupQueue(ushort queueIndex)
    {
        if (queueIndex >= _numQueues)
            return false;

        // Select queue
        *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueSelect) = queueIndex;

        // Get queue size
        ushort queueSize = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueSize);
        if (queueSize == 0)
            return false;

        // Create virtqueue
        var queue = new Virtqueue(queueIndex, queueSize);
        _queues[queueIndex] = queue;

        // Set queue addresses
        *(ulong*)(_commonCfg + VirtioCommonCfgOffsets.QueueDesc) = queue.DescPhysAddr;
        *(ulong*)(_commonCfg + VirtioCommonCfgOffsets.QueueDriver) = queue.AvailPhysAddr;
        *(ulong*)(_commonCfg + VirtioCommonCfgOffsets.QueueDevice) = queue.UsedPhysAddr;

        // Enable queue
        *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueEnable) = 1;

        return true;
    }

    /// <summary>
    /// Notify the device about available buffers.
    /// </summary>
    protected void NotifyQueue(ushort queueIndex)
    {
        if (queueIndex >= _numQueues || _queues[queueIndex] == null)
            return;

        // Calculate notify address
        *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueSelect) = queueIndex;
        ushort notifyOff = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueNotifyOff);

        ushort* notifyAddr = (ushort*)(_notifyCfg + notifyOff * _notifyMultiplier);
        *notifyAddr = queueIndex;
    }

    /// <summary>
    /// Mark device as ready.
    /// </summary>
    protected void SetDriverOk()
    {
        WriteStatus(ReadStatus() | VirtioDeviceStatus.DriverOk);
    }

    /// <summary>
    /// Read device status.
    /// </summary>
    protected VirtioDeviceStatus ReadStatus()
    {
        byte* ptr = _commonCfg + VirtioCommonCfgOffsets.DeviceStatus;
        return (VirtioDeviceStatus)(*ptr);
    }

    /// <summary>
    /// Write device status.
    /// </summary>
    protected void WriteStatus(VirtioDeviceStatus status)
    {
        *(_commonCfg + VirtioCommonCfgOffsets.DeviceStatus) = (byte)status;
    }

    /// <summary>
    /// Read ISR status (clears interrupt).
    /// </summary>
    protected byte ReadIsr()
    {
        return _isrCfg != null ? *_isrCfg : (byte)0;
    }

    /// <summary>
    /// Get a virtqueue.
    /// </summary>
    protected Virtqueue GetQueue(int index)
    {
        if (index >= 0 && index < _numQueues)
            return _queues[index];
        return null;
    }

    public virtual void Dispose()
    {
        if (_queues != null)
        {
            foreach (var queue in _queues)
                queue?.Dispose();
        }
    }
}
