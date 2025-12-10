# X64 Code Emission Patterns - ProtonOS JIT

This document provides the complete x64 instruction encoding reference used by the ProtonOS Tier 0 JIT compiler.

## Register Encoding

### 64-bit Registers

| Register | Code | REX.B/R/X | Notes |
|----------|------|-----------|-------|
| RAX | 0 | 0 | Accumulator, return value |
| RCX | 1 | 0 | Arg 0, shift count |
| RDX | 2 | 0 | Arg 1, high return (128-bit) |
| RBX | 3 | 0 | Callee-saved |
| RSP | 4 | 0 | Stack pointer |
| RBP | 5 | 0 | Frame pointer, callee-saved |
| RSI | 6 | 0 | String src (SysV: callee-saved, Win: caller-saved) |
| RDI | 7 | 0 | String dst (SysV: callee-saved, Win: caller-saved) |
| R8 | 0 | 1 | Arg 2 |
| R9 | 1 | 1 | Arg 3 |
| R10 | 2 | 1 | Caller-saved |
| R11 | 3 | 1 | Caller-saved |
| R12 | 4 | 1 | Callee-saved |
| R13 | 5 | 1 | Callee-saved |
| R14 | 6 | 1 | Callee-saved |
| R15 | 7 | 1 | Callee-saved |

### JIT Register Aliases (VReg enum)

```csharp
public enum VReg : byte
{
    R0 = 0,   // RAX
    R1 = 1,   // RCX
    R2 = 2,   // RDX
    R3 = 3,   // RBX
    SP = 4,   // RSP
    BP = 5,   // RBP
    R6 = 6,   // RSI
    R7 = 7,   // RDI
    R8 = 8,
    R9 = 9,
    R10 = 10,
    R11 = 11,
    R12 = 12,
    R13 = 13,
    R14 = 14,
    R15 = 15,
}
```

### XMM Registers

| Register | Code | Notes |
|----------|------|-------|
| XMM0 | 0 | Arg 0 (float), return value |
| XMM1 | 1 | Arg 1 (float) |
| XMM2 | 2 | Arg 2 (float) |
| XMM3 | 3 | Arg 3 (float) |
| XMM4-5 | 4-5 | Caller-saved |
| XMM6-15 | 6-15 | Callee-saved (Windows only; SysV: all XMM caller-saved) |

---

## REX Prefix

The REX prefix (0x40-0x4F) extends register addressing for 64-bit mode.

```
REX = 0100 WRXB
      0100 = fixed prefix
      W = 1 for 64-bit operand size
      R = extension of ModR/M.reg
      X = extension of SIB.index
      B = extension of ModR/M.rm or SIB.base
```

| REX | Binary | Hex | Use |
|-----|--------|-----|-----|
| REX.W | 0100 1000 | 48 | 64-bit operand |
| REX.R | 0100 0100 | 44 | Reg is R8-R15 |
| REX.X | 0100 0010 | 42 | SIB index is R8-R15 |
| REX.B | 0100 0001 | 41 | R/M is R8-R15 |

**Common Combinations**:
| Pattern | Hex | Meaning |
|---------|-----|---------|
| REX.W | 48 | 64-bit operation |
| REX.WB | 49 | 64-bit, R/M is R8-R15 |
| REX.WR | 4C | 64-bit, Reg is R8-R15 |
| REX.WRB | 4D | 64-bit, both extended |

---

## ModR/M Byte

```
ModR/M = MM RRR MMM
         MM  = Mod (addressing mode)
         RRR = Reg (register operand or opcode extension)
         MMM = R/M (register or memory operand)
```

| Mod | Meaning |
|-----|---------|
| 00 | [R/M] (no displacement, except [RBP]=disp32, [RSP]=SIB) |
| 01 | [R/M + disp8] |
| 10 | [R/M + disp32] |
| 11 | Register direct (R/M is register) |

**Special Cases with Mod=00**:
- R/M=100 (RSP): Uses SIB byte
- R/M=101 (RBP): Uses [RIP+disp32] (not [RBP])

To encode `[RBP]`, use Mod=01 with disp8=0.

---

## SIB Byte

Used when Mod!=11 and R/M=100.

```
SIB = SS III BBB
      SS  = Scale (00=1, 01=2, 10=4, 11=8)
      III = Index register
      BBB = Base register
```

