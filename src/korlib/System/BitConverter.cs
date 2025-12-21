// ProtonOS korlib - BitConverter
// Converts base data types to and from arrays of bytes.

namespace System;

/// <summary>
/// Converts base data types to an array of bytes, and an array of bytes to base data types.
/// </summary>
public static class BitConverter
{
    /// <summary>
    /// Indicates the byte order ("endianness") in which data is stored in this computer architecture.
    /// </summary>
    public static bool IsLittleEndian => true;

    // GetBytes methods
    public static byte[] GetBytes(bool value)
    {
        return new byte[] { value ? (byte)1 : (byte)0 };
    }

    public static byte[] GetBytes(char value)
    {
        return GetBytes((short)value);
    }

    public static byte[] GetBytes(short value)
    {
        return new byte[]
        {
            (byte)value,
            (byte)(value >> 8)
        };
    }

    public static byte[] GetBytes(int value)
    {
        return new byte[]
        {
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24)
        };
    }

    public static byte[] GetBytes(long value)
    {
        return new byte[]
        {
            (byte)value,
            (byte)(value >> 8),
            (byte)(value >> 16),
            (byte)(value >> 24),
            (byte)(value >> 32),
            (byte)(value >> 40),
            (byte)(value >> 48),
            (byte)(value >> 56)
        };
    }

    public static byte[] GetBytes(ushort value) => GetBytes((short)value);
    public static byte[] GetBytes(uint value) => GetBytes((int)value);
    public static byte[] GetBytes(ulong value) => GetBytes((long)value);

    public static unsafe byte[] GetBytes(float value)
    {
        int val = *(int*)&value;
        return GetBytes(val);
    }

    public static unsafe byte[] GetBytes(double value)
    {
        long val = *(long*)&value;
        return GetBytes(val);
    }

    // ToXxx methods
    public static bool ToBoolean(byte[] value, int startIndex)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (startIndex < 0 || startIndex >= value.Length) throw new ArgumentOutOfRangeException(nameof(startIndex));
        return value[startIndex] != 0;
    }

    public static char ToChar(byte[] value, int startIndex)
    {
        return (char)ToInt16(value, startIndex);
    }

    public static short ToInt16(byte[] value, int startIndex)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (startIndex < 0 || startIndex > value.Length - 2) throw new ArgumentOutOfRangeException(nameof(startIndex));
        return (short)(value[startIndex] | (value[startIndex + 1] << 8));
    }

    public static int ToInt32(byte[] value, int startIndex)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (startIndex < 0 || startIndex > value.Length - 4) throw new ArgumentOutOfRangeException(nameof(startIndex));
        return value[startIndex] |
               (value[startIndex + 1] << 8) |
               (value[startIndex + 2] << 16) |
               (value[startIndex + 3] << 24);
    }

    public static long ToInt64(byte[] value, int startIndex)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (startIndex < 0 || startIndex > value.Length - 8) throw new ArgumentOutOfRangeException(nameof(startIndex));

        uint lo = (uint)(value[startIndex] |
                         (value[startIndex + 1] << 8) |
                         (value[startIndex + 2] << 16) |
                         (value[startIndex + 3] << 24));
        uint hi = (uint)(value[startIndex + 4] |
                         (value[startIndex + 5] << 8) |
                         (value[startIndex + 6] << 16) |
                         (value[startIndex + 7] << 24));
        return (long)((ulong)hi << 32 | lo);
    }

    public static ushort ToUInt16(byte[] value, int startIndex) => (ushort)ToInt16(value, startIndex);
    public static uint ToUInt32(byte[] value, int startIndex) => (uint)ToInt32(value, startIndex);
    public static ulong ToUInt64(byte[] value, int startIndex) => (ulong)ToInt64(value, startIndex);

    public static unsafe float ToSingle(byte[] value, int startIndex)
    {
        int val = ToInt32(value, startIndex);
        return *(float*)&val;
    }

    public static unsafe double ToDouble(byte[] value, int startIndex)
    {
        long val = ToInt64(value, startIndex);
        return *(double*)&val;
    }

    // Single value convenience overloads with ReadOnlySpan
    public static bool ToBoolean(ReadOnlySpan<byte> value) => value[0] != 0;
    public static char ToChar(ReadOnlySpan<byte> value) => (char)ToInt16(value);

    public static short ToInt16(ReadOnlySpan<byte> value)
    {
        return (short)(value[0] | (value[1] << 8));
    }

    public static int ToInt32(ReadOnlySpan<byte> value)
    {
        return value[0] |
               (value[1] << 8) |
               (value[2] << 16) |
               (value[3] << 24);
    }

    public static long ToInt64(ReadOnlySpan<byte> value)
    {
        uint lo = (uint)(value[0] |
                         (value[1] << 8) |
                         (value[2] << 16) |
                         (value[3] << 24));
        uint hi = (uint)(value[4] |
                         (value[5] << 8) |
                         (value[6] << 16) |
                         (value[7] << 24));
        return (long)((ulong)hi << 32 | lo);
    }

    public static ushort ToUInt16(ReadOnlySpan<byte> value) => (ushort)ToInt16(value);
    public static uint ToUInt32(ReadOnlySpan<byte> value) => (uint)ToInt32(value);
    public static ulong ToUInt64(ReadOnlySpan<byte> value) => (ulong)ToInt64(value);

    public static unsafe float ToSingle(ReadOnlySpan<byte> value)
    {
        int val = ToInt32(value);
        return *(float*)&val;
    }

    public static unsafe double ToDouble(ReadOnlySpan<byte> value)
    {
        long val = ToInt64(value);
        return *(double*)&val;
    }

    /// <summary>
    /// Converts a single-precision floating-point value to a 32-bit signed integer.
    /// </summary>
    public static unsafe int SingleToInt32Bits(float value)
    {
        return *(int*)&value;
    }

    /// <summary>
    /// Converts a 32-bit signed integer to a single-precision floating-point value.
    /// </summary>
    public static unsafe float Int32BitsToSingle(int value)
    {
        return *(float*)&value;
    }

    /// <summary>
    /// Converts a double-precision floating-point value to a 64-bit signed integer.
    /// </summary>
    public static unsafe long DoubleToInt64Bits(double value)
    {
        return *(long*)&value;
    }

    /// <summary>
    /// Converts a 64-bit signed integer to a double-precision floating-point value.
    /// </summary>
    public static unsafe double Int64BitsToDouble(long value)
    {
        return *(double*)&value;
    }

    /// <summary>
    /// Converts a half-precision floating-point value to a 16-bit signed integer.
    /// </summary>
    public static unsafe short HalfToInt16Bits(Half value)
    {
        return *(short*)&value;
    }

    /// <summary>
    /// Converts a 16-bit signed integer to a half-precision floating-point value.
    /// </summary>
    public static unsafe Half Int16BitsToHalf(short value)
    {
        return *(Half*)&value;
    }

    /// <summary>
    /// Converts a half-precision floating-point value to a 16-bit unsigned integer.
    /// </summary>
    public static unsafe ushort HalfToUInt16Bits(Half value)
    {
        return *(ushort*)&value;
    }

    /// <summary>
    /// Converts a 16-bit unsigned integer to a half-precision floating-point value.
    /// </summary>
    public static unsafe Half UInt16BitsToHalf(ushort value)
    {
        return *(Half*)&value;
    }
}
