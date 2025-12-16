// ProtonOS korlib - DDK PCI API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// DDK PCI configuration space API.
/// </summary>
public static class PCI
{
    /// <summary>
    /// Read a 32-bit value from PCI config space.
    /// </summary>
    public static uint ReadConfig32(byte bus, byte device, byte function, byte offset)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read a 16-bit value from PCI config space.
    /// </summary>
    public static ushort ReadConfig16(byte bus, byte device, byte function, byte offset)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read an 8-bit value from PCI config space.
    /// </summary>
    public static byte ReadConfig8(byte bus, byte device, byte function, byte offset)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write a 32-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig32(byte bus, byte device, byte function, byte offset, uint value)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write a 16-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig16(byte bus, byte device, byte function, byte offset, ushort value)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Write an 8-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig8(byte bus, byte device, byte function, byte offset, byte value)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get BAR (Base Address Register) value.
    /// </summary>
    /// <param name="bus">PCI bus number.</param>
    /// <param name="device">Device number on bus.</param>
    /// <param name="function">Function number.</param>
    /// <param name="barIndex">BAR index (0-5).</param>
    /// <returns>BAR value.</returns>
    public static uint GetBar(byte bus, byte device, byte function, int barIndex)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get BAR size by probing.
    /// </summary>
    public static uint GetBarSize(byte bus, byte device, byte function, int barIndex)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Enable memory space access for a device.
    /// </summary>
    public static void EnableMemorySpace(byte bus, byte device, byte function)
        => throw new PlatformNotSupportedException();

    /// <summary>
    /// Enable bus mastering for a device.
    /// </summary>
    public static void EnableBusMaster(byte bus, byte device, byte function)
        => throw new PlatformNotSupportedException();
}
#endif
