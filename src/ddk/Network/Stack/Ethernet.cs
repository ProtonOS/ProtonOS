// ProtonOS DDK - Ethernet Layer (L2)
// Handles Ethernet frame parsing and building

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// Common EtherType values.
/// </summary>
public static class EtherType
{
    public const ushort IPv4 = 0x0800;
    public const ushort ARP = 0x0806;
    public const ushort IPv6 = 0x86DD;
    public const ushort VLAN = 0x8100;
}

/// <summary>
/// Ethernet frame header (14 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct EthernetHeader
{
    /// <summary>Destination MAC address (6 bytes).</summary>
    public fixed byte DestinationMac[6];

    /// <summary>Source MAC address (6 bytes).</summary>
    public fixed byte SourceMac[6];

    /// <summary>EtherType (big-endian).</summary>
    public ushort EtherTypeRaw;

    /// <summary>Get EtherType in host byte order.</summary>
    public ushort EtherType => SwapBytes(EtherTypeRaw);

    /// <summary>Header size in bytes.</summary>
    public const int Size = 14;

    /// <summary>Minimum Ethernet frame size (without FCS).</summary>
    public const int MinFrameSize = 60;

    /// <summary>Maximum Ethernet frame size (without FCS).</summary>
    public const int MaxFrameSize = 1514;

    /// <summary>Maximum payload size (MTU).</summary>
    public const int MaxPayloadSize = 1500;

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }
}

/// <summary>
/// Parsed Ethernet frame information.
/// </summary>
public unsafe struct EthernetFrame
{
    /// <summary>Pointer to destination MAC (6 bytes).</summary>
    public byte* DestinationMac;

    /// <summary>Pointer to source MAC (6 bytes).</summary>
    public byte* SourceMac;

    /// <summary>EtherType in host byte order.</summary>
    public ushort EtherType;

    /// <summary>Pointer to payload data.</summary>
    public byte* Payload;

    /// <summary>Payload length in bytes.</summary>
    public int PayloadLength;

    /// <summary>Total frame length.</summary>
    public int TotalLength;

    /// <summary>Check if destination is broadcast.</summary>
    public bool IsBroadcast
    {
        get
        {
            if (DestinationMac == null) return false;
            return DestinationMac[0] == 0xFF && DestinationMac[1] == 0xFF &&
                   DestinationMac[2] == 0xFF && DestinationMac[3] == 0xFF &&
                   DestinationMac[4] == 0xFF && DestinationMac[5] == 0xFF;
        }
    }

    /// <summary>Check if destination is multicast.</summary>
    public bool IsMulticast
    {
        get
        {
            if (DestinationMac == null) return false;
            return (DestinationMac[0] & 0x01) != 0;
        }
    }
}

/// <summary>
/// Ethernet frame parser and builder.
/// </summary>
public static unsafe class Ethernet
{
    /// <summary>
    /// Parse an Ethernet frame.
    /// </summary>
    /// <param name="data">Pointer to frame data.</param>
    /// <param name="length">Length of frame data.</param>
    /// <param name="frame">Parsed frame information.</param>
    /// <returns>True if frame was parsed successfully.</returns>
    public static bool Parse(byte* data, int length, out EthernetFrame frame)
    {
        frame = default;

        // Minimum frame size check
        if (data == null || length < EthernetHeader.Size)
            return false;

        var header = (EthernetHeader*)data;

        frame.DestinationMac = data;
        frame.SourceMac = data + 6;
        frame.EtherType = header->EtherType;
        frame.Payload = data + EthernetHeader.Size;
        frame.PayloadLength = length - EthernetHeader.Size;
        frame.TotalLength = length;

        return true;
    }

    /// <summary>
    /// Build an Ethernet frame header.
    /// </summary>
    /// <param name="buffer">Buffer to write header to (must be at least 14 bytes).</param>
    /// <param name="destMac">Destination MAC address (6 bytes).</param>
    /// <param name="srcMac">Source MAC address (6 bytes).</param>
    /// <param name="etherType">EtherType value.</param>
    /// <returns>Number of bytes written (14), or 0 on error.</returns>
    public static int BuildHeader(byte* buffer, byte* destMac, byte* srcMac, ushort etherType)
    {
        if (buffer == null || destMac == null || srcMac == null)
            return 0;

        // Copy destination MAC
        for (int i = 0; i < 6; i++)
            buffer[i] = destMac[i];

        // Copy source MAC
        for (int i = 0; i < 6; i++)
            buffer[6 + i] = srcMac[i];

        // Write EtherType (big-endian)
        buffer[12] = (byte)(etherType >> 8);
        buffer[13] = (byte)(etherType & 0xFF);

        return EthernetHeader.Size;
    }

    /// <summary>
    /// Build a complete Ethernet frame.
    /// </summary>
    /// <param name="buffer">Buffer to write frame to.</param>
    /// <param name="bufferSize">Size of buffer.</param>
    /// <param name="destMac">Destination MAC address (6 bytes).</param>
    /// <param name="srcMac">Source MAC address (6 bytes).</param>
    /// <param name="etherType">EtherType value.</param>
    /// <param name="payload">Payload data.</param>
    /// <param name="payloadLength">Payload length.</param>
    /// <returns>Total frame length, or 0 on error.</returns>
    public static int BuildFrame(byte* buffer, int bufferSize, byte* destMac, byte* srcMac,
                                  ushort etherType, byte* payload, int payloadLength)
    {
        int totalLength = EthernetHeader.Size + payloadLength;

        // Check buffer size
        if (buffer == null || bufferSize < totalLength)
            return 0;

        // Check payload size
        if (payloadLength > EthernetHeader.MaxPayloadSize)
            return 0;

        // Build header
        int headerLen = BuildHeader(buffer, destMac, srcMac, etherType);
        if (headerLen == 0)
            return 0;

        // Copy payload
        if (payload != null && payloadLength > 0)
        {
            byte* dest = buffer + EthernetHeader.Size;
            for (int i = 0; i < payloadLength; i++)
                dest[i] = payload[i];
        }

        // Pad to minimum frame size if needed
        if (totalLength < EthernetHeader.MinFrameSize)
        {
            // Zero-pad
            for (int i = totalLength; i < EthernetHeader.MinFrameSize; i++)
                buffer[i] = 0;
            totalLength = EthernetHeader.MinFrameSize;
        }

        return totalLength;
    }

    /// <summary>
    /// Copy a MAC address.
    /// </summary>
    public static void CopyMac(byte* dest, byte* src)
    {
        if (dest == null || src == null) return;
        for (int i = 0; i < 6; i++)
            dest[i] = src[i];
    }

    /// <summary>
    /// Compare two MAC addresses.
    /// </summary>
    public static bool CompareMac(byte* a, byte* b)
    {
        if (a == null || b == null) return false;
        for (int i = 0; i < 6; i++)
        {
            if (a[i] != b[i])
                return false;
        }
        return true;
    }

    /// <summary>
    /// Set MAC to broadcast (FF:FF:FF:FF:FF:FF).
    /// </summary>
    public static void SetBroadcast(byte* mac)
    {
        if (mac == null) return;
        for (int i = 0; i < 6; i++)
            mac[i] = 0xFF;
    }

    /// <summary>
    /// Check if MAC is broadcast.
    /// </summary>
    public static bool IsBroadcast(byte* mac)
    {
        if (mac == null) return false;
        for (int i = 0; i < 6; i++)
        {
            if (mac[i] != 0xFF)
                return false;
        }
        return true;
    }
}
