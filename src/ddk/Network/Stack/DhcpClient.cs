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
    Renewing,   // T1 expired, unicast REQUEST to server
    Rebinding,  // T2 expired, broadcast REQUEST to any server
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
    private ulong _leaseAcquiredTime;  // Uptime (ms) when lease was acquired

    /// <summary>
    /// Get current DHCP state.
    /// </summary>
    public DhcpState State => _state;

    /// <summary>
    /// Get current lease information (valid when State == Bound).
    /// </summary>
    public DhcpResponse CurrentLease => _currentLease;

    /// <summary>
    /// Get uptime (ms) when current lease was acquired.
    /// </summary>
    public ulong LeaseAcquiredTime => _leaseAcquiredTime;

    /// <summary>
    /// Check if the current lease has expired.
    /// </summary>
    public bool IsLeaseExpired
    {
        get
        {
            if (_state != DhcpState.Bound && _state != DhcpState.Renewing && _state != DhcpState.Rebinding)
                return true;
            ulong elapsedSec = (Timer.GetUptimeMilliseconds() - _leaseAcquiredTime) / 1000;
            return elapsedSec >= _currentLease.LeaseTime;
        }
    }

    /// <summary>
    /// Check if the lease needs renewal (T1 expired but lease still valid).
    /// </summary>
    public bool NeedsRenewal
    {
        get
        {
            if (_state != DhcpState.Bound)
                return false;
            ulong elapsedSec = (Timer.GetUptimeMilliseconds() - _leaseAcquiredTime) / 1000;
            // TODO: Use T1 from DHCP response after fixing JIT struct parameter issue
            // Default T1 = 50% of lease
            uint t1 = _currentLease.LeaseTime / 2;
            return elapsedSec >= t1;
        }
    }

    /// <summary>
    /// Check if the lease needs rebinding (T2 expired but lease still valid).
    /// </summary>
    public bool NeedsRebinding
    {
        get
        {
            if (_state != DhcpState.Bound && _state != DhcpState.Renewing)
                return false;
            ulong elapsedSec = (Timer.GetUptimeMilliseconds() - _leaseAcquiredTime) / 1000;
            // TODO: Use T2 from DHCP response after fixing JIT struct parameter issue
            // Default T2 = 87.5% of lease
            uint t2 = (_currentLease.LeaseTime * 7) / 8;
            return elapsedSec >= t2;
        }
    }

    /// <summary>
    /// Get seconds remaining until lease expires (0 if expired).
    /// </summary>
    public uint SecondsRemaining
    {
        get
        {
            if (_currentLease.LeaseTime == 0)
                return 0;
            ulong elapsedSec = (Timer.GetUptimeMilliseconds() - _leaseAcquiredTime) / 1000;
            if (elapsedSec >= _currentLease.LeaseTime)
                return 0;
            return _currentLease.LeaseTime - (uint)elapsedSec;
        }
    }

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
        _leaseAcquiredTime = Timer.GetUptimeMilliseconds();

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
        Debug.Write("s, T1=");
        Debug.WriteDecimal(ack.LeaseTime / 2);  // Default T1
        Debug.Write("s, T2=");
        Debug.WriteDecimal((ack.LeaseTime * 7) / 8);  // Default T2
        Debug.WriteLine("s");

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

    /// <summary>
    /// Attempt to renew the DHCP lease.
    /// Should be called periodically when NeedsRenewal or NeedsRebinding returns true.
    /// </summary>
    /// <param name="timeoutMs">Timeout for the renewal attempt.</param>
    /// <param name="transmit">Frame transmit delegate.</param>
    /// <param name="receive">Frame receive delegate.</param>
    /// <returns>True if lease was renewed, false on failure.</returns>
    public bool Renew(int timeoutMs, TransmitFrameDelegate transmit,
                      ReceiveFrameDelegate receive)
    {
        if (transmit == null || receive == null)
            return false;

        // Must be in Bound, Renewing, or Rebinding state
        if (_state != DhcpState.Bound && _state != DhcpState.Renewing && _state != DhcpState.Rebinding)
        {
            Debug.WriteLine("[DHCP] Cannot renew: not in valid state");
            return false;
        }

        // Check if lease has expired
        if (IsLeaseExpired)
        {
            Debug.WriteLine("[DHCP] Lease expired, need full reconfiguration");
            _state = DhcpState.Failed;
            return false;
        }

        byte* macAddress = _stack.MacAddress;
        if (macAddress == null)
            return false;

        // Determine if we should RENEW (T1 expired) or REBIND (T2 expired)
        bool rebinding = NeedsRebinding;
        DhcpState previousState = _state;

        if (rebinding)
        {
            _state = DhcpState.Rebinding;
            Debug.WriteLine("[DHCP] Rebinding (T2 expired, broadcast)...");
        }
        else
        {
            _state = DhcpState.Renewing;
            Debug.WriteLine("[DHCP] Renewing (T1 expired)...");
        }

        // Generate new XID for renewal
        _xid = (uint)(Timer.GetUptimeMilliseconds() & 0xFFFFFFFF);

        // Build renewal REQUEST packet
        byte* requestBuf = stackalloc byte[DHCP.MaxPacketSize];
        int requestLen = DHCP.BuildRenewalRequest(requestBuf, _xid, macAddress,
                                                   _currentLease.YourIP, rebinding);
        if (requestLen == 0)
        {
            _state = previousState;
            return false;
        }

        // Send as broadcast (works for both states; unicast optimization could be added later)
        int frameLen = _stack.SendDhcpBroadcast(DHCP.ClientPort, DHCP.ServerPort,
                                                 requestBuf, requestLen);
        if (frameLen == 0)
        {
            Debug.WriteLine("[DHCP] Failed to build renewal frame");
            _state = previousState;
            return false;
        }

        // Transmit
        byte* txBuf = _stack.GetTxBuffer();
        transmit(txBuf, frameLen);

        // Wait for ACK (or NAK)
        DhcpResponse response;
        if (!WaitForResponse(timeoutMs, receive, 0, out response))
        {
            Debug.WriteLine("[DHCP] No renewal response received");
            _state = previousState;  // Stay in previous state, can retry
            return false;
        }

        // Check response type
        if (response.MessageType == DHCP.MessageNak)
        {
            Debug.WriteLine("[DHCP] Renewal rejected (NAK)");
            _state = DhcpState.Failed;
            return false;
        }

        if (response.MessageType != DHCP.MessageAck)
        {
            Debug.WriteLine("[DHCP] Unexpected response type");
            _state = previousState;
            return false;
        }

        // Success! Update lease
        _currentLease = response;
        _state = DhcpState.Bound;
        _leaseAcquiredTime = Timer.GetUptimeMilliseconds();

        Debug.WriteLine("[DHCP] Lease renewed:");
        Debug.Write("  IP: ");
        DHCP.PrintIP(response.YourIP);
        Debug.Write(", Lease: ");
        Debug.WriteDecimal(response.LeaseTime);
        Debug.Write("s, T1=");
        Debug.WriteDecimal(response.LeaseTime / 2);  // Default T1
        Debug.Write("s, T2=");
        Debug.WriteDecimal((response.LeaseTime * 7) / 8);  // Default T2
        Debug.WriteLine("s");

        // Update network stack configuration in case anything changed
        _stack.Configure(response.YourIP, response.SubnetMask, response.Gateway,
                        response.DnsServer, response.DnsServer2);

        return true;
    }

    /// <summary>
    /// Release the current DHCP lease.
    /// </summary>
    public void Release()
    {
        if (_state == DhcpState.Bound || _state == DhcpState.Renewing || _state == DhcpState.Rebinding)
        {
            Debug.WriteLine("[DHCP] Releasing lease");
            _state = DhcpState.Init;
            _currentLease = new DhcpResponse();
            _leaseAcquiredTime = 0;
        }
    }
}
