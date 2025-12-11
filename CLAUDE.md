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

## Interactive Debugging with GDB

When crashes occur or behavior is unclear, **use GDB to debug instead of theorizing**. This requires two tmux sessions running in parallel.

### Setup Debug Sessions

```bash
# Kill any existing sessions and QEMU
tmux kill-session -t qemu 2>/dev/null || true
tmux kill-session -t gdb 2>/dev/null || true
./kill.sh

# Create named tmux sessions
tmux new-session -d -s qemu -c /home/shane/protonos "bash"
tmux new-session -d -s gdb -c /home/shane/protonos "bash"
```

### Start QEMU in Debug Mode

The `-s -S` flags enable GDB stub (port 1234) and pause at startup:

```bash
tmux send-keys -t qemu "qemu-system-x86_64 -machine q35 -cpu max -smp 4,sockets=2,cores=2,threads=1 -m 512M -object memory-backend-ram,id=mem0,size=256M -object memory-backend-ram,id=mem1,size=256M -numa node,nodeid=0,cpus=0-1,memdev=mem0 -numa node,nodeid=1,cpus=2-3,memdev=mem1 -numa dist,src=0,dst=1,val=20 -drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd -drive format=raw,file=build/x64/boot.img -drive id=virtio-disk0,if=none,format=raw,file=build/x64/test.img -device virtio-blk-pci,drive=virtio-disk0,disable-legacy=on -serial mon:stdio -display none -no-reboot -no-shutdown -s -S" Enter
```

### Connect GDB

```bash
tmux send-keys -t gdb "gdb" Enter
sleep 1
tmux send-keys -t gdb "target remote :1234" Enter
```

### Common GDB Commands

```bash
# Continue execution
tmux send-keys -t gdb "c" Enter

# Set breakpoint at address
tmux send-keys -t gdb "b *0x00000000001234" Enter

# Step one instruction
tmux send-keys -t gdb "si" Enter

# Print registers
tmux send-keys -t gdb "info registers" Enter

# Examine memory (16 bytes at address)
tmux send-keys -t gdb "x/16xb 0xADDRESS" Enter

# Disassemble at address
tmux send-keys -t gdb "x/10i 0xADDRESS" Enter
```

### Capture Output

```bash
# Check QEMU serial output
tmux capture-pane -t qemu -p | tail -30

# Check GDB output
tmux capture-pane -t gdb -p | tail -20
```

### Cleanup

```bash
tmux kill-session -t qemu
tmux kill-session -t gdb
./kill.sh
```

## Documentation

- Architecture details: `docs/ARCHITECTURE.md`
- korlib roadmap: `docs/KORLIB_PLAN.md`
