// ProtonOS VirtioNet Device Implementation
// Virtio network device driver

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.Drivers.Virtio;

namespace ProtonOS.Drivers.Network.VirtioNet;

/// <summary>
/// Virtio network device driver.
/// </summary>
public unsafe class VirtioNetDevice : VirtioDevice
{
    // Queue indices
    private const ushort RX_QUEUE = 0;
    private const ushort TX_QUEUE = 1;

    // MAC address (6 bytes)
    private byte* _macAddress;

    // RX buffers - pre-allocated for incoming packets
    private const int RX_BUFFER_COUNT = 16;
    private const int RX_BUFFER_SIZE = 1526; // Max frame + virtio header
    private ulong _rxBuffersPhys;
    private byte* _rxBuffers;

    // Track which descriptor is used for each RX buffer
    private int[] _rxDescriptors;

    // TX buffer pool
    private const int TX_BUFFER_COUNT = 16;
    private const int TX_BUFFER_SIZE = 1526;
    private ulong _txBuffersPhys;
    private byte* _txBuffers;
    private int _txNextBuffer;

    /// <summary>
    /// Get the MAC address (6 bytes).
    /// </summary>
    public byte* MacAddress => _macAddress;

    /// <summary>
    /// Constructor - set wanted features.
    /// </summary>
    public VirtioNetDevice()
    {
        // Request features we want
        _wantedFeatures = (ulong)(
            VirtioNetFeatures.Mac |        // Want device MAC address
            VirtioNetFeatures.Status       // Want link status
            // Note: Not requesting MrgRxBuf for simplicity
        );
    }

    /// <summary>
    /// Initialize network-specific functionality after base virtio init.
    /// </summary>
    public bool InitializeNetDevice()
    {
        Debug.WriteLine("[virtio-net] Initializing network device...");

        // Read MAC address from device config
        if (_deviceCfg == null)
        {
            Debug.WriteLine("[virtio-net] No device config available");
            return false;
        }

        // Allocate MAC address storage
        ulong macPhys = Memory.AllocatePages(1);
        if (macPhys == 0)
        {
            Debug.WriteLine("[virtio-net] Failed to allocate MAC storage");
            return false;
        }
        _macAddress = (byte*)Memory.PhysToVirt(macPhys);

        // Read MAC address (first 6 bytes of device config)
        for (int i = 0; i < 6; i++)
        {
            _macAddress[i] = _deviceCfg[i];
        }

        Debug.Write("[virtio-net] MAC: ");
        for (int i = 0; i < 6; i++)
        {
            Debug.WriteHex(_macAddress[i]);
            if (i < 5) Debug.Write(":");
        }
        Debug.WriteLine();

        // Set up RX queue (queue 0)
        if (!SetupQueue(RX_QUEUE))
        {
            Debug.WriteLine("[virtio-net] Failed to set up RX queue");
            return false;
        }
        Debug.WriteLine("[virtio-net] RX queue set up");

        // Set up TX queue (queue 1)
        if (!SetupQueue(TX_QUEUE))
        {
            Debug.WriteLine("[virtio-net] Failed to set up TX queue");
            return false;
        }
        Debug.WriteLine("[virtio-net] TX queue set up");

        // Allocate RX buffers
        ulong rxPages = (RX_BUFFER_COUNT * RX_BUFFER_SIZE + 4095) / 4096;
        _rxBuffersPhys = Memory.AllocatePages(rxPages);
        if (_rxBuffersPhys == 0)
        {
            Debug.WriteLine("[virtio-net] Failed to allocate RX buffers");
            return false;
        }
        _rxBuffers = (byte*)Memory.PhysToVirt(_rxBuffersPhys);
        _rxDescriptors = new int[RX_BUFFER_COUNT];

        // Allocate TX buffers
        ulong txPages = (TX_BUFFER_COUNT * TX_BUFFER_SIZE + 4095) / 4096;
        _txBuffersPhys = Memory.AllocatePages(txPages);
        if (_txBuffersPhys == 0)
        {
            Debug.WriteLine("[virtio-net] Failed to allocate TX buffers");
            return false;
        }
        _txBuffers = (byte*)Memory.PhysToVirt(_txBuffersPhys);
        _txNextBuffer = 0;

        // Populate RX queue with empty buffers
        PopulateRxQueue();

        // Mark device as ready
        SetDriverOk();

        Debug.WriteLine("[virtio-net] Device initialized successfully");
        return true;
    }

    /// <summary>
    /// Populate the RX queue with empty buffers to receive packets.
    /// </summary>
    private void PopulateRxQueue()
    {
        var queue = GetQueue(RX_QUEUE);
        if (queue == null)
            return;

        for (int i = 0; i < RX_BUFFER_COUNT; i++)
        {
            // Allocate a descriptor
            int descIdx = queue.AllocateDescriptors(1);
            if (descIdx < 0)
            {
                Debug.WriteLine("[virtio-net] Failed to allocate RX descriptor");
                break;
            }

            _rxDescriptors[i] = descIdx;

            // Calculate buffer address for this entry
            ulong bufferPhys = _rxBuffersPhys + (ulong)(i * RX_BUFFER_SIZE);

            // Set up descriptor - device writes to this buffer
            queue.SetDescriptor(descIdx, bufferPhys, (uint)RX_BUFFER_SIZE,
                VirtqDescFlags.Write, 0);

            // Submit to available ring
            queue.SubmitAvailable(descIdx);
        }

        // Notify device about available buffers
        NotifyQueue(RX_QUEUE);
    }

