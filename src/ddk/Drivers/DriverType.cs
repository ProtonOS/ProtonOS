// ProtonOS DDK - Driver Type Enumeration

namespace ProtonOS.DDK.Drivers;

/// <summary>
/// Types of drivers supported by the DDK.
/// </summary>
public enum DriverType
{
    /// <summary>Unknown or unspecified driver type.</summary>
    Unknown = 0,

    /// <summary>Platform/bus enumeration driver (PCI, USB hub).</summary>
    Bus,

    /// <summary>Block storage device driver (NVMe, AHCI, virtio-blk).</summary>
    Storage,

    /// <summary>Filesystem driver (FAT32, ext4).</summary>
    Filesystem,

    /// <summary>Network device driver (e1000, virtio-net).</summary>
    Network,

    /// <summary>USB host controller driver (xHCI, EHCI).</summary>
    USBHost,

    /// <summary>USB device/class driver (HID, mass storage).</summary>
    USBDevice,

    /// <summary>Input device driver (keyboard, mouse).</summary>
    Input,

    /// <summary>Graphics/display driver (GOP, virtio-gpu).</summary>
    Graphics,

    /// <summary>Audio device driver (HDA, USB audio).</summary>
    Audio,

    /// <summary>Serial port driver (UART).</summary>
    Serial,

    /// <summary>Timer/RTC driver.</summary>
    Timer,

    /// <summary>Platform-specific driver (ACPI, power management).</summary>
    Platform,
}

/// <summary>
/// Driver state during its lifecycle.
/// </summary>
public enum DriverState
{
    /// <summary>Driver is loaded but not initialized.</summary>
    Loaded,

    /// <summary>Driver is initializing.</summary>
    Initializing,

    /// <summary>Driver is running normally.</summary>
    Running,

    /// <summary>Driver is suspended (power management).</summary>
    Suspended,

    /// <summary>Driver is stopping.</summary>
    Stopping,

    /// <summary>Driver has stopped.</summary>
    Stopped,

    /// <summary>Driver failed to initialize or encountered fatal error.</summary>
    Failed,
}
