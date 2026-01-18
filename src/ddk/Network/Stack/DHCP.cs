// ProtonOS DDK - DHCP Protocol Implementation
// DHCP packet building and parsing for automatic network configuration.

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// DHCP response data parsed from OFFER or ACK packets.
/// </summary>
public struct DhcpResponse
{
    public byte MessageType;     // OFFER, ACK, NAK
    public uint YourIP;          // Offered/assigned IP (host byte order)
    public uint ServerIP;        // DHCP server IP (host byte order)
    public uint SubnetMask;      // From option 1 (host byte order)
    public uint Gateway;         // From option 3 (host byte order)
    public uint DnsServer;       // From option 6 (host byte order)
    public uint DnsServer2;      // Second DNS if provided (host byte order)
    public uint LeaseTime;       // In seconds
    public uint RenewalTime;     // T1: seconds until renewal (option 58, default 0.5 * lease)
    public uint RebindingTime;   // T2: seconds until rebinding (option 59, default 0.875 * lease)
}

/// <summary>
/// DHCP protocol constants and packet building/parsing.
/// DHCP uses UDP ports 67 (server) and 68 (client).
/// </summary>
public static unsafe class DHCP
{
    // DHCP ports
    public const ushort ServerPort = 67;
    public const ushort ClientPort = 68;

    // BOOTP header size (without options)
    public const int HeaderSize = 236;

    // Minimum DHCP packet size (header + magic cookie + message type option + end)
    public const int MinPacketSize = 244;

    // Maximum DHCP packet size
    public const int MaxPacketSize = 576;

    // BOOTP operation codes
    public const byte OpRequest = 1;   // Client to server
    public const byte OpReply = 2;     // Server to client

    // Hardware type (Ethernet)
    public const byte HtypeEthernet = 1;
    public const byte HlenEthernet = 6;

    // DHCP message types (option 53)
    public const byte MessageDiscover = 1;
    public const byte MessageOffer = 2;
    public const byte MessageRequest = 3;
    public const byte MessageDecline = 4;
    public const byte MessageAck = 5;
    public const byte MessageNak = 6;
    public const byte MessageRelease = 7;
    public const byte MessageInform = 8;

    // DHCP options
    public const byte OptionPad = 0;
    public const byte OptionSubnetMask = 1;
    public const byte OptionRouter = 3;
    public const byte OptionDns = 6;
    public const byte OptionHostname = 12;
    public const byte OptionRequestedIP = 50;
    public const byte OptionLeaseTime = 51;
    public const byte OptionMessageType = 53;
    public const byte OptionServerId = 54;
    public const byte OptionParamRequest = 55;
    public const byte OptionRenewalTime = 58;    // T1: Renewal time
    public const byte OptionRebindingTime = 59;  // T2: Rebinding time
    public const byte OptionEnd = 255;

    // DHCP magic cookie (identifies DHCP vs BOOTP)
    public const uint MagicCookie = 0x63825363;

    // Broadcast flag
    public const ushort FlagBroadcast = 0x8000;

