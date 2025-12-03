# Milestone: Tier 0 JIT Engine Complete

**Tag:** `v0.1.0-tier0-jit`
**Date:** December 2024

---

## Summary

ProtonOS has reached a significant milestone: a fully functional Tier 0 JIT compiler running in a bare-metal UEFI kernel. This is a complete IL-to-x64 compiler that can execute arbitrary .NET IL code without any operating system support.

What makes this remarkable is that the entire kernel, including the JIT compiler itself, is written in C# and compiled using bflat's NativeAOT. We're running managed code that compiles and executes more managed code, all on bare metal.

**Latest Update (December 2024):** The JIT now supports **lazy compilation** - methods are compiled on-demand when first called. The FullTest assembly exercises 65+ real methods compiled from IL to native x64 code, including recursive methods via indirect calls through the method registry.

---

## By the Numbers

| Metric | Value |
|--------|-------|
| **FullTest Assembly Tests** | 55 passing (real .NET assembly) |
| **Methods JIT Compiled** | 65+ on-demand |
| **IL Opcodes Supported** | ~200 (full ECMA-335 coverage) |
| **Lines of JIT Code** | ~5,500 (ILCompiler.cs) |
| **Lines of x64 Emitter** | ~600 (X64Emitter.cs) |
| **Kernel Binary Size** | ~400 KB |
| **Boot to Tests Complete** | ~3 seconds |

---

## Key Achievements

### 1. Complete IL Opcode Coverage

Every category of IL instruction is implemented:

- **Constants:** `ldc.i4`, `ldc.i8`, `ldc.r4`, `ldc.r8`, `ldnull`, `ldstr`
- **Arithmetic:** `add`, `sub`, `mul`, `div`, `rem` (signed and unsigned, with overflow variants)
- **Bitwise:** `and`, `or`, `xor`, `not`, `shl`, `shr`, `shr.un`
- **Comparison:** `ceq`, `cgt`, `clt` (signed and unsigned)
- **Branching:** All conditional and unconditional branches, `switch`
- **Locals/Args:** `ldloc`, `stloc`, `ldarg`, `starg`, `ldloca`, `ldarga`
- **Memory:** `ldind`, `stind`, `ldobj`, `stobj`, `cpobj`, `initobj`, `cpblk`, `initblk`, `localloc`
- **Fields:** `ldfld`, `stfld`, `ldsfld`, `stsfld`, `ldflda`, `ldsflda`
- **Arrays:** `newarr`, `ldlen`, `ldelem`, `stelem`, `ldelema` (all element types, including multi-dimensional)
- **Objects:** `newobj`, `box`, `unbox`, `unbox.any`, `isinst`, `castclass`
- **Calls:** `call`, `callvirt`, `calli`, `jmp`, `ret`, `tail.`
- **Conversions:** All `conv.*` opcodes including overflow checking
- **Type Operations:** `sizeof`, `ldtoken`, `mkrefany`, `refanyval`, `refanytype`, `arglist`
- **Exception Handling:** `throw`, `rethrow`, `leave`, `endfinally`, `endfilter`

### 2. SEH-Compliant Exception Handling

The JIT generates Windows-compatible exception handling:

- **Funclets:** Separate code blocks for catch/finally/filter handlers
- **Unwind Info:** RUNTIME_FUNCTION and UNWIND_INFO for stack unwinding
- **Two-Pass Dispatch:** First pass finds handler, second executes finally blocks
- **Proper Semantics:** Catch type matching, filter evaluation, finally guarantees

### 3. GC-Safe Code Generation

Every JIT-compiled method includes precise GC information:

- **Stack Maps:** Exact locations of object references on the stack
- **Safe Points:** Call sites where GC can safely interrupt
- **Liveness Tracking:** Which slots are live at each safe point
- **Compact Encoding:** Custom format averaging ~10-15 bytes per method

### 4. String Interning

String literals are properly interned:

- **Token Cache:** O(1) lookup for `ldstr` by metadata token
- **Content Hash:** FNV-1a for `String.Intern` lookups
- **GC Integration:** StringPool acts as root, interned strings never collected

### 5. Multi-Architecture Abstraction

The JIT is designed for portability:

- **ICodeEmitter Interface:** Static abstract methods for code generation
- **VReg Abstraction:** Architecture-neutral virtual registers
- **Compile-Time Dispatch:** Global using aliases select implementation
- **Clean Separation:** ILCompiler is arch-agnostic, X64Emitter handles encoding

### 6. Lazy JIT Compilation

Methods are compiled on-demand when first called:

- **Method Resolver:** Integrated into call instruction compilation
- **Recursive Method Support:** Uses indirect calls through registry entries
- **Compilation State Tracking:** `IsBeingCompiled` flag prevents infinite recursion
- **Reservation Pattern:** Methods reserved before compilation, completed after

