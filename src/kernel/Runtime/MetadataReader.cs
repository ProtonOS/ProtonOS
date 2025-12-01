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

        // FieldMarshal (0x0D): Parent (HasFieldMarshal coded) + NativeType (blob)
        int hasFieldMarshalSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasFieldMarshal, ref header);
        sizes.RowSizes[(int)MetadataTableId.FieldMarshal] =
            hasFieldMarshalSize + sizes.BlobIndexSize;

        // DeclSecurity (0x0E): Action (2) + Parent (HasDeclSecurity coded) + PermissionSet (blob)
        int hasDeclSecuritySize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasDeclSecurity, ref header);
        sizes.RowSizes[(int)MetadataTableId.DeclSecurity] =
            2 + hasDeclSecuritySize + sizes.BlobIndexSize;

        // ClassLayout (0x0F): PackingSize (2) + ClassSize (4) + Parent (TypeDef index)
        sizes.RowSizes[(int)MetadataTableId.ClassLayout] =
            2 + 4 + sizes.TypeDefIndexSize;

        // FieldLayout (0x10): Offset (4) + Field (Field index)
        sizes.RowSizes[(int)MetadataTableId.FieldLayout] =
            4 + sizes.FieldIndexSize;

        // StandAloneSig (0x11): Signature (blob)
        sizes.RowSizes[(int)MetadataTableId.StandAloneSig] =
            sizes.BlobIndexSize;

        // EventMap (0x12): Parent (TypeDef index) + EventList (Event index)
        int eventIndexSize = header.RowCounts[(int)MetadataTableId.Event] > 0xFFFF ? 4 : 2;
        sizes.RowSizes[(int)MetadataTableId.EventMap] =
            sizes.TypeDefIndexSize + eventIndexSize;

        // EventPtr (0x13): Event (Event index) - rarely used
        sizes.RowSizes[(int)MetadataTableId.EventPtr] = eventIndexSize;

        // Event (0x14): EventFlags (2) + Name (str) + EventType (TypeDefOrRef coded)
        sizes.RowSizes[(int)MetadataTableId.Event] =
            2 + sizes.StringIndexSize + sizes.TypeDefOrRefSize;

        // PropertyMap (0x15): Parent (TypeDef index) + PropertyList (Property index)
        int propertyIndexSize = header.RowCounts[(int)MetadataTableId.Property] > 0xFFFF ? 4 : 2;
        sizes.RowSizes[(int)MetadataTableId.PropertyMap] =
            sizes.TypeDefIndexSize + propertyIndexSize;

        // PropertyPtr (0x16): Property (Property index) - rarely used
        sizes.RowSizes[(int)MetadataTableId.PropertyPtr] = propertyIndexSize;

        // Property (0x17): Flags (2) + Name (str) + Type (blob)
        sizes.RowSizes[(int)MetadataTableId.Property] =
            2 + sizes.StringIndexSize + sizes.BlobIndexSize;

        // MethodSemantics (0x18): Semantics (2) + Method (MethodDef index) + Association (HasSemantics coded)
        int hasSemanticsSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasSemantics, ref header);
        sizes.RowSizes[(int)MetadataTableId.MethodSemantics] =
            2 + sizes.MethodIndexSize + hasSemanticsSize;

        // MethodImpl (0x19): Class (TypeDef index) + MethodBody (MethodDefOrRef) + MethodDeclaration (MethodDefOrRef)
        int methodDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MethodDefOrRef, ref header);
        sizes.RowSizes[(int)MetadataTableId.MethodImpl] =
            sizes.TypeDefIndexSize + methodDefOrRefSize + methodDefOrRefSize;

        // ModuleRef (0x1A): Name (str)
        sizes.RowSizes[(int)MetadataTableId.ModuleRef] =
            sizes.StringIndexSize;

        // TypeSpec (0x1B): Signature (blob)
        sizes.RowSizes[(int)MetadataTableId.TypeSpec] =
            sizes.BlobIndexSize;

        // ImplMap (0x1C): MappingFlags (2) + MemberForwarded (MemberForwarded coded) + ImportName (str) + ImportScope (ModuleRef index)
        int memberForwardedSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MemberForwarded, ref header);
        int moduleRefIndexSize = header.RowCounts[(int)MetadataTableId.ModuleRef] > 0xFFFF ? 4 : 2;
        sizes.RowSizes[(int)MetadataTableId.ImplMap] =
            2 + memberForwardedSize + sizes.StringIndexSize + moduleRefIndexSize;

        // FieldRVA (0x1D): RVA (4) + Field (Field index)
        sizes.RowSizes[(int)MetadataTableId.FieldRVA] =
            4 + sizes.FieldIndexSize;

        // EncLog (0x1E): Token (4) + FuncCode (4) - Edit and Continue, rarely used
        sizes.RowSizes[(int)MetadataTableId.EncLog] = 4 + 4;

        // EncMap (0x1F): Token (4) - Edit and Continue, rarely used
        sizes.RowSizes[(int)MetadataTableId.EncMap] = 4;

        // Assembly (0x20): HashAlgId (4) + MajorVersion (2) + MinorVersion (2) + BuildNumber (2) + RevisionNumber (2) +
        //                  Flags (4) + PublicKey (blob) + Name (str) + Culture (str)
        sizes.RowSizes[(int)MetadataTableId.Assembly] =
            4 + 2 + 2 + 2 + 2 + 4 + sizes.BlobIndexSize + sizes.StringIndexSize * 2;

        // AssemblyProcessor (0x21): Processor (4) - obsolete
        sizes.RowSizes[(int)MetadataTableId.AssemblyProcessor] = 4;

        // AssemblyOS (0x22): OSPlatformId (4) + OSMajorVersion (4) + OSMinorVersion (4) - obsolete
        sizes.RowSizes[(int)MetadataTableId.AssemblyOS] = 4 + 4 + 4;

        // AssemblyRef (0x23): MajorVersion (2) + MinorVersion (2) + BuildNumber (2) + RevisionNumber (2) +
        //                     Flags (4) + PublicKeyOrToken (blob) + Name (str) + Culture (str) + HashValue (blob)
        sizes.RowSizes[(int)MetadataTableId.AssemblyRef] =
            2 + 2 + 2 + 2 + 4 + sizes.BlobIndexSize + sizes.StringIndexSize * 2 + sizes.BlobIndexSize;

        // AssemblyRefProcessor (0x24): Processor (4) + AssemblyRef (AssemblyRef index) - obsolete
        int assemblyRefIndexSize = header.RowCounts[(int)MetadataTableId.AssemblyRef] > 0xFFFF ? 4 : 2;
        sizes.RowSizes[(int)MetadataTableId.AssemblyRefProcessor] = 4 + assemblyRefIndexSize;

        // AssemblyRefOS (0x25): OSPlatformId (4) + OSMajorVersion (4) + OSMinorVersion (4) + AssemblyRef (AssemblyRef index) - obsolete
        sizes.RowSizes[(int)MetadataTableId.AssemblyRefOS] = 4 + 4 + 4 + assemblyRefIndexSize;

        // File (0x26): Flags (4) + Name (str) + HashValue (blob)
        sizes.RowSizes[(int)MetadataTableId.File] =
            4 + sizes.StringIndexSize + sizes.BlobIndexSize;

        // ExportedType (0x27): Flags (4) + TypeDefId (4) + TypeName (str) + TypeNamespace (str) + Implementation (Implementation coded)
        int implementationSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.Implementation, ref header);
        sizes.RowSizes[(int)MetadataTableId.ExportedType] =
            4 + 4 + sizes.StringIndexSize * 2 + implementationSize;

        // ManifestResource (0x28): Offset (4) + Flags (4) + Name (str) + Implementation (Implementation coded)
        sizes.RowSizes[(int)MetadataTableId.ManifestResource] =
            4 + 4 + sizes.StringIndexSize + implementationSize;

        // NestedClass (0x29): NestedClass (TypeDef index) + EnclosingClass (TypeDef index)
        sizes.RowSizes[(int)MetadataTableId.NestedClass] =
            sizes.TypeDefIndexSize + sizes.TypeDefIndexSize;

        // GenericParam (0x2A): Number (2) + Flags (2) + Owner (TypeOrMethodDef coded) + Name (str)
        int typeOrMethodDefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.TypeOrMethodDef, ref header);
        sizes.RowSizes[(int)MetadataTableId.GenericParam] =
            2 + 2 + typeOrMethodDefSize + sizes.StringIndexSize;

        // MethodSpec (0x2B): Method (MethodDefOrRef coded) + Instantiation (blob)
        sizes.RowSizes[(int)MetadataTableId.MethodSpec] =
            methodDefOrRefSize + sizes.BlobIndexSize;

        // GenericParamConstraint (0x2C): Owner (GenericParam index) + Constraint (TypeDefOrRef coded)
        int genericParamIndexSize = header.RowCounts[(int)MetadataTableId.GenericParam] > 0xFFFF ? 4 : 2;
        sizes.RowSizes[(int)MetadataTableId.GenericParamConstraint] =
            genericParamIndexSize + sizes.TypeDefOrRefSize;

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

    // ============================================================================
    // MemberRef Table (0x0A) Accessors
    // ============================================================================

    /// <summary>
    /// Get the Class (MemberRefParent coded index) for a MemberRef row
    /// </summary>
    public static CodedIndex GetMemberRefClass(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MemberRef, rowId);
        if (row == null)
            return default;

        uint value = ReadIndex(row, sizes.MemberRefParentSize);
        return CodedIndexHelper.Decode(CodedIndexType.MemberRefParent, value);
    }

    /// <summary>
    /// Get the Name string index for a MemberRef row
    /// </summary>
    public static uint GetMemberRefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MemberRef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.MemberRefParentSize, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Signature blob index for a MemberRef row
    /// </summary>
    public static uint GetMemberRefSignature(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MemberRef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.MemberRefParentSize + sizes.StringIndexSize, sizes.BlobIndexSize);
    }

    /// <summary>
    /// Dump MemberRef table with full details
    /// </summary>
    public static void DumpMemberRefTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        uint rowCount = tables.RowCounts[(int)MetadataTableId.MemberRef];
        if (rowCount == 0)
        {
            DebugConsole.WriteLine("[Meta] No MemberRef table");
            return;
        }

        var sizes = TableSizes.Calculate(ref tables);

        DebugConsole.Write("[Meta] MemberRef table (");
        DebugConsole.WriteDecimal(rowCount);
        DebugConsole.WriteLine(" rows):");

        for (uint i = 1; i <= rowCount; i++)
        {
            var classRef = GetMemberRefClass(ref tables, ref sizes, i);
            uint nameIdx = GetMemberRefName(ref tables, ref sizes, i);

            DebugConsole.Write("[Meta]   ");
            PrintString(ref root, nameIdx);
            DebugConsole.Write(" -> ");
            PrintTableName((int)classRef.Table);
            DebugConsole.Write("[");
            DebugConsole.WriteDecimal(classRef.RowId);
            DebugConsole.WriteLine("]");
        }
    }

    // ============================================================================
    // Field Table (0x04) Accessors
    // ============================================================================

    /// <summary>
    /// Get the Flags for a Field row
    /// </summary>
    public static ushort GetFieldFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Field, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Name string index for a Field row
    /// </summary>
    public static uint GetFieldName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Field, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Signature blob index for a Field row
    /// </summary>
    public static uint GetFieldSignature(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Field, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2 + sizes.StringIndexSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // Param Table (0x08) Accessors
    // ============================================================================

    /// <summary>
    /// Get the Flags for a Param row
    /// </summary>
    public static ushort GetParamFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Param, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Sequence number for a Param row
    /// </summary>
    public static ushort GetParamSequence(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Param, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 2);
    }

    /// <summary>
    /// Get the Name string index for a Param row
    /// </summary>
    public static uint GetParamName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Param, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4, sizes.StringIndexSize);
    }

    // ============================================================================
    // AssemblyRef Table (0x23) Accessors
    // ============================================================================

    /// <summary>
    /// Get the major version for an AssemblyRef row
    /// </summary>
    public static ushort GetAssemblyRefMajorVersion(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the minor version for an AssemblyRef row
    /// </summary>
    public static ushort GetAssemblyRefMinorVersion(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 2);
    }

    /// <summary>
    /// Get the build number for an AssemblyRef row
    /// </summary>
    public static ushort GetAssemblyRefBuildNumber(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 4);
    }

    /// <summary>
    /// Get the revision number for an AssemblyRef row
    /// </summary>
    public static ushort GetAssemblyRefRevisionNumber(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 6);
    }

    /// <summary>
    /// Get the flags for an AssemblyRef row
    /// </summary>
    public static uint GetAssemblyRefFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        return *(uint*)(row + 8);
    }

    /// <summary>
    /// Get the Name string index for an AssemblyRef row
    /// </summary>
    public static uint GetAssemblyRefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.AssemblyRef, rowId);
        if (row == null)
            return 0;

        // Layout: MajorVersion(2) + MinorVersion(2) + BuildNumber(2) + RevisionNumber(2) + Flags(4) + PublicKeyOrToken(blob) + Name(str)
        int offset = 12 + sizes.BlobIndexSize;
        return ReadIndex(row + offset, sizes.StringIndexSize);
    }

    /// <summary>
    /// Dump AssemblyRef table with full details
    /// </summary>
    public static void DumpAssemblyRefTable(ref MetadataRoot root, ref TablesHeader tables)
    {
        uint rowCount = tables.RowCounts[(int)MetadataTableId.AssemblyRef];
        if (rowCount == 0)
        {
            DebugConsole.WriteLine("[Meta] No AssemblyRef table");
            return;
        }

        var sizes = TableSizes.Calculate(ref tables);

        DebugConsole.Write("[Meta] AssemblyRef table (");
        DebugConsole.WriteDecimal(rowCount);
        DebugConsole.WriteLine(" rows):");

        for (uint i = 1; i <= rowCount; i++)
        {
            ushort major = GetAssemblyRefMajorVersion(ref tables, ref sizes, i);
            ushort minor = GetAssemblyRefMinorVersion(ref tables, ref sizes, i);
            ushort build = GetAssemblyRefBuildNumber(ref tables, ref sizes, i);
            ushort rev = GetAssemblyRefRevisionNumber(ref tables, ref sizes, i);
            uint nameIdx = GetAssemblyRefName(ref tables, ref sizes, i);

            DebugConsole.Write("[Meta]   ");
            PrintString(ref root, nameIdx);
            DebugConsole.Write(" v");
            DebugConsole.WriteDecimal(major);
            DebugConsole.Write(".");
            DebugConsole.WriteDecimal(minor);
            DebugConsole.Write(".");
            DebugConsole.WriteDecimal(build);
            DebugConsole.Write(".");
            DebugConsole.WriteDecimal(rev);
            DebugConsole.WriteLine();
        }
    }

    // ============================================================================
    // InterfaceImpl Table (0x09)
    // Layout: Class (TypeDef index) + Interface (TypeDefOrRef coded)
    // ============================================================================

    /// <summary>
    /// Get the Class (TypeDef row ID) for an InterfaceImpl row
    /// </summary>
    public static uint GetInterfaceImplClass(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.InterfaceImpl, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.TypeDefIndexSize);
    }

    /// <summary>
    /// Get the Interface (TypeDefOrRef coded index) for an InterfaceImpl row
    /// </summary>
    public static uint GetInterfaceImplInterface(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.InterfaceImpl, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.TypeDefIndexSize, sizes.TypeDefOrRefSize);
    }

    // ============================================================================
    // Constant Table (0x0B)
    // Layout: Type (2) + Parent (HasConstant coded) + Value (blob)
    // ============================================================================

    /// <summary>
    /// Get the element type for a Constant row
    /// </summary>
    public static byte GetConstantType(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Constant, rowId);
        if (row == null)
            return 0;

        return *row; // Type is first byte (second byte is padding)
    }

    /// <summary>
    /// Get the Parent (HasConstant coded index) for a Constant row
    /// </summary>
    public static uint GetConstantParent(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Constant, rowId);
        if (row == null)
            return 0;

        int hasConstantSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasConstant, ref header);
        return ReadIndex(row + 2, hasConstantSize);
    }

    /// <summary>
    /// Get the Value blob index for a Constant row
    /// </summary>
    public static uint GetConstantValue(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Constant, rowId);
        if (row == null)
            return 0;

        int hasConstantSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasConstant, ref header);
        return ReadIndex(row + 2 + hasConstantSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // CustomAttribute Table (0x0C)
    // Layout: Parent (HasCustomAttribute coded) + Type (CustomAttributeType coded) + Value (blob)
    // ============================================================================

    /// <summary>
    /// Get the Parent (HasCustomAttribute coded index) for a CustomAttribute row
    /// </summary>
    public static uint GetCustomAttributeParent(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.CustomAttribute, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.HasCustomAttributeSize);
    }

    /// <summary>
    /// Get the Type (CustomAttributeType coded index) for a CustomAttribute row
    /// </summary>
    public static uint GetCustomAttributeType(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.CustomAttribute, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.HasCustomAttributeSize, sizes.CustomAttributeTypeSize);
    }

    /// <summary>
    /// Get the Value blob index for a CustomAttribute row
    /// </summary>
    public static uint GetCustomAttributeValue(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.CustomAttribute, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.HasCustomAttributeSize + sizes.CustomAttributeTypeSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // StandAloneSig Table (0x11)
    // Layout: Signature (blob)
    // ============================================================================

    /// <summary>
    /// Get the Signature blob index for a StandAloneSig row
    /// </summary>
    public static uint GetStandAloneSigSignature(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.StandAloneSig, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.BlobIndexSize);
    }

    // ============================================================================
    // TypeSpec Table (0x1B)
    // Layout: Signature (blob)
    // ============================================================================

    /// <summary>
    /// Get the Signature blob index for a TypeSpec row
    /// </summary>
    public static uint GetTypeSpecSignature(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.TypeSpec, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.BlobIndexSize);
    }

    // ============================================================================
    // GenericParam Table (0x2A)
    // Layout: Number (2) + Flags (2) + Owner (TypeOrMethodDef coded) + Name (str)
    // ============================================================================

    /// <summary>
    /// Get the generic parameter number for a GenericParam row
    /// </summary>
    public static ushort GetGenericParamNumber(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParam, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Flags for a GenericParam row
    /// </summary>
    public static ushort GetGenericParamFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParam, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 2);
    }

    /// <summary>
    /// Get the Owner (TypeOrMethodDef coded index) for a GenericParam row
    /// </summary>
    public static uint GetGenericParamOwner(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParam, rowId);
        if (row == null)
            return 0;

        int typeOrMethodDefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.TypeOrMethodDef, ref header);
        return ReadIndex(row + 4, typeOrMethodDefSize);
    }

    /// <summary>
    /// Get the Name string index for a GenericParam row
    /// </summary>
    public static uint GetGenericParamName(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParam, rowId);
        if (row == null)
            return 0;

        int typeOrMethodDefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.TypeOrMethodDef, ref header);
        return ReadIndex(row + 4 + typeOrMethodDefSize, sizes.StringIndexSize);
    }

    // ============================================================================
    // MethodSpec Table (0x2B)
    // Layout: Method (MethodDefOrRef coded) + Instantiation (blob)
    // ============================================================================

    /// <summary>
    /// Get the Method (MethodDefOrRef coded index) for a MethodSpec row
    /// </summary>
    public static uint GetMethodSpecMethod(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodSpec, rowId);
        if (row == null)
            return 0;

        int methodDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MethodDefOrRef, ref header);
        return ReadIndex(row, methodDefOrRefSize);
    }

    /// <summary>
    /// Get the Instantiation blob index for a MethodSpec row
    /// </summary>
    public static uint GetMethodSpecInstantiation(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodSpec, rowId);
        if (row == null)
            return 0;

        int methodDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MethodDefOrRef, ref header);
        return ReadIndex(row + methodDefOrRefSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // GenericParamConstraint Table (0x2C)
    // Layout: Owner (GenericParam index) + Constraint (TypeDefOrRef coded)
    // ============================================================================

    /// <summary>
    /// Get the Owner (GenericParam row ID) for a GenericParamConstraint row
    /// </summary>
    public static uint GetGenericParamConstraintOwner(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParamConstraint, rowId);
        if (row == null)
            return 0;

        int genericParamIndexSize = header.RowCounts[(int)MetadataTableId.GenericParam] > 0xFFFF ? 4 : 2;
        return ReadIndex(row, genericParamIndexSize);
    }

    /// <summary>
    /// Get the Constraint (TypeDefOrRef coded index) for a GenericParamConstraint row
    /// </summary>
    public static uint GetGenericParamConstraintConstraint(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.GenericParamConstraint, rowId);
        if (row == null)
            return 0;

        int genericParamIndexSize = header.RowCounts[(int)MetadataTableId.GenericParam] > 0xFFFF ? 4 : 2;
        return ReadIndex(row + genericParamIndexSize, sizes.TypeDefOrRefSize);
    }

    // ============================================================================
    // NestedClass Table (0x29)
    // Layout: NestedClass (TypeDef index) + EnclosingClass (TypeDef index)
    // ============================================================================

    /// <summary>
    /// Get the NestedClass TypeDef row ID for a NestedClass row
    /// </summary>
    public static uint GetNestedClassNestedClass(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.NestedClass, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.TypeDefIndexSize);
    }

    /// <summary>
    /// Get the EnclosingClass TypeDef row ID for a NestedClass row
    /// </summary>
    public static uint GetNestedClassEnclosingClass(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.NestedClass, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + sizes.TypeDefIndexSize, sizes.TypeDefIndexSize);
    }

    // ============================================================================
    // Property Table (0x17)
    // Layout: Flags (2) + Name (str) + Type (blob)
    // ============================================================================

    /// <summary>
    /// Get the Flags for a Property row
    /// </summary>
    public static ushort GetPropertyFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Property, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Name string index for a Property row
    /// </summary>
    public static uint GetPropertyName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Property, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Type blob index for a Property row
    /// </summary>
    public static uint GetPropertyType(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Property, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2 + sizes.StringIndexSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // Event Table (0x14)
    // Layout: EventFlags (2) + Name (str) + EventType (TypeDefOrRef coded)
    // ============================================================================

    /// <summary>
    /// Get the EventFlags for an Event row
    /// </summary>
    public static ushort GetEventFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Event, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Name string index for an Event row
    /// </summary>
    public static uint GetEventName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Event, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the EventType (TypeDefOrRef coded index) for an Event row
    /// </summary>
    public static uint GetEventType(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Event, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2 + sizes.StringIndexSize, sizes.TypeDefOrRefSize);
    }

    // ============================================================================
    // MethodSemantics Table (0x18)
    // Layout: Semantics (2) + Method (MethodDef index) + Association (HasSemantics coded)
    // ============================================================================

    /// <summary>
    /// Get the Semantics flags for a MethodSemantics row
    /// </summary>
    public static ushort GetMethodSemanticsSemantics(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodSemantics, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the Method (MethodDef row ID) for a MethodSemantics row
    /// </summary>
    public static uint GetMethodSemanticsMethod(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodSemantics, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 2, sizes.MethodIndexSize);
    }

    /// <summary>
    /// Get the Association (HasSemantics coded index) for a MethodSemantics row
    /// </summary>
    public static uint GetMethodSemanticsAssociation(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodSemantics, rowId);
        if (row == null)
            return 0;

        int hasSemanticsSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.HasSemantics, ref header);
        return ReadIndex(row + 2 + sizes.MethodIndexSize, hasSemanticsSize);
    }

    // ============================================================================
    // ImplMap Table (0x1C)
    // Layout: MappingFlags (2) + MemberForwarded (MemberForwarded coded) + ImportName (str) + ImportScope (ModuleRef index)
    // ============================================================================

    /// <summary>
    /// Get the MappingFlags for an ImplMap row
    /// </summary>
    public static ushort GetImplMapMappingFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ImplMap, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the MemberForwarded (MemberForwarded coded index) for an ImplMap row
    /// </summary>
    public static uint GetImplMapMemberForwarded(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ImplMap, rowId);
        if (row == null)
            return 0;

        int memberForwardedSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MemberForwarded, ref header);
        return ReadIndex(row + 2, memberForwardedSize);
    }

    /// <summary>
    /// Get the ImportName string index for an ImplMap row
    /// </summary>
    public static uint GetImplMapImportName(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ImplMap, rowId);
        if (row == null)
            return 0;

        int memberForwardedSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MemberForwarded, ref header);
        return ReadIndex(row + 2 + memberForwardedSize, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the ImportScope (ModuleRef row ID) for an ImplMap row
    /// </summary>
    public static uint GetImplMapImportScope(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ImplMap, rowId);
        if (row == null)
            return 0;

        int memberForwardedSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MemberForwarded, ref header);
        int moduleRefIndexSize = header.RowCounts[(int)MetadataTableId.ModuleRef] > 0xFFFF ? 4 : 2;
        return ReadIndex(row + 2 + memberForwardedSize + sizes.StringIndexSize, moduleRefIndexSize);
    }

    // ============================================================================
    // ModuleRef Table (0x1A)
    // Layout: Name (str)
    // ============================================================================

    /// <summary>
    /// Get the Name string index for a ModuleRef row
    /// </summary>
    public static uint GetModuleRefName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ModuleRef, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.StringIndexSize);
    }

    // ============================================================================
    // ClassLayout Table (0x0F)
    // Layout: PackingSize (2) + ClassSize (4) + Parent (TypeDef index)
    // ============================================================================

    /// <summary>
    /// Get the PackingSize for a ClassLayout row
    /// </summary>
    public static ushort GetClassLayoutPackingSize(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ClassLayout, rowId);
        if (row == null)
            return 0;

        return *(ushort*)row;
    }

    /// <summary>
    /// Get the ClassSize for a ClassLayout row
    /// </summary>
    public static uint GetClassLayoutClassSize(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ClassLayout, rowId);
        if (row == null)
            return 0;

        return *(uint*)(row + 2);
    }

    /// <summary>
    /// Get the Parent (TypeDef row ID) for a ClassLayout row
    /// </summary>
    public static uint GetClassLayoutParent(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ClassLayout, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 6, sizes.TypeDefIndexSize);
    }

    // ============================================================================
    // FieldLayout Table (0x10)
    // Layout: Offset (4) + Field (Field index)
    // ============================================================================

    /// <summary>
    /// Get the Offset for a FieldLayout row
    /// </summary>
    public static uint GetFieldLayoutOffset(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.FieldLayout, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the Field (Field row ID) for a FieldLayout row
    /// </summary>
    public static uint GetFieldLayoutField(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.FieldLayout, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4, sizes.FieldIndexSize);
    }

    // ============================================================================
    // Assembly Table (0x20)
    // Layout: HashAlgId (4) + MajorVersion (2) + MinorVersion (2) + BuildNumber (2) + RevisionNumber (2) +
    //         Flags (4) + PublicKey (blob) + Name (str) + Culture (str)
    // ============================================================================

    /// <summary>
    /// Get the HashAlgId for an Assembly row
    /// </summary>
    public static uint GetAssemblyHashAlgId(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the MajorVersion for an Assembly row
    /// </summary>
    public static ushort GetAssemblyMajorVersion(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 4);
    }

    /// <summary>
    /// Get the MinorVersion for an Assembly row
    /// </summary>
    public static ushort GetAssemblyMinorVersion(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 6);
    }

    /// <summary>
    /// Get the BuildNumber for an Assembly row
    /// </summary>
    public static ushort GetAssemblyBuildNumber(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 8);
    }

    /// <summary>
    /// Get the RevisionNumber for an Assembly row
    /// </summary>
    public static ushort GetAssemblyRevisionNumber(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(ushort*)(row + 10);
    }

    /// <summary>
    /// Get the Flags for an Assembly row
    /// </summary>
    public static uint GetAssemblyFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        return *(uint*)(row + 12);
    }

    /// <summary>
    /// Get the Name string index for an Assembly row
    /// </summary>
    public static uint GetAssemblyName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        // Layout: HashAlgId(4) + MajorVersion(2) + MinorVersion(2) + BuildNumber(2) + RevisionNumber(2) +
        //         Flags(4) + PublicKey(blob) + Name(str) + Culture(str)
        int offset = 16 + sizes.BlobIndexSize;
        return ReadIndex(row + offset, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Culture string index for an Assembly row
    /// </summary>
    public static uint GetAssemblyCulture(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.Assembly, rowId);
        if (row == null)
            return 0;

        // Layout: HashAlgId(4) + MajorVersion(2) + MinorVersion(2) + BuildNumber(2) + RevisionNumber(2) +
        //         Flags(4) + PublicKey(blob) + Name(str) + Culture(str)
        int offset = 16 + sizes.BlobIndexSize + sizes.StringIndexSize;
        return ReadIndex(row + offset, sizes.StringIndexSize);
    }

    // ============================================================================
    // EventMap Table (0x12)
    // Layout: Parent (TypeDef index) + EventList (Event index)
    // ============================================================================

    /// <summary>
    /// Get the Parent (TypeDef row ID) for an EventMap row
    /// </summary>
    public static uint GetEventMapParent(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.EventMap, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.TypeDefIndexSize);
    }

    /// <summary>
    /// Get the EventList (first Event row ID) for an EventMap row
    /// </summary>
    public static uint GetEventMapEventList(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.EventMap, rowId);
        if (row == null)
            return 0;

        int eventIndexSize = header.RowCounts[(int)MetadataTableId.Event] > 0xFFFF ? 4 : 2;
        return ReadIndex(row + sizes.TypeDefIndexSize, eventIndexSize);
    }

    // ============================================================================
    // PropertyMap Table (0x15)
    // Layout: Parent (TypeDef index) + PropertyList (Property index)
    // ============================================================================

    /// <summary>
    /// Get the Parent (TypeDef row ID) for a PropertyMap row
    /// </summary>
    public static uint GetPropertyMapParent(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.PropertyMap, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.TypeDefIndexSize);
    }

    /// <summary>
    /// Get the PropertyList (first Property row ID) for a PropertyMap row
    /// </summary>
    public static uint GetPropertyMapPropertyList(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.PropertyMap, rowId);
        if (row == null)
            return 0;

        int propertyIndexSize = header.RowCounts[(int)MetadataTableId.Property] > 0xFFFF ? 4 : 2;
        return ReadIndex(row + sizes.TypeDefIndexSize, propertyIndexSize);
    }

    // ============================================================================
    // MethodImpl Table (0x19)
    // Layout: Class (TypeDef index) + MethodBody (MethodDefOrRef coded) + MethodDeclaration (MethodDefOrRef coded)
    // ============================================================================

    /// <summary>
    /// Get the Class (TypeDef row ID) for a MethodImpl row
    /// </summary>
    public static uint GetMethodImplClass(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodImpl, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row, sizes.TypeDefIndexSize);
    }

    /// <summary>
    /// Get the MethodBody (MethodDefOrRef coded index) for a MethodImpl row
    /// </summary>
    public static uint GetMethodImplMethodBody(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodImpl, rowId);
        if (row == null)
            return 0;

        int methodDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MethodDefOrRef, ref header);
        return ReadIndex(row + sizes.TypeDefIndexSize, methodDefOrRefSize);
    }

    /// <summary>
    /// Get the MethodDeclaration (MethodDefOrRef coded index) for a MethodImpl row
    /// </summary>
    public static uint GetMethodImplMethodDeclaration(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.MethodImpl, rowId);
        if (row == null)
            return 0;

        int methodDefOrRefSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.MethodDefOrRef, ref header);
        return ReadIndex(row + sizes.TypeDefIndexSize + methodDefOrRefSize, methodDefOrRefSize);
    }

    // ============================================================================
    // FieldRVA Table (0x1D)
    // Layout: RVA (4) + Field (Field index)
    // ============================================================================

    /// <summary>
    /// Get the RVA for a FieldRVA row
    /// </summary>
    public static uint GetFieldRvaRva(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.FieldRVA, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the Field (Field row ID) for a FieldRVA row
    /// </summary>
    public static uint GetFieldRvaField(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.FieldRVA, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4, sizes.FieldIndexSize);
    }

    // ============================================================================
    // File Table (0x26)
    // Layout: Flags (4) + Name (str) + HashValue (blob)
    // ============================================================================

    /// <summary>
    /// Get the Flags for a File row
    /// </summary>
    public static uint GetFileFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.File, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the Name string index for a File row
    /// </summary>
    public static uint GetFileName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.File, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the HashValue blob index for a File row
    /// </summary>
    public static uint GetFileHashValue(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.File, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 4 + sizes.StringIndexSize, sizes.BlobIndexSize);
    }

    // ============================================================================
    // ExportedType Table (0x27)
    // Layout: Flags (4) + TypeDefId (4) + TypeName (str) + TypeNamespace (str) + Implementation (Implementation coded)
    // ============================================================================

    /// <summary>
    /// Get the Flags for an ExportedType row
    /// </summary>
    public static uint GetExportedTypeFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ExportedType, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the TypeDefId for an ExportedType row
    /// </summary>
    public static uint GetExportedTypeTypeDefId(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ExportedType, rowId);
        if (row == null)
            return 0;

        return *(uint*)(row + 4);
    }

    /// <summary>
    /// Get the TypeName string index for an ExportedType row
    /// </summary>
    public static uint GetExportedTypeName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ExportedType, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the TypeNamespace string index for an ExportedType row
    /// </summary>
    public static uint GetExportedTypeNamespace(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ExportedType, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8 + sizes.StringIndexSize, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Implementation (Implementation coded index) for an ExportedType row
    /// </summary>
    public static uint GetExportedTypeImplementation(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ExportedType, rowId);
        if (row == null)
            return 0;

        int implementationSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.Implementation, ref header);
        return ReadIndex(row + 8 + sizes.StringIndexSize * 2, implementationSize);
    }

    // ============================================================================
    // ManifestResource Table (0x28)
    // Layout: Offset (4) + Flags (4) + Name (str) + Implementation (Implementation coded)
    // ============================================================================

    /// <summary>
    /// Get the Offset for a ManifestResource row
    /// </summary>
    public static uint GetManifestResourceOffset(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ManifestResource, rowId);
        if (row == null)
            return 0;

        return *(uint*)row;
    }

    /// <summary>
    /// Get the Flags for a ManifestResource row
    /// </summary>
    public static uint GetManifestResourceFlags(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ManifestResource, rowId);
        if (row == null)
            return 0;

        return *(uint*)(row + 4);
    }

    /// <summary>
    /// Get the Name string index for a ManifestResource row
    /// </summary>
    public static uint GetManifestResourceName(ref TablesHeader tables, ref TableSizes sizes, uint rowId)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ManifestResource, rowId);
        if (row == null)
            return 0;

        return ReadIndex(row + 8, sizes.StringIndexSize);
    }

    /// <summary>
    /// Get the Implementation (Implementation coded index) for a ManifestResource row
    /// </summary>
    public static uint GetManifestResourceImplementation(ref TablesHeader tables, ref TableSizes sizes, uint rowId, ref TablesHeader header)
    {
        byte* row = GetTableRow(ref tables, ref sizes, MetadataTableId.ManifestResource, rowId);
        if (row == null)
            return 0;

        int implementationSize = CodedIndexHelper.GetCodedIndexSize(CodedIndexType.Implementation, ref header);
        return ReadIndex(row + 8 + sizes.StringIndexSize, implementationSize);
    }

    // ============================================================================
    // IL Method Body Reader (ECMA-335 II.25.4)
    // ============================================================================

    /// <summary>
    /// Read an IL method body from a pointer (typically resolved from MethodDef RVA)
    /// </summary>
    /// <param name="ptr">Pointer to the start of the method body</param>
    /// <param name="body">Output: parsed method body info</param>
    /// <returns>True if successfully parsed</returns>
    public static bool ReadMethodBody(byte* ptr, out MethodBody body)
    {
        body = default;
        if (ptr == null)
            return false;

        byte headerByte = *ptr;

        // Check format: low 2 bits
        if ((headerByte & MethodBodyConstants.FormatMask) == MethodBodyConstants.TinyFormat)
        {
            // Tiny format: single byte header
            // Bits 2-7 = code size (max 63 bytes)
            body.IsTiny = true;
            body.CodeSize = (uint)(headerByte >> MethodBodyConstants.TinyFormatSizeShift);
            body.MaxStack = 8;  // Tiny format default
            body.LocalVarSigToken = 0;
            body.InitLocals = false;
            body.HasMoreSections = false;
            body.ILCode = ptr + 1;  // IL starts right after 1-byte header
            body.HeaderSize = 1;
            return true;
        }

        if ((headerByte & MethodBodyConstants.FormatMask) != MethodBodyConstants.FatFormat)
        {
            // Invalid format
            return false;
        }

        // Fat format: 12 bytes header
        // Byte 0: flags (low 4 bits) + header size in dwords (high 4 bits)
        // Byte 1: more flags
        byte headerByte2 = ptr[1];
        int headerSizeDwords = headerByte2 >> MethodBodyConstants.FatFormatHeaderSizeShift;
        if (headerSizeDwords != MethodBodyConstants.FatFormatHeaderSizeDwords)
        {
            // Header size should be 3 dwords (12 bytes)
            return false;
        }

        body.IsTiny = false;
        body.InitLocals = (headerByte & MethodBodyConstants.InitLocals) != 0;
        body.HasMoreSections = (headerByte & MethodBodyConstants.MoreSects) != 0;

        // Read MaxStack (2 bytes at offset 2)
        body.MaxStack = *(ushort*)(ptr + 2);

        // Read CodeSize (4 bytes at offset 4)
        body.CodeSize = *(uint*)(ptr + 4);

        // Read LocalVarSig token (4 bytes at offset 8)
        body.LocalVarSigToken = *(uint*)(ptr + 8);

        body.HeaderSize = 12;
        body.ILCode = ptr + 12;

        return true;
    }

    /// <summary>
    /// Parse exception handling sections following a fat method body
    /// </summary>
    /// <param name="body">Method body (must have HasMoreSections = true)</param>
    /// <param name="exceptionClauses">Output array for clauses (must be pre-allocated)</param>
    /// <param name="maxClauses">Maximum clauses to read</param>
    /// <returns>Number of clauses read</returns>
    public static int ReadExceptionClauses(ref MethodBody body, ExceptionClause* exceptionClauses, int maxClauses)
    {
        if (!body.HasMoreSections || body.IsTiny)
            return 0;

        // Exception sections are 4-byte aligned after the IL code
        byte* ptr = body.ILCode + body.CodeSize;

        // Align to 4-byte boundary
        ulong addr = (ulong)ptr;
        ulong aligned = (addr + 3) & ~3UL;
        ptr = (byte*)aligned;

        // Read section header
        byte sectionHeader = *ptr++;

        if ((sectionHeader & MethodBodyConstants.SectEHTable) == 0)
        {
            // Not an EH section (could be other data)
            return 0;
        }

        bool isFat = (sectionHeader & MethodBodyConstants.SectFatFormat) != 0;
        int clauseCount;
        int clauseSize;

        if (isFat)
        {
            // Fat format: 3 more bytes for data size (24-bit)
            uint dataSize = (uint)(ptr[0] | (ptr[1] << 8) | (ptr[2] << 16));
            ptr += 3;
            clauseSize = 24;  // Fat clause is 24 bytes
            clauseCount = (int)(dataSize / clauseSize);
        }
        else
        {
            // Small format: 1 byte for data size, then 2 reserved bytes
            byte dataSize = *ptr++;
            ptr += 2;  // Skip reserved
            clauseSize = 12;  // Small clause is 12 bytes
            clauseCount = dataSize / clauseSize;
        }

        if (clauseCount > maxClauses)
            clauseCount = maxClauses;

        for (int i = 0; i < clauseCount; i++)
        {
            if (isFat)
            {
                // Fat clause (24 bytes)
                exceptionClauses[i].Kind = (ExceptionClauseKind)(*(uint*)ptr);
                ptr += 4;
                exceptionClauses[i].TryOffset = *(int*)ptr;
                ptr += 4;
                exceptionClauses[i].TryLength = *(int*)ptr;
                ptr += 4;
                exceptionClauses[i].HandlerOffset = *(int*)ptr;
                ptr += 4;
                exceptionClauses[i].HandlerLength = *(int*)ptr;
                ptr += 4;
                exceptionClauses[i].ClassTokenOrFilterOffset = *(int*)ptr;
                ptr += 4;
            }
            else
            {
                // Small clause (12 bytes)
                exceptionClauses[i].Kind = (ExceptionClauseKind)(*(ushort*)ptr);
                ptr += 2;
                exceptionClauses[i].TryOffset = *(ushort*)ptr;
                ptr += 2;
                exceptionClauses[i].TryLength = *ptr++;
                exceptionClauses[i].HandlerOffset = *(ushort*)ptr;
                ptr += 2;
                exceptionClauses[i].HandlerLength = *ptr++;
                exceptionClauses[i].ClassTokenOrFilterOffset = *(int*)ptr;
                ptr += 4;
            }
        }

        return clauseCount;
    }

    /// <summary>
    /// Dump method body info for debugging
    /// </summary>
    public static void DumpMethodBody(ref MethodBody body)
    {
        if (body.IsTiny)
        {
            DebugConsole.Write("[IL] Tiny format, ");
        }
        else
        {
            DebugConsole.Write("[IL] Fat format, ");
            if (body.InitLocals)
                DebugConsole.Write("initlocals, ");
            if (body.HasMoreSections)
                DebugConsole.Write("moresects, ");
        }

        DebugConsole.Write("maxstack=");
        DebugConsole.WriteDecimal((int)body.MaxStack);
        DebugConsole.Write(", codesize=");
        DebugConsole.WriteDecimal((int)body.CodeSize);

        if (body.LocalVarSigToken != 0)
        {
            DebugConsole.Write(", locals=0x");
            DebugConsole.WriteHex(body.LocalVarSigToken);
        }
        DebugConsole.WriteLine();

        // Dump first few bytes of IL
        DebugConsole.Write("[IL] Code: ");
        int bytesToShow = body.CodeSize > 16 ? 16 : (int)body.CodeSize;
        for (int i = 0; i < bytesToShow; i++)
        {
            DebugConsole.WriteHex(body.ILCode[i]);
            DebugConsole.Write(" ");
        }
        if (body.CodeSize > 16)
            DebugConsole.Write("...");
        DebugConsole.WriteLine();
    }
}

