# netlib - Custom Standard Library Implementation Plan

## Overview

**netlib** is netos's minimal runtime library - the smallest possible foundation needed to load
and execute standard .NET assemblies from NuGet.

### Core Insight
Instead of reimplementing List<T>, Dictionary<K,V>, Stream, ImmutableArray, etc., we implement
only the **runtime-critical types** that the CLR/NativeAOT requires, then **load everything else
from existing NuGet packages**.

This means:
- netlib = ~20-30 core types (primitives, Object, String, Span, GC plumbing)
- Collections, I/O, etc. = loaded from `System.Collections.dll`, `System.IO.dll`, etc.
- System.Reflection.Metadata = loaded from NuGet directly

### What netlib MUST implement (cannot be loaded)
1. **Primitive types** - Int32, Boolean, Byte, etc. (compiler intrinsics)
2. **Core object model** - Object, ValueType, Enum, Array, String, Type
3. **Span/Memory** - tightly coupled to runtime, special compiler handling
4. **Delegates** - runtime-managed function pointers
5. **Attributes** - compiler-required (but minimal implementations)
6. **GC infrastructure** - RhpNewFast, RhpAssignRef, collection
7. **Exception plumbing** - throw/catch mechanics
8. **Threading primitives** - if multi-threaded

### What netlib should NOT implement (load from NuGet)
- List<T>, Dictionary<K,V>, HashSet<T> → System.Collections.dll
- ImmutableArray<T> → System.Collections.Immutable.dll
- Stream, MemoryStream, BinaryReader → System.IO.dll
- Encoding → System.Text.Encoding.dll
- System.Reflection.Metadata → NuGet package

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│              Loaded .NET Assemblies (from NuGet)            │
│  System.Collections.dll, System.IO.dll, S.R.Metadata.dll   │
│  - Pure managed code                                        │
│  - Loaded via our assembly loader                          │
│  - JIT'd or interpreted                                    │
└─────────────────────────────────────────────────────────────┘
                              ↑ Assembly.Load()
┌─────────────────────────────────────────────────────────────┐
│                    netlib (AOT compiled)                    │
│  - Primitives, Object, String, Array, Span<T>              │
│  - GC (mark-sweep)                                          │
│  - Exception handling                                       │
│  - Assembly loader (PE + metadata reader)                  │
│  - JIT/Interpreter bridge                                  │
└─────────────────────────────────────────────────────────────┘
                              ↑ RhpNewFast, etc.
┌─────────────────────────────────────────────────────────────┐
│                   Core Kernel (struct-based)                │
│  - No GC dependency                                         │
│  - Manual memory management                                │
│  - Boot, VMM, scheduler                                    │
└─────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Fork and Integrate zerolib ✓ COMPLETE

### Goal
Get zerolib source into our tree as the foundation for netlib.

### Completed Tasks

#### 1.1 Created netlib directory structure ✓
```
src/netlib/
├── Internal/
│   ├── Runtime/
│   │   └── CompilerHelpers/
│   │       └── InteropHelpers.cs
│   ├── Startup.Efi.cs            # UEFI entry point flow
│   └── Stubs.cs                  # Runtime exports (RhpNewFast, etc.)
├── System/
│   ├── Array.cs
│   ├── Attribute.cs
│   ├── Delegate.cs
│   ├── Enum.cs
│   ├── Environment.cs            # Imports PalFailFast from kernel
│   ├── Exception.cs              # Minimal stub for exception type
│   ├── Math.cs
│   ├── Nullable.cs
│   ├── Object.cs / Object.Efi.cs
│   ├── Primitives.cs             # Int32, Boolean, Byte, etc.
│   ├── ReadOnlySpan.cs
│   ├── Span.cs / SpanHelpers.cs
│   ├── String.cs
│   ├── Type.cs
│   ├── ValueTuple.cs
│   ├── ValueType.cs
│   ├── RuntimeHandles.cs
│   ├── Reflection/
│   │   └── ReflectionAttributes.cs
│   └── Runtime/
│       ├── CompilerServices/
│       │   ├── ClassConstructorRunner.cs
│       │   ├── CompilerAttributes.cs
│       │   ├── RuntimeFeature.cs
│       │   ├── RuntimeHelpers.cs
│       │   └── Unsafe.cs
│       └── InteropServices/
│           ├── InteropAttributes.cs
│           └── MemoryMarshal.cs
```

#### 1.2 Copied and adapted zerolib sources ✓
- Forked from bflat v10.0.0-rc.1 zerolib
- Removed platform-specific code (Console.cs, Thread.Efi.cs, etc.)
- Kept only UEFI-relevant files (removed Unix/Windows)
- Added Environment.cs with DllImport to kernel's PalFailFast
- Added Exception.cs stub for API compatibility

