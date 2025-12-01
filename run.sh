#!/bin/bash
# ProtonOS QEMU launcher
# Run from inside the dev container

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

ARCH="${ARCH:-x64}"
BUILD_DIR="build/${ARCH}"
IMG_FILE="${BUILD_DIR}/boot.img"

# Check for boot image
if [ ! -f "$IMG_FILE" ]; then
    echo "Error: Boot image not found: $IMG_FILE"
    echo "Run 'make image' first"
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

echo "OVMF: $OVMF"
echo "Image: $IMG_FILE"
echo ""
echo "Serial output below (Ctrl+A, X to exit QEMU):"
echo "=============================================="

qemu-system-x86_64 \
    -machine q35 \
    -cpu max \
    -m 256M \
    -drive if=pflash,format=raw,readonly=on,file="$OVMF" \
    -drive format=raw,file="$IMG_FILE" \
    -serial mon:stdio \
    -display none \
    -no-reboot \
    -no-shutdown
