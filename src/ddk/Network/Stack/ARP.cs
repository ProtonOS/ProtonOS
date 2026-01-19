// ProtonOS DDK - ARP Protocol (Address Resolution Protocol)
// Resolves IPv4 addresses to MAC addresses

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// ARP operation codes.
/// </summary>
public static class ArpOperation
{
    public const ushort Request = 1;
    public const ushort Reply = 2;
}

/// <summary>
/// ARP hardware types.
/// </summary>
public static class ArpHardwareType
{
    public const ushort Ethernet = 1;
}

/// <summary>
/// ARP packet structure for Ethernet/IPv4 (28 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ArpPacket
{
    /// <summary>Hardware type (big-endian). Ethernet = 1.</summary>
    public ushort HardwareTypeRaw;

    /// <summary>Protocol type (big-endian). IPv4 = 0x0800.</summary>
    public ushort ProtocolTypeRaw;

    /// <summary>Hardware address length. Ethernet = 6.</summary>
    public byte HardwareLength;

    /// <summary>Protocol address length. IPv4 = 4.</summary>
    public byte ProtocolLength;

    /// <summary>Operation (big-endian). Request = 1, Reply = 2.</summary>
    public ushort OperationRaw;

    /// <summary>Sender hardware address (MAC).</summary>
    public fixed byte SenderMac[6];

    /// <summary>Sender protocol address (IP).</summary>
    public fixed byte SenderIP[4];

    /// <summary>Target hardware address (MAC).</summary>
    public fixed byte TargetMac[6];

    /// <summary>Target protocol address (IP).</summary>
    public fixed byte TargetIP[4];

    /// <summary>Packet size in bytes.</summary>
    public const int Size = 28;

    /// <summary>Get hardware type in host byte order.</summary>
    public ushort HardwareType => SwapBytes(HardwareTypeRaw);

    /// <summary>Get protocol type in host byte order.</summary>
    public ushort ProtocolType => SwapBytes(ProtocolTypeRaw);

    /// <summary>Get operation in host byte order.</summary>
    public ushort Operation => SwapBytes(OperationRaw);

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }
}

/// <summary>
/// ARP cache entry.
/// </summary>
public unsafe struct ArpCacheEntry
{
    /// <summary>IPv4 address (host byte order).</summary>
    public uint IPAddress;

    /// <summary>MAC address.</summary>
    public fixed byte MacAddress[6];

    /// <summary>Entry is valid.</summary>
    public bool IsValid;

    /// <summary>Entry timestamp (for expiration).</summary>
    public ulong Timestamp;

    /// <summary>Entry is static (never expires).</summary>
    public bool IsStatic;
}

/// <summary>
/// ARP cache for storing IP to MAC mappings.
/// </summary>
public unsafe class ArpCache
{
    private const int MaxEntries = 64;
    private const ulong EntryTimeoutMs = 300000; // 5 minutes

    private ArpCacheEntry[] _entries;
    private int _count;

    public ArpCache()
    {
        _entries = new ArpCacheEntry[MaxEntries];
        _count = 0;
    }

