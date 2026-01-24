// ProtonOS DDK - TCP Listener
// Manages TCP listening sockets for server functionality

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.DDK.Network.Sockets;

/// <summary>
/// TCP listener for accepting incoming connections.
/// </summary>
public unsafe class TcpListener
{
    private readonly NetworkStack _stack;
    private readonly uint _localAddress;
    private readonly ushort _port;
    private bool _isListening;

    // Accept queue - connections that completed handshake
    private const int AcceptQueueSize = 8;
    private TcpConnection?[] _acceptQueue;
    private int _acceptHead;
    private int _acceptTail;
    private int _acceptCount;

    // Pending connections in SYN_RECEIVED state
    private const int MaxPendingConnections = 16;
    private TcpConnection?[] _pendingConnections;
    private int _pendingCount;

    /// <summary>
    /// Get the local port this listener is bound to.
    /// </summary>
    public ushort Port => _port;

    /// <summary>
    /// Get the local IP address (host byte order).
    /// </summary>
    public uint LocalAddress => _localAddress;

    /// <summary>
    /// Check if the listener is currently listening.
    /// </summary>
    public bool IsListening => _isListening;

    /// <summary>
    /// Create a new TCP listener.
    /// </summary>
    /// <param name="stack">Network stack to use.</param>
    /// <param name="localAddress">Local IP address (host byte order), 0 for any.</param>
    /// <param name="port">Local port to listen on.</param>
    public TcpListener(NetworkStack stack, uint localAddress, ushort port)
    {
        _stack = stack;
        _localAddress = localAddress;
        _port = port;
        _isListening = false;

        _acceptQueue = new TcpConnection?[AcceptQueueSize];
        _acceptHead = 0;
        _acceptTail = 0;
        _acceptCount = 0;

        _pendingConnections = new TcpConnection?[MaxPendingConnections];
        _pendingCount = 0;
    }

    /// <summary>
    /// Start listening for incoming connections.
    /// </summary>
    /// <returns>True if started successfully.</returns>
    public bool Start()
    {
        if (_isListening)
            return true;

        if (!_stack.RegisterListener(this))
        {
            Debug.WriteLine("[TcpListener] Failed to register listener");
            return false;
        }

        _isListening = true;

        Debug.Write("[TcpListener] Listening on port ");
        Debug.WriteDecimal(_port);
        Debug.WriteLine("");

        return true;
    }

    /// <summary>
    /// Stop listening for incoming connections.
    /// </summary>
    public void Stop()
    {
        if (!_isListening)
            return;

        _stack.UnregisterListener(this);
        _isListening = false;

        // Clear pending connections
        for (int i = 0; i < MaxPendingConnections; i++)
        {
            _pendingConnections[i] = null;
        }
        _pendingCount = 0;

        // Clear accept queue
        for (int i = 0; i < AcceptQueueSize; i++)
        {
            _acceptQueue[i] = null;
        }
        _acceptHead = 0;
        _acceptTail = 0;
        _acceptCount = 0;

        Debug.Write("[TcpListener] Stopped listening on port ");
        Debug.WriteDecimal(_port);
        Debug.WriteLine("");
    }

    /// <summary>
    /// Check if there are pending connections ready to accept.
    /// </summary>
    /// <returns>True if connections are available.</returns>
    public bool Pending()
    {
        return _acceptCount > 0;
    }

    /// <summary>
    /// Accept an incoming connection.
    /// </summary>
    /// <returns>A TcpSocket for the accepted connection, or null if none available.</returns>
    public TcpSocket? AcceptSocket()
    {
        if (_acceptCount == 0)
            return null;

        var conn = _acceptQueue[_acceptHead];
        _acceptQueue[_acceptHead] = null;
        _acceptHead = (_acceptHead + 1) % AcceptQueueSize;
        _acceptCount--;

        if (conn == null)
            return null;

        Debug.Write("[TcpListener] Accepted connection from ");
        PrintIP(conn.RemoteEndpoint.IP);
        Debug.Write(":");
        Debug.WriteDecimal(conn.RemoteEndpoint.Port);
        Debug.WriteLine("");

        return new TcpSocket(conn);
    }

    /// <summary>
    /// Handle an incoming SYN packet.
    /// Called by NetworkStack when a SYN is received for this listener's port.
    /// </summary>
    internal void HandleIncomingSyn(uint srcIP, ushort srcPort, ushort dstPort,
                                     uint seqNum, ushort window, byte* tcpData, int tcpLength)
    {
        if (!_isListening)
            return;

        // Check if we already have a pending connection from this source
        for (int i = 0; i < MaxPendingConnections; i++)
        {
            var pending = _pendingConnections[i];
            if (pending != null &&
                pending.RemoteEndpoint.IP == srcIP &&
                pending.RemoteEndpoint.Port == srcPort)
            {
                // Already have this connection pending
                return;
            }
        }

        // Check if we have room for more pending connections
        if (_pendingCount >= MaxPendingConnections)
        {
            Debug.WriteLine("[TcpListener] Too many pending connections, dropping SYN");
            return;
        }

        // Get local IP from stack config
        uint localIP = _stack.Config.IPAddress;
        if (_localAddress != 0)
            localIP = _localAddress;

        // Create new connection in SYN_RECEIVED state
        var conn = new TcpConnection(localIP, dstPort, srcIP, srcPort, seqNum, window);
        conn.Listener = this;

        // Find empty slot
        for (int i = 0; i < MaxPendingConnections; i++)
        {
            if (_pendingConnections[i] == null)
            {
                _pendingConnections[i] = conn;
                _pendingCount++;
                break;
            }
        }

        // Register connection with stack so it receives further packets
        _stack.AddListenerConnection(conn);

        // Build SYN-ACK response
        byte* synAckBuffer = stackalloc byte[TcpHeader.MaxSize];
        int synAckLen = conn.BuildSynAck(synAckBuffer);

        if (synAckLen > 0)
        {
            // Send SYN-ACK through the stack
            int frameLen = _stack.BuildIPv4Frame(srcIP, TCP.ProtocolNumber, synAckBuffer, synAckLen);
            if (frameLen > 0)
            {
                _stack.QueuePendingTx(frameLen);
            }
        }

        Debug.Write("[TcpListener] SYN received from ");
        PrintIP(srcIP);
        Debug.Write(":");
        Debug.WriteDecimal(srcPort);
        Debug.WriteLine(", sent SYN-ACK");
    }

    /// <summary>
    /// Called when a pending connection has been established (received final ACK).
    /// </summary>
    internal void ConnectionEstablished(TcpConnection conn)
    {
        // Remove from pending
        for (int i = 0; i < MaxPendingConnections; i++)
        {
            if (_pendingConnections[i] == conn)
            {
                _pendingConnections[i] = null;
                _pendingCount--;
                break;
            }
        }

        // Add to accept queue
        if (_acceptCount < AcceptQueueSize)
        {
            _acceptQueue[_acceptTail] = conn;
            _acceptTail = (_acceptTail + 1) % AcceptQueueSize;
            _acceptCount++;

            Debug.Write("[TcpListener] Connection established, queued for accept from ");
            PrintIP(conn.RemoteEndpoint.IP);
            Debug.Write(":");
            Debug.WriteDecimal(conn.RemoteEndpoint.Port);
            Debug.WriteLine("");
        }
        else
        {
            Debug.WriteLine("[TcpListener] Accept queue full, dropping established connection");
        }
    }

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
