// netos mernel - PAL System APIs
// Win32-compatible system information, timing, and debug APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.X64;
using Kernel.Platform;

namespace Kernel.PAL;

/// <summary>
/// Processor architecture constants.
/// </summary>
public static class ProcessorArchitecture
{
    public const ushort PROCESSOR_ARCHITECTURE_AMD64 = 9;
    public const ushort PROCESSOR_ARCHITECTURE_INTEL = 0;
    public const ushort PROCESSOR_ARCHITECTURE_ARM = 5;
    public const ushort PROCESSOR_ARCHITECTURE_ARM64 = 12;
    public const ushort PROCESSOR_ARCHITECTURE_UNKNOWN = 0xFFFF;
}

/// <summary>
/// SYSTEM_INFO structure - compatible with Win32 SYSTEM_INFO.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SystemInfo
{
    public ushort wProcessorArchitecture;
    public ushort wReserved;
    public uint dwPageSize;
    public nuint lpMinimumApplicationAddress;
    public nuint lpMaximumApplicationAddress;
    public nuint dwActiveProcessorMask;
    public uint dwNumberOfProcessors;
    public uint dwProcessorType;
    public uint dwAllocationGranularity;
    public ushort wProcessorLevel;
    public ushort wProcessorRevision;
}

/// <summary>
/// PAL System APIs - Win32-compatible system information and timing functions.
/// </summary>
public static unsafe class SystemApi
{
    // Processor type constants (legacy, but kept for compatibility)
    private const uint PROCESSOR_AMD_X8664 = 8664;

    /// <summary>
    /// Query the high-resolution performance counter.
    /// Returns true on success, with the counter value in lpPerformanceCount.
    /// </summary>
    public static bool QueryPerformanceCounter(out long lpPerformanceCount)
    {
        if (!Hpet.IsInitialized)
        {
            lpPerformanceCount = 0;
            return false;
        }

        lpPerformanceCount = (long)Hpet.ReadCounter();
        return true;
    }

    /// <summary>
    /// Query the frequency of the high-resolution performance counter.
    /// Returns true on success, with the frequency (counts per second) in lpFrequency.
    /// </summary>
    public static bool QueryPerformanceFrequency(out long lpFrequency)
    {
        if (!Hpet.IsInitialized)
        {
            lpFrequency = 0;
            return false;
        }

        lpFrequency = (long)Hpet.FrequencyHz;
        return true;
    }

    /// <summary>
    /// Get system information.
    /// Fills in the SYSTEM_INFO structure with information about the current system.
    /// </summary>
    public static void GetSystemInfo(out SystemInfo lpSystemInfo)
    {
        lpSystemInfo = new SystemInfo();

        // Processor architecture
#if ARCH_X64
        lpSystemInfo.wProcessorArchitecture = ProcessorArchitecture.PROCESSOR_ARCHITECTURE_AMD64;
        lpSystemInfo.dwProcessorType = PROCESSOR_AMD_X8664;
        lpSystemInfo.wProcessorLevel = 6;  // Typical for modern x64
        lpSystemInfo.wProcessorRevision = 0;
#elif ARCH_ARM64
        lpSystemInfo.wProcessorArchitecture = ProcessorArchitecture.PROCESSOR_ARCHITECTURE_ARM64;
        lpSystemInfo.dwProcessorType = 0;
        lpSystemInfo.wProcessorLevel = 0;
        lpSystemInfo.wProcessorRevision = 0;
#else
        lpSystemInfo.wProcessorArchitecture = ProcessorArchitecture.PROCESSOR_ARCHITECTURE_UNKNOWN;
        lpSystemInfo.dwProcessorType = 0;
        lpSystemInfo.wProcessorLevel = 0;
        lpSystemInfo.wProcessorRevision = 0;
#endif

        // Page size
        lpSystemInfo.dwPageSize = (uint)VirtualMemory.PageSize;

        // Allocation granularity (typically 64KB on Windows, but we use page size)
        // JIT typically uses this for aligning large allocations
        lpSystemInfo.dwAllocationGranularity = 65536;  // 64KB

        // Address range
        // Leave a null guard page at 0, kernel space starts low
        lpSystemInfo.lpMinimumApplicationAddress = (nuint)VirtualMemory.PageSize;
        // User space up to 128TB (typical for x64 Windows)
        lpSystemInfo.lpMaximumApplicationAddress = (nuint)0x00007FFFFFFFFFFF;

        // Number of processors (single CPU for now)
        // TODO: Get actual CPU count from ACPI/MADT when SMP is implemented
        lpSystemInfo.dwNumberOfProcessors = 1;
        lpSystemInfo.dwActiveProcessorMask = 1;  // CPU 0 is active
    }

    /// <summary>
    /// Get native system information.
    /// On x64, this is the same as GetSystemInfo since there's no WOW64.
    /// </summary>
    public static void GetNativeSystemInfo(out SystemInfo lpSystemInfo)
    {
        GetSystemInfo(out lpSystemInfo);
    }

    /// <summary>
    /// Get the current tick count in milliseconds.
    /// This is a lower-precision timer compared to QueryPerformanceCounter.
    /// </summary>
    public static uint GetTickCount()
    {
        if (!Hpet.IsInitialized)
            return 0;

        ulong ticks = Hpet.ReadCounter();
        ulong ms = Hpet.TicksToNanoseconds(ticks) / 1_000_000;
        return (uint)ms;
    }

