// ProtonOS DDK - PCI Bus Enumeration
// Enumerates PCI devices and notifies the driver manager.

using System;
using System.Collections.Generic;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;

namespace ProtonOS.DDK.Platform;

/// <summary>
/// PCI bus enumerator.
/// Scans the PCI bus and registers discovered devices with the DriverManager.
/// </summary>
public static class PCIEnumerator
{
    private static List<PciDeviceInfo> _devices = new();
    private static bool _enumerated;

    /// <summary>
    /// All discovered PCI devices.
    /// </summary>
    public static IReadOnlyList<PciDeviceInfo> Devices => _devices;

    /// <summary>
    /// Enumerate all PCI devices.
    /// </summary>
    public static void Enumerate()
    {
        if (_enumerated)
            return;

        _devices = new List<PciDeviceInfo>();

        // Scan bus 0 first
        ScanBus(0);

        _enumerated = true;

        // Register all devices with DriverManager
        foreach (var device in _devices)
        {
            DriverManager.AddPciDevice(device);
        }
    }

    /// <summary>
    /// Scan a single PCI bus.
    /// </summary>
    private static void ScanBus(byte bus)
    {
        for (byte device = 0; device < 32; device++)
        {
            ScanDevice(bus, device);
        }
    }

    /// <summary>
    /// Scan a device's functions.
    /// </summary>
    private static void ScanDevice(byte bus, byte device)
    {
        // Check function 0
        uint vendorDevice = PCI.ReadConfig32(bus, device, 0, PCI.PCI_VENDOR_ID);
        ushort vendorId = (ushort)vendorDevice;

        if (vendorId == 0xFFFF)
            return; // No device

        var info = ScanFunction(bus, device, 0);
        if (info != null)
        {
            _devices.Add(info);

            // Check if multi-function device
            if (info.IsMultiFunction)
            {
                for (byte function = 1; function < 8; function++)
                {
                    vendorDevice = PCI.ReadConfig32(bus, device, function, PCI.PCI_VENDOR_ID);
                    if ((ushort)vendorDevice != 0xFFFF)
                    {
                        var funcInfo = ScanFunction(bus, device, function);
                        if (funcInfo != null)
                            _devices.Add(funcInfo);
                    }
                }
            }

            // If this is a PCI-to-PCI bridge, scan the secondary bus
            if (info.ClassCode == 0x06 && info.SubclassCode == 0x04)
            {
                byte secondaryBus = PCI.ReadConfig8(bus, device, 0, 0x19);
                if (secondaryBus != 0)
                    ScanBus(secondaryBus);
            }
        }
    }

    /// <summary>
    /// Scan a single function and build device info.
    /// </summary>
    private static PciDeviceInfo? ScanFunction(byte bus, byte device, byte function)
    {
        uint vendorDevice = PCI.ReadConfig32(bus, device, function, PCI.PCI_VENDOR_ID);
        ushort vendorId = (ushort)vendorDevice;
        ushort deviceId = (ushort)(vendorDevice >> 16);

        if (vendorId == 0xFFFF)
            return null;

        var info = new PciDeviceInfo
        {
            Address = new PciAddress(bus, device, function),
            VendorId = vendorId,
            DeviceId = deviceId,
        };

        // Read class/revision
        uint classRev = PCI.ReadConfig32(bus, device, function, PCI.PCI_REVISION_ID);
        info.RevisionId = (byte)classRev;
        info.ProgIf = (byte)(classRev >> 8);
        info.SubclassCode = (byte)(classRev >> 16);
        info.ClassCode = (byte)(classRev >> 24);

        // Read header type
        byte headerType = PCI.ReadConfig8(bus, device, function, PCI.PCI_HEADER_TYPE);
        info.HeaderType = (byte)(headerType & 0x7F);
        info.IsMultiFunction = (headerType & 0x80) != 0;

        // Read subsystem IDs (only for header type 0)
        if (info.HeaderType == 0)
        {
            uint subsystem = PCI.ReadConfig32(bus, device, function, PCI.PCI_SUBSYSTEM_VENDOR_ID);
            info.SubsystemVendorId = (ushort)subsystem;
            info.SubsystemDeviceId = (ushort)(subsystem >> 16);
        }

        // Read interrupt info
        byte intInfo = PCI.ReadConfig8(bus, device, function, PCI.PCI_INTERRUPT_LINE);
        info.InterruptLine = intInfo;
        info.InterruptPin = PCI.ReadConfig8(bus, device, function, PCI.PCI_INTERRUPT_PIN);

        // Read BARs (only for header type 0)
        if (info.HeaderType == 0)
        {
            ReadBARs(bus, device, function, info);
        }

        // Find MSI/MSI-X capabilities
        info.MsiCapabilityOffset = PCI.FindCapability(info.Address, PCI.PCI_CAP_MSI);
        info.MsixCapabilityOffset = PCI.FindCapability(info.Address, PCI.PCI_CAP_MSIX);

        return info;
    }

