#!/usr/bin/env python3
"""
ProtonOS GDB debug helper script.

This script automates loading kernel symbols at the correct address by:
1. Setting a watchpoint on the GDB debug marker (0x10000)
2. Waiting for the kernel to write the marker (0xDEADBEEF)
3. Reading the actual ImageBase from 0x10008
4. Loading symbols with the correct offset
5. Enabling JIT symbol registration (manual handling to work around GDB bug)

Usage in GDB:
  source tools/gdb-protonos.py
  proton-connect        # Connect to QEMU and load symbols automatically
  proton-load-symbols   # Just load symbols (if already connected and base known)
  proton-jit-enable     # Enable JIT symbol debugging (after symbols loaded)
"""

import gdb
import tempfile
import os
import struct

# Constants matching the kernel's Startup.Efi.cs
GDB_DEBUG_MARKER_ADDR = 0x10000
GDB_DEBUG_IMAGEBASE_ADDR = 0x10008
GDB_DEBUG_MARKER_VALUE = 0xDEADBEEF
PE_IMAGE_BASE = 0x140000000  # Expected base in the PE header

# Global state for symbol offset and JIT tracking
_symbol_offset = 0
_jit_symbols = {}  # elf_addr -> info dict
_temp_dir = None

def get_temp_dir():
    """Get or create temp directory for JIT ELF files."""
    global _temp_dir
    if _temp_dir is None:
        _temp_dir = tempfile.mkdtemp(prefix='protonos_jit_')
    return _temp_dir

def read_memory(addr, size):
    """Read memory from target as bytes."""
    inferior = gdb.selected_inferior()
    return bytes(inferior.read_memory(addr, size))

