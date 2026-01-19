// ProtonOS DDK - /proc/net/dev Generator
// Generates network interface statistics in Linux-compatible format.

using ProtonOS.DDK.Network;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/net/dev.
/// Reports network interface statistics from NetworkManager.
/// </summary>
public class NetDevGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        // Header lines (Linux-style)
        result = result + "Inter-|   Receive                                                |  Transmit\n";
        result = result + " face |bytes    packets errs drop fifo frame compressed multicast|bytes    packets errs drop fifo colls carrier compressed\n";

        // List all interfaces (use concrete list and index loop to avoid JIT interface issues)
        var interfaces = NetworkManager.InterfacesList;
        if (interfaces == null)
            return result;
        int interfaceCount = interfaces.Count;
        for (int ifIdx = 0; ifIdx < interfaceCount; ifIdx++)
        {
            var iface = interfaces[ifIdx];
            // Get stats from network stack if available
            ulong rxBytes = 0, txBytes = 0;
            ulong rxPackets = 0, txPackets = 0;

            if (iface.Stack != null)
            {
                ulong arpReq, arpRep;
                iface.Stack.GetStats(out rxPackets, out txPackets, out arpReq, out arpRep);
            }

            // Format interface name (right-padded to 6 chars, then colon)
            string name = PadRight(iface.Name, 6);

            // Build line piece by piece to avoid multi-arg String.Concat
            // RX stats: bytes packets errs drop fifo frame compressed multicast
            string line = name;
            line = line + ":";
            line = line + PadLeft(rxBytes, 8);
            line = line + " ";
            line = line + PadLeft(rxPackets, 7);
            line = line + "    0    0    0     0          0         0 ";
            // TX stats: bytes packets errs drop fifo colls carrier compressed
            line = line + PadLeft(txBytes, 8);
            line = line + " ";
            line = line + PadLeft(txPackets, 7);
            line = line + "    0    0    0     0       0          0\n";

            result = result + line;
        }

        return result;
    }

    /// <summary>
    /// Pad a number with leading spaces to the given width.
    /// </summary>
    private static string PadLeft(ulong value, int width)
    {
        string str = value.ToString();
        while (str.Length < width)
            str = " " + str;
        return str;
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
