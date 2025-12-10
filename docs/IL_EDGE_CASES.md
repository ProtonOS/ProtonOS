# IL Opcode Edge Cases - ProtonOS JIT

This document catalogs known edge cases, past bugs, and areas requiring special attention in the JIT compiler. Each entry includes the root cause, fix, and verification approach.

---

## 1. Large Struct `dup` - FIXED

**Date Fixed**: 2025-12-09

### Symptom
- Page fault with CR2 = small address (e.g., 0x00000001)
- Corruption reading fields from structs > 8 bytes
- Debug output shows correct pointer access but wrong `ldfld` access

### Root Cause
The `dup` opcode only copied 8 bytes regardless of value type size.

**Original Code**:
```csharp
private bool CompileDup()
{
    // BROKEN: Only duplicates 8 bytes!
    X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);
    X64Emitter.Push(ref _code, VReg.R0);
    PushStackType(PeekStackType());
    return true;
}
```

**Problem Sequence**:
```il
ldfld _descBuffer     ; Push 32-byte struct (4 slots)
dup                   ; BROKEN: Only duplicates 8 bytes (1 slot)
ldfld PhysicalAddress ; Consumes 32 bytes (4 slots) - STACK NOW MISALIGNED!
```

### Fix
Count consecutive `StackType_ValueType` entries and copy all slots:

```csharp
private bool CompileDup()
{
    int vtSlots = 0;
    for (int i = _evalStackDepth - 1; i >= 0 && _evalStackTypes[i] == StackType_ValueType; i--)
        vtSlots++;

    if (vtSlots > 1)
    {
        int byteSize = vtSlots * 8;
        X64Emitter.SubRI(ref _code, VReg.SP, byteSize);
        for (int i = 0; i < vtSlots; i++)
        {
            int offset = i * 8;
            X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, byteSize + offset);
            X64Emitter.MovMR(ref _code, VReg.SP, offset, VReg.R0);
        }
        for (int i = 0; i < vtSlots; i++)
            PushStackType(StackType_ValueType);
    }
    else
    {
        // Original single-slot behavior
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(PeekStackType());
    }
    return true;
}
```

### Verification
Test with 32-byte struct:
```csharp
DMABuffer buf = _descBuffer;
Debug.WriteHex(buf.PhysicalAddress);  // Uses dup + ldfld
Debug.WriteHex(buf.VirtualAddress);   // Uses dup + ldfld
// Both should return correct values
```

### Related
`pop` needed the same fix - must discard all slots of multi-slot value types.

---

## 2. Large Struct `pop` - FIXED

**Date Fixed**: 2025-12-09

### Root Cause
Same as `dup` - only discarded 8 bytes.

### Fix
```csharp
private bool CompilePop()
{
    int vtSlots = 0;
    for (int i = _evalStackDepth - 1; i >= 0 && _evalStackTypes[i] == StackType_ValueType; i--)
        vtSlots++;

    if (vtSlots > 1)
    {
        // Multi-slot value type: discard all slots
        X64Emitter.AddRI(ref _code, VReg.SP, vtSlots * 8);
        for (int i = 0; i < vtSlots; i++)
            PopStackType();
    }
    else
    {
        // Single slot: just bump RSP
        // Note: This naive JIT doesn't use TOS register caching, so values
        // are always on the stack. No FlushTOS needed.
        X64Emitter.AddRI(ref _code, VReg.SP, 8);
        PopStackType();
    }
    return true;
}
```

---

## 3. Large Struct Return Convention - IMPLEMENTED

**Status**: Correctly uses 16-byte threshold per x64 ABI

### Current Behavior (Correct)
```csharp
// In ILCompiler.cs:902
if (_returnIsValueType && _returnTypeSize > 16)  // Correct threshold
    physicalArgCount = _argCount + 1;
```

**Note**: Tier0JIT.cs has a debug log at `> 8` for visibility, but the actual
hidden buffer logic triggers at `> 16` as required.

### Correct Behavior (x64 ABI)

| Return Size | Convention |
|-------------|------------|
| ≤8 bytes | RAX |
| 9-16 bytes | RAX:RDX |
| >16 bytes | Hidden buffer (ptr in RCX) |

### Medium Return (9-16 bytes) - NEEDS IMPLEMENTATION