**Common SIB Patterns**:
| Pattern | SIB | Meaning |
|---------|-----|---------|
| [RSP] | 24 | Base=RSP, Index=none, Scale=1 |
| [RSP+disp8] | 24 | Same, use with Mod=01 |
| [RAX+RCX*4] | 88 | Base=RAX, Index=RCX, Scale=4 |

---

## Common Instruction Encodings

### MOV - Register to Register

```
mov r64, r64    ; REX.W + 89 /r    (store form)
                ; REX.W + 8B /r    (load form)
```

| Instruction | Hex | Notes |
|-------------|-----|-------|
| mov rax, rcx | 48 89 C8 | REX.W, MOV r/m64,r64, ModRM=11 001 000 |
| mov rcx, rax | 48 89 C1 | REX.W, MOV r/m64,r64, ModRM=11 000 001 |
| mov rax, r8 | 4C 89 C0 | REX.WR (R8 is reg), ModRM=11 000 000 |
| mov r8, rax | 49 89 C0 | REX.WB (R8 is r/m), ModRM=11 000 000 |

### MOV - Immediate to Register

```
mov r32, imm32  ; B8+rd id          (5 bytes)
mov r64, imm64  ; REX.W B8+rd io    (10 bytes)
mov r64, imm32  ; REX.W C7 /0 id    (7 bytes, sign-extended)
```

| Instruction | Hex |
|-------------|-----|
| mov eax, 0x12345678 | B8 78 56 34 12 |
| mov ecx, 0x12345678 | B9 78 56 34 12 |
| mov rax, imm64 | 48 B8 <8 bytes> |
| mov r8, imm64 | 49 B8 <8 bytes> |

### MOV - Memory Operations

```
mov r64, [r64]          ; REX.W 8B /r
mov [r64], r64          ; REX.W 89 /r
mov r64, [r64+disp8]    ; REX.W 8B /r (Mod=01)
mov r64, [r64+disp32]   ; REX.W 8B /r (Mod=10)
```

| Instruction | Hex | Notes |
|-------------|-----|-------|
| mov rax, [rcx] | 48 8B 01 | ModRM=00 000 001 |
| mov rax, [rbp] | 48 8B 45 00 | ModRM=01 000 101, disp8=0 |
| mov rax, [rbp+8] | 48 8B 45 08 | ModRM=01 000 101, disp8=8 |
| mov rax, [rbp-8] | 48 8B 45 F8 | ModRM=01 000 101, disp8=-8 |
| mov rax, [rbp+0x80] | 48 8B 85 80 00 00 00 | ModRM=10, disp32 |
| mov rax, [rsp] | 48 8B 04 24 | ModRM=00 000 100, SIB=24 |
| mov rax, [rsp+8] | 48 8B 44 24 08 | ModRM=01, SIB=24, disp8 |
| mov [rcx], rax | 48 89 01 | Store form |
| mov [rbp+16], rcx | 48 89 4D 10 | |

### LEA - Load Effective Address

```
lea r64, [mem]  ; REX.W 8D /r
```

| Instruction | Hex |
|-------------|-----|
| lea rax, [rbp+16] | 48 8D 45 10 |
| lea rax, [rbp-32] | 48 8D 45 E0 |
| lea rax, [rcx+rdx*4] | 48 8D 04 91 |

### PUSH/POP

```
push r64    ; 50+rd (no REX needed for RAX-RDI)
            ; REX.B 50+rd (for R8-R15)
pop r64     ; 58+rd
            ; REX.B 58+rd (for R8-R15)
```

| Instruction | Hex |
|-------------|-----|
| push rax | 50 |
| push rcx | 51 |
| push rbp | 55 |
| push r8 | 41 50 |
| push r12 | 41 54 |
| pop rax | 58 |
| pop rcx | 59 |
| pop rbp | 5D |
| pop r8 | 41 58 |

### Arithmetic Instructions

```
add r64, r64    ; REX.W 01 /r (store form)
                ; REX.W 03 /r (load form)
add r64, imm8   ; REX.W 83 /0 ib
add r64, imm32  ; REX.W 81 /0 id
add rax, imm32  ; REX.W 05 id (short form)
```

