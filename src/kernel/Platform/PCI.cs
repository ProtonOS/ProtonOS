// ProtonOS Kernel - PCI Configuration Access
// Native PCI enumeration and configuration space access

using System;
using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Platform;

/// <summary>
/// Represents a detected PCI device.
/// </summary>
public struct PciDevice
{
    public byte Bus;
    public byte Device;
    public byte Function;
    public ushort VendorId;
    public ushort DeviceId;
    public byte BaseClass;
    public byte SubClass;
    public byte ProgIF;
}

/// <summary>
/// PCI configuration space access via I/O ports 0xCF8/0xCFC.
/// </summary>
public static unsafe class PCI
{
    private const ushort ConfigAddressPort = 0xCF8;
    private const ushort ConfigDataPort = 0xCFC;

    // Storage for detected devices (simple fixed-size array)
    private const int MaxDevices = 64;
    private static PciDevice* _devices;
    private static int _deviceCount;

    /// <summary>
    /// Number of detected PCI devices.
    /// </summary>
    public static int DeviceCount => _deviceCount;

    /// <summary>
    /// Get a detected device by index.
    /// </summary>
    public static PciDevice* GetDevice(int index)
    {
        if (index < 0 || index >= _deviceCount || _devices == null)
            return null;
        return &_devices[index];
    }

    /// <summary>
    /// Initialize PCI device storage.
    /// </summary>
    public static void Initialize()
    {
        // Allocate device array from heap
        _devices = (PciDevice*)Memory.HeapAllocator.Alloc((ulong)(sizeof(PciDevice) * MaxDevices));
        _deviceCount = 0;
    }

    /// <summary>
    /// Build a PCI config address for the given BDF and register.
    /// </summary>
    private static uint BuildConfigAddress(byte bus, byte device, byte function, byte offset)
    {
        return (uint)(
            (1 << 31) |           // Enable bit
            (bus << 16) |         // Bus number
            ((device & 0x1F) << 11) |  // Device number (5 bits)
            ((function & 0x07) << 8) | // Function number (3 bits)
            (offset & 0xFC)       // Register offset (aligned to 4 bytes)
        );
    }

    /// <summary>
    /// Read a 32-bit value from PCI config space.
    /// </summary>
    public static uint ReadConfig32(byte bus, byte device, byte function, byte offset)
    {
        uint address = BuildConfigAddress(bus, device, function, offset);
        CPU.OutDword(ConfigAddressPort, address);
        return CPU.InDword(ConfigDataPort);
    }

    /// <summary>
    /// Read a 16-bit value from PCI config space.
    /// </summary>
    public static ushort ReadConfig16(byte bus, byte device, byte function, byte offset)
    {
        uint data = ReadConfig32(bus, device, function, (byte)(offset & 0xFC));
        int shift = (offset & 2) * 8;
        return (ushort)((data >> shift) & 0xFFFF);
    }

    /// <summary>
    /// Read an 8-bit value from PCI config space.
    /// </summary>
    public static byte ReadConfig8(byte bus, byte device, byte function, byte offset)
    {
        uint data = ReadConfig32(bus, device, function, (byte)(offset & 0xFC));
        int shift = (offset & 3) * 8;
        return (byte)((data >> shift) & 0xFF);
    }

    /// <summary>
    /// Write a 32-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig32(byte bus, byte device, byte function, byte offset, uint value)
    {
        uint address = BuildConfigAddress(bus, device, function, offset);
        CPU.OutDword(ConfigAddressPort, address);
        CPU.OutDword(ConfigDataPort, value);
    }

    /// <summary>
    /// Write a 16-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig16(byte bus, byte device, byte function, byte offset, ushort value)
    {
        uint address = BuildConfigAddress(bus, device, function, offset);
        CPU.OutDword(ConfigAddressPort, address);

        // Read-modify-write
        uint data = CPU.InDword(ConfigDataPort);
        int shift = (offset & 2) * 8;
        data = (data & ~(0xFFFFu << shift)) | ((uint)value << shift);
        CPU.OutDword(ConfigDataPort, data);
    }

    /// <summary>
    /// Write an 8-bit value to PCI config space.
    /// </summary>
    public static void WriteConfig8(byte bus, byte device, byte function, byte offset, byte value)
    {
        uint address = BuildConfigAddress(bus, device, function, offset);
        CPU.OutDword(ConfigAddressPort, address);

        // Read-modify-write
        uint data = CPU.InDword(ConfigDataPort);
        int shift = (offset & 3) * 8;
        data = (data & ~(0xFFu << shift)) | ((uint)value << shift);
        CPU.OutDword(ConfigDataPort, data);
    }

