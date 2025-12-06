// ProtonOS DDK - Network Device Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Network;

/// <summary>
/// Network device capabilities.
/// </summary>
[Flags]
public enum NetworkCapabilities
{
    None = 0,

    /// <summary>Device supports transmit.</summary>
    Transmit = 1 << 0,

    /// <summary>Device supports receive.</summary>
    Receive = 1 << 1,

    /// <summary>Device supports promiscuous mode.</summary>
    Promiscuous = 1 << 2,

    /// <summary>Device supports multicast.</summary>
    Multicast = 1 << 3,

    /// <summary>Device supports VLAN tagging.</summary>
    VlanTagging = 1 << 4,

    /// <summary>Device supports checksum offload.</summary>
    ChecksumOffload = 1 << 5,

    /// <summary>Device supports TCP segmentation offload.</summary>
    TsoOffload = 1 << 6,

    /// <summary>Device supports large receive offload.</summary>
    LroOffload = 1 << 7,

    /// <summary>Device supports scatter-gather DMA.</summary>
    ScatterGather = 1 << 8,

    /// <summary>Device supports interrupt coalescing.</summary>
    InterruptCoalescing = 1 << 9,

    /// <summary>Basic transmit and receive.</summary>
    Basic = Transmit | Receive,
}

/// <summary>
/// Network link speed.
/// </summary>
public enum LinkSpeed
{
    Unknown = 0,
    Speed10Mbps = 10,
    Speed100Mbps = 100,
    Speed1Gbps = 1000,
    Speed2_5Gbps = 2500,
    Speed5Gbps = 5000,
    Speed10Gbps = 10000,
    Speed25Gbps = 25000,
    Speed40Gbps = 40000,
    Speed100Gbps = 100000,
}

/// <summary>
/// Network link duplex mode.
/// </summary>
public enum LinkDuplex
{
    Unknown,
    Half,
    Full,
}

/// <summary>
/// Network link status.
/// </summary>
public struct LinkStatus
{
    /// <summary>True if link is up.</summary>
    public bool IsUp;

    /// <summary>Link speed.</summary>
    public LinkSpeed Speed;

    /// <summary>Duplex mode.</summary>
    public LinkDuplex Duplex;

    /// <summary>True if auto-negotiation is complete.</summary>
    public bool AutoNegotiationComplete;
}

/// <summary>
/// Network device statistics.
/// </summary>
public struct NetworkStats
{
    /// <summary>Total packets received.</summary>
    public ulong RxPackets;

    /// <summary>Total bytes received.</summary>
    public ulong RxBytes;

    /// <summary>Receive errors.</summary>
    public ulong RxErrors;

    /// <summary>Dropped receive packets.</summary>
    public ulong RxDropped;

    /// <summary>Total packets transmitted.</summary>
    public ulong TxPackets;

    /// <summary>Total bytes transmitted.</summary>
    public ulong TxBytes;

    /// <summary>Transmit errors.</summary>
    public ulong TxErrors;

    /// <summary>Dropped transmit packets.</summary>
    public ulong TxDropped;

    /// <summary>Collisions detected.</summary>
    public ulong Collisions;
}

/// <summary>
/// Result of network operations.
/// </summary>
public enum NetworkResult
{
    Success = 0,
    NoLink = -1,
    NoBuffer = -2,
    PacketTooLarge = -3,
    IoError = -4,
    NotReady = -5,
    Busy = -6,
}

/// <summary>
/// Delegate for receive packet callback.
/// </summary>
/// <param name="data">Pointer to packet data</param>
/// <param name="length">Length of packet in bytes</param>
public unsafe delegate void PacketReceivedCallback(byte* data, int length);

/// <summary>
/// Interface for network device drivers.
/// </summary>
public unsafe interface INetworkDevice : IDriver
{
    /// <summary>
    /// Device name for identification.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// MAC address (6 bytes).
    /// </summary>
    ReadOnlySpan<byte> MacAddress { get; }

    /// <summary>
    /// Device capabilities.
    /// </summary>
    NetworkCapabilities Capabilities { get; }

    /// <summary>
    /// Current link status.
    /// </summary>
    LinkStatus LinkStatus { get; }

    /// <summary>
    /// True if link is up.
    /// </summary>
    bool IsLinkUp => LinkStatus.IsUp;

    /// <summary>
    /// Maximum transmission unit (default 1500).
    /// </summary>
    uint MTU { get; set; }

    /// <summary>
    /// Device statistics.
    /// </summary>
    NetworkStats Statistics { get; }

    /// <summary>
    /// Transmit a packet.
    /// </summary>
    /// <param name="packet">Pointer to packet data (starting with Ethernet header)</param>
    /// <param name="length">Length of packet in bytes</param>
    /// <returns>NetworkResult indicating success or failure</returns>
    NetworkResult Transmit(byte* packet, int length);

    /// <summary>
    /// Set the receive callback.
    /// Called from interrupt context when a packet is received.
    /// </summary>
    void SetReceiveCallback(PacketReceivedCallback? callback);

    /// <summary>
    /// Enable/disable promiscuous mode.
    /// </summary>
    void SetPromiscuous(bool enabled);

    /// <summary>
    /// Add a multicast MAC address to the filter.
    /// </summary>
    void AddMulticastAddress(ReadOnlySpan<byte> mac);

    /// <summary>
    /// Remove a multicast MAC address from the filter.
    /// </summary>
    void RemoveMulticastAddress(ReadOnlySpan<byte> mac);

    /// <summary>
    /// Clear all multicast filters.
    /// </summary>
    void ClearMulticastAddresses();

    /// <summary>
    /// Reset device statistics.
    /// </summary>
    void ResetStatistics();
}

/// <summary>
/// Helper for MAC address operations.
/// </summary>
public static class MacAddress
{
    /// <summary>
    /// Broadcast MAC address (FF:FF:FF:FF:FF:FF).
    /// </summary>
    public static readonly byte[] Broadcast = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    /// <summary>
    /// Check if a MAC address is broadcast.
    /// </summary>
    public static bool IsBroadcast(ReadOnlySpan<byte> mac)
    {
        return mac.Length == 6 &&
               mac[0] == 0xFF && mac[1] == 0xFF && mac[2] == 0xFF &&
               mac[3] == 0xFF && mac[4] == 0xFF && mac[5] == 0xFF;
    }

    /// <summary>
    /// Check if a MAC address is multicast.
    /// </summary>
    public static bool IsMulticast(ReadOnlySpan<byte> mac)
    {
        return mac.Length >= 1 && (mac[0] & 0x01) != 0;
    }

    /// <summary>
    /// Format MAC address as string.
    /// </summary>
    public static string Format(ReadOnlySpan<byte> mac)
    {
        if (mac.Length != 6)
            return "??:??:??:??:??:??";

        return $"{mac[0]:X2}:{mac[1]:X2}:{mac[2]:X2}:{mac[3]:X2}:{mac[4]:X2}:{mac[5]:X2}";
    }
}
