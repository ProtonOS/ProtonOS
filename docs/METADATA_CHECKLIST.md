# Metadata Reader Implementation Checklist

This document tracks implementation progress for Phase 4 (UEFI FS) and Phase 5 (Metadata Reader).

**Locations**:
- Phase 4 - UEFI FS: `src/kernel/Platform/UEFIFS.cs` ✅
- Phase 5 - Metadata: `src/kernel/Runtime/Metadata/`

**Reference**: `dotnet/` submodule (.NET 10 runtime source)

**Testing Strategy**: Implement UEFI file loading first (Phase 4), then build metadata reader
incrementally (Phase 5), validating each component against a real test DLL loaded from the
boot image. This catches bit-level parsing bugs early.

---

## Phase 4: UEFI File System ✅

Completed. UEFI file loading works before ExitBootServices.

### Research

- [x] UEFI Simple File System Protocol (EFI_SIMPLE_FILE_SYSTEM_PROTOCOL)
- [x] EFI_FILE_PROTOCOL for file operations
- [x] How to locate the boot device's file system
- [x] File info structures (EFI_FILE_INFO)

**Research Notes:**

The UEFI file system access follows this pattern:
1. Use `LocateProtocol` or `HandleProtocol` on the boot device to get `EFI_SIMPLE_FILE_SYSTEM_PROTOCOL`
2. Call `OpenVolume()` to get root `EFI_FILE_PROTOCOL` handle
3. Use `Open()` to navigate/open files, `Read()` to read content, `GetInfo()` for file size
4. Must be done **before** `ExitBootServices()`

**Key GUIDs:**
- `EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_GUID`: `{0x0964e5b22,0x6459,0x11d2,{0x8e,0x39,0x00,0xa0,0xc9,0x69,0x72,0x3b}}`
- `EFI_FILE_INFO_ID`: `{0x09576e92,0x6d3f,0x11d2,{0x8e,0x39,0x00,0xa0,0xc9,0x69,0x72,0x3b}}`

**EFI_SIMPLE_FILE_SYSTEM_PROTOCOL** (UEFI Spec 2.10 §13.4):
```c
struct {
    UINT64 Revision;                           // 0x00010000
    EFI_STATUS (*OpenVolume)(This, &Root);     // Returns EFI_FILE_PROTOCOL*
}
```

**EFI_FILE_PROTOCOL** (UEFI Spec 2.10 §13.5):
```c
struct {
    UINT64 Revision;
    Open(This, &NewHandle, FileName, OpenMode, Attributes);  // Open file/dir
    Close(This);
    Delete(This);
    Read(This, &BufferSize, Buffer);   // BufferSize is in/out
    Write(This, &BufferSize, Buffer);
    GetPosition(This, &Position);
    SetPosition(This, Position);
    GetInfo(This, &InfoType, &BufferSize, Buffer);  // Use EFI_FILE_INFO_ID
    SetInfo(This, &InfoType, BufferSize, Buffer);
    Flush(This);
    // Rev2 adds: OpenEx, ReadEx, WriteEx, FlushEx (async versions)
}
```

**Open Modes:** `EFI_FILE_MODE_READ=0x01`, `EFI_FILE_MODE_WRITE=0x02`, `EFI_FILE_MODE_CREATE=0x8000000000000000`

**EFI_FILE_INFO:**
```c
struct {
    UINT64 Size;              // Total struct size (including filename)
    UINT64 FileSize;          // File size in bytes
    UINT64 PhysicalSize;      // On-disk size
    EFI_TIME CreateTime;
    EFI_TIME LastAccessTime;
    EFI_TIME ModificationTime;
    UINT64 Attribute;
    CHAR16 FileName[];        // Null-terminated UTF-16
}
```

**Locating boot device FS:** Use `HandleProtocol` on `LoadedImageProtocol->DeviceHandle` to get the
SimpleFileSystem for the boot device (same device we booted from).

