# NUMA Support Implementation Plan

## Overview

NUMA (Non-Uniform Memory Access) is a memory architecture where memory access times depend on the memory location relative to the processor. CPUs have "local" memory that's fast to access and "remote" memory (on other nodes) that's slower.

**Goal**: Enable NUMA-aware memory allocation so threads/drivers can allocate from local memory for better performance.

**Approach**: AOT kernel implementation with DDK exports (same pattern as SMP).

---

## NUMA Concepts

```
┌─────────────────────────────────────────────────────────────────────┐
│                        NUMA System (2 nodes)                         │
├─────────────────────────────────────────────────────────────────────┤
│                                                                       │
│   ┌─────────────────────┐         ┌─────────────────────┐           │
│   │     NUMA Node 0     │         │     NUMA Node 1     │           │
│   ├─────────────────────┤         ├─────────────────────┤           │
│   │  CPU 0    CPU 1     │◄───────►│  CPU 2    CPU 3     │           │
│   │                     │ QPI/UPI │                     │           │
│   │  ┌───────────────┐  │         │  ┌───────────────┐  │           │
│   │  │  Local Memory │  │         │  │  Local Memory │  │           │
│   │  │   (512 MB)    │  │         │  │   (512 MB)    │  │           │
│   │  └───────────────┘  │         │  └───────────────┘  │           │
│   └─────────────────────┘         └─────────────────────┘           │
│                                                                       │
│   Access latency:                                                     │
│   - CPU 0 → Node 0 memory: ~10ns (local)                             │
│   - CPU 0 → Node 1 memory: ~20ns (remote, 2x slower)                 │
│                                                                       │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Data from ACPI:**
- **SRAT (System Resource Affinity Table)**: Maps CPUs and memory ranges to proximity domains
- **SLIT (System Locality Information Table)**: Provides relative distances between domains

---

## ACPI Table Structures

### SRAT Entry Types

| Type | Name | Description |
|------|------|-------------|
| 0 | Processor Local APIC/SAPIC Affinity | Maps CPU (APIC ID) to proximity domain |
| 1 | Memory Affinity | Maps physical memory range to proximity domain |
| 2 | Processor Local x2APIC Affinity | Extended APIC for >255 CPUs |

### SLIT Structure

The SLIT contains an NxN matrix of relative distances between N nodes:
- Distance 10 = local (same node)
- Distance 20 = typical remote (1 hop)
- Higher values = more hops

---

## Implementation Checklist

### Phase 1: SRAT/SLIT Structure Definitions

**File: `src/kernel/Platform/ACPI.cs`**

- [ ] Add SRAT table header struct (`ACPISRAT`)
- [ ] Add SRAT entry type constants (`SRATEntryType`)
- [ ] Add SRAT Processor Local APIC Affinity struct (`SRATProcessorAffinity`)
- [ ] Add SRAT Memory Affinity struct (`SRATMemoryAffinity`)
- [ ] Add SRAT Processor x2APIC Affinity struct (`SRATx2ApicAffinity`)
- [ ] Add SLIT table header struct (`ACPISLIT`)
- [ ] Add `FindSrat()` helper method
- [ ] Add `FindSlit()` helper method

**Structures to add:**

```csharp
// SRAT entry types
public static class SRATEntryType
{
    public const byte ProcessorLocalApicAffinity = 0;
    public const byte MemoryAffinity = 1;
    public const byte ProcessorX2ApicAffinity = 2;
}

// SRAT header (signature "SRAT")
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ACPISRAT
{
    public ACPITableHeader Header;
    public uint Reserved1;          // Must be 1
    public ulong Reserved2;         // Reserved
    // Followed by variable-length SRAT entries
}

// SRAT Type 0: Processor Local APIC Affinity
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SRATProcessorAffinity
{
    public byte Type;               // 0
    public byte Length;             // 16
    public byte ProximityDomainLo;  // Bits 0-7 of proximity domain
    public byte ApicId;             // Local APIC ID
    public uint Flags;              // Bit 0: Enabled
    public byte LocalSapicEid;      // Local SAPIC EID (Itanium)
    public byte ProximityDomainHi0; // Bits 8-15
    public byte ProximityDomainHi1; // Bits 16-23
    public byte ProximityDomainHi2; // Bits 24-31
    public uint ClockDomain;        // Reserved
}

