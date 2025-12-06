// ProtonOS DDK - Timer Kernel Wrappers
// DllImport wrappers for timing and delay operations.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// DDK wrappers for kernel timer and delay APIs.
/// </summary>
public static class Timer
{
    [DllImport("*", EntryPoint = "Kernel_GetTickCount")]
    public static extern ulong GetTickCount();

    [DllImport("*", EntryPoint = "Kernel_GetTickFrequency")]
    public static extern ulong GetTickFrequency();

    [DllImport("*", EntryPoint = "Kernel_GetUptime")]
    public static extern ulong GetUptimeNanoseconds();

    [DllImport("*", EntryPoint = "Kernel_DelayMicroseconds")]
    public static extern void DelayMicroseconds(uint microseconds);

    [DllImport("*", EntryPoint = "Kernel_DelayMilliseconds")]
    public static extern void DelayMilliseconds(uint milliseconds);

    [DllImport("*", EntryPoint = "Kernel_ReadTSC")]
    public static extern ulong ReadTSC();

    [DllImport("*", EntryPoint = "Kernel_GetTSCFrequency")]
    public static extern ulong GetTSCFrequency();

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
