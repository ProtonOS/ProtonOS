// ProtonOS DDK - TCP Socket
// High-level wrapper for TCP connections

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.DDK.Network.Sockets;

/// <summary>
/// TCP socket for connection-oriented communication.
/// </summary>
public unsafe class TcpSocket
{
    private readonly NetworkStack? _stack;
    private TcpConnection? _connection;
    private int _connectionIndex;

    /// <summary>
    /// Check if the socket is connected.
    /// </summary>
    public bool Connected => _connection != null && _connection.State == TcpState.Established;

    /// <summary>
    /// Get the remote IP address (host byte order).
    /// </summary>
    public uint RemoteAddress => _connection?.RemoteEndpoint.IP ?? 0;

    /// <summary>
    /// Get the remote port.
    /// </summary>
    public ushort RemotePort => _connection?.RemoteEndpoint.Port ?? 0;

    /// <summary>
    /// Get the local IP address (host byte order).
    /// </summary>
    public uint LocalAddress => _connection?.LocalEndpoint.IP ?? 0;

    /// <summary>
    /// Get the local port.
    /// </summary>
    public ushort LocalPort => _connection?.LocalEndpoint.Port ?? 0;

    /// <summary>
    /// Get the number of bytes available to read.
    /// </summary>
    public int Available => _connection?.Available ?? 0;

    /// <summary>
    /// Get the underlying connection state.
    /// </summary>
    public TcpState State => _connection?.State ?? TcpState.Closed;

    /// <summary>
    /// Create a socket for outgoing connections.
    /// </summary>
    /// <param name="stack">Network stack to use.</param>
    public TcpSocket(NetworkStack stack)
    {
        _stack = stack;
        _connection = null;
        _connectionIndex = -1;
    }

    /// <summary>
    /// Create a socket from an accepted connection (used by TcpListener).
    /// </summary>
    internal TcpSocket(TcpConnection connection)
    {
        _stack = null;  // Connection already managed by stack
        _connection = connection;
        _connectionIndex = -1;  // Not tracked by index for accepted connections
    }

    /// <summary>
    /// Connect to a remote endpoint.
    /// </summary>
    /// <param name="address">Remote IP address (host byte order).</param>
    /// <param name="port">Remote port.</param>
    /// <returns>True if connection initiated (check Connected for completion).</returns>
    public bool Connect(uint address, ushort port)
    {
        if (_stack == null)
            return false;

        if (_connection != null)
            return false;  // Already connected/connecting

        _connectionIndex = _stack.TcpConnect(address, port);
        if (_connectionIndex < 0)
        {
            if (_connectionIndex == -2)
            {
                Debug.WriteLine("[TcpSocket] Connect failed - need ARP resolution");
            }
            else
            {
                Debug.WriteLine("[TcpSocket] Connect failed - connection table full");
            }
            return false;
        }

        _connection = _stack.GetTcpConnection(_connectionIndex);
        return true;
    }

    /// <summary>
    /// Send data on the socket.
    /// </summary>
    /// <param name="data">Pointer to data to send.</param>
    /// <param name="length">Number of bytes to send.</param>
    /// <returns>Number of bytes sent, or 0 on error.</returns>
    public int Send(byte* data, int length)
    {
        if (_connection == null || !Connected)
            return 0;

        if (_stack != null && _connectionIndex >= 0)
        {
            return _stack.TcpSend(_connectionIndex, data, length);
        }

        // For accepted connections, we need to send through the connection directly
        // This requires the stack to expose a method for sending
        return 0;
    }

    /// <summary>
    /// Send data from a span.
    /// </summary>
    public int Send(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return 0;

        fixed (byte* ptr = data)
        {
            return Send(ptr, data.Length);
        }
    }

    /// <summary>
    /// Receive data from the socket.
    /// </summary>
    /// <param name="buffer">Buffer to receive data into.</param>
    /// <param name="maxLength">Maximum bytes to read.</param>
    /// <returns>Number of bytes received.</returns>
    public int Receive(byte* buffer, int maxLength)
    {
        if (_connection == null)
            return 0;

        if (_stack != null && _connectionIndex >= 0)
        {
            return _stack.TcpReceive(_connectionIndex, buffer, maxLength);
        }

        // For accepted connections, read directly from connection
        return _connection.Read(buffer, maxLength);
    }

    /// <summary>
    /// Receive data into a span.
    /// </summary>
    public int Receive(Span<byte> buffer)
    {
        if (buffer.Length == 0)
            return 0;

        fixed (byte* ptr = buffer)
        {
            return Receive(ptr, buffer.Length);
        }
    }

    /// <summary>
    /// Close the socket.
    /// </summary>
    public void Close()
    {
        if (_connection == null)
            return;

        if (_stack != null && _connectionIndex >= 0)
        {
            _stack.TcpClose(_connectionIndex);
        }

        _connection = null;
        _connectionIndex = -1;
    }

    /// <summary>
    /// Poll for connection state changes (call periodically after Connect).
    /// </summary>
    /// <returns>True if connected.</returns>
    public bool Poll()
    {
        if (_connection == null)
            return false;

        return _connection.State == TcpState.Established;
    }
}
