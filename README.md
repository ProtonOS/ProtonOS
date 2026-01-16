# ProtonOS

A bare-metal operating system written entirely in C#, targeting x86-64 UEFI systems. Features a custom JIT compiler, garbage collector, device drivers, filesystems, and TCP/IP networking.

## The Idea

**C is unnecessary for OS development.** Everything traditionally done in C can be done in C# with `unsafe`, while privileged CPU instructions require assembly regardless of your systems language. ProtonOS proves this by implementing a complete kernel in managed code.

## Features

### Core Kernel
- **UEFI Boot** - Two-stage bootloader with predictable kernel load addresses
- **Custom Runtime (korlib)** - Minimal .NET runtime with collections (List, Dictionary, StringBuilder)
- **Compacting GC** - Mark-sweep with Lisp-2 compaction, Large Object Heap (LOH)
- **Full Exception Handling** - try/catch/finally/filter with funclet-based unwinding
- **SMP Support** - Multi-processor boot with per-CPU scheduling
- **NUMA Awareness** - Topology detection and NUMA-aware memory allocation
- **Preemptive Scheduler** - Multi-threaded with APIC timer, per-CPU run queues, thread cleanup
- **Virtual Memory** - 4-level paging with higher-half kernel

### JIT Compiler
- **Tier 0 JIT** - Full IL compiler supporting generics, delegates, interfaces, reflection
- **Cross-Assembly Loading** - Load and link multiple .NET assemblies at runtime
- **AOT↔JIT Interop** - Seamless calls between AOT kernel code and JIT-compiled drivers
- **GDB Debugging** - JIT methods visible in GDB via JIT interface

### Device Drivers
- **VirtIO** - Common infrastructure, virtio-blk (storage), virtio-net (networking)
- **AHCI/SATA** - Native SATA controller support
- **Dynamic Loading** - Drivers loaded from /drivers at runtime with lifecycle management

### Filesystems
- **FAT32** - Full read/write support
- **EXT2** - Full read/write support
- **VFS** - Virtual filesystem abstraction with mount support

### Networking
- **Network Stack** - Ethernet, ARP, IPv4, ICMP (ping), UDP, TCP
- **DNS Resolution** - Hostname-to-IP resolution via UDP queries
- **TCP Client** - Connection management, data transmission, graceful close
- **HTTP/1.1** - Client library for HTTP requests over TCP

### Debugging
- **GDB Support** - Automatic symbol loading for AOT and JIT code
- **JIT Debugging** - Set breakpoints in JIT-compiled methods by name
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
| Driver Development Kit (DDK) | Complete |
| VirtIO-blk driver (block storage) | Complete |
| VirtIO-net driver (networking) | Complete |
| AHCI/SATA driver | Complete |
| FAT32 filesystem (read/write) | Complete |
| EXT2 filesystem (read/write) | Complete |
| VFS with mount support | Complete |
| TCP/IP network stack | Complete |
| DNS resolution | Complete |
| HTTP/1.1 client | Complete |
| GDB debugging support | Complete |
| Userspace processes | Not Started |

### Test Results

The kernel runs comprehensive test suites on boot: **712 tests passing**

- **673** JIT/runtime tests (FullTest)
- **32** network stack tests (including DNS)
- **7** application-level tests (HTTP, DNS, etc.)

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

### First-Time Setup

ProtonOS uses a custom fork of bflat with fixes for our AOT scenarios.

```bash
git submodule update --init --recursive
make install-deps  # Install system packages and .NET SDK 10 (requires sudo)
make deps          # Build runtime and bflat (~10-15 min first time)
```

The install script supports Ubuntu/Debian, Fedora, and Arch Linux.

### Build and Run

```bash
./build.sh    # Build the kernel and assemblies
./run.sh      # Run in QEMU (boots in ~3 seconds)
./kill.sh     # Kill running QEMU instances
```

### Toolchain

