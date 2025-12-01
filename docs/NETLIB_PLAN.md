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

## Phase 3: Garbage Collector ✓ COMPLETE

### Goal
Implement mark-sweep GC for managed heap, designed to evolve into generational GC.

### Status
Mark-sweep GC is fully operational with stop-the-world collection, static roots, stack roots,
and free list allocator for memory reuse.

### Progress

#### 3.0 ReadyToRun Header and GCInfo Parsing ✓ COMPLETE

Created infrastructure for locating and parsing NativeAOT runtime metadata:

**New files created:**
- `src/kernel/Runtime/ReadyToRunInfo.cs` - RTR header parsing via .modules section
- `src/kernel/Runtime/GCInfo.cs` - GCInfo decoder for stack maps

**ReadyToRunInfo features:**
- Locates RTR header via `.modules` PE section pointer
- Enumerates all NativeAOT sections (GCStaticRegion, FrozenObjects, TypeManager, etc.)
- Caches commonly-used section pointers for fast access
- Validated: 31 sections found in kernel

**GCInfoDecoder features:**
- Bit-stream based decoder matching .NET runtime's exact format
- Decodes slim and fat headers correctly
- Parses variable-length integers with proper XOR-continuation encoding
- Handles slim header implicit stack base register (RBP)
- Decodes slot table with presence bits for each count
- AMD64-specific `SizeOfStackOutgoingAndScratchArea` field handling
- Full slot definition parsing with register/stack slot delta encoding
- Safe point offset decoding and liveness data access
- **Validated: 312/312 functions (100%) including comprehensive slot/liveness validation!**

**Key implementation details:**
- Slim headers: `HasStackBaseRegister=true` means implicit RBP (no value encoded)
- Variable-length integers: uses XOR with continuation chunks per .NET runtime spec
- Slot counts: each preceded by 1-bit presence flag (0 = count is 0)
- AMD64 fat headers: must read `SizeOfStackOutgoingAndScratchArea` (always present)
- Stack slot delta encoding: `spBase` (2 bits) is ALWAYS read per slot, offset may be delta

**Validation output (comprehensive):**
```
[GCInfo] Results: 331 OK, 4 no-gcinfo, 0 hdr-fail, 0 slot-fail, 0 sanity-fail
[GCInfo] Stats: totalSP=3866 totalSlots=171 maxCode=7795 maxSP=177 maxSlots=6
[GCInfo] All functions validated successfully!
[GCInfo] === Comprehensive Validation Results ===
[GCInfo] Success: 331/331 (4 no-gcinfo)
[GCInfo] Totals: regs=145 stk=26 untrk=197 safePoints=3866
[GCInfo] Functions with liveness data: 34
[GCInfo] === ALL VALIDATION PASSED ===
```

#### 3.1 MethodTable and GCDesc Parsing ✓ COMPLETE

Created infrastructure for parsing MethodTable flags and GCDesc reference field metadata:

**New files created:**
- `src/kernel/Runtime/MethodTable.cs` - Kernel-side MethodTable struct with flag accessors
- `src/kernel/Runtime/GCDesc.cs` - GCDesc parser for enumerating object reference fields

**MethodTable features:**
- Mirrors NativeAOT's MethodTable layout exactly
- Correct flag values from MethodTable.h:
  - `HasPointers = 0x01000000` (type contains GC references)
  - `HasFinalizer = 0x00100000` (type has finalizer)
  - `IsArray = 0x00080000` (type is an array)
- Provides `CombinedFlags` property for flag checking
- Helper properties: `HasPointers`, `HasFinalizer`, `IsArray`, `ComponentSize`

**GCDesc features:**
- `GetSeriesCount()` - Gets number of reference series from GCDesc
- `GetSeriesArray()` - Gets pointer to series entries (stored before MethodTable)
- `EnumerateReferences()` - Walks all reference fields in an object
- `EnumerateValueTypeArrayReferences()` - Handles struct arrays with refs
- `TestWithFrozenObjects()` - Validates by walking frozen object region
- `TestWithHeapObject()` - Validates with heap-allocated Exception

**Validation results:**
```
[GCDesc] Frozen region: 79184 bytes
[GCDesc] 414 objects (414 strings), 0 with refs, 0 total ref slots
[GCDesc] Exception MT=0x... BaseSize=32 Flags=0x51000000 [HasPtrs] Series=1 [off=8,cnt=2] Total=2
```

- Frozen objects: 414 string literals successfully walked (strings have no refs)
- Heap object: Exception has 2 reference fields (`_message`, `_innerException`) at offset 8
- GCDesc series format correctly decoded for mark phase traversal

#### 3.2 Static Roots Enumeration and InitializeStatics ✓ COMPLETE

Implemented NativeAOT's static GC roots initialization and enumeration:

**New files created:**
- `src/kernel/Runtime/StaticRoots.cs` - Static roots enumeration and diagnostics
- `src/kernel/Runtime/InitializeStatics.cs` - GCStaticRegion initialization

**GCStaticRegion Format:**
The GCStaticRegion is an array of 4-byte relative pointers to "static blocks":
```
GCStaticRegion: [relptr32][relptr32][relptr32]...
                    |
                    v
               Static Block (before initialization):
               +0: int32 value = (relptr_to_EEType) | flags
                   Bit 0: Uninitialized flag
                   Bit 1: HasPreInitializedData flag

               Static Block (after initialization):
               +0: nint value = object reference (holder object)
               The holder object contains the actual static field slots
```

**InitializeStatics Implementation:**
1. Walks GCStaticRegion as array of 4-byte relative pointers
2. For each entry, decodes relative pointer to get static block address
3. Reads block value as 32-bit signed (critical: must be signed for negative offsets!)
4. If Uninitialized flag set:
   - Masks off flags to get EEType relative offset
   - Computes MethodTable address: `block_addr + signed_offset`
   - Allocates holder object from heap using `BaseSize` from MethodTable
   - Sets MethodTable pointer in allocated object
   - Stores object reference back in block (clears Uninitialized flag)

