# Multi-Assembly Loader Design

## Overview

This document describes the refactored assembly loading system for ProtonOS that supports:
- Multiple assemblies loaded simultaneously
- Hot-loading and unloading of driver assemblies
- Per-assembly metadata context and JIT state
- Assembly dependency resolution (TypeRef → TypeDef across assemblies)

## Current State (Problems)

The current implementation has several limitations:

1. **Single Assembly Hardcoded**: `Kernel.cs` has `_testAssembly`, `_testMetadataRoot`, etc. as static fields for one assembly.

2. **Global Token Namespace**: `MetadataIntegration` stores types/fields in flat arrays keyed only by token. Token 0x02000001 in Assembly A would collide with token 0x02000001 in Assembly B.

3. **Scattered Code**: Assembly loading code is embedded in `Kernel.cs` instead of being a self-contained module.

4. **No Unloading Support**: No mechanism to track what was loaded per-assembly for cleanup.

## Design Goals

1. **Per-Assembly Context**: Each loaded assembly gets its own `LoadedAssembly` structure containing all metadata and JIT state.

2. **Assembly Registry**: Central `AssemblyLoader` manages all loaded assemblies and provides cross-assembly resolution.

3. **Qualified Tokens**: All token lookups use (assemblyId, token) tuples to avoid namespace collisions.

4. **Unloadable**: Each assembly tracks its allocations (code, static fields, types) for clean unloading.

5. **Modular**: New `src/kernel/Runtime/AssemblyLoader.cs` contains all assembly loading logic.

## Core Data Structures

### LoadedAssembly

Represents a single loaded assembly with all its metadata and runtime state:

```csharp
public unsafe struct LoadedAssembly
{
    // Identity
    public uint AssemblyId;           // Unique ID assigned by loader
    public fixed byte Name[64];       // Assembly simple name (UTF-8)
    public fixed byte Version[16];    // Version string "1.0.0.0"

    // Binary data (persists after UEFI exit)
    public byte* ImageBase;           // PE file bytes in memory
    public ulong ImageSize;           // Size of PE file

    // Parsed metadata (pointers into ImageBase)
    public MetadataRoot Metadata;     // Parsed metadata streams
    public TablesHeader Tables;       // #~ tables header
    public TableSizes Sizes;          // Computed index sizes

    // Runtime state
    public TypeRegistry* Types;       // Token → MethodTable mapping
    public StaticFieldStorage* Statics; // Static field storage
    public CompiledMethodRegistry* Methods; // JIT'd methods

    // Dependency tracking
    public ushort DependencyCount;    // Number of referenced assemblies
    public uint* Dependencies;        // AssemblyIds of referenced assemblies

    // Flags
    public AssemblyFlags Flags;
}

[Flags]
public enum AssemblyFlags : uint
{
    None = 0,
    Loaded = 1,           // Metadata parsed
    TypesResolved = 2,    // TypeRef→TypeDef resolved
    Initialized = 4,      // .cctor run
    Unloadable = 8,       // Can be unloaded (drivers)
    CoreLib = 16,         // This is korlib/mscorlib
}
```

### TypeRegistry (Per-Assembly)

Each assembly has its own type registry:

```csharp
public unsafe struct TypeRegistry
{
    public const int MaxTypes = 256;

    public TypeEntry* Entries;
    public int Count;
    public uint AssemblyId;           // Owner assembly
}

public unsafe struct TypeEntry
{
    public uint Token;                // TypeDef/TypeSpec token (local to assembly)
    public MethodTable* MT;           // Runtime MethodTable
}
```

### StaticFieldStorage (Per-Assembly)

Static field storage isolated per-assembly for clean unloading:

```csharp
public unsafe struct StaticFieldStorage
{
    public const int StorageSize = 64 * 1024;  // 64KB per assembly

    public byte* Storage;             // Allocated storage block
    public int Used;                  // Bytes used
    public uint AssemblyId;           // Owner assembly

    public StaticFieldEntry* Fields;  // Field tracking
    public int FieldCount;
}
```

### AssemblyLoader (Global Coordinator)

```csharp
public static unsafe class AssemblyLoader
{
    public const int MaxAssemblies = 32;

    private static LoadedAssembly* _assemblies;  // Array of loaded assemblies
    private static int _assemblyCount;
    private static uint _nextAssemblyId;

    // Special assembly IDs
    public const uint CoreLibAssemblyId = 1;      // korlib/mscorlib
    public const uint KernelAssemblyId = 2;       // ProtonOS kernel (AOT)

    // Core operations
    public static uint Load(byte* peBytes, ulong size);
    public static bool Unload(uint assemblyId);
    public static LoadedAssembly* GetAssembly(uint assemblyId);

    // Resolution
    public static MethodTable* ResolveType(uint assemblyId, uint token);
    public static void* ResolveField(uint assemblyId, uint token, out ResolvedField info);
    public static void* ResolveMethod(uint assemblyId, uint token);

    // Cross-assembly resolution
    public static uint ResolveAssemblyRef(uint sourceAssemblyId, uint assemblyRefToken);
    public static MethodTable* ResolveTypeRef(uint sourceAssemblyId, uint typeRefToken);
}
```

## Assembly Loading Flow

### Phase 1: Load Binary (Before ExitBootServices)

```
UEFIFS.ReadFile("\\Driver.dll")
  → AllocatePool() for PE bytes
  → File copied to memory
  → Survives ExitBootServices
```

### Phase 2: Parse Metadata

```
AssemblyLoader.Load(peBytes, size)
  1. Allocate LoadedAssembly slot
  2. Verify PE signature (MZ) and CLI header
  3. MetadataReader.Init() → populate Metadata field
  4. MetadataReader.ParseTablesHeader() → populate Tables field
  5. TableSizes.Calculate() → populate Sizes field
  6. Extract assembly name/version from Assembly table (0x20)
  7. Parse AssemblyRef table → Dependencies array
  8. Return assemblyId
```

