// ProtonOS DDK - Debug Output
// Provides debug logging for drivers.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// Debug output for drivers.
/// </summary>
public static unsafe class Debug
{
    [DllImport("*", EntryPoint = "Kernel_DebugWrite")]
    private static extern void Kernel_DebugWrite(char* str, int len);

    [DllImport("*", EntryPoint = "Kernel_DebugWriteLine")]
    private static extern void Kernel_DebugWriteLine(char* str, int len);

    [DllImport("*", EntryPoint = "Kernel_DebugWriteHex64")]
    private static extern void Kernel_DebugWriteHex64(ulong value);

    [DllImport("*", EntryPoint = "Kernel_DebugWriteHex32")]
    private static extern void Kernel_DebugWriteHex32(uint value);

    [DllImport("*", EntryPoint = "Kernel_DebugWriteHex16")]
    private static extern void Kernel_DebugWriteHex16(ushort value);

    [DllImport("*", EntryPoint = "Kernel_DebugWriteHex8")]
    private static extern void Kernel_DebugWriteHex8(byte value);

    /// <summary>
    /// Write a string.
    /// </summary>
    public static void Write(string s)
    {
        fixed (char* ptr = s)
        {
            Kernel_DebugWrite(ptr, s.Length);
        }
    }

    /// <summary>
    /// Write a string with newline.
    /// </summary>
    public static void WriteLine(string s)
    {
        fixed (char* ptr = s)
        {
            Kernel_DebugWriteLine(ptr, s.Length);
        }
    }

    /// <summary>
    /// Write an empty newline.
    /// </summary>
    public static void WriteLine()
    {
        WriteLine("");
    }

    /// <summary>
    /// Write a hex value.
    /// </summary>
    public static void WriteHex(ulong value)
    {
        Kernel_DebugWriteHex64(value);
    }

    /// <summary>
    /// Write a hex value.
    /// </summary>
    public static void WriteHex(uint value)
    {
        Kernel_DebugWriteHex32(value);
    }

    /// <summary>
    /// Write a hex value.
    /// </summary>
    public static void WriteHex(ushort value)
    {
        Kernel_DebugWriteHex16(value);
    }

    /// <summary>
    /// Write a hex value.
    /// </summary>
    public static void WriteHex(byte value)
    {
        Kernel_DebugWriteHex8(value);
    }
}
