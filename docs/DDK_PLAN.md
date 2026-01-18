# ProtonOS Driver Development Kit (DDK) Plan

## Status

**Last Updated**: January 2026

### Recent Progress (Phase 3 Complete!)

**Bootstrap Storage & Filesystem - DONE:**
- [x] Kernel loads DDK.dll and drivers from UEFI boot image
- [x] DDKInit.Initialize() works via JIT compilation
- [x] Native kernel PCI enumeration detects all PCI devices
- [x] Virtio-blk driver complete (device init, queues, read/write)
- [x] AHCI/SATA driver complete (port init, IDENTIFY, read/write)
- [x] FAT filesystem driver complete (read/write, directory ops)
- [x] EXT2 filesystem driver complete (read/write, directory ops)
- [x] VFS layer complete (mount, read, write, delete, directory enumeration)

**VFS Mount Points:**
- `/` - EXT2 on SATA disk (read-write)
- `/boot` - FAT on boot partition (read-only)

**Dynamic Driver Loading:**
- [x] Drivers load from `/drivers` directory on root filesystem
- [x] Test driver demonstrates full Initialize/Shutdown lifecycle
- [x] Driver unloading works correctly

**JIT Compiler:**
- [x] 666 tests passing
- [x] Interface dispatch working across assemblies
- [x] Generic method instantiation with type arguments
- [x] Span<T> and inline array support

### Completed AOT Work

The following subsystems are implemented in the AOT kernel and expose DDK exports:

| Subsystem | Status | Kernel Exports | Notes |
|-----------|--------|----------------|-------|
| **SMP** | ✅ Complete | `src/kernel/Exports/CPU.cs` | 4 CPUs tested |
| CPU Topology | ✅ Complete | GetCpuCount, GetCpuInfo | MADT parsing |
| Per-CPU State | ✅ Complete | GetCurrentCpu | GS-based access |
| Thread Affinity | ✅ Complete | Get/SetThreadAffinity | Bitmask per thread |
| AP Startup | ✅ Complete | (internal) | INIT-SIPI-SIPI |
| I/O APIC | ✅ Complete | (internal) | ISA IRQ routing |
| IPI | ✅ Complete | (internal) | Cross-CPU messaging |
| Per-CPU Scheduler | ✅ Complete | (internal) | Work stealing queues |

### Available CPU Exports (for JIT DDK)

```csharp
// src/kernel/Exports/CPU.cs - Already implemented
Kernel_GetCpuCount()           // Number of CPUs
Kernel_GetCurrentCpu()         // Current CPU index
Kernel_GetCpuInfo(index, info) // CPU details (APIC ID, online status)
Kernel_SetThreadAffinity(mask) // Pin thread to CPUs
Kernel_GetThreadAffinity()     // Get current affinity
Kernel_IsCpuOnline(index)      // Check if CPU is running
Kernel_GetBspIndex()           // Bootstrap processor index
Kernel_GetSystemAffinityMask() // All-CPUs bitmask
```

---

## Architecture Overview

**JIT-First Driver Model**: Drivers are JIT-compiled assemblies, NOT AOT.

```
┌─────────────────────────────────────────────────────────────────┐
│  Optional Drivers (JIT, loaded from filesystem after mount)     │
│  src/drivers/shared/ - network, USB class, graphics, etc.       │
├─────────────────────────────────────────────────────────────────┤
│  Bootstrap Drivers (JIT, loaded from UEFI before FS mount)      │
│  PCI, storage (NVMe/AHCI), filesystem (FAT32/ext4)              │
├─────────────────────────────────────────────────────────────────┤
│  Architecture Assembly (JIT, loaded at boot)                    │
│  X64.dll (src/drivers/x64/) or ARM64.dll (src/drivers/arm64/)   │
├─────────────────────────────────────────────────────────────────┤
│  DDK - Driver Development Kit (JIT compiled, shared lib)        │
│  src/ddk/ - ProtonOS.DDK namespace                              │
├─────────────────────────────────────────────────────────────────┤
│  korlib (AOT compiled)                                          │
│  src/korlib/ - runtime, GC, core types                          │
├─────────────────────────────────────────────────────────────────┤
│  Kernel (AOT compiled + native assembly)                        │
│  src/kernel/ - exports privileged ops via UnmanagedCallersOnly  │
└─────────────────────────────────────────────────────────────────┘
```

