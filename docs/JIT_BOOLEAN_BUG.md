# JIT Boolean Return Value Bug Analysis

## Problem Summary

There is a bug in the ProtonOS Tier0 JIT compiler where boolean return values from `callvirt` instructions are not correctly branched upon. Despite the called method returning `true` (verified via debug output), the subsequent `brfalse` (or `brtrue`) instruction behaves incorrectly.

## Observed Behavior

### Runtime Log Output
```
...D00D0009...D00D000A...FACE0000...DEAD0001[Drivers]   Bind failed
```

- `D00D000A` is printed inside `VirtioDevice.Initialize()` right before `return true;`
- `FACE0000` is printed immediately after the call in the caller, showing the stored local is false
- `DEAD0001` is printed in the caller when the false branch is taken
- This proves the method returns `true` but the branch instruction acts as if it returned `false`

### C# Code (Caller)

**File**: `/home/shane/protonos/src/drivers/shared/storage/virtio-blk/VirtioBlkEntry.cs` lines 95-125

```csharp
// Create and initialize the virtio block device
_device = new VirtioBlkDevice();
Debug.WriteHex(0xBEEF0001u); // Created VirtioBlkDevice

// Initialize virtio device (handles modern/legacy detection, feature negotiation)
// Cast to VirtioDevice to ensure we call the base method, not the IDriver.Initialize()
// WORKAROUND: Use while loop pattern with local variable to avoid JIT branch bugs
// The issue is brtrue after callvirt+cgt.un. while(!x) uses brfalse on local load.
bool initResult = ((VirtioDevice)_device).Initialize(pciDevice);
while (!initResult)
{
    Debug.WriteHex(0xDEAD0001u);
    _device = null;
    return false;
}

Debug.WriteHex(0xBEEF0003u); // Initialize succeeded

// Initialize block-specific functionality
bool blkResult = _device.InitializeBlockDevice();
while (!blkResult)
{
    Debug.WriteHex(0xDEAD0002u);
    _device.Dispose();
    _device = null;
    return false;
}

Debug.WriteHex(0x00010000u); // Success marker
return true;
```

### C# Code (Callee)

**File**: `/home/shane/protonos/src/drivers/shared/virtio/VirtioDevice.cs` lines 155-162

```csharp
// Allocate queue array
_queues = new Virtqueue[_numQueues];
Debug.WriteHex(0xD00D0009u); // After queue array allocation

_initialized = true;
Debug.WriteHex(0xD00D000Au); // Initialized
return true;
```

## Actual IL Sequence (from ildasm)

The method `VirtioBlkEntry::Bind` has the following relevant IL sequence:

```
IL_0117: ldsfld    class VirtioBlkDevice VirtioBlkEntry::_device
IL_011c: ldloc.0   // pciDevice (PciDeviceInfo)
IL_011d: callvirt  instance bool VirtioDevice::Initialize(PciDeviceInfo)
IL_0122: stloc.s   V_4  // Store result to local 'initResult' (bool)
IL_0124: br.s      IL_0138  // Unconditional jump to loop check

// FAILURE PATH:
IL_0126: ldc.i4    -559087615  // 0xDEAD0001
IL_012b: call      Debug::WriteHex(uint32)
IL_0130: ldnull
IL_0131: stsfld    VirtioBlkEntry::_device
IL_0136: ldc.i4.0
IL_0137: ret

// LOOP CHECK (while (!initResult)):
IL_0138: ldloc.s   V_4          // Load local 'initResult'
IL_013a: brfalse.s IL_0126      // If false (0), jump to failure path

// SUCCESS PATH:
IL_013c: ldc.i4    -1091633149  // 0xBEEF0003
IL_0141: call      Debug::WriteHex(uint32)
// ... continues
```

Note: `V_4` is local variable index 4 (bool type).
Note: `.locals init(... bool V_4, bool V_5, ...)` shows V_4 is at index 4.

## Latest Findings (Dec 9)

- The call site in `VirtioBlkEntry.Bind` calls `VirtioDevice.Initialize` (MethodDef 0x06000006) at native address `0x0000000200011000`. This method is a tiny dispatcher: it stores `_pciDevice`, evaluates `IsLegacyDevice`, and tail-calls either legacy or modern init.
- JIT code locations:
  - `VirtioDevice.Initialize` dispatcher: `0x0000000200011000`
  - `InitializeLegacy` stub (returns false): `0x0000000200013000`
  - `InitializeModern` full implementation: `0x0000000200014000` and its epilogue explicitly sets `mov eax,1` before `ret`.