    /// <summary>
    /// Get the current tick count in milliseconds as a 64-bit value.
    /// Won't wrap around like the 32-bit version.
    /// </summary>
    public static ulong GetTickCount64()
    {
        if (!Hpet.IsInitialized)
            return 0;

        ulong ticks = Hpet.ReadCounter();
        return Hpet.TicksToNanoseconds(ticks) / 1_000_000;
    }

    /// <summary>
    /// Get current system time as FILETIME (100-nanosecond intervals since 1601-01-01).
    /// This is the primary PAL API for wall-clock time.
    /// </summary>
    /// <param name="lpSystemTimeAsFileTime">Pointer to FILETIME structure to receive the time</param>
    public static void GetSystemTimeAsFileTime(FileTime* lpSystemTimeAsFileTime)
    {
        if (lpSystemTimeAsFileTime == null)
            return;

        ulong ft = Rtc.GetSystemTimeAsFileTime();
        lpSystemTimeAsFileTime->dwLowDateTime = (uint)(ft & 0xFFFFFFFF);
        lpSystemTimeAsFileTime->dwHighDateTime = (uint)(ft >> 32);
    }

    /// <summary>
    /// Get current system time as SYSTEMTIME structure.
    /// </summary>
    /// <param name="lpSystemTime">Pointer to SYSTEMTIME structure to receive the time</param>
    public static void GetSystemTime(SystemTime* lpSystemTime)
    {
        if (lpSystemTime == null)
            return;

        Rtc.GetSystemTime(out int year, out int month, out int day,
                          out int hour, out int minute, out int second,
                          out int millisecond);

        lpSystemTime->wYear = (ushort)year;
        lpSystemTime->wMonth = (ushort)month;
        lpSystemTime->wDayOfWeek = 0;  // TODO: Calculate day of week
        lpSystemTime->wDay = (ushort)day;
        lpSystemTime->wHour = (ushort)hour;
        lpSystemTime->wMinute = (ushort)minute;
        lpSystemTime->wSecond = (ushort)second;
        lpSystemTime->wMilliseconds = (ushort)millisecond;
    }
}

/// <summary>
/// FILETIME structure - Win32-compatible 64-bit time value.
/// Represents 100-nanosecond intervals since January 1, 1601 (UTC).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct FileTime
{
    public uint dwLowDateTime;
    public uint dwHighDateTime;

    /// <summary>
    /// Convert to 64-bit value
    /// </summary>
    public ulong ToUInt64()
    {
        return ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }
}

/// <summary>
/// SYSTEMTIME structure - Win32-compatible broken-down time.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct SystemTime
{
    public ushort wYear;
    public ushort wMonth;
    public ushort wDayOfWeek;
    public ushort wDay;
    public ushort wHour;
    public ushort wMinute;
    public ushort wSecond;
    public ushort wMilliseconds;
}

/// <summary>
/// PAL Debug APIs - Win32-compatible debugging functions.
/// </summary>
public static unsafe class DebugApi
{
    /// <summary>
    /// Output a debug string (ANSI version).
    /// Sends the string to the debug console (serial port).
    /// </summary>
    /// <param name="lpOutputString">Pointer to null-terminated string to output</param>
    public static void OutputDebugStringA(byte* lpOutputString)
    {
        if (lpOutputString == null)
            return;

        // Output each character until null terminator
        byte* p = lpOutputString;
        while (*p != 0)
        {
            DebugConsole.WriteByte(*p);
            p++;
        }
    }

    /// <summary>
    /// Output a debug string (Wide/Unicode version).
    /// Sends the string to the debug console (serial port).
    /// Note: Unicode characters > 127 are output as '?'.
    /// </summary>
    /// <param name="lpOutputString">Pointer to null-terminated wide string to output</param>
    public static void OutputDebugStringW(char* lpOutputString)
    {
        if (lpOutputString == null)
            return;

        // Output each character until null terminator
        char* p = lpOutputString;
        while (*p != 0)
        {
            // Convert wide char to ASCII, replace non-ASCII with '?'
            char c = *p;
            if (c < 128)
                DebugConsole.WriteByte((byte)c);
            else
                DebugConsole.WriteByte((byte)'?');
            p++;
        }
    }

    /// <summary>
    /// Check if a debugger is attached to the current process.
    /// In our kernel, we always return false since we don't have a real debugger.
    /// Could be extended to check for a debug flag or GDB stub connection.
    /// </summary>
    /// <returns>True if debugger is present, false otherwise</returns>
    public static bool IsDebuggerPresent()
    {
        // No debugger attached in kernel mode (yet)
        // This could be extended to check for:
        // - A kernel debugger flag
        // - GDB stub connection over serial
        // - Debug registers (DR7) indicating debug breakpoints
        return false;
    }

    /// <summary>
    /// Trigger a debug break (INT 3 instruction).
    /// This is the same as Cpu.Breakpoint but provided for PAL compatibility.
    /// </summary>
    public static void DebugBreak()
    {
        Cpu.Breakpoint();
    }
}
