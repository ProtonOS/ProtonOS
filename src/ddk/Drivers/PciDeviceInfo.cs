// ProtonOS DDK - PCI Device Information

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Drivers;

/// <summary>
/// PCI device location (bus/device/function).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PciAddress
{
    public byte Bus;
    public byte Device;
    public byte Function;

    public PciAddress(byte bus, byte device, byte function)
    {
        Bus = bus;
        Device = device;
        Function = function;
    }

    public override string ToString()
    {
        return $"{Bus:X2}:{Device:X2}.{Function:X1}";
    }
}

/// <summary>
/// PCI Base Address Register (BAR) information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct PciBar
{
    /// <summary>BAR index (0-5).</summary>
    public int Index;

    /// <summary>Base physical address.</summary>
    public ulong BaseAddress;

    /// <summary>Size of the BAR region in bytes.</summary>
    public ulong Size;

    /// <summary>True if this is an I/O BAR, false for memory BAR.</summary>
    public bool IsIO;

    /// <summary>True if this is a 64-bit memory BAR.</summary>
    public bool Is64Bit;

    /// <summary>True if this memory BAR is prefetchable.</summary>
    public bool IsPrefetchable;

    /// <summary>True if this BAR is valid and contains a resource.</summary>
    public bool IsValid => Size > 0;
}

/// <summary>
/// Complete PCI device information.
/// </summary>
public class PciDeviceInfo
{
    /// <summary>Device location on PCI bus.</summary>
    public PciAddress Address;

    /// <summary>PCI vendor ID.</summary>
    public ushort VendorId;

    /// <summary>PCI device ID.</summary>
    public ushort DeviceId;

    /// <summary>Device revision.</summary>
    public byte RevisionId;

    /// <summary>PCI class code.</summary>
    public byte ClassCode;

    /// <summary>PCI subclass code.</summary>
    public byte SubclassCode;

    /// <summary>PCI programming interface.</summary>
    public byte ProgIf;

    /// <summary>Subsystem vendor ID.</summary>
    public ushort SubsystemVendorId;

    /// <summary>Subsystem device ID.</summary>
    public ushort SubsystemDeviceId;

    /// <summary>Header type (0=normal, 1=bridge, 2=cardbus).</summary>
    public byte HeaderType;

    /// <summary>True if device is multi-function.</summary>
    public bool IsMultiFunction;

    /// <summary>Interrupt line (legacy IRQ).</summary>
    public byte InterruptLine;

    /// <summary>Interrupt pin (1=INTA, 2=INTB, etc., 0=none).</summary>
    public byte InterruptPin;

    /// <summary>MSI/MSI-X capability offset, or 0 if not supported.</summary>
    public byte MsiCapabilityOffset;

    /// <summary>MSI-X capability offset, or 0 if not supported.</summary>
    public byte MsixCapabilityOffset;

    /// <summary>Base Address Registers.</summary>
    public PciBar[] Bars = new PciBar[6];

    /// <summary>Virtual address for MMIO config space (PCIe ECAM), or 0 for legacy.</summary>
    public ulong ConfigSpaceAddress;

    /// <summary>
    /// Check if this device has MSI support.
    /// </summary>
    public bool SupportsMsi => MsiCapabilityOffset != 0;

    /// <summary>
    /// Check if this device has MSI-X support.
    /// </summary>
    public bool SupportsMsix => MsixCapabilityOffset != 0;

    /// <summary>
    /// Get a string description of the device class.
    /// </summary>
    public string GetClassDescription()
    {
        return ClassCode switch
        {
            0x00 => "Unclassified",
            0x01 => SubclassCode switch
            {
                0x00 => "SCSI Controller",
                0x01 => "IDE Controller",
                0x02 => "Floppy Controller",
                0x05 => "ATA Controller",
                0x06 => "SATA Controller",
                0x08 => "NVMe Controller",
                _ => "Mass Storage Controller"
            },
            0x02 => SubclassCode switch
            {
                0x00 => "Ethernet Controller",
                0x80 => "Other Network Controller",
                _ => "Network Controller"
            },
            0x03 => "Display Controller",
            0x04 => "Multimedia Controller",
            0x05 => "Memory Controller",
            0x06 => SubclassCode switch
            {
                0x00 => "Host Bridge",
                0x01 => "ISA Bridge",
                0x04 => "PCI-to-PCI Bridge",
                _ => "Bridge Device"
            },
            0x07 => "Communication Controller",
            0x08 => "System Peripheral",
            0x09 => "Input Device",
            0x0C => SubclassCode switch
            {
                0x03 => ProgIf switch
                {
                    0x00 => "UHCI Controller",
                    0x10 => "OHCI Controller",
                    0x20 => "EHCI Controller",
                    0x30 => "xHCI Controller",
                    _ => "USB Controller"
                },
                _ => "Serial Bus Controller"
            },
            _ => "Unknown"
        };
    }

    public override string ToString()
    {
        return $"PCI {Address}: {VendorId:X4}:{DeviceId:X4} ({GetClassDescription()})";
    }
}