### Phase 3: Resolve Dependencies

```
For each assembly with unresolved dependencies:
  For each AssemblyRef in the assembly:
    1. Extract name, version, public key token from AssemblyRef row
    2. Search _assemblies for matching loaded assembly
    3. Store resolved assemblyId in Dependencies array
```

### Phase 4: Type Registration

```
For each assembly with loaded metadata:
  For each TypeDef in TypeDef table:
    1. Build/get MethodTable for the type
    2. TypeRegistry.Register(token, mt)
```

### Phase 5: Cross-Assembly TypeRef Resolution

```
ResolveTypeRef(sourceAssemblyId, typeRefToken):
  1. Read TypeRef row from source assembly
  2. Get ResolutionScope (coded index)
  3. If ResolutionScope is AssemblyRef:
     a. Resolve AssemblyRef → targetAssemblyId
     b. Get type name and namespace from TypeRef
     c. Search target assembly's TypeDef table by name
     d. Return target TypeDef's MethodTable
```

## Token Qualification

All external APIs use qualified tokens (assemblyId + token):

```csharp
// For JIT compiler - resolvers take assembly context
public delegate bool TypeResolver(uint assemblyId, uint token, out void* result);
public delegate bool FieldResolver(uint assemblyId, uint token, out ResolvedField result);

// Usage in ILCompiler:
if (_typeResolver(_currentAssemblyId, token, out var mtPtr))
{
    // Use mtPtr
}
```

## Unloading Support

### What Gets Cleaned Up

When `AssemblyLoader.Unload(assemblyId)` is called:

1. **JIT Code**: Remove all RUNTIME_FUNCTIONs, free CodeHeap allocations
2. **Static Storage**: Free the 64KB static field block
3. **Type Registry**: Clear all TypeEntry slots
4. **Method Registry**: Clear all CompiledMethod slots
5. **Dependencies**: Remove assembly from other assemblies' dependency lists
6. **Metadata**: PE bytes can be freed (or kept for reload)

### Constraints

- **Cannot unload if depended upon**: If Assembly B references Assembly A, A cannot be unloaded until B is unloaded first.
- **CoreLib never unloads**: korlib/mscorlib is permanent.
- **Kernel assembly never unloads**: AOT kernel code is permanent.

## Well-Known Types

Well-known types (System.Object, System.String, etc.) use synthetic tokens in a reserved range:

```csharp
public static class WellKnownTypes
{
    // Synthetic tokens (0xF0xxxxxx) - globally unique, no assembly qualification needed
    public const uint Object = 0xF0000001;
    public const uint String = 0xF0000002;
    // ...
}
```

These are registered during kernel init from AOT korlib types and are always resolvable from any assembly.

## File Structure

```
src/kernel/Runtime/
├── AssemblyLoader.cs      # NEW: Main loader, LoadedAssembly struct
├── TypeRegistry.cs        # NEW: Per-assembly type registry
├── StaticFieldStorage.cs  # NEW: Per-assembly static field storage
├── MetadataIntegration.cs # REFACTOR: Use AssemblyLoader for resolution
├── MetadataReader.cs      # UNCHANGED: Low-level metadata parsing
├── PEFormat.cs            # UNCHANGED: PE file parsing
└── JIT/
    ├── ILCompiler.cs      # MODIFY: Accept assemblyId in resolvers
    └── ...
```

## Migration Plan

### Phase 1: Extract Assembly Context

1. Create `LoadedAssembly` struct
2. Create `AssemblyLoader` with `Load()` returning assemblyId
3. Move assembly loading code from `Kernel.cs` to `AssemblyLoader`
4. Kernel.cs calls `AssemblyLoader.Load()` for test assembly
5. All tests continue to pass

### Phase 2: Per-Assembly Registries

1. Create `TypeRegistry` struct with per-assembly storage
2. Create `StaticFieldStorage` struct with per-assembly storage
3. Migrate `MetadataIntegration` to use `LoadedAssembly.Types`
4. Update resolvers to take assemblyId parameter

### Phase 3: Cross-Assembly Resolution

1. Implement `ResolveAssemblyRef()` - name matching
2. Implement `ResolveTypeRef()` - cross-assembly type lookup
3. Implement `ResolveMemberRef()` - cross-assembly method/field lookup

### Phase 4: Unloading

1. Track all allocations per-assembly
2. Implement `AssemblyLoader.Unload()`
3. Add dependency checking

### Phase 5: Multiple Driver Loading

1. Load multiple assemblies from UEFI FS
2. Driver discovery/enumeration
3. Driver initialization order based on dependencies

## Testing Strategy

1. **Single Assembly**: Existing tests with one assembly (regression)
2. **Two Assemblies**: Load test assembly + a second stub assembly
3. **Cross-Reference**: Type in assembly A references type in assembly B
4. **Unload**: Load driver, use it, unload it, verify cleanup
5. **Reload**: Unload and reload same driver

## Appendix: ECMA-335 Token Tables

| Table ID | Name | Purpose |
|----------|------|---------|
| 0x00 | Module | This assembly's module |
| 0x01 | TypeRef | Types in other assemblies |
| 0x02 | TypeDef | Types defined here |
| 0x04 | Field | Fields defined here |
| 0x06 | MethodDef | Methods defined here |
| 0x0A | MemberRef | Methods/fields in other assemblies |
| 0x20 | Assembly | This assembly's identity |
| 0x23 | AssemblyRef | Referenced assemblies |

Tokens: `(tableId << 24) | rowIndex` (1-based row index)
