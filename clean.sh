#!/bin/bash
# netos build cleanup script (runs inside container)
# Removes build artifacts to ensure a clean build state

BUILD_DIR="/usr/src/netos/build"

if [ -d "$BUILD_DIR" ]; then
    rm -rf "$BUILD_DIR/x64"
    echo "[clean] Removed build/x64"
fi
