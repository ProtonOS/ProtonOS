// ProtonOS DDK - /proc/net/tcp Generator
// Generates TCP connection table in Linux-compatible format.

using ProtonOS.DDK.Network;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.DDK.Storage.Proc.Generators;

/// <summary>
/// Generates content for /proc/net/tcp.
/// Reports TCP connection table from all network interfaces.
/// </summary>
public class NetTcpGenerator : IProcContentGenerator
{
    /// <inheritdoc/>
    public string Generate()
    {
        string result = "";

        // Header line (Linux-style, simplified)
        result = result + "  sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode\n";

        int slot = 0;

        // Enumerate all interfaces and their TCP connections (use concrete list to avoid JIT interface issues)
        var interfaces = NetworkManager.InterfacesList;
        if (interfaces == null)
            return result;
        int interfaceCount = interfaces.Count;
        for (int ifIdx = 0; ifIdx < interfaceCount; ifIdx++)
        {
            var iface = interfaces[ifIdx];
            if (iface.Stack == null)
                continue;

            int slotCount = iface.Stack.TcpConnectionSlots;

            for (int i = 0; i < slotCount; i++)
            {
                var conn = iface.Stack.GetTcpConnection(i);
                if (conn == null)
                    continue;

                // Format: sl local_address:port rem_address:port state
                // Addresses are in hex (network byte order)

                // Slot number
                string slotStr = PadLeft(slot.ToString(), 4);

                // Local address (IP in hex, little endian like Linux) : port
                string localAddr = FormatHexAddress(conn.LocalEndpoint.IP, conn.LocalEndpoint.Port);

                // Remote address
                string remoteAddr = FormatHexAddress(conn.RemoteEndpoint.IP, conn.RemoteEndpoint.Port);

                // State (2-digit hex)
                string stateStr = TcpStateToHex(conn.State);

                // Build line piece by piece
                string line = slotStr;
                line = line + ": ";
                line = line + localAddr;
                line = line + " ";
                line = line + remoteAddr;
                line = line + " ";
                line = line + stateStr;
                line = line + " 00000000:00000000 00:00000000 00000000     0        0 0\n";

                result = result + line;
                slot++;
            }
        }

        return result;
    }

    /// <summary>
    /// Format an IP:port as hex address (like Linux /proc/net/tcp).
    /// </summary>
    private static string FormatHexAddress(uint ip, ushort port)
    {
        // Linux stores IP in network byte order (reversed from host order)
        // So 192.168.1.1 (0xC0A80101) becomes 0101A8C0
        uint networkOrder = ((ip & 0xFF) << 24) |
                           ((ip & 0xFF00) << 8) |
                           ((ip & 0xFF0000) >> 8) |
                           ((ip & 0xFF000000) >> 24);

        string s = ToHex8(networkOrder);
        s = s + ":";
        s = s + ToHex4(port);
        return s;
    }

    /// <summary>
    /// Convert TCP state to 2-digit hex code (Linux format).
    /// </summary>
    private static string TcpStateToHex(TcpState state)
    {
        int code = 0;
        if (state == TcpState.Established) code = 0x01;
        else if (state == TcpState.SynSent) code = 0x02;
        else if (state == TcpState.SynReceived) code = 0x03;
        else if (state == TcpState.FinWait1) code = 0x04;
        else if (state == TcpState.FinWait2) code = 0x05;
        else if (state == TcpState.TimeWait) code = 0x06;
        else if (state == TcpState.Closed) code = 0x07;
        else if (state == TcpState.CloseWait) code = 0x08;
        else if (state == TcpState.LastAck) code = 0x09;
        else if (state == TcpState.Listen) code = 0x0A;
        else if (state == TcpState.Closing) code = 0x0B;
        return ToHex2((byte)code);
    }

    /// <summary>
    /// Convert a byte to a 2-digit uppercase hex string.
    /// </summary>
    private static string ToHex2(byte value)
    {
        char[] hex = { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };
        return new string(new char[] { hex[value >> 4], hex[value & 0xF] });
    }

    /// <summary>
    /// Convert a ushort to a 4-digit uppercase hex string.
    /// </summary>
    private static string ToHex4(ushort value)
    {
        string s = ToHex2((byte)(value >> 8));
        s = s + ToHex2((byte)(value & 0xFF));
        return s;
    }

    /// <summary>
    /// Convert a uint to an 8-digit uppercase hex string.
    /// </summary>
    private static string ToHex8(uint value)
    {
        string s = ToHex4((ushort)(value >> 16));
        s = s + ToHex4((ushort)(value & 0xFFFF));
        return s;
    }

    /// <summary>
    /// Pad a string with leading spaces to the given width.
    /// </summary>
    private static string PadLeft(string str, int width)
    {
        while (str.Length < width)
            str = " " + str;
        return str;
    }
}