// SRAT Type 1: Memory Affinity
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct SRATMemoryAffinity
{
    public byte Type;               // 1
    public byte Length;             // 40
    public uint ProximityDomain;    // Proximity domain
    public ushort Reserved1;
    public ulong BaseAddress;       // Physical base address
    public ulong Length;            // Length in bytes
    public uint Reserved2;
    public uint Flags;              // Bit 0: Enabled, Bit 1: Hot-pluggable, Bit 2: Non-volatile
    public ulong Reserved3;
}

// SRAT Type 2: Processor x2APIC Affinity
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SRATx2ApicAffinity
{
    public byte Type;               // 2
    public byte Length;             // 24
    public ushort Reserved1;
    public uint ProximityDomain;    // Proximity domain
    public uint X2ApicId;           // x2APIC ID
    public uint Flags;              // Bit 0: Enabled
    public uint ClockDomain;        // Reserved
    public uint Reserved2;
}

// SLIT header (signature "SLIT")
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ACPISLIT
{
    public ACPITableHeader Header;
    public ulong NumberOfSystemLocalities;
    // Followed by N*N byte matrix of distances
}
```

---

### Phase 2: NUMA Topology Module

**File: `src/kernel/Platform/NumaTopology.cs`** (NEW)

- [ ] Create `NumaNodeInfo` struct
- [ ] Create `NumaMemoryRange` struct
- [ ] Create `NumaTopology` static class with:
  - [ ] `MaxNodes` constant (16 typical, 64 max)
  - [ ] `NodeCount` property
  - [ ] `GetNode(int index)` method
  - [ ] `GetNodeForCpu(uint apicId)` method
  - [ ] `GetNodeDistance(int node1, int node2)` method
  - [ ] `Init()` method to parse SRAT/SLIT
  - [ ] Internal SRAT parsing methods
  - [ ] Internal SLIT parsing methods

**Structures:**

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct NumaNodeInfo
{
    public uint NodeId;             // NUMA node ID (proximity domain)
    public uint CpuCount;           // Number of CPUs in this node
    public ulong MemoryBase;        // Lowest memory address in node
    public ulong MemorySize;        // Total memory in node (bytes)
    public bool IsValid;            // Node has CPUs or memory
}

[StructLayout(LayoutKind.Sequential)]
public struct NumaMemoryRange
{
    public uint NodeId;             // Owning NUMA node
    public ulong BaseAddress;       // Physical base
    public ulong Length;            // Size in bytes
    public bool IsHotPluggable;     // Can be hot-added
    public bool IsNonVolatile;      // Persistent memory (NVDIMM)
}
```

---

### Phase 3: Extend CpuInfo with NUMA

**File: `src/kernel/Platform/CPUTopology.cs`**

- [ ] Add `NumaNode` field to `CpuInfo` struct
- [ ] Update `ParseLocalApic()` to set `NumaNode` (requires SRAT data)
- [ ] Update `ParseLocalX2Apic()` to set `NumaNode`
- [ ] Add integration point with NumaTopology

**Change to CpuInfo:**

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct CpuInfo
{
    public uint CpuIndex;           // Kernel-assigned CPU index
    public uint ApicId;             // Hardware APIC ID
    public byte AcpiProcessorId;    // ACPI-assigned processor ID
    public uint NumaNode;           // NUMA node (proximity domain) - NEW
    public bool IsBsp;              // Is Bootstrap Processor
    public bool IsOnline;           // Currently running
    public bool IsEnabled;          // Enabled in MADT
}
```

---

### Phase 4: NUMA-Aware PageAllocator

**File: `src/kernel/Memory/PageAllocator.cs`**

This is the most complex phase. Options:

**Option A: Per-Node Free Lists (Recommended)**
- Maintain separate free page lists per NUMA node
- Each node tracks: first free page, free count
- Allocation prefers local node, falls back to others

**Option B: Node Tags in Bitmap**
- Add a secondary bitmap/array storing node ID per page
- Query node during allocation
- Simpler but slower

**Implementation tasks (Option A):**

- [ ] Add `NumaNodePageInfo` struct (per-node tracking)
- [ ] Add `_nodeInfo` array for per-node stats
- [ ] Add `_pageToNode` array or compute from ranges
- [ ] Modify `Init()` to classify pages by NUMA node
- [ ] Add `AllocatePageFromNode(uint node)` method
- [ ] Add `AllocatePageLocal()` using current CPU's node
- [ ] Modify `AllocatePage()` to prefer local node
- [ ] Add `GetPageNode(ulong physAddr)` helper
- [ ] Add per-node statistics

**New structures:**

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct NumaNodePageInfo
{
    public ulong FirstFreePage;     // First free page in this node (0 = none)
    public ulong FreeCount;         // Free pages in this node
    public ulong TotalCount;        // Total pages in this node
    public ulong BaseAddress;       // Lowest address in node
    public ulong TopAddress;        // Highest address in node
}
```

