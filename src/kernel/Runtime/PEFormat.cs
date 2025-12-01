// ProtonOS - PE/COFF Format Structures
// Portable Executable format definitions for parsing PE images.

using System.Runtime.InteropServices;

namespace ProtonOS.Runtime;

/// <summary>
/// PE format magic constants
/// </summary>
public static class PEConstants
{
    public const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;      // "MZ"
    public const uint IMAGE_NT_SIGNATURE = 0x00004550;    // "PE\0\0"
    public const ushort IMAGE_NT_OPTIONAL_HDR32_MAGIC = 0x10B;  // PE32
    public const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20B;  // PE32+
    public const uint METADATA_SIGNATURE = 0x424A5342;    // "BSJB" - .NET metadata signature

    // Machine types
    public const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
    public const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;
    public const ushort IMAGE_FILE_MACHINE_I386 = 0x14C;

    // Section characteristics
    public const uint IMAGE_SCN_CNT_CODE = 0x00000020;
    public const uint IMAGE_SCN_CNT_INITIALIZED_DATA = 0x00000040;
    public const uint IMAGE_SCN_CNT_UNINITIALIZED_DATA = 0x00000080;
    public const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;
    public const uint IMAGE_SCN_MEM_READ = 0x40000000;
    public const uint IMAGE_SCN_MEM_WRITE = 0x80000000;

    // Data directory indices
    public const int IMAGE_DIRECTORY_ENTRY_EXPORT = 0;
    public const int IMAGE_DIRECTORY_ENTRY_IMPORT = 1;
    public const int IMAGE_DIRECTORY_ENTRY_RESOURCE = 2;
    public const int IMAGE_DIRECTORY_ENTRY_EXCEPTION = 3;
    public const int IMAGE_DIRECTORY_ENTRY_SECURITY = 4;
    public const int IMAGE_DIRECTORY_ENTRY_BASERELOC = 5;
    public const int IMAGE_DIRECTORY_ENTRY_DEBUG = 6;
    public const int IMAGE_DIRECTORY_ENTRY_ARCHITECTURE = 7;
    public const int IMAGE_DIRECTORY_ENTRY_GLOBALPTR = 8;
    public const int IMAGE_DIRECTORY_ENTRY_TLS = 9;
    public const int IMAGE_DIRECTORY_ENTRY_LOAD_CONFIG = 10;
    public const int IMAGE_DIRECTORY_ENTRY_BOUND_IMPORT = 11;
    public const int IMAGE_DIRECTORY_ENTRY_IAT = 12;
    public const int IMAGE_DIRECTORY_ENTRY_DELAY_IMPORT = 13;
    public const int IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR = 14;
}

/// <summary>
/// DOS header (64 bytes) - just need to get to the PE header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ImageDosHeader
{
    public ushort e_magic;      // "MZ"
    public ushort e_cblp;
    public ushort e_cp;
    public ushort e_crlc;
    public ushort e_cparhdr;
    public ushort e_minalloc;
    public ushort e_maxalloc;
    public ushort e_ss;
    public ushort e_sp;
    public ushort e_csum;
    public ushort e_ip;
    public ushort e_cs;
    public ushort e_lfarlc;
    public ushort e_ovno;
    public fixed ushort e_res[4];   // 4 reserved words
    public ushort e_oemid;
    public ushort e_oeminfo;
    public fixed ushort e_res2[10]; // 10 reserved words
    public int e_lfanew;            // Offset to PE header
}

/// <summary>
/// PE file header (COFF header)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageFileHeader
{
    public ushort Machine;
    public ushort NumberOfSections;
    public uint TimeDateStamp;
    public uint PointerToSymbolTable;
    public uint NumberOfSymbols;
    public ushort SizeOfOptionalHeader;
    public ushort Characteristics;
}

/// <summary>
/// Data directory entry
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageDataDirectory
{
    public uint VirtualAddress;
    public uint Size;
}

