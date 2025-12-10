# JIT Large Value Type Stack Operations Bug

## Date Fixed: 2025-12-09

## Symptoms

- Page fault with CR2 = 0x0000000000000001 or similar small addresses
- Corruption when reading fields from structs > 8 bytes
- Debug output shows correct values via pointer access but wrong values via `ldfld` access
- Example: `DMABuffer` (32-byte struct) fields returning garbage after being loaded with `ldfld`

## Root Cause

The `dup` and `pop` IL opcodes only operated on single 8-byte stack slots, but large value types (structs > 8 bytes) occupy multiple slots on the eval stack.

### How Large Structs Are Stored

When `ldloc` or `ldfld` loads a struct > 8 bytes onto the eval stack:
- The struct data is spread across multiple 8-byte slots
- Each slot is tracked with `StackType_ValueType` in the type tracking array
- Example: 32-byte `DMABuffer` uses 4 slots, each marked `StackType_ValueType`

### The Bug

The C# compiler often generates `dup` to access multiple fields of a struct:

```il
ldfld _descBuffer     ; Push 32-byte struct (4 slots)
dup                   ; Duplicate for first field access
ldfld PhysicalAddress ; Read field, clean up 32 bytes
dup                   ; Duplicate for second field access
ldfld VirtualAddress  ; Read field, clean up 32 bytes
...
```

The old `CompileDup()` implementation:
```csharp
// BROKEN: Only duplicates 8 bytes!
X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);
X64Emitter.Push(ref _code, VReg.R0);
PushStackType(PeekStackType());
```

After the first `ldfld`:
1. `dup` pushed only 8 bytes (first qword of struct)
2. `ldfld` cleaned up 32 bytes (the full struct size)
3. Stack was now misaligned by 24 bytes
4. Subsequent operations read from wrong locations

## The Fix

Modified `CompileDup()` and `CompilePop()` in `ILCompiler.cs` to detect multi-slot value types by counting consecutive `StackType_ValueType` entries:

```csharp
private bool CompileDup()
{
    // Count consecutive StackType_ValueType entries
    int vtSlots = 0;
    for (int i = _evalStackDepth - 1; i >= 0 && _evalStackTypes[i] == StackType_ValueType; i--)
        vtSlots++;

    if (vtSlots > 1)
    {
        // Multi-slot value type: duplicate all slots
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
        // Single slot: original behavior
        X64Emitter.MovRM(ref _code, VReg.R0, VReg.SP, 0);
        X64Emitter.Push(ref _code, VReg.R0);
        PushStackType(PeekStackType());
    }
    return true;
}
```

Similar fix for `CompilePop()` to discard all slots of a multi-slot value type.

## How to Debug Similar Issues

1. **Compare pointer vs ldfld access**: If pointer access works but ldfld doesn't, suspect stack corruption
2. **Check struct size**: Issues appear with structs > 8 bytes (multi-slot)
3. **Look at IL**: Use `dotnet ildasm` to see if `dup` is used before `ldfld` on structs
4. **Check stack type tracking**: Add debug output for `_evalStackDepth` and `_evalStackTypes[]`
5. **Trace stack layout**: The struct at `[SP]` should have byte 0 at `[SP+0]`, byte 8 at `[SP+8]`, etc.

## Files Modified

- `src/kernel/Runtime/JIT/ILCompiler.cs`: `CompileDup()`, `CompilePop()`

## Related Operations That May Need Similar Treatment

If other opcodes assume single-slot values, they may have similar bugs:
- `starg` / `ldarg` for large struct parameters passed by value
- Any operation that manipulates stack entries without considering multi-slot values

## Test Case

```csharp
DMABuffer buf = _descBuffer;  // 32-byte struct
Debug.WriteHex(buf.PhysicalAddress);  // Uses dup + ldfld
Debug.WriteHex(buf.VirtualAddress);   // Uses dup + ldfld again
```

Before fix: Second field access returns garbage
After fix: Both accesses return correct values
