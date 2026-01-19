// ProtonOS DDK - Network Manager
// Central management of network interfaces and configuration.

using System.Collections.Generic;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Stack;
using ProtonOS.DDK.Storage;

namespace ProtonOS.DDK.Network;

/// <summary>
/// Central manager for network interfaces.
/// Handles interface registration, naming, and configuration.
/// </summary>
public static class NetworkManager
{
    private static List<NetworkInterface>? _interfaces;
    private static Dictionary<InterfaceType, int>? _interfaceCounters;
    private static bool _initialized;

    /// <summary>
    /// Get all registered network interfaces (as interface for API compatibility).
    /// </summary>
    public static IReadOnlyList<NetworkInterface> Interfaces =>
        _interfaces ?? (IReadOnlyList<NetworkInterface>)new List<NetworkInterface>();

    /// <summary>
    /// Get the concrete list of network interfaces (internal, avoids interface dispatch).
    /// </summary>
    internal static List<NetworkInterface>? InterfacesList => _interfaces;

    /// <summary>
    /// Initialize the NetworkManager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        Debug.WriteLine("[NetMgr] Initializing NetworkManager...");

        _interfaces = new List<NetworkInterface>();
        _interfaceCounters = new Dictionary<InterfaceType, int>();
        _initialized = true;

        // Create loopback interface
        var lo = new NetworkInterface("lo", InterfaceType.Loopback, null);
        lo.IPAddress = 0x7F000001;     // 127.0.0.1
        lo.SubnetMask = 0xFF000000;    // 255.0.0.0
        lo.State = InterfaceState.Configured;
        lo.Mode = ConfigMode.Static;
        _interfaces.Add(lo);

        // Set counter so next loopback would be "lo1"
        _interfaceCounters[InterfaceType.Loopback] = 1;

