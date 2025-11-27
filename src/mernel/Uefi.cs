// netos mernel - UEFI type definitions and boot services
// Provides access to UEFI memory map and boot services for kernel initialization.

using System;
using System.Runtime.InteropServices;

namespace Mernel;

// ============================================================================
// UEFI Status Codes
// ============================================================================

public enum EfiStatus : ulong
{
    Success = 0,
    BufferTooSmall = 0x8000000000000005,
    InvalidParameter = 0x8000000000000002,
}

// ============================================================================
// UEFI Memory Types
// ============================================================================

public enum EfiMemoryType : uint
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
public static class EfiMemoryAttribute
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
// UEFI Structures
// ============================================================================

[StructLayout(LayoutKind.Sequential)]
public struct EfiTableHeader
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
public struct EfiMemoryDescriptor
{
    public EfiMemoryType Type;           // Type of memory region
    public ulong PhysicalStart;          // Physical address of region start
    public ulong VirtualStart;           // Virtual address (if mapped)
    public ulong NumberOfPages;          // Size in 4KB pages
    public ulong Attribute;              // Memory attributes
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EfiSystemTable
{
    public EfiTableHeader Hdr;
    public char* FirmwareVendor;
    public uint FirmwareRevision;
    public void* ConsoleInHandle;
    public void* ConIn;
    public void* ConsoleOutHandle;
    public EfiSimpleTextOutputProtocol* ConOut;
    public void* StandardErrorHandle;
    public void* StdErr;
    public void* RuntimeServices;
    public EfiBootServices* BootServices;
    public ulong NumberOfTableEntries;
    public void* ConfigurationTable;
}

[StructLayout(LayoutKind.Sequential)]
public unsafe struct EfiSimpleTextOutputProtocol
{
    public void* Reset;
    public delegate* unmanaged<EfiSimpleTextOutputProtocol*, char*, EfiStatus> OutputString;
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
/// Boot services table layout (x64):
/// - Header: 24 bytes
/// - RaiseTPL, RestoreTPL: 16 bytes (offsets 24, 32)
/// - AllocatePages, FreePages: 16 bytes (offsets 40, 48)
/// - GetMemoryMap: 8 bytes (offset 56)
/// - AllocatePool, FreePool: 16 bytes (offsets 64, 72)
/// - ... more services follow
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EfiBootServices
{
    public EfiTableHeader Hdr;

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
        EfiMemoryDescriptor*, // MemoryMap (out)
        ulong*,              // MapKey (out)
        ulong*,              // DescriptorSize (out)
        uint*,               // DescriptorVersion (out)
        EfiStatus> GetMemoryMap;

    public delegate* unmanaged<
        EfiMemoryType,       // PoolType
        ulong,               // Size
        void**,              // Buffer (out)
        EfiStatus> AllocatePool;

    public delegate* unmanaged<
        void*,               // Buffer
        EfiStatus> FreePool;

    // We need more padding to get to ExitBootServices
    // CreateEvent, SetTimer, WaitForEvent, SignalEvent, CloseEvent, CheckEvent (6)
    private void* _pad1, _pad2, _pad3, _pad4, _pad5, _pad6;

    // Protocol Handler Services (6 functions)
    private void* _pad7, _pad8, _pad9, _pad10, _pad11, _pad12;

    // Image Services: LoadImage, StartImage, Exit, UnloadImage (4)
    private void* _pad13, _pad14, _pad15, _pad16;

    /// <summary>
    /// ExitBootServices - Terminate boot services
    /// Must be called with the map key from GetMemoryMap
    /// </summary>
    public delegate* unmanaged<
        void*,               // ImageHandle
        ulong,               // MapKey
        EfiStatus> ExitBootServices;
}

// ============================================================================
// UEFI Boot Context - captures UEFI state for kernel use
// ============================================================================

/// <summary>
/// Provides access to UEFI boot information.
/// EfiEntry in native.asm saves the UEFI parameters before calling zerolib's EfiMain.
/// </summary>
public static unsafe class UefiBoot
{
    private static bool _bootServicesExited;

    // Native functions to retrieve UEFI parameters saved by EfiEntry
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern EfiSystemTable* get_uefi_system_table();

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void* get_uefi_image_handle();

    /// <summary>
    /// Get the UEFI System Table (saved by EfiEntry)
    /// </summary>
    public static EfiSystemTable* SystemTable => get_uefi_system_table();

    /// <summary>
    /// Get the UEFI Image Handle (saved by EfiEntry)
    /// </summary>
    public static void* ImageHandle => get_uefi_image_handle();

    /// <summary>
    /// Get the Boot Services table (only valid before ExitBootServices)
    /// </summary>
    public static EfiBootServices* BootServices
    {
        get
        {
            if (_bootServicesExited)
                return null;
            var st = SystemTable;
            if (st == null)
                return null;
            return st->BootServices;
        }
    }

    /// <summary>
    /// Check if boot services are still available
    /// </summary>
    public static bool BootServicesAvailable => !_bootServicesExited && SystemTable != null;

    /// <summary>
    /// Exit boot services - after this, UEFI boot services are no longer available.
    /// The memory map must be retrieved before calling this.
    /// </summary>
    public static EfiStatus ExitBootServices(ulong mapKey)
    {
        if (_bootServicesExited)
            return EfiStatus.InvalidParameter;

        var st = SystemTable;
        if (st == null || st->BootServices == null)
            return EfiStatus.InvalidParameter;

        var status = st->BootServices->ExitBootServices(ImageHandle, mapKey);
        if (status == EfiStatus.Success)
        {
            _bootServicesExited = true;
        }
        return status;
    }

    /// <summary>
    /// Get the UEFI memory map. Must be called before ExitBootServices.
    /// Uses a static buffer to avoid heap allocation.
    /// </summary>
    /// <param name="buffer">Buffer to receive memory descriptors</param>
    /// <param name="bufferSize">Size of buffer in bytes</param>
    /// <param name="mapKey">Receives the map key needed for ExitBootServices</param>
    /// <param name="descriptorSize">Receives the actual size of each descriptor</param>
    /// <param name="entryCount">Receives the number of entries in the map</param>
    /// <returns>EFI status code</returns>
    public static EfiStatus GetMemoryMap(
        byte* buffer,
        ulong bufferSize,
        out ulong mapKey,
        out ulong descriptorSize,
        out int entryCount)
    {
        mapKey = 0;
        descriptorSize = 0;
        entryCount = 0;

        var bs = BootServices;
        if (bs == null)
            return EfiStatus.InvalidParameter;

        ulong mapSize = bufferSize;
        ulong localMapKey = 0;
        ulong localDescSize = 0;
        uint descriptorVersion = 0;

        var status = bs->GetMemoryMap(
            &mapSize,
            (EfiMemoryDescriptor*)buffer,
            &localMapKey,
            &localDescSize,
            &descriptorVersion);

        mapKey = localMapKey;
        descriptorSize = localDescSize;

        if (status == EfiStatus.Success)
        {
            entryCount = (int)(mapSize / localDescSize);
        }

        return status;
    }

    /// <summary>
    /// Get a memory descriptor from a raw buffer
    /// </summary>
    public static EfiMemoryDescriptor* GetDescriptor(byte* buffer, ulong descriptorSize, int index)
    {
        return (EfiMemoryDescriptor*)(buffer + (ulong)index * descriptorSize);
    }
}