| Instruction | Hex |
|-------------|-----|
| add rax, rcx | 48 01 C8 |
| add rcx, rax | 48 01 C1 |
| add rax, 8 | 48 83 C0 08 |
| add rax, 0x1000 | 48 05 00 10 00 00 |
| add rsp, 32 | 48 83 C4 20 |

| Operation | Opcode | /r | Notes |
|-----------|--------|----|----|
| add | 01/03 | /0 | |
| or | 09/0B | /1 | |
| adc | 11/13 | /2 | With carry |
| sbb | 19/1B | /3 | With borrow |
| and | 21/23 | /4 | |
| sub | 29/2B | /5 | |
| xor | 31/33 | /6 | |
| cmp | 39/3B | /7 | |

### SUB

| Instruction | Hex |
|-------------|-----|
| sub rax, rcx | 48 29 C8 |
| sub rsp, 32 | 48 83 EC 20 |
| sub rsp, 0x100 | 48 81 EC 00 01 00 00 |

### IMUL - Signed Multiply

```
imul r64, r/m64         ; REX.W 0F AF /r (two operand)
imul r64, r/m64, imm8   ; REX.W 6B /r ib
imul r64, r/m64, imm32  ; REX.W 69 /r id
```

| Instruction | Hex |
|-------------|-----|
| imul rax, rcx | 48 0F AF C1 |
| imul rcx, rax | 48 0F AF C8 |
| imul rax, rcx, 8 | 48 6B C1 08 |

### DIV/IDIV

```
div r/m64   ; REX.W F7 /6    ; Unsigned: RDX:RAX / r/m64 -> RAX (quot), RDX (rem)
idiv r/m64  ; REX.W F7 /7    ; Signed
```

| Instruction | Hex |
|-------------|-----|
| div rcx | 48 F7 F1 |
| idiv rcx | 48 F7 F9 |

**Prerequisite**: Sign-extend RAX to RDX:RAX with `cqo` (signed) or zero RDX (unsigned).

### CQO/CDQ - Sign Extend

```
cqo     ; 48 99     ; Sign-extend RAX to RDX:RAX (64-bit)
cdq     ; 99        ; Sign-extend EAX to EDX:EAX (32-bit)
```

### NEG/NOT

```
neg r/m64   ; REX.W F7 /3
not r/m64   ; REX.W F7 /2
```

| Instruction | Hex |
|-------------|-----|
| neg rax | 48 F7 D8 |
| not rax | 48 F7 D0 |

### Shift Operations

```
shl r/m64, cl   ; REX.W D3 /4
shr r/m64, cl   ; REX.W D3 /5
sar r/m64, cl   ; REX.W D3 /7
shl r/m64, imm8 ; REX.W C1 /4 ib
```

| Instruction | Hex |
|-------------|-----|
| shl rax, cl | 48 D3 E0 |
| shr rax, cl | 48 D3 E8 |
| sar rax, cl | 48 D3 F8 |
| shl rax, 4 | 48 C1 E0 04 |

### CMP - Compare

```
cmp r/m64, r64  ; REX.W 39 /r
cmp r64, r/m64  ; REX.W 3B /r
cmp rax, imm32  ; REX.W 3D id
cmp r/m64, imm8 ; REX.W 83 /7 ib
```

| Instruction | Hex |
|-------------|-----|
| cmp rax, rcx | 48 39 C8 |
| cmp rcx, rax | 48 39 C1 |
| cmp rax, 0 | 48 83 F8 00 |

### TEST

```
test r/m64, r64 ; REX.W 85 /r
test rax, imm32 ; REX.W A9 id
```

| Instruction | Hex |
|-------------|-----|
| test rax, rax | 48 85 C0 |
| test rcx, rcx | 48 85 C9 |

### SETcc - Set Byte on Condition

```
setcc r/m8  ; 0F 9x /0
```

| Condition | Opcode | Flag Test |
|-----------|--------|-----------|
| sete/setz | 0F 94 | ZF=1 |
| setne/setnz | 0F 95 | ZF=0 |
| setl/setnge | 0F 9C | SF≠OF |
| setge/setnl | 0F 9D | SF=OF |
| setle/setng | 0F 9E | ZF=1 or SF≠OF |
| setg/setnle | 0F 9F | ZF=0 and SF=OF |
| setb/setnae | 0F 92 | CF=1 |
| setae/setnb | 0F 93 | CF=0 |
| setbe/setna | 0F 96 | CF=1 or ZF=1 |
| seta/setnbe | 0F 97 | CF=0 and ZF=0 |