def find_kernel_base_by_mz_scan(verbose=False, retries=5):
    """Find the kernel base by scanning for MZ header around the current PC.

    This is a fallback when the low-memory ImageBase variable is not accessible.
    Returns the kernel base address or None if not found.
    """
    import time

    # Ensure the target is stopped and GDB connection is stable

    # First try to interrupt the target in case it's still running
    try:
        gdb.execute("interrupt", to_string=True)
    except:
        pass

    # Give QEMU's GDB stub time to fully initialize
    time.sleep(3.0)

    # Wait for memory access to become available
    # This can take a few seconds after connecting to a running QEMU
    inferior = gdb.selected_inferior()
    pc = None
    memory_ready = False
    for warmup in range(10):
        try:
            pc = int(gdb.parse_and_eval("$rip"))
            # Try to actually read memory at PC
            _ = bytes(inferior.read_memory(pc, 4))
            print(f"[ProtonOS] Memory access ready after {warmup+1} warmup attempts (PC={hex(pc)})")
            memory_ready = True
            break
        except Exception as e:
            if warmup == 0:
                print(f"[ProtonOS] Waiting for memory access... ({e})")
            time.sleep(0.5)

    if not memory_ready:
        print("[ProtonOS] WARNING: Memory access not ready, trying recovery methods...")

        # Try multiple recovery methods
        for recovery_attempt in range(3):
            # Method 1: Try continue + interrupt to wake up memory access
            try:
                print(f"[ProtonOS] Recovery attempt {recovery_attempt+1}: continue+interrupt...")
                gdb.execute("continue &", to_string=True)
                time.sleep(0.5)
                gdb.execute("interrupt", to_string=True)
                time.sleep(1)

                # Test memory access
                for i in range(5):
                    try:
                        pc = int(gdb.parse_and_eval("$rip"))
                        _ = bytes(inferior.read_memory(pc, 4))
                        print(f"[ProtonOS] Memory access recovered (PC={hex(pc)})")
                        memory_ready = True
                        break
                    except:
                        time.sleep(0.3)

                if memory_ready:
                    break
            except Exception as e:
                pass

            # Method 2: Disconnect and reconnect
            if not memory_ready:
                try:
                    print(f"[ProtonOS] Recovery attempt {recovery_attempt+1}: reconnect...")
                    gdb.execute("disconnect", to_string=True)
                    time.sleep(1)
                    gdb.execute("target remote :1234", to_string=True)
                    time.sleep(1)
                    gdb.execute("interrupt", to_string=True)
                    time.sleep(1)

                    for i in range(5):
                        try:
                            pc = int(gdb.parse_and_eval("$rip"))
                            inferior = gdb.selected_inferior()
                            _ = bytes(inferior.read_memory(pc, 4))
                            print(f"[ProtonOS] Memory access recovered after reconnect (PC={hex(pc)})")
                            memory_ready = True
                            break
                        except:
                            time.sleep(0.3)

                    if memory_ready:
                        break
                except Exception as e:
                    print(f"[ProtonOS] Reconnect error: {e}")

    if not memory_ready:
        print("[ProtonOS] ERROR: Memory access unavailable after all recovery attempts")

    for attempt in range(retries):
        # Delay before each attempt - increases with retries
        delay = 1.0 + (attempt * 1.0)
        time.sleep(delay)

        try:
            pc = int(gdb.parse_and_eval("$rip"))
        except Exception as e:
            if verbose:
                print(f"[ProtonOS] Attempt {attempt+1}: Could not read PC: {e}")
            continue

        if verbose:
            print(f"[ProtonOS] Attempt {attempt+1}: Scanning around PC={hex(pc)}")

        # First verify we can read memory at PC - if not, memory access is broken
        inferior = gdb.selected_inferior()
        try:
            _ = bytes(inferior.read_memory(pc, 4))
        except:
            if verbose:
                print(f"[ProtonOS] Attempt {attempt+1}: Can't read memory at PC, retrying...")
            continue

        # Round PC down to various boundaries
        pc_64k = (pc >> 16) << 16
        pc_4k = pc & ~0xFFF

        # Check the most likely kernel bases first - very close to PC
        priority_bases = [
            pc_4k,                    # Exactly page-aligned
            pc_4k - 0x1000,           # One page below
            pc_4k - 0x2000,           # Two pages below
            pc_64k,                   # 64KB aligned
            pc_64k - 0x10000,         # One 64KB segment below
        ]

        inferior = gdb.selected_inferior()
        for pbase in priority_bases:
            if pbase <= 0:
                continue
            try:
                data = bytes(inferior.read_memory(pbase, 2))
                if verbose:
                    print(f"[ProtonOS] Priority check {hex(pbase)}: {data.hex()}")
                if data == b'MZ':
                    print(f"[ProtonOS] Found MZ at priority address {hex(pbase)}")
                    return pbase
            except:
                pass

        # Search a wide range: ±4MB in 64KB steps
        # Also check 4KB aligned addresses near the PC
        potential_bases = []

        # First try likely PE alignments: 64KB, 4KB, and 2KB aligned
        for offset in range(-0x400000, 0x400000, 0x10000):  # ±4MB in 64KB steps
            potential_bases.append(pc_64k + offset)

        # 4KB aligned addresses near PC
        for offset in range(-0x100000, 0x100000, 0x1000):  # ±1MB in 4KB steps
            addr = (pc & ~0xFFF) + offset
            if addr not in potential_bases:
                potential_bases.append(addr)

        # Also check 2KB aligned addresses close to PC (UEFI can use unusual alignment)
        for offset in range(-0x40000, 0x40000, 0x800):  # ±256KB in 2KB steps
            addr = (pc & ~0x7FF) + offset
            if addr not in potential_bases:
                potential_bases.append(addr)

        # Remove duplicates and sort by distance from PC
        potential_bases = list(set(potential_bases))
        potential_bases.sort(key=lambda x: abs(x - pc))

        inferior = gdb.selected_inferior()
        checked = 0
        errors = 0

        for base in potential_bases:
            if base <= 0:
                continue
            try:
                data = bytes(inferior.read_memory(base, 2))
                checked += 1
                if data == b'MZ':
                    if verbose:
                        print(f"[ProtonOS] Found MZ at {hex(base)} after checking {checked} addresses")
                    return base
            except gdb.MemoryError:
                errors += 1
            except Exception as e:
                errors += 1

        if verbose:
            print(f"[ProtonOS] Attempt {attempt+1}: Checked {checked} addresses, {errors} errors")

        # If too many errors relative to checked, try again after delay
        if errors > checked * 2 and attempt < retries - 1:
            if verbose:
                print(f"[ProtonOS] High error rate, retrying...")
            continue

        # If we checked addresses but didn't find it, don't retry
        if checked > 100:
            break

    if verbose:
        print(f"[ProtonOS] No MZ header found after {retries} attempts")
    return None

