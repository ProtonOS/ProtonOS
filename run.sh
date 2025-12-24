#!/bin/bash
# ProtonOS QEMU launcher
# Run from inside the dev container

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
IMG_FILE="${BUILD_DIR}/boot.img"
TEST_DISK="${BUILD_DIR}/test.img"

# Check for boot image
if [ ! -f "$IMG_FILE" ]; then
    echo "Error: Boot image not found: $IMG_FILE"
    echo "Run './build.sh' first"
    exit 1
fi

# Check for test disk
if [ ! -f "$TEST_DISK" ]; then
    echo "Error: Test disk not found: $TEST_DISK"
    echo "Run './build.sh' first"
    exit 1
fi

# Find OVMF firmware
OVMF_PATHS=(
    "/usr/share/OVMF/OVMF_CODE_4M.fd"
    "/usr/share/OVMF/OVMF_CODE.fd"
    "/usr/share/edk2-ovmf/x64/OVMF_CODE.fd"
)

OVMF=""
for path in "${OVMF_PATHS[@]}"; do
    if [ -f "$path" ]; then
        OVMF="$path"
        break
    fi
done

if [ -z "$OVMF" ]; then
    echo "Error: OVMF firmware not found"
    exit 1
fi

LOG_FILE="qemu.log"

# Remove old log to ensure clean output for each run
rm -f "$LOG_FILE"

echo "OVMF: $OVMF"
echo "Boot image: $IMG_FILE"
echo "Test disk: $TEST_DISK"
echo ""
echo "Serial output below (Ctrl+A, X to exit QEMU):"
echo "=============================================="

# Use tee to write serial output to both stdout and log file
# NUMA configuration: 2 nodes, 256MB each, CPUs 0-1 on node 0, CPUs 2-3 on node 1
# virtio-blk-pci with disable-legacy=on forces modern virtio 1.0+ mode
qemu-system-x86_64 \
    -machine q35 \
    -cpu max \
    -smp 4,sockets=2,cores=2,threads=1 \
    -m 512M \
    -object memory-backend-ram,id=mem0,size=256M \
    -object memory-backend-ram,id=mem1,size=256M \
    -numa node,nodeid=0,cpus=0-1,memdev=mem0 \
    -numa node,nodeid=1,cpus=2-3,memdev=mem1 \
    -numa dist,src=0,dst=1,val=20 \
    -drive if=pflash,format=raw,readonly=on,file="$OVMF" \
    -drive format=raw,file="$IMG_FILE" \
    -drive id=virtio-disk0,if=none,format=raw,file="$TEST_DISK" \
    -device virtio-blk-pci,drive=virtio-disk0,disable-legacy=on \
    -serial mon:stdio \
    -display none \
    -no-reboot \
    -no-shutdown 2>&1 | tee "$LOG_FILE"
