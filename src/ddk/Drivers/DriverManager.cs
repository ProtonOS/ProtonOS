// ProtonOS DDK - Driver Manager
// Handles driver registration, device binding, and lifecycle management.

using System;
using System.Collections.Generic;

namespace ProtonOS.DDK.Drivers;

/// <summary>
/// Information about a registered driver.
/// </summary>
public class DriverRegistration
{
    /// <summary>Driver instance.</summary>
    public IDriver Driver { get; }

    /// <summary>Driver type (for matching).</summary>
    public Type DriverType { get; }

    /// <summary>PCI device attributes for matching.</summary>
    public PciDeviceAttribute[] PciMatches { get; }

    /// <summary>USB device attributes for matching.</summary>
    public UsbDeviceAttribute[] UsbMatches { get; }

    /// <summary>Is this a bootstrap driver?</summary>
    public bool IsBootstrap { get; }

    /// <summary>Bootstrap priority (lower = earlier).</summary>
    public int BootstrapPriority { get; }

    public DriverRegistration(IDriver driver, Type driverType)
    {
        Driver = driver;
        DriverType = driverType;

        // Get PCI match attributes
        var pciAttrs = driverType.GetCustomAttributes(typeof(PciDeviceAttribute), false);
        PciMatches = new PciDeviceAttribute[pciAttrs.Length];
        for (int i = 0; i < pciAttrs.Length; i++)
            PciMatches[i] = (PciDeviceAttribute)pciAttrs[i];

        // Get USB match attributes
        var usbAttrs = driverType.GetCustomAttributes(typeof(UsbDeviceAttribute), false);
        UsbMatches = new UsbDeviceAttribute[usbAttrs.Length];
        for (int i = 0; i < usbAttrs.Length; i++)
            UsbMatches[i] = (UsbDeviceAttribute)usbAttrs[i];

        // Check for bootstrap attribute
        var bootstrapAttr = driverType.GetCustomAttributes(typeof(BootstrapDriverAttribute), false);
        if (bootstrapAttr.Length > 0)
        {
            IsBootstrap = true;
            BootstrapPriority = ((BootstrapDriverAttribute)bootstrapAttr[0]).Priority;
        }
    }
}

/// <summary>
/// Information about a bound device.
/// </summary>
public class DeviceBinding
{
    /// <summary>Driver handling this device.</summary>
    public IDriver Driver { get; }

    /// <summary>PCI device info (if PCI device).</summary>
    public PciDeviceInfo? PciDevice { get; }

    public DeviceBinding(IDriver driver, PciDeviceInfo? pciDevice)
    {
        Driver = driver;
        PciDevice = pciDevice;
    }
}

/// <summary>
/// Manages driver registration, device binding, and lifecycle.
/// </summary>
public static class DriverManager
{
    private static List<DriverRegistration> _registeredDrivers = new();
    private static List<DeviceBinding> _boundDevices = new();
    private static List<PciDeviceInfo> _unboundPciDevices = new();
    private static bool _initialized;

    /// <summary>
    /// All registered drivers.
    /// </summary>
    public static IReadOnlyList<DriverRegistration> RegisteredDrivers => _registeredDrivers;

    /// <summary>
    /// All bound devices.
    /// </summary>
    public static IReadOnlyList<DeviceBinding> BoundDevices => _boundDevices;

    /// <summary>
    /// PCI devices waiting for a driver.
    /// </summary>
    public static IReadOnlyList<PciDeviceInfo> UnboundPciDevices => _unboundPciDevices;