- IL for dispatcher (46 bytes):
  - `stfld _pciDevice`
  - `ldarg.1` → `ldfld DeviceId` → `call IsLegacyDevice`
  - `stfld _isLegacy`
  - `ldfld _isLegacy` → `brtrue.s legacy`
  - `ldarg.0` → `call InitializeModern`
  - `ret`
  - `legacy:` `ldarg.0` → `call InitializeLegacy` → `ret`
- Disassembled dispatcher shows the modern path returning the callee’s result, and `InitializeModern`’s code ends with `mov eax,1` followed by epilogue, so the callee does produce `1`.
- Despite the above, the caller still sees `RAX==0` after the `callvirt` to the dispatcher and takes the false branch (`FACE0000`/`DEAD0001`). That means the `callvirt`/dispatcher path is losing the boolean return somewhere between `InitializeModern`’s `ret` and the branch in `Bind`.

## JIT Compiler Code Analysis

### 1. CompileCallvirt - Return Value Handling

**File**: `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` lines 4878-4913

```csharp
// Handle return value (same as CompileCall)
switch (method.ReturnKind)
{
    case ReturnKind.Void:
        break;

    case ReturnKind.Int32:
        _code.EmitByte(0x48);  // REX.W
        _code.EmitByte(0x63);  // MOVSXD
        _code.EmitByte(0xC0);  // ModRM: RAX, EAX
        X64Emitter.Push(ref _code, VReg.R0);
        _evalStackDepth++;
        break;
    // ... other cases
}
```

**Issue**: After `callvirt`, the JIT:
1. Sign-extends EAX to RAX with MOVSXD (correct for Int32)
2. Pushes RAX to memory stack
3. Increments `_evalStackDepth`
4. Does NOT set `_tosCached = true` or call `MarkR0AsTOS()`

This means the value is pushed to memory, and TOS caching is not active.

### 2. CompileStloc - Storing to Local

**File**: `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` lines 1983-1988

```csharp
// Pop from eval stack, store to local
// Uses TOS caching - if value is in R0, no pop needed
PopToR0();
int offset = X64Emitter.GetLocalOffset(index);
X64Emitter.MovMR(ref _code, VReg.FP, offset, VReg.R0);
return true;
```

**Generated code**:
```asm
POP     RAX                    ; PopToR0() generates this since _tosCached is false
MOV     [RBP+offset], RAX      ; Store to local
```

### 3. CompileLdloc - Loading from Local

**File**: `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` lines 1911-1918

```csharp
else
{
    // Primitive or reference type: load VALUE directly
    X64Emitter.MovRM(ref _code, VReg.R0, VReg.FP, offset);
    stackType = StackType_Int;
}

MarkR0AsTOS(stackType);
return true;
```

**Generated code**:
```asm
MOV     RAX, [RBP+offset]      ; Load local to R0
; MarkR0AsTOS sets _tosCached = true
```

### 4. CompileBrfalse - Branch If False

**File**: `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` lines 3843-3850

```csharp
private bool CompileBrfalse(int targetIL)
{
    // Pop value, branch if zero
    PopToR0();  // Uses TOS cache if available
    X64Emitter.TestRR(ref _code, VReg.R0, VReg.R0);
    int patchOffset = X64Emitter.Je(ref _code);  // Jump if equal (to zero)
    RecordBranch(_ilOffset, targetIL, patchOffset);
    return true;
}
```

Since `_tosCached` was set true by `MarkR0AsTOS` in `CompileLdloc`, `PopToR0()` should be a no-op (value already in R0).

**Expected generated code**:
```asm
; PopToR0() - no code since _tosCached is true
TEST    RAX, RAX               ; Test if zero
JE      target                 ; Jump if zero
```

### 5. PopToR0 - Pop with TOS Caching

**File**: `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs` lines 2190-2210

```csharp
private void PopToR0()
{
    PopStackType();
    if (_tosIsConst)
    {
        // Constant TOS: materialize to R0
        MaterializeConstToR0();
        _tosCached = false;
    }
    else if (_tosCached)
    {
        // Value is already in R0 - no code needed!
        _tosCached = false;
    }
    else
    {
        X64Emitter.Pop(ref _code, VReg.R0);
    }
    // Promote TOS2 to TOS
    PromoteTOS2();
}
```

