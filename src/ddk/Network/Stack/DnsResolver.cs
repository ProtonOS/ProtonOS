// ProtonOS DDK - DNS Resolver
// High-level DNS resolver with timeout and retry support.

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// DNS resolver for hostname-to-IP resolution.
/// Uses the network stack's UDP API to communicate with DNS servers.
/// </summary>
public unsafe class DnsResolver
{
    private NetworkStack _stack;
    private ushort _nextTransactionId;

    /// <summary>
    /// Create a new DNS resolver.
    /// </summary>
    /// <param name="stack">Network stack to use for communication.</param>
    public DnsResolver(NetworkStack stack)
    {
        _stack = stack;
        // Initialize transaction ID with some randomness based on uptime
        _nextTransactionId = (ushort)(Timer.GetUptimeMilliseconds() & 0xFFFF);
    }

    /// <summary>
    /// Delegate for transmitting a frame.
    /// </summary>
    public delegate void TransmitFrameDelegate(byte* data, int length);

    /// <summary>
    /// Delegate for receiving a frame.
    /// </summary>
    public delegate int ReceiveFrameDelegate(byte* buffer, int maxLength);

    /// <summary>
    /// Resolve a hostname to an IPv4 address.
    /// </summary>
    /// <param name="hostname">Hostname to resolve (null-terminated or with explicit length).</param>
    /// <param name="hostnameLen">Length of hostname.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <param name="transmit">Delegate to transmit frames.</param>
    /// <param name="receive">Delegate to receive frames.</param>
    /// <returns>IPv4 address in host byte order, or 0 on failure.</returns>
    public uint Resolve(byte* hostname, int hostnameLen, int timeoutMs,
                        TransmitFrameDelegate transmit, ReceiveFrameDelegate receive)
    {
        if (hostname == null || hostnameLen <= 0)
            return 0;

        uint dnsServer = _stack.Config.DnsServer;
        if (dnsServer == 0)
        {
            Debug.WriteLine("[DNS] No DNS server configured");
            return 0;
        }

        // Allocate query buffer on stack
        byte* queryBuffer = stackalloc byte[DNS.MaxMessageSize];

        // Generate transaction ID
        ushort transactionId = _nextTransactionId++;

        // Build DNS query
        int queryLen = DNS.BuildQuery(queryBuffer, transactionId, hostname, hostnameLen);
        if (queryLen <= 0)
        {
            Debug.WriteLine("[DNS] Failed to build query");
            return 0;
        }

        Debug.Write("[DNS] Resolving hostname via ");
        PrintIP(dnsServer);
        Debug.WriteLine();

        // Send query via UDP
        ushort localPort = 53000;  // Use a high port for our queries
        int sent = _stack.SendUdp(dnsServer, localPort, DNS.Port, queryBuffer, queryLen);
        if (sent == 0)
        {
            Debug.WriteLine("[DNS] Failed to send query (ARP needed?)");
            return 0;
        }

        // Transmit the UDP packet
        int pendingLen = _stack.GetPendingTxLen();
        if (pendingLen > 0)
        {
            byte* txBuf = _stack.GetTxBuffer();
            transmit(txBuf, pendingLen);
            Debug.Write("[DNS] Query sent: ");
            Debug.WriteDecimal((uint)pendingLen);
            Debug.WriteLine(" bytes");
        }

        // Wait for response
        ulong startTime = Timer.GetUptimeMilliseconds();
        byte* rxBuffer = stackalloc byte[1514];
        byte* responseBuffer = stackalloc byte[DNS.MaxMessageSize];

        while (true)
        {
            ulong elapsed = Timer.GetUptimeMilliseconds() - startTime;
            if (elapsed >= (ulong)timeoutMs)
            {
                Debug.WriteLine("[DNS] Timeout waiting for response");
                return 0;
            }

            // Receive and process frames
            int rxLen = receive(rxBuffer, 1514);
            if (rxLen > 0)
            {
                _stack.ProcessFrame(rxBuffer, rxLen);

                // Check for UDP response
                if (_stack.UdpAvailable() > 0)
                {
                    uint srcIP;
                    ushort srcPort, destPort;
                    int recvLen = _stack.ReceiveUdp(out srcIP, out srcPort, out destPort,
                                                     responseBuffer, DNS.MaxMessageSize);

                    if (recvLen > 0 && srcIP == dnsServer && srcPort == DNS.Port)
                    {
                        Debug.Write("[DNS] Received response: ");
                        Debug.WriteDecimal((uint)recvLen);
                        Debug.WriteLine(" bytes");

                        // Parse response
                        uint resolvedIP;
                        if (DNS.ParseResponse(responseBuffer, recvLen, transactionId, out resolvedIP))
                        {
                            Debug.Write("[DNS] Resolved to ");
                            PrintIP(resolvedIP);
                            Debug.WriteLine();
                            return resolvedIP;
                        }
                        else
                        {
                            Debug.WriteLine("[DNS] Failed to parse response");
                            return 0;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Resolve a hostname string to an IPv4 address.
    /// </summary>
    /// <param name="hostname">Hostname string.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    /// <param name="transmit">Delegate to transmit frames.</param>
    /// <param name="receive">Delegate to receive frames.</param>
    /// <returns>IPv4 address in host byte order, or 0 on failure.</returns>
    public uint Resolve(string hostname, int timeoutMs,
                        TransmitFrameDelegate transmit, ReceiveFrameDelegate receive)
    {
        if (string.IsNullOrEmpty(hostname))
            return 0;

        // Check if hostname is already an IP address
        uint ip = TryParseIPAddress(hostname);
        if (ip != 0)
            return ip;

        // Convert string to byte array on stack
        int len = hostname.Length;
        byte* hostnameBytes = stackalloc byte[len];
        for (int i = 0; i < len; i++)
        {
            char c = hostname[i];
            if (c > 127)
            {
                Debug.WriteLine("[DNS] Non-ASCII hostname not supported");
                return 0;
            }
            hostnameBytes[i] = (byte)c;
        }

        return Resolve(hostnameBytes, len, timeoutMs, transmit, receive);
    }

    /// <summary>
    /// Try to parse a string as an IP address (e.g., "192.168.1.1").
    /// </summary>
    /// <param name="s">String to parse.</param>
    /// <returns>IP address in host byte order, or 0 if not a valid IP.</returns>
    private static uint TryParseIPAddress(string s)
    {
        if (string.IsNullOrEmpty(s))
            return 0;

        int[] octets = new int[4];
        int octetIndex = 0;
        int currentValue = 0;
        bool hasDigit = false;

        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];

            if (c >= '0' && c <= '9')
            {
                currentValue = currentValue * 10 + (c - '0');
                if (currentValue > 255)
                    return 0;
                hasDigit = true;
            }
            else if (c == '.')
            {
                if (!hasDigit || octetIndex >= 3)
                    return 0;
                octets[octetIndex++] = currentValue;
                currentValue = 0;
                hasDigit = false;
            }
            else
            {
                // Non-digit, non-dot character - not an IP
                return 0;
            }
        }

        // Final octet
        if (!hasDigit || octetIndex != 3)
            return 0;
        octets[3] = currentValue;

        // Convert to uint (host byte order: MSB first)
        return ((uint)octets[0] << 24) |
               ((uint)octets[1] << 16) |
               ((uint)octets[2] << 8) |
               (uint)octets[3];
    }

    /// <summary>
    /// Print an IP address for debugging.
    /// </summary>
    private static void PrintIP(uint ip)
    {
        Debug.WriteDecimal((ip >> 24) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 16) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal((ip >> 8) & 0xFF);
        Debug.Write(".");
        Debug.WriteDecimal(ip & 0xFF);
    }
}
