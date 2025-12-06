# Kernel Exports

This document describes the kernel export structure for managed code (korlib, JIT-compiled drivers).

## Export Categories

### 1. PAL Exports (Internal AOT Linkage)

These exports are used internally by NativeAOT-compiled code and are linked statically by bflat.
They use the `Pal` prefix and live in `src/kernel/PAL/`.

| Export | File | Purpose |
|--------|------|---------|
| `PalAllocObject` | Memory.cs | Object allocation for GC |
| `PalFailFast` | Environment.cs | Fatal error handling |

### 2. Reflection Exports (korlib)

These exports are called by korlib via `DllImport("*")` for runtime reflection.
They use the `Reflection_` prefix and live in `src/kernel/Exports/Reflection/` (namespace `ProtonOS.Exports.Reflection`).

| Export | Purpose |
|--------|---------|
| `Reflection_GetTypeInfo` | Get assembly ID and type token from MethodTable |
| `Reflection_GetTypeName` | Get type name from TypeDef token |
| `Reflection_GetTypeNamespace` | Get type namespace from TypeDef token |
| `Reflection_GetTypeFlags` | Get TypeDef attributes/flags |
| `Reflection_GetTypeBaseType` | Get base type token |
| `Reflection_GetTypeMethodTable` | Get MethodTable pointer for type |
| `Reflection_InvokeMethod` | Invoke a method by token |
| `Reflection_GetMethodName` | Get method name from token |
| `Reflection_GetMethodCount` | Get method count for a type |
| `Reflection_GetMethodToken` | Get method token by index |
| `Reflection_IsMethodStatic` | Check if method is static |
| `Reflection_IsMethodVirtual` | Check if method is virtual |
| `Reflection_GetFieldValue` | Get field value |
| `Reflection_SetFieldValue` | Set field value |
| `Reflection_GetFieldValueRaw` | Get raw field bytes |
| `Reflection_GetFieldCount` | Get field count for a type |
| `Reflection_GetFieldToken` | Get field token by index |
| `Reflection_GetFieldName` | Get field name from token |
| `Reflection_GetFieldElementType` | Get field element type for boxing |
| `Reflection_GetAssemblyCount` | Get number of loaded assemblies |
| `Reflection_GetAssemblyIdByIndex` | Get assembly ID by index |
| `Reflection_GetTypeCount` | Get type count in assembly |
| `Reflection_GetTypeTokenByIndex` | Get type token by index |
| `Reflection_FindTypeByName` | Find type by name/namespace |

### 3. DDK Exports (JIT-compiled drivers)

These exports are called by JIT-compiled drivers via `UnmanagedCallersOnly`.
They use the `Kernel_` prefix and live in `src/kernel/Exports/DDK/` (namespace `ProtonOS.Exports.DDK`).

#### CPU Topology (`CPU.cs`)

| Export | Purpose |
|--------|---------|
| `Kernel_GetCpuCount` | Get number of CPUs |
| `Kernel_GetCurrentCpu` | Get current CPU index |
| `Kernel_GetCpuInfo` | Get CPU info structure |
| `Kernel_SetThreadAffinity` | Set thread CPU affinity mask |
| `Kernel_GetThreadAffinity` | Get thread CPU affinity mask |
| `Kernel_IsCpuOnline` | Check if CPU is online |
| `Kernel_GetBspIndex` | Get Bootstrap Processor index |
| `Kernel_GetSystemAffinityMask` | Get system-wide CPU mask |

#### NUMA Topology (`NUMA.cs`)

| Export | Purpose |
|--------|---------|
| `Kernel_GetNumaNodeCount` | Get number of NUMA nodes |
| `Kernel_GetNumaNodeForCpu` | Get NUMA node for CPU |
| `Kernel_GetCurrentNumaNode` | Get current CPU's NUMA node |
| `Kernel_GetNumaDistance` | Get distance between nodes |
| `Kernel_GetNumaNodeInfo` | Get NUMA node info structure |
| `Kernel_GetNumaNodeForAddress` | Get NUMA node for address |
| `Kernel_AllocatePageLocal` | Allocate page from local node |
| `Kernel_AllocatePageFromNode` | Allocate page from specific node |
| `Kernel_AllocatePagesFromNode` | Allocate contiguous pages |
| `Kernel_GetPageNumaNode` | Get NUMA node for page |
| `Kernel_SetThreadNumaNode` | Set thread's preferred node |
| `Kernel_GetThreadNumaNode` | Get thread's preferred node |
| `Kernel_IsNumaAvailable` | Check if NUMA info available |
| `Kernel_HasNumaDistanceMatrix` | Check if distance matrix available |

## Directory Structure

```
src/kernel/
  PAL/                          # Win32 compatibility layer (internal AOT)
    Memory.cs                   # PalAllocObject
    Environment.cs              # PalFailFast
    ...                         # Other Win32 APIs (internal use)

  Exports/                      # External APIs for managed code
    DDK/                        # Driver Development Kit exports
      CPU.cs                    # Kernel_* CPU topology APIs
      NUMA.cs                   # Kernel_* NUMA APIs
    Reflection/                 # Reflection exports for korlib
      ReflectionExports.cs      # Reflection_* APIs

  Runtime/                      # JIT and managed code execution
    Reflection/
      ReflectionRuntime.cs      # Implementation (called by ReflectionExports)
```

## Naming Conventions

| Prefix | Usage | Called By |
|--------|-------|-----------|
| `Pal` | Internal AOT linkage | bflat-compiled kernel code |
| `Reflection_` | Reflection/type system | korlib via DllImport |
| `Kernel_` | DDK/architecture APIs | JIT-compiled drivers |

## Adding New Exports

### For korlib (Reflection_*)
1. Add implementation to `Runtime/Reflection/ReflectionRuntime.cs`
2. Add export wrapper to `Exports/Reflection/ReflectionExports.cs` with `[RuntimeExport]`
3. Add `DllImport` declaration in korlib

### For DDK (Kernel_*)
1. Create or update file in `Exports/DDK/`
2. Add function with `[UnmanagedCallersOnly(EntryPoint = "Kernel_...")]`
3. Document in DDK header files