def parse_elf_symbol(elf_data):
    """Parse a minimal ELF to extract the function symbol name and address."""
    # Check ELF magic
    if elf_data[:4] != b'\x7fELF':
        return None, None

    # ELF64 header: e_shoff at offset 40 (8 bytes)
    e_shoff = struct.unpack('<Q', elf_data[40:48])[0]
    e_shnum = struct.unpack('<H', elf_data[60:62])[0]
    e_shstrndx = struct.unpack('<H', elf_data[62:64])[0]

    # Find .symtab and .strtab sections
    symtab_offset = None
    symtab_size = None
    strtab_offset = None

    for i in range(e_shnum):
        shdr_off = e_shoff + i * 64
        sh_type = struct.unpack('<I', elf_data[shdr_off+4:shdr_off+8])[0]
        sh_offset = struct.unpack('<Q', elf_data[shdr_off+24:shdr_off+32])[0]
        sh_size = struct.unpack('<Q', elf_data[shdr_off+32:shdr_off+40])[0]

        if sh_type == 2:  # SHT_SYMTAB
            symtab_offset = sh_offset
            symtab_size = sh_size
        elif sh_type == 3 and strtab_offset is None:  # SHT_STRTAB (first one is usually strtab)
            strtab_offset = sh_offset

    if symtab_offset is None or strtab_offset is None:
        return None, None

    # Parse symbol table (skip null symbol at index 0)
    num_symbols = symtab_size // 24
    for i in range(1, num_symbols):
        sym_off = symtab_offset + i * 24
        st_name = struct.unpack('<I', elf_data[sym_off:sym_off+4])[0]
        st_info = elf_data[sym_off+4]
        st_value = struct.unpack('<Q', elf_data[sym_off+8:sym_off+16])[0]

        # Get symbol type (lower 4 bits)
        sym_type = st_info & 0xf
        if sym_type == 2:  # STT_FUNC
            # Read null-terminated string from strtab
            name_bytes = []
            idx = strtab_offset + st_name
            while idx < len(elf_data) and elf_data[idx] != 0:
                name_bytes.append(elf_data[idx])
                idx += 1
            name = bytes(name_bytes).decode('utf-8', errors='replace')
            return name, st_value

    return None, None

def _scan_jit_list(verbose=False):
    """Walk the JIT linked list and collect all registered methods.

    This is called on-demand when the user wants to see JIT methods,
    rather than using a breakpoint during execution (which is slow).

    Note: Some memory regions (especially low addresses <1MB) may not be
    accessible via GDB. The scan will collect what it can and report
    how many entries were inaccessible.
    """
    global _jit_symbols
    _jit_symbols = {}  # Clear and rescan
    errors = 0
    stop_reason = None
    inaccessible_count = 0
    total_count = 0

    try:
        # Get first_entry from descriptor
        desc_addr = int(gdb.parse_and_eval("(unsigned long long)&__proton_jit_descriptor"))
        if verbose:
            print(f"[ProtonOS] Descriptor at {hex(desc_addr)}")

        # Read all 24 bytes of descriptor at once
        desc_data = read_memory(desc_addr, 24)
        version = struct.unpack('<I', desc_data[0:4])[0]
        action = struct.unpack('<I', desc_data[4:8])[0]
        relevant_entry = struct.unpack('<Q', desc_data[8:16])[0]
        first_entry = struct.unpack('<Q', desc_data[16:24])[0]

        if verbose:
            print(f"[ProtonOS] version={version} action={action}")
            print(f"[ProtonOS] relevant_entry={hex(relevant_entry)}")
            print(f"[ProtonOS] first_entry={hex(first_entry)}")

        if first_entry == 0:
            stop_reason = "first_entry is NULL"
            return 0

        # Walk the linked list
        entry = first_entry
        visited = set()  # Prevent infinite loops

        while entry != 0:
            if entry in visited:
                stop_reason = f"cycle detected at {hex(entry)}"
                break
            visited.add(entry)
            total_count += 1

            # Sanity check - addresses should be in kernel range
            if entry < 0x10000:
                stop_reason = f"invalid entry pointer {hex(entry)} (too low)"
                break
            if entry > 0xFFFFFFFFFFFF:
                stop_reason = f"invalid entry pointer {hex(entry)} (too high)"
                break

            try:
                # Read jit_code_entry: next(8) + prev(8) + symfile_addr(8) + symfile_size(8)
                entry_data = read_memory(entry, 32)
                next_entry = struct.unpack('<Q', entry_data[0:8])[0]
                prev_entry = struct.unpack('<Q', entry_data[8:16])[0]
                symfile_addr = struct.unpack('<Q', entry_data[16:24])[0]
                symfile_size = struct.unpack('<Q', entry_data[24:32])[0]

                if verbose and total_count <= 5:
                    print(f"[ProtonOS] Entry {total_count}: addr={hex(entry)} next={hex(next_entry)} elf={hex(symfile_addr)} size={symfile_size}")

                if symfile_addr != 0 and symfile_size != 0 and symfile_size < 0x100000:
                    _jit_symbols[symfile_addr] = {
                        'elf_addr': symfile_addr,
                        'elf_size': symfile_size,
                        'loaded': False,
                        'name': None,
                        'code_addr': None
                    }

                entry = next_entry

            except gdb.MemoryError:
                # Memory at this address is not accessible (common for low EFI memory)
                # Count how many entries we can't access but continue
                inaccessible_count += 1
                if inaccessible_count == 1 and verbose:
                    print(f"[ProtonOS] First inaccessible entry at {hex(entry)} (low memory not exposed to GDB)")
                # We can't continue traversing - we don't know next_entry
                stop_reason = f"hit inaccessible memory at {hex(entry)} (remaining entries in low memory)"
                break

            except Exception as e:
                stop_reason = f"error at entry {hex(entry)}: {e}"
                errors += 1
                break

        if entry == 0 and stop_reason is None:
            stop_reason = "reached end of list"

    except Exception as e:
        stop_reason = f"initialization error: {e}"

    if verbose:
        print(f"[ProtonOS] Scan complete: {stop_reason}")
        if inaccessible_count > 0:
            print(f"[ProtonOS] Note: {inaccessible_count}+ entries in inaccessible low memory")

    return len(_jit_symbols)

