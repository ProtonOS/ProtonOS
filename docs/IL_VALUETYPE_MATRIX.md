# IL Value Type Handling Matrix - ProtonOS JIT

This document provides a comprehensive matrix of how each IL opcode handles value types of different sizes. This is critical for avoiding stack corruption bugs.

## Size Categories (x64 ABI Aligned)

| Category | Size | Stack Slots | Return Convention | Passing Convention |
|----------|------|-------------|-------------------|-------------------|
| **Small** | 1-8 bytes | 1 | RAX | Register or stack |
| **Medium** | 9-16 bytes | 2 | RAX:RDX | Two registers or stack |
| **Large** | >16 bytes | N = ceil(size/8) | Hidden buffer (RCX) | By pointer |

## Stack Type Markers

The JIT tracks what's in each evaluation stack slot:

```csharp
const byte StackType_Int = 0;        // Integer, pointer, object ref (8 bytes)
const byte StackType_Float32 = 1;    // Single-precision float
const byte StackType_Float64 = 2;    // Double-precision float
const byte StackType_ValueType = 3;  // Part of a value type (may span multiple slots)
```

**Multi-slot value types**: When a struct occupies N slots, all N entries in `_evalStackTypes[]` are marked `StackType_ValueType`. The JIT counts consecutive `StackType_ValueType` entries to determine the total size.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| **V** | Value (actual bytes on stack) |
| **P** | Pointer/address (single 8-byte slot) |
| **1S** | Single 8-byte slot |
| **2S** | Two 8-byte slots |
| **NS** | N slots (N = ceil(size/8)) |
| **HB** | Hidden buffer convention |
| **ST:I** | StackType_Int |
| **ST:VT** | StackType_ValueType |
| **N/A** | Not applicable |

---

## Opcode Matrix

### Local Variable Operations

| Opcode | Primitive | Small VT (≤8) | Medium VT (9-16) | Large VT (>16) |
|--------|-----------|---------------|------------------|----------------|
| **ldloc** | V, 1S, ST:I | V, 1S, ST:VT | V, 2S, ST:VT×2 | V, NS, ST:VT×N |
| **stloc** | Pop 1S | Pop 1S | Pop 2S | Pop NS |
| **ldloca** | P, 1S, ST:I | P, 1S, ST:I | P, 1S, ST:I | P, 1S, ST:I |

**Key Points**:
- `ldloc` always loads the **actual value** onto the stack (unlike ldarg for large structs)
- `ldloca` always pushes a **pointer** regardless of the local's type
- Locals are stored directly in the stack frame, never passed by pointer

### Argument Operations

| Opcode | Primitive | Small VT (≤8) | Medium VT (9-16) | Large VT (>16) |
|--------|-----------|---------------|------------------|----------------|
| **ldarg** | V, 1S, ST:I | V, 1S, ST:VT | V, 2S, ST:VT×2 | **P**, 1S, ST:I |
| **starg** | Pop 1S | Pop 1S | Pop 2S | Copy via ptr |
| **ldarga** | P, 1S, ST:I | P, 1S, ST:I | P, 1S, ST:I | P to ptr slot |

**CRITICAL**: For large struct arguments (>16 bytes), the x64 ABI passes a **pointer**. `ldarg` loads that pointer, NOT the value. To get the value, use `ldarg` + `ldobj`.

```
; Large struct arg handling:
ldarg.0           ; Pushes POINTER to struct (ST:I)
ldobj StructType  ; Dereferences pointer, pushes VALUE (ST:VT×N)
```

### Field Operations

| Opcode | Source | Primitive Field | Small VT Field | Medium VT Field | Large VT Field |
|--------|--------|-----------------|----------------|-----------------|----------------|
| **ldfld** | Obj ref (1S) | V, 1S | V, 1S | V, 2S | V, NS |
| **ldfld** | Ptr (1S) | V, 1S | V, 1S | V, 2S | V, NS |
| **ldfld** | Small VT val (1S) | V, 1S | V, 1S | V, 2S | V, NS |
| **ldfld** | Medium VT val (2S) | Pop 2S, Push 1S | Pop 2S, Push 1S | Pop 2S, Push 2S | Pop 2S, Push NS |
| **ldfld** | Large VT val (NS) | Pop NS, Push 1S | Pop NS, Push 1S | Pop NS, Push 2S | Pop NS, Push NS |
| **ldflda** | Obj ref/Ptr (1S) | P, 1S | P, 1S | P, 1S | P, 1S |
| **stfld** | Obj ref (1S) | Pop 1S+1S | Pop 1S+1S | Pop 1S+2S | Pop 1S+NS |