/// <summary>
/// IL method body constants (ECMA-335 II.25.4)
/// </summary>
public static class MethodBodyConstants
{
    // Format detection (bits 0-1)
    public const byte TinyFormat = 0x02;
    public const byte FatFormat = 0x03;
    public const byte FormatMask = 0x03;

    // Tiny format
    public const int TinyFormatSizeShift = 2;  // Code size in bits 2-7

    // Fat format flags (byte 0)
    public const byte MoreSects = 0x08;     // More sections follow
    public const byte InitLocals = 0x10;    // Initialize locals to zero

    // Fat format header size (byte 1, bits 4-7)
    public const int FatFormatHeaderSizeShift = 4;
    public const int FatFormatHeaderSizeDwords = 3;  // 12 bytes = 3 dwords

    // Section types
    public const byte SectEHTable = 0x01;    // Exception handling table
    public const byte SectOptILTable = 0x02; // Reserved
    public const byte SectFatFormat = 0x40;  // Fat format section
    public const byte SectMoreSects = 0x80;  // More sections follow
}

/// <summary>
/// Parsed IL method body
/// </summary>
public unsafe struct MethodBody
{
    public bool IsTiny;           // True if tiny format, false if fat
    public bool InitLocals;       // Initialize locals to zero (fat only)
    public bool HasMoreSections;  // More sections follow (fat only)
    public ushort MaxStack;       // Maximum stack depth
    public uint CodeSize;         // Size of IL code in bytes
    public uint LocalVarSigToken; // Metadata token for local variables signature (fat only)
    public byte* ILCode;          // Pointer to IL code bytes
    public int HeaderSize;        // Size of header in bytes (1 or 12)
}

