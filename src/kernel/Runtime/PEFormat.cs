// netos - PE/COFF Format Structures
// Portable Executable format definitions for parsing PE images.

using System.Runtime.InteropServices;

namespace Kernel.Runtime;

/// <summary>
/// PE format magic constants
/// </summary>
public static class PEConstants
{
    public const ushort IMAGE_DOS_SIGNATURE = 0x5A4D;      // "MZ"
    public const uint IMAGE_NT_SIGNATURE = 0x00004550;    // "PE\0\0"
    public const ushort IMAGE_NT_OPTIONAL_HDR64_MAGIC = 0x20B;  // PE32+

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
/// Helper methods for PE parsing
/// </summary>
public static unsafe class PEHelper
{
    /// <summary>
    /// Get the NT headers from an image base address
    /// </summary>
    public static ImageNtHeaders64* GetNtHeaders(void* imageBase)
    {
        var dosHeader = (ImageDosHeader*)imageBase;
        if (dosHeader->e_magic != PEConstants.IMAGE_DOS_SIGNATURE)
            return null;

        var ntHeaders = (ImageNtHeaders64*)((byte*)imageBase + dosHeader->e_lfanew);
        if (ntHeaders->Signature != PEConstants.IMAGE_NT_SIGNATURE)
            return null;

        return ntHeaders;
    }

    /// <summary>
    /// Get first section header from NT headers
    /// </summary>
    public static ImageSectionHeader* GetFirstSectionHeader(ImageNtHeaders64* ntHeaders)
    {
        return (ImageSectionHeader*)((byte*)&ntHeaders->OptionalHeader + ntHeaders->FileHeader.SizeOfOptionalHeader);
    }

    /// <summary>
    /// Convert RVA to pointer using image base
    /// </summary>
    public static void* RvaToPointer(void* imageBase, uint rva)
    {
        return (byte*)imageBase + rva;
    }

    /// <summary>
    /// Get a data directory entry
    /// </summary>
    public static ImageDataDirectory* GetDataDirectory(ImageNtHeaders64* ntHeaders, int index)
    {
        if (index < 0 || index >= ntHeaders->OptionalHeader.NumberOfRvaAndSizes)
            return null;

        return &(&ntHeaders->OptionalHeader.ExportTable)[index];
    }
}
