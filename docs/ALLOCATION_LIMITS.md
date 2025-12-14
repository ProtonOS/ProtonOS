# ProtonOS Fixed Allocation Limits

This document tracks all fixed-size allocations in the kernel that could be exhausted as the system grows with more types, methods, drivers, and tests.

## Status Legend
- ✅ CONVERTED: Now uses block allocator (no fixed limit)
- 🔴 HIGH RISK: Likely to exhaust with moderate growth
- 🟡 MEDIUM RISK: Could exhaust with significant growth
- 🟢 LOW RISK: Unlikely to exhaust in normal use
- ⚪ HARDWARE: Tied to physical hardware limits

---

## Converted to Block Allocator (No Fixed Limit)

These registries now use the BlockAllocator with small block sizes (32 entries) to enable unlimited growth and exercise block allocation during tests.

### ✅ ExceptionHandling.FunctionTableStorage
- **File**: `src/kernel/x64/ExceptionHandling.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 2048 entries (previously 512)
- **Now**: Grows dynamically, no hard limit

### ✅ JITMethodRegistry
- **File**: `src/kernel/Runtime/JIT/JITMethodInfo.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 1024 entries
- **Now**: Grows dynamically, no hard limit

### ✅ TypeRegistry (per assembly)
- **File**: `src/kernel/Runtime/AssemblyLoader.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 256 entries per assembly
- **Now**: Grows dynamically, no hard limit

### ✅ StaticFieldStorage (per assembly)
- **File**: `src/kernel/Runtime/AssemblyLoader.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 256 field entries per assembly
- **Now**: Grows dynamically, no hard limit
- **Note**: Storage block (64KB) is still fixed

### ✅ MetadataIntegration.TypeRegistry (global AOT types)
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 512 entries
- **Now**: Grows dynamically, no hard limit

### ✅ MetadataIntegration.StaticFieldRegistry (global AOT statics)
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 256 entries
- **Now**: Grows dynamically, no hard limit
- **Note**: Static storage block (64KB) is still fixed

### ✅ MetadataIntegration.FieldLayoutCache
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 512 entries
- **Now**: Grows dynamically, no hard limit

### ✅ MetadataIntegration.CctorRegistry
- **File**: `src/kernel/Runtime/JIT/MetadataIntegration.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 256 entries
- **Now**: Grows dynamically, no hard limit

### ✅ ReflectionRuntime.TypeInfoRegistry
- **File**: `src/kernel/Runtime/Reflection/ReflectionRuntime.cs`
- **Block size**: 32 entries per block
- **Was**: Fixed 512 entries
- **Now**: Grows dynamically, no hard limit

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

### Completed: Block Allocator
Created `BlockAllocator.cs` providing growable block-based storage:
- Allocates fixed-size blocks (32 entries by default for testing)
- Chains blocks together as needed
- No hard limit (grows until memory exhausted)
- Common implementation reusable across registries
- Applied to 9 critical registries (all high-priority conversions complete)

### Future: Convert Medium-Priority Caches
Could apply block allocator to caching structures for consistency:
- AssemblyLoader.MaxArrayMTCache → BlockAllocator
- AssemblyLoader.MaxGenericInstCache → BlockAllocator

These are lower priority since cache exhaustion only affects performance, not correctness.

---

## Change Log

### 2025-12 MetadataIntegration/ReflectionRuntime Conversion
- Converted MetadataIntegration.TypeRegistry (global AOT types) to block allocator
- Converted MetadataIntegration.StaticFieldRegistry to block allocator
- Converted MetadataIntegration.FieldLayoutCache to block allocator
- Converted MetadataIntegration.CctorRegistry to block allocator
- Converted ReflectionRuntime.TypeInfoRegistry to block allocator
- Updated PrintStatistics() to report block chain statistics
- All 300 tests pass with 9 registries using block allocator

### 2025-12 Block Allocator Implementation (Phase 1)
- Created `BlockAllocator.cs` with generic block chain implementation
- Converted FunctionTableStorage to block allocator (was hitting 512 limit)
- Converted JITMethodRegistry to block allocator
- Converted TypeRegistry (per-assembly) to block allocator
- Converted StaticFieldStorage field entries to block allocator
- All registries use 32-entry blocks to exercise growth during tests

### 2025-12 Function Table Fix (Initial)
- Increased MaxFunctionTables from 512 to 2048
- Root cause of exception handler failures with 300+ tests