/// <summary>
/// Exception handling clause kinds (ECMA-335 II.25.4.6)
/// </summary>
public enum ExceptionClauseKind : uint
{
    Catch = 0x0000,   // Catch clause with type filter
    Filter = 0x0001,  // Filter clause with user-supplied filter code
    Finally = 0x0002, // Finally clause (always executes)
    Fault = 0x0004,   // Fault clause (executes on exception only)
}

/// <summary>
/// Exception handling clause
/// </summary>
public struct ExceptionClause
{
    public ExceptionClauseKind Kind;
    public int TryOffset;          // IL offset of try block start
    public int TryLength;          // Length of try block in bytes
    public int HandlerOffset;      // IL offset of handler start
    public int HandlerLength;      // Length of handler in bytes
    public int ClassTokenOrFilterOffset;  // Type token (Catch) or filter IL offset (Filter)
}

// ============================================================================
// Signature Constants and Types (ECMA-335 II.23.2)
// ============================================================================

/// <summary>
/// Element type codes for signatures (ECMA-335 II.23.1.16)
/// </summary>
public static class ElementType
{
    public const byte End = 0x00;
    public const byte Void = 0x01;
    public const byte Boolean = 0x02;
    public const byte Char = 0x03;
    public const byte I1 = 0x04;      // sbyte
    public const byte U1 = 0x05;      // byte
    public const byte I2 = 0x06;      // short
    public const byte U2 = 0x07;      // ushort
    public const byte I4 = 0x08;      // int
    public const byte U4 = 0x09;      // uint
    public const byte I8 = 0x0A;      // long
    public const byte U8 = 0x0B;      // ulong
    public const byte R4 = 0x0C;      // float
    public const byte R8 = 0x0D;      // double
    public const byte String = 0x0E;
    public const byte Ptr = 0x0F;     // Pointer type
    public const byte ByRef = 0x10;   // Managed reference (ref/out)
    public const byte ValueType = 0x11;
    public const byte Class = 0x12;
    public const byte Var = 0x13;     // Generic type parameter
    public const byte Array = 0x14;   // Multi-dimensional array
    public const byte GenericInst = 0x15;  // Generic instantiation
    public const byte TypedByRef = 0x16;
    public const byte I = 0x18;       // IntPtr
    public const byte U = 0x19;       // UIntPtr
    public const byte FnPtr = 0x1B;   // Function pointer
    public const byte Object = 0x1C;
    public const byte SzArray = 0x1D; // Single-dimension array
    public const byte MVar = 0x1E;    // Generic method parameter
    public const byte CModReqd = 0x1F;
    public const byte CModOpt = 0x20;
    public const byte Internal = 0x21;
    public const byte Modifier = 0x40;
    public const byte Sentinel = 0x41;
    public const byte Pinned = 0x45;
}