## Potential Bug Locations

### Hypothesis 1: TOS Cache State Corruption

The `while (!initResult)` compiles to:
1. `ldloc` → sets `_tosCached = true`
2. `brfalse` → uses `PopToR0()` which clears `_tosCached`

But what if between `ldloc` and `brfalse`, something clears `_tosCached`?

Check: Is there any code between these that calls `SpillTOSIfCached()` or `FlushTOS()`?

### Hypothesis 2: Register Clobbering

After `ldloc` puts the value in RAX and marks `_tosCached = true`, is there any instruction that modifies RAX before `brfalse`?

The `while` loop might generate additional branch setup code that modifies registers.

### Hypothesis 3: Incorrect Branch Target Patching

Check `RecordBranch()` and branch patching - maybe the branch is patched to the wrong target offset.

### Hypothesis 4: Local Variable Size/Type Issue

Boolean locals might be stored incorrectly. The local might be:
- Stored as 8 bytes (correct for 64-bit stack slot)
- But loaded incorrectly?

Check `_localTypeSize[]` for boolean locals and how `MovRM` handles different sizes.

### Hypothesis 5: MOVSXD Sign Extension Issue

For a boolean `true` (value 1):
- EAX = 0x00000001
- MOVSXD RAX, EAX → RAX = 0x0000000000000001 (correct)

But if EAX has garbage in upper bits from previous operations:
- EAX = 0xFFFFFF01 (hypothetically)
- MOVSXD RAX, EAX → RAX = 0xFFFFFFFFFFFFFF01 (wrong!)

The callee might not be zeroing upper EAX bits before returning.

## Key Files to Examine

1. `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs`
   - `CompileCallvirt` (line ~4660)
   - `CompileStloc` (line ~1922)
   - `CompileLdloc` (line ~1872)
   - `CompileBrfalse` (line ~3843)
   - `PopToR0` (line ~2190)
   - `MarkR0AsTOS` (line ~2134)

2. `/home/shane/protonos/src/kernel/Runtime/JIT/X64Emitter.cs`
   - Check `MovRM`, `MovMR`, `Push`, `Pop` implementations
   - Check that TEST and JE are emitted correctly

3. `/home/shane/protonos/src/drivers/shared/virtio/VirtioDevice.cs`
   - The `Initialize()` method that returns bool

4. `/home/shane/protonos/src/drivers/shared/storage/virtio-blk/VirtioBlkEntry.cs`
   - The caller code with the `while (!initResult)` pattern

## Debug Suggestions

1. **Add JIT debug output**: In `CompileStloc`, `CompileLdloc`, and `CompileBrfalse`, add debug hex markers to trace the generated code paths.

2. **Dump generated machine code**: Before executing, dump the raw bytes of the compiled method and disassemble them.

3. **Check local variable allocation**: Print `_localTypeSize[]` and `_localIsValueType[]` for the method being compiled.

4. **Verify RAX value**: Add inline machine code to print RAX value after `ldloc` and before `brfalse`.

## Expected Machine Code Generation

For the IL sequence at IL_011d through IL_013a, the JIT should generate approximately:

```
; IL_011d: callvirt VirtioDevice::Initialize
CALL    [target_address]
; Return value in EAX (0 or 1 for bool)
MOVSXD  RAX, EAX           ; Sign-extend Int32 to Int64
PUSH    RAX                ; Push to eval stack

; IL_0122: stloc.s V_4
POP     RAX                ; Pop from stack
MOV     [RBP-offset], RAX  ; Store to local V_4 (offset = GetLocalOffset(4))

; IL_0124: br.s IL_0138 (unconditional jump)
JMP     <label_IL_0138>

; ...failure code at IL_0126...

label_IL_0138:
; IL_0138: ldloc.s V_4
MOV     RAX, [RBP-offset]  ; Load from local V_4

; IL_013a: brfalse.s IL_0126
TEST    RAX, RAX           ; Test if zero
JE      <label_IL_0126>    ; Jump to failure if zero
```

## Potential Bug Analysis

The key question is: **Why does JE branch to failure when RAX should contain 1?**

### Theory 1: Local Variable Offset Calculation

