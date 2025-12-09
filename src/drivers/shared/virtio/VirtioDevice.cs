// ProtonOS Virtio Driver - Base Device Implementation
// Common PCI virtio device initialization and management

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.Drivers.Virtio;

/// <summary>
/// Base class for virtio PCI devices.
/// Handles device initialization, feature negotiation, and queue setup.
/// </summary>
public abstract unsafe class VirtioDevice : IDisposable
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
    /// Get device-specific feature bits (override in subclass).
    /// </summary>
    protected abstract ulong DeviceFeatures { get; }

    /// <summary>
    /// Initialize the virtio device from PCI info.
    /// </summary>
    public bool Initialize(PciDeviceInfo pciDevice)
    {
        Debug.WriteHex(0xCAFE0001u);
        _pciDevice = pciDevice;
        Debug.WriteHex(0xCAFE0002u);

        // Determine if legacy or modern
        bool isLeg = VirtioPciIds.IsLegacyDevice(pciDevice.DeviceId);
        Debug.WriteHex(0xCAFE0003u);
        _isLegacy = isLeg;

        if (_isLegacy)
        {
            return InitializeLegacy();
        }
        else
        {
            return InitializeModern();
        }
    }

    /// <summary>
    /// Initialize modern virtio device (1.0+).
    /// </summary>
    private bool InitializeModern()
    {
        Debug.WriteHex(0xCAFE0010u);

        // Enable memory space and bus mastering
        byte bus = _pciDevice.Address.Bus;
        byte device = _pciDevice.Address.Device;
        byte function = _pciDevice.Address.Function;
        Debug.WriteHex(0xCAFE0011u);

        ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
        cmd |= PCI.PCI_CMD_MEMORY_SPACE;
        PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);
        Debug.WriteHex(0xCAFE0012u);

        PCI.EnableBusMaster(_pciDevice.Address);
        Debug.WriteHex(0xCAFE0013u);

        // Map BARs
        if (!MapBars())
            return false;
        Debug.WriteHex(0xCAFE0014u);

        // Find virtio capabilities
        if (!FindCapabilities())
            return false;
        Debug.WriteHex(0xCAFE0015u);

        // Reset device
        WriteStatus(VirtioDeviceStatus.Reset);
        Debug.WriteHex(0xCAFE0016u);

        // Acknowledge device
        WriteStatus(VirtioDeviceStatus.Acknowledge);
        Debug.WriteHex(0xCAFE0017u);

        // We know how to drive it
        WriteStatus(ReadStatus() | VirtioDeviceStatus.Driver);
        Debug.WriteHex(0xCAFE0018u);

        // Read and negotiate features
        if (!NegotiateFeatures())
            return false;
        Debug.WriteHex(0xCAFE0019u);

        // Features OK
        WriteStatus(ReadStatus() | VirtioDeviceStatus.FeaturesOk);
        Debug.WriteHex(0xCAFE001Au);

        // Check if features were accepted
        if ((ReadStatus() & VirtioDeviceStatus.FeaturesOk) == 0)
        {
            // Features not accepted
            WriteStatus(VirtioDeviceStatus.Failed);
            return false;
        }
        Debug.WriteHex(0xCAFE001Bu);

        // Get number of queues
        _numQueues = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.NumQueues);
        Debug.WriteHex(0xCAFE001Cu);

        // Allocate queue array
        _queues = new Virtqueue[_numQueues];
        Debug.WriteHex(0xCAFE001Du);

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
        Debug.WriteHex(0xBAF00001u);
        if (_pciDevice == null)
        {
            Debug.WriteHex(0xBAF0DEADu); // _pciDevice is null!
            return false;
        }
        Debug.WriteHex(0xBAF00002u);

        var bars = _pciDevice.Bars;
        Debug.WriteHex(0xBAF00003u);

        if (bars == null)
        {
            Debug.WriteHex(0xBAF0DEAEu); // Bars is null!
            return false;
        }
        Debug.WriteHex(0xBAF00004u);

        for (int i = 0; i < 6; i++)
        {
            // Debug: Show BAR info
            Debug.WriteHex(0xBAF00010u);
            Debug.WriteHex((uint)i);

            // Copy BAR struct to local variable (JIT now handles this correctly)
            PciBar bar = bars[i];
            ulong baseAddr = bar.BaseAddress;
            ulong size = bar.Size;
            bool isValid = bar.IsValid;
            bool isIO = bar.IsIO;

            Debug.WriteHex((uint)(baseAddr >> 32));
            Debug.WriteHex((uint)baseAddr);
            Debug.WriteHex((uint)size);
            Debug.WriteHex(isValid ? 1u : 0u);
            Debug.WriteHex(isIO ? 1u : 0u);

            if (isValid && !isIO)
            {
                Debug.WriteHex(0xBAF00020u); // Mapping this BAR
                _barPhysAddr[i] = baseAddr;
                _barSize[i] = size;
                _barVirtAddr[i] = Memory.MapMMIO(_barPhysAddr[i], _barSize[i]);
                Debug.WriteHex((uint)(_barVirtAddr[i] >> 32));
                Debug.WriteHex((uint)_barVirtAddr[i]);

                if (_barVirtAddr[i] == 0)
                {
                    Debug.WriteHex(0xBAF0FA11u); // MapMMIO failed
                    return false;
                }
            }
        }
        return true;
    }

    /// <summary>
    /// Find virtio PCI capabilities.
    /// </summary>
    private bool FindCapabilities()
    {
        Debug.WriteHex(0xCAC00001u);

        // Extract address components for convenience
        var addr = _pciDevice.Address;
        byte bus = addr.Bus;
        byte device = addr.Device;
        byte function = addr.Function;

        Debug.WriteHex(0xCAC00002u);

        // Find first vendor-specific capability
        byte capPtr = PCI.FindCapability(addr, 0x09);
        Debug.WriteHex(0xCAC00003u);
        Debug.WriteHex((uint)capPtr);

        // Extract BAR addresses array for convenience
        var barAddrs = _barVirtAddr;

        // Iterate through all PCI capabilities
        while (capPtr != 0)
        {
            Debug.WriteHex(0xCAC00010u);
            Debug.WriteHex((uint)capPtr);

            // Read capability structure
            byte capLen = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 2));

            if (capLen < 16)
            {
                // Skip - capability too short
                Debug.WriteHex(0xCAC00011u);
                capPtr = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 1));
                continue;
            }

            byte cfgType = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 3));
            byte bar = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 4));
            uint offset = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 8));
            uint length = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 12));

            Debug.WriteHex(0xCAC00012u);
            Debug.WriteHex((uint)cfgType);
            Debug.WriteHex((uint)bar);

            // Process capability by type
            ProcessCapability(cfgType, bar, offset, length, capPtr, bus, device, function, barAddrs);

            // Get next capability pointer
            capPtr = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 1));
            Debug.WriteHex(0xCAC00030u);
            Debug.WriteHex((uint)capPtr);
        }

        Debug.WriteHex(0xCAC00040u);

        // Common config is required
        bool hasCommon = _commonCfg != null;
        Debug.WriteHex(hasCommon ? 0xCAC00041u : 0xCAC00042u);
        return hasCommon;
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
            Debug.WriteHex(0xCAC00060u); // BAR index too high
            return;
        }

        ulong barAddr = barAddrs[bar];
        if (barAddr == 0)
        {
            Debug.WriteHex(0xCAC00061u); // BAR not mapped
            return;
        }

        Debug.WriteHex(0xCAC00062u); // Valid BAR

        // Handle capability by type using switch (JIT now handles if-else chains correctly)
        switch (cfgType)
        {
            case 1: // CommonCfg
                _commonCfg = (byte*)(barAddr + offset);
                _commonCfgBar = bar;
                _commonCfgOffset = offset;
                _commonCfgLength = length;
                Debug.WriteHex(0xCAC00020u); // CommonCfg found
                break;

            case 2: // NotifyCfg
                _notifyCfg = (byte*)(barAddr + offset);
                _notifyBar = bar;
                _notifyOffset = offset;
                _notifyMultiplier = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 16));
                Debug.WriteHex(0xCAC00021u); // NotifyCfg found
                break;

            case 3: // IsrCfg
                _isrCfg = (byte*)(barAddr + offset);
                Debug.WriteHex(0xCAC00022u); // IsrCfg found
                break;

            case 4: // DeviceCfg
                _deviceCfg = (byte*)(barAddr + offset);
                _deviceCfgLength = length;
                Debug.WriteHex(0xCAC00023u); // DeviceCfg found
                break;

            default:
                // Unknown type - just skip
                Debug.WriteHex(0xCAC00024u);
                Debug.WriteHex((uint)cfgType);
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

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeatureSelect) = 1;
        deviceFeatures |= (ulong)*(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeature) << 32;

        // Calculate features we want
        ulong wantFeatures = DeviceFeatures;

        // Always want VERSION_1 for modern devices
        wantFeatures |= (ulong)VirtioFeatures.Version1;

        // Negotiate: only features both support
        _features = (VirtioFeatures)(deviceFeatures & wantFeatures);

        // Write driver features
        // Low 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 0;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)_features;

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 1;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)((ulong)_features >> 32);

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
