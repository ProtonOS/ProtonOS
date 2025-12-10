// ProtonOS DDK - PCI Bus Access
// Provides PCI configuration space access for drivers.

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Platform;

/// <summary>
/// PCI configuration space access.
/// Supports both legacy I/O port access (x86) and PCIe ECAM (memory-mapped).
/// </summary>
public static unsafe class PCI
{
    // Legacy PCI configuration ports (x86)
    private const ushort PCI_CONFIG_ADDRESS = 0xCF8;
    private const ushort PCI_CONFIG_DATA = 0xCFC;

    // PCIe ECAM base (set during initialization)
    private static ulong _ecamBase;
    private static ulong _ecamVirtBase;
    private static byte _startBus;
    private static byte _endBus;
    private static bool _useEcam;
    private static bool _initialized;

    /// <summary>
    /// True if PCIe ECAM is available.
    /// </summary>
    public static bool HasECAM => _useEcam;

    /// <summary>
    /// Initialize PCI access.
    /// Called during DDK initialization.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        // Try to find MCFG table for PCIe ECAM
        var mcfg = ACPI.FindTable(ACPI.MCFG);
        if (mcfg != null)
        {
            // Parse MCFG to get ECAM base address
            // MCFG header is 44 bytes, entries start at offset 44
            byte* ptr = (byte*)mcfg;
            uint length = mcfg->Length;

            if (length >= 44 + 16) // At least one entry
            {
                // First entry at offset 44
                ulong baseAddr = *(ulong*)(ptr + 44);
                ushort segment = *(ushort*)(ptr + 52);
                byte startBus = ptr[54];
                byte endBus = ptr[55];

                if (segment == 0) // We only support segment 0 for now
                {
                    _ecamBase = baseAddr;
                    _startBus = startBus;
                    _endBus = endBus;

                    // Map ECAM region
                    ulong ecamSize = (ulong)(endBus - startBus + 1) * 256 * 4096; // buses * devices * config space
                    _ecamVirtBase = Memory.MapMMIO(baseAddr, ecamSize);
                    if (_ecamVirtBase != 0)
                    {
                        _useEcam = true;
                    }
                }
            }
        }