**ILCompiler.cs** (emission):
```csharp
// For 9-16 byte returns:
// - Caller expects result in RAX:RDX
// - Push both to eval stack as 2 slots

// After call:
if (returnSize > 8 && returnSize <= 16)
{
    // Result in RAX (low) and RDX (high)
    X64Emitter.Push(ref _code, VReg.R0);  // RAX
    X64Emitter.Push(ref _code, VReg.R2);  // RDX
    PushStackType(StackType_ValueType);
    PushStackType(StackType_ValueType);
}
```

### Verification
```csharp
struct Size12 { int a, b, c; }

Size12 GetSize12() { return new Size12 { a=1, b=2, c=3 }; }

var s = GetSize12();
// Verify s.a == 1, s.b == 2, s.c == 3
```

---

## 4. `ldarg` for Large Struct Arguments - NEEDS AUDIT

### Current Understanding
Large struct arguments (>16 bytes) are passed by pointer per x64 ABI.

### Potential Issue
When caller loads a large struct arg with `ldarg`, does it:
1. Load the **pointer** (correct for ABI)
2. Load the **value** (incorrect - would read wrong data)

### Expected Behavior
```il
ldarg.0           ; For >16 byte struct: Push POINTER (ST:I)
ldobj StructType  ; Dereference pointer, push VALUE (ST:VT×N)
```

### Current Implementation
Needs verification that `CompileLdarg` correctly returns pointer for large structs.

### Verification
```csharp
void TakeLargeStruct(LargeStruct s)
{
    // Access s.field - should work correctly
}
```

---

## 5. `ldfld` on Multi-Slot Value Type Source - NEEDS AUDIT

### Scenario
Stack has a large value type (multiple slots), and we access a field.

```il
ldloc.0           ; Push 48-byte struct (6 slots)
ldfld SomeField   ; Access field within struct
```

### Expected Behavior
1. Calculate field offset within the struct at [RSP]
2. Load field value (may be multi-slot itself)
3. Pop the **entire** source struct (6 slots)
4. Push the field value

### Potential Issue
Does `CompileLdfld` correctly:
1. Consume all source slots (not just 1)?
2. Handle large field types?

### Current Implementation
Located at `ILCompiler.cs:5267-5573`. The code appears to handle this case but needs verification.

### Verification
```csharp
struct Outer { Inner inner; int x; }
struct Inner { long a, b, c, d; }  // 32 bytes

Outer o = GetOuter();
long val = o.inner.a;  // ldloc.0; ldfld inner; ldfld a
// Verify stack is correctly managed
```

---

## 6. Call with Large Struct Argument - NEEDS AUDIT

### x64 ABI Rule
Structs >16 bytes passed by pointer. Caller:
1. Copies struct to temp location
2. Passes pointer to temp

### Expected Behavior
```il
; Passing 48-byte struct to method
ldloc.0           ; Push struct value (6 slots)
call Method       ; Should convert to pointer call
```

### Potential Issue
Does `CompileCall` correctly:
1. Detect large struct argument
2. Copy struct to temp location
3. Pass pointer instead of value
4. Clean up correctly after call

### Current Implementation
Needs verification of argument handling in `CompileCall`.

---

## 7. `stloc` with Large Struct - WORKS

### Current Behavior
`CompileStloc` correctly copies all bytes from multi-slot stack to local.

### Implementation
```csharp
if (vtSlots > 0)
{
    // Copy all slots from stack to local
    for (int i = 0; i < vtSlots; i++)
    {
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, i * 8);
        X64Emitter.MovMR(ref _code, VReg.BP, _localOffset[index] + i * 8, VReg.R0);
    }
    X64Emitter.AddRI(ref _code, VReg.SP, vtSlots * 8);
    // Pop stack types...
}
```

---

## 8. Branch Target Stack Depth - WORKS

### Background
When a branch targets an instruction, the stack depth at that point must match.

### Current Implementation
`RecordBranch()` and `FindBranchTargetDepth()` track expected stack depth at branch targets.

### Potential Issue
Does this correctly handle value types? Stack depth is counted in **slots**, so a 4-slot value type should add 4 to depth.

### Verification
```csharp
LargeStruct s;
if (condition)
    s = GetOne();
else
    s = GetOther();
// Stack depth should be consistent at merge point
```

---

## 9. Exception Handler Entry - NEEDS AUDIT

### Background
When entering a catch handler, the exception object is on the stack.

### Potential Issue
If exception is a value type (rare but possible with constrained generics), is it handled correctly?

### Note
For most cases, exceptions are reference types (object ref = 1 slot), so this is low priority.

