// ProtonOS DDK - /proc/net/arp Generator
// Generates ARP cache entries in Linux-compatible format.

using ProtonOS.DDK.Network;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/net/arp.
/// Reports ARP cache entries from all network interfaces.
/// </summary>
public unsafe class NetArpGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        // Header line (Linux-style)
        result = result + "IP address       HW type     Flags       HW address            Mask     Device\n";

        // Enumerate all interfaces and their ARP caches (use concrete list to avoid JIT interface issues)
        var interfaces = NetworkManager.InterfacesList;
        if (interfaces == null)
            return result;
        int interfaceCount = interfaces.Count;
        for (int ifIdx = 0; ifIdx < interfaceCount; ifIdx++)
        {
            var iface = interfaces[ifIdx];
            if (iface.Stack == null)
                continue;

            var arpCache = iface.Stack.ArpCache;
            if (arpCache == null)
                continue;

            int entryCount = arpCache.EntryCount;
            byte* mac = stackalloc byte[6];

            for (int i = 0; i < entryCount; i++)
            {
                uint ip;
                bool isStatic;

                if (!arpCache.TryGetEntry(i, out ip, mac, out isStatic))
                    continue;

                // Format IP address (left-padded to 16 chars)
                string ipStr = PadRight(FormatIP(ip), 16);

                // HW type (0x1 = Ethernet)
                // Flags (0x2 = complete, 0x6 = complete + static)
                string flags = isStatic ? "0x6         " : "0x2         ";

                // HW address (MAC)
                string macStr = PadRight(FormatMac(mac), 22);

                // Build line piece by piece
                string line = ipStr;
                line = line + " 0x1         ";
                line = line + flags;
                line = line + macStr;
                line = line + "*        ";
                line = line + iface.Name;
                line = line + "\n";
                result = result + line;
            }
        }

        return result;
    }

    /// <summary>
    /// Format an IP address as a dotted decimal string.
    /// </summary>
    private static string FormatIP(uint ip)
    {
        string s = ((ip >> 24) & 0xFF).ToString();
        s = s + ".";
        s = s + ((ip >> 16) & 0xFF).ToString();
        s = s + ".";
        s = s + ((ip >> 8) & 0xFF).ToString();
        s = s + ".";
        s = s + (ip & 0xFF).ToString();
        return s;
    }

    /// <summary>
    /// Format a MAC address as a colon-separated hex string.
    /// </summary>
    private static string FormatMac(byte* mac)
    {
        string s = ToHex2(mac[0]);
        s = s + ":";
        s = s + ToHex2(mac[1]);
        s = s + ":";
        s = s + ToHex2(mac[2]);
        s = s + ":";
        s = s + ToHex2(mac[3]);
        s = s + ":";
        s = s + ToHex2(mac[4]);
        s = s + ":";
        s = s + ToHex2(mac[5]);
        return s;
    }

    /// <summary>
    /// Convert a byte to a 2-digit lowercase hex string.
    /// </summary>
    private static string ToHex2(byte value)
    {
        char[] hex = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f' };
        return new string(new char[] { hex[value >> 4], hex[value & 0xF] });
    }

    /// <summary>
    /// Pad a string with trailing spaces to the given width.
    /// </summary>
    private static string PadRight(string str, int width)
    {
        while (str.Length < width)
            str = str + " ";
        return str;
    }
}
