// ProtonOS korlib - DDK Port I/O API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// DDK Port I/O API for x86/x64 port operations.
/// </summary>
public static unsafe class PortIO
{
    /// <summary>
    /// Read a byte from an I/O port.
    /// </summary>
    public static byte InByte(ushort port) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write a byte to an I/O port.
    /// </summary>
    public static void OutByte(ushort port, byte value) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read a word (16-bit) from an I/O port.
    /// </summary>
    public static ushort InWord(ushort port) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write a word (16-bit) to an I/O port.
    /// </summary>
    public static void OutWord(ushort port, ushort value) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read a dword (32-bit) from an I/O port.
    /// </summary>
    public static uint InDword(ushort port) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write a dword (32-bit) to an I/O port.
    /// </summary>
    public static void OutDword(ushort port, uint value) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read multiple bytes from a port.
    /// </summary>
    public static void InBytes(ushort port, byte* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InByte(port);
    }

    /// <summary>
    /// Write multiple bytes to a port.
    /// </summary>
    public static void OutBytes(ushort port, byte* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutByte(port, buffer[i]);
    }

    /// <summary>
    /// Read multiple words from a port.
    /// </summary>
    public static void InWords(ushort port, ushort* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InWord(port);
    }

    /// <summary>
    /// Write multiple words to a port.
    /// </summary>
    public static void OutWords(ushort port, ushort* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutWord(port, buffer[i]);
    }

    /// <summary>
    /// Read multiple dwords from a port.
    /// </summary>
    public static void InDwords(ushort port, uint* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InDword(port);
    }

    /// <summary>
    /// Write multiple dwords to a port.
    /// </summary>
    public static void OutDwords(ushort port, uint* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutDword(port, buffer[i]);
    }
}
#endif
