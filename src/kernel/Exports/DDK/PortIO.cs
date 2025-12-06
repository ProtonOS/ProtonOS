// ProtonOS kernel - DDK Port I/O Exports
// Exposes x86 I/O port operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for x86 I/O port operations.
/// </summary>
public static class PortIOExports
{
    [UnmanagedCallersOnly(EntryPoint = "Kernel_InByte")]
    public static byte InByte(ushort port) => CPU.InByte(port);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_OutByte")]
    public static void OutByte(ushort port, byte value) => CPU.OutByte(port, value);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_InWord")]
    public static ushort InWord(ushort port) => CPU.InWord(port);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_OutWord")]
    public static void OutWord(ushort port, ushort value) => CPU.OutWord(port, value);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_InDword")]
    public static uint InDword(ushort port) => CPU.InDword(port);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_OutDword")]
    public static void OutDword(ushort port, uint value) => CPU.OutDword(port, value);
}
