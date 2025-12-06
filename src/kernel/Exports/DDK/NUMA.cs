// ProtonOS kernel - DDK NUMA Exports
// Exposes NUMA topology and memory allocation APIs to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for NUMA topology and NUMA-aware memory allocation.
/// These are callable from JIT-compiled code via their entry point names.
/// </summary>
public static unsafe class NUMAExports
{
    /// <summary>
    /// Get the number of NUMA nodes in the system.
    /// </summary>
    /// <returns>Number of NUMA nodes detected from ACPI SRAT</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeCount")]
    public static int GetNumaNodeCount()
    {
        return NumaTopology.NodeCount;
    }

    /// <summary>
    /// Get the NUMA node for a specific CPU.
    /// </summary>
    /// <param name="cpuIndex">CPU index (0 to CpuCount-1)</param>
    /// <returns>NUMA node ID, or 0 if invalid CPU index</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeForCpu")]
    public static uint GetNumaNodeForCpu(int cpuIndex)
    {
        var cpuInfo = CPUTopology.GetCpu(cpuIndex);
        if (cpuInfo == null)
            return 0;

        return cpuInfo->NumaNode;
    }

    /// <summary>
    /// Get the NUMA node for the current CPU.
    /// </summary>
    /// <returns>NUMA node ID of the current CPU</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCurrentNumaNode")]
    public static uint GetCurrentNumaNode()
    {
        return PerCpu.NumaNode;
    }

    /// <summary>
    /// Get the relative distance between two NUMA nodes.
    /// Distance 10 is local (same node), higher values indicate more hops.
    /// </summary>
    /// <param name="node1">First NUMA node ID</param>
    /// <param name="node2">Second NUMA node ID</param>
    /// <returns>Relative distance (10 = local, typically 20+ = remote)</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaDistance")]
    public static int GetNumaDistance(int node1, int node2)
    {
        return NumaTopology.GetNodeDistance(node1, node2);
    }

    /// <summary>
    /// Get information about a specific NUMA node.
    /// </summary>
    /// <param name="nodeIndex">Node index (0 to NodeCount-1)</param>
    /// <param name="info">Pointer to NumaNodeInfo structure to fill</param>
    /// <returns>true if successful, false if index out of range</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeInfo")]
    public static bool GetNumaNodeInfo(int nodeIndex, NumaNodeInfo* info)
    {
        if (info == null)
            return false;

        var nodeInfo = NumaTopology.GetNode(nodeIndex);
        if (nodeInfo == null)
            return false;

        *info = *nodeInfo;
        return true;
    }

    /// <summary>
    /// Get the NUMA node containing a physical address.
    /// </summary>
    /// <param name="physicalAddress">Physical memory address</param>
    /// <returns>NUMA node ID, or 0 if not found</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeForAddress")]
    public static uint GetNumaNodeForAddress(ulong physicalAddress)
    {
        return NumaTopology.GetNodeForAddress(physicalAddress);
    }

    /// <summary>
    /// Allocate a page of physical memory from the current CPU's NUMA node.
    /// </summary>
    /// <returns>Physical address of allocated page, or 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePageLocal")]
    public static ulong AllocatePageLocal()
    {
        return PageAllocator.AllocatePageLocal();
    }

    /// <summary>
    /// Allocate a page of physical memory from a specific NUMA node.
    /// </summary>
    /// <param name="node">Target NUMA node</param>
    /// <returns>Physical address of allocated page, or 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePageFromNode")]
    public static ulong AllocatePageFromNode(uint node)
    {
        return PageAllocator.AllocatePageFromNode(node);
    }

    /// <summary>
    /// Allocate multiple contiguous pages from a specific NUMA node.
    /// </summary>
    /// <param name="count">Number of pages to allocate</param>
    /// <param name="node">Target NUMA node</param>
    /// <returns>Physical address of first page, or 0 on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePagesFromNode")]
    public static ulong AllocatePagesFromNode(ulong count, uint node)
    {
        return PageAllocator.AllocatePagesFromNode(count, node);
    }

    /// <summary>
    /// Get the NUMA node that owns a specific page of memory.
    /// </summary>
    /// <param name="physicalAddress">Physical address of the page</param>
    /// <returns>NUMA node ID, or -1 if unknown</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetPageNumaNode")]
    public static int GetPageNumaNode(ulong physicalAddress)
    {
        return PageAllocator.GetPageNode(physicalAddress);
    }

    /// <summary>
    /// Set the preferred NUMA node for the current thread's memory allocations.
    /// </summary>
    /// <param name="node">Preferred NUMA node</param>
    /// <returns>Previous preferred node</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SetThreadNumaNode")]
    public static uint SetThreadNumaNode(uint node)
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return 0;

        uint oldNode = thread->PreferredNumaNode;
        thread->PreferredNumaNode = node;
        return oldNode;
    }

    /// <summary>
    /// Get the preferred NUMA node for the current thread's memory allocations.
    /// </summary>
    /// <returns>Preferred NUMA node</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetThreadNumaNode")]
    public static uint GetThreadNumaNode()
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return 0;

        return thread->PreferredNumaNode;
    }

    /// <summary>
    /// Check if NUMA topology information is available.
    /// </summary>
    /// <returns>true if NUMA info from SRAT/SLIT is available</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_IsNumaAvailable")]
    public static bool IsNumaAvailable()
    {
        return NumaTopology.IsInitialized && NumaTopology.NodeCount > 1;
    }

    /// <summary>
    /// Check if NUMA distance matrix (from SLIT) is available.
    /// </summary>
    /// <returns>true if distance matrix is available</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_HasNumaDistanceMatrix")]
    public static bool HasNumaDistanceMatrix()
    {
        return NumaTopology.HasDistanceMatrix;
    }
}
