// ProtonOS DDK - TCP Connection State Management
// Manages individual TCP connection state and data transfer

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Sockets;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// Represents a TCP connection endpoint.
/// </summary>
public struct TcpEndpoint
{
    public uint IP;
    public ushort Port;

    public TcpEndpoint(uint ip, ushort port)
    {
        IP = ip;
        Port = port;
    }
}

/// <summary>
/// TCP connection state and data management.
/// </summary>
public unsafe class TcpConnection
{
    // Connection endpoints
    private TcpEndpoint _local;
    private TcpEndpoint _remote;

    // Connection state
    private TcpState _state;

    // Sequence numbers
    private uint _sendNext;      // Next sequence number to send (SND.NXT)
    private uint _sendUnack;     // Oldest unacknowledged sequence (SND.UNA)
    private uint _sendWindow;    // Send window size (SND.WND)
    private uint _recvNext;      // Next expected sequence number (RCV.NXT)
    private ushort _recvWindow;  // Receive window size (RCV.WND)

    // Initial sequence numbers (for connection setup)
    private uint _iss;           // Initial send sequence number
    private uint _irs;           // Initial receive sequence number

    // Receive buffer (simple ring buffer)
    private const int RecvBufferSize = 8192;
    private byte[] _recvBuffer;
    private int _recvHead;
    private int _recvTail;
    private int _recvCount;

    // Send buffer
    private const int SendBufferSize = 8192;
    private byte[] _sendBuffer;
    private int _sendHead;
    private int _sendTail;
    private int _sendCount;

    // Connection flags
    private bool _finSent;
    private bool _finReceived;

    // Listener reference for server-side connections
    private TcpListener? _listener;

    /// <summary>
    /// Get or set the listener that created this connection (for server-side connections).
    /// </summary>
    internal TcpListener? Listener
    {
        get => _listener;
        set => _listener = value;
    }

    /// <summary>
    /// Get the local endpoint.
    /// </summary>
    public TcpEndpoint LocalEndpoint => _local;

    /// <summary>
    /// Get the remote endpoint.
    /// </summary>
    public TcpEndpoint RemoteEndpoint => _remote;

    /// <summary>
    /// Get the current connection state.
    /// </summary>
    public TcpState State => _state;

    /// <summary>
    /// Check if connection is established.
    /// </summary>
    public bool IsConnected => _state == TcpState.Established;

    /// <summary>
    /// Check if connection is closed.
    /// </summary>
    public bool IsClosed => _state == TcpState.Closed || _state == TcpState.TimeWait;

    /// <summary>
    /// Get bytes available to read.
    /// </summary>
    public int Available => _recvCount;

    /// <summary>
    /// Get the next sequence number to send.
    /// </summary>
    public uint SendNext => _sendNext;

    /// <summary>
    /// Get the next expected receive sequence.
    /// </summary>
    public uint RecvNext => _recvNext;

    /// <summary>
    /// Get the receive window size.
    /// </summary>
    public ushort RecvWindow => _recvWindow;

    /// <summary>
    /// Create a new TCP connection (for outgoing connections).
    /// </summary>
    public TcpConnection(uint localIP, ushort localPort, uint remoteIP, ushort remotePort)
    {
        _local = new TcpEndpoint(localIP, localPort);
        _remote = new TcpEndpoint(remoteIP, remotePort);
        _state = TcpState.Closed;

        // Initialize sequence number (should be random in production)
        _iss = GenerateISN();
        _sendNext = _iss;
        _sendUnack = _iss;

        // Default window sizes
        _recvWindow = (ushort)RecvBufferSize;
        _sendWindow = RecvBufferSize;

        // Allocate buffers
        _recvBuffer = new byte[RecvBufferSize];
        _sendBuffer = new byte[SendBufferSize];
    }

