// ProtonOS DDK - Network Stack Manager
// Ties together Ethernet, ARP, and higher protocol layers

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// Network interface configuration.
/// </summary>
public struct NetworkConfig
{
    /// <summary>IPv4 address in host byte order.</summary>
    public uint IPAddress;

    /// <summary>Subnet mask in host byte order.</summary>
    public uint SubnetMask;

    /// <summary>Gateway address in host byte order.</summary>
    public uint Gateway;

    /// <summary>Check if an IP is on the local subnet.</summary>
    public bool IsLocalSubnet(uint ip)
    {
        return (ip & SubnetMask) == (IPAddress & SubnetMask);
    }
}

// Note: IPv4 packet handler delegate removed due to JIT delegate argument limits
// TODO: Add IPv4 handler support when JIT supports more delegate arguments

/// <summary>
/// Received UDP datagram.
/// </summary>
public unsafe struct UdpDatagram
{
    /// <summary>Source IP address (host byte order).</summary>
    public uint SourceIP;

    /// <summary>Source port.</summary>
    public ushort SourcePort;

    /// <summary>Destination port.</summary>
    public ushort DestPort;

    /// <summary>Data buffer.</summary>
    public fixed byte Data[1500];

    /// <summary>Data length.</summary>
    public int Length;

    /// <summary>Whether this slot contains valid data.</summary>
    public bool Valid;
}

/// <summary>
/// Network stack manager - handles Ethernet/ARP/IP processing.
/// </summary>
public unsafe class NetworkStack
{
    // Network device MAC address
    private byte* _macAddress;

    // Network configuration
    private NetworkConfig _config;

    // ARP cache
    private ArpCache _arpCache;

    // Frame buffer for sending
    private byte* _txBuffer;
    private ulong _txBufferPhys;
    private const int TxBufferSize = 1600;

    // Statistics
    private ulong _rxFrames;
    private ulong _txFrames;
    private ulong _arpRequests;
    private ulong _arpReplies;
    private ulong _icmpSent;
    private ulong _icmpReceived;
    private ulong _udpSent;
    private ulong _udpReceived;

    // UDP receive queue (simple ring buffer)
    private const int MaxUdpQueueSize = 16;
    private const int MaxUdpDatagramSize = 1500;
    private UdpDatagram[] _udpQueue;
    private int _udpQueueHead;
    private int _udpQueueTail;
    private int _udpQueueCount;

    // Ping tracking
    private ushort _pingIdentifier;
    private ushort _pingSequence;
    private bool _pingPending;
    private uint _pingTargetIP;
    private ulong _pingTimestamp;

    /// <summary>
    /// Create a new network stack instance.
    /// </summary>
    /// <param name="macAddress">Pointer to MAC address (6 bytes, must remain valid).</param>
    public NetworkStack(byte* macAddress)
    {
        _macAddress = macAddress;
        _arpCache = new ArpCache();

        // Allocate TX buffer
        _txBufferPhys = Memory.AllocatePages(1);
        _txBuffer = (byte*)Memory.PhysToVirt(_txBufferPhys);

        // Initialize UDP queue
        _udpQueue = new UdpDatagram[MaxUdpQueueSize];
        _udpQueueHead = 0;
        _udpQueueTail = 0;
        _udpQueueCount = 0;
    }

    /// <summary>
    /// Configure the network interface.
    /// </summary>
    public void Configure(uint ipAddress, uint subnetMask, uint gateway)
    {
        _config.IPAddress = ipAddress;
        _config.SubnetMask = subnetMask;
        _config.Gateway = gateway;

        Debug.Write("[NetStack] Configured IP: ");
        PrintIP(ipAddress);
        Debug.Write(" Mask: ");
        PrintIP(subnetMask);
        Debug.Write(" Gateway: ");
        PrintIP(gateway);
        Debug.WriteLine();
    }

    /// <summary>
    /// Get the ARP cache.
    /// </summary>
    public ArpCache ArpCache => _arpCache;

    /// <summary>
    /// Get the network configuration.
    /// </summary>
    public NetworkConfig Config => _config;