| Instruction | Hex |
|-------------|-----|
| sete al | 0F 94 C0 |
| setne al | 0F 95 C0 |
| setg al | 0F 9F C0 |
| seta al | 0F 97 C0 |

### MOVZX/MOVSX - Zero/Sign Extend

```
movzx r32, r/m8     ; 0F B6 /r
movzx r32, r/m16    ; 0F B7 /r
movsx r32, r/m8     ; 0F BE /r
movsx r32, r/m16    ; 0F BF /r
movsxd r64, r/m32   ; REX.W 63 /r
```

| Instruction | Hex |
|-------------|-----|
| movzx eax, al | 0F B6 C0 |
| movzx eax, cl | 0F B6 C1 |
| movsx eax, al | 0F BE C0 |
| movsxd rax, eax | 48 63 C0 |

---

## Control Flow Instructions

### JMP - Unconditional Jump

```
jmp rel8    ; EB cb      ; Short jump (-128 to +127)
jmp rel32   ; E9 cd      ; Near jump
jmp r/m64   ; FF /4      ; Indirect jump
```

| Instruction | Hex |
|-------------|-----|
| jmp +5 | EB 05 |
| jmp +0x100 | E9 00 01 00 00 |
| jmp rax | FF E0 |

### Jcc - Conditional Jump

```
jcc rel8    ; 7x cb      ; Short form
jcc rel32   ; 0F 8x cd   ; Near form
```

| Condition | Short | Near | Flag Test |
|-----------|-------|------|-----------|
| je/jz | 74 | 0F 84 | ZF=1 |
| jne/jnz | 75 | 0F 85 | ZF=0 |
| jl/jnge | 7C | 0F 8C | SF≠OF |
| jge/jnl | 7D | 0F 8D | SF=OF |
| jle/jng | 7E | 0F 8E | ZF=1 or SF≠OF |
| jg/jnle | 7F | 0F 8F | ZF=0 and SF=OF |
| jb/jnae/jc | 72 | 0F 82 | CF=1 |
| jae/jnb/jnc | 73 | 0F 83 | CF=0 |
| jbe/jna | 76 | 0F 86 | CF=1 or ZF=1 |
| ja/jnbe | 77 | 0F 87 | CF=0 and ZF=0 |
| jo | 70 | 0F 80 | OF=1 |
| jno | 71 | 0F 81 | OF=0 |
| js | 78 | 0F 88 | SF=1 |
| jns | 79 | 0F 89 | SF=0 |

### CALL

```
call rel32  ; E8 cd      ; Near call
call r/m64  ; FF /2      ; Indirect call
```

| Instruction | Hex |
|-------------|-----|
| call +0x100 | E8 00 01 00 00 |
| call rax | FF D0 |
| call rcx | FF D1 |
| call r10 | 41 FF D2 |

### RET

```
ret     ; C3           ; Return
ret imm16 ; C2 iw      ; Return and pop stack
```

---

## Stack Frame Instructions

### ENTER/LEAVE

```
enter imm16, imm8   ; C8 iw ib   ; Create stack frame
leave               ; C9          ; Destroy stack frame (mov rsp,rbp; pop rbp)
```

**Typical Prologue**:
```x64
push rbp            ; 55
mov rbp, rsp        ; 48 89 E5
sub rsp, frameSize  ; 48 83 EC xx (or 48 81 EC xxxxxxxx)
```

**Typical Epilogue**:
```x64
leave               ; C9 (equivalent to: mov rsp,rbp; pop rbp)
ret                 ; C3
```

---

## SSE/Floating-Point Instructions

### MOVSS/MOVSD - Scalar Move

```
movss xmm, xmm/m32  ; F3 0F 10 /r
movss xmm/m32, xmm  ; F3 0F 11 /r
movsd xmm, xmm/m64  ; F2 0F 10 /r
movsd xmm/m64, xmm  ; F2 0F 11 /r
```

| Instruction | Hex |
|-------------|-----|
| movss xmm0, xmm1 | F3 0F 10 C1 |
| movss xmm0, [rax] | F3 0F 10 00 |
| movss [rax], xmm0 | F3 0F 11 00 |
| movsd xmm0, xmm1 | F2 0F 10 C1 |

