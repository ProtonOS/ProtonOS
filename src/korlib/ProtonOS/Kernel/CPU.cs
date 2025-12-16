// ProtonOS korlib - DDK CPU API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Kernel;

/// <summary>
/// CPU information structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CpuInfo
{
    /// <summary>CPU index (0 to CpuCount-1).</summary>
    public uint CpuIndex;
    /// <summary>Local APIC ID.</summary>
    public uint ApicId;
    /// <summary>ACPI processor ID.</summary>
    public byte AcpiProcessorId;
    /// <summary>NUMA node this CPU belongs to.</summary>
    public uint NumaNode;
    /// <summary>True if this is the bootstrap processor.</summary>
    public bool IsBsp;
    /// <summary>True if CPU is currently online.</summary>
    public bool IsOnline;
    /// <summary>True if CPU is enabled in ACPI.</summary>
    public bool IsEnabled;
}

/// <summary>
/// DDK CPU topology and affinity API.
/// </summary>
public static unsafe class CPU
{
    /// <summary>
    /// Get the number of CPUs in the system.
    /// </summary>
    public static int GetCpuCount() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the index of the current CPU.
    /// </summary>
    public static int GetCurrentCpu() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get information about a specific CPU.
    /// </summary>
    /// <param name="index">CPU index (0 to CpuCount-1).</param>
    /// <param name="info">Pointer to CpuInfo structure to fill.</param>
    /// <returns>true if successful, false if index out of range.</returns>
    public static bool GetCpuInfo(int index, CpuInfo* info) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Set the CPU affinity mask for the current thread.
    /// </summary>
    /// <param name="mask">Bitmask of allowed CPUs (0 = any CPU).</param>
    /// <returns>Previous affinity mask.</returns>
    public static ulong SetThreadAffinity(ulong mask) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the CPU affinity mask for the current thread.
    /// </summary>
    public static ulong GetThreadAffinity() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Check if a specific CPU is online.
    /// </summary>
    public static bool IsCpuOnline(int index) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the Bootstrap Processor index.
    /// </summary>
    public static int GetBspIndex() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get a system-wide affinity mask with all CPUs set.
    /// </summary>
    public static ulong GetSystemAffinityMask() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Pin current thread to the CPU it's currently running on.
    /// </summary>
    public static void PinToCurrentCpu()
    {
        int cpu = GetCurrentCpu();
        SetThreadAffinity(1UL << cpu);
    }

    /// <summary>
    /// Allow current thread to run on any CPU.
    /// </summary>
    public static void UnpinFromCpu()
    {
        SetThreadAffinity(0);
    }
}
#endif
