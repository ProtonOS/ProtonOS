// netos mernel - UEFI type definitions and boot services
// Provides access to UEFI memory map and boot services for kernel initialization.

using System;
using System.Runtime.InteropServices;

namespace Kernel.Platform;

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
// UEFI GUIDs
// ============================================================================

/// <summary>
/// UEFI GUID structure (128-bit identifier)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct EfiGuid
{
    public uint Data1;
    public ushort Data2;
    public ushort Data3;
    public unsafe fixed byte Data4[8];

    /// <summary>
    /// EFI_LOADED_IMAGE_PROTOCOL_GUID
    /// {5B1B31A1-9562-11D2-8E3F-00A0C969723B}
    /// </summary>
    public static EfiGuid LoadedImageProtocol => new EfiGuid
    {
        Data1 = 0x5B1B31A1,
        Data2 = 0x9562,
        Data3 = 0x11D2,
    };

    public static unsafe void InitLoadedImageGuid(EfiGuid* guid)
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

/// <summary>
/// EFI_LOADED_IMAGE_PROTOCOL - Information about a loaded image
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EfiLoadedImageProtocol
{
    public uint Revision;
    public void* ParentHandle;
    public EfiSystemTable* SystemTable;

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
    public EfiMemoryType ImageCodeType;
    public EfiMemoryType ImageDataType;
    public void* Unload;            // Unload function pointer
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
        EfiGuid*,            // Protocol GUID
        void**,              // Interface (out)
        EfiStatus> HandleProtocol;

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

    // Cached loaded image info
    private static ulong _imageBase;
    private static ulong _imageSize;
    private static bool _imageInfoLoaded;

    /// <summary>
    /// Get the base address where the kernel image is loaded.
    /// Must be called before ExitBootServices.
    /// </summary>
    public static ulong ImageBase
    {
        get
        {
            if (!_imageInfoLoaded)
                LoadImageInfo();
            return _imageBase;
        }
    }

    /// <summary>
    /// Get the size of the kernel image in bytes.
    /// Must be called before ExitBootServices.
    /// </summary>
    public static ulong ImageSize
    {
        get
        {
            if (!_imageInfoLoaded)
                LoadImageInfo();
            return _imageSize;
        }
    }

    /// <summary>
    /// Load image information from EFI_LOADED_IMAGE_PROTOCOL.
    /// This uses HandleProtocol to get the loaded image protocol for our image handle.
    /// </summary>
    private static void LoadImageInfo()
    {
        if (_imageInfoLoaded)
            return;

        _imageInfoLoaded = true;  // Mark as loaded even if we fail, to avoid retry

        var bs = BootServices;
        if (bs == null)
        {
            DebugConsole.WriteLine("[UEFI] Cannot get image info: boot services unavailable");
            return;
        }

        var handle = ImageHandle;
        if (handle == null)
        {
            DebugConsole.WriteLine("[UEFI] Cannot get image info: no image handle");
            return;
        }

        // Set up the GUID for EFI_LOADED_IMAGE_PROTOCOL
        EfiGuid guid;
        EfiGuid.InitLoadedImageGuid(&guid);

        // Call HandleProtocol to get the loaded image protocol
        EfiLoadedImageProtocol* loadedImage = null;
        var status = bs->HandleProtocol(handle, &guid, (void**)&loadedImage);

        if (status != EfiStatus.Success || loadedImage == null)
        {
            DebugConsole.Write("[UEFI] HandleProtocol failed: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return;
        }

        // Extract image base and size
        _imageBase = (ulong)loadedImage->ImageBase;
        _imageSize = loadedImage->ImageSize;

        DebugConsole.Write("[UEFI] Image loaded at 0x");
        DebugConsole.WriteHex(_imageBase);
        DebugConsole.Write(", size 0x");
        DebugConsole.WriteHex(_imageSize);
        DebugConsole.WriteLine();
    }

    // Static buffer for ExitBootServices memory map (needs fresh map key)
    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct ExitMemoryMapBuffer
    {
        public fixed byte Data[8192];
    }
    private static ExitMemoryMapBuffer _exitMemMapBuffer;

    /// <summary>
    /// Exit boot services - after this, UEFI boot services are no longer available.
    /// This function gets a fresh memory map and calls ExitBootServices with the map key.
    /// If the memory map changes between GetMemoryMap and ExitBootServices, it retries.
    ///
    /// IMPORTANT: No UEFI calls (including DebugConsole which uses ConOut) can be made
    /// between GetMemoryMap and ExitBootServices, or the map key becomes invalid.
    /// </summary>
    public static bool ExitBootServices()
    {
        if (_bootServicesExited)
            return true;

        var st = SystemTable;
        if (st == null || st->BootServices == null)
            return false;

        DebugConsole.WriteLine("[UEFI] Exiting boot services...");

        // ExitBootServices requires a fresh map key. If the memory map changes
        // between GetMemoryMap and ExitBootServices (rare), we retry.
        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            fixed (byte* buffer = _exitMemMapBuffer.Data)
            {
                ulong mapSize = 8192;
                ulong mapKey = 0;
                ulong descSize = 0;
                uint descVersion = 0;

                var status = st->BootServices->GetMemoryMap(
                    &mapSize,
                    (EfiMemoryDescriptor*)buffer,
                    &mapKey,
                    &descSize,
                    &descVersion);

                if (status != EfiStatus.Success)
                {
                    DebugConsole.Write("[UEFI] GetMemoryMap failed: 0x");
                    DebugConsole.WriteHex((ulong)status);
                    DebugConsole.WriteLine();
                    continue;
                }

                status = st->BootServices->ExitBootServices(ImageHandle, mapKey);
                if (status == EfiStatus.Success)
                {
                    _bootServicesExited = true;
                    DebugConsole.WriteLine("[UEFI] Boot services exited");
                    return true;
                }

                DebugConsole.Write("[UEFI] ExitBootServices attempt ");
                DebugConsole.WriteHex((ushort)(attempt + 1));
                DebugConsole.Write(" failed: 0x");
                DebugConsole.WriteHex((ulong)status);
                DebugConsole.WriteLine();
            }
        }

        DebugConsole.WriteLine("[UEFI] ExitBootServices failed after all retries");
        return false;
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
