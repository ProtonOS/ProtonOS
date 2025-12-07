# VirtioBlk JIT Page Fault Debug

## Current Status (Latest)
All four JIT bugs have been fixed. The kernel now continues successfully after driver binding.
VirtioBlk driver's Bind() returns false (driver not fully implemented), but no crashes occur.

### Bug 1: Array ComponentSize (FIXED)
**Problem:** Array allocations for struct element types were undersized.
- `PciBar[] Bars = new PciBar[6]` allocated only 24 bytes instead of 112 bytes
- Root cause: `newarr` used element type's MethodTable which had ComponentSize=0
- Array needs its own MethodTable with ComponentSize = element size

**Fix:** Added `GetOrCreateArrayMethodTable` in AssemblyLoader.cs:
- Creates array MethodTables on-demand with correct ComponentSize
- For value types: ComponentSize = BaseSize - 8 (removes boxed MT pointer overhead)
- For reference types: ComponentSize = 8 (pointer size)

**Result:** Arrays now allocate correctly (112 bytes for PciBar[6])

### Bug 2: Instance Method 'this' Homing (FIXED)
**Problem:** Constructors with no explicit params had null 'this' pointer.
- Signature's `ParamCount` doesn't include 'this', but instance methods have 'this' in RCX as arg0
- JIT used `ParamCount` directly for `_argCount`, so `HomeArguments` wasn't called
- RCX was never saved to [RBP+16], so `ldarg.0` loaded garbage

**Fix:** Modified Tier0JIT.cs:
```csharp
int jitArgCount = hasThis ? paramCount + 1 : paramCount;
// Pass jitArgCount to ILCompiler (includes 'this')
// Store paramCount to registry (for CompileNewobj which needs user args only)
```

**Result:** `this` is now properly homed and `ldarg.0` works correctly.

