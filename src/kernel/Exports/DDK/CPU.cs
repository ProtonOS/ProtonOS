// ProtonOS kernel - DDK CPU Exports
// Exposes CPU topology and affinity APIs to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Threading;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for CPU topology and thread affinity.
/// These are callable from JIT-compiled code via their entry point names.
/// </summary>
public static unsafe class CPUExports
{
    /// <summary>
    /// Get the number of CPUs in the system.
    /// </summary>
    /// <returns>Number of CPUs detected from ACPI MADT</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCpuCount")]
    public static int GetCpuCount()
    {
        return CPUTopology.CpuCount;
    }

    /// <summary>
    /// Get the index of the current CPU.
    /// </summary>
    /// <returns>Current CPU index (0 to CpuCount-1)</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCurrentCpu")]
    public static int GetCurrentCpu()
    {
        return (int)PerCpu.CpuIndex;
    }

    /// <summary>
    /// Get information about a specific CPU.
    /// </summary>
    /// <param name="index">CPU index (0 to CpuCount-1)</param>
    /// <param name="info">Pointer to CpuInfo structure to fill</param>
    /// <returns>true if successful, false if index out of range</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCpuInfo")]
    public static bool GetCpuInfo(int index, CpuInfo* info)
    {
        if (info == null)
            return false;

        var cpuInfo = CPUTopology.GetCpu(index);
        if (cpuInfo == null)
            return false;

        *info = *cpuInfo;
        return true;
    }

    /// <summary>
    /// Set the CPU affinity mask for the current thread.
    /// </summary>
    /// <param name="mask">Bitmask of allowed CPUs (0 = any CPU)</param>
    /// <returns>Previous affinity mask</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SetThreadAffinity")]
    public static ulong SetThreadAffinity(ulong mask)
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return 0;

        ulong oldMask = thread->CpuAffinity;
        thread->CpuAffinity = mask;
        return oldMask;
    }

    /// <summary>
    /// Get the CPU affinity mask for the current thread.
    /// </summary>
    /// <returns>Current affinity mask (0 = any CPU)</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetThreadAffinity")]
    public static ulong GetThreadAffinity()
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return 0;

        return thread->CpuAffinity;
    }

    /// <summary>
    /// Check if a specific CPU is online (running).
    /// </summary>
    /// <param name="index">CPU index (0 to CpuCount-1)</param>
    /// <returns>true if CPU is online, false otherwise</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_IsCpuOnline")]
    public static bool IsCpuOnline(int index)
    {
        var cpuInfo = CPUTopology.GetCpu(index);
        if (cpuInfo == null)
            return false;

        return cpuInfo->IsOnline;
    }

    /// <summary>
    /// Get the Bootstrap Processor index.
    /// </summary>
    /// <returns>BSP CPU index</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetBspIndex")]
    public static int GetBspIndex()
    {
        return CPUTopology.BspIndex;
    }

    /// <summary>
    /// Get a system-wide affinity mask with all CPUs set.
    /// </summary>
    /// <returns>Bitmask with bits 0..CpuCount-1 set</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetSystemAffinityMask")]
    public static ulong GetSystemAffinityMask()
    {
        int cpuCount = CPUTopology.CpuCount;
        if (cpuCount >= 64)
            return 0xFFFFFFFFFFFFFFFFUL;

        return (1UL << cpuCount) - 1;
    }
}
