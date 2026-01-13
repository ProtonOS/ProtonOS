// ProtonOS DDK - ICMP Protocol (L3)
// Handles ICMP packet parsing and building for ping support

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// ICMP message types.
/// </summary>
public static class IcmpType
{
    /// <summary>Echo Reply (response to ping).</summary>
    public const byte EchoReply = 0;

    /// <summary>Destination Unreachable.</summary>
    public const byte DestinationUnreachable = 3;

    /// <summary>Redirect.</summary>
    public const byte Redirect = 5;

    /// <summary>Echo Request (ping).</summary>
    public const byte EchoRequest = 8;

    /// <summary>Time Exceeded.</summary>
    public const byte TimeExceeded = 11;

    /// <summary>Parameter Problem.</summary>
    public const byte ParameterProblem = 12;

    /// <summary>Timestamp Request.</summary>
    public const byte TimestampRequest = 13;

    /// <summary>Timestamp Reply.</summary>
    public const byte TimestampReply = 14;
}

/// <summary>
/// ICMP header structure (8 bytes for echo request/reply).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IcmpHeader
{
    /// <summary>ICMP message type.</summary>
    public byte Type;

    /// <summary>ICMP message code.</summary>
    public byte Code;

    /// <summary>Checksum (big-endian).</summary>
    public ushort ChecksumRaw;

    /// <summary>Header size in bytes.</summary>
    public const int Size = 4;
}

/// <summary>
/// ICMP Echo header (extends IcmpHeader with identifier and sequence).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct IcmpEchoHeader
{
    /// <summary>ICMP message type.</summary>
    public byte Type;

    /// <summary>ICMP message code (0 for echo).</summary>
    public byte Code;

    /// <summary>Checksum (big-endian).</summary>
    public ushort ChecksumRaw;

    /// <summary>Identifier (big-endian).</summary>
    public ushort IdentifierRaw;

    /// <summary>Sequence number (big-endian).</summary>
    public ushort SequenceRaw;

    /// <summary>Get identifier in host byte order.</summary>
    public ushort Identifier => SwapBytes(IdentifierRaw);

    /// <summary>Get sequence in host byte order.</summary>
    public ushort Sequence => SwapBytes(SequenceRaw);

    /// <summary>Header size in bytes.</summary>
    public const int Size = 8;

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }
}

/// <summary>
/// Parsed ICMP packet information.
/// </summary>
public unsafe struct IcmpPacket
{
    /// <summary>ICMP message type.</summary>
    public byte Type;

    /// <summary>ICMP message code.</summary>
    public byte Code;

    /// <summary>Checksum from packet.</summary>
    public ushort Checksum;

    /// <summary>Identifier (for echo request/reply).</summary>
    public ushort Identifier;

    /// <summary>Sequence number (for echo request/reply).</summary>
    public ushort Sequence;

    /// <summary>Pointer to payload data.</summary>
    public byte* Payload;

    /// <summary>Payload length in bytes.</summary>
    public int PayloadLength;

    /// <summary>Total ICMP packet length.</summary>
    public int TotalLength;
}

/// <summary>
/// ICMP protocol handler.
/// </summary>
public static unsafe class ICMP
{
    /// <summary>IP protocol number for ICMP.</summary>
    public const byte ProtocolNumber = 1;

    /// <summary>
    /// Parse an ICMP packet.
    /// </summary>
    /// <param name="data">Pointer to ICMP data (after IP header).</param>
    /// <param name="length">Length of ICMP data.</param>
    /// <param name="packet">Parsed packet information.</param>
    /// <returns>True if packet was parsed successfully.</returns>
    public static bool Parse(byte* data, int length, out IcmpPacket packet)
    {
        packet = default;

        // Minimum ICMP header size
        if (data == null || length < IcmpHeader.Size)
            return false;

        packet.Type = data[0];
        packet.Code = data[1];
        packet.Checksum = (ushort)((data[2] << 8) | data[3]);

        // For echo request/reply, parse identifier and sequence
        if ((packet.Type == IcmpType.EchoRequest || packet.Type == IcmpType.EchoReply) &&
            length >= IcmpEchoHeader.Size)
        {
            packet.Identifier = (ushort)((data[4] << 8) | data[5]);
            packet.Sequence = (ushort)((data[6] << 8) | data[7]);
            packet.Payload = data + IcmpEchoHeader.Size;
            packet.PayloadLength = length - IcmpEchoHeader.Size;
        }
        else
        {
            packet.Payload = data + IcmpHeader.Size;
            packet.PayloadLength = length - IcmpHeader.Size;
        }

        packet.TotalLength = length;
        return true;
    }

