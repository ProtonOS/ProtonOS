// ProtonOS DDK - Serial Port Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Serial;

/// <summary>
/// Serial port baud rates.
/// </summary>
public enum BaudRate
{
    B300 = 300,
    B1200 = 1200,
    B2400 = 2400,
    B4800 = 4800,
    B9600 = 9600,
    B19200 = 19200,
    B38400 = 38400,
    B57600 = 57600,
    B115200 = 115200,
    B230400 = 230400,
    B460800 = 460800,
    B921600 = 921600,
}

/// <summary>
/// Data bits per character.
/// </summary>
public enum DataBits
{
    Five = 5,
    Six = 6,
    Seven = 7,
    Eight = 8,
}

/// <summary>
/// Parity mode.
/// </summary>
public enum Parity
{
    None,
    Odd,
    Even,
    Mark,
    Space,
}

/// <summary>
/// Stop bits.
/// </summary>
public enum StopBits
{
    One,
    OnePointFive,
    Two,
}

/// <summary>
/// Flow control mode.
/// </summary>
public enum FlowControl
{
    None,
    Hardware,   // RTS/CTS
    Software,   // XON/XOFF
}

/// <summary>
/// Serial port configuration.
/// </summary>
public struct SerialConfig
{
    /// <summary>Baud rate.</summary>
    public BaudRate BaudRate;

    /// <summary>Data bits per character.</summary>
    public DataBits DataBits;

    /// <summary>Parity mode.</summary>
    public Parity Parity;

    /// <summary>Stop bits.</summary>
    public StopBits StopBits;

    /// <summary>Flow control mode.</summary>
    public FlowControl FlowControl;

    /// <summary>Default configuration (115200 8N1).</summary>
    public static SerialConfig Default => new()
    {
        BaudRate = BaudRate.B115200,
        DataBits = DataBits.Eight,
        Parity = Parity.None,
        StopBits = StopBits.One,
        FlowControl = FlowControl.None,
    };
}

/// <summary>
/// Modem control lines.
/// </summary>
[Flags]
public enum ModemLines
{
    None = 0,
    DTR = 1 << 0,   // Data Terminal Ready (output)
    RTS = 1 << 1,   // Request To Send (output)
    CTS = 1 << 2,   // Clear To Send (input)
    DSR = 1 << 3,   // Data Set Ready (input)
    DCD = 1 << 4,   // Data Carrier Detect (input)
    RI = 1 << 5,    // Ring Indicator (input)
}

/// <summary>
/// Delegate for serial data received callback.
/// </summary>
public unsafe delegate void SerialDataCallback(byte* data, int length);

/// <summary>
/// Interface for serial port drivers.
/// </summary>
public unsafe interface ISerialPort : IDriver
{
    /// <summary>
    /// Port name (e.g., "COM1", "ttyS0").
    /// </summary>
    string PortName { get; }

    /// <summary>
    /// Base I/O address (for x86 ports).
    /// </summary>
    ushort IoAddress { get; }

    /// <summary>
    /// IRQ number.
    /// </summary>
    int Irq { get; }

    /// <summary>
    /// Current configuration.
    /// </summary>
    SerialConfig Config { get; }

    /// <summary>
    /// True if the port is open.
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// Open the port with the specified configuration.
    /// </summary>
    bool Open(SerialConfig config);

    /// <summary>
    /// Close the port.
    /// </summary>
    void Close();

    /// <summary>
    /// Write data to the port.
    /// </summary>
    /// <param name="data">Data to write</param>
    /// <param name="length">Number of bytes to write</param>
    /// <returns>Number of bytes written</returns>
    int Write(byte* data, int length);

    /// <summary>
    /// Read data from the port.
    /// </summary>
    /// <param name="buffer">Buffer to receive data</param>
    /// <param name="maxLength">Maximum bytes to read</param>
    /// <returns>Number of bytes read</returns>
    int Read(byte* buffer, int maxLength);

    /// <summary>
    /// Write a single byte.
    /// </summary>
    void WriteByte(byte b);

    /// <summary>
    /// Read a single byte.
    /// </summary>
    /// <returns>Byte read, or -1 if no data available</returns>
    int ReadByte();

    /// <summary>
    /// Check if data is available to read.
    /// </summary>
    bool DataAvailable { get; }

    /// <summary>
    /// Check if transmit buffer is empty.
    /// </summary>
    bool TransmitEmpty { get; }

    /// <summary>
    /// Flush transmit buffer.
    /// </summary>
    void Flush();

    /// <summary>
    /// Clear receive and transmit buffers.
    /// </summary>
    void Clear();

    /// <summary>
    /// Get modem control line states.
    /// </summary>
    ModemLines GetModemLines();

    /// <summary>
    /// Set modem control lines (DTR, RTS).
    /// </summary>
    void SetModemLines(ModemLines lines);

    /// <summary>
    /// Send a break signal.
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds</param>
    void SendBreak(int durationMs);

    /// <summary>
    /// Set callback for received data.
    /// Called from interrupt context.
    /// </summary>
    void SetReceiveCallback(SerialDataCallback? callback);
}

/// <summary>
/// Serial port manager.
/// </summary>
public static class SerialManager
{
    private static System.Collections.Generic.List<ISerialPort> _ports = new();

    /// <summary>
    /// All registered serial ports.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<ISerialPort> Ports => _ports;

    /// <summary>
    /// Register a serial port.
    /// </summary>
    public static void RegisterPort(ISerialPort port)
    {
        _ports.Add(port);
    }

    /// <summary>
    /// Unregister a serial port.
    /// </summary>
    public static void UnregisterPort(ISerialPort port)
    {
        _ports.Remove(port);
    }

    /// <summary>
    /// Find a port by name.
    /// </summary>
    public static ISerialPort? FindPort(string name)
    {
        foreach (var port in _ports)
        {
            if (port.PortName == name)
                return port;
        }
        return null;
    }

    /// <summary>
    /// Get the first available port.
    /// </summary>
    public static ISerialPort? GetDefaultPort()
    {
        return _ports.Count > 0 ? _ports[0] : null;
    }
}
