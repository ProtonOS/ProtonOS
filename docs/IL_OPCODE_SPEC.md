# IL Opcode Specification - ProtonOS JIT

This document provides the authoritative reference for all CIL (Common Intermediate Language) opcodes as implemented in the ProtonOS Tier 0 JIT compiler. For each opcode, it specifies:

1. **ECMA-335 Semantics** - Official stack behavior and operation
2. **Value Type Handling** - How structs of different sizes are treated
3. **ProtonOS x64 Implementation** - Expected machine code emission
4. **Verification Patterns** - Expected byte sequences for testing

## Reference Documents

- [ECMA-335 6th Edition](https://ecma-international.org/publications-and-standards/standards/ecma-335/)
- [stakx/ecma-335 Markdown](https://github.com/stakx/ecma-335) - Complete spec in markdown
- ProtonOS JIT: `src/kernel/Runtime/JIT/ILCompiler.cs`

## Value Type Size Thresholds (x64 ABI)

| Size | Category | Return Convention | Stack Slots |
|------|----------|-------------------|-------------|
| 1-8 bytes | Small | RAX | 1 |
| 9-16 bytes | Medium | RAX:RDX | 2 |
| >16 bytes | Large | Hidden buffer (RCX) | N = ceil(size/8) |

## Stack Type Tracking

The JIT tracks evaluation stack entries with these types:
- `StackType_Int` (0) - Integer/pointer (8 bytes on x64)
- `StackType_Float32` (1) - 32-bit float
- `StackType_Float64` (2) - 64-bit float
- `StackType_ValueType` (3) - Part of a value type (may span multiple slots)

## TOS Caching

The naive JIT uses "Top-of-Stack caching" - the topmost value is kept in RAX when possible, avoiding unnecessary push/pop sequences.

---

# 1. Constants (0x14-0x23)

## 1.1 ldnull (0x14)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., null
- **Operation**: Push a null object reference onto the stack
- **Operands**: None
- **Description**: Pushes a null reference (type O) on the stack

### Value Type Considerations
- N/A - pushes object reference, not value type

### ProtonOS x64 Implementation

**TOS Caching**: Not cached before → cached after (RAX = 0)

**Machine Code**:
```x64
xor eax, eax          ; RAX = 0 (null reference)
```

**Hex**: `31 C0`

### Verification
```
IL:       14 2A           ; ldnull; ret
Expected: 31 C0 C3        ; xor eax,eax; ret (minimal)
```

---

## 1.2 ldc.i4.m1 (0x15)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., -1 (int32)
- **Operation**: Push -1 onto the stack as int32
- **Operands**: None

### Value Type Considerations
- N/A - primitive int32

### ProtonOS x64 Implementation

**TOS Caching**: Not cached before → cached after (RAX = -1)

**Constant Tracking**: `_tosIsConst = true`, `_tosConstValue = -1`

**Machine Code** (if constant is materialized):
```x64
mov eax, 0xFFFFFFFF   ; or: or rax, -1
```

**Hex**: `B8 FF FF FF FF`

### Verification
```
IL:       15 2A           ; ldc.i4.m1; ret
Expected: B8 FF FF FF FF C3
```

---

## 1.3-1.11 ldc.i4.0 through ldc.i4.8 (0x16-0x1E)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., N (int32) where N is 0-8
- **Operation**: Push constant 0-8 onto stack as int32
- **Operands**: None

### Value Type Considerations
- N/A - primitive int32

### ProtonOS x64 Implementation

| Opcode | Hex | Constant | Machine Code |
|--------|-----|----------|--------------|
| ldc.i4.0 | 0x16 | 0 | `xor eax, eax` or `mov eax, 0` |
| ldc.i4.1 | 0x17 | 1 | `mov eax, 1` |
| ldc.i4.2 | 0x18 | 2 | `mov eax, 2` |
| ldc.i4.3 | 0x19 | 3 | `mov eax, 3` |
| ldc.i4.4 | 0x1A | 4 | `mov eax, 4` |
| ldc.i4.5 | 0x1B | 5 | `mov eax, 5` |
| ldc.i4.6 | 0x1C | 6 | `mov eax, 6` |
| ldc.i4.7 | 0x1D | 7 | `mov eax, 7` |
| ldc.i4.8 | 0x1E | 8 | `mov eax, 8` |

**Note**: With constant folding enabled, these may not emit code immediately - value is tracked in `_tosConstValue` and materialized when needed.

### Verification
```
IL:       17 2A           ; ldc.i4.1; ret
Expected: B8 01 00 00 00 C3
```

---

## 1.12 ldc.i4.s (0x1F)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., num (int32)
- **Operation**: Push signed 8-bit integer as int32
- **Operands**: `<int8>` - signed byte (-128 to 127)
- **Description**: The value is sign-extended to int32

### Value Type Considerations
- N/A - primitive int32

### ProtonOS x64 Implementation

**Operand Decoding**: Read 1 byte, sign-extend to int32

**Machine Code**:
```x64
mov eax, <sign-extended value>
```

**Example** (value = -5 = 0xFB):
```x64
mov eax, 0xFFFFFFFB    ; -5 sign-extended
```

**Hex**: `B8 FB FF FF FF`

### Verification
```
IL:       1F 05 2A        ; ldc.i4.s 5; ret
Expected: B8 05 00 00 00 C3

IL:       1F FB 2A        ; ldc.i4.s -5; ret
Expected: B8 FB FF FF FF C3
```

---

## 1.13 ldc.i4 (0x20)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., num (int32)
- **Operation**: Push 32-bit integer constant
- **Operands**: `<int32>` - 4 bytes, little-endian

### Value Type Considerations
- N/A - primitive int32

### ProtonOS x64 Implementation

**Operand Decoding**: Read 4 bytes as int32

**Machine Code**:
```x64
mov eax, <imm32>
```

**Hex**: `B8 <imm32-le>`

### Verification
```
IL:       20 78 56 34 12 2A    ; ldc.i4 0x12345678; ret
Expected: B8 78 56 34 12 C3
```

---

## 1.14 ldc.i8 (0x21)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., num (int64)
- **Operation**: Push 64-bit integer constant
- **Operands**: `<int64>` - 8 bytes, little-endian

### Value Type Considerations
- N/A - primitive int64

### ProtonOS x64 Implementation

**Operand Decoding**: Read 8 bytes as int64

**Machine Code**:
```x64
mov rax, <imm64>       ; REX.W + mov r64, imm64
```

**Hex**: `48 B8 <imm64-le>`

### Verification
```
IL:       21 EF CD AB 90 78 56 34 12 2A    ; ldc.i8 0x1234567890ABCDEF; ret
Expected: 48 B8 EF CD AB 90 78 56 34 12 C3
```

---

## 1.15 ldc.r4 (0x22)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., num (F)
- **Operation**: Push 32-bit float constant
- **Operands**: `<float32>` - 4 bytes, IEEE 754 single precision
- **Description**: Pushed as F (native float representation)

### Value Type Considerations
- N/A - primitive float32 (uses XMM register)

### ProtonOS x64 Implementation

**Operand Decoding**: Read 4 bytes as float32 bit pattern

**Machine Code** (constant stored inline):
```x64
; Option 1: Load from inline constant
mov eax, <float-bits>
movd xmm0, eax

; Option 2: Push to stack, load from stack
mov dword [rsp-4], <float-bits>
movss xmm0, [rsp-4]
```

**Stack Type**: `StackType_Float32`

**Note**: TOS caching doesn't apply to floats - they use XMM0

### Verification
```
IL:       22 00 00 80 3F 2A    ; ldc.r4 1.0f; ret (1.0f = 0x3F800000)
; Returns float in XMM0
```

---

## 1.16 ldc.r8 (0x23)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., num (F)
- **Operation**: Push 64-bit float constant
- **Operands**: `<float64>` - 8 bytes, IEEE 754 double precision
- **Description**: Pushed as F (native float representation)

### Value Type Considerations
- N/A - primitive float64 (uses XMM register)

### ProtonOS x64 Implementation

**Operand Decoding**: Read 8 bytes as float64 bit pattern

**Machine Code**:
```x64
mov rax, <double-bits>
movq xmm0, rax
```

**Stack Type**: `StackType_Float64`

### Verification
```
IL:       23 00 00 00 00 00 00 F0 3F 2A    ; ldc.r8 1.0; ret (1.0 = 0x3FF0000000000000)
; Returns double in XMM0
```

---

# 2. Arguments (0x02-0x05, 0x0E-0x10, 0xFE09-0xFE0B)

## 2.1 ldarg.0 through ldarg.3 (0x02-0x05)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., value
- **Operation**: Load argument N onto evaluation stack
- **Operands**: None (index encoded in opcode)
- **Description**: For instance methods, arg 0 is `this`

### Value Type Considerations

| Arg Type | Size | Stack Representation |
|----------|------|---------------------|
| Primitive/ref | 8 bytes | Single slot, value in slot |
| Small struct | ≤8 bytes | Single slot, value in slot |
| Medium struct | 9-16 bytes | Two slots, value spread |
| Large struct | >16 bytes | Single slot, **pointer** to arg |

**Critical**: For large structs (>16 bytes), the argument is passed by pointer per x64 ABI. `ldarg` loads that **pointer**, not the value. Use `ldobj` to dereference.

### ProtonOS x64 Implementation

**Argument Locations** (x64 calling convention):
| Arg Index | Register | Shadow Space |
|-----------|----------|--------------|
| 0 | RCX | [RBP+16] |
| 1 | RDX | [RBP+24] |
| 2 | R8 | [RBP+32] |
| 3 | R9 | [RBP+40] |
| 4+ | - | [RBP+48+n*8] |

**Machine Code** (for arg in shadow space, single slot):
```x64
mov rax, [rbp+16]     ; ldarg.0
mov rax, [rbp+24]     ; ldarg.1
mov rax, [rbp+32]     ; ldarg.2
mov rax, [rbp+40]     ; ldarg.3
```

**For medium struct (9-16 bytes)**:
```x64
; Need to load both slots
mov rax, [rbp+offset]
push rax
mov rax, [rbp+offset+8]
; Now have 2 slots on stack
```

### Verification
```
IL:       02 2A           ; ldarg.0; ret
Expected: 48 8B 45 10 C3  ; mov rax,[rbp+16]; ret
```

---

## 2.2 ldarg.s (0x0E)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., value
- **Operation**: Load argument at index (short form)
- **Operands**: `<uint8>` - argument index (0-255)

### Value Type Considerations
Same as ldarg.0-3

### ProtonOS x64 Implementation

**Operand Decoding**: Read 1 byte as unsigned index

**Machine Code**:
```x64
mov rax, [rbp + 16 + index*8]   ; for args 0-3 in shadow
; Or for args beyond shadow space:
mov rax, [rbp + 48 + (index-4)*8]
```

### Verification
```
IL:       0E 04 2A        ; ldarg.s 4; ret (5th argument)
Expected: 48 8B 45 30 C3  ; mov rax,[rbp+48]; ret
```

---

## 2.3 starg.s (0x10)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Store value to argument slot
- **Operands**: `<uint8>` - argument index

### Value Type Considerations
Same as ldarg - large structs store to the pointer location

### ProtonOS x64 Implementation

**Machine Code**:
```x64
; Flush TOS to stack if needed
mov [rbp + 16 + index*8], rax
```

### Verification
```
IL:       17 10 00 2A     ; ldc.i4.1; starg.s 0; ret
```

---

## 2.4 ldarga.s (0x0F)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., address
- **Operation**: Load address of argument
- **Operands**: `<uint8>` - argument index
- **Description**: Pushes managed pointer to the argument

### Value Type Considerations
- Always pushes a **pointer** (single slot)
- For large struct args (already passed by pointer), this is address of the pointer slot, not the struct data

### ProtonOS x64 Implementation

**Machine Code**:
```x64
lea rax, [rbp + 16 + index*8]
```

**Stack Type**: `StackType_Int` (pointer)

### Verification
```
IL:       0F 00 2A        ; ldarga.s 0; ret
Expected: 48 8D 45 10 C3  ; lea rax,[rbp+16]; ret
```

---

## 2.5 ldarg (0xFE 0x09)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., value
- **Operation**: Load argument (extended form)
- **Operands**: `<uint16>` - argument index (0-65534)

### ProtonOS x64 Implementation
Same as ldarg.s but with 2-byte operand.

---

## 2.6 starg (0xFE 0x0B)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Store to argument (extended form)
- **Operands**: `<uint16>` - argument index

---

## 2.7 ldarga (0xFE 0x0A)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., address
- **Operation**: Load argument address (extended form)
- **Operands**: `<uint16>` - argument index

---

# 3. Locals (0x06-0x0D, 0x11-0x13, 0xFE0C-0xFE0E)

## 3.1 ldloc.0 through ldloc.3 (0x06-0x09)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., value
- **Operation**: Load local variable N onto stack
- **Operands**: None (index encoded in opcode)

### Value Type Considerations

| Local Type | Size | Stack Representation |
|------------|------|---------------------|
| Primitive/ref | 8 bytes | Single slot, value |
| Small struct | ≤8 bytes | Single slot, value |
| Medium struct | 9-16 bytes | Two slots, value |
| Large struct | >16 bytes | N slots, full value |

**Unlike arguments**: Local variables always hold the **actual value**, never a pointer.

### ProtonOS x64 Implementation

**Local Variable Layout**:
- Locals are below RBP, after callee-saved registers
- `_localOffset[i]` stores the offset from RBP for each local

**Machine Code** (single slot):
```x64
mov rax, [rbp - offset]
```

**Machine Code** (multi-slot value type):
```x64
; For 32-byte struct (4 slots):
sub rsp, 32           ; Make room
mov rax, [rbp-localOffset]
mov [rsp], rax
mov rax, [rbp-localOffset+8]
mov [rsp+8], rax
mov rax, [rbp-localOffset+16]
mov [rsp+16], rax
mov rax, [rbp-localOffset+24]
mov [rsp+24], rax
; Stack types: 4x StackType_ValueType
```

### Verification
```
IL:       06 2A           ; ldloc.0; ret
; Depends on local offset
```

---

## 3.2 stloc.0 through stloc.3 (0x0A-0x0D)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Store to local variable N
- **Operands**: None

### Value Type Considerations
- Consumes the appropriate number of slots for value types
- Must copy all bytes of multi-slot values

### ProtonOS x64 Implementation

**Machine Code** (single slot):
```x64
mov [rbp - offset], rax
```

**Machine Code** (multi-slot):
```x64
; For 32-byte struct from stack:
mov rax, [rsp]
mov [rbp-localOffset], rax
mov rax, [rsp+8]
mov [rbp-localOffset+8], rax
mov rax, [rsp+16]
mov [rbp-localOffset+16], rax
mov rax, [rsp+24]
mov [rbp-localOffset+24], rax
add rsp, 32           ; Pop 4 slots
```

---

## 3.3 ldloc.s (0x11)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., value
- **Operation**: Load local (short form)
- **Operands**: `<uint8>` - local index (0-255)

---

## 3.4 stloc.s (0x13)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Store to local (short form)
- **Operands**: `<uint8>` - local index

---

## 3.5 ldloca.s (0x12)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., address
- **Operation**: Load address of local variable
- **Operands**: `<uint8>` - local index
- **Description**: Pushes managed pointer (&) to the local

### Value Type Considerations
- Always pushes single slot (pointer)
- The pointed-to data may be any size

### ProtonOS x64 Implementation

**Machine Code**:
```x64
lea rax, [rbp - offset]
```

**Stack Type**: `StackType_Int` (pointer, not value type)

---

## 3.6 ldloc (0xFE 0x0C)

Extended form, 2-byte index.

## 3.7 stloc (0xFE 0x0E)

Extended form, 2-byte index.

## 3.8 ldloca (0xFE 0x0D)

Extended form, 2-byte index.

---

# 4. Stack Operations (0x00, 0x01, 0x25-0x27)

## 4.1 nop (0x00)

### ECMA-335 Semantics
- **Stack Transition**: ... → ... (unchanged)
- **Operation**: No operation
- **Operands**: None

### ProtonOS x64 Implementation
No code emitted (truly no-op).

---

## 4.2 break (0x01)

### ECMA-335 Semantics
- **Stack Transition**: ... → ... (unchanged)
- **Operation**: Breakpoint for debugger
- **Operands**: None

### ProtonOS x64 Implementation

**Machine Code**:
```x64
int3
```

**Hex**: `CC`

---

## 4.3 dup (0x25)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., value, value
- **Operation**: Duplicate top stack value
- **Operands**: None

### Value Type Considerations

**CRITICAL**: Must duplicate ALL slots of multi-slot value types!

| TOS Type | Slots | Action |
|----------|-------|--------|
| Primitive | 1 | Duplicate 8 bytes |
| Small VT | 1 | Duplicate 8 bytes |
| Medium VT | 2 | Duplicate 16 bytes |
| Large VT | N | Duplicate N*8 bytes |

### ProtonOS x64 Implementation

**Single Slot** (TOS in RAX):
```x64
push rax              ; Duplicate RAX to stack
; RAX still holds TOS (cached)
```

**Multi-Slot Value Type**:
```x64
; Count consecutive StackType_ValueType entries = N slots
sub rsp, N*8          ; Make room for copy
; Copy each slot:
mov rax, [rsp + N*8 + 0]   ; Original slot 0
mov [rsp + 0], rax
mov rax, [rsp + N*8 + 8]   ; Original slot 1
mov [rsp + 8], rax
; ... repeat for all N slots
; Push N entries of StackType_ValueType to type array
```

### Edge Case (BUG FIXED)
The original `dup` only copied 8 bytes regardless of value type size. This caused stack corruption when duplicating large structs before field access.

---

## 4.4 pop (0x26)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Discard top stack value
- **Operands**: None

### Value Type Considerations

**CRITICAL**: Must discard ALL slots of multi-slot value types!

### ProtonOS x64 Implementation

**Single Slot**:
```x64
add rsp, 8            ; If TOS not in RAX
; Or just clear _tosCached if it was in RAX
```

**Multi-Slot**:
```x64
add rsp, N*8          ; Discard N slots
; Pop N entries from _evalStackTypes
```

---

## 4.5 jmp (0x27)

### ECMA-335 Semantics
- **Stack Transition**: ... → ...
- **Operation**: Jump to method, transferring arguments
- **Operands**: `<T:method>` - method token
- **Description**: Exit current method and jump to target, passing arguments

### ProtonOS x64 Implementation

**Machine Code**:
```x64
; Restore callee-saved registers
; Leave (restore RSP, pop RBP)
jmp <target-address>  ; Instead of ret
```

---

# 5. Arithmetic (0x58-0x66, 0xD6-0xDB)

## 5.1 add (0x58)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 + value2
- **Operands**: None
- **Description**: Integer addition without overflow check. For floats, produces infinity on overflow.

### Type Combinations (Table 2: Binary Numeric Operations)

| value1 | value2 | result |
|--------|--------|--------|
| int32 | int32 | int32 |
| int64 | int64 | int64 |
| native int | native int | native int |
| F | F | F |
| & | native int | & |
| native int | & | & |

### Value Type Considerations
- N/A - operates on primitives only

### ProtonOS x64 Implementation

**Integer** (TOS in RAX, value1 at [RSP]):
```x64
pop rcx               ; value1
add rax, rcx          ; result in RAX
```

**Float** (values in XMM):
```x64
addss xmm0, xmm1      ; float32
; or
addsd xmm0, xmm1      ; float64
```

**Hex** (integer): `59 48 01 C8` (pop rcx; add rax,rcx)

### Verification
```
IL:       17 18 58 2A     ; ldc.i4.1; ldc.i4.2; add; ret (1+2=3)
Expected result: RAX = 3
```

---

## 5.2 sub (0x59)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 - value2
- **Operands**: None

### ProtonOS x64 Implementation

**Integer**:
```x64
pop rcx               ; value1
sub rcx, rax          ; value1 - value2
mov rax, rcx          ; result to RAX
```

**Note**: Order matters! value1 is deeper on stack, value2 is TOS.

---

## 5.3 mul (0x5A)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 * value2

### ProtonOS x64 Implementation

**Integer**:
```x64
pop rcx
imul rax, rcx
```

---

## 5.4 div (0x5B)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 / value2 (signed)
- **Exceptions**:
  - `DivideByZeroException` if value2 is 0
  - `ArithmeticException` if value1 is most negative and value2 is -1

### ProtonOS x64 Implementation

**Integer (signed)**:
```x64
mov rcx, rax          ; value2 (divisor)
pop rax               ; value1 (dividend)
cqo                   ; Sign-extend RAX into RDX:RAX
idiv rcx              ; RAX = quotient, RDX = remainder
```

---

## 5.5 div.un (0x5C)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 / value2 (unsigned)

### ProtonOS x64 Implementation

**Integer (unsigned)**:
```x64
mov rcx, rax          ; value2 (divisor)
pop rax               ; value1 (dividend)
xor edx, edx          ; Zero-extend (unsigned)
div rcx               ; Unsigned division
```

---

## 5.6 rem (0x5D)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 % value2 (signed remainder)

### ProtonOS x64 Implementation
Same as div, but result is in RDX:
```x64
; After idiv:
mov rax, rdx          ; Remainder to RAX
```

---

## 5.7 rem.un (0x5E)

Unsigned remainder - same pattern with `div` instead of `idiv`.

---

## 5.8 and (0x5F)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: result = value1 & value2 (bitwise AND)

### ProtonOS x64 Implementation
```x64
pop rcx
and rax, rcx
```

---

## 5.9 or (0x60)

### ProtonOS x64 Implementation
```x64
pop rcx
or rax, rcx
```

---

## 5.10 xor (0x61)

### ProtonOS x64 Implementation
```x64
pop rcx
xor rax, rcx
```

---

## 5.11 shl (0x62)

### ECMA-335 Semantics
- **Stack Transition**: ..., value, shiftAmount → ..., result
- **Operation**: result = value << shiftAmount

### ProtonOS x64 Implementation
```x64
mov rcx, rax          ; Shift amount must be in CL
pop rax               ; Value to shift
shl rax, cl
```

---

## 5.12 shr (0x63)

### ECMA-335 Semantics
- **Operation**: Arithmetic (signed) right shift

### ProtonOS x64 Implementation
```x64
mov rcx, rax
pop rax
sar rax, cl           ; Arithmetic shift (preserves sign)
```

---

## 5.13 shr.un (0x64)

### ECMA-335 Semantics
- **Operation**: Logical (unsigned) right shift

### ProtonOS x64 Implementation
```x64
mov rcx, rax
pop rax
shr rax, cl           ; Logical shift (zero-fill)
```

---

## 5.14 neg (0x65)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: result = -value

### ProtonOS x64 Implementation
```x64
neg rax
```

---

## 5.15 not (0x66)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: result = ~value (bitwise complement)

### ProtonOS x64 Implementation
```x64
not rax
```

---

## 5.16-5.21 Overflow Arithmetic (0xD6-0xDB)

### add.ovf (0xD6) - Signed addition with overflow check
### add.ovf.un (0xD7) - Unsigned addition with overflow check
### sub.ovf (0xDA) - Signed subtraction with overflow check
### sub.ovf.un (0xDB) - Unsigned subtraction with overflow check
### mul.ovf (0xD8) - Signed multiplication with overflow check
### mul.ovf.un (0xD9) - Unsigned multiplication with overflow check

### ECMA-335 Semantics
- Same stack behavior as non-overflow variants
- **Exception**: `OverflowException` if result doesn't fit

### ProtonOS x64 Implementation

**Signed Overflow** (check OF flag):
```x64
pop rcx
add rax, rcx
jo overflow_handler   ; Jump if overflow flag set
```

**Unsigned Overflow** (check CF flag):
```x64
pop rcx
add rax, rcx
jc overflow_handler   ; Jump if carry flag set
```

---

# 6. Conversion (0x67-0x6E, 0x76, 0xB3-0xBA, 0xD1-0xD5, 0xE0, 0xFE82-0xFE8B)

## 6.1 conv.i1 (0x67)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: Convert to int8, then sign-extend to int32
- **Description**: Truncates value to 8 bits, then sign-extends

### ProtonOS x64 Implementation
```x64
movsx eax, al         ; Sign-extend AL to EAX
```

---

## 6.2 conv.i2 (0x68)

### ProtonOS x64 Implementation
```x64
movsx eax, ax         ; Sign-extend AX to EAX
```

---

## 6.3 conv.i4 (0x69)

### ProtonOS x64 Implementation
```x64
movsxd rax, eax       ; Sign-extend EAX to RAX (on x64)
; Or just: cdqe
```

---

## 6.4 conv.i8 (0x6A)

For int32 to int64:
```x64
movsxd rax, eax       ; Sign-extend 32-bit to 64-bit
```

---

## 6.5 conv.r4 (0x6B)

### ECMA-335 Semantics
- **Operation**: Convert to float32

### ProtonOS x64 Implementation (from int):
```x64
cvtsi2ss xmm0, rax    ; Convert signed int to single-precision float
```

---

## 6.6 conv.r8 (0x6C)

### ProtonOS x64 Implementation:
```x64
cvtsi2sd xmm0, rax    ; Convert signed int to double-precision float
```

---

## 6.7 conv.u4 (0x6D)

### ProtonOS x64 Implementation
```x64
mov eax, eax          ; Zero-extend 32-bit to 64-bit (clears upper 32 bits)
```

---

## 6.8 conv.u8 (0x6E)

No conversion needed on x64 (already 64-bit).

---

## 6.9 conv.r.un (0x76)

### ECMA-335 Semantics
- **Operation**: Convert unsigned integer to float

### ProtonOS x64 Implementation
```x64
; For unsigned int64 to double:
test rax, rax
js handle_negative_bit
cvtsi2sd xmm0, rax
jmp done
handle_negative_bit:
; Special handling for values >= 2^63
mov rcx, rax
shr rcx, 1
and eax, 1
or rcx, rax
cvtsi2sd xmm0, rcx
addsd xmm0, xmm0
done:
```

---

## 6.10 conv.u1 (0xD2)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: Convert to unsigned int8, then zero-extend to int32

### ProtonOS x64 Implementation
```x64
movzx eax, al         ; Zero-extend AL to EAX
```

---

## 6.11 conv.u2 (0xD1)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: Convert to unsigned int16, then zero-extend to int32

### ProtonOS x64 Implementation
```x64
movzx eax, ax         ; Zero-extend AX to EAX
```

---

## 6.12 conv.i (0xD3)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: Convert to native int (sign-extend on 64-bit)

### ProtonOS x64 Implementation
On x64, native int is 64-bit:
```x64
movsxd rax, eax       ; Sign-extend 32-bit to 64-bit (if source is i4)
; If source is already 64-bit, no conversion needed
```

---

## 6.13 conv.u (0xE0)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ..., result
- **Operation**: Convert to native unsigned int (zero-extend on 64-bit)

### ProtonOS x64 Implementation
```x64
mov eax, eax          ; Zero-extend 32-bit to 64-bit (clears upper 32 bits)
```

---

## 6.14-6.23 conv.ovf.* variants (0xB3-0xBA, 0xD4-0xD5)

These check for overflow during conversion and throw `OverflowException` if the value doesn't fit in the target type.

| Opcode | Hex | Operation |
|--------|-----|-----------|
| conv.ovf.i1 | 0xB3 | Convert to int8 with overflow check |
| conv.ovf.u1 | 0xB4 | Convert to uint8 with overflow check |
| conv.ovf.i2 | 0xB5 | Convert to int16 with overflow check |
| conv.ovf.u2 | 0xB6 | Convert to uint16 with overflow check |
| conv.ovf.i4 | 0xB7 | Convert to int32 with overflow check |
| conv.ovf.u4 | 0xB8 | Convert to uint32 with overflow check |
| conv.ovf.i8 | 0xB9 | Convert to int64 with overflow check |
| conv.ovf.u8 | 0xBA | Convert to uint64 with overflow check |
| conv.ovf.i | 0xD4 | Convert to native int with overflow check |
| conv.ovf.u | 0xD5 | Convert to native uint with overflow check |

### Implementation Pattern (conv.ovf.i1 example)
```x64
; Check if value fits in signed 8-bit range (-128 to 127)
cmp rax, -128
jl overflow_handler
cmp rax, 127
jg overflow_handler
movsx eax, al         ; Truncate and sign-extend
```

### Implementation Pattern (conv.ovf.u1 example)
```x64
; Check if value fits in unsigned 8-bit range (0 to 255)
test rax, rax
js overflow_handler   ; Negative = overflow for unsigned
cmp rax, 255
ja overflow_handler
movzx eax, al         ; Truncate and zero-extend
```

---

## 6.24-6.33 conv.ovf.*.un variants (unsigned source, 0xFE82-0xFE8B)

These convert from an unsigned source value with overflow checking.

| Opcode | Hex | Operation |
|--------|-----|-----------|
| conv.ovf.i1.un | 0xFE 0x82 | Unsigned to int8 with overflow check |
| conv.ovf.u1.un | 0xFE 0x83 | Unsigned to uint8 with overflow check |
| conv.ovf.i2.un | 0xFE 0x84 | Unsigned to int16 with overflow check |
| conv.ovf.u2.un | 0xFE 0x85 | Unsigned to uint16 with overflow check |
| conv.ovf.i4.un | 0xFE 0x86 | Unsigned to int32 with overflow check |
| conv.ovf.u4.un | 0xFE 0x87 | Unsigned to uint32 with overflow check |
| conv.ovf.i8.un | 0xFE 0x88 | Unsigned to int64 with overflow check |
| conv.ovf.u8.un | 0xFE 0x89 | Unsigned to uint64 with overflow check |
| conv.ovf.i.un | 0xFE 0x8A | Unsigned to native int with overflow check |
| conv.ovf.u.un | 0xFE 0x8B | Unsigned to native uint with overflow check |

### Key Difference from Signed Variants
- Source value is treated as unsigned
- For `.un` converting to signed target: value must be in positive range of target

---

# 7. Comparison (0xFE01-0xFE05)

## 7.1 ceq (0xFE 0x01)

### ECMA-335 Semantics
- **Stack Transition**: ..., value1, value2 → ..., result
- **Operation**: Push 1 if equal, 0 otherwise
- **Result Type**: int32

### ProtonOS x64 Implementation
```x64
pop rcx               ; value1
cmp rcx, rax          ; Compare
sete al               ; AL = 1 if equal
movzx eax, al         ; Zero-extend to full register
```

---

## 7.2 cgt (0xFE 0x02)

### ECMA-335 Semantics
- **Operation**: Push 1 if value1 > value2 (signed)

### ProtonOS x64 Implementation
```x64
pop rcx
cmp rcx, rax
setg al               ; Signed greater
movzx eax, al
```

---

## 7.3 cgt.un (0xFE 0x03)

### ECMA-335 Semantics
- **Operation**: Push 1 if value1 > value2 (unsigned/unordered)

### ProtonOS x64 Implementation
```x64
pop rcx
cmp rcx, rax
seta al               ; Unsigned above
movzx eax, al
```

---

## 7.4 clt (0xFE 0x04)

### ProtonOS x64 Implementation
```x64
pop rcx
cmp rcx, rax
setl al               ; Signed less
movzx eax, al
```

---

## 7.5 clt.un (0xFE 0x05)

### ProtonOS x64 Implementation
```x64
pop rcx
cmp rcx, rax
setb al               ; Unsigned below
movzx eax, al
```

---

# 8. Branches (0x2B-0x45, 0x38-0x44)

## 8.1 br.s (0x2B)

### ECMA-335 Semantics
- **Stack Transition**: ... → ... (unchanged)
- **Operation**: Unconditional branch (short form)
- **Operands**: `<int8>` - signed offset from next instruction

### ProtonOS x64 Implementation
```x64
jmp <target>
```

---

## 8.2 brfalse.s / brnull.s / brzero.s (0x2C)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Branch if value is false/null/zero
- **Operands**: `<int8>` - signed offset

### ProtonOS x64 Implementation
```x64
test rax, rax         ; Check if zero
je <target>
```

---

## 8.3 brtrue.s / brinst.s (0x2D)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Branch if value is true/non-null/non-zero

### ProtonOS x64 Implementation
```x64
test rax, rax
jne <target>
```

---

## 8.4-8.13 Conditional Branches (Short Form)

| Opcode | Hex | Condition | x64 Instruction |
|--------|-----|-----------|-----------------|
| beq.s | 0x2E | value1 == value2 | je |
| bge.s | 0x2F | value1 >= value2 (signed) | jge |
| bgt.s | 0x30 | value1 > value2 (signed) | jg |
| ble.s | 0x31 | value1 <= value2 (signed) | jle |
| blt.s | 0x32 | value1 < value2 (signed) | jl |
| bne.un.s | 0x33 | value1 != value2 | jne |
| bge.un.s | 0x34 | value1 >= value2 (unsigned) | jae |
| bgt.un.s | 0x35 | value1 > value2 (unsigned) | ja |
| ble.un.s | 0x36 | value1 <= value2 (unsigned) | jbe |
| blt.un.s | 0x37 | value1 < value2 (unsigned) | jb |

### Common Implementation Pattern
```x64
pop rcx               ; value1
cmp rcx, rax          ; Compare value1 with value2
j<cc> <target>        ; Conditional jump
```

---

## 8.14-8.27 Long Form Branches (0x38-0x44)

Same semantics as short form, but with `<int32>` offset operand instead of `<int8>`.

---

## 8.28 switch (0x45)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: Jump table dispatch
- **Operands**: `<uint32>` count, followed by count `<int32>` offsets
- **Description**: If value < count, jump to offset[value], else fall through

### ProtonOS x64 Implementation
```x64
cmp eax, <count>
jae fall_through      ; If value >= count, skip switch
lea rcx, [rip + jump_table]
movsxd rax, dword [rcx + rax*4]
add rcx, rax
jmp rcx
jump_table:
    dd offset0, offset1, offset2, ...
fall_through:
```

---

# 9. Method Calls (0x28, 0x29, 0x2A, 0x6F, 0xFE06, 0xFE07)

## 9.1 call (0x28)

### ECMA-335 Semantics
- **Stack Transition**: ..., arg0, arg1, ... argN → ..., retVal (if non-void)
- **Operation**: Call method
- **Operands**: `<T:method>` - method token

### Value Type Considerations

**Arguments**:
| Arg Type | Passing Convention |
|----------|-------------------|
| Primitive/ref | By value in register/stack |
| Small struct (≤8) | By value in register/stack |
| Medium struct (9-16) | By value in two registers/stack slots |
| Large struct (>16) | By pointer (caller copies to temp, passes ptr) |

**Returns**:
| Return Type | Convention |
|-------------|------------|
| void | Nothing |
| Primitive | RAX |
| Small struct (≤8) | RAX |
| Medium struct (9-16) | RAX:RDX |
| Large struct (>16) | Hidden buffer (ptr in RCX as arg0) |

### ProtonOS x64 Implementation

**Setup for call with hidden buffer return**:
```x64
; Caller allocates buffer on stack
sub rsp, <struct_size>
; Hidden buffer pointer becomes arg0
lea rcx, [rsp]
; Shift other args: arg0→RDX, arg1→R8, arg2→R9, arg3+→stack
mov rdx, <original_arg0>
mov r8, <original_arg1>
mov r9, <original_arg2>
; Stack args shift by 8 bytes
```

**Call sequence**:
```x64
; Arguments in RCX, RDX, R8, R9 (first 4)
; Additional args on stack (right to left)
; Shadow space already allocated in frame
call <target>
; Return value in RAX (or RAX:RDX for medium struct)
```

**After call with struct return**:
```x64
; For medium struct, push both parts:
push rdx              ; High 8 bytes
push rax              ; Low 8 bytes (will be at [RSP])
; For large struct, value is in buffer at [rsp]
; Just mark the slots as StackType_ValueType
```

---

## 9.2 calli (0x29)

### ECMA-335 Semantics
- **Stack Transition**: ..., arg0, ...argN, ftn → ..., retVal
- **Operation**: Indirect call through function pointer
- **Operands**: `<T:signature>` - call site signature token

### ProtonOS x64 Implementation
```x64
; ftn (function pointer) is in RAX (TOS)
mov r10, rax          ; Save function pointer
; Pop and set up arguments...
call r10              ; Indirect call
```

---

## 9.3 ret (0x2A)

### ECMA-335 Semantics
- **Stack Transition**: retVal → ... (stack must be empty after)
- **Operation**: Return from method
- **Operands**: None

### Value Type Considerations

**Large struct return (>16 bytes)**:
- Hidden buffer pointer was passed in RCX and saved
- Copy return value to buffer
- Return buffer pointer in RAX

### ProtonOS x64 Implementation

**Void return**:
```x64
; Restore callee-saved registers
leave
ret
```

**Value return**:
```x64
; Return value already in RAX (or RAX:RDX)
; Restore callee-saved registers
leave
ret
```

**Large struct return**:
```x64
; Copy struct from stack/local to hidden buffer
mov rdi, [rbp+16]     ; Hidden buffer ptr (was arg0/RCX)
lea rsi, [rbp-localOffset]   ; Source
mov ecx, <struct_size>
rep movsb
mov rax, [rbp+16]     ; Return buffer pointer
; Restore and return
leave
ret
```

---

## 9.4 callvirt (0x6F)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj, arg0, ...argN → ..., retVal
- **Operation**: Virtual method call
- **Operands**: `<T:method>` - method token

### ProtonOS x64 Implementation

**Virtual dispatch**:
```x64
; obj is in RCX (first arg for instance method)
mov rax, [rcx]        ; Load MethodTable pointer from object
mov rax, [rax + vtable_offset + slot*8]  ; Load method from vtable
call rax
```

---

## 9.5 ldftn (0xFE 0x06)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., ftn
- **Operation**: Push function pointer for method
- **Operands**: `<T:method>` - method token

### ProtonOS x64 Implementation
```x64
mov rax, <method_address>
```

---

## 9.6 ldvirtftn (0xFE 0x07)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., ftn
- **Operation**: Push virtual method function pointer
- **Operands**: `<T:method>` - method token

### ProtonOS x64 Implementation
```x64
; obj is TOS (in RAX)
mov rax, [rax]        ; MethodTable
mov rax, [rax + vtable_offset + slot*8]
```

---

# 10. Field Access (0x7B-0x80)

## 10.1 ldfld (0x7B)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., value
- **Operation**: Load instance field value
- **Operands**: `<T:field>` - field token
- **Description**: obj can be object reference, managed pointer, or value type instance

### Value Type Considerations

**Source Types**:
| Source | Stack Representation |
|--------|---------------------|
| Object reference | Single slot (pointer) |
| Managed pointer (&) | Single slot (pointer) |
| Value type (small) | Single slot (value) |
| Value type (medium) | Two slots (value) |
| Value type (large) | N slots (value) |

**Field Types**:
| Field Type | Result |
|------------|--------|
| Primitive | Single slot, extended to 4+ bytes |
| Small struct | Single slot |
| Medium struct | Two slots |
| Large struct | N slots |

### ProtonOS x64 Implementation

**From object reference/pointer**:
```x64
; RAX has object ref or pointer
mov rax, [rax + field_offset]   ; Load field (assumes single slot)
```

**From value type on stack**:
```x64
; Value type is at [RSP], field at offset
mov rax, [rsp + field_offset]
; Pop the source value type (all its slots)
add rsp, source_size
; If field is multi-slot, must copy all bytes
```

**Large field from pointer**:
```x64
; RAX has pointer to struct
; Field is large, must copy to stack
sub rsp, field_size
; Copy loop or unrolled moves
mov rcx, [rax + field_offset]
mov [rsp], rcx
mov rcx, [rax + field_offset + 8]
mov [rsp + 8], rcx
; ... etc
; Mark slots as StackType_ValueType
```

---

## 10.2 ldflda (0x7C)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., address
- **Operation**: Load address of instance field

### Value Type Considerations
- Always pushes single slot (pointer)
- Cannot get address of field in value type on stack (need ldloca first)

### ProtonOS x64 Implementation
```x64
; RAX has object ref or pointer to struct
lea rax, [rax + field_offset]
```

---

## 10.3 stfld (0x7D)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj, value → ...
- **Operation**: Store to instance field

### Value Type Considerations
- value may be multi-slot for struct fields
- Must copy entire value type

### ProtonOS x64 Implementation

**Single slot field**:
```x64
; value in RAX (TOS), obj at [RSP]
pop rcx               ; obj
mov [rcx + field_offset], rax
```

**Multi-slot field**:
```x64
; Multiple slots of value at [RSP], obj below them
; Copy all bytes to destination
```

---

## 10.4-10.6 Static Fields (ldsfld, ldsflda, stsfld)

Similar to instance fields but address is resolved at compile time from static field metadata.

```x64
; ldsfld
mov rax, [<static_address>]

; ldsflda
mov rax, <static_address>

; stsfld
mov [<static_address>], rax
```

---

# 11. Value Type Operations (0x70, 0x71, 0x79, 0x81, 0x8C, 0xA5, 0xFE15)

## 11.1 ldobj (0x71)

### ECMA-335 Semantics
- **Stack Transition**: ..., src → ..., val
- **Operation**: Copy value type from address to stack
- **Operands**: `<T:type>` - type token
- **Description**: src is managed or unmanaged pointer

### Value Type Considerations
- Pushes full value type onto stack
- Number of slots depends on type size

### ProtonOS x64 Implementation
```x64
; RAX has pointer to value type
; For 32-byte struct:
sub rsp, 32
mov rcx, [rax]
mov [rsp], rcx
mov rcx, [rax+8]
mov [rsp+8], rcx
mov rcx, [rax+16]
mov [rsp+16], rcx
mov rcx, [rax+24]
mov [rsp+24], rcx
; TOS is now the struct value
```

---

## 11.2 stobj (0x81)

### ECMA-335 Semantics
- **Stack Transition**: ..., dest, src → ...
- **Operation**: Copy value type from stack to address
- **Operands**: `<T:type>` - type token

### ProtonOS x64 Implementation
```x64
; src (value type) at [RSP], dest pointer below it
; Pop value type slots and copy to destination
```

---

## 11.3 cpobj (0x70)

### ECMA-335 Semantics
- **Stack Transition**: ..., dest, src → ...
- **Operation**: Copy value type from src address to dest address
- **Operands**: `<T:type>` - type token

### ProtonOS x64 Implementation
```x64
; src pointer in RAX, dest at [RSP]
pop rdi               ; dest
mov rsi, rax          ; src
mov ecx, <size>
rep movsb
```

---

## 11.4 initobj (0xFE 0x15)

### ECMA-335 Semantics
- **Stack Transition**: ..., dest → ...
- **Operation**: Initialize value type at address to zero
- **Operands**: `<T:type>` - type token

### ProtonOS x64 Implementation
```x64
; RAX has pointer to value type
mov rdi, rax
xor eax, eax
mov ecx, <size>
rep stosb
```

---

## 11.5 box (0x8C)

### ECMA-335 Semantics
- **Stack Transition**: ..., val → ..., obj
- **Operation**: Box value type (allocate object, copy value)
- **Operands**: `<T:type>` - type token

### Value Type Considerations
- val may be multi-slot
- Result is single slot (object reference)

### ProtonOS x64 Implementation
```x64
; Allocate object with RhpNewObject helper
; Copy value type bytes into object after header
```

---

## 11.6 unbox (0x79)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., valueTypePtr
- **Operation**: Get pointer to boxed value type data
- **Operands**: `<T:type>` - type token
- **Description**: Returns managed pointer, not the value itself

### ProtonOS x64 Implementation
```x64
; RAX has boxed object
add rax, <object_header_size>   ; Skip header to get to data
```

---

## 11.7 unbox.any (0xA5)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., val
- **Operation**: Unbox and copy value
- **Operands**: `<T:type>` - type token

### Value Type Considerations
- Result is actual value (potentially multi-slot)

### ProtonOS x64 Implementation
```x64
; Equivalent to: unbox; ldobj
add rax, <header_size>
; Then ldobj sequence to copy value to stack
```

---

# 12. Array Operations (0x8D-0xA4)

## 12.1 newarr (0x8D)

### ECMA-335 Semantics
- **Stack Transition**: ..., numElems → ..., array
- **Operation**: Allocate zero-initialized array
- **Operands**: `<T:type>` - element type token

### ProtonOS x64 Implementation
```x64
; numElems in RAX
; Call RhpNewArray helper with element type info
```

---

## 12.2 ldlen (0x8E)

### ECMA-335 Semantics
- **Stack Transition**: ..., array → ..., length
- **Operation**: Load array length as native int

### ProtonOS x64 Implementation
```x64
; Array in RAX
mov rax, [rax + length_offset]
```

---

## 12.3 ldelema (0x8F)

### ECMA-335 Semantics
- **Stack Transition**: ..., array, index → ..., address
- **Operation**: Load address of array element
- **Operands**: `<T:type>` - element type token

### ProtonOS x64 Implementation
```x64
; index in RAX, array at [RSP]
pop rcx               ; array
; address = array + header_size + index * element_size
imul rax, <element_size>
lea rax, [rcx + array_header_size + rax]
```

---

## 12.4-12.15 ldelem.* variants

Load element from array by type:

| Opcode | Hex | Type | Extension |
|--------|-----|------|-----------|
| ldelem.i1 | 0x90 | int8 | sign-extend |
| ldelem.u1 | 0x91 | uint8 | zero-extend |
| ldelem.i2 | 0x92 | int16 | sign-extend |
| ldelem.u2 | 0x93 | uint16 | zero-extend |
| ldelem.i4 | 0x94 | int32 | sign-extend |
| ldelem.u4 | 0x95 | uint32 | zero-extend |
| ldelem.i8 | 0x96 | int64 | none |
| ldelem.i | 0x97 | native int | none |
| ldelem.r4 | 0x98 | float32 | to XMM |
| ldelem.r8 | 0x99 | float64 | to XMM |
| ldelem.ref | 0x9A | object ref | none |

### Common Implementation
```x64
; index in RAX, array at [RSP]
pop rcx               ; array
; element_addr = array + header + index * elem_size
imul rax, <elem_size>
mov<type> rax/xmm0, [rcx + header + rax]
```

---

## 12.16 ldelem (0xA3)

### ECMA-335 Semantics
- **Stack Transition**: ..., array, index → ..., value
- **Operation**: Load element with type token
- **Operands**: `<T:type>` - element type token

### Value Type Considerations
- If element is value type, may push multiple slots

---

## 12.17-12.26 stelem.* variants

Similar pattern to ldelem but storing instead of loading.

---

# 13. Object Operations (0x73, 0x74, 0x75)

## 13.1 newobj (0x73)

### ECMA-335 Semantics
- **Stack Transition**: ..., arg0, ...argN → ..., obj
- **Operation**: Allocate object and call constructor
- **Operands**: `<T:method>` - constructor token

### ProtonOS x64 Implementation
```x64
; Allocate object with RhpNewObject
; Push object ref
; Set up constructor args (obj becomes 'this')
; Call constructor
; Result is object ref in RAX
```

---

## 13.2 castclass (0x74)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., obj
- **Operation**: Cast object to type, throw on failure
- **Operands**: `<T:type>` - target type token
- **Exception**: `InvalidCastException` if cast fails

### ProtonOS x64 Implementation
```x64
; Type check using MethodTable comparison
; If fail, call throw helper
```

---

## 13.3 isinst (0x75)

### ECMA-335 Semantics
- **Stack Transition**: ..., obj → ..., result
- **Operation**: Test if object is instance of type
- **Operands**: `<T:type>` - type token
- **Description**: Returns obj if success, null if fail

### ProtonOS x64 Implementation
```x64
; Type check
; If fail, xor eax, eax (return null)
; If success, RAX still has obj
```

---

# 14. Exception Handling (0x7A, 0xDC-0xDE, 0xFE11, 0xFE1A)

## 14.1 throw (0x7A)

### ECMA-335 Semantics
- **Stack Transition**: ..., object → ...
- **Operation**: Throw exception
- **Description**: Stack is emptied, exception object is passed to handler

### ProtonOS x64 Implementation
```x64
; Exception object in RAX
; Call RhpThrowEx helper
; Does not return normally
```

---

## 14.2 rethrow (0xFE 0x1A)

### ECMA-335 Semantics
- **Stack Transition**: ... → ...
- **Operation**: Rethrow current exception
- **Description**: Only valid in catch handler

---

## 14.3 leave / leave.s (0xDD / 0xDE)

### ECMA-335 Semantics
- **Stack Transition**: ... → ... (empties stack)
- **Operation**: Exit protected region
- **Operands**: `<int32>` or `<int8>` offset

### ProtonOS x64 Implementation
```x64
; Clear evaluation stack
; Jump to target (may trigger finally handlers)
jmp <target>
```

---

## 14.4 endfinally / endfault (0xDC)

### ECMA-335 Semantics
- **Stack Transition**: ... → ...
- **Operation**: End finally or fault handler

### ProtonOS x64 Implementation
```x64
; Return from funclet
ret
```

---

## 14.5 endfilter (0xFE 0x11)

### ECMA-335 Semantics
- **Stack Transition**: ..., value → ...
- **Operation**: End filter clause
- **Description**: value indicates whether to handle (1) or continue (0)

---

# 15. Indirect Load/Store (0x46-0x57, 0xDF)

## 15.1-15.11 ldind.* variants (0x46-0x50)

### ECMA-335 Semantics
- **Stack Transition**: ..., addr → ..., value
- **Operation**: Load value indirectly from address

| Opcode | Hex | Type |
|--------|-----|------|
| ldind.i1 | 0x46 | int8 (sign-extend) |
| ldind.u1 | 0x47 | uint8 (zero-extend) |
| ldind.i2 | 0x48 | int16 (sign-extend) |
| ldind.u2 | 0x49 | uint16 (zero-extend) |
| ldind.i4 | 0x4A | int32 |
| ldind.u4 | 0x4B | uint32 |
| ldind.i8 | 0x4C | int64 |
| ldind.i | 0x4D | native int |
| ldind.r4 | 0x4E | float32 |
| ldind.r8 | 0x4F | float64 |
| ldind.ref | 0x50 | object ref |

### ProtonOS x64 Implementation
```x64
; addr in RAX
movsx/movzx/mov rax, [rax]    ; Depends on type
```

---

## 15.12-15.19 stind.* variants (0x51-0x57, 0xDF)

### ECMA-335 Semantics
- **Stack Transition**: ..., addr, val → ...
- **Operation**: Store value indirectly to address

### ProtonOS x64 Implementation
```x64
; val in RAX, addr at [RSP]
pop rcx               ; addr
mov [rcx], al/ax/eax/rax      ; Depends on type
```

---

# 16. Block Operations (0xFE15-0xFE18, 0xFE1C)

## 16.1 cpblk (0xFE 0x17)

### ECMA-335 Semantics
- **Stack Transition**: ..., dest, src, size → ...
- **Operation**: Copy block of bytes

### ProtonOS x64 Implementation
```x64
; size in RAX, src at [RSP], dest at [RSP+8]
mov rcx, rax          ; count
pop rsi               ; src
pop rdi               ; dest
rep movsb
```

---

## 16.2 initblk (0xFE 0x18)

### ECMA-335 Semantics
- **Stack Transition**: ..., addr, value, size → ...
- **Operation**: Initialize block with byte value

### ProtonOS x64 Implementation
```x64
; size in RAX, value at [RSP], addr at [RSP+8]
mov rcx, rax          ; count
pop rax               ; value (use AL)
pop rdi               ; addr
rep stosb
```

---

## 16.3 sizeof (0xFE 0x1C)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., size
- **Operation**: Push size of value type
- **Operands**: `<T:type>` - type token

### ProtonOS x64 Implementation
```x64
mov eax, <type_size>  ; Resolved at compile time
```

---

## 16.4 localloc (0xFE 0x0F)

### ECMA-335 Semantics
- **Stack Transition**: ..., size → ..., address
- **Operation**: Allocate space on stack
- **Description**: Memory is uninitialized, aligned to native int

### ProtonOS x64 Implementation
```x64
; size in RAX
; Round up to alignment
add rax, 15
and rax, -16          ; 16-byte align
sub rsp, rax          ; Allocate
mov rax, rsp          ; Return address
```

---

# 17. Miscellaneous

## 17.1 ldstr (0x72)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., string
- **Operation**: Push string object reference
- **Operands**: `<T:string>` - string token

### ProtonOS x64 Implementation
```x64
; Call StringResolver to get/create string object
mov rax, <string_object_address>
```

---

## 17.2 ldtoken (0xD0)

### ECMA-335 Semantics
- **Stack Transition**: ... → ..., RuntimeHandle
- **Operation**: Push runtime handle
- **Operands**: `<T:token>` - metadata token (type, method, or field)

---

## 17.3 Prefix Opcodes

These modify the following instruction:

| Prefix | Hex | Effect |
|--------|-----|--------|
| volatile. | 0xFE 0x13 | Memory access is volatile |
| unaligned. | 0xFE 0x12 | Following access may be unaligned |
| tail. | 0xFE 0x14 | Following call is tail call |
| constrained. | 0xFE 0x16 | Constrained virtual call |
| readonly. | 0xFE 0x1E | Array address is readonly |
| no. | 0xFE 0x19 | No typecheck/rangecheck/nullcheck |

In naive JIT, most of these are no-ops.

---

# Appendix A: Opcode Quick Reference

| Opcode | Hex | Stack Effect | Category |
|--------|-----|--------------|----------|
| nop | 00 | - | Control |
| break | 01 | - | Debug |
| ldarg.0 | 02 | → val | Args |
| ldarg.1 | 03 | → val | Args |
| ldarg.2 | 04 | → val | Args |
| ldarg.3 | 05 | → val | Args |
| ldloc.0 | 06 | → val | Locals |
| ldloc.1 | 07 | → val | Locals |
| ldloc.2 | 08 | → val | Locals |
| ldloc.3 | 09 | → val | Locals |
| stloc.0 | 0A | val → | Locals |
| stloc.1 | 0B | val → | Locals |
| stloc.2 | 0C | val → | Locals |
| stloc.3 | 0D | val → | Locals |
| ldarg.s | 0E | → val | Args |
| ldarga.s | 0F | → addr | Args |
| starg.s | 10 | val → | Args |
| ldloc.s | 11 | → val | Locals |
| ldloca.s | 12 | → addr | Locals |
| stloc.s | 13 | val → | Locals |
| ldnull | 14 | → null | Constants |
| ldc.i4.m1 | 15 | → -1 | Constants |
| ldc.i4.0 | 16 | → 0 | Constants |
| ldc.i4.1 | 17 | → 1 | Constants |
| ldc.i4.2 | 18 | → 2 | Constants |
| ldc.i4.3 | 19 | → 3 | Constants |
| ldc.i4.4 | 1A | → 4 | Constants |
| ldc.i4.5 | 1B | → 5 | Constants |
| ldc.i4.6 | 1C | → 6 | Constants |
| ldc.i4.7 | 1D | → 7 | Constants |
| ldc.i4.8 | 1E | → 8 | Constants |
| ldc.i4.s | 1F | → n | Constants |
| ldc.i4 | 20 | → n | Constants |
| ldc.i8 | 21 | → n | Constants |
| ldc.r4 | 22 | → n | Constants |
| ldc.r8 | 23 | → n | Constants |
| dup | 25 | v → v,v | Stack |
| pop | 26 | v → | Stack |
| jmp | 27 | → | Control |
| call | 28 | args → ret | Call |
| calli | 29 | args,ftn → ret | Call |
| ret | 2A | ret → | Control |
| br.s | 2B | - | Branch |
| brfalse.s | 2C | v → | Branch |
| brtrue.s | 2D | v → | Branch |
| beq.s | 2E | v,v → | Branch |
| bge.s | 2F | v,v → | Branch |
| bgt.s | 30 | v,v → | Branch |
| ble.s | 31 | v,v → | Branch |
| blt.s | 32 | v,v → | Branch |
| bne.un.s | 33 | v,v → | Branch |
| bge.un.s | 34 | v,v → | Branch |
| bgt.un.s | 35 | v,v → | Branch |
| ble.un.s | 36 | v,v → | Branch |
| blt.un.s | 37 | v,v → | Branch |
| br | 38 | - | Branch |
| brfalse | 39 | v → | Branch |
| brtrue | 3A | v → | Branch |
| beq | 3B | v,v → | Branch |
| bge | 3C | v,v → | Branch |
| bgt | 3D | v,v → | Branch |
| ble | 3E | v,v → | Branch |
| blt | 3F | v,v → | Branch |
| bne.un | 40 | v,v → | Branch |
| bge.un | 41 | v,v → | Branch |
| bgt.un | 42 | v,v → | Branch |
| ble.un | 43 | v,v → | Branch |
| blt.un | 44 | v,v → | Branch |
| switch | 45 | v → | Branch |
| ldind.i1 | 46 | addr → val | Indirect |
| ldind.u1 | 47 | addr → val | Indirect |
| ldind.i2 | 48 | addr → val | Indirect |
| ldind.u2 | 49 | addr → val | Indirect |
| ldind.i4 | 4A | addr → val | Indirect |
| ldind.u4 | 4B | addr → val | Indirect |
| ldind.i8 | 4C | addr → val | Indirect |
| ldind.i | 4D | addr → val | Indirect |
| ldind.r4 | 4E | addr → val | Indirect |
| ldind.r8 | 4F | addr → val | Indirect |
| ldind.ref | 50 | addr → val | Indirect |
| stind.ref | 51 | addr,val → | Indirect |
| stind.i1 | 52 | addr,val → | Indirect |
| stind.i2 | 53 | addr,val → | Indirect |
| stind.i4 | 54 | addr,val → | Indirect |
| stind.i8 | 55 | addr,val → | Indirect |
| stind.r4 | 56 | addr,val → | Indirect |
| stind.r8 | 57 | addr,val → | Indirect |
| add | 58 | v,v → v | Arith |
| sub | 59 | v,v → v | Arith |
| mul | 5A | v,v → v | Arith |
| div | 5B | v,v → v | Arith |
| div.un | 5C | v,v → v | Arith |
| rem | 5D | v,v → v | Arith |
| rem.un | 5E | v,v → v | Arith |
| and | 5F | v,v → v | Arith |
| or | 60 | v,v → v | Arith |
| xor | 61 | v,v → v | Arith |
| shl | 62 | v,v → v | Arith |
| shr | 63 | v,v → v | Arith |
| shr.un | 64 | v,v → v | Arith |
| neg | 65 | v → v | Arith |
| not | 66 | v → v | Arith |
| conv.i1 | 67 | v → v | Conv |
| conv.i2 | 68 | v → v | Conv |
| conv.i4 | 69 | v → v | Conv |
| conv.i8 | 6A | v → v | Conv |
| conv.r4 | 6B | v → v | Conv |
| conv.r8 | 6C | v → v | Conv |
| conv.u4 | 6D | v → v | Conv |
| conv.u8 | 6E | v → v | Conv |
| callvirt | 6F | obj,args → ret | Call |
| cpobj | 70 | dest,src → | VT |
| ldobj | 71 | src → val | VT |
| ldstr | 72 | → str | Object |
| newobj | 73 | args → obj | Object |
| castclass | 74 | obj → obj | Object |
| isinst | 75 | obj → obj/null | Object |
| conv.r.un | 76 | v → v | Conv |
| unbox | 79 | obj → ptr | VT |
| throw | 7A | obj → | EH |
| ldfld | 7B | obj → val | Field |
| ldflda | 7C | obj → addr | Field |
| stfld | 7D | obj,val → | Field |
| ldsfld | 7E | → val | Field |
| ldsflda | 7F | → addr | Field |
| stsfld | 80 | val → | Field |
| stobj | 81 | dest,src → | VT |
| conv.ovf.* | B3-BA | v → v | Conv |
| box | 8C | val → obj | VT |
| newarr | 8D | n → arr | Array |
| ldlen | 8E | arr → len | Array |
| ldelema | 8F | arr,i → addr | Array |
| ldelem.* | 90-9A | arr,i → val | Array |
| stelem.* | 9B-A2 | arr,i,val → | Array |
| ldelem | A3 | arr,i → val | Array |
| stelem | A4 | arr,i,val → | Array |
| unbox.any | A5 | obj → val | VT |
| conv.u2 | D1 | v → v | Conv |
| conv.u1 | D2 | v → v | Conv |
| conv.i | D3 | v → v | Conv |
| conv.ovf.i | D4 | v → v | Conv |
| conv.ovf.u | D5 | v → v | Conv |
| add.ovf | D6 | v,v → v | Arith |
| add.ovf.un | D7 | v,v → v | Arith |
| mul.ovf | D8 | v,v → v | Arith |
| mul.ovf.un | D9 | v,v → v | Arith |
| sub.ovf | DA | v,v → v | Arith |
| sub.ovf.un | DB | v,v → v | Arith |
| endfinally | DC | - | EH |
| leave | DD | - | EH |
| leave.s | DE | - | EH |
| stind.i | DF | addr,val → | Indirect |
| conv.u | E0 | v → v | Conv |
| (Two-byte opcodes follow 0xFE prefix) |

---

*Document generated for ProtonOS JIT - see /home/shane/protonos/src/kernel/Runtime/JIT/ for implementation*
