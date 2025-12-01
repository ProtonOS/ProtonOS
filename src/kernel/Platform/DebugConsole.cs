// ProtonOS kernel - Debug console output (COM1 serial)

using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

/// <summary>
/// Early debug output via COM1 serial port.
/// Used for kernel debugging before proper console/logging is available.
/// </summary>
public static unsafe class DebugConsole
{
    // COM1 port addresses
    private const ushort COM1 = 0x3F8;
    private const ushort COM1_DATA = COM1 + 0;      // Data register
    private const ushort COM1_IER = COM1 + 1;       // Interrupt Enable Register
    private const ushort COM1_FCR = COM1 + 2;       // FIFO Control Register
    private const ushort COM1_LCR = COM1 + 3;       // Line Control Register
    private const ushort COM1_MCR = COM1 + 4;       // Modem Control Register
    private const ushort COM1_LSR = COM1 + 5;       // Line Status Register
    private const ushort COM1_DLL = COM1 + 0;       // Divisor Latch Low (DLAB=1)
    private const ushort COM1_DLH = COM1 + 1;       // Divisor Latch High (DLAB=1)

    // Import nernel port I/O functions
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void outb(ushort port, byte value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte inb(ushort port);

    /// <summary>
    /// Initialize COM1 at 115200 baud, 8N1
    /// </summary>
    public static void Init()
    {
        // Disable interrupts
        outb(COM1_IER, 0x00);

        // Enable DLAB (set baud rate divisor)
        outb(COM1_LCR, 0x80);

        // Set divisor to 1 (115200 baud)
        outb(COM1_DLL, 0x01);
        outb(COM1_DLH, 0x00);

        // 8 bits, no parity, one stop bit (8N1), disable DLAB
        outb(COM1_LCR, 0x03);

        // Enable FIFO, clear them, 14-byte threshold
        outb(COM1_FCR, 0xC7);

        // Enable DTR, RTS, OUT2
        outb(COM1_MCR, 0x0B);
    }

    /// <summary>
    /// Write a single byte
    /// </summary>
    public static void WriteByte(byte b)
    {
        // Wait for transmit buffer empty
        while ((inb(COM1_LSR) & 0x20) == 0) { }
        outb(COM1_DATA, b);
    }

    /// <summary>
    /// Write a string
    /// </summary>
    public static void Write(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            WriteByte((byte)s[i]);
        }
    }

    /// <summary>
    /// Write a string followed by newline
    /// </summary>
    public static void WriteLine(string s)
    {
        Write(s);
        WriteLine();
    }

    /// <summary>
    /// Write a newline (CR+LF)
    /// </summary>
    public static void WriteLine()
    {
        WriteByte(0x0D);  // CR
        WriteByte(0x0A);  // LF
    }

    /// <summary>
    /// Write a 64-bit value as hexadecimal
    /// </summary>
    public static void WriteHex(ulong value)
    {
        // Write digits from most significant to least
        for (int i = 60; i >= 0; i -= 4)
        {
            int nibble = (int)((value >> i) & 0xF);
            byte c = (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
            WriteByte(c);
        }
    }

    /// <summary>
    /// Write a 32-bit value as hexadecimal
    /// </summary>
    public static void WriteHex(uint value)
    {
        for (int i = 28; i >= 0; i -= 4)
        {
            int nibble = (int)((value >> i) & 0xF);
            byte c = (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
            WriteByte(c);
        }
    }

    /// <summary>
    /// Write a 16-bit value as hexadecimal
    /// </summary>
    public static void WriteHex(ushort value)
    {
        for (int i = 12; i >= 0; i -= 4)
        {
            int nibble = (value >> i) & 0xF;
            byte c = (byte)(nibble < 10 ? '0' + nibble : 'A' + nibble - 10);
            WriteByte(c);
        }
    }

    /// <summary>
    /// Write a signed integer as decimal
    /// </summary>
    public static void WriteDecimal(int value)
    {
        if (value == 0)
        {
            WriteByte((byte)'0');
            return;
        }

        if (value < 0)
        {
            WriteByte((byte)'-');
            value = -value;
        }

        WriteDecimal((uint)value);
    }

    /// <summary>
    /// Write an unsigned integer as decimal
    /// </summary>
    public static void WriteDecimal(uint value)
    {
        if (value == 0)
        {
            WriteByte((byte)'0');
            return;
        }

        // Find the highest power of 10 <= value
        uint divisor = 1;
        uint temp = value;
        while (temp >= 10)
        {
            divisor *= 10;
            temp /= 10;
        }

        // Write digits from most significant to least
        while (divisor > 0)
        {
            uint digit = value / divisor;
            WriteByte((byte)('0' + digit));
            value %= divisor;
            divisor /= 10;
        }
    }

    /// <summary>
    /// Write an unsigned 64-bit integer as decimal
    /// </summary>
    public static void WriteDecimal(ulong value)
    {
        if (value == 0)
        {
            WriteByte((byte)'0');
            return;
        }

        // Find the highest power of 10 <= value
        ulong divisor = 1;
        ulong temp = value;
        while (temp >= 10)
        {
            divisor *= 10;
            temp /= 10;
        }

        // Write digits from most significant to least
        while (divisor > 0)
        {
            ulong digit = value / divisor;
            WriteByte((byte)('0' + digit));
            value %= divisor;
            divisor /= 10;
        }
    }

    /// <summary>
    /// Write a decimal number with zero-padding to specified width
    /// </summary>
    public static void WriteDecimalPadded(int value, int width)
    {
        // Calculate number of digits
        int digits = 1;
        int temp = value;
        if (temp < 0) temp = -temp;
        while (temp >= 10)
        {
            digits++;
            temp /= 10;
        }

        // Add padding zeros
        for (int i = digits; i < width; i++)
        {
            WriteByte((byte)'0');
        }

        WriteDecimal(value);
    }
}
