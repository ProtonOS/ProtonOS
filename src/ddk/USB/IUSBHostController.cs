// ProtonOS DDK - USB Host Controller Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.USB;

/// <summary>
/// USB port status.
/// </summary>
[Flags]
public enum USBPortStatus
{
    None = 0,
    Connected = 1 << 0,
    Enabled = 1 << 1,
    Suspended = 1 << 2,
    OverCurrent = 1 << 3,
    Reset = 1 << 4,
    Power = 1 << 8,
    LowSpeed = 1 << 9,
    HighSpeed = 1 << 10,
}

/// <summary>
/// USB transfer result.
/// </summary>
public enum USBTransferResult
{
    Success = 0,
    Stall = -1,
    BufferError = -2,
    BabbleDetected = -3,
    NAKReceived = -4,
    CRCError = -5,
    BitstuffError = -6,
    Timeout = -7,
    NotReady = -8,
    InvalidParameter = -9,
    NoDevice = -10,
    ShortPacket = 1,  // Positive = success with short packet
}

/// <summary>
/// Callback for completed USB transfers.
/// </summary>
/// <param name="result">Transfer result</param>
/// <param name="bytesTransferred">Number of bytes actually transferred</param>
/// <param name="context">User-provided context</param>
public delegate void USBTransferCallback(USBTransferResult result, int bytesTransferred, object? context);

/// <summary>
/// USB transfer request.
/// </summary>
public unsafe class USBTransferRequest
{
    /// <summary>Device address (1-127).</summary>
    public byte DeviceAddress;

    /// <summary>Endpoint number (0-15).</summary>
    public byte Endpoint;

    /// <summary>Transfer type.</summary>
    public USBTransferType TransferType;

    /// <summary>Direction.</summary>
    public USBDirection Direction;

    /// <summary>Data buffer.</summary>
    public byte* Buffer;

    /// <summary>Buffer length.</summary>
    public int Length;

    /// <summary>Setup packet for control transfers.</summary>
    public USBSetupPacket? SetupPacket;

    /// <summary>Completion callback.</summary>
    public USBTransferCallback? Callback;

    /// <summary>User context for callback.</summary>
    public object? Context;

    /// <summary>Maximum packet size for this endpoint.</summary>
    public ushort MaxPacketSize = 64;
}

/// <summary>
/// Interface for USB host controller drivers (xHCI, EHCI, OHCI, UHCI).
/// </summary>
public interface IUSBHostController : IDriver
{
    /// <summary>
    /// Controller type.
    /// </summary>
    USBControllerType Type { get; }

    /// <summary>
    /// Number of root hub ports.
    /// </summary>
    int PortCount { get; }

    /// <summary>
    /// Maximum supported USB speed.
    /// </summary>
    USBSpeed MaxSpeed { get; }

    /// <summary>
    /// Get the status of a root hub port.
    /// </summary>
    /// <param name="port">Port number (0-based)</param>
    USBPortStatus GetPortStatus(int port);

    /// <summary>
    /// Reset a root hub port.
    /// </summary>
    /// <param name="port">Port number (0-based)</param>
    /// <returns>true if reset succeeded</returns>
    bool ResetPort(int port);

    /// <summary>
    /// Enable/disable a root hub port.
    /// </summary>
    void SetPortEnabled(int port, bool enabled);

    /// <summary>
    /// Get the speed of a device connected to a port.
    /// </summary>
    USBSpeed GetPortSpeed(int port);

    /// <summary>
    /// Allocate a device address for a new device.
    /// </summary>
    /// <returns>Device address (1-127), or 0 on failure</returns>
    byte AllocateDeviceAddress();

    /// <summary>
    /// Free a device address when device is removed.
    /// </summary>
    void FreeDeviceAddress(byte address);

    /// <summary>
    /// Submit a transfer request.
    /// </summary>
    /// <param name="request">Transfer request</param>
    /// <returns>Transfer result (may be pending if async)</returns>
    USBTransferResult SubmitTransfer(USBTransferRequest request);

    /// <summary>
    /// Cancel a pending transfer.
    /// </summary>
    void CancelTransfer(USBTransferRequest request);

    /// <summary>
    /// Perform a synchronous control transfer.
    /// </summary>
    unsafe USBTransferResult ControlTransfer(
        byte deviceAddress,
        USBSetupPacket setup,
        byte* buffer,
        int length,
        out int bytesTransferred);

    /// <summary>
    /// Perform a synchronous bulk transfer.
    /// </summary>
    unsafe USBTransferResult BulkTransfer(
        byte deviceAddress,
        byte endpoint,
        USBDirection direction,
        byte* buffer,
        int length,
        out int bytesTransferred);

    /// <summary>
    /// Perform a synchronous interrupt transfer.
    /// </summary>
    unsafe USBTransferResult InterruptTransfer(
        byte deviceAddress,
        byte endpoint,
        USBDirection direction,
        byte* buffer,
        int length,
        out int bytesTransferred);

    /// <summary>
    /// Set the callback for port status changes.
    /// </summary>
    void SetPortChangeCallback(Action<int>? callback);
}

/// <summary>
/// Callback for device connect/disconnect events.
/// </summary>
public delegate void USBDeviceEventCallback(USBDevice device, bool connected);

/// <summary>
/// USB device representation.
/// </summary>
public class USBDevice
{
    /// <summary>Host controller this device is connected to.</summary>
    public IUSBHostController Controller { get; }

    /// <summary>Device address (1-127).</summary>
    public byte Address { get; internal set; }

    /// <summary>Root hub port number.</summary>
    public int Port { get; }

    /// <summary>Device speed.</summary>
    public USBSpeed Speed { get; }

    /// <summary>Device descriptor.</summary>
    public USBDeviceDescriptor DeviceDescriptor;

    /// <summary>Current configuration.</summary>
    public byte Configuration { get; internal set; }

    /// <summary>Vendor ID.</summary>
    public ushort VendorId => DeviceDescriptor.idVendor;

    /// <summary>Product ID.</summary>
    public ushort ProductId => DeviceDescriptor.idProduct;

    /// <summary>Device class.</summary>
    public byte DeviceClass => DeviceDescriptor.bDeviceClass;

    /// <summary>Device subclass.</summary>
    public byte DeviceSubclass => DeviceDescriptor.bDeviceSubClass;

    /// <summary>Device protocol.</summary>
    public byte DeviceProtocol => DeviceDescriptor.bDeviceProtocol;

    /// <summary>Manufacturer string (if available).</summary>
    public string? Manufacturer { get; internal set; }

    /// <summary>Product string (if available).</summary>
    public string? Product { get; internal set; }

    /// <summary>Serial number string (if available).</summary>
    public string? SerialNumber { get; internal set; }

    /// <summary>Driver bound to this device.</summary>
    public IDriver? Driver { get; internal set; }

    public USBDevice(IUSBHostController controller, int port, USBSpeed speed)
    {
        Controller = controller;
        Port = port;
        Speed = speed;
    }

    public override string ToString()
    {
        return $"USB {VendorId:X4}:{ProductId:X4} @ {Address}";
    }
}
