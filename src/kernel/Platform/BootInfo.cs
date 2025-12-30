// BootInfo.cs - Platform-agnostic boot information
// This structure is passed from the bootloader to the kernel.
// The kernel should not need to know whether it was booted via UEFI, BIOS, etc.

using System;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

/// <summary>
/// Magic value for BootInfo validation ("PROTONOS" in ASCII)
/// </summary>
public static class BootInfoConstants
{
    public const ulong Magic = 0x50524F544F4E4F53; // "PROTONOS"
    public const uint Version = 2;  // Version 2: bootloader handles ExitBootServices and file loading
}

/// <summary>
/// Static accessor for the BootInfo structure passed by the bootloader
/// </summary>
public static unsafe class BootInfoAccess
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    [RuntimeImport("*", "get_boot_info")]
    private static extern BootInfo* GetBootInfoPtr();

    private static BootInfo* _cached;

    /// <summary>
    /// Get the BootInfo pointer passed by the bootloader
    /// </summary>
    public static BootInfo* Get()
    {
        if (_cached == null)
            _cached = GetBootInfoPtr();
        return _cached;
    }

    /// <summary>
    /// Check if BootInfo is available and valid
    /// </summary>
    public static bool IsValid
    {
        get
        {
            var info = Get();
            return info != null && info->IsValid;
        }
    }

    /// <summary>
    /// Find a loaded file by name (case-insensitive, matches just filename without path)
    /// </summary>
    /// <param name="name">Filename to search for (can include path, path is stripped)</param>
    /// <param name="size">Output: file size in bytes</param>
    /// <returns>Pointer to file data, or null if not found</returns>
    public static byte* FindFile(string name, out ulong size)
    {
        size = 0;
        var info = Get();
        if (info == null || info->LoadedFilesAddress == 0 || info->LoadedFilesCount == 0)
            return null;

        // Strip path from name (get just the filename)
        int lastSlash = -1;
        for (int i = 0; i < name.Length; i++)
        {
            if (name[i] == '/' || name[i] == '\\')
                lastSlash = i;
        }
        int nameStart = lastSlash + 1;
        int nameLen = name.Length - nameStart;

        var files = (LoadedFile*)info->LoadedFilesAddress;
        for (uint i = 0; i < info->LoadedFilesCount; i++)
        {
            var file = &files[i];
            byte* fileNamePtr = file->GetNameBytes();
            int fileNameLen = file->NameLength;

            // Compare filenames (case-insensitive)
            if (fileNameLen == nameLen)
            {
                bool match = true;
                for (int j = 0; j < nameLen; j++)
                {
                    char c1 = (char)fileNamePtr[j];
                    char c2 = name[nameStart + j];

                    // Simple case-insensitive compare for ASCII
                    if (c1 >= 'A' && c1 <= 'Z') c1 = (char)(c1 + 32);
                    if (c2 >= 'A' && c2 <= 'Z') c2 = (char)(c2 + 32);

                    if (c1 != c2)
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    size = file->Size;
                    return (byte*)file->PhysicalAddress;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get the loaded files array
    /// </summary>
    public static LoadedFile* GetLoadedFiles(out uint count)
    {
        var info = Get();
        if (info == null || info->LoadedFilesAddress == 0)
        {
            count = 0;
            return null;
        }
        count = info->LoadedFilesCount;
        return (LoadedFile*)info->LoadedFilesAddress;
    }
}

/// <summary>
/// Boot information passed from bootloader to kernel.
/// This structure is platform-agnostic - the same structure is used
/// regardless of whether the kernel was booted via UEFI, BIOS, or other means.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct BootInfo
{
    public ulong Magic;              // Must be BootInfoConstants.Magic
    public uint Version;             // BootInfoConstants.Version
    public uint Flags;               // BootInfoFlags

    // Memory information
    public ulong MemoryMapAddress;   // Physical address of MemoryMapEntry array
    public uint MemoryMapEntries;    // Number of entries
    public uint MemoryMapEntrySize;  // Size of each entry (for versioning)

    // Kernel location
    public ulong KernelPhysicalBase; // Where kernel was loaded
    public ulong KernelVirtualBase;  // Requested virtual base (from PE header)
    public ulong KernelSize;         // Total size in memory
    public ulong KernelEntryOffset;  // Offset from base to entry point

    // Loaded files
    public ulong LoadedFilesAddress; // Physical address of LoadedFile array
    public uint LoadedFilesCount;    // Number of loaded files
    public uint Reserved1;

    // ACPI
    public ulong AcpiRsdp;           // Physical address of RSDP (v1 or v2)

    // Framebuffer (optional)
    public ulong FramebufferAddress; // Physical address, 0 if none
    public uint FramebufferWidth;
    public uint FramebufferHeight;
    public uint FramebufferPitch;    // Bytes per row
    public uint FramebufferBpp;      // Bits per pixel

    // Debug
    public ulong SerialPort;         // I/O port for debug serial (0x3F8 typically)

    // Reserved for future expansion
    public fixed ulong Reserved[8];

    /// <summary>
    /// Validate the BootInfo structure
    /// </summary>
    public bool IsValid => Magic == BootInfoConstants.Magic && Version >= 1;

    /// <summary>
    /// Get the memory map as a span
    /// </summary>
    public ReadOnlySpan<MemoryMapEntry> GetMemoryMap()
    {
        if (MemoryMapAddress == 0 || MemoryMapEntries == 0)
            return default;
        return new ReadOnlySpan<MemoryMapEntry>((void*)MemoryMapAddress, (int)MemoryMapEntries);
    }

    /// <summary>
    /// Get loaded files as a span
    /// </summary>
    public ReadOnlySpan<LoadedFile> GetLoadedFiles()
    {
        if (LoadedFilesAddress == 0 || LoadedFilesCount == 0)
            return default;
        return new ReadOnlySpan<LoadedFile>((void*)LoadedFilesAddress, (int)LoadedFilesCount);
    }
}

[Flags]
public enum BootInfoFlags : uint
{
    None = 0,
    HasFramebuffer = 0x01,
    HasAcpi = 0x02,
    HasSerial = 0x04,
}

/// <summary>
/// Memory map entry describing a physical memory region
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MemoryMapEntry
{
    public ulong PhysicalStart;
    public ulong PhysicalEnd;        // Exclusive (start + size)
    public MemoryType Type;
    public MemoryFlags Flags;

    public ulong Size => PhysicalEnd - PhysicalStart;
}

/// <summary>
/// Memory region type
/// </summary>
public enum MemoryType : uint
{
    Available = 0,         // Free for use
    Reserved = 1,          // Hardware reserved
    AcpiReclaimable = 2,   // ACPI tables, can free after parsing
    AcpiNvs = 3,           // ACPI non-volatile storage
    Kernel = 4,            // Kernel image
    LoadedFile = 5,        // Boot-loaded file
    BootInfo = 6,          // BootInfo and related structures
    PageTables = 7,        // Initial page tables
    Stack = 8,             // Initial kernel stack
}

/// <summary>
/// Memory region flags
/// </summary>
[Flags]
public enum MemoryFlags : uint
{
    None = 0,
    WriteBack = 0x01,      // Write-back cacheable
    WriteThrough = 0x02,   // Write-through cacheable
    Uncached = 0x04,       // Uncacheable
    WriteProtect = 0x08,   // Write-protected
    Runtime = 0x10,        // Needed at runtime (don't reclaim)
}

/// <summary>
/// Information about a file loaded by the bootloader
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct LoadedFile
{
    public ulong PhysicalAddress;    // Where file is loaded
    public ulong Size;               // File size in bytes
    public fixed byte NameBytes[64]; // Null-terminated filename
    public LoadedFileFlags Flags;
    public uint Reserved;

    /// <summary>
    /// Get the filename length (use GetNameBytes for raw access)
    /// </summary>
    public int NameLength
    {
        get
        {
            fixed (byte* p = NameBytes)
            {
                int len = 0;
                while (len < 64 && p[len] != 0) len++;
                return len;
            }
        }
    }

    /// <summary>
    /// Get a pointer to the name bytes
    /// </summary>
    public byte* GetNameBytes()
    {
        fixed (byte* p = NameBytes)
        {
            return p;
        }
    }

    /// <summary>
    /// Get the file contents as a span
    /// </summary>
    public ReadOnlySpan<byte> GetContents()
    {
        if (PhysicalAddress == 0 || Size == 0)
            return default;
        return new ReadOnlySpan<byte>((void*)PhysicalAddress, (int)Size);
    }
}

/// <summary>
/// Flags for loaded files
/// </summary>
[Flags]
public enum LoadedFileFlags : uint
{
    None = 0,
    Executable = 0x01,     // PE/COFF executable
    Driver = 0x02,         // Driver DLL
    Data = 0x04,           // Data file
}
