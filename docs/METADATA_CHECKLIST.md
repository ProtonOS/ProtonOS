# Metadata Reader Implementation Checklist

This document tracks implementation progress for Phase 4 (UEFI FS) and Phase 5 (Metadata Reader).

**Locations**:
- Phase 4 - UEFI FS: `src/kernel/Platform/UefiFileSystem.cs`
- Phase 5 - Metadata: `src/kernel/Runtime/Metadata/`

**Reference**: `dotnet/` submodule (.NET 10 runtime source)

**Testing Strategy**: Implement UEFI file loading first (Phase 4), then build metadata reader
incrementally (Phase 5), validating each component against a real test DLL loaded from the
boot image. This catches bit-level parsing bugs early.

---

## Phase 4: UEFI File System

Must be completed first to enable iterative testing of metadata reader.

### Research

- [ ] UEFI Simple File System Protocol (EFI_SIMPLE_FILE_SYSTEM_PROTOCOL)
- [ ] EFI_FILE_PROTOCOL for file operations
- [ ] How to locate the boot device's file system
- [ ] File info structures (EFI_FILE_INFO)

### Implementation

**File**: `src/kernel/Platform/UefiFileSystem.cs`

- [ ] Locate EFI_SIMPLE_FILE_SYSTEM_PROTOCOL on boot device
- [ ] Open root directory
- [ ] Open file by path
- [ ] Get file size (EFI_FILE_INFO)
- [ ] Read file into memory (allocate from heap)
- [ ] Close file handle
- [ ] Public API: `ReadFile(string path, out int length) -> byte*`

### Test Assembly

- [ ] Create minimal test project (`test/TestAssembly/`)
- [ ] Build TestAssembly.dll with standard `dotnet build`
- [ ] Add to boot image (update Makefile)
- [ ] Load and print basic info (size, first bytes) to verify FS works

---

## Phase 5: Metadata Reader

### Research Tasks

These items need investigation before implementation begins.

#### PE Format

- [ ] Review existing `PEFormat.cs` - what's already implemented?
- [ ] CLI header structure (IMAGE_COR20_HEADER)
- [ ] Data directories relevant to CLI (COM descriptor, etc.)
- [ ] RVA to file offset translation for sections

#### Metadata Physical Layout