    /// <summary>
    /// Process a received Ethernet frame.
    /// </summary>
    /// <param name="data">Pointer to frame data.</param>
    /// <param name="length">Length of frame.</param>
    public void ProcessFrame(byte* data, int length)
    {
        _rxFrames++;

        // Parse Ethernet header
        EthernetFrame frame;
        if (!Ethernet.Parse(data, length, out frame))
        {
            Debug.WriteLine("[NetStack] Failed to parse Ethernet frame");
            return;
        }

        // Check if frame is for us (unicast to our MAC, broadcast, or multicast)
        bool isForUs = Ethernet.CompareMac(frame.DestinationMac, _macAddress) ||
                       Ethernet.IsBroadcast(frame.DestinationMac);

        if (!isForUs)
            return;

        // Dispatch based on EtherType
        switch (frame.EtherType)
        {
            case EtherType.ARP:
                ProcessArp(frame.Payload, frame.PayloadLength, frame.SourceMac);
                break;

            case EtherType.IPv4:
                ProcessIPv4(frame.Payload, frame.PayloadLength);
                break;

            default:
                // Unknown protocol - ignore
                break;
        }
    }

    /// <summary>
    /// Process an ARP packet.
    /// </summary>
    private void ProcessArp(byte* data, int length, byte* senderMac)
    {
        ArpPacket arpPacket;
        if (!ARP.Parse(data, length, out arpPacket))
        {
            Debug.WriteLine("[NetStack] Failed to parse ARP packet");
            return;
        }

        uint senderIP = ARP.GetSenderIP(&arpPacket);
        uint targetIP = ARP.GetTargetIP(&arpPacket);

        // Always update cache with sender info (ARP snooping)
        // Note: SenderMac is a fixed buffer, so we can take its address directly
        _arpCache.Update(senderIP, arpPacket.SenderMac);

        if (arpPacket.Operation == ArpOperation.Request)
        {
            _arpRequests++;

            // Is this request for our IP?
            if (targetIP == _config.IPAddress)
            {
                Debug.Write("[NetStack] ARP request for our IP from ");
                PrintIP(senderIP);
                Debug.WriteLine();

                // Send ARP reply
                SendArpReply(senderIP, senderMac);
            }
        }
        else if (arpPacket.Operation == ArpOperation.Reply)
        {
            _arpReplies++;

            Debug.Write("[NetStack] ARP reply: ");
            PrintIP(senderIP);
            Debug.Write(" is at ");
            PrintMac(senderMac);
            Debug.WriteLine();
        }
    }

    /// <summary>
    /// Process an IPv4 packet.
    /// </summary>
    private void ProcessIPv4(byte* data, int length)
    {
        // Basic IPv4 header validation
        if (length < 20)
            return;

        byte version = (byte)(data[0] >> 4);
        if (version != 4)
            return;

        byte headerLen = (byte)((data[0] & 0x0F) * 4);
        if (headerLen < 20 || headerLen > length)
            return;

        // Extract addresses and protocol
        uint srcIP = ((uint)data[12] << 24) | ((uint)data[13] << 16) |
                     ((uint)data[14] << 8) | data[15];
        uint destIP = ((uint)data[16] << 24) | ((uint)data[17] << 16) |
                      ((uint)data[18] << 8) | data[19];
        byte protocol = data[9];

        // Check if packet is for us
        if (destIP != _config.IPAddress && destIP != 0xFFFFFFFF)
            return;

        // Dispatch based on protocol
        byte* payload = data + headerLen;
        int payloadLen = length - headerLen;

        switch (protocol)
        {
            case ICMP.ProtocolNumber:
                ProcessIcmp(payload, payloadLen, srcIP);
                break;

            case UDP.ProtocolNumber:
                ProcessUdp(payload, payloadLen, srcIP, destIP);
                break;

            default:
                Debug.Write("[NetStack] IPv4 packet from ");
                PrintIP(srcIP);
                Debug.Write(" proto=");
                Debug.WriteDecimal(protocol);
                Debug.WriteLine();
                break;
        }
    }

