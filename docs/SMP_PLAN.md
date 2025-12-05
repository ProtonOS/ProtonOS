# SMP Support Implementation Plan

## Overview

Enable Symmetric Multi-Processing (SMP) in ProtonOS so all CPU cores run managed threads before JIT execution begins. This is AOT-side kernel work following the existing static abstract interface pattern.

## Design Decisions

- **Scheduler**: Per-CPU queues with work stealing (not global queue)
- **I/O APIC**: Include from the start for proper interrupt distribution
- **ARM64**: Design interfaces with ARM64 in mind (PSCI/spin-tables abstraction)
- **DDK Export**: Follow DDK pattern for exposing CPU topology to JIT

---

## Phase 1: MADT Parsing & CPU Topology

### New File: `src/kernel/Platform/CPUTopology.cs`

```csharp
public struct CpuInfo {
    public uint CpuIndex;      // Kernel-assigned (0, 1, 2...)
    public uint ApicId;        // Hardware APIC ID
    public bool IsBsp;         // Bootstrap Processor
    public bool IsOnline;      // Currently running
}

public static class CPUTopology {
    public const int MaxCpus = 64;
    public static int CpuCount { get; }
    public static int BspIndex { get; }
    public static CpuInfo* GetCpu(int index);
    public static void Init();  // Parse MADT
}
```

### Modify: `src/kernel/Platform/ACPI.cs`

Add MADT structures and `FindMadt()`:
- `ACPIMADT` - MADT table header (signature "APIC")
- `MADTEntryHeader` - Type + Length for each entry
- `MADTLocalApic` (Type 0) - Processor Local APIC entry
- `MADTLocalX2Apic` (Type 9) - For >255 CPUs
- `MADTIOAPIC` (Type 1) - I/O APIC entry
- `MADTInterruptSourceOverride` (Type 2) - IRQ remapping

---

## Phase 2: Per-CPU State Infrastructure

### New File: `src/kernel/Threading/PerCpuState.cs`

```csharp
[StructLayout(LayoutKind.Sequential)]
public unsafe struct PerCpuState {
    public PerCpuState* Self;           // GS:0 self-pointer for fast access
    public uint CpuIndex;
    public uint ApicId;

    // Scheduler state (per-CPU queue)
    public Thread* CurrentThread;
    public Thread* ReadyQueueHead;
    public Thread* ReadyQueueTail;
    public SpinLock SchedulerLock;
    public Thread* IdleThread;

    // Stats
    public ulong TickCount;
    public int InterruptDepth;
}

public static class PerCpu {
    public static PerCpuState* Current { get; }  // Via GS base
    public static Thread* CurrentThread => Current->CurrentThread;
    public static uint CpuIndex => Current->CpuIndex;
}
```

### Modify: `src/kernel/x64/CPU.cs`

Add GS/KERNEL_GS segment base MSR access:
- `SetGsBase(ulong)` / `GetGsBase()` - MSR 0xC0000101
- `SetKernelGsBase(ulong)` / `GetKernelGsBase()` - MSR 0xC0000102

---

## Phase 3: AP Startup (x64)

### Modify: `src/kernel/x64/native.asm`

Add AP trampoline code (~200 lines):
1. Real-mode entry at 0x8000 (copied there by BSP)
2. Load temporary GDT, enable protected mode
3. Enable PAE, load CR3 (shared page tables)
4. Enable long mode via IA32_EFER
5. Enable paging, jump to 64-bit code
6. Reload 64-bit GDT, set up stack
7. Set GS base to per-CPU state
8. Call C# `ApEntry(PerCpuState*)`

Data structure for BSP→AP communication:
```nasm
ap_startup_data:
    .gdt_ptr:   dq 0    ; Pointer to GDT
    .pml4:      dq 0    ; CR3 value
    .stack:     dq 0    ; Per-AP stack pointer
    .percpu:    dq 0    ; Per-CPU state pointer
    .entry:     dq 0    ; C# ApEntry address
    .ap_ready:  dd 0    ; Signaled when AP is ready
```

### Modify: `src/kernel/x64/APIC.cs`

Add IPI support:
- `SendIpi(uint apicId, uint vector)` - Fixed delivery
- `SendInitIpi(uint apicId)` - INIT IPI for AP reset
- `SendStartupIpi(uint apicId, byte vector)` - SIPI with trampoline page
- `BroadcastIpi(uint vector)` - Send to all other CPUs

ICR (Interrupt Command Register) constants:
- `ICR_DELIVERY_FIXED`, `ICR_DELIVERY_INIT`, `ICR_DELIVERY_STARTUP`
- `ICR_DEST_PHYSICAL`, `ICR_LEVEL_ASSERT`

### New File: `src/kernel/x64/SMP.cs`

