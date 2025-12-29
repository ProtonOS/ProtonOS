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

### Debug Symbol Support

The build generates debug symbols for both AOT and JIT code:

- **AOT symbols**: `build/x64/kernel_syms.elf` (generated from `BOOTX64.pdb`)
- **JIT symbols**: Registered at runtime via GDB JIT interface

Symbol format:
- AOT: `kernel_Namespace_Type__MethodName` (e.g., `kernel_ProtonOS_Kernel__Main`)
- JIT: `jit_Namespace_Type__MethodName` (e.g., `jit_FullTest_Tests__TestMethod`)

### Quick Start: GDB with Automatic Symbol Loading

```bash
# 1. Kill any existing sessions
tmux kill-session -t qemu 2>/dev/null || true
tmux kill-session -t gdb 2>/dev/null || true
./kill.sh

# 2. Create tmux sessions
tmux new-session -d -s qemu -c /home/shane/protonos "bash"
tmux new-session -d -s gdb -c /home/shane/protonos "bash"

# 3. Start QEMU in debug mode (paused at startup)
tmux send-keys -t qemu "qemu-system-x86_64 -machine q35 -cpu max -smp 4,sockets=2,cores=2,threads=1 -m 512M -object memory-backend-ram,id=mem0,size=256M -object memory-backend-ram,id=mem1,size=256M -numa node,nodeid=0,cpus=0-1,memdev=mem0 -numa node,nodeid=1,cpus=2-3,memdev=mem1 -numa dist,src=0,dst=1,val=20 -drive if=pflash,format=raw,readonly=on,file=/usr/share/OVMF/OVMF_CODE_4M.fd -drive format=raw,file=build/x64/boot.img -drive id=virtio-disk0,if=none,format=raw,file=build/x64/test.img -device virtio-blk-pci,drive=virtio-disk0,disable-legacy=on -serial mon:stdio -display none -no-reboot -no-shutdown -s -S" Enter

# 4. Start GDB with ProtonOS helper script
tmux send-keys -t gdb "gdb -x tools/gdb-protonos.py" Enter
sleep 1

# 5. Connect and auto-load symbols (waits for kernel to write ImageBase)
tmux send-keys -t gdb "proton-connect" Enter

# 6. After symbols load, set breakpoints by name
tmux send-keys -t gdb "break kernel_ProtonOS_Kernel__Main" Enter
tmux send-keys -t gdb "continue" Enter
```

### ProtonOS GDB Helper Commands

The `tools/gdb-protonos.py` script provides these commands:

| Command | Description |
|---------|-------------|
| `proton-connect [port]` | Connect to QEMU and auto-load symbols (default port 1234) |
| `proton-load-symbols` | Load symbols if already connected and kernel is running |
| `proton-jit-enable` | Enable breakpoints on JIT method registration |
| `proton-jit-list` | List registered JIT method symbols |
| `proton-info` | Show debug marker addresses and current values |

### Common Debugging Commands

```bash
# Set breakpoint by symbol name (after proton-connect)
tmux send-keys -t gdb "break kernel_ProtonOS_Kernel__Main" Enter

# Set breakpoint at address
tmux send-keys -t gdb "b *0x00000000001234" Enter

# Continue execution
tmux send-keys -t gdb "continue" Enter

# Step one instruction
tmux send-keys -t gdb "si" Enter

# Step one line (requires debug info)
tmux send-keys -t gdb "n" Enter

# Print registers
tmux send-keys -t gdb "info registers" Enter

# Examine memory (16 bytes at address)
tmux send-keys -t gdb "x/16xb 0xADDRESS" Enter

# Disassemble at address
tmux send-keys -t gdb "x/10i 0xADDRESS" Enter

# Disassemble function
tmux send-keys -t gdb "disas kernel_ProtonOS_Kernel__Main" Enter

# List all kernel symbols matching pattern
tmux send-keys -t gdb "info functions kernel_" Enter

# Backtrace
tmux send-keys -t gdb "bt" Enter
```

### JIT Debugging

To debug JIT-compiled code:

```bash
# After proton-connect, enable JIT debugging
tmux send-keys -t gdb "proton-jit-enable" Enter
tmux send-keys -t gdb "continue" Enter

# When GDB stops at JIT registration, list JIT symbols
tmux send-keys -t gdb "proton-jit-list" Enter

# Set breakpoint on a JIT method
tmux send-keys -t gdb "break jit_FullTest_Tests__SomeTest" Enter
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

### Debug Symbol Files

| File | Description |
|------|-------------|
| `build/x64/BOOTX64.pdb` | PDB debug symbols (Windows format, ~1.5MB) |
| `build/x64/kernel_syms.elf` | GDB-compatible ELF symbol table (~111KB) |
| `tools/gdb-protonos.py` | GDB Python helper script |
| `tools/gen_elf_syms.py` | PDB to ELF symbol converter |

## Documentation

- Architecture details: `docs/ARCHITECTURE.md`
- korlib roadmap: `docs/KORLIB_PLAN.md`
