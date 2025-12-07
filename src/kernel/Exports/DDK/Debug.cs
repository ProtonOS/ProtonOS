// ProtonOS kernel - DDK Debug Exports
// Provides debug output for JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for debug output.
/// </summary>
public static unsafe class DebugExports
{
    /// <summary>
    /// Write a debug string.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWrite")]
    public static void DebugWrite(char* str, int len)
    {
        for (int i = 0; i < len && str[i] != '\0'; i++)
        {
            DebugConsole.WriteChar(str[i]);
        }
    }

    /// <summary>
    /// Write a debug string with newline.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWriteLine")]
    public static void DebugWriteLine(char* str, int len)
    {
        for (int i = 0; i < len && str[i] != '\0'; i++)
        {
            DebugConsole.WriteChar(str[i]);
        }
        DebugConsole.WriteLine("");
    }

    /// <summary>
    /// Write a hex value (64-bit).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWriteHex64")]
    public static void DebugWriteHex64(ulong value)
    {
        DebugConsole.WriteHex(value);
    }

    /// <summary>
    /// Write a hex value (32-bit).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWriteHex32")]
    public static void DebugWriteHex32(uint value)
    {
        DebugConsole.WriteHex(value);
    }

    /// <summary>
    /// Write a hex value (16-bit).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWriteHex16")]
    public static void DebugWriteHex16(ushort value)
    {
        DebugConsole.WriteHex(value);
    }

    /// <summary>
    /// Write a hex value (8-bit).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DebugWriteHex8")]
    public static void DebugWriteHex8(byte value)
    {
        DebugConsole.WriteHex(value);
    }
}
