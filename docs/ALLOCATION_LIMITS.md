# ProtonOS Fixed Allocation Limits

This document tracks all fixed-size allocations in the kernel that could be exhausted as the system grows with more types, methods, drivers, and tests.

## Status Legend
- 🔴 HIGH RISK: Likely to exhaust with moderate growth
- 🟡 MEDIUM RISK: Could exhaust with significant growth
- 🟢 LOW RISK: Unlikely to exhaust in normal use
- ⚪ HARDWARE: Tied to physical hardware limits

---

## High Priority (Growing with JIT'd code)

### 🔴 JITMethodRegistry.MaxMethods = 1024
- **File**: `src/kernel/Runtime/JIT/JITMethodInfo.cs:820`
- **Warning**: "Method limit reached"
- **Current usage**: ~300+ methods (300 tests + framework methods)
- **Growth rate**: Every JIT'd method with exception handlers
- **Impact**: Registration fails, EH stops working for new methods
- **Recommendation**: Increase to 4096 or make dynamic

### 🔴 ExceptionHandling.MaxFunctionTables = 2048
- **File**: `src/kernel/x64/ExceptionHandling.cs:686`
- **Fixed storage**: `fixed byte Data[2048 * 40]` (~80KB)
- **Warning**: "[Tier0JIT] WARNING: Failed to register method"
- **Current usage**: Was hitting 512/512, now 2048
- **Growth rate**: Every JIT'd method needs an entry
- **Impact**: Exception handlers stop working
- **Recommendation**: Already increased; consider dynamic allocation

### 🔴 TypeRegistry.MaxTypes = 256 (per assembly)
- **File**: `src/kernel/Runtime/AssemblyLoader.cs:39`
- **Warning**: Silent (returns false)
- **Growth rate**: Every unique type in an assembly
- **Impact**: Type resolution fails, objects can't be created
- **Recommendation**: Increase to 1024 or make dynamic

### 🔴 MetadataIntegration.MaxTypeEntries = 512
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs:130`
- **Warning**: "Type registry full"
- **Growth rate**: All types across all assemblies
- **Impact**: Type resolution fails
- **Recommendation**: Increase to 2048 or make dynamic

### 🟡 MetadataIntegration.MaxFieldLayoutEntries = 512
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs:145`
- **Warning**: "Field layout cache full"
- **Growth rate**: Every unique type's field layout
- **Impact**: Field access may fail or require re-computation
- **Recommendation**: Increase to 2048

### 🟡 MetadataIntegration.MaxCctorEntries = 256
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs:175`
- **Warning**: "Cctor registry full"
- **Growth rate**: Every type with static constructor
- **Impact**: Static constructors won't be tracked
- **Recommendation**: Increase to 1024

### 🟡 MetadataIntegration.MaxStaticFields = 256
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs:135`
- **Warning**: "Static field registry full"
- **Growth rate**: Every static field across all types
- **Impact**: Static field access may fail
- **Recommendation**: Increase to 1024

### 🟡 ReflectionRuntime.MaxTypeInfoEntries = 512
- **File**: `src/kernel/Runtime/Reflection/ReflectionRuntime.cs:32`
- **Warning**: "Type info registry full"
- **Growth rate**: Every type registered for reflection
- **Impact**: Type info lookup fails
- **Recommendation**: Match MetadataIntegration.MaxTypeEntries

---

## Medium Priority (Caching/optimization limits)

### 🟡 AssemblyLoader.MaxArrayMTCache = 128
- **File**: `src/kernel/Runtime/AssemblyLoader.cs:5450`
- **Warning**: Silent (stops caching, not fatal)
- **Growth rate**: Every unique array element type
- **Impact**: Performance (cache misses cause MT recreation)
- **Recommendation**: Increase to 512

### 🟡 AssemblyLoader.MaxGenericInstCache = 64
- **File**: `src/kernel/Runtime/AssemblyLoader.cs:5547`
- **Warning**: Silent (stops caching)
- **Growth rate**: Every unique generic instantiation
- **Impact**: Performance (cache misses cause MT recreation)
- **Recommendation**: Increase to 256

### 🟢 AotMethodRegistry.MaxEntries = 128
- **File**: `src/kernel/Runtime/AotMethodRegistry.cs:47`
- **Warning**: On overflow (prevents registration)
- **Growth rate**: BCL method registrations
- **Impact**: AOT method lookup fails
- **Recommendation**: Increase to 256

### 🟢 StringPool.MaxTokenCacheSize = 1024
- **File**: `src/kernel/Runtime/StringPool.cs:29`
- **Growth rate**: Unique string literals
- **Impact**: Cache misses
- **Recommendation**: Likely sufficient

---

## Per-Method Limits (Reasonable)

These are per-method limits and unlikely to be exceeded in practice:

| Limit | Value | File |
|-------|-------|------|
| MaxStackDepth | 32 | ILCompiler.cs:617 |
| MaxBranches | 64 | ILCompiler.cs:623 |
| MaxLabels | 512 | ILCompiler.cs:631 |
| MaxLocals | 64 | ILCompiler.cs:640 |
| MaxArgs | 32 | ILCompiler.cs:641 |
| MaxFinallyCalls | 16 | ILCompiler.cs:680 |
| MaxEHClauses | 16/32 | JITMethodInfo.cs, EHClauses.cs |
| MaxMethodTypeArgs | 8 | MetadataIntegration.cs:160 |
| MaxTypeTypeArgs | 8 | MetadataIntegration.cs:169 |
| MaxSlots (GCInfo) | 32 | JITGCInfo.cs:44 |
| MaxSafePoints | 64 | JITGCInfo.cs:45 |

---

## Hardware-Related (Static)

These are tied to hardware limits and don't grow with software:

| Limit | Value | Purpose |
|-------|-------|---------|
| MaxCpus | 64 | CPUTopology.cs |
| MaxIOApics | 8 | CPUTopology.cs, IOAPIC.cs |
| MaxOverrides | 24 | CPUTopology.cs |
| MaxNodes | 16 | NumaTopology.cs, PageAllocator.cs |
| MaxMemoryRanges | 32 | NumaTopology.cs |
| MaxCpuAffinities | 64 | NumaTopology.cs |
| MaxDevices | 64 | PCI.cs |
| MaxAssemblies | 32 | AssemblyLoader.cs |
| MaxEnvironmentVariables | 256 | Environment.cs |
| MaxExports | 128 | KernelExportRegistry.cs |

---

## Recommendations

### Short-term: Increase Limits
Double or quadruple the high-priority limits:
- MaxMethods: 1024 → 4096
- MaxTypes (per assembly): 256 → 1024
- MaxTypeEntries: 512 → 2048
- MaxFieldLayoutEntries: 512 → 2048
- MaxCctorEntries: 256 → 1024
- MaxStaticFields: 256 → 1024

### Medium-term: Block Allocator
Create a shared growable block allocator for registries:
- Allocates fixed-size blocks (e.g., 64 entries)
- Chains blocks together as needed
- No hard limit (grows until memory exhausted)
- Common implementation reusable across registries

### Long-term: Dynamic Resizing
Implement true dynamic arrays with reallocation:
- Start small (e.g., 64 entries)
- Double capacity when needed
- Copy existing data to new allocation
- More complex but most flexible

---

## Change Log

### 2025-12 Function Table Fix
- Increased MaxFunctionTables from 512 to 2048
- Root cause of exception handler failures with 300+ tests
