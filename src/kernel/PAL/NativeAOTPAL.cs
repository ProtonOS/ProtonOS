// ProtonOS kernel - NativeAOT PAL Functions
// Internal PAL functions used by NativeAOT runtime.
// These are not Win32 APIs but are called by the NativeAOT GC and runtime.

using System.Runtime.InteropServices;
using ProtonOS.Threading;
using ProtonOS.Memory;
using ProtonOS.X64;
using ProtonOS.Platform;

namespace ProtonOS.PAL;

/// <summary>
/// NativeAOT-specific PAL functions.
/// These are internal runtime functions that NativeAOT expects from the platform.
/// </summary>
public static unsafe class NativeAOTPAL
{
    // Module bounds - set during kernel initialization
    private static ulong _moduleBase;
    private static ulong _moduleEnd;
    private static bool _moduleBoundsInitialized;

    /// <summary>
    /// Initialize module bounds from PE headers or link-time symbols.
    /// This should be called early in kernel initialization.
    /// </summary>
    /// <param name="baseAddress">Base address of the kernel module</param>
    /// <param name="size">Size of the kernel module in bytes</param>
    public static void SetModuleBounds(ulong baseAddress, ulong size)
    {
        _moduleBase = baseAddress;
        _moduleEnd = baseAddress + size;
        _moduleBoundsInitialized = true;

        DebugConsole.Write("[PAL] Module bounds: 0x");
        DebugConsole.WriteHex(baseAddress);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex(_moduleEnd);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Get the bounds (start and end addresses) of the main module.
    /// Used by the GC and runtime for code range detection.
    /// </summary>
    /// <param name="pLowerBound">Receives the lower bound (base address)</param>
    /// <param name="pUpperBound">Receives the upper bound (end address)</param>
    /// <returns>True on success</returns>
    public static bool GetModuleBounds(ulong* pLowerBound, ulong* pUpperBound)
    {
        if (!_moduleBoundsInitialized)
        {
            // Get kernel bounds from BootInfo
            var bootInfo = BootInfoAccess.Get();
            if (bootInfo != null && bootInfo->IsValid &&
                bootInfo->KernelPhysicalBase != 0 && bootInfo->KernelSize != 0)
            {
                _moduleBase = bootInfo->KernelPhysicalBase;
                _moduleEnd = bootInfo->KernelPhysicalBase + bootInfo->KernelSize;
            }
            else
            {
                // Fallback: use reasonable defaults
                _moduleBase = 0x8000000;      // 128MB (our kernel load address)
                _moduleEnd = 0x10000000;      // 256MB (generous upper bound)
            }
            _moduleBoundsInitialized = true;
        }

        if (pLowerBound != null)
            *pLowerBound = _moduleBase;
        if (pUpperBound != null)
            *pUpperBound = _moduleEnd;

        return true;
    }

    /// <summary>
    /// Get the maximum stack bounds for the current thread.
    /// Used by the GC for stack scanning.
    /// </summary>
    /// <param name="pLowLimit">Receives the low limit (stack bottom / highest address for descending stacks)</param>
    /// <param name="pHighLimit">Receives the high limit (stack top / lowest address for descending stacks)</param>
    /// <returns>True on success</returns>
    public static bool GetMaximumStackBounds(ulong* pLowLimit, ulong* pHighLimit)
    {
        var thread = Scheduler.CurrentThread;

        if (thread != null && thread->StackBase != 0 && thread->StackSize != 0)
        {
            // Stack grows downward on x64
            // StackBase is the high address (where stack starts)
            // StackBase - StackSize is the low address (stack limit)
            if (pHighLimit != null)
                *pHighLimit = thread->StackBase;  // Top of stack (high address)
            if (pLowLimit != null)
                *pLowLimit = thread->StackBase - thread->StackSize;  // Bottom of stack (low address)

            return true;
        }

        // Fallback: try to use VirtualQuery on current RSP
        ulong rsp = CPU.GetRsp();
        MemoryBasicInformation memInfo;

        ulong result = Memory.VirtualQuery((void*)rsp, &memInfo, (ulong)sizeof(MemoryBasicInformation));
        if (result > 0 && memInfo.State != MemoryState.MEM_FREE)
        {
            // Region found - use allocation base and size
            if (pLowLimit != null)
                *pLowLimit = memInfo.AllocationBase;
            if (pHighLimit != null)
                *pHighLimit = memInfo.AllocationBase + memInfo.RegionSize;

            return true;
        }

        // Last resort defaults - 1MB stack
        if (pHighLimit != null)
            *pHighLimit = rsp + 0x10000;  // 64KB above RSP
        if (pLowLimit != null)
            *pLowLimit = rsp - 0xF0000;   // 960KB below RSP

        return true;
    }

    /// <summary>
    /// Get the filename of the module containing a given address.
    /// </summary>
    /// <param name="address">Address within the module</param>
    /// <param name="pFileName">Buffer to receive the filename</param>
    /// <param name="cchFileName">Size of buffer in characters</param>
    /// <returns>Number of characters written, or 0 on failure</returns>
    public static uint GetModuleFileName(void* address, char* pFileName, uint cchFileName)
    {
        // We only have one module - the kernel
        // Return a fixed path
        const string kernelPath = "\\kernel";

        if (pFileName == null || cchFileName == 0)
            return 0;

        uint i = 0;
        for (; i < kernelPath.Length && i < cchFileName - 1; i++)
        {
            pFileName[i] = kernelPath[(int)i];
        }
        pFileName[i] = '\0';

        return i;
    }

    /// <summary>
    /// Get PDB debug information for a module.
    /// Not supported in ProtonOS - we don't have PDBs.
    /// </summary>
    public static bool GetPDBInfo(
        void* moduleBase,
        void* pdbSignature,
        uint* pAge,
        char* pdbPath,
        uint pathSize)
    {
        // No PDB info available
        if (pAge != null)
            *pAge = 0;
        if (pdbPath != null && pathSize > 0)
            *pdbPath = '\0';

        return false;
    }

    /// <summary>
    /// Get the number of processors available to the process.
    /// </summary>
    public static uint GetProcessCPUCount()
    {
        SystemInfo sysInfo;
        SystemApi.GetSystemInfo(out sysInfo);
        return sysInfo.dwNumberOfProcessors;
    }

    /// <summary>
    /// Get the current system time in 100-nanosecond intervals since FILETIME epoch.
    /// </summary>
    public static ulong GetSystemTime()
    {
        FileTime ft;
        SystemApi.GetSystemTimeAsFileTime(&ft);
        return ft.ToUInt64();
    }

    /// <summary>
    /// Get high-resolution performance counter value.
    /// </summary>
    public static ulong GetPerformanceCounter()
    {
        long value;
        SystemApi.QueryPerformanceCounter(out value);
        return (ulong)value;
    }

    /// <summary>
    /// Get high-resolution performance counter frequency.
    /// </summary>
    public static ulong GetPerformanceFrequency()
    {
        long freq;
        SystemApi.QueryPerformanceFrequency(out freq);
        return (ulong)freq;
    }
}