    /// <summary>
    /// Check if a device exists at the given BDF.
    /// </summary>
    public static bool DeviceExists(byte bus, byte device, byte function)
    {
        uint vendorDevice = ReadConfig32(bus, device, function, 0);
        return vendorDevice != 0xFFFFFFFF && (vendorDevice & 0xFFFF) != 0xFFFF;
    }

    /// <summary>
    /// Get the vendor ID of a device.
    /// </summary>
    public static ushort GetVendorId(byte bus, byte device, byte function)
    {
        return ReadConfig16(bus, device, function, 0);
    }

    /// <summary>
    /// Get the device ID of a device.
    /// </summary>
    public static ushort GetDeviceId(byte bus, byte device, byte function)
    {
        return ReadConfig16(bus, device, function, 2);
    }

    /// <summary>
    /// Get the class code (base class, subclass, prog IF).
    /// </summary>
    public static uint GetClassCode(byte bus, byte device, byte function)
    {
        return ReadConfig32(bus, device, function, 8) >> 8;
    }

    /// <summary>
    /// Get the header type.
    /// </summary>
    public static byte GetHeaderType(byte bus, byte device, byte function)
    {
        return ReadConfig8(bus, device, function, 0x0E);
    }

    /// <summary>
    /// Check if this is a multi-function device.
    /// </summary>
    public static bool IsMultiFunction(byte bus, byte device)
    {
        return (GetHeaderType(bus, device, 0) & 0x80) != 0;
    }

    /// <summary>
    /// Enable memory space access for a device.
    /// </summary>
    public static void EnableMemorySpace(byte bus, byte device, byte function)
    {
        ushort cmd = ReadConfig16(bus, device, function, 4);
        cmd |= 0x02; // Memory Space Enable
        WriteConfig16(bus, device, function, 4, cmd);
    }

    /// <summary>
    /// Enable bus mastering for a device.
    /// </summary>
    public static void EnableBusMaster(byte bus, byte device, byte function)
    {
        ushort cmd = ReadConfig16(bus, device, function, 4);
        cmd |= 0x04; // Bus Master Enable
        WriteConfig16(bus, device, function, 4, cmd);
    }

    /// <summary>
    /// Get BAR value.
    /// </summary>
    public static uint GetBAR(byte bus, byte device, byte function, int barIndex)
    {
        if (barIndex < 0 || barIndex > 5)
            return 0;
        return ReadConfig32(bus, device, function, (byte)(0x10 + barIndex * 4));
    }

    /// <summary>
    /// Get BAR size by writing all 1s and reading back.
    /// </summary>
    public static uint GetBARSize(byte bus, byte device, byte function, int barIndex)
    {
        if (barIndex < 0 || barIndex > 5)
            return 0;

        byte offset = (byte)(0x10 + barIndex * 4);
        uint originalValue = ReadConfig32(bus, device, function, offset);

        // Write all 1s
        WriteConfig32(bus, device, function, offset, 0xFFFFFFFF);
        uint sizeMask = ReadConfig32(bus, device, function, offset);

        // Restore original value
        WriteConfig32(bus, device, function, offset, originalValue);

        // Check if this is a memory or I/O BAR
        if ((sizeMask & 1) != 0)
        {
            // I/O BAR
            sizeMask &= ~0x3u;
        }
        else
        {
            // Memory BAR
            sizeMask &= ~0xFu;
        }

        if (sizeMask == 0)
            return 0;

        // Size is the inverse + 1
        return (~sizeMask) + 1;
    }

