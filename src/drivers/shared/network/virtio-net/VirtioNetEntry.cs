// ProtonOS VirtioNet Entry Point
// Static entry point for JIT compilation

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Network.Stack;
using ProtonOS.Drivers.Virtio;

namespace ProtonOS.Drivers.Network.VirtioNet;

/// <summary>
/// Static entry point for VirtioNet driver, designed for JIT compilation.
/// </summary>
public static unsafe class VirtioNetEntry
{
    // Active device instance
    private static VirtioNetDevice? _device;

    /// <summary>
    /// Run network stack unit tests.
    /// Call this before device binding to verify protocol logic.
    /// </summary>
    /// <returns>1 if all tests pass, 0 if any fail.</returns>
    public static int RunUnitTests()
    {
        return NetworkStackTests.RunAll() ? 1 : 0;
    }

    /// <summary>
    /// Check if this driver supports the given PCI device.
    /// </summary>
    /// <param name="vendorId">PCI vendor ID</param>
    /// <param name="deviceId">PCI device ID</param>
    /// <returns>true if this driver can handle the device</returns>
    public static bool Probe(ushort vendorId, ushort deviceId)
    {
        // Check for virtio vendor
        if (vendorId != 0x1AF4)
            return false;

        // Check for virtio-net device IDs
        // Legacy: 0x1000, Modern: 0x1041
        return deviceId == 0x1000 || deviceId == 0x1041;
    }

    /// <summary>
    /// Bind the driver to a PCI device.
    /// </summary>
    /// <param name="bus">PCI bus number</param>
    /// <param name="device">PCI device number</param>
    /// <param name="function">PCI function number</param>
    /// <returns>true if binding succeeded</returns>
    public static bool Bind(byte bus, byte device, byte function)
    {
        // Build PciDeviceInfo from bus/device/function
        PciDeviceInfo pciDevice = new PciDeviceInfo();
        pciDevice.Address = new PciAddress(bus, device, function);

        // Read vendor/device IDs
        uint vendorDevice = PCI.ReadConfig32(bus, device, function, PCI.PCI_VENDOR_ID);
        pciDevice.VendorId = (ushort)(vendorDevice & 0xFFFF);
        pciDevice.DeviceId = (ushort)(vendorDevice >> 16);

        // Read class/subclass/progif
        uint classReg = PCI.ReadConfig32(bus, device, function, PCI.PCI_REVISION_ID);
        pciDevice.RevisionId = (byte)(classReg & 0xFF);
        pciDevice.ProgIf = (byte)((classReg >> 8) & 0xFF);
        pciDevice.SubclassCode = (byte)((classReg >> 16) & 0xFF);
        pciDevice.ClassCode = (byte)((classReg >> 24) & 0xFF);

        // Read BARs and program any unprogrammed ones
        int i = 0;
        while (i < 6)
        {
            PciBar bar;
            ReadAndProgramBar(bus, device, function, i, out bar);
            pciDevice.Bars[i] = bar;

            // If this is a 64-bit BAR, skip the next index (upper 32 bits)
            if (bar.Is64Bit)
            {
                i++;
                if (i < 6)
                {
                    PciBar empty;
                    empty.Index = i;
                    empty.BaseAddress = 0;
                    empty.Size = 0;
                    empty.IsIO = false;
                    empty.Is64Bit = false;
                    empty.IsPrefetchable = false;
                    pciDevice.Bars[i] = empty;
                }
            }
            i++;
        }

        // Create and initialize the virtio network device
        Debug.WriteLine("[virtio-net] Binding to PCI {0}:{1}.{2}", bus, device, function);
        _device = new VirtioNetDevice();

        // Initialize virtio device (handles modern/legacy detection, feature negotiation)
        bool initResult = ((VirtioDevice)_device).Initialize(pciDevice);
        if (!initResult)
        {
            Debug.WriteLine("[virtio-net] VirtioDevice.Initialize failed");
            _device = null;
            return false;
        }

        // Initialize network-specific functionality
        bool netResult = _device.InitializeNetDevice();
        if (!netResult)
        {
            Debug.WriteLine("[virtio-net] InitializeNetDevice failed");
            _device.Dispose();
            _device = null;
            return false;
        }

        Debug.WriteLine("[virtio-net] Driver bound successfully");
        return true;
    }

