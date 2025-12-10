# ProtonOS

A bare-metal operating system written entirely in C#, targeting x86-64 UEFI systems. Features a custom JIT compiler, garbage collector, and minimal runtime library.

## The Idea

**C is unnecessary for OS development.** Everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless of your systems language. ProtonOS proves this by implementing a complete kernel in managed code.

## Features

- **UEFI Boot** - Native UEFI application, no legacy BIOS
- **Custom Runtime (korlib)** - Minimal .NET runtime library forked from bflat's zerolib
- **Compacting GC** - Mark-sweep with Lisp-2 compaction, Large Object Heap (LOH)
- **Exception Handling** - Complete try/catch/throw support
- **SMP Support** - Multi-processor boot with per-CPU scheduling
- **NUMA Awareness** - Topology detection and NUMA-aware memory allocation
- **Preemptive Scheduler** - Multi-threaded with APIC timer, per-CPU run queues
- **Virtual Memory** - 4-level paging with higher-half kernel
- **Tier 0 JIT** - Naive stack-based IL compiler with full x64 calling convention support
- **Cross-Assembly Loading** - Load and link multiple .NET assemblies at runtime

## Current Status

| Component | Status |
|-----------|--------|
| UEFI boot, serial console | Complete |
| GDT/IDT, interrupts, exceptions | Complete |
| Physical/virtual memory management | Complete |
| Kernel heap allocator | Complete |
| APIC timer, HPET, RTC | Complete |
| SMP boot (multi-processor) | Complete |
| NUMA topology detection | Complete |
| Preemptive threading (per-CPU queues) | Complete |
| Compacting GC with LOH | Complete |
| Exception handling (try/catch/throw) | Complete |
| Tier 0 JIT compiler | Complete |
| PE/Metadata reader | Complete |
| Assembly loading and execution | Complete |
| Cross-assembly type resolution | Complete |
| Reflection API | Complete |
| Driver Development Kit (DDK) | In Progress |
| VirtIO drivers | In Progress |

### JIT Test Results

The JIT runs a comprehensive test suite on boot: **44/48 tests passing**

Tests cover: arithmetic, comparisons, bitwise ops, control flow, locals, method calls, conversions, arrays, fields, structs (small/medium/large), exception handling, boxing, generics, and cross-assembly type resolution.

## Building

### Prerequisites

- **bflat** - C# to Native AOT compiler ([bflat.io](https://flattened.net))
- **.NET SDK 10.0** - For building test assemblies
- **NASM** - x86-64 assembler
- **LLD** - LLVM linker (lld-link)
- **QEMU** - Emulation with OVMF firmware
- **mtools** - FAT filesystem utilities (mformat, mcopy)

### Build and Run

```bash
./build.sh    # Build the kernel and assemblies
./run.sh      # Run in QEMU (boots in ~3 seconds)
./kill.sh     # Kill running QEMU instances
```

### Toolchain

- **bflat 10.0.0** - Compiles kernel C# to native UEFI executable
- **dotnet** - Builds driver and test assemblies as standard .NET DLLs
- **NASM** - Assembles low-level x64 code (interrupts, context switch)
- **lld-link** - Links final UEFI PE executable

### Output

```
build/x64/
├── BOOTX64.EFI    # UEFI executable
└── boot.img       # FAT32 boot image
```

## Architecture

```
src/
├── korlib/              # Kernel runtime library (from bflat zerolib)
│   ├── Internal/        # Runtime internals, startup
│   └── System/          # Core types (Object, String, Array, Reflection, etc.)
├── kernel/              # Kernel implementation
│   ├── Kernel.cs        # Entry point
│   ├── Exports/         # DDK and reflection exports for JIT code
│   ├── Memory/          # Heap, page allocator, GC, compaction
│   ├── PAL/             # Platform Abstraction Layer (Win32-style APIs)
│   ├── Platform/        # UEFI, ACPI, NUMA, CPU topology
│   ├── Runtime/         # PE loader, metadata reader, JIT compiler
│   ├── Threading/       # Scheduler, threads, per-CPU state
│   └── x64/             # x64-specific (GDT, IDT, APIC, SMP, assembly)
├── ddk/                 # Driver Development Kit (loaded by JIT)
│   ├── Platform/        # DMA, PCI, ACPI access for drivers
│   ├── Drivers/         # Driver manager, device enumeration
│   └── Kernel/          # Kernel service exports (Memory, Debug, etc.)
├── drivers/             # Device drivers (JIT-compiled)
│   └── shared/
│       ├── virtio/      # VirtIO common infrastructure
│       └── storage/     # Block device drivers (virtio-blk)
├── SystemRuntime/       # Cross-assembly type definitions
└── FullTest/            # JIT test assembly (runs on boot)
```

## How It Works

ProtonOS uses [bflat](https://github.com/bflattened/bflat) to compile C# directly to a native UEFI executable with `--stdlib:none`. This means:

1. **No .NET runtime dependency** - The kernel IS the runtime
2. **Direct hardware access** - `unsafe` code and P/Invoke to assembly
3. **Full control** - Custom object layout, GC, exception handling

The ~650 lines of assembly (`native.asm`) handle only what C# cannot:
- Port I/O (`in`/`out` instructions)
- Control registers (CR0, CR3, CR4)
- Descriptor tables (GDT, IDT, TSS)
- Interrupt entry points
- Context switching

Everything else is C#.

## Documentation

- [Architecture Reference](docs/ARCHITECTURE.md) - Detailed system design
- [korlib Plan](docs/KORLIB_PLAN.md) - Runtime library roadmap
- [JIT Checklist](docs/JIT_CHECKLIST.md) - JIT compiler implementation status
- [Tier 0 Optimizations](docs/TIER0_OPTIMIZATIONS.md) - JIT optimization phases

## Contributing

This is currently a solo project. Pull requests are welcome and will be reviewed. Please open an issue first to discuss major changes.

## License

[AGPL-3.0](LICENSE) - Due to korlib's derivation from bflat's zerolib.

This means:
- You can use, modify, and distribute freely
- Modifications must be shared under the same license
- Source must be provided if distributed or run as a network service

## Acknowledgments

- [bflat](https://github.com/bflattened/bflat) - The C# Native AOT compiler that makes this possible
- [.NET Runtime](https://github.com/dotnet/runtime) - Reference for runtime internals
