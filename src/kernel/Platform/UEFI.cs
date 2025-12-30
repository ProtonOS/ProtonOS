// ProtonOS kernel - UEFI type definitions and boot services
// Provides access to UEFI memory map and boot services for kernel initialization.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

// ============================================================================
// UEFI Status Codes
// ============================================================================

public enum EFIStatus : ulong
{
    Success = 0,
    BufferTooSmall = 0x8000000000000005,
    InvalidParameter = 0x8000000000000002,
}

// ============================================================================
// UEFI Memory Types
// ============================================================================

public enum EFIMemoryType : uint
{
    ReservedMemoryType = 0,
    LoaderCode = 1,
    LoaderData = 2,
    BootServicesCode = 3,
    BootServicesData = 4,
    RuntimeServicesCode = 5,
    RuntimeServicesData = 6,
    ConventionalMemory = 7,      // Free memory we can use!
    UnusableMemory = 8,
    ACPIReclaimMemory = 9,
    ACPIMemoryNVS = 10,
    MemoryMappedIO = 11,
    MemoryMappedIOPortSpace = 12,
    PalCode = 13,
    PersistentMemory = 14,
    MaxMemoryType = 15,
}

// Memory attribute bits
public static class EFIMemoryAttribute
{
    public const ulong UC = 0x0000000000000001;  // Uncacheable
    public const ulong WC = 0x0000000000000002;  // Write-combining
    public const ulong WT = 0x0000000000000004;  // Write-through
    public const ulong WB = 0x0000000000000008;  // Write-back
    public const ulong UCE = 0x0000000000000010; // Uncacheable, exported
    public const ulong WP = 0x0000000000001000;  // Write-protected
    public const ulong RP = 0x0000000000002000;  // Read-protected
    public const ulong XP = 0x0000000000004000;  // Execute-protected
    public const ulong NV = 0x0000000000008000;  // Non-volatile
    public const ulong MoreReliable = 0x0000000000010000;
    public const ulong RO = 0x0000000000020000;  // Read-only
    public const ulong Runtime = 0x8000000000000000; // Memory region needs runtime mapping
}

// ============================================================================
// UEFI GUIDs
// ============================================================================

/// <summary>
/// UEFI GUID structure (128-bit identifier)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EFIGUID
{
    public uint Data1;
    public ushort Data2;
    public ushort Data3;
    public unsafe fixed byte Data4[8];

    /// <summary>
    /// EFI_LOADED_IMAGE_PROTOCOL_GUID
    /// {5B1B31A1-9562-11D2-8E3F-00A0C969723B}
    /// </summary>
    public static EFIGUID LoadedImageProtocol => new EFIGUID
    {
        Data1 = 0x5B1B31A1,
        Data2 = 0x9562,
        Data3 = 0x11D2,
    };

    public static unsafe void InitLoadedImageGuid(EFIGUID* guid)
    {
        guid->Data1 = 0x5B1B31A1;
        guid->Data2 = 0x9562;
        guid->Data3 = 0x11D2;
        guid->Data4[0] = 0x8E;
        guid->Data4[1] = 0x3F;
        guid->Data4[2] = 0x00;
        guid->Data4[3] = 0xA0;
        guid->Data4[4] = 0xC9;
        guid->Data4[5] = 0x69;
        guid->Data4[6] = 0x72;
        guid->Data4[7] = 0x3B;
    }

    /// <summary>
    /// EFI_SIMPLE_FILE_SYSTEM_PROTOCOL_GUID
    /// {964E5B22-6459-11D2-8E39-00A0C969723B}
    /// </summary>
    public static unsafe void InitSimpleFileSystemGuid(EFIGUID* guid)
    {
        guid->Data1 = 0x964E5B22;
        guid->Data2 = 0x6459;
        guid->Data3 = 0x11D2;
        guid->Data4[0] = 0x8E;
        guid->Data4[1] = 0x39;
        guid->Data4[2] = 0x00;
        guid->Data4[3] = 0xA0;
        guid->Data4[4] = 0xC9;
        guid->Data4[5] = 0x69;
        guid->Data4[6] = 0x72;
        guid->Data4[7] = 0x3B;
    }

    /// <summary>
    /// EFI_FILE_INFO_ID
    /// {09576E92-6D3F-11D2-8E39-00A0C969723B}
    /// </summary>
    public static unsafe void InitFileInfoGuid(EFIGUID* guid)
    {
        guid->Data1 = 0x09576E92;
        guid->Data2 = 0x6D3F;
        guid->Data3 = 0x11D2;
        guid->Data4[0] = 0x8E;
        guid->Data4[1] = 0x39;
        guid->Data4[2] = 0x00;
        guid->Data4[3] = 0xA0;
        guid->Data4[4] = 0xC9;
        guid->Data4[5] = 0x69;
        guid->Data4[6] = 0x72;
        guid->Data4[7] = 0x3B;
    }
}

// ============================================================================
// UEFI Structures
// ============================================================================