**Key Implementation Detail:**
The block value before initialization is a **32-bit signed relative pointer** with flags.
On x64, reading as `nint` (64-bit) would incorrectly interpret negative offsets as large
positive values. Must read as `int` and use signed arithmetic for pointer calculation.

Example:
- Block value: `0xFFFE76D9` (32-bit)
- Flags masked: `0xFFFE76D8` = `-0x18928` as signed int32
- Block at: `0x0DCDFDB0`
- MethodTable: `0x0DCDFDB0 + (-0x18928) = 0x0DCC7488` ✓

**Validation output:**
```
[InitStatics] Processing 1 static blocks...
[InitStatics] Initialized 1 blocks, skipped 0
[StaticRoots] BlockVal=0x000000000000D018 -> ObjRef=0x000000000000D018 MT=0x0DCC7488
```

**Integration:**
- Called from Kernel.Main() after HeapAllocator.Init()
- Must run before any code accesses static GC fields
- Static field assignments (e.g., `_gcTestObject = new object()`) now work correctly

### Object Layout

Every managed object has a 16-byte header before the MethodTable pointer:

```
┌─────────────────────────────────────────────────────────────────┐
│              Block Size Header (64 bits) @ offset -16            │
├─────────────────────────────┬───────────────────────────────────┤
│ Bits 0-31                   │ Bits 32-63                        │
│ Block Size (total alloc)    │ Reserved                          │
└─────────────────────────────┴───────────────────────────────────┘
┌─────────────────────────────────────────────────────────────────┐
│                 Object Header (64 bits) @ offset -8              │
├─────────────────────────────┬───────────────────────────────────┤
│ Bits 0-31                   │ Bits 32-63                        │
│ Flags (8) + Sync Index (24) │ Identity Hash Code (32)           │
└─────────────────────────────┴───────────────────────────────────┘
│                      MethodTable* (64 bits) @ offset 0           │
├─────────────────────────────────────────────────────────────────┤
│                      Fields...                                   │
└─────────────────────────────────────────────────────────────────┘

Block Size Header (offset -16):
  Bits 0-31:  Block size (total allocation including both headers)
  Bits 32-63: Reserved

Object Header (offset -8):
  Bit 0:      GC Mark bit (1 = reachable)
  Bit 1:      Pinned (cannot be relocated)
  Bit 2:      Free block flag (1 = in free list)
  Bit 3:      Reserved
  Bits 4-5:   Generation (0=Gen0, 1=Gen1, 2=Gen2, 3=reserved)
  Bits 6-7:   Reserved
  Bits 8-31:  Sync Block Index (24 bits = 16M entries)
  Bits 32-63: Identity Hash Code (matches int GetHashCode())
              Value 0 = not yet computed, actual 0 maps to 1
```

**Key points:**
- Object reference points at MethodTable*, headers are at `obj - 8` and `obj - 16`
- `RhpNewFast` allocates `16 + size`, returns pointer past headers
- Block size enables heap walking over variable-sized objects
- Free block flag marks blocks in the free list (not live objects)
- Hash is lazily computed on first `GetHashCode()` call
- Generation bits (2) support generational GC (Gen0/1/2)

### Tasks

#### 3.3 GC Heap ✓ COMPLETE

Implemented separate GC heap with proper object headers in `src/kernel/Memory/GCHeap.cs`.
Static root objects now allocated from GCHeap so GC can find and mark them.

**Features:**
- Bump allocator within contiguous 1MB regions obtained from PageAllocator
- 16-byte object header before MethodTable pointer:
  - Block size header (offset -16): Total allocation size for heap walking
  - Object header (offset -8): GC flags, generation, sync block, hash code
- Free list allocator for reusing swept blocks
- Block splitting for efficient memory reuse
- Automatic region allocation when current region fills
- Virtual address mapping via physmap (higher-half)
- `PalAllocObject` updated to use GCHeap (falls back to HeapAllocator during early boot)

**API:**
```csharp
public static class GCHeap
{
    public static void* Alloc(uint size);       // Allocate with header
    public static void* AllocZeroed(uint size); // Allocate and zero
    public static ulong* GetHeader(void* obj);  // Get header before object
    public static bool IsMarked(void* obj);     // Check mark bit
    public static void SetMark(void* obj);      // Set mark bit
    public static void ClearMark(void* obj);    // Clear mark bit
    public static int GetHashCode(void* obj);   // Get/compute hash
    public static bool IsInHeap(void* ptr);     // Check if in GC heap
}
```

**Validation:**
```
[GCHeap] Initialized: FFFF800000100000 - FFFF800000200000 (1024 KB)
[InitStatics] Initialized 1 blocks, skipped 0
[GCDesc] Exception MT=... BaseSize=32 Flags=0x51000000 [HasPtrs] Series=1
```

#### 3.4 Runtime exports (existing)
```csharp
[RuntimeExport("RhpNewFast")]
static void* RhpNewFast(MethodTable* mt);

[RuntimeExport("RhpNewArray")]
static void* RhpNewArray(MethodTable* mt, int length);

[RuntimeExport("RhpAssignRef")]
static void RhpAssignRef(void** dst, void* src);
```

#### 3.5 Stack Root Enumeration ✓ COMPLETE

Implemented precise stack root enumeration using GCInfo liveness data in `src/kernel/x64/StackRoots.cs`.

**Features:**
- Walks stack frames using RUNTIME_FUNCTION and VirtualUnwind
- Decodes GCInfo for each frame to find safe point and slot liveness
- Supports all three slot base types:
  - `GC_SP_REL` - Relative to current RSP
  - `GC_CALLER_SP_REL` - Relative to caller's SP (computed via VirtualUnwind)
  - `GC_FRAMEREG_REL` - Relative to frame pointer (RBP or custom)
