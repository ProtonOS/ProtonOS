#!/bin/bash
# ProtonOS development environment launcher

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Kill any existing protonos-dev containers first
docker ps -q --filter ancestor=protonos-dev | xargs -r docker kill >/dev/null 2>&1

# Use -it only if we have a TTY
if [ -t 0 ]; then
    DOCKER_FLAGS="-it"
else
    DOCKER_FLAGS=""
fi

docker run $DOCKER_FLAGS --rm \
    -v "${SCRIPT_DIR}:/usr/src/protonos" \
    -w /usr/src/protonos \
    protonos-dev "$@"