**Alternative simpler approach for Phase 4:**

Instead of per-node free lists (complex), we can:
1. Store node ID per page in a byte array (`_pageNode[pageNum]`)
2. During allocation, scan for free pages in preferred node first
3. Fall back to any node if local is exhausted

This is O(n) scan but simpler to implement initially.

---

### Phase 5: Per-CPU/Thread NUMA Preference

**File: `src/kernel/Threading/PerCpuState.cs`**

- [ ] Add `PreferredNumaNode` field to `PerCpuState`
- [ ] Initialize to CPU's local node

**File: `src/kernel/Threading/Thread.cs`**

- [ ] Add `PreferredNumaNode` field to `Thread` struct
- [ ] Inherit from creating thread or set explicitly

---

### Phase 6: DDK Exports

**File: `src/kernel/Exports/NUMA.cs`** (NEW)

- [ ] `Kernel_GetNumaNodeCount()` - Number of NUMA nodes
- [ ] `Kernel_GetNumaNodeInfo(int node, NumaNodeInfo* info)` - Node details
- [ ] `Kernel_GetNumaNodeForCpu(int cpuIndex)` - CPU's node
- [ ] `Kernel_GetNumaDistance(int node1, int node2)` - Latency metric
- [ ] `Kernel_SetPreferredNumaNode(int node)` - Set allocation hint
- [ ] `Kernel_GetPreferredNumaNode()` - Get current preference
- [ ] `Kernel_AllocatePageFromNode(int node)` - Allocate from specific node

```csharp
public static unsafe class NUMAExports
{
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeCount")]
    public static int GetNumaNodeCount()
    {
        return NumaTopology.NodeCount;
    }

    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaNodeForCpu")]
    public static int GetNumaNodeForCpu(int cpuIndex)
    {
        var cpu = CPUTopology.GetCpu(cpuIndex);
        if (cpu == null) return -1;
        return (int)cpu->NumaNode;
    }

    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetNumaDistance")]
    public static int GetNumaDistance(int node1, int node2)
    {
        return NumaTopology.GetNodeDistance(node1, node2);
    }

    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePageFromNode")]
    public static ulong AllocatePageFromNode(int node)
    {
        return PageAllocator.AllocatePageFromNode((uint)node);
    }

    // ... etc
}
```

---

### Phase 7: Update IArchitecture Interface (Optional)

**File: `src/kernel/Arch/IArchitecture.cs`**

Consider adding NUMA-related properties if needed for ARM64 compatibility:

- [ ] `NumaNodeCount` property
- [ ] `CurrentNumaNode` property

(May not be needed if NUMA is architecture-neutral via ACPI)

---

## Testing

### QEMU NUMA Configuration

The `run.sh` script is already configured for NUMA testing:

```bash
qemu-system-x86_64 \
  -machine q35 \
  -cpu max \
  -smp 4,sockets=2,cores=2,threads=1 \
  -m 512M \
  -object memory-backend-ram,id=mem0,size=256M \
  -object memory-backend-ram,id=mem1,size=256M \
  -numa node,nodeid=0,cpus=0-1,memdev=mem0 \
  -numa node,nodeid=1,cpus=2-3,memdev=mem1 \
  -numa dist,src=0,dst=1,val=20 \
  ...
```

**Note**: The q35 machine requires `memdev=` syntax (not `mem=`). Memory backends
must be defined first with `-object memory-backend-ram`.

