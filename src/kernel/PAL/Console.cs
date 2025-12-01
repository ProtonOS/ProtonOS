// ProtonOS kernel - PAL Console APIs
// Win32-compatible console I/O APIs for PAL compatibility.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.PAL;

/// <summary>
/// Standard handle types for GetStdHandle.
/// </summary>
public static class StdHandle
{
    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;
}

/// <summary>
/// Pseudo-handle values for standard I/O.
/// In ProtonOS, these map to the debug console (COM1 serial port).
/// </summary>
public static class ConsoleHandles
{
    // Use negative values to distinguish from regular handles
    public static readonly nuint StdIn = unchecked((nuint)(nint)(-10));
    public static readonly nuint StdOut = unchecked((nuint)(nint)(-11));
    public static readonly nuint StdErr = unchecked((nuint)(nint)(-12));
    public static readonly nuint InvalidHandle = unchecked((nuint)(nint)(-1));
}

/// <summary>
/// PAL Console APIs - Win32-compatible console I/O functions.
/// In ProtonOS, console output goes to the debug console (COM1 serial port).
/// </summary>
public static unsafe class Console
{
    /// <summary>
    /// Get a handle to the specified standard device.
    /// </summary>
    /// <param name="nStdHandle">Standard device identifier (STD_INPUT_HANDLE, STD_OUTPUT_HANDLE, STD_ERROR_HANDLE)</param>
    /// <returns>Handle to the device, or INVALID_HANDLE_VALUE on failure</returns>
    public static nuint GetStdHandle(int nStdHandle)
    {
        return nStdHandle switch
        {
            StdHandle.STD_INPUT_HANDLE => ConsoleHandles.StdIn,
            StdHandle.STD_OUTPUT_HANDLE => ConsoleHandles.StdOut,
            StdHandle.STD_ERROR_HANDLE => ConsoleHandles.StdErr,
            _ => ConsoleHandles.InvalidHandle,
        };
    }

    /// <summary>
    /// Write data to a file or device.
    /// For stdout/stderr, writes to the debug console (serial port).
    /// </summary>
    /// <param name="hFile">Handle to write to</param>
    /// <param name="lpBuffer">Data to write</param>
    /// <param name="nNumberOfBytesToWrite">Number of bytes to write</param>
    /// <param name="lpNumberOfBytesWritten">Receives number of bytes actually written</param>
    /// <param name="lpOverlapped">Overlapped structure (ignored)</param>
    /// <returns>True on success</returns>
    public static bool WriteFile(
        nuint hFile,
        void* lpBuffer,
        uint nNumberOfBytesToWrite,
        uint* lpNumberOfBytesWritten,
        void* lpOverlapped)
    {
        if (lpBuffer == null || nNumberOfBytesToWrite == 0)
        {
            if (lpNumberOfBytesWritten != null)
                *lpNumberOfBytesWritten = 0;
            return true; // Writing 0 bytes is always successful
        }

        // Check if this is a console handle
        bool isConsole = hFile == ConsoleHandles.StdOut ||
                         hFile == ConsoleHandles.StdErr ||
                         hFile == ConsoleHandles.StdIn;

        if (isConsole)
        {
            // Write to debug console (serial port)
            byte* buffer = (byte*)lpBuffer;
            for (uint i = 0; i < nNumberOfBytesToWrite; i++)
            {
                DebugConsole.WriteByte(buffer[i]);
            }

            if (lpNumberOfBytesWritten != null)
                *lpNumberOfBytesWritten = nNumberOfBytesToWrite;

            return true;
        }

        // Not a console handle - not supported yet
        if (lpNumberOfBytesWritten != null)
            *lpNumberOfBytesWritten = 0;
        return false;
    }

    /// <summary>
    /// Read data from a file or device.
    /// Currently not supported (returns failure).
    /// </summary>
    public static bool ReadFile(
        nuint hFile,
        void* lpBuffer,
        uint nNumberOfBytesToRead,
        uint* lpNumberOfBytesRead,
        void* lpOverlapped)
    {
        // Console input not implemented yet
        if (lpNumberOfBytesRead != null)
            *lpNumberOfBytesRead = 0;
        return false;
    }

    /// <summary>
    /// Get console mode (stub - returns success with no flags).
    /// </summary>
    public static bool GetConsoleMode(nuint hConsoleHandle, uint* lpMode)
    {
        if (lpMode == null)
            return false;

        // Return 0 (no special modes enabled)
        *lpMode = 0;
        return true;
    }

    /// <summary>
    /// Set console mode (stub - always returns success).
    /// </summary>
    public static bool SetConsoleMode(nuint hConsoleHandle, uint dwMode)
    {
        // Ignore mode changes
        return true;
    }

    /// <summary>
    /// Check if handle refers to a console.
    /// </summary>
    public static bool IsConsoleHandle(nuint handle)
    {
        return handle == ConsoleHandles.StdIn ||
               handle == ConsoleHandles.StdOut ||
               handle == ConsoleHandles.StdErr;
    }

    /// <summary>
    /// Get console output code page (stub - returns UTF-8).
    /// </summary>
    public static uint GetConsoleOutputCP()
    {
        return 65001; // UTF-8
    }

    /// <summary>
    /// Set console output code page (stub - always returns success).
    /// </summary>
    public static bool SetConsoleOutputCP(uint wCodePageID)
    {
        return true;
    }
}
