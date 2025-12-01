# ProtonOS Architecture Reference

## Project Overview

**ProtonOS** is a hybrid managed OS kernel written in C# using .NET Native AOT compilation. The key insight: **C is unnecessary** - everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless.

### Architecture Layers
1. **korlib** - Kernel runtime library (forked from bflat zerolib, AGPL v3)
2. **kernel** - Core kernel in C# + assembly compiled via bflat/NativeAOT (namespace: ProtonOS)
   - Includes ~650 LOC assembly per arch (native.asm) for CPU intrinsics, ISR stubs
3. **JIT Extensions (Future)** - Hot-loadable drivers/modules

## Build Environment

Docker-based development using `protonos-dev` container:
- **Base:** Debian Bookworm (12) Slim
- **bflat:** 10.0.0-rc.1 - C# to Native AOT compiler (.NET 10)
- **NASM:** 2.16.01 - x86_64 assembler (use `-f win64` for UEFI)
- **lld-link:** LLVM PE/COFF linker
- **QEMU:** x86_64/ARM64 emulation with OVMF firmware

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
├── korlib/              # Kernel runtime library (AGPL v3, from bflat zerolib)
│   ├── Internal/        # Runtime internals, startup
│   ├── System/          # Core types (Object, String, Span, etc.)
│   └── LICENSE          # AGPL v3 license
└── kernel/              # Kernel (C# + assembly, namespace: ProtonOS)
    ├── Kernel.cs        # Main entry point
    ├── Memory/          # HeapAllocator, PageAllocator
    ├── PAL/             # Platform Abstraction Layer (Win32-compatible APIs)
    ├── Platform/        # UEFI, ACPI, DebugConsole
    ├── Runtime/         # PE format, GC infrastructure
    ├── Threading/       # Scheduler, Thread
    └── x64/             # x64-specific (Arch, GDT, IDT, APIC, native.asm)

build/
└── x64/
    ├── native.obj       # Assembled native code
    ├── kernel.obj       # Compiled C# (korlib + kernel)
    ├── BOOTX64.EFI      # UEFI executable
    └── boot.img         # FAT32 boot image
```

### stdlib:none Mode
We use `--stdlib:none` with our own korlib (forked from bflat's zerolib). This provides:
- Core types: Object, String, Array, Span<T>, etc.
- Compiler support types: RuntimeHelpers, Unsafe, etc.
- Mark-sweep GC with stack/static root enumeration

### Export/Import Pattern
korlib and kernel are compiled together but use export/import for cross-module calls:

```csharp
// Kernel exports (src/kernel/PAL/Environment.cs)
[UnmanagedCallersOnly(EntryPoint = "PalFailFast")]
public static void FailFast() { CPU.HaltForever(); }

// korlib imports (src/korlib/System/Environment.cs)
[DllImport("*")]
private static extern void PalFailFast();
```

## Architecture Abstraction

Compile-time architecture selection via preprocessor:

```csharp
// In Kernel.cs - compile-time dispatch
#if ARCH_X64
    Arch.Init();  // ProtonOS.X64.Arch
#elif ARCH_ARM64
    Arch.Init();  // ProtonOS.Arm64.Arch (future)
#endif
```

Each architecture provides a static `Arch` class with:
- `Init()` - Initialize GDT/IDT (or equivalent), interrupt handlers
- `RegisterHandler(int vector, delegate*<InterruptFrame*, void> handler)`
- `EnableInterrupts()` / `DisableInterrupts()`
- `Halt()`

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

### Kernel Infrastructure (Complete)
- [x] UEFI boot, serial console, memory map
- [x] GDT/IDT, interrupt handling, exception dispatch
- [x] Physical memory allocator (PageAllocator)
- [x] Virtual memory (4-level paging, higher-half physmap)
- [x] Kernel heap (HeapAllocator)
- [x] APIC timer, HPET, RTC
- [x] Preemptive scheduler with threading
- [x] Win32-compatible PAL (sync primitives, TLS, SEH, etc.)

### korlib (In Progress)
- [x] Phase 1: Fork zerolib as korlib foundation
- [x] Phase 2: Exception support (try/catch/throw)
- [x] Phase 3: Garbage collector (mark-sweep with stack/static roots)
- [ ] Phase 4: UEFI file system (load test assemblies)
- [ ] Phase 5: Metadata reader (native System.Reflection.Metadata)
- [ ] Phase 6: RyuJIT integration
- [ ] Phase 7-9: Assembly execution, BCL support, validation

See `docs/KORLIB_PLAN.md` for detailed roadmap.
