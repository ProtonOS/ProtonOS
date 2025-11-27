# ManagedKernel Docker Development Environment

**Version:** 1.0  
**Target Platforms:** Windows (Docker Desktop + WSL2), Linux, macOS  

---

## Table of Contents

1. [Overview](#1-overview)
2. [Object File Formats Explained](#2-object-file-formats-explained)
3. [Architecture](#3-architecture)
4. [Quick Start](#4-quick-start)
5. [Dockerfile](#5-dockerfile)
6. [Build Scripts](#6-build-scripts)
7. [VS Code Integration](#7-vs-code-integration)
8. [Running and Debugging](#8-running-and-debugging)
9. [CI/CD Integration](#9-cicd-integration)
10. [Image Versioning Strategy](#10-image-versioning-strategy)
11. [Troubleshooting](#11-troubleshooting)

---

## 1. Overview

### Why Docker?

| Benefit | Description |
|---------|-------------|
| **Reproducibility** | Identical environment on every machine |
| **Portability** | Same container runs on Windows/Linux/macOS |
| **Versioning** | Pin exact tool versions, update via new images |
| **Isolation** | No pollution of host system |
| **Onboarding** | `docker pull` and you're ready to build |
| **CI/CD Ready** | Same container runs in GitHub Actions |

### Container Contents

| Tool | Version | Source | Purpose |
|------|---------|--------|---------|
| bflat | 8.0.x | GitHub release | C# → Native AOT compiler |
| NASM | 2.16.01 | `apt` package | x86_64 assembler (supports win64 output) |
| lld | 14.0 | `apt` package | LLVM PE/COFF linker |
| mtools | 4.0.33 | `apt` package | FAT32 image creation |
| QEMU | 7.2 | `apt` package | x86_64/ARM64 emulation |
| OVMF | 2022.11 | `apt` package | UEFI firmware |

**Base image:** Debian Bookworm (12) Slim  
**Only bflat requires download** - everything else comes from packages!

---

## 2. Object File Formats Explained

Understanding object file formats is crucial because we're cross-compiling: building on Linux but targeting UEFI (which uses PE/COFF, the Windows format).

### Format Overview

| Format | Extension | Used By | Description |
|--------|-----------|---------|-------------|
| **PE/COFF** | `.obj` | Windows, UEFI | Microsoft's Portable Executable format |
| **ELF** | `.o` | Linux, BSD | Executable and Linkable Format |
| **Mach-O** | `.o` | macOS | Apple's executable format |

### What UEFI Requires

UEFI executables are **PE32+ format** (64-bit PE/COFF) - the same format Windows uses. This means:

1. Object files must be PE/COFF (`.obj`)
2. The final executable must be PE32+ (`.EFI`)
3. We use Windows-style linking even on Linux

### NASM Output Formats

NASM can produce any format from any host - it's a true cross-assembler. The Debian Bookworm package (version 2.16.01) fully supports all formats we need:

```bash
# Same source file, different output formats:
nasm -f win64 code.asm -o code.obj    # PE/COFF for Windows/UEFI
nasm -f elf64 code.asm -o code.o      # ELF for Linux
nasm -f macho64 code.asm -o code.o    # Mach-O for macOS
nasm -f bin code.asm -o code.bin      # Raw binary (no format)
```

**For our UEFI kernel, always use `-f win64`** regardless of host OS.

You can verify your NASM supports win64 with:
```bash
$ nasm -hf | grep win64
    win64    Microsoft extended COFF for Win64 (x86-64)
```

### bflat Output Formats

bflat is also a cross-compiler. The `--os` flag determines the output format:

```bash
# On Linux host, targeting UEFI:
bflat build --os:uefi -c -o kernel.obj   # Produces PE/COFF .obj

# On Linux host, targeting Linux:
bflat build --os:linux -c -o kernel.o    # Produces ELF .o
```

**For our UEFI kernel, `--os:uefi` ensures PE/COFF output.**

### Linker Selection

LLVM provides multiple linker "flavors" via `lld`:

| Command | Format | Use Case |
|---------|--------|----------|
| `lld-link` | PE/COFF → PE | Windows, UEFI |
| `ld.lld` | ELF → ELF | Linux |
| `ld64.lld` | Mach-O → Mach-O | macOS |

On Ubuntu, the `lld` package provides `lld-link` for PE/COFF linking.

### Complete Format Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                    Source Files                                  │
├─────────────────────────────────────────────────────────────────┤
│  native_x64.asm          Kernel/*.cs                            │
│       │                       │                                  │
│       ▼                       ▼                                  │
│  ┌─────────┐            ┌──────────┐                            │
│  │  NASM   │            │  bflat   │                            │
│  │-f win64 │            │--os:uefi │                            │
│  └────┬────┘            └────┬─────┘                            │
│       │                      │                                   │
│       ▼                      ▼                                   │
│  native.obj             kernel.obj      ← Both PE/COFF format   │
│  (PE/COFF)              (PE/COFF)                               │
│       │                      │                                   │
│       └──────────┬───────────┘                                  │
│                  │                                               │
│                  ▼                                               │
│            ┌──────────┐                                         │
│            │ lld-link │                                         │
│            └────┬─────┘                                         │
│                 │                                                │
│                 ▼                                                │
│           BOOTX64.EFI       ← PE32+ executable                  │
│           (PE32+ / UEFI)                                        │
└─────────────────────────────────────────────────────────────────┘
```

### Verifying Formats

Use `file` command to verify object formats:

```bash
$ file build/x64/native.obj
build/x64/native.obj: Intel amd64 COFF object file, not stripped, 7 sections

$ file build/x64/kernel.obj  
build/x64/kernel.obj: Intel amd64 COFF object file, not stripped, 12 sections

$ file build/x64/BOOTX64.EFI
build/x64/BOOTX64.EFI: PE32+ executable (EFI application) x86-64, for MS Windows
```

If you see "ELF" anywhere, something is misconfigured.

---

## 3. Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        Host System (Windows/Linux/macOS)                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│   ┌──────────────────────────────────────────────────────────────────────┐  │
│   │                    Docker Desktop / Docker Engine                     │  │
│   │                                                                       │  │
│   │   ┌───────────────────────────────────────────────────────────────┐  │  │
│   │   │               managedkernel-dev Container                      │  │  │
│   │   │                                                                │  │  │
│   │   │   ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌────────────┐ │  │  │
│   │   │   │   bflat    │ │    NASM    │ │    lld     │ │   mtools   │ │  │  │
│   │   │   │  (C# AOT)  │ │ (Assembler)│ │  (Linker)  │ │(FAT32 img) │ │  │  │
│   │   │   └────────────┘ └────────────┘ └────────────┘ └────────────┘ │  │  │
│   │   │                                                                │  │  │
│   │   │   ┌────────────┐ ┌────────────┐ ┌────────────────────────────┐│  │  │
│   │   │   │    QEMU    │ │   OVMF/    │ │  /usr/src/netos (mount)    ││  │  │
│   │   │   │ (Emulator) │ │   AAVMF    │ │   - Source code            ││  │  │
│   │   │   └────────────┘ └────────────┘ │   - Build outputs          ││  │  │
│   │   │                                  └────────────────────────────┘│  │  │
│   │   └───────────────────────────────────────────────────────────────┘  │  │
│   │                                                                       │  │
│   └──────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│   ┌────────────────────────────────────────┐                                │
│   │         Project Directory               │                                │
│   │   /path/to/netos/                      │  ← Mounted as /usr/src/netos   │
│   └────────────────────────────────────────┘                                │
│                                                                              │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Workflow

1. **Edit** source files on host (VS Code, Rider, etc.)
2. **Build** inside container (sources mounted at `/usr/src/netos`)
3. **Test** with QEMU inside container (or on host with forwarded display)
4. **Debug** with GDB inside container or attach from host

---

## 4. Quick Start

### Prerequisites

- **Windows:** Docker Desktop with WSL2 backend
- **Linux:** Docker Engine
- **macOS:** Docker Desktop

### First Time Setup

```bash
# Clone the repository
git clone https://github.com/yourorg/ManagedKernel.git
cd ManagedKernel

# Build the development container (one-time, ~5 minutes)
docker build -t managedkernel-dev .

# Or pull pre-built image (if published)
# docker pull ghcr.io/yourorg/managedkernel-dev:latest
```

### Build the Kernel

```bash
# Start development shell (recommended)
./dev.sh

# Or manually:
docker run -it --rm \
    -v "$(pwd):/usr/src/netos" \
    -w /usr/src/netos \
    netos-dev

# Inside container:
./build.sh
```

### Run in QEMU

```bash
# Inside container:
./run.sh

# Serial output appears in terminal
# Press Ctrl+A, X to exit QEMU
```

### One-Liner Build & Run

```bash
./dev.sh ./build.sh && ./run.sh
```

---

## 5. Dockerfile

### Dockerfile

```dockerfile
# ManagedKernel Development Environment
# Based on Debian Bookworm (12) - lean and stable

FROM debian:bookworm-slim

# Prevent interactive prompts
ENV DEBIAN_FRONTEND=noninteractive

# ============================================================
# Install all tools from packages where possible
# ============================================================
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Build essentials
    ca-certificates \
    curl \
    git \
    make \
    # NASM assembler (2.16.01 in bookworm - supports win64 output)
    nasm \
    # LLVM/lld for PE/COFF linking
    lld \
    # mtools for FAT32 image creation (mformat, mcopy, mmd)
    mtools \
    dosfstools \
    # QEMU for x86_64 and ARM64 emulation
    qemu-system-x86 \
    qemu-system-arm \
    # UEFI firmware
    ovmf \
    qemu-efi-aarch64 \
    # Debugging tools
    gdb \
    gdb-multiarch \
    # Utilities
    file \
    xxd \
    tree \
    less \
    # Clean up
    && rm -rf /var/lib/apt/lists/*

# ============================================================
# Install bflat (not available as package, download release)
# ============================================================
ARG BFLAT_VERSION=8.0.5
RUN curl -fsSL "https://github.com/bflattened/bflat/releases/download/v${BFLAT_VERSION}/bflat-${BFLAT_VERSION}-linux-glibc-x64.tar.gz" \
    | tar -xzC /usr/local/lib \
    && ln -s /usr/local/lib/bflat/bflat /usr/local/bin/bflat

# ============================================================
# Create convenient symlinks for UEFI firmware
# ============================================================
RUN ln -sf /usr/share/OVMF/OVMF_CODE_4M.fd /opt/OVMF_x64.fd \
    && ln -sf /usr/share/AAVMF/AAVMF_CODE.fd /opt/OVMF_aa64.fd 2>/dev/null || true

# ============================================================
# Verify installations
# ============================================================
RUN echo "=== Tool Versions ===" \
    && nasm --version \
    && bflat --version \
    && ld.lld --version \
    && qemu-system-x86_64 --version | head -1 \
    && echo "=== NASM Output Formats ===" \
    && nasm -hf | grep -E "win64|elf64" \
    && echo "=== All tools verified ==="

# Set up working directory
WORKDIR /workspace

# Default command
CMD ["/bin/bash"]

# Labels
LABEL org.opencontainers.image.title="ManagedKernel Dev Environment"
LABEL org.opencontainers.image.description="Development environment for ManagedKernel OS"
LABEL org.opencontainers.image.version="1.0"
LABEL org.opencontainers.image.base.name="debian:bookworm-slim"
```

### .dockerignore

```
# Build artifacts
build/
logs/
*.obj
*.o
*.EFI
*.img

# IDE files
.vs/
.vscode/
.idea/
*.user

# Git
.git/
.gitignore

# Documentation
*.md
docs/

# Docker
Dockerfile
.dockerignore
docker-compose.yml
```

---

## 6. Build Scripts

### build.sh

```bash
#!/bin/bash
set -e

# ============================================================
# ManagedKernel Build Script
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Configuration
ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
SRC_DIR="src"
ARCH_DIR="arch/${ARCH}"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

info() { echo -e "${GREEN}[INFO]${NC} $1"; }
warn() { echo -e "${YELLOW}[WARN]${NC} $1"; }
error() { echo -e "${RED}[ERROR]${NC} $1"; exit 1; }

# Create build directory
mkdir -p "$BUILD_DIR"
mkdir -p logs

# ============================================================
# Step 1: Assemble native code
# ============================================================
info "Assembling native layer (${ARCH})..."

if [ "$ARCH" = "x64" ]; then
    # IMPORTANT: Use win64 format for UEFI targets, even on Linux!
    # UEFI uses PE/COFF executables, same as Windows.
    # NASM is a cross-assembler - the -f flag selects output format
    # at assembly time, not build time.
    nasm -f win64 \
        "${ARCH_DIR}/native_x64.asm" \
        -o "${BUILD_DIR}/native.obj" \
        -l "${BUILD_DIR}/native.lst"
elif [ "$ARCH" = "arm64" ]; then
    # ARM64 assembly would use a different assembler (GNU as or LLVM)
    # For now, placeholder
    error "ARM64 assembly not yet implemented"
fi

info "  → ${BUILD_DIR}/native.obj"

# ============================================================
# Step 2: Compile C# to object file
# ============================================================
info "Compiling C# kernel..."

# Collect all C# source files
CS_FILES=$(find "$SRC_DIR" -name "*.cs" | tr '\n' ' ')

if [ -z "$CS_FILES" ]; then
    error "No C# source files found in $SRC_DIR"
fi

# bflat with --os:uefi produces PE/COFF object files (.obj)
# even when running on Linux - it's a cross-compiler
bflat build $CS_FILES \
    --os:uefi \
    --arch:${ARCH} \
    --stdlib:zero \
    --no-stacktrace-data \
    --no-globalization \
    --no-reflection \
    --no-exception-messages \
    -d ARCH_X64 \
    -d BOOT_UEFI \
    -c \
    -o "${BUILD_DIR}/kernel.obj" \
    2>&1 | tee logs/bflat.log

if [ ! -f "${BUILD_DIR}/kernel.obj" ]; then
    error "C# compilation failed - no output file"
fi

info "  → ${BUILD_DIR}/kernel.obj"

# ============================================================
# Step 3: Link into UEFI executable
# ============================================================
info "Linking UEFI executable..."

if [ "$ARCH" = "x64" ]; then
    EFI_NAME="BOOTX64.EFI"
    
    # Use lld-link (LLVM's PE/COFF linker) directly
    # Both kernel.obj and native.obj are PE/COFF format
    # 
    # Note: On Linux, we use lld-link (not ld.lld with -flavor link)
    # because we're linking PE/COFF objects into a PE executable
    lld-link \
        -subsystem:efi_application \
        -entry:EfiMain \
        -out:"${BUILD_DIR}/${EFI_NAME}" \
        "${BUILD_DIR}/kernel.obj" \
        "${BUILD_DIR}/native.obj" \
        2>&1 | tee logs/link.log
        
elif [ "$ARCH" = "arm64" ]; then
    EFI_NAME="BOOTAA64.EFI"
    error "ARM64 linking not yet implemented"
fi

if [ ! -f "${BUILD_DIR}/${EFI_NAME}" ]; then
    error "Linking failed - no output file"
fi

info "  → ${BUILD_DIR}/${EFI_NAME}"

# ============================================================
# Step 4: Create boot image
# ============================================================
info "Creating boot image..."

IMG_FILE="${BUILD_DIR}/boot.img"
IMG_SIZE_MB=64

# Create empty image
dd if=/dev/zero of="$IMG_FILE" bs=1M count=$IMG_SIZE_MB status=none

# Format as FAT32
mformat -i "$IMG_FILE" -F -v KERNEL :: 

# Create EFI directory structure
mmd -i "$IMG_FILE" ::/EFI
mmd -i "$IMG_FILE" ::/EFI/BOOT

# Copy kernel
mcopy -i "$IMG_FILE" "${BUILD_DIR}/${EFI_NAME}" "::/EFI/BOOT/${EFI_NAME}"

info "  → ${IMG_FILE}"

# ============================================================
# Summary
# ============================================================
echo ""
echo "=========================================="
echo -e "${GREEN}Build successful!${NC}"
echo "=========================================="
echo "Kernel:     ${BUILD_DIR}/${EFI_NAME}"
echo "Boot image: ${BUILD_DIR}/boot.img"
echo ""
echo "Run with: ./run.sh"
```

### run.sh

```bash
#!/bin/bash
set -e

# ============================================================
# ManagedKernel Run Script (QEMU)
# ============================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Configuration
ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
IMG_FILE="${BUILD_DIR}/boot.img"
MEMORY="${MEMORY:-256M}"
DEBUG="${DEBUG:-0}"

# Check for boot image
if [ ! -f "$IMG_FILE" ]; then
    echo "Error: Boot image not found: $IMG_FILE"
    echo "Run ./build.sh first"
    exit 1
fi

# UEFI firmware paths (try multiple locations)
if [ "$ARCH" = "x64" ]; then
    OVMF_PATHS=(
        "/opt/OVMF.fd"
        "/usr/share/OVMF/OVMF_CODE_4M.fd"
        "/usr/share/OVMF/OVMF_CODE.fd"
        "/usr/share/edk2-ovmf/x64/OVMF_CODE.fd"
    )
    QEMU_CMD="qemu-system-x86_64"
    MACHINE="q35"
    CPU="qemu64"
elif [ "$ARCH" = "arm64" ]; then
    OVMF_PATHS=(
        "/opt/AAVMF.fd"
        "/usr/share/AAVMF/AAVMF_CODE.fd"
        "/usr/share/qemu-efi-aarch64/QEMU_EFI.fd"
    )
    QEMU_CMD="qemu-system-aarch64"
    MACHINE="virt"
    CPU="cortex-a72"
fi

# Find UEFI firmware
OVMF=""
for path in "${OVMF_PATHS[@]}"; do
    if [ -f "$path" ]; then
        OVMF="$path"
        break
    fi
done

if [ -z "$OVMF" ]; then
    echo "Error: UEFI firmware not found"
    echo "Tried: ${OVMF_PATHS[*]}"
    exit 1
fi

echo "Using UEFI firmware: $OVMF"
echo "Boot image: $IMG_FILE"
echo ""

# Build QEMU command
QEMU_ARGS=(
    -machine "$MACHINE"
    -cpu "$CPU"
    -m "$MEMORY"
    -bios "$OVMF"
    -drive "format=raw,file=$IMG_FILE"
    -serial stdio
    -no-reboot
    -no-shutdown
)

# Debug options
if [ "$DEBUG" = "1" ]; then
    echo "Debug mode enabled - GDB server on port 1234"
    echo "Connect with: gdb -ex 'target remote localhost:1234'"
    QEMU_ARGS+=(-s -S)  # -s: gdbserver on 1234, -S: freeze on start
fi

# Add debug console for UEFI debug output
QEMU_ARGS+=(
    -debugcon "file:logs/debug.log"
    -global isa-debugcon.iobase=0x402
)

# Create logs directory
mkdir -p logs

# Run QEMU
echo "Starting QEMU..."
echo "Press Ctrl+A, X to exit"
echo "=========================================="
exec $QEMU_CMD "${QEMU_ARGS[@]}"
```

### clean.sh

```bash
#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "Cleaning build artifacts..."

rm -rf build/
rm -rf logs/

echo "Done."
```

### Make scripts executable

```bash
chmod +x build.sh run.sh clean.sh
```

---

## 7. VS Code Integration

### .devcontainer/devcontainer.json

VS Code can automatically use the Docker container for development:

```json
{
    "name": "ManagedKernel Dev",
    "build": {
        "dockerfile": "../Dockerfile",
        "context": ".."
    },
    "workspaceFolder": "/workspace",
    "workspaceMount": "source=${localWorkspaceFolder},target=/workspace,type=bind",
    
    "customizations": {
        "vscode": {
            "extensions": [
                "ms-dotnettools.csharp",
                "ms-dotnettools.csdevkit",
                "13xforever.language-x86-64-assembly",
                "webfreak.debug",
                "ms-vscode.hexeditor"
            ],
            "settings": {
                "terminal.integrated.defaultProfile.linux": "bash",
                "files.associations": {
                    "*.asm": "asm-intel-x86-generic"
                }
            }
        }
    },
    
    "postCreateCommand": "echo 'Dev container ready!'",
    
    "remoteUser": "root"
}
```

### .vscode/tasks.json

```json
{
    "version": "2.0.0",
    "tasks": [
        {
            "label": "Build Kernel",
            "type": "shell",
            "command": "./build.sh",
            "group": {
                "kind": "build",
                "isDefault": true
            },
            "problemMatcher": []
        },
        {
            "label": "Run in QEMU",
            "type": "shell",
            "command": "./run.sh",
            "group": "test",
            "problemMatcher": []
        },
        {
            "label": "Run with Debugger",
            "type": "shell",
            "command": "DEBUG=1 ./run.sh",
            "group": "test",
            "problemMatcher": []
        },
        {
            "label": "Clean",
            "type": "shell",
            "command": "./clean.sh",
            "problemMatcher": []
        }
    ]
}
```

### .vscode/launch.json

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Attach to QEMU (GDB)",
            "type": "cppdbg",
            "request": "launch",
            "program": "${workspaceFolder}/build/x64/BOOTX64.EFI",
            "miDebuggerServerAddress": "localhost:1234",
            "miDebuggerPath": "gdb-multiarch",
            "cwd": "${workspaceFolder}",
            "setupCommands": [
                {
                    "text": "set architecture i386:x86-64:intel",
                    "ignoreFailures": false
                }
            ]
        }
    ]
}
```

---

## 8. Running and Debugging

### Interactive Development Shell

```bash
# Start a shell in the container (recommended)
./dev.sh

# Or manually:
docker run -it --rm \
    -v "$(pwd):/usr/src/netos" \
    -w /usr/src/netos \
    --name netos-dev \
    netos-dev

# Now you have access to all tools:
bflat -v
nasm --version
lld-link --version
qemu-system-x86_64 --version
```

### Building Specific Architectures

```bash
# Build for x64 (default)
./build.sh

# Build for ARM64
ARCH=arm64 ./build.sh

# Run ARM64 version
ARCH=arm64 ./run.sh
```

### Debugging with GDB

```bash
# Terminal 1: Start QEMU with GDB server
DEBUG=1 ./run.sh

# Terminal 2: Connect GDB
gdb-multiarch build/x64/BOOTX64.EFI -ex 'target remote localhost:1234'

# In GDB:
(gdb) break EfiMain
(gdb) continue
(gdb) info registers
(gdb) x/10i $rip
```

### Viewing Serial Output

Serial output goes to the terminal when using `./run.sh`. To capture to a file:

```bash
./run.sh 2>&1 | tee logs/serial.log
```

---

## 9. CI/CD Integration

### GitHub Actions Workflow

`.github/workflows/build.yml`:

```yaml
name: Build Kernel

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Build Dev Container
        run: docker build -t managedkernel-dev .
      
      - name: Build Kernel (x64)
        run: |
          docker run --rm \
            -v ${{ github.workspace }}:/workspace \
            -w /workspace \
            managedkernel-dev \
            ./build.sh
      
      - name: Upload Artifacts
        uses: actions/upload-artifact@v4
        with:
          name: kernel-x64
          path: |
            build/x64/BOOTX64.EFI
            build/x64/boot.img

  build-arm64:
    runs-on: ubuntu-latest
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Build Dev Container
        run: docker build -t managedkernel-dev .
      
      - name: Build Kernel (ARM64)
        run: |
          docker run --rm \
            -v ${{ github.workspace }}:/workspace \
            -w /workspace \
            -e ARCH=arm64 \
            managedkernel-dev \
            ./build.sh
        continue-on-error: true  # ARM64 not fully implemented yet
```

### Container Registry Publishing

```yaml
# .github/workflows/docker-publish.yml
name: Publish Dev Container

on:
  push:
    branches: [main]
    paths:
      - 'Dockerfile'

jobs:
  push:
    runs-on: ubuntu-latest
    permissions:
      packages: write
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Login to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ghcr.io
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Build and Push
        uses: docker/build-push-action@v5
        with:
          push: true
          tags: |
            ghcr.io/${{ github.repository_owner }}/managedkernel-dev:latest
            ghcr.io/${{ github.repository_owner }}/managedkernel-dev:${{ github.sha }}
```

---

## 10. Image Versioning Strategy

### Semantic Versioning

| Version | When to Bump |
|---------|--------------|
| `1.0.0` → `1.0.1` | Bug fixes, minor tool updates |
| `1.0.0` → `1.1.0` | New tools added, feature updates |
| `1.0.0` → `2.0.0` | Breaking changes (tool removals, major version bumps) |

### Tag Strategy

```bash
# Development builds
ghcr.io/yourorg/managedkernel-dev:latest     # Latest from main
ghcr.io/yourorg/managedkernel-dev:sha-abc123 # Specific commit

# Release builds
ghcr.io/yourorg/managedkernel-dev:1.0.0      # Stable release
ghcr.io/yourorg/managedkernel-dev:1.0        # Latest patch of 1.0.x
ghcr.io/yourorg/managedkernel-dev:1          # Latest minor of 1.x.x
```

### Tool Version Pinning

Most tools come from Debian packages, so versions are tied to the base image:

```dockerfile
# Pin bflat version (only tool downloaded separately)
ARG BFLAT_VERSION=8.0.5

# Other tools: pinned by Debian version
# Debian Bookworm (12) provides:
#   - NASM 2.16.01
#   - lld 14.0
#   - QEMU 7.2
#   - mtools 4.0.33
#   - OVMF 2022.11
```

To get newer package versions, either:
1. Use Debian Trixie (testing) as base - has newer packages
2. Use bookworm-backports for specific packages
3. Download specific tools separately (like we do with bflat)

---

## 11. Troubleshooting

### "bflat: command not found"

```bash
# Check if bflat is installed
ls -la /opt/bflat/bflat

# Check PATH
echo $PATH | tr ':' '\n' | grep bflat

# Manual test
/opt/bflat/bflat --version
```

### "nasm: error: unrecognized output format 'win64'"

This shouldn't happen with Debian Bookworm's NASM 2.16.01, but verify:

```bash
# List supported formats
nasm -hf | grep win64

# Should show:
#     win64    Microsoft extended COFF for Win64 (x86-64)
```

### "Permission denied" on scripts

```bash
# Inside container or on host
chmod +x build.sh run.sh clean.sh
```

### QEMU: "Could not access KVM kernel module"

KVM acceleration isn't available in most Docker containers. The container uses software emulation which is slower but works everywhere.

### QEMU: Display issues

For headless operation (CI), QEMU runs without display. Serial output goes to terminal.

For local testing with GUI:

```bash
# Linux with X11
docker run -it --rm \
    -v "$(pwd):/workspace" \
    -v /tmp/.X11-unix:/tmp/.X11-unix \
    -e DISPLAY=$DISPLAY \
    managedkernel-dev ./run.sh

# macOS/Windows: Use VNC or serial-only mode
```

### Build fails with "no space left on device"

```bash
# Clean Docker build cache
docker builder prune

# Or increase Docker disk allocation in Docker Desktop settings
```

### mformat: "Cannot initialize '::''"

Make sure the image file exists and has correct size:

```bash
# Check image
ls -la build/x64/boot.img

# Recreate if needed
dd if=/dev/zero of=build/x64/boot.img bs=1M count=64
```

---

## Appendix A: docker-compose.yml

For more complex setups:

```yaml
version: '3.8'

services:
  dev:
    build: .
    image: managedkernel-dev
    volumes:
      - .:/workspace
    working_dir: /workspace
    stdin_open: true
    tty: true
    # Uncomment for GUI support on Linux
    # environment:
    #   - DISPLAY=${DISPLAY}
    # volumes:
    #   - /tmp/.X11-unix:/tmp/.X11-unix
```

Usage:
```bash
docker-compose run dev
# or
docker-compose run dev ./build.sh
```

---

## Appendix B: Minimal Test Program

Create a minimal test to verify the toolchain:

`src/test/Hello.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Kernel.Test;

public static unsafe class Hello
{
    // UEFI Simple Text Output Protocol GUID
    // For testing, we'll just return success
    
    [UnmanagedCallersOnly(EntryPoint = "EfiMain")]
    public static long EfiMain(void* imageHandle, void* systemTable)
    {
        // If we get here and return, UEFI considers boot successful
        // (though nothing visible happens without serial output)
        return 0; // EFI_SUCCESS
    }
}
```

Build and test:

```bash
# Quick test without full kernel
mkdir -p build/test

bflat build src/test/Hello.cs \
    --os:uefi \
    --arch:x64 \
    --stdlib:zero \
    -o build/test/BOOTX64.EFI

# Verify it's a valid PE executable
file build/test/BOOTX64.EFI
# Should show: PE32+ executable (EFI application) x86-64, for MS Windows

# If you want to verify object file formats during build:
bflat build src/test/Hello.cs \
    --os:uefi \
    --arch:x64 \
    --stdlib:zero \
    -c \
    -o build/test/hello.obj

file build/test/hello.obj
# Should show: Intel amd64 COFF object file, not stripped
# NOT: ELF 64-bit LSB relocatable (that would be wrong!)
```