**Key Points**:
- Source consumption varies by source type
- Field value size determines what gets pushed
- `ldflda` always returns a pointer (single slot)
- Cannot get address of field in value type on stack (need `ldloca` first)

**IL Semantics Clarification**:

While `ldfld` *can* consume a value type directly from the stack (rows 3-5 above), this is
unusual in practice. The common patterns are:

1. **Pointer-based access** (typical): `ldloca.s 0; ldfld MyField`
   - Load address of local, then access field through pointer
   - Source is a managed pointer (ST:I), single slot consumed

2. **Object reference access** (for reference types): `ldarg.0; ldfld MyField`
   - Load `this` reference, access instance field
   - Source is object reference (ST:I), single slot consumed

3. **Direct value-type access** (rare): `ldloc.0; ldfld MyField`
   - Loads entire value type to stack, then extracts field
   - Source is value on stack (ST:VT×N), all N slots consumed
   - Compiler typically avoids this pattern for large structs due to inefficiency

**Test Expectation**: When writing tests for ldfld, focus on patterns 1 and 2. Pattern 3
primarily occurs with small value types where the extra copy is acceptable.

### Value Type Operations

| Opcode | Input | Output | Notes |
|--------|-------|--------|-------|
| **ldobj** | P (1S) | V (size-dependent) | Dereferences pointer, copies value to stack |
| **stobj** | P + V | - | Copies value from stack to memory via pointer |
| **cpobj** | P(dest) + P(src) | - | Copies between two memory locations |
| **initobj** | P (1S) | - | Zeros memory at pointer |
| **box** | V (NS) | O (1S) | Allocates, copies value, returns obj ref |
| **unbox** | O (1S) | P (1S) | Returns pointer to boxed data (not value!) |
| **unbox.any** | O (1S) | V (size-dependent) | Returns actual value (equivalent to unbox + ldobj) |

**ldobj Expansion by Size**:
| Type Size | Slots Pushed | Stack Types |
|-----------|--------------|-------------|
| ≤8 bytes | 1 | ST:VT |
| 9-16 bytes | 2 | ST:VT, ST:VT |
| 17-24 bytes | 3 | ST:VT×3 |
| etc. | ceil(size/8) | ST:VT×N |

### Stack Operations

| Opcode | Small (1S) | Medium (2S) | Large (NS) |
|--------|------------|-------------|------------|
| **dup** | Push 1S | Push 2S | Push NS |
| **pop** | Discard 1S | Discard 2S | Discard NS |

**CRITICAL BUG HISTORY**: The original `dup` only copied 8 bytes. For large structs, this caused:
1. Only first 8 bytes duplicated
2. `ldfld` on "duplicated" struct consumed full N slots
3. Stack became misaligned by (N-1)*8 bytes
4. Subsequent operations read wrong memory

**Correct Implementation**:
```csharp
// Count consecutive StackType_ValueType entries
int vtSlots = 0;
for (int i = _evalStackDepth - 1; i >= 0 && _evalStackTypes[i] == StackType_ValueType; i--)
    vtSlots++;

if (vtSlots > 1) {
    // Multi-slot: duplicate ALL slots
    int byteSize = vtSlots * 8;
    // ... copy all bytes
}
```

### Method Calls

| Opcode | Arg: Small VT | Arg: Medium VT | Arg: Large VT |
|--------|---------------|----------------|---------------|
| **call** | Pass by value | Pass by value | Pass by pointer |
| **calli** | Pass by value | Pass by value | Pass by pointer |
| **callvirt** | Pass by value | Pass by value | Pass by pointer |

**Return Values**:
| Return Type | Convention | Stack After Call |
|-------------|------------|------------------|
| void | - | Nothing pushed |
| Primitive | RAX | 1S, ST:I |
| Small VT (≤8) | RAX | 1S, ST:VT |
| Medium VT (9-16) | RAX:RDX | 2S, ST:VT×2 |
| Large VT (>16) | Hidden buffer | NS, ST:VT×N |

**Hidden Buffer Protocol**:
```
; Caller setup for large struct return:
sub rsp, structSize           ; Allocate buffer
lea rcx, [rsp]                ; Hidden buffer ptr = arg0
mov rdx, <original_arg0>      ; Shift other args
mov r8, <original_arg1>
mov r9, <original_arg2>
; Stack args also shift by 8 bytes

; Callee return:
mov rdi, [rbp+16]             ; Get hidden buffer ptr (was RCX)
; ... copy return value to buffer ...
mov rax, [rbp+16]             ; Return buffer ptr in RAX
```

### Array Operations

