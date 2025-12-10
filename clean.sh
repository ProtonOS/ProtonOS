#!/bin/bash
# ProtonOS build cleanup script
# Removes build artifacts to ensure a clean build state

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILD_DIR="$SCRIPT_DIR/build"

if [ -d "$BUILD_DIR/x64" ]; then
    rm -rf "$BUILD_DIR/x64"
    echo "[clean] Removed build/x64"
fi
