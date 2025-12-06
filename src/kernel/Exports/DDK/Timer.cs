// ProtonOS kernel - DDK Timer Exports
// Exposes timing and delay operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for timing and delay operations.
/// </summary>
public static class TimerExports
{
    /// <summary>
    /// Get the current HPET tick count.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetTickCount")]
    public static ulong GetTickCount()
    {
        return HPET.ReadCounter();
    }

    /// <summary>
    /// Get the HPET tick frequency in Hz.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetTickFrequency")]
    public static ulong GetTickFrequency()
    {
        return HPET.FrequencyHz;
    }

    /// <summary>
    /// Get system uptime in nanoseconds.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetUptime")]
    public static ulong GetUptimeNanoseconds()
    {
        return HPET.TicksToNanoseconds(HPET.ReadCounter());
    }

    /// <summary>
    /// Delay for a number of microseconds (busy-wait).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DelayMicroseconds")]
    public static void DelayMicroseconds(uint microseconds)
    {
        HPET.BusyWaitUs(microseconds);
    }

    /// <summary>
    /// Delay for a number of milliseconds (busy-wait).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_DelayMilliseconds")]
    public static void DelayMilliseconds(uint milliseconds)
    {
        HPET.BusyWaitMs(milliseconds);
    }

    /// <summary>
    /// Read the TSC (Time Stamp Counter).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_ReadTSC")]
    public static ulong ReadTSC()
    {
        // TSC is read via RDTSC instruction
        // For now, use HPET ticks as a fallback
        // TODO: Add RDTSC native wrapper
        return HPET.ReadCounter();
    }

    /// <summary>
    /// Get the TSC frequency in Hz.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetTSCFrequency")]
    public static ulong GetTSCFrequency()
    {
        // Return HPET frequency as fallback
        // TODO: Calibrate and return actual TSC frequency
        return HPET.FrequencyHz;
    }
}
