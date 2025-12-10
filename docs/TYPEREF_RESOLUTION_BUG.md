# TypeRef Resolution Bug - JIT Compilation Failure

## Problem Summary

The JIT compiler fails to compile `VirtioDevice.InitializeModern()` because it cannot properly resolve TypeRef tokens to get value type sizes. This causes stack tracking corruption and the error:

```
at IL 140/478: insufficient stack depth 4 for 5 args (ArgCount=5, HasThis=false)
[Tier0JIT] ERROR: Compilation failed
```

## Root Cause

When the JIT encounters a field whose declaring type is a **TypeRef** (reference to a type in another assembly), it fails to resolve that TypeRef to the actual **TypeDef** to get the real size of the value type.

### Specific Case: PciAddress

- `PciAddress` is a 3-byte struct defined in the DDK assembly
- `PciDeviceInfo` has a field `Address` of type `PciAddress`
- When compiling code in `VirtioDevice` (drivers assembly), the metadata contains a **TypeRef** to `PciAddress`, not a TypeDef
- The JIT needs to resolve: `TypeRef(PciAddress)` → `TypeDef(PciAddress)` → size = 3 bytes

### What Happens Now

1. `ldfld Address` encounters TypeRef for PciAddress
2. JIT cannot get the size (returns 0 or wrong value)
3. Stack type tracking becomes corrupted
4. When calling `PCI.WriteConfig16(bus, device, function, offset, value)`:
   - JIT thinks there are 4 items on stack
   - Actually need 5 arguments
   - Compilation fails

## Required Fix

### TypeRef → TypeDef Resolution

The metadata resolver needs to properly handle TypeRef tokens:

1. **Parse the TypeRef**: Get the type name and resolution scope (which assembly it's from)
2. **Find the target assembly**: Look up the referenced assembly in loaded assemblies
3. **Find the TypeDef**: Search the target assembly's TypeDef table for matching type name
4. **Get the size**: Once you have the TypeDef, you can compute the actual struct size from its fields

### Key Code Locations

- **ILCompiler.cs**: `CompileLdfld()` calls field resolver to get declaring type info
- **MetadataResolver.cs**: Needs `ResolveTypeRef()` method that returns TypeDef handle
- **TypeDef table**: Contains actual field layouts needed to compute struct sizes

### Pseudocode for Fix

```csharp
int GetTypeSize(uint typeToken)
{
    // If it's a TypeDef, we can get size directly
    if (IsTypeDef(typeToken))
        return ComputeTypeSizeFromFields(typeToken);

    // If it's a TypeRef, resolve it first
    if (IsTypeRef(typeToken))
    {
        uint typeDefToken = ResolveTypeRefToTypeDef(typeToken);
        if (typeDefToken != 0)
            return ComputeTypeSizeFromFields(typeDefToken);
    }

    return 0; // Unknown
}

uint ResolveTypeRefToTypeDef(uint typeRefToken)
{
    // 1. Get TypeRef row from metadata
    var typeRef = GetTypeRefRow(typeRefToken);

    // 2. Get resolution scope (AssemblyRef usually)
    var assemblyRef = typeRef.ResolutionScope;

    // 3. Find the target assembly
    var targetAssembly = FindLoadedAssembly(assemblyRef);

    // 4. Search target assembly's TypeDef table for matching name
    foreach (var typeDef in targetAssembly.TypeDefs)
    {
        if (typeDef.Name == typeRef.Name &&
            typeDef.Namespace == typeRef.Namespace)
            return typeDef.Token;
    }

    return 0; // Not found
}
```

## Files to Modify

1. **src/kernel/Runtime/JIT/MetadataResolver.cs** (or equivalent)
   - Add `ResolveTypeRefToTypeDef()` method
   - Modify `GetTypeSize()` to handle TypeRef tokens

2. **src/kernel/Runtime/JIT/ILCompiler.cs**
   - Ensure `CompileLdfld()` uses resolved type size for stack tracking

## Test Case

The failure occurs in `VirtioDevice.InitializeModern()` at IL offset 140, which is a call to:
```csharp
PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);
```

Where `bus`, `device`, `function` are loaded from `_pciDevice.Address.Bus/Device/Function` - byte fields from the embedded 3-byte `PciAddress` struct.

## Current Workarounds Attempted (All Failed)

1. stloc VT fallback fix - didn't address root cause
2. Various stack tracking patches - symptoms, not cause

The ONLY real fix is proper TypeRef resolution to get accurate value type sizes.
