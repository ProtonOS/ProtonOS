// ProtonOS DDK - UDP Protocol (L4)
// Handles UDP packet parsing and building

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// UDP header structure (8 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UdpHeader
{
    /// <summary>Source port (big-endian).</summary>
    public ushort SourcePortRaw;

    /// <summary>Destination port (big-endian).</summary>
    public ushort DestPortRaw;

    /// <summary>Length including header (big-endian).</summary>
    public ushort LengthRaw;

    /// <summary>Checksum (big-endian), 0 = not computed.</summary>
    public ushort ChecksumRaw;

    /// <summary>Header size in bytes.</summary>
    public const int Size = 8;

    /// <summary>Get source port in host byte order.</summary>
    public ushort SourcePort => SwapBytes(SourcePortRaw);

    /// <summary>Get destination port in host byte order.</summary>
    public ushort DestPort => SwapBytes(DestPortRaw);

    /// <summary>Get length in host byte order.</summary>
    public ushort Length => SwapBytes(LengthRaw);

    /// <summary>Get checksum in host byte order.</summary>
    public ushort Checksum => SwapBytes(ChecksumRaw);

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }
}

/// <summary>
/// Parsed UDP packet information.
/// </summary>
public unsafe struct UdpPacket
{
    /// <summary>Source port (host byte order).</summary>
    public ushort SourcePort;

    /// <summary>Destination port (host byte order).</summary>
    public ushort DestPort;

    /// <summary>Total UDP length including header.</summary>
    public ushort Length;

    /// <summary>Checksum from packet.</summary>
    public ushort Checksum;

    /// <summary>Pointer to payload data.</summary>
    public byte* Payload;

    /// <summary>Payload length in bytes.</summary>
    public int PayloadLength;
}

/// <summary>
/// UDP protocol handler.
/// </summary>
public static unsafe class UDP
{
    /// <summary>IP protocol number for UDP.</summary>
    public const byte ProtocolNumber = 17;

    /// <summary>Maximum UDP payload size (65535 - 8 byte header).</summary>
    public const int MaxPayloadSize = 65527;

    /// <summary>
    /// Parse a UDP packet.
    /// </summary>
    /// <param name="data">Pointer to UDP data (after IP header).</param>
    /// <param name="length">Length of UDP data.</param>
    /// <param name="packet">Parsed packet information.</param>
    /// <returns>True if packet was parsed successfully.</returns>
    public static bool Parse(byte* data, int length, out UdpPacket packet)
    {
        packet = default;

        // Minimum UDP header size
        if (data == null || length < UdpHeader.Size)
            return false;

        // Extract header fields (all big-endian)
        packet.SourcePort = (ushort)((data[0] << 8) | data[1]);
        packet.DestPort = (ushort)((data[2] << 8) | data[3]);
        packet.Length = (ushort)((data[4] << 8) | data[5]);
        packet.Checksum = (ushort)((data[6] << 8) | data[7]);

        // Validate length
        if (packet.Length < UdpHeader.Size || packet.Length > length)
            return false;

        // Set payload pointer and length
        packet.Payload = data + UdpHeader.Size;
        packet.PayloadLength = packet.Length - UdpHeader.Size;

        return true;
    }

