// ProtonOS - .NET Metadata Reader
// Parses CLI metadata from .NET assemblies (ECMA-335 compliant)

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Runtime;

/// <summary>
/// Metadata stream information
/// </summary>
public unsafe struct MetadataStream
{
    public uint Offset;     // Offset from metadata root
    public uint Size;       // Size in bytes
    public byte* Name;      // Pointer to null-terminated stream name
}

/// <summary>
/// Heap size flags from #~ stream header
/// </summary>
public static class HeapSizeFlags
{
    public const byte StringHeapLarge = 0x01;  // #Strings uses 4-byte indexes
    public const byte GuidHeapLarge = 0x02;    // #GUID uses 4-byte indexes
    public const byte BlobHeapLarge = 0x04;    // #Blob uses 4-byte indexes
}

/// <summary>
/// Coded index types for metadata table references (ECMA-335 II.24.2.6)
/// </summary>
public enum CodedIndexType
{
    TypeDefOrRef,       // TypeDef, TypeRef, TypeSpec (2 bits)
    HasConstant,        // Field, Param, Property (2 bits)
    HasCustomAttribute, // 22 targets (5 bits)
    HasFieldMarshal,    // Field, Param (1 bit)
    HasDeclSecurity,    // TypeDef, MethodDef, Assembly (2 bits)
    MemberRefParent,    // TypeDef, TypeRef, ModuleRef, MethodDef, TypeSpec (3 bits)
    HasSemantics,       // Event, Property (1 bit)
    MethodDefOrRef,     // MethodDef, MemberRef (1 bit)
    MemberForwarded,    // Field, MethodDef (1 bit)
    Implementation,     // File, AssemblyRef, ExportedType (2 bits)
    CustomAttributeType,// MethodDef, MemberRef (3 bits, tags 2,3 used)
    ResolutionScope,    // Module, ModuleRef, AssemblyRef, TypeRef (2 bits)
    TypeOrMethodDef     // TypeDef, MethodDef (1 bit)
}

/// <summary>
/// Metadata table identifiers (ECMA-335 II.22)
/// </summary>
public enum MetadataTableId : byte
{
    Module = 0x00,
    TypeRef = 0x01,
    TypeDef = 0x02,
    FieldPtr = 0x03,
    Field = 0x04,
    MethodPtr = 0x05,
    MethodDef = 0x06,
    ParamPtr = 0x07,
    Param = 0x08,
    InterfaceImpl = 0x09,
    MemberRef = 0x0A,
    Constant = 0x0B,
    CustomAttribute = 0x0C,
    FieldMarshal = 0x0D,
    DeclSecurity = 0x0E,
    ClassLayout = 0x0F,
    FieldLayout = 0x10,
    StandAloneSig = 0x11,
    EventMap = 0x12,
    EventPtr = 0x13,
    Event = 0x14,
    PropertyMap = 0x15,
    PropertyPtr = 0x16,
    Property = 0x17,
    MethodSemantics = 0x18,
    MethodImpl = 0x19,
    ModuleRef = 0x1A,
    TypeSpec = 0x1B,
    ImplMap = 0x1C,
    FieldRVA = 0x1D,
    EncLog = 0x1E,
    EncMap = 0x1F,
    Assembly = 0x20,
    AssemblyProcessor = 0x21,
    AssemblyOS = 0x22,
    AssemblyRef = 0x23,
    AssemblyRefProcessor = 0x24,
    AssemblyRefOS = 0x25,
    File = 0x26,
    ExportedType = 0x27,
    ManifestResource = 0x28,
    NestedClass = 0x29,
    GenericParam = 0x2A,
    MethodSpec = 0x2B,
    GenericParamConstraint = 0x2C,
    // 0x2D-0x3F reserved
    MaxTableId = 0x2C
}

/// <summary>
/// Parsed #~ (tables) stream header
/// </summary>
public unsafe struct TablesHeader
{
    public byte* Base;              // Pointer to #~ stream start
    public uint Size;               // Size of #~ stream
    public byte MajorVersion;       // Schema major (usually 2)
    public byte MinorVersion;       // Schema minor (usually 0)
    public byte HeapSizes;          // Heap size flags
    public ulong ValidTables;       // Bitmask of present tables
    public ulong SortedTables;      // Bitmask of sorted tables
    public fixed uint RowCounts[64]; // Row count per table (0 if not present)
    public byte* TableData;         // Pointer to start of table data
}

/// <summary>
/// Decoded coded index value with table ID and row index
/// </summary>
public struct CodedIndex
{
    public MetadataTableId Table;
    public uint RowId;  // 1-based row index
}

/// <summary>
/// Static helpers for coded index operations
/// </summary>
public static unsafe class CodedIndexHelper
{
    /// <summary>
    /// Get the number of tag bits for a coded index type
    /// </summary>
    public static int GetTagBits(CodedIndexType type)
    {
        return type switch
        {
            CodedIndexType.TypeDefOrRef => 2,
            CodedIndexType.HasConstant => 2,
            CodedIndexType.HasCustomAttribute => 5,
            CodedIndexType.HasFieldMarshal => 1,
            CodedIndexType.HasDeclSecurity => 2,
            CodedIndexType.MemberRefParent => 3,
            CodedIndexType.HasSemantics => 1,
            CodedIndexType.MethodDefOrRef => 1,
            CodedIndexType.MemberForwarded => 1,
            CodedIndexType.Implementation => 2,
            CodedIndexType.CustomAttributeType => 3,
            CodedIndexType.ResolutionScope => 2,
            CodedIndexType.TypeOrMethodDef => 1,
            _ => 0
        };
    }

    /// <summary>
    /// Get the tables referenced by a coded index type
    /// </summary>
    public static void GetTargetTables(CodedIndexType type, out MetadataTableId* tables, out int count)
    {
        // Stack-allocated arrays for each coded index type
        tables = null;
        count = 0;

        // We use static arrays since we can't easily allocate on demand
        // The caller should use the switch to get the right tables
    }

    /// <summary>
    /// Calculate coded index size (2 or 4 bytes) based on max row count of target tables
    /// </summary>
    public static int GetCodedIndexSize(CodedIndexType type, ref TablesHeader header)
    {
        int tagBits = GetTagBits(type);
        uint maxRows = GetMaxRowCount(type, ref header);

        // Index is large (4 bytes) if max rows shifted by tag bits exceeds 16 bits
        return (maxRows << tagBits) > 0xFFFF ? 4 : 2;
    }

