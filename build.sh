#!/bin/bash
# ProtonOS build script - kills containers, cleans, and builds

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Kill any running containers first (leftover QEMU instances, etc.)
"$SCRIPT_DIR/kill.sh" 2>/dev/null || true

# Use -it only if we have a TTY
if [ -t 0 ]; then
    DOCKER_FLAGS="-it"
else
    DOCKER_FLAGS=""
fi

# Run clean.sh then make image inside container
docker run $DOCKER_FLAGS --rm \
    -v "${SCRIPT_DIR}:/usr/src/protonos" \
    -w /usr/src/protonos \
    protonos-dev bash -c "./clean.sh && make image"