    /// <summary>
    /// Look up a MAC address for an IP address.
    /// </summary>
    /// <param name="ip">IPv4 address in host byte order.</param>
    /// <param name="mac">Buffer to receive MAC address (6 bytes).</param>
    /// <returns>True if found.</returns>
    public bool Lookup(uint ip, byte* mac)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].IsValid && _entries[i].IPAddress == ip)
            {
                // Copy MAC address
                fixed (byte* srcMac = _entries[i].MacAddress)
                {
                    for (int j = 0; j < 6; j++)
                        mac[j] = srcMac[j];
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Add or update an ARP cache entry.
    /// </summary>
    /// <param name="ip">IPv4 address in host byte order.</param>
    /// <param name="mac">MAC address (6 bytes).</param>
    /// <param name="isStatic">True for static entries that never expire.</param>
    public void Update(uint ip, byte* mac, bool isStatic = false)
    {
        // Look for existing entry
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].IPAddress == ip)
            {
                // Update existing entry
                fixed (byte* destMac = _entries[i].MacAddress)
                {
                    for (int j = 0; j < 6; j++)
                        destMac[j] = mac[j];
                }
                _entries[i].IsValid = true;
                _entries[i].IsStatic = isStatic;
                // TODO: Update timestamp when timer is available
                return;
            }
        }

        // Add new entry
        if (_count < MaxEntries)
        {
            _entries[_count].IPAddress = ip;
            fixed (byte* destMac = _entries[_count].MacAddress)
            {
                for (int j = 0; j < 6; j++)
                    destMac[j] = mac[j];
            }
            _entries[_count].IsValid = true;
            _entries[_count].IsStatic = isStatic;
            _count++;
        }
        else
        {
            // Cache full - replace oldest non-static entry
            for (int i = 0; i < MaxEntries; i++)
            {
                if (!_entries[i].IsStatic)
                {
                    _entries[i].IPAddress = ip;
                    fixed (byte* destMac = _entries[i].MacAddress)
                    {
                        for (int j = 0; j < 6; j++)
                            destMac[j] = mac[j];
                    }
                    _entries[i].IsValid = true;
                    _entries[i].IsStatic = isStatic;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Remove an entry from the cache.
    /// </summary>
    public void Remove(uint ip)
    {
        for (int i = 0; i < _count; i++)
        {
            if (_entries[i].IPAddress == ip)
            {
                _entries[i].IsValid = false;
                return;
            }
        }
    }

    /// <summary>
    /// Clear all non-static entries.
    /// </summary>
    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            if (!_entries[i].IsStatic)
                _entries[i].IsValid = false;
        }
    }

    /// <summary>
    /// Number of valid entries.
    /// </summary>
    public int Count
    {
        get
        {
            int count = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].IsValid)
                    count++;
            }
            return count;
        }
    }

    /// <summary>
    /// Total number of entries (including invalid ones) for enumeration.
    /// </summary>
    public int EntryCount => _count;

    /// <summary>
    /// Try to get an entry by index for enumeration.
    /// </summary>
    /// <param name="index">Entry index (0 to EntryCount-1).</param>
    /// <param name="ip">Receives the IP address (host byte order).</param>
    /// <param name="mac">Buffer to receive MAC address (6 bytes).</param>
    /// <param name="isStatic">Receives whether the entry is static.</param>
    /// <returns>True if the entry at this index is valid.</returns>
    public bool TryGetEntry(int index, out uint ip, byte* mac, out bool isStatic)
    {
        ip = 0;
        isStatic = false;

        if (index < 0 || index >= _count)
            return false;

        if (!_entries[index].IsValid)
            return false;

        ip = _entries[index].IPAddress;
        isStatic = _entries[index].IsStatic;

        fixed (byte* srcMac = _entries[index].MacAddress)
        {
            for (int j = 0; j < 6; j++)
                mac[j] = srcMac[j];
        }

        return true;
    }
}

/// <summary>
/// ARP protocol handler.
/// </summary>
public static unsafe class ARP
{
    /// <summary>
    /// Parse an ARP packet.
    /// </summary>
    /// <param name="data">Pointer to ARP packet data.</param>
    /// <param name="length">Length of data.</param>
    /// <param name="packet">Parsed packet.</param>
    /// <returns>True if valid ARP packet.</returns>
    public static bool Parse(byte* data, int length, out ArpPacket packet)
    {
        packet = default;

        if (data == null || length < ArpPacket.Size)
            return false;

        packet = *(ArpPacket*)data;

        // Validate Ethernet/IPv4 ARP
        if (packet.HardwareType != ArpHardwareType.Ethernet)
            return false;
        if (packet.ProtocolType != EtherType.IPv4)
            return false;
        if (packet.HardwareLength != 6)
            return false;
        if (packet.ProtocolLength != 4)
            return false;

        return true;
    }

    /// <summary>
    /// Build an ARP request packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to (at least 28 bytes).</param>
    /// <param name="senderMac">Sender MAC address (6 bytes).</param>
    /// <param name="senderIP">Sender IP address in host byte order.</param>
    /// <param name="targetIP">Target IP address in host byte order.</param>
    /// <returns>Packet size (28), or 0 on error.</returns>
    public static int BuildRequest(byte* buffer, byte* senderMac, uint senderIP, uint targetIP)
    {
        if (buffer == null || senderMac == null)
            return 0;

        var packet = (ArpPacket*)buffer;

        // Hardware type: Ethernet (big-endian)
        packet->HardwareTypeRaw = SwapBytes(ArpHardwareType.Ethernet);

        // Protocol type: IPv4 (big-endian)
        packet->ProtocolTypeRaw = SwapBytes(EtherType.IPv4);

        // Lengths
        packet->HardwareLength = 6;
        packet->ProtocolLength = 4;

        // Operation: Request (big-endian)
        packet->OperationRaw = SwapBytes(ArpOperation.Request);

        // Sender MAC
        for (int i = 0; i < 6; i++)
            packet->SenderMac[i] = senderMac[i];

        // Sender IP (network byte order = big-endian)
        packet->SenderIP[0] = (byte)(senderIP >> 24);
        packet->SenderIP[1] = (byte)(senderIP >> 16);
        packet->SenderIP[2] = (byte)(senderIP >> 8);
        packet->SenderIP[3] = (byte)senderIP;

        // Target MAC: unknown (zeros)
        for (int i = 0; i < 6; i++)
            packet->TargetMac[i] = 0;

        // Target IP (network byte order = big-endian)
        packet->TargetIP[0] = (byte)(targetIP >> 24);
        packet->TargetIP[1] = (byte)(targetIP >> 16);
        packet->TargetIP[2] = (byte)(targetIP >> 8);
        packet->TargetIP[3] = (byte)targetIP;

        return ArpPacket.Size;
    }

