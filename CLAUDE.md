# netos - Claude Reference

## Project Overview

**netos** is a hybrid managed OS kernel written in C# using .NET Native AOT compilation. The key insight: **C is unnecessary** - everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless.

### Naming
- **netos** - The overall project/OS
- **nernel** - Native kernel (minimal assembly layer)
- **mernel** - Managed kernel (C# AOT layer)

### Architecture Layers
1. **nernel** (~150-200 LOC assembly per arch) - CPU intrinsics only
2. **mernel** - Core kernel in C# compiled via bflat/NativeAOT
3. **JIT Extensions (Future)** - Hot-loadable drivers/modules

## Build Environment

Docker-based development using `netos-dev` container:
- **Base:** Debian Bookworm (12) Slim
- **bflat:** 8.0.x - C# to Native AOT compiler
- **NASM:** 2.16.01 - x86_64 assembler (use `-f win64` for UEFI)
- **lld-link:** LLVM PE/COFF linker
- **QEMU:** x86_64/ARM64 emulation with OVMF firmware

### Build Commands
```bash
# Start dev shell (mounts project to /usr/src/netos)
./dev.sh

# Or manually:
docker run -it --rm -v "$(pwd):/usr/src/netos" -w /usr/src/netos netos-dev

# Inside container
./build.sh              # Build kernel
./run.sh                # Run in QEMU
DEBUG=1 ./run.sh        # Run with GDB server on port 1234
./clean.sh              # Clean artifacts
ARCH=arm64 ./build.sh   # Build for ARM64
```

### Output Format Flow
```
*.asm (NASM -f win64) → *.obj (PE/COFF)
*.cs (bflat --os:uefi) → *.obj (PE/COFF)
        ↓
    lld-link
        ↓
    BOOTX64.EFI (PE32+ UEFI application)
```

## Project Structure

```
src/
├── Kernel.HAL/          # Hardware abstraction interfaces (arch-independent)
├── Kernel.Core/         # Architecture-independent kernel services
│   ├── Memory/          # PhysicalMemory, VirtualMemory, Heap
│   ├── Console/         # Console output
│   └── Drivers/         # DriverManager
├── Kernel.Arch.X64/     # x86_64 implementation
├── Kernel.Arch.Arm64/   # ARM64 implementation
├── Kernel.Boot.UEFI/    # UEFI boot protocol
└── Kernel.Boot.M1N1/    # Apple Silicon (future)

arch/
├── x64/native_x64.asm   # ~150 LOC
└── arm64/native_arm64.asm

build/
├── x64/
│   ├── BOOTX64.EFI
│   └── boot.img
└── arm64/
```

## Key HAL Interfaces

- `IArchitecture` - Architecture detection, initialization
- `IInterruptController` - Interrupt registration, enable/disable
- `IPageTableManager` - Virtual memory, address spaces
- `ICpuServices` - CPU count, halt, barriers, timestamp
- `ITimer` - Periodic/one-shot timers
- `ISerialPort` - Early console output
- `IBootServices` - Memory map, framebuffer, config tables

## Native Assembly Functions (x86_64)

Must be implemented in assembly (cannot be done in C#):
- **Port I/O:** `outb`, `outw`, `outd`, `inb`, `inw`, `ind`
- **Control Registers:** `read_cr0`, `write_cr0`, `read_cr2`, `read_cr3`, `write_cr3`, `read_cr4`, `write_cr4`
- **Descriptor Tables:** `lgdt`, `lidt`, `ltr`
- **Interrupts:** `cli`, `sti`, `save_flags_cli`, `restore_flags`, `hlt`, `pause`
- **TLB:** `invlpg`, `flush_tlb`
- **MSR:** `rdmsr`, `wrmsr`
- **Other:** `rdtsc`, `mfence`, `lfence`, `sfence`, `cpuid`
- **ISR Stubs:** 256 interrupt entry points calling `managed_interrupt_dispatch`

## C# Patterns

### P/Invoke for native functions
```csharp
[DllImport("*", CallingConvention = CallingConvention.Cdecl)]
public static extern void outb(ushort port, byte value);
```

### UEFI Entry Point
```csharp
[UnmanagedCallersOnly(EntryPoint = "EfiMain")]
public static long EfiMain(void* imageHandle, void* systemTable)
```

### Struct Layout Control
```csharp
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GdtEntry { ... }
```

### Preprocessor Definitions
- Architecture: `ARCH_X64`, `ARCH_ARM64`
- Boot protocol: `BOOT_UEFI`, `BOOT_M1N1`
- bflat auto-defines: `BFLAT`, `UEFI`, `X64`, `ARM64`

## Architecture Differences

| Feature | x86_64 | ARM64 |
|---------|--------|-------|
| Interrupt Controller | APIC/PIC | GIC v2/v3 |
| Timer | APIC Timer, HPET | Generic Timer |
| Page Tables | 4-level PML4 | 4-level TTBR |
| Serial | 16550 UART (port I/O) | PL011 (MMIO) |
| Port I/O | Yes (`in`/`out`) | No (MMIO only) |
| Config | ACPI | ACPI or DeviceTree |

## QEMU Testing

```bash
# Serial output to terminal, debug log to file
qemu-system-x86_64 \
    -machine q35 \
    -bios /usr/share/OVMF/OVMF_CODE_4M.fd \
    -drive format=raw,file=build/x64/boot.img \
    -serial stdio \
    -debugcon file:logs/debug.log \
    -global isa-debugcon.iobase=0x402

# Exit QEMU: Ctrl+A, X
```

## Implementation Status

- [ ] Phase 1: Minimal Boot - UEFI entry, serial console, memory map, "Hello World"
- [ ] Phase 2: Core Infrastructure - GDT/IDT, interrupts, physical memory, page tables
- [ ] Phase 3: Kernel Services - Virtual memory, heap, timer, scheduler
- [ ] Phase 4: Driver Framework - PCI, AHCI, FAT32
- [ ] Phase 5: JIT Integration - RyuJIT hosting, hot-loadable drivers