    /// <summary>
    /// Send an Ethernet frame.
    /// </summary>
    /// <param name="data">Frame data (including Ethernet header)</param>
    /// <param name="length">Frame length in bytes</param>
    /// <returns>true if frame was queued successfully</returns>
    public bool SendFrame(byte* data, int length)
    {
        if (length <= 0 || length > 1514)
        {
            Debug.WriteLine("[virtio-net] SendFrame: Invalid length");
            return false;
        }

        var queue = GetQueue(TX_QUEUE);
        if (queue == null)
        {
            Debug.WriteLine("[virtio-net] SendFrame: No TX queue");
            return false;
        }

        // Allocate a descriptor
        int descIdx = queue.AllocateDescriptors(1);
        if (descIdx < 0)
        {
            Debug.WriteLine("[virtio-net] SendFrame: No free descriptors");
            return false;
        }

        // Get a TX buffer
        int bufIdx = _txNextBuffer;
        _txNextBuffer = (_txNextBuffer + 1) % TX_BUFFER_COUNT;

        byte* buffer = _txBuffers + (bufIdx * TX_BUFFER_SIZE);
        ulong bufferPhys = _txBuffersPhys + (ulong)(bufIdx * TX_BUFFER_SIZE);

        // Build virtio-net header (10 bytes without mrg_rxbuf, 12 with)
        // struct virtio_net_hdr {
        //   u8 flags;
        //   u8 gso_type;
        //   u16 hdr_len;
        //   u16 gso_size;
        //   u16 csum_start;
        //   u16 csum_offset;
        // }
        buffer[0] = 0;  // flags
        buffer[1] = 0;  // gso_type (VIRTIO_NET_HDR_GSO_NONE)
        buffer[2] = 0;  // hdr_len low
        buffer[3] = 0;  // hdr_len high
        buffer[4] = 0;  // gso_size low
        buffer[5] = 0;  // gso_size high
        buffer[6] = 0;  // csum_start low
        buffer[7] = 0;  // csum_start high
        buffer[8] = 0;  // csum_offset low
        buffer[9] = 0;  // csum_offset high

        // Copy frame data after header (10 bytes)
        byte* frameData = buffer + 10;
        for (int i = 0; i < length; i++)
        {
            frameData[i] = data[i];
        }

        // Set up descriptor - device reads from this buffer
        // Total length = header (10) + frame data
        queue.SetDescriptor(descIdx, bufferPhys, (uint)(10 + length),
            VirtqDescFlags.None, 0);

        // Submit to available ring
        queue.SubmitAvailable(descIdx);

        // Notify device
        NotifyQueue(TX_QUEUE);

        // Wait for completion (simple polling for now)
        int timeout = 10000;
        while (timeout > 0)
        {
            if (queue.HasUsedBuffers())
            {
                uint len;
                int usedDesc = queue.PopUsed(out len);
                if (usedDesc >= 0)
                {
                    queue.FreeDescriptors(usedDesc);
                    return true;
                }
            }
            timeout--;
        }

        Debug.WriteLine("[virtio-net] SendFrame: Timeout waiting for TX completion");
        queue.FreeDescriptors(descIdx);
        return false;
    }

    /// <summary>
    /// Receive an Ethernet frame.
    /// </summary>
    /// <param name="buffer">Buffer to store received frame</param>
    /// <param name="maxLength">Maximum buffer size</param>
    /// <returns>Number of bytes received, or 0 if no frame available</returns>
    public int ReceiveFrame(byte* buffer, int maxLength)
    {
        var queue = GetQueue(RX_QUEUE);
        if (queue == null)
            return 0;

        if (!queue.HasUsedBuffers())
            return 0;

        // Get the used buffer
        uint usedLen;
        int usedDesc = queue.PopUsed(out usedLen);
        if (usedDesc < 0)
            return 0;

        // Find which RX buffer this descriptor corresponds to
        int bufIdx = -1;
        for (int i = 0; i < RX_BUFFER_COUNT; i++)
        {
            if (_rxDescriptors[i] == usedDesc)
            {
                bufIdx = i;
                break;
            }
        }

        if (bufIdx < 0)
        {
            // Unknown descriptor - just re-add it
            queue.FreeDescriptors(usedDesc);
            return 0;
        }

        byte* rxBuffer = _rxBuffers + (bufIdx * RX_BUFFER_SIZE);

        // Skip virtio-net header (10 bytes without mrg_rxbuf)
        int frameLen = (int)usedLen - 10;
        if (frameLen <= 0 || frameLen > maxLength)
        {
            // Re-add buffer to queue
            ResubmitRxBuffer(queue, bufIdx, usedDesc);
            return 0;
        }

        // Copy frame data (skip header)
        byte* frameData = rxBuffer + 10;
        for (int i = 0; i < frameLen; i++)
        {
            buffer[i] = frameData[i];
        }

        // Re-add buffer to queue for next receive
        ResubmitRxBuffer(queue, bufIdx, usedDesc);

        return frameLen;
    }