    /// <summary>
    /// Get the maximum row count across all tables referenced by a coded index type
    /// </summary>
    public static uint GetMaxRowCount(CodedIndexType type, ref TablesHeader header)
    {
        uint max = 0;

        switch (type)
        {
            case CodedIndexType.TypeDefOrRef:
                max = MaxOf3(
                    header.RowCounts[(int)MetadataTableId.TypeDef],
                    header.RowCounts[(int)MetadataTableId.TypeRef],
                    header.RowCounts[(int)MetadataTableId.TypeSpec]);
                break;

            case CodedIndexType.HasConstant:
                max = MaxOf3(
                    header.RowCounts[(int)MetadataTableId.Field],
                    header.RowCounts[(int)MetadataTableId.Param],
                    header.RowCounts[(int)MetadataTableId.Property]);
                break;

            case CodedIndexType.HasFieldMarshal:
                max = Max(
                    header.RowCounts[(int)MetadataTableId.Field],
                    header.RowCounts[(int)MetadataTableId.Param]);
                break;

            case CodedIndexType.HasDeclSecurity:
                max = MaxOf3(
                    header.RowCounts[(int)MetadataTableId.TypeDef],
                    header.RowCounts[(int)MetadataTableId.MethodDef],
                    header.RowCounts[(int)MetadataTableId.Assembly]);
                break;

            case CodedIndexType.MemberRefParent:
                max = MaxOf5(
                    header.RowCounts[(int)MetadataTableId.TypeDef],
                    header.RowCounts[(int)MetadataTableId.TypeRef],
                    header.RowCounts[(int)MetadataTableId.ModuleRef],
                    header.RowCounts[(int)MetadataTableId.MethodDef],
                    header.RowCounts[(int)MetadataTableId.TypeSpec]);
                break;

            case CodedIndexType.HasSemantics:
                max = Max(
                    header.RowCounts[(int)MetadataTableId.Event],
                    header.RowCounts[(int)MetadataTableId.Property]);
                break;

            case CodedIndexType.MethodDefOrRef:
                max = Max(
                    header.RowCounts[(int)MetadataTableId.MethodDef],
                    header.RowCounts[(int)MetadataTableId.MemberRef]);
                break;

            case CodedIndexType.MemberForwarded:
                max = Max(
                    header.RowCounts[(int)MetadataTableId.Field],
                    header.RowCounts[(int)MetadataTableId.MethodDef]);
                break;

            case CodedIndexType.Implementation:
                max = MaxOf3(
                    header.RowCounts[(int)MetadataTableId.File],
                    header.RowCounts[(int)MetadataTableId.AssemblyRef],
                    header.RowCounts[(int)MetadataTableId.ExportedType]);
                break;

            case CodedIndexType.CustomAttributeType:
                // Only tags 2 (MethodDef) and 3 (MemberRef) are used
                max = Max(
                    header.RowCounts[(int)MetadataTableId.MethodDef],
                    header.RowCounts[(int)MetadataTableId.MemberRef]);
                break;

            case CodedIndexType.ResolutionScope:
                max = MaxOf5(
                    header.RowCounts[(int)MetadataTableId.Module],
                    header.RowCounts[(int)MetadataTableId.ModuleRef],
                    header.RowCounts[(int)MetadataTableId.AssemblyRef],
                    header.RowCounts[(int)MetadataTableId.TypeRef],
                    0);
                break;

            case CodedIndexType.TypeOrMethodDef:
                max = Max(
                    header.RowCounts[(int)MetadataTableId.TypeDef],
                    header.RowCounts[(int)MetadataTableId.MethodDef]);
                break;

            case CodedIndexType.HasCustomAttribute:
                // 22 target tables - take max of all
                max = MaxOf5(
                    header.RowCounts[(int)MetadataTableId.MethodDef],
                    header.RowCounts[(int)MetadataTableId.Field],
                    header.RowCounts[(int)MetadataTableId.TypeRef],
                    header.RowCounts[(int)MetadataTableId.TypeDef],
                    header.RowCounts[(int)MetadataTableId.Param]);
                max = MaxWithBase(max,
                    header.RowCounts[(int)MetadataTableId.InterfaceImpl],
                    header.RowCounts[(int)MetadataTableId.MemberRef],
                    header.RowCounts[(int)MetadataTableId.Module],
                    header.RowCounts[(int)MetadataTableId.DeclSecurity]);
                max = MaxWithBase(max,
                    header.RowCounts[(int)MetadataTableId.Property],
                    header.RowCounts[(int)MetadataTableId.Event],
                    header.RowCounts[(int)MetadataTableId.StandAloneSig],
                    header.RowCounts[(int)MetadataTableId.ModuleRef]);
                max = MaxWithBase(max,
                    header.RowCounts[(int)MetadataTableId.TypeSpec],
                    header.RowCounts[(int)MetadataTableId.Assembly],
                    header.RowCounts[(int)MetadataTableId.AssemblyRef],
                    header.RowCounts[(int)MetadataTableId.File]);
                max = MaxWithBase(max,
                    header.RowCounts[(int)MetadataTableId.ExportedType],
                    header.RowCounts[(int)MetadataTableId.ManifestResource],
                    header.RowCounts[(int)MetadataTableId.GenericParam],
                    header.RowCounts[(int)MetadataTableId.GenericParamConstraint]);
                max = Max(max, header.RowCounts[(int)MetadataTableId.MethodSpec]);
                break;
        }

        return max;
    }

    /// <summary>
    /// Decode a coded index value into table ID and row ID
    /// </summary>
    public static CodedIndex Decode(CodedIndexType type, uint value)
    {
        int tagBits = GetTagBits(type);
        uint tagMask = (1u << tagBits) - 1;
        uint tag = value & tagMask;
        uint rowId = value >> tagBits;

        MetadataTableId table = DecodeTag(type, tag);

        return new CodedIndex { Table = table, RowId = rowId };
    }