    /// <summary>
    /// Build a DHCP DISCOVER packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to (must be at least MaxPacketSize).</param>
    /// <param name="xid">Transaction ID.</param>
    /// <param name="macAddress">Client MAC address (6 bytes).</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildDiscover(byte* buffer, uint xid, byte* macAddress)
    {
        if (buffer == null || macAddress == null)
            return 0;

        // Clear buffer
        for (int i = 0; i < MaxPacketSize; i++)
            buffer[i] = 0;

        int offset = 0;

        // BOOTP header
        buffer[offset++] = OpRequest;        // op
        buffer[offset++] = HtypeEthernet;    // htype
        buffer[offset++] = HlenEthernet;     // hlen
        buffer[offset++] = 0;                // hops

        // xid (4 bytes, network byte order)
        buffer[offset++] = (byte)(xid >> 24);
        buffer[offset++] = (byte)(xid >> 16);
        buffer[offset++] = (byte)(xid >> 8);
        buffer[offset++] = (byte)xid;

        // secs (2 bytes)
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // flags (2 bytes) - set broadcast flag (0x8000)
        buffer[offset++] = 0x80;  // High byte
        buffer[offset++] = 0x00;  // Low byte

        // ciaddr (4 bytes) - 0.0.0.0
        offset += 4;

        // yiaddr (4 bytes) - 0.0.0.0
        offset += 4;

        // siaddr (4 bytes) - 0.0.0.0
        offset += 4;

        // giaddr (4 bytes) - 0.0.0.0
        offset += 4;

        // chaddr (16 bytes) - client MAC + padding
        for (int i = 0; i < 6; i++)
            buffer[offset++] = macAddress[i];
        offset += 10;  // padding

        // sname (64 bytes) - server hostname, unused
        offset += 64;

        // file (128 bytes) - boot filename, unused
        offset += 128;

        // Magic cookie (4 bytes)
        buffer[offset++] = 0x63;
        buffer[offset++] = 0x82;
        buffer[offset++] = 0x53;
        buffer[offset++] = 0x63;

        // DHCP options

        // Option 53: DHCP Message Type = DISCOVER
        buffer[offset++] = OptionMessageType;
        buffer[offset++] = 1;  // length
        buffer[offset++] = MessageDiscover;

        // Option 55: Parameter Request List
        buffer[offset++] = OptionParamRequest;
        buffer[offset++] = 4;  // length
        buffer[offset++] = OptionSubnetMask;
        buffer[offset++] = OptionRouter;
        buffer[offset++] = OptionDns;
        buffer[offset++] = OptionLeaseTime;

        // Option 255: End
        buffer[offset++] = OptionEnd;

        // Pad to minimum BOOTP size (300 bytes is common)
        if (offset < 300)
            offset = 300;

        return offset;
    }