class ProtonConnectCommand(gdb.Command):
    """Connect to QEMU and automatically load ProtonOS kernel symbols."""

    def __init__(self):
        super().__init__("proton-connect", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        port = arg.strip() if arg.strip() else "1234"

        print(f"[ProtonOS] Connecting to QEMU on localhost:{port}...")
        gdb.execute(f"target remote localhost:{port}")

        print("[ProtonOS] Setting watchpoint on debug marker (0x10000)...")
        gdb.execute(f"watch *(unsigned long long*){GDB_DEBUG_MARKER_ADDR}")

        print("[ProtonOS] Continuing until kernel writes debug marker...")
        gdb.execute("continue")

        # Check if we hit the watchpoint
        marker = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_MARKER_ADDR}"))
        if marker != GDB_DEBUG_MARKER_VALUE:
            print(f"[ProtonOS] Warning: Marker value is {hex(marker)}, expected {hex(GDB_DEBUG_MARKER_VALUE)}")
            return

        # Read the actual image base
        actual_base = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_IMAGEBASE_ADDR}"))
        print(f"[ProtonOS] Kernel loaded at: {hex(actual_base)}")

        # Calculate offset
        offset = actual_base - PE_IMAGE_BASE
        print(f"[ProtonOS] Symbol offset: {hex(offset)}")

        # Delete the watchpoint (it served its purpose)
        gdb.execute("delete")

        # Load symbols with offset
        print("[ProtonOS] Loading AOT symbols...")
        gdb.execute(f"add-symbol-file build/x64/kernel_syms.elf -o {offset}")

        # Save offset for JIT symbol support
        global _symbol_offset
        _symbol_offset = offset

        print("[ProtonOS] AOT symbols loaded! You can now set breakpoints by function name.")
        print("[ProtonOS] Example: break kernel_ProtonOS_Kernel__Main")
        print("[ProtonOS] ")
        print("[ProtonOS] JIT: After tests run, use 'proton-jit-scan' to find JIT methods.")
        print("[ProtonOS] JIT methods will have names like: jit_FullTest_Tests__TestMethod")

