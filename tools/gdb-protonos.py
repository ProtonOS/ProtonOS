#!/usr/bin/env python3
"""
ProtonOS GDB debug helper script.

This script automates loading kernel symbols at the correct address by:
1. Setting a watchpoint on the GDB debug marker (0x10000)
2. Waiting for the kernel to write the marker (0xDEADBEEF)
3. Reading the actual ImageBase from 0x10008
4. Loading symbols with the correct offset

Usage in GDB:
  source tools/gdb-protonos.py
  proton-connect        # Connect to QEMU and load symbols automatically
  proton-load-symbols   # Just load symbols (if already connected and base known)
"""

import gdb

# Constants matching the kernel's Startup.Efi.cs
GDB_DEBUG_MARKER_ADDR = 0x10000
GDB_DEBUG_IMAGEBASE_ADDR = 0x10008
GDB_DEBUG_MARKER_VALUE = 0xDEADBEEF
PE_IMAGE_BASE = 0x140000000  # Expected base in the PE header

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
        print("[ProtonOS] Loading symbols...")
        gdb.execute(f"add-symbol-file build/x64/kernel_syms.elf -o {offset}")

        print("[ProtonOS] Symbols loaded! You can now set breakpoints by function name.")
        print("[ProtonOS] Example: break kernel_ProtonOS_Kernel__Main")

class ProtonLoadSymbolsCommand(gdb.Command):
    """Load ProtonOS symbols using the ImageBase already written to memory."""

    def __init__(self):
        super().__init__("proton-load-symbols", gdb.COMMAND_USER)

    def invoke(self, arg, from_tty):
        # Read the actual image base
        try:
            actual_base = int(gdb.parse_and_eval(f"*(unsigned long long*){GDB_DEBUG_IMAGEBASE_ADDR}"))
        except gdb.error as e:
            print(f"[ProtonOS] Error reading ImageBase: {e}")
            return

        if actual_base == 0:
            print("[ProtonOS] ImageBase is 0 - kernel may not have started yet")
            return

        print(f"[ProtonOS] Kernel loaded at: {hex(actual_base)}")

        # Calculate offset
        offset = actual_base - PE_IMAGE_BASE
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

# Register commands
ProtonConnectCommand()
ProtonLoadSymbolsCommand()
ProtonInfoCommand()

print("[ProtonOS] GDB helper loaded. Commands available:")
print("  proton-connect [port]  - Connect to QEMU and load symbols automatically")
print("  proton-load-symbols    - Load symbols (if ImageBase already available)")
print("  proton-info            - Show debug addresses and current values")