    /// <summary>
    /// Process an ICMP packet.
    /// </summary>
    private void ProcessIcmp(byte* data, int length, uint srcIP)
    {
        _icmpReceived++;

        IcmpPacket packet;
        if (!ICMP.Parse(data, length, out packet))
        {
            Debug.WriteLine("[NetStack] Failed to parse ICMP packet");
            return;
        }

        // Verify checksum
        if (!ICMP.VerifyChecksum(data, length))
        {
            Debug.WriteLine("[NetStack] ICMP checksum invalid");
            return;
        }

        switch (packet.Type)
        {
            case IcmpType.EchoRequest:
                Debug.Write("[NetStack] ICMP Echo Request from ");
                PrintIP(srcIP);
                Debug.Write(" id=");
                Debug.WriteDecimal(packet.Identifier);
                Debug.Write(" seq=");
                Debug.WriteDecimal(packet.Sequence);
                Debug.WriteLine();

                // Send echo reply
                SendEchoReply(srcIP, packet.Identifier, packet.Sequence,
                              packet.Payload, packet.PayloadLength);
                break;

            case IcmpType.EchoReply:
                Debug.Write("[NetStack] ICMP Echo Reply from ");
                PrintIP(srcIP);
                Debug.Write(" id=");
                Debug.WriteDecimal(packet.Identifier);
                Debug.Write(" seq=");
                Debug.WriteDecimal(packet.Sequence);
                Debug.WriteLine();

                // Check if this is our pending ping
                if (_pingPending && srcIP == _pingTargetIP &&
                    packet.Identifier == _pingIdentifier &&
                    packet.Sequence == _pingSequence)
                {
                    _pingPending = false;
                    Debug.WriteLine("[NetStack] Ping reply received!");
                }
                break;

            default:
                Debug.Write("[NetStack] ICMP type=");
                Debug.WriteDecimal(packet.Type);
                Debug.Write(" from ");
                PrintIP(srcIP);
                Debug.WriteLine();
                break;
        }
    }

    /// <summary>
    /// Process a UDP packet.
    /// </summary>
    private void ProcessUdp(byte* data, int length, uint srcIP, uint destIP)
    {
        _udpReceived++;

        UdpPacket packet;
        if (!UDP.Parse(data, length, out packet))
        {
            Debug.WriteLine("[NetStack] Failed to parse UDP packet");
            return;
        }

        // Verify checksum if present
        if (!UDP.VerifyChecksum(data, length, srcIP, destIP))
        {
            Debug.WriteLine("[NetStack] UDP checksum invalid");
            return;
        }

        Debug.Write("[NetStack] UDP from ");
        PrintIP(srcIP);
        Debug.Write(":");
        Debug.WriteDecimal(packet.SourcePort);
        Debug.Write(" to port ");
        Debug.WriteDecimal(packet.DestPort);
        Debug.Write(" len=");
        Debug.WriteDecimal(packet.PayloadLength);
        Debug.WriteLine();

        // Queue the datagram for application processing
        if (_udpQueueCount < MaxUdpQueueSize && packet.PayloadLength <= MaxUdpDatagramSize)
        {
            int idx = _udpQueueTail;
            _udpQueue[idx].SourceIP = srcIP;
            _udpQueue[idx].SourcePort = packet.SourcePort;
            _udpQueue[idx].DestPort = packet.DestPort;
            _udpQueue[idx].Length = packet.PayloadLength;
            _udpQueue[idx].Valid = true;

            // Copy data into the fixed buffer
            fixed (byte* dest = _udpQueue[idx].Data)
            {
                for (int i = 0; i < packet.PayloadLength; i++)
                    dest[i] = packet.Payload[i];
            }

            _udpQueueTail = (_udpQueueTail + 1) % MaxUdpQueueSize;
            _udpQueueCount++;
        }
        else
        {
            Debug.WriteLine("[NetStack] UDP queue full, dropping packet");
        }
    }

    /// <summary>
    /// Send a UDP datagram.
    /// </summary>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <param name="srcPort">Source port.</param>
    /// <param name="destPort">Destination port.</param>
    /// <param name="data">Payload data.</param>
    /// <param name="dataLen">Payload length.</param>
    /// <returns>Frame length, or 0 if MAC unknown (need ARP first).</returns>
    public int SendUdp(uint destIP, ushort srcPort, ushort destPort, byte* data, int dataLen)
    {
        if (dataLen > UDP.MaxPayloadSize)
            return 0;

        // Build UDP packet in a temp buffer
        int maxUdpLen = UdpHeader.Size + dataLen;
        byte* udpBuffer = stackalloc byte[maxUdpLen];
        int udpLen = UDP.BuildPacketWithChecksum(udpBuffer, srcPort, destPort, data, dataLen,
                                                  _config.IPAddress, destIP);
        if (udpLen == 0)
            return 0;

        // Build IPv4 frame with UDP payload
        int frameLen = BuildIPv4Frame(destIP, UDP.ProtocolNumber, udpBuffer, udpLen);
        if (frameLen == 0)
        {
            Debug.Write("[NetStack] UDP to ");
            PrintIP(destIP);
            Debug.WriteLine(" - need ARP first");
            return 0;
        }

        _udpSent++;

        Debug.Write("[NetStack] Sent UDP to ");
        PrintIP(destIP);
        Debug.Write(":");
        Debug.WriteDecimal(destPort);
        Debug.Write(" len=");
        Debug.WriteDecimal(dataLen);
        Debug.WriteLine();

        return frameLen;
    }