/// <summary>
/// Signature header byte encoding (ECMA-335 II.23.2.1-3)
/// Low nibble (0x0F): calling convention or signature kind
/// High nibble (0xF0): attributes (generic, hasthis, explicitthis)
/// </summary>
public static class SignatureHeader
{
    // Calling conventions (low nibble for method signatures)
    public const byte Default = 0x00;
    public const byte CDecl = 0x01;
    public const byte StdCall = 0x02;
    public const byte ThisCall = 0x03;
    public const byte FastCall = 0x04;
    public const byte VarArg = 0x05;
    public const byte Unmanaged = 0x09;

    // Signature kinds (low nibble for non-method signatures)
    public const byte Field = 0x06;
    public const byte LocalSig = 0x07;
    public const byte Property = 0x08;
    public const byte GenericInst = 0x0A;

    // Masks
    public const byte CallingConventionMask = 0x0F;

    // Attributes (high nibble flags)
    public const byte Generic = 0x10;    // Generic method with explicit generic param count
    public const byte HasThis = 0x20;    // Instance method (has 'this' pointer)
    public const byte ExplicitThis = 0x40; // 'this' is explicitly passed as first param

    /// <summary>
    /// Check if signature header indicates a method signature
    /// </summary>
    public static bool IsMethod(byte header)
    {
        byte kind = (byte)(header & CallingConventionMask);
        return kind <= VarArg || kind == Unmanaged;
    }
}