The local variable `V_4` is at index 4. Check `GetLocalOffset(4)`:
- Is the offset calculated correctly?
- Are there overlapping locals?
- Is the stack frame large enough?

The method has 8 locals: `V_0` through `V_7`. Check `.locals init`:
```
.locals init(class PciDeviceInfo V_0, uint32 V_1, uint32 V_2, int32 V_3,
             bool V_4, bool V_5, PciBar V_6, PciBar V_7)
```

Note: V_6 and V_7 are `PciBar` which is a 40-byte struct. This could affect layout.

### Theory 2: Stack Corruption During callvirt

The `callvirt` may be:
1. Corrupting the stack frame
2. Not properly restoring callee-saved registers
3. The return value in RAX may be corrupted before `stloc.s`

### Theory 3: br.s Patching Issue

The `br.s IL_0138` jump at IL_0124 might:
1. Jump to the wrong address
2. Have an off-by-one in the target calculation
3. Actually skip over important code

### Theory 4: ldloc.s Index Wrong

The `stloc.s V_4` and `ldloc.s V_4` should use the same local index.
But what if one uses 4 and the other uses a different index due to:
1. `stloc.s` opcode parsing error
2. `ldloc.s` opcode parsing error
3. Short vs long form handling issue

### Theory 5: The Value is Being Stored Correctly but Branch Target is Wrong

The `brfalse.s IL_0126` might:
1. Have incorrect relative offset calculation
2. Jump to wrong location regardless of condition

## Debug Approach

1. **Add inline debug at stloc.s**:
   After `POP RAX; MOV [RBP-offset], RAX`, emit:
   ```
   PUSH RAX
   MOV  ECX, EAX        ; Arg1 = value being stored
   CALL Debug::WriteHex
   POP  RAX
   ```

2. **Add inline debug at ldloc.s**:
   After `MOV RAX, [RBP-offset]`, emit debug output.

3. **Dump raw machine code**:
   In the JIT, after compilation completes, dump the raw bytes and offsets.

4. **Verify branch targets**:
   Print each branch patch: IL offset → native offset mapping.

## Workaround Attempts (All Failed)

1. **Direct `if` pattern**: `if (!initResult) { ... }` - same bug
2. **`while` loop pattern**: `while (!initResult) { ... }` - same bug
3. **Separate variable**: Store result in local variable first - same bug

All patterns compile to similar IL sequences and all fail the same way.

## Additional Context

- The JIT passes 98 other tests successfully
- The issue is specific to boolean returns from `callvirt` instructions
- Simple boolean expressions and comparisons work correctly
- The issue appears to be in how the boolean value flows through:
  callvirt → push → pop → stloc → ldloc → brfalse/brtrue
- Added a regression in FullTest (`InstanceTests.TestCallvirtBoolBranchWithLargeLocals`) that mirrors the driver local layout; it passes. This suggests the bug is specific to the driver call site or codegen for that method.
- Instrumentation shows `VirtioDevice.Initialize` is MethodDef token 0x06000006 in assembly 3, resolved with `ReturnKind=Int32` and `argCount=1` (MemberRef trace also reports ReturnKind=1), so signature/ReturnKind resolution is correct.
- Despite correct ReturnKind, the local immediately after the call contains 0 (`FACE0000` trace) even though the callee logged true (`D00D000A`), pointing to the call/return value handling or stloc/ldloc/brfalse path for this method.

## Key Files to Investigate

| File | Purpose | Key Functions |
|------|---------|---------------|
| `ILCompiler.cs` | Main JIT compiler | `CompileCallvirt`, `CompileStloc`, `CompileLdloc`, `CompileBrfalse`, `CompileBr` |
| `X64Emitter.cs` | x86-64 code generation | `MovRM`, `MovMR`, `Push`, `Pop`, `TestRR`, `Je` |
| `VirtioBlkEntry.cs` | Driver code with bug | `Bind` method (lines 45-125) |
| `VirtioDevice.cs` | Called method | `Initialize` method (returns bool) |

## To Reproduce

1. Build: `./build.sh`
2. Run: `./dev.sh ./run.sh 2>&1 | tee /tmp/run.log`
3. Search for: `DEAD0001` following `D00D000A` in the output
4. The sequence `D00D000A...DEAD0001` proves Initialize returned true but brfalse took the false path
