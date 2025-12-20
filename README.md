# ProtonOS

A bare-metal operating system written entirely in C#, targeting x86-64 UEFI systems. Features a custom JIT compiler, garbage collector, and minimal runtime library.

## The Idea

**C is unnecessary for OS development.** Everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless of your systems language. ProtonOS proves this by implementing a complete kernel in managed code.

## Features

- **UEFI Boot** - Native UEFI application, no legacy BIOS
- **Custom Runtime (korlib)** - Minimal .NET runtime with collections (List, Dictionary, StringBuilder)
- **Compacting GC** - Mark-sweep with Lisp-2 compaction, Large Object Heap (LOH)
- **Full Exception Handling** - try/catch/finally/filter with funclet-based unwinding
- **SMP Support** - Multi-processor boot with per-CPU scheduling
- **NUMA Awareness** - Topology detection and NUMA-aware memory allocation
- **Preemptive Scheduler** - Multi-threaded with APIC timer, per-CPU run queues, thread cleanup
- **Virtual Memory** - 4-level paging with higher-half kernel
- **Tier 0 JIT** - Full IL compiler supporting generics, delegates, interfaces, reflection
- **Cross-Assembly Loading** - Load and link multiple .NET assemblies at runtime
- **Reflection API** - Type introspection, method enumeration, dynamic invocation

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
| Exception handling (try/catch/finally/filter) | Complete |
| Tier 0 JIT compiler | Complete |
| PE/Metadata reader | Complete |
| Assembly loading and execution | Complete |
| Cross-assembly type resolution | Complete |
| Reflection API (types, methods, fields, invoke) | Complete |
| Driver Development Kit (DDK) | In Progress |
| VirtIO drivers | In Progress |

### JIT Test Results

The JIT runs a comprehensive test suite on boot: **562 tests passing**

### Supported C# Features

| Category | Features |
|----------|----------|
| **Basic Operations** | All arithmetic, bitwise, comparison, and conversion operations |
| **Control Flow** | if/else, switch, for/while/do loops, goto |
| **Methods** | Static, instance, virtual, interface dispatch, delegates |
| **Exception Handling** | try/catch/finally, throw/rethrow, filters (`catch when`), nested handlers |
| **Object Model** | Classes, structs, interfaces, inheritance, boxing/unboxing |
| **Arrays** | Single-dimension, multi-dimension (2D/3D), jagged, bounds checking |
| **Generics** | Generic classes, methods, interfaces, delegates, constraints, variance |
| **Delegates** | Static/instance, multicast (Combine/Remove), closures/lambdas |
| **Value Types** | Structs (all sizes), Nullable<T> with lifted operators, explicit layout |
| **Reflection** | typeof, GetType, GetMethods/Fields/Constructors, MethodInfo.Invoke |
| **Unsafe Code** | Pointers, stackalloc, fixed buffers, calli, function pointers |
| **Threading** | Interlocked operations, thread APIs (via kernel exports) |
| **Resource Management** | IDisposable, using statement, foreach on arrays and collections |
| **Collections** | List\<T\>, Dictionary\<TKey,TValue\>, StringBuilder, custom iterators |
| **Special** | Static constructors, overflow checking, varargs (__arglist), nameof |

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
├── SystemRuntime/       # Deprecated - reference code only (korlib handles all types)
└── FullTest/            # JIT test assembly (525 tests, runs on boot)
```

## How It Works

ProtonOS uses [bflat](https://github.com/bflattened/bflat) to compile C# directly to a native UEFI executable with `--stdlib:none`. This means:

1. **No .NET runtime dependency** - The kernel IS the runtime
2. **Direct hardware access** - `unsafe` code and P/Invoke to assembly
3. **Full control** - Custom object layout, GC, exception handling

The ~1700 lines of assembly (`native.asm`) handle only what C# cannot:
- Port I/O (`in`/`out` instructions)
- Control registers (CR0, CR3, CR4)
- Descriptor tables (GDT, IDT, TSS)
- Interrupt entry points
- Context switching

Everything else is C#.

## Documentation

- [Architecture Reference](docs/ARCHITECTURE.md) - Detailed system design
- [JIT Test Coverage](docs/JIT_TEST_COVERAGE.md) - Comprehensive IL opcode and feature coverage
- [korlib Plan](docs/KORLIB_PLAN.md) - Runtime library roadmap
- [System.Runtime Migration](docs/SYSTEMRUNTIME_MIGRATION.md) - Type migration status and JIT fixes
- [Allocation Limits](docs/ALLOCATION_LIMITS.md) - Registry and allocator documentation

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
