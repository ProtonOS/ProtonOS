// ProtonOS DDK - Base Driver Interface

using System;

namespace ProtonOS.DDK.Drivers;

/// <summary>
/// Base interface for all drivers.
/// </summary>
public interface IDriver
{
    /// <summary>Display name of the driver.</summary>
    string DriverName { get; }

    /// <summary>Driver version.</summary>
    Version DriverVersion { get; }

    /// <summary>Type of driver.</summary>
    DriverType Type { get; }

    /// <summary>Current state of the driver.</summary>
    DriverState State { get; }

    /// <summary>
    /// Initialize the driver.
    /// Called after the driver is loaded and bound to hardware.
    /// </summary>
    /// <returns>true if initialization succeeded</returns>
    bool Initialize();

    /// <summary>
    /// Shutdown the driver.
    /// Called before the driver is unloaded.
    /// </summary>
    void Shutdown();

    /// <summary>
    /// Suspend the driver for power management.
    /// </summary>
    void Suspend();

    /// <summary>
    /// Resume the driver from suspension.
    /// </summary>
    void Resume();
}

/// <summary>
/// Interface for drivers that bind to PCI devices.
/// </summary>
public interface IPciDriver : IDriver
{
    /// <summary>
    /// Probe a PCI device to see if this driver can handle it.
    /// </summary>
    /// <param name="device">PCI device information</param>
    /// <returns>true if this driver can handle the device</returns>
    bool Probe(PciDeviceInfo device);

    /// <summary>
    /// Bind to a PCI device.
    /// Called after Probe returns true.
    /// </summary>
    /// <param name="device">PCI device to bind to</param>
    void Bind(PciDeviceInfo device);

    /// <summary>
    /// Unbind from a PCI device.
    /// Called before driver unload or device removal.
    /// </summary>
    void Unbind();
}

/// <summary>
/// Interface for bus enumeration drivers.
/// </summary>
public interface IBusDriver : IDriver
{
    /// <summary>
    /// Enumerate devices on the bus.
    /// </summary>
    void EnumerateDevices();

    /// <summary>
    /// Get the number of devices on the bus.
    /// </summary>
    int DeviceCount { get; }
}
