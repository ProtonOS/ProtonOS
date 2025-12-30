# ProtonOS Boot Protocol Specification

## Overview

This document specifies the interface between platform-specific bootloaders and the ProtonOS kernel. The goal is to abstract away platform differences (UEFI, legacy BIOS, Apple M-series, etc.) so the kernel doesn't need to know how it was booted.

## Design Principles

1. **Kernel is platform-agnostic**: The kernel receives a standardized `BootInfo` structure regardless of boot method
2. **Loader handles platform specifics**: ExitBootServices, firmware teardown, etc. happen in the loader
3. **Predictable load address**: Kernel loads at 128MB for debuggability
4. **Pre-loaded files**: Files needed at boot are loaded into memory before jumping to kernel
5. **Single-core entry**: Kernel enters on one core in 64-bit long mode; other cores are offline

## Memory Layout

```
0x00000000 - 0x000FFFFF    Reserved (real mode IVT, BIOS data, etc.)
0x00100000 - 0x001FFFFF    BootInfo region (1 MB)
                           - BootInfo struct at 0x100000
                           - Memory map immediately after
                           - LoadedFile array at 0x100100
0x00200000 - 0x01FFFFFF    Free (available after boot)
0x02000000 - 0x05FFFFFF    Loaded files (64 MB reserved, actual usage varies)
0x06000000 - 0x07FFFFFF    Free
0x08000000 - 0x087FFFFF    Kernel (128 MB base, 8 MB max size)
0x08800000+                Free (large contiguous block)
```

The kernel loads at **128 MB (0x8000000)**. The actual address is recorded in `BootInfo.KernelPhysicalBase`.

Maximum kernel size: **8 MB** (0x00800000)

## Kernel Entry Point

```c
void KernelEntry(BootInfo* bootInfo)
```

**Calling convention**: Microsoft x64 ABI
- `rcx` = pointer to BootInfo structure
- Stack is 16-byte aligned
- At least 8 KB of stack available

**CPU State at Entry**:
- 64-bit long mode enabled
- Paging enabled (identity-mapped 0-4GB minimum)
- Interrupts disabled
- Single core running (BSP)
- FPU/SSE available
- No GDT/IDT installed (kernel must set up its own)

**Important**: UEFI Boot Services are NOT available. The bootloader calls ExitBootServices before jumping to the kernel. The kernel must not attempt to use any UEFI services.

## BootInfo Structure

```c
#define BOOTINFO_MAGIC 0x50524F544F4E4F53  // "PROTONOS"
#define BOOTINFO_VERSION 2

struct BootInfo {
    uint64_t Magic;              // Must be BOOTINFO_MAGIC
    uint32_t Version;            // BOOTINFO_VERSION
    uint32_t Flags;              // Feature flags (BootInfoFlags)

    // Memory information (raw EFI memory map format)
    uint64_t MemoryMapAddress;   // Physical address of EFI_MEMORY_DESCRIPTOR array
    uint32_t MemoryMapEntries;   // Number of entries
    uint32_t MemoryMapEntrySize; // Size of each descriptor (48 bytes typical)

    // Kernel location
    uint64_t KernelPhysicalBase; // Where kernel was loaded (0x8000000)
    uint64_t KernelVirtualBase;  // Requested virtual base (from PE header)
    uint64_t KernelSize;         // Total size in memory
    uint64_t KernelEntryOffset;  // Offset from base to entry point

    // Loaded files
    uint64_t LoadedFilesAddress; // Physical address of LoadedFile array
    uint32_t LoadedFilesCount;   // Number of loaded files
    uint32_t Reserved1;

    // ACPI
    uint64_t AcpiRsdp;           // Physical address of RSDP (v1 or v2)

    // Framebuffer (optional)
    uint64_t FramebufferAddress; // Physical address, 0 if none
    uint32_t FramebufferWidth;
    uint32_t FramebufferHeight;
    uint32_t FramebufferPitch;   // Bytes per row
    uint32_t FramebufferBpp;     // Bits per pixel

    // Debug
    uint64_t SerialPort;         // I/O port for debug serial (0x3F8 typically)

    // Reserved for future expansion
    uint64_t Reserved[8];
};

enum BootInfoFlags {
    HasFramebuffer = 0x01,
    HasAcpi        = 0x02,
    HasSerial      = 0x04,
};

struct LoadedFile {
    uint64_t PhysicalAddress;    // Where file is loaded
    uint64_t Size;               // File size in bytes
    char Name[64];               // Null-terminated filename (ASCII)
    uint32_t Flags;              // LoadedFileFlags
    uint32_t Reserved;
};

enum LoadedFileFlags {
    File_Executable = 0x01,      // PE/COFF executable
    File_Driver     = 0x02,      // Driver DLL
    File_Data       = 0x04,      // Data file
};
```