    /// <summary>
    /// Build an ICMP Echo Request packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="identifier">Echo identifier.</param>
    /// <param name="sequence">Echo sequence number.</param>
    /// <param name="payload">Optional payload data.</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildEchoRequest(byte* buffer, ushort identifier, ushort sequence,
                                        byte* payload, int payloadLength)
    {
        if (buffer == null)
            return 0;

        int totalLength = IcmpEchoHeader.Size + payloadLength;

        // Type: Echo Request (8)
        buffer[0] = IcmpType.EchoRequest;
        // Code: 0
        buffer[1] = 0;
        // Checksum: initially 0 (calculated after)
        buffer[2] = 0;
        buffer[3] = 0;
        // Identifier (big-endian)
        buffer[4] = (byte)(identifier >> 8);
        buffer[5] = (byte)(identifier & 0xFF);
        // Sequence (big-endian)
        buffer[6] = (byte)(sequence >> 8);
        buffer[7] = (byte)(sequence & 0xFF);

        // Copy payload
        if (payload != null && payloadLength > 0)
        {
            byte* dest = buffer + IcmpEchoHeader.Size;
            for (int i = 0; i < payloadLength; i++)
                dest[i] = payload[i];
        }

        // Calculate and set checksum
        ushort checksum = CalculateChecksum(buffer, totalLength);
        buffer[2] = (byte)(checksum >> 8);
        buffer[3] = (byte)(checksum & 0xFF);

        return totalLength;
    }

    /// <summary>
    /// Build an ICMP Echo Reply packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="identifier">Echo identifier (from request).</param>
    /// <param name="sequence">Echo sequence number (from request).</param>
    /// <param name="payload">Payload data (from request).</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildEchoReply(byte* buffer, ushort identifier, ushort sequence,
                                      byte* payload, int payloadLength)
    {
        if (buffer == null)
            return 0;

        int totalLength = IcmpEchoHeader.Size + payloadLength;

        // Type: Echo Reply (0)
        buffer[0] = IcmpType.EchoReply;
        // Code: 0
        buffer[1] = 0;
        // Checksum: initially 0 (calculated after)
        buffer[2] = 0;
        buffer[3] = 0;
        // Identifier (big-endian)
        buffer[4] = (byte)(identifier >> 8);
        buffer[5] = (byte)(identifier & 0xFF);
        // Sequence (big-endian)
        buffer[6] = (byte)(sequence >> 8);
        buffer[7] = (byte)(sequence & 0xFF);

        // Copy payload
        if (payload != null && payloadLength > 0)
        {
            byte* dest = buffer + IcmpEchoHeader.Size;
            for (int i = 0; i < payloadLength; i++)
                dest[i] = payload[i];
        }

        // Calculate and set checksum
        ushort checksum = CalculateChecksum(buffer, totalLength);
        buffer[2] = (byte)(checksum >> 8);
        buffer[3] = (byte)(checksum & 0xFF);

        return totalLength;
    }

    /// <summary>
    /// Calculate ICMP checksum.
    /// </summary>
    /// <param name="data">Pointer to ICMP packet data.</param>
    /// <param name="length">Length of data.</param>
    /// <returns>Checksum value (in network byte order).</returns>
    public static ushort CalculateChecksum(byte* data, int length)
    {
        uint sum = 0;

        // Sum all 16-bit words
        int i = 0;
        while (i < length - 1)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
            i += 2;
        }

        // Add odd byte if present
        if (i < length)
        {
            sum += (uint)(data[i] << 8);
        }

        // Fold 32-bit sum to 16 bits
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        // Return one's complement
        return (ushort)(~sum & 0xFFFF);
    }

    /// <summary>
    /// Verify ICMP checksum.
    /// </summary>
    /// <param name="data">Pointer to ICMP packet data.</param>
    /// <param name="length">Length of data.</param>
    /// <returns>True if checksum is valid.</returns>
    public static bool VerifyChecksum(byte* data, int length)
    {
        // When computing checksum over data that includes checksum,
        // result should be 0 (or 0xFFFF before complement)
        uint sum = 0;

        int i = 0;
        while (i < length - 1)
        {
            sum += (uint)((data[i] << 8) | data[i + 1]);
            i += 2;
        }

        if (i < length)
        {
            sum += (uint)(data[i] << 8);
        }

        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        return sum == 0xFFFF;
    }
}
