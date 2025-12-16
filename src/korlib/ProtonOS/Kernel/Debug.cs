// ProtonOS korlib - DDK Debug API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// DDK Debug output API.
/// </summary>
public static unsafe class Debug
{
    // Low-level exports (mapped to kernel exports)
    private static void Kernel_DebugWrite(char* str, int len) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteLine(char* str, int len) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteHex64(ulong value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteHex32(uint value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteHex16(ushort value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteHex8(byte value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteDecimal(int value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteDecimalU(uint value) => throw new PlatformNotSupportedException();
    private static void Kernel_DebugWriteDecimal64(ulong value) => throw new PlatformNotSupportedException();

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
    /// Write a formatted string.
    /// </summary>
    public static void Write(string format, object? arg0)
    {
        Write(string.Format(format, arg0));
    }

    /// <summary>
    /// Write a formatted string.
    /// </summary>
    public static void Write(string format, object? arg0, object? arg1)
    {
        Write(string.Format(format, arg0, arg1));
    }

    /// <summary>
    /// Write a formatted string.
    /// </summary>
    public static void Write(string format, object? arg0, object? arg1, object? arg2)
    {
        Write(string.Format(format, arg0, arg1, arg2));
    }

    /// <summary>
    /// Write a formatted string with newline.
    /// </summary>
    public static void WriteLine(string format, object? arg0)
    {
        WriteLine(string.Format(format, arg0));
    }

    /// <summary>
    /// Write a formatted string with newline.
    /// </summary>
    public static void WriteLine(string format, object? arg0, object? arg1)
    {
        WriteLine(string.Format(format, arg0, arg1));
    }

    /// <summary>
    /// Write a formatted string with newline.
    /// </summary>
    public static void WriteLine(string format, object? arg0, object? arg1, object? arg2)
    {
        WriteLine(string.Format(format, arg0, arg1, arg2));
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

    /// <summary>
    /// Write a signed decimal value.
    /// </summary>
    public static void WriteDecimal(int value)
    {
        Kernel_DebugWriteDecimal(value);
    }

    /// <summary>
    /// Write an unsigned decimal value.
    /// </summary>
    public static void WriteDecimal(uint value)
    {
        Kernel_DebugWriteDecimalU(value);
    }

    /// <summary>
    /// Write an unsigned 64-bit decimal value.
    /// </summary>
    public static void WriteDecimal(ulong value)
    {
        Kernel_DebugWriteDecimal64(value);
    }
}
#endif