/// <summary>
/// Parsed method signature from blob
/// </summary>
public unsafe struct MethodSignature
{
    public byte Header;            // Raw header byte
    public byte CallingConvention; // Calling convention (low nibble)
    public bool HasThis;           // Instance method
    public bool ExplicitThis;      // Explicit this parameter
    public bool IsGeneric;         // Has generic parameters
    public uint GenericParamCount; // Number of generic type parameters
    public uint ParamCount;        // Number of parameters (not including return)
    public TypeSig ReturnType;     // Return type
}

/// <summary>
/// Parsed type from signature
/// </summary>
public struct TypeSig
{
    public byte ElementType;       // ElementType constant
    public uint Token;             // TypeDef/TypeRef/TypeSpec token (for class/valuetype)
    public uint GenericParamIndex; // For Var/MVar
    public bool IsValid;           // True if successfully parsed
}

/// <summary>
/// Signature reader/decoder
/// </summary>
public static unsafe class SignatureReader
{
    /// <summary>
    /// Parse a method signature from blob data
    /// </summary>
    public static bool ReadMethodSignature(byte* blob, uint blobLength, out MethodSignature sig)
    {
        sig = default;

        if (blob == null || blobLength == 0)
            return false;

        byte* ptr = blob;
        byte* end = blob + blobLength;

        // Read header byte
        sig.Header = *ptr++;
        sig.CallingConvention = (byte)(sig.Header & SignatureHeader.CallingConventionMask);
        sig.HasThis = (sig.Header & SignatureHeader.HasThis) != 0;
        sig.ExplicitThis = (sig.Header & SignatureHeader.ExplicitThis) != 0;
        sig.IsGeneric = (sig.Header & SignatureHeader.Generic) != 0;

        // Verify it's a method signature
        if (!SignatureHeader.IsMethod(sig.Header))
            return false;

        // Generic param count (only if generic flag set)
        if (sig.IsGeneric)
        {
            sig.GenericParamCount = MetadataReader.ReadCompressedUInt(ref ptr);
        }

        // Parameter count
        if (ptr >= end) return false;
        sig.ParamCount = MetadataReader.ReadCompressedUInt(ref ptr);

        // Return type
        if (ptr >= end) return false;
        sig.ReturnType = ReadTypeSig(ref ptr, end);

        return sig.ReturnType.IsValid;
    }

