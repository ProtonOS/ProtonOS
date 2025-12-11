// ProtonOS kernel - RTC (Real-Time Clock) driver
// Reads the CMOS RTC to get wall-clock time at boot.
// Uses HPET to track elapsed time for ongoing timekeeping.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.X64;

/// <summary>
/// RTC (Real-Time Clock) driver for reading wall-clock time from CMOS.
/// The RTC provides calendar time (year, month, day, hour, minute, second).
/// </summary>
public static unsafe class RTC
{
    // CMOS/RTC I/O ports
    private const ushort CMOS_ADDRESS = 0x70;
    private const ushort CMOS_DATA = 0x71;

    // CMOS RTC register indices
    private const byte RTC_SECONDS = 0x00;
    private const byte RTC_MINUTES = 0x02;
    private const byte RTC_HOURS = 0x04;
    private const byte RTC_DAY_OF_WEEK = 0x06;
    private const byte RTC_DAY_OF_MONTH = 0x07;
    private const byte RTC_MONTH = 0x08;
    private const byte RTC_YEAR = 0x09;
    private const byte RTC_CENTURY = 0x32;  // May not be present on all systems
    private const byte RTC_STATUS_A = 0x0A;
    private const byte RTC_STATUS_B = 0x0B;

    // Status register A bit - update in progress
    private const byte RTC_UPDATE_IN_PROGRESS = 0x80;

    // Status register B bits
    private const byte RTC_24_HOUR_MODE = 0x02;
    private const byte RTC_BINARY_MODE = 0x04;

    // P/Invoke for port I/O
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void outb(ushort port, byte value);

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern byte inb(ushort port);

    // Boot time in 100-nanosecond intervals since January 1, 1601 (FILETIME epoch)
    private static ulong _bootTimeFileTime;

    // HPET counter value at boot (for calculating elapsed time)
    private static ulong _bootHpetTicks;

    private static bool _initialized;

    /// <summary>
    /// Whether RTC is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Boot time as FILETIME (100-nanosecond intervals since 1601-01-01)
    /// </summary>
    public static ulong BootTimeFileTime => _bootTimeFileTime;

    /// <summary>
    /// Initialize RTC by reading current time from CMOS.
    /// Should be called after HPET is initialized.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        DebugConsole.WriteLine("[RTC] Initializing...");

        // Read time from CMOS RTC
        int year, month, day, hour, minute, second;

        // Wait for any update to complete and read time
        if (!ReadRtcTime(out year, out month, out day, out hour, out minute, out second))
        {
            DebugConsole.WriteLine("[RTC] Failed to read time from CMOS!");
            // Use a default epoch time (2024-01-01 00:00:00)
            year = 2024;
            month = 1;
            day = 1;
            hour = 0;
            minute = 0;
            second = 0;
        }

        // Print the time we read
        DebugConsole.WriteLine(string.Format("[RTC] Time: {0}-{1}-{2} {3}:{4}:{5} UTC",
            year, month.ToString("D2", null), day.ToString("D2", null),
            hour.ToString("D2", null), minute.ToString("D2", null), second.ToString("D2", null)));

        // Convert to FILETIME
        _bootTimeFileTime = DateTimeToFileTime(year, month, day, hour, minute, second);

        // Record HPET ticks at boot time
        if (HPET.IsInitialized)
        {
            _bootHpetTicks = HPET.ReadCounter();
        }

        _initialized = true;
        DebugConsole.WriteLine("[RTC] Initialized");