### MOVD/MOVQ - Move to/from GP Register

```
movd xmm, r/m32     ; 66 0F 6E /r
movd r/m32, xmm     ; 66 0F 7E /r
movq xmm, r/m64     ; 66 REX.W 0F 6E /r
movq r/m64, xmm     ; 66 REX.W 0F 7E /r
```

| Instruction | Hex |
|-------------|-----|
| movd xmm0, eax | 66 0F 6E C0 |
| movd eax, xmm0 | 66 0F 7E C0 |
| movq xmm0, rax | 66 48 0F 6E C0 |

### Scalar Arithmetic

```
addss xmm, xmm/m32  ; F3 0F 58 /r
addsd xmm, xmm/m64  ; F2 0F 58 /r
subss xmm, xmm/m32  ; F3 0F 5C /r
subsd xmm, xmm/m64  ; F2 0F 5C /r
mulss xmm, xmm/m32  ; F3 0F 59 /r
mulsd xmm, xmm/m64  ; F2 0F 59 /r
divss xmm, xmm/m32  ; F3 0F 5E /r
divsd xmm, xmm/m64  ; F2 0F 5E /r
```

| Instruction | Hex |
|-------------|-----|
| addss xmm0, xmm1 | F3 0F 58 C1 |
| addsd xmm0, xmm1 | F2 0F 58 C1 |
| subss xmm0, xmm1 | F3 0F 5C C1 |
| mulss xmm0, xmm1 | F3 0F 59 C1 |
| divss xmm0, xmm1 | F3 0F 5E C1 |

### Conversion Instructions

```
cvtsi2ss xmm, r/m32     ; F3 0F 2A /r
cvtsi2ss xmm, r/m64     ; F3 REX.W 0F 2A /r
cvtsi2sd xmm, r/m32     ; F2 0F 2A /r
cvtsi2sd xmm, r/m64     ; F2 REX.W 0F 2A /r
cvtss2si r32, xmm/m32   ; F3 0F 2D /r
cvtss2si r64, xmm/m32   ; F3 REX.W 0F 2D /r
cvtsd2si r32, xmm/m64   ; F2 0F 2D /r
cvtsd2si r64, xmm/m64   ; F2 REX.W 0F 2D /r
cvtss2sd xmm, xmm/m32   ; F3 0F 5A /r
cvtsd2ss xmm, xmm/m64   ; F2 0F 5A /r
```

| Instruction | Hex |
|-------------|-----|
| cvtsi2ss xmm0, eax | F3 0F 2A C0 |
| cvtsi2ss xmm0, rax | F3 48 0F 2A C0 |
| cvtsi2sd xmm0, rax | F2 48 0F 2A C0 |
| cvtss2si eax, xmm0 | F3 0F 2D C0 |
| cvtsd2si rax, xmm0 | F2 48 0F 2D C0 |
| cvtss2sd xmm0, xmm1 | F3 0F 5A C1 |
| cvtsd2ss xmm0, xmm1 | F2 0F 5A C1 |

### Comparison

```
ucomiss xmm, xmm/m32    ; 0F 2E /r   ; Unordered compare
ucomisd xmm, xmm/m64    ; 66 0F 2E /r
```

---

## String/Block Operations

```
rep movsb   ; F3 A4      ; Copy RCX bytes from [RSI] to [RDI]
rep movsq   ; F3 48 A5   ; Copy RCX qwords
rep stosb   ; F3 AA      ; Fill RCX bytes at [RDI] with AL
rep stosq   ; F3 48 AB   ; Fill RCX qwords with RAX
```

---

## Miscellaneous

### INT3 - Breakpoint

```
int3    ; CC
```

### NOP

```
nop     ; 90
; Multi-byte NOPs for alignment:
nop [rax]           ; 0F 1F 00
nop [rax+0]         ; 0F 1F 40 00
; etc.
```

### XCHG

```
xchg rax, r64   ; 90+rd (when one operand is RAX)
xchg r64, r64   ; REX.W 87 /r
```

---

## Prologue/Epilogue Patterns

### Standard Frame