    /// <summary>
    /// Read and decode BAR registers.
    /// </summary>
    private static void ReadBARs(byte bus, byte device, byte function, PciDeviceInfo info)
    {
        Debug.WriteHex(0xDDB00000u); // ReadBARs entry
        for (int i = 0; i < 6; i++)
        {
            ushort offset = (ushort)(PCI.PCI_BAR0 + i * 4);
            uint bar = PCI.ReadConfig32(bus, device, function, offset);
            Debug.WriteHex(0xDDB10000u | (uint)i); // BAR index
            Debug.WriteHex(bar); // Raw BAR value

            if (bar == 0)
                continue;

            info.Bars[i].Index = i;

            if ((bar & 0x01) != 0)
            {
                // I/O BAR
                info.Bars[i].IsIO = true;
                info.Bars[i].BaseAddress = bar & 0xFFFFFFFC;

                // Determine size
                PCI.WriteConfig32(bus, device, function, offset, 0xFFFFFFFF);
                uint size = PCI.ReadConfig32(bus, device, function, offset);
                PCI.WriteConfig32(bus, device, function, offset, bar);

                size = ~(size & 0xFFFFFFFC) + 1;
                info.Bars[i].Size = size & 0xFFFF; // I/O is 16-bit
            }
            else
            {
                // Memory BAR
                info.Bars[i].IsIO = false;
                int type = (int)((bar >> 1) & 0x03);
                info.Bars[i].IsPrefetchable = (bar & 0x08) != 0;

                if (type == 0x02)
                {
                    // 64-bit BAR
                    info.Bars[i].Is64Bit = true;
                    uint barHigh = PCI.ReadConfig32(bus, device, function, (ushort)(offset + 4));
                    Debug.WriteHex(0xDDB20000u | (uint)i); // 64-bit BAR
                    Debug.WriteHex(barHigh); // High 32 bits
                    ulong baseAddr = (bar & 0xFFFFFFF0) | ((ulong)barHigh << 32);
                    Debug.WriteHex((uint)(baseAddr >> 32));
                    Debug.WriteHex((uint)baseAddr);
                    info.Bars[i].BaseAddress = baseAddr;

                    // Determine size
                    PCI.WriteConfig32(bus, device, function, offset, 0xFFFFFFFF);
                    PCI.WriteConfig32(bus, device, function, (ushort)(offset + 4), 0xFFFFFFFF);
                    uint sizeLow = PCI.ReadConfig32(bus, device, function, offset);
                    uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(offset + 4));
                    PCI.WriteConfig32(bus, device, function, offset, bar);
                    PCI.WriteConfig32(bus, device, function, (ushort)(offset + 4), barHigh);

                    ulong size = ((ulong)sizeHigh << 32) | (sizeLow & 0xFFFFFFF0);
                    info.Bars[i].Size = ~size + 1;

                    i++; // Skip next BAR (used for upper 32 bits)
                }
                else
                {
                    // 32-bit BAR
                    info.Bars[i].Is64Bit = false;
                    info.Bars[i].BaseAddress = bar & 0xFFFFFFF0;

                    // Determine size
                    PCI.WriteConfig32(bus, device, function, offset, 0xFFFFFFFF);
                    uint size = PCI.ReadConfig32(bus, device, function, offset);
                    PCI.WriteConfig32(bus, device, function, offset, bar);

                    size = ~(size & 0xFFFFFFF0) + 1;
                    info.Bars[i].Size = size;
                }
            }
        }
    }

    /// <summary>
    /// Find all devices matching a class/subclass.
    /// </summary>
    public static List<PciDeviceInfo> FindByClass(byte classCode, byte subclass)
    {
        var results = new List<PciDeviceInfo>();
        foreach (var device in _devices)
        {
            if (device.ClassCode == classCode && device.SubclassCode == subclass)
                results.Add(device);
        }
        return results;
    }

    /// <summary>
    /// Find all devices matching a vendor/device ID.
    /// </summary>
    public static List<PciDeviceInfo> FindByVendorDevice(ushort vendorId, ushort deviceId)
    {
        var results = new List<PciDeviceInfo>();
        foreach (var device in _devices)
        {
            if (device.VendorId == vendorId && device.DeviceId == deviceId)
                results.Add(device);
        }
        return results;
    }

    /// <summary>
    /// Print device info for debugging.
    /// </summary>
    public static void DumpDevices()
    {
        foreach (var device in _devices)
        {
            // Use kernel debug output when available
            // For now, this is just a placeholder
            _ = device.ToString();
        }
    }
}
