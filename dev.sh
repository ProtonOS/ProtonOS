#!/bin/bash
# netos development environment launcher

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Use -it only if we have a TTY
if [ -t 0 ]; then
    DOCKER_FLAGS="-it"
else
    DOCKER_FLAGS=""
fi

docker run $DOCKER_FLAGS --rm \
    -v "${SCRIPT_DIR}:/usr/src/netos" \
    -w /usr/src/netos \
    netos-dev "$@"