### Bug 3: Value Type Field Offset (FIXED)
**Problem:** Value type field offsets included +8 for MethodTable pointer.
- Field resolver always added +8 to offsets (for boxed object's MT pointer)
- But value type instance methods receive `this` as byref to raw struct data (no MT)
- PciAddress fields got offsets 8, 9, 10 instead of 0, 1, 2

**Fix:** Modified MetadataIntegration.cs:
- Added `IsTypeDefValueType(uint typeDefRow)` helper function
- Modified `CalculateFieldOffset()` to start at 0 for value types
- Modified `ResolveFieldDef()` to NOT add +8 for explicit offsets on value types

```csharp
// Before: int offset = 8;  // Always started at 8
// After:
bool isValueType = IsTypeDefValueType(typeRow);
int offset = isValueType ? 0 : 8;  // 0 for value types, 8 for reference types
```

**Result:** PciAddress fields now correctly use offsets 0, 1, 2:
```
[JIT stfld] token=0x0400030F offset=0 size=1  // Was offset=8
[JIT stfld] token=0x04000310 offset=1 size=1  // Was offset=9
[JIT stfld] token=0x04000311 offset=2 size=1  // Was offset=10
```

### Bug 4: Page Fault at CR2=0x0 (FIXED)
**Problem:** Two related issues caused crashes after JIT code returned to AOT caller.

**Part A: Local Variable Slot Size**
- Local variable slots were only 8 bytes each
- PciBar struct is ~32 bytes, overflowing into saved RBP/return address
- Writing to struct fields at offset >= 8 corrupted the stack frame

**Fix A:** Changed local slot size from 8 to 64 bytes in ILCompiler.cs and X64Emitter.cs:
```csharp
// ILCompiler.cs line 793
int localBytes = _localCount * 64 + 64;  // Was * 8

// X64Emitter.cs GetLocalOffset
return -((localIndex + 1) * 64);  // Was * 8
```

**Part B: Callee-Saved Register Corruption**
- JIT prologue only saved RBP, not other callee-saved registers
- VRegs R7-R11 map to RBX, R12, R13, R14, R15 (callee-saved)
- JIT code used these without saving/restoring them
- After JIT returned, AOT kernel had corrupted register values

**Fix B:** Added callee-saved register preservation in X64Emitter.cs:
```csharp
// Prologue: Save RBX, R12, R13, R14, R15 to [RBP-8] through [RBP-40]
// Epilogue: Restore them before leave/ret
```

**Result:** Kernel now continues successfully after driver binding:
```
[Drivers]   Bind failed
[OK] Kernel initialization complete
```

---

## Historical Context

### Original Problem
VirtioBlk driver crashed at CR2=0x13 (offset 19 = ProgIf field) - null 'this' pointer.

### C# Code Pattern (VirtioBlkEntry.cs)
```csharp
public static bool Bind(byte bus, byte device, byte function)
{
    var pciDevice = new PciDeviceInfo
    {
        Address = new PciAddress(bus, device, function)
    };
    // Field accesses crashed because pciDevice was null
    pciDevice.VendorId = ...;
    pciDevice.ProgIf = ...;  // <-- Original crash at CR2=0x13
}
```

### IL Pattern for Object Initializer
```
newobj      PciDeviceInfo::.ctor()    // Inside ctor: allocates Bars array
dup                                    // Duplicate ref for stloc later
ldarg.0                                // Push bus
ldarg.1                                // Push device
ldarg.2                                // Push function
newobj      PciAddress::.ctor(byte, byte, byte)  // Nested newobj
stfld       PciDeviceInfo::Address    // Store to field
stloc.0                                // Store ref to local
```

### PciDeviceInfo Constructor
The constructor has a field initializer that allocates an array:
```csharp
public PciBar[] Bars = new PciBar[6];
```
This was causing the undersized allocation bug (now fixed).

---

## Technical Details

### JIT Eval Stack
- RSP-based evaluation stack
- TOS (Top of Stack) caching: last value may be in R0 instead of on RSP stack
- `FlushTOS()` spills R0 to RSP when direct stack manipulation is needed

### VReg Mappings
- R0 = RAX (result register)
- R1 = RCX (1st arg / 'this')
- R2 = RDX (2nd arg)
- R7 = RBX, R8 = R12, R9 = R13, R10 = R14 (callee-saved temps)
- SP = RSP, FP = RBP

### Microsoft x64 ABI
- First 4 args: RCX, RDX, R8, R9
- Caller reserves 32-byte shadow space
- Callee homes args to shadow space: [RBP+16], [RBP+24], [RBP+32], [RBP+40]

### Argument Homing
For instance methods, `this` is arg0:
- arg0 (this): RCX -> [RBP+16]
- arg1: RDX -> [RBP+24]
- arg2: R8 -> [RBP+32]
- arg3: R9 -> [RBP+40]

---

## Files Modified

1. **src/kernel/Runtime/AssemblyLoader.cs**
   - Added `GetOrCreateArrayMethodTable` function
   - Creates array MTs with correct ComponentSize
   - Caches created array MTs to avoid duplication

2. **src/kernel/Runtime/JIT/MetadataIntegration.cs**
   - Added `ResolveArrayElementType` function
   - Bridges element type tokens to array MethodTables
   - Added `IsTypeDefValueType(uint typeDefRow)` helper function
   - Modified `CalculateFieldOffset()` to use 0 base for value types
   - Modified `ResolveFieldDef()` explicit offset handling for value types

3. **src/kernel/Runtime/JIT/ILCompiler.cs**
   - Modified `CompileNewarr` to use `ResolveArrayElementType`
   - Falls back to old resolver if new one fails

4. **src/kernel/Runtime/JIT/Tier0JIT.cs**
   - Separated `paramCount` (signature's count) from `jitArgCount` (includes 'this')
   - Pass `jitArgCount` to ILCompiler for homing
   - Pass `paramCount` to registry for CompileNewobj

---

## Next Steps to Debug CR2=0x0

### 1. Analyze the stfld trace
The runtime stfld trace shows successful stores before crash:
```
[stfld RT] obj=0x000000001FE665B0 off=0   <- PciAddress field Bus
[stfld RT] obj=0x000000001FE665B0 off=1   <- PciAddress field Device
[stfld RT] obj=0x000000001FE665B0 off=2   <- PciAddress field Function
[stfld RT] obj=0xFFFF800000100188 off=8   <- PciDeviceInfo.Address (ref type)
[stfld RT] obj=0x000000001FE66538 off=0   <- ???
[stfld RT] obj=0x000000001FE66538 off=8   <- ???
!!! EXCEPTION 000E: Page Fault at CR2=0x0
```

### 2. Investigate what objects are being accessed
- 0x1FE665B0 appears to be a stack address (PciAddress value type)
- 0xFFFF800000100188 is in the GC heap (managed object)
- 0x1FE66538 appears to be another stack address

### 3. Check for null object references
The CR2=0x0 means we're dereferencing a null pointer. Possible causes:
- Eval stack underflow returning 0
- Uninitialized local variable
- Bad object reference from constructor

---

## Build & Test Commands
```bash
./build.sh 2>&1          # Build (timeout: 120000)
./dev.sh ./run.sh 2>&1   # Run in QEMU (timeout: 10000)
./kill.sh                # Kill containers after test
```
