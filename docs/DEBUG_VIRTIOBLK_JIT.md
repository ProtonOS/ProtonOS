# VirtioBlk JIT Page Fault Debug

## Current Status (2025-12-07)

**ACTIVELY DEBUGGING: Bug 5 - ldfld on value types from ldloc**

A page fault at CR2=0x300 occurs during VirtioBlk driver initialization. The crash happens when JIT-compiled code attempts to access a field of a struct that was loaded with `ldloc`.

### Current Crash Details
```
BEEF0001-BEEF0008  <- VirtioBlkEntry.Bind progressing
CAFE0001, CAFE0002, CAFE0003  <- VirtioDevice.Initialize starts
CAFE0010, CAFE0011, CAFE0012  <- VirtioDevice.InitializeModern progressing
!!! EXCEPTION 000E: Page Fault
    Error Code: 0x0000000000000000
    CR2: 0x0000000000000300  <- FAULT ADDRESS
```

### Root Cause Analysis
The JIT has a bug in `CompileLdfld` when accessing fields of value types loaded via `ldloc`:

**The Problem:**
When C# compiles `localStruct.Field`, it emits:
1. `ldloc localStruct` - Loads the struct VALUE onto the eval stack
2. `ldfld Field` - Should access field FROM the struct value on stack

But the JIT's `CompileLdfld` implementation treats the value on the stack as a POINTER and dereferences it:
```csharp
// ILCompiler.cs CompileLdfld (around line 5100-5150)
_emitter.Mov(VReg.R0, VReg.SP, 0);  // Load "address" from stack
_emitter.Mov(VReg.R0, VReg.R0, offset);  // MOV R0, [R0 + offset] - CRASH!
```

**Why CR2=0x300?**
- PciAddress struct = 3 bytes: Bus=0x00, Device=0x03, Function=0x00
- When `ldloc` loads this as a value, it's 0x00030000 (little-endian)
- `ldfld` treats this as an address and tries to read [0x300 + offset]
- Address 0x300 is in the null guard page, so page fault!

### Current Workaround Applied
Modified `VirtioDevice.InitializeModern()` to avoid local struct variables by accessing struct fields through the object reference directly:

```csharp
// OLD CODE (crashes):
var pd = _pciDevice;
var addr = pd.Address;  // addr is local struct variable
byte b = addr.Bus;      // CRASH - ldfld on value from ldloc

// NEW CODE (workaround):
byte bus = _pciDevice.Address.Bus;
byte device = _pciDevice.Address.Device;
byte function = _pciDevice.Address.Function;
```

**However,** the crash still occurs at CAFE0012 because `PCI.EnableBusMaster(_pciDevice.Address)` still passes the struct by value, triggering the same bug when the callee accesses struct fields.

### Next Steps to Fix

**Option A: Fix CompileLdfld (proper fix)**
Modify `CompileLdfld` to detect when the source is a value type (not a reference):
- If value type on stack: read field directly from stack at [SP + offset]
- If reference type on stack: dereference first, then read at [ptr + offset]

The challenge: JIT needs to track whether the TOS is a value type or reference type.

**Option B: Workaround in driver code**
Modify all code paths that pass struct by value to instead pass individual fields:
- `PCI.EnableBusMaster(PciAddress addr)` -> `PCI.EnableBusMaster(byte bus, byte dev, byte func)`
- This is fragile and requires modifying all call sites

### Files to Investigate

1. **src/kernel/Runtime/JIT/ILCompiler.cs**
   - `CompileLdfld` at line ~5100 - needs to handle value types on stack
   - `CompileLdloc` at line ~3400 - understand how value types are loaded
   - May need to track value type sizes in eval stack metadata

2. **src/kernel/Runtime/JIT/X64Emitter.cs**
   - Stack operations for value types

3. **src/drivers/shared/virtio/VirtioDevice.cs**
   - Current workaround location
   - Line 119: `PCI.EnableBusMaster(_pciDevice.Address);` still crashes