This creates:
- 2 NUMA nodes
- CPUs 0,1 on node 0 with 256MB
- CPUs 2,3 on node 1 with 256MB
- Distance of 20 between nodes (local = 10)
- ACPI SRAT and SLIT tables populated

### Verification Points

1. **SRAT Parse**: Log "Found N NUMA nodes" with memory ranges
2. **SLIT Parse**: Log distance matrix
3. **CPU Assignment**: Each CPU reports correct `NumaNode`
4. **Memory Classification**: Per-node page counts
5. **Allocation**: Test allocating from specific nodes

### Test Code

```csharp
// In Kernel.cs or a test module
public static void TestNuma()
{
    DebugConsole.WriteLine("[NUMA Test] Starting...");

    int nodeCount = NumaTopology.NodeCount;
    DebugConsole.Write("[NUMA Test] Node count: ");
    DebugConsole.WriteDecimal(nodeCount);
    DebugConsole.WriteLine();

    for (int i = 0; i < nodeCount; i++)
    {
        var node = NumaTopology.GetNode(i);
        if (node != null && node->IsValid)
        {
            DebugConsole.Write("[NUMA Test] Node ");
            DebugConsole.WriteDecimal(i);
            DebugConsole.Write(": ");
            DebugConsole.WriteDecimal((int)(node->MemorySize / (1024 * 1024)));
            DebugConsole.Write(" MB, ");
            DebugConsole.WriteDecimal((int)node->CpuCount);
            DebugConsole.WriteLine(" CPUs");
        }
    }

    // Test allocation from each node
    for (int i = 0; i < nodeCount; i++)
    {
        ulong page = PageAllocator.AllocatePageFromNode((uint)i);
        if (page != 0)
        {
            DebugConsole.Write("[NUMA Test] Allocated page 0x");
            DebugConsole.WriteHex(page);
            DebugConsole.Write(" from node ");
            DebugConsole.WriteDecimal(i);
            DebugConsole.WriteLine();
            PageAllocator.FreePage(page);
        }
    }
}
```

---

## Implementation Order

```
1. Phase 1: SRAT/SLIT Structures
   └─ Test: FindSrat()/FindSlit() return valid pointers with QEMU NUMA

2. Phase 2: NumaTopology Module
   └─ Test: Log detected nodes, memory ranges, distances

3. Phase 3: CpuInfo.NumaNode
   └─ Test: Each CPU reports correct NUMA node

4. Phase 4: PageAllocator NUMA Support
   └─ Test: Per-node free counts, allocate from specific node

5. Phase 5: Thread/PerCpu NUMA Preference
   └─ Test: Allocation prefers current CPU's node

6. Phase 6: DDK Exports
   └─ Test: JIT code can query NUMA topology
```

---

## Files Summary

### New Files
| File | Description |
|------|-------------|
| `src/kernel/Platform/NumaTopology.cs` | NUMA node enumeration and distance matrix |
| `src/kernel/Exports/NUMA.cs` | DDK exports for NUMA APIs |

### Modified Files
| File | Changes |
|------|---------|
| `src/kernel/Platform/ACPI.cs` | Add SRAT/SLIT structures and FindSrat/FindSlit |
| `src/kernel/Platform/CPUTopology.cs` | Add NumaNode field to CpuInfo |
| `src/kernel/Memory/PageAllocator.cs` | Add per-node tracking and AllocatePageFromNode |
| `src/kernel/Threading/PerCpuState.cs` | Add PreferredNumaNode field |
| `src/kernel/Threading/Thread.cs` | Add PreferredNumaNode field |

---

## Non-NUMA Fallback

When SRAT is not present (no NUMA system):
- `NumaTopology.NodeCount` = 1
- All CPUs belong to node 0
- All memory belongs to node 0
- `GetNodeDistance(0, 0)` = 10 (local)
- Allocation works normally (single node)

This ensures the code works on non-NUMA systems without special casing.

---

## Future Considerations (Not in Initial Scope)

- **NUMA-aware GC heap**: Allocate GC regions from local node
- **NUMA-aware scheduler**: Prefer scheduling on CPUs near thread's memory
- **Memory migration**: Move pages between nodes for better locality
- **Heterogeneous memory**: Support for HBM, persistent memory (NVDIMM)
- **Hot-plug memory**: Dynamic NUMA topology changes
