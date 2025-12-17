# Dictionary JIT Bug Investigation

## Status: UNRESOLVED - Dictionary tests disabled

## Problem Summary

When `Dictionary<string, int>.Add()` is called, the JIT-compiled code crashes with a null pointer write (CR2=0). The crash occurs during `entries[index].hashCode = hashCode` in the Insert method.

## Reproduction

1. Enable `CollectionTests.TestDictAddAndCount` in `src/FullTest/Program.cs:253`
2. Build and run: `./build.sh && ./run.sh`
3. Crash occurs after "TestListToArray ... PASS"

## Key Observations

### 1. The Crash Location

```
RIP: 0x00000002003F38D9  (JIT code heap)
CR2: 0x0000000000000000  (writing to address 0)
```

Disassembly at crash:
```asm
0x2003f38b8: push   %rax             ; Push element address (from ldelema)
0x2003f38b9: sub    $0x18,%rsp       ; Allocate 24 bytes (Entry struct size)
0x2003f38bd: mov    -0x80(%rbp),%rax ; Load struct bytes 0-7 from local
0x2003f38c1: mov    %rax,(%rsp)
0x2003f38c5: mov    -0x78(%rbp),%rax ; Load struct bytes 8-15
0x2003f38c9: mov    %rax,0x8(%rsp)
0x2003f38ce: mov    -0x70(%rbp),%rax ; Load struct bytes 16-23
0x2003f38d2: mov    %rax,0x10(%rsp)
0x2003f38d7: pop    %rdx             ; Pop bytes 0-7 into RDX
0x2003f38d8: pop    %rax             ; Pop bytes 8-15 into RAX
0x2003f38d9: mov    %edx,(%rax)      ; CRASH: Store to [RAX] where RAX=0
```

### 2. What's Happening

The code is doing:
1. Computing element address via `ldelema Entry` (imul by 24, add array offset)
2. Pushing element address to stack
3. Loading a 24-byte Entry struct from local variables onto the stack
4. Popping only 16 bytes (2 pops) into RDX/RAX
5. Trying to store EDX (first 4 bytes of struct = hashCode) to [RAX] (bytes 8-15 of struct = key pointer = 0)

The element address was pushed but never retrieved - it's still on the stack at RSP+8 after the two pops.

### 3. Expected Behavior

For `entries[index].hashCode = hashCode`, the IL should be:
```
ldarg.0 / ldloc entries  ; get entries array
ldloc index              ; get index
ldelema Entry            ; get managed pointer to entries[index]
ldloc hashCode           ; load the value to store (4 bytes)
stfld hashCode           ; store to field at offset 0
```

The JIT should generate:
```asm
; ldelema - compute &entries[index]
; ...
push element_address
; ldloc hashCode (4 bytes)
push hashCode_value
; stfld hashCode
pop rdx          ; value
pop rax          ; element address
mov [rax+0], edx ; store
```

### 4. Actual Behavior

The JIT is generating code that loads an entire 24-byte Entry struct (ldloc entry) instead of just the 4-byte hashCode value. This suggests:

- Either the IL has wrong local variable index
- Or the JIT is misidentifying which local to load
- Or there's an eval stack tracking bug

## Dictionary.Entry Structure

```csharp
internal struct Entry  // 24 bytes total
{
    public int hashCode;   // offset 0, 4 bytes
    public int next;       // offset 4, 4 bytes
    public TKey key;       // offset 8, 8 bytes (reference for string)
    public TValue value;   // offset 16, 4 bytes (int)
    // padding            // offset 20, 4 bytes
}
```

## Files Involved

- `src/korlib/System/Collections/Generic/Dictionary.cs` - Lines 302-305 do the entry stores
- `src/kernel/Runtime/JIT/ILCompiler.cs` - JIT compilation
  - `CompileLdloc()` around line 2267 - loads local variables
  - `CompileStfld()` around line 7416 - stores to fields
  - `CompileLdelema()` - computes array element addresses

## Debug Helpers Issue (Separate)

The debug helpers (`_debugStfld`, etc.) were causing a separate crash because function pointers obtained via `&Method` were pointing 16 bytes before actual code. These have been disabled in ILCompiler.cs:756-762.

## Theories to Investigate

1. **Local variable index confusion**: The JIT might be using wrong local index, loading Entry struct (local 4) instead of hashCode (local 5 or wherever).

2. **IL stream corruption/misparse**: The token after stfld might be parsed incorrectly.

3. **Eval stack type tracking**: The eval stack might not correctly track that ldelema produces a managed pointer vs ldloc producing a value.

4. **stfld not handling managed pointers**: CompileStfld might expect object reference but get managed pointer from ldelema.

## How to Debug

1. **Dump the IL for Dictionary.Insert**:
   ```bash
   grep -A 200 "::Insert\|::TryInsert" build/x64/FullTest.il
   ```
   Look for the stfld sequence for hashCode.

2. **Add JIT debug output**:
   In `CompileLdloc()`, add:
   ```csharp
   DebugConsole.Write("[ldloc] idx=");
   DebugConsole.WriteDecimal((uint)index);
   DebugConsole.Write(" size=");
   DebugConsole.WriteDecimal((uint)typeSize);
   DebugConsole.WriteLine();
   ```

3. **Trace the IL being compiled**:
   The method being JIT'd is likely `Dictionary<string,int>.Insert`. Add IL offset tracing.

4. **Use GDB**:
   ```bash
   # Start QEMU with -s -S
   # In GDB:
   target remote :1234
   # Set breakpoint at crash address
   b *0x2003f38d9
   c
   # Examine registers and stack
   info registers
   x/20xg $rsp
   ```

## Test Status

- All 492 non-Dictionary tests pass
- Dictionary test disabled in `src/FullTest/Program.cs:253`

## Session Notes

Multiple approaches tried without success:
- Investigated vtable dispatch (not the issue)
- Fixed debug helper function pointer bug (separate issue)
- Traced through disassembly to understand crash pattern
- The core issue is JIT generating wrong ldloc (24-byte struct instead of 4-byte int)
