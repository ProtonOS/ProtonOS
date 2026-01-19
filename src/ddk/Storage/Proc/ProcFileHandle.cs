// ProtonOS DDK - Proc Filesystem File Handle
// Serves dynamically-generated content with Position support.

using System;

namespace ProtonOS.DDK.Storage.Proc;

/// <summary>
/// File handle for proc filesystem entries.
/// Content is generated once when the file is opened and then served from a buffer.
/// </summary>
public unsafe class ProcFileHandle : IFileHandle
{
    private byte[] _buffer;
    private long _position;
    private bool _isOpen;

    /// <summary>
    /// Create a new proc file handle with content from the given generator.
    /// </summary>
    /// <param name="generator">The content generator for this proc entry.</param>
    public ProcFileHandle(IProcContentGenerator generator)
    {
        // Generate content immediately
        string content = generator.Generate();
        _buffer = StringToBytes(content);
        _position = 0;
        _isOpen = true;
    }

    /// <summary>
    /// Create a new proc file handle with static content.
    /// </summary>
    /// <param name="content">The static content to serve.</param>
    public ProcFileHandle(string content)
    {
        _buffer = StringToBytes(content);
        _position = 0;
        _isOpen = true;
    }

    /// <summary>
    /// Convert a string to UTF-8 bytes without using System.Text.Encoding.
    /// Handles ASCII and common UTF-8 code points.
    /// </summary>
    private static byte[] StringToBytes(string s)
    {
        if (string.IsNullOrEmpty(s))
            return new byte[0];

        // First pass: calculate required buffer size
        int byteCount = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
                byteCount += 1;
            else if (c < 0x800)
                byteCount += 2;
            else
                byteCount += 3;
        }

        // Allocate buffer
        byte[] result = new byte[byteCount];

        // Second pass: encode
        int pos = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < 0x80)
            {
                result[pos++] = (byte)c;
            }
            else if (c < 0x800)
            {
                result[pos++] = (byte)(0xC0 | (c >> 6));
                result[pos++] = (byte)(0x80 | (c & 0x3F));
            }
            else
            {
                result[pos++] = (byte)(0xE0 | (c >> 12));
                result[pos++] = (byte)(0x80 | ((c >> 6) & 0x3F));
                result[pos++] = (byte)(0x80 | (c & 0x3F));
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public bool IsOpen => _isOpen;

    /// <inheritdoc/>
    public long Position
    {
        get => _position;
        set
        {
            if (value < 0)
                _position = 0;
            else if (value > _buffer.Length)
                _position = _buffer.Length;
            else
                _position = value;
        }
    }

    /// <inheritdoc/>
    public long Length => _buffer.Length;

    /// <inheritdoc/>
    public FileAccess Access => FileAccess.Read;

    /// <inheritdoc/>
    public int Read(byte* buffer, int count)
    {
        if (!_isOpen)
            return (int)FileResult.InvalidHandle;

        if (buffer == null || count < 0)
            return 0;

        // Calculate how many bytes we can read
        long remaining = _buffer.Length - _position;
        if (remaining <= 0)
            return 0;

        int bytesToRead = count;
        if (bytesToRead > remaining)
            bytesToRead = (int)remaining;

        // Copy data to output buffer
        for (int i = 0; i < bytesToRead; i++)
        {
            buffer[i] = _buffer[_position + i];
        }

        _position += bytesToRead;
        return bytesToRead;
    }

    /// <inheritdoc/>
    public int Write(byte* buffer, int count)
    {
        // Proc files are read-only
        return (int)FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public FileResult Flush()
    {
        // Nothing to flush for read-only content
        return FileResult.Success;
    }

    /// <inheritdoc/>
    public FileResult SetLength(long length)
    {
        // Cannot modify proc file content
        return FileResult.ReadOnly;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isOpen = false;
        _buffer = new byte[0];
    }
}