    /// <summary>
    /// Build a UDP packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="srcPort">Source port (host byte order).</param>
    /// <param name="destPort">Destination port (host byte order).</param>
    /// <param name="payload">Payload data.</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildPacket(byte* buffer, ushort srcPort, ushort destPort,
                                   byte* payload, int payloadLength)
    {
        if (buffer == null || payloadLength > MaxPayloadSize)
            return 0;

        int totalLength = UdpHeader.Size + payloadLength;

        // Source port (big-endian)
        buffer[0] = (byte)(srcPort >> 8);
        buffer[1] = (byte)(srcPort & 0xFF);

        // Destination port (big-endian)
        buffer[2] = (byte)(destPort >> 8);
        buffer[3] = (byte)(destPort & 0xFF);

        // Length (big-endian)
        buffer[4] = (byte)(totalLength >> 8);
        buffer[5] = (byte)(totalLength & 0xFF);

        // Checksum - set to 0 initially (optional for IPv4)
        buffer[6] = 0;
        buffer[7] = 0;

        // Copy payload
        if (payload != null && payloadLength > 0)
        {
            byte* dest = buffer + UdpHeader.Size;
            for (int i = 0; i < payloadLength; i++)
                dest[i] = payload[i];
        }

        return totalLength;
    }

    /// <summary>
    /// Build a UDP packet with checksum.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="srcPort">Source port (host byte order).</param>
    /// <param name="destPort">Destination port (host byte order).</param>
    /// <param name="payload">Payload data.</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <param name="srcIP">Source IP address (host byte order).</param>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildPacketWithChecksum(byte* buffer, ushort srcPort, ushort destPort,
                                               byte* payload, int payloadLength,
                                               uint srcIP, uint destIP)
    {
        int totalLength = BuildPacket(buffer, srcPort, destPort, payload, payloadLength);
        if (totalLength == 0)
            return 0;

        // Calculate and set checksum
        ushort checksum = CalculateChecksum(buffer, totalLength, srcIP, destIP);
        buffer[6] = (byte)(checksum >> 8);
        buffer[7] = (byte)(checksum & 0xFF);

        return totalLength;
    }

    /// <summary>
    /// Calculate UDP checksum using pseudo-header.
    /// </summary>
    /// <param name="udpData">Pointer to UDP packet data.</param>
    /// <param name="udpLength">Length of UDP packet.</param>
    /// <param name="srcIP">Source IP address (host byte order).</param>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <returns>Checksum value (in network byte order).</returns>
    public static ushort CalculateChecksum(byte* udpData, int udpLength, uint srcIP, uint destIP)
    {
        uint sum = 0;

        // Pseudo-header: src IP, dest IP, zero, protocol, UDP length
        // Source IP (4 bytes as two 16-bit words)
        sum += (srcIP >> 16) & 0xFFFF;
        sum += srcIP & 0xFFFF;

        // Destination IP
        sum += (destIP >> 16) & 0xFFFF;
        sum += destIP & 0xFFFF;

        // Zero + Protocol (UDP = 17)
        sum += ProtocolNumber;

        // UDP length
        sum += (uint)udpLength;

        // UDP header and data
        int i = 0;
        while (i < udpLength - 1)
        {
            sum += (uint)((udpData[i] << 8) | udpData[i + 1]);
            i += 2;
        }

        // Add odd byte if present
        if (i < udpLength)
        {
            sum += (uint)(udpData[i] << 8);
        }

        // Fold 32-bit sum to 16 bits
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        // Return one's complement (0 means checksum disabled, use 0xFFFF instead)
        ushort result = (ushort)(~sum & 0xFFFF);
        return result == 0 ? (ushort)0xFFFF : result;
    }

    /// <summary>
    /// Verify UDP checksum.
    /// </summary>
    /// <param name="udpData">Pointer to UDP packet data.</param>
    /// <param name="udpLength">Length of UDP packet.</param>
    /// <param name="srcIP">Source IP address (host byte order).</param>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <returns>True if checksum is valid or disabled (0).</returns>
    public static bool VerifyChecksum(byte* udpData, int udpLength, uint srcIP, uint destIP)
    {
        // Get checksum from packet
        ushort packetChecksum = (ushort)((udpData[6] << 8) | udpData[7]);

        // Checksum of 0 means not computed (valid for IPv4)
        if (packetChecksum == 0)
            return true;

        // Calculate checksum over entire packet including the checksum field
        uint sum = 0;

        // Pseudo-header
        sum += (srcIP >> 16) & 0xFFFF;
        sum += srcIP & 0xFFFF;
        sum += (destIP >> 16) & 0xFFFF;
        sum += destIP & 0xFFFF;
        sum += ProtocolNumber;
        sum += (uint)udpLength;

        // UDP data
        int i = 0;
        while (i < udpLength - 1)
        {
            sum += (uint)((udpData[i] << 8) | udpData[i + 1]);
            i += 2;
        }

        if (i < udpLength)
        {
            sum += (uint)(udpData[i] << 8);
        }

        // Fold
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        // Result should be 0xFFFF if valid
        return sum == 0xFFFF;
    }
}