    /// <summary>
    /// Decode the tag portion of a coded index to a table ID
    /// </summary>
    private static MetadataTableId DecodeTag(CodedIndexType type, uint tag)
    {
        return type switch
        {
            CodedIndexType.TypeDefOrRef => tag switch
            {
                0 => MetadataTableId.TypeDef,
                1 => MetadataTableId.TypeRef,
                2 => MetadataTableId.TypeSpec,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.HasConstant => tag switch
            {
                0 => MetadataTableId.Field,
                1 => MetadataTableId.Param,
                2 => MetadataTableId.Property,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.HasFieldMarshal => tag switch
            {
                0 => MetadataTableId.Field,
                1 => MetadataTableId.Param,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.HasDeclSecurity => tag switch
            {
                0 => MetadataTableId.TypeDef,
                1 => MetadataTableId.MethodDef,
                2 => MetadataTableId.Assembly,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.MemberRefParent => tag switch
            {
                0 => MetadataTableId.TypeDef,
                1 => MetadataTableId.TypeRef,
                2 => MetadataTableId.ModuleRef,
                3 => MetadataTableId.MethodDef,
                4 => MetadataTableId.TypeSpec,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.HasSemantics => tag switch
            {
                0 => MetadataTableId.Event,
                1 => MetadataTableId.Property,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.MethodDefOrRef => tag switch
            {
                0 => MetadataTableId.MethodDef,
                1 => MetadataTableId.MemberRef,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.MemberForwarded => tag switch
            {
                0 => MetadataTableId.Field,
                1 => MetadataTableId.MethodDef,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.Implementation => tag switch
            {
                0 => MetadataTableId.File,
                1 => MetadataTableId.AssemblyRef,
                2 => MetadataTableId.ExportedType,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.CustomAttributeType => tag switch
            {
                2 => MetadataTableId.MethodDef,
                3 => MetadataTableId.MemberRef,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.ResolutionScope => tag switch
            {
                0 => MetadataTableId.Module,
                1 => MetadataTableId.ModuleRef,
                2 => MetadataTableId.AssemblyRef,
                3 => MetadataTableId.TypeRef,
                _ => (MetadataTableId)0xFF
            },
            CodedIndexType.TypeOrMethodDef => tag switch
            {
                0 => MetadataTableId.TypeDef,
                1 => MetadataTableId.MethodDef,
                _ => (MetadataTableId)0xFF
            },
            _ => (MetadataTableId)0xFF
        };
    }

    // Helper methods for max calculations
    private static uint Max(uint a, uint b) => a > b ? a : b;
    private static uint MaxOf3(uint a, uint b, uint c) => Max(Max(a, b), c);
    private static uint MaxOf5(uint a, uint b, uint c, uint d, uint e) => Max(Max(Max(Max(a, b), c), d), e);
    private static uint MaxWithBase(uint baseVal, uint a, uint b, uint c, uint d) => Max(Max(Max(Max(baseVal, a), b), c), d);
}

/// <summary>
/// Pre-calculated table row sizes and offsets for efficient reading
/// </summary>
public unsafe struct TableSizes
{
    // Heap index sizes (2 or 4 bytes)
    public int StringIndexSize;
    public int GuidIndexSize;
    public int BlobIndexSize;

    // Simple table index sizes (2 or 4 bytes based on row counts)
    public int FieldIndexSize;
    public int MethodIndexSize;
    public int ParamIndexSize;
    public int TypeDefIndexSize;

    // Coded index sizes
    public int TypeDefOrRefSize;
    public int ResolutionScopeSize;
    public int MemberRefParentSize;
    public int HasCustomAttributeSize;
    public int CustomAttributeTypeSize;

    // Row sizes for each table
    public fixed int RowSizes[64];

    // Table offsets from TableData pointer (cumulative)
    public fixed uint TableOffsets[64];

    /// <summary>
    /// Initialize table sizes from a parsed tables header
    /// </summary>
    public static TableSizes Calculate(ref TablesHeader header)
    {
        TableSizes sizes = default;

        // Heap index sizes
        sizes.StringIndexSize = (header.HeapSizes & HeapSizeFlags.StringHeapLarge) != 0 ? 4 : 2;
        sizes.GuidIndexSize = (header.HeapSizes & HeapSizeFlags.GuidHeapLarge) != 0 ? 4 : 2;
        sizes.BlobIndexSize = (header.HeapSizes & HeapSizeFlags.BlobHeapLarge) != 0 ? 4 : 2;

        // Simple table index sizes (large if >65535 rows)
        sizes.FieldIndexSize = header.RowCounts[(int)MetadataTableId.Field] > 0xFFFF ? 4 : 2;
        sizes.MethodIndexSize = header.RowCounts[(int)MetadataTableId.MethodDef] > 0xFFFF ? 4 : 2;
        sizes.ParamIndexSize = header.RowCounts[(int)MetadataTableId.Param] > 0xFFFF ? 4 : 2;
        sizes.TypeDefIndexSize = header.RowCounts[(int)MetadataTableId.TypeDef] > 0xFFFF ? 4 : 2;

        // Coded index sizes
        sizes.TypeDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.TypeDefOrRef, ref header);
        sizes.ResolutionScopeSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.ResolutionScope, ref header);
        sizes.MemberRefParentSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MemberRefParent, ref header);
        sizes.HasCustomAttributeSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasCustomAttribute, ref header);
        sizes.CustomAttributeTypeSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.CustomAttributeType, ref header);

        // Calculate row sizes for each present table
        // Module (0x00): Generation (2) + Name (str) + Mvid (guid) + EncId (guid) + EncBaseId (guid)
        sizes.RowSizes[(int)MetadataTableId.Module] =
            2 + sizes.StringIndexSize + sizes.GuidIndexSize * 3;

        // TypeRef (0x01): ResolutionScope (coded) + TypeName (str) + TypeNamespace (str)
        sizes.RowSizes[(int)MetadataTableId.TypeRef] =
            sizes.ResolutionScopeSize + sizes.StringIndexSize * 2;

        // TypeDef (0x02): Flags (4) + TypeName (str) + TypeNamespace (str) + Extends (coded) + FieldList + MethodList
        sizes.RowSizes[(int)MetadataTableId.TypeDef] =
            4 + sizes.StringIndexSize * 2 + sizes.TypeDefOrRefSize + sizes.FieldIndexSize + sizes.MethodIndexSize;

        // Field (0x04): Flags (2) + Name (str) + Signature (blob)
        sizes.RowSizes[(int)MetadataTableId.Field] =
            2 + sizes.StringIndexSize + sizes.BlobIndexSize;

        // MethodDef (0x06): RVA (4) + ImplFlags (2) + Flags (2) + Name (str) + Signature (blob) + ParamList
        sizes.RowSizes[(int)MetadataTableId.MethodDef] =
            4 + 2 + 2 + sizes.StringIndexSize + sizes.BlobIndexSize + sizes.ParamIndexSize;

        // Param (0x08): Flags (2) + Sequence (2) + Name (str)
        sizes.RowSizes[(int)MetadataTableId.Param] =
            2 + 2 + sizes.StringIndexSize;

        // InterfaceImpl (0x09): Class (TypeDef index) + Interface (TypeDefOrRef coded)
        sizes.RowSizes[(int)MetadataTableId.InterfaceImpl] =
            sizes.TypeDefIndexSize + sizes.TypeDefOrRefSize;

        // MemberRef (0x0A): Class (MemberRefParent coded) + Name (str) + Signature (blob)
        sizes.RowSizes[(int)MetadataTableId.MemberRef] =
            sizes.MemberRefParentSize + sizes.StringIndexSize + sizes.BlobIndexSize;

        // Constant (0x0B): Type (2) + Parent (HasConstant coded) + Value (blob)
        int hasConstantSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasConstant, ref header);
        sizes.RowSizes[(int)MetadataTableId.Constant] =
            2 + hasConstantSize + sizes.BlobIndexSize;

        // CustomAttribute (0x0C): Parent (HasCustomAttribute coded) + Type (CustomAttributeType coded) + Value (blob)
        sizes.RowSizes[(int)MetadataTableId.CustomAttribute] =
            sizes.HasCustomAttributeSize + sizes.CustomAttributeTypeSize + sizes.BlobIndexSize;

        // Assembly (0x20): HashAlgId (4) + MajorVersion (2) + MinorVersion (2) + BuildNumber (2) + RevisionNumber (2) +
        //                  Flags (4) + PublicKey (blob) + Name (str) + Culture (str)
        sizes.RowSizes[(int)MetadataTableId.Assembly] =
            4 + 2 + 2 + 2 + 2 + 4 + sizes.BlobIndexSize + sizes.StringIndexSize * 2;

        // AssemblyRef (0x23): MajorVersion (2) + MinorVersion (2) + BuildNumber (2) + RevisionNumber (2) +
        //                     Flags (4) + PublicKeyOrToken (blob) + Name (str) + Culture (str) + HashValue (blob)
        sizes.RowSizes[(int)MetadataTableId.AssemblyRef] =
            2 + 2 + 2 + 2 + 4 + sizes.BlobIndexSize + sizes.StringIndexSize * 2 + sizes.BlobIndexSize;

        // Calculate table offsets (cumulative based on row counts and sizes)
        uint offset = 0;
        for (int tableId = 0; tableId <= (int)MetadataTableId.MaxTableId; tableId++)
        {
            sizes.TableOffsets[tableId] = offset;
            if ((header.ValidTables & (1UL << tableId)) != 0)
            {
                offset += header.RowCounts[tableId] * (uint)sizes.RowSizes[tableId];
            }
        }

        return sizes;
    }
}

/// <summary>
/// Parsed metadata root with stream accessors
/// </summary>
public unsafe struct MetadataRoot
{
    public byte* Base;              // Pointer to metadata root (BSJB signature)
    public uint Size;               // Total metadata size
    public ushort MajorVersion;     // Usually 1
    public ushort MinorVersion;     // Usually 1
    public byte* VersionString;     // Runtime version string (e.g., "v4.0.30319")
    public uint VersionStringLength;
    public ushort StreamCount;      // Number of streams
    public byte* StreamsStart;      // Pointer to first stream header

    // Stream pointers (null if not present)
    public byte* TablesStream;      // #~ stream (compressed tables)
    public uint TablesStreamSize;
    public byte* StringsHeap;       // #Strings heap
    public uint StringsHeapSize;
    public byte* USHeap;            // #US (user strings) heap
    public uint USHeapSize;
    public byte* GUIDHeap;          // #GUID heap
    public uint GUIDHeapSize;
    public byte* BlobHeap;          // #Blob heap
    public uint BlobHeapSize;
}

/// <summary>
/// Metadata reader for .NET assemblies
/// </summary>
public static unsafe class MetadataReader
{
    /// <summary>
    /// Initialize metadata root from a pointer to the BSJB signature.
    /// </summary>
    /// <param name="metadataBase">Pointer to metadata root (from PEHelper.GetMetadataRootFromFile)</param>
    /// <param name="metadataSize">Size of metadata section from CLI header</param>
    /// <param name="root">Output metadata root structure</param>
    /// <returns>True if metadata was parsed successfully</returns>
    public static bool Init(byte* metadataBase, uint metadataSize, out MetadataRoot root)
    {
        root = default;

        if (metadataBase == null)
            return false;

        // Verify signature
        uint signature = *(uint*)metadataBase;
        if (signature != PEConstants.METADATA_SIGNATURE)
            return false;

        root.Base = metadataBase;
        root.Size = metadataSize;

        // Parse STORAGESIGNATURE
        // Offset 0: uint32 signature (BSJB)
        // Offset 4: uint16 major version
        // Offset 6: uint16 minor version
        // Offset 8: uint32 reserved/extra data
        // Offset 12: uint32 version string length
        // Offset 16: char[] version string (padded to 4 bytes)
        root.MajorVersion = *(ushort*)(metadataBase + 4);
        root.MinorVersion = *(ushort*)(metadataBase + 6);
        root.VersionStringLength = *(uint*)(metadataBase + 12);
        root.VersionString = metadataBase + 16;

        // Calculate offset to STORAGEHEADER (after version string, 4-byte aligned)
        uint versionPadded = (root.VersionStringLength + 3) & ~3u;
        byte* storageHeader = metadataBase + 16 + versionPadded;

        // Parse STORAGEHEADER
        // Offset 0: uint8 flags
        // Offset 1: uint8 pad
        // Offset 2: uint16 stream count
        root.StreamCount = *(ushort*)(storageHeader + 2);
        root.StreamsStart = storageHeader + 4;

        // Parse stream headers
        byte* streamPtr = root.StreamsStart;
        for (int i = 0; i < root.StreamCount; i++)
        {
            uint offset = *(uint*)streamPtr;
            uint size = *(uint*)(streamPtr + 4);
            byte* name = streamPtr + 8;

            // Find stream name length (null-terminated, 4-byte aligned total)
            int nameLen = 0;
            while (name[nameLen] != 0)
                nameLen++;

            // Identify stream by name and store pointer
            if (CompareStreamName(name, "#~"))
            {
                root.TablesStream = metadataBase + offset;
                root.TablesStreamSize = size;
            }
            else if (CompareStreamName(name, "#Strings"))
            {
                root.StringsHeap = metadataBase + offset;
                root.StringsHeapSize = size;
            }
            else if (CompareStreamName(name, "#US"))
            {
                root.USHeap = metadataBase + offset;
                root.USHeapSize = size;
            }
            else if (CompareStreamName(name, "#GUID"))
            {
                root.GUIDHeap = metadataBase + offset;
                root.GUIDHeapSize = size;
            }
            else if (CompareStreamName(name, "#Blob"))
            {
                root.BlobHeap = metadataBase + offset;
                root.BlobHeapSize = size;
            }
            // #Pdb and #- (uncompressed tables) are ignored for now

            // Move to next stream header (name is 4-byte aligned)
            int namePadded = (nameLen + 1 + 3) & ~3;
            streamPtr += 8 + namePadded;
        }

        return true;
    }

    /// <summary>
    /// Get a stream by index (for enumeration)
    /// </summary>
    public static bool GetStream(ref MetadataRoot root, int index, out MetadataStream stream)
    {
        stream = default;

        if (index < 0 || index >= root.StreamCount)
            return false;

        byte* streamPtr = root.StreamsStart;
        for (int i = 0; i < index; i++)
        {
            // Skip to next stream header
            byte* name = streamPtr + 8;
            int nameLen = 0;
            while (name[nameLen] != 0)
                nameLen++;
            int namePadded = (nameLen + 1 + 3) & ~3;
            streamPtr += 8 + namePadded;
        }

        stream.Offset = *(uint*)streamPtr;
        stream.Size = *(uint*)(streamPtr + 4);
        stream.Name = streamPtr + 8;

        return true;
    }

    /// <summary>
    /// Get a null-terminated string from the #Strings heap
    /// </summary>
    public static byte* GetString(ref MetadataRoot root, uint index)
    {
        if (root.StringsHeap == null || index >= root.StringsHeapSize)
            return null;
        return root.StringsHeap + index;
    }

    /// <summary>
    /// Compare a stream name (null-terminated bytes) with a string literal
    /// </summary>
    private static bool CompareStreamName(byte* name, string expected)
    {
        for (int i = 0; i < expected.Length; i++)
        {
            if (name[i] != (byte)expected[i])
                return false;
        }
        return name[expected.Length] == 0;
    }

    /// <summary>
    /// Dump metadata information for debugging
    /// </summary>
    public static void Dump(ref MetadataRoot root)
    {
        DebugConsole.Write("[Meta] Version ");
        DebugConsole.WriteDecimal(root.MajorVersion);
        DebugConsole.Write(".");
        DebugConsole.WriteDecimal(root.MinorVersion);
        DebugConsole.Write(", ");
        DebugConsole.WriteDecimal(root.StreamCount);
        DebugConsole.WriteLine(" streams");

        // Print version string
        DebugConsole.Write("[Meta] Runtime: ");
        for (uint i = 0; i < root.VersionStringLength && root.VersionString[i] != 0; i++)
        {
            DebugConsole.WriteChar((char)root.VersionString[i]);
        }
        DebugConsole.WriteLine();

        // Print each stream
        for (int i = 0; i < root.StreamCount; i++)
        {
            if (GetStream(ref root, i, out var stream))
            {
                DebugConsole.Write("[Meta]   ");

                // Print stream name
                byte* n = stream.Name;
                while (*n != 0)
                {
                    DebugConsole.WriteChar((char)*n);
                    n++;
                }

                DebugConsole.Write(" offset=0x");
                DebugConsole.WriteHex(stream.Offset);
                DebugConsole.Write(" size=0x");
                DebugConsole.WriteHex(stream.Size);
                DebugConsole.WriteLine();
            }
        }
    }

    /// <summary>
    /// Parse the #~ (tables) stream header
    /// </summary>
    public static bool ParseTablesHeader(ref MetadataRoot root, out TablesHeader header)
    {
        header = default;

        if (root.TablesStream == null)
            return false;

        byte* ptr = root.TablesStream;
        header.Base = ptr;
        header.Size = root.TablesStreamSize;

        // #~ header layout:
        // Offset 0: uint32 reserved (must be 0)
        // Offset 4: uint8 major version
        // Offset 5: uint8 minor version
        // Offset 6: uint8 heap sizes
        // Offset 7: uint8 reserved
        // Offset 8: uint64 valid tables bitmask
        // Offset 16: uint64 sorted tables bitmask
        // Offset 24: uint32[] row counts (one per set bit in valid tables)

        uint reserved = *(uint*)ptr;
        if (reserved != 0)
        {
            DebugConsole.Write("[Meta] WARNING: #~ reserved field non-zero: 0x");
            DebugConsole.WriteHex(reserved);
            DebugConsole.WriteLine();
        }

        header.MajorVersion = ptr[4];
        header.MinorVersion = ptr[5];
        header.HeapSizes = ptr[6];
        header.ValidTables = *(ulong*)(ptr + 8);
        header.SortedTables = *(ulong*)(ptr + 16);

        // Read row counts for each present table
        byte* rowCountPtr = ptr + 24;
        for (int tableId = 0; tableId <= (int)MetadataTableId.MaxTableId; tableId++)
        {
            if ((header.ValidTables & (1UL << tableId)) != 0)
            {
                header.RowCounts[tableId] = *(uint*)rowCountPtr;
                rowCountPtr += 4;
            }
        }

        // Table data starts after all row counts
        header.TableData = rowCountPtr;

        return true;
    }

    /// <summary>
    /// Dump tables header for debugging
    /// </summary>
    public static void DumpTablesHeader(ref TablesHeader header)
    {
        DebugConsole.Write("[Meta] #~ schema ");
        DebugConsole.WriteDecimal(header.MajorVersion);
        DebugConsole.Write(".");
        DebugConsole.WriteDecimal(header.MinorVersion);
        DebugConsole.Write(", heaps=0x");
        DebugConsole.WriteHex((ushort)header.HeapSizes);
        DebugConsole.WriteLine();

        // Heap size info
        DebugConsole.Write("[Meta]   Heap indexes: Strings=");
        DebugConsole.WriteDecimal((header.HeapSizes & HeapSizeFlags.StringHeapLarge) != 0 ? 4 : 2);
        DebugConsole.Write(", GUID=");
        DebugConsole.WriteDecimal((header.HeapSizes & HeapSizeFlags.GuidHeapLarge) != 0 ? 4 : 2);
        DebugConsole.Write(", Blob=");
        DebugConsole.WriteDecimal((header.HeapSizes & HeapSizeFlags.BlobHeapLarge) != 0 ? 4 : 2);
        DebugConsole.WriteLine(" bytes");

        // Count present tables
        int tableCount = 0;
        for (int i = 0; i <= (int)MetadataTableId.MaxTableId; i++)
        {
            if ((header.ValidTables & (1UL << i)) != 0)
                tableCount++;
        }
        DebugConsole.Write("[Meta]   ");
        DebugConsole.WriteDecimal(tableCount);
        DebugConsole.WriteLine(" tables present:");

        // Print each present table with row count
        for (int tableId = 0; tableId <= (int)MetadataTableId.MaxTableId; tableId++)
        {
            if ((header.ValidTables & (1UL << tableId)) != 0)
            {
                DebugConsole.Write("[Meta]     ");
                PrintTableName(tableId);
                DebugConsole.Write(" (0x");
                DebugConsole.WriteHex((ushort)tableId);
                DebugConsole.Write("): ");
                DebugConsole.WriteDecimal(header.RowCounts[tableId]);
                DebugConsole.WriteLine(" rows");
            }
        }
    }

    /// <summary>
    /// Print table name for debugging
    /// </summary>
    private static void PrintTableName(int tableId)
    {
        switch ((MetadataTableId)tableId)
        {
            case MetadataTableId.Module: DebugConsole.Write("Module"); break;
            case MetadataTableId.TypeRef: DebugConsole.Write("TypeRef"); break;
            case MetadataTableId.TypeDef: DebugConsole.Write("TypeDef"); break;
            case MetadataTableId.Field: DebugConsole.Write("Field"); break;
            case MetadataTableId.MethodDef: DebugConsole.Write("MethodDef"); break;
            case MetadataTableId.Param: DebugConsole.Write("Param"); break;
            case MetadataTableId.InterfaceImpl: DebugConsole.Write("InterfaceImpl"); break;
            case MetadataTableId.MemberRef: DebugConsole.Write("MemberRef"); break;
            case MetadataTableId.Constant: DebugConsole.Write("Constant"); break;
            case MetadataTableId.CustomAttribute: DebugConsole.Write("CustomAttribute"); break;
            case MetadataTableId.FieldMarshal: DebugConsole.Write("FieldMarshal"); break;
            case MetadataTableId.DeclSecurity: DebugConsole.Write("DeclSecurity"); break;
            case MetadataTableId.ClassLayout: DebugConsole.Write("ClassLayout"); break;
            case MetadataTableId.FieldLayout: DebugConsole.Write("FieldLayout"); break;
            case MetadataTableId.StandAloneSig: DebugConsole.Write("StandAloneSig"); break;
            case MetadataTableId.EventMap: DebugConsole.Write("EventMap"); break;
            case MetadataTableId.Event: DebugConsole.Write("Event"); break;
            case MetadataTableId.PropertyMap: DebugConsole.Write("PropertyMap"); break;
            case MetadataTableId.Property: DebugConsole.Write("Property"); break;
            case MetadataTableId.MethodSemantics: DebugConsole.Write("MethodSemantics"); break;
            case MetadataTableId.MethodImpl: DebugConsole.Write("MethodImpl"); break;
            case MetadataTableId.ModuleRef: DebugConsole.Write("ModuleRef"); break;
            case MetadataTableId.TypeSpec: DebugConsole.Write("TypeSpec"); break;
            case MetadataTableId.ImplMap: DebugConsole.Write("ImplMap"); break;
            case MetadataTableId.FieldRVA: DebugConsole.Write("FieldRVA"); break;
            case MetadataTableId.Assembly: DebugConsole.Write("Assembly"); break;
            case MetadataTableId.AssemblyRef: DebugConsole.Write("AssemblyRef"); break;
            case MetadataTableId.File: DebugConsole.Write("File"); break;
            case MetadataTableId.ExportedType: DebugConsole.Write("ExportedType"); break;
            case MetadataTableId.ManifestResource: DebugConsole.Write("ManifestResource"); break;
            case MetadataTableId.NestedClass: DebugConsole.Write("NestedClass"); break;
            case MetadataTableId.GenericParam: DebugConsole.Write("GenericParam"); break;
            case MetadataTableId.MethodSpec: DebugConsole.Write("MethodSpec"); break;
            case MetadataTableId.GenericParamConstraint: DebugConsole.Write("GenericParamConstraint"); break;
            default: DebugConsole.Write("Unknown"); break;
        }
    }

    /// <summary>
    /// Read a compressed unsigned integer from blob data (ECMA-335 II.23.2)
    /// </summary>
    /// <param name="ptr">Pointer to compressed data (advanced past the integer)</param>
    /// <returns>Decoded unsigned integer value</returns>
    public static uint ReadCompressedUInt(ref byte* ptr)
    {
        byte first = *ptr++;

        // Single byte: 0xxxxxxx (0-127)
        if ((first & 0x80) == 0)
            return first;

        // Two bytes: 10xxxxxx xxxxxxxx (0-16383)
        if ((first & 0xC0) == 0x80)
        {
            byte second = *ptr++;
            return (uint)(((first & 0x3F) << 8) | second);
        }

        // Four bytes: 110xxxxx xxxxxxxx xxxxxxxx xxxxxxxx (0-536870911)
        if ((first & 0xE0) == 0xC0)
        {
            byte b1 = *ptr++;
            byte b2 = *ptr++;
            byte b3 = *ptr++;
            return (uint)(((first & 0x1F) << 24) | (b1 << 16) | (b2 << 8) | b3);
        }

        // Invalid encoding
        return 0;
    }

    /// <summary>
    /// Read a compressed signed integer from blob data (ECMA-335 II.23.2)
    /// Used for encoding things like branch offsets in signatures.
    /// </summary>
    /// <param name="ptr">Pointer to compressed data (advanced past the integer)</param>
    /// <returns>Decoded signed integer value</returns>
    public static int ReadCompressedInt(ref byte* ptr)
    {
        uint raw = ReadCompressedUInt(ref ptr);

        // The sign bit is stored in the least significant bit
        // If bit 0 is set, the value is negative
        if ((raw & 1) != 0)
        {
            // Negative: complement and negate
            // For 1-byte: values -64 to -1 are encoded as 0x01 to 0x7F (odd)
            // For 2-byte: values -8192 to -1
            // For 4-byte: values -268435456 to -1
            return -((int)(raw >> 1)) - 1;
        }
        else
        {
            // Positive: just shift right
            return (int)(raw >> 1);
        }
    }

    /// <summary>
    /// Get blob data from #Blob heap by index
    /// </summary>
    /// <param name="root">Metadata root</param>
    /// <param name="index">Blob heap index</param>
    /// <param name="length">Output: blob length in bytes</param>
    /// <returns>Pointer to blob data (after length prefix), or null if invalid</returns>
    public static byte* GetBlob(ref MetadataRoot root, uint index, out uint length)
    {
        length = 0;

        if (root.BlobHeap == null || index >= root.BlobHeapSize)
            return null;

        byte* ptr = root.BlobHeap + index;
        length = ReadCompressedUInt(ref ptr);

        return ptr;
    }

    /// <summary>
    /// Get a GUID from #GUID heap by 1-based index
    /// </summary>
    /// <param name="root">Metadata root</param>
    /// <param name="index">1-based GUID index (0 means null GUID)</param>
    /// <returns>Pointer to 16-byte GUID, or null if invalid</returns>
    public static byte* GetGuid(ref MetadataRoot root, uint index)
    {
        if (index == 0 || root.GUIDHeap == null)
            return null;

        // GUID heap indexes are 1-based, each GUID is 16 bytes
        uint offset = (index - 1) * 16;
        if (offset + 16 > root.GUIDHeapSize)
            return null;

        return root.GUIDHeap + offset;
    }

    /// <summary>
    /// Get a user string from #US heap by index
    /// </summary>
    /// <param name="root">Metadata root</param>
    /// <param name="index">User string heap index</param>
    /// <param name="charCount">Output: number of UTF-16 characters</param>
    /// <returns>Pointer to UTF-16 string data, or null if invalid</returns>
    public static ushort* GetUserString(ref MetadataRoot root, uint index, out uint charCount)
    {
        charCount = 0;

        if (root.USHeap == null || index >= root.USHeapSize)
            return null;

        byte* ptr = root.USHeap + index;
        uint byteLength = ReadCompressedUInt(ref ptr);

        // User strings are UTF-16 with a trailing byte (0 or 1)
        // The byte count includes the trailing byte, so char count = (byteLength - 1) / 2
        if (byteLength > 0)
            charCount = (byteLength - 1) / 2;

        return (ushort*)ptr;
    }

    /// <summary>
    /// Print a string from the #Strings heap for debugging
    /// </summary>
    public static void PrintString(ref MetadataRoot root, uint index)
    {
        byte* str = GetString(ref root, index);
        if (str == null)
        {
            DebugConsole.Write("<null>");
            return;
        }
        while (*str != 0)
        {
            DebugConsole.WriteChar((char)*str);
            str++;
        }
    }

    /// <summary>
    /// Print a GUID from the #GUID heap for debugging
    /// </summary>
    public static void PrintGuid(ref MetadataRoot root, uint index)
    {
        byte* guid = GetGuid(ref root, index);
        if (guid == null)
        {
            DebugConsole.Write("<null>");
            return;
        }
        // GUID format: {xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}
        // Layout: Data1 (4), Data2 (2), Data3 (2), Data4 (8)
        DebugConsole.Write("{");

        // Data1 - 4 bytes, little-endian
        uint data1 = *(uint*)guid;
        DebugConsole.WriteHex(data1);
        DebugConsole.Write("-");

        // Data2 - 2 bytes, little-endian
        ushort data2 = *(ushort*)(guid + 4);
        DebugConsole.WriteHex(data2);
        DebugConsole.Write("-");

        // Data3 - 2 bytes, little-endian
        ushort data3 = *(ushort*)(guid + 6);
        DebugConsole.WriteHex(data3);
        DebugConsole.Write("-");

        // Data4[0..1] - 2 bytes, big-endian display
        DebugConsole.WriteHex(guid[8]);
        DebugConsole.WriteHex(guid[9]);
        DebugConsole.Write("-");

        // Data4[2..7] - 6 bytes, big-endian display
        for (int i = 10; i < 16; i++)
        {
            DebugConsole.WriteHex(guid[i]);
        }
        DebugConsole.Write("}");
    }

    /// <summary>
    /// Test heap access by reading the Module table.
    /// Module table (0x00) has columns: Generation (2), Name (String), Mvid (GUID), EncId (GUID), EncBaseId (GUID)
    /// </summary>
    public static void DumpModuleTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        if (tables.RowCounts[(int)MetadataTableId.Module] == 0)
        {
            DebugConsole.WriteLine("[Meta] No Module table");
            return;
        }

        // Calculate column sizes based on heap size flags
        int stringIndexSize = (tables.HeapSizes & HeapSizeFlags.StringHeapLarge) != 0 ? 4 : 2;
        int guidIndexSize = (tables.HeapSizes & HeapSizeFlags.GuidHeapLarge) != 0 ? 4 : 2;

        // Module row: Generation (2) + Name (string) + Mvid (guid) + EncId (guid) + EncBaseId (guid)
        byte* ptr = tables.TableData;

        // Read Generation (2 bytes)
        ushort generation = *(ushort*)ptr;
        ptr += 2;

        // Read Name (string index)
        uint nameIndex = stringIndexSize == 4 ? *(uint*)ptr : *(ushort*)ptr;
        ptr += stringIndexSize;

        // Read Mvid (GUID index)
        uint mvidIndex = guidIndexSize == 4 ? *(uint*)ptr : *(ushort*)ptr;
        ptr += guidIndexSize;

        // Read EncId (GUID index) - usually 0
        uint encIdIndex = guidIndexSize == 4 ? *(uint*)ptr : *(ushort*)ptr;
        ptr += guidIndexSize;

        // Read EncBaseId (GUID index) - usually 0
        uint encBaseIdIndex = guidIndexSize == 4 ? *(uint*)ptr : *(ushort*)ptr;

        DebugConsole.Write("[Meta] Module: \"");
        PrintString(ref root, nameIndex);
        DebugConsole.Write("\", gen=");
        DebugConsole.WriteDecimal(generation);
        DebugConsole.WriteLine();

        DebugConsole.Write("[Meta]   Mvid: ");
        PrintGuid(ref root, mvidIndex);
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Test heap access by reading TypeDef table entries.
    /// TypeDef table (0x02): Flags (4), TypeName (String), TypeNamespace (String), Extends (coded), FieldList, MethodList
    /// </summary>
    public static void DumpTypeDefTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        uint rowCount = tables.RowCounts[(int)MetadataTableId.TypeDef];
        if (rowCount == 0)
        {
            DebugConsole.WriteLine("[Meta] No TypeDef table");
            return;
        }

        // Calculate column sizes
        int stringIndexSize = (tables.HeapSizes & HeapSizeFlags.StringHeapLarge) != 0 ? 4 : 2;

        // For coded index TypeDefOrRef: tag is 2 bits, targets TypeDef, TypeRef, TypeSpec
        // Index size depends on max(TypeDef rows, TypeRef rows, TypeSpec rows) << 2
        uint typeDefRows = tables.RowCounts[(int)MetadataTableId.TypeDef];
        uint typeRefRows = tables.RowCounts[(int)MetadataTableId.TypeRef];
        uint typeSpecRows = tables.RowCounts[(int)MetadataTableId.TypeSpec];
        uint maxRows = typeDefRows > typeRefRows ? typeDefRows : typeRefRows;
        if (typeSpecRows > maxRows) maxRows = typeSpecRows;
        int typeDefOrRefSize = (maxRows << 2) > 0xFFFF ? 4 : 2;

        // FieldList and MethodList are simple indexes into Field/MethodDef tables
        uint fieldRows = tables.RowCounts[(int)MetadataTableId.Field];
        uint methodRows = tables.RowCounts[(int)MetadataTableId.MethodDef];
        int fieldIndexSize = fieldRows > 0xFFFF ? 4 : 2;
        int methodIndexSize = methodRows > 0xFFFF ? 4 : 2;

        // TypeDef row size
        int rowSize = 4 + stringIndexSize + stringIndexSize + typeDefOrRefSize + fieldIndexSize + methodIndexSize;

        // Skip Module table to get to TypeDef table data
        // First we need to skip all tables before TypeDef (0x02)
        byte* ptr = tables.TableData;

        // Skip Module (0x00): 2 + string + guid + guid + guid
        int guidIndexSize = (tables.HeapSizes & HeapSizeFlags.GuidHeapLarge) != 0 ? 4 : 2;
        int moduleRowSize = 2 + stringIndexSize + guidIndexSize * 3;
        ptr += moduleRowSize * tables.RowCounts[(int)MetadataTableId.Module];

        // Skip TypeRef (0x01): ResolutionScope (coded) + TypeName (string) + TypeNamespace (string)
        // ResolutionScope targets Module, ModuleRef, AssemblyRef, TypeRef (4 options, 2-bit tag)
        uint moduleRefRows = tables.RowCounts[(int)MetadataTableId.ModuleRef];
        uint assemblyRefRows = tables.RowCounts[(int)MetadataTableId.AssemblyRef];
        uint moduleRows = tables.RowCounts[(int)MetadataTableId.Module];
        uint resMax = moduleRows > moduleRefRows ? moduleRows : moduleRefRows;
        if (assemblyRefRows > resMax) resMax = assemblyRefRows;
        if (typeRefRows > resMax) resMax = typeRefRows;
        int resScopeSize = (resMax << 2) > 0xFFFF ? 4 : 2;
        int typeRefRowSize = resScopeSize + stringIndexSize + stringIndexSize;
        ptr += typeRefRowSize * tables.RowCounts[(int)MetadataTableId.TypeRef];

        DebugConsole.Write("[Meta] TypeDef table (");
        DebugConsole.WriteDecimal(rowCount);
        DebugConsole.WriteLine(" rows):");

        for (uint i = 0; i < rowCount; i++)
        {
            byte* rowPtr = ptr + (i * rowSize);

            // Read Flags (4 bytes)
            uint flags = *(uint*)rowPtr;
            rowPtr += 4;

            // Read TypeName (string index)
            uint nameIndex = stringIndexSize == 4 ? *(uint*)rowPtr : *(ushort*)rowPtr;
            rowPtr += stringIndexSize;

            // Read TypeNamespace (string index)
            uint namespaceIndex = stringIndexSize == 4 ? *(uint*)rowPtr : *(ushort*)rowPtr;

            DebugConsole.Write("[Meta]   ");
            if (namespaceIndex != 0)
            {
                PrintString(ref root, namespaceIndex);
                DebugConsole.Write(".");
            }
            PrintString(ref root, nameIndex);
            DebugConsole.Write(" (flags=0x");
            DebugConsole.WriteHex(flags);
            DebugConsole.WriteLine(")");
        }
    }

    // ============================================================================
    // Low-level index reading helpers
    // ============================================================================

    /// <summary>
    /// Read a 2-byte or 4-byte index from a pointer
    /// </summary>
    public static uint ReadIndex(byte* ptr, int size)
    {
        return size == 4 ? *(uint*)ptr : *(ushort*)ptr;
    }

    /// <summary>
    /// Get pointer to a specific row in a table
    /// </summary>
    public static byte* GetTableRow(ref TablesHeader tables, ref TableSizes sizes, MetadataTableId tableId, uint rowId)
    {
        if (rowId == 0 || rowId > tables.RowCounts[(int)tableId])
            return null;

        return tables.TableData + sizes.TableOffsets[(int)tableId] + ((rowId - 1) * (uint)sizes.RowSizes[(int)tableId]);
    }

    // ============================================================================
    // TypeRef table accessors (0x01)
    // ============================================================================

    /// <summary>
    /// Get the ResolutionScope coded index for a TypeRef row
    /// </summary>
    public static CodedIndex GetTypeRefResolutionScope(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeRef, rowId);
        if (row == null)
            return default;

        uint value = ReadIndex(row, sizes.ResolutionScopeSize);
        return CodedIndexHelper.Decode(CodedIndexType.ResolutionScope, value);
    }

    /// <summary>
    /// Get the TypeName string index for a TypeRef row
    /// </summary>
    public static uint GetTypeRefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeRef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.ResolutionScopeSize, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the TypeNamespace string index for a TypeRef row
    /// </summary>
    public static uint GetTypeRefNamespace(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeRef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.ResolutionScopeSize + sizes.StringIndexSize, sizes.StringIndexSize);
    }

    // ============================================================================
    // TypeDef table accessors (0x02)
    // ============================================================================

    /// <summary>
    /// Get the Flags for a TypeDef row
    /// </summary>
    public static uint GetTypeDefFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the TypeName string index for a TypeDef row
    /// </summary>
    public static uint GetTypeDefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the TypeNamespace string index for a TypeDef row
    /// </summary>
    public static uint GetTypeDefNamespace(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4 + sizes.StringIndexSize, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Extends coded index for a TypeDef row
    /// </summary>
    public static CodedIndex GetTypeDefExtends(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return default;

        uint value = ReadIndex(row + 4 + sizes.StringIndexSize * 2, sizes.TypeDefOrRefSize);
        return CodedIndexHelper.Decode(CodedIndexType.TypeDefOrRef, value);
    }

    /// <summary>
    /// Get the FieldList index for a TypeDef row (1-based index into Field table)
    /// </summary>
    public static uint GetTypeDefFieldList(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4 + sizes.StringIndexSize * 2 + sizes.TypeDefOrRefSize, sizes.FieldIndexSize);
    }

    /// <summary>
    /// Get the MethodList index for a TypeDef row (1-based index into MethodDef table)
    /// </summary>
    public static uint GetTypeDefMethodList(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4 + sizes.StringIndexSize * 2 + sizes.TypeDefOrRefSize + sizes.FieldIndexSize, sizes.MethodIndexSize);
    }

