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
# PREFERRED: Use build.sh for building (handles cleanup automatically)
./build.sh              # Kill containers + clean + build (ALWAYS USE THIS)
./dev.sh ./run.sh       # Run in QEMU

# Or run commands inside container via ./dev.sh wrapper
./dev.sh make           # Build kernel (incremental)
./dev.sh make image     # Create boot image
./dev.sh make clean     # Clean artifacts

# Helper scripts
./kill.sh               # Kill all Docker containers
./clean.sh              # Clean build directory (runs inside container)

# Interactive dev shell
./dev.sh
```

### Testing - SIMPLE WORKFLOW

**ALWAYS use `./build.sh` for building.** It automatically:
1. Kills any running Docker containers (stale QEMU instances)
2. Cleans the build directory
3. Runs `make image`

**Standard build + test pattern:**
```bash
# Step 1: Build (use Bash tool with timeout: 120000 for build)
./build.sh 2>&1

# Step 2: Run test (use Bash tool with timeout: 30000 for test)
./dev.sh ./run.sh 2>&1

# Step 3: ALWAYS run kill.sh after test completes
./kill.sh
```

**RULE: ALWAYS run `./kill.sh` after a test**
This kills all Docker containers to prevent stale QEMU instances from accumulating.

**Why build.sh matters:**
- Each `./dev.sh` invocation starts a NEW container
- QEMU keeps the container running until it exits
- Stale containers lock files in `build/` and cause failures
- `./build.sh` handles all cleanup automatically before building

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
├── nernel/              # Native kernel (assembly)
│   └── x64/
│       └── native.asm   # CPU intrinsics, ISR stubs (~280 LOC)
└── mernel/              # Managed kernel (C#)
    ├── Mernel.cs        # Main entry point
    ├── DebugConsole.cs  # COM1 serial output
    ├── NativeMemory.cs  # Bump allocator for early boot
    └── x64/             # x64-specific code
        ├── Arch.cs      # Architecture init, interrupt dispatch
        ├── Gdt.cs       # Global Descriptor Table + TSS
        └── Idt.cs       # Interrupt Descriptor Table

build/
└── x64/
    ├── BOOTX64.EFI      # UEFI executable
    └── boot.img         # FAT32 boot image
```

### stdlib:zero Constraints
bflat's `--stdlib:zero` mode has no runtime, so:
- **No `new` for classes** - Use static classes or structs
- **No managed delegates** - Use `delegate*<T, void>` function pointers
- **No arrays via `new`** - Use `NativeMemory.Alloc()` for buffers
- **No GC** - Manual memory management only

## Architecture Abstraction

Due to stdlib:zero constraints (no `new` for classes), we use **compile-time architecture selection** instead of runtime interfaces:

```csharp
// In Mernel.cs - compile-time dispatch via preprocessor
#if ARCH_X64
    Arch.Init();  // Mernel.X64.Arch
#elif ARCH_ARM64
    Arch.Init();  // Mernel.Arm64.Arch (future)
#endif
```

Each architecture provides a static `Arch` class with:
- `Init()` - Initialize GDT/IDT (or equivalent), interrupt handlers
- `RegisterHandler(int vector, delegate*<InterruptFrame*, void> handler)`
- `EnableInterrupts()` / `DisableInterrupts()`
- `Halt()`

**Future:** Once JIT is available, we can use proper interfaces with runtime polymorphism.

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

## Critical Memory
- Use `./build.sh` for building - it handles container cleanup and build directory cleaning automatically
- Always run `./kill.sh` after running a test to clean up Docker containers