    /// <summary>
    /// Resubmit an RX buffer to the queue.
    /// </summary>
    private void ResubmitRxBuffer(Virtqueue queue, int bufIdx, int oldDesc)
    {
        // Free the old descriptor
        queue.FreeDescriptors(oldDesc);

        // Allocate new descriptor
        int descIdx = queue.AllocateDescriptors(1);
        if (descIdx < 0)
        {
            Debug.WriteLine("[virtio-net] Failed to reallocate RX descriptor");
            return;
        }

        _rxDescriptors[bufIdx] = descIdx;

        ulong bufferPhys = _rxBuffersPhys + (ulong)(bufIdx * RX_BUFFER_SIZE);
        queue.SetDescriptor(descIdx, bufferPhys, (uint)RX_BUFFER_SIZE,
            VirtqDescFlags.Write, 0);
        queue.SubmitAvailable(descIdx);
        NotifyQueue(RX_QUEUE);
    }

    /// <summary>
    /// Check if link is up.
    /// </summary>
    public bool IsLinkUp()
    {
        if ((_features & VirtioFeatures.Version1) == 0)
            return true; // Assume up if no status feature

        // Check status byte in device config (offset 6)
        if (_deviceCfg == null)
            return true;

        ushort status = *(ushort*)(_deviceCfg + 6);
        return (status & 1) != 0;
    }

    public override void Dispose()
    {
        // Free RX buffers
        if (_rxBuffersPhys != 0)
        {
            ulong rxPages = (RX_BUFFER_COUNT * RX_BUFFER_SIZE + 4095) / 4096;
            Memory.FreePages(_rxBuffersPhys, rxPages);
        }

        // Free TX buffers
        if (_txBuffersPhys != 0)
        {
            ulong txPages = (TX_BUFFER_COUNT * TX_BUFFER_SIZE + 4095) / 4096;
            Memory.FreePages(_txBuffersPhys, txPages);
        }

        base.Dispose();
    }
}

/// <summary>
/// Virtio network feature bits.
/// </summary>
[Flags]
public enum VirtioNetFeatures : ulong
{
    None = 0,

    /// <summary>Device handles packets with partial checksum.</summary>
    Csum = 1UL << 0,

    /// <summary>Driver handles packets with partial checksum.</summary>
    GuestCsum = 1UL << 1,

    /// <summary>Control channel offloads reconfiguration support.</summary>
    CtrlGuestOffloads = 1UL << 2,

    /// <summary>Device maximum MTU reporting is supported.</summary>
    Mtu = 1UL << 3,

    /// <summary>Device has given MAC address.</summary>
    Mac = 1UL << 5,

    /// <summary>Device handles packets with any GSO type.</summary>
    GuestTso4 = 1UL << 7,

    /// <summary>Device handles TSO for IPv6.</summary>
    GuestTso6 = 1UL << 8,

    /// <summary>Device handles TSO with ECN bits.</summary>
    GuestEcn = 1UL << 9,

    /// <summary>Device handles UFO packets.</summary>
    GuestUfo = 1UL << 10,

    /// <summary>Device can send TSO.</summary>
    HostTso4 = 1UL << 11,

    /// <summary>Device can send TSO for IPv6.</summary>
    HostTso6 = 1UL << 12,

    /// <summary>Device can send TSO with ECN.</summary>
    HostEcn = 1UL << 13,

    /// <summary>Device can send UFO.</summary>
    HostUfo = 1UL << 14,

    /// <summary>Driver can merge receive buffers.</summary>
    MrgRxBuf = 1UL << 15,

    /// <summary>Configuration status field is available.</summary>
    Status = 1UL << 16,

    /// <summary>Control channel is available.</summary>
    CtrlVq = 1UL << 17,

    /// <summary>Control channel RX mode support.</summary>
    CtrlRx = 1UL << 18,

    /// <summary>Control channel VLAN filtering.</summary>
    CtrlVlan = 1UL << 19,

    /// <summary>Driver can send gratuitous packets.</summary>
    GuestAnnounce = 1UL << 21,

    /// <summary>Device supports multiqueue.</summary>
    Mq = 1UL << 22,

    /// <summary>Set MAC address through control channel.</summary>
    CtrlMacAddr = 1UL << 23,

    /// <summary>Device supports RSS.</summary>
    Rss = 1UL << 60,

    /// <summary>Device supports hash reports.</summary>
    HashReport = 1UL << 61,

    /// <summary>Driver can handle any layout.</summary>
    GuestHdrLen = 1UL << 59,
}