[StructLayout(LayoutKind.Sequential)]
public struct EFITableHeader
{
    public ulong Signature;
    public uint Revision;
    public uint HeaderSize;
    public uint Crc32;
    public uint Reserved;
}

/// <summary>
/// UEFI Memory Descriptor - describes a region of physical memory
/// Note: The actual size may be larger than sizeof() due to UEFI versioning.
/// Always use DescriptorSize from GetMemoryMap.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EFIMemoryDescriptor
{
    public EFIMemoryType Type;           // Type of memory region
    public ulong PhysicalStart;          // Physical address of region start
    public ulong VirtualStart;           // Virtual address (if mapped)
    public ulong NumberOfPages;          // Size in 4KB pages
    public ulong Attribute;              // Memory attributes
}

/// <summary>
/// EFI_LOADED_IMAGE_PROTOCOL - Information about a loaded image
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFILoadedImageProtocol
{
    public uint Revision;
    public void* ParentHandle;
    public EFISystemTable* SystemTable;

    // Source location of the image
    public void* DeviceHandle;
    public void* FilePath;          // EFI_DEVICE_PATH_PROTOCOL
    public void* Reserved;

    // Image's load options
    public uint LoadOptionsSize;
    public void* LoadOptions;

    // Location where the image was loaded
    public void* ImageBase;         // Start of image in memory
    public ulong ImageSize;         // Size of image in bytes
    public EFIMemoryType ImageCodeType;
    public EFIMemoryType ImageDataType;
    public void* Unload;            // Unload function pointer
}

// ============================================================================
// UEFI File System Protocol Structures
// ============================================================================

/// <summary>
/// EFI_SIMPLE_FILE_SYSTEM_PROTOCOL - provides access to FAT file systems
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFISimpleFileSystemProtocol
{
    public ulong Revision;

    /// <summary>
    /// Opens the root directory on a volume
    /// </summary>
    public delegate* unmanaged<
        EFISimpleFileSystemProtocol*,  // This
        EFIFileProtocol**,              // Root (out)
        EFIStatus> OpenVolume;
}

/// <summary>
/// EFI_FILE_PROTOCOL - interface for file operations
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFIFileProtocol
{
    public ulong Revision;

    /// <summary>
    /// Opens a new file relative to this file's location
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIFileProtocol**,   // NewHandle (out)
        char*,               // FileName (UTF-16)
        ulong,               // OpenMode
        ulong,               // Attributes
        EFIStatus> Open;

    /// <summary>
    /// Closes the file handle
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIStatus> Close;

    /// <summary>
    /// Deletes the file
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIStatus> Delete;

    /// <summary>
    /// Reads data from the file
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        ulong*,              // BufferSize (in/out)
        void*,               // Buffer
        EFIStatus> Read;

    /// <summary>
    /// Writes data to the file
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        ulong*,              // BufferSize (in/out)
        void*,               // Buffer
        EFIStatus> Write;

    /// <summary>
    /// Gets the current file position
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        ulong*,              // Position (out)
        EFIStatus> GetPosition;

    /// <summary>
    /// Sets the current file position
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        ulong,               // Position
        EFIStatus> SetPosition;

    /// <summary>
    /// Gets information about the file
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIGUID*,            // InformationType
        ulong*,              // BufferSize (in/out)
        void*,               // Buffer
        EFIStatus> GetInfo;

    /// <summary>
    /// Sets information about the file
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIGUID*,            // InformationType
        ulong,               // BufferSize
        void*,               // Buffer
        EFIStatus> SetInfo;

    /// <summary>
    /// Flushes all modified data to the device
    /// </summary>
    public delegate* unmanaged<
        EFIFileProtocol*,    // This
        EFIStatus> Flush;
}

/// <summary>
/// File open modes
/// </summary>
public static class EFIFileMode
{
    public const ulong Read = 0x0000000000000001;
    public const ulong Write = 0x0000000000000002;
    public const ulong Create = 0x8000000000000000;
}

/// <summary>
/// File attributes
/// </summary>
public static class EFIFileAttribute
{
    public const ulong ReadOnly = 0x0000000000000001;
    public const ulong Hidden = 0x0000000000000002;
    public const ulong System = 0x0000000000000004;
    public const ulong Reserved = 0x0000000000000008;
    public const ulong Directory = 0x0000000000000010;
    public const ulong Archive = 0x0000000000000020;
}

/// <summary>
/// EFI_TIME structure for file timestamps
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EFITime
{
    public ushort Year;
    public byte Month;
    public byte Day;
    public byte Hour;
    public byte Minute;
    public byte Second;
    public byte Pad1;
    public uint Nanosecond;
    public short TimeZone;
    public byte Daylight;
    public byte Pad2;
}

