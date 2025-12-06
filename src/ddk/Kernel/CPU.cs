// ProtonOS DDK - CPU Kernel Wrappers
// DllImport wrappers for kernel CPU topology and affinity exports.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// CPU information structure matching kernel's CpuInfo.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CpuInfo
{
    public uint CpuIndex;
    public uint ApicId;
    public byte AcpiProcessorId;
    public uint NumaNode;
    public bool IsBsp;
    public bool IsOnline;
    public bool IsEnabled;
}

/// <summary>
/// DDK wrappers for kernel CPU topology and thread affinity APIs.
/// </summary>
public static unsafe class CPU
{
    [DllImport("*", EntryPoint = "Kernel_GetCpuCount")]
    public static extern int GetCpuCount();

    [DllImport("*", EntryPoint = "Kernel_GetCurrentCpu")]
    public static extern int GetCurrentCpu();

    [DllImport("*", EntryPoint = "Kernel_GetCpuInfo")]
    public static extern bool GetCpuInfo(int index, CpuInfo* info);

    [DllImport("*", EntryPoint = "Kernel_SetThreadAffinity")]
    public static extern ulong SetThreadAffinity(ulong mask);

    [DllImport("*", EntryPoint = "Kernel_GetThreadAffinity")]
    public static extern ulong GetThreadAffinity();

    [DllImport("*", EntryPoint = "Kernel_IsCpuOnline")]
    public static extern bool IsCpuOnline(int index);

    [DllImport("*", EntryPoint = "Kernel_GetBspIndex")]
    public static extern int GetBspIndex();

    [DllImport("*", EntryPoint = "Kernel_GetSystemAffinityMask")]
    public static extern ulong GetSystemAffinityMask();

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