    /// <summary>
    /// Check if there are UDP datagrams available to receive.
    /// </summary>
    public int UdpAvailable() => _udpQueueCount;

    /// <summary>
    /// Receive a UDP datagram from the queue.
    /// </summary>
    /// <param name="srcIP">Receives source IP address.</param>
    /// <param name="srcPort">Receives source port.</param>
    /// <param name="destPort">Receives destination port.</param>
    /// <param name="buffer">Buffer to receive data.</param>
    /// <param name="bufferLen">Buffer size.</param>
    /// <returns>Number of bytes received, or 0 if no data available.</returns>
    public int ReceiveUdp(out uint srcIP, out ushort srcPort, out ushort destPort,
                          byte* buffer, int bufferLen)
    {
        srcIP = 0;
        srcPort = 0;
        destPort = 0;

        if (_udpQueueCount == 0)
            return 0;

        int idx = _udpQueueHead;
        if (!_udpQueue[idx].Valid)
            return 0;

        srcIP = _udpQueue[idx].SourceIP;
        srcPort = _udpQueue[idx].SourcePort;
        destPort = _udpQueue[idx].DestPort;
        int dataLen = _udpQueue[idx].Length;

        // Copy data from the fixed buffer
        int copyLen = dataLen < bufferLen ? dataLen : bufferLen;
        fixed (byte* src = _udpQueue[idx].Data)
        {
            for (int i = 0; i < copyLen; i++)
                buffer[i] = src[i];
        }

        // Mark slot as empty
        _udpQueue[idx].Valid = false;
        _udpQueueHead = (_udpQueueHead + 1) % MaxUdpQueueSize;
        _udpQueueCount--;

        return copyLen;
    }

    /// <summary>
    /// Send an ICMP Echo Request (ping).
    /// </summary>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <returns>Frame length, or 0 if MAC unknown (need ARP first).</returns>
    public int SendPing(uint destIP)
    {
        // Increment sequence for each ping
        _pingSequence++;

        // Use a fixed identifier for this session
        if (_pingIdentifier == 0)
            _pingIdentifier = 0x1234;

        // Build ICMP Echo Request in a temp buffer
        byte* icmpBuffer = stackalloc byte[64];
        int icmpLen = ICMP.BuildEchoRequest(icmpBuffer, _pingIdentifier, _pingSequence, null, 0);
        if (icmpLen == 0)
            return 0;

        // Build IPv4 frame with ICMP payload
        int frameLen = BuildIPv4Frame(destIP, ICMP.ProtocolNumber, icmpBuffer, icmpLen);
        if (frameLen == 0)
        {
            // Need ARP resolution
            Debug.Write("[NetStack] Ping to ");
            PrintIP(destIP);
            Debug.WriteLine(" - need ARP first");
            return 0;
        }

        // Track pending ping
        _pingPending = true;
        _pingTargetIP = destIP;
        _icmpSent++;

        Debug.Write("[NetStack] Sent ping to ");
        PrintIP(destIP);
        Debug.Write(" id=");
        Debug.WriteDecimal(_pingIdentifier);
        Debug.Write(" seq=");
        Debug.WriteDecimal(_pingSequence);
        Debug.WriteLine();

        return frameLen;
    }

