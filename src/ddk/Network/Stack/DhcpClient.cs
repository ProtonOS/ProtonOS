// ProtonOS DDK - DHCP Client
// High-level DHCP client with state machine for automatic network configuration.

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// DHCP client state.
/// </summary>
public enum DhcpState
{
    Init,       // Initial state, not configured
    Selecting,  // Sent DISCOVER, waiting for OFFER
    Requesting, // Sent REQUEST, waiting for ACK
    Bound,      // Have valid lease
    Failed      // DHCP failed
}

/// <summary>
/// DHCP client for automatic network configuration.
/// </summary>
public unsafe class DhcpClient
{
    /// <summary>
    /// Delegate for transmitting a frame.
    /// </summary>
    public delegate void TransmitFrameDelegate(byte* data, int length);

    /// <summary>
    /// Delegate for receiving a frame.
    /// </summary>
    public delegate int ReceiveFrameDelegate(byte* buffer, int maxLength);

    private NetworkStack _stack;
    private uint _xid;
    private DhcpState _state;
    private DhcpResponse _currentLease;

    /// <summary>
    /// Get current DHCP state.
    /// </summary>
    public DhcpState State => _state;

    /// <summary>
    /// Get current lease information (valid when State == Bound).
    /// </summary>
    public DhcpResponse CurrentLease => _currentLease;

    /// <summary>
    /// Create a new DHCP client.
    /// </summary>
    /// <param name="stack">Network stack to configure.</param>
    public DhcpClient(NetworkStack stack)
    {
        _stack = stack;
        _state = DhcpState.Init;
        // Generate XID from uptime
        _xid = (uint)(Timer.GetUptimeMilliseconds() & 0xFFFFFFFF);
    }

    /// <summary>
    /// Perform DHCP configuration (DISCOVER -> OFFER -> REQUEST -> ACK).
    /// </summary>
    /// <param name="timeoutMs">Total timeout in milliseconds.</param>
    /// <param name="transmit">Frame transmit delegate.</param>
    /// <param name="receive">Frame receive delegate.</param>
    /// <returns>True if configuration succeeded, false on timeout/failure.</returns>
    public bool Configure(int timeoutMs, TransmitFrameDelegate transmit,
                         ReceiveFrameDelegate receive)
    {
        if (transmit == null || receive == null)
            return false;

        byte* macAddress = _stack.MacAddress;
        if (macAddress == null)
            return false;

        Debug.WriteLine("[DHCP] Starting configuration...");

        ulong startTime = Timer.GetUptimeMilliseconds();
        int discoverTimeout = timeoutMs / 2;  // Half time for DISCOVER phase
        int requestTimeout = timeoutMs / 2;   // Half time for REQUEST phase

        // Phase 1: DISCOVER -> OFFER
        _state = DhcpState.Selecting;
        DhcpResponse offer;
        if (!SendDiscoverAndWaitForOffer(discoverTimeout, transmit, receive, out offer))
        {
            _state = DhcpState.Failed;
            Debug.WriteLine("[DHCP] No OFFER received");
            return false;
        }

        Debug.Write("[DHCP] Got OFFER: IP=");
        DHCP.PrintIP(offer.YourIP);
        Debug.Write(" Server=");
        DHCP.PrintIP(offer.ServerIP);
        Debug.WriteLine();

        // Phase 2: REQUEST -> ACK
        _state = DhcpState.Requesting;
        DhcpResponse ack;
        if (!SendRequestAndWaitForAck(requestTimeout, transmit, receive,
                                       offer.YourIP, offer.ServerIP, out ack))
        {
            _state = DhcpState.Failed;
            Debug.WriteLine("[DHCP] No ACK received");
            return false;
        }

        // Verify we got ACK (not NAK)
        if (ack.MessageType == DHCP.MessageNak)
        {
            _state = DhcpState.Failed;
            Debug.WriteLine("[DHCP] Received NAK");
            return false;
        }

        // Success! Configure the network stack
        _currentLease = ack;
        _state = DhcpState.Bound;

        Debug.WriteLine("[DHCP] Configuration complete:");
        Debug.Write("  IP: ");
        DHCP.PrintIP(ack.YourIP);
        Debug.WriteLine();
        Debug.Write("  Subnet: ");
        DHCP.PrintIP(ack.SubnetMask);
        Debug.WriteLine();
        Debug.Write("  Gateway: ");
        DHCP.PrintIP(ack.Gateway);
        Debug.WriteLine();
        Debug.Write("  DNS: ");
        DHCP.PrintIP(ack.DnsServer);
        Debug.WriteLine();
        Debug.Write("  Lease: ");
        Debug.WriteDecimal(ack.LeaseTime);
        Debug.WriteLine(" seconds");

        // Apply configuration to network stack
        _stack.Configure(ack.YourIP, ack.SubnetMask, ack.Gateway,
                        ack.DnsServer, ack.DnsServer2);

        return true;
    }

