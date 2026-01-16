// ProtonOS.Net - HTTP Client
// Simple HTTP/1.1 client using TCP

using System;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Network.Stack;

namespace ProtonOS.Net.Http;

/// <summary>
/// HTTP client result.
/// </summary>
public unsafe struct HttpResult
{
    /// <summary>Whether the request succeeded.</summary>
    public bool Success;

    /// <summary>HTTP status code (if successful).</summary>
    public int StatusCode;

    /// <summary>Response body pointer (valid only during callback or until next request).</summary>
    public byte* Body;

    /// <summary>Response body length.</summary>
    public int BodyLength;

    /// <summary>Error message if not successful.</summary>
    public string? Error;
}

/// <summary>
/// Simple HTTP/1.1 client.
/// </summary>
public unsafe class HttpClient
{
    private NetworkStack _stack;
    private byte[] _requestBuffer;
    private byte[] _responseBuffer;
    private int _responseLength;
    private int _timeoutMs;

    /// <summary>
    /// Create a new HTTP client.
    /// </summary>
    /// <param name="stack">Network stack to use.</param>
    /// <param name="timeoutMs">Request timeout in milliseconds (default 5000).</param>
    public HttpClient(NetworkStack stack, int timeoutMs = 5000)
    {
        _stack = stack;
        _requestBuffer = new byte[2048];
        _responseBuffer = new byte[16384]; // 16KB response buffer
        _responseLength = 0;
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Perform an HTTP GET request.
    /// </summary>
    /// <param name="ip">Server IP address (host byte order).</param>
    /// <param name="port">Server port (default 80).</param>
    /// <param name="host">Host header value.</param>
    /// <param name="path">Request path.</param>
    /// <param name="transmitFrame">Callback to transmit frames.</param>
    /// <param name="receiveFrame">Callback to receive frames (returns frame length, 0 if none).</param>
    /// <returns>HTTP result.</returns>
    public HttpResult Get(uint ip, ushort port, string host, string path,
                          TransmitFrameDelegate transmitFrame,
                          ReceiveFrameDelegate receiveFrame)
    {
        HttpResult result = new HttpResult();
        result.Success = false;
        result.Body = null;
        result.BodyLength = 0;

        // Build request
        fixed (byte* reqBuf = _requestBuffer)
        {
            int reqLen = HTTP.BuildGetRequest(reqBuf, _requestBuffer.Length, host, path);
            if (reqLen == 0)
            {
                result.Error = "Failed to build request";
                return result;
            }

            // Perform request
            return DoRequest(ip, port, reqBuf, reqLen, transmitFrame, receiveFrame);
        }
    }

    /// <summary>
    /// Perform an HTTP POST request.
    /// </summary>
    /// <param name="ip">Server IP address (host byte order).</param>
    /// <param name="port">Server port (default 80).</param>
    /// <param name="host">Host header value.</param>
    /// <param name="path">Request path.</param>
    /// <param name="contentType">Content-Type header.</param>
    /// <param name="body">Request body.</param>
    /// <param name="bodyLength">Body length.</param>
    /// <param name="transmitFrame">Callback to transmit frames.</param>
    /// <param name="receiveFrame">Callback to receive frames.</param>
    /// <returns>HTTP result.</returns>
    public HttpResult Post(uint ip, ushort port, string host, string path,
                           string contentType, byte* body, int bodyLength,
                           TransmitFrameDelegate transmitFrame,
                           ReceiveFrameDelegate receiveFrame)
    {
        HttpResult result = new HttpResult();
        result.Success = false;
        result.Body = null;
        result.BodyLength = 0;

        // Build request
        fixed (byte* reqBuf = _requestBuffer)
        {
            int reqLen = HTTP.BuildPostRequest(reqBuf, _requestBuffer.Length, host, path,
                                                contentType, body, bodyLength);
            if (reqLen == 0)
            {
                result.Error = "Failed to build request";
                return result;
            }

            return DoRequest(ip, port, reqBuf, reqLen, transmitFrame, receiveFrame);
        }
    }

    /// <summary>
    /// Delegate for transmitting a frame.
    /// </summary>
    public delegate void TransmitFrameDelegate(byte* data, int length);

    /// <summary>
    /// Delegate for receiving a frame.
    /// </summary>
    /// <returns>Length of received frame, 0 if none available.</returns>
    public delegate int ReceiveFrameDelegate(byte* buffer, int maxLength);

    /// <summary>
    /// Perform the actual HTTP request.
    /// </summary>
    private HttpResult DoRequest(uint ip, ushort port, byte* request, int requestLen,
                                  TransmitFrameDelegate transmitFrame,
                                  ReceiveFrameDelegate receiveFrame)
    {
        HttpResult result = new HttpResult();
        result.Success = false;
        result.Body = null;
        result.BodyLength = 0;

        // Connect
        Debug.Write("[HTTP] Connecting to ");
        Debug.WriteHex((byte)(ip >> 24));
        Debug.Write(".");
        Debug.WriteHex((byte)(ip >> 16));
        Debug.Write(".");
        Debug.WriteHex((byte)(ip >> 8));
        Debug.Write(".");
        Debug.WriteHex((byte)ip);
        Debug.Write(":");
        Debug.WriteDecimal(port);
        Debug.WriteLine();

        int connIndex = _stack.TcpConnect(ip, port);
        if (connIndex < 0)
        {
            result.Error = "Failed to initiate connection";
            return result;
        }

        // Send SYN
        int pendingLen = _stack.GetPendingTxLen();
        if (pendingLen > 0)
        {
            byte* txBuf = _stack.GetTxBuffer();
            transmitFrame(txBuf, pendingLen);
        }

        // Wait for connection
        byte* rxBuf = stackalloc byte[1514];
        ulong startTick = Timer.GetUptimeMilliseconds();
        bool connected = false;

        while (!connected)
        {
            // Check timeout
            ulong elapsed = Timer.GetUptimeMilliseconds() - startTick;
            if (elapsed > (ulong)_timeoutMs)
            {
                _stack.TcpClose(connIndex);
                result.Error = "Connection timeout";
                return result;
            }

            // Receive frames
            int rxLen = receiveFrame(rxBuf, 1514);
            if (rxLen > 0)
            {
                _stack.ProcessFrame(rxBuf, rxLen);

                // Send any pending responses
                pendingLen = _stack.GetPendingTxLen();
                if (pendingLen > 0)
                {
                    byte* txBuf = _stack.GetTxBuffer();
                    transmitFrame(txBuf, pendingLen);
                }

                // Check if connected
                var conn = _stack.GetTcpConnection(connIndex);
                if (conn != null && conn.IsConnected)
                {
                    connected = true;
                    Debug.WriteLine("[HTTP] Connected!");
                }
            }
        }

        // Send HTTP request
        Debug.Write("[HTTP] Sending request (");
        Debug.WriteDecimal(requestLen);
        Debug.WriteLine(" bytes)");

        int sentBytes = _stack.TcpSend(connIndex, request, requestLen);
        if (sentBytes == 0)
        {
            _stack.TcpClose(connIndex);
            result.Error = "Failed to send request";
            return result;
        }

        // Send the data packet
        pendingLen = _stack.GetPendingTxLen();
        if (pendingLen > 0)
        {
            byte* txBuf = _stack.GetTxBuffer();
            transmitFrame(txBuf, pendingLen);
        }

        // Receive response
        _responseLength = 0;
        HttpResponse response = new HttpResponse();
        startTick = Timer.GetUptimeMilliseconds();
        int pollCount = 0;

        Debug.Write("[HTTP] Starting receive loop, startTick=");
        Debug.WriteDecimal((uint)startTick);
        Debug.Write(" timeout=");
        Debug.WriteDecimal(_timeoutMs);
        Debug.WriteLine();

        fixed (byte* respBuf = _responseBuffer)
        {
            while (true)
            {
                pollCount++;

                // Check timeout
                ulong elapsed = Timer.GetUptimeMilliseconds() - startTick;
                if (elapsed > (ulong)_timeoutMs)
                {
                    Debug.Write("[HTTP] Timeout after ");
                    Debug.WriteDecimal(pollCount);
                    Debug.Write(" polls, elapsed=");
                    Debug.WriteDecimal((uint)elapsed);
                    Debug.WriteLine();
                    _stack.TcpClose(connIndex);
                    result.Error = "Response timeout";
                    return result;
                }

                // Receive frames
                int rxLen = receiveFrame(rxBuf, 1514);
                if (rxLen > 0)
                {
                    Debug.Write("[HTTP] Received frame ");
                    Debug.WriteDecimal(rxLen);
                    Debug.WriteLine(" bytes");
                    _stack.ProcessFrame(rxBuf, rxLen);

                    // Send any pending ACKs
                    pendingLen = _stack.GetPendingTxLen();
                    if (pendingLen > 0)
                    {
                        byte* txBuf = _stack.GetTxBuffer();
                        transmitFrame(txBuf, pendingLen);
                    }

                    // Read data from connection
                    var conn = _stack.GetTcpConnection(connIndex);
                    if (conn != null)
                    {
                        int available = conn.Available;
                        if (available > 0 && _responseLength + available <= _responseBuffer.Length)
                        {
                            int read = conn.Read(respBuf + _responseLength, available);
                            _responseLength += read;

                            // Parse response
                            HTTP.UpdateResponse(respBuf, _responseLength, ref response);

                            Debug.Write("[HTTP] Received ");
                            Debug.WriteDecimal(_responseLength);
                            Debug.Write(" bytes, status=");
                            Debug.WriteDecimal(response.StatusCode);
                            Debug.WriteLine();

                            // Check if complete
                            if (response.Complete)
                            {
                                Debug.WriteLine("[HTTP] Response complete");
                                break;
                            }
                        }

                        // Check for connection close
                        if (conn.IsClosed || conn.State == TcpState.CloseWait)
                        {
                            Debug.WriteLine("[HTTP] Connection closed by server");
                            // Parse what we have
                            if (_responseLength > 0)
                            {
                                HTTP.ParseResponse(respBuf, _responseLength, ref response);
                                response.Complete = true;
                            }
                            break;
                        }
                    }
                }
            }

            // Close connection
            _stack.TcpClose(connIndex);

            // Process pending close
            for (int i = 0; i < 1000; i++)
            {
                int rxLen = receiveFrame(rxBuf, 1514);
                if (rxLen > 0)
                {
                    _stack.ProcessFrame(rxBuf, rxLen);
                    pendingLen = _stack.GetPendingTxLen();
                    if (pendingLen > 0)
                    {
                        byte* txBuf = _stack.GetTxBuffer();
                        transmitFrame(txBuf, pendingLen);
                    }
                }
            }

            // Return result
            if (response.HeadersComplete && response.StatusCode > 0)
            {
                result.Success = true;
                result.StatusCode = response.StatusCode;
                result.Body = respBuf + response.BodyOffset;
                result.BodyLength = response.BodyLength;
                result.Error = null;
            }
            else
            {
                result.Error = "Invalid response";
            }

            return result;
        }
    }

    /// <summary>
    /// Get the raw response buffer (for inspection).
    /// </summary>
    public byte[] ResponseBuffer => _responseBuffer;

    /// <summary>
    /// Get the response length.
    /// </summary>
    public int ResponseLength => _responseLength;
}
