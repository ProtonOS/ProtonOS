// ProtonOS DDK - TCP Protocol (L4)
// Handles TCP packet parsing, building, and connection state management

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// TCP flags.
/// </summary>
public static class TcpFlags
{
    public const byte FIN = 0x01;
    public const byte SYN = 0x02;
    public const byte RST = 0x04;
    public const byte PSH = 0x08;
    public const byte ACK = 0x10;
    public const byte URG = 0x20;
    public const byte ECE = 0x40;
    public const byte CWR = 0x80;
}

/// <summary>
/// TCP connection states.
/// </summary>
public enum TcpState
{
    Closed,
    Listen,
    SynSent,
    SynReceived,
    Established,
    FinWait1,
    FinWait2,
    CloseWait,
    Closing,
    LastAck,
    TimeWait
}

/// <summary>
/// TCP header structure (20 bytes minimum).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct TcpHeader
{
    /// <summary>Source port (big-endian).</summary>
    public ushort SourcePortRaw;

    /// <summary>Destination port (big-endian).</summary>
    public ushort DestPortRaw;

    /// <summary>Sequence number (big-endian).</summary>
    public uint SeqNumRaw;

    /// <summary>Acknowledgment number (big-endian).</summary>
    public uint AckNumRaw;

    /// <summary>Data offset (high 4 bits) + Reserved (low 4 bits).</summary>
    public byte DataOffsetAndReserved;

    /// <summary>TCP flags.</summary>
    public byte Flags;

    /// <summary>Window size (big-endian).</summary>
    public ushort WindowRaw;

    /// <summary>Checksum (big-endian).</summary>
    public ushort ChecksumRaw;

    /// <summary>Urgent pointer (big-endian).</summary>
    public ushort UrgentPtrRaw;

    /// <summary>Minimum header size in bytes (no options).</summary>
    public const int MinSize = 20;

    /// <summary>Maximum header size in bytes (with options).</summary>
    public const int MaxSize = 60;
}

/// <summary>
/// Parsed TCP packet information.
/// </summary>
public unsafe struct TcpPacket
{
    /// <summary>Source port (host byte order).</summary>
    public ushort SourcePort;

    /// <summary>Destination port (host byte order).</summary>
    public ushort DestPort;

    /// <summary>Sequence number (host byte order).</summary>
    public uint SeqNum;

    /// <summary>Acknowledgment number (host byte order).</summary>
    public uint AckNum;

    /// <summary>Header length in bytes.</summary>
    public int HeaderLength;

    /// <summary>TCP flags.</summary>
    public byte Flags;

    /// <summary>Window size (host byte order).</summary>
    public ushort Window;

    /// <summary>Checksum from packet.</summary>
    public ushort Checksum;

    /// <summary>Urgent pointer (host byte order).</summary>
    public ushort UrgentPtr;

    /// <summary>Pointer to payload data.</summary>
    public byte* Payload;

    /// <summary>Payload length in bytes.</summary>
    public int PayloadLength;

    /// <summary>Check if SYN flag is set.</summary>
    public bool IsSyn => (Flags & TcpFlags.SYN) != 0;

    /// <summary>Check if ACK flag is set.</summary>
    public bool IsAck => (Flags & TcpFlags.ACK) != 0;

    /// <summary>Check if FIN flag is set.</summary>
    public bool IsFin => (Flags & TcpFlags.FIN) != 0;

    /// <summary>Check if RST flag is set.</summary>
    public bool IsRst => (Flags & TcpFlags.RST) != 0;

    /// <summary>Check if PSH flag is set.</summary>
    public bool IsPsh => (Flags & TcpFlags.PSH) != 0;
}

/// <summary>
/// TCP protocol handler.
/// </summary>
public static unsafe class TCP
{
    /// <summary>IP protocol number for TCP.</summary>
    public const byte ProtocolNumber = 6;

    /// <summary>Maximum segment size (default).</summary>
    public const int DefaultMSS = 1460;