See: [UEFI Spec 2.10 §13 Media Access](https://uefi.org/specs/UEFI/2.10/13_Protocols_Media_Access.html)

### Implementation

**File**: `src/kernel/Platform/UEFIFS.cs`

- [x] Locate EFI_SIMPLE_FILE_SYSTEM_PROTOCOL on boot device
- [x] Open root directory
- [x] Open file by path
- [x] Get file size (EFI_FILE_INFO)
- [x] Read file into memory (allocate from UEFI pool - LoaderData)
- [x] Close file handle
- [x] Public API: `ReadFile(char* path, out ulong size) -> byte*`, `ReadFileAscii(string path, out ulong size) -> byte*`

**Memory Safety**: File is loaded before `PageAllocator.Init()` so the allocation is captured in
the memory map snapshot. LoaderData memory is not marked as free, so it persists after ExitBootServices.

### Test Assembly

- [x] Create minimal test project (`src/MetadataTest/`)
- [x] Build MetadataTest.dll with standard `dotnet build` (.NET 10)
- [x] Add to boot image (update Makefile `test` and `image` targets)
- [x] Load and print basic info (address, size, MZ signature) to verify FS works

---

## Phase 5: Metadata Reader

### Research Tasks

These items need investigation before implementation begins.

#### PE Format

- [x] Review existing `PEFormat.cs` - what's already implemented?
- [x] CLI header structure (IMAGE_COR20_HEADER)
- [x] Data directories relevant to CLI (COM descriptor, etc.)
- [x] RVA to file offset translation for sections

**PEFormat.cs Status:** Now has complete PE32 and PE32+ support:
- `ImageOptionalHeader32`, `ImageOptionalHeader64`, `ImageNtHeaders32`, `ImageNtHeaders64`
- `ImageCor20Header` for CLI header, `CorFlags` constants
- `IsPE64()` for format detection
- `RvaToFilePointer()` for raw file loading (translates RVA through section headers)
- `GetCorHeaderFromFile()`, `GetMetadataRootFromFile()` for assembly parsing

**IMAGE_COR20_HEADER** (from `dotnet/src/coreclr/inc/corhdr.h`):
```c
struct IMAGE_COR20_HEADER {
    uint32_t cb;                      // Size of header (72 bytes)
    uint16_t MajorRuntimeVersion;
    uint16_t MinorRuntimeVersion;
    IMAGE_DATA_DIRECTORY MetaData;    // RVA/Size of metadata
    uint32_t Flags;                   // CorFlags
    union {
        uint32_t EntryPointToken;     // Managed entry point (if not NATIVE_ENTRYPOINT)
        uint32_t EntryPointRVA;       // Native entry point (deprecated)
    };
    IMAGE_DATA_DIRECTORY Resources;
    IMAGE_DATA_DIRECTORY StrongNameSignature;
    IMAGE_DATA_DIRECTORY CodeManagerTable;      // Deprecated
    IMAGE_DATA_DIRECTORY VTableFixups;
    IMAGE_DATA_DIRECTORY ExportAddressTableJumps;
    IMAGE_DATA_DIRECTORY ManagedNativeHeader;   // Points to READYTORUN_HEADER in R2R images
};  // Total: 72 bytes
```

#### Metadata Physical Layout

- [x] Metadata root structure (signature, version, stream count)
- [x] Stream headers (#~, #Strings, #US, #GUID, #Blob, #Pdb)
- [x] #~ stream (tables) header format
- [x] Heap size flags and their effect on index sizes
- [x] Valid/sorted table bitmasks

**Metadata Root** (from `dotnet/src/coreclr/inc/mdfileformat.h`):

Layout: `STORAGESIGNATURE` + version string + `STORAGEHEADER` + stream headers

```c
// STORAGESIGNATURE (variable size due to version string)
struct STORAGESIGNATURE {
    uint32_t lSignature;      // 0x424A5342 ("BSJB")
    uint16_t iMajorVer;       // Usually 1
    uint16_t iMinorVer;       // Usually 1
    uint32_t iExtraData;      // Reserved/extra data offset
    uint32_t iVersionString;  // Length of version string (padded to 4 bytes)
    char pVersion[0];         // Version string (e.g., "v4.0.30319")
};

// STORAGEHEADER (4 bytes, follows version string)
struct STORAGEHEADER {
    uint8_t  fFlags;          // 0x00 normal, 0x01 extra data
    uint8_t  pad;
    uint16_t iStreams;        // Number of streams
};

// STORAGESTREAM (variable size per stream, 4-byte aligned name)
struct STORAGESTREAM {
    uint32_t iOffset;         // Offset from metadata root
    uint32_t iSize;           // Size in bytes
    char rcName[32];          // Null-terminated stream name, 4-byte aligned
};
```

**Stream Names:** `#~` (compressed tables), `#Strings`, `#US` (user strings), `#GUID`, `#Blob`, `#Pdb`

**#~ Stream Header** (tables stream, from `dotnet/src/coreclr/md/inc/metamodel.h`):

```c
// CMiniMdSchemaBase - first 24 bytes of #~ stream
struct {
    uint32_t m_ulReserved;    // Must be 0
    uint8_t  m_major;         // Schema major (usually 2)
    uint8_t  m_minor;         // Schema minor (usually 0)
    uint8_t  m_heaps;         // Heap size flags
    uint8_t  m_rid;           // log2(largest rid)
    uint64_t m_maskvalid;     // Bitmask of present tables
    uint64_t m_sorted;        // Bitmask of sorted tables
    // Followed by: uint32_t row counts for each present table (only where bit set in m_maskvalid)
    // Then: table data rows
};
```

**Heap Size Flags (m_heaps):**
- `0x01` - String heap uses 4-byte indexes (otherwise 2-byte)
- `0x02` - GUID heap uses 4-byte indexes
- `0x04` - Blob heap uses 4-byte indexes
- `0x08` - Padding bit (extra column bits for growth)
- `0x20` - Delta only
- `0x40` - Extra 4-byte data follows header
- `0x80` - Has delete tokens

**Reference:** `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Internal/COR20Constants.cs`

#### Metadata Tables ✅

- [x] Table row counts encoding
- [x] Simple index sizes (2 vs 4 bytes based on row count)
- [x] Coded index types and tag bits
- [x] All 45 table schemas (columns and types) - accessors for all applicable tables

#### Signature Format ✅

- [x] Calling conventions (default, vararg, generic, etc.)
- [x] Type encoding (primitives, classes, generics, arrays)
- [x] Compressed integers in signatures
- [x] MethodDefSig, FieldSig, LocalVarSig, TypeSpec formats

**Reference Files:**
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Signatures/SignatureTypeCode.cs`
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Internal/CorElementType.cs`
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Signatures/SignatureHeader.cs`
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/SignatureDecoder.cs`

#### IL Method Bodies ✅

- [x] Tiny vs fat header format
- [x] Local variable signature token
- [x] Exception handling clause formats (small/fat)
- [x] Section header format for EH data

**Reference Files:**
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/IL/MethodBodyBlock.cs`
- `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/IL/ExceptionRegion.cs`

#### .NET Runtime Source Locations

Document key files in `dotnet/` for reference:

- [x] `src/libraries/System.Reflection.Metadata/` - S.R.Metadata implementation
- [x] `src/coreclr/vm/` - CoreCLR runtime (type system, metadata access)
- [x] `src/coreclr/inc/` - Header files with structure definitions
- [ ] ECMA-335 spec locations in source comments

**Key Reference Files:**

| File | Purpose |
|------|---------|
| `dotnet/src/coreclr/inc/corhdr.h` | IMAGE_COR20_HEADER, token types, CorFlags |
| `dotnet/src/coreclr/inc/mdfileformat.h` | STORAGESIGNATURE, STORAGEHEADER, STORAGESTREAM |
| `dotnet/src/coreclr/md/inc/metamodel.h` | CMiniMdSchemaBase (#~ header), heap flags, table schemas |
| `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Internal/COR20Constants.cs` | Magic numbers, stream names |
| `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Internal/Tables.cs` | Table reader implementations (good reference for column layouts) |
| `dotnet/src/libraries/System.Reflection.Metadata/src/System/Reflection/PortableExecutable/PEReader.cs` | PE parsing logic |
| `dotnet/src/coreclr/md/runtime/metamodel.cpp` | Row count loading, index size calculation |

### Implementation Checklist

#### 5.1 PE Reader Enhancement ✅

**File**: `src/kernel/Runtime/PEFormat.cs`

- [x] DOS header validation
- [x] PE signature check
- [x] COFF header parsing
- [x] Optional header (PE32 and PE32+) parsing
- [x] Data directories array
- [x] Section headers enumeration
- [x] RVA to file offset helper (`RvaToFilePointer`)
- [x] CLI header (COR20) location and parsing
- [x] Metadata root location and BSJB signature verification

**Tested:** MetadataTest.dll parses successfully - CLI header shows runtime 2.5, flags ILOnly (0x1),
metadata at RVA 0x205C with valid BSJB signature, version 1.1.

#### 5.2 Metadata Root and Streams ✅

**File**: `src/kernel/Runtime/MetadataReader.cs`

- [x] Metadata signature validation (0x424A5342)
- [x] Version string reading
- [x] Stream count and headers
- [x] Stream lookup by name (#~, #Strings, #US, #GUID, #Blob)
- [x] #~ stream location
- [x] #Strings heap accessor
- [x] #US (user strings) heap accessor
- [x] #GUID heap accessor
- [x] #Blob heap accessor

**Tested:** MetadataTest.dll shows 5 streams parsed correctly:
- #~ (tables): offset 0x6C, size 0x180
- #Strings: offset 0x1EC, size 0x1F4
- #US: offset 0x3E0, size 0x04
- #GUID: offset 0x3E4, size 0x10
- #Blob: offset 0x3F4, size 0xFC

#### 5.3 Tables Stream (#~) ✅

**File**: `src/kernel/Runtime/MetadataReader.cs` (TablesHeader struct + ParseTablesHeader method)

- [x] Tables stream header parsing
- [x] HeapSizes flags interpretation
- [x] Valid tables bitmask
- [x] Sorted tables bitmask
- [x] Row counts for all present tables
- [x] Index size calculation (2 vs 4 bytes based on heap flags)
- [x] Table data start offset calculation

**Tested:** MetadataTest.dll #~ header parsed correctly:
- Schema version 2.0, all heaps use 2-byte indexes
- 8 tables present: Module(1), TypeRef(13), TypeDef(2), MethodDef(2), MemberRef(12), CustomAttribute(11), Assembly(1), AssemblyRef(1)

#### 5.4 Coded Indexes ✅

**File**: `src/kernel/Runtime/MetadataReader.cs` (CodedIndexType enum, CodedIndexHelper class)

- [x] TypeDefOrRef (TypeDef, TypeRef, TypeSpec) - 2 tag bits
- [x] HasConstant (Field, Param, Property) - 2 tag bits
- [x] HasCustomAttribute (22 options) - 5 tag bits
- [x] HasFieldMarshal (Field, Param) - 1 tag bit
- [x] HasDeclSecurity (TypeDef, MethodDef, Assembly) - 2 tag bits
- [x] MemberRefParent (TypeDef, TypeRef, ModuleRef, MethodDef, TypeSpec) - 3 tag bits
- [x] HasSemantics (Event, Property) - 1 tag bit
- [x] MethodDefOrRef (MethodDef, MemberRef) - 1 tag bit
- [x] MemberForwarded (Field, MethodDef) - 1 tag bit
- [x] Implementation (File, AssemblyRef, ExportedType) - 2 tag bits
- [x] CustomAttributeType (MethodDef, MemberRef) - 3 tag bits
- [x] ResolutionScope (Module, ModuleRef, AssemblyRef, TypeRef) - 2 tag bits
- [x] TypeOrMethodDef (TypeDef, MethodDef) - 1 tag bit

**Implementation includes:**
- `CodedIndexType` enum with all 13 coded index types
- `CodedIndex` struct (Table + 1-based RowId)
- `CodedIndexHelper.GetTagBits()` - returns tag bit count per type
- `CodedIndexHelper.GetCodedIndexSize()` - calculates 2 or 4 byte size
- `CodedIndexHelper.GetMaxRowCount()` - max row count across target tables
- `CodedIndexHelper.Decode()` - decodes value to table ID and row ID
- `CodedIndexHelper.DecodeTag()` - maps tag to MetadataTableId

**Tested:** ResolutionScope correctly decodes TypeRef rows to AssemblyRef[1] and nested TypeRef[3].

#### 5.5 Metadata Tables ✅

**File**: `src/kernel/Runtime/MetadataReader.cs` (TableSizes struct + table accessor methods)

**Infrastructure:**
- [x] `TableSizes` struct with pre-calculated row sizes and table offsets
- [x] `TableSizes.Calculate()` to compute all sizes based on heap flags and row counts
- [x] `GetTableRow()` - generic row accessor by table ID and 1-based row ID
- [x] `ReadIndex()` - helper to read 2 or 4 byte index values

Core tables:
- [x] Module (0x00) - `DumpModuleTable()` with name/MVID access
- [x] TypeRef (0x01) - `GetTypeRefResolutionScope/Name/Namespace()`, `DumpTypeRefTable()`
- [x] TypeDef (0x02) - `DumpTypeDefTable()` with flags/name/namespace/extends/field/method
- [x] Field (0x04) - `GetFieldFlags/Name/Signature()`
- [x] MethodDef (0x06) - `GetMethodDefRVA/Name/Flags()`, `DumpMethodDefTable()`
- [x] Param (0x08) - `GetParamFlags/Sequence/Name()`
- [x] InterfaceImpl (0x09) - `GetInterfaceImplClass/Interface()`
- [x] MemberRef (0x0A) - `GetMemberRefClass/Name/Signature()`, `DumpMemberRefTable()`
- [x] Constant (0x0B) - `GetConstantType/Parent/Value()`
- [x] CustomAttribute (0x0C) - `GetCustomAttributeParent/Type/Value()`

Layout tables:
- [x] ClassLayout (0x0F) - `GetClassLayoutPackingSize/ClassSize/Parent()`
- [x] FieldLayout (0x10) - `GetFieldLayoutOffset/Field()`

Signature tables:
- [x] StandAloneSig (0x11) - `GetStandAloneSigSignature()`

Event/Property tables:
- [x] EventMap (0x12) - `GetEventMapParent/EventList()`
- [x] Event (0x14) - `GetEventFlags/Name/Type()`
- [x] PropertyMap (0x15) - `GetPropertyMapParent/PropertyList()`
- [x] Property (0x17) - `GetPropertyFlags/Name/Type()`
- [x] MethodSemantics (0x18) - `GetMethodSemanticsSemantics/Method/Association()`

Method implementation:
- [x] MethodImpl (0x19) - `GetMethodImplClass/MethodBody/MethodDeclaration()`
- [x] ModuleRef (0x1A) - `GetModuleRefName()`
- [x] TypeSpec (0x1B) - `GetTypeSpecSignature()`

P/Invoke:
- [x] ImplMap (0x1C) - `GetImplMapMappingFlags/MemberForwarded/ImportName/ImportScope()`
- [x] FieldRVA (0x1D) - `GetFieldRvaRva/Field()`

Assembly:
- [x] Assembly (0x20) - `GetAssemblyHashAlgId/MajorVersion/MinorVersion/BuildNumber/RevisionNumber/Flags/Name/Culture()`
- [x] AssemblyRef (0x23) - `GetAssemblyRefMajorVersion/MinorVersion/BuildNumber/RevisionNumber/Flags/Name()`, `DumpAssemblyRefTable()`

Manifest:
- [x] File (0x26) - `GetFileFlags/Name/HashValue()`
- [x] ExportedType (0x27) - `GetExportedTypeFlags/TypeDefId/Name/Namespace/Implementation()`
- [x] ManifestResource (0x28) - `GetManifestResourceOffset/Flags/Name/Implementation()`

Nested types:
- [x] NestedClass (0x29) - `GetNestedClassNestedClass/EnclosingClass()`

Generics:
- [x] GenericParam (0x2A) - `GetGenericParamNumber/Flags/Owner/Name()`
- [x] MethodSpec (0x2B) - `GetMethodSpecMethod/Instantiation()`
- [x] GenericParamConstraint (0x2C) - `GetGenericParamConstraintOwner/Constraint()`

#### 5.6 Heap Access ✅

**File**: `src/kernel/Runtime/MetadataReader.cs`

- [x] String heap: null-terminated UTF-8 (`GetString`, `PrintString`)
- [x] Blob heap: length-prefixed bytes (`GetBlob`)
- [x] GUID heap: 16-byte GUIDs by 1-based index (`GetGuid`, `PrintGuid`)
- [x] UserString heap: length-prefixed UTF-16 with terminal byte (`GetUserString`)
- [x] Compressed integer decoding (1/2/4 byte) (`ReadCompressedUInt`)
- [x] Signed compressed integer decoding (`ReadCompressedInt`)

**Tested:** Full end-to-end heap access working:
- Module name: "MetadataTest.dll" from #Strings
- MVID: {7262FC15-F77D-464D-80A5-515853BEFAE1} from #GUID
- TypeDef names: "<Module>", "Program" from #Strings

#### 5.7 Signature Decoding ✅

**File**: `src/kernel/Runtime/MetadataReader.cs` (ElementType, SignatureHeader, MethodSignature, TypeSig, SignatureReader)

Calling conventions:
- [x] DEFAULT (0x00)
- [x] VARARG (0x05)
- [x] GENERIC (0x10)
- [x] HASTHIS (0x20)
- [x] EXPLICITTHIS (0x40)

Element types:
- [x] VOID, BOOLEAN, CHAR, I1-I8, U1-U8, R4, R8
- [x] STRING, OBJECT, TYPEDBYREF
- [x] PTR, BYREF, VALUETYPE, CLASS
- [x] VAR, GENERICINST, ARRAY, SZARRAY
- [x] FNPTR, MVAR
- [x] CMOD_REQD, CMOD_OPT
- [x] SENTINEL, PINNED

Signature types:
- [x] MethodDefSig (ReadMethodSignature)
- [ ] MethodRefSig
- [x] FieldSig (ReadFieldSignature)
- [ ] PropertySig
- [x] LocalVarSig (ReadLocalVarSignature)
- [ ] TypeSpec signatures

**Implementation includes:**
- `ElementType` constants (0x00-0x45)
- `SignatureHeader` constants for calling conventions and attributes
- `MethodSignature` struct with parsed header, param count, return type
- `TypeSig` struct with element type, token, generic param index
- `SignatureReader` static class with:
  - `ReadMethodSignature()` - parse MethodDef signature blob
  - `ReadTypeSig()` - parse a single type
  - `SkipType()` / `SkipMethodSig()` - skip over types in stream
  - `PrintElementType()` / `PrintMethodSignature()` - debug output

**Tested:** MetadataTest.dll signatures parsed correctly:
- `Main`: `void (0 params)` - static method
- `.ctor`: `void (instance 0 params)` - instance method with HasThis flag

#### 5.8 IL Method Body Reading ✅

**File**: `src/kernel/Runtime/MetadataReader.cs` (MethodBody struct, MethodBodyConstants, ExceptionClause)

- [x] Tiny header detection (size < 64, no locals, no EH)
- [x] Fat header parsing (flags, size, max stack, code size, local var token)
- [x] IL bytes access (pointer to IL code after header)
- [x] Exception handling section detection (4-byte aligned after IL)
- [x] Small EH clause format (12 bytes per clause)
- [x] Fat EH clause format (24 bytes per clause)
- [x] EH clause types (Catch, Filter, Finally, Fault)

**Implementation includes:**
- `MethodBody` struct with header info and IL code pointer
- `MethodBodyConstants` class with format flags and section types
- `ExceptionClause` struct and `ExceptionClauseKind` enum
- `ReadMethodBody()` - parses tiny or fat header
- `ReadExceptionClauses()` - parses EH sections after IL code
- `DumpMethodBody()` - debug output for method bodies

**Tested:** MetadataTest.dll parses correctly:
- `Main`: Tiny format, 1 byte (ret)
- `.ctor`: Tiny format, 7 bytes (ldarg.0, call, ret)

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

**MetadataTest.dll Validation** - Comprehensive test assembly exercising 30 metadata tables:

- [x] Parse MetadataTest.dll from boot image
- [x] Module, TypeRef, TypeDef, Field, MethodDef, Param, InterfaceImpl tables
- [x] MemberRef, Constant, CustomAttribute, DeclSecurity tables
- [x] ClassLayout, FieldLayout, StandAloneSig tables
- [x] EventMap, Event, PropertyMap, Property, MethodSemantics tables
- [x] MethodImpl, ModuleRef, TypeSpec, ImplMap, FieldRVA tables
- [x] Assembly, AssemblyRef, NestedClass tables
- [x] GenericParam, MethodSpec, GenericParamConstraint tables
- [x] Method signature parsing (all primitive types, arrays, generics, pointers, spans)
- [x] IL method body parsing (tiny and fat formats)
- [x] Exception handling clause parsing

**Future testing:**
- [ ] Parse our own BOOTX64.EFI (NativeAOT output)
- [ ] Parse System.Runtime.dll from BCL
- [ ] Enumerate all types in an assembly
- [ ] Resolve cross-assembly type references

---

## Notes

### Implementation Order Recommendation

1. ~~**Phase 4 first** - Get UEFI file loading working before ExitBootServices~~ ✅
2. ~~**Add IMAGE_COR20_HEADER** to PEFormat.cs~~ ✅
3. ~~**Parse metadata root** - verify signature, find streams~~ ✅
4. ~~**Implement heap readers** - #Strings first (simplest), then #Blob~~ ✅
5. ~~**Parse #~ header** - extract row counts and heap size flags~~ ✅
6. ~~**Add coded index support** - all 13 coded index types with decode/size helpers~~ ✅
7. ~~**Implement core tables** - Module, TypeDef, TypeRef, MethodDef with accessors~~ ✅
8. ~~**Complete all table accessors** - all 45 ECMA-335 tables have accessors~~ ✅
9. **Type Resolution** - TypeDef lookup, TypeRef resolution, generic instantiation ← **NEXT**
10. **Assembly Identity** - version matching, AssemblyRef resolution

### Gotchas

- **Index sizes vary**: Heap indexes are 2 or 4 bytes based on heap size flags.
  Table row indexes are 2 or 4 bytes based on row count (>0xFFFF = 4 bytes).
- **Coded indexes**: Tag bits vary per coded index type (1-5 bits). Size depends
  on max(row counts of target tables) shifted by tag bits.
- **Stream name alignment**: Stream names are 4-byte aligned; size includes padding.
- **RVA vs file offset**: For loaded images, RVA works directly. For files on disk,
  must translate RVA through section headers.
- **Little-endian**: All metadata is little-endian (no conversion needed on x64).

### Design Decisions

*Add decisions here as work progresses.*

