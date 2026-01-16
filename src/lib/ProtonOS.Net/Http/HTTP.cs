// ProtonOS.Net - HTTP Protocol (Application Layer)
// Handles HTTP/1.1 request building and response parsing

using System;

namespace ProtonOS.Net.Http;

/// <summary>
/// HTTP methods.
/// </summary>
public static class HttpMethod
{
    public const int GET = 0;
    public const int POST = 1;
    public const int PUT = 2;
    public const int DELETE = 3;
    public const int HEAD = 4;
}

/// <summary>
/// HTTP status codes.
/// </summary>
public static class HttpStatus
{
    // 1xx Informational
    public const int Continue = 100;
    public const int SwitchingProtocols = 101;

    // 2xx Success
    public const int OK = 200;
    public const int Created = 201;
    public const int Accepted = 202;
    public const int NoContent = 204;

    // 3xx Redirection
    public const int MovedPermanently = 301;
    public const int Found = 302;
    public const int NotModified = 304;
    public const int TemporaryRedirect = 307;

    // 4xx Client Error
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int MethodNotAllowed = 405;
    public const int RequestTimeout = 408;

    // 5xx Server Error
    public const int InternalServerError = 500;
    public const int NotImplemented = 501;
    public const int BadGateway = 502;
    public const int ServiceUnavailable = 503;
    public const int GatewayTimeout = 504;
}

/// <summary>
/// Parsed HTTP response.
/// </summary>
public unsafe struct HttpResponse
{
    /// <summary>HTTP version (10 for 1.0, 11 for 1.1).</summary>
    public int Version;

    /// <summary>HTTP status code.</summary>
    public int StatusCode;

    /// <summary>Content-Length header value (-1 if not present).</summary>
    public int ContentLength;

    /// <summary>Whether Connection: close was specified.</summary>
    public bool ConnectionClose;

    /// <summary>Whether Transfer-Encoding: chunked was specified.</summary>
    public bool Chunked;

    /// <summary>Offset to body in the response buffer.</summary>
    public int BodyOffset;

    /// <summary>Length of body data received so far.</summary>
    public int BodyLength;

    /// <summary>Whether headers have been fully received.</summary>
    public bool HeadersComplete;

    /// <summary>Whether the full response has been received.</summary>
    public bool Complete;
}

/// <summary>
/// HTTP protocol handler.
/// </summary>
public static unsafe class HTTP
{
    /// <summary>Default HTTP port.</summary>
    public const int DefaultPort = 80;

    /// <summary>HTTP version string length.</summary>
    private const int HttpVersion11Length = 8;

    /// <summary>
    /// Build an HTTP GET request.
    /// </summary>
    /// <param name="buffer">Buffer to write request to.</param>
    /// <param name="bufferSize">Size of buffer.</param>
    /// <param name="host">Host header value.</param>
    /// <param name="path">Request path (e.g., "/index.html").</param>
    /// <returns>Length of request, or 0 on error.</returns>
    public static int BuildGetRequest(byte* buffer, int bufferSize, string host, string path)
    {
        if (buffer == null || bufferSize < 64)
            return 0;

        int pos = 0;

        // GET
        buffer[pos++] = (byte)'G';
        buffer[pos++] = (byte)'E';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)' ';

        // Path
        if (path == null || path.Length == 0)
        {
            buffer[pos++] = (byte)'/';
        }
        else
        {
            for (int i = 0; i < path.Length && pos < bufferSize - 50; i++)
                buffer[pos++] = (byte)path[i];
        }

        buffer[pos++] = (byte)' ';