#### 1.3 Updated Makefile ✓
- Changed `--stdlib:zero` to `--stdlib:none`
- Added NETLIB_DIR and NETLIB_SRC
- Compiles netlib + kernel sources together into single kernel.obj
- Reorganized source structure: src/native/, src/netlib/, src/kernel/

#### 1.4 Verification ✓
- Build succeeds with bflat 10.0.0-rc.1
- All existing kernel tests pass
- netlib integrated with kernel via export/import pattern

### Deliverables
- [x] netlib directory with zerolib core (30 files)
- [x] Build system integration (--stdlib:none)
- [x] Export/import pattern for kernel<->netlib (PalFailFast)
- [x] Source reorganization (src/native, src/netlib, src/kernel)

---

## Phase 2: Exception Support ✓ COMPLETE

### Goal
Enable try/catch/throw for error handling.

### Completed Tasks

#### 2.1 Exception class hierarchy ✓
Implemented in `src/netlib/System/Exception.cs`:
- Exception base class with Message, InnerException, HResult
- SystemException, ArgumentException, ArgumentNullException
- InvalidOperationException, NotSupportedException, NotImplementedException
- IndexOutOfRangeException, ArrayTypeMismatchException
- NullReferenceException, InvalidCastException
- OutOfMemoryException, OverflowException, DivideByZeroException
- ArithmeticException, FormatException, PlatformNotSupportedException

#### 2.2 ThrowHelpers ✓
Implemented in `src/netlib/Internal/Stubs.cs`:
- RhpThrowEx - compiler-generated throw statements
- RhpRethrow - rethrow current exception
- RhpThrowHwEx - hardware exceptions (via native.asm)
- ThrowHelpers class for common throw operations

#### 2.3 NativeAOT Exception Dispatch ✓
Implemented full NativeAOT-compatible exception handling:
- **native.asm**: RhpThrowEx captures context, calls C# handler, invokes funclet
- **ExceptionHandling.cs**:
  - NativePrimitiveDecoder for clause data parsing (custom format, not LEB128)
  - Unwind block flags parsing (UBF_FUNC_HAS_EHINFO, etc.)
  - EH clause parsing: tryStart, (tryLen<<2)|kind, handler, typeRva
  - FindMatchingEHClause for catch handler lookup
  - Funclet calling convention (CALL handler, JMP to returned continuation)
- **bflat modification**: Added `--emit-eh-info` flag to enable EH table emission

### Deliverables
- [x] Exception class hierarchy
- [x] ThrowHelpers and runtime exports
- [x] try/catch/throw works (tested and passing!)

---

## Phase 3: Garbage Collector

### Goal
Implement mark-sweep GC for managed heap.

### Tasks

#### 3.1 GC Heap
```csharp
public static class GCHeap
{
    public static void* Alloc(nuint size);
    public static void Collect();
}
```

#### 3.2 Runtime exports
```csharp
[RuntimeExport("RhpNewFast")]
static void* RhpNewFast(MethodTable* mt);

[RuntimeExport("RhpNewArray")]
static void* RhpNewArray(MethodTable* mt, int length);

[RuntimeExport("RhpAssignRef")]
static void RhpAssignRef(void** dst, void* src);
```

#### 3.3 Mark phase
- Scan static roots
- Scan stack roots
- Traverse object graph

#### 3.4 Sweep phase
- Free unmarked objects
- Maintain free list

### Deliverables
- [ ] GC heap allocation
- [ ] Mark-sweep collection
- [ ] Runtime exports working

---

## Phase 4: Assembly Loader (Bootstrap)

### Goal
Implement minimal PE/metadata reader **in netlib itself** (AOT compiled) to bootstrap
loading of other assemblies.

### Key Insight
We need to read PE files to load System.Reflection.Metadata... but we can't use
System.Reflection.Metadata until we load it. So netlib needs a minimal, hand-written
PE/metadata reader just sufficient to:
1. Parse PE headers
2. Read metadata tables
3. Resolve type/method references
4. Load IL for JIT/interpreter

This bootstrap loader doesn't need to be complete - just enough to load the real
S.R.Metadata library, which then handles complex cases.

### Tasks

#### 4.1 PE Header parsing
```csharp
// Minimal PE reader - structs, no allocations
public readonly ref struct PEFile
{
    public PEFile(ReadOnlySpan<byte> data);
    public PEHeader Header { get; }
    public ReadOnlySpan<SectionHeader> Sections { get; }
    public ReadOnlySpan<byte> GetSection(string name);
}
```

