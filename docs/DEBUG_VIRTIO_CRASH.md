# VirtIO Driver JIT Crash Debug Guide

## For AI Assistant: Context and Current State

You are debugging a JIT compiler for a custom OS kernel (ProtonOS). The JIT compiles .NET IL code to x64 machine code at runtime. The current issue is a crash during VirtIO block driver initialization.

## Quick Reference

```bash
# Build the kernel
./build.sh 2>&1

# Run in QEMU (10 second timeout - kernel boots in ~3 seconds)
./dev.sh ./run.sh 2>&1

# Kill containers after testing (ALWAYS do this)
./kill.sh
```

## Current Crash Details

- **Current failure point**: `Virtqueue.AllocateBuffers` still page faults during queue setup.
- **RIP/CR2** (latest run): RIP `0x20002262F` (was `0x2000224DD/4FB`), `CR2=0x8` (previously 0x0/0xB). Exception 0E, error code 0.
- **Symptoms**:
  - `DMA.Allocate` returns valid buffers (descBuf prints as `phys=0x22E000, virt=0xFFFF800022E000`).
  - After storing into `_descBuffer`, re-read prints only `00000000`, `00000001` before the crash (virtual address never printed because we fault before finishing).
  - Crash occurs immediately after the BBB10005 readback block, before any markers for avail/used buffers (`_availBuffer` allocation likely not reached).
- **New debug instrumentation**:
  - `stfld` runtime trace for large structs now logs even in the large-struct path.
  - In `Virtqueue.AllocateBuffers`, `_descBuffer/_availBuffer/_usedBuffer` fields are populated by manually copying individual fields (avoiding `stfld` of a 32-byte struct).
  - `_debugStfld` logs show the `_descBuffer` store using offset 16 on the Virtqueue instance, and subsequent field stores for each DMABuffer slot (offsets 0/8/16/24) but the readback still shows garbage.

### Confirmed log excerpt (latest)
```
BBB10001
[stfld RT] obj=0x000000001FE65B90 off=0
[stfld RT] obj=0x000000001FE65B90 off=8
[stfld RT] obj=0x000000001FE65B90 off=16
[stfld RT] obj=0x000000001FE65B90 off=24
BBB10002
00000000
0022E000
FFFF8000
0022E000
BBB10003
[stfld RT] obj=0xFFFF8000001004F0 off=0
[stfld RT] obj=0xFFFF8000001004F0 off=8
[stfld RT] obj=0xFFFF8000001004F0 off=16
[stfld RT] obj=0xFFFF8000001004F0 off=24
BBB10004
BBB10005
00000000
00000001
!!! EXCEPTION 000E (CR2=0x8)
```

Interpretation: the `descBuf` local is correct, but after copying into `_descBuffer` the struct appears corrupted (PhysicalAddress low word becomes 1 and we fault before printing the virtual address).

## Key Data Structures

- `DMABuffer` (struct, 32 bytes): `{ ulong PhysicalAddress; void* VirtualAddress; ulong Size; ulong PageCount; }`
- `Virtqueue` field layout (relevant offsets from runtime logging):
  - `_queueIndex` (ushort) @ +8
  - `_queueSize` (ushort) @ +10
  - `_descBuffer` (DMABuffer) appears at offset +16 (debugStfld).
  - `_availBuffer` at +24? (subsequent stflds show offsets 64/72/80 when assigning fields).

## Recent Fixes/Changes

- **JIT fixes**
  - Struct return handling: large value-type returns now copy inline stack data into hidden buffers instead of treating TOS as an address (fixed CompileRet).
  - `stfld` for large structs now calls the runtime debug hook so offsets are observable.
- **Driver changes**
  - `Virtqueue.AllocateBuffers` now copies `DMABuffer` fields individually into `_descBuffer/_availBuffer/_usedBuffer` to avoid large-struct `stfld`.
  - Extra debug markers around buffer allocation/readback remain (BBB markers).

## Observations / Hypotheses

- Hidden-buffer calling convention now returns correct values (descBuf local is correct), so DMA.Allocate is likely OK.
- Copying into the `_descBuffer` field still produces corrupted data when read back immediately, suggesting a JIT bug in:
  - field-address calculation for value-type fields, or
  - handling of `ldflda`/`stfld` of value-type fields inside classes, or
  - locals metadata not propagating value-type sizes for this method (ldloc logging isn’t emitted here).
- Crash happens before `_availBuffer`/`_usedBuffer` allocations, so the bad `_descBuffer` likely leads to a null virtual address when the code later touches it.

## Next Steps / Ideas

