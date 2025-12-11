// ProtonOS kernel - UEFI File System support
// Provides file loading from the boot device before ExitBootServices.

using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

/// <summary>
/// Provides access to files on the UEFI boot device.
/// Must be used before ExitBootServices is called.
/// </summary>
public static unsafe class UEFIFS
{
    private static EFIFileProtocol* _rootDir;
    private static bool _initialized;

    /// <summary>
    /// Initialize the file system by opening the boot device's root directory.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return _rootDir != null;

        _initialized = true;

        var bs = UEFIBoot.BootServices;
        if (bs == null)
        {
            DebugConsole.WriteLine("[FS] Boot services not available");
            return false;
        }

        // Get the loaded image protocol to find our boot device
        var imageHandle = UEFIBoot.ImageHandle;
        if (imageHandle == null)
        {
            DebugConsole.WriteLine("[FS] No image handle");
            return false;
        }

        EFIGUID loadedImageGuid;
        EFIGUID.InitLoadedImageGuid(&loadedImageGuid);

        EFILoadedImageProtocol* loadedImage = null;
        var status = bs->HandleProtocol(imageHandle, &loadedImageGuid, (void**)&loadedImage);
        if (status != EFIStatus.Success || loadedImage == null)
        {
            DebugConsole.Write("[FS] Failed to get loaded image: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return false;
        }

        // Get the SimpleFileSystem protocol from the device we booted from
        var deviceHandle = loadedImage->DeviceHandle;
        if (deviceHandle == null)
        {
            DebugConsole.WriteLine("[FS] No device handle on loaded image");
            return false;
        }

        EFIGUID fsGuid;
        EFIGUID.InitSimpleFileSystemGuid(&fsGuid);

        EFISimpleFileSystemProtocol* fs = null;
        status = bs->HandleProtocol(deviceHandle, &fsGuid, (void**)&fs);
        if (status != EFIStatus.Success || fs == null)
        {
            DebugConsole.Write("[FS] Failed to get SimpleFileSystem: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return false;
        }

        // Open the root directory
        EFIFileProtocol* root = null;
        status = fs->OpenVolume(fs, &root);
        if (status != EFIStatus.Success || root == null)
        {
            DebugConsole.Write("[FS] Failed to open root: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return false;
        }

        _rootDir = root;
        DebugConsole.WriteLine("[FS] File system initialized");
        return true;
    }

    /// <summary>
    /// Read a file from the boot device into memory.
    /// The caller is responsible for the returned memory (allocated with UEFI AllocatePool).
    /// </summary>
    /// <param name="path">File path (UTF-16, backslash-separated, e.g. "\\MetadataTest.dll")</param>
    /// <param name="fileSize">Receives the file size in bytes</param>
    /// <returns>Pointer to file data, or null on failure</returns>
    public static byte* ReadFile(char* path, out ulong fileSize)
    {
        fileSize = 0;

        if (_rootDir == null)
        {
            if (!Init())
                return null;
        }

        var bs = UEFIBoot.BootServices;
        if (bs == null)
        {
            DebugConsole.WriteLine("[FS] Boot services not available");
            return null;
        }

        // Open the file
        EFIFileProtocol* file = null;
        var status = _rootDir->Open(_rootDir, &file, path, EFIFileMode.Read, 0);
        if (status != EFIStatus.Success || file == null)
        {
            DebugConsole.Write("[FS] Failed to open file: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            return null;
        }

        // Get file info to determine size
        // First call with size 0 to get required buffer size
        EFIGUID fileInfoGuid;
        EFIGUID.InitFileInfoGuid(&fileInfoGuid);

        ulong infoSize = 0;
        status = file->GetInfo(file, &fileInfoGuid, &infoSize, null);
        // Expected: BufferTooSmall, with infoSize set to required size

        if (infoSize == 0)
        {
            DebugConsole.WriteLine("[FS] GetInfo returned zero size");
            file->Close(file);
            return null;
        }

        // Allocate buffer for file info (on stack if small enough)
        byte* infoBuffer = stackalloc byte[(int)infoSize];
        status = file->GetInfo(file, &fileInfoGuid, &infoSize, infoBuffer);
        if (status != EFIStatus.Success)
        {
            DebugConsole.Write("[FS] GetInfo failed: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            file->Close(file);
            return null;
        }

        var fileInfo = (EFIFileInfo*)infoBuffer;
        fileSize = fileInfo->FileSize;

        // DebugConsole.Write("[FS] File size: ");
        // DebugConsole.WriteHex(fileSize);
        // DebugConsole.Write(" bytes");
        // DebugConsole.WriteLine();

        if (fileSize == 0)
        {
            file->Close(file);
            return null;
        }

        // Allocate memory for file content using UEFI pool
        void* buffer = null;
        status = bs->AllocatePool(EFIMemoryType.LoaderData, fileSize, &buffer);
        if (status != EFIStatus.Success || buffer == null)
        {
            DebugConsole.Write("[FS] AllocatePool failed: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            file->Close(file);
            return null;
        }

        // Read the file
        ulong bytesRead = fileSize;
        status = file->Read(file, &bytesRead, buffer);
        if (status != EFIStatus.Success)
        {
            DebugConsole.Write("[FS] Read failed: 0x");
            DebugConsole.WriteHex((ulong)status);
            DebugConsole.WriteLine();
            bs->FreePool(buffer);
            file->Close(file);
            return null;
        }

        // DebugConsole.Write("[FS] Read ");
        // DebugConsole.WriteHex(bytesRead);
        // DebugConsole.Write(" bytes");
        // DebugConsole.WriteLine();

        // Close the file
        file->Close(file);

        return (byte*)buffer;
    }

    /// <summary>
    /// Helper to read a file using a simple ASCII path (converted to UTF-16 internally)
    /// </summary>
    public static byte* ReadFileAscii(string path, out ulong fileSize)
    {
        // Convert ASCII path to UTF-16 on stack
        // Add 1 for null terminator
        char* utf16Path = stackalloc char[path.Length + 1];
        for (int i = 0; i < path.Length; i++)
        {
            utf16Path[i] = path[i];
        }
        utf16Path[path.Length] = '\0';

        return ReadFile(utf16Path, out fileSize);
    }
}