/// <summary>
/// PE32 optional header (32-bit)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageOptionalHeader32
{
    public ushort Magic;                // 0x10b for PE32
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public uint SizeOfCode;
    public uint SizeOfInitializedData;
    public uint SizeOfUninitializedData;
    public uint AddressOfEntryPoint;
    public uint BaseOfCode;
    public uint BaseOfData;             // Not present in PE32+
    public uint ImageBase;              // 32-bit (vs 64-bit in PE32+)
    public uint SectionAlignment;
    public uint FileAlignment;
    public ushort MajorOperatingSystemVersion;
    public ushort MinorOperatingSystemVersion;
    public ushort MajorImageVersion;
    public ushort MinorImageVersion;
    public ushort MajorSubsystemVersion;
    public ushort MinorSubsystemVersion;
    public uint Win32VersionValue;
    public uint SizeOfImage;
    public uint SizeOfHeaders;
    public uint CheckSum;
    public ushort Subsystem;
    public ushort DllCharacteristics;
    public uint SizeOfStackReserve;     // 32-bit (vs 64-bit in PE32+)
    public uint SizeOfStackCommit;
    public uint SizeOfHeapReserve;
    public uint SizeOfHeapCommit;
    public uint LoaderFlags;
    public uint NumberOfRvaAndSizes;

    // Standard data directories (16 entries)
    public ImageDataDirectory ExportTable;
    public ImageDataDirectory ImportTable;
    public ImageDataDirectory ResourceTable;
    public ImageDataDirectory ExceptionTable;
    public ImageDataDirectory CertificateTable;
    public ImageDataDirectory BaseRelocationTable;
    public ImageDataDirectory Debug;
    public ImageDataDirectory Architecture;
    public ImageDataDirectory GlobalPtr;
    public ImageDataDirectory TLSTable;
    public ImageDataDirectory LoadConfigTable;
    public ImageDataDirectory BoundImport;
    public ImageDataDirectory IAT;
    public ImageDataDirectory DelayImportDescriptor;
    public ImageDataDirectory CLRRuntimeHeader;
    public ImageDataDirectory Reserved;
}

/// <summary>
/// PE32+ optional header (64-bit)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageOptionalHeader64
{
    public ushort Magic;                // 0x20b for PE32+
    public byte MajorLinkerVersion;
    public byte MinorLinkerVersion;
    public uint SizeOfCode;
    public uint SizeOfInitializedData;
    public uint SizeOfUninitializedData;
    public uint AddressOfEntryPoint;
    public uint BaseOfCode;
    public ulong ImageBase;
    public uint SectionAlignment;
    public uint FileAlignment;
    public ushort MajorOperatingSystemVersion;
    public ushort MinorOperatingSystemVersion;
    public ushort MajorImageVersion;
    public ushort MinorImageVersion;
    public ushort MajorSubsystemVersion;
    public ushort MinorSubsystemVersion;
    public uint Win32VersionValue;
    public uint SizeOfImage;
    public uint SizeOfHeaders;
    public uint CheckSum;
    public ushort Subsystem;
    public ushort DllCharacteristics;
    public ulong SizeOfStackReserve;
    public ulong SizeOfStackCommit;
    public ulong SizeOfHeapReserve;
    public ulong SizeOfHeapCommit;
    public uint LoaderFlags;
    public uint NumberOfRvaAndSizes;

    // Standard data directories (16 entries)
    public ImageDataDirectory ExportTable;
    public ImageDataDirectory ImportTable;
    public ImageDataDirectory ResourceTable;
    public ImageDataDirectory ExceptionTable;       // .pdata
    public ImageDataDirectory CertificateTable;
    public ImageDataDirectory BaseRelocationTable;
    public ImageDataDirectory Debug;
    public ImageDataDirectory Architecture;
    public ImageDataDirectory GlobalPtr;
    public ImageDataDirectory TLSTable;
    public ImageDataDirectory LoadConfigTable;
    public ImageDataDirectory BoundImport;
    public ImageDataDirectory IAT;
    public ImageDataDirectory DelayImportDescriptor;
    public ImageDataDirectory CLRRuntimeHeader;
    public ImageDataDirectory Reserved;
}