    /// <summary>
    /// Build a DHCP REQUEST packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="xid">Transaction ID.</param>
    /// <param name="macAddress">Client MAC address (6 bytes).</param>
    /// <param name="requestedIP">IP address to request (host byte order).</param>
    /// <param name="serverIP">DHCP server IP (host byte order).</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildRequest(byte* buffer, uint xid, byte* macAddress,
                                   uint requestedIP, uint serverIP)
    {
        if (buffer == null || macAddress == null)
            return 0;

        // Clear buffer
        for (int i = 0; i < MaxPacketSize; i++)
            buffer[i] = 0;

        int offset = 0;

        // BOOTP header (same as DISCOVER)
        buffer[offset++] = OpRequest;
        buffer[offset++] = HtypeEthernet;
        buffer[offset++] = HlenEthernet;
        buffer[offset++] = 0;

        // xid
        buffer[offset++] = (byte)(xid >> 24);
        buffer[offset++] = (byte)(xid >> 16);
        buffer[offset++] = (byte)(xid >> 8);
        buffer[offset++] = (byte)xid;

        // secs
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // flags - broadcast (0x8000)
        buffer[offset++] = 0x80;  // High byte
        buffer[offset++] = 0x00;  // Low byte

        // ciaddr - 0.0.0.0 (we don't have IP yet)
        offset += 4;

        // yiaddr
        offset += 4;

        // siaddr
        offset += 4;

        // giaddr
        offset += 4;

        // chaddr
        for (int i = 0; i < 6; i++)
            buffer[offset++] = macAddress[i];
        offset += 10;

        // sname
        offset += 64;

        // file
        offset += 128;

        // Magic cookie
        buffer[offset++] = 0x63;
        buffer[offset++] = 0x82;
        buffer[offset++] = 0x53;
        buffer[offset++] = 0x63;

        // DHCP options

        // Option 53: DHCP Message Type = REQUEST
        buffer[offset++] = OptionMessageType;
        buffer[offset++] = 1;
        buffer[offset++] = MessageRequest;

        // Option 50: Requested IP Address
        buffer[offset++] = OptionRequestedIP;
        buffer[offset++] = 4;
        buffer[offset++] = (byte)(requestedIP >> 24);
        buffer[offset++] = (byte)(requestedIP >> 16);
        buffer[offset++] = (byte)(requestedIP >> 8);
        buffer[offset++] = (byte)requestedIP;

        // Option 54: Server Identifier
        buffer[offset++] = OptionServerId;
        buffer[offset++] = 4;
        buffer[offset++] = (byte)(serverIP >> 24);
        buffer[offset++] = (byte)(serverIP >> 16);
        buffer[offset++] = (byte)(serverIP >> 8);
        buffer[offset++] = (byte)serverIP;

        // Option 55: Parameter Request List
        buffer[offset++] = OptionParamRequest;
        buffer[offset++] = 4;
        buffer[offset++] = OptionSubnetMask;
        buffer[offset++] = OptionRouter;
        buffer[offset++] = OptionDns;
        buffer[offset++] = OptionLeaseTime;

        // Option 255: End
        buffer[offset++] = OptionEnd;

        // Pad to minimum size
        if (offset < 300)
            offset = 300;

        return offset;
    }

    /// <summary>
    /// Parse a DHCP response (OFFER, ACK, or NAK).
    /// </summary>
    /// <param name="data">Packet data.</param>
    /// <param name="length">Packet length.</param>
    /// <param name="expectedXid">Expected transaction ID.</param>
    /// <param name="response">Output: Parsed response data.</param>
    /// <returns>True if successfully parsed, false on error.</returns>
    public static bool ParseResponse(byte* data, int length, uint expectedXid,
                                     out DhcpResponse response)
    {
        // Store parameters in locals to prevent JIT issues with complex expressions
        byte* pData = data;
        int len = length;
        uint xid = expectedXid;

        response = default;

        if (pData == null)
            return false;
        if (len < MinPacketSize)
            return false;

        // Verify XID matches
        uint packetXid = ((uint)pData[4] << 24) | ((uint)pData[5] << 16) |
                         ((uint)pData[6] << 8) | pData[7];
        if (packetXid != xid)
            return false;

        // Verify this is a reply (op == 2)
        if (pData[0] != OpReply)
            return false;

        // Parse yiaddr (your IP address) at offset 16
        response.YourIP = ((uint)pData[16] << 24) | ((uint)pData[17] << 16) |
                          ((uint)pData[18] << 8) | pData[19];

        // Parse siaddr (server IP) at offset 20
        response.ServerIP = ((uint)pData[20] << 24) | ((uint)pData[21] << 16) |
                            ((uint)pData[22] << 8) | pData[23];

        // Check magic cookie at offset 236
        int optOffset = HeaderSize;
        if (pData[optOffset] != 0x63 || pData[optOffset + 1] != 0x82 ||
            pData[optOffset + 2] != 0x53 || pData[optOffset + 3] != 0x63)
            return false;

        optOffset += 4;  // Skip magic cookie

        // Parse options
        while (optOffset < len)
        {
            byte optCode = pData[optOffset++];

            if (optCode == OptionEnd)
                break;

            if (optCode == OptionPad)
                continue;

            if (optOffset >= len)
                break;

            byte optLen = pData[optOffset++];

            if (optOffset + optLen > len)
                break;

            if (optCode == OptionMessageType && optLen >= 1)
            {
                response.MessageType = pData[optOffset];
            }
            else if (optCode == OptionSubnetMask && optLen >= 4)
            {
                response.SubnetMask = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                      ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
            }
            else if (optCode == OptionRouter && optLen >= 4)
            {
                response.Gateway = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                   ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
            }
            else if (optCode == OptionLeaseTime && optLen >= 4)
            {
                response.LeaseTime = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                     ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
            }
            else if (optCode == OptionDns && optLen >= 4)
            {
                response.DnsServer = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                     ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
                if (optLen >= 8)
                {
                    response.DnsServer2 = ((uint)pData[optOffset + 4] << 24) | ((uint)pData[optOffset + 5] << 16) |
                                          ((uint)pData[optOffset + 6] << 8) | pData[optOffset + 7];
                }
            }
            else if (optCode == OptionRenewalTime && optLen >= 4)
            {
                response.RenewalTime = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                       ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
            }
            else if (optCode == OptionRebindingTime && optLen >= 4)
            {
                response.RebindingTime = ((uint)pData[optOffset] << 24) | ((uint)pData[optOffset + 1] << 16) |
                                         ((uint)pData[optOffset + 2] << 8) | pData[optOffset + 3];
            }

            optOffset += optLen;
        }

        return true;
    }

    /// <summary>
    /// Build a DHCP REQUEST packet for lease renewal (RENEWING/REBINDING state).
    /// Per RFC 2131: ciaddr is set to current IP, no requested-ip or server-id options.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="xid">Transaction ID.</param>
    /// <param name="macAddress">Client MAC address (6 bytes).</param>
    /// <param name="clientIP">Current client IP address (host byte order).</param>
    /// <param name="broadcast">True for REBINDING (broadcast), false for RENEWING (unicast).</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildRenewalRequest(byte* buffer, uint xid, byte* macAddress,
                                          uint clientIP, bool broadcast)
    {
        if (buffer == null || macAddress == null)
            return 0;

        // Clear buffer
        for (int i = 0; i < MaxPacketSize; i++)
            buffer[i] = 0;

        int offset = 0;

        // BOOTP header
        buffer[offset++] = OpRequest;
        buffer[offset++] = HtypeEthernet;
        buffer[offset++] = HlenEthernet;
        buffer[offset++] = 0;  // hops

        // xid
        buffer[offset++] = (byte)(xid >> 24);
        buffer[offset++] = (byte)(xid >> 16);
        buffer[offset++] = (byte)(xid >> 8);
        buffer[offset++] = (byte)xid;

        // secs
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // flags - broadcast flag only for REBINDING
        if (broadcast)
        {
            buffer[offset++] = 0x80;  // High byte of 0x8000
            buffer[offset++] = 0x00;
        }
        else
        {
            buffer[offset++] = 0;
            buffer[offset++] = 0;
        }

        // ciaddr - set to current IP (we have a lease)
        buffer[offset++] = (byte)(clientIP >> 24);
        buffer[offset++] = (byte)(clientIP >> 16);
        buffer[offset++] = (byte)(clientIP >> 8);
        buffer[offset++] = (byte)clientIP;

        // yiaddr
        offset += 4;

        // siaddr
        offset += 4;

        // giaddr
        offset += 4;

        // chaddr
        for (int i = 0; i < 6; i++)
            buffer[offset++] = macAddress[i];
        offset += 10;

        // sname
        offset += 64;

        // file
        offset += 128;

        // Magic cookie
        buffer[offset++] = 0x63;
        buffer[offset++] = 0x82;
        buffer[offset++] = 0x53;
        buffer[offset++] = 0x63;

        // DHCP options

        // Option 53: DHCP Message Type = REQUEST
        buffer[offset++] = OptionMessageType;
        buffer[offset++] = 1;
        buffer[offset++] = MessageRequest;

        // NOTE: Per RFC 2131, renewal requests do NOT include:
        // - Option 50 (Requested IP Address) - ciaddr is used instead
        // - Option 54 (Server Identifier) - any server can respond in REBINDING

        // Option 55: Parameter Request List
        buffer[offset++] = OptionParamRequest;
        buffer[offset++] = 6;  // length
        buffer[offset++] = OptionSubnetMask;
        buffer[offset++] = OptionRouter;
        buffer[offset++] = OptionDns;
        buffer[offset++] = OptionLeaseTime;
        buffer[offset++] = OptionRenewalTime;
        buffer[offset++] = OptionRebindingTime;

        // Option 255: End
        buffer[offset++] = OptionEnd;

        // Pad to minimum size
        if (offset < 300)
            offset = 300;

        return offset;
    }

    /// <summary>
    /// Print IP address for debugging.
    /// </summary>
    public static void PrintIP(uint ip)
    {
        Debug.WriteDecimal((ip >> 24) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 16) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 8) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal(ip & 0xFF);
    }
}
