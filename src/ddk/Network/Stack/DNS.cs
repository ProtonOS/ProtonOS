// ProtonOS DDK - DNS Protocol Implementation
// DNS query/response building and parsing for hostname resolution.

using System;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Network.Stack;

/// <summary>
/// DNS protocol constants and packet building/parsing.
/// DNS uses UDP port 53.
/// </summary>
public static unsafe class DNS
{
    /// <summary>DNS server port.</summary>
    public const int Port = 53;

    /// <summary>DNS header size in bytes.</summary>
    public const int HeaderSize = 12;

    /// <summary>Maximum DNS message size for UDP.</summary>
    public const int MaxMessageSize = 512;

    /// <summary>Query type: A record (IPv4 address).</summary>
    public const ushort TypeA = 1;

    /// <summary>Query type: AAAA record (IPv6 address).</summary>
    public const ushort TypeAAAA = 28;

    /// <summary>Query type: CNAME (canonical name).</summary>
    public const ushort TypeCNAME = 5;

    /// <summary>Query class: Internet.</summary>
    public const ushort ClassIN = 1;

    /// <summary>Query flags: Standard query with recursion desired.</summary>
    public const ushort FlagsQuery = 0x0100;  // RD=1

    /// <summary>Response flag bit.</summary>
    public const ushort FlagResponse = 0x8000;

    /// <summary>Response code mask.</summary>
    public const ushort RCodeMask = 0x000F;

    /// <summary>
    /// Build a DNS query packet for an A record lookup.
    /// </summary>
    /// <param name="buffer">Buffer to write query to (must be at least MaxMessageSize bytes).</param>
    /// <param name="transactionId">Transaction ID to match response.</param>
    /// <param name="hostname">Hostname to resolve (e.g., "example.com").</param>
    /// <param name="hostnameLen">Length of hostname.</param>
    /// <returns>Total query length in bytes, or 0 on error.</returns>
    public static int BuildQuery(byte* buffer, ushort transactionId,
                                  byte* hostname, int hostnameLen)
    {
        if (buffer == null || hostname == null || hostnameLen <= 0 || hostnameLen > 253)
            return 0;

        int offset = 0;

        // DNS Header (12 bytes)
        // Transaction ID (2 bytes)
        buffer[offset++] = (byte)(transactionId >> 8);
        buffer[offset++] = (byte)(transactionId & 0xFF);

        // Flags (2 bytes) - Standard query, recursion desired
        buffer[offset++] = (byte)(FlagsQuery >> 8);
        buffer[offset++] = (byte)(FlagsQuery & 0xFF);

        // QDCOUNT - 1 question (2 bytes)
        buffer[offset++] = 0;
        buffer[offset++] = 1;

        // ANCOUNT - 0 answers (2 bytes)
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // NSCOUNT - 0 authority (2 bytes)
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // ARCOUNT - 0 additional (2 bytes)
        buffer[offset++] = 0;
        buffer[offset++] = 0;

        // Question Section
        // QNAME - domain name in label format
        int nameLen = EncodeName(buffer + offset, hostname, hostnameLen);
        if (nameLen <= 0)
            return 0;
        offset += nameLen;

        // QTYPE - A record (2 bytes)
        buffer[offset++] = (byte)(TypeA >> 8);
        buffer[offset++] = (byte)(TypeA & 0xFF);

        // QCLASS - Internet (2 bytes)
        buffer[offset++] = (byte)(ClassIN >> 8);
        buffer[offset++] = (byte)(ClassIN & 0xFF);

        return offset;
    }

    /// <summary>
    /// Parse a DNS response and extract the first A record IP address.
    /// </summary>
    /// <param name="data">Response data.</param>
    /// <param name="length">Response length.</param>
    /// <param name="expectedId">Expected transaction ID.</param>
    /// <param name="ipAddress">Output: Resolved IPv4 address (host byte order).</param>
    /// <returns>True if successful, false on error or no A record found.</returns>
    public static bool ParseResponse(byte* data, int length, ushort expectedId, out uint ipAddress)
    {
        ipAddress = 0;

        if (data == null || length < HeaderSize)
            return false;

        // Parse header
        ushort transactionId = (ushort)((data[0] << 8) | data[1]);
        ushort flags = (ushort)((data[2] << 8) | data[3]);
        ushort qdCount = (ushort)((data[4] << 8) | data[5]);
        ushort anCount = (ushort)((data[6] << 8) | data[7]);
        // nsCount and arCount not needed for basic resolution

        // Verify transaction ID
        if (transactionId != expectedId)
        {
            Debug.Write("[DNS] Transaction ID mismatch: got ");
            Debug.WriteHex(transactionId);
            Debug.Write(" expected ");
            Debug.WriteHex(expectedId);
            Debug.WriteLine();
            return false;
        }

        // Check response flag
        if ((flags & FlagResponse) == 0)
        {
            Debug.WriteLine("[DNS] Not a response packet");
            return false;
        }

        // Check response code
        int rcode = flags & RCodeMask;
        if (rcode != 0)
        {
            Debug.Write("[DNS] Error response code: ");
            Debug.WriteDecimal((uint)rcode);
            Debug.WriteLine();
            return false;
        }

        // No answers?
        if (anCount == 0)
        {
            Debug.WriteLine("[DNS] No answers in response");
            return false;
        }

        int offset = HeaderSize;

        // Skip question section
        for (int i = 0; i < qdCount; i++)
        {
            // Skip QNAME
            offset = SkipName(data, length, offset);
            if (offset < 0)
                return false;

            // Skip QTYPE (2) and QCLASS (2)
            offset += 4;
            if (offset > length)
                return false;
        }

        // Parse answer section
        for (int i = 0; i < anCount; i++)
        {
            // Skip NAME (may be compressed)
            offset = SkipName(data, length, offset);
            if (offset < 0 || offset + 10 > length)
                return false;

            // Read TYPE, CLASS, TTL, RDLENGTH
            ushort type = (ushort)((data[offset] << 8) | data[offset + 1]);
            ushort cls = (ushort)((data[offset + 2] << 8) | data[offset + 3]);
            // TTL is 4 bytes at offset+4, skip it
            ushort rdLength = (ushort)((data[offset + 8] << 8) | data[offset + 9]);
            offset += 10;

            if (offset + rdLength > length)
                return false;

            // Check for A record (IPv4)
            if (type == TypeA && cls == ClassIN && rdLength == 4)
            {
                // Extract IPv4 address (network byte order -> host byte order)
                ipAddress = ((uint)data[offset] << 24) |
                            ((uint)data[offset + 1] << 16) |
                            ((uint)data[offset + 2] << 8) |
                            (uint)data[offset + 3];
                return true;
            }

            // Skip RDATA for other record types
            offset += rdLength;
        }

        Debug.WriteLine("[DNS] No A record found in response");
        return false;
    }