/// <summary>
/// PE NT headers (32-bit)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageNtHeaders32
{
    public uint Signature;              // "PE\0\0"
    public ImageFileHeader FileHeader;
    public ImageOptionalHeader32 OptionalHeader;
}

/// <summary>
/// PE NT headers (64-bit)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageNtHeaders64
{
    public uint Signature;              // "PE\0\0"
    public ImageFileHeader FileHeader;
    public ImageOptionalHeader64 OptionalHeader;
}

/// <summary>
/// CLI header (IMAGE_COR20_HEADER) - .NET assembly metadata location
/// Located via data directory index 14 (COM_DESCRIPTOR)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageCor20Header
{
    public uint cb;                         // Size of this header (72 bytes)
    public ushort MajorRuntimeVersion;
    public ushort MinorRuntimeVersion;
    public ImageDataDirectory MetaData;     // RVA/size of metadata
    public uint Flags;                      // CorFlags
    public uint EntryPointTokenOrRVA;       // Managed entry point token (or native RVA if NATIVE_ENTRYPOINT)
    public ImageDataDirectory Resources;
    public ImageDataDirectory StrongNameSignature;
    public ImageDataDirectory CodeManagerTable;      // Deprecated, always 0
    public ImageDataDirectory VTableFixups;
    public ImageDataDirectory ExportAddressTableJumps;
    public ImageDataDirectory ManagedNativeHeader;   // READYTORUN_HEADER in R2R images
}

/// <summary>
/// CorFlags from IMAGE_COR20_HEADER.Flags
/// </summary>
public static class CorFlags
{
    public const uint ILOnly = 0x00000001;
    public const uint Requires32Bit = 0x00000002;
    public const uint ILLibrary = 0x00000004;
    public const uint StrongNameSigned = 0x00000008;
    public const uint NativeEntryPoint = 0x00000010;
    public const uint TrackDebugData = 0x00010000;
    public const uint Prefers32Bit = 0x00020000;
}

/// <summary>
/// PE section header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ImageSectionHeader
{
    // Name is 8 bytes, split for comparison
    public uint Name0;  // First 4 chars
    public uint Name1;  // Last 4 chars
    public uint VirtualSize;
    public uint VirtualAddress;
    public uint SizeOfRawData;
    public uint PointerToRawData;
    public uint PointerToRelocations;
    public uint PointerToLinenumbers;
    public ushort NumberOfRelocations;
    public ushort NumberOfLinenumbers;
    public uint Characteristics;
}

/// <summary>
/// Helper methods for PE parsing (supports both PE32 and PE32+)
/// </summary>
public static unsafe class PEHelper
{
    /// <summary>
    /// Check if PE is 64-bit (PE32+) or 32-bit (PE32)
    /// </summary>
    public static bool IsPE64(void* imageBase)
    {
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != PEConstants.IMAGE_DOS_SIGNATURE)
            return false;