    /// <summary>
    /// Initialize the driver manager.
    /// Called early in boot to set up driver infrastructure.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _registeredDrivers = new List<DriverRegistration>();
        _boundDevices = new List<DeviceBinding>();
        _unboundPciDevices = new List<PciDeviceInfo>();
        _initialized = true;
    }

    /// <summary>
    /// Register a driver with the driver manager.
    /// </summary>
    /// <param name="driver">Driver instance to register</param>
    /// <typeparam name="T">Driver type (for attribute extraction)</typeparam>
    public static void RegisterDriver<T>(T driver) where T : class, IDriver
    {
        if (!_initialized)
            Initialize();

        var registration = new DriverRegistration(driver, typeof(T));
        _registeredDrivers.Add(registration);

        // Try to match any unbound devices
        TryBindUnboundDevices();
    }

    /// <summary>
    /// Register a driver by type (instantiates using default constructor).
    /// </summary>
    public static void RegisterDriver<T>() where T : class, IDriver, new()
    {
        RegisterDriver(new T());
    }

    /// <summary>
    /// Unregister a driver.
    /// </summary>
    public static void UnregisterDriver(IDriver driver)
    {
        // Find and remove the registration
        for (int i = _registeredDrivers.Count - 1; i >= 0; i--)
        {
            if (_registeredDrivers[i].Driver == driver)
            {
                // Shutdown driver
                if (driver.State == DriverState.Running)
                    driver.Shutdown();

                _registeredDrivers.RemoveAt(i);
                break;
            }
        }

        // Remove any bindings for this driver
        for (int i = _boundDevices.Count - 1; i >= 0; i--)
        {
            if (_boundDevices[i].Driver == driver)
            {
                // Return device to unbound pool
                if (_boundDevices[i].PciDevice != null)
                    _unboundPciDevices.Add(_boundDevices[i].PciDevice);

                _boundDevices.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Add a discovered PCI device.
    /// Will attempt to find and bind a matching driver.
    /// </summary>
    public static void AddPciDevice(PciDeviceInfo device)
    {
        if (!_initialized)
            Initialize();

        // Try to find a matching driver
        var driver = FindPciDriver(device);
        if (driver != null)
        {
            BindPciDevice(device, driver);
        }
        else
        {
            _unboundPciDevices.Add(device);
        }
    }

    /// <summary>
    /// Find a driver that can handle a PCI device.
    /// </summary>
    private static IPciDriver? FindPciDriver(PciDeviceInfo device)
    {
        foreach (var reg in _registeredDrivers)
        {
            if (reg.Driver is not IPciDriver pciDriver)
                continue;

            // Check attribute-based matching first
            foreach (var match in reg.PciMatches)
            {
                if (match.Matches(device))
                    return pciDriver;
            }

            // Fall back to Probe method
            if (pciDriver.Probe(device))
                return pciDriver;
        }

        return null;
    }

    /// <summary>
    /// Bind a PCI device to a driver.
    /// </summary>
    private static void BindPciDevice(PciDeviceInfo device, IPciDriver driver)
    {
        driver.Bind(device);
        _boundDevices.Add(new DeviceBinding(driver, device));

        // Initialize driver if not already
        if (driver.State == DriverState.Loaded)
        {
            driver.Initialize();
        }
    }

    /// <summary>
    /// Try to bind any unbound devices to newly registered drivers.
    /// </summary>
    private static void TryBindUnboundDevices()
    {
        for (int i = _unboundPciDevices.Count - 1; i >= 0; i--)
        {
            var device = _unboundPciDevices[i];
            var driver = FindPciDriver(device);
            if (driver != null)
            {
                _unboundPciDevices.RemoveAt(i);
                BindPciDevice(device, driver);
            }
        }
    }

    /// <summary>
    /// Initialize bootstrap drivers (PCI, storage, filesystem).
    /// Called during early boot before filesystem is available.
    /// </summary>
    public static void InitBootstrap()
    {
        if (!_initialized)
            Initialize();

        // Sort bootstrap drivers by priority
        var bootstrapDrivers = new List<DriverRegistration>();
        foreach (var reg in _registeredDrivers)
        {
            if (reg.IsBootstrap)
                bootstrapDrivers.Add(reg);
        }

        // Sort by priority (lower = earlier)
        bootstrapDrivers.Sort((a, b) => a.BootstrapPriority.CompareTo(b.BootstrapPriority));

        // Initialize each bootstrap driver
        foreach (var reg in bootstrapDrivers)
        {
            if (reg.Driver.State == DriverState.Loaded)
            {
                reg.Driver.Initialize();
            }
        }
    }

    /// <summary>
    /// Load optional drivers from a filesystem path.
    /// Called after filesystem is mounted.
    /// </summary>
    public static void LoadOptionalDrivers(string driverPath)
    {
        // TODO: Enumerate DLL files in path and load/register them
        // This requires filesystem and assembly loading support
    }

    /// <summary>
    /// Suspend all drivers for power management.
    /// </summary>
    public static void SuspendAll()
    {
        foreach (var binding in _boundDevices)
        {
            if (binding.Driver.State == DriverState.Running)
            {
                binding.Driver.Suspend();
            }
        }
    }

    /// <summary>
    /// Resume all drivers from suspension.
    /// </summary>
    public static void ResumeAll()
    {
        foreach (var binding in _boundDevices)
        {
            if (binding.Driver.State == DriverState.Suspended)
            {
                binding.Driver.Resume();
            }
        }
    }

    /// <summary>
    /// Shutdown all drivers.
    /// Called during system shutdown.
    /// </summary>
    public static void ShutdownAll()
    {
        // Shutdown in reverse order of binding
        for (int i = _boundDevices.Count - 1; i >= 0; i--)
        {
            var driver = _boundDevices[i].Driver;
            if (driver.State == DriverState.Running || driver.State == DriverState.Suspended)
            {
                driver.Shutdown();
            }
        }
    }
}