#### 4.2 Metadata tables
```csharp
public readonly ref struct MetadataReader
{
    public MetadataReader(ReadOnlySpan<byte> metadata);
    public TypeDefEnumerator TypeDefinitions { get; }
    public MethodDefEnumerator MethodDefinitions { get; }
    // Minimal - just what we need to resolve and load
}
```

#### 4.3 Assembly loading
```csharp
public static class AssemblyLoader
{
    // Load assembly from byte array
    public static Assembly Load(ReadOnlySpan<byte> assemblyData);

    // Resolve type from loaded assemblies
    public static Type GetType(string assemblyQualifiedName);
}
```

### Deliverables
- [ ] Minimal PE parser (structs, Span-based)
- [ ] Minimal metadata reader
- [ ] Can load a simple assembly

---

## Phase 5: JIT/Interpreter Bridge

### Goal
Execute IL code from loaded assemblies.

### Options

#### Option A: IL Interpreter (simpler, slower)
Interpret IL bytecode directly. Good for bootstrapping.

```csharp
public static class ILInterpreter
{
    public static object Execute(MethodInfo method, object[] args);
}
```

#### Option B: RyuJIT Integration (complex, fast)
Use RyuJIT to compile IL to native code. Requires more infrastructure.

### Recommendation
Start with interpreter for Phase 5, add RyuJIT in later phase.

### Tasks

#### 5.1 IL Interpreter core
- Operand stack
- Local variables
- Basic opcodes (ldarg, stloc, add, call, ret, etc.)

#### 5.2 Method dispatch
- Resolve method references
- Handle virtual calls
- P/Invoke to native code

#### 5.3 Type instantiation
- Create objects of loaded types
- Initialize fields

### Deliverables
- [ ] IL interpreter executes basic methods
- [ ] Can call methods from loaded assemblies
- [ ] Object instantiation works

---

## Phase 6: Load BCL Assemblies

### Goal
Load standard .NET assemblies from NuGet to provide collections, I/O, etc.

### Tasks

#### 6.1 Identify minimal BCL set
For System.Reflection.Metadata, we need:
- System.Collections.dll (List, Dictionary)
- System.Collections.Immutable.dll (ImmutableArray)
- System.Memory.dll (Memory<T> extensions)
- System.IO.dll (Stream, BinaryReader)

#### 6.2 Load from embedded resources or UEFI FS
```csharp
// Embed assemblies in kernel image or load from filesystem
var collectionsData = LoadAssemblyBytes("System.Collections.dll");
AssemblyLoader.Load(collectionsData);
```

#### 6.3 Verify type resolution
Ensure loaded assemblies can reference netlib types and vice versa.

### Deliverables
- [ ] System.Collections.dll loads and works
- [ ] System.Collections.Immutable.dll loads and works
- [ ] System.IO.dll loads and works
- [ ] Can use List<T>, Dictionary<K,V> from loaded assemblies

---

## Phase 7: Load System.Reflection.Metadata

### Goal
Load the real System.Reflection.Metadata from NuGet - validates entire stack.

### Why This Matters
If we can load an unmodified S.R.Metadata.dll from NuGet:
- Proves netlib provides correct API surface
- Proves assembly loader works
- Proves JIT/interpreter works
- Proves we can use ANY pure-managed NuGet package

### Tasks

#### 7.1 Download from NuGet
```bash
# No source code needed - just the compiled DLL
nuget install System.Reflection.Metadata
```

#### 7.2 Load and verify
```csharp
var srmData = LoadAssemblyBytes("System.Reflection.Metadata.dll");
var srm = AssemblyLoader.Load(srmData);

// Get PEReader type
var peReaderType = srm.GetType("System.Reflection.PortableExecutable.PEReader");

// Create instance and use it
var peReader = Activator.CreateInstance(peReaderType, testDllStream);
```

#### 7.3 Test with real PE file
1. Load a test .NET DLL
2. Create PEReader from loaded S.R.Metadata
3. Read headers
4. Enumerate types/methods
5. Print to debug console

### Deliverables
- [ ] System.Reflection.Metadata.dll loads
- [ ] PEReader instantiates and works
- [ ] MetadataReader works
- [ ] Can enumerate types from a PE file

---

## Phase 8: UEFI File System Integration

### Goal
Load assemblies from UEFI filesystem instead of embedded resources.

### Tasks

#### 8.1 UEFI Simple File System Protocol
```csharp
public static class UefiFileSystem
{
    public static byte[] ReadFile(string path);
    public static bool FileExists(string path);
    public static string[] ListDirectory(string path);
}
```