## Memory Map Format

The memory map is stored in **raw EFI format** (EFI_MEMORY_DESCRIPTOR array). This allows the kernel to use existing EFI parsing code. Each entry is 48 bytes:

```c
struct EFI_MEMORY_DESCRIPTOR {
    uint32_t Type;           // EFI_MEMORY_TYPE
    uint64_t PhysicalStart;  // Physical address of region
    uint64_t VirtualStart;   // Virtual address (unused)
    uint64_t NumberOfPages;  // Size in 4KB pages
    uint64_t Attribute;      // Memory attributes
};
```

### Reclaimable Memory Types

After ExitBootServices, the kernel can reclaim memory marked as:
- `EfiLoaderCode` (1) - Bootloader code
- `EfiLoaderData` (2) - Bootloader data
- `EfiBootServicesCode` (3) - UEFI boot services code
- `EfiBootServicesData` (4) - UEFI boot services data
- `EfiConventionalMemory` (7) - Free memory

The kernel should NOT use:
- `EfiRuntimeServicesCode` (5) - UEFI runtime services
- `EfiRuntimeServicesData` (6) - UEFI runtime data
- `EfiACPIMemoryNVS` (10) - ACPI non-volatile storage
- `EfiReservedMemoryType` (0) - Reserved by firmware

## Files Loaded by Bootloader

The UEFI bootloader loads the following files before jumping to kernel:

| File | Path | Purpose |
|------|------|---------|
| KERNEL.BIN | \EFI\BOOT\KERNEL.BIN | Kernel binary (PE/COFF) |
| FullTest.dll | \FullTest.dll | Test harness |
| korlib.dll | \korlib.dll | Core library IL for JIT |
| TestSupport.dll | \TestSupport.dll | Test support library |
| ProtonOS.DDK.dll | \ProtonOS.DDK.dll | Driver Development Kit |
| Drivers | \drivers\*.dll | Device drivers |

Files are loaded contiguously starting at 32 MB (0x2000000). The `LoadedFile` array contains the actual addresses and sizes.

## Loader Responsibilities

### UEFI Loader (src/bootloader/boot.asm)

1. **Initialize**: Get SystemTable, BootServices
2. **Load files**: Use SimpleFileSystem to load kernel and support files to 32MB region
3. **Parse kernel PE**: Extract entry point, size, relocation info
4. **Allocate memory**: At 128 MB for kernel
5. **Relocate kernel**: Copy sections, apply base relocations
6. **Get memory map**: Store raw EFI map in BootInfo
7. **Find ACPI**: Locate RSDP in EFI configuration tables
8. **Build BootInfo**: Populate structure at 1 MB
9. **Exit boot services**: Call ExitBootServices()
10. **Set up paging**: Identity map 0-4GB
11. **Jump to kernel**: Pass BootInfo pointer in RCX

### Future: Legacy BIOS Loader

Would use INT 15h for memory map, load from disk via INT 13h.

### Future: Apple M-series Loader

Would interface with m1n1 or similar, use DeviceTree for hardware info.

## Version History

| Version | Changes |
|---------|---------|
| 1 | Initial specification |
| 2 | Bootloader handles ExitBootServices and file loading; removed UEFI handles from BootInfo; memory map uses raw EFI format |

## Implementation Notes

### Memory Efficiency

The kernel's PageAllocator reclaims all BS*/Loader* memory types after boot. With 512MB RAM:
- ~505 MB reclaimable (after firmware reservations)
- ~2.3 MB reserved (kernel + bootinfo + files)
- ~503 MB free for kernel use

### Current Limitations (v2)

- No framebuffer support (serial only)
- No kernel command line
- No initramfs/initrd concept
- Single fixed file list

### Future Considerations

- SMP startup (pass AP trampoline info)
- Secure boot chain
- Measured boot support
- Kernel command line parameters
