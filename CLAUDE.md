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
# Run commands inside container via ./dev.sh wrapper
./dev.sh make           # Build kernel
./dev.sh make image     # Create boot image
./dev.sh ./run.sh       # Run in QEMU
./dev.sh make clean     # Clean artifacts

# Or start interactive dev shell
./dev.sh
```

### Testing
**CRITICAL:** The dev.sh script runs commands inside Docker containers. Each `./dev.sh` invocation starts a NEW container. If tests run QEMU (via `./run.sh`), that container will keep running until QEMU exits. Stale containers can lock files in `build/` and cause subsequent builds to fail.

**ALWAYS use this single-command pattern for build + test:**
```bash
# Build, create image, run test with timeout, then cleanup containers - ALL IN ONE COMMAND
./dev.sh make 2>&1 && ./dev.sh make image 2>&1 && timeout 15 ./dev.sh ./run.sh 2>&1; \
docker stop $(docker ps -q) 2>/dev/null; docker rm $(docker ps -aq) 2>/dev/null; \
echo "--- Done ---"
```

This pattern ensures:
1. Build and image creation happen first (fails fast on errors)
2. QEMU runs with 15-second timeout (auto-exits)
3. Docker containers are ALWAYS cleaned up at the end
4. The `echo` confirms the command completed

**If file locks occur (e.g., "Device or resource busy"):**
```bash
# Stop ALL Docker containers first
docker stop $(docker ps -q) 2>/dev/null; docker rm $(docker ps -aq) 2>/dev/null

# Clean build directory via Docker (files are owned by root)
./dev.sh rm -rf /usr/src/netos/build/x64
./dev.sh mkdir -p /usr/src/netos/build/x64/mernel /usr/src/netos/build/x64/nernel

# Then use the standard build+test pattern above
```

**NOTE:** The dev.sh mounts the project to `/usr/src/netos` inside the container (NOT `/work`).

**NEVER leave test containers running.** The single-command pattern above handles this automatically.

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
