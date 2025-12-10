# ProtonOS - Claude Instructions

## Build & Test Commands

```bash
./build.sh    # Clean and build (kills QEMU, cleans, builds, generates IL)
./run.sh      # Run in QEMU (boots in ~3 seconds)
./kill.sh     # Kill any running QEMU instances
```

## Bash Tool Timeouts

| Command | Timeout |
|---------|---------|
| `./build.sh` | 120000ms (2 min) |
| `./run.sh` | 10000ms (10 sec) |
| `./kill.sh` | 2000ms |

**Do NOT use the `timeout` shell command** - use the Bash tool's `timeout` parameter instead.

## Workflow

1. Build: `./build.sh 2>&1` (timeout: 120000)
2. Test: `./run.sh 2>&1` (timeout: 10000)
3. Cleanup: `./kill.sh` (timeout: 2000)

**ALWAYS run `./kill.sh` after tests** - QEMU keeps running and locks build files.

**Do NOT compound commands** - just run and read the log file (`qemu.log`) after. Don't grep output until after running times out.

## Documentation

- Architecture details: `docs/ARCHITECTURE.md`
- korlib roadmap: `docs/KORLIB_PLAN.md`
