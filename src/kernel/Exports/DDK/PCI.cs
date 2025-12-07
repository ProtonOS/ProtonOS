// ProtonOS kernel - DDK PCI Exports
// Exposes PCI configuration space access to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for PCI configuration space access.
/// </summary>
public static class PCIExports
{
    /// <summary>
    /// Read a 32-bit value from PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciReadConfig32")]
    public static uint ReadConfig32(byte bus, byte device, byte function, byte offset)
        => PCI.ReadConfig32(bus, device, function, offset);

    /// <summary>
    /// Read a 16-bit value from PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciReadConfig16")]
    public static ushort ReadConfig16(byte bus, byte device, byte function, byte offset)
        => PCI.ReadConfig16(bus, device, function, offset);

    /// <summary>
    /// Read an 8-bit value from PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciReadConfig8")]
    public static byte ReadConfig8(byte bus, byte device, byte function, byte offset)
        => PCI.ReadConfig8(bus, device, function, offset);

    /// <summary>
    /// Write a 32-bit value to PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciWriteConfig32")]
    public static void WriteConfig32(byte bus, byte device, byte function, byte offset, uint value)
        => PCI.WriteConfig32(bus, device, function, offset, value);

    /// <summary>
    /// Write a 16-bit value to PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciWriteConfig16")]
    public static void WriteConfig16(byte bus, byte device, byte function, byte offset, ushort value)
        => PCI.WriteConfig16(bus, device, function, offset, value);

    /// <summary>
    /// Write an 8-bit value to PCI config space.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciWriteConfig8")]
    public static void WriteConfig8(byte bus, byte device, byte function, byte offset, byte value)
        => PCI.WriteConfig8(bus, device, function, offset, value);

    /// <summary>
    /// Get BAR value.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciGetBar")]
    public static uint GetBar(byte bus, byte device, byte function, int barIndex)
        => PCI.GetBAR(bus, device, function, barIndex);

    /// <summary>
    /// Get BAR size by probing.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciGetBarSize")]
    public static uint GetBarSize(byte bus, byte device, byte function, int barIndex)
        => PCI.GetBARSize(bus, device, function, barIndex);

    /// <summary>
    /// Enable memory space access for a device.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciEnableMemorySpace")]
    public static void EnableMemorySpace(byte bus, byte device, byte function)
        => PCI.EnableMemorySpace(bus, device, function);

    /// <summary>
    /// Enable bus mastering for a device.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_PciEnableBusMaster")]
    public static void EnableBusMaster(byte bus, byte device, byte function)
        => PCI.EnableBusMaster(bus, device, function);
}