---

## 10. `initobj` on Stack - WORKS

### Current Behavior
`initobj` zeros memory at a pointer. Used for value type initialization.

```csharp
X64Emitter.Mov(ref _code, VReg.R7, VReg.R0);  // RDI = dest
X64Emitter.Xor(ref _code, VReg.R0, VReg.R0);  // AL = 0
X64Emitter.MovRI32(ref _code, VReg.R1, typeSize);  // RCX = count
X64Emitter.RepStosb(ref _code);  // Fill with zeros
```

---

## 11. Boolean Return Truncation - FIXED (2025-12-08)

### Symptom
Boolean return values sometimes had garbage in upper bits.

### Root Cause
C# `bool` is 1 byte, but stack slots are 8 bytes. Return value in RAX might have garbage in bits 8-63.

### Fix
Ensure boolean returns are zero-extended:
```csharp
// After loading boolean result
movzx eax, al  ; Zero-extend to clear upper bits
```

---

## 12. Float/Double in TOS Cache - DESIGN DECISION

### Current Behavior
TOS caching only works for integer/pointer values (RAX). Floats use separate XMM tracking.

### Rationale
- Integer TOS is very common
- Float TOS would require separate XMM tracking
- Keeps naive JIT simple

### Implementation
`_tosType` tracks whether TOS is int, float32, or float64. Float values are always in XMM0, not RAX.

---

## 13. Tail Call Optimization - NOT IMPLEMENTED

### Current State
`tail.` prefix is recognized but ignored. Normal call is emitted.

### Reason
Tail call optimization is complex:
- Must verify signatures match
- Must handle different stack layouts
- Not critical for correctness

### Future Work
Could implement for recursive methods to avoid stack overflow.

---

## 14. Unaligned Access - HANDLED BY x64

### Current State
`unaligned.` prefix is recognized but treated as no-op.

### Reason
x64 handles unaligned access in hardware. Performance penalty exists but no correctness issue.

---

## 15. Volatile Access - HANDLED BY NAIVE JIT

### Current State
`volatile.` prefix is recognized but treated as no-op.

### Reason
Naive JIT doesn't reorder memory accesses, so volatile semantics are naturally preserved.

### Future Work
When adding optimization passes, must respect volatile markers.

---

## Debugging Checklist

When investigating a JIT bug:

1. **Check struct sizes**: Is it a large struct (>8 or >16 bytes)?

2. **Check stack depth**: Add debug output for `_evalStackDepth` and `_evalStackTypes[]`

3. **Compare pointer vs value access**:
   ```csharp
   // Direct pointer access
   byte* ptr = (byte*)&myStruct;
   Debug.WriteHex(*(long*)ptr);

   // IL access via ldfld
   Debug.WriteHex(myStruct.SomeField);
   // If these differ, suspect stack corruption
   ```

4. **Check IL with ildasm**:
   ```bash
   ./dev.sh dotnet ildasm build/x64/YourAssembly.dll -o build/x64/output.il
   ```

5. **Look for problematic patterns**:
   - `dup` before `ldfld` on large struct
   - Multiple `ldfld` on same large struct
   - Call returning large struct followed by field access

6. **Add hex dump**: Enable code dump in Tier0JIT for problematic methods

7. **Trace stack types**: Log stack type array before/after each opcode

---

## Test Cases for Edge Cases

### Large Struct Dup
```csharp
struct Large { long a, b, c, d; }  // 32 bytes
Large GetLarge() { ... }
var l = GetLarge();
var a = l.a;  // dup + ldfld
var b = l.b;  // dup + ldfld
var c = l.c;  // dup + ldfld
var d = l.d;  // ldfld (no dup)
```

### Medium Struct Return (9-16 bytes)
```csharp
struct Medium { long a; int b; }  // 12 bytes
Medium GetMedium() { ... }
var m = GetMedium();
// Verify RAX:RDX convention
```

### Large Struct Argument
```csharp
struct Large { long a, b, c, d; }
void TakeLarge(Large l) { ... }
Large l = new Large { a=1, b=2, c=3, d=4 };
TakeLarge(l);  // Should pass by pointer
```

### Nested Struct Field Access
```csharp
struct Outer { Inner inner; }
struct Inner { long a, b, c, d; }
Outer o = GetOuter();
long val = o.inner.a;  // Multiple ldfld
```

---

*Reference Implementation: /home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs*