1. **Inspect ldflda/stfld for value-type fields**: Verify that `_descBuffer` field address is computed correctly (offset 16) and that the code copies all 32 bytes from the local.
2. **Instrument stack layout** right before `_descBuffer` assignment to confirm the eval stack actually contains 32 bytes (ldloc type size for local #2 may be mis-sized).
3. **Optionally bypass field access**: As a short-term workaround, keep using locals only and set `_desc`/`_avail...` pointers directly from those locals without relying on `_descBuffer` being correct (to confirm whether the crash is purely the field write).
4. **Disassemble AllocateBuffers** again with the new codegen to map RIP `0x20002262F` to the exact instruction (likely near BBB10005 -> first access to VirtualAddress).
5. **Check MetadataIntegration** for local type sizes in assembly 3: ensure the DMABuffer local in AllocateBuffers is marked size 32 so ldloc pushes 4 slots.

## Quick Reference

```bash
# Build
./build.sh

# Run (20s timeout recommended to avoid hang after crash)
timeout 20s ./dev.sh ./run.sh

# QEMU log auto-saved to qemu.log
# Kill containers
./kill.sh
```

## Key Files to Examine

1. **VirtioDevice.cs** - `/home/shane/protonos/src/drivers/shared/virtio/VirtioDevice.cs`
   - The crashing code

2. **VirtioBlkEntry.cs** - `/home/shane/protonos/src/drivers/shared/storage/virtio-blk/VirtioBlkEntry.cs`
   - Entry point that creates the device

3. **PciDeviceInfo.cs** - `/home/shane/protonos/src/ddk/Drivers/PciDeviceInfo.cs`
   - Data structures (PciAddress, PciBar, PciDeviceInfo)

4. **ILCompiler.cs** - `/home/shane/protonos/src/kernel/Runtime/JIT/ILCompiler.cs`
   - JIT compiler (very large file, ~5000+ lines)
   - Key methods: CompileLdfld, CompileStfld, CompileCall, CompileCallvirt

## Debugging Steps

### Step 1: Add Entry Marker to InitializeModern
```csharp
private bool InitializeModern()
{
    Debug.WriteHex(0xMOD00001u); // FIRST LINE - does this print?

    // Rest of method...
}
```
If 0xMOD00001 doesn't print, the crash is in the method prologue (stack setup, etc.)

### Step 2: Check _pciDevice Before Access
```csharp
private bool InitializeModern()
{
    Debug.WriteHex(0xMOD00001u);

    // Check if _pciDevice is null
    if (_pciDevice == null)
    {
        Debug.WriteHex(0xDEAD0000u); // _pciDevice is null!
        return false;
    }
    Debug.WriteHex(0xMOD00002u); // _pciDevice is not null

    byte bus = _pciDevice.Address.Bus;
    Debug.WriteHex(0xMOD00003u); // After bus access
    ...
}
```

### Step 3: Check this Pointer
```csharp
private bool InitializeModern()
{
    // Print 'this' pointer value
    Debug.WriteHex(0xTHIS0000u);
    Debug.WriteHex((uint)((ulong)System.Runtime.CompilerServices.Unsafe.AsPointer(ref this) >> 32));
    Debug.WriteHex((uint)(ulong)System.Runtime.CompilerServices.Unsafe.AsPointer(ref this));
    ...
}
```
Note: This might not work due to JIT limitations with Unsafe methods.

### Step 4: Examine JIT Output for stfld
Look at ILCompiler.cs `CompileStfld` method to see if storing to `_pciDevice` field is correct.

### Step 5: Check Field Offsets
The JIT calculates field offsets. Add debug output to see what offset it calculates for `_pciDevice` in VirtioDevice:
```csharp
// In ILCompiler.cs CompileLdfld or CompileStfld
Debug.WriteHex(0xFLD00000u | (uint)fieldOffset);
```

## Quick Test: Simplify InitializeModern

Try replacing the entire method body with just debug output:
```csharp
private bool InitializeModern()
{
    Debug.WriteHex(0xMOD00001u);
    return false;  // Just return false for now
}
```

If this STILL crashes, the bug is in:
- Method call generation (call/callvirt)
- Stack frame setup
- `this` pointer handling

If it WORKS, progressively add back code to find the exact line that crashes.

## x64 Calling Convention Reminder

- RCX = first argument (or `this` for instance methods)
- RDX = second argument
- R8 = third argument
- R9 = fourth argument
- Stack for arguments 5+
- RAX = return value
- Callee must preserve: RBX, RBP, RDI, RSI, R12-R15

## Summary

The crash at CR2=0x18 in InitializeModern() could be:
1. `this` pointer is null/garbage (accessing VirtioDevice._commonCfgBar)
2. `_pciDevice` is null (accessing PciDeviceInfo.HeaderType at offset 0x18)
3. JIT generating wrong field offsets
4. JIT corrupting registers during method call

Start by adding the most basic debug marker at the very start of InitializeModern() to see if we even enter the method. Then progressively narrow down which field access crashes.

## New Findings (instrumentation run)

- Added markers at the very top of `InitializeModern()` and inside `_pciDevice` null check and after each bus/device/function read: `D00DA001..D00DA005`.
- Added instrumentation inside `PCI.ReadConfig16`: prints `D00DB001` (entry), `D00DB002/3` for ECAM flag, upper/lower `_ecamVirtBase`, `D00DB004` (legacy path), address, `D00DB005/6/7` around port I/O + returned dword.
- Run (20s timeout) produced:
  - We DO enter `InitializeModern`: D00DA001-5 all print before crash.
  - `_pciDevice` is **not null** (D00DA002 printed).
  - Both legacy reads succeed: `_useEcam` is false (D00DB003), config address `0x80001804`, data `0x00100007`.
  - `ReadConfig16` is executed twice before the crash (second time same values), consistent with the call inside `EnableBusMaster`.
  - Crash still occurs immediately after the second `ReadConfig16` sequence, before any `MapBars` markers (`D00D4000+`) run.
  - Crash still at `CR2=0x18`, `RIP=0x200015231`, error code 0.
- Interpretation: the initial field loads and both PCI config reads work; failure happens right after `PCI.EnableBusMaster(_pciDevice.Address)` (another `ReadConfig16` + `WriteConfig16`), pointing to a calling-convention/register preservation issue in that call or immediately afterward. Likely the `this` pointer gets clobbered (null) so the next field access hits offset 0x18 in `VirtioDevice` (`_commonCfgBar`) or `PciDeviceInfo.HeaderType`.