AP startup orchestration:
```csharp
public static class SMP {
    public static void Init() {
        CPUTopology.Init();
        if (CpuCount <= 1) return;

        AllocatePerCpuStates();
        SetupBspPerCpuState();
        CopyTrampoline();
        StartApplicationProcessors();
        WaitForApsReady();
    }

    private static void StartAp(CpuInfo* cpu) {
        SetupApStartupData(cpu);
        APIC.SendInitIpi(cpu->ApicId);
        BusyWait(10ms);
        APIC.SendStartupIpi(cpu->ApicId, 0x08);  // Trampoline at 0x8000
        BusyWait(1ms);
        APIC.SendStartupIpi(cpu->ApicId, 0x08);  // Send twice per Intel spec
    }

    [UnmanagedCallersOnly(EntryPoint = "ApEntry")]
    public static void ApEntry(PerCpuState* perCpu) {
        // Initialize this AP's GDT, IDT
        // Initialize Local APIC
        // Create idle thread
        // Signal ready
        // Enter scheduler idle loop
    }
}
```

---

## Phase 4: I/O APIC Configuration

### New File: `src/kernel/x64/IOAPIC.cs`

```csharp
public static class IOAPIC {
    public static void Init();  // Find via MADT, configure
    public static void SetIrqRoute(int irq, int vector, uint destApicId);
    public static void MaskIrq(int irq);
    public static void UnmaskIrq(int irq);
}
```

I/O APIC MMIO registers:
- IOREGSEL (0x00) - Register select
- IOWIN (0x10) - Register data window
- Redirection Table entries (24 entries, 64-bit each)

Parse MADT for:
- I/O APIC base address
- Interrupt Source Overrides (ISA IRQ remapping)

---

## Phase 5: Scheduler Updates

### Modify: `src/kernel/Threading/Scheduler.cs`

Major changes for per-CPU queues:

```csharp
// Remove global _currentThread, _readyQueue*
// Access via PerCpu.Current instead

public static Thread* CurrentThread => PerCpu.CurrentThread;

public static void Init() {
    // Create boot thread on BSP
    // Other CPUs create idle threads in ApEntry
}

public static void InitSecondaryCpu() {
    // Called by each AP after startup
    // Create idle thread for this CPU
    // Set PerCpu.Current->IdleThread
}

public static void Schedule() {
    var perCpu = PerCpu.Current;
    perCpu->SchedulerLock.Acquire();

    // Try local queue first
    var next = perCpu->ReadyQueueHead;

    // Work stealing if local queue empty
    if (next == null) {
        next = StealWork();
    }

    // Fall back to idle thread
    if (next == null) {
        next = perCpu->IdleThread;
    }

    // Context switch...
}

private static Thread* StealWork() {
    // Try to steal from other CPUs' queues
    // Round-robin through other CPUs
    // Steal from tail (opposite end from local dequeue)
}

public static void MakeReady(Thread* thread) {
    // Add to appropriate CPU's queue based on affinity
    // Send reschedule IPI if target CPU is idle
}
```

### Modify: `src/kernel/Threading/Thread.cs`

Add SMP fields:
```csharp
public ulong CpuAffinity;    // Bitmask of allowed CPUs (0 = any)
public uint LastCpu;         // Last CPU this thread ran on
public uint PreferredCpu;    // Preferred CPU for cache affinity
```

### New: Reschedule IPI Handler

Vector 0xFD for cross-CPU reschedule:
```csharp
private static void RescheduleIpiHandler(InterruptFrame* frame) {
    APIC.SendEoi();
    Schedule();  // Check for new work
}
```

---

## Phase 6: Interface Extensions

### Modify: `src/kernel/Arch/IArchitecture.cs`

Add SMP-related methods (designed for ARM64 compatibility):

```csharp
public interface IArchitecture<TSelf> {
    // ... existing methods ...

    // SMP support
    static abstract int CpuCount { get; }
    static abstract int CurrentCpuIndex { get; }
    static abstract bool IsBsp { get; }

    static abstract void InitSecondaryCpu(int cpuIndex);
    static abstract void StartSecondaryCpus();

    // IPI
    static abstract void SendIpi(int targetCpu, int vector);
    static abstract void BroadcastIpi(int vector);  // All except self
}
```

### Modify: `src/kernel/Arch/ICpu.cs`

Add per-CPU state access:
```csharp
public interface ICpu<TSelf> {
    // ... existing methods ...

    static abstract ulong GetPerCpuStateBase();
    static abstract void SetPerCpuStateBase(ulong address);
}
```

---

## Phase 7: Boot Sequence Changes

### Modify: `src/kernel/Kernel.cs`