- Handles register slots (callee-saved registers in context)
- Proper signed offset arithmetic for negative stack offsets

**CallerSP Handling:**
For CallerSP-relative slots, we compute CallerSP by virtually unwinding a copy of the context
before enumerating each frame's roots. CallerSP = RSP value after unwinding the current frame.

**Validation:**
```
[GC] Walking stack from RIP=0x... RSP=0x...
  [StackRoot] Frame 4 offset=53: 1 root(s)
  [Stack] 9 frames, 4 with slots, 1 live, 1 in heap
[GC] Current thread: 1 stack roots
```

#### 3.6 Mark Phase GarbageCollector ✓ COMPLETE

Implemented full mark phase with stop-the-world collection in `src/kernel/Memory/GarbageCollector.cs`.

**Features:**
- Stop-the-world thread suspension using Scheduler
- Mark bit storage in object headers (bit 0)
- Iterative marking with explicit work stack (avoids recursion overflow)
- Root enumeration:
  - Static roots from GCStaticRegion via `StaticRoots.EnumerateStaticRoots`
  - Stack roots from all threads via `StackRoots.EnumerateStackRoots`
- Transitive closure via GCDesc reference enumeration

**Mark Phase Algorithm:**
1. Stop the world (disable scheduling, suspend all threads except GC thread)
2. Clear all mark bits on heap objects
3. Enumerate roots: static roots + stack roots from all threads
4. For each root, mark object and push to work stack
5. Process work stack: for each object, traverse references via GCDesc, mark and push
6. Resume the world

**Test Results:**
```
[GC] Starting collection #1...
[GC] Stopping the world...
[GC] Cleared marks on 7 objects in 1 region(s)
[GC] Current thread: 1 stack roots
[GC] Mark phase complete: 2 roots, 2 objects marked
[GC] Resuming the world...
[GC Test] Stack object marked: YES
[GC Test] Static object marked: YES
[GC Test] Total marked: 2 objects
```

#### 3.7 Sweep Phase ✓ COMPLETE

Implemented sweep phase with free list allocator in `src/kernel/Memory/GarbageCollector.cs`.

**Features:**
- Walks heap using block size header for precise object boundaries
- Unmarked objects added to free list for reuse
- Free list allocation with first-fit strategy
- Block splitting when free block is larger than needed
- Marks cleared on surviving objects for next collection

**Sweep Algorithm:**
1. Walk each heap region from start to AllocPtr
2. Read block size from offset -16 to find next object
3. If object is not marked: add to free list, set free flag
4. If object is marked: clear mark bit for next GC cycle
5. Update free list statistics

**Free List Allocator:**
- First-fit search through free list
- Block splitting: if free block >= needed + 24 bytes, split it
- Minimum allocation size: 24 bytes (16 headers + 8 MT pointer)
- Free blocks store next pointer where MethodTable* would be

**Test Results:**
```
[GC Test] Freed blocks: 8 (expected: 7+) - PASSED
[GC Test] Live objects still valid: YES
[GC Test] Pre-alloc: 8 blocks, 344 bytes
[GC Test] Post-alloc: 7 blocks, 304 bytes
[GC Test] Allocated from free list: YES - PASSED
```

### MethodTable and GCDesc

NativeAOT/bflat emits a GCDesc **before** each MethodTable in memory. The GCDesc describes
which fields in an object contain references (for the mark phase to traverse).

```
Memory layout (grows backward from MethodTable):

         ┌─────────────────────────┐
         │ GCDescSeries[n-1]       │  ← Series n-1 (offset, size)
         ├─────────────────────────┤
         │ ...                     │
         ├─────────────────────────┤
         │ GCDescSeries[0]         │  ← Series 0 (offset, size)
         ├─────────────────────────┤
         │ Series Count (nint)     │  ← Positive = normal, Negative = value array
         ├─────────────────────────┤
mt ───►  │ MethodTable             │  ← Object reference points here
         │   _usComponentSize      │
         │   _usFlags              │  ← HasPointersFlag (0x01000000) indicates GCDesc present
         │   _uBaseSize            │
         │   _relatedType          │
         │   _usNumVtableSlots     │
         │   _usNumInterfaces      │
         │   _uHashCode            │
         │   VTable[...]           │
         └─────────────────────────┘
```

**GCDescSeries structure (16 bytes on x64):**
```csharp
struct GCDescSeries
{
    nint SeriesSize;    // Adjusted size (actual_size - base_size)
    nint StartOffset;   // Offset from object start to first ref in series
}
```

**Interpreting series (positive count):**
- Series count at `((nint*)mt)[-1]`
- Each series describes a contiguous run of reference fields
- `StartOffset` = byte offset from object reference to first ref
- `SeriesSize` = (actual_bytes - base_size), add base_size back to get true size
- Number of refs in series = `(SeriesSize + BaseSize) / sizeof(nint)`

**Example: Enumerating references:**
```csharp
static unsafe void EnumerateReferences(object obj, Action<nint> callback)
{
    MethodTable* mt = obj.m_pMethodTable;
    nint* pMT = (nint*)mt;
    nint seriesCount = pMT[-1];

    if (seriesCount <= 0)
        return;  // No refs, or value-type array (handle separately)

    nint baseSize = mt->_uBaseSize;
    GCDescSeries* series = (GCDescSeries*)(pMT - 1);

    for (int i = 1; i <= seriesCount; i++)
    {
        nint offset = series[-i].StartOffset;
        nint size = series[-i].SeriesSize + baseSize;
        nint* refPtr = (nint*)((byte*)obj + offset);
        nint count = size / sizeof(nint);

        for (nint j = 0; j < count; j++)
            callback(refPtr[j]);
    }
}
```

**Key flag:**
- `HasPointersFlag = 0x01000000` in `_usFlags` indicates the type contains GC references
- If this flag is clear, no GCDesc exists and object has no refs to scan

### Stack Maps for Root Scanning

