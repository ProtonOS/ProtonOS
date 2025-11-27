# ManagedKernel Architecture Document

**Project:** Hybrid Managed Operating System Kernel  
**Version:** 0.1 (Design Phase)  
**Last Updated:** November 2024  

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Design Philosophy](#2-design-philosophy)
3. [System Architecture Overview](#3-system-architecture-overview)
4. [Hardware Abstraction Layer (HAL)](#4-hardware-abstraction-layer-hal)
5. [Architecture-Specific Implementations](#5-architecture-specific-implementations)
6. [Boot Protocol Abstraction](#6-boot-protocol-abstraction)
7. [Native Code Requirements](#7-native-code-requirements)
8. [AOT Kernel Components](#8-aot-kernel-components)
9. [JIT Runtime Integration (Future)](#9-jit-runtime-integration-future)
10. [Console and I/O Model](#10-console-and-io-model)
11. [Project Structure](#11-project-structure)
12. [Implementation Phases](#12-implementation-phases)

---

## 1. Executive Summary

ManagedKernel is an operating system kernel where the core functionality is written in C# using .NET's Native AOT compilation. The design follows a hybrid architecture:

- **Minimal Native Layer**: ~150-200 lines of assembly per architecture providing CPU intrinsics
- **AOT Managed Kernel**: Core kernel services compiled ahead-of-time via bflat/NativeAOT
- **JIT Extensions (Future)**: Hot-loadable drivers and modules compiled at runtime

The key insight driving this architecture is that **C is unnecessary**. Every operation traditionally done in C can be done in C# with `unsafe` code, while privileged CPU instructions require assembly regardless of whether the caller is C or C#.

---

## 2. Design Philosophy

### 2.1 Core Principles

1. **Minimal Native Code**
   - Only assembly where CPU instructions cannot be expressed in any high-level language
   - No C code in the kernel
   - Native layer provides primitives; all logic lives in managed code

2. **Architecture Independence**
   - Common interfaces (HAL) define kernel services
   - Architecture-specific code isolated in separate modules
   - Adding a new architecture is additive, not invasive

3. **Linux-Like Console Model**
   - Serial/TTY-style output from earliest boot
   - Virtual terminals with framebuffer (later)
   - SSH access for remote management (future)

4. **Stable Core, Dynamic Extensions**
   - AOT kernel rarely changes after stabilization
   - Drivers and extensions can be loaded/unloaded at runtime via JIT

### 2.2 Why No C?

| Capability | C | C# (unsafe) | Assembly |
|------------|---|-------------|----------|
| Pointer arithmetic | ✅ | ✅ | ✅ |
| Memory-mapped I/O | ✅ | ✅ | ✅ |
| Struct layout control | ✅ | ✅ (`StructLayout`) | ✅ |
| Calling conventions | ✅ | ✅ (`UnmanagedCallersOnly`) | ✅ |
| Port I/O (`in`/`out`) | ❌ (needs inline asm) | ❌ | ✅ |
| Control registers | ❌ (needs inline asm) | ❌ | ✅ |
| Privileged instructions | ❌ | ❌ | ✅ |

C provides no capabilities that C# with `unsafe` doesn't have, while privileged CPU operations require assembly in both cases. Eliminating C:
- Reduces toolchain complexity (no cross-compiler needed)
- Eliminates a language boundary
- Provides consistent coding patterns throughout

---

## 3. System Architecture Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Boot Firmware                                   │
│                     (UEFI / m1n1 / U-Boot / other)                          │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Kernel.Boot.*                                      │
│                  (Boot protocol specific initialization)                     │
│         ┌─────────────────┬─────────────────┬─────────────────┐            │
│         │  Kernel.Boot    │  Kernel.Boot    │  Kernel.Boot    │            │
│         │     .UEFI       │     .M1N1       │    .UBoot       │            │
│         └─────────────────┴─────────────────┴─────────────────┘            │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Kernel.Arch.*                                      │
│                (Architecture-specific implementations)                       │
│    ┌────────────────────┬────────────────────┬────────────────────┐        │
│    │  Kernel.Arch.X64   │ Kernel.Arch.Arm64  │ Kernel.Arch.Apple  │        │
│    │  ┌──────────────┐  │  ┌──────────────┐  │  ┌──────────────┐  │        │
│    │  │ native_x64   │  │  │ native_arm64 │  │  │ native_apple │  │        │
│    │  │    .asm      │  │  │    .asm      │  │  │    .asm      │  │        │
│    │  │  (~150 LOC)  │  │  │  (~120 LOC)  │  │  │  (~150 LOC)  │  │        │
│    │  └──────────────┘  │  └──────────────┘  │  └──────────────┘  │        │
│    └────────────────────┴────────────────────┴────────────────────┘        │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            Kernel.HAL                                        │
│                   (Hardware Abstraction Layer Interfaces)                    │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  IArchitecture    IInterruptController    IPageTableManager         │   │
│  │  ITimer           ICpuServices            ISerialPort               │   │
│  │  IBootServices    IMemoryMap              ICacheControl             │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           Kernel.Core                                        │
│              (Architecture-independent kernel services)                      │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  PhysicalMemory    VirtualMemory    Heap/GC        Scheduler        │   │
│  │  Console           DriverManager    Syscalls       IPC              │   │
│  │  VFS               ProcessManager   ModuleLoader   ...              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼ (Future)
┌─────────────────────────────────────────────────────────────────────────────┐
│                         JIT Runtime (Future)                                 │
│              (Hot-loadable drivers and kernel modules)                       │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  RyuJIT Integration    Assembly Loader    Driver Sandbox            │   │
│  │  Network Drivers       Storage Drivers    Filesystem Modules        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. Hardware Abstraction Layer (HAL)

The HAL defines architecture-independent interfaces that the core kernel uses. Each supported architecture provides implementations.

### 4.1 Core Interfaces

#### IArchitecture
```csharp
public interface IArchitecture
{
    ArchitectureType Type { get; }  // X64, Arm64, AppleSilicon
    
    void EarlyInit(IBootServices bootServices);  // Minimal init for serial
    void Initialize();                            // Full initialization
    
    IInterruptController InterruptController { get; }
    IPageTableManager PageTableManager { get; }
    ITimer Timer { get; }
    ICpuServices Cpu { get; }
}
```

#### IInterruptController
```csharp
public interface IInterruptController
{
    void Initialize();
    void RegisterHandler(uint vector, InterruptHandler handler);
    void UnregisterHandler(uint vector);
    void EnableInterrupt(uint vector);
    void DisableInterrupt(uint vector);
    void EndOfInterrupt(uint vector);
    void EnableInterrupts();
    bool DisableInterrupts();  // Returns previous state
    void RestoreInterrupts(bool previousState);
}
```

#### IPageTableManager
```csharp
public interface IPageTableManager
{
    PageSize DefaultPageSize { get; }
    
    nint CreateAddressSpace();
    void DestroyAddressSpace(nint addressSpace);
    nint GetCurrentAddressSpace();
    void SwitchAddressSpace(nint addressSpace);
    
    void Map(nint space, ulong virt, ulong phys, PageFlags flags, PageSize size);
    void Unmap(nint space, ulong virt, PageSize size);
    void Protect(nint space, ulong virt, PageFlags flags);
    ulong? Translate(nint space, ulong virt);
    
    void InvalidatePage(ulong virt);
    void InvalidateAll();
}
```

#### ICpuServices
```csharp
public interface ICpuServices
{
    int ProcessorCount { get; }
    int CurrentProcessor { get; }
    CpuFeatures Features { get; }
    
    void Halt();           // Wait for interrupt
    void SpinWait();       // Pause hint for spinlocks
    void MemoryBarrier();  // Full barrier
    void ReadBarrier();
    void WriteBarrier();
    
    ulong ReadTimestamp();
    ulong TimestampFrequency { get; }
}
```

#### ITimer
```csharp
public interface ITimer
{
    void Initialize();
    ulong Frequency { get; }
    ulong CurrentTicks { get; }
    
    void SetPeriodicTimer(uint frequencyHz, TimerCallback callback);
    void SetOneShotTimer(ulong delayTicks, TimerCallback callback);
    void Stop();
    void BusyWaitNanoseconds(ulong ns);
}
```

#### ISerialPort
```csharp
public interface ISerialPort
{
    void Initialize(uint baudRate = 115200);
    void WriteByte(byte b);
    void Write(ReadOnlySpan<char> text);
    void WriteLine(ReadOnlySpan<char> text);
    byte ReadByte();
    bool TryReadByte(out byte b);
    bool DataAvailable { get; }
    bool TransmitReady { get; }
}
```

#### IBootServices
```csharp
public interface IBootServices
{
    BootProtocol Protocol { get; }
    MemoryMapEntry[] GetMemoryMap();
    FramebufferInfo? GetFramebuffer();
    nint GetConfigurationTable(ConfigTableType type);  // ACPI, DeviceTree, etc.
    void ExitBootServices();
}
```

### 4.2 Architecture Comparison

| Feature | x86_64 | ARM64 | Apple Silicon |
|---------|--------|-------|---------------|
| Interrupt Controller | APIC/PIC | GIC (v2/v3) | AIC |
| Timer | APIC Timer, HPET | Generic Timer | Generic Timer |
| Page Table Format | 4-level (PML4) | 4-level (TTBR) | 4-level (Apple variant) |
| Serial | 16550 UART (port I/O) | PL011 (MMIO) | Custom (MMIO) |
| Port I/O | Yes (`in`/`out`) | No (MMIO only) | No (MMIO only) |
| Segmentation | GDT (vestigial) | None | None |
| Configuration | ACPI | ACPI or DeviceTree | Apple DeviceTree |

---

## 5. Architecture-Specific Implementations

### 5.1 x86_64 (Kernel.Arch.X64)

#### Native Functions Required (~150 LOC ASM)

| Category | Functions |
|----------|-----------|
| Port I/O | `outb`, `outw`, `outd`, `inb`, `inw`, `ind` |
| Control Registers | `read_cr0`, `write_cr0`, `read_cr2`, `read_cr3`, `write_cr3`, `read_cr4`, `write_cr4` |
| Descriptor Tables | `lgdt`, `lidt`, `ltr` |
| Interrupts | `cli`, `sti`, `save_flags_cli`, `restore_flags` |
| CPU Control | `hlt`, `pause` |
| TLB | `invlpg`, `flush_tlb` |
| MSR | `rdmsr`, `wrmsr` |
| CPUID | `cpuid` |
| Timestamp | `rdtsc`, `rdtscp` |
| Barriers | `mfence`, `lfence`, `sfence` |
| ISR Stubs | 256 interrupt entry points + common handler |

#### Key Components (C#)

- **GDT.cs**: Global Descriptor Table setup (structures in C#, `lgdt` in ASM)
- **IDT.cs**: Interrupt Descriptor Table setup
- **Interrupts.cs**: Handler dispatch, `managed_interrupt_dispatch` entry point
- **X64PageTable.cs**: 4-level page table management
- **X64InterruptController.cs**: APIC/PIC driver
- **X64Timer.cs**: APIC Timer / PIT
- **Serial16550.cs**: 16550 UART driver (uses port I/O)

### 5.2 ARM64 (Kernel.Arch.Arm64)

#### Native Functions Required (~120 LOC ASM)

| Category | Functions |
|----------|-----------|
| System Registers | `read_*/write_*` for SCTLR_EL1, TCR_EL1, TTBR0/1_EL1, MAIR_EL1, VBAR_EL1, ESR_EL1, FAR_EL1, ELR_EL1, SPSR_EL1 |
| ID Registers | `read_midr_el1`, `read_id_aa64pfr0_el1`, etc. |
| Interrupts | `enable_interrupts`, `disable_interrupts`, `save_and_disable`, `restore` |
| CPU Control | `wfi`, `wfe`, `sev`, `yield` |
| TLB | `tlbi_vmalle1`, `tlbi_vale1` |
| Barriers | `dmb_sy`, `dmb_ld`, `dmb_st`, `dsb_sy`, `isb` |
| Cache | `dc_civac`, `ic_iallu` |
| Timer | `read_cntpct_el0`, `read_cntfrq_el0`, `write_cntp_ctl_el0`, `write_cntp_tval_el0` |
| Exception Vectors | Vector table + entry/exit macros |

#### Key Differences from x86_64

- **No Port I/O**: All device access is memory-mapped
- **No GDT**: ARM64 doesn't use segmentation
- **Exception Levels**: EL0-EL3 instead of Ring 0-3
- **Vector Table**: Single table with entries for Sync/IRQ/FIQ/SError at each EL
- **Generic Timer**: Architecturally defined, no PIT/HPET equivalent

### 5.3 Apple Silicon (Kernel.Arch.Apple) - Future

Apple Silicon uses ARM64 but with proprietary peripherals:

- **AIC**: Apple Interrupt Controller (not GIC)
- **ADT**: Apple Device Tree (not standard DeviceTree)
- **Boot**: m1n1 bootloader (not UEFI)
- **Display**: DCP (Display Coprocessor)
- **Storage**: ANS (Apple NVMe)

This architecture will share ARM64 CPU intrinsics but have different peripheral drivers.

---

## 6. Boot Protocol Abstraction

### 6.1 Supported Boot Protocols

| Protocol | Architectures | Status |
|----------|---------------|--------|
| UEFI | x86_64, ARM64 | Primary target |
| m1n1 | Apple Silicon | Future |
| U-Boot | ARM64 boards | Future |

### 6.2 UEFI Boot (Kernel.Boot.UEFI)

UEFI provides:
- 64-bit long mode (x64) or EL1 (ARM64) already configured
- Identity-mapped page tables
- Memory map via `GetMemoryMap()`
- Framebuffer via GOP
- ACPI tables pointer
- FAT32 filesystem access (before ExitBootServices)

#### Boot Sequence

```
EfiMain(ImageHandle, SystemTable)
    │
    ├─► Early serial init (via UEFI ConOut or direct)
    │
    ├─► Get memory map
    │
    ├─► Get framebuffer info (optional)
    │
    ├─► Get ACPI RSDP
    │
    ├─► ExitBootServices() ─── Point of no return
    │
    ├─► Architecture.Initialize()
    │   ├─► Set up own GDT/IDT (x64) or VBAR (ARM64)
    │   ├─► Initialize interrupt controller
    │   ├─► Initialize timer
    │   └─► Set up kernel page tables
    │
    └─► KernelMain()
```

### 6.3 m1n1 Boot (Future)

m1n1 is Asahi Linux's bootloader for Apple Silicon. It provides:
- Device tree describing hardware
- Framebuffer
- Serial console
- Memory regions

---

## 7. Native Code Requirements

### 7.1 Complete x86_64 Native Layer

```asm
; native_x64.asm - Complete native layer for x86_64 (~150 lines)

BITS 64
DEFAULT REL

section .text

;; ==================== Port I/O ====================
global outb, outw, outd, inb, inw, ind

outb:   ; void outb(u16 port, u8 value) - port in CX, value in DL
    mov eax, edx
    mov dx, cx
    out dx, al
    ret

outw:   ; void outw(u16 port, u16 value)
    mov eax, edx
    mov dx, cx
    out dx, ax
    ret

outd:   ; void outd(u16 port, u32 value)
    mov eax, edx
    mov dx, cx
    out dx, eax
    ret

inb:    ; u8 inb(u16 port)
    mov dx, cx
    in al, dx
    movzx eax, al
    ret

inw:    ; u16 inw(u16 port)
    mov dx, cx
    in ax, dx
    movzx eax, ax
    ret

ind:    ; u32 ind(u16 port)
    mov dx, cx
    in eax, dx
    ret

;; ==================== Control Registers ====================
global read_cr0, write_cr0, read_cr2, read_cr3, write_cr3, read_cr4, write_cr4

read_cr0:   mov rax, cr0 / ret
write_cr0:  mov cr0, rcx / ret
read_cr2:   mov rax, cr2 / ret
read_cr3:   mov rax, cr3 / ret
write_cr3:  mov cr3, rcx / ret
read_cr4:   mov rax, cr4 / ret
write_cr4:  mov cr4, rcx / ret

;; ==================== Descriptor Tables ====================
global lgdt, lidt, ltr

lgdt:       ; void lgdt(void* gdtr)
    lgdt [rcx]
    mov ax, 0x10        ; Data segment
    mov ds, ax
    mov es, ax
    mov fs, ax
    mov gs, ax
    mov ss, ax
    pop rax             ; Return address
    push qword 0x08     ; Code segment
    push rax
    retfq

lidt:       ; void lidt(void* idtr)
    lidt [rcx]
    ret

ltr:        ; void ltr(u16 selector)
    ltr cx
    ret

;; ==================== Interrupts ====================
global cli, sti, save_flags_cli, restore_flags, hlt, pause

cli:            cli / ret
sti:            sti / ret
hlt:            hlt / ret
pause:          pause / ret

save_flags_cli: ; u64 save_flags_cli()
    pushfq
    cli
    pop rax
    ret

restore_flags:  ; void restore_flags(u64 flags)
    push rcx
    popfq
    ret

;; ==================== TLB ====================
global invlpg, flush_tlb

invlpg:     ; void invlpg(void* addr)
    invlpg [rcx]
    ret

flush_tlb:
    mov rax, cr3
    mov cr3, rax
    ret

;; ==================== MSR ====================
global rdmsr, wrmsr

rdmsr:      ; u64 rdmsr(u32 msr)
    rdmsr
    shl rdx, 32
    or rax, rdx
    ret

wrmsr:      ; void wrmsr(u32 msr, u64 value)
    mov eax, edx
    shr rdx, 32
    wrmsr
    ret

;; ==================== Other ====================
global rdtsc, mfence, lfence, sfence, cpuid, get_isr_table

rdtsc:
    rdtsc
    shl rdx, 32
    or rax, rdx
    ret

mfence:     mfence / ret
lfence:     lfence / ret
sfence:     sfence / ret

cpuid:      ; void cpuid(u32 leaf, u32* eax, u32* ebx, u32* ecx, u32* edx)
    push rbx
    mov eax, ecx
    mov r10, rdx        ; eax out
    mov r11, r8         ; ebx out
    cpuid
    mov [r10], eax
    mov [r11], ebx
    mov [r9], ecx
    mov rax, [rsp+48]   ; edx out (stack arg)
    mov [rax], edx
    pop rbx
    ret

get_isr_table:
    lea rax, [isr_table]
    ret

;; ==================== ISR Stubs ====================
extern managed_interrupt_dispatch

%macro ISR_NOERRCODE 1
global isr%1
isr%1:
    push qword 0
    push qword %1
    jmp isr_common
%endmacro

%macro ISR_ERRCODE 1
global isr%1
isr%1:
    push qword %1
    jmp isr_common
%endmacro

isr_common:
    push rax
    push rbx
    push rcx
    push rdx
    push rsi
    push rdi
    push rbp
    push r8
    push r9
    push r10
    push r11
    push r12
    push r13
    push r14
    push r15
    
    mov rcx, rsp
    mov rbp, rsp
    and rsp, ~0xF
    sub rsp, 32
    
    call managed_interrupt_dispatch
    
    mov rsp, rbp
    
    pop r15
    pop r14
    pop r13
    pop r12
    pop r11
    pop r10
    pop r9
    pop r8
    pop rbp
    pop rdi
    pop rsi
    pop rdx
    pop rcx
    pop rbx
    pop rax
    add rsp, 16
    iretq

; Generate all 256 ISR stubs
ISR_NOERRCODE 0
; ... (remaining 255 stubs via macros)

section .data
isr_table:
    dq isr0, isr1, isr2, ; ... (all 256 addresses)
```

### 7.2 What's NOT in Native Code

Everything else is in C#:

- GDT/IDT/TSS **structures** and setup **logic**
- Page table **structures** and mapping **logic**
- Interrupt **handlers** (dispatch logic, device handling)
- Memory management (physical allocator, virtual memory)
- All drivers
- Scheduler
- Everything in Kernel.Core

---

## 8. AOT Kernel Components

### 8.1 Memory Management

```csharp
// Physical memory - page frame allocator
public static class PhysicalMemory
{
    public static void Initialize(MemoryMapEntry[] map);
    public static nint AllocatePage();
    public static nint AllocatePages(int count);
    public static void FreePage(nint page);
    public static void FreePages(nint page, int count);
}

// Virtual memory - address space management
public static class VirtualMemory
{
    public static void Initialize(IPageTableManager ptm);
    public static void MapKernel();
    public static nint CreateUserSpace();
    // ...
}
```

### 8.2 Console Subsystem

```csharp
public static class Console
{
    private static ISerialPort _serial;
    
    public static void Initialize(ISerialPort serial) => _serial = serial;
    
    public static void Write(string text) => _serial.Write(text);
    public static void WriteLine(string text) => _serial.WriteLine(text);
    public static void WriteHex(ulong value) { /* ... */ }
}
```

### 8.3 Driver Infrastructure

```csharp
public interface IDriver
{
    string Name { get; }
    void Initialize();
    void Shutdown();
}

public static class DriverManager
{
    public static void RegisterDriver(IDriver driver);
    public static void InitializeAll();
}
```

---

## 9. JIT Runtime Integration (Future)

### 9.1 Purpose

The JIT runtime enables:
- Hot-loadable device drivers
- Runtime kernel module loading
- Future user-space application support

### 9.2 AOT vs JIT Code

| Aspect | AOT Code | JIT Code |
|--------|----------|----------|
| Compilation | Build time | Runtime |
| First call | Instant | Compilation delay |
| Subsequent calls | Same speed | Same speed |
| Cross-calls | Zero overhead | Zero overhead |
| Hot reload | Requires reboot | Instant |

**Key insight**: After JIT compilation, both AOT and JIT code are native machine code. Calling between them has zero overhead—it's just a `call` instruction.

### 9.3 What Must Be AOT

The AOT kernel provides infrastructure that JIT code depends on:

- Physical memory manager
- Virtual memory manager
- GC / Heap
- JIT compiler host (RyuJIT integration)
- Assembly loader
- Type system / metadata reader
- Exception handling runtime
- Interrupt handlers (dispatch)
- Core I/O (serial, console)

### 9.4 What Can Be JIT

Once AOT infrastructure is running:

- Device drivers (network, disk, USB)
- Filesystem implementations
- Network stack
- User-space services
- Shell / applications

---

## 10. Console and I/O Model

### 10.1 Output Channels by Boot Stage

| Stage | Method | Destination | Implementation |
|-------|--------|-------------|----------------|
| UEFI Boot | UEFI ConOut | Screen | `SystemTable->ConOut` |
| Post-ExitBootServices | Serial (COM1) | QEMU log | `Serial16550` / `Pl011Uart` |
| Post-Init | Virtual TTY | Framebuffer | Future |
| Networked | SSH | Network | Future |

### 10.2 QEMU Debug Integration

```batch
qemu-system-x86_64 ^
    -serial stdio ^                              # Kernel console to terminal
    -debugcon file:debug.log ^                   # OVMF debug to file
    -global isa-debugcon.iobase=0x402
```

---

## 11. Project Structure

```
ManagedKernel/
├── src/
│   ├── Kernel.HAL/                      # Interfaces only
│   │   ├── IArchitecture.cs
│   │   ├── IInterruptController.cs
│   │   ├── IPageTableManager.cs
│   │   ├── ICpuServices.cs
│   │   ├── ITimer.cs
│   │   ├── ISerialPort.cs
│   │   └── IBootServices.cs
│   │
│   ├── Kernel.Core/                     # Architecture-independent
│   │   ├── Kernel.cs                    # Entry point
│   │   ├── Memory/
│   │   │   ├── PhysicalMemory.cs
│   │   │   ├── VirtualMemory.cs
│   │   │   └── Heap.cs
│   │   ├── Console/
│   │   │   └── Console.cs
│   │   └── Drivers/
│   │       └── DriverManager.cs
│   │
│   ├── Kernel.Arch.X64/                 # x86_64 implementation
│   │   ├── X64Architecture.cs
│   │   ├── Native.cs                    # P/Invoke declarations
│   │   ├── GDT.cs
│   │   ├── IDT.cs
│   │   ├── Interrupts.cs
│   │   ├── X64PageTable.cs
│   │   ├── X64InterruptController.cs
│   │   ├── X64Timer.cs
│   │   ├── X64CpuServices.cs
│   │   └── Serial16550.cs
│   │
│   ├── Kernel.Arch.Arm64/               # ARM64 implementation
│   │   ├── Arm64Architecture.cs
│   │   ├── Native.cs
│   │   ├── ExceptionVectors.cs
│   │   ├── Arm64PageTable.cs
│   │   ├── GicV3.cs
│   │   ├── GenericTimer.cs
│   │   ├── Arm64CpuServices.cs
│   │   └── Pl011Uart.cs
│   │
│   ├── Kernel.Boot.UEFI/                # UEFI boot
│   │   ├── UefiBootServices.cs
│   │   ├── EfiTypes.cs
│   │   └── EfiMain.cs
│   │
│   └── Kernel.Boot.M1N1/                # Apple Silicon (future)
│       └── ...
│
├── arch/
│   ├── x64/
│   │   └── native_x64.asm               # ~150 lines
│   └── arm64/
│       └── native_arm64.asm             # ~120 lines
│
├── build/
│   ├── x64/
│   │   └── BOOTX64.EFI
│   └── arm64/
│       └── BOOTAA64.EFI
│
└── tools/                               # Build tools
    ├── bflat/
    ├── nasm/
    ├── lld/
    └── mtools/
```

---

## 12. Implementation Phases

### Phase 1: Minimal Boot (Weeks 1-4)
- [ ] UEFI entry point
- [ ] Serial console output
- [ ] Memory map parsing
- [ ] "Hello World" from managed code

### Phase 2: Core Infrastructure (Months 2-3)
- [ ] GDT/IDT setup
- [ ] Interrupt handling
- [ ] Physical memory allocator
- [ ] Basic page table management

### Phase 3: Kernel Services (Months 4-6)
- [ ] Virtual memory manager
- [ ] Simple heap allocator
- [ ] Timer and scheduling basics
- [ ] Keyboard input

### Phase 4: Driver Framework (Months 7-9)
- [ ] Driver interface design
- [ ] PCI enumeration
- [ ] Basic disk driver (AHCI)
- [ ] FAT32 filesystem

### Phase 5: JIT Integration (Months 10-12+)
- [ ] RyuJIT hosting
- [ ] Assembly loader
- [ ] Hot-loadable driver support
- [ ] GC coordination

---

## Appendix A: Preprocessor Definitions

```csharp
// Architecture selection
#if ARCH_X64
    // x86_64 specific code
#elif ARCH_ARM64
    // ARM64 specific code
#endif

// Boot protocol selection
#if BOOT_UEFI
    // UEFI boot
#elif BOOT_M1N1
    // Apple Silicon boot
#endif

// bflat defines these automatically:
// BFLAT, UEFI, WINDOWS, LINUX, X64, ARM64
```

## Appendix B: References

- **bflat**: https://flattened.net
- **zerosharp**: https://github.com/MichalStrehovsky/zerosharp
- **NativeAOT**: https://learn.microsoft.com/dotnet/core/deploying/native-aot
- **OSDev Wiki**: https://wiki.osdev.org
- **UEFI Specification**: https://uefi.org/specifications
- **ARM Architecture Reference Manual**: ARM DDI 0487
- **Intel SDM**: Intel 64 and IA-32 Architectures Software Developer's Manual
- **Asahi Linux**: https://asahilinux.org (Apple Silicon documentation)
