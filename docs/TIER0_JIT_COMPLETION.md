# Tier 0 JIT Completion Assessment

This document assesses the remaining work required to complete the ProtonOS Tier 0 (baseline) JIT compiler before moving to optimizations.

## Executive Summary

The JIT is **functionally complete** for internal testing (110 tests passing). Five gaps exist between testing mode and real .NET assembly execution. Three of these gaps are about **integration** (wiring existing components together), not missing features. Two gaps (funclet-based EH, filter clause execution) are missing runtime features.

| Gap | Status | Effort | Blocks Real Assemblies? |
|-----|--------|--------|------------------------|
| Funclet-Based EH | Handlers inline, need separate funclets | Large | Yes (for nested EH, async) |
| Filter Clause Execution | Parsing done, execution missing | Medium | Only if code uses `catch when` |
| Type Token Resolution | Interface exists, not wired | Small | Yes |
| Field Token Resolution | Interface exists, not wired | Small | Yes |
| Metadata Integration Layer | Components exist separately | Medium-Large | Yes |

---

## Gap 0: Funclet-Based Exception Handling (PREREQUISITE)

### What It Is

Windows x64 SEH and NativeAOT require exception handlers to be compiled as **separate funclets** - small functions with their own `RUNTIME_FUNCTION` and `UNWIND_INFO` entries. Currently, our JIT compiles handlers inline within the main method body.

**Current approach (inline handlers):**
```
Method code:
  [prolog]
  [try body]
  [leave -> jumps over handler]
  [catch handler - inline]   <-- same RUNTIME_FUNCTION as main method
  [finally handler - inline]
  [epilog]

Metadata:
  1 RUNTIME_FUNCTION for entire method
  EH clauses point to offsets within that single function
```

**Required approach (funclet handlers):**
```
Method code:
  [prolog]
  [try body]
  [leave -> jumps to after try]
  [epilog]

Catch funclet (separate function):
  [funclet prolog]
  [handler code]
  [ret]  <-- returns to EH runtime

Finally funclet (separate function):
  [funclet prolog]
  [handler code]
  [ret]

Metadata:
  3 RUNTIME_FUNCTIONs (main + 2 handlers)
  Each funclet has UBF_FUNC_KIND_HANDLER flag
  Funclets reference parent's GCInfo
```

### Why It Matters

1. **Nested exception handling**: When an exception occurs inside a catch/finally handler, the OS needs to unwind the funclet separately from the parent function

2. **Async exceptions**: Thread abort or stack overflow during handler execution requires proper funclet unwinding

3. **Lambda closures**: Lambdas inside catch blocks that capture the exception variable need funclet semantics for proper lifetime

4. **GC during handlers**: GC stack walking needs to enumerate roots in both the parent frame and the funclet frame

5. **NativeAOT compatibility**: NativeAOT's `CoffNativeCodeManager.cpp` expects funclets marked with `UBF_FUNC_KIND_HANDLER` or `UBF_FUNC_KIND_FILTER`

### Current State