        // HTTP/1.1
        if (pos + HttpVersion11Length > bufferSize - 40) return 0;
        buffer[pos++] = (byte)'H';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)'P';
        buffer[pos++] = (byte)'/';
        buffer[pos++] = (byte)'1';
        buffer[pos++] = (byte)'.';
        buffer[pos++] = (byte)'1';

        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        // Host header
        pos = AppendHeader(buffer, bufferSize, pos, "Host", host);
        if (pos == 0) return 0;

        // Connection header
        pos = AppendHeader(buffer, bufferSize, pos, "Connection", "close");
        if (pos == 0) return 0;

        // User-Agent header
        pos = AppendHeader(buffer, bufferSize, pos, "User-Agent", "ProtonOS/1.0");
        if (pos == 0) return 0;

        // End of headers
        if (pos + 2 > bufferSize) return 0;
        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        return pos;
    }

    /// <summary>
    /// Build an HTTP POST request.
    /// </summary>
    /// <param name="buffer">Buffer to write request to.</param>
    /// <param name="bufferSize">Size of buffer.</param>
    /// <param name="host">Host header value.</param>
    /// <param name="path">Request path.</param>
    /// <param name="contentType">Content-Type header value.</param>
    /// <param name="body">Request body.</param>
    /// <param name="bodyLength">Length of body.</param>
    /// <returns>Length of request, or 0 on error.</returns>
    public static int BuildPostRequest(byte* buffer, int bufferSize, string host, string path,
                                        string contentType, byte* body, int bodyLength)
    {
        if (buffer == null || bufferSize < 64)
            return 0;

        int pos = 0;

        // POST
        buffer[pos++] = (byte)'P';
        buffer[pos++] = (byte)'O';
        buffer[pos++] = (byte)'S';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)' ';

        // Path
        if (path == null || path.Length == 0)
        {
            buffer[pos++] = (byte)'/';
        }
        else
        {
            for (int i = 0; i < path.Length && pos < bufferSize - 100; i++)
                buffer[pos++] = (byte)path[i];
        }

        buffer[pos++] = (byte)' ';

        // HTTP/1.1
        if (pos + HttpVersion11Length > bufferSize - 80) return 0;
        buffer[pos++] = (byte)'H';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)'T';
        buffer[pos++] = (byte)'P';
        buffer[pos++] = (byte)'/';
        buffer[pos++] = (byte)'1';
        buffer[pos++] = (byte)'.';
        buffer[pos++] = (byte)'1';

        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        // Host header
        pos = AppendHeader(buffer, bufferSize, pos, "Host", host);
        if (pos == 0) return 0;

        // Connection header
        pos = AppendHeader(buffer, bufferSize, pos, "Connection", "close");
        if (pos == 0) return 0;

        // Content-Type header
        if (contentType != null && contentType.Length > 0)
        {
            pos = AppendHeader(buffer, bufferSize, pos, "Content-Type", contentType);
            if (pos == 0) return 0;
        }

        // Content-Length header
        pos = AppendContentLength(buffer, bufferSize, pos, bodyLength);
        if (pos == 0) return 0;

        // User-Agent header
        pos = AppendHeader(buffer, bufferSize, pos, "User-Agent", "ProtonOS/1.0");
        if (pos == 0) return 0;

        // End of headers
        if (pos + 2 > bufferSize) return 0;
        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        // Body
        if (body != null && bodyLength > 0)
        {
            if (pos + bodyLength > bufferSize) return 0;
            for (int i = 0; i < bodyLength; i++)
                buffer[pos++] = body[i];
        }

        return pos;
    }

    /// <summary>
    /// Parse HTTP response headers.
    /// </summary>
    /// <param name="data">Response data.</param>
    /// <param name="length">Length of data.</param>
    /// <param name="response">Parsed response info.</param>
    /// <returns>True if headers were parsed (may be incomplete).</returns>
    public static bool ParseResponse(byte* data, int length, ref HttpResponse response)
    {
        if (data == null || length < 12) // Minimum: "HTTP/1.1 200"
            return false;

        // Check for HTTP/1.x
        if (data[0] != 'H' || data[1] != 'T' || data[2] != 'T' || data[3] != 'P' || data[4] != '/')
            return false;

        // Parse version
        if (data[5] == '1' && data[6] == '.' && data[7] == '0')
            response.Version = 10;
        else if (data[5] == '1' && data[6] == '.' && data[7] == '1')
            response.Version = 11;
        else
            return false;

        // Skip space
        if (data[8] != ' ')
            return false;

        // Parse status code
        int statusCode = 0;
        int pos = 9;
        while (pos < length && data[pos] >= '0' && data[pos] <= '9')
        {
            statusCode = statusCode * 10 + (data[pos] - '0');
            pos++;
        }
        response.StatusCode = statusCode;

        // Initialize defaults
        response.ContentLength = -1;
        response.ConnectionClose = false;
        response.Chunked = false;
        response.HeadersComplete = false;
        response.Complete = false;
        response.BodyOffset = 0;
        response.BodyLength = 0;

        // Find end of status line
        while (pos < length - 1 && !(data[pos] == '\r' && data[pos + 1] == '\n'))
            pos++;

        if (pos >= length - 1)
            return true; // Incomplete

        pos += 2; // Skip \r\n

        // Parse headers
        while (pos < length - 1)
        {
            // Check for end of headers
            if (data[pos] == '\r' && data[pos + 1] == '\n')
            {
                response.HeadersComplete = true;
                response.BodyOffset = pos + 2;
                response.BodyLength = length - (pos + 2);

                // Check if response is complete
                if (response.ContentLength >= 0)
                {
                    response.Complete = (response.BodyLength >= response.ContentLength);
                }
                else if (response.ConnectionClose)
                {
                    // Will be complete when connection closes
                    response.Complete = false;
                }

                return true;
            }

            // Find header name end
            int headerStart = pos;
            while (pos < length && data[pos] != ':' && data[pos] != '\r')
                pos++;

            if (pos >= length || data[pos] == '\r')
                return true; // Incomplete or malformed

            int headerNameLen = pos - headerStart;
            pos++; // Skip ':'

            // Skip whitespace
            while (pos < length && (data[pos] == ' ' || data[pos] == '\t'))
                pos++;

            // Find header value end
            int valueStart = pos;
            while (pos < length - 1 && !(data[pos] == '\r' && data[pos + 1] == '\n'))
                pos++;

            if (pos >= length - 1)
                return true; // Incomplete

            int valueLen = pos - valueStart;

            // Check for known headers
            if (HeaderEquals(data + headerStart, headerNameLen, "Content-Length"))
            {
                response.ContentLength = ParseInt(data + valueStart, valueLen);
            }
            else if (HeaderEquals(data + headerStart, headerNameLen, "Connection"))
            {
                response.ConnectionClose = ValueContains(data + valueStart, valueLen, "close");
            }
            else if (HeaderEquals(data + headerStart, headerNameLen, "Transfer-Encoding"))
            {
                response.Chunked = ValueContains(data + valueStart, valueLen, "chunked");
            }

            pos += 2; // Skip \r\n
        }

        return true;
    }

    /// <summary>
    /// Check if more data is needed for the response.
    /// </summary>
    public static bool NeedsMoreData(ref HttpResponse response)
    {
        if (!response.HeadersComplete)
            return true;

        if (response.Complete)
            return false;

        if (response.ContentLength >= 0)
            return response.BodyLength < response.ContentLength;

        // For Connection: close or unknown length, we need more data until connection closes
        return true;
    }

    /// <summary>
    /// Update response with additional data.
    /// </summary>
    public static void UpdateResponse(byte* data, int totalLength, ref HttpResponse response)
    {
        if (!response.HeadersComplete)
        {
            // Re-parse headers
            ParseResponse(data, totalLength, ref response);
            return;
        }

        // Update body length
        response.BodyLength = totalLength - response.BodyOffset;

        // Check if complete
        if (response.ContentLength >= 0)
        {
            response.Complete = (response.BodyLength >= response.ContentLength);
        }
    }

    /// <summary>
    /// Append a header to the buffer.
    /// </summary>
    private static int AppendHeader(byte* buffer, int bufferSize, int pos, string name, string value)
    {
        if (value == null) value = "";

        int needed = name.Length + 2 + value.Length + 2; // name: value\r\n
        if (pos + needed > bufferSize)
            return 0;

        for (int i = 0; i < name.Length; i++)
            buffer[pos++] = (byte)name[i];

        buffer[pos++] = (byte)':';
        buffer[pos++] = (byte)' ';

        for (int i = 0; i < value.Length; i++)
            buffer[pos++] = (byte)value[i];

        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        return pos;
    }

    /// <summary>
    /// Append Content-Length header.
    /// </summary>
    private static int AppendContentLength(byte* buffer, int bufferSize, int pos, int length)
    {
        // "Content-Length: " = 16 chars, max int = 10 digits, "\r\n" = 2
        if (pos + 30 > bufferSize)
            return 0;

        byte* header = stackalloc byte[] {
            (byte)'C', (byte)'o', (byte)'n', (byte)'t', (byte)'e', (byte)'n', (byte)'t',
            (byte)'-', (byte)'L', (byte)'e', (byte)'n', (byte)'g', (byte)'t', (byte)'h',
            (byte)':', (byte)' '
        };

        for (int i = 0; i < 16; i++)
            buffer[pos++] = header[i];

        // Convert length to string
        if (length == 0)
        {
            buffer[pos++] = (byte)'0';
        }
        else
        {
            byte* digits = stackalloc byte[12];
            int numDigits = 0;
            int val = length;
            while (val > 0)
            {
                digits[numDigits++] = (byte)('0' + (val % 10));
                val /= 10;
            }
            for (int i = numDigits - 1; i >= 0; i--)
                buffer[pos++] = digits[i];
        }

        buffer[pos++] = (byte)'\r';
        buffer[pos++] = (byte)'\n';

        return pos;
    }

    /// <summary>
    /// Case-insensitive header name comparison.
    /// </summary>
    private static bool HeaderEquals(byte* header, int headerLen, string name)
    {
        if (headerLen != name.Length)
            return false;

        for (int i = 0; i < headerLen; i++)
        {
            byte h = header[i];
            byte n = (byte)name[i];

            // Convert to lowercase
            if (h >= 'A' && h <= 'Z') h = (byte)(h + 32);
            if (n >= 'A' && n <= 'Z') n = (byte)(n + 32);

            if (h != n)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Check if header value contains a token (case-insensitive).
    /// </summary>
    private static bool ValueContains(byte* value, int valueLen, string token)
    {
        int tokenLen = token.Length;
        if (valueLen < tokenLen)
            return false;

        for (int i = 0; i <= valueLen - tokenLen; i++)
        {
            bool match = true;
            for (int j = 0; j < tokenLen; j++)
            {
                byte v = value[i + j];
                byte t = (byte)token[j];

                // Convert to lowercase
                if (v >= 'A' && v <= 'Z') v = (byte)(v + 32);
                if (t >= 'A' && t <= 'Z') t = (byte)(t + 32);

                if (v != t)
                {
                    match = false;
                    break;
                }
            }
            if (match)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parse integer from bytes.
    /// </summary>
    private static int ParseInt(byte* data, int length)
    {
        int result = 0;
        for (int i = 0; i < length; i++)
        {
            if (data[i] >= '0' && data[i] <= '9')
                result = result * 10 + (data[i] - '0');
            else if (data[i] != ' ' && data[i] != '\t')
                break;
        }
        return result;
    }
}
