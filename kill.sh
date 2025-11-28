#!/bin/bash
# Kill all running docker containers (leftover QEMU instances, etc.)
docker ps -q | xargs -r docker kill 2>/dev/null