4. **src/ddk/Drivers/PciDeviceInfo.cs**
   - `PciAddress` struct definition (3 bytes: Bus, Device, Function)

---

## Previously Fixed Bugs

### Bug 1: Array ComponentSize (FIXED)
**Problem:** Array allocations for struct element types were undersized.
- `PciBar[] Bars = new PciBar[6]` allocated only 24 bytes instead of 112 bytes
- Root cause: `newarr` used element type's MethodTable which had ComponentSize=0
- Array needs its own MethodTable with ComponentSize = element size

**Fix:** Added `GetOrCreateArrayMethodTable` in AssemblyLoader.cs

### Bug 2: Instance Method 'this' Homing (FIXED)
**Problem:** Constructors with no explicit params had null 'this' pointer.
- Signature's `ParamCount` doesn't include 'this', but instance methods have 'this' in RCX

**Fix:** Modified Tier0JIT.cs to calculate `jitArgCount = hasThis ? paramCount + 1 : paramCount`

### Bug 3: Value Type Field Offset (FIXED)
**Problem:** Value type field offsets included +8 for MethodTable pointer.
- Field resolver always added +8 to offsets (for boxed object's MT pointer)
- But value type instance methods receive `this` as byref to raw struct data (no MT)

**Fix:** Modified MetadataIntegration.cs to start at offset 0 for value types

### Bug 4: Local Variable Slot Size / Callee-Saved Registers (FIXED)
**Problem:** Two related issues:
- Local variable slots were only 8 bytes (too small for structs)
- Callee-saved registers (RBX, R12-R15) were not preserved

**Fix:** Changed local slot size to 64 bytes, added register preservation

---

## Debug Markers

### VirtioBlkEntry.Bind (VirtioBlkEntry.cs)
```
0xBEEF0001 - Entry
0xBEEF0002 - Created PciDeviceInfo
0xBEEF0003 - Set Address
0xBEEF0004 - Before BAR loop
0xBEEF0005 - After BAR loop
0xBEEF0006 - Before new VirtioBlkDevice
0xBEEF0007 - After new VirtioBlkDevice
0xBEEF0008 - Before Initialize call
0xBEEF0009 - After Initialize success
0xBEEF000A - Before InitializeBlockDevice
0xBEEF000B - After InitializeBlockDevice success
0xDEADFFA1 - Initialize failed
0xDEADFFA2 - InitializeBlockDevice failed
```

### VirtioDevice.Initialize (VirtioDevice.cs)
```
0xCAFE0001 - Entry
0xCAFE0002 - After _pciDevice assignment
0xCAFE0003 - After IsLegacyDevice check
```

### VirtioDevice.InitializeModern (VirtioDevice.cs)
```
0xCAFE0010 - Entry
0xCAFE0011 - After extracting bus/device/function
0xCAFE0012 - After WriteConfig16 (CURRENTLY CRASHES AFTER THIS)
0xCAFE0013 - After EnableBusMaster
0xCAFE0014 - After MapBars
0xCAFE0015 - After FindCapabilities
0xCAFE0016 - After WriteStatus(Reset)
0xCAFE0017 - After WriteStatus(Acknowledge)
0xCAFE0018 - After WriteStatus(Driver)
0xCAFE0019 - After NegotiateFeatures
0xCAFE001A - After WriteStatus(FeaturesOk)
0xCAFE001B - After features check
0xCAFE001C - After read NumQueues
0xCAFE001D - After allocate queues array
```

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

---

## Build & Test Commands
```bash
./build.sh 2>&1          # Build (timeout: 120000)
./dev.sh ./run.sh 2>&1   # Run in QEMU (timeout: 10000)
./kill.sh                # Kill containers after test
```

---

## Pending Tasks
1. **[IN PROGRESS]** Fix Bug 5: ldfld on value types from ldloc
2. Test driver binding with modern virtio device
3. Add block read test after driver binding
4. Implement FAT32 filesystem driver
5. Test mounting filesystem and reading/writing files
