# ProtonOS - Claude Instructions

## Build & Test Commands

```bash
./build.sh              # Build (kills containers, cleans, builds)
./dev.sh ./run.sh       # Run in QEMU
./kill.sh               # Kill all Docker containers
```

## Bash Tool Timeouts

| Command | Timeout |
|---------|---------|
| `./build.sh` | 120000ms (2 min) |
| `./dev.sh ./run.sh` | 10000ms (10 sec) |
| `./kill.sh` | 2000ms |

**Kernel boots in ~3 seconds.** Use 10s timeout for run tests.

**Do NOT use the `timeout` shell command** - it doesn't work in this environment. Use the Bash tool's `timeout` parameter instead.

## Workflow

1. Build: `./build.sh 2>&1` (timeout: 120000)
2. Test: `./dev.sh ./run.sh 2>&1` (timeout: 10000)
3. Cleanup: `./kill.sh` (timeout: 2000)

**ALWAYS run `./kill.sh` after tests** - QEMU keeps containers alive and they lock build files.

**Do NOT compound commands, just run and read the log file after** - more consistent with all output. Don't grep the output until after running times out.

## Documentation

- Architecture details: `docs/ARCHITECTURE.md`
- korlib roadmap: `docs/KORLIB_PLAN.md`
