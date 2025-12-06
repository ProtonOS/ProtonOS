// ProtonOS DDK - USB Device Manager
// Handles USB device enumeration, driver binding, and lifecycle.

using System;
using System.Collections.Generic;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.USB;

/// <summary>
/// Manages USB devices across all host controllers.
/// </summary>
public static class USBManager
{
    private static List<IUSBHostController> _controllers = new();
    private static List<USBDevice> _devices = new();
    private static List<IDriver> _classDrivers = new();
    private static bool _initialized;

    /// <summary>
    /// All registered USB host controllers.
    /// </summary>
    public static IReadOnlyList<IUSBHostController> Controllers => _controllers;

    /// <summary>
    /// All discovered USB devices.
    /// </summary>
    public static IReadOnlyList<USBDevice> Devices => _devices;

    /// <summary>
    /// Event fired when a USB device is connected.
    /// </summary>
    public static event USBDeviceEventCallback? DeviceConnected;

    /// <summary>
    /// Event fired when a USB device is disconnected.
    /// </summary>
    public static event USBDeviceEventCallback? DeviceDisconnected;

    /// <summary>
    /// Initialize the USB manager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _controllers = new List<IUSBHostController>();
        _devices = new List<USBDevice>();
        _classDrivers = new List<IDriver>();
        _initialized = true;
    }

    /// <summary>
    /// Register a USB host controller.
    /// </summary>
    public static void RegisterController(IUSBHostController controller)
    {
        if (!_initialized)
            Initialize();

        _controllers.Add(controller);

        // Set up port change callback
        controller.SetPortChangeCallback(port => OnPortChange(controller, port));

        // Enumerate existing devices
        EnumerateController(controller);
    }

    /// <summary>
    /// Unregister a USB host controller.
    /// </summary>
    public static void UnregisterController(IUSBHostController controller)
    {
        // Remove all devices from this controller
        for (int i = _devices.Count - 1; i >= 0; i--)
        {
            if (_devices[i].Controller == controller)
            {
                var device = _devices[i];
                _devices.RemoveAt(i);
                DeviceDisconnected?.Invoke(device, false);
            }
        }

        controller.SetPortChangeCallback(null);
        _controllers.Remove(controller);
    }

    /// <summary>
    /// Register a USB class driver.
    /// </summary>
    public static void RegisterClassDriver(IDriver driver)
    {
        if (!_initialized)
            Initialize();

        _classDrivers.Add(driver);

        // Try to bind to existing devices
        foreach (var device in _devices)
        {
            if (device.Driver == null)
            {
                TryBindDriver(device);
            }
        }
    }

    /// <summary>
    /// Unregister a USB class driver.
    /// </summary>
    public static void UnregisterClassDriver(IDriver driver)
    {
        _classDrivers.Remove(driver);

        // Unbind from devices
        foreach (var device in _devices)
        {
            if (device.Driver == driver)
            {
                device.Driver = null;
            }
        }
    }

    /// <summary>
    /// Enumerate all ports on a controller.
    /// </summary>
    private static void EnumerateController(IUSBHostController controller)
    {
        for (int port = 0; port < controller.PortCount; port++)
        {
            var status = controller.GetPortStatus(port);
            if ((status & USBPortStatus.Connected) != 0)
            {
                OnPortChange(controller, port);
            }
        }
    }

    /// <summary>
    /// Handle port status change.
    /// </summary>
    private static void OnPortChange(IUSBHostController controller, int port)
    {
        var status = controller.GetPortStatus(port);

        if ((status & USBPortStatus.Connected) != 0)
        {
            // Device connected
            var existingDevice = FindDevice(controller, port);
            if (existingDevice == null)
            {
                EnumerateDevice(controller, port);
            }
        }
        else
        {
            // Device disconnected
            var device = FindDevice(controller, port);
            if (device != null)
            {
                RemoveDevice(device);
            }
        }
    }

    /// <summary>
    /// Find a device by controller and port.
    /// </summary>
    private static USBDevice? FindDevice(IUSBHostController controller, int port)
    {
        foreach (var device in _devices)
        {
            if (device.Controller == controller && device.Port == port)
                return device;
        }
        return null;
    }

    /// <summary>
    /// Enumerate a newly connected device.
    /// </summary>
    private static void EnumerateDevice(IUSBHostController controller, int port)
    {
        // Reset the port
        if (!controller.ResetPort(port))
            return;

        // Get device speed
        var speed = controller.GetPortSpeed(port);

        // Create device object
        var device = new USBDevice(controller, port, speed);

        // Allocate address
        device.Address = controller.AllocateDeviceAddress();
        if (device.Address == 0)
            return;

        // Read device descriptor
        if (!ReadDeviceDescriptor(device))
        {
            controller.FreeDeviceAddress(device.Address);
            return;
        }

        // Read string descriptors
        ReadStringDescriptors(device);

        // Add to list
        _devices.Add(device);

        // Try to bind a driver
        TryBindDriver(device);

        // Fire event
        DeviceConnected?.Invoke(device, true);
    }

    /// <summary>
    /// Remove a disconnected device.
    /// </summary>
    private static void RemoveDevice(USBDevice device)
    {
        // Shutdown driver if bound
        if (device.Driver != null)
        {
            device.Driver.Shutdown();
            device.Driver = null;
        }

        // Free address
        device.Controller.FreeDeviceAddress(device.Address);

        // Remove from list
        _devices.Remove(device);

        // Fire event
        DeviceDisconnected?.Invoke(device, false);
    }

    /// <summary>
    /// Read the device descriptor.
    /// </summary>
    private static unsafe bool ReadDeviceDescriptor(USBDevice device)
    {
        var setup = new USBSetupPacket(
            USBRequestType.DeviceToHost | USBRequestType.Standard | USBRequestType.Device,
            USBRequest.GetDescriptor,
            (ushort)((byte)USBDescriptorType.Device << 8),
            0,
            (ushort)sizeof(USBDeviceDescriptor));

        fixed (USBDeviceDescriptor* desc = &device.DeviceDescriptor)
        {
            var result = device.Controller.ControlTransfer(
                device.Address,
                setup,
                (byte*)desc,
                sizeof(USBDeviceDescriptor),
                out int transferred);

            return result == USBTransferResult.Success && transferred >= 8;
        }
    }

    /// <summary>
    /// Read string descriptors (manufacturer, product, serial).
    /// </summary>
    private static void ReadStringDescriptors(USBDevice device)
    {
        // TODO: Implement string descriptor reading
        // For now, leave strings as null
    }

    /// <summary>
    /// Try to bind a class driver to a device.
    /// </summary>
    private static void TryBindDriver(USBDevice device)
    {
        foreach (var driver in _classDrivers)
        {
            // Get USB device attributes from the driver
            var attrs = driver.GetType().GetCustomAttributes(typeof(UsbDeviceAttribute), false);
            foreach (UsbDeviceAttribute attr in attrs)
            {
                bool matches = true;

                // Check vendor/product
                if (attr.VendorId != 0xFFFF && attr.VendorId != device.VendorId)
                    matches = false;
                if (attr.ProductId != 0xFFFF && attr.ProductId != device.ProductId)
                    matches = false;

                // Check class/subclass/protocol
                if (attr.DeviceClass != 0xFF && attr.DeviceClass != device.DeviceClass)
                    matches = false;
                if (attr.DeviceSubclass != 0xFF && attr.DeviceSubclass != device.DeviceSubclass)
                    matches = false;
                if (attr.DeviceProtocol != 0xFF && attr.DeviceProtocol != device.DeviceProtocol)
                    matches = false;

                if (matches)
                {
                    device.Driver = driver;
                    // TODO: Call driver initialization for this device
                    return;
                }
            }
        }
    }
}