- [ ] Metadata root structure (signature, version, stream count)
- [ ] Stream headers (#~, #Strings, #US, #GUID, #Blob, #Pdb)
- [ ] #~ stream (tables) header format
- [ ] Heap size flags and their effect on index sizes
- [ ] Valid/sorted table bitmasks

#### Metadata Tables

- [ ] Table row counts encoding
- [ ] Simple index sizes (2 vs 4 bytes based on row count)
- [ ] Coded index types and tag bits
- [ ] All 45 table schemas (columns and types)

#### Signature Format

- [ ] Calling conventions (default, vararg, generic, etc.)
- [ ] Type encoding (primitives, classes, generics, arrays)
- [ ] Compressed integers in signatures
- [ ] MethodDefSig, FieldSig, LocalVarSig, TypeSpec formats

#### IL Method Bodies

- [ ] Tiny vs fat header format
- [ ] Local variable signature token
- [ ] Exception handling clause formats (small/fat)
- [ ] Section header format for EH data

#### .NET Runtime Source Locations

Document key files in `dotnet/` for reference:

- [ ] `src/libraries/System.Reflection.Metadata/` - S.R.Metadata implementation
- [ ] `src/coreclr/vm/` - CoreCLR runtime (type system, metadata access)
- [ ] `src/coreclr/inc/` - Header files with structure definitions
- [ ] ECMA-335 spec locations in source comments

### Implementation Checklist

#### 5.1 PE Reader Enhancement

**Files**: `src/kernel/Runtime/PEFormat.cs`, `src/kernel/Runtime/Metadata/PEReader.cs`

- [ ] DOS header validation
- [ ] PE signature check
- [ ] COFF header parsing
- [ ] Optional header (PE32+) parsing
- [ ] Data directories array
- [ ] Section headers enumeration
- [ ] RVA to file offset helper
- [ ] CLI header (COR20) location and parsing
- [ ] Metadata root location

#### 5.2 Metadata Root and Streams

**Files**: `src/kernel/Runtime/Metadata/MetadataRoot.cs`

- [ ] Metadata signature validation (0x424A5342)
- [ ] Version string reading
- [ ] Stream count and headers
- [ ] Stream lookup by name
- [ ] #~ stream location
- [ ] #Strings heap accessor
- [ ] #US (user strings) heap accessor
- [ ] #GUID heap accessor
- [ ] #Blob heap accessor

#### 5.3 Tables Stream (#~)

**Files**: `src/kernel/Runtime/Metadata/TablesStream.cs`

- [ ] Tables stream header parsing
- [ ] HeapSizes flags interpretation
- [ ] Valid tables bitmask
- [ ] Sorted tables bitmask
- [ ] Row counts for all present tables
- [ ] Index size calculation (2 vs 4 bytes)
- [ ] Table data start offset calculation

#### 5.4 Coded Indexes

**Files**: `src/kernel/Runtime/Metadata/CodedIndex.cs`

- [ ] TypeDefOrRef (TypeDef, TypeRef, TypeSpec)
- [ ] HasConstant (Field, Param, Property)
- [ ] HasCustomAttribute (22 options)
- [ ] HasFieldMarshal (Field, Param)
- [ ] HasDeclSecurity (TypeDef, MethodDef, Assembly)
- [ ] MemberRefParent (TypeDef, TypeRef, ModuleRef, MethodDef, TypeSpec)
- [ ] HasSemantics (Event, Property)
- [ ] MethodDefOrRef (MethodDef, MemberRef)
- [ ] MemberForwarded (Field, MethodDef)
- [ ] Implementation (File, AssemblyRef, ExportedType)
- [ ] CustomAttributeType (MethodDef, MemberRef)
- [ ] ResolutionScope (Module, ModuleRef, AssemblyRef, TypeRef)
- [ ] TypeOrMethodDef (TypeDef, MethodDef)

#### 5.5 Metadata Tables

**Files**: `src/kernel/Runtime/Metadata/Tables/*.cs`

Core tables:
- [ ] Module (0x00)
- [ ] TypeRef (0x01)
- [ ] TypeDef (0x02)
- [ ] Field (0x04)
- [ ] MethodDef (0x06)
- [ ] Param (0x08)
- [ ] InterfaceImpl (0x09)
- [ ] MemberRef (0x0A)
- [ ] Constant (0x0B)
- [ ] CustomAttribute (0x0C)

Layout tables:
- [ ] ClassLayout (0x0F)
- [ ] FieldLayout (0x10)

Signature tables:
- [ ] StandAloneSig (0x11)

Event/Property tables:
- [ ] EventMap (0x12)
- [ ] Event (0x14)
- [ ] PropertyMap (0x15)
- [ ] Property (0x17)
- [ ] MethodSemantics (0x18)

Method implementation:
- [ ] MethodImpl (0x19)
- [ ] ModuleRef (0x1A)
- [ ] TypeSpec (0x1B)

P/Invoke:
- [ ] ImplMap (0x1C)
- [ ] FieldRVA (0x1D)

Assembly:
- [ ] Assembly (0x20)
- [ ] AssemblyRef (0x23)

Manifest:
- [ ] File (0x26)
- [ ] ExportedType (0x27)
- [ ] ManifestResource (0x28)

Nested types:
- [ ] NestedClass (0x29)

Generics:
- [ ] GenericParam (0x2A)
- [ ] MethodSpec (0x2B)
- [ ] GenericParamConstraint (0x2C)

#### 5.6 Heap Access

**Files**: `src/kernel/Runtime/Metadata/Heaps.cs`

- [ ] String heap: null-terminated UTF-8
- [ ] Blob heap: length-prefixed bytes
- [ ] GUID heap: 16-byte GUIDs by index
- [ ] UserString heap: length-prefixed UTF-16 with terminal byte
- [ ] Compressed integer decoding (1/2/4 byte)
- [ ] Signed compressed integer decoding

#### 5.7 Signature Decoding

**Files**: `src/kernel/Runtime/Metadata/SignatureDecoder.cs`

Calling conventions:
- [ ] DEFAULT (0x00)
- [ ] VARARG (0x05)
- [ ] GENERIC (0x10)
- [ ] HASTHIS (0x20)
- [ ] EXPLICITTHIS (0x40)

Element types:
- [ ] VOID, BOOLEAN, CHAR, I1-I8, U1-U8, R4, R8
- [ ] STRING, OBJECT, TYPEDBYREF
- [ ] PTR, BYREF, VALUETYPE, CLASS
- [ ] VAR, GENERICINST, ARRAY, SZARRAY
- [ ] FNPTR, MVAR
- [ ] CMOD_REQD, CMOD_OPT
- [ ] SENTINEL, PINNED

Signature types:
- [ ] MethodDefSig
- [ ] MethodRefSig
- [ ] FieldSig
- [ ] PropertySig
- [ ] LocalVarSig
- [ ] TypeSpec signatures

#### 5.8 IL Method Body Reading

**Files**: `src/kernel/Runtime/Metadata/MethodBody.cs`

- [ ] Tiny header detection (size < 64, no locals, no EH)
- [ ] Fat header parsing (flags, size, max stack, code size, local var token)
- [ ] IL bytes access
- [ ] Exception handling section detection
- [ ] Small EH clause format
- [ ] Fat EH clause format
- [ ] EH clause types (exception, filter, finally, fault)

#### 5.9 Type Resolution

**Files**: `src/kernel/Runtime/Metadata/TypeResolver.cs`

- [ ] TypeDef lookup by name
- [ ] TypeRef resolution to TypeDef
- [ ] Nested type resolution
- [ ] Generic type instantiation
- [ ] Base type chain walking
- [ ] Interface implementation enumeration
- [ ] Field enumeration for type
- [ ] Method enumeration for type

#### 5.10 Assembly Identity

**Files**: `src/kernel/Runtime/Metadata/AssemblyIdentity.cs`

- [ ] Assembly name
- [ ] Version (major, minor, build, revision)
- [ ] Culture
- [ ] Public key / public key token
- [ ] AssemblyRef matching
- [ ] Type forwarding (ExportedType)

---

## Testing

- [ ] Parse our own BOOTX64.EFI (NativeAOT output)
- [ ] Parse a simple .NET DLL (HelloWorld)
- [ ] Parse System.Runtime.dll from BCL
- [ ] Parse System.Collections.dll
- [ ] Enumerate all types in an assembly
- [ ] Read IL for a specific method
- [ ] Resolve cross-assembly type references

---

## Notes

*Add implementation notes, gotchas, and decisions here as work progresses.*