    /// <summary>
    /// Encode a hostname into DNS label format.
    /// E.g., "example.com" -> "\x07example\x03com\x00"
    /// </summary>
    /// <param name="dest">Destination buffer.</param>
    /// <param name="hostname">Hostname string.</param>
    /// <param name="hostnameLen">Hostname length.</param>
    /// <returns>Number of bytes written, or 0 on error.</returns>
    public static int EncodeName(byte* dest, byte* hostname, int hostnameLen)
    {
        if (dest == null || hostname == null || hostnameLen <= 0)
            return 0;

        int destOffset = 0;
        int labelStart = 0;

        for (int i = 0; i <= hostnameLen; i++)
        {
            byte c = (i < hostnameLen) ? hostname[i] : (byte)0;

            if (c == '.' || c == 0)
            {
                int labelLen = i - labelStart;
                if (labelLen == 0 && i < hostnameLen)
                {
                    // Empty label (double dot) - invalid
                    return 0;
                }
                if (labelLen > 63)
                {
                    // Label too long
                    return 0;
                }

                if (labelLen > 0)
                {
                    // Write label length
                    dest[destOffset++] = (byte)labelLen;

                    // Write label bytes
                    for (int j = 0; j < labelLen; j++)
                    {
                        dest[destOffset++] = hostname[labelStart + j];
                    }
                }

                labelStart = i + 1;
            }
        }

        // Write terminating zero
        dest[destOffset++] = 0;

        return destOffset;
    }

    /// <summary>
    /// Skip a DNS name (handles compression pointers).
    /// </summary>
    /// <param name="data">Packet data.</param>
    /// <param name="length">Packet length.</param>
    /// <param name="offset">Current offset.</param>
    /// <returns>New offset after the name, or -1 on error.</returns>
    private static int SkipName(byte* data, int length, int offset)
    {
        if (offset >= length)
            return -1;

        bool jumped = false;
        int maxIterations = 128;  // Prevent infinite loops

        while (maxIterations-- > 0)
        {
            if (offset >= length)
                return -1;

            byte labelLen = data[offset];

            if (labelLen == 0)
            {
                // End of name
                if (!jumped)
                    offset++;
                return offset;
            }

            if ((labelLen & 0xC0) == 0xC0)
            {
                // Compression pointer
                if (!jumped)
                    offset += 2;  // Pointer is 2 bytes
                jumped = true;

                // Follow the pointer (but don't update return offset)
                if (offset > length)
                    return -1;
                int pointer = ((labelLen & 0x3F) << 8) | data[offset - 1];
                if (pointer >= length)
                    return -1;

                // We don't actually need to follow for skipping, just return
                return jumped ? offset : offset;
            }

            if (labelLen > 63)
            {
                // Invalid label length
                return -1;
            }

            // Skip this label
            offset += 1 + labelLen;
        }

        // Too many iterations - likely a malformed packet
        return -1;
    }

    /// <summary>
    /// Decode a DNS name from packet data (for debugging).
    /// </summary>
    /// <param name="data">Packet data.</param>
    /// <param name="length">Packet length.</param>
    /// <param name="offset">Offset to name.</param>
    /// <param name="dest">Destination buffer for decoded name.</param>
    /// <param name="destLen">Destination buffer size.</param>
    /// <returns>Length of decoded name, or 0 on error.</returns>
    public static int DecodeName(byte* data, int length, int offset,
                                  byte* dest, int destLen)
    {
        if (data == null || dest == null || offset >= length || destLen < 2)
            return 0;

        int destOffset = 0;
        bool first = true;
        int maxIterations = 128;

        while (maxIterations-- > 0)
        {
            if (offset >= length)
                return 0;

            byte labelLen = data[offset];

            if (labelLen == 0)
            {
                // End of name
                if (destOffset == 0)
                    return 0;
                return destOffset;
            }

            if ((labelLen & 0xC0) == 0xC0)
            {
                // Compression pointer
                if (offset + 1 >= length)
                    return 0;
                int pointer = ((labelLen & 0x3F) << 8) | data[offset + 1];
                if (pointer >= length)
                    return 0;
                offset = pointer;
                continue;
            }

            if (labelLen > 63)
                return 0;

            // Add dot separator (except for first label)
            if (!first)
            {
                if (destOffset >= destLen)
                    return 0;
                dest[destOffset++] = (byte)'.';
            }
            first = false;

            // Copy label
            offset++;
            for (int i = 0; i < labelLen; i++)
            {
                if (offset >= length || destOffset >= destLen)
                    return 0;
                dest[destOffset++] = data[offset++];
            }
        }

        return 0;  // Too many iterations
    }
}