class ProtonLoadSymbolsCommand(gdb.Command):
    """Load ProtonOS symbols using the ImageBase already written to memory."""

    def __init__(self):
        super().__init__("proton-load-symbols", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _symbol_offset

        actual_base = None

        # First try reading from low-memory ImageBase variable
        try:
            actual_base = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_IMAGEBASE_ADDR}"))
            if actual_base != 0:
                print(f"[ProtonOS] Found ImageBase in low memory: {hex(actual_base)}")
        except gdb.error:
            pass

        # If that failed, scan for MZ header
        if actual_base is None or actual_base == 0:
            print("[ProtonOS] Low memory not accessible, scanning for MZ header...")
            actual_base = find_kernel_base_by_mz_scan(verbose=True)
            if actual_base:
                print(f"[ProtonOS] Found kernel at {hex(actual_base)} via MZ scan")
            else:
                print("[ProtonOS] Could not find kernel base. Target may not be running.")
                return

        print(f"[ProtonOS] Kernel loaded at: {hex(actual_base)}")

        # Calculate offset
        offset = actual_base - PE_IMAGE_BASE
        _symbol_offset = offset
        print(f"[ProtonOS] Symbol offset: {hex(offset)}")

        # Load symbols with offset
        gdb.execute(f"add-symbol-file build/x64/kernel_syms.elf -o {offset}")
        print("[ProtonOS] Symbols loaded!")

class ProtonInfoCommand(gdb.Command):
    """Show ProtonOS debug info addresses."""

    def __init__(self):
        super().__init__("proton-info", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        print(f"GDB Debug Marker Address:    {hex(GDB_DEBUG_MARKER_ADDR)}")
        print(f"GDB Debug ImageBase Address: {hex(GDB_DEBUG_IMAGEBASE_ADDR)}")
        print(f"Expected Marker Value:       {hex(GDB_DEBUG_MARKER_VALUE)}")
        print(f"PE Image Base (in binary):   {hex(PE_IMAGE_BASE)}")

        try:
            marker = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_MARKER_ADDR}"))
            base = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_IMAGEBASE_ADDR}"))
            print(f"\nCurrent Marker Value:        {hex(marker)}")
            print(f"Current ImageBase:           {hex(base)}")
            if base != 0:
                print(f"Required Offset:             {hex(base - PE_IMAGE_BASE)}")
        except gdb.error:
            print("\n(Not connected to target)")

