// ProtonOS JIT - Code Buffer
// Manages executable memory for JIT-compiled code

using ProtonOS.Memory;

namespace ProtonOS.Runtime.JIT;

/// <summary>
/// Buffer for emitting machine code during JIT compilation.
/// Allocates from CodeHeap which provides executable memory with proper W^X.
/// </summary>
public unsafe struct CodeBuffer
{
    private byte* _buffer;
    private int _size;
    private int _offset;
    private bool _overflow;

    /// <summary>
    /// Initialize a code buffer with the specified capacity.
    /// Allocates from the CodeHeap for proper executable memory.
    /// </summary>
    public static CodeBuffer Create(int capacity)
    {
        CodeBuffer buf;
        buf._buffer = CodeHeap.Alloc((ulong)capacity);
        buf._size = buf._buffer != null ? capacity : 0;
        buf._offset = 0;
        buf._overflow = false;
        return buf;
    }

    /// <summary>
    /// Get pointer to the start of the code buffer
    /// </summary>
    public byte* Code => _buffer;

    /// <summary>
    /// Current write position (bytes emitted so far)
    /// </summary>
    public int Position => _offset;

    /// <summary>
    /// Total capacity of buffer
    /// </summary>
    public int Capacity => _size;

    /// <summary>
    /// Remaining space in buffer
    /// </summary>
    public int Remaining => _size - _offset;

    /// <summary>
    /// Check if buffer is valid (allocated successfully)
    /// </summary>
    public bool IsValid => _buffer != null;

    /// <summary>
    /// Check if buffer has overflowed (tried to emit more bytes than capacity)
    /// </summary>
    public bool HasOverflow => _overflow;

    /// <summary>
    /// Emit a single byte
    /// </summary>
    public void EmitByte(byte b)
    {
        if (_offset < _size)
        {
            _buffer[_offset++] = b;
        }
        else
        {
            _overflow = true;
        }
    }

    /// <summary>
    /// Emit two bytes (little-endian)
    /// </summary>
    public void EmitWord(ushort w)
    {
        if (_offset + 2 <= _size)
        {
            _buffer[_offset++] = (byte)w;
            _buffer[_offset++] = (byte)(w >> 8);
        }
        else
        {
            _overflow = true;
        }
    }

    /// <summary>
    /// Emit four bytes (little-endian)
    /// </summary>
    public void EmitDword(uint d)
    {
        if (_offset + 4 <= _size)
        {
            _buffer[_offset++] = (byte)d;
            _buffer[_offset++] = (byte)(d >> 8);
            _buffer[_offset++] = (byte)(d >> 16);
            _buffer[_offset++] = (byte)(d >> 24);
        }
        else
        {
            _overflow = true;
        }
    }

    /// <summary>
    /// Emit eight bytes (little-endian)
    /// </summary>
    public void EmitQword(ulong q)
    {
        if (_offset + 8 <= _size)
        {
            _buffer[_offset++] = (byte)q;
            _buffer[_offset++] = (byte)(q >> 8);
            _buffer[_offset++] = (byte)(q >> 16);
            _buffer[_offset++] = (byte)(q >> 24);
            _buffer[_offset++] = (byte)(q >> 32);
            _buffer[_offset++] = (byte)(q >> 40);
            _buffer[_offset++] = (byte)(q >> 48);
            _buffer[_offset++] = (byte)(q >> 56);
        }
        else
        {
            _overflow = true;
        }
    }

    /// <summary>
    /// Emit a signed 32-bit integer
    /// </summary>
    public void EmitInt32(int i)
    {
        EmitDword((uint)i);
    }

    /// <summary>
    /// Get a function pointer to the emitted code.
    /// Example: var fn = buf.GetFunctionPointer&lt;delegate*&lt;int, int, int&gt;&gt;();
    /// </summary>
    public void* GetFunctionPointer()
    {
        return _buffer;
    }

    /// <summary>
    /// Reserve a slot and return its offset (for later patching)
    /// </summary>
    public int ReserveInt32()
    {
        int pos = _offset;
        EmitDword(0);
        return pos;
    }

    /// <summary>
    /// Patch a previously reserved int32 slot
    /// </summary>
    public void PatchInt32(int offset, int value)
    {
        if (offset >= 0 && offset + 4 <= _size)
        {
            _buffer[offset] = (byte)value;
            _buffer[offset + 1] = (byte)(value >> 8);
            _buffer[offset + 2] = (byte)(value >> 16);
            _buffer[offset + 3] = (byte)(value >> 24);
        }
    }

    /// <summary>
    /// Patch a relative jump at the given offset to target the current position
    /// </summary>
    public void PatchRelative32(int patchOffset)
    {
        // rel32 is calculated from the end of the instruction (patchOffset + 4)
        int rel = _offset - (patchOffset + 4);
        PatchInt32(patchOffset, rel);
    }
}
