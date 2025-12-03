# Funclet-Based Exception Handling Design

## Overview

This document describes the design for implementing proper funclet-based exception handling in the ProtonOS JIT. This replaces the temporary inline handler approach with full Windows x64 SEH compliance, matching our NativeAOT-based AOT code.

**Key Decision:** We will fully transition to funclets for ALL exception handling. The inline handler approach will be removed, not maintained alongside funclets. This ensures:
- Complete Windows x64 SEH compliance
- Compatibility with NativeAOT runtime expectations
- Proper nested exception handling
- Consistent behavior between JIT and AOT code

## Current State (Inline Handlers)

Currently, exception handlers are compiled inline with the main method body:

```
Main method code:
  0x00: push rbp
  0x01: mov rbp, rsp
  0x04: sub rsp, 0x20
        ; try block
  0x08: ... main code ...
  0x20: leave L_end           ; jump over handlers
        ; catch handler (inline)
  0x25: ... catch code ...
  0x35: ret                   ; endfinally
        ; finally handler (inline)
  0x40: ... finally code ...
  0x50: ret                   ; endfinally
  L_end:
  0x55: ... after try ...
  0x60: mov rsp, rbp
  0x63: pop rbp
  0x64: ret

Metadata:
  1 RUNTIME_FUNCTION: [0x00, 0x65]
  EH clauses point to offsets 0x25, 0x40
```

Problems:
- Nested exceptions inside handlers can't unwind properly
- No way for OS to distinguish handler from main code
- GC can't distinguish handler stack frame from main frame

## Target State (Funclet Handlers)

Each handler becomes a separate "funclet" - a mini-function with its own unwind info:

```
Main method code:
  0x00: push rbp
  0x01: mov rbp, rsp
  0x04: sub rsp, 0x20
        ; try block
  0x08: ... main code ...
  0x20: leave L_end
  L_end:
  0x25: ... after try ...
  0x30: mov rsp, rbp
  0x33: pop rbp
  0x34: ret

Catch funclet (separate code region):
  0x100: push rbp              ; Funclet prolog
  0x101: mov rbp, rdx          ; RDX = parent frame pointer from EH runtime
  0x104: ... catch code ...
  0x114: pop rbp               ; Funclet epilog
  0x115: ret

Finally funclet (separate code region):
  0x200: push rbp
  0x201: mov rbp, rdx
  0x204: ... finally code ...
  0x214: pop rbp
  0x215: ret

Metadata:
  RUNTIME_FUNCTION[0]: [0x00, 0x35] - main method, UBF_FUNC_KIND_ROOT
  RUNTIME_FUNCTION[1]: [0x100, 0x116] - catch, UBF_FUNC_KIND_HANDLER
  RUNTIME_FUNCTION[2]: [0x200, 0x216] - finally, UBF_FUNC_KIND_HANDLER
```

## Implementation Strategy

### Phase 1: Data Structure Changes

#### 1.1 FuncletInfo Structure
```csharp
public struct FuncletInfo
{
    public uint CodeOffset;       // Offset from main method start
    public uint CodeSize;         // Size of funclet code
    public EHClauseKind Kind;     // Handler, Filter
    public byte EHClauseIndex;    // Which clause this funclet belongs to
    // UnwindInfo embedded or allocated separately
}
```

#### 1.2 JITMethodInfo Extension
```csharp
public const int MaxFunclets = 16;

public int FuncletCount;
public fixed byte _funcletData[MaxFunclets * sizeof(FuncletInfo)];
public fixed byte _funcletUnwindData[MaxFunclets * 32]; // Per-funclet UNWIND_INFO
```

### Phase 2: Compilation Changes

#### 2.1 Two-Pass Compilation

**Pass 1: Main method body (excluding handler regions)**
- Compile IL from start to first handler
- Skip handler IL regions (track their offsets)
- Continue with any code after handlers
- Record where each handler IL region maps

**Pass 2: Funclets**
- For each handler region:
  - Emit funclet prolog
  - Compile handler IL
  - Emit funclet epilog (ret)
  - Record funclet bounds

#### 2.2 Funclet Prolog/Epilog

**Prolog:**
```asm
push rbp                    ; Save our RBP
mov rbp, rdx                ; RDX = parent frame pointer (from EH runtime)
```

**Epilog:**
```asm
pop rbp                     ; Restore our RBP
ret                         ; Return to EH runtime
```

#### 2.3 Leave Instruction Change

Current `leave` jumps over inline handler. With funclets:
- `leave` just jumps to the target (no handler to skip)
- Or if finally exists, EH runtime will call the funclet

### Phase 3: Registration Changes

#### 3.1 Multiple RUNTIME_FUNCTION Registration

```csharp
public void RegisterMethodWithFunclets(
    JITMethodInfo* info,
    FuncletInfo* funclets,
    int funcletCount)
{
    // Allocate array of RUNTIME_FUNCTIONs (1 + funcletCount)
    // Register all with ExceptionHandling.AddFunctionTable
}
```

#### 3.2 EH Clause Updates

EH clause HandlerOffset now points to funclet code (RVA from image base).

### Phase 4: Runtime Changes

#### 4.1 Funclet Detection

Already implemented in `IsFunclet()` - checks UBF_FUNC_KIND_MASK.

#### 4.2 Handler Invocation

For funclets, the EH runtime calls the funclet with:
- RCX = exception object (for catch)
- RDX = parent frame pointer

The funclet returns a value indicating disposition (for filters).

## Code Layout Options

### Option A: Contiguous Allocation
All code (main + funclets) in one contiguous region:
```
[main method code][funclet 1][funclet 2]...
```
- Simple allocation
- All code in same cache region
- But requires knowing total size upfront

### Option B: Separate Allocation
Funclets allocated in separate code regions:
- More flexible
- But fragmented memory
- Need separate W^X handling for each

**Recommendation: Option A** - Compile main method first, then append funclets.

## Memory Layout

```
JITMethodInfo:
  +0x00: CodeBase
  +0x08: Function (main RUNTIME_FUNCTION)
  +0x14: PrologSize, UnwindCodeCount, etc.
  +0x18: _unwindData[64] (main method UNWIND_INFO)
  +0x58: _ehClauseData[256]
  +0x158: _gcInfoData[128]
  +0x1D8: GCInfoSize
  +0x1DC: FuncletCount                        // NEW
  +0x1E0: FuncletFunctions[MaxFunclets]       // NEW - array of RuntimeFunction
  +0x2E0: FuncletUnwindData[MaxFunclets][32]  // NEW - per-funclet UNWIND_INFO
```

## Testing Plan

1. **Test 111: Simple funclet compilation**
   - Method with try/finally
   - Verify finally executes when no exception
   - Verify finally executes when exception thrown

2. **Test 112: Nested exception in handler**
   - Throw exception inside catch block
   - Verify inner exception is handled
   - Verify outer finally still runs

3. **Test 113: Multiple funclets**
   - try/catch/finally/fault
   - Verify correct handler selection

4. **Test 114: Filter funclet**
   - catch when (condition)
   - Verify filter receives exception
   - Verify filter return value controls handling

## Migration Path

1. Implement funclet support in parallel with existing inline handlers
2. Add a test (Test 111+) that uses funclet compilation
3. Once funclets work, update all existing EH tests to use funclets
4. Remove the inline handler code path entirely
5. Remove `call_finally_handler` assembly helper (no longer needed)

**Note:** We are NOT maintaining backward compatibility with inline handlers. Once funclets are implemented, inline handlers will be removed completely.
