// ProtonOS kernel - NUMA Topology
// Parses ACPI SRAT/SLIT tables to enumerate NUMA nodes and memory ranges.

using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

/// <summary>
/// Information about a single NUMA node
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NumaNodeInfo
{
    public uint NodeId;                // NUMA node ID (proximity domain)
    public uint CpuCount;              // Number of CPUs in this node
    public ulong MemoryBase;           // Lowest memory address in node
    public ulong MemoryTop;            // Highest memory address in node
    public ulong TotalMemory;          // Total memory in node (bytes)
    public bool IsValid;               // Node has CPUs or memory
}

/// <summary>
/// Information about a memory range belonging to a NUMA node
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct NumaMemoryRange
{
    public uint NodeId;                // Owning NUMA node
    public ulong BaseAddress;          // Physical base
    public ulong Length;               // Size in bytes
    public bool IsHotPluggable;        // Can be hot-added
    public bool IsNonVolatile;         // Persistent memory (NVDIMM)
    public bool IsEnabled;             // Entry is valid
}

/// <summary>
/// NUMA topology enumeration from ACPI SRAT/SLIT
/// </summary>
public static unsafe class NumaTopology
{
    public const int MaxNodes = 16;           // Maximum supported NUMA nodes
    public const int MaxMemoryRanges = 32;    // Maximum memory affinity entries
    public const int MaxCpuAffinities = 64;   // Maximum CPU affinity entries

    // Internal storage for CPU -> Node mapping
    private struct CpuAffinity
    {
        public uint ApicId;
        public uint NodeId;
        public bool IsX2Apic;
        public bool IsEnabled;
    }

    private static NumaNodeInfo* _nodes;
    private static int _nodeCount;

    private static NumaMemoryRange* _memoryRanges;
    private static int _memoryRangeCount;

    private static CpuAffinity* _cpuAffinities;
    private static int _cpuAffinityCount;

    // Distance matrix (flat array, row-major: distance[i,j] = _distances[i * _nodeCount + j])
    private static byte* _distances;
    private static bool _hasDistances;

    private static bool _initialized;

    /// <summary>
    /// Number of NUMA nodes detected
    /// </summary>
    public static int NodeCount => _nodeCount;

    /// <summary>
    /// Number of memory ranges detected
    /// </summary>
    public static int MemoryRangeCount => _memoryRangeCount;

    /// <summary>
    /// Whether the topology has been initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Whether SLIT distance matrix is available
    /// </summary>
    public static bool HasDistanceMatrix => _hasDistances;

    /// <summary>
    /// Get NUMA node info by index
    /// </summary>
    public static NumaNodeInfo* GetNode(int index)
    {
        if (index < 0 || index >= _nodeCount)
            return null;
        return &_nodes[index];
    }

    /// <summary>
    /// Get memory range info by index
    /// </summary>
    public static NumaMemoryRange* GetMemoryRange(int index)
    {
        if (index < 0 || index >= _memoryRangeCount)
            return null;
        return &_memoryRanges[index];
    }

    /// <summary>
    /// Get the NUMA node for a given APIC ID.
    /// Returns the node ID, or 0 if not found (default to node 0).
    /// </summary>
    public static uint GetNodeForApicId(uint apicId)
    {
        for (int i = 0; i < _cpuAffinityCount; i++)
        {
            if (_cpuAffinities[i].ApicId == apicId && _cpuAffinities[i].IsEnabled)
                return _cpuAffinities[i].NodeId;
        }
        return 0;  // Default to node 0
    }

    /// <summary>
    /// Get the NUMA node containing a physical address.
    /// Returns the node ID, or 0 if not found.
    /// </summary>
    public static uint GetNodeForAddress(ulong physicalAddress)
    {
        for (int i = 0; i < _memoryRangeCount; i++)
        {
            if (_memoryRanges[i].IsEnabled &&
                physicalAddress >= _memoryRanges[i].BaseAddress &&
                physicalAddress < _memoryRanges[i].BaseAddress + _memoryRanges[i].Length)
            {
                return _memoryRanges[i].NodeId;
            }
        }
        return 0;  // Default to node 0
    }