    /// <summary>
    /// Parse a single type from signature data
    /// </summary>
    public static TypeSig ReadTypeSig(ref byte* ptr, byte* end)
    {
        TypeSig type = default;

        if (ptr >= end)
            return type;

        byte elemType = *ptr++;
        type.ElementType = elemType;

        switch (elemType)
        {
            // Primitive types - no additional data
            case ElementType.Void:
            case ElementType.Boolean:
            case ElementType.Char:
            case ElementType.I1:
            case ElementType.U1:
            case ElementType.I2:
            case ElementType.U2:
            case ElementType.I4:
            case ElementType.U4:
            case ElementType.I8:
            case ElementType.U8:
            case ElementType.R4:
            case ElementType.R8:
            case ElementType.String:
            case ElementType.I:
            case ElementType.U:
            case ElementType.Object:
            case ElementType.TypedByRef:
                type.IsValid = true;
                break;

            // Types with a TypeDefOrRef token
            case ElementType.Class:
            case ElementType.ValueType:
                if (ptr >= end) return type;
                type.Token = MetadataReader.ReadCompressedUInt(ref ptr);
                type.IsValid = true;
                break;

            // Generic type/method parameters
            case ElementType.Var:
            case ElementType.MVar:
                if (ptr >= end) return type;
                type.GenericParamIndex = MetadataReader.ReadCompressedUInt(ref ptr);
                type.IsValid = true;
                break;

            // Pointer and ByRef - recurse for element type
            case ElementType.Ptr:
            case ElementType.ByRef:
            case ElementType.SzArray:
            case ElementType.Pinned:
                // Skip the inner type for now (just mark as valid)
                // A full implementation would recursively parse
                SkipType(ref ptr, end);
                type.IsValid = true;
                break;

            // Generic instantiation: GENERICINST (CLASS|VALUETYPE) TypeDefOrRef GenArgCount Type*
            case ElementType.GenericInst:
                {
                    if (ptr >= end) return type;
                    byte classOrValue = *ptr++;  // CLASS or VALUETYPE
                    if (ptr >= end) return type;
                    type.Token = MetadataReader.ReadCompressedUInt(ref ptr);
                    if (ptr >= end) return type;
                    uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                    // Skip all type arguments
                    for (uint i = 0; i < argCount && ptr < end; i++)
                    {
                        SkipType(ref ptr, end);
                    }
                    type.ElementType = classOrValue;  // Store the actual CLASS/VALUETYPE
                    type.IsValid = true;
                }
                break;

            // Multi-dimensional array
            case ElementType.Array:
                {
                    // Skip element type
                    SkipType(ref ptr, end);
                    if (ptr >= end) return type;
                    uint rank = MetadataReader.ReadCompressedUInt(ref ptr);
                    if (ptr >= end) return type;
                    uint numSizes = MetadataReader.ReadCompressedUInt(ref ptr);
                    for (uint i = 0; i < numSizes && ptr < end; i++)
                        MetadataReader.ReadCompressedUInt(ref ptr);
                    if (ptr >= end) return type;
                    uint numLoBounds = MetadataReader.ReadCompressedUInt(ref ptr);
                    for (uint i = 0; i < numLoBounds && ptr < end; i++)
                        MetadataReader.ReadCompressedInt(ref ptr);
                    type.IsValid = true;
                }
                break;

            // Function pointer
            case ElementType.FnPtr:
                // Skip the entire method signature
                SkipMethodSig(ref ptr, end);
                type.IsValid = true;
                break;

            // Custom modifiers - skip modifier and continue with type
            case ElementType.CModReqd:
            case ElementType.CModOpt:
                if (ptr >= end) return type;
                MetadataReader.ReadCompressedUInt(ref ptr);  // Skip type token
                return ReadTypeSig(ref ptr, end);  // Continue with actual type

            default:
                // Unknown element type
                return type;
        }

        return type;
    }

