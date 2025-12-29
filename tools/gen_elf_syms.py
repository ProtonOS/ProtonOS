#!/usr/bin/env python3
import sys
import re
import subprocess
import struct

pdb_file = sys.argv[1]
output_file = sys.argv[2]
IMAGE_BASE = 0x140000000

# Get section info from PDB
sections = {}
result = subprocess.run(['llvm-pdbutil', 'dump', '--section-headers', pdb_file], 
                       capture_output=True)
section_num = 0
for line in result.stdout.decode('utf-8', errors='replace').split('\n'):
    if 'SECTION HEADER #' in line:
        section_num = int(line.split('#')[1].strip())
    if 'virtual address' in line:
        va = int(line.split()[0], 16)
        sections[section_num] = va

# Get symbols
result = subprocess.run(['llvm-pdbutil', 'dump', '--modi=1', '--symbols', pdb_file],
                       capture_output=True)

symbols = []
lines = result.stdout.decode('utf-8', errors='replace').split('\n')
i = 0
while i < len(lines):
    line = lines[i]
    if 'S_GPROC32' in line:
        match = re.search(r'`([^`]+)`', line)
        if match:
            name = match.group(1)
            if i + 1 < len(lines):
                addr_line = lines[i + 1]
                addr_match = re.search(r'addr = (\d+):(\d+)', addr_line)
                size_match = re.search(r'code size = (\d+)', addr_line)
                if addr_match:
                    section = int(addr_match.group(1))
                    offset = int(addr_match.group(2))
                    size = int(size_match.group(1)) if size_match else 0
                    if section in sections:
                        addr = IMAGE_BASE + sections[section] + offset
                        symbols.append((addr, size, name))
    i += 1

# Build string table for section names
shstrtab = b'\x00.text\x00.symtab\x00.strtab\x00.shstrtab\x00'
# Offsets: .text=1, .symtab=7, .strtab=15, .shstrtab=23

# Build string table for symbols
strtab = b'\x00'
str_offsets = {}
for addr, size, name in symbols:
    str_offsets[name] = len(strtab)
    strtab += name.encode('utf-8', errors='replace') + b'\x00'

# ELF64 header
ehdr = bytearray(64)
ehdr[0:4] = b'\x7fELF'
ehdr[4] = 2  # ELFCLASS64
ehdr[5] = 1  # ELFDATA2LSB
ehdr[6] = 1  # EV_CURRENT
struct.pack_into('<H', ehdr, 16, 2)  # e_type = ET_EXEC
struct.pack_into('<H', ehdr, 18, 62)  # e_machine = EM_X86_64
struct.pack_into('<I', ehdr, 20, 1)  # e_version
struct.pack_into('<Q', ehdr, 24, IMAGE_BASE + 0x3d000)  # e_entry
struct.pack_into('<Q', ehdr, 32, 0)  # e_phoff
struct.pack_into('<Q', ehdr, 40, 64)  # e_shoff
struct.pack_into('<H', ehdr, 52, 64)  # e_ehsize
struct.pack_into('<H', ehdr, 58, 64)  # e_shentsize
struct.pack_into('<H', ehdr, 60, 5)  # e_shnum
struct.pack_into('<H', ehdr, 62, 4)  # e_shstrndx

# Section header offset calculations
shdrs_size = 5 * 64  # 5 sections
text_offset = 64 + shdrs_size
symtab_offset = text_offset  # Empty text section
strtab_offset = symtab_offset + (len(symbols) + 1) * 24
shstrtab_offset = strtab_offset + len(strtab)

# Section headers
# 0: NULL
shdr_null = bytes(64)

# 1: .text (empty but defines the address range)
shdr_text = bytearray(64)
struct.pack_into('<I', shdr_text, 0, 1)  # sh_name = ".text"
struct.pack_into('<I', shdr_text, 4, 1)  # sh_type = SHT_PROGBITS
struct.pack_into('<Q', shdr_text, 8, 6)  # sh_flags = ALLOC | EXEC
struct.pack_into('<Q', shdr_text, 16, IMAGE_BASE + 0x3d000)  # sh_addr
struct.pack_into('<Q', shdr_text, 24, text_offset)  # sh_offset
struct.pack_into('<Q', shdr_text, 32, 0)  # sh_size (no actual content)

# 2: .symtab
shdr_symtab = bytearray(64)
struct.pack_into('<I', shdr_symtab, 0, 7)  # sh_name = ".symtab"
struct.pack_into('<I', shdr_symtab, 4, 2)  # sh_type = SHT_SYMTAB
struct.pack_into('<Q', shdr_symtab, 24, symtab_offset)
struct.pack_into('<Q', shdr_symtab, 32, (len(symbols)+1)*24)
struct.pack_into('<I', shdr_symtab, 40, 3)  # sh_link = strtab
struct.pack_into('<I', shdr_symtab, 44, 1)  # sh_info = first global
struct.pack_into('<Q', shdr_symtab, 56, 24)

# 3: .strtab
shdr_strtab = bytearray(64)
struct.pack_into('<I', shdr_strtab, 0, 15)  # sh_name = ".strtab"
struct.pack_into('<I', shdr_strtab, 4, 3)  # sh_type = SHT_STRTAB
struct.pack_into('<Q', shdr_strtab, 24, strtab_offset)
struct.pack_into('<Q', shdr_strtab, 32, len(strtab))

# 4: .shstrtab
shdr_shstrtab = bytearray(64)
struct.pack_into('<I', shdr_shstrtab, 0, 23)  # sh_name = ".shstrtab"
struct.pack_into('<I', shdr_shstrtab, 4, 3)  # sh_type = SHT_STRTAB
struct.pack_into('<Q', shdr_shstrtab, 24, shstrtab_offset)
struct.pack_into('<Q', shdr_shstrtab, 32, len(shstrtab))

# Build symbol table
symtab = bytearray(24)  # Null symbol
for addr, size, name in sorted(symbols):
    sym = bytearray(24)
    struct.pack_into('<I', sym, 0, str_offsets[name])
    sym[4] = (1 << 4) | 2  # GLOBAL | FUNC
    sym[5] = 0  # st_other
    struct.pack_into('<H', sym, 6, 1)  # st_shndx = .text section
    struct.pack_into('<Q', sym, 8, addr)
    struct.pack_into('<Q', sym, 16, size)
    symtab += sym

# Write
with open(output_file, 'wb') as f:
    f.write(ehdr)
    f.write(shdr_null)
    f.write(shdr_text)
    f.write(shdr_symtab)
    f.write(shdr_strtab)
    f.write(shdr_shstrtab)
    f.write(symtab)
    f.write(strtab)
    f.write(shstrtab)

print(f"Generated {output_file} with {len(symbols)} symbols")