    /// <summary>
    /// Parse a TCP packet.
    /// </summary>
    /// <param name="data">Pointer to TCP data (after IP header).</param>
    /// <param name="length">Length of TCP data.</param>
    /// <param name="packet">Parsed packet information.</param>
    /// <returns>True if packet was parsed successfully.</returns>
    public static bool Parse(byte* data, int length, out TcpPacket packet)
    {
        packet = default;

        // Minimum TCP header size
        if (data == null || length < TcpHeader.MinSize)
            return false;

        // Extract header fields (all big-endian)
        packet.SourcePort = (ushort)((data[0] << 8) | data[1]);
        packet.DestPort = (ushort)((data[2] << 8) | data[3]);

        packet.SeqNum = ((uint)data[4] << 24) | ((uint)data[5] << 16) |
                        ((uint)data[6] << 8) | data[7];
        packet.AckNum = ((uint)data[8] << 24) | ((uint)data[9] << 16) |
                        ((uint)data[10] << 8) | data[11];

        // Data offset is in upper 4 bits, measured in 32-bit words
        int dataOffset = (data[12] >> 4) & 0x0F;
        packet.HeaderLength = dataOffset * 4;

        if (packet.HeaderLength < TcpHeader.MinSize || packet.HeaderLength > length)
            return false;

        packet.Flags = data[13];
        packet.Window = (ushort)((data[14] << 8) | data[15]);
        packet.Checksum = (ushort)((data[16] << 8) | data[17]);
        packet.UrgentPtr = (ushort)((data[18] << 8) | data[19]);

        // Set payload pointer and length
        packet.Payload = data + packet.HeaderLength;
        packet.PayloadLength = length - packet.HeaderLength;

        return true;
    }

    /// <summary>
    /// Build a TCP packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to.</param>
    /// <param name="srcPort">Source port (host byte order).</param>
    /// <param name="destPort">Destination port (host byte order).</param>
    /// <param name="seqNum">Sequence number.</param>
    /// <param name="ackNum">Acknowledgment number.</param>
    /// <param name="flags">TCP flags.</param>
    /// <param name="window">Window size.</param>
    /// <param name="payload">Payload data (can be null).</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <returns>Total packet length, or 0 on error.</returns>
    public static int BuildPacket(byte* buffer, ushort srcPort, ushort destPort,
                                   uint seqNum, uint ackNum, byte flags, ushort window,
                                   byte* payload, int payloadLength)
    {
        if (buffer == null)
            return 0;

        int headerLength = TcpHeader.MinSize; // No options for now
        int totalLength = headerLength + payloadLength;

        // Source port (big-endian)
        buffer[0] = (byte)(srcPort >> 8);
        buffer[1] = (byte)(srcPort & 0xFF);

        // Destination port (big-endian)
        buffer[2] = (byte)(destPort >> 8);
        buffer[3] = (byte)(destPort & 0xFF);

        // Sequence number (big-endian)
        buffer[4] = (byte)(seqNum >> 24);
        buffer[5] = (byte)(seqNum >> 16);
        buffer[6] = (byte)(seqNum >> 8);
        buffer[7] = (byte)(seqNum & 0xFF);

        // Acknowledgment number (big-endian)
        buffer[8] = (byte)(ackNum >> 24);
        buffer[9] = (byte)(ackNum >> 16);
        buffer[10] = (byte)(ackNum >> 8);
        buffer[11] = (byte)(ackNum & 0xFF);

        // Data offset (5 = 20 bytes / 4) in upper 4 bits, reserved in lower
        buffer[12] = (byte)((headerLength / 4) << 4);

        // Flags
        buffer[13] = flags;

        // Window size (big-endian)
        buffer[14] = (byte)(window >> 8);
        buffer[15] = (byte)(window & 0xFF);

        // Checksum - set to 0 initially (calculated after with pseudo-header)
        buffer[16] = 0;
        buffer[17] = 0;

        // Urgent pointer
        buffer[18] = 0;
        buffer[19] = 0;

        // Copy payload
        if (payload != null && payloadLength > 0)
        {
            byte* dest = buffer + headerLength;
            for (int i = 0; i < payloadLength; i++)
                dest[i] = payload[i];
        }

        return totalLength;
    }

    /// <summary>
    /// Build a TCP packet with checksum.
    /// </summary>
    public static int BuildPacketWithChecksum(byte* buffer, ushort srcPort, ushort destPort,
                                               uint seqNum, uint ackNum, byte flags, ushort window,
                                               byte* payload, int payloadLength,
                                               uint srcIP, uint destIP)
    {
        int totalLength = BuildPacket(buffer, srcPort, destPort, seqNum, ackNum,
                                       flags, window, payload, payloadLength);
        if (totalLength == 0)
            return 0;

        // Calculate and set checksum
        ushort checksum = CalculateChecksum(buffer, totalLength, srcIP, destIP);
        buffer[16] = (byte)(checksum >> 8);
        buffer[17] = (byte)(checksum & 0xFF);

        return totalLength;
    }