    // ============================================================================
    // MethodDef table accessors (0x06)
    // ============================================================================

    /// <summary>
    /// Get the RVA for a MethodDef row
    /// </summary>
    public static uint GetMethodDefRva(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the ImplFlags for a MethodDef row
    /// </summary>
    public static ushort GetMethodDefImplFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 4);
    }

    /// <summary>
    /// Get the Flags for a MethodDef row
    /// </summary>
    public static ushort GetMethodDefFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 6);
    }

    /// <summary>
    /// Get the Name string index for a MethodDef row
    /// </summary>
    public static uint GetMethodDefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Signature blob index for a MethodDef row
    /// </summary>
    public static uint GetMethodDefSignature(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8 + sizes.StringIndexSize, sizes.BlobIndexSize);
    }

    /// <summary>
    /// Get the ParamList index for a MethodDef row (1-based index into Param table)
    /// </summary>
    public static uint GetMethodDefParamList(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodDef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8 + sizes.StringIndexSize + sizes.BlobIndexSize, sizes.ParamIndexSize);
    }

    // ============================================================================
    // Dump methods using the new infrastructure
    // ============================================================================

    /// <summary>
    /// Dump TypeRef table with full details
    /// </summary>
    public static void DumpTypeRefTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        uint rowCount = tables.RowCounts[(int)MetadataTableId.TypeRef];
        if (rowCount == 0)
        {
            DebugConsole.WriteLine("[Meta] No TypeRef table");
            return;
        }

        var sizes = TableSizes.Calculate(ref tables);

        DebugConsole.Write("[Meta] TypeRef table (");
        DebugConsole.WriteDecimal(rowCount);
        DebugConsole.WriteLine(" rows):");

        for (uint i = 1; i <= rowCount; i++)
        {
            var scope = GetTypeRefResolutionScope(ref tables, ref sizes, i);
            uint nameIdx = GetTypeRefName(ref tables, ref sizes, i);
            uint nsIdx = GetTypeRefNamespace(ref tables, ref sizes, i);

            DebugConsole.Write("[Meta]   ");
            if (nsIdx != 0)
            {
                PrintString(ref root, nsIdx);
                DebugConsole.Write(".");
            }
            PrintString(ref root, nameIdx);
            DebugConsole.Write(" -> ");
            PrintTableName((int)scope.Table);
            DebugConsole.Write("[");
            DebugConsole.WriteDecimal(scope.RowId);
            DebugConsole.WriteLine("]");
        }
    }

    /// <summary>
    /// Dump MethodDef table with full details
    /// </summary>
    public static void DumpMethodDefTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        uint rowCount = tables.RowCounts[(int)MetadataTableId.MethodDef];
        if (rowCount == 0)
        {
            DebugConsole.WriteLine("[Meta] No MethodDef table");
            return;
        }

        var sizes = TableSizes.Calculate(ref tables);

        DebugConsole.Write("[Meta] MethodDef table (");
        DebugConsole.WriteDecimal(rowCount);
        DebugConsole.WriteLine(" rows):");

        for (uint i = 1; i <= rowCount; i++)
        {
            uint rva = GetMethodDefRva(ref tables, ref sizes, i);
            ushort implFlags = GetMethodDefImplFlags(ref tables, ref sizes, i);
            ushort flags = GetMethodDefFlags(ref tables, ref sizes, i);
            uint nameIdx = GetMethodDefName(ref tables, ref sizes, i);

            DebugConsole.Write("[Meta]   ");
            PrintString(ref root, nameIdx);
            DebugConsole.Write(" (RVA=0x");
            DebugConsole.WriteHex(rva);
            DebugConsole.Write(", flags=0x");
            DebugConsole.WriteHex(flags);
            DebugConsole.WriteLine(")");
        }
    }
}
