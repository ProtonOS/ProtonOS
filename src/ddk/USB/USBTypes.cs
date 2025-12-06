// ProtonOS DDK - USB Type Definitions

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.USB;

/// <summary>
/// USB speed classification.
/// </summary>
public enum USBSpeed
{
    Unknown = 0,
    Low = 1,       // 1.5 Mbps (USB 1.0)
    Full = 2,      // 12 Mbps (USB 1.1)
    High = 3,      // 480 Mbps (USB 2.0)
    Super = 4,     // 5 Gbps (USB 3.0)
    SuperPlus = 5, // 10 Gbps (USB 3.1)
    Super20 = 6,   // 20 Gbps (USB 3.2)
}

/// <summary>
/// USB controller type.
/// </summary>
public enum USBControllerType
{
    Unknown = 0,
    UHCI,  // USB 1.0 - Intel
    OHCI,  // USB 1.1 - Compaq/Microsoft/National
    EHCI,  // USB 2.0
    XHCI,  // USB 3.x
}

/// <summary>
/// USB transfer type.
/// </summary>
public enum USBTransferType
{
    Control = 0,
    Isochronous = 1,
    Bulk = 2,
    Interrupt = 3,
}

/// <summary>
/// USB endpoint direction.
/// </summary>
public enum USBDirection
{
    Out = 0,  // Host to device
    In = 1,   // Device to host
}

/// <summary>
/// USB device class codes.
/// </summary>
public static class USBClass
{
    public const byte PerInterface = 0x00;     // Class defined per-interface
    public const byte Audio = 0x01;
    public const byte CDC = 0x02;              // Communications Device Class
    public const byte HID = 0x03;              // Human Interface Device
    public const byte Physical = 0x05;
    public const byte Image = 0x06;
    public const byte Printer = 0x07;
    public const byte MassStorage = 0x08;
    public const byte Hub = 0x09;
    public const byte CDCData = 0x0A;
    public const byte SmartCard = 0x0B;
    public const byte ContentSecurity = 0x0D;
    public const byte Video = 0x0E;
    public const byte PersonalHealthcare = 0x0F;
    public const byte AudioVideo = 0x10;
    public const byte Billboard = 0x11;
    public const byte TypeCBridge = 0x12;
    public const byte Diagnostic = 0xDC;
    public const byte WirelessController = 0xE0;
    public const byte Miscellaneous = 0xEF;
    public const byte ApplicationSpecific = 0xFE;
    public const byte VendorSpecific = 0xFF;
}

/// <summary>
/// USB HID subclass codes.
/// </summary>
public static class USBHIDSubclass
{
    public const byte None = 0x00;
    public const byte Boot = 0x01;
}

/// <summary>
/// USB HID protocol codes.
/// </summary>
public static class USBHIDProtocol
{
    public const byte None = 0x00;
    public const byte Keyboard = 0x01;
    public const byte Mouse = 0x02;
}

/// <summary>
/// USB request type.
/// </summary>
[Flags]
public enum USBRequestType : byte
{
    // Direction (bit 7)
    HostToDevice = 0x00,
    DeviceToHost = 0x80,

    // Type (bits 6:5)
    Standard = 0x00,
    Class = 0x20,
    Vendor = 0x40,

    // Recipient (bits 4:0)
    Device = 0x00,
    Interface = 0x01,
    Endpoint = 0x02,
    Other = 0x03,
}

/// <summary>
/// Standard USB requests.
/// </summary>
public enum USBRequest : byte
{
    GetStatus = 0,
    ClearFeature = 1,
    SetFeature = 3,
    SetAddress = 5,
    GetDescriptor = 6,
    SetDescriptor = 7,
    GetConfiguration = 8,
    SetConfiguration = 9,
    GetInterface = 10,
    SetInterface = 11,
    SynchFrame = 12,
}

/// <summary>
/// USB descriptor types.
/// </summary>
public enum USBDescriptorType : byte
{
    Device = 1,
    Configuration = 2,
    String = 3,
    Interface = 4,
    Endpoint = 5,
    DeviceQualifier = 6,
    OtherSpeedConfiguration = 7,
    InterfacePower = 8,
    OTG = 9,
    Debug = 10,
    InterfaceAssociation = 11,
    BOS = 15,
    DeviceCapability = 16,
    HID = 0x21,
    Report = 0x22,
    Physical = 0x23,
    Hub = 0x29,
    SuperSpeedHub = 0x2A,
    SSEndpointCompanion = 0x30,
}

/// <summary>
/// USB device descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct USBDeviceDescriptor
{
    public byte bLength;
    public byte bDescriptorType;
    public ushort bcdUSB;
    public byte bDeviceClass;
    public byte bDeviceSubClass;
    public byte bDeviceProtocol;
    public byte bMaxPacketSize0;
    public ushort idVendor;
    public ushort idProduct;
    public ushort bcdDevice;
    public byte iManufacturer;
    public byte iProduct;
    public byte iSerialNumber;
    public byte bNumConfigurations;
}

/// <summary>
/// USB configuration descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct USBConfigurationDescriptor
{
    public byte bLength;
    public byte bDescriptorType;
    public ushort wTotalLength;
    public byte bNumInterfaces;
    public byte bConfigurationValue;
    public byte iConfiguration;
    public byte bmAttributes;
    public byte bMaxPower;
}

/// <summary>
/// USB interface descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct USBInterfaceDescriptor
{
    public byte bLength;
    public byte bDescriptorType;
    public byte bInterfaceNumber;
    public byte bAlternateSetting;
    public byte bNumEndpoints;
    public byte bInterfaceClass;
    public byte bInterfaceSubClass;
    public byte bInterfaceProtocol;
    public byte iInterface;
}

/// <summary>
/// USB endpoint descriptor.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct USBEndpointDescriptor
{
    public byte bLength;
    public byte bDescriptorType;
    public byte bEndpointAddress;
    public byte bmAttributes;
    public ushort wMaxPacketSize;
    public byte bInterval;

    public int EndpointNumber => bEndpointAddress & 0x0F;
    public USBDirection Direction => (bEndpointAddress & 0x80) != 0 ? USBDirection.In : USBDirection.Out;
    public USBTransferType TransferType => (USBTransferType)(bmAttributes & 0x03);
}

/// <summary>
/// USB setup packet for control transfers.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct USBSetupPacket
{
    public byte bmRequestType;
    public byte bRequest;
    public ushort wValue;
    public ushort wIndex;
    public ushort wLength;

    public USBSetupPacket(USBRequestType requestType, USBRequest request, ushort value, ushort index, ushort length)
    {
        bmRequestType = (byte)requestType;
        bRequest = (byte)request;
        wValue = value;
        wIndex = index;
        wLength = length;
    }
}
