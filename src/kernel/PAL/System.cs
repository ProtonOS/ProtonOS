// netos mernel - PAL System APIs
// Win32-compatible system information and timing APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.X64;

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
}