To properly scan stack roots, we need **stack maps** - metadata that tells the GC which stack
slots and registers contain live object references at each potential GC safepoint.

**Why stack maps matter:**
- Conservative scanning (treating every pointer-like value as a potential ref) is fragile
- Can't relocate objects if we might have false positives
- Generational GC needs accurate root information

**NativeAOT stack map format:**
NativeAOT emits GC info for each method that describes:
1. Which stack slots contain refs at each safepoint (call sites, loops)
2. Which callee-saved registers contain refs
3. Return address locations for stack walking

**Implementation approach:**
1. Parse GC info emitted by bflat/NativeAOT compiler
2. At GC time, walk the stack using return addresses
3. For each frame, look up the GC info by instruction pointer
4. Enumerate live refs from stack slots and saved registers

**Key data structures:**
```csharp
// Per-method GC info (lookup by instruction pointer range)
struct GCInfo
{
    nint StartIP;
    nint EndIP;
    // Encoded safepoint data...
}

// Runtime needs to maintain a sorted table of GCInfo by IP for lookup
```

**Confirmed:** bflat/NativeAOT **already emits GCInfo** for every method! Verified via `--map`:
```xml
<MethodCode Name="kernel_Kernel_Kernel__Main" Length="435" />
<GCInfo Name="kernel_Kernel_Kernel__Main" Length="57" />
```

**GCInfo location in .xdata (verified by manual parsing):**

```
.xdata section layout for each method:

┌─────────────────────────────────────────┐
│ UNWIND_INFO (Windows standard)          │
│   - Header (4 bytes)                    │
│   - UnwindCodes (CountOfCodes * 2)      │
├─────────────────────────────────────────┤
│ unwindBlockFlags (1 byte)               │  ← NativeAOT extension
│   bit 0-1: func kind (0=root,1=handler) │
│   bit 2: UBF_FUNC_HAS_EHINFO            │
│   bit 3: UBF_FUNC_REVERSE_PINVOKE       │
│   bit 4: UBF_FUNC_HAS_ASSOCIATED_DATA   │
├─────────────────────────────────────────┤
│ Associated data RVA (4 bytes, optional) │  ← if bit 4 set
├─────────────────────────────────────────┤
│ EH info RVA (4 bytes, optional)         │  ← if bit 2 set
├─────────────────────────────────────────┤
│ GCInfo (variable length)                │  ← Stack maps here!
└─────────────────────────────────────────┘
```

**Verified examples:**
- `Main` (no EH): UNWIND_INFO=22 bytes, flags=0x00, GCInfo at offset 23 (57 bytes)
- `TestExceptionHandling` (has EH): UNWIND_INFO=10 bytes, flags=0x04, EH RVA, GCInfo at offset 15 (65 bytes)

**GCInfo format (confirmed - contains full stack maps):**
Uses the standard NativeAOT/CoreCLR GcInfoDecoder format. The GCInfo blob encodes:

**Header Information:**
- Header flags (fat vs. slim format)
- Return kind
- Code length (denormalized)
- Prolog/epilog size

**Stack/Security Management:**
- GS cookie stack slot (stack guard offset)
- Valid range (where GS cookie is valid)
- PSP symbol stack slot
- Stack base register (frame pointer)

**Generic Context:**
- Generics instantiation context stack slot

**Interruptibility Information:**
- Number of interruptible ranges (GC-safe regions in code)
- Interruptible range deltas (start/stop offsets)

**Safe Points (the "stack map"):**
- Number of safe points (call sites, loops where GC can occur)
- Safe point offsets (normalized code offsets)

**Slot Table (tracked references):**
- Number of tracked slots (registers + stack)
- For each slot: register ID or stack offset, flags (pinned, interior pointer)
- Slot array describes which locations CAN hold references

**Live State Per Safe Point:**
- Bit vector or RLE-compressed liveness
- For each safe point, indicates WHICH slots are live (contain valid refs)
- This is the actual "stack map" - tells GC exactly what to scan at each IP

**Lifetime Transitions:**
- State changes within code chunks
- Tracks when slots become live/dead

Reference: [gcinfodecoder.cpp](https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/gcinfodecoder.cpp)

**Implementation approach:**
We already have `GetNativeAotEHInfo()` in ExceptionHandling.cs that parses this structure.
For GC, we create a similar `GetGCInfo()` that:
1. Reuses RUNTIME_FUNCTION lookup (already implemented)
2. Parses past UNWIND_INFO using existing logic
3. Skips unwindBlockFlags and optional RVAs
4. Returns pointer to GCInfo for decoder

#### 3.3 Mark phase
- Scan static roots (global variables containing refs)
- Scan stack roots using stack maps (walk frames, decode GC info per IP)
- For each root, mark object and recursively traverse using GCDesc

#### 3.4 Sweep phase
- Walk GC heap linearly
- Free unmarked objects to free list
- Clear mark bits on surviving objects

### Research Summary

| Topic | Status | Notes |
|-------|--------|-------|
| Object Header | ✓ Implemented | 64-bit: flags+sync (32) + hash (32) in GCHeap.cs |
| GC Heap | ✓ Implemented | Bump allocator with 1MB regions from PageAllocator |
| GCDesc | ✓ Implemented | Series format parsing in GCDesc.cs |
| GCInfo/Stack Maps | ✓ Complete | 312/312 functions validated including liveness |
| GCInfo Header | ✓ Complete | Slim/fat headers, AMD64 SizeOfStackArea |
| GCInfo Varint | ✓ Verified | XOR-continuation encoding |
| GCInfo Safe Points | ✓ Complete | Absolute offsets, liveness decoding works |
| GCInfo Slot Table | ✓ Complete | Register/stack slots, delta encoding, spBase |
| GCInfo Liveness | ✓ Complete | Direct bit vector + indirect table support |
| Interruptible Ranges | ✓ Implemented | Delta-encoded range pairs decoded |
| Interior/Pinned Ptrs | ✓ Implemented | Slot flags decoded (2 bits per slot) |
| Indirect Liveness | ✓ Implemented | Pointer table + RLE deduplication |
| Static Roots | ✓ Implemented | __GCStaticRegion + InitializeStatics |
| Stack Roots | ✓ Implemented | StackRoots.cs with CaptureContext |
| Write Barriers | ✓ Documented | Simple store for mark-sweep |
| MethodTable Flags | ✓ Documented | HasPointers, HasFinalizer |
| Allocation Context | ✓ Documented | Per-thread bump allocation |
| Finalization | ⏸ Deferred | Not needed for initial GC |
| Weak References | ⏸ Deferred | Not needed for initial GC |
| Thread Suspension | ✓ Documented | Design for SMP from start |
| RTR Header | ✓ Verified | .modules section → RTR header location |
| UNWIND_INFO Size | ✓ Verified | 4 + CountCodes*2, NO padding |