| Opcode | Element: Primitive | Element: Small VT | Element: Large VT |
|--------|-------------------|-------------------|-------------------|
| **ldelem** | V, 1S | V, 1S | V, NS |
| **stelem** | Pop arr+idx+1S | Pop arr+idx+1S | Pop arr+idx+NS |
| **ldelema** | P, 1S | P, 1S | P, 1S |

**Note**: `ldelem` with large struct elements must copy the entire struct to the evaluation stack.

### Type Operations

| Opcode | Input | Output | Notes |
|--------|-------|--------|-------|
| **sizeof** | - | 4-byte int | Compile-time constant |
| **castclass** | O (1S) | O (1S) | Type check, throws on failure |
| **isinst** | O (1S) | O or null (1S) | Type test |
| **newobj** | args | O (1S) | For value types, creates boxed instance |

---

## Common Bug Patterns

### Bug 1: Single-Slot Assumption

**Symptom**: Stack corruption when processing large structs

**Cause**: Opcode implementation assumes TOS is always 1 slot

**Affected Operations**:
- `dup` - must copy all slots
- `pop` - must discard all slots
- `ldfld` on VT - must consume correct number of source slots
- `stfld` - must consume correct number of value slots
- `call` args - must pass all slots or convert to pointer

**Fix Pattern**:
```csharp
// Count value type slots
int slots = 1;
if (_evalStackTypes[_evalStackDepth - 1] == StackType_ValueType) {
    slots = 0;
    for (int i = _evalStackDepth - 1;
         i >= 0 && _evalStackTypes[i] == StackType_ValueType;
         i--)
        slots++;
}
```

### Bug 2: Pointer vs Value Confusion

**Symptom**: Null dereference or garbage data

**Cause**: Treating pointer as value or vice versa

**Common Cases**:
- `ldarg` of large struct returns **pointer**, not value
- `ldloca`/`ldarga`/`ldflda` return **pointer**
- `unbox` returns **pointer**, not value (use `unbox.any` for value)

**Fix**: Check stack type - `ST:I` for pointers, `ST:VT` for values

### Bug 3: Return Value Slot Mismatch

**Symptom**: Return value corrupted or stack misaligned

**Cause**: Wrong number of slots pushed for return value

**Medium Struct Return**:
```csharp
// After call returning 9-16 byte struct:
// RAX has low 8 bytes, RDX has high bytes
Push(RAX);   // Slot 1
Push(RDX);   // Slot 2
PushStackType(StackType_ValueType);
PushStackType(StackType_ValueType);
```

**Large Struct Return**:
```csharp
// After call with hidden buffer:
// RAX has buffer pointer, buffer has data
// Copy buffer to stack slots
for (int i = 0; i < numSlots; i++) {
    mov rax, [buffer + i*8]
    push rax
    PushStackType(StackType_ValueType);
}
```

### Bug 4: Arg Shifting with Hidden Buffer

**Symptom**: Wrong argument values in callee

**Cause**: Forgot to shift args when hidden buffer uses RCX

**Correct Arg Mapping with Large Return**:
| Logical Arg | Physical Location |
|-------------|-------------------|
| (hidden buffer) | RCX |
| arg0 | RDX |
| arg1 | R8 |
| arg2 | R9 |
| arg3 | [RSP+32] |
| arg4 | [RSP+40] |

---

## Decision Tree for Value Type Handling

```
Is it a value type?
├── No → Single slot (ST:I), 8 bytes
└── Yes → What's the size?
    ├── ≤8 bytes → Single slot (ST:VT)
    ├── 9-16 bytes → Two slots (ST:VT×2)
    └── >16 bytes → N slots (ST:VT×N) where N = ceil(size/8)
        └── Is this an argument?
            ├── Yes → Passed as pointer (ldarg gives pointer, not value)
            └── No → Stored/loaded as full value

Is this a return value?
├── void → Nothing
├── ≤8 bytes → RAX, push 1 slot
├── 9-16 bytes → RAX:RDX, push 2 slots
└── >16 bytes → Hidden buffer
    └── Caller allocates buffer, passes ptr in RCX
    └── Callee fills buffer, returns ptr in RAX
    └── Caller: buffer content is now on stack

Is this an address operation (ldloca, ldarga, ldflda, ldelema)?
└── Always single slot containing pointer (ST:I)
```

---

## Implementation Checklist

For each opcode that can involve value types, verify:

- [ ] Correct slot count consumed from stack
- [ ] Correct slot count pushed to stack
- [ ] Correct stack type markers set
- [ ] Large struct arguments handled as pointers
- [ ] Large struct returns use hidden buffer
- [ ] Medium struct returns use RAX:RDX
- [ ] `dup` copies all slots
- [ ] `pop` discards all slots
- [ ] Address operations return pointer (ST:I), not value

---

*Reference: /home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs*