    /// <summary>
    /// Enumerate all PCI devices and print them.
    /// </summary>
    public static void EnumerateAndPrint()
    {
        DebugConsole.WriteLine("[PCI] Enumerating PCI devices...");

        int deviceCount = 0;
        int virtioCount = 0;

        for (int busInt = 0; busInt < 256; busInt++)
        {
            byte bus = (byte)busInt;
            for (int devInt = 0; devInt < 32; devInt++)
            {
                byte device = (byte)devInt;
                if (!DeviceExists(bus, device, 0))
                    continue;

                int functionCount = IsMultiFunction(bus, device) ? 8 : 1;

                for (int funcInt = 0; funcInt < functionCount; funcInt++)
                {
                    byte function = (byte)funcInt;
                    if (!DeviceExists(bus, device, function))
                        continue;

                    ushort vendorId = GetVendorId(bus, device, function);
                    ushort pciDeviceId = GetDeviceId(bus, device, function);
                    uint classCode = GetClassCode(bus, device, function);

                    byte baseClass = (byte)(classCode >> 16);
                    byte subClass = (byte)(classCode >> 8);
                    byte progIF = (byte)classCode;

                    // Store the device
                    if (_devices != null && _deviceCount < MaxDevices)
                    {
                        _devices[_deviceCount].Bus = bus;
                        _devices[_deviceCount].Device = device;
                        _devices[_deviceCount].Function = function;
                        _devices[_deviceCount].VendorId = vendorId;
                        _devices[_deviceCount].DeviceId = pciDeviceId;
                        _devices[_deviceCount].BaseClass = baseClass;
                        _devices[_deviceCount].SubClass = subClass;
                        _devices[_deviceCount].ProgIF = progIF;
                        _deviceCount++;
                    }

                    DebugConsole.Write("[PCI]   ");
                    DebugConsole.WriteHex(bus);
                    DebugConsole.Write(":");
                    DebugConsole.WriteHex(device);
                    DebugConsole.Write(".");
                    DebugConsole.WriteHex(function);
                    DebugConsole.Write(" - Vendor:");
                    DebugConsole.WriteHex(vendorId);
                    DebugConsole.Write(" Device:");
                    DebugConsole.WriteHex(pciDeviceId);
                    DebugConsole.Write(" Class:");
                    DebugConsole.WriteHex(baseClass);
                    DebugConsole.Write(":");
                    DebugConsole.WriteHex(subClass);
                    DebugConsole.Write(":");
                    DebugConsole.WriteHex(progIF);

                    // Check for virtio devices
                    if (vendorId == 0x1AF4)
                    {
                        DebugConsole.Write(" [VIRTIO");
                        if (pciDeviceId >= 0x1000 && pciDeviceId <= 0x107F)
                        {
                            // Legacy device IDs
                            if (pciDeviceId == 0x1001) DebugConsole.Write("-BLK");
                            else if (pciDeviceId == 0x1000) DebugConsole.Write("-NET");
                            else if (pciDeviceId == 0x1002) DebugConsole.Write("-BAL");
                            else if (pciDeviceId == 0x1003) DebugConsole.Write("-CON");
                            else if (pciDeviceId == 0x1005) DebugConsole.Write("-RNG");
                            else if (pciDeviceId == 0x1009) DebugConsole.Write("-9P");
                        }
                        else if (pciDeviceId >= 0x1040 && pciDeviceId <= 0x107F)
                        {
                            // Modern device IDs
                            ushort type = (ushort)(pciDeviceId - 0x1040);
                            if (type == 1) DebugConsole.Write("-NET");
                            else if (type == 2) DebugConsole.Write("-BLK");
                            else if (type == 3) DebugConsole.Write("-CON");
                            else if (type == 4) DebugConsole.Write("-RNG");
                        }
                        DebugConsole.Write("]");
                        virtioCount++;
                    }

                    // Print device class name
                    PrintClassName(baseClass, subClass);

                    DebugConsole.WriteLine();
                    deviceCount++;
                }
            }
        }

        DebugConsole.Write("[PCI] Found ");
        DebugConsole.WriteDecimal(deviceCount);
        DebugConsole.Write(" device(s), ");
        DebugConsole.WriteDecimal(virtioCount);
        DebugConsole.WriteLine(" virtio device(s)");
    }

    private static void PrintClassName(byte baseClass, byte subClass)
    {
        DebugConsole.Write(" ");
        switch (baseClass)
        {
            case 0x00:
                DebugConsole.Write("Unclassified");
                break;
            case 0x01:
                DebugConsole.Write("Storage");
                if (subClass == 0x01) DebugConsole.Write("/IDE");
                else if (subClass == 0x06) DebugConsole.Write("/SATA");
                else if (subClass == 0x08) DebugConsole.Write("/NVMe");
                break;
            case 0x02:
                DebugConsole.Write("Network");
                break;
            case 0x03:
                DebugConsole.Write("Display");
                break;
            case 0x04:
                DebugConsole.Write("Multimedia");
                break;
            case 0x05:
                DebugConsole.Write("Memory");
                break;
            case 0x06:
                DebugConsole.Write("Bridge");
                if (subClass == 0x00) DebugConsole.Write("/Host");
                else if (subClass == 0x01) DebugConsole.Write("/ISA");
                else if (subClass == 0x04) DebugConsole.Write("/PCI");
                break;
            case 0x07:
                DebugConsole.Write("Serial");
                break;
            case 0x08:
                DebugConsole.Write("System");
                break;
            case 0x09:
                DebugConsole.Write("Input");
                break;
            case 0x0C:
                DebugConsole.Write("SerialBus");
                if (subClass == 0x03) DebugConsole.Write("/USB");
                else if (subClass == 0x05) DebugConsole.Write("/SMBus");
                break;
            case 0x0D:
                DebugConsole.Write("Wireless");
                break;
            default:
                DebugConsole.Write("Other");
                break;
        }
    }
}
