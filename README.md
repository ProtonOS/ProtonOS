# ProtonOS

A bare-metal operating system written entirely in C#, targeting x86-64 UEFI systems. Features a custom JIT compiler, garbage collector, and minimal runtime library.

## The Idea

**C is unnecessary for OS development.** Everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless of your systems language. ProtonOS proves this by implementing a complete kernel in managed code.

## Features

- **UEFI Boot** - Native UEFI application, no legacy BIOS
- **Custom Runtime (korlib)** - Minimal .NET runtime library forked from bflat's zerolib
- **Mark-Sweep GC** - Full garbage collector with precise stack scanning
- **Exception Handling** - Complete try/catch/throw support
- **Preemptive Scheduler** - Multi-threaded with APIC timer
- **Virtual Memory** - 4-level paging with higher-half kernel
- **Tier 0 JIT** - Load and execute .NET assemblies at runtime

## Current Status

| Component | Status |
|-----------|--------|
| UEFI boot, serial console | Complete |
| GDT/IDT, interrupts, exceptions | Complete |
| Physical/virtual memory management | Complete |
| Kernel heap allocator | Complete |
| APIC timer, HPET, RTC | Complete |
| Preemptive threading | Complete |
| Mark-sweep garbage collector | Complete |
| Exception handling (try/catch/throw) | Complete |
| Tier 0 JIT compiler | Complete |
| PE/Metadata reader | Complete |
| Assembly loading and execution | In Progress |

## Building

### Prerequisites

- Docker (for the build environment)
- QEMU (for testing)

### Build and Run

```bash
./build.sh          # Build the kernel
./dev.sh ./run.sh   # Run in QEMU
./kill.sh           # Stop QEMU containers
```

The build uses a Docker container with:
- **bflat 10.0.0** - C# to Native AOT compiler
- **NASM** - x86-64 assembler
- **lld-link** - LLVM PE/COFF linker
- **QEMU** - Emulation with OVMF firmware

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
│   └── System/          # Core types (Object, String, Span, etc.)
└── kernel/              # Kernel implementation
    ├── Kernel.cs        # Entry point
    ├── Jit/             # JIT compiler
    ├── Memory/          # Heap, page allocator, GC
    ├── PAL/             # Platform Abstraction Layer
    ├── Platform/        # UEFI, ACPI, console
    ├── Runtime/         # PE loader, metadata reader
    ├── Threading/       # Scheduler, threads
    └── x64/             # x64-specific (GDT, IDT, APIC, assembly)
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
