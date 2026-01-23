#!/bin/bash
# ProtonOS build script - cleans and builds (native, no Docker)

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
TEST_DISK="${BUILD_DIR}/test.img"
SATA_DISK="${BUILD_DIR}/sata.img"
TESTDATA_DIR="$SCRIPT_DIR/testdata"

# Kill any running QEMU instances
"$SCRIPT_DIR/kill.sh" 2>/dev/null || true

# Clean build directory
"$SCRIPT_DIR/clean.sh"

# Build
make image

# Generate IL disassembly for all runtime assemblies
echo "Generating IL disassembly..."
dotnet ildasm build/x64/JITTest.dll -o build/x64/JITTest.il 2>/dev/null || true
dotnet ildasm build/x64/TestSupport.dll -o build/x64/TestSupport.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.DDK.dll -o build/x64/ProtonOS.DDK.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Virtio.dll -o build/x64/ProtonOS.Drivers.Virtio.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.VirtioBlk.dll -o build/x64/ProtonOS.Drivers.VirtioBlk.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Fat.dll -o build/x64/ProtonOS.Drivers.Fat.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Ahci.dll -o build/x64/ProtonOS.Drivers.Ahci.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Ext2.dll -o build/x64/ProtonOS.Drivers.Ext2.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.VirtioNet.dll -o build/x64/ProtonOS.Drivers.VirtioNet.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Drivers.Test.dll -o build/x64/ProtonOS.Drivers.Test.il 2>/dev/null || true
dotnet ildasm build/x64/ProtonOS.Net.dll -o build/x64/ProtonOS.Net.il 2>/dev/null || true
dotnet ildasm build/x64/AppTest.dll -o build/x64/AppTest.il 2>/dev/null || true
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

# Create root filesystem image (EXT2)
# This will be mounted as / after boot
echo "Creating root filesystem image (EXT2)..."
ROOTFS_DIR="$SCRIPT_DIR/rootfs"
ROOTFS_STAGING="${BUILD_DIR}/rootfs_staging"

# Create staging directory with rootfs structure
rm -rf "$ROOTFS_STAGING"
mkdir -p "$ROOTFS_STAGING"

# Copy rootfs template
if [ -d "$ROOTFS_DIR" ]; then
    cp -r "$ROOTFS_DIR"/* "$ROOTFS_STAGING"/
fi

# Ensure standard directories exist
mkdir -p "$ROOTFS_STAGING/drivers"
mkdir -p "$ROOTFS_STAGING/etc"
mkdir -p "$ROOTFS_STAGING/system"
mkdir -p "$ROOTFS_STAGING/tmp"

# Copy test driver to /drivers (dynamically loaded after root mount)
if [ -f "${BUILD_DIR}/ProtonOS.Drivers.Test.dll" ]; then
    cp "${BUILD_DIR}/ProtonOS.Drivers.Test.dll" "$ROOTFS_STAGING/drivers/"
    echo "Copied test driver to rootfs/drivers/"
fi

# Copy testdata for VFS testing (optional, can be removed later)
if [ -d "$TESTDATA_DIR" ]; then
    cp -r "$TESTDATA_DIR"/* "$ROOTFS_STAGING"/
fi

# Create the ext2 filesystem with the staging directory contents
dd if=/dev/zero of="$SATA_DISK" bs=1M count=64 status=none
mkfs.ext2 -F -q -d "$ROOTFS_STAGING" "$SATA_DISK"

# Clean up staging
rm -rf "$ROOTFS_STAGING"

echo "Root filesystem created: $SATA_DISK (EXT2)"
echo "  /drivers  - additional drivers (loaded after root mount)"
echo "  /etc      - configuration files"
echo "  /system   - system files"
