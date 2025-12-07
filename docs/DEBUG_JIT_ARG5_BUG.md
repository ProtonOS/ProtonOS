# JIT Bug: 5th Argument (out parameter) - FIXED

## Summary
When calling a method with 5 arguments (>4 args path), the 5th argument was arriving at the callee as 0 (NULL) instead of the correct value.

**STATUS: FIXED** - The 5th argument now passes correctly.

## Root Causes Found and Fixed

### Bug 1: Wrong Scratch Registers for Stack Arguments (FIXED)
The JIT was using `VReg.R6/R7` (R11/RBX) for stack arguments 4 and 5, but:
- VReg.R7 maps to RBX, which is a **callee-saved** register
- Using RBX for temporary storage could corrupt it before the call

**Fix:** Changed to use `VReg.R5/R6` (R10/R11), which are scratch registers.

Location: `ILCompiler.cs` lines 4191-4232

### Bug 2: Object Initializer Syntax Causes Eval Stack Depth Issues (WORKAROUND)
C# object initializer syntax like `result = new PciBar { Index = barIndex };` generates:
```
ldarg.4    // Push address (depth 1)
dup        // Duplicate for stfld (depth 2)
initobj    // Zero struct (depth 1)
ldarg.3    // Push value (depth 2)
stfld      // Store (depth 0)
```

The `dup` + `initobj` combination confuses the JIT's TOS caching, causing eval stack depth tracking to be off by 1.

**Workaround:** Use explicit field assignment instead:
```csharp
// Instead of:
result = new PciBar { Index = barIndex };

// Use:
result = default;
result.Index = barIndex;
```

This generates simpler IL without `dup`:
```
ldarg.4    // Push address (depth 1)
initobj    // Zero struct (depth 0)
ldarg.4    // Push address (depth 1)
ldarg.3    // Push value (depth 2)
stfld      // Store (depth 0)
```

## Key Files Changed
- `src/kernel/Runtime/JIT/ILCompiler.cs` - Fixed VReg usage for >4 args
- `src/drivers/shared/storage/virtio-blk/VirtioBlkEntry.cs` - Avoided object initializers

## VReg to x64 Register Mapping
```
VReg.R0 = RAX (return value, scratch)
VReg.R1 = RCX (arg0)
VReg.R2 = RDX (arg1)
VReg.R3 = R8  (arg2)
VReg.R4 = R9  (arg3)
VReg.R5 = R10 (scratch) ← Use for stack arg4
VReg.R6 = R11 (scratch) ← Use for stack arg5
VReg.R7 = RBX (callee-saved!) ← DO NOT use for temporaries
VReg.FP = RBP (frame pointer)
VReg.SP = RSP (stack pointer)
```

## Stack Layout After Call (for 5+ args)
```
Before CALL:
  [RSP+0..31]  = shadow space (32 bytes)
  [RSP+32..39] = arg4
  [RSP+40..47] = padding for alignment

After CALL + push rbp:
  [RBP+0..7]   = saved RBP
  [RBP+8..15]  = return address
  [RBP+16..47] = shadow space
  [RBP+48..55] = arg4  <-- GetArgOffset(4) = 48
```

## Future Work
The proper fix for Bug 2 would be to make the JIT correctly handle `dup` followed by `initobj` and `stfld`, maintaining correct eval stack depth tracking through the TOS caching optimization.