    /// <summary>
    /// Send DHCPDISCOVER and wait for DHCPOFFER.
    /// </summary>
    private bool SendDiscoverAndWaitForOffer(int timeoutMs,
                                              TransmitFrameDelegate transmit,
                                              ReceiveFrameDelegate receive,
                                              out DhcpResponse offer)
    {
        offer = new DhcpResponse();
        byte* macAddress = _stack.MacAddress;

        // Build DISCOVER packet
        byte* discoverBuf = stackalloc byte[DHCP.MaxPacketSize];
        int discoverLen = DHCP.BuildDiscover(discoverBuf, _xid, macAddress);
        if (discoverLen == 0)
            return false;

        // Send as broadcast
        Debug.WriteLine("[DHCP] Sending DISCOVER...");
        int frameLen = _stack.SendDhcpBroadcast(DHCP.ClientPort, DHCP.ServerPort,
                                                 discoverBuf, discoverLen);
        if (frameLen == 0)
        {
            Debug.WriteLine("[DHCP] Failed to build broadcast frame");
            return false;
        }

        // Transmit
        byte* txBuf = _stack.GetTxBuffer();
        transmit(txBuf, frameLen);

        // Wait for OFFER
        return WaitForResponse(timeoutMs, receive, DHCP.MessageOffer, out offer);
    }

    /// <summary>
    /// Send DHCPREQUEST and wait for DHCPACK.
    /// </summary>
    private bool SendRequestAndWaitForAck(int timeoutMs,
                                           TransmitFrameDelegate transmit,
                                           ReceiveFrameDelegate receive,
                                           uint requestedIP, uint serverIP,
                                           out DhcpResponse ack)
    {
        ack = new DhcpResponse();
        byte* macAddress = _stack.MacAddress;

        // Build REQUEST packet
        byte* requestBuf = stackalloc byte[DHCP.MaxPacketSize];
        int requestLen = DHCP.BuildRequest(requestBuf, _xid, macAddress,
                                            requestedIP, serverIP);
        if (requestLen == 0)
            return false;

        // Send as broadcast (REQUEST is still broadcast per RFC)
        Debug.WriteLine("[DHCP] Sending REQUEST...");
        int frameLen = _stack.SendDhcpBroadcast(DHCP.ClientPort, DHCP.ServerPort,
                                                 requestBuf, requestLen);
        if (frameLen == 0)
        {
            Debug.WriteLine("[DHCP] Failed to build broadcast frame");
            return false;
        }

        // Transmit
        byte* txBuf = _stack.GetTxBuffer();
        transmit(txBuf, frameLen);

        // Wait for ACK (or NAK)
        DhcpResponse response;
        if (!WaitForResponse(timeoutMs, receive, 0, out response))  // 0 = any type
            return false;

        // Accept ACK or NAK
        if (response.MessageType == DHCP.MessageAck ||
            response.MessageType == DHCP.MessageNak)
        {
            ack = response;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Wait for a DHCP response of the specified type.
    /// </summary>
    private bool WaitForResponse(int timeoutMs, ReceiveFrameDelegate receive,
                                  byte expectedType, out DhcpResponse response)
    {
        response = new DhcpResponse();
        ulong startTime = Timer.GetUptimeMilliseconds();
        byte* rxBuffer = stackalloc byte[1514];
        byte* udpData = stackalloc byte[DHCP.MaxPacketSize];

        while (true)
        {
            ulong elapsed = Timer.GetUptimeMilliseconds() - startTime;
            if (elapsed >= (ulong)timeoutMs)
                return false;

            // Receive frame
            int rxLen = receive(rxBuffer, 1514);
            if (rxLen > 0)
            {
                // Process frame through stack (handles ARP, etc.)
                _stack.ProcessFrame(rxBuffer, rxLen);

                // Check for DHCP response in UDP queue
                while (_stack.UdpAvailable() > 0)
                {
                    uint srcIP;
                    ushort srcPort, destPort;
                    int udpLen = _stack.ReceiveUdp(out srcIP, out srcPort, out destPort,
                                                    udpData, DHCP.MaxPacketSize);

                    if (udpLen > 0 && srcPort == DHCP.ServerPort && destPort == DHCP.ClientPort)
                    {
                        Debug.Write("[DHCP] Received response: ");
                        Debug.WriteDecimal((uint)udpLen);
                        Debug.WriteLine(" bytes");

                        DhcpResponse parsed;
                        if (DHCP.ParseResponse(udpData, udpLen, _xid, out parsed))
                        {
                            // If expectedType is 0, accept any type
                            if (expectedType == 0 || parsed.MessageType == expectedType)
                            {
                                response = parsed;
                                return true;
                            }
                        }
                    }
                }
            }
        }
    }
}