---

### ReadyToRun (RTR) Header Structure

NativeAOT embeds a ReadyToRunHeader in the PE file containing section pointers for runtime
metadata including GC static regions, frozen objects, and type manager indirection.

#### Locating the RTR Header

```
.modules PE section (at RVA 0x3C000 in our kernel):
┌────────────────────────────────┐
│ Pointer to RTR Header (8 bytes)│ → Points to RVA 0x3480 in .rdata
└────────────────────────────────┘

The .modules section contains an array of pointers to ReadyToRunHeader structures.
For single-module NativeAOT executables, there's typically one entry.
```

#### RTR Header Format (NativeAOT)

```
Offset  Size  Field
──────────────────────────────────────────────────
0x00    4     Signature         0x00525452 ('RTR\0')
0x04    2     MajorVersion      16 (current)
0x06    2     MinorVersion      0
0x08    4     Flags             0
0x0C    2     NumberOfSections  31 (in our kernel)
0x0E    1     EntrySize         24 bytes per section entry
0x0F    1     EntryType         1 (pointer-based entries)
──────────────────────────────────────────────────
0x10    varies  Section entries array
```

#### Section Entry Format (ModuleInfoRow)

Each section entry is 24 bytes:
```
Offset  Size  Field
──────────────────────────────────────────────────
0x00    4     SectionId         ReadyToRunSectionType enum
0x04    4     Flags             ModuleInfoFlags (bit 0 = HasEndPointer)
0x08    8     Start             Absolute VA (pointer)
0x10    8     End               Absolute VA (or same as Start if no end)
```

#### NativeAOT Section Types (200+ range)

| ID  | Name                   | Description                              |
|-----|------------------------|------------------------------------------|
| 200 | StringTable            | String constants                         |
| 201 | GCStaticRegion         | Pointers to GC static data blocks        |
| 202 | ThreadStaticRegion     | Thread-local static data                 |
| 204 | TypeManagerIndirection | Pointer to TypeManager                   |
| 205 | EagerCctor             | Static constructors to run at startup    |
| 206 | FrozenObjectRegion     | Pre-allocated frozen objects (strings)   |
| 207 | DehydratedData         | Compressed metadata (if present)         |
| 208 | ThreadStaticOffsetRegion | Thread static offsets                  |
| 212 | ImportAddressTables    | Import tables for DllImport              |
| 213 | ModuleInitializerList  | Module initializer function pointers     |
| 300-399 | ReadonlyBlobRegion | RhFindBlob compatibility blobs          |

#### Kernel Binary RTR Analysis (Verified)

Our BOOTX64.EFI contains:
- RTR Header at RVA 0x3480 (file offset 0x1A80)
- 31 sections with 24-byte entries
- Key sections:
  - GCStaticRegion (201): RVA 0x7810, 4 bytes
  - FrozenObjectRegion (206): RVA 0x7828-0x19E00, 75KB of frozen objects
  - ModuleInitializerList (213): Empty (no module initializers)
  - Various ReadonlyBlob sections for runtime data