        Debug.WriteLine("[NetMgr] Created loopback interface (lo)");
    }

    /// <summary>
    /// Register a new network interface.
    /// </summary>
    /// <param name="type">Interface type.</param>
    /// <param name="device">Underlying network device.</param>
    /// <returns>The newly created NetworkInterface.</returns>
    public static NetworkInterface? RegisterInterface(InterfaceType type, INetworkDevice? device)
    {
        if (!_initialized)
            Initialize();

        string name = GenerateInterfaceName(type);
        var iface = new NetworkInterface(name, type, device);
        _interfaces!.Add(iface);

        Debug.Write("[NetMgr] Registered interface: ");
        Debug.WriteLine(name);

        return iface;
    }

    /// <summary>
    /// Get an interface by name.
    /// </summary>
    /// <param name="name">Interface name (e.g., "eth0").</param>
    /// <returns>The interface, or null if not found.</returns>
    public static NetworkInterface? GetInterface(string name)
    {
        if (_interfaces == null)
            return null;

        foreach (var iface in _interfaces)
        {
            if (iface.Name == name)
                return iface;
        }
        return null;
    }

    /// <summary>
    /// Get the first configured interface (useful for getting default gateway).
    /// </summary>
    public static NetworkInterface? GetDefaultInterface()
    {
        if (_interfaces == null)
            return null;

        foreach (var iface in _interfaces)
        {
            if (iface.State == InterfaceState.Configured && iface.Type != InterfaceType.Loopback)
                return iface;
        }
        return null;
    }

    /// <summary>
    /// Configure an interface with DHCP.
    /// </summary>
    /// <param name="iface">Interface to configure.</param>
    /// <param name="transmit">Frame transmit delegate.</param>
    /// <param name="receive">Frame receive delegate.</param>
    /// <param name="timeoutMs">Configuration timeout.</param>
    /// <returns>True if configuration succeeded.</returns>
    public static bool ConfigureWithDhcp(NetworkInterface iface,
                                          DhcpClient.TransmitFrameDelegate transmit,
                                          DhcpClient.ReceiveFrameDelegate receive,
                                          int timeoutMs = 10000)
    {
        if (iface.Stack == null)
        {
            Debug.WriteLine("[NetMgr] Cannot configure: no network stack");
            return false;
        }

        iface.State = InterfaceState.Configuring;
        iface.Mode = ConfigMode.DHCP;

        var dhcp = new DhcpClient(iface.Stack);
        iface.DhcpClient = dhcp;

        Debug.Write("[NetMgr] Configuring ");
        Debug.Write(iface.Name);
        Debug.WriteLine(" via DHCP...");

        if (dhcp.Configure(timeoutMs, transmit, receive))
        {
            // Copy configuration from DHCP response
            var lease = dhcp.CurrentLease;
            iface.IPAddress = lease.YourIP;
            iface.SubnetMask = lease.SubnetMask;
            iface.Gateway = lease.Gateway;
            iface.DnsServer = lease.DnsServer;
            iface.DnsServer2 = lease.DnsServer2;
            iface.State = InterfaceState.Configured;

            Debug.Write("[NetMgr] ");
            Debug.Write(iface.Name);
            Debug.Write(" configured: ");
            DHCP.PrintIP(iface.IPAddress);
            Debug.WriteLine();

            return true;
        }

        Debug.Write("[NetMgr] ");
        Debug.Write(iface.Name);
        Debug.WriteLine(" DHCP configuration failed");
        iface.State = InterfaceState.Up;
        return false;
    }

    /// <summary>
    /// Configure an interface with static IP settings.
    /// </summary>
    /// <param name="iface">Interface to configure.</param>
    /// <param name="ip">IP address (host byte order).</param>
    /// <param name="subnet">Subnet mask (host byte order).</param>
    /// <param name="gateway">Gateway (host byte order).</param>
    /// <param name="dns">Primary DNS (host byte order).</param>
    /// <param name="dns2">Secondary DNS (host byte order).</param>
    /// <returns>True if configuration succeeded.</returns>
    public static bool ConfigureStatic(NetworkInterface iface,
                                        uint ip, uint subnet, uint gateway,
                                        uint dns, uint dns2 = 0)
    {
        if (iface.Stack == null && iface.Type != InterfaceType.Loopback)
        {
            Debug.WriteLine("[NetMgr] Cannot configure: no network stack");
            return false;
        }

        iface.Mode = ConfigMode.Static;
        iface.IPAddress = ip;
        iface.SubnetMask = subnet;
        iface.Gateway = gateway;
        iface.DnsServer = dns;
        iface.DnsServer2 = dns2;

        // Apply to network stack if present
        if (iface.Stack != null)
        {
            iface.Stack.Configure(ip, subnet, gateway, dns, dns2);
        }

        iface.State = InterfaceState.Configured;

        Debug.Write("[NetMgr] ");
        Debug.Write(iface.Name);
        Debug.Write(" configured (static): ");
        DHCP.PrintIP(ip);
        Debug.WriteLine();

        return true;
    }

    /// <summary>
    /// Load interface configuration from a file.
    /// </summary>
    /// <param name="configPath">Path to configuration file.</param>
    /// <returns>Number of interfaces configured from file.</returns>
    public static unsafe int ConfigureFromFile(string configPath = "/etc/network/interfaces")
    {
        if (!_initialized)
            Initialize();

        if (!VFS.Exists(configPath))
        {
            Debug.Write("[NetMgr] Config file not found: ");
            Debug.WriteLine(configPath);
            return 0;
        }

        IFileHandle? handle;
        var result = VFS.OpenFile(configPath, FileMode.Open, FileAccess.Read, out handle);
        if (result != FileResult.Success || handle == null)
        {
            Debug.WriteLine("[NetMgr] Failed to open config file");
            return 0;
        }

        // Read file content
        byte* buffer = stackalloc byte[4096];
        int bytesRead = handle.Read(buffer, 4096);
        handle.Dispose();

        if (bytesRead <= 0)
        {
            Debug.WriteLine("[NetMgr] Config file empty");
            return 0;
        }

        Debug.Write("[NetMgr] Loaded config file: ");
        Debug.WriteDecimal((uint)bytesRead);
        Debug.WriteLine(" bytes");

        // Parse config
        InterfaceConfig[] configs = new InterfaceConfig[16];
        int configCount;
        if (!NetworkConfigParser.Parse(buffer, bytesRead, configs, out configCount))
        {
            Debug.WriteLine("[NetMgr] Failed to parse config file");
            return 0;
        }

        Debug.Write("[NetMgr] Parsed ");
        Debug.WriteDecimal((uint)configCount);
        Debug.WriteLine(" interface configurations");

        // Apply configurations to matching interfaces
        int appliedCount = 0;
        for (int i = 0; i < configCount; i++)
        {
            var cfg = configs[i];
            var iface = GetInterface(cfg.Name);
            if (iface == null)
            {
                Debug.Write("[NetMgr] Interface not found: ");
                Debug.WriteLine(cfg.Name);
                continue;
            }

            iface.Mode = cfg.Mode;
            iface.AutoStart = cfg.AutoStart;

            if (cfg.Mode == ConfigMode.Static)
            {
                iface.IPAddress = cfg.Address;
                iface.SubnetMask = cfg.Netmask;
                iface.Gateway = cfg.Gateway;
                iface.DnsServer = cfg.DnsServer;
                iface.DnsServer2 = cfg.DnsServer2;
            }

            appliedCount++;
        }

        return appliedCount;
    }

    /// <summary>
    /// Print status of all interfaces.
    /// </summary>
    public static void PrintStatus()
    {
        if (_interfaces == null)
        {
            Debug.WriteLine("[NetMgr] Not initialized");
            return;
        }

        Debug.WriteLine("[NetMgr] Interface status:");
        foreach (var iface in _interfaces)
        {
            Debug.Write("  ");
            Debug.Write(iface.Name);
            Debug.Write(": ");

            switch (iface.State)
            {
                case InterfaceState.Down:
                    Debug.Write("DOWN");
                    break;
                case InterfaceState.Up:
                    Debug.Write("UP (no IP)");
                    break;
                case InterfaceState.Configuring:
                    Debug.Write("CONFIGURING");
                    break;
                case InterfaceState.Configured:
                    DHCP.PrintIP(iface.IPAddress);
                    Debug.Write("/");
                    // Calculate CIDR prefix from subnet mask
                    uint mask = iface.SubnetMask;
                    int prefix = 0;
                    while (mask != 0)
                    {
                        prefix++;
                        mask <<= 1;
                    }
                    Debug.WriteDecimal((uint)prefix);
                    break;
            }

            if (iface.Mode == ConfigMode.DHCP)
                Debug.Write(" (DHCP)");
            else if (iface.Mode == ConfigMode.Static)
                Debug.Write(" (static)");

            Debug.WriteLine();
        }
    }

    /// <summary>
    /// Generate a unique interface name.
    /// </summary>
    private static string GenerateInterfaceName(InterfaceType type)
    {
        string prefix = type switch
        {
            InterfaceType.Loopback => "lo",
            InterfaceType.Ethernet => "eth",
            InterfaceType.WiFi => "wifi",
            InterfaceType.Bridge => "br",
            InterfaceType.Virtual => "veth",
            _ => "net"
        };

        if (!_interfaceCounters!.TryGetValue(type, out int counter))
            counter = 0;

        _interfaceCounters[type] = counter + 1;

        return prefix + counter.ToString();
    }
}