    /// <summary>
    /// Get the active network device instance.
    /// </summary>
    public static VirtioNetDevice? GetDevice() => _device;

    /// <summary>
    /// Test sending a packet (for debugging).
    /// </summary>
    public static int TestSend()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-net] TestSend: No device bound");
            return 0;
        }

        Debug.WriteLine("[virtio-net] TestSend: Sending test frame...");

        // Create a simple ARP request to probe the network
        // This is a broadcast ARP "who has 10.0.2.2?" (QEMU's gateway)
        byte* frame = stackalloc byte[64];

        // Ethernet header (14 bytes)
        // Destination: broadcast (ff:ff:ff:ff:ff:ff)
        frame[0] = 0xFF; frame[1] = 0xFF; frame[2] = 0xFF;
        frame[3] = 0xFF; frame[4] = 0xFF; frame[5] = 0xFF;

        // Source: our MAC address
        byte* mac = _device.MacAddress;
        frame[6] = mac[0]; frame[7] = mac[1]; frame[8] = mac[2];
        frame[9] = mac[3]; frame[10] = mac[4]; frame[11] = mac[5];

        // EtherType: ARP (0x0806)
        frame[12] = 0x08;
        frame[13] = 0x06;

        // ARP packet (28 bytes)
        // Hardware type: Ethernet (1)
        frame[14] = 0x00; frame[15] = 0x01;
        // Protocol type: IPv4 (0x0800)
        frame[16] = 0x08; frame[17] = 0x00;
        // Hardware size: 6
        frame[18] = 6;
        // Protocol size: 4
        frame[19] = 4;
        // Opcode: request (1)
        frame[20] = 0x00; frame[21] = 0x01;
        // Sender MAC
        frame[22] = mac[0]; frame[23] = mac[1]; frame[24] = mac[2];
        frame[25] = mac[3]; frame[26] = mac[4]; frame[27] = mac[5];
        // Sender IP: 10.0.2.15 (QEMU default guest IP)
        frame[28] = 10; frame[29] = 0; frame[30] = 2; frame[31] = 15;
        // Target MAC: 00:00:00:00:00:00 (unknown)
        frame[32] = 0; frame[33] = 0; frame[34] = 0;
        frame[35] = 0; frame[36] = 0; frame[37] = 0;
        // Target IP: 10.0.2.2 (QEMU gateway)
        frame[38] = 10; frame[39] = 0; frame[40] = 2; frame[41] = 2;

        // Pad to minimum Ethernet frame size (46 bytes of data)
        for (int j = 42; j < 60; j++)
            frame[j] = 0;

        // Send the frame (60 bytes: 14 header + 28 ARP + 18 padding)
        bool result = _device.SendFrame(frame, 60);

        if (result)
        {
            Debug.WriteLine("[virtio-net] TestSend: Frame sent successfully");
            return 1;
        }
        else
        {
            Debug.WriteLine("[virtio-net] TestSend: Failed to send frame");
            return 0;
        }
    }

    /// <summary>
    /// Test receiving packets (for debugging).
    /// </summary>
    public static int TestReceive()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-net] TestReceive: No device bound");
            return 0;
        }

        Debug.WriteLine("[virtio-net] TestReceive: Checking for received frames...");

        int count = 0;
        byte* buffer = stackalloc byte[1514]; // Max Ethernet frame size

        // Try to receive up to 10 frames
        for (int i = 0; i < 10; i++)
        {
            int len = _device.ReceiveFrame(buffer, 1514);
            if (len <= 0)
                break;

            count++;
            Debug.Write("[virtio-net] Received frame ");
            Debug.WriteDecimal(count);
            Debug.Write(": ");
            Debug.WriteDecimal(len);
            Debug.Write(" bytes, src=");

            // Print source MAC
            for (int j = 6; j < 12; j++)
            {
                Debug.WriteHex(buffer[j]);
                if (j < 11) Debug.Write(":");
            }

            // Print EtherType
            ushort etherType = (ushort)((buffer[12] << 8) | buffer[13]);
            Debug.Write(" type=");
            Debug.WriteHex(etherType);
            Debug.WriteLine();
        }

        Debug.Write("[virtio-net] TestReceive: Received ");
        Debug.WriteDecimal(count);
        Debug.WriteLine(" frame(s)");

        return count;
    }

    // Network stack instance for protocol handling
    private static NetworkStack? _netStack;

    /// <summary>
    /// Test the network stack with ARP resolution.
    /// </summary>
    public static int TestNetworkStack()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-net] TestNetworkStack: No device bound");
            return 0;
        }

        Debug.WriteLine("[virtio-net] TestNetworkStack: Testing network stack...");

        // Create network stack if not already created
        if (_netStack == null)
        {
            _netStack = new NetworkStack(_device.MacAddress);

            // Configure with QEMU user-mode network defaults:
            // Guest IP: 10.0.2.15, Subnet: 255.255.255.0, Gateway: 10.0.2.2
            uint guestIP = ARP.MakeIP(10, 0, 2, 15);
            uint subnetMask = ARP.MakeIP(255, 255, 255, 0);
            uint gateway = ARP.MakeIP(10, 0, 2, 2);

            _netStack.Configure(guestIP, subnetMask, gateway);
        }

        // Check if gateway MAC is already cached
        byte* gatewayMac = stackalloc byte[6];
        uint gateway2 = ARP.MakeIP(10, 0, 2, 2);

        if (_netStack.ArpCache.Lookup(gateway2, gatewayMac))
        {
            Debug.Write("[virtio-net] Gateway MAC already cached: ");
            for (int i = 0; i < 6; i++)
            {
                Debug.WriteHex(gatewayMac[i]);
                if (i < 5) Debug.Write(":");
            }
            Debug.WriteLine();
            return 1;
        }

        // Send ARP request for gateway
        int frameLen = _netStack.SendArpRequest(gateway2);
        if (frameLen == 0)
        {
            Debug.WriteLine("[virtio-net] TestNetworkStack: Failed to build ARP request");
            return 0;
        }

        // Transmit the ARP request
        byte* txBuffer = _netStack.GetTxBuffer();
        bool sent = _device.SendFrame(txBuffer, frameLen);
        if (!sent)
        {
            Debug.WriteLine("[virtio-net] TestNetworkStack: Failed to send ARP request");
            return 0;
        }

        Debug.WriteLine("[virtio-net] TestNetworkStack: ARP request sent, waiting for reply...");

        // Poll for ARP reply (simple busy-wait for demo)
        byte* rxBuffer = stackalloc byte[1514];
        int attempts = 100000;
        int repliesReceived = 0;

        while (attempts > 0)
        {
            int len = _device.ReceiveFrame(rxBuffer, 1514);
            if (len > 0)
            {
                // Process the received frame through the network stack
                _netStack.ProcessFrame(rxBuffer, len);

                // Check if we now have the gateway in our cache
                if (_netStack.ArpCache.Lookup(gateway2, gatewayMac))
                {
                    Debug.Write("[virtio-net] TestNetworkStack: Resolved gateway MAC: ");
                    for (int i = 0; i < 6; i++)
                    {
                        Debug.WriteHex(gatewayMac[i]);
                        if (i < 5) Debug.Write(":");
                    }
                    Debug.WriteLine();
                    repliesReceived = 1;
                    break;
                }
            }
            attempts--;
        }

        if (repliesReceived == 0)
        {
            Debug.WriteLine("[virtio-net] TestNetworkStack: No ARP reply received (timeout)");
        }

        // Print stats
        ulong rxFrames, txFrames, arpReq, arpRep;
        _netStack.GetStats(out rxFrames, out txFrames, out arpReq, out arpRep);
        Debug.Write("[virtio-net] Stats: RX=");
        Debug.WriteDecimal((uint)rxFrames);
        Debug.Write(" TX=");
        Debug.WriteDecimal((uint)txFrames);
        Debug.Write(" ARP-Req=");
        Debug.WriteDecimal((uint)arpReq);
        Debug.Write(" ARP-Rep=");
        Debug.WriteDecimal((uint)arpRep);
        Debug.WriteLine();

        return repliesReceived;
    }

    /// <summary>
    /// Test sending a ping (ICMP echo request) to the gateway.
    /// </summary>
    public static int TestPing()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-net] TestPing: No device bound");
            return 0;
        }

        // Create network stack if not already created
        if (_netStack == null)
        {
            _netStack = new NetworkStack(_device.MacAddress);

            uint guestIP = ARP.MakeIP(10, 0, 2, 15);
            uint subnetMask = ARP.MakeIP(255, 255, 255, 0);
            uint gateway = ARP.MakeIP(10, 0, 2, 2);

            _netStack.Configure(guestIP, subnetMask, gateway);
        }

        uint gatewayIP = ARP.MakeIP(10, 0, 2, 2);

        // First, ensure we have the gateway MAC (via ARP if needed)
        byte* gatewayMac = stackalloc byte[6];
        if (!_netStack.ArpCache.Lookup(gatewayIP, gatewayMac))
        {
            Debug.WriteLine("[virtio-net] TestPing: Need ARP resolution first...");

            // Send ARP request
            int arpLen = _netStack.SendArpRequest(gatewayIP);
            if (arpLen > 0)
            {
                byte* txBuffer = _netStack.GetTxBuffer();
                _device.SendFrame(txBuffer, arpLen);
            }

            // Wait for ARP reply
            byte* rxBuffer = stackalloc byte[1514];
            int attempts = 50000;
            while (attempts > 0 && !_netStack.ArpCache.Lookup(gatewayIP, gatewayMac))
            {
                int len = _device.ReceiveFrame(rxBuffer, 1514);
                if (len > 0)
                    _netStack.ProcessFrame(rxBuffer, len);
                attempts--;
            }

            if (!_netStack.ArpCache.Lookup(gatewayIP, gatewayMac))
            {
                Debug.WriteLine("[virtio-net] TestPing: Failed to resolve gateway MAC");
                return 0;
            }
        }

        Debug.WriteLine("[virtio-net] TestPing: Sending ping to gateway 10.0.2.2...");

        // Send ping
        int pingLen = _netStack.SendPing(gatewayIP);
        if (pingLen == 0)
        {
            Debug.WriteLine("[virtio-net] TestPing: Failed to build ping");
            return 0;
        }

        // Transmit the ping
        byte* pingBuffer = _netStack.GetTxBuffer();
        bool sent = _device.SendFrame(pingBuffer, pingLen);
        if (!sent)
        {
            Debug.WriteLine("[virtio-net] TestPing: Failed to send ping");
            return 0;
        }

        // Wait for ping reply
        byte* rxBuf = stackalloc byte[1514];
        int pollAttempts = 100000;
        int pingReceived = 0;

        while (pollAttempts > 0)
        {
            int len = _device.ReceiveFrame(rxBuf, 1514);
            if (len > 0)
            {
                _netStack.ProcessFrame(rxBuf, len);

                // Check if ping was received
                if (!_netStack.IsPingPending())
                {
                    Debug.WriteLine("[virtio-net] TestPing: Ping reply received!");
                    pingReceived = 1;
                    break;
                }
            }
            pollAttempts--;
        }

        if (pingReceived == 0)
        {
            Debug.WriteLine("[virtio-net] TestPing: No ping reply received (timeout)");
        }

        // Print ICMP stats
        ulong icmpSent, icmpRecv;
        _netStack.GetIcmpStats(out icmpSent, out icmpRecv);
        Debug.Write("[virtio-net] ICMP Stats: Sent=");
        Debug.WriteDecimal((uint)icmpSent);
        Debug.Write(" Recv=");
        Debug.WriteDecimal((uint)icmpRecv);
        Debug.WriteLine();

        return pingReceived;
    }

    /// <summary>
    /// Test sending a UDP packet (DNS query) to QEMU's DNS server.
    /// </summary>
    public static int TestUdp()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-net] TestUdp: No device bound");
            return 0;
        }

        // Create network stack if not already created
        if (_netStack == null)
        {
            _netStack = new NetworkStack(_device.MacAddress);

            uint guestIP = ARP.MakeIP(10, 0, 2, 15);
            uint subnetMask = ARP.MakeIP(255, 255, 255, 0);
            uint gateway = ARP.MakeIP(10, 0, 2, 2);

            _netStack.Configure(guestIP, subnetMask, gateway);
        }

        // QEMU's DNS server is at 10.0.2.3
        uint dnsServerIP = ARP.MakeIP(10, 0, 2, 3);

        // First, ensure we have the DNS server MAC (via ARP if needed)
        byte* dnsServerMac = stackalloc byte[6];
        if (!_netStack.ArpCache.Lookup(dnsServerIP, dnsServerMac))
        {
            Debug.WriteLine("[virtio-net] TestUdp: Need ARP resolution for DNS server...");

            // Send ARP request
            int arpLen = _netStack.SendArpRequest(dnsServerIP);
            if (arpLen > 0)
            {
                byte* txBuffer = _netStack.GetTxBuffer();
                _device.SendFrame(txBuffer, arpLen);
            }

            // Wait for ARP reply
            byte* rxBuffer = stackalloc byte[1514];
            int attempts = 50000;
            while (attempts > 0 && !_netStack.ArpCache.Lookup(dnsServerIP, dnsServerMac))
            {
                int len = _device.ReceiveFrame(rxBuffer, 1514);
                if (len > 0)
                    _netStack.ProcessFrame(rxBuffer, len);
                attempts--;
            }

            if (!_netStack.ArpCache.Lookup(dnsServerIP, dnsServerMac))
            {
                Debug.WriteLine("[virtio-net] TestUdp: Failed to resolve DNS server MAC");
                return 0;
            }
        }

        Debug.WriteLine("[virtio-net] TestUdp: Sending DNS query to 10.0.2.3...");

        // Build a minimal DNS query for "test.local"
        // DNS header: ID (2), Flags (2), QDCOUNT (2), ANCOUNT (2), NSCOUNT (2), ARCOUNT (2)
        // Query: Name, Type (2), Class (2)
        byte* dnsQuery = stackalloc byte[32];

        // Transaction ID
        dnsQuery[0] = 0x12; dnsQuery[1] = 0x34;
        // Flags: Standard query
        dnsQuery[2] = 0x01; dnsQuery[3] = 0x00;
        // QDCOUNT: 1 question
        dnsQuery[4] = 0x00; dnsQuery[5] = 0x01;
        // ANCOUNT, NSCOUNT, ARCOUNT: 0
        dnsQuery[6] = 0x00; dnsQuery[7] = 0x00;
        dnsQuery[8] = 0x00; dnsQuery[9] = 0x00;
        dnsQuery[10] = 0x00; dnsQuery[11] = 0x00;

        // Query name: "test" (4 chars) + "local" (5 chars) + root
        dnsQuery[12] = 4;  // length of "test"
        dnsQuery[13] = (byte)'t'; dnsQuery[14] = (byte)'e'; dnsQuery[15] = (byte)'s'; dnsQuery[16] = (byte)'t';
        dnsQuery[17] = 5;  // length of "local"
        dnsQuery[18] = (byte)'l'; dnsQuery[19] = (byte)'o'; dnsQuery[20] = (byte)'c'; dnsQuery[21] = (byte)'a'; dnsQuery[22] = (byte)'l';
        dnsQuery[23] = 0;  // root label

        // Type: A (host address)
        dnsQuery[24] = 0x00; dnsQuery[25] = 0x01;
        // Class: IN (Internet)
        dnsQuery[26] = 0x00; dnsQuery[27] = 0x01;

        int dnsQueryLen = 28;

        // Send UDP to DNS port 53
        ushort srcPort = 54321;  // Ephemeral source port
        ushort destPort = 53;    // DNS port

        int frameLen = _netStack.SendUdp(dnsServerIP, srcPort, destPort, dnsQuery, dnsQueryLen);
        if (frameLen == 0)
        {
            Debug.WriteLine("[virtio-net] TestUdp: Failed to build UDP packet");
            return 0;
        }

        // Transmit the packet
        byte* udpBuffer = _netStack.GetTxBuffer();
        bool sent = _device.SendFrame(udpBuffer, frameLen);
        if (!sent)
        {
            Debug.WriteLine("[virtio-net] TestUdp: Failed to send UDP packet");
            return 0;
        }

        // Wait for DNS response
        byte* rxBuf = stackalloc byte[1514];
        int pollAttempts = 100000;
        int responseReceived = 0;

        while (pollAttempts > 0)
        {
            int len = _device.ReceiveFrame(rxBuf, 1514);
            if (len > 0)
            {
                _netStack.ProcessFrame(rxBuf, len);

                // Check if we received any UDP packets
                if (_netStack.UdpAvailable() > 0)
                {
                    Debug.WriteLine("[virtio-net] TestUdp: DNS response received!");
                    responseReceived = 1;

                    // Read and discard the response
                    uint srcIP;
                    ushort rSrcPort, rDestPort;
                    byte* respBuf = stackalloc byte[512];
                    int respLen = _netStack.ReceiveUdp(out srcIP, out rSrcPort, out rDestPort, respBuf, 512);

                    Debug.Write("[virtio-net] TestUdp: Response from ");
                    Debug.WriteDecimal((srcIP >> 24) & 0xFF);
                    Debug.Write(".");
                    Debug.WriteDecimal((srcIP >> 16) & 0xFF);
                    Debug.Write(".");
                    Debug.WriteDecimal((srcIP >> 8) & 0xFF);
                    Debug.Write(".");
                    Debug.WriteDecimal(srcIP & 0xFF);
                    Debug.Write(":");
                    Debug.WriteDecimal(rSrcPort);
                    Debug.Write(" len=");
                    Debug.WriteDecimal(respLen);
                    Debug.WriteLine();

                    break;
                }
            }
            pollAttempts--;
        }

        if (responseReceived == 0)
        {
            Debug.WriteLine("[virtio-net] TestUdp: No DNS response received (timeout)");
        }

        // Print UDP stats
        ulong udpSent, udpRecv;
        _netStack.GetUdpStats(out udpSent, out udpRecv);
        Debug.Write("[virtio-net] UDP Stats: Sent=");
        Debug.WriteDecimal((uint)udpSent);
        Debug.Write(" Recv=");
        Debug.WriteDecimal((uint)udpRecv);
        Debug.WriteLine();

        return responseReceived;
    }

    // Static counter for allocating BAR addresses
    private static ulong _nextBarAddress;
    private static bool _barAddressInitialized;

    private static void EnsureBarAddressInitialized()
    {
        if (!_barAddressInitialized)
        {
            _nextBarAddress = 0xC0000000;
            _barAddressInitialized = true;
        }
    }

    /// <summary>
    /// Read a PCI BAR, program it if needed, and store the BAR info.
    /// </summary>
    private static unsafe void ReadAndProgramBar(byte bus, byte device, byte function, int barIndex, out PciBar result)
    {
        EnsureBarAddressInitialized();

        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        // Read original BAR value
        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        // Probe the BAR
        PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
        uint sizeLow = PCI.ReadConfig32(bus, device, function, barOffset);
        PCI.WriteConfig32(bus, device, function, barOffset, barValue);

        bool isZeroSize = sizeLow == 0;
        bool isAllOnes = sizeLow == 0xFFFFFFFF;
        bool shouldSkip = isZeroSize || isAllOnes;

        if (shouldSkip)
        {
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = false;
            empty.IsPrefetchable = false;
            result = empty;
            return;
        }

        bool isIO = (sizeLow & 1) != 0;

        // Skip I/O BARs - we only need memory BARs for modern virtio
        if (isIO)
        {
            ReadBar(bus, device, function, barIndex, out result);
            return;
        }

        bool is64Bit = ((sizeLow >> 1) & 3) == 2;
        bool isPrefetchable = (sizeLow & 8) != 0;

        ulong baseAddr = (barValue & 0xFFFFFFF0u) & 0xFFFFFFFFUL;
        uint upperBar = 0;

        if (is64Bit && barIndex < 5)
        {
            upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            baseAddr |= ((ulong)upperBar & 0xFFFFFFFFUL) << 32;
        }

        // Calculate size
        ulong size;
        if (is64Bit && barIndex < 5)
        {
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), 0xFFFFFFFF);
            uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), upperBar);

            ulong sizeMask = (((ulong)sizeHigh & 0xFFFFFFFFUL) << 32) | ((sizeLow & 0xFFFFFFF0u) & 0xFFFFFFFFUL);
            size = ~sizeMask + 1;
        }
        else
        {
            ulong masked = (sizeLow & 0xFFFFFFF0u) & 0xFFFFFFFFUL;
            size = (~masked + 1) & 0xFFFFFFFFUL;
        }

        // Handle non-zero base address (BAR already programmed)
        if (baseAddr != 0)
        {
            PciBar earlyResult;
            earlyResult.Index = barIndex;
            earlyResult.BaseAddress = baseAddr;
            earlyResult.Size = size;
            earlyResult.IsIO = false;
            earlyResult.Is64Bit = is64Bit;
            earlyResult.IsPrefetchable = isPrefetchable;
            result = earlyResult;
            return;
        }

        // Need to program the BAR
        if (size == 0)
        {
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = is64Bit;
            empty.IsPrefetchable = isPrefetchable;
            result = empty;
            return;
        }

        // Align address to BAR size
        ulong alignedAddr = (_nextBarAddress + size - 1) & ~(size - 1);

        // Program the BAR
        if (is64Bit && barIndex < 5)
        {
            uint newLow = (uint)(alignedAddr & 0xFFFFFFF0) | (sizeLow & 0xF);
            uint newHigh = (uint)(alignedAddr >> 32);

            PCI.WriteConfig32(bus, device, function, barOffset, newLow);
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), newHigh);

            baseAddr = alignedAddr;
        }
        else
        {
            uint newValue = (uint)(alignedAddr & 0xFFFFFFF0) | (sizeLow & 0xF);
            PCI.WriteConfig32(bus, device, function, barOffset, newValue);
            baseAddr = alignedAddr & 0xFFFFFFFF;
        }

        _nextBarAddress = alignedAddr + size;

        // Enable memory space decoding
        ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
        if ((cmd & 0x02) == 0)
        {
            cmd |= 0x02;
            PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);
        }

        PciBar local;
        local.Index = barIndex;
        local.BaseAddress = baseAddr;
        local.Size = size;
        local.IsIO = false;
        local.Is64Bit = is64Bit;
        local.IsPrefetchable = isPrefetchable;
        result = local;
    }

    /// <summary>
    /// Read a PCI I/O BAR and its size.
    /// </summary>
    private static void ReadBar(byte bus, byte device, byte function, int barIndex, out PciBar result)
    {
        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        if (barValue == 0)
        {
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = false;
            empty.IsPrefetchable = false;
            result = empty;
            return;
        }

        bool isIO = (barValue & 1) != 0;

        if (isIO)
        {
            ulong baseAddr = barValue & 0xFFFFFFFC;

            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeValue = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size = ~(sizeValue & 0xFFFFFFFC) + 1;
            size &= 0xFFFF;

            PciBar local;
            local.Index = barIndex;
            local.BaseAddress = baseAddr;
            local.Size = size;
            local.IsIO = true;
            local.Is64Bit = false;
            local.IsPrefetchable = false;
            result = local;
        }
        else
        {
            bool is64Bit = ((barValue >> 1) & 3) == 2;
            bool isPrefetchable = (barValue & 8) != 0;

            ulong baseAddr = barValue & 0xFFFFFFF0;
            uint upperBar = 0;

            if (is64Bit && barIndex < 5)
            {
                upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                baseAddr |= (ulong)upperBar << 32;
            }

            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeLow = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size;
            if (is64Bit && barIndex < 5)
            {
                PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), 0xFFFFFFFF);
                uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), upperBar);

                ulong sizeMask = ((ulong)sizeHigh << 32) | (sizeLow & 0xFFFFFFF0);
                size = ~sizeMask + 1;
            }
            else
            {
                size = ~((ulong)(sizeLow & 0xFFFFFFF0)) + 1;
                size &= 0xFFFFFFFF;
            }

            PciBar local;
            local.Index = barIndex;
            local.BaseAddress = baseAddr;
            local.Size = size;
            local.IsIO = false;
            local.Is64Bit = is64Bit;
            local.IsPrefetchable = isPrefetchable;
            result = local;
        }
    }
}
