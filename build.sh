#!/bin/bash
# ProtonOS build script - cleans and builds (native, no Docker)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
TEST_DISK="${BUILD_DIR}/test.img"
TESTDATA_DIR="$SCRIPT_DIR/testdata"

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
dotnet ildasm build/x64/ProtonOS.Drivers.Fat.dll -o build/x64/ProtonOS.Drivers.Fat.il 2>/dev/null || true
echo "IL disassembly complete"

# Create test disk image for FAT filesystem testing
echo "Creating test disk image..."
# Create 64MB FAT32 disk image (FAT32 needs at least ~33MB)
dd if=/dev/zero of="$TEST_DISK" bs=1M count=64 status=none
mformat -i "$TEST_DISK" -F -v TESTDISK ::

# Copy test files from testdata/ directory
if [ -d "$TESTDATA_DIR" ]; then
    echo "Copying test files from testdata/..."
    # Copy root level files
    for file in "$TESTDATA_DIR"/*; do
        if [ -f "$file" ]; then
            mcopy -i "$TEST_DISK" "$file" "::/$(basename "$file")"
        fi
    done
    # Copy directories recursively
    for dir in "$TESTDATA_DIR"/*/; do
        if [ -d "$dir" ]; then
            dirname=$(basename "$dir")
            mmd -i "$TEST_DISK" "::/$dirname" 2>/dev/null || true
            for file in "$dir"*; do
                if [ -f "$file" ]; then
                    mcopy -i "$TEST_DISK" "$file" "::/$dirname/$(basename "$file")"
                fi
            done
        fi
    done
fi
echo "Test disk created: $TEST_DISK"