    /// <summary>
    /// Create a TCP connection from an incoming SYN (for listening sockets).
    /// </summary>
    public TcpConnection(uint localIP, ushort localPort, uint remoteIP, ushort remotePort,
                         uint remoteSeq, ushort remoteWindow)
    {
        _local = new TcpEndpoint(localIP, localPort);
        _remote = new TcpEndpoint(remoteIP, remotePort);
        _state = TcpState.SynReceived;

        // Initialize our sequence number
        _iss = GenerateISN();
        _sendNext = _iss;
        _sendUnack = _iss;

        // Record remote's initial sequence
        _irs = remoteSeq;
        _recvNext = remoteSeq + 1; // SYN consumes one sequence number

        // Window sizes
        _recvWindow = (ushort)RecvBufferSize;
        _sendWindow = remoteWindow;

        // Allocate buffers
        _recvBuffer = new byte[RecvBufferSize];
        _sendBuffer = new byte[SendBufferSize];
    }

    /// <summary>
    /// Generate an initial sequence number.
    /// In a real implementation, this should be based on a secure random source.
    /// </summary>
    private static uint GenerateISN()
    {
        // Simple ISN based on timer - NOT secure, just for testing
        // Real implementations should use RFC 6528 algorithm
        return (uint)(Timer.GetTickCount() & 0xFFFFFFFF);
    }

    /// <summary>
    /// Initiate an active open (client connect).
    /// Returns the SYN packet to send.
    /// </summary>
    public int InitiateConnect(byte* buffer)
    {
        if (_state != TcpState.Closed)
            return 0;

        _state = TcpState.SynSent;

        // Build SYN packet
        int len = TCP.BuildSyn(buffer, _local.Port, _remote.Port, _sendNext,
                               _recvWindow, _local.IP, _remote.IP);

        // SYN consumes one sequence number
        _sendNext++;

        return len;
    }

    /// <summary>
    /// Build a SYN-ACK response (for server-side connections in SYN_RECEIVED state).
    /// Returns the packet length.
    /// </summary>
    public int BuildSynAck(byte* buffer)
    {
        if (_state != TcpState.SynReceived)
            return 0;

        int len = TCP.BuildSynAck(buffer, _local.Port, _remote.Port,
                                   _sendNext, _recvNext, _recvWindow,
                                   _local.IP, _remote.IP);

        // SYN consumes one sequence number
        _sendNext++;

        return len;
    }

    /// <summary>
    /// Process a received TCP packet.
    /// Returns the response packet length (0 if no response needed).
    /// </summary>
    public int ProcessPacket(TcpPacket* packet, byte* responseBuffer)
    {
        int responseLen = 0;

        switch (_state)
        {
            case TcpState.Closed:
                // Send RST for any packet to closed connection
                if (!packet->IsRst)
                {
                    responseLen = TCP.BuildRst(responseBuffer, _local.Port, _remote.Port,
                                               packet->AckNum, _local.IP, _remote.IP);
                }
                break;

            case TcpState.Listen:
                responseLen = ProcessListen(packet, responseBuffer);
                break;

            case TcpState.SynSent:
                responseLen = ProcessSynSent(packet, responseBuffer);
                break;

            case TcpState.SynReceived:
                responseLen = ProcessSynReceived(packet, responseBuffer);
                break;

            case TcpState.Established:
                responseLen = ProcessEstablished(packet, responseBuffer);
                break;

            case TcpState.FinWait1:
                responseLen = ProcessFinWait1(packet, responseBuffer);
                break;

            case TcpState.FinWait2:
                responseLen = ProcessFinWait2(packet, responseBuffer);
                break;

            case TcpState.CloseWait:
                responseLen = ProcessCloseWait(packet, responseBuffer);
                break;

            case TcpState.Closing:
                responseLen = ProcessClosing(packet, responseBuffer);
                break;

            case TcpState.LastAck:
                responseLen = ProcessLastAck(packet, responseBuffer);
                break;

            case TcpState.TimeWait:
                // In TIME_WAIT, just ACK any valid segment
                if (IsValidSequence(packet->SeqNum))
                {
                    responseLen = TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                               _sendNext, _recvNext, _recvWindow,
                                               _local.IP, _remote.IP);
                }
                break;
        }