Source references:
- [ModuleHeaders.h](https://github.com/dotnet/runtime/blob/main/src/coreclr/nativeaot/Runtime/inc/ModuleHeaders.h)
- [CoffObjectWriter.Aot.cs](https://github.com/dotnet/runtime/blob/main/src/coreclr/tools/aot/ILCompiler.Compiler/Compiler/ObjectWriter/CoffObjectWriter.Aot.cs)

---

### Pending Research

#### Static Roots (Verified)
NativeAOT emits these symbols for static fields:
- `__NONGCSTATICS` - Non-reference statics (primitives, pointers, structs)
- `__GCSTATICS` - Reference statics (object fields) - 8 bytes per object reference
- `__GCStaticEEType_XX` - EEType for each GC statics block (for GC scanning)
- `__GCStaticRegion` - Array of pointers to GC static blocks

**Verified with test:** Added `static object? _gcTestObject` to Kernel class:
```xml
<GCStatics Name="?__GCSTATICS@kernel_Kernel_Kernel@@" Length="8" />
<GCStaticEEType Name="__GCStaticEEType_01" Length="40" />
<ArrayOfEmbeddedPointers Name="__GCStaticRegion" Length="4" />
```

**Format of __GCStaticRegion:**
- Array of pointers to GC static blocks
- Each entry points to a `__GCSTATICS` block for a class
- Entry format: pointer with flags in low bits
  - Bit 0: Uninitialized flag
  - Bit 1: Has pre-initialized data blob flag
- The `__GCStaticEEType` describes the layout of reference fields in the block

**To enumerate static roots:**
1. Get `__GCStaticRegion` start/end pointers
2. Iterate through each entry (4 or 8 bytes depending on relative/direct pointers)
3. For each initialized entry, use the associated EEType to find reference offsets
4. Report each reference field as a GC root

#### GcInfoDecoder (Confirmed: Stack Maps ARE Emitted)
NativeAOT/bflat emits complete stack maps in GCInfo. No additional compiler flags needed.

**Implementation approach:**
1. **Port full C++ decoder to C#** - Implement complete GcInfoDecoder
   - Reference: [gcinfodecoder.cpp](https://github.com/dotnet/runtime/blob/main/src/coreclr/vm/gcinfodecoder.cpp)
   - Test against our own kernel binary to validate correctness
   - Enumerate all methods, decode GCInfo, print slot tables and safe points
2. Once decoder is working, integrate with stack walker for root enumeration
3. Conservative scanning can be used as fallback/validation during development

##### GCInfo Location in .xdata

GCInfo is located after UNWIND_INFO in the .xdata section. We already have parsing code in
`ExceptionHandling.cs:GetNativeAotEHInfo()` that locates EH info - GCInfo follows immediately.

```
.xdata layout for each method:
┌──────────────────────────────────────────────────┐
│ UNWIND_INFO (Windows x64 standard)               │
│   - VersionAndFlags (1 byte)                     │
│   - SizeOfProlog (1 byte)                        │
│   - CountOfUnwindCodes (1 byte)                  │
│   - FrameRegisterAndOffset (1 byte)              │
│   - UnwindCodes[CountOfUnwindCodes] (2 bytes ea) │
├──────────────────────────────────────────────────┤
│ [DWORD-align if EHANDLER/UHANDLER flag set]      │
│ [Exception Handler RVA - 4 bytes, if flags set]  │
├──────────────────────────────────────────────────┤
│ unwindBlockFlags (1 byte) - NativeAOT extension  │
│   Bits 0-1: Func kind (0=root, 1=handler, 2=flt) │
│   Bit 2: UBF_FUNC_HAS_EHINFO                     │
│   Bit 3: UBF_FUNC_REVERSE_PINVOKE                │
│   Bit 4: UBF_FUNC_HAS_ASSOCIATED_DATA            │
├──────────────────────────────────────────────────┤
│ [Associated Data RVA - 4 bytes, if bit 4 set]    │
├──────────────────────────────────────────────────┤
│ [EH Info RVA - 4 bytes, if bit 2 set]            │
├──────────────────────────────────────────────────┤
│ GCInfo (variable length) ◄── STARTS HERE         │
└──────────────────────────────────────────────────┘
```

##### Variable-Length Integer Encoding

GCInfo uses a custom variable-length encoding with a `base` parameter:

```csharp
// Encoding algorithm (EncodeVarLengthUnsigned):
// - Each chunk is (base + 1) bits
// - High bit of chunk is continuation flag (1 = more chunks follow)
// - Low 'base' bits are payload
// - Multi-chunk values: accumulate with left shift, NO XOR
//
// Example with base=8 (AMD64):
//   Values 0-255: encoded in 9 bits (8 payload + continuation=0)
//   Values 256+:  first chunk has continuation=1, value bits 0-7
//                 second chunk has continuation=0, value bits 8-15
//                 Result = chunk0_value | (chunk1_value << 8)

size_t DecodeVarLengthUnsigned(int base)
{
    size_t result = 0;
    int shift = 0;
    while (true) {
        size_t chunk = Read(base + 1);
        result |= (chunk & ((1 << base) - 1)) << shift;
        if ((chunk >> base) == 0)  // No continuation?
            break;
        shift += base;
    }
    return result;
}
```

##### GCInfo Header Format (100% Verified Against Kernel Binary)

GCInfo uses a slim/fat header distinction controlled by the first bit:

**Slim Header (bit0=0) - 173 functions (63%) in our kernel:**
```
Bit 0:      Header type = 0 (slim)
Bit 1:      Stack base register present flag
Bits 2+:    Code length (varint, base=8 for AMD64)
            ... remaining fields ...
```

**Fat Header (bit0=1) - 100 functions (37%) in our kernel:**
```
Bit 0:      Header type = 1 (fat)
Bit 1:      IsVarArg
Bit 2:      Unused (was hasSecurityObject)
Bit 3:      HasGSCookie
Bit 4:      Unused (was hasPSPSymStackSlot)
Bits 5-6:   ContextParamType (2 bits)
Bit 7:      HasStackBaseRegister
Bit 8:      WantsReportOnlyLeaf (AMD64-specific)
Bit 9:      HasEditAndContinuePreservedArea
Bit 10:     HasReversePInvokeFrame
Bits 11+:   Code length (varint, base=8 for AMD64)
            ... remaining fields ...
```

**IMPORTANT:** The `code_length` in GCInfo covers the ROOT function PLUS all its funclets
(exception handlers/filters). For functions with try/catch, the GCInfo code_length will be
larger than the RUNTIME_FUNCTION length because it includes the handler code.

**Verified: 273/273 ROOT functions (100%) decode correctly!**
(Including 2 functions with funclets where GCInfo code_length covers root + handlers)

**Example - Slim header, Function #0 (507 bytes):**
```
GCInfo: EE 0F C0 AE C3 89 ...
Binary: 11101110 00001111 ...
        ^^------ Header bits (0b10 = slim header, has stack base)
          ^^^^^^ Code length starts at bit 2
Decoded code_length = 507 ✓
```

**Example - Fat header, Function #62 (445 bytes):**
```
GCInfo: 81 E8 1D 00 08 ...
Binary: 10000001 11101000 ...
        ^------- Fat indicator (1)
         ^^^^^^^ Flags: vararg=0, gs=0, ctx=0, stack=1, leaf=0, enc=0, rp=0
               ^^ Code length starts at bit 11
Decoded code_length = 445 ✓
```

**Source reference:**
- [gcinfoencoder.cpp](https://github.com/dotnet/runtime/blob/main/src/coreclr/gcinfo/gcinfoencoder.cpp) - Build() method

##### Full Header Field Order (after code_length)

```
1.  Header type (1 bit)           - 0=slim, 1=fat
2.  [Slim] Stack base flag (1 bit)
    [Fat] Header flags            - GcInfoHeaderFlags (size varies)
3.  Code length                   - DecodeVarLengthUnsigned(base=8) ← VERIFIED
4.  [If GS cookie] Prolog size    - DecodeVarLengthUnsigned(base=5)
5.  [If GS cookie] Epilog size    - DecodeVarLengthUnsigned(base=5)
6.  GS cookie stack slot          - DecodeVarLengthSigned(base=6)
7.  [Old format] PSP sym slot     - DecodeVarLengthSigned(base=6)
8.  Generics inst context slot    - DecodeVarLengthSigned(base=6)
9.  Stack base register           - DecodeVarLengthUnsigned(base=2)
10. E&C preserved area size       - DecodeVarLengthUnsigned(base=3)
11. Reverse P/Invoke frame slot   - DecodeVarLengthSigned(base=6)
12. Number of safe points         - DecodeVarLengthUnsigned(base=2)
13. Number of interruptible ranges- DecodeVarLengthUnsigned(base=1)
```

##### Slot Table Format (VERIFIED)

After safe points/interruptible ranges comes the slot table:

```
Slot table decoding (all verified against kernel binary):
1. Register count                 - DecodeVarLengthUnsigned(base=2)
2. Stack slot count               - DecodeVarLengthUnsigned(base=2)
3. Untracked slot count           - DecodeVarLengthUnsigned(base=1)

For first register slot:
  - Register number               - DecodeVarLengthUnsigned(base=3) [0=RAX, 1=RCX, ...]
  - Flags (2 bits)                - bit0=interior, bit1=pinned

For subsequent register slots (delta encoded):
  - Register delta                - DecodeVarLengthUnsigned(base=2) + 1 from previous
  - Flags (2 bits)

For first stack slot:
  - Stack slot base (2 bits)      - 0=SP_REL, 1=CALLER_SP_REL, 2=FRAMEREG_REL
  - Stack offset                  - DecodeVarLengthUnsigned(base=6)
  - Flags (2 bits)

For subsequent stack slots (delta encoded):
  - Offset delta                  - DecodeVarLengthUnsigned(base=4)
  - Flags (2 bits)
```

##### Safe Points and Liveness (VERIFIED)

Safe points are code offsets where GC can occur (call sites, loop back-edges):

```
Safe point encoding:
1. Safe point offsets             - ABSOLUTE, NOT delta-encoded!
   - Each offset: fixed-width bits = ceil(log2(code_length + 1))
   - Example: code_length=507 → offset_width=9 bits

After slot table comes liveness data:
1. Indirection flag (1 bit)       - 0=direct, 1=indirect
2. If direct (flag=0):
   - For each safe point: 1 bit per tracked slot
   - bit=1 means slot contains live GC reference at that point
   - Example: 3 tracked slots × 52 safe points = 156 bits

Verified example from Function #0:
  SP #7 @ offset 103: bits=010 → RCX is live (slot 1)
  SP #8 @ offset 113: bits=000 → no live refs
```

##### AMD64-Specific Encoding Constants

```csharp
// From gcinfotypes.h - AMD64GcInfoEncoding
// All values VERIFIED against kernel binary parsing

// Header fields
const int CODE_LENGTH_ENCBASE = 8;                    // ← VERIFIED
const int NORM_PROLOG_SIZE_ENCBASE = 5;               // +1 denormalization
const int NORM_EPILOG_SIZE_ENCBASE = 3;
const int SIZE_OF_EDIT_AND_CONTINUE_PRESERVED_AREA_ENCBASE = 4;
const int STACK_BASE_REGISTER_ENCBASE = 3;            // ← VERIFIED (was 2 in docs)
const int GS_COOKIE_STACK_SLOT_ENCBASE = 6;
const int GENERICS_INST_CONTEXT_STACK_SLOT_ENCBASE = 6;
const int REVERSE_PINVOKE_FRAME_ENCBASE = 6;          // ← VERIFIED

// Counts
const int NUM_SAFE_POINTS_ENCBASE = 2;                // ← VERIFIED
const int NUM_INTERRUPTIBLE_RANGES_ENCBASE = 1;       // ← VERIFIED (fat headers only)
const int NUM_REGISTERS_ENCBASE = 2;
const int NUM_STACK_SLOTS_ENCBASE = 2;                // ← Was 5 in docs, using 2
const int NUM_UNTRACKED_SLOTS_ENCBASE = 1;            // ← Was 5 in docs, using 1

// Slot table
const int REGISTER_ENCBASE = 3;
const int REGISTER_DELTA_ENCBASE = 2;
const int STACK_SLOT_ENCBASE = 6;
const int STACK_SLOT_DELTA_ENCBASE = 4;

// Safe points and liveness
const int NORM_CODE_OFFSET_DELTA_ENCBASE = 3;
const int INTERRUPTIBLE_RANGE_DELTA1_ENCBASE = 6;
const int INTERRUPTIBLE_RANGE_DELTA2_ENCBASE = 6;
const int LIVESTATE_RLE_RUN_ENCBASE = 2;
const int LIVESTATE_RLE_SKIP_ENCBASE = 4;
```

**Key insight:** The slot table describes WHERE refs CAN live. The liveness bits per safe point
tell you WHICH slots actually contain refs at that instruction. Combined with IP from stack walk,
gives precise root enumeration.

##### Items to Verify During Implementation

These features are understood conceptually but need verification during C# implementation:

1. **Interruptible Ranges** - Cumulative delta encoding for fully-interruptible code regions.
   Investigation showed decoded values were too large; may be bit position issue or different
   encoding base. Will verify incrementally during implementation.

2. **Interior/Pinned Pointer Flags** - Slot flags (2 bits after each slot) encode interior=bit0,
   pinned=bit1. Interior pointers point within objects (not at header). Pinned objects can't move.
   Need to verify flag decoding and handle appropriately during root enumeration.

3. **Indirect Liveness** - When liveness flag=1, uses deduplication with live state table.
   16 functions in kernel use this. Will implement after direct liveness works.

4. **Untracked Slots** - 71 functions have untracked slots. These are always-live references
   that don't need per-safe-point tracking. Need to enumerate separately.

#### Thread Suspension (Required - Design for SMP)
GC must be designed for multicore from the start. SMP support may be added before GC implementation.

**Key components:**
- `RhpGcPoll` - Called at safepoints (loop back-edges, method entries)
- `RhpTrapThreads` - Global flag checked by poll, emitted by compiler as data symbol

**Stop-the-world mechanism for SMP:**
1. GC thread sets `RhpTrapThreads = 1`
2. All threads check flag at next safepoint via `RhpGcPoll`
3. Threads reaching safepoint: save context, signal ready, wait
4. GC thread waits for all threads to reach safepoint
5. GC performs collection
6. GC thread clears flag, signals threads to resume

**Implementation needs:**
- Per-thread state: running/suspended/waiting flags
- Synchronization primitive for thread coordination (spinlock or futex-like)
- IPI (Inter-Processor Interrupt) for threads in kernel mode or long-running native code
- Memory barriers to ensure flag visibility across cores

**NativeAOT/CoreCLR approach:**
- Threads voluntarily suspend at safepoints via `RhpGcPoll`
- **Hijacking** for threads not at safepoints: modify return address on stack to redirect
  to suspension helper when method returns (all call sites are GC-safe)
- If thread is in loop without calls, method must be "FullyInterruptible" (GC-safe everywhere)
- Repeat hijack if thread hasn't reached safe point after return

**Our approach:**
- Implement `RhpGcPoll` to check `RhpTrapThreads` flag
- Use IPI (Inter-Processor Interrupt) to interrupt threads on other cores
- IPI handler checks if thread is at safepoint, if not sets pending flag
- Thread checks pending flag on next safepoint or kernel entry/exit
- For kernel threads: ensure kernel code has periodic safepoint checks

Reference: [Thread suspension review](https://github.com/dotnet/runtime/issues/73655)

#### Write Barriers (Verified)
The compiler emits calls to write barrier helpers when storing references to heap locations.

**Functions:**
- `RhpAssignRef(void** dst, void* ref)` - Unchecked write barrier
- `RhpCheckedAssignRef(void** dst, void* ref)` - Checked (validates dst in heap)

**Current implementation:** Simple pointer store (sufficient for mark-sweep)
```csharp
public static unsafe void RhpAssignRef(void** dst, void* r) { *dst = r; }
```

**Full implementation (for generational GC):**
1. Write reference: `mov [rcx], rdx`
2. Check if ref points to ephemeral generation (`g_ephemeral_low` to `g_ephemeral_high`)
3. If yes, update card table: `shr rcx, 11; mov byte [rcx + g_card_table], 0xFF`

**Card table:** Divides heap into 2KB cards (2^11 bytes). Each byte in card table represents
one card. Value 0xFF means "this card contains refs to ephemeral objects - scan during minor GC".

Reference: [WriteBarriers.asm in CoreRT](https://github.com/dotnet/corert/blob/master/src/Native/Runtime/amd64/WriteBarriers.asm)

#### MethodTable Flags (Verified)
Key flags in `MethodTable._usFlags` for GC:
- `HasPointersFlag = 0x01000000` - Type contains GC references (GCDesc present)
- `HasFinalizerFlag = 0x00100000` - Type requires finalization
- `CollectibleFlag = 0x00200000` - For collectible assemblies
- `HasCriticalFinalizerFlag = 0x0002` (extended) - Critical finalizer ordering

#### Finalization (Deferred)
Not needed for initial GC. When implemented:
- Track finalizable objects in separate queue during allocation
- On collection, move unreachable finalizable objects to "f-reachable" queue
- Finalizer thread processes f-reachable queue
- `GC.SuppressFinalize` sets bit in object header to skip finalization

Reference: [Finalization Implementation Details](https://devblogs.microsoft.com/dotnet/finalization-implementation-details/)

#### Weak References / GCHandle (Deferred)
Not needed for initial GC. When implemented:
- `RhHandleAlloc(object, GCHandleType)` - Allocate handle
- `RhHandleGet/Set` - Access handle target
- Handle types: Weak, WeakTrackResurrection, Normal, Pinned
- Handle table scanned as roots, weak handles cleared when target unreachable

#### Allocation Context (Verified)
NativeAOT uses per-thread bump allocation:

**Thread allocation context:**
```csharp
struct AllocationContext {
    void* alloc_ptr;    // Current bump pointer
    void* alloc_limit;  // End of current allocation region
}
```

**RhpNewFast fast path:**
1. Read `base_size` from MethodTable
2. `new_ptr = alloc_ptr + base_size`
3. If `new_ptr <= alloc_limit`: update `alloc_ptr`, return old ptr
4. Else: call `RhpGcAlloc` slow path (may trigger GC)

**Our initial implementation:** Can use simple bump allocator from a GC heap region.
When region exhausted, trigger collection or allocate new region.

### Deliverables
- [x] Object header implementation (16-byte header with block size, flags, sync index, hash)
- [x] GCDesc parsing and reference enumeration (GCDescHelper.EnumerateReferences)
- [x] Static roots enumeration (GCStaticRegion parsing + InitializeStatics)
- [x] Stack map parsing and stack root enumeration (GCInfo decoder + StackRoots)
- [x] GC heap allocation (GCHeap.cs - separate from kernel heap)
- [x] Mark phase (GarbageCollector.cs - stop-the-world, multi-thread)
- [x] Sweep phase (free unmarked objects, add to free list)
- [x] Free list allocator (reuse freed blocks, block splitting)
- [x] Runtime exports working (RhpNewFast uses GCHeap)

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
| 3 ✓ | Mark-Sweep GC | High | Managed heap works, memory reclaimed |
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
