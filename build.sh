#!/bin/bash
# ProtonOS build script - cleans and builds (native, no Docker)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

# Kill any running QEMU instances
"$SCRIPT_DIR/kill.sh" 2>/dev/null || true

# Clean build directory
"$SCRIPT_DIR/clean.sh"

# Build
make image

# Generate IL disassembly for all runtime assemblies
echo "Generating IL disassembly..."
dotnet ildasm build/x64/FullTest.dll -o build/x64/FullTest.il 2>/dev/null || true
dotnet ildasm build/x64/TestSupport.dll -o build/x64/TestSupport.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.DDK.dll -o build/x64/ProtonOS.DDK.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Virtio.dll -o build/x64/ProtonOS.Drivers.Virtio.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.VirtioBlk.dll -o build/x64/ProtonOS.Drivers.VirtioBlk.il 2>/dev/null || true
echo "IL disassembly complete: FullTest.il, TestSupport.il, ProtonOS.DDK.il, ProtonOS.Drivers.Virtio.il, ProtonOS.Drivers.VirtioBlk.il"