#### 8.2 Assembly resolution from filesystem
```csharp
// Configure loader to find assemblies on disk
AssemblyLoader.AddSearchPath("\\EFI\\assemblies\\");
```

#### 8.3 End-to-end test
1. Place test DLLs on boot image
2. Boot kernel
3. Load assemblies from filesystem
4. Parse PE, enumerate metadata
5. Print results

### Deliverables
- [ ] UEFI file reading
- [ ] Assembly loading from filesystem
- [ ] End-to-end test passes

---

## Implementation Summary

| Phase | Description | Complexity | What It Proves |
|-------|-------------|------------|----------------|
| 1 ✓ | Fork zerolib → netlib | Low | Build system works |
| 2 ✓ | Exception support | Low-Medium | Error handling |
| 3 | Mark-Sweep GC | High | Managed heap works |
| 4 | Bootstrap assembly loader | High | Can read PE/metadata |
| 5 | IL Interpreter | High | Can execute loaded code |
| 6 | Load BCL assemblies | Medium | Runtime compatibility |
| 7 | Load S.R.Metadata | Low | Full stack validation |
| 8 | UEFI FS integration | Low | Real-world usage |

## Dependencies Graph

```
Phase 1 (netlib base)
    ↓
Phase 2 (Exceptions)
    ↓
Phase 3 (GC) ─────────────────────┐
    ↓                              │
Phase 4 (Bootstrap loader) ───────┤ Core runtime
    ↓                              │
Phase 5 (IL Interpreter) ─────────┘
    ↓
Phase 6 (Load BCL) ← Load System.Collections, System.IO, etc.
    ↓
Phase 7 (Load S.R.Metadata) ← Validates everything
    ↓
Phase 8 (UEFI FS)
```

## Key Differences from Original Plan

| Original Plan | New Plan |
|--------------|----------|
| Implement List<T> in netlib | Load System.Collections.dll |
| Implement Dictionary<K,V> | Load System.Collections.dll |
| Implement ImmutableArray<T> | Load System.Collections.Immutable.dll |
| Implement Stream | Load System.IO.dll |
| Implement BinaryReader | Load System.IO.dll |
| Port S.R.Metadata source | Load S.R.Metadata.dll |

**Result**: netlib stays minimal (~30 types), everything else is loaded at runtime.

## Success Criteria

At the end of Phase 8:
1. Boot kernel with netlib
2. GC works for managed heap
3. Load assemblies from UEFI filesystem
4. Execute code from loaded assemblies (interpreter)
5. Use System.Reflection.Metadata to parse PE files
6. Print assembly contents to debug console

This proves foundation for:
- Loading RyuJIT as an assembly
- Hot-loadable managed drivers
- Reusing entire NuGet ecosystem

## Internal Calls and P/Invoke Handling

### Overview
.NET assemblies use several mechanisms to call into native code or the runtime. When loading
BCL assemblies (System.Collections.dll, System.IO.dll, etc.), we need to handle these calls
rather than requiring pure-managed assemblies only.

### Types of Native Calls

#### 1. InternalCall Methods
Methods marked with `[MethodImpl(MethodImplOptions.InternalCall)]` are declared in managed
code but implemented by the runtime.

```csharp
// In System.IO.dll (loaded assembly)
public static class Path
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    internal static extern string GetTempPath();
}
```

#### 2. P/Invoke (DllImport)
Methods that call into native DLLs. Common targets include kernel32.dll, ntdll.dll, libc.

```csharp
// In loaded assembly
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);
```

#### 3. RuntimeExport / QCall
Faster internal calls used by CoreCLR. Similar to InternalCall but with different ABI.

### Implementation Strategy

#### Internal Call Table
```csharp
public static class InternalCallResolver
{
    // Map of "Namespace.Type::Method" -> implementation
    private static Dictionary<string, delegate*<void>> _internalCalls = new()
    {
        ["System.IO.Path::GetTempPath"] = &Impl_Path_GetTempPath,
        ["System.Environment::GetProcessorCount"] = &Impl_Environment_GetProcessorCount,
        ["System.Buffer::Memmove"] = &Impl_Buffer_Memmove,
        // etc.
    };

    public static void* Resolve(string fullName)
    {
        if (_internalCalls.TryGetValue(fullName, out var impl))
            return impl;
        return null;  // Not implemented
    }
}
```