**Parsing/Detection (DONE):**
- `IsFunclet()` in `ExceptionHandling.cs:1553` detects funclets in AOT code
- `UBF_FUNC_KIND_HANDLER` and `UBF_FUNC_KIND_FILTER` constants defined
- GCInfo.cs skips funclets during root enumeration (they share parent's GCInfo)

**Inline Handler Execution (WORKS FOR SIMPLE CASES):**
- `ExecuteFinallyHandler()` calls inline finally code via `call_finally_handler` trampoline
- Works because we set up RBP to access parent's locals
- Two-pass exception handling (Test 108, 109) passes

**Missing:**
- JIT does not emit separate code for handlers
- JIT does not create separate `RUNTIME_FUNCTION` entries for handlers
- JIT does not emit funclet prologs/epilogs
- JIT does not mark handlers with `UBF_FUNC_KIND_HANDLER`
- `JITMethodInfo` struct only supports one `RUNTIME_FUNCTION`

### Implementation Required

#### Phase 1: JIT Code Generation Changes
```
1. During compilation, identify EH handler regions
2. Emit handler code into separate memory regions (not inline)
3. Each handler gets:
   - Funclet prolog (set up RBP to access parent locals)
   - Handler IL compiled
   - 'ret' to return to EH runtime
4. Track handler code addresses separately from main method
```

#### Phase 2: Metadata Generation Changes
```
1. Create RUNTIME_FUNCTION for each handler:
   - BeginAddress = handler start RVA
   - EndAddress = handler end RVA
   - UnwindInfoAddress points to funclet's UNWIND_INFO

2. Create UNWIND_INFO for each handler:
   - Version = 1, Flags = 0 (no nested handlers)
   - Frame register points to parent frame
   - UnwindBlockFlags has UBF_FUNC_KIND_HANDLER or UBF_FUNC_KIND_FILTER

3. Register all RUNTIME_FUNCTIONs (main + handlers) together
```

#### Phase 3: JITMethodInfo Extension
```
1. Change from single RuntimeFunction to array:
   - RuntimeFunction[MaxFunclets]  // main + handlers
   - FuncletCount field
   - Per-funclet UnwindInfo storage

2. Or: allocate funclet metadata separately in code heap
```

### Effort: Large (5-10 days)

This is the most significant architectural change remaining:
- Requires changes to `ILCompiler.cs` code emission strategy
- Requires changes to `JITMethodInfo` structure
- Requires changes to `JITMethodRegistry` registration
- May need changes to `ExceptionHandling.cs` funclet invocation
- Extensive testing for various EH scenarios

### Priority: HIGH for production use

While our current inline approach works for simple throw/catch/finally (Tests 92, 108, 109), it will fail for:
- Exceptions thrown inside catch handlers
- Exceptions thrown inside finally handlers
- Code that uses `catch when` filters
- Async code with exception handlers
- Debugger attach during exception handling

### Dependencies

The following gaps should be completed AFTER funclet support:
- **Filter Clause Execution** - Filters are funclets, need this infrastructure first
- Any advanced EH scenarios in the Metadata Integration Layer

---

## Gap 1: Filter Clause Execution

### What It Is
ECMA-335 defines four EH clause types:
- `catch` (Typed) - ✅ Implemented
- `finally` - ✅ Implemented
- `fault` - ✅ Implemented
- `filter` - ❌ Parsing done, execution missing

Filter clauses are `catch when (expression)` in C#:
```csharp
try { ... }
catch (Exception e) when (e.Message.Contains("specific")) { ... }
```

### Current State

**Parsing (DONE):**
- `EHClauseKind.Filter` enum defined (`ExceptionHandling.cs:289`)
- `NativeAotEHClause.FilterOffset` field exists (`ExceptionHandling.cs:302`)
- NativeAOT EH info parsing reads filter offset (`ExceptionHandling.cs:1791-1795`)
- JIT emits `endfilter` opcode correctly (`ILCompiler.cs:5283-5308`)

**Execution (MISSING):**
- `FindCatchClause` only matches `EHClauseKind.Typed` (`ExceptionHandling.cs:1826`)
- No code path to:
  1. Execute the filter funclet
  2. Check filter return value (0 = don't handle, 1 = handle)
  3. Continue to handler if filter returns 1

### Implementation Required

```
In ExceptionHandling.cs FindCatchClause():
1. If kind == EHClauseKind.Filter:
   a. Call the filter funclet at FilterOffset
   b. Filter receives exception object in a register
   c. Filter returns int32: 0 = pass, 1 = handle
   d. If returns 1, set clause and return true
   e. If returns 0, continue searching
```

### Effort: Medium (1-2 days)
- Need to call filter funclet (similar to finally handler execution)
- Need to pass exception object to filter
- Need to check return value and branch accordingly

### Priority: Low (but depends on Gap 0)
Filter clauses are rarely used in practice. Most code uses typed catch blocks.

**Note:** Filter expressions are funclets. Implementing filter execution properly requires the funclet infrastructure from Gap 0. The filter funclet receives the exception object, evaluates the condition, and returns 0 or 1. This requires proper funclet prolog/epilog and `RUNTIME_FUNCTION` registration.

---

## Gap 2: Type Token Resolution (TypeResolver)

### What It Is
CIL instructions reference types via metadata tokens:
- `newarr` (0x8D) - create array of element type
- `castclass` (0x74) - cast object to type
- `isinst` (0x75) - test if object is type
- `ldtoken` (0xD0) - load type handle

The JIT needs to convert these 32-bit tokens to `MethodTable*` pointers.

### Current State

**JIT Interface (DONE):**
```csharp
// ILCompiler.cs:376
public unsafe delegate bool TypeResolver(uint token, out void* methodTablePtr);

// ILCompiler.cs:540
public void SetTypeResolver(TypeResolver resolver)
```

**Usage Pattern (DONE):**
```csharp
// ILCompiler.cs:5229
if (_typeResolver != null)
{
    void* mtPtr;
    if (_typeResolver(token, out mtPtr) && mtPtr != null)
    {
        handleValue = (ulong)mtPtr;
    }
}
```

**Fallback for Testing (DONE):**
Without resolver, token is used directly as value (works for test harness).

**MetadataReader (EXISTS BUT NOT WIRED):**
- `FindTypeDef()` - find type by namespace/name
- `ResolveTypeRefLocal()` - resolve TypeRef to TypeDef
- `GetTypeDefExtends()` - get base type
- `GetTypeDefInterfaces()` - get implemented interfaces

### What's Missing
A `TypeResolver` implementation that:
1. Decodes the metadata token (table ID + row index)
2. For TypeDef tokens: looks up in MethodTable registry
3. For TypeRef tokens: resolves to TypeDef, then looks up MethodTable
4. For TypeSpec tokens: handles generic instantiations (future)

### Effort: Small (0.5-1 day)
The MetadataReader already has the lookup functions. Need to:
- Create a TypeResolver delegate that calls MetadataReader
- Build a TypeDef-to-MethodTable mapping (may already exist for AOT types)

---

## Gap 3: Field Token Resolution (FieldResolver)

### What It Is
CIL instructions reference fields via metadata tokens:
- `ldfld` (0x7B) - load instance field
- `stfld` (0x7D) - store instance field
- `ldsfld` (0x7E) - load static field
- `stsfld` (0x80) - store static field

### Current State

**JIT Interface (DONE):**
```csharp
// ILCompiler.cs:385-415
public struct ResolvedField
{
    public int Offset;           // For instance fields
    public int Size;             // Field size in bytes
    public bool IsSigned;        // Sign extension needed
    public bool IsStatic;        // Static vs instance
    public void* StaticAddress;  // For static fields
    public bool IsValid;
}

public unsafe delegate bool FieldResolver(uint token, out ResolvedField result);
```

**Usage Pattern (DONE):**
```csharp
// ILCompiler.cs:4056
if (_fieldResolver != null && _fieldResolver(token, out var field) && field.IsValid)
{
    offset = field.Offset;
    size = field.Size;
    signed = field.IsSigned;
}
else
{
    DecodeFieldToken(token, out offset, out size, out signed);
}
```

**Fallback for Testing (DONE):**
- Instance fields: token encodes offset/size directly (bits 0-15: offset, 16-23: size, 24-31: flags)
- Static fields: token IS the memory address

**MetadataReader (EXISTS BUT NOT WIRED):**
- `FindFieldByName()` - find field by name in a type
- `GetFieldFlags()` - get field attributes
- `GetFieldSignature()` - get field type signature
- `GetFieldLayoutOffset()` - get explicit field offset
- `GetFieldRvaRva()` - get RVA for static fields with initial data

### What's Missing
A `FieldResolver` implementation that:
1. Decodes the metadata token (Field or MemberRef table)
2. Looks up field in parent type
3. Computes offset from type layout
4. Gets size from field signature
5. For statics: computes address in static data segment

### Effort: Small-Medium (1-2 days)
- Need type layout calculation (sum of preceding field sizes)
- Need to handle explicit layout ([StructLayout], [FieldOffset])
- Need static field address calculation

---

## Gap 4: Metadata Integration Layer

### What It Is
The "glue" that connects metadata tokens to runtime artifacts (MethodTable pointers, native code addresses, static field locations).

### Current State

**MetadataReader (COMPLETE):**
3700+ lines of ECMA-335 compliant metadata parsing:
- All metadata tables parsed
- String/Blob/GUID heaps accessible
- Method signatures decoded
- Type hierarchy traversal
- MemberRef resolution

**Method Resolution (PARTIALLY DONE):**
```csharp
// CompiledMethodRegistry for tracking JIT'd methods
// MethodResolver delegate for custom resolution
```

**String Resolution (INTERFACE DONE):**
```csharp
// StringResolver delegate
// MetadataReader.GetUserString() exists
```

### What's Missing

1. **Token-to-MethodTable Registry**
   - Map TypeDef row IDs to MethodTable pointers
   - Handle AOT-compiled types (from bflat)
   - Handle runtime-generated types (none yet)

2. **Static Field Storage**
   - Allocate memory for static fields
   - Initialize from FieldRVA data
   - Track addresses for stsfld/ldsfld

3. **Assembly Loading Coordination**
   - Currently hardcoded to parse embedded test assembly
   - Need to load korlib types
   - Need to resolve cross-assembly TypeRefs

4. **MethodTable Generation**
   - AOT types have MethodTables from bflat
   - For JIT'd types, would need to generate MethodTables
   - (Not needed for Tier 0 - we JIT methods, not types)

### Effort: Medium-Large (3-7 days)
This is the largest gap but also the least urgent for basic JIT testing. The pieces exist; they need integration.

---

## Recommendations

### For Moving to Optimization Phase

The JIT is **ready for optimization work** on simple code paths. All 220+ CIL opcodes are implemented. 110 tests pass. The testing infrastructure validates code generation.

However, **funclet-based EH is required for production use**. The current inline handler approach will fail for nested exceptions and advanced scenarios.

### Recommended Priority Order

For production-ready Tier 0 JIT:

| Order | Gap | Effort | Rationale |
|-------|-----|--------|-----------|
| 1 | **Funclet-Based EH** | 5-10 days | Prerequisite for robust EH, filter clauses depend on it |
| 2 | **Filter Clause Execution** | 1-2 days | Completes EH support, requires funclet infrastructure |
| 3 | **TypeResolver** | 0.5-1 day | Unblocks castclass/isinst/newarr for real assemblies |
| 4 | **FieldResolver** | 1-2 days | Unblocks field access for real assemblies |
| 5 | **Integration Layer** | 3-7 days | Full assembly loading, ties everything together |

### Alternative: Parallel Tracks

If optimization work is urgent:

**Track A (Optimizations):**
- Proceed with register allocation, constant folding
- Use existing test infrastructure with hand-crafted IL
- Funclet issues won't affect optimizer correctness

**Track B (Completeness):**
- Implement funclet-based EH
- Wire up token resolvers
- Build integration layer

These can proceed in parallel since they touch different subsystems.

### Testing Strategy

**Current (sufficient for optimizations):**
- Hand-crafted IL bytecode
- Controlled token values
- Direct address injection
- Simple EH scenarios (no nested handlers)

**For production:**
- Real .NET assemblies require integration layer
- Nested exception tests require funclet support
- `catch when` tests require filter execution

---

## Appendix: File Locations

| Component | File | Lines |
|-----------|------|-------|
| JIT Compiler | `src/kernel/Runtime/JIT/ILCompiler.cs` | ~5500 |
| x64 Emitter | `src/kernel/Runtime/JIT/X64Emitter.cs` | ~2000 |
| Exception Handling | `src/kernel/x64/ExceptionHandling.cs` | ~2200 |
| MethodTable | `src/kernel/Runtime/MethodTable.cs` | ~430 |
| Metadata Reader | `src/kernel/Runtime/MetadataReader.cs` | ~3700 |
| JIT Tests | `src/kernel/Tests.cs` | ~13000 |
| EH Clauses | `src/kernel/Runtime/JIT/EHClauses.cs` | ~160 |
| JIT Method Info | `src/kernel/Runtime/JIT/JITMethodInfo.cs` | ~520 |