The kernel is compiled using a custom build of [bflat](https://flattened.net), a C# native AOT compiler:

- **[ProtonOS/runtime](https://github.com/ProtonOS/runtime)** - Fork of bflattened/runtime with fixes for array element type symbols in NativeAOT
- **[ProtonOS/bflat](https://github.com/ProtonOS/bflat)** - Fork of bflat configured to use locally-built ILCompiler from ProtonOS/runtime
- **NASM** - Assembles low-level x64 code (interrupts, context switch)
- **lld-link** - Links final UEFI PE executable
- **.NET SDK 10** - Builds driver and test assemblies as standard .NET DLLs for JIT loading

### Output

```
build/x64/
├── BOOTX64.EFI    # UEFI executable
└── boot.img       # FAT32 boot image
```

## Architecture

```
src/
├── korlib/              # Kernel runtime library (derived from bflat zerolib)
│   ├── Internal/        # Runtime internals, startup
│   └── System/          # Core types (Object, String, Array, Collections, etc.)
├── kernel/              # Kernel implementation
│   ├── Kernel.cs        # Entry point
│   ├── Exports/         # DDK and reflection exports for JIT code
│   ├── Memory/          # Heap, page allocator, GC, compaction
│   ├── PAL/             # Platform Abstraction Layer (Win32-style APIs)
│   ├── Platform/        # UEFI, ACPI, NUMA, CPU topology
│   ├── Runtime/         # PE loader, metadata reader, JIT compiler
│   ├── Threading/       # Scheduler, threads, per-CPU state
│   └── x64/             # x64-specific (GDT, IDT, APIC, SMP, assembly)
├── bootloader/          # UEFI bootloader (loads kernel at fixed address)
├── ddk/                 # Driver Development Kit (JIT-loaded)
│   ├── Kernel/          # Kernel API wrappers (Timer, Memory, Debug, etc.)
│   ├── Network/         # Network stack (Ethernet, ARP, IP, TCP, UDP)
│   └── VFS/             # Virtual filesystem abstraction
├── drivers/             # Device drivers (JIT-compiled)
│   └── shared/
│       ├── virtio/      # VirtIO common, virtio-blk, virtio-net
│       ├── storage/     # AHCI/SATA driver
│       └── filesystem/  # FAT32, EXT2 drivers
├── lib/                 # Application libraries
│   └── ProtonOS.Net/    # HTTP client library
├── AppTest/             # Application-level tests (HTTP, etc.)
├── TestSupport/         # Cross-assembly test helpers
└── FullTest/            # JIT test assembly (runs on boot)

tools/
├── runtime/             # ProtonOS/runtime submodule (NativeAOT ILCompiler)
├── bflat/               # ProtonOS/bflat submodule (C# AOT compiler)
├── gdb-protonos.py      # GDB helper script for debugging
└── gen_elf_syms.py      # PDB to ELF symbol converter
```

## How It Works

ProtonOS uses [bflat](https://flattened.net) to compile C# directly to a native UEFI executable with `--stdlib:none`. This means:

1. **No .NET runtime dependency** - The kernel IS the runtime
2. **Direct hardware access** - `unsafe` code and P/Invoke to assembly
3. **Full control** - Custom object layout, GC, exception handling

The kernel is AOT-compiled, but can load and JIT-compile standard .NET assemblies at runtime. This enables:
- Driver hot-loading without recompilation
- Test suites that run on the bare metal
- Future plugin/module support

The ~1750 lines of assembly (`native.asm`) handle only what C# cannot:
- Port I/O (`in`/`out` instructions)
- Control registers (CR0, CR3, CR4)
- Descriptor tables (GDT, IDT, TSS)
- Interrupt entry points
- Context switching

Everything else is C#.

## Documentation

- [Architecture Reference](docs/ARCHITECTURE.md) - System design and memory layout
- [Boot Protocol](docs/BOOT_PROTOCOL.md) - UEFI bootloader and kernel handoff
- [korlib Plan](docs/KORLIB_PLAN.md) - Runtime library roadmap
- [DDK Plan](docs/DDK_PLAN.md) - Driver development kit design

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
- [bflattened/runtime](https://github.com/bflattened/runtime) - NativeAOT runtime fork that bflat is built on
- [.NET Runtime](https://github.com/dotnet/runtime) - Reference for runtime internals
