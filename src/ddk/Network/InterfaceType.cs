// ProtonOS DDK - Network Interface Types
// Enumeration of supported network interface types.

namespace ProtonOS.DDK.Network;

/// <summary>
/// Network interface types.
/// </summary>
public enum InterfaceType
{
    /// <summary>Unknown interface type.</summary>
    Unknown = 0,

    /// <summary>Loopback interface (lo).</summary>
    Loopback,

    /// <summary>Ethernet interface (eth0, eth1, ...).</summary>
    Ethernet,

    /// <summary>WiFi/wireless interface (wifi0, wifi1, ...).</summary>
    WiFi,

    /// <summary>Bridge interface (br0, br1, ...).</summary>
    Bridge,

    /// <summary>Virtual/tunnel interface (veth0, veth1, ...).</summary>
    Virtual
}

/// <summary>
/// Network interface operational state.
/// </summary>
public enum InterfaceState
{
    /// <summary>Interface is administratively down.</summary>
    Down,

    /// <summary>Interface is up but not configured.</summary>
    Up,

    /// <summary>Interface is being configured (e.g., DHCP in progress).</summary>
    Configuring,

    /// <summary>Interface is up and configured with IP address.</summary>
    Configured
}

/// <summary>
/// Network interface configuration mode.
/// </summary>
public enum ConfigMode
{
    /// <summary>No configuration.</summary>
    None,

    /// <summary>Static IP configuration.</summary>
    Static,

    /// <summary>Dynamic configuration via DHCP.</summary>
    DHCP
}