    /// <summary>
    /// Send an ICMP Echo Reply.
    /// </summary>
    private int SendEchoReply(uint destIP, ushort identifier, ushort sequence,
                               byte* payload, int payloadLen)
    {
        // Build ICMP Echo Reply in a temp buffer
        int maxIcmpLen = IcmpEchoHeader.Size + payloadLen;
        byte* icmpBuffer = stackalloc byte[maxIcmpLen];
        int icmpLen = ICMP.BuildEchoReply(icmpBuffer, identifier, sequence, payload, payloadLen);
        if (icmpLen == 0)
            return 0;

        // Build IPv4 frame with ICMP payload
        int frameLen = BuildIPv4Frame(destIP, ICMP.ProtocolNumber, icmpBuffer, icmpLen);
        if (frameLen == 0)
        {
            Debug.Write("[NetStack] Echo reply to ");
            PrintIP(destIP);
            Debug.WriteLine(" - need ARP first");
            return 0;
        }

        _icmpSent++;

        Debug.Write("[NetStack] Sent echo reply to ");
        PrintIP(destIP);
        Debug.WriteLine();

        return frameLen;
    }

    /// <summary>
    /// Check if a ping is pending.
    /// </summary>
    public bool IsPingPending() => _pingPending;

    /// <summary>
    /// Get ping statistics.
    /// </summary>
    public void GetPingStats(out ushort identifier, out ushort sequence, out bool pending)
    {
        identifier = _pingIdentifier;
        sequence = _pingSequence;
        pending = _pingPending;
    }

    /// <summary>
    /// Send an ARP request.
    /// </summary>
    /// <param name="targetIP">IP address to resolve (host byte order).</param>
    /// <returns>Frame length sent, or 0 on error.</returns>
    public int SendArpRequest(uint targetIP)
    {
        // Build ARP request
        byte* arpData = _txBuffer + EthernetHeader.Size;
        int arpLen = ARP.BuildRequest(arpData, _macAddress, _config.IPAddress, targetIP);
        if (arpLen == 0)
            return 0;

        // Build Ethernet frame (broadcast)
        byte* broadcastMac = stackalloc byte[6];
        Ethernet.SetBroadcast(broadcastMac);

        int frameLen = Ethernet.BuildHeader(_txBuffer, broadcastMac, _macAddress, EtherType.ARP);
        if (frameLen == 0)
            return 0;

        // Total frame length
        int totalLen = EthernetHeader.Size + arpLen;

        // Pad to minimum frame size
        if (totalLen < EthernetHeader.MinFrameSize)
        {
            for (int i = totalLen; i < EthernetHeader.MinFrameSize; i++)
                _txBuffer[i] = 0;
            totalLen = EthernetHeader.MinFrameSize;
        }

        Debug.Write("[NetStack] Sending ARP request for ");
        PrintIP(targetIP);
        Debug.WriteLine();

        _txFrames++;
        return totalLen;
    }

    /// <summary>
    /// Get the TX buffer after building a frame.
    /// </summary>
    public byte* GetTxBuffer() => _txBuffer;

    /// <summary>
    /// Send an ARP reply.
    /// </summary>
    private void SendArpReply(uint targetIP, byte* targetMac)
    {
        // Build ARP reply
        byte* arpData = _txBuffer + EthernetHeader.Size;
        int arpLen = ARP.BuildReply(arpData, _macAddress, _config.IPAddress, targetMac, targetIP);
        if (arpLen == 0)
            return;

        // Build Ethernet frame (unicast to requester)
        Ethernet.BuildHeader(_txBuffer, targetMac, _macAddress, EtherType.ARP);

        // Total frame length
        int totalLen = EthernetHeader.Size + arpLen;
        if (totalLen < EthernetHeader.MinFrameSize)
            totalLen = EthernetHeader.MinFrameSize;

        _txFrames++;

        Debug.Write("[NetStack] Sending ARP reply to ");
        PrintIP(targetIP);
        Debug.WriteLine();

        // Note: Caller needs to actually transmit _txBuffer
    }

    /// <summary>
    /// Resolve an IP address to a MAC address.
    /// </summary>
    /// <param name="ip">IP address to resolve (host byte order).</param>
    /// <param name="mac">Buffer to receive MAC address (6 bytes).</param>
    /// <returns>True if resolved from cache, false if ARP request needed.</returns>
    public bool ResolveIP(uint ip, byte* mac)
    {
        // Check if on local subnet
        uint nextHop = _config.IsLocalSubnet(ip) ? ip : _config.Gateway;

        // Look up in cache
        return _arpCache.Lookup(nextHop, mac);
    }