**Principle**: Implement everything possible in JIT. Only use AOT for:
- x86 privileged instructions (port I/O, MSR, control registers)
- Interrupt handler registration (ISR stubs are in native.asm)
- Anything that truly cannot work from managed JIT code

---

## Driver Loading Strategy

**Architecture-Specific + Optional**: Drivers organized by architecture, with shared optional drivers.

### Architecture-Specific Drivers

Each architecture has its own assembly containing arch-specific drivers:
- **X64.dll** - x64/AMD64 architecture drivers (from `src/drivers/x64/`)
- **ARM64.dll** - AArch64 architecture drivers (from `src/drivers/arm64/`)
- Only the relevant architecture's assembly is compiled and loaded

**What goes in architecture assemblies**:
- Platform-specific code (port I/O patterns, MSR access, etc.)
- PS/2 keyboard/mouse (x64 port I/O based, may not exist on ARM)
- Legacy hardware drivers (ISA devices, BIOS-era peripherals)
- Architecture-specific USB host controller quirks

### Shared Drivers (Architecture-Independent)

Many drivers work across architectures due to arch abstractions:
- **Network drivers** - e1000, virtio-net, RTL8139 (MMIO/PCI-based)
- **NVMe** - PCIe memory-mapped, arch-neutral
- **AHCI/SATA** - PCI MMIO-based
- **USB class drivers** - Protocol-only, no arch dependency
- **Filesystem drivers** - Pure software, no hardware dependency

These are loaded as optional drivers from the filesystem, not bundled.

### Driver Categories

| Category | Location | Loading |
|----------|----------|---------|
| Arch-specific | `src/drivers/x64/`, `src/drivers/arm64/` | Compiled into X64.dll/ARM64.dll |
| Bootstrap (storage/FS) | `/EFI/PROTONOS/boot/` | Loaded from UEFI before FS mount |
| Optional | `/EFI/PROTONOS/drivers/` | Loaded after FS available |

### Bootstrap Sequence

The key insight: **DriverManager needs a filesystem to load most drivers**, but needs some drivers to *get* a filesystem. This creates a bootstrap sequence:

```
┌─────────────────────────────────────────────────────────────────────┐
│  PHASE 1: UEFI BOOT (No Filesystem Yet)                             │
├─────────────────────────────────────────────────────────────────────┤
│  1. Kernel boots (AOT)                                              │
│  2. Load DDK.dll from UEFI SimpleFileSystem                         │
│  3. Load X64.dll (or ARM64.dll) - arch-specific platform drivers    │
│  4. Load bootstrap drivers from /EFI/PROTONOS/boot/:                │
│     - PCI enumeration driver                                        │
│     - Storage drivers (NVMe, AHCI, virtio-blk)                      │
│     - Filesystem drivers (FAT32, ext4, or target FS)                │
│  5. DriverManager.InitBootstrap() - enumerate PCI, find storage     │
├─────────────────────────────────────────────────────────────────────┤
│  PHASE 2: FILESYSTEM MOUNT                                          │
├─────────────────────────────────────────────────────────────────────┤
│  6. Read boot config to determine:                                  │
│     - Which filesystem driver to activate                           │
│     - What storage device to mount                                  │
│     - Mount point configuration                                     │
│  7. Mount target filesystem (could be same boot partition or other) │
│  8. DriverManager now has a path to find remaining drivers          │
├─────────────────────────────────────────────────────────────────────┤
│  PHASE 3: FULL DRIVER LOADING                                       │
├─────────────────────────────────────────────────────────────────────┤
│  9. DriverManager.LoadOptionalDrivers(driverPath) - from mounted FS │
│ 10. Enumerate remaining PCI devices, bind matching drivers          │
│ 11. Initialize USB controllers, enumerate USB devices               │
│ 12. System fully operational                                        │
└─────────────────────────────────────────────────────────────────────┘
```

### Boot Image Composition

For bootable media (USB installers, live images), the UEFI boot partition includes:

```
/EFI/PROTONOS/
├── kernel.efi              # AOT kernel
├── DDK.dll                 # Always required
├── X64.dll                 # Arch-specific (or ARM64.dll)
├── boot.cfg                # Boot configuration
├── boot/                   # Bootstrap drivers (storage + FS)
│   ├── pci.dll
│   ├── nvme.dll
│   ├── ahci.dll
│   ├── virtio-blk.dll
│   ├── fat32.dll
│   └── ext4.dll            # Or whatever target FS
└── drivers/                # Optional drivers
    ├── e1000.dll
    ├── virtio-net.dll
    ├── xhci.dll
    ├── usb-hid.dll
    └── ...
```

**Size considerations**: If UEFI boot partition size becomes limiting, we can:
- Only include drivers for most common hardware
- Use compressed driver packages
- Implement network boot to fetch additional drivers
- Stage drivers: minimal set boots, downloads rest from network

### Boot Configuration (boot.cfg)

```ini
[filesystem]
driver=fat32
device=nvme0n1p2          # Or autodiscover
mount=/

[drivers]
path=/drivers             # Where to find optional drivers
autoload=true             # Scan and load matching drivers

[debug]
serial=true
verbose=false
```

### DriverManager Expectations

After bootstrap, DriverManager operates with these assumptions:
1. **Filesystem is available** - mounted and accessible
2. **Driver path is known** - from boot config
3. **PCI enumeration complete** - knows what devices exist
4. **Can load drivers on-demand** - as new devices appear (USB hotplug)

### Driver Hot-Loading (Development)

For development scenarios, drivers can be unloaded and reloaded at runtime:

```
Normal Flow:
  Boot → Load drivers from UEFI → Mount FS → Load remaining drivers → Run

Development Flow:
  Boot → Load drivers from UEFI → Mount FS → Load drivers →
  [Developer updates driver on FS] → Unload old → Load new → Continue
```

This allows iterating on driver code without full reboots. The natural production
flow always loads from UEFI boot image, but development can override with updated
drivers from the mounted filesystem.

---

## AOT vs JIT Boundary

### Must Be AOT (Kernel Exports)

| Operation | Why AOT Required | Kernel Export |
|-----------|------------------|---------------|
| Port I/O (inb/outb) | x86 IN/OUT instructions | Already in native.asm |
| MSR access (rdmsr/wrmsr) | x86 RDMSR/WRMSR | Already in native.asm |
| Control registers (CR0-4) | x86 privileged | Already in native.asm |
| Interrupt enable/disable | CLI/STI | Already in native.asm |
| Interrupt handler registration | IDT/ISR stubs | Arch.RegisterHandler |
| Physical memory allocation | PageAllocator | Need to export |
| PhysToVirt translation | VirtualMemory | Need to export |

### Hybrid Approach for Existing AOT Hardware

**Export essential operations** from existing AOT implementations:
- `HPET.GetTicks()`, `HPET.GetFrequency()` - timing
- `APIC.SendEoi()` - interrupt acknowledgment
- `ACPI.FindTable(signature)` - table lookup
- `RTC.GetTime()` - wall clock

**Re-implement in JIT for extensibility**:
- I/O APIC discovery and configuration (MMIO, builds on ACPI)
- PCI enumeration (ECAM = MMIO, legacy = port I/O export)
- Additional ACPI table parsing (MADT, MCFG, etc.)
- Timer comparator setup (beyond basic tick reading)

### Can Be JIT (DDK + Drivers)

Everything else - which is most of the work:
- MMIO device access - pointer dereference after PhysToVirt
- PCI enumeration - ECAM is MMIO, legacy uses port I/O exports
- I/O APIC configuration - MMIO access
- DMA buffer management - call PageAllocator exports
- All driver protocol logic - parsing, queues, state machines
- ACPI table parsing - already works in managed code, can extend
- Device discovery - parse ACPI tables, walk PCI bus
- All interfaces - IBlockDevice, INetworkDevice, etc.

---

## Implementation Checklist

### Phase 0: Infrastructure

