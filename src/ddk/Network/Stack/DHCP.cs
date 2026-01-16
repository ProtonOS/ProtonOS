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
        response = new DhcpResponse();

        if (data == null || length < MinPacketSize)
            return false;

        // Check operation (must be reply)
        if (data[0] != OpReply)
            return false;

        // Check hardware type
        if (data[1] != HtypeEthernet || data[2] != HlenEthernet)
            return false;

        // Parse XID
        uint xid = ((uint)data[4] << 24) | ((uint)data[5] << 16) |
                   ((uint)data[6] << 8) | data[7];
        if (xid != expectedXid)
        {
            Debug.Write("[DHCP] XID mismatch: got ");
            Debug.WriteHex(xid);
            Debug.Write(" expected ");
            Debug.WriteHex(expectedXid);
            Debug.WriteLine();
            return false;
        }

        // Parse yiaddr (your IP address)
        response.YourIP = ((uint)data[16] << 24) | ((uint)data[17] << 16) |
                          ((uint)data[18] << 8) | data[19];

        // Parse siaddr (server IP) as fallback
        response.ServerIP = ((uint)data[20] << 24) | ((uint)data[21] << 16) |
                            ((uint)data[22] << 8) | data[23];

        // Verify magic cookie at offset 236
        if (data[236] != 0x63 || data[237] != 0x82 ||
            data[238] != 0x53 || data[239] != 0x63)
        {
            Debug.WriteLine("[DHCP] Invalid magic cookie");
            return false;
        }

        // Parse options starting at offset 240
        int offset = 240;
        while (offset < length)
        {
            byte option = data[offset++];

            if (option == OptionEnd)
                break;

            if (option == OptionPad)
                continue;

            if (offset >= length)
                break;

            byte optLen = data[offset++];
            if (offset + optLen > length)
                break;

            switch (option)
            {
                case OptionMessageType:
                    if (optLen >= 1)
                        response.MessageType = data[offset];
                    break;

                case OptionSubnetMask:
                    if (optLen >= 4)
                    {
                        response.SubnetMask = ((uint)data[offset] << 24) |
                                              ((uint)data[offset + 1] << 16) |
                                              ((uint)data[offset + 2] << 8) |
                                              data[offset + 3];
                    }
                    break;

                case OptionRouter:
                    if (optLen >= 4)
                    {
                        response.Gateway = ((uint)data[offset] << 24) |
                                           ((uint)data[offset + 1] << 16) |
                                           ((uint)data[offset + 2] << 8) |
                                           data[offset + 3];
                    }
                    break;

                case OptionDns:
                    if (optLen >= 4)
                    {
                        response.DnsServer = ((uint)data[offset] << 24) |
                                             ((uint)data[offset + 1] << 16) |
                                             ((uint)data[offset + 2] << 8) |
                                             data[offset + 3];
                        if (optLen >= 8)
                        {
                            response.DnsServer2 = ((uint)data[offset + 4] << 24) |
                                                  ((uint)data[offset + 5] << 16) |
                                                  ((uint)data[offset + 6] << 8) |
                                                  data[offset + 7];
                        }
                    }
                    break;

                case OptionLeaseTime:
                    if (optLen >= 4)
                    {
                        response.LeaseTime = ((uint)data[offset] << 24) |
                                             ((uint)data[offset + 1] << 16) |
                                             ((uint)data[offset + 2] << 8) |
                                             data[offset + 3];
                    }
                    break;

                case OptionServerId:
                    if (optLen >= 4)
                    {
                        response.ServerIP = ((uint)data[offset] << 24) |
                                            ((uint)data[offset + 1] << 16) |
                                            ((uint)data[offset + 2] << 8) |
                                            data[offset + 3];
                    }
                    break;
            }

            offset += optLen;
        }

        // Must have a message type
        if (response.MessageType == 0)
        {
            Debug.WriteLine("[DHCP] No message type in response");
            return false;
        }

        return true;
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
