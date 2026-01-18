// ProtonOS DDK - Network Interface
// Represents a single network interface with configuration and state.

using System;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.DDK.Network;

/// <summary>
/// Represents a network interface with its configuration and state.
/// </summary>
public unsafe class NetworkInterface
{
    /// <summary>
    /// Interface name (e.g., "eth0", "wifi0", "lo").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Interface type.
    /// </summary>
    public InterfaceType Type { get; }

    /// <summary>
    /// Underlying network device (null for loopback).
    /// </summary>
    public INetworkDevice? Device { get; }

    /// <summary>
    /// Network stack for this interface.
    /// </summary>
    public NetworkStack? Stack { get; set; }

    /// <summary>
    /// DHCP client (if using DHCP configuration).
    /// </summary>
    public DhcpClient? DhcpClient { get; set; }

    /// <summary>
    /// Current interface state.
    /// </summary>
    public InterfaceState State { get; set; }

    /// <summary>
    /// Configuration mode (static, DHCP, or none).
    /// </summary>
    public ConfigMode Mode { get; set; }

    /// <summary>
    /// IP address (host byte order).
    /// </summary>
    public uint IPAddress { get; set; }

    /// <summary>
    /// Subnet mask (host byte order).
    /// </summary>
    public uint SubnetMask { get; set; }

    /// <summary>
    /// Default gateway (host byte order).
    /// </summary>
    public uint Gateway { get; set; }

    /// <summary>
    /// Primary DNS server (host byte order).
    /// </summary>
    public uint DnsServer { get; set; }

    /// <summary>
    /// Secondary DNS server (host byte order).
    /// </summary>
    public uint DnsServer2 { get; set; }

    /// <summary>
    /// Whether this interface should be configured automatically on boot.
    /// </summary>
    public bool AutoStart { get; set; }

    /// <summary>
    /// MAC address of the device (6 bytes), or empty if not available.
    /// </summary>
    public ReadOnlySpan<byte> MacAddress => Device != null ? Device.MacAddress : ReadOnlySpan<byte>.Empty;

    /// <summary>
    /// Create a new network interface.
    /// </summary>
    /// <param name="name">Interface name.</param>
    /// <param name="type">Interface type.</param>
    /// <param name="device">Underlying network device (can be null for loopback).</param>
    public NetworkInterface(string name, InterfaceType type, INetworkDevice? device)
    {
        Name = name;
        Type = type;
        Device = device;
        State = InterfaceState.Down;
        Mode = ConfigMode.None;
    }

    /// <summary>
    /// Bring the interface up (enable it).
    /// </summary>
    public void Up()
    {
        if (State == InterfaceState.Down)
            State = InterfaceState.Up;
    }

    /// <summary>
    /// Bring the interface down (disable it).
    /// </summary>
    public void Down()
    {
        State = InterfaceState.Down;
        DhcpClient?.Release();
    }

    /// <summary>
    /// Check if the interface has a valid IP configuration.
    /// </summary>
    public bool HasIPAddress => IPAddress != 0;

    /// <summary>
    /// Get the IP address as a formatted string (for display).
    /// </summary>
    public string GetIPAddressString()
    {
        if (IPAddress == 0)
            return "0.0.0.0";

        return $"{(IPAddress >> 24) & 0xFF}.{(IPAddress >> 16) & 0xFF}.{(IPAddress >> 8) & 0xFF}.{IPAddress & 0xFF}";
    }
}
