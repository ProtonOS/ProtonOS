// ProtonOS DDK - Port I/O Kernel Wrappers
// DllImport wrappers for x86 port I/O operations.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// DDK wrappers for x86 port I/O operations.
/// Only available on x86/x64 architecture.
/// </summary>
public static class PortIO
{
    [DllImport("*", EntryPoint = "Kernel_InByte")]
    public static extern byte InByte(ushort port);

    [DllImport("*", EntryPoint = "Kernel_OutByte")]
    public static extern void OutByte(ushort port, byte value);

    [DllImport("*", EntryPoint = "Kernel_InWord")]
    public static extern ushort InWord(ushort port);

    [DllImport("*", EntryPoint = "Kernel_OutWord")]
    public static extern void OutWord(ushort port, ushort value);

    [DllImport("*", EntryPoint = "Kernel_InDword")]
    public static extern uint InDword(ushort port);

    [DllImport("*", EntryPoint = "Kernel_OutDword")]
    public static extern void OutDword(ushort port, uint value);

    /// <summary>
    /// Read multiple bytes from a port.
    /// </summary>
    public static unsafe void InBytes(ushort port, byte* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InByte(port);
    }

    /// <summary>
    /// Write multiple bytes to a port.
    /// </summary>
    public static unsafe void OutBytes(ushort port, byte* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutByte(port, buffer[i]);
    }

    /// <summary>
    /// Read multiple words from a port (REP INSW).
    /// </summary>
    public static unsafe void InWords(ushort port, ushort* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InWord(port);
    }

    /// <summary>
    /// Write multiple words to a port (REP OUTSW).
    /// </summary>
    public static unsafe void OutWords(ushort port, ushort* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutWord(port, buffer[i]);
    }

    /// <summary>
    /// Read multiple dwords from a port (REP INSD).
    /// </summary>
    public static unsafe void InDwords(ushort port, uint* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            buffer[i] = InDword(port);
    }

    /// <summary>
    /// Write multiple dwords to a port (REP OUTSD).
    /// </summary>
    public static unsafe void OutDwords(ushort port, uint* buffer, int count)
    {
        for (int i = 0; i < count; i++)
            OutDword(port, buffer[i]);
    }
}
