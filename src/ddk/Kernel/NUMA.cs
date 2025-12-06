// ProtonOS DDK - NUMA Kernel Wrappers
// DllImport wrappers for kernel NUMA topology and allocation exports.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// NUMA node information structure matching kernel's NumaNodeInfo.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NumaNodeInfo
{
    public uint NodeId;
    public ulong BaseAddress;
    public ulong Length;
    public uint CpuCount;
    public ulong CpuMask;
}

/// <summary>
/// DDK wrappers for kernel NUMA topology and memory allocation APIs.
/// </summary>
public static unsafe class NUMA
{
    [DllImport("*", EntryPoint = "Kernel_GetNumaNodeCount")]
    public static extern int GetNodeCount();

    [DllImport("*", EntryPoint = "Kernel_GetNumaNodeForCpu")]
    public static extern uint GetNodeForCpu(int cpuIndex);

    [DllImport("*", EntryPoint = "Kernel_GetCurrentNumaNode")]
    public static extern uint GetCurrentNode();

    [DllImport("*", EntryPoint = "Kernel_GetNumaDistance")]
    public static extern int GetNodeDistance(int node1, int node2);

    [DllImport("*", EntryPoint = "Kernel_GetNumaNodeInfo")]
    public static extern bool GetNodeInfo(int nodeIndex, NumaNodeInfo* info);

    [DllImport("*", EntryPoint = "Kernel_GetNumaNodeForAddress")]
    public static extern uint GetNodeForAddress(ulong physicalAddress);

    [DllImport("*", EntryPoint = "Kernel_AllocatePageLocal")]
    public static extern ulong AllocatePageLocal();

    [DllImport("*", EntryPoint = "Kernel_AllocatePageFromNode")]
    public static extern ulong AllocatePageFromNode(uint node);

    [DllImport("*", EntryPoint = "Kernel_AllocatePagesFromNode")]
    public static extern ulong AllocatePagesFromNode(ulong count, uint node);

    [DllImport("*", EntryPoint = "Kernel_GetPageNumaNode")]
    public static extern int GetPageNode(ulong physicalAddress);

    [DllImport("*", EntryPoint = "Kernel_SetThreadNumaNode")]
    public static extern uint SetThreadNode(uint node);

    [DllImport("*", EntryPoint = "Kernel_GetThreadNumaNode")]
    public static extern uint GetThreadNode();

    [DllImport("*", EntryPoint = "Kernel_IsNumaAvailable")]
    public static extern bool IsAvailable();

    [DllImport("*", EntryPoint = "Kernel_HasNumaDistanceMatrix")]
    public static extern bool HasDistanceMatrix();
}