    /// <summary>
    /// Skip over a type in the signature stream
    /// </summary>
    public static void SkipType(ref byte* ptr, byte* end)
    {
        if (ptr >= end) return;

        byte elemType = *ptr++;

        switch (elemType)
        {
            case ElementType.Void:
            case ElementType.Boolean:
            case ElementType.Char:
            case ElementType.I1:
            case ElementType.U1:
            case ElementType.I2:
            case ElementType.U2:
            case ElementType.I4:
            case ElementType.U4:
            case ElementType.I8:
            case ElementType.U8:
            case ElementType.R4:
            case ElementType.R8:
            case ElementType.String:
            case ElementType.I:
            case ElementType.U:
            case ElementType.Object:
            case ElementType.TypedByRef:
                // No additional data
                break;

            case ElementType.Class:
            case ElementType.ValueType:
            case ElementType.Var:
            case ElementType.MVar:
                MetadataReader.ReadCompressedUInt(ref ptr);
                break;

            case ElementType.Ptr:
            case ElementType.ByRef:
            case ElementType.SzArray:
            case ElementType.Pinned:
                SkipType(ref ptr, end);
                break;

            case ElementType.GenericInst:
                ptr++;  // Skip CLASS/VALUETYPE
                MetadataReader.ReadCompressedUInt(ref ptr);  // Type token
                uint argCount = MetadataReader.ReadCompressedUInt(ref ptr);
                for (uint i = 0; i < argCount && ptr < end; i++)
                    SkipType(ref ptr, end);
                break;

            case ElementType.Array:
                SkipType(ref ptr, end);
                uint rank = MetadataReader.ReadCompressedUInt(ref ptr);
                uint numSizes = MetadataReader.ReadCompressedUInt(ref ptr);
                for (uint i = 0; i < numSizes && ptr < end; i++)
                    MetadataReader.ReadCompressedUInt(ref ptr);
                uint numLoBounds = MetadataReader.ReadCompressedUInt(ref ptr);
                for (uint i = 0; i < numLoBounds && ptr < end; i++)
                    MetadataReader.ReadCompressedInt(ref ptr);
                break;

            case ElementType.FnPtr:
                SkipMethodSig(ref ptr, end);
                break;

            case ElementType.CModReqd:
            case ElementType.CModOpt:
                MetadataReader.ReadCompressedUInt(ref ptr);
                SkipType(ref ptr, end);
                break;
        }
    }