### 7. Runtime Infrastructure

Supporting systems that make it all work:

- **AssemblyLoader:** Per-assembly registries, unloading support
- **CompiledMethodRegistry:** Fast lookup of JIT-compiled methods, reservation for recursive calls
- **MetadataIntegration:** Token resolution, type lookup, signature parsing, method resolution
- **CodeHeap:** Executable memory allocation with proper permissions
- **GCHeap:** Object allocation with headers for GC and type info
- **Tier0JIT:** High-level entry point for JIT compilation from metadata tokens

---

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                        Kernel.Main()                        │
├─────────────────────────────────────────────────────────────┤
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────┐  │
│  │ AssemblyLoader│ │MetadataReader│ │ MetadataIntegration │  │
│  └──────┬──────┘  └──────┬──────┘  └──────────┬──────────┘  │
│         │                │                     │             │
│         ▼                ▼                     ▼             │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                    ILCompiler<TEmitter>                │ │
│  │  - IL parsing and analysis                             │ │
│  │  - Stack tracking and type inference                   │ │
│  │  - EH clause management                                │ │
│  │  - GCInfo generation                                   │ │
│  └────────────────────────┬───────────────────────────────┘ │
│                           │                                  │
│                           ▼                                  │
│  ┌────────────────────────────────────────────────────────┐ │
│  │              X64Emitter : ICodeEmitter                 │ │
│  │  - x64 instruction encoding                            │ │
│  │  - Register allocation (VReg → Reg64)                  │ │
│  │  - Calling convention (MS x64 ABI)                     │ │
│  └────────────────────────┬───────────────────────────────┘ │
│                           │                                  │
│                           ▼                                  │
│  ┌────────────────────────────────────────────────────────┐ │
│  │                      CodeHeap                          │ │
│  │  - Executable memory pages                             │ │
│  │  - W^X separation                                      │ │
│  └────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
```

---

## Test Coverage Highlights

### FullTest Assembly (55 tests via real .NET assembly)

The FullTest assembly is a real .NET assembly (compiled with standard `dotnet build`) that exercises the JIT through actual method execution:

1. **Arithmetic Tests:** Add, subtract, multiply, divide, remainder, negate, overflow variants
2. **Comparison Tests:** ceq, cgt, clt (signed and unsigned)
3. **Bitwise Tests:** And, or, xor, not, shift left/right
4. **Control Flow Tests:** Branches (beq, bne, bgt, blt, bge, ble), loops, switch
5. **Local Variable Tests:** Simple locals, many locals, local addresses
6. **Method Call Tests:** Simple calls, calls with args, call chains, recursive calls (Factorial)
7. **Conversion Tests:** conv.i4, conv.i8, conv.u4, conv.i1, conv.u1, conv.i2, conv.u2
8. **Array Tests:** newarr, stelem, ldelem, ldlen, array sum
9. **Field Tests:** Static fields (read, write, multiple)
10. **Object Tests:** ldnull

### Infrastructure Tests (synthetic IL, 116 tests)

The kernel also includes comprehensive synthetic IL tests that validate individual opcodes and edge cases.

---

## What's NOT in Tier 0

This is a "Tier 0" (baseline) JIT, meaning it prioritizes correctness over optimization:

- **No Register Allocation:** All values go through the stack
- **No Inlining:** Every call is a real call
- **No CSE/DCE:** No common subexpression or dead code elimination
- **No Loop Optimization:** No unrolling, hoisting, or vectorization

These are deliberate choices. The goal was a correct, complete foundation that can be optimized later or used as a fallback.

---

## Celebration

This milestone represents months of work building something unique: a managed runtime that bootstraps itself on bare metal. The JIT compiler, written in C#, can compile any valid .NET IL to executable x64 code, including code that allocates objects, throws exceptions, and interacts with the GC.

Some highlights worth celebrating:

- **Zero dependencies:** No libc, no OS, no runtime library imports
- **Self-hosting potential:** The kernel could theoretically JIT-compile itself
- **Full .NET semantics:** Exception handling, GC safety, type safety
- **Clean architecture:** Ready for ARM64, ready for Tier 1 optimization

---

## What's Next?

Possible directions from here:

1. **Tier 1 JIT:** Add optimizations (register allocation, inlining)
2. **ARM64 Port:** Implement Arm64Emitter using the ICodeEmitter interface
3. **Interpreter:** Add a fallback interpreter for debugging
4. **Debugger Support:** Source-level debugging via GDB stub
5. **Threading:** Preemptive scheduling is implemented; add real workloads
6. **File System:** FAT32/ext4 for persistent storage
7. **Networking:** virtio-net driver and TCP/IP stack
8. **Graphics:** virtio-gpu or simple framebuffer console

The foundation is solid. The journey continues.

---

*ProtonOS - A managed kernel for the modern age*