- [x] **src/kernel/Exports/PortIO.cs** - Export inb/outb/inw/outw/ind/outd ✅ Complete
- [x] **src/kernel/Exports/MSR.cs** - Export rdmsr/wrmsr ✅ Complete
- [x] **src/kernel/Exports/Memory.cs** - Export PageAllocator, PhysToVirt ✅ Complete
- [x] **src/kernel/Exports/Interrupts.cs** - Export RegisterHandler, SendEOI ✅ Complete
- [x] **src/kernel/Exports/ACPI.cs** - Export ACPI table access ✅ Complete
- [x] **src/kernel/Exports/Timer.cs** - Export HPET.GetTicks, APIC.SendEoi ✅ Complete
- [x] **src/kernel/Exports/CPU.cs** - Export CPU topology/affinity ✅ Complete
- [x] **src/ddk/DDK.csproj** - Create DDK project (ProtonOS.DDK namespace) ✅ Complete
- [x] **src/ddk/Kernel/*.cs** - DllImport wrappers for kernel exports ✅ Complete
- [x] **Build integration** - Update Makefile to compile DDK ✅ Complete

### Phase 1: Core Platform (in DDK, JIT)

- [x] ~~**ddk/Platform/IOAPIC.cs**~~ - Done in AOT (`src/kernel/x64/IOAPIC.cs`)
- [x] **ddk/Platform/DMA.cs** - DMA buffer allocation helpers ✅ Complete
- [x] **ddk/Platform/PCI.cs** - PCI config space access (ECAM + legacy) ✅ Complete
- [x] **ddk/Platform/PCIEnumerator.cs** - PCI bus enumeration ✅ Complete

**Note**: I/O APIC is now in AOT kernel as part of SMP implementation. PCI enumeration
remains a DDK/driver task (loaded early during bootstrap). ECAM is pure MMIO, legacy
config space access on x64 uses port I/O which requires a small arch shim.

### Phase 2: Driver Model (in DDK, JIT)

- [x] **ddk/Drivers/IDriver.cs** - Base driver interface ✅ Complete
- [x] **ddk/Drivers/DriverManager.cs** - Registration, PCI binding ✅ Complete
- [x] **ddk/Drivers/DriverAttribute.cs** - PCI vendor/device matching ✅ Complete
- [x] **ddk/Drivers/DriverType.cs** - Driver type enumerations ✅ Complete
- [x] **ddk/Drivers/PciDeviceInfo.cs** - PCI device info structures ✅ Complete
- [x] **ddk/DDKInit.cs** - DDK entry point and initialization ✅ Complete

### Phase 3: Bootstrap Storage & Filesystem ✅ COMPLETE

These are critical for Phase 2 bootstrap - must be loadable from UEFI:

- [x] **ddk/Storage/IBlockDevice.cs** - Block device interface ✅ Complete
- [x] **ddk/Storage/IFileSystem.cs** - Filesystem interface ✅ Complete
- [x] **ddk/Storage/VFS.cs** - Virtual filesystem / mount management ✅ Complete
- [x] **drivers/shared/virtio/** - Virtio common library (virtqueue, device init) ✅ Complete
- [x] **drivers/shared/storage/virtio-blk/** - Virtio block driver (QEMU) ✅ Complete
- [x] **drivers/shared/storage/ahci/** - AHCI/SATA driver ✅ Complete
- [ ] **drivers/shared/storage/nvme/** - NVMe driver (future)
- [x] **drivers/shared/storage/fat/** - FAT filesystem driver ✅ Complete
- [x] **drivers/shared/storage/ext2/** - EXT2 filesystem driver ✅ Complete
- [ ] **drivers/shared/filesystem/ext4/** - ext4 filesystem driver (future, ext2 works)

### Phase 4: Network Stack (in DDK, JIT) ✅ COMPLETE

- [x] **ddk/Network/INetworkDevice.cs** - Network device interface ✅ Complete
- [x] **ddk/Network/Stack/NetworkStack.cs** - Core stack (Ethernet, ARP, IPv4, ICMP, routing) ✅ Complete
- [x] **ddk/Network/Stack/UDP.cs** - UDP protocol helpers ✅ Complete
- [x] **ddk/Network/Stack/TCP.cs** - TCP state machine and helpers ✅ Complete
- [x] **ddk/Network/Stack/DNS.cs** - DNS packet building/parsing ✅ Complete
- [x] **ddk/Network/Stack/DnsResolver.cs** - High-level DNS resolver ✅ Complete
- [x] **ddk/Network/Stack/DHCP.cs** - DHCP packet building/parsing ✅ Complete
- [x] **ddk/Network/Stack/DhcpClient.cs** - DHCP client with state machine ✅ Complete
- [x] **lib/ProtonOS.Net/HTTP.cs** - HTTP/1.1 request/response handling ✅ Complete
- [x] **lib/ProtonOS.Net/HttpClient.cs** - High-level HTTP client ✅ Complete

**717 tests passing** (36 network stack tests + integration tests)

### Phase 5: Network Drivers (Shared)

- [x] **drivers/shared/network/virtio-net/** - Virtio network driver (QEMU) ✅ Complete
- [ ] **drivers/shared/network/e1000/** - Intel e1000 driver (future)
- [ ] **drivers/shared/network/rtl8139/** - Realtek RTL8139 driver (future)

### Phase 6: USB Core (in DDK, JIT)

- [x] **ddk/USB/IUSBHostController.cs** - Host controller interface ✅ Complete
- [x] **ddk/USB/USBTypes.cs** - USB types and descriptors ✅ Complete
- [x] **ddk/USB/USBManager.cs** - USB device enumeration ✅ Complete
- [ ] **ddk/USB/USBHub.cs** - Hub driver support

### Phase 7: USB Host Controllers (Shared)

- [ ] **drivers/shared/usb/xhci/** - xHCI (USB 3.x) controller
- [ ] **drivers/shared/usb/ehci/** - EHCI (USB 2.0) controller
- [ ] **drivers/shared/usb/ohci/** - OHCI (USB 1.1) controller
- [ ] **drivers/shared/usb/uhci/** - UHCI (USB 1.0) controller

### Phase 8: USB Class Drivers (Shared)

- [ ] **drivers/shared/usb/usb-hid/** - HID (keyboard, mouse, gamepad)
- [ ] **drivers/shared/usb/usb-storage/** - Mass storage (USB drives)
- [ ] **drivers/shared/usb/usb-audio/** - Audio class
- [ ] **drivers/shared/usb/usb-video/** - Video class (webcams)
- [ ] **Composite device support** - Multiple interfaces per device

### Phase 9: Input (Mixed)

- [x] **ddk/Input/IInputDevice.cs** - Input device interface ✅ Complete
- [x] **ddk/Input/InputTypes.cs** - Input event types and scan codes ✅ Complete
- [x] **ddk/Input/InputManager.cs** - Event queue management ✅ Complete
- [ ] **drivers/x64/PS2/** - PS/2 keyboard/mouse (x64-specific, port I/O)
- [ ] **USB HID integration** - Route to input system (shared)

**Note**: PS/2 is x64-specific (uses port I/O). Systems with USB-only input
(ARM64, modern x64) use USB HID instead. PS/2 is optional even on x64.

### Phase 10: Serial/Debug (Mixed)

- [x] **ddk/Serial/ISerialPort.cs** - Serial port interface ✅ Complete
- [ ] **drivers/shared/serial/uart/** - UART 16550 driver (shared with arch shims)
- [ ] **Kernel.log** - Serial backend, filesystem later

### Phase 11: Graphics (Shared)

- [x] **ddk/Graphics/IFramebuffer.cs** - Framebuffer interface ✅ Complete
- [x] **ddk/Graphics/IDisplayDevice.cs** - Display device interface ✅ Complete
- [x] **ddk/Graphics/DisplayManager.cs** - Multi-monitor support ✅ Complete (in IDisplayDevice.cs)
- [ ] **UEFI GOP wrapper** - Basic framebuffer from UEFI
- [ ] **drivers/shared/graphics/virtio-gpu/** - Virtio GPU (QEMU)
- [ ] **GPU command interface** - Abstract command buffer model

### Phase 12: Audio (Shared)

- [x] **ddk/Audio/IAudioDevice.cs** - Audio device interface ✅ Complete
- [ ] **drivers/shared/audio/hda/** - Intel HD Audio
- [ ] **USB Audio integration** - Via USB class driver

### Phase 13: Architecture-Specific Assemblies

**X64.dll** - x64/AMD64 architecture assembly:
- [ ] **drivers/x64/X64.csproj** - Project file for X64.dll
- [ ] **drivers/x64/PS2/PS2Controller.cs** - PS/2 controller (8042)
- [ ] **drivers/x64/PS2/PS2Keyboard.cs** - PS/2 keyboard driver
- [ ] **drivers/x64/PS2/PS2Mouse.cs** - PS/2 mouse driver
- [ ] **drivers/x64/Legacy/PIT.cs** - Programmable Interval Timer (if needed)
- [ ] **drivers/x64/Platform/** - Any x64-specific platform code

**ARM64.dll** - AArch64 architecture assembly:
- [ ] **drivers/arm64/ARM64.csproj** - Project file for ARM64.dll
- [ ] **drivers/arm64/Platform/GIC.cs** - Generic Interrupt Controller
- [ ] **drivers/arm64/Platform/** - ARM64-specific platform code

### Phase 14: CPU Features (in DDK, JIT)

- [ ] **ddk/CPU/TSC.cs** - Time Stamp Counter (x64)
- [ ] **ddk/CPU/PerformanceCounters.cs** - PMC support
- [ ] **ddk/CPU/DebugRegisters.cs** - Hardware breakpoints
- [x] ~~**ddk/CPU/Topology.cs**~~ - Done in AOT (use `Kernel_GetCpu*` exports)

### Phase 15: Advanced Features

- [ ] **IOMMU/VT-d** - DMA isolation
- [ ] **Power management** - ACPI sleep states
- [ ] **Driver hot-loading** - Load/unload at runtime
- [ ] **SYSCALL/SYSRET** - User-mode transitions

### Phase 16: NUMA Support (AOT with DDK Exports)

**See: [`docs/NUMA_PLAN.md`](NUMA_PLAN.md) for detailed implementation plan.**

NUMA will be implemented in AOT following the SMP pattern (ACPI parsing, PageAllocator
integration, DDK exports). Summary of phases:

- [ ] **Phase 1**: SRAT/SLIT structures in ACPI.cs
- [ ] **Phase 2**: NumaTopology.cs module
- [ ] **Phase 3**: Add NumaNode to CpuInfo
- [ ] **Phase 4**: NUMA-aware PageAllocator
- [ ] **Phase 5**: Per-CPU/Thread NUMA preference
- [ ] **Phase 6**: DDK exports (NUMA.cs)

---

## Project Structure

```
src/
├── kernel/                    # AOT compiled kernel (existing)
│   ├── Exports/               # UnmanagedCallersOnly exports for JIT
│   │   └── DDK/               # DDK kernel exports (all ✅ Complete)
│   │       ├── CPU.cs         # CPU topology, affinity
│   │       ├── NUMA.cs        # NUMA topology
│   │       ├── PortIO.cs      # inb/outb/inw/outw/ind/outd
│   │       ├── MSR.cs         # rdmsr/wrmsr
│   │       ├── Memory.cs      # PageAllocator, PhysToVirt
│   │       ├── Interrupts.cs  # RegisterHandler, SendEOI
│   │       ├── ACPI.cs        # ACPI table access
│   │       └── Timer.cs       # HPET.GetTicks, APIC.SendEoi
│   └── ... (existing code)
│
├── korlib/                    # AOT compiled runtime (existing)
│
├── ddk/                       # NEW: Driver Development Kit (JIT)
│   ├── DDK.csproj             # ProtonOS.DDK namespace
│   ├── Kernel/                # DllImport wrappers
│   ├── Platform/              # PCI, IOAPIC, DMA
│   ├── Drivers/               # IDriver, DriverManager
│   ├── Storage/               # IBlockDevice, IFileSystem
│   ├── Network/               # INetworkDevice, Stack/
│   ├── USB/                   # IUSBHostController, USBDevice
│   ├── Input/                 # IInputDevice, InputManager
│   ├── Graphics/              # IFramebuffer, IDisplayDevice
│   ├── Audio/                 # IAudioDevice
│   ├── Serial/                # ISerialPort
│   └── CPU/                   # TSC, PMC (Topology via Kernel exports)
│
└── drivers/                   # NEW: Driver assemblies (JIT)
    │
    ├── x64/                   # x64 ARCHITECTURE-SPECIFIC → X64.dll
    │   ├── X64.csproj         # Compiled into single X64.dll
    │   ├── PS2/               # PS/2 keyboard/mouse (port I/O)
    │   │   ├── PS2Controller.cs
    │   │   ├── PS2Keyboard.cs
    │   │   └── PS2Mouse.cs
    │   ├── Legacy/            # ISA/legacy device support
    │   │   └── PIT.cs         # Programmable Interval Timer
    │   └── Platform/          # x64-specific platform code
    │       └── HPET.cs        # If arch-specific parts needed
    │
    ├── arm64/                 # ARM64 ARCHITECTURE-SPECIFIC → ARM64.dll
    │   ├── ARM64.csproj       # Compiled into single ARM64.dll
    │   ├── Platform/          # ARM64-specific platform code
    │   │   └── GIC.cs         # Generic Interrupt Controller
    │   └── ...
    │
    ├── shared/                # SHARED DRIVERS (arch-independent)
    │   │                      # Loaded as optional drivers from FS
    │   ├── storage/
    │   │   ├── virtio-blk/    # Virtio block (QEMU) - MMIO
    │   │   ├── ahci/          # AHCI/SATA - PCI MMIO
    │   │   └── nvme/          # NVMe - PCIe MMIO
    │   │
    │   ├── filesystem/
    │   │   ├── fat32/         # FAT32 filesystem
    │   │   └── ext4/          # ext4 filesystem
    │   │
    │   ├── network/
    │   │   ├── virtio-net/    # Virtio network (QEMU)
    │   │   ├── e1000/         # Intel e1000
    │   │   └── rtl8139/       # Realtek RTL8139
    │   │
    │   ├── usb/
    │   │   ├── xhci/          # xHCI (USB 3.x) controller
    │   │   ├── ehci/          # EHCI (USB 2.0) controller
    │   │   ├── ohci/          # OHCI (USB 1.1) controller
    │   │   ├── uhci/          # UHCI (USB 1.0) controller
    │   │   ├── usb-hid/       # HID class driver
    │   │   ├── usb-storage/   # Mass storage class
    │   │   ├── usb-audio/     # Audio class
    │   │   └── usb-video/     # Video class (webcams)
    │   │
    │   ├── graphics/
    │   │   └── virtio-gpu/    # Virtio GPU (QEMU)
    │   │
    │   ├── audio/
    │   │   └── hda/           # Intel HD Audio
    │   │
    │   └── serial/
    │       └── uart/          # UART 16550 (may have arch shims)
    │
    └── platform/              # Platform enumeration (shared, but loaded early)
        └── pci/               # PCI enumeration (ECAM=MMIO, legacy=arch shim)
```

### Build Integration

The build system conditionally includes architecture directories:

```bash
# For x64 builds:
#   - Compile src/drivers/x64/ → X64.dll
#   - Compile src/drivers/shared/* → individual .dll files
#   - Skip src/drivers/arm64/

# For ARM64 builds:
#   - Compile src/drivers/arm64/ → ARM64.dll
#   - Compile src/drivers/shared/* → individual .dll files
#   - Skip src/drivers/x64/
```

### Why This Organization?

1. **Clean separation**: Arch-specific code isolated in dedicated directories
2. **Single arch assembly**: All x64 drivers in one X64.dll simplifies boot loading
3. **Shared drivers maximize reuse**: Network, storage, USB class drivers work everywhere
4. **Conditional compilation**: Build only what's needed for target architecture
5. **Clear categorization**: Easy to know where a new driver should go

---

## Kernel Export Examples

### src/kernel/Exports/PortIO.cs
```csharp
public static class PortIOExports
{
    [UnmanagedCallersOnly(EntryPoint = "Kernel_OutByte")]
    public static void OutByte(ushort port, byte value) => CPU.OutByte(port, value);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_InByte")]
    public static byte InByte(ushort port) => CPU.InByte(port);
}
```

### src/kernel/Exports/Memory.cs
```csharp
public static class MemoryExports
{
    [UnmanagedCallersOnly(EntryPoint = "Kernel_AllocatePage")]
    public static ulong AllocatePage() => PageAllocator.AllocatePage();

    [UnmanagedCallersOnly(EntryPoint = "Kernel_PhysToVirt")]
    public static ulong PhysToVirt(ulong physAddr) => VirtualMemory.PhysToVirt(physAddr);
}
```

---

## DDK Examples

### src/ddk/Kernel/PortIO.cs
```csharp
namespace ProtonOS.DDK.Kernel;

public static class PortIO
{
    [DllImport("*", EntryPoint = "Kernel_OutByte")]
    public static extern void OutByte(ushort port, byte value);

    [DllImport("*", EntryPoint = "Kernel_InByte")]
    public static extern byte InByte(ushort port);
}
```

### src/ddk/Kernel/Memory.cs
```csharp
namespace ProtonOS.DDK.Kernel;

public static unsafe class Memory
{
    [DllImport("*", EntryPoint = "Kernel_AllocatePage")]
    public static extern ulong AllocatePage();

    [DllImport("*", EntryPoint = "Kernel_PhysToVirt")]
    public static extern ulong PhysToVirt(ulong physAddr);

    public static void* AllocateDMABuffer(out ulong physAddress)
    {
        physAddress = AllocatePage();
        return (void*)PhysToVirt(physAddress);
    }
}
```

### src/ddk/Kernel/CPU.cs (wraps kernel exports)
```csharp
namespace ProtonOS.DDK.Kernel;

public static unsafe class CPU
{
    [DllImport("*", EntryPoint = "Kernel_GetCpuCount")]
    public static extern int GetCpuCount();

    [DllImport("*", EntryPoint = "Kernel_GetCurrentCpu")]
    public static extern int GetCurrentCpu();

    [DllImport("*", EntryPoint = "Kernel_GetCpuInfo")]
    public static extern bool GetCpuInfo(int index, CpuInfo* info);

    [DllImport("*", EntryPoint = "Kernel_SetThreadAffinity")]
    public static extern ulong SetThreadAffinity(ulong mask);

    [DllImport("*", EntryPoint = "Kernel_GetThreadAffinity")]
    public static extern ulong GetThreadAffinity();

    [DllImport("*", EntryPoint = "Kernel_IsCpuOnline")]
    public static extern bool IsCpuOnline(int index);

    [DllImport("*", EntryPoint = "Kernel_GetBspIndex")]
    public static extern int GetBspIndex();

    [DllImport("*", EntryPoint = "Kernel_GetSystemAffinityMask")]
    public static extern ulong GetSystemAffinityMask();

    // Convenience: Pin current thread to a specific CPU
    public static void PinToCurrentCpu()
    {
        int cpu = GetCurrentCpu();
        SetThreadAffinity(1UL << cpu);
    }
}
```

---

## Interface Definitions

### IBlockDevice
```csharp
namespace ProtonOS.DDK.Storage;

public unsafe interface IBlockDevice
{
    string DeviceName { get; }
    ulong BlockCount { get; }
    uint BlockSize { get; }
    bool IsReadOnly { get; }

    int Read(ulong block, uint count, byte* buffer);
    int Write(ulong block, uint count, byte* buffer);
    void Flush();
}
```

### INetworkDevice
```csharp
namespace ProtonOS.DDK.Network;

public unsafe interface INetworkDevice
{
    string DeviceName { get; }
    byte[] MacAddress { get; }
    bool IsLinkUp { get; }
    uint MTU { get; set; }

    int Transmit(byte* packet, int length);
    void SetReceiveCallback(delegate*<byte*, int, void> callback);
}
```

### IUSBHostController
```csharp
namespace ProtonOS.DDK.USB;

public interface IUSBHostController
{
    USBControllerType Type { get; }
    int PortCount { get; }
    USBSpeed MaxSpeed { get; }

    void Initialize();
    void Shutdown();
    bool IsPortConnected(int port);
    void ResetPort(int port);
}
```

### IDriver
```csharp
namespace ProtonOS.DDK.Drivers;

public interface IDriver
{
    string DriverName { get; }
    Version DriverVersion { get; }
    DriverType Type { get; }

    bool Initialize();
    void Shutdown();
    void Suspend();
    void Resume();
}
```

---

## Hardware Reference

### PCI Discovery
- **ACPI MCFG table** → PCIe ECAM (memory-mapped config space)
- **Legacy** → Port I/O 0xCF8/0xCFC

### USB Controller Detection
| ProgIF | Type | Speed |
|--------|------|-------|
| 0x00 | UHCI | USB 1.0 |
| 0x10 | OHCI | USB 1.1 |
| 0x20 | EHCI | USB 2.0 |
| 0x30 | xHCI | USB 3.x |

### Storage Controller Detection
| Class | Subclass | ProgIF | Type |
|-------|----------|--------|------|
| 0x01 | 0x06 | 0x01 | AHCI |
| 0x01 | 0x08 | 0x02 | NVMe |

### Network Controller Detection
| Vendor | Device | Type |
|--------|--------|------|
| 0x1AF4 | 0x1000/0x1041 | Virtio-net |
| 0x8086 | 0x100E/100F | Intel e1000 |
| 0x10EC | 0x8139 | Realtek RTL8139 |