    /// <summary>
    /// Build an ARP reply packet.
    /// </summary>
    /// <param name="buffer">Buffer to write packet to (at least 28 bytes).</param>
    /// <param name="senderMac">Sender MAC address (our MAC, 6 bytes).</param>
    /// <param name="senderIP">Sender IP address (our IP, host byte order).</param>
    /// <param name="targetMac">Target MAC address (requester's MAC, 6 bytes).</param>
    /// <param name="targetIP">Target IP address (requester's IP, host byte order).</param>
    /// <returns>Packet size (28), or 0 on error.</returns>
    public static int BuildReply(byte* buffer, byte* senderMac, uint senderIP,
                                  byte* targetMac, uint targetIP)
    {
        if (buffer == null || senderMac == null || targetMac == null)
            return 0;

        var packet = (ArpPacket*)buffer;

        // Hardware type: Ethernet (big-endian)
        packet->HardwareTypeRaw = SwapBytes(ArpHardwareType.Ethernet);

        // Protocol type: IPv4 (big-endian)
        packet->ProtocolTypeRaw = SwapBytes(EtherType.IPv4);

        // Lengths
        packet->HardwareLength = 6;
        packet->ProtocolLength = 4;

        // Operation: Reply (big-endian)
        packet->OperationRaw = SwapBytes(ArpOperation.Reply);

        // Sender MAC (our MAC)
        for (int i = 0; i < 6; i++)
            packet->SenderMac[i] = senderMac[i];

        // Sender IP (our IP, network byte order)
        packet->SenderIP[0] = (byte)(senderIP >> 24);
        packet->SenderIP[1] = (byte)(senderIP >> 16);
        packet->SenderIP[2] = (byte)(senderIP >> 8);
        packet->SenderIP[3] = (byte)senderIP;

        // Target MAC (requester's MAC)
        for (int i = 0; i < 6; i++)
            packet->TargetMac[i] = targetMac[i];

        // Target IP (requester's IP, network byte order)
        packet->TargetIP[0] = (byte)(targetIP >> 24);
        packet->TargetIP[1] = (byte)(targetIP >> 16);
        packet->TargetIP[2] = (byte)(targetIP >> 8);
        packet->TargetIP[3] = (byte)targetIP;

        return ArpPacket.Size;
    }

    /// <summary>
    /// Extract sender IP from ARP packet (host byte order).
    /// </summary>
    public static uint GetSenderIP(ArpPacket* packet)
    {
        return ((uint)packet->SenderIP[0] << 24) |
               ((uint)packet->SenderIP[1] << 16) |
               ((uint)packet->SenderIP[2] << 8) |
               packet->SenderIP[3];
    }

    /// <summary>
    /// Extract target IP from ARP packet (host byte order).
    /// </summary>
    public static uint GetTargetIP(ArpPacket* packet)
    {
        return ((uint)packet->TargetIP[0] << 24) |
               ((uint)packet->TargetIP[1] << 16) |
               ((uint)packet->TargetIP[2] << 8) |
               packet->TargetIP[3];
    }

    /// <summary>
    /// Convert IP address to host byte order from network byte order.
    /// </summary>
    public static uint IPFromBytes(byte* ip)
    {
        return ((uint)ip[0] << 24) | ((uint)ip[1] << 16) | ((uint)ip[2] << 8) | ip[3];
    }

    /// <summary>
    /// Convert dotted decimal string to IP address (host byte order).
    /// </summary>
    /// <param name="a">First octet.</param>
    /// <param name="b">Second octet.</param>
    /// <param name="c">Third octet.</param>
    /// <param name="d">Fourth octet.</param>
    /// <returns>IP address in host byte order.</returns>
    public static uint MakeIP(byte a, byte b, byte c, byte d)
    {
        return ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d;
    }

    private static ushort SwapBytes(ushort value)
    {
        return (ushort)((value >> 8) | (value << 8));
    }
}