        _initialized = true;
    }

    /// <summary>
    /// Build a legacy PCI configuration address.
    /// </summary>
    private static uint MakeConfigAddress(byte bus, byte device, byte function, byte offset)
    {
        return 0x80000000 |
               ((uint)bus << 16) |
               ((uint)(device & 0x1F) << 11) |
               ((uint)(function & 0x07) << 8) |
               (uint)(offset & 0xFC);
    }

    /// <summary>
    /// Get ECAM virtual address for a configuration register.
    /// </summary>
    private static byte* GetEcamAddress(byte bus, byte device, byte function, ushort offset)
    {
        if (!_useEcam || bus < _startBus || bus > _endBus)
            return null;

        ulong addr = _ecamVirtBase +
                     ((ulong)(bus - _startBus) << 20) |
                     ((ulong)device << 15) |
                     ((ulong)function << 12) |
                     offset;
        return (byte*)addr;
    }

    /// <summary>
    /// Read a byte from PCI configuration space.
    /// </summary>
    public static byte ReadConfig8(byte bus, byte device, byte function, ushort offset)
    {
        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
                return *addr;
        }

        // Fall back to legacy I/O
        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        return (byte)(PortIO.InDword(PCI_CONFIG_DATA) >> ((offset & 3) * 8));
    }

    /// <summary>
    /// Read a word from PCI configuration space.
    /// </summary>
    public static ushort ReadConfig16(byte bus, byte device, byte function, ushort offset)
    {
        Debug.WriteHex(0xD00DB001u);
        Debug.WriteHex(_useEcam ? 0xD00DB002u : 0xD00DB003u);
        Debug.WriteHex((uint)(_ecamVirtBase >> 32));
        Debug.WriteHex((uint)_ecamVirtBase);

        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
                return *(ushort*)addr;
        }

        Debug.WriteHex(0xD00DB004u);
        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        Debug.WriteHex(address);
        Debug.WriteHex(0xD00DB005u);
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        Debug.WriteHex(0xD00DB006u);
        uint data = PortIO.InDword(PCI_CONFIG_DATA);
        Debug.WriteHex(0xD00DB007u);
        Debug.WriteHex(data);
        return (ushort)(data >> ((offset & 2) * 8));
    }

    /// <summary>
    /// Read a dword from PCI configuration space.
    /// </summary>
    public static uint ReadConfig32(byte bus, byte device, byte function, ushort offset)
    {
        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
                return *(uint*)addr;
        }

        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        return PortIO.InDword(PCI_CONFIG_DATA);
    }

    /// <summary>
    /// Write a byte to PCI configuration space.
    /// </summary>
    public static void WriteConfig8(byte bus, byte device, byte function, ushort offset, byte value)
    {
        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
            {
                *addr = value;
                return;
            }
        }

        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        uint data = PortIO.InDword(PCI_CONFIG_DATA);
        int shift = (offset & 3) * 8;
        data = (data & ~(0xFFU << shift)) | ((uint)value << shift);
        PortIO.OutDword(PCI_CONFIG_DATA, data);
    }

    /// <summary>
    /// Write a word to PCI configuration space.
    /// </summary>
    public static void WriteConfig16(byte bus, byte device, byte function, ushort offset, ushort value)
    {
        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
            {
                *(ushort*)addr = value;
                return;
            }
        }

        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        uint data = PortIO.InDword(PCI_CONFIG_DATA);
        int shift = (offset & 2) * 8;
        data = (data & ~(0xFFFFU << shift)) | ((uint)value << shift);
        PortIO.OutDword(PCI_CONFIG_DATA, data);
    }

    /// <summary>
    /// Write a dword to PCI configuration space.
    /// </summary>
    public static void WriteConfig32(byte bus, byte device, byte function, ushort offset, uint value)
    {
        if (_useEcam)
        {
            byte* addr = GetEcamAddress(bus, device, function, offset);
            if (addr != null)
            {
                *(uint*)addr = value;
                return;
            }
        }

        uint address = MakeConfigAddress(bus, device, function, (byte)(offset & 0xFF));
        PortIO.OutDword(PCI_CONFIG_ADDRESS, address);
        PortIO.OutDword(PCI_CONFIG_DATA, value);
    }

    /// <summary>
    /// Read a device's configuration using PciAddress.
    /// </summary>
    public static uint ReadConfig32(PciAddress addr, ushort offset)
    {
        return ReadConfig32(addr.Bus, addr.Device, addr.Function, offset);
    }

    /// <summary>
    /// Write to a device's configuration using PciAddress.
    /// </summary>
    public static void WriteConfig32(PciAddress addr, ushort offset, uint value)
    {
        WriteConfig32(addr.Bus, addr.Device, addr.Function, offset, value);
    }

    // PCI configuration space offsets
    public const ushort PCI_VENDOR_ID = 0x00;
    public const ushort PCI_DEVICE_ID = 0x02;
    public const ushort PCI_COMMAND = 0x04;
    public const ushort PCI_STATUS = 0x06;
    public const ushort PCI_REVISION_ID = 0x08;
    public const ushort PCI_PROG_IF = 0x09;
    public const ushort PCI_SUBCLASS = 0x0A;
    public const ushort PCI_CLASS = 0x0B;
    public const ushort PCI_CACHE_LINE = 0x0C;
    public const ushort PCI_LATENCY = 0x0D;
    public const ushort PCI_HEADER_TYPE = 0x0E;
    public const ushort PCI_BIST = 0x0F;
    public const ushort PCI_BAR0 = 0x10;
    public const ushort PCI_BAR1 = 0x14;
    public const ushort PCI_BAR2 = 0x18;
    public const ushort PCI_BAR3 = 0x1C;
    public const ushort PCI_BAR4 = 0x20;
    public const ushort PCI_BAR5 = 0x24;
    public const ushort PCI_SUBSYSTEM_VENDOR_ID = 0x2C;
    public const ushort PCI_SUBSYSTEM_ID = 0x2E;
    public const ushort PCI_INTERRUPT_LINE = 0x3C;
    public const ushort PCI_INTERRUPT_PIN = 0x3D;
    public const ushort PCI_CAPABILITIES_PTR = 0x34;

    // PCI command register bits
    public const ushort PCI_CMD_IO_SPACE = 0x0001;
    public const ushort PCI_CMD_MEMORY_SPACE = 0x0002;
    public const ushort PCI_CMD_BUS_MASTER = 0x0004;
    public const ushort PCI_CMD_INTERRUPT_DISABLE = 0x0400;

    // PCI capability IDs
    public const byte PCI_CAP_MSI = 0x05;
    public const byte PCI_CAP_MSIX = 0x11;

    /// <summary>
    /// Enable bus mastering for a device.
    /// </summary>
    public static void EnableBusMaster(PciAddress addr)
    {
        ushort cmd = ReadConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND);
        cmd |= PCI_CMD_BUS_MASTER;
        WriteConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND, cmd);
    }

    /// <summary>
    /// Enable memory space access for a device.
    /// </summary>
    public static void EnableMemorySpace(PciAddress addr)
    {
        ushort cmd = ReadConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND);
        cmd |= PCI_CMD_MEMORY_SPACE;
        WriteConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND, cmd);
    }

    /// <summary>
    /// Enable I/O space access for a device.
    /// </summary>
    public static void EnableIOSpace(PciAddress addr)
    {
        ushort cmd = ReadConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND);
        cmd |= PCI_CMD_IO_SPACE;
        WriteConfig16(addr.Bus, addr.Device, addr.Function, PCI_COMMAND, cmd);
    }

    /// <summary>
    /// Find a capability in the device's capability list.
    /// </summary>
    /// <returns>Capability offset, or 0 if not found</returns>
    public static byte FindCapability(PciAddress addr, byte capId)
    {
        // Check if device has capabilities
        ushort status = ReadConfig16(addr.Bus, addr.Device, addr.Function, PCI_STATUS);
        if ((status & 0x10) == 0) // Capabilities bit
            return 0;

        byte ptr = ReadConfig8(addr.Bus, addr.Device, addr.Function, PCI_CAPABILITIES_PTR);
        ptr &= 0xFC; // Align to dword

        while (ptr != 0)
        {
            byte id = ReadConfig8(addr.Bus, addr.Device, addr.Function, ptr);
            if (id == capId)
                return ptr;
            ptr = ReadConfig8(addr.Bus, addr.Device, addr.Function, (ushort)(ptr + 1));
            ptr &= 0xFC;
        }

        return 0;
    }
}