        // Read the optional header magic (at offset 24 from NT headers start: 4 sig + 20 file header)
        byte* ntBase = (byte*)imageBase + dosHeader->e_lfanew;
        ushort magic = *(ushort*)(ntBase + 24);
        return magic == PEConstants.IMAGE_NT_OPTIONAL_HDR64_MAGIC;
    }

    /// <summary>
    /// Get the NT headers (PE32+) from an image base address.
    /// Returns null if PE32 format or invalid.
    /// </summary>
    public static ImageNtHeaders64* GetNtHeaders64(void* imageBase)
    {
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != PEConstants.IMAGE_DOS_SIGNATURE)
            return null;

        var ntHeaders = (ImageNtHeaders64*)((byte*)imageBase + dosHeader->e_lfanew);
        if (ntHeaders->Signature != PEConstants.IMAGE_NT_SIGNATURE)
            return null;
        if (ntHeaders->OptionalHeader.Magic != PEConstants.IMAGE_NT_OPTIONAL_HDR64_MAGIC)
            return null;

        return ntHeaders;
    }

    /// <summary>
    /// Get the NT headers (PE32) from an image base address.
    /// Returns null if PE32+ format or invalid.
    /// </summary>
    public static ImageNtHeaders32* GetNtHeaders32(void* imageBase)
    {
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != PEConstants.IMAGE_DOS_SIGNATURE)
            return null;

        var ntHeaders = (ImageNtHeaders32*)((byte*)imageBase + dosHeader->e_lfanew);
        if (ntHeaders->Signature != PEConstants.IMAGE_NT_SIGNATURE)
            return null;
        if (ntHeaders->OptionalHeader.Magic != PEConstants.IMAGE_NT_OPTIONAL_HDR32_MAGIC)
            return null;

        return ntHeaders;
    }

    /// <summary>
    /// Get the NT headers from an image base address (for PE32+ images).
    /// Legacy method - use GetNtHeaders64 or GetNtHeaders32 for explicit format.
    /// </summary>
    public static ImageNtHeaders64* GetNtHeaders(void* imageBase)
    {
        return GetNtHeaders64(imageBase);
    }

    /// <summary>
    /// Get first section header from PE32+ NT headers
    /// </summary>
    public static ImageSectionHeader* GetFirstSectionHeader(ImageNtHeaders64* ntHeaders)
    {
        return (ImageSectionHeader*)((byte*)&ntHeaders->OptionalHeader + ntHeaders->FileHeader.SizeOfOptionalHeader);
    }

    /// <summary>
    /// Get first section header from PE32 NT headers
    /// </summary>
    public static ImageSectionHeader* GetFirstSectionHeader(ImageNtHeaders32* ntHeaders)
    {
        return (ImageSectionHeader*)((byte*)&ntHeaders->OptionalHeader + ntHeaders->FileHeader.SizeOfOptionalHeader);
    }

    /// <summary>
    /// Convert RVA to pointer using image base (for memory-mapped images)
    /// </summary>
    public static void* RvaToPointer(void* imageBase, uint rva)
    {
        return (byte*)imageBase + rva;
    }

    /// <summary>
    /// Convert RVA to file offset for a PE file loaded as raw bytes.
    /// Supports both PE32 and PE32+ formats.
    /// Returns null if the RVA is not within any section.
    /// </summary>
    public static void* RvaToFilePointer(void* fileBase, uint rva)
    {
        var dosHeader = (ImageDosHeader*)fileBase;
        if (dosHeader->e_magic != PEConstants.IMAGE_DOS_SIGNATURE)
            return null;

        byte* ntBase = (byte*)fileBase + dosHeader->e_lfanew;
        uint signature = *(uint*)ntBase;
        if (signature != PEConstants.IMAGE_NT_SIGNATURE)
            return null;

        // Get file header (same location for PE32 and PE32+)
        var fileHeader = (ImageFileHeader*)(ntBase + 4);
        int numSections = fileHeader->NumberOfSections;

        // Check optional header magic to determine format
        ushort magic = *(ushort*)(ntBase + 24);
        uint sizeOfHeaders;
        ImageSectionHeader* sections;

        if (magic == PEConstants.IMAGE_NT_OPTIONAL_HDR64_MAGIC)
        {
            var ntHeaders = (ImageNtHeaders64*)ntBase;
            sizeOfHeaders = ntHeaders->OptionalHeader.SizeOfHeaders;
            sections = GetFirstSectionHeader(ntHeaders);
        }
        else if (magic == PEConstants.IMAGE_NT_OPTIONAL_HDR32_MAGIC)
        {
            var ntHeaders = (ImageNtHeaders32*)ntBase;
            sizeOfHeaders = ntHeaders->OptionalHeader.SizeOfHeaders;
            sections = GetFirstSectionHeader(ntHeaders);
        }
        else
        {
            return null;  // Unknown PE format
        }

        // Check if RVA is in the headers (before any section)
        if (rva < sizeOfHeaders)
            return (byte*)fileBase + rva;

        // Find the section containing this RVA
        for (int i = 0; i < numSections; i++)
        {
            uint sectionStart = sections[i].VirtualAddress;
            uint sectionEnd = sectionStart + sections[i].VirtualSize;

            if (rva >= sectionStart && rva < sectionEnd)
            {
                // RVA is in this section - convert to file offset
                uint offsetInSection = rva - sectionStart;
                uint fileOffset = sections[i].PointerToRawData + offsetInSection;
                return (byte*)fileBase + fileOffset;
            }
        }

        return null;  // RVA not found in any section
    }

    /// <summary>
    /// Get a data directory entry from PE32+ headers
    /// </summary>
    public static ImageDataDirectory* GetDataDirectory(ImageNtHeaders64* ntHeaders, int index)
    {
        if (index < 0 || index >= ntHeaders->OptionalHeader.NumberOfRvaAndSizes)
            return null;

        return &(&ntHeaders->OptionalHeader.ExportTable)[index];
    }

    /// <summary>
    /// Get a data directory entry from PE32 headers
    /// </summary>
    public static ImageDataDirectory* GetDataDirectory(ImageNtHeaders32* ntHeaders, int index)
    {
        if (index < 0 || index >= ntHeaders->OptionalHeader.NumberOfRvaAndSizes)
            return null;

        return &(&ntHeaders->OptionalHeader.ExportTable)[index];
    }

    /// <summary>
    /// Get a data directory entry (auto-detects PE32/PE32+)
    /// </summary>
    public static ImageDataDirectory* GetDataDirectoryAuto(void* imageBase, int index)
    {
        if (IsPE64(imageBase))
        {
            var ntHeaders = GetNtHeaders64(imageBase);
            return ntHeaders != null ? GetDataDirectory(ntHeaders, index) : null;
        }
        else
        {
            var ntHeaders = GetNtHeaders32(imageBase);
            return ntHeaders != null ? GetDataDirectory(ntHeaders, index) : null;
        }
    }

    /// <summary>
    /// Get the CLI header (IMAGE_COR20_HEADER) from a memory-mapped .NET assembly
    /// </summary>
    public static ImageCor20Header* GetCorHeader(void* imageBase)
    {
        var clrDir = GetDataDirectoryAuto(imageBase, PEConstants.IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR);
        if (clrDir == null || clrDir->VirtualAddress == 0)
            return null;

        return (ImageCor20Header*)RvaToPointer(imageBase, clrDir->VirtualAddress);
    }

    /// <summary>
    /// Get the CLI header from a PE file loaded as raw bytes (supports PE32 and PE32+)
    /// </summary>
    public static ImageCor20Header* GetCorHeaderFromFile(void* fileBase)
    {
        var clrDir = GetDataDirectoryAuto(fileBase, PEConstants.IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR);
        if (clrDir == null || clrDir->VirtualAddress == 0)
            return null;

        return (ImageCor20Header*)RvaToFilePointer(fileBase, clrDir->VirtualAddress);
    }

    /// <summary>
    /// Get metadata root pointer from a memory-mapped .NET assembly
    /// </summary>
    public static void* GetMetadataRoot(void* imageBase)
    {
        var corHeader = GetCorHeader(imageBase);
        if (corHeader == null || corHeader->MetaData.VirtualAddress == 0)
            return null;

        return RvaToPointer(imageBase, corHeader->MetaData.VirtualAddress);
    }

    /// <summary>
    /// Get metadata root pointer from a PE file loaded as raw bytes
    /// </summary>
    public static void* GetMetadataRootFromFile(void* fileBase)
    {
        var corHeader = GetCorHeaderFromFile(fileBase);
        if (corHeader == null || corHeader->MetaData.VirtualAddress == 0)
            return null;

        return RvaToFilePointer(fileBase, corHeader->MetaData.VirtualAddress);
    }
}
