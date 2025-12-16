// ProtonOS korlib - DDK Timer API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// DDK Timer and delay API.
/// </summary>
public static class Timer
{
    /// <summary>
    /// Get the current tick count.
    /// </summary>
    public static ulong GetTickCount() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the tick frequency (ticks per second).
    /// </summary>
    public static ulong GetTickFrequency() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get system uptime in nanoseconds.
    /// </summary>
    public static ulong GetUptimeNanoseconds() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Busy-wait for the specified number of microseconds.
    /// </summary>
    public static void DelayMicroseconds(uint microseconds) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Busy-wait for the specified number of milliseconds.
    /// </summary>
    public static void DelayMilliseconds(uint milliseconds) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Read the Time Stamp Counter (TSC).
    /// </summary>
    public static ulong ReadTSC() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the TSC frequency in Hz.
    /// </summary>
    public static ulong GetTSCFrequency() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get uptime in milliseconds.
    /// </summary>
    public static ulong GetUptimeMilliseconds()
    {
        return GetUptimeNanoseconds() / 1_000_000;
    }

    /// <summary>
    /// Get uptime in seconds.
    /// </summary>
    public static ulong GetUptimeSeconds()
    {
        return GetUptimeNanoseconds() / 1_000_000_000;
    }

    /// <summary>
    /// Convert ticks to nanoseconds.
    /// </summary>
    public static ulong TicksToNanoseconds(ulong ticks)
    {
        ulong freq = GetTickFrequency();
        if (freq == 0)
            return 0;
        return (ticks * 1_000_000_000) / freq;
    }

    /// <summary>
    /// Convert nanoseconds to ticks.
    /// </summary>
    public static ulong NanosecondsToTicks(ulong nanoseconds)
    {
        ulong freq = GetTickFrequency();
        return (nanoseconds * freq) / 1_000_000_000;
    }
}
#endif