        return true;
    }

    /// <summary>
    /// Read a CMOS register value
    /// </summary>
    private static byte ReadCmos(byte register)
    {
        // Disable NMI (bit 7 = 1) and select register
        outb(CMOS_ADDRESS, (byte)(0x80 | register));
        return inb(CMOS_DATA);
    }

    /// <summary>
    /// Check if RTC update is in progress
    /// </summary>
    private static bool IsUpdateInProgress()
    {
        return (ReadCmos(RTC_STATUS_A) & RTC_UPDATE_IN_PROGRESS) != 0;
    }

    /// <summary>
    /// Convert BCD to binary
    /// </summary>
    private static int BcdToBinary(byte bcd)
    {
        return (bcd & 0x0F) + ((bcd >> 4) * 10);
    }

    /// <summary>
    /// Read time from RTC, handling BCD conversion and 12/24 hour mode
    /// </summary>
    private static bool ReadRtcTime(out int year, out int month, out int day,
                                     out int hour, out int minute, out int second)
    {
        year = month = day = hour = minute = second = 0;

        // Wait for any update to complete (with timeout)
        int timeout = 10000;
        while (IsUpdateInProgress() && timeout-- > 0)
        {
            // Spin
        }

        if (timeout <= 0)
            return false;

        // Read status register B to determine format
        byte statusB = ReadCmos(RTC_STATUS_B);
        bool binaryMode = (statusB & RTC_BINARY_MODE) != 0;
        bool is24Hour = (statusB & RTC_24_HOUR_MODE) != 0;

        // Read all time values
        byte secRaw = ReadCmos(RTC_SECONDS);
        byte minRaw = ReadCmos(RTC_MINUTES);
        byte hourRaw = ReadCmos(RTC_HOURS);
        byte dayRaw = ReadCmos(RTC_DAY_OF_MONTH);
        byte monthRaw = ReadCmos(RTC_MONTH);
        byte yearRaw = ReadCmos(RTC_YEAR);

        // Try to read century register (not always present)
        byte centuryRaw = ReadCmos(RTC_CENTURY);

        // Convert from BCD if needed
        if (binaryMode)
        {
            second = secRaw;
            minute = minRaw;
            hour = hourRaw & 0x7F;  // Mask off PM bit if present
            day = dayRaw;
            month = monthRaw;
            year = yearRaw;
        }
        else
        {
            second = BcdToBinary(secRaw);
            minute = BcdToBinary(minRaw);
            hour = BcdToBinary((byte)(hourRaw & 0x7F));  // Mask off PM bit
            day = BcdToBinary(dayRaw);
            month = BcdToBinary(monthRaw);
            year = BcdToBinary(yearRaw);
        }

        // Handle 12-hour mode
        if (!is24Hour && (hourRaw & 0x80) != 0)
        {
            // PM bit set in 12-hour mode
            hour = (hour % 12) + 12;
        }

        // Determine century
        int century;
        if (centuryRaw != 0 && centuryRaw != 0xFF)
        {
            // Century register appears valid
            century = binaryMode ? centuryRaw : BcdToBinary(centuryRaw);
        }
        else
        {
            // Assume 2000s for years 00-99
            century = (year >= 70) ? 19 : 20;
        }

        year = century * 100 + year;

        // Sanity check
        if (year < 1970 || year > 2100 ||
            month < 1 || month > 12 ||
            day < 1 || day > 31 ||
            hour > 23 || minute > 59 || second > 59)
        {
            DebugConsole.WriteLine("[RTC] Invalid time values read!");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Convert date/time to FILETIME (100-nanosecond intervals since 1601-01-01 00:00:00 UTC)
    /// </summary>
    private static ulong DateTimeToFileTime(int year, int month, int day,
                                             int hour, int minute, int second)
    {
        // First convert to Unix timestamp (seconds since 1970-01-01)
        long unixTime = DateTimeToUnixTime(year, month, day, hour, minute, second);

        // FILETIME epoch is 1601-01-01, Unix epoch is 1970-01-01
        // Difference is 11644473600 seconds (369 years, accounting for leap years)
        const long UNIX_TO_FILETIME_OFFSET = 11644473600L;

        // Convert to 100-nanosecond intervals
        // FILETIME = (unixTime + offset) * 10,000,000
        ulong fileTime = (ulong)(unixTime + UNIX_TO_FILETIME_OFFSET) * 10_000_000UL;

        return fileTime;
    }

    /// <summary>
    /// Get days in a month (1-indexed)
    /// </summary>
    private static int GetDaysInMonth(int month, int year)
    {
        return month switch
        {
            1 => 31,   // January
            2 => IsLeapYear(year) ? 29 : 28,  // February
            3 => 31,   // March
            4 => 30,   // April
            5 => 31,   // May
            6 => 30,   // June
            7 => 31,   // July
            8 => 31,   // August
            9 => 30,   // September
            10 => 31,  // October
            11 => 30,  // November
            12 => 31,  // December
            _ => 0
        };
    }

    /// <summary>
    /// Convert date/time to Unix timestamp (seconds since 1970-01-01 00:00:00 UTC)
    /// </summary>
    private static long DateTimeToUnixTime(int year, int month, int day,
                                            int hour, int minute, int second)
    {
        // Calculate days since Unix epoch (1970-01-01)
        long days = 0;

        // Add days for complete years
        for (int y = 1970; y < year; y++)
        {
            days += IsLeapYear(y) ? 366 : 365;
        }

        // Add days for complete months in current year
        for (int m = 1; m < month; m++)
        {
            days += GetDaysInMonth(m, year);
        }

        // Add days in current month (minus 1 because day 1 = 0 days elapsed)
        days += day - 1;

        // Convert to seconds and add time of day
        long totalSeconds = days * 86400L +
                           hour * 3600L +
                           minute * 60L +
                           second;

        return totalSeconds;
    }

    /// <summary>
    /// Check if year is a leap year
    /// </summary>
    private static bool IsLeapYear(int year)
    {
        return (year % 4 == 0 && year % 100 != 0) || (year % 400 == 0);
    }

    /// <summary>
    /// Get current system time as FILETIME.
    /// Combines boot time from RTC with elapsed time from HPET.
    /// </summary>
    public static ulong GetSystemTimeAsFileTime()
    {
        if (!_initialized)
            return 0;

        // If HPET is available, add elapsed time since boot
        if (HPET.IsInitialized)
        {
            ulong currentHpetTicks = HPET.ReadCounter();
            ulong elapsedTicks = currentHpetTicks - _bootHpetTicks;

            // Convert HPET ticks to 100-nanosecond intervals (FILETIME units)
            // HPET gives nanoseconds via TicksToNanoseconds, divide by 100 for FILETIME
            ulong elapsedNs = HPET.TicksToNanoseconds(elapsedTicks);
            ulong elapsed100Ns = elapsedNs / 100;

            return _bootTimeFileTime + elapsed100Ns;
        }

        // No HPET, just return boot time
        return _bootTimeFileTime;
    }

    /// <summary>
    /// Get current system time broken down into components.
    /// </summary>
    public static void GetSystemTime(out int year, out int month, out int day,
                                      out int hour, out int minute, out int second,
                                      out int millisecond)
    {
        ulong fileTime = GetSystemTimeAsFileTime();
        FileTimeToDateTime(fileTime, out year, out month, out day,
                           out hour, out minute, out second, out millisecond);
    }

    /// <summary>
    /// Convert FILETIME to date/time components
    /// </summary>
    private static void FileTimeToDateTime(ulong fileTime,
                                            out int year, out int month, out int day,
                                            out int hour, out int minute, out int second,
                                            out int millisecond)
    {
        // Convert from 100-ns intervals to seconds since FILETIME epoch
        ulong totalSeconds = fileTime / 10_000_000UL;
        ulong remaining100Ns = fileTime % 10_000_000UL;

        millisecond = (int)(remaining100Ns / 10_000);

        // Convert from FILETIME epoch (1601) to Unix epoch (1970)
        const long UNIX_TO_FILETIME_OFFSET = 11644473600L;
        long unixTime = (long)totalSeconds - UNIX_TO_FILETIME_OFFSET;

        // Extract time of day
        second = (int)(unixTime % 60);
        unixTime /= 60;
        minute = (int)(unixTime % 60);
        unixTime /= 60;
        hour = (int)(unixTime % 24);
        long totalDays = unixTime / 24;

        // Add days from Unix epoch (1970-01-01)
        // This is a simplified algorithm - good enough for our purposes
        year = 1970;

        while (true)
        {
            int daysInYear = IsLeapYear(year) ? 366 : 365;
            if (totalDays < daysInYear)
                break;
            totalDays -= daysInYear;
            year++;
        }

        month = 1;
        while (month <= 12)
        {
            int dim = GetDaysInMonth(month, year);
            if (totalDays < dim)
                break;
            totalDays -= dim;
            month++;
        }

        day = (int)totalDays + 1;  // Days are 1-indexed
    }
}