    /// <summary>
    /// Get the relative distance between two NUMA nodes.
    /// Returns 10 for local (same node), typically 20 for 1 hop.
    /// Returns 10 if no SLIT or invalid nodes.
    /// </summary>
    public static int GetNodeDistance(int node1, int node2)
    {
        if (!_hasDistances || node1 < 0 || node2 < 0 || node1 >= _nodeCount || node2 >= _nodeCount)
        {
            // No distance info - return local if same node, else typical remote
            return (node1 == node2) ? 10 : 20;
        }
        return _distances[node1 * _nodeCount + node2];
    }

    /// <summary>
    /// Initialize NUMA topology by parsing SRAT and SLIT tables.
    /// Must be called after ACPI.Init() and HeapAllocator.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        DebugConsole.WriteLine("[NUMA] Initializing NUMA topology...");

        // Allocate storage
        _nodes = (NumaNodeInfo*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(NumaNodeInfo) * MaxNodes));
        _memoryRanges = (NumaMemoryRange*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(NumaMemoryRange) * MaxMemoryRanges));
        _cpuAffinities = (CpuAffinity*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(CpuAffinity) * MaxCpuAffinities));
        _distances = (byte*)Memory.HeapAllocator.AllocZeroed((ulong)(MaxNodes * MaxNodes));

        if (_nodes == null || _memoryRanges == null || _cpuAffinities == null || _distances == null)
        {
            DebugConsole.WriteLine("[NUMA] Failed to allocate memory!");
            return false;
        }

        // Find SRAT table
        var srat = ACPI.FindSrat();
        if (srat == null)
        {
            DebugConsole.WriteLine("[NUMA] No SRAT found - assuming single NUMA node");
            // Create a default single-node topology
            _nodeCount = 1;
            _nodes[0].NodeId = 0;
            _nodes[0].IsValid = true;
            _initialized = true;
            return true;
        }

        DebugConsole.Write("[NUMA] SRAT at 0x");
        DebugConsole.WriteHex((ulong)srat);
        DebugConsole.Write(" length ");
        DebugConsole.WriteDecimal((int)srat->Header.Length);
        DebugConsole.WriteLine();

        // Parse SRAT entries
        ParseSrat(srat);

        // Build node info from parsed data
        BuildNodeInfo();

        // Find and parse SLIT for distances
        var slit = ACPI.FindSlit();
        if (slit != null)
        {
            DebugConsole.Write("[NUMA] SLIT at 0x");
            DebugConsole.WriteHex((ulong)slit);
            DebugConsole.WriteLine();
            ParseSlit(slit);
        }
        else
        {
            DebugConsole.WriteLine("[NUMA] No SLIT found - using default distances");
        }

        // Log results
        DebugConsole.Write("[NUMA] Detected ");
        DebugConsole.WriteDecimal(_nodeCount);
        DebugConsole.Write(" NUMA node(s), ");
        DebugConsole.WriteDecimal(_memoryRangeCount);
        DebugConsole.Write(" memory range(s), ");
        DebugConsole.WriteDecimal(_cpuAffinityCount);
        DebugConsole.WriteLine(" CPU affinity entries");

        // Per-node details debug (verbose - commented out)
        // for (int i = 0; i < _nodeCount; i++)
        // {
        //     if (_nodes[i].IsValid)
        //     {
        //         DebugConsole.Write("[NUMA]   Node ");
        //         DebugConsole.WriteDecimal((int)_nodes[i].NodeId);
        //         DebugConsole.Write(": ");
        //         DebugConsole.WriteDecimal((int)(_nodes[i].TotalMemory / (1024 * 1024)));
        //         DebugConsole.Write(" MB, ");
        //         DebugConsole.WriteDecimal((int)_nodes[i].CpuCount);
        //         DebugConsole.WriteLine(" CPU(s)");
        //     }
        // }

        // Distance matrix debug (verbose - commented out)
        // if (_hasDistances && _nodeCount > 1)
        // {
        //     DebugConsole.WriteLine("[NUMA] Distance matrix:");
        //     for (int i = 0; i < _nodeCount; i++)
        //     {
        //         DebugConsole.Write("[NUMA]   ");
        //         for (int j = 0; j < _nodeCount; j++)
        //         {
        //             DebugConsole.WriteDecimal(GetNodeDistance(i, j));
        //             DebugConsole.Write(" ");
        //         }
        //         DebugConsole.WriteLine();
        //     }
        // }

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Parse SRAT table entries
    /// </summary>
    private static void ParseSrat(ACPISRAT* srat)
    {
        byte* entryPtr = (byte*)srat + sizeof(ACPISRAT);
        byte* endPtr = (byte*)srat + srat->Header.Length;

        while (entryPtr < endPtr)
        {
            byte type = entryPtr[0];
            byte length = entryPtr[1];

            if (length == 0)
            {
                DebugConsole.WriteLine("[NUMA] Invalid SRAT entry length 0!");
                break;
            }

            switch (type)
            {
                case SRATEntryType.ProcessorLocalApicAffinity:
                    ParseProcessorAffinity((SRATProcessorAffinity*)entryPtr);
                    break;

                case SRATEntryType.MemoryAffinity:
                    ParseMemoryAffinity((SRATMemoryAffinity*)entryPtr);
                    break;

                case SRATEntryType.ProcessorX2ApicAffinity:
                    ParseX2ApicAffinity((SRATx2ApicAffinity*)entryPtr);
                    break;
            }

            entryPtr += length;
        }
    }

    /// <summary>
    /// Parse SRAT Type 0: Processor Local APIC Affinity
    /// </summary>
    private static void ParseProcessorAffinity(SRATProcessorAffinity* entry)
    {
        if (_cpuAffinityCount >= MaxCpuAffinities)
            return;

        bool enabled = (entry->Flags & SRATAffinityFlags.Enabled) != 0;
        uint domain = entry->GetProximityDomain();

        _cpuAffinities[_cpuAffinityCount].ApicId = entry->ApicId;
        _cpuAffinities[_cpuAffinityCount].NodeId = domain;
        _cpuAffinities[_cpuAffinityCount].IsX2Apic = false;
        _cpuAffinities[_cpuAffinityCount].IsEnabled = enabled;
        _cpuAffinityCount++;
    }

    /// <summary>
    /// Parse SRAT Type 1: Memory Affinity
    /// </summary>
    private static void ParseMemoryAffinity(SRATMemoryAffinity* entry)
    {
        if (_memoryRangeCount >= MaxMemoryRanges)
            return;

        bool enabled = (entry->Flags & SRATAffinityFlags.Enabled) != 0;
        if (!enabled)
            return;  // Skip disabled entries

        _memoryRanges[_memoryRangeCount].NodeId = entry->ProximityDomain;
        _memoryRanges[_memoryRangeCount].BaseAddress = entry->BaseAddress;
        _memoryRanges[_memoryRangeCount].Length = entry->MemoryLength;
        _memoryRanges[_memoryRangeCount].IsHotPluggable = (entry->Flags & SRATAffinityFlags.HotPluggable) != 0;
        _memoryRanges[_memoryRangeCount].IsNonVolatile = (entry->Flags & SRATAffinityFlags.NonVolatile) != 0;
        _memoryRanges[_memoryRangeCount].IsEnabled = enabled;
        _memoryRangeCount++;
    }

    /// <summary>
    /// Parse SRAT Type 2: Processor x2APIC Affinity
    /// </summary>
    private static void ParseX2ApicAffinity(SRATx2ApicAffinity* entry)
    {
        if (_cpuAffinityCount >= MaxCpuAffinities)
            return;

        bool enabled = (entry->Flags & SRATAffinityFlags.Enabled) != 0;

        _cpuAffinities[_cpuAffinityCount].ApicId = entry->X2ApicId;
        _cpuAffinities[_cpuAffinityCount].NodeId = entry->ProximityDomain;
        _cpuAffinities[_cpuAffinityCount].IsX2Apic = true;
        _cpuAffinities[_cpuAffinityCount].IsEnabled = enabled;
        _cpuAffinityCount++;
    }

    /// <summary>
    /// Build NumaNodeInfo from parsed CPU and memory affinities
    /// </summary>
    private static void BuildNodeInfo()
    {
        // First, find all unique node IDs
        uint maxNodeId = 0;
        for (int i = 0; i < _cpuAffinityCount; i++)
        {
            if (_cpuAffinities[i].IsEnabled && _cpuAffinities[i].NodeId > maxNodeId)
                maxNodeId = _cpuAffinities[i].NodeId;
        }
        for (int i = 0; i < _memoryRangeCount; i++)
        {
            if (_memoryRanges[i].IsEnabled && _memoryRanges[i].NodeId > maxNodeId)
                maxNodeId = _memoryRanges[i].NodeId;
        }

        // Initialize nodes (use contiguous indices, not proximity domain IDs)
        // For simplicity, assume proximity domains are 0, 1, 2...
        _nodeCount = (int)maxNodeId + 1;
        if (_nodeCount > MaxNodes)
            _nodeCount = MaxNodes;

        for (int i = 0; i < _nodeCount; i++)
        {
            _nodes[i].NodeId = (uint)i;
            _nodes[i].CpuCount = 0;
            _nodes[i].MemoryBase = 0xFFFFFFFFFFFFFFFF;
            _nodes[i].MemoryTop = 0;
            _nodes[i].TotalMemory = 0;
            _nodes[i].IsValid = false;
        }

        // Count CPUs per node
        for (int i = 0; i < _cpuAffinityCount; i++)
        {
            if (_cpuAffinities[i].IsEnabled)
            {
                uint nodeId = _cpuAffinities[i].NodeId;
                if (nodeId < (uint)_nodeCount)
                {
                    _nodes[nodeId].CpuCount++;
                    _nodes[nodeId].IsValid = true;
                }
            }
        }

        // Accumulate memory per node
        for (int i = 0; i < _memoryRangeCount; i++)
        {
            if (_memoryRanges[i].IsEnabled)
            {
                uint nodeId = _memoryRanges[i].NodeId;
                if (nodeId < (uint)_nodeCount)
                {
                    _nodes[nodeId].TotalMemory += _memoryRanges[i].Length;
                    _nodes[nodeId].IsValid = true;

                    // Track address range
                    ulong rangeBase = _memoryRanges[i].BaseAddress;
                    ulong rangeTop = rangeBase + _memoryRanges[i].Length;

                    if (rangeBase < _nodes[nodeId].MemoryBase)
                        _nodes[nodeId].MemoryBase = rangeBase;
                    if (rangeTop > _nodes[nodeId].MemoryTop)
                        _nodes[nodeId].MemoryTop = rangeTop;
                }
            }
        }

        // Fix up nodes with no memory ranges
        for (int i = 0; i < _nodeCount; i++)
        {
            if (_nodes[i].MemoryBase == 0xFFFFFFFFFFFFFFFF)
                _nodes[i].MemoryBase = 0;
        }
    }

    /// <summary>
    /// Parse SLIT distance matrix
    /// </summary>
    private static void ParseSlit(ACPISLIT* slit)
    {
        ulong localities = slit->NumberOfSystemLocalities;
        if (localities == 0 || localities > MaxNodes)
        {
            DebugConsole.Write("[NUMA] Invalid SLIT localities: ");
            DebugConsole.WriteDecimal((int)localities);
            DebugConsole.WriteLine();
            return;
        }

        // The distance matrix follows the header
        byte* matrix = (byte*)slit + sizeof(ACPISLIT);

        // Copy distances for our nodes
        int n = (localities < (ulong)_nodeCount) ? (int)localities : _nodeCount;
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                _distances[i * _nodeCount + j] = matrix[i * (int)localities + j];
            }
        }

        _hasDistances = true;
    }
}