    /// <summary>
    /// Build a SYN packet for connection initiation.
    /// </summary>
    public static int BuildSyn(byte* buffer, ushort srcPort, ushort destPort,
                                uint seqNum, ushort window, uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, 0,
                                        TcpFlags.SYN, window, null, 0, srcIP, destIP);
    }

    /// <summary>
    /// Build a SYN-ACK packet.
    /// </summary>
    public static int BuildSynAck(byte* buffer, ushort srcPort, ushort destPort,
                                   uint seqNum, uint ackNum, ushort window,
                                   uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, ackNum,
                                        (byte)(TcpFlags.SYN | TcpFlags.ACK), window,
                                        null, 0, srcIP, destIP);
    }

    /// <summary>
    /// Build an ACK packet.
    /// </summary>
    public static int BuildAck(byte* buffer, ushort srcPort, ushort destPort,
                                uint seqNum, uint ackNum, ushort window,
                                uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, ackNum,
                                        TcpFlags.ACK, window, null, 0, srcIP, destIP);
    }

    /// <summary>
    /// Build a FIN-ACK packet for connection termination.
    /// </summary>
    public static int BuildFinAck(byte* buffer, ushort srcPort, ushort destPort,
                                   uint seqNum, uint ackNum, ushort window,
                                   uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, ackNum,
                                        (byte)(TcpFlags.FIN | TcpFlags.ACK), window,
                                        null, 0, srcIP, destIP);
    }

    /// <summary>
    /// Build a RST packet for connection reset.
    /// </summary>
    public static int BuildRst(byte* buffer, ushort srcPort, ushort destPort,
                                uint seqNum, uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, 0,
                                        TcpFlags.RST, 0, null, 0, srcIP, destIP);
    }

    /// <summary>
    /// Build a data packet (PSH-ACK).
    /// </summary>
    public static int BuildData(byte* buffer, ushort srcPort, ushort destPort,
                                 uint seqNum, uint ackNum, ushort window,
                                 byte* data, int dataLength,
                                 uint srcIP, uint destIP)
    {
        return BuildPacketWithChecksum(buffer, srcPort, destPort, seqNum, ackNum,
                                        (byte)(TcpFlags.PSH | TcpFlags.ACK), window,
                                        data, dataLength, srcIP, destIP);
    }

    /// <summary>
    /// Calculate TCP checksum using pseudo-header.
    /// </summary>
    /// <param name="tcpData">Pointer to TCP packet data.</param>
    /// <param name="tcpLength">Length of TCP packet.</param>
    /// <param name="srcIP">Source IP address (host byte order).</param>
    /// <param name="destIP">Destination IP address (host byte order).</param>
    /// <returns>Checksum value (in network byte order).</returns>
    public static ushort CalculateChecksum(byte* tcpData, int tcpLength, uint srcIP, uint destIP)
    {
        uint sum = 0;

        // Pseudo-header: src IP, dest IP, zero, protocol, TCP length
        // Source IP (4 bytes as two 16-bit words)
        sum += (srcIP >> 16) & 0xFFFF;
        sum += srcIP & 0xFFFF;

        // Destination IP
        sum += (destIP >> 16) & 0xFFFF;
        sum += destIP & 0xFFFF;

        // Zero + Protocol (TCP = 6)
        sum += ProtocolNumber;

        // TCP length
        sum += (uint)tcpLength;

        // TCP header and data
        int i = 0;
        while (i < tcpLength - 1)
        {
            sum += (uint)((tcpData[i] << 8) | tcpData[i + 1]);
            i += 2;
        }

        // Add odd byte if present
        if (i < tcpLength)
        {
            sum += (uint)(tcpData[i] << 8);
        }

        // Fold 32-bit sum to 16 bits
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }

        // Return one's complement
        ushort result = (ushort)(~sum & 0xFFFF);
        return result == 0 ? (ushort)0xFFFF : result;
    }

    /// <summary>
    /// Verify TCP checksum.
    /// </summary>
    public static bool VerifyChecksum(byte* tcpData, int tcpLength, uint srcIP, uint destIP)
    {
        uint sum = 0;

        // Pseudo-header
        sum += (srcIP >> 16) & 0xFFFF;
        sum += srcIP & 0xFFFF;
        sum += (destIP >> 16) & 0xFFFF;
        sum += destIP & 0xFFFF;
        sum += ProtocolNumber;
        sum += (uint)tcpLength;

        // TCP data
        int i = 0;
        while (i < tcpLength - 1)
        {
            sum += (uint)((tcpData[i] << 8) | tcpData[i + 1]);
            i += 2;
        }

        if (i < tcpLength)
        {
            sum += (uint)(tcpData[i] << 8);
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
