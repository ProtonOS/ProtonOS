// ProtonOS DDK - Driver Attributes for Registration and Matching

using System;

namespace ProtonOS.DDK.Drivers;

/// <summary>
/// Base attribute for driver metadata.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DriverAttribute : Attribute
{
    /// <summary>Display name of the driver.</summary>
    public string Name { get; }

    /// <summary>Driver version string.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Description of the driver.</summary>
    public string Description { get; set; } = "";

    /// <summary>Author of the driver.</summary>
    public string Author { get; set; } = "";

    public DriverAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Attribute for PCI device matching.
/// A driver class can have multiple PciDeviceAttribute instances to match multiple devices.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class PciDeviceAttribute : Attribute
{
    /// <summary>PCI vendor ID to match (0xFFFF = any).</summary>
    public ushort VendorId { get; }

    /// <summary>PCI device ID to match (0xFFFF = any).</summary>
    public ushort DeviceId { get; }

    /// <summary>PCI class code to match (0xFF = any).</summary>
    public byte ClassCode { get; set; } = 0xFF;

    /// <summary>PCI subclass code to match (0xFF = any).</summary>
    public byte SubclassCode { get; set; } = 0xFF;

    /// <summary>PCI programming interface to match (0xFF = any).</summary>
    public byte ProgIf { get; set; } = 0xFF;

    /// <summary>Subsystem vendor ID to match (0xFFFF = any).</summary>
    public ushort SubsystemVendorId { get; set; } = 0xFFFF;

    /// <summary>Subsystem device ID to match (0xFFFF = any).</summary>
    public ushort SubsystemDeviceId { get; set; } = 0xFFFF;

    /// <summary>
    /// Match by vendor/device ID.
    /// </summary>
    public PciDeviceAttribute(ushort vendorId, ushort deviceId)
    {
        VendorId = vendorId;
        DeviceId = deviceId;
    }

    /// <summary>
    /// Match by class/subclass/progif.
    /// </summary>
    public PciDeviceAttribute(byte classCode, byte subclassCode, byte progIf)
    {
        VendorId = 0xFFFF;
        DeviceId = 0xFFFF;
        ClassCode = classCode;
        SubclassCode = subclassCode;
        ProgIf = progIf;
    }

    /// <summary>
    /// Check if this attribute matches a PCI device.
    /// </summary>
    public bool Matches(PciDeviceInfo device)
    {
        // Check vendor/device ID if specified
        if (VendorId != 0xFFFF && VendorId != device.VendorId)
            return false;
        if (DeviceId != 0xFFFF && DeviceId != device.DeviceId)
            return false;

        // Check class/subclass/progif if specified
        if (ClassCode != 0xFF && ClassCode != device.ClassCode)
            return false;
        if (SubclassCode != 0xFF && SubclassCode != device.SubclassCode)
            return false;
        if (ProgIf != 0xFF && ProgIf != device.ProgIf)
            return false;

        // Check subsystem IDs if specified
        if (SubsystemVendorId != 0xFFFF && SubsystemVendorId != device.SubsystemVendorId)
            return false;
        if (SubsystemDeviceId != 0xFFFF && SubsystemDeviceId != device.SubsystemDeviceId)
            return false;

        return true;
    }
}

/// <summary>
/// Attribute for USB device matching.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class UsbDeviceAttribute : Attribute
{
    /// <summary>USB vendor ID to match (0xFFFF = any).</summary>
    public ushort VendorId { get; }

    /// <summary>USB product ID to match (0xFFFF = any).</summary>
    public ushort ProductId { get; }

    /// <summary>USB device class to match (0xFF = any).</summary>
    public byte DeviceClass { get; set; } = 0xFF;

    /// <summary>USB device subclass to match (0xFF = any).</summary>
    public byte DeviceSubclass { get; set; } = 0xFF;

    /// <summary>USB device protocol to match (0xFF = any).</summary>
    public byte DeviceProtocol { get; set; } = 0xFF;

    /// <summary>
    /// Match by vendor/product ID.
    /// </summary>
    public UsbDeviceAttribute(ushort vendorId, ushort productId)
    {
        VendorId = vendorId;
        ProductId = productId;
    }

    /// <summary>
    /// Match by class/subclass/protocol.
    /// </summary>
    public UsbDeviceAttribute(byte deviceClass, byte deviceSubclass, byte deviceProtocol)
    {
        VendorId = 0xFFFF;
        ProductId = 0xFFFF;
        DeviceClass = deviceClass;
        DeviceSubclass = deviceSubclass;
        DeviceProtocol = deviceProtocol;
    }
}

/// <summary>
/// Attribute to mark a driver as a bootstrap driver.
/// Bootstrap drivers are loaded before filesystem mount.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class BootstrapDriverAttribute : Attribute
{
    /// <summary>Load priority (lower = earlier).</summary>
    public int Priority { get; set; } = 100;
}