    /// <summary>
    /// Build an IPv4 frame for transmission.
    /// </summary>
    /// <param name="destIP">Destination IP (host byte order).</param>
    /// <param name="protocol">IP protocol number.</param>
    /// <param name="payload">Payload data.</param>
    /// <param name="payloadLen">Payload length.</param>
    /// <returns>Frame length, or 0 if MAC unknown (need ARP).</returns>
    public int BuildIPv4Frame(uint destIP, byte protocol, byte* payload, int payloadLen)
    {
        // Determine next hop
        uint nextHop = _config.IsLocalSubnet(destIP) ? destIP : _config.Gateway;

        // Resolve MAC address
        byte* destMac = stackalloc byte[6];
        if (!_arpCache.Lookup(nextHop, destMac))
        {
            // Need ARP resolution
            return 0;
        }

        // Build IP header
        int ipHeaderLen = 20;
        int totalIpLen = ipHeaderLen + payloadLen;

        byte* ipHeader = _txBuffer + EthernetHeader.Size;

        // Version (4) and header length (5 = 20 bytes)
        ipHeader[0] = 0x45;
        // DSCP/ECN
        ipHeader[1] = 0;
        // Total length (big-endian)
        ipHeader[2] = (byte)(totalIpLen >> 8);
        ipHeader[3] = (byte)totalIpLen;
        // Identification
        ipHeader[4] = 0;
        ipHeader[5] = 0;
        // Flags and fragment offset
        ipHeader[6] = 0x40; // Don't fragment
        ipHeader[7] = 0;
        // TTL
        ipHeader[8] = 64;
        // Protocol
        ipHeader[9] = protocol;
        // Header checksum (initially zero, calculate after)
        ipHeader[10] = 0;
        ipHeader[11] = 0;
        // Source IP
        ipHeader[12] = (byte)(_config.IPAddress >> 24);
        ipHeader[13] = (byte)(_config.IPAddress >> 16);
        ipHeader[14] = (byte)(_config.IPAddress >> 8);
        ipHeader[15] = (byte)_config.IPAddress;
        // Destination IP
        ipHeader[16] = (byte)(destIP >> 24);
        ipHeader[17] = (byte)(destIP >> 16);
        ipHeader[18] = (byte)(destIP >> 8);
        ipHeader[19] = (byte)destIP;

        // Calculate header checksum
        uint sum = 0;
        for (int i = 0; i < ipHeaderLen; i += 2)
        {
            sum += (uint)((ipHeader[i] << 8) | ipHeader[i + 1]);
        }
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        ushort checksum = (ushort)~sum;
        ipHeader[10] = (byte)(checksum >> 8);
        ipHeader[11] = (byte)checksum;

        // Copy payload
        byte* payloadDest = ipHeader + ipHeaderLen;
        for (int i = 0; i < payloadLen; i++)
            payloadDest[i] = payload[i];

        // Build Ethernet header
        Ethernet.BuildHeader(_txBuffer, destMac, _macAddress, EtherType.IPv4);

        int totalLen = EthernetHeader.Size + totalIpLen;
        if (totalLen < EthernetHeader.MinFrameSize)
        {
            for (int i = totalLen; i < EthernetHeader.MinFrameSize; i++)
                _txBuffer[i] = 0;
            totalLen = EthernetHeader.MinFrameSize;
        }

        _txFrames++;
        return totalLen;
    }

    /// <summary>
    /// Print an IP address.
    /// </summary>
    private void PrintIP(uint ip)
    {
        Debug.WriteDecimal((ip >> 24) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 16) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 8) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal(ip & 0xFF);
    }

    /// <summary>
    /// Print a MAC address.
    /// </summary>
    private void PrintMac(byte* mac)
    {
        for (int i = 0; i < 6; i++)
        {
            Debug.WriteHex(mac[i]);
            if (i < 5) Debug.Write(":");
        }
    }

    /// <summary>
    /// Get statistics.
    /// </summary>
    public void GetStats(out ulong rxFrames, out ulong txFrames, out ulong arpReq, out ulong arpRep)
    {
        rxFrames = _rxFrames;
        txFrames = _txFrames;
        arpReq = _arpRequests;
        arpRep = _arpReplies;
    }

    /// <summary>
    /// Get ICMP statistics.
    /// </summary>
    public void GetIcmpStats(out ulong sent, out ulong received)
    {
        sent = _icmpSent;
        received = _icmpReceived;
    }

    /// <summary>
    /// Get UDP statistics.
    /// </summary>
    public void GetUdpStats(out ulong sent, out ulong received)
    {
        sent = _udpSent;
        received = _udpReceived;
    }
}