```x64
; Prologue
push rbp                    ; 55
mov rbp, rsp               ; 48 89 E5
sub rsp, frameSize         ; 48 83 EC xx / 48 81 EC xxxxxxxx

; Callee-save registers (if needed)
mov [rbp-8], rbx           ; 48 89 5D F8
mov [rbp-16], r12          ; 4C 89 65 F0
mov [rbp-24], r13          ; 4C 89 6D E8
mov [rbp-32], r14          ; 4C 89 75 E0
mov [rbp-40], r15          ; 4C 89 7D D8

; Home arguments (shadow space)
mov [rbp+16], rcx          ; 48 89 4D 10  (arg0)
mov [rbp+24], rdx          ; 48 89 55 18  (arg1)
mov [rbp+32], r8           ; 4C 89 45 20  (arg2)
mov [rbp+40], r9           ; 4C 89 4D 28  (arg3)

; ... function body ...

; Epilogue
mov rbx, [rbp-8]           ; 48 8B 5D F8
mov r12, [rbp-16]          ; 4C 8B 65 F0
mov r13, [rbp-24]          ; 4C 8B 6D E8
mov r14, [rbp-32]          ; 4C 8B 75 E0
mov r15, [rbp-40]          ; 4C 8B 7D D8
leave                      ; C9
ret                        ; C3
```

**Callee-Saved Register Notes**:

1. **RBX, R12-R15**: Always callee-saved on both Windows and SysV ABIs. Must save if used.

2. **RSI, RDI**:
   - Windows x64: Caller-saved (no need to preserve)
   - System V x64: Callee-saved (must preserve if used)
   - ProtonOS uses a simplified calling convention based on Windows x64 (args in RCX/RDX/R8/R9)
   - The JIT doesn't typically use RSI/RDI except for `rep movsb`/`rep stosb`, which are
     used only in specific contexts where they can be freely modified.

3. **XMM6-XMM15**:
   - Windows x64: Callee-saved (must save/restore 128-bit registers)
   - System V x64: Caller-saved (no need to preserve)
   - ProtonOS currently doesn't save XMM registers in the prologue. This works because
     the naive JIT only uses XMM0-XMM3 for float args/returns, not for persistent storage.

The example prologue above shows the minimum saves needed for typical JIT-generated code.
Functions that use additional registers must add appropriate save/restore sequences.

### Frame Layout

```
[Higher addresses]
+------------------------+
| Arg N (if N > 4)       | [RBP + 48 + (N-4)*8]
| ...                    |
| Arg 4 (5th arg)        | [RBP + 48]
+------------------------+
| Shadow: Arg 3 (R9)     | [RBP + 40]
| Shadow: Arg 2 (R8)     | [RBP + 32]
| Shadow: Arg 1 (RDX)    | [RBP + 24]
| Shadow: Arg 0 (RCX)    | [RBP + 16]
+------------------------+
| Return address         | [RBP + 8]
+------------------------+
| Saved RBP              | [RBP] <- RBP points here
+------------------------+
| Saved RBX              | [RBP - 8]
| Saved R12              | [RBP - 16]
| Saved R13              | [RBP - 24]
| Saved R14              | [RBP - 32]
| Saved R15              | [RBP - 40]
+------------------------+
| Local 0                | [RBP - 48 - local0Size]
| Local 1                | [RBP - 48 - local0Size - local1Size]
| ...                    |
+------------------------+
| Eval stack / temps     |
+------------------------+
| Outgoing shadow space  | [RSP + 0..31]
+------------------------+ <- RSP (16-byte aligned)
[Lower addresses]
```

---

## Call Sequence

### Caller Side

```x64
; Set up arguments (first 4 in registers)
mov rcx, arg0
mov rdx, arg1
mov r8, arg2
mov r9, arg3

; Additional args on stack (right to left, above shadow space)
; Stack already has 32-byte shadow space from frame setup

call target

; Return value in RAX (or RAX:RDX for 16-byte structs)
```

### With Hidden Buffer (Large Struct Return)

```x64
; Allocate buffer on stack
sub rsp, structSize

; Hidden buffer pointer = arg0
lea rcx, [rsp]

; Shift other arguments
mov rdx, <original_arg0>
mov r8, <original_arg1>
mov r9, <original_arg2>

call target

; Return value is now in buffer at [RSP]
; (RAX also contains buffer pointer)
```

---

*Reference: /home/shane/protonos/src/kernel/Runtime/JIT/X64Emitter.cs*
