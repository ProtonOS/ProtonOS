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
        Debug.WriteHex(0xD00D1000u | deviceId); // Device ID seen by JIT path
        _isLegacy = VirtioPciIds.IsLegacyDevice(deviceId);
        Debug.WriteHex(_isLegacy ? 0xD00D2001u : 0xD00D2000u); // Legacy/modern decision

        bool result = _isLegacy ? InitializeLegacy() : InitializeModern();
        Debug.WriteHex(result ? 0xD00D3001u : 0xD00D3000u); // Final result returned
        return result;
    }

    /// <summary>
    /// Initialize modern virtio device (1.0+).
    /// </summary>
    private bool InitializeModern()
    {
        // Early markers to see if we enter the method before crashing
        Debug.WriteHex(0xD00DA001u);

        if (_pciDevice == null)
        {
            Debug.WriteHex(0xDEAD0000u); // _pciDevice is null
            return false;
        }
        Debug.WriteHex(0xD00DA002u);

        // Enable memory space and bus mastering
        byte bus = _pciDevice.Address.Bus;
        Debug.WriteHex(0xD00DA003u);
        byte device = _pciDevice.Address.Device;
        Debug.WriteHex(0xD00DA004u);
        byte function = _pciDevice.Address.Function;
        Debug.WriteHex(0xD00DA005u);

        ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
        cmd |= PCI.PCI_CMD_MEMORY_SPACE;
        PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);

        PCI.EnableBusMaster(_pciDevice.Address);

        // Map BARs
        if (!MapBars())
        {
            Debug.WriteHex(0xD00D4000u); // MapBars failed
            return false;
        }
        Debug.WriteHex(0xD00D4001u); // MapBars succeeded

        // Find virtio capabilities
        if (!FindCapabilities())
        {
            Debug.WriteHex(0xD00D4002u); // FindCapabilities failed
            return false;
        }
        Debug.WriteHex(0xD00D4003u); // FindCapabilities succeeded

        Debug.WriteHex(0xD00D0001u); // After FindCapabilities

        // Reset device
        WriteStatus(VirtioDeviceStatus.Reset);
        Debug.WriteHex(0xD00D0002u); // After Reset

        // Acknowledge device
        WriteStatus(VirtioDeviceStatus.Acknowledge);
        Debug.WriteHex(0xD00D0003u); // After Acknowledge

        // We know how to drive it
        WriteStatus(ReadStatus() | VirtioDeviceStatus.Driver);
        Debug.WriteHex(0xD00D0004u); // After Driver

        // Read and negotiate features
        if (!NegotiateFeatures())
        {
            Debug.WriteHex(0xD00D4004u); // NegotiateFeatures failed
            return false;
        }
        Debug.WriteHex(0xD00D4005u); // NegotiateFeatures succeeded
        Debug.WriteHex(0xD00D0005u); // After NegotiateFeatures

        // Features OK
        WriteStatus(ReadStatus() | VirtioDeviceStatus.FeaturesOk);
        Debug.WriteHex(0xD00D0006u); // After FeaturesOk

        // Check if features were accepted
        if ((ReadStatus() & VirtioDeviceStatus.FeaturesOk) == 0)
        {
            WriteStatus(VirtioDeviceStatus.Failed);
            Debug.WriteHex(0xD00D4006u); // FeaturesOk not accepted
            return false;
        }
        Debug.WriteHex(0xD00D0007u); // Features accepted

        // Get number of queues
        _numQueues = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.NumQueues);
        Debug.WriteHex(0xD00D0008u); // Got numQueues
        Debug.WriteHex((uint)_numQueues);

        // Allocate queue array
        _queues = new Virtqueue[_numQueues];
        Debug.WriteHex(0xD00D0009u); // After queue array allocation

        _initialized = true;
        Debug.WriteHex(0xD00D000Au); // Initialized
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

            // Debug: Show BAR info
            Debug.WriteHex(0xBAA10000u | (uint)i);
            Debug.WriteHex((uint)(baseAddr >> 32));
            Debug.WriteHex((uint)baseAddr);
            Debug.WriteHex((uint)size);
            Debug.WriteHex(isValid ? 1u : 0u);
            Debug.WriteHex(isIO ? 1u : 0u);

            if (isValid && !isIO)
            {
                _barPhysAddr[i] = baseAddr;
                _barSize[i] = size;
                _barVirtAddr[i] = Memory.MapMMIO(_barPhysAddr[i], _barSize[i]);

                Debug.WriteHex(0xBAA20000u | (uint)i);
                Debug.WriteHex((uint)(_barVirtAddr[i] >> 32));
                Debug.WriteHex((uint)_barVirtAddr[i]);

                if (_barVirtAddr[i] == 0)
                {
                    Debug.WriteHex(0xBAA40000u | (uint)i); // MapMMIO failed
                    return false;
                }
                mappedCount++;
            }
        }

        Debug.WriteHex(0xBAA30000u | (uint)mappedCount);
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

        Debug.WriteHex(0xCAC00000u); // FindCapabilities entry

        // Find first vendor-specific capability
        byte capPtr = PCI.FindCapability(addr, 0x09);

        Debug.WriteHex(0xCAC00001u);
        Debug.WriteHex((uint)capPtr);

        // Extract BAR addresses array for convenience
        var barAddrs = _barVirtAddr;

        // Debug: Show BAR array contents
        Debug.WriteHex(0xCAC00002u);
        for (int bi = 0; bi < 6; bi++)
        {
            Debug.WriteHex(0xCAC00003u | (uint)bi);
            Debug.WriteHex((uint)(_barVirtAddr[bi] >> 32));
            Debug.WriteHex((uint)_barVirtAddr[bi]);
        }

        // Iterate through all PCI capabilities
        while (capPtr != 0)
        {
            // Read capability structure
            byte capLen = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 2));

            Debug.WriteHex(0xCAC00010u | (uint)capLen);

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

            Debug.WriteHex(0xCAC00020u | (uint)cfgType);
            Debug.WriteHex(0xCAC00030u | (uint)bar);

            // Process capability by type
            ProcessCapability(cfgType, bar, offset, length, capPtr, bus, device, function, barAddrs);

            // Get next capability pointer
            capPtr = PCI.ReadConfig8(bus, device, function, (ushort)(capPtr + 1));
        }

        // Common config is required
        Debug.WriteHex(0xCAC00090u);
        uint cfgHigh = (uint)((ulong)_commonCfg >> 32);
        uint cfgLow = (uint)(ulong)_commonCfg;
        Debug.WriteHex(cfgHigh);
        Debug.WriteHex(cfgLow);
        // Use explicit address comparison (JIT workaround for pointer != null)
        // Avoid boolean logic - check each condition separately
        if (cfgLow != 0)
        {
            Debug.WriteHex(0xCAC00041u);
            return true;
        }
        if (cfgHigh != 0)
        {
            Debug.WriteHex(0xCAC00043u);
            return true;
        }
        Debug.WriteHex(0xCAC00042u);
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
            Debug.WriteHex(0xCAC00050u); // BAR out of range
            return;
        }

        // Use direct field access instead of parameter to avoid JIT issue
        ulong barAddr = _barVirtAddr[bar];
        Debug.WriteHex(0xCAC00060u | (uint)bar);
        Debug.WriteHex((uint)(barAddr >> 32));
        Debug.WriteHex((uint)barAddr);

        if (barAddr == 0)
        {
            Debug.WriteHex(0xCAC00051u); // BAR not mapped
            return;
        }

        // Handle capability by type
        Debug.WriteHex(0xCAC00070u | (uint)cfgType);
        Debug.WriteHex(0xCAC00075u); // Debug: print offset before switch
        Debug.WriteHex(offset);
        switch (cfgType)
        {
            case 1: // CommonCfg
                // Debug: print barAddr and offset before addition
                Debug.WriteHex(0xCAC00076u); // About to add
                Debug.WriteHex((uint)(barAddr >> 32));
                Debug.WriteHex((uint)barAddr);
                Debug.WriteHex(offset);
                ulong sum = barAddr + offset;
                Debug.WriteHex(0xCAC00077u); // After add
                Debug.WriteHex((uint)(sum >> 32));
                Debug.WriteHex((uint)sum);
                byte* cfgAddr = (byte*)sum;
                Debug.WriteHex(0xCAC00091u);
                Debug.WriteHex((uint)((ulong)cfgAddr >> 32));
                Debug.WriteHex((uint)(ulong)cfgAddr);
                _commonCfg = cfgAddr;
                _commonCfgBar = bar;
                _commonCfgOffset = offset;
                _commonCfgLength = length;
                Debug.WriteHex(0xCAC00092u);
                Debug.WriteHex((uint)((ulong)_commonCfg >> 32));
                Debug.WriteHex((uint)(ulong)_commonCfg);
                Debug.WriteHex(0xCAC00081u); // CommonCfg set
                break;

            case 2: // NotifyCfg
                _notifyCfg = (byte*)(barAddr + offset);
                _notifyBar = bar;
                _notifyOffset = offset;
                _notifyMultiplier = PCI.ReadConfig32(bus, device, function, (ushort)(capPtr + 16));
                Debug.WriteHex(0xCAC00082u); // NotifyCfg set
                break;

            case 3: // IsrCfg
                _isrCfg = (byte*)(barAddr + offset);
                Debug.WriteHex(0xCAC00083u); // IsrCfg set
                break;

            case 4: // DeviceCfg
                _deviceCfg = (byte*)(barAddr + offset);
                _deviceCfgLength = length;
                Debug.WriteHex(0xCAC00084u); // DeviceCfg set
                break;
        }
    }

    /// <summary>
    /// Negotiate features with device.
    /// </summary>
    private bool NegotiateFeatures()
    {
        Debug.WriteHex(0xFEA00001u); // NegotiateFeatures entry

        // Read device features
        ulong deviceFeatures = 0;

        Debug.WriteHex(0xFEA00002u); // Before DeviceFeatureSelect write
        // Low 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeatureSelect) = 0;
        Debug.WriteHex(0xFEA00003u); // After DeviceFeatureSelect write
        deviceFeatures = *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeature);
        Debug.WriteHex(0xFEA00004u); // After DeviceFeature read

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeatureSelect) = 1;
        deviceFeatures |= (ulong)*(uint*)(_commonCfg + VirtioCommonCfgOffsets.DeviceFeature) << 32;
        Debug.WriteHex(0xFEA00005u); // After reading both feature halves

        // Calculate features we want
        Debug.WriteHex(0xFEA00006u); // Before reading _wantedFeatures
        ulong wantFeatures = _wantedFeatures;
        Debug.WriteHex(0xFEA00007u); // After reading _wantedFeatures

        // Always want VERSION_1 for modern devices
        wantFeatures |= (ulong)VirtioFeatures.Version1;

        // Negotiate: only features both support
        _features = (VirtioFeatures)(deviceFeatures & wantFeatures);
        Debug.WriteHex(0xFEA00008u); // After feature negotiation

        // Write driver features
        // Low 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 0;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)_features;
        Debug.WriteHex(0xFEA00009u); // After writing low features

        // High 32 bits
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeatureSelect) = 1;
        *(uint*)(_commonCfg + VirtioCommonCfgOffsets.DriverFeature) = (uint)((ulong)_features >> 32);
        Debug.WriteHex(0xFEA0000Au); // After writing high features

        return true;
    }

    /// <summary>
    /// Set up a virtqueue.
    /// </summary>
    protected bool SetupQueue(ushort queueIndex)
    {
        Debug.WriteHex(0x5EE00001u); // SetupQueue entry
        if (queueIndex >= _numQueues)
            return false;

        Debug.WriteHex(0x5EE00002u); // After numQueues check

        // Select queue
        *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueSelect) = queueIndex;

        Debug.WriteHex(0x5EE00003u); // After QueueSelect

        // Get queue size
        ushort queueSize = *(ushort*)(_commonCfg + VirtioCommonCfgOffsets.QueueSize);
        Debug.WriteHex(0x5EE00004u); // After reading queueSize
        Debug.WriteHex((uint)queueSize);
        if (queueSize == 0)
            return false;

        Debug.WriteHex(0x5EE00005u); // Before newobj Virtqueue

        // Create virtqueue
        var queue = new Virtqueue(queueIndex, queueSize);

        Debug.WriteHex(0x5EE00006u); // After newobj Virtqueue
        // Check if queue is null vs non-null
        if (queue == null)
        {
            Debug.WriteHex(0x5EE06000u); // queue is NULL - this is the bug!
        }
        else
        {
            Debug.WriteHex(0x5EE06001u); // queue is non-null
            Debug.WriteHex((uint)queue.QueueIndex); // Access a simple property
        }

        _queues[queueIndex] = queue;

        Debug.WriteHex(0x5EE00007u); // After array store

        // Set queue addresses - debug before accessing properties
        Debug.WriteHex(0x5EE00008u); // Before DescPhysAddr
        ulong descAddr = queue.DescPhysAddr;
        Debug.WriteHex(0x5EE00009u); // After DescPhysAddr
        Debug.WriteHex((uint)(descAddr >> 32));
        Debug.WriteHex((uint)descAddr);

        *(ulong*)(_commonCfg + VirtioCommonCfgOffsets.QueueDesc) = descAddr;
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
