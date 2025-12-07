// ProtonOS VirtioBlk Entry Point
// Simple static entry point for JIT compilation

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Platform;
using ProtonOS.Drivers.Virtio;

namespace ProtonOS.Drivers.Storage.VirtioBlk;

/// <summary>
/// Static entry point for VirtioBlk driver, designed for JIT compilation.
/// </summary>
public static unsafe class VirtioBlkEntry
{
    // Active device instance
    private static VirtioBlkDevice? _device;

    /// <summary>
    /// Check if this driver supports the given PCI device.
    /// </summary>
    /// <param name="vendorId">PCI vendor ID</param>
    /// <param name="deviceId">PCI device ID</param>
    /// <returns>true if this driver can handle the device</returns>
    public static bool Probe(ushort vendorId, ushort deviceId)
    {
        // Check for virtio vendor
        if (vendorId != 0x1AF4)
            return false;

        // Check for virtio-blk device IDs
        // Legacy: 0x1001, Modern: 0x1042
        return deviceId == 0x1001 || deviceId == 0x1042;
    }

    /// <summary>
    /// Bind the driver to a PCI device.
    /// </summary>
    /// <param name="bus">PCI bus number</param>
    /// <param name="device">PCI device number</param>
    /// <param name="function">PCI function number</param>
    /// <returns>true if binding succeeded</returns>
    public static bool Bind(byte bus, byte device, byte function)
    {
        // Build PciDeviceInfo from bus/device/function
        var pciDevice = new PciDeviceInfo
        {
            Address = new PciAddress(bus, device, function)
        };

        // Read vendor/device IDs
        uint vendorDevice = PCI.ReadConfig32(bus, device, function, PCI.PCI_VENDOR_ID);
        pciDevice.VendorId = (ushort)(vendorDevice & 0xFFFF);
        pciDevice.DeviceId = (ushort)(vendorDevice >> 16);

        // Read class/subclass/progif
        uint classReg = PCI.ReadConfig32(bus, device, function, PCI.PCI_REVISION_ID);
        pciDevice.RevisionId = (byte)(classReg & 0xFF);
        pciDevice.ProgIf = (byte)((classReg >> 8) & 0xFF);
        pciDevice.SubclassCode = (byte)((classReg >> 16) & 0xFF);
        pciDevice.ClassCode = (byte)((classReg >> 24) & 0xFF);

        // Read BARs
        for (int i = 0; i < 6; i++)
        {
            pciDevice.Bars[i] = ReadBar(bus, device, function, i);
        }

        // Create and initialize the virtio block device
        _device = new VirtioBlkDevice();

        // Initialize virtio device (handles modern/legacy detection, feature negotiation)
        if (!_device.Initialize(pciDevice))
        {
            _device = null;
            return false;
        }

        // Initialize block-specific functionality
        if (!_device.InitializeBlockDevice())
        {
            _device.Dispose();
            _device = null;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Get the active block device instance.
    /// </summary>
    public static VirtioBlkDevice? GetDevice() => _device;

    /// <summary>
    /// Read a PCI BAR and its size.
    /// </summary>
    private static PciBar ReadBar(byte bus, byte device, byte function, int barIndex)
    {
        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        // Read original BAR value
        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        // Check if BAR is valid
        if (barValue == 0)
            return new PciBar { Index = barIndex };

        bool isIO = (barValue & 1) != 0;

        if (isIO)
        {
            // I/O BAR
            ulong baseAddr = barValue & 0xFFFFFFFC;

            // Read size by writing all 1s
            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeValue = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size = ~(sizeValue & 0xFFFFFFFC) + 1;
            size &= 0xFFFF; // I/O BARs are 16-bit

            return new PciBar
            {
                Index = barIndex,
                BaseAddress = baseAddr,
                Size = size,
                IsIO = true,
                Is64Bit = false,
                IsPrefetchable = false
            };
        }
        else
        {
            // Memory BAR
            bool is64Bit = ((barValue >> 1) & 3) == 2;
            bool isPrefetchable = (barValue & 8) != 0;

            ulong baseAddr = barValue & 0xFFFFFFF0;

            // For 64-bit BARs, read upper 32 bits
            if (is64Bit && barIndex < 5)
            {
                uint upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                baseAddr |= (ulong)upperBar << 32;
            }

            // Read size by writing all 1s
            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeValue = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size = ~(sizeValue & 0xFFFFFFF0) + 1;

            return new PciBar
            {
                Index = barIndex,
                BaseAddress = baseAddr,
                Size = size,
                IsIO = false,
                Is64Bit = is64Bit,
                IsPrefetchable = isPrefetchable
            };
        }
    }
}
