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
        _pciDevice = pciDevice;

        // Determine if legacy or modern
        _isLegacy = VirtioPciIds.IsLegacyDevice(pciDevice.DeviceId);

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
        // Enable memory space and bus mastering
        PCI.EnableMemorySpace(_pciDevice.Address);
        PCI.EnableBusMaster(_pciDevice.Address);

        // Map BARs
        if (!MapBars())
            return false;

        // Find virtio capabilities
        if (!FindCapabilities())
            return false;

        // Reset device
        WriteStatus(VirtioDeviceStatus.Reset);

        // Acknowledge device
        WriteStatus(VirtioDeviceStatus.Acknowledge);

        // We know how to drive it
        WriteStatus(ReadStatus() | VirtioDeviceStatus.Driver);

        // Read and negotiate features
        if (!NegotiateFeatures())
            return false;

        // Features OK
        WriteStatus(ReadStatus() | VirtioDeviceStatus.FeaturesOk);

        // Check if features were accepted
        if ((ReadStatus() & VirtioDeviceStatus.FeaturesOk) == 0)
        {
            // Features not accepted
            WriteStatus(VirtioDeviceStatus.Failed);
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
        for (int i = 0; i < 6; i++)
        {
            if (_pciDevice.Bars[i].IsValid && !_pciDevice.Bars[i].IsIO)
            {
                _barPhysAddr[i] = _pciDevice.Bars[i].BaseAddress;
                _barSize[i] = _pciDevice.Bars[i].Size;
                _barVirtAddr[i] = Memory.MapMMIO(_barPhysAddr[i], _barSize[i]);

                if (_barVirtAddr[i] == 0)
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Find virtio PCI capabilities.
    /// </summary>
    private bool FindCapabilities()
    {
        byte capPtr = PCI.FindCapability(_pciDevice.Address, 0x09); // Vendor-specific cap

        while (capPtr != 0)
        {
            // Check if this is a virtio cap
            byte capLen = PCI.ReadConfig8(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                           _pciDevice.Address.Function, capPtr);

            if (capLen >= 16)
            {
                byte cfgType = PCI.ReadConfig8(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                                _pciDevice.Address.Function, (ushort)(capPtr + 3));
                byte bar = PCI.ReadConfig8(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                            _pciDevice.Address.Function, (ushort)(capPtr + 4));
                uint offset = PCI.ReadConfig32(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                               _pciDevice.Address.Function, (ushort)(capPtr + 8));
                uint length = PCI.ReadConfig32(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                               _pciDevice.Address.Function, (ushort)(capPtr + 12));

                switch ((VirtioPciCapType)cfgType)
                {
                    case VirtioPciCapType.CommonCfg:
                        _commonCfg = (byte*)(_barVirtAddr[bar] + offset);
                        _commonCfgBar = bar;
                        _commonCfgOffset = offset;
                        _commonCfgLength = length;
                        break;

                    case VirtioPciCapType.NotifyCfg:
                        _notifyCfg = (byte*)(_barVirtAddr[bar] + offset);
                        _notifyBar = bar;
                        _notifyOffset = offset;
                        // Read notify multiplier (at cap offset 16)
                        _notifyMultiplier = PCI.ReadConfig32(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                                             _pciDevice.Address.Function, (ushort)(capPtr + 16));
                        break;

                    case VirtioPciCapType.IsrCfg:
                        _isrCfg = (byte*)(_barVirtAddr[bar] + offset);
                        break;

                    case VirtioPciCapType.DeviceCfg:
                        _deviceCfg = (byte*)(_barVirtAddr[bar] + offset);
                        _deviceCfgLength = length;
                        break;
                }
            }

            // Next capability
            capPtr = PCI.ReadConfig8(_pciDevice.Address.Bus, _pciDevice.Address.Device,
                                      _pciDevice.Address.Function, (ushort)(capPtr + 1));
        }

        // Common config is required
        return _commonCfg != null;
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