#### P/Invoke Resolver
```csharp
public static class PInvokeResolver
{
    public static void* Resolve(string dllName, string methodName)
    {
        // Map Windows API calls to our implementations
        if (dllName is "kernel32.dll" or "kernel32")
        {
            return methodName switch
            {
                "CloseHandle" => &Uefi_CloseHandle,
                "VirtualAlloc" => &Uefi_VirtualAlloc,
                "GetLastError" => &Uefi_GetLastError,
                _ => null
            };
        }

        // libc calls (from Linux-targeting assemblies)
        if (dllName is "libc" or "libSystem.Native")
        {
            return methodName switch
            {
                "malloc" => &KernelHeap_Alloc,
                "free" => &KernelHeap_Free,
                _ => null
            };
        }

        return null;
    }
}
```

### Detection at Load Time

When loading an assembly, scan for native dependencies and report status:

```csharp
public class AssemblyLoadReport
{
    public List<string> ImplementedInternalCalls { get; }
    public List<string> MissingInternalCalls { get; }
    public List<string> ImplementedPInvokes { get; }
    public List<string> MissingPInvokes { get; }
}

public static AssemblyLoadReport AnalyzeAssembly(Assembly asm)
{
    var report = new AssemblyLoadReport();

    foreach (var method in asm.GetAllMethods())
    {
        if (method.IsInternalCall)
        {
            var name = $"{method.DeclaringType.FullName}::{method.Name}";
            if (InternalCallResolver.Resolve(name) != null)
                report.ImplementedInternalCalls.Add(name);
            else
                report.MissingInternalCalls.Add(name);
        }

        if (method.IsPInvoke)
        {
            var (dll, entry) = method.GetPInvokeInfo();
            if (PInvokeResolver.Resolve(dll, entry) != null)
                report.ImplementedPInvokes.Add($"{dll}!{entry}");
            else
                report.MissingPInvokes.Add($"{dll}!{entry}");
        }
    }

    return report;
}
```

### Runtime Behavior Options

When an unimplemented call is encountered:

| Option | Behavior | Use Case |
|--------|----------|----------|
| **Throw** | `NotImplementedException` | Fail fast, good for debugging |
| **Stub** | Return default value | Allow assembly to load, fail later |
| **Warn** | Log and continue | Development diagnostics |

```csharp
public enum MissingCallBehavior
{
    Throw,       // Immediately throw NotImplementedException
    StubDefault, // Return default(T), may cause subtle bugs
    WarnAndThrow // Log warning then throw
}

public static MissingCallBehavior OnMissingInternalCall = MissingCallBehavior.WarnAndThrow;
```

### Common Internal Calls to Implement

These are frequently used by BCL assemblies and should be prioritized:

```
# Memory operations (System.Buffer, System.Runtime.*)
System.Buffer::Memmove
System.Buffer::BulkMoveWithWriteBarrier
System.Runtime.CompilerServices.RuntimeHelpers::InitializeArray

# String operations
System.String::FastAllocateString
System.String::InternalIntern

# Array operations
System.Array::Copy
System.Array::Clear
System.Array::GetLength

# Threading (if multi-threaded)
System.Threading.Monitor::Enter
System.Threading.Monitor::Exit
System.Threading.Interlocked::CompareExchange

# Environment
System.Environment::GetProcessorCount
System.Environment::FailFast

# GC
System.GC::Collect
System.GC::SuppressFinalize
```

### Incremental Implementation

1. **Load assembly** → get list of missing calls
2. **Implement critical ones** → basic functionality works
3. **Stub non-critical** → assembly loads, warn on use
4. **Iterate** → implement more as needed

This allows us to load real BCL assemblies even before we implement every internal call.

---

## Open Questions

1. **Which BCL assemblies are truly needed?**
   - Start minimal, add as MissingTypeException reveals needs

2. **Interpreter vs JIT for Phase 5?**
   - Interpreter is simpler to implement
   - JIT is faster but needs more infrastructure
   - Could do interpreter first, JIT later

3. **Assembly versioning/binding?**
   - May need to handle version mismatches
   - Could start with exact-match only

4. **Reflection support level?**
   - Loaded assemblies may use reflection
   - Need enough Type/MethodInfo to work

5. **Internal call coverage?**
   - Which internal calls do BCL assemblies actually use?
   - Can we identify a minimal set for our use cases?

## References

- [bflat zerolib source](https://github.com/bflattened/bflat/tree/master/src/zerolib)
- [System.Reflection.Metadata NuGet](https://www.nuget.org/packages/System.Reflection.Metadata)
- [.NET runtime source](https://github.com/dotnet/runtime)
- [ECMA-335 CLI Specification](https://www.ecma-international.org/publications-and-standards/standards/ecma-335/)