    /// <summary>
    /// Skip over a method signature in the stream
    /// </summary>
    public static void SkipMethodSig(ref byte* ptr, byte* end)
    {
        if (ptr >= end) return;

        byte header = *ptr++;
        if ((header & SignatureHeader.Generic) != 0)
            MetadataReader.ReadCompressedUInt(ref ptr);  // Generic param count

        uint paramCount = MetadataReader.ReadCompressedUInt(ref ptr);

        // Skip return type
        SkipType(ref ptr, end);

        // Skip all parameters
        for (uint i = 0; i < paramCount && ptr < end; i++)
        {
            SkipType(ref ptr, end);
        }
    }

    /// <summary>
    /// Get a human-readable name for an element type
    /// </summary>
    public static void PrintElementType(byte elemType)
    {
        switch (elemType)
        {
            case ElementType.Void: DebugConsole.Write("void"); break;
            case ElementType.Boolean: DebugConsole.Write("bool"); break;
            case ElementType.Char: DebugConsole.Write("char"); break;
            case ElementType.I1: DebugConsole.Write("sbyte"); break;
            case ElementType.U1: DebugConsole.Write("byte"); break;
            case ElementType.I2: DebugConsole.Write("short"); break;
            case ElementType.U2: DebugConsole.Write("ushort"); break;
            case ElementType.I4: DebugConsole.Write("int"); break;
            case ElementType.U4: DebugConsole.Write("uint"); break;
            case ElementType.I8: DebugConsole.Write("long"); break;
            case ElementType.U8: DebugConsole.Write("ulong"); break;
            case ElementType.R4: DebugConsole.Write("float"); break;
            case ElementType.R8: DebugConsole.Write("double"); break;
            case ElementType.String: DebugConsole.Write("string"); break;
            case ElementType.I: DebugConsole.Write("IntPtr"); break;
            case ElementType.U: DebugConsole.Write("UIntPtr"); break;
            case ElementType.Object: DebugConsole.Write("object"); break;
            case ElementType.TypedByRef: DebugConsole.Write("TypedReference"); break;
            case ElementType.Ptr: DebugConsole.Write("ptr"); break;
            case ElementType.ByRef: DebugConsole.Write("ref"); break;
            case ElementType.ValueType: DebugConsole.Write("valuetype"); break;
            case ElementType.Class: DebugConsole.Write("class"); break;
            case ElementType.Var: DebugConsole.Write("!T"); break;
            case ElementType.MVar: DebugConsole.Write("!!M"); break;
            case ElementType.Array: DebugConsole.Write("array"); break;
            case ElementType.GenericInst: DebugConsole.Write("generic"); break;
            case ElementType.SzArray: DebugConsole.Write("[]"); break;
            case ElementType.FnPtr: DebugConsole.Write("fnptr"); break;
            default:
                DebugConsole.Write("0x");
                DebugConsole.WriteHex(elemType);
                break;
        }
    }

    /// <summary>
    /// Print a method signature for debugging
    /// </summary>
    public static void PrintMethodSignature(ref MethodSignature sig)
    {
        // Return type
        PrintElementType(sig.ReturnType.ElementType);
        if (sig.ReturnType.Token != 0)
        {
            DebugConsole.Write("[0x");
            DebugConsole.WriteHex(sig.ReturnType.Token);
            DebugConsole.Write("]");
        }

        DebugConsole.Write(" (");

        // Flags
        if (sig.HasThis) DebugConsole.Write("instance ");
        if (sig.IsGeneric)
        {
            DebugConsole.Write("<");
            DebugConsole.WriteDecimal(sig.GenericParamCount);
            DebugConsole.Write("> ");
        }

        DebugConsole.WriteDecimal(sig.ParamCount);
        DebugConsole.Write(" params)");
    }

    /// <summary>
    /// Parse a field signature from blob data (ECMA-335 II.23.2.4)
    /// FieldSig: FIELD Type
    /// </summary>
    public static bool ReadFieldSignature(byte* blob, uint blobLength, out TypeSig fieldType)
    {
        fieldType = default;

        if (blob == null || blobLength == 0)
            return false;

        byte* ptr = blob;
        byte* end = blob + blobLength;

        // Read header byte - must be FIELD (0x06)
        byte header = *ptr++;
        if (header != SignatureHeader.Field)
            return false;

        // Read the field type
        if (ptr >= end)
            return false;

        fieldType = ReadTypeSig(ref ptr, end);
        return fieldType.IsValid;
    }

    /// <summary>
    /// Parse a local variable signature from blob data (ECMA-335 II.23.2.6)
    /// LocalVarSig: LOCAL_SIG Count Type+
    /// </summary>
    public static bool ReadLocalVarSignature(byte* blob, uint blobLength, out uint localCount, TypeSig* locals, uint maxLocals)
    {
        localCount = 0;

        if (blob == null || blobLength == 0)
            return false;

        byte* ptr = blob;
        byte* end = blob + blobLength;

        // Read header byte - must be LOCAL_SIG (0x07)
        byte header = *ptr++;
        if (header != SignatureHeader.LocalSig)
            return false;

        // Read local count
        if (ptr >= end)
            return false;
        localCount = MetadataReader.ReadCompressedUInt(ref ptr);

        // Read each local type
        uint count = localCount < maxLocals ? localCount : maxLocals;
        for (uint i = 0; i < count && ptr < end; i++)
        {
            locals[i] = ReadTypeSig(ref ptr, end);
            if (!locals[i].IsValid)
                return false;
        }

        // Skip remaining locals if we hit maxLocals
        for (uint i = count; i < localCount && ptr < end; i++)
        {
            SkipType(ref ptr, end);
        }

        return true;
    }

    /// <summary>
    /// Print a field signature for debugging
    /// </summary>
    public static void PrintFieldSignature(ref TypeSig fieldType)
    {
        PrintElementType(fieldType.ElementType);
        if (fieldType.Token != 0)
        {
            DebugConsole.Write("[0x");
            DebugConsole.WriteHex(fieldType.Token);
            DebugConsole.Write("]");
        }
    }
}