class ProtonJitScanCommand(gdb.Command):
    """Scan and list all JIT-compiled methods (requires paused target).

    Usage: proton-jit-scan [-v]
      -v  Verbose mode: show diagnostic details

    Note: The target should be paused before scanning. If not, try:
      - Use proton-jit-pause to pause the target first
      - Or set a breakpoint before running
    """

    def __init__(self):
        super().__init__("proton-jit-scan", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _symbol_offset

        if _symbol_offset == 0:
            print("[ProtonOS] Error: Symbol offset not set. Run proton-connect first.")
            return

        verbose = "-v" in arg if arg else False
        print("[ProtonOS] Scanning JIT method list...")
        count = _scan_jit_list(verbose=verbose)
        print(f"[ProtonOS] Found {count} JIT-compiled methods.")
        if count > 0:
            print("[ProtonOS] Use 'proton-jit-search <pattern>' to find specific methods.")
            print("[ProtonOS] Use 'proton-jit-load [pattern]' to load symbols into GDB.")

class ProtonJitPauseCommand(gdb.Command):
    """Pause the target using QEMU monitor stop command.

    This avoids SIGQUIT issues that can occur with Ctrl-C.
    """

    def __init__(self):
        super().__init__("proton-jit-pause", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        print("[ProtonOS] Sending stop command via QEMU monitor...")
        try:
            # Use GDB's monitor command to send QEMU command
            gdb.execute("monitor stop", to_string=True)
            print("[ProtonOS] Target paused via QEMU monitor.")
        except gdb.error as e:
            print(f"[ProtonOS] Error: {e}")
            print("[ProtonOS] Try: Ctrl-C or set a breakpoint instead.")

def _resolve_jit_symbol(info):
    """Resolve a JIT symbol's name and code address by reading its ELF data."""
    if info['name'] is not None:
        return True  # Already resolved

    try:
        elf_data = read_memory(info['elf_addr'], info['elf_size'])
        name, code_addr = parse_elf_symbol(elf_data)
        if name and code_addr:
            info['name'] = name
            info['code_addr'] = code_addr
            return True
    except:
        pass
    return False

class ProtonJitListCommand(gdb.Command):
    """List registered JIT symbols."""

    def __init__(self):
        super().__init__("proton-jit-list", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _jit_symbols

        if not _jit_symbols:
            print("[ProtonOS] No JIT symbols registered yet.")
            print("[ProtonOS] Run 'proton-jit-enable' and continue execution to compile JIT methods.")
            return

        print(f"[ProtonOS] {len(_jit_symbols)} JIT methods captured.")
        print("[ProtonOS] Use 'proton-jit-load' to load symbols (pauses target).")
        print("[ProtonOS] Use 'proton-jit-search <pattern>' to find methods.")

class ProtonJitSearchCommand(gdb.Command):
    """Search for JIT methods by pattern. Resolves names on-demand."""

    def __init__(self):
        super().__init__("proton-jit-search", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _jit_symbols

        if not _jit_symbols:
            print("[ProtonOS] No JIT symbols registered yet.")
            return

        pattern = arg.strip().lower() if arg else ""
        if not pattern:
            print("[ProtonOS] Usage: proton-jit-search <pattern>")
            print("[ProtonOS] Example: proton-jit-search ToString")
            return

        print(f"[ProtonOS] Searching {len(_jit_symbols)} methods for '{pattern}'...")
        matches = []

        for elf_addr, info in _jit_symbols.items():
            # Resolve name if not already done
            if not _resolve_jit_symbol(info):
                continue

            if pattern in info['name'].lower():
                matches.append((info['code_addr'], info['name'], elf_addr))

        if not matches:
            print("[ProtonOS] No matches found.")
            return

        print(f"[ProtonOS] Found {len(matches)} matches:")
        for code_addr, name, elf_addr in sorted(matches):
            print(f"  {hex(code_addr)}: {name}")

class ProtonJitLoadCommand(gdb.Command):
    """Load JIT symbols into GDB (requires paused target)."""

    def __init__(self):
        super().__init__("proton-jit-load", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _jit_symbols

        if not _jit_symbols:
            print("[ProtonOS] No JIT symbols to load.")
            return

        pattern = arg.strip().lower() if arg else ""

        print(f"[ProtonOS] Loading JIT symbols{' matching: ' + pattern if pattern else ''}...")
        loaded = 0
        errors = 0

        temp_dir = get_temp_dir()

        for elf_addr, info in _jit_symbols.items():
            if info['loaded']:
                continue

            # Resolve name first
            if not _resolve_jit_symbol(info):
                errors += 1
                continue

            # Filter by pattern if provided
            if pattern and pattern not in info['name'].lower():
                continue

            try:
                # Read ELF and save to temp file
                elf_data = read_memory(info['elf_addr'], info['elf_size'])
                elf_path = os.path.join(temp_dir, f"jit_{info['code_addr']:x}.elf")
                with open(elf_path, 'wb') as f:
                    f.write(elf_data)

                # Load symbol file
                gdb.execute(f"add-symbol-file {elf_path}", to_string=True)
                info['loaded'] = True
                loaded += 1
            except Exception as e:
                errors += 1

        print(f"[ProtonOS] Loaded {loaded} symbols ({errors} errors).")

class ProtonJitClearCommand(gdb.Command):
    """Clear JIT symbol tracking and temp files."""

    def __init__(self):
        super().__init__("proton-jit-clear", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        global _jit_symbols, _temp_dir

        # Clear symbols
        count = len(_jit_symbols)
        _jit_symbols = {}

        # Clean up temp directory
        if _temp_dir and os.path.exists(_temp_dir):
            import shutil
            shutil.rmtree(_temp_dir, ignore_errors=True)
            _temp_dir = None

        print(f"[ProtonOS] Cleared {count} JIT symbols and temp files.")

# Register commands
ProtonConnectCommand()
ProtonLoadSymbolsCommand()
ProtonInfoCommand()
ProtonJitScanCommand()
ProtonJitPauseCommand()
ProtonJitListCommand()
ProtonJitSearchCommand()
ProtonJitLoadCommand()
ProtonJitClearCommand()

print("[ProtonOS] GDB helper loaded. Commands available:")
print("  proton-connect [port]  - Connect to QEMU and load symbols automatically")
print("  proton-load-symbols    - Load symbols (if ImageBase already available)")
print("  proton-jit-scan        - Scan for JIT methods (pauses target, no overhead)")
print("  proton-jit-list        - Show count of scanned JIT methods")
print("  proton-jit-search <p>  - Search JIT methods by pattern")
print("  proton-jit-load [pat]  - Load JIT symbols into GDB")
print("  proton-jit-clear       - Clear JIT symbols and temp files")
print("  proton-info            - Show debug addresses and current values")