/// <summary>
/// EFI_FILE_INFO - file information structure
/// Note: FileName is variable-length UTF-16 at the end
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFIFileInfo
{
    public ulong Size;              // Total structure size including filename
    public ulong FileSize;          // File size in bytes
    public ulong PhysicalSize;      // Physical storage used
    public EFITime CreateTime;
    public EFITime LastAccessTime;
    public EFITime ModificationTime;
    public ulong Attribute;
    // CHAR16 FileName[] follows - variable length, null-terminated
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFISystemTable
{
    public EFITableHeader Hdr;
    public char* FirmwareVendor;
    public uint FirmwareRevision;
    public void* ConsoleInHandle;
    public void* ConIn;
    public void* ConsoleOutHandle;
    public EFISimpleTextOutputProtocol* ConOut;
    public void* StandardErrorHandle;
    public void* StdErr;
    public void* RuntimeServices;
    public EFIBootServices* BootServices;
    public ulong NumberOfTableEntries;
    public void* ConfigurationTable;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFISimpleTextOutputProtocol
{
    public void* Reset;
    public delegate* unmanaged<EFISimpleTextOutputProtocol*, char*, EFIStatus> OutputString;
    public void* TestString;
    public void* QueryMode;
    public void* SetMode;
    public void* SetAttribute;
    public void* ClearScreen;
    public void* SetCursorPosition;
    public void* EnableCursor;
    public void* Mode;
}

/// <summary>
/// UEFI Boot Services table
/// We only define the fields we need, with padding for the rest.
/// Boot services table layout (x64, all pointers are 8 bytes):
/// - Header: 24 bytes (offsets 0-23)
/// - Task Priority: RaiseTPL, RestoreTPL (offsets 24, 32)
/// - Memory: AllocatePages, FreePages, GetMemoryMap, AllocatePool, FreePool (offsets 40-72)
/// - Events: CreateEvent, SetTimer, WaitForEvent, SignalEvent, CloseEvent, CheckEvent (offsets 80-120)
/// - Protocol Handlers: 9 functions (offsets 128-192)
/// - Image Services: LoadImage, StartImage, Exit, UnloadImage (offsets 200-224)
/// - ExitBootServices: offset 232
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFIBootServices
{
    public EFITableHeader Hdr;

    // Task Priority Services (2 functions)
    private void* _raiseTPL;
    private void* _restoreTPL;

    // Memory Services (5 functions)
    private void* _allocatePages;
    private void* _freePages;

    /// <summary>
    /// GetMemoryMap - Get the current memory map from UEFI
    /// </summary>
    public delegate* unmanaged<
        ulong*,              // MemoryMapSize (in/out)
        EFIMemoryDescriptor*, // MemoryMap (out)
        ulong*,              // MapKey (out)
        ulong*,              // DescriptorSize (out)
        uint*,               // DescriptorVersion (out)
        EFIStatus> GetMemoryMap;

    public delegate* unmanaged<
        EFIMemoryType,       // PoolType
        ulong,               // Size
        void**,              // Buffer (out)
        EFIStatus> AllocatePool;

    public delegate* unmanaged<
        void*,               // Buffer
        EFIStatus> FreePool;

    // We need more padding to get to ExitBootServices (at offset 232)
    // Event Services: CreateEvent, SetTimer, WaitForEvent, SignalEvent, CloseEvent, CheckEvent (6)
    private void* _createEvent, _setTimer, _waitForEvent, _signalEvent, _closeEvent, _checkEvent;

    // Protocol Handler Services (9 functions, offsets 128-192)
    // InstallProtocolInterface, ReinstallProtocolInterface, UninstallProtocolInterface,
    // HandleProtocol, Reserved, RegisterProtocolNotify, LocateHandle, LocateDevicePath,
    // InstallConfigurationTable
    private void* _installProto, _reinstallProto, _uninstallProto;

    /// <summary>
    /// HandleProtocol - Get a protocol interface for a handle
    /// </summary>
    public delegate* unmanaged<
        void*,               // Handle
        EFIGUID*,            // Protocol GUID
        void**,              // Interface (out)
        EFIStatus> HandleProtocol;

    private void* _reserved;
    private void* _registerProtoNotify, _locateHandle, _locateDevicePath, _installConfigTable;

    // Image Services: LoadImage, StartImage, Exit, UnloadImage (4, offsets 200-224)
    private void* _loadImage, _startImage, _exit, _unloadImage;

    /// <summary>
    /// ExitBootServices - Terminate boot services
    /// Must be called with the map key from GetMemoryMap
    /// </summary>
    public delegate* unmanaged<
        void*,               // ImageHandle
        ulong,               // MapKey
        EFIStatus> ExitBootServices;
}

// ============================================================================
// UEFI Boot Context - captures UEFI state for kernel use
// ============================================================================

/// <summary>
/// UEFI type definitions and helpers for parsing boot information.
/// Note: Boot services are NOT available - the bootloader exits them before calling the kernel.
/// Use BootInfoAccess for all boot information instead.
/// </summary>
public static unsafe class UEFIBoot
{
    /// <summary>
    /// Get a memory descriptor from a raw buffer (EFI memory map format).
    /// The memory map is in EFI format because BootInfo stores the raw UEFI map.
    /// </summary>
    public static EFIMemoryDescriptor* GetDescriptor(byte* buffer, ulong descriptorSize, int index)
    {
        return (EFIMemoryDescriptor*)(buffer + (ulong)index * descriptorSize);
    }
}