        return responseLen;
    }

    private int ProcessListen(TcpPacket* packet, byte* responseBuffer)
    {
        // Only accept SYN in Listen state
        if (packet->IsSyn && !packet->IsAck)
        {
            _irs = packet->SeqNum;
            _recvNext = packet->SeqNum + 1;
            _sendWindow = packet->Window;
            _state = TcpState.SynReceived;

            // Send SYN-ACK
            int len = TCP.BuildSynAck(responseBuffer, _local.Port, _remote.Port,
                                       _sendNext, _recvNext, _recvWindow,
                                       _local.IP, _remote.IP);
            _sendNext++; // SYN consumes one sequence
            return len;
        }

        return 0;
    }

    private int ProcessSynSent(TcpPacket* packet, byte* responseBuffer)
    {
        // Expecting SYN-ACK
        if (packet->IsSyn && packet->IsAck)
        {
            // Verify ACK acknowledges our SYN
            if (packet->AckNum != _sendNext)
            {
                // Invalid ACK, send RST
                return TCP.BuildRst(responseBuffer, _local.Port, _remote.Port,
                                    packet->AckNum, _local.IP, _remote.IP);
            }

            _irs = packet->SeqNum;
            _recvNext = packet->SeqNum + 1;
            _sendUnack = packet->AckNum;
            _sendWindow = packet->Window;
            _state = TcpState.Established;

            Debug.WriteLine("[TCP] Connection established");

            // Send ACK
            return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                _local.IP, _remote.IP);
        }
        else if (packet->IsSyn && !packet->IsAck)
        {
            // Simultaneous open - rare case
            _irs = packet->SeqNum;
            _recvNext = packet->SeqNum + 1;
            _state = TcpState.SynReceived;

            return TCP.BuildSynAck(responseBuffer, _local.Port, _remote.Port,
                                    _sendNext - 1, _recvNext, _recvWindow,
                                    _local.IP, _remote.IP);
        }

        return 0;
    }

    private int ProcessSynReceived(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsRst)
        {
            _state = TcpState.Listen; // Or Closed if not a server
            return 0;
        }

        if (packet->IsAck)
        {
            if (packet->AckNum == _sendNext)
            {
                _sendUnack = packet->AckNum;
                _state = TcpState.Established;
                Debug.WriteLine("[TCP] Connection established (server)");

                // Notify listener that connection is established
                if (_listener != null)
                {
                    _listener.ConnectionEstablished(this);
                }
            }
        }

        return 0;
    }

    private int ProcessEstablished(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsRst)
        {
            _state = TcpState.Closed;
            return 0;
        }

        // Process ACK
        if (packet->IsAck)
        {
            // Update send window
            if (IsValidAck(packet->AckNum))
            {
                _sendUnack = packet->AckNum;
                _sendWindow = packet->Window;
            }
        }

        // Process incoming data
        if (packet->PayloadLength > 0)
        {
            if (packet->SeqNum == _recvNext)
            {
                // In-order data - copy to receive buffer
                int copied = CopyToRecvBuffer(packet->Payload, packet->PayloadLength);
                _recvNext += (uint)copied;

                // Send ACK
                return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                    _sendNext, _recvNext, _recvWindow,
                                    _local.IP, _remote.IP);
            }
            else
            {
                // Out of order - send duplicate ACK
                return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                    _sendNext, _recvNext, _recvWindow,
                                    _local.IP, _remote.IP);
            }
        }

        // Process FIN
        if (packet->IsFin)
        {
            _recvNext++; // FIN consumes one sequence
            _finReceived = true;
            _state = TcpState.CloseWait;

            return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                _local.IP, _remote.IP);
        }

        return 0;
    }

    private int ProcessFinWait1(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsAck && packet->AckNum == _sendNext)
        {
            _sendUnack = packet->AckNum;

            if (packet->IsFin)
            {
                _recvNext++;
                _state = TcpState.TimeWait;
                return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                    _sendNext, _recvNext, _recvWindow,
                                    _local.IP, _remote.IP);
            }
            else
            {
                _state = TcpState.FinWait2;
            }
        }
        else if (packet->IsFin)
        {
            _recvNext++;
            _state = TcpState.Closing;
            return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                _local.IP, _remote.IP);
        }

        return 0;
    }

    private int ProcessFinWait2(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsFin)
        {
            _recvNext++;
            _state = TcpState.TimeWait;
            return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                _local.IP, _remote.IP);
        }

        // Can still receive data
        if (packet->PayloadLength > 0 && packet->SeqNum == _recvNext)
        {
            int copied = CopyToRecvBuffer(packet->Payload, packet->PayloadLength);
            _recvNext += (uint)copied;
            return TCP.BuildAck(responseBuffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                _local.IP, _remote.IP);
        }

        return 0;
    }

    private int ProcessCloseWait(TcpPacket* packet, byte* responseBuffer)
    {
        // Waiting for application to close
        // Just ACK any data
        if (packet->IsAck)
        {
            _sendUnack = packet->AckNum;
        }
        return 0;
    }

    private int ProcessClosing(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsAck && packet->AckNum == _sendNext)
        {
            _state = TcpState.TimeWait;
        }
        return 0;
    }

    private int ProcessLastAck(TcpPacket* packet, byte* responseBuffer)
    {
        if (packet->IsAck && packet->AckNum == _sendNext)
        {
            _state = TcpState.Closed;
        }
        return 0;
    }

    /// <summary>
    /// Initiate connection close.
    /// Returns the FIN packet to send.
    /// </summary>
    public int InitiateClose(byte* buffer)
    {
        if (_state == TcpState.Established)
        {
            _state = TcpState.FinWait1;
            int len = TCP.BuildFinAck(buffer, _local.Port, _remote.Port,
                                       _sendNext, _recvNext, _recvWindow,
                                       _local.IP, _remote.IP);
            _sendNext++; // FIN consumes one sequence
            _finSent = true;
            return len;
        }
        else if (_state == TcpState.CloseWait)
        {
            _state = TcpState.LastAck;
            int len = TCP.BuildFinAck(buffer, _local.Port, _remote.Port,
                                       _sendNext, _recvNext, _recvWindow,
                                       _local.IP, _remote.IP);
            _sendNext++;
            _finSent = true;
            return len;
        }

        return 0;
    }

    /// <summary>
    /// Build a data packet to send.
    /// Returns packet length, or 0 if no data to send.
    /// </summary>
    public int BuildDataPacket(byte* buffer, byte* data, int dataLength)
    {
        if (_state != TcpState.Established || dataLength <= 0)
            return 0;

        // Limit to send window
        int maxSend = (int)(_sendUnack + _sendWindow - _sendNext);
        if (maxSend <= 0)
            return 0;

        int sendLen = dataLength < maxSend ? dataLength : maxSend;

        int len = TCP.BuildData(buffer, _local.Port, _remote.Port,
                                _sendNext, _recvNext, _recvWindow,
                                data, sendLen, _local.IP, _remote.IP);

        if (len > 0)
        {
            _sendNext += (uint)sendLen;
        }

        return len;
    }

    /// <summary>
    /// Read data from receive buffer.
    /// </summary>
    public int Read(byte* buffer, int maxLength)
    {
        if (_recvCount == 0)
            return 0;

        int toRead = _recvCount < maxLength ? _recvCount : maxLength;
        int read = 0;

        while (read < toRead)
        {
            buffer[read] = _recvBuffer[_recvHead];
            _recvHead = (_recvHead + 1) % RecvBufferSize;
            read++;
        }

        _recvCount -= read;
        return read;
    }

    private int CopyToRecvBuffer(byte* data, int length)
    {
        int space = RecvBufferSize - _recvCount;
        int toCopy = length < space ? length : space;

        for (int i = 0; i < toCopy; i++)
        {
            _recvBuffer[_recvTail] = data[i];
            _recvTail = (_recvTail + 1) % RecvBufferSize;
        }

        _recvCount += toCopy;
        return toCopy;
    }

    private bool IsValidSequence(uint seq)
    {
        // Simplified check - just verify it's in expected range
        return true; // TODO: Proper sequence number validation
    }

    private bool IsValidAck(uint ack)
    {
        // ACK should be for data we've sent but not yet acknowledged
        // SND.UNA < ACK <= SND.NXT
        return ack > _sendUnack && ack <= _sendNext;
    }

    /// <summary>
    /// Check if this connection matches the given endpoints.
    /// </summary>
    public bool Matches(uint remoteIP, ushort remotePort, ushort localPort)
    {
        return _remote.IP == remoteIP &&
               _remote.Port == remotePort &&
               _local.Port == localPort;
    }

    /// <summary>
    /// Set to Listen state (for server sockets).
    /// </summary>
    public void Listen()
    {
        if (_state == TcpState.Closed)
        {
            _state = TcpState.Listen;
        }
    }
}