Updated boot flow:
```
Kernel.Main()
├─ PageAllocator.Init()
├─ ACPI.Init()
├─ ExitBootServices()
├─ Arch.InitStage1()
│   ├─ GDT, IDT, VirtualMemory
│   └─ CPUTopology.Init()  // NEW: Parse MADT
├─ HeapAllocator.Init()
├─ GCHeap.Init()
├─ Scheduler.Init()        // Creates BSP boot thread
├─ Arch.InitStage2()
│   ├─ HPET, RTC, APIC (BSP)
│   ├─ IOAPIC.Init()       // NEW: Configure I/O APIC
│   └─ SMP.Init()          // NEW: Start all APs
│       ├─ AllocatePerCpuStates()
│       ├─ SetupBspPerCpuState()
│       ├─ For each AP:
│       │   ├─ SendInitIpi()
│       │   ├─ SendStartupIpi() x2
│       │   └─ AP executes: ApEntry()
│       │       ├─ Init Local APIC
│       │       ├─ Scheduler.InitSecondaryCpu()
│       │       └─ Enter idle loop
│       └─ WaitForApsReady()
├─ Scheduler.EnableScheduling()
└─ JIT execution (all CPUs available)
```

---

## Phase 8: DDK Exports

### New File: `src/kernel/Exports/CPU.cs`

Expose CPU topology to JIT drivers:
```csharp
public static class CPUExports {
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCpuCount")]
    public static int GetCpuCount() => CPUTopology.CpuCount;

    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCurrentCpu")]
    public static int GetCurrentCpu() => (int)PerCpu.CpuIndex;

    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCpuInfo")]
    public static bool GetCpuInfo(int index, CpuInfo* info);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_SetThreadAffinity")]
    public static ulong SetThreadAffinity(ulong mask);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetThreadAffinity")]
    public static ulong GetThreadAffinity();
}
```

---

## Files to Create

| File | Purpose |
|------|---------|
| `src/kernel/Platform/CPUTopology.cs` | CPU enumeration from MADT |
| `src/kernel/Threading/PerCpuState.cs` | Per-CPU state structure |
| `src/kernel/x64/SMP.cs` | AP startup orchestration |
| `src/kernel/x64/IOAPIC.cs` | I/O APIC driver |
| `src/kernel/Exports/CPU.cs` | DDK exports for CPU info |

## Files to Modify

| File | Changes |
|------|---------|
| `src/kernel/Platform/ACPI.cs` | Add MADT structures, `FindMadt()` |
| `src/kernel/x64/native.asm` | Add AP trampoline (~200 lines) |
| `src/kernel/x64/APIC.cs` | Add IPI support (SendIpi, SendInitIpi, SendStartupIpi) |
| `src/kernel/x64/CPU.cs` | Add GS/KERNEL_GS base MSR access |
| `src/kernel/x64/Arch.cs` | Integrate SMP init, implement new interface methods |
| `src/kernel/Arch/IArchitecture.cs` | Add SMP interface methods |
| `src/kernel/Arch/ICpu.cs` | Add per-CPU state base access |
| `src/kernel/Threading/Scheduler.cs` | Per-CPU queues, work stealing |
| `src/kernel/Threading/Thread.cs` | Add affinity fields |
| `src/kernel/Kernel.cs` | Add SMP init to boot sequence |

---

## Implementation Order

```
1. MADT Parsing (ACPI.cs, CPUTopology.cs)
   └─ Can test: Log detected CPU count

2. Per-CPU State (PerCpuState.cs, CPU.cs GS base)
   └─ Can test: BSP uses per-CPU state

3. AP Trampoline (native.asm, APIC.cs IPI, SMP.cs)
   └─ Can test: APs reach ApEntry, log their APIC IDs

4. I/O APIC (IOAPIC.cs, MADT parsing for IOAPIC)
   └─ Can test: Timer interrupts still work

5. Scheduler Updates (Scheduler.cs, Thread.cs)
   └─ Can test: Threads run on different CPUs

6. Interface Extensions (IArchitecture.cs, ICpu.cs, Arch.cs)
   └─ Can test: Clean compile, ARM64 interface ready

7. DDK Exports (Exports/CPU.cs)
   └─ Can test: JIT code can query CPU count
```

---

## Testing Strategy

### QEMU Command
```bash
qemu-system-x86_64 -smp 4 ...
```

### Verification Points
1. **MADT Parse**: Log "Detected N CPUs" with APIC IDs
2. **AP Startup**: Each AP logs "CPU N online"
3. **Per-CPU State**: Each CPU reports correct index via GS
4. **Scheduler**: Test threads log which CPU they're running on
5. **Work Stealing**: Create more threads than CPUs, verify distribution

### Test Thread
```csharp
[UnmanagedCallersOnly]
static uint SmpTestThread(void* param) {
    for (int i = 0; i < 5; i++) {
        DebugConsole.Write("Thread on CPU ");
        DebugConsole.WriteDecimal(PerCpu.CpuIndex);
        Scheduler.Yield();
    }
    return 0;
}
```

---

## Future Considerations (Not in Scope)

- **NUMA awareness**: Memory locality for large systems
- **CPU hotplug**: Adding/removing CPUs at runtime
- **Power management**: CPU parking, frequency scaling
- **proc filesystem**: `/proc/cpuinfo` via future VFS (design considered)
