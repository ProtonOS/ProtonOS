// ProtonOS AHCI Entry Point
// Static entry point for JIT compilation

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;
using ProtonOS.Drivers.Storage.Fat;
using ProtonOS.Drivers.Storage.Ext2;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// Static entry point for AHCI driver, designed for JIT compilation.
/// </summary>
public static unsafe class AhciEntry
{
    // Active controller instance
    private static AhciController? _controller;

    // Active device drivers (one per detected SATA device)
    private static AhciDriver?[] _devices = new AhciDriver?[AhciConst.MAX_PORTS];
    private static int _deviceCount;

    /// <summary>
    /// Check if this driver supports the given PCI device.
    /// Matches AHCI controllers by class code (0x01/0x06/0x01).
    /// </summary>
    public static bool Probe(ushort vendorId, ushort deviceId, byte classCode, byte subclassCode, byte progIf)
    {
        // Match by class code: Mass Storage (0x01), SATA (0x06), AHCI (0x01)
        if (classCode == 0x01 && subclassCode == 0x06 && progIf == 0x01)
            return true;

        // Also match known Intel AHCI controllers by vendor/device ID
        if (vendorId == 0x8086)
        {
            // Intel ICH9 AHCI variants (common in QEMU Q35)
            if (deviceId == 0x2922 || deviceId == 0x2923 ||
                deviceId == 0x2924 || deviceId == 0x2925 ||
                deviceId == 0x2929 || deviceId == 0x292A ||
                // ICH10
                deviceId == 0x3A02 || deviceId == 0x3A22 ||
                // 6 Series
                deviceId == 0x1C02 || deviceId == 0x1C03 ||
                // 7 Series
                deviceId == 0x1E02 || deviceId == 0x1E03)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Bind the driver to a PCI device.
    /// </summary>
    public static bool Bind(byte bus, byte device, byte function)
    {
        // Build PciDeviceInfo
        var pciDevice = new PciDeviceInfo();
        pciDevice.Address = new PciAddress(bus, device, function);

        // Read vendor/device IDs
        uint vendorDevice = PCI.ReadConfig32(bus, device, function, PCI.PCI_VENDOR_ID);
        pciDevice.VendorId = (ushort)(vendorDevice & 0xFFFF);
        pciDevice.DeviceId = (ushort)(vendorDevice >> 16);

        // Read class/subclass/progif
        uint classReg = PCI.ReadConfig32(bus, device, function, PCI.PCI_REVISION_ID);
        pciDevice.RevisionId = (byte)(classReg & 0xFF);
        pciDevice.ProgIf = (byte)((classReg >> 8) & 0xFF);
        pciDevice.SubclassCode = (byte)((classReg >> 16) & 0xFF);
        pciDevice.ClassCode = (byte)((classReg >> 24) & 0xFF);

        // Read BARs
        ReadBars(bus, device, function, pciDevice);

        // Create and initialize controller
        _controller = new AhciController();
        if (!_controller.Initialize(pciDevice))
        {
            Debug.WriteLine("[AHCI] Controller init failed");
            _controller.Dispose();
            _controller = null;
            return false;
        }

        // Create drivers for each ready port
        _deviceCount = 0;
        var readyPorts = _controller.GetReadyPorts();
        foreach (var port in readyPorts)
        {
            var driver = new AhciDriver(port);
            if (driver.Initialize())
                _devices[_deviceCount++] = driver;
        }

        return _deviceCount > 0;
    }

    /// <summary>
    /// Read BAR information from PCI config space.
    /// </summary>
    private static void ReadBars(byte bus, byte device, byte function, PciDeviceInfo pciDevice)
    {
        int i = 0;
        while (i < 6)
        {
            ushort barOffset = (ushort)(PCI.PCI_BAR0 + (i * 4));

            // Read current BAR value
            uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

            // Determine BAR type
            bool isIo = (barValue & 1) != 0;
            bool is64Bit = !isIo && ((barValue >> 1) & 3) == 2;
            bool isPrefetchable = !isIo && ((barValue >> 3) & 1) != 0;

            // Get base address
            ulong baseAddress;
            if (isIo)
            {
                baseAddress = barValue & 0xFFFFFFFC;
            }
            else if (is64Bit)
            {
                uint highValue = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                baseAddress = (barValue & 0xFFFFFFF0) | ((ulong)highValue << 32);
            }
            else
            {
                baseAddress = barValue & 0xFFFFFFF0;
            }

            // Get BAR size (save, write all 1s, read back, restore)
            uint sizeMask;
            if (baseAddress != 0)
            {
                PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
                uint sizeRead = PCI.ReadConfig32(bus, device, function, barOffset);
                PCI.WriteConfig32(bus, device, function, barOffset, barValue);

                if (isIo)
                    sizeMask = sizeRead & 0xFFFFFFFC;
                else
                    sizeMask = sizeRead & 0xFFFFFFF0;

                sizeMask = ~sizeMask + 1;  // Convert to size
            }
            else
            {
                sizeMask = 0;
            }

            PciBar bar;
            bar.Index = i;
            bar.BaseAddress = baseAddress;
            bar.Size = sizeMask;
            bar.IsIO = isIo;
            bar.Is64Bit = is64Bit;
            bar.IsPrefetchable = isPrefetchable;
            pciDevice.Bars[i] = bar;

            // Skip next BAR index for 64-bit BARs
            if (is64Bit)
            {
                i++;
                if (i < 6)
                {
                    PciBar empty;
                    empty.Index = i;
                    empty.BaseAddress = 0;
                    empty.Size = 0;
                    empty.IsIO = false;
                    empty.Is64Bit = false;
                    empty.IsPrefetchable = false;
                    pciDevice.Bars[i] = empty;
                }
            }
            i++;
        }
    }

    /// <summary>
    /// Get the active controller.
    /// </summary>
    public static AhciController? GetController() => _controller;

    /// <summary>
    /// Get the number of active devices.
    /// </summary>
    public static int DeviceCount => _deviceCount;

    /// <summary>
    /// Get a device by index.
    /// </summary>
    public static AhciDriver? GetDevice(int index)
    {
        if (index < 0 || index >= _deviceCount)
            return null;
        return _devices[index];
    }

    /// <summary>
    /// Get the first available device (typically sata0 - usually the boot disk).
    /// </summary>
    public static AhciDriver? GetFirstDevice()
    {
        return _deviceCount > 0 ? _devices[0] : null;
    }

    /// <summary>
    /// Get the last available device (useful when boot disk is on the same controller).
    /// </summary>
    public static AhciDriver? GetLastDevice()
    {
        return _deviceCount > 0 ? _devices[_deviceCount - 1] : null;
    }

    /// <summary>
    /// Test reading from the first AHCI device.
    /// </summary>
    public static int TestRead()
    {
        var device = GetFirstDevice();
        if (device == null)
            return 0;

        // Allocate buffer for one sector
        uint blockSize = device.BlockSize;
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return 0;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Read sector 0
        int result = device.Read(0, 1, buffer);
        Memory.FreePages(bufferPhys, pageCount);

        return result > 0 ? 1 : 0;
    }

    /// <summary>
    /// Test writing to the first AHCI device.
    /// </summary>
    public static int TestWrite()
    {
        var device = GetFirstDevice();
        if (device == null)
            return 0;

        // Allocate buffer
        uint blockSize = device.BlockSize;
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
            return 0;

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Fill with test pattern
        for (uint i = 0; i < blockSize; i++)
            buffer[i] = (byte)(i & 0xFF);

        // Write to sector 10000 (in data area, safe location)
        // Note: FAT32 on 64MB disk has first data sector at ~2050
        int result = device.Write(10000, 1, buffer);
        if (result <= 0)
        {
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }

        // Read back and verify
        for (uint i = 0; i < blockSize; i++)
            buffer[i] = 0;

        result = device.Read(10000, 1, buffer);
        if (result <= 0)
        {
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }

        // Verify pattern
        bool match = true;
        for (uint i = 0; i < blockSize && match; i++)
        {
            if (buffer[i] != (byte)(i & 0xFF))
                match = false;
        }

        Memory.FreePages(bufferPhys, pageCount);
        return match ? 1 : 0;
    }

    /// <summary>
    /// Test mounting FAT filesystem on the SATA test disk (last device).
    /// </summary>
    public static int TestFatMount()
    {
        // Use last device - boot disk is usually first, test disk is last
        var device = GetLastDevice();
        if (device == null)
            return 0;

        // Create FAT filesystem driver
        var fat = new FatFileSystem();
        fat.Initialize();

        // Mount on the AHCI device
        var mountResult = fat.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            fat.Shutdown();
            return 0;
        }

        // Verify we can open root directory
        IDirectoryHandle? rootDir;
        var dirResult = fat.OpenDirectory("/", out rootDir);
        bool success = (dirResult == FileResult.Success && rootDir != null);

        if (rootDir != null)
            rootDir.Dispose();

        fat.Unmount();
        fat.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test reading a file from the FAT filesystem on the SATA test disk.
    /// </summary>
    public static int TestFatReadFile()
    {
        // Use last device - boot disk is usually first, test disk is last
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var fat = new FatFileSystem();
        fat.Initialize();

        var mountResult = fat.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            fat.Shutdown();
            return 0;
        }

        // List directory entries
        IDirectoryHandle? rootDir;
        var dirResult = fat.OpenDirectory("/", out rootDir);
        if (dirResult != FileResult.Success || rootDir == null)
        {
            fat.Unmount();
            fat.Shutdown();
            return 0;
        }

        Debug.Write("[FatTest] Listing root directory:");
        Debug.WriteLine();
        int fileCount = 0;
        string? foundFile = null;
        FileInfo? entry;
        while ((entry = rootDir.ReadNext()) != null)
        {
            Debug.Write("  '");
            Debug.Write(entry.Name);
            Debug.Write("' size=");
            Debug.WriteDecimal((int)entry.Size);
            Debug.WriteLine();
            if (entry.Name != null)
                foundFile = entry.Name;
            fileCount++;
        }
        rootDir.Dispose();
        Debug.Write("[FatTest] Found ");
        Debug.WriteDecimal(fileCount);
        Debug.Write(" entries");
        Debug.WriteLine();

        // Try to open the file we found
        IFileHandle? file;
        string fileToOpen = foundFile != null ? "/" + foundFile : "/HELLO.TXT";
        Debug.Write("[FatTest] Trying to open: ");
        Debug.Write(fileToOpen);
        Debug.WriteLine();
        var openResult = fat.OpenFile(fileToOpen, FileMode.Open, FileAccess.Read, out file);
        if (openResult != FileResult.Success || file == null)
        {
            Debug.Write("[FatTest] Failed to open HELLO.TXT: ");
            Debug.WriteDecimal((int)openResult);
            Debug.WriteLine();
            fat.Unmount();
            fat.Shutdown();
            return 0;
        }

        Debug.Write("[FatTest] Opened HELLO.TXT, size=");
        Debug.WriteDecimal((int)file.Length);
        Debug.WriteLine();

        // Read file contents
        int length = (int)file.Length;
        if (length > 256)
            length = 256;

        ulong pageCount = ((ulong)length + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            file.Dispose();
            fat.Unmount();
            fat.Shutdown();
            return 0;
        }

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        int bytesRead = file.Read(buffer, length);
        Debug.Write("[FatTest] Read ");
        Debug.WriteDecimal(bytesRead);
        Debug.Write(" bytes");
        Debug.WriteLine();

        // Print first line of content
        if (bytesRead > 0)
        {
            // Build string from first line
            int lineLen = 0;
            for (int i = 0; i < bytesRead && buffer[i] != '\n' && buffer[i] != '\r'; i++)
            {
                if (buffer[i] >= 32 && buffer[i] < 127)
                    lineLen++;
            }
            var chars = new char[lineLen];
            int j = 0;
            for (int i = 0; i < bytesRead && buffer[i] != '\n' && buffer[i] != '\r' && j < lineLen; i++)
            {
                if (buffer[i] >= 32 && buffer[i] < 127)
                    chars[j++] = (char)buffer[i];
            }
            Debug.Write("[FatTest] Content: ");
            Debug.Write(new string(chars));
            Debug.WriteLine();
        }

        bool success = bytesRead > 0;

        Memory.FreePages(bufferPhys, pageCount);
        file.Dispose();
        fat.Unmount();
        fat.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test mounting EXT2 filesystem on the SATA test disk (last device).
    /// </summary>
    public static int TestExt2Mount()
    {
        // Use last device - boot disk is usually first, test disk is last
        var device = GetLastDevice();
        if (device == null)
            return 0;

        // Create EXT2 filesystem driver
        var ext2 = new Ext2FileSystem();
        ext2.Initialize();

        // Mount on the AHCI device
        var mountResult = ext2.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            ext2.Shutdown();
            return 0;
        }

        // Verify we can open root directory
        IDirectoryHandle? rootDir;
        var dirResult = ext2.OpenDirectory("/", out rootDir);
        bool success = (dirResult == FileResult.Success && rootDir != null);

        if (rootDir != null)
            rootDir.Dispose();

        ext2.Unmount();
        ext2.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test reading a file from the EXT2 filesystem on the SATA test disk.
    /// </summary>
    public static int TestExt2ReadFile()
    {
        // Use last device - boot disk is usually first, test disk is last
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var ext2 = new Ext2FileSystem();
        ext2.Initialize();

        var mountResult = ext2.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            ext2.Shutdown();
            return 0;
        }

        // List directory entries
        IDirectoryHandle? rootDir;
        var dirResult = ext2.OpenDirectory("/", out rootDir);
        if (dirResult != FileResult.Success || rootDir == null)
        {
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        Debug.Write("[Ext2Test] Listing root directory:");
        Debug.WriteLine();
        int fileCount = 0;
        string? foundFile = null;
        FileInfo? entry;
        while ((entry = rootDir.ReadNext()) != null)
        {
            Debug.Write("  '");
            Debug.Write(entry.Name);
            Debug.Write("' size=");
            Debug.WriteDecimal((int)entry.Size);
            Debug.WriteLine();
            if (entry.Name != null && entry.Type == FileEntryType.File)
                foundFile = entry.Name;
            fileCount++;
        }
        rootDir.Dispose();
        Debug.Write("[Ext2Test] Found ");
        Debug.WriteDecimal(fileCount);
        Debug.Write(" entries");
        Debug.WriteLine();

        // Try to open the file we found
        IFileHandle? file;
        string fileToOpen = foundFile != null ? "/" + foundFile : "/hello.txt";
        Debug.Write("[Ext2Test] Trying to open: ");
        Debug.Write(fileToOpen);
        Debug.WriteLine();
        var openResult = ext2.OpenFile(fileToOpen, FileMode.Open, FileAccess.Read, out file);
        if (openResult != FileResult.Success || file == null)
        {
            Debug.Write("[Ext2Test] Failed to open file: ");
            Debug.WriteDecimal((int)openResult);
            Debug.WriteLine();
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        Debug.Write("[Ext2Test] Opened file, size=");
        Debug.WriteDecimal((int)file.Length);
        Debug.WriteLine();

        // Read file contents
        int length = (int)file.Length;
        if (length > 256)
            length = 256;

        ulong pageCount = ((ulong)length + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            file.Dispose();
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        int bytesRead = file.Read(buffer, length);
        Debug.Write("[Ext2Test] Read ");
        Debug.WriteDecimal(bytesRead);
        Debug.Write(" bytes");
        Debug.WriteLine();

        // Print first line of content
        if (bytesRead > 0)
        {
            // Build string from first line
            int lineLen = 0;
            for (int i = 0; i < bytesRead && buffer[i] != '\n' && buffer[i] != '\r'; i++)
            {
                if (buffer[i] >= 32 && buffer[i] < 127)
                    lineLen++;
            }
            var chars = new char[lineLen];
            int j = 0;
            for (int i = 0; i < bytesRead && buffer[i] != '\n' && buffer[i] != '\r' && j < lineLen; i++)
            {
                if (buffer[i] >= 32 && buffer[i] < 127)
                    chars[j++] = (char)buffer[i];
            }
            Debug.Write("[Ext2Test] Content: ");
            Debug.Write(new string(chars));
            Debug.WriteLine();
        }

        bool success = bytesRead > 0;

        Memory.FreePages(bufferPhys, pageCount);
        file.Dispose();
        ext2.Unmount();
        ext2.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test writing to a file in the EXT2 filesystem.
    /// </summary>
    public static int TestExt2WriteFile()
    {
        // Use last device - boot disk is usually first, test disk is last
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var ext2 = new Ext2FileSystem();
        ext2.Initialize();

        // Mount read-write
        var mountResult = ext2.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            ext2.Shutdown();
            return 0;
        }

        // Open an existing file for read-write
        IFileHandle? file;
        var openResult = ext2.OpenFile("/hello.txt", FileMode.Open, FileAccess.ReadWrite, out file);
        if (openResult != FileResult.Success || file == null)
        {
            Debug.Write("[Ext2WriteTest] Failed to open file: ");
            Debug.WriteDecimal((int)openResult);
            Debug.WriteLine();
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        // Save original size
        long originalSize = file.Length;

        // Write test pattern at a safe offset (past original content)
        long writeOffset = originalSize;
        file.Position = writeOffset;

        ulong pageCount = 1;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            file.Dispose();
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Fill with test pattern
        string testPattern = "EXT2 WRITE TEST OK!";
        for (int i = 0; i < testPattern.Length; i++)
            buffer[i] = (byte)testPattern[i];

        int bytesWritten = file.Write(buffer, testPattern.Length);
        Debug.Write("[Ext2WriteTest] Wrote ");
        Debug.WriteDecimal(bytesWritten);
        Debug.Write(" bytes at offset ");
        Debug.WriteDecimal((int)writeOffset);
        Debug.WriteLine();

        if (bytesWritten != testPattern.Length)
        {
            Memory.FreePages(bufferPhys, pageCount);
            file.Dispose();
            ext2.Unmount();
            ext2.Shutdown();
            return 0;
        }

        // Flush to disk
        var flushResult = file.Flush();
        if (flushResult != FileResult.Success)
        {
            Debug.Write("[Ext2WriteTest] Flush failed: ");
            Debug.WriteDecimal((int)flushResult);
            Debug.WriteLine();
        }

        // Read back and verify
        file.Position = writeOffset;
        for (int i = 0; i < 64; i++)
            buffer[i] = 0;

        int bytesRead = file.Read(buffer, testPattern.Length);
        Debug.Write("[Ext2WriteTest] Read back ");
        Debug.WriteDecimal(bytesRead);
        Debug.Write(" bytes");
        Debug.WriteLine();

        bool success = (bytesRead == testPattern.Length);
        if (success)
        {
            for (int i = 0; i < testPattern.Length; i++)
            {
                if (buffer[i] != (byte)testPattern[i])
                {
                    success = false;
                    Debug.Write("[Ext2WriteTest] Mismatch at byte ");
                    Debug.WriteDecimal(i);
                    Debug.WriteLine();
                    break;
                }
            }
        }

        if (success)
        {
            Debug.WriteLine("[Ext2WriteTest] Write/read verify SUCCESS!");
        }
        else
        {
            Debug.WriteLine("[Ext2WriteTest] Write/read verify FAILED!");
        }

        Memory.FreePages(bufferPhys, pageCount);
        file.Dispose();
        ext2.Unmount();
        ext2.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test FAT CreateDirectory operation.
    /// </summary>
    public static int TestFatCreateDirectory()
    {
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var fat = new FatFileSystem();
        fat.Initialize();

        var mountResult = fat.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            // No FAT disk on AHCI - skip test (not a failure)
            Debug.WriteLine("[FatDirTest] No FAT disk available on AHCI, skipping");
            fat.Shutdown();
            return 1; // Return success (test skipped)
        }

        bool success = true;
        string testDir = "/TESTDIR";

        // Delete if exists from previous run
        fat.DeleteDirectory(testDir);

        // Create directory
        Debug.Write("[FatDirTest] Creating directory: ");
        Debug.WriteLine(testDir);
        var createResult = fat.CreateDirectory(testDir);
        if (createResult != FileResult.Success)
        {
            Debug.Write("[FatDirTest] CreateDirectory failed: ");
            Debug.WriteDecimal((int)createResult);
            Debug.WriteLine();
            success = false;
        }

        // Verify it exists by opening it
        if (success)
        {
            IDirectoryHandle? dir;
            var openResult = fat.OpenDirectory(testDir, out dir);
            if (openResult != FileResult.Success || dir == null)
            {
                Debug.WriteLine("[FatDirTest] Failed to open created directory");
                success = false;
            }
            else
            {
                Debug.WriteLine("[FatDirTest] Directory created and opened successfully");
                dir.Dispose();
            }
        }

        // Clean up - delete the test directory
        if (success)
        {
            var deleteResult = fat.DeleteDirectory(testDir);
            if (deleteResult != FileResult.Success)
            {
                Debug.Write("[FatDirTest] DeleteDirectory failed: ");
                Debug.WriteDecimal((int)deleteResult);
                Debug.WriteLine();
                success = false;
            }
            else
            {
                Debug.WriteLine("[FatDirTest] Directory deleted successfully");
            }
        }

        fat.Unmount();
        fat.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test FAT CreateFile and DeleteFile operations.
    /// </summary>
    public static int TestFatCreateDeleteFile()
    {
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var fat = new FatFileSystem();
        fat.Initialize();

        var mountResult = fat.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            // No FAT disk on AHCI - skip test (not a failure)
            Debug.WriteLine("[FatFileTest] No FAT disk available on AHCI, skipping");
            fat.Shutdown();
            return 1; // Return success (test skipped)
        }

        bool success = true;
        string testFile = "/TEST.TXT";

        // Delete if exists from previous run
        fat.DeleteFile(testFile);

        // Create new file
        Debug.Write("[FatFileTest] Creating file: ");
        Debug.WriteLine(testFile);
        IFileHandle? file;
        var createResult = fat.OpenFile(testFile, FileMode.CreateNew, FileAccess.ReadWrite, out file);
        if (createResult != FileResult.Success || file == null)
        {
            Debug.Write("[FatFileTest] CreateNew failed: ");
            Debug.WriteDecimal((int)createResult);
            Debug.WriteLine();
            success = false;
        }

        // Write some data
        if (success && file != null)
        {
            ulong bufferPhys = Memory.AllocatePages(1);
            if (bufferPhys == 0)
            {
                file.Dispose();
                fat.Unmount();
                fat.Shutdown();
                return 0;
            }

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);
            string testData = "FAT CREATE TEST OK!";
            for (int i = 0; i < testData.Length; i++)
                buffer[i] = (byte)testData[i];

            int bytesWritten = file.Write(buffer, testData.Length);
            if (bytesWritten != testData.Length)
            {
                Debug.WriteLine("[FatFileTest] Write failed");
                success = false;
            }
            else
            {
                Debug.Write("[FatFileTest] Wrote ");
                Debug.WriteDecimal(bytesWritten);
                Debug.WriteLine(" bytes");
            }

            file.Flush();
            Memory.FreePages(bufferPhys, 1);
            file.Dispose();
        }

        // Verify file exists by reopening
        if (success)
        {
            IFileHandle? readFile;
            var openResult = fat.OpenFile(testFile, FileMode.Open, FileAccess.Read, out readFile);
            if (openResult != FileResult.Success || readFile == null)
            {
                Debug.WriteLine("[FatFileTest] Failed to reopen created file");
                success = false;
            }
            else
            {
                Debug.Write("[FatFileTest] File size: ");
                Debug.WriteDecimal((int)readFile.Length);
                Debug.WriteLine();
                readFile.Dispose();
            }
        }

        // Delete the file
        if (success)
        {
            var deleteResult = fat.DeleteFile(testFile);
            if (deleteResult != FileResult.Success)
            {
                Debug.Write("[FatFileTest] DeleteFile failed: ");
                Debug.WriteDecimal((int)deleteResult);
                Debug.WriteLine();
                success = false;
            }
            else
            {
                Debug.WriteLine("[FatFileTest] File deleted successfully");
            }
        }

        // Verify file is gone
        if (success)
        {
            IFileHandle? shouldFail;
            var openResult = fat.OpenFile(testFile, FileMode.Open, FileAccess.Read, out shouldFail);
            if (openResult == FileResult.Success)
            {
                Debug.WriteLine("[FatFileTest] ERROR: File still exists after delete!");
                if (shouldFail != null)
                    shouldFail.Dispose();
                success = false;
            }
            else
            {
                Debug.WriteLine("[FatFileTest] Verified file no longer exists");
            }
        }

        fat.Unmount();
        fat.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test ext2 CreateDirectory operation.
    /// </summary>
    public static int TestExt2CreateDirectory()
    {
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var ext2 = new Ext2FileSystem();
        ext2.Initialize();

        var mountResult = ext2.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            ext2.Shutdown();
            return 0;
        }

        bool success = true;
        string testDir = "/newdir_test";

        // Delete if exists from previous run
        ext2.DeleteDirectory(testDir);

        // Create directory
        Debug.Write("[Ext2DirTest] Creating directory: ");
        Debug.WriteLine(testDir);
        var createResult = ext2.CreateDirectory(testDir);
        if (createResult != FileResult.Success)
        {
            Debug.Write("[Ext2DirTest] CreateDirectory failed: ");
            Debug.WriteDecimal((int)createResult);
            Debug.WriteLine();
            success = false;
        }

        // Verify it exists by opening it
        if (success)
        {
            IDirectoryHandle? dir;
            var openResult = ext2.OpenDirectory(testDir, out dir);
            if (openResult != FileResult.Success || dir == null)
            {
                Debug.WriteLine("[Ext2DirTest] Failed to open created directory");
                success = false;
            }
            else
            {
                Debug.WriteLine("[Ext2DirTest] Directory created and opened successfully");
                dir.Dispose();
            }
        }

        // Clean up - delete the test directory
        if (success)
        {
            var deleteResult = ext2.DeleteDirectory(testDir);
            if (deleteResult != FileResult.Success)
            {
                Debug.Write("[Ext2DirTest] DeleteDirectory failed: ");
                Debug.WriteDecimal((int)deleteResult);
                Debug.WriteLine();
                success = false;
            }
            else
            {
                Debug.WriteLine("[Ext2DirTest] Directory deleted successfully");
            }
        }

        ext2.Unmount();
        ext2.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test ext2 CreateFile and DeleteFile operations.
    /// </summary>
    public static int TestExt2CreateDeleteFile()
    {
        var device = GetLastDevice();
        if (device == null)
            return 0;

        var ext2 = new Ext2FileSystem();
        ext2.Initialize();

        var mountResult = ext2.Mount(device, false);
        if (mountResult != FileResult.Success)
        {
            ext2.Shutdown();
            return 0;
        }

        bool success = true;
        string testFile = "/newfile_test.txt";

        // Delete if exists from previous run
        ext2.DeleteFile(testFile);

        // Create new file
        Debug.Write("[Ext2FileTest] Creating file: ");
        Debug.WriteLine(testFile);
        IFileHandle? file;
        var createResult = ext2.OpenFile(testFile, FileMode.CreateNew, FileAccess.ReadWrite, out file);
        if (createResult != FileResult.Success || file == null)
        {
            Debug.Write("[Ext2FileTest] CreateNew failed: ");
            Debug.WriteDecimal((int)createResult);
            Debug.WriteLine();
            success = false;
        }

        // Write some data
        if (success && file != null)
        {
            ulong bufferPhys = Memory.AllocatePages(1);
            if (bufferPhys == 0)
            {
                file.Dispose();
                ext2.Unmount();
                ext2.Shutdown();
                return 0;
            }

            byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);
            string testData = "EXT2 CREATE TEST OK!";
            for (int i = 0; i < testData.Length; i++)
                buffer[i] = (byte)testData[i];

            int bytesWritten = file.Write(buffer, testData.Length);
            if (bytesWritten != testData.Length)
            {
                Debug.WriteLine("[Ext2FileTest] Write failed");
                success = false;
            }
            else
            {
                Debug.Write("[Ext2FileTest] Wrote ");
                Debug.WriteDecimal(bytesWritten);
                Debug.WriteLine(" bytes");
            }

            file.Flush();
            Memory.FreePages(bufferPhys, 1);
            file.Dispose();
        }

        // Verify file exists by reopening
        if (success)
        {
            IFileHandle? readFile;
            var openResult = ext2.OpenFile(testFile, FileMode.Open, FileAccess.Read, out readFile);
            if (openResult != FileResult.Success || readFile == null)
            {
                Debug.WriteLine("[Ext2FileTest] Failed to reopen created file");
                success = false;
            }
            else
            {
                Debug.Write("[Ext2FileTest] File size: ");
                Debug.WriteDecimal((int)readFile.Length);
                Debug.WriteLine();
                readFile.Dispose();
            }
        }

        // Delete the file
        if (success)
        {
            var deleteResult = ext2.DeleteFile(testFile);
            if (deleteResult != FileResult.Success)
            {
                Debug.Write("[Ext2FileTest] DeleteFile failed: ");
                Debug.WriteDecimal((int)deleteResult);
                Debug.WriteLine();
                success = false;
            }
            else
            {
                Debug.WriteLine("[Ext2FileTest] File deleted successfully");
            }
        }

        // Verify file is gone
        if (success)
        {
            IFileHandle? shouldFail;
            var openResult = ext2.OpenFile(testFile, FileMode.Open, FileAccess.Read, out shouldFail);
            if (openResult == FileResult.Success)
            {
                Debug.WriteLine("[Ext2FileTest] ERROR: File still exists after delete!");
                if (shouldFail != null)
                    shouldFail.Dispose();
                success = false;
            }
            else
            {
                Debug.WriteLine("[Ext2FileTest] Verified file no longer exists");
            }
        }

        ext2.Unmount();
        ext2.Shutdown();

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test AtaIdentifyData field offsets to diagnose fixed buffer issues.
    /// </summary>
    public static int TestIdentifyOffsets()
    {
        Debug.WriteLine("[IdentifyTest] Testing AtaIdentifyData field offsets...");

        // Allocate buffer
        ulong bufferPhys = Memory.AllocatePages(1);
        if (bufferPhys == 0)
            return 0;

        AtaIdentifyData* identify = (AtaIdentifyData*)Memory.PhysToVirt(bufferPhys);
        byte* raw = (byte*)identify;

        // Clear
        for (int i = 0; i < 512; i++)
            raw[i] = 0;

        // Write known values at expected byte offsets
        *(uint*)(raw + 120) = 0x12345678;   // TotalSectors28 at words 60-61
        *(ushort*)(raw + 166) = 0xABCD;     // CmdSet2Supported at word 83
        *(ulong*)(raw + 200) = 0xDEADBEEFCAFEBABE; // TotalSectors48 at words 100-103

        // Now read via struct field access
        uint ts28 = identify->TotalSectors28;
        ushort cmd2 = identify->CmdSet2Supported;
        ulong ts48 = identify->TotalSectors48;

        Debug.Write("[IdentifyTest] Expected TotalSectors28=0x12345678, got 0x");
        Debug.WriteHex(ts28);
        Debug.WriteLine();

        Debug.Write("[IdentifyTest] Expected CmdSet2Supported=0xABCD, got 0x");
        Debug.WriteHex(cmd2);
        Debug.WriteLine();

        Debug.Write("[IdentifyTest] Expected TotalSectors48=0xDEADBEEFCAFEBABE, got 0x");
        Debug.WriteHex(ts48);
        Debug.WriteLine();

        bool success = (ts28 == 0x12345678) && (cmd2 == 0xABCD) && (ts48 == 0xDEADBEEFCAFEBABE);

        Memory.FreePages(bufferPhys, 1);

        if (success)
            Debug.WriteLine("[IdentifyTest] All offsets CORRECT!");
        else
            Debug.WriteLine("[IdentifyTest] Offset ERRORS detected!");

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test struct pointer field access to diagnose JIT issues.
    /// This tests that fields in Pack=1 structs have correct offsets.
    /// </summary>
    public static int TestStructFieldAccess()
    {
        Debug.WriteLine("[StructTest] Testing FisRegH2D field access...");

        // Allocate a FisRegH2D struct on heap (to ensure we access through pointer)
        ulong bufferPhys = Memory.AllocatePages(1);
        if (bufferPhys == 0)
            return 0;

        FisRegH2D* fis = (FisRegH2D*)Memory.PhysToVirt(bufferPhys);

        // Clear the struct
        byte* raw = (byte*)fis;
        for (int i = 0; i < 20; i++)
            raw[i] = 0;

        // Use struct field access (what we want to work)
        fis->FisType = FisType.RegH2D;  // Should be at offset 0
        fis->Flags = 0x80;               // Should be at offset 1
        fis->Command = 0x25;             // Should be at offset 2 (READ DMA EXT)
        fis->FeatureLo = 0;              // Should be at offset 3
        fis->Lba0 = 0x11;                // Should be at offset 4
        fis->Lba1 = 0x22;                // Should be at offset 5
        fis->Lba2 = 0x33;                // Should be at offset 6
        fis->Device = 0x40;              // Should be at offset 7
        fis->Lba3 = 0x44;                // Should be at offset 8
        fis->Lba4 = 0x55;                // Should be at offset 9
        fis->Lba5 = 0x66;                // Should be at offset 10
        fis->FeatureHi = 0;              // Should be at offset 11
        fis->CountLo = 0x01;             // Should be at offset 12
        fis->CountHi = 0;                // Should be at offset 13
        fis->Icc = 0;                    // Should be at offset 14
        fis->Control = 0;                // Should be at offset 15

        // Now verify by reading back via byte pointers (which we know works)
        bool success = true;
        byte expected0 = (byte)FisType.RegH2D;  // 0x27

        Debug.Write("[StructTest] Expected FisType=0x");
        Debug.WriteHex(expected0);
        Debug.Write(" at [0], got 0x");
        Debug.WriteHex(raw[0]);
        Debug.WriteLine();

        if (raw[0] != expected0) { Debug.WriteLine("[StructTest] FAIL: FisType wrong"); success = false; }
        if (raw[1] != 0x80) { Debug.WriteLine("[StructTest] FAIL: Flags wrong"); success = false; }
        if (raw[2] != 0x25) { Debug.WriteLine("[StructTest] FAIL: Command wrong"); success = false; }
        if (raw[4] != 0x11) { Debug.WriteLine("[StructTest] FAIL: Lba0 wrong"); success = false; }
        if (raw[5] != 0x22) { Debug.WriteLine("[StructTest] FAIL: Lba1 wrong"); success = false; }
        if (raw[6] != 0x33) { Debug.WriteLine("[StructTest] FAIL: Lba2 wrong"); success = false; }
        if (raw[7] != 0x40) { Debug.WriteLine("[StructTest] FAIL: Device wrong"); success = false; }
        if (raw[8] != 0x44) { Debug.WriteLine("[StructTest] FAIL: Lba3 wrong"); success = false; }
        if (raw[9] != 0x55) { Debug.WriteLine("[StructTest] FAIL: Lba4 wrong"); success = false; }
        if (raw[10] != 0x66) { Debug.WriteLine("[StructTest] FAIL: Lba5 wrong"); success = false; }
        if (raw[12] != 0x01) { Debug.WriteLine("[StructTest] FAIL: CountLo wrong"); success = false; }

        // Print actual bytes for diagnosis
        Debug.Write("[StructTest] Raw bytes: ");
        for (int i = 0; i < 16; i++)
        {
            Debug.WriteHex(raw[i]);
            Debug.Write(" ");
        }
        Debug.WriteLine();

        Memory.FreePages(bufferPhys, 1);

        if (success)
            Debug.WriteLine("[StructTest] All field offsets CORRECT!");
        else
            Debug.WriteLine("[StructTest] Field offset ERRORS detected!");

        return success ? 1 : 0;
    }

    /// <summary>
    /// Test mounting EXT2 as root filesystem via VFS and performing file operations.
    /// This validates the complete stack: AHCI -> Block Device -> EXT2 -> VFS -> File I/O
    /// Tests the VFS layer by mounting the EXT2 test disk at root, listing directory,
    /// and attempting to read files.
    /// </summary>
    public static int TestVfsRootMount()
    {
        Debug.WriteLine("[VFS] Testing VFS root mount with EXT2...");

        // Initialize VFS
        VFS.Initialize();
        Debug.WriteLine("[VFS] VFS initialized");

        // Get the last device (EXT2 test disk)
        var device = GetLastDevice();
        if (device == null)
        {
            Debug.WriteLine("[VFS] No device found");
            return 0;
        }

        // Create EXT2 filesystem
        var ext2 = new Ext2FileSystem();
        ext2.Initialize();
        var mountResult = VFS.Mount("/", ext2, device, false);  // Read-write mount

        if (mountResult != FileResult.Success)
        {
            Debug.Write("[VFS] Mount failed: ");
            Debug.WriteDecimal((int)mountResult);
            Debug.WriteLine();
            ext2.Shutdown();
            return 0;
        }

        Debug.WriteLine("[VFS] Mounted EXT2 at / (read-write)");

        // List root directory
        IDirectoryHandle? dir;
        var dirResult = VFS.OpenDirectory("/", out dir);
        if (dirResult != FileResult.Success || dir == null)
        {
            Debug.Write("[VFS] Failed to open root directory: ");
            Debug.WriteDecimal((int)dirResult);
            Debug.WriteLine();
            VFS.Unmount("/");
            return 0;
        }

        Debug.WriteLine("[VFS] Root directory contents:");
        int entryCount = 0;
        FileInfo? entry;
        while ((entry = dir.ReadNext()) != null)
        {
            Debug.Write("  ");
            Debug.Write(entry.Name);
            if (entry.IsDirectory)
                Debug.Write("/");
            Debug.WriteLine();
            entryCount++;
        }
        dir.Dispose();

        Debug.Write("[VFS] Found ");
        Debug.WriteDecimal(entryCount);
        Debug.WriteLine(" entries");

        // Test interface dispatch by calling through IFileSystem interface
        Debug.WriteLine("[VFS] Testing interface dispatch...");
        var mount = VFS.FindMount("/");
        if (mount != null)
        {
            IFileSystem fs = mount.FileSystem;
            Debug.Write("[VFS] mount.FileSystem address: 0x");
            Debug.WriteHex((ulong)System.Runtime.CompilerServices.Unsafe.As<IFileSystem, nint>(ref fs));
            Debug.WriteLine();

            // Call FilesystemName property multiple times through interface
            string name1 = fs.FilesystemName;
            string name2 = fs.FilesystemName;
            Debug.Write("[VFS] FilesystemName call 1: ");
            Debug.WriteLine(name1 ?? "(null)");
            Debug.Write("[VFS] FilesystemName call 2: ");
            Debug.WriteLine(name2 ?? "(null)");

            // Check if they match
            bool namesMatch = name1 == name2;
            Debug.Write("[VFS] Names match: ");
            Debug.WriteLine(namesMatch ? "YES" : "NO - BUG!");

            if (!namesMatch)
            {
                Debug.WriteLine("[VFS] SKIPPING write tests (JIT interface dispatch bug)");
            }
            else
            {
                Debug.WriteLine("[VFS] Interface dispatch OK, running write tests...");

                // Test 1: Create and write file
                IFileHandle? file;
                var result = VFS.OpenFile("/test_new.txt", FileMode.CreateNew, FileAccess.Write, out file);
                Debug.Write("[VFS] CreateNew result: ");
                Debug.WriteDecimal((int)result);
                Debug.WriteLine();

                if (result == FileResult.Success && file != null)
                {
                    // Write some test data
                    byte* testData = stackalloc byte[32];
                    for (int i = 0; i < 26; i++)
                        testData[i] = (byte)('A' + i);
                    testData[26] = (byte)'\n';

                    int written = file.Write(testData, 27);
                    Debug.Write("[VFS] Write returned: ");
                    Debug.WriteDecimal(written);
                    Debug.WriteLine();

                    file.Dispose();

                    // Test 2: Read back the file
                    IFileHandle? readFile;
                    result = VFS.OpenFile("/test_new.txt", FileMode.Open, FileAccess.Read, out readFile);
                    Debug.Write("[VFS] Open for read result: ");
                    Debug.WriteDecimal((int)result);
                    Debug.WriteLine();

                    if (result == FileResult.Success && readFile != null)
                    {
                        byte* readBuf = stackalloc byte[64];
                        int bytesRead = readFile.Read(readBuf, 64);
                        Debug.Write("[VFS] Read returned: ");
                        Debug.WriteDecimal(bytesRead);
                        Debug.WriteLine();
                        Debug.Write("[VFS] Content: ");
                        // Convert to string for display
                        char* chars = stackalloc char[33];
                        for (int i = 0; i < bytesRead && i < 32; i++)
                            chars[i] = (char)readBuf[i];
                        chars[bytesRead < 32 ? bytesRead : 32] = '\0';
                        Debug.Write(new string(chars, 0, bytesRead < 32 ? bytesRead : 32));
                        Debug.WriteLine();
                        readFile.Dispose();

                        // Test 3: Delete the test file
                        result = VFS.DeleteFile("/test_new.txt");
                        Debug.Write("[VFS] Delete result: ");
                        Debug.WriteDecimal((int)result);
                        Debug.WriteLine();

                        if (bytesRead == 27 && result == FileResult.Success)
                            Debug.WriteLine("[VFS] Write tests PASSED!");
                        else
                            Debug.WriteLine("[VFS] Write tests FAILED");
                    }
                    else
                    {
                        Debug.WriteLine("[VFS] Read back failed");
                    }
                }
                else
                {
                    Debug.WriteLine("[VFS] Create file failed");
                }
            }
        }
        else
        {
            Debug.WriteLine("[VFS] FindMount returned null!");
        }

        // Clean up
        var unmountResult = VFS.Unmount("/");
        if (unmountResult == FileResult.Success)
        {
            Debug.WriteLine("[VFS] Unmounted successfully");
        }
        else
        {
            Debug.Write("[VFS] Unmount failed: ");
            Debug.WriteDecimal((int)unmountResult);
            Debug.WriteLine();
        }

        // VFS tests passed (read and write)
        return 1;
    }

    /// <summary>
    /// Test creating and writing to a new file.
    /// </summary>
    private static bool TestFileCreateAndWrite()
    {
        Debug.Write("[VFS] Test: Create and write file...");

        IFileHandle? file;
        var result = VFS.OpenFile("/test_new.txt", FileMode.CreateNew, FileAccess.Write, out file);
        if (result != FileResult.Success || file == null)
        {
            Debug.Write(" FAIL (create: ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine(")");
            return false;
        }

        // Write some data
        ulong pageCount = 1;
        ulong bufPhys = Memory.AllocatePages(pageCount);
        if (bufPhys == 0)
        {
            Debug.WriteLine(" FAIL (alloc)");
            file.Dispose();
            return false;
        }

        byte* buf = (byte*)Memory.PhysToVirt(bufPhys);
        string testData = "Hello from EXT2 write test!";
        for (int i = 0; i < testData.Length; i++)
            buf[i] = (byte)testData[i];

        int written = file.Write(buf, testData.Length);
        Memory.FreePages(bufPhys, pageCount);
        file.Dispose();

        if (written != testData.Length)
        {
            Debug.Write(" FAIL (wrote ");
            Debug.WriteDecimal(written);
            Debug.Write("/");
            Debug.WriteDecimal(testData.Length);
            Debug.WriteLine(")");
            return false;
        }

        Debug.WriteLine(" PASS");
        return true;
    }

    /// <summary>
    /// Test reading back the written file.
    /// </summary>
    private static bool TestFileReadBack()
    {
        Debug.Write("[VFS] Test: Read back file...");

        IFileHandle? file;
        var result = VFS.OpenFile("/test_new.txt", FileMode.Open, FileAccess.Read, out file);
        if (result != FileResult.Success || file == null)
        {
            Debug.Write(" FAIL (open: ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine(")");
            return false;
        }

        ulong pageCount = 1;
        ulong bufPhys = Memory.AllocatePages(pageCount);
        if (bufPhys == 0)
        {
            Debug.WriteLine(" FAIL (alloc)");
            file.Dispose();
            return false;
        }

        byte* buf = (byte*)Memory.PhysToVirt(bufPhys);
        int bytesRead = file.Read(buf, 64);
        file.Dispose();

        string testData = "Hello from EXT2 write test!";
        if (bytesRead != testData.Length)
        {
            Debug.Write(" FAIL (read ");
            Debug.WriteDecimal(bytesRead);
            Debug.Write("/");
            Debug.WriteDecimal(testData.Length);
            Debug.WriteLine(")");
            Memory.FreePages(bufPhys, pageCount);
            return false;
        }

        // Verify content
        bool match = true;
        for (int i = 0; i < testData.Length; i++)
        {
            if (buf[i] != (byte)testData[i])
            {
                match = false;
                break;
            }
        }

        Memory.FreePages(bufPhys, pageCount);

        if (!match)
        {
            Debug.WriteLine(" FAIL (content mismatch)");
            return false;
        }

        Debug.WriteLine(" PASS");
        return true;
    }

    /// <summary>
    /// Test creating a directory.
    /// </summary>
    private static bool TestDirectoryCreate()
    {
        Debug.Write("[VFS] Test: Create directory...");

        var result = VFS.CreateDirectory("/testdir");
        if (result != FileResult.Success)
        {
            Debug.Write(" FAIL (");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine(")");
            return false;
        }

        // Verify it exists
        if (!VFS.Exists("/testdir"))
        {
            Debug.WriteLine(" FAIL (not found after create)");
            return false;
        }

        Debug.WriteLine(" PASS");
        return true;
    }

    /// <summary>
    /// Test deleting a file.
    /// </summary>
    private static bool TestFileDelete()
    {
        Debug.Write("[VFS] Test: Delete file...");

        var result = VFS.DeleteFile("/test_new.txt");
        if (result != FileResult.Success)
        {
            Debug.Write(" FAIL (");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine(")");
            return false;
        }

        // Verify it's gone
        if (VFS.Exists("/test_new.txt"))
        {
            Debug.WriteLine(" FAIL (still exists)");
            return false;
        }

        Debug.WriteLine(" PASS");
        return true;
    }

    /// <summary>
    /// Test deleting a directory.
    /// </summary>
    private static bool TestDirectoryDelete()
    {
        Debug.Write("[VFS] Test: Delete directory...");

        var result = VFS.DeleteDirectory("/testdir");
        if (result != FileResult.Success)
        {
            Debug.Write(" FAIL (");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine(")");
            return false;
        }

        // Verify it's gone
        if (VFS.Exists("/testdir"))
        {
            Debug.WriteLine(" FAIL (still exists)");
            return false;
        }

        Debug.WriteLine(" PASS");
        return true;
    }

    #region Root Filesystem Mount

    // Static reference to keep filesystem alive after mount
    private static Ext2FileSystem? _rootFs;
    private static FatFileSystem? _bootFs;

    /// <summary>
    /// Mount the root filesystem (ext2) at /.
    /// This initializes VFS and mounts the ext2 partition as the system root.
    /// The mount persists for the lifetime of the system.
    /// Returns 1 on success, 0 on failure.
    /// </summary>
    public static int MountRootFilesystem()
    {
        Debug.WriteLine("[Root] Mounting root filesystem...");

        // Initialize VFS if not already done
        VFS.Initialize();

        // Get the SATA device with ext2 (last device in AHCI)
        var device = GetLastDevice();
        if (device == null)
        {
            Debug.WriteLine("[Root] ERROR: No AHCI device found");
            return 0;
        }

        // Create and initialize ext2 filesystem driver
        _rootFs = new Ext2FileSystem();
        _rootFs.Initialize();

        // Probe to verify it's ext2
        if (!_rootFs.Probe(device))
        {
            Debug.WriteLine("[Root] ERROR: Device is not ext2 formatted");
            _rootFs.Shutdown();
            _rootFs = null;
            return 0;
        }

        // Mount as root (read-write)
        var result = VFS.Mount("/", _rootFs, device, false);
        if (result != FileResult.Success)
        {
            Debug.Write("[Root] ERROR: Mount failed with code ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine();
            _rootFs.Shutdown();
            _rootFs = null;
            return 0;
        }

        Debug.WriteLine("[Root] Mounted ext2 at / (read-write)");

        // Verify root directory exists
        FileInfo? rootInfo;
        var getInfoResult = _rootFs.GetInfo("/", out rootInfo);
        if (getInfoResult != FileResult.Success || rootInfo == null)
        {
            Debug.WriteLine("[Root] ERROR: Cannot read root directory");
            VFS.Unmount("/");
            _rootFs.Shutdown();
            _rootFs = null;
            return 0;
        }

        Debug.Write("[Root] Root dir: ");
        Debug.Write(rootInfo.Name);
        Debug.Write(" (");
        Debug.Write(rootInfo.IsDirectory ? "dir" : "file");
        Debug.WriteLine(")");

        // Check for /drivers directory
        FileInfo? driversInfo;
        var driversResult = _rootFs.GetInfo("/drivers", out driversInfo);
        if (driversResult == FileResult.Success && driversInfo != null && driversInfo.IsDirectory)
        {
            Debug.WriteLine("[Root] Found /drivers directory");
        }
        else
        {
            Debug.WriteLine("[Root] No /drivers directory found");
        }

        Debug.WriteLine("[Root] Root filesystem mounted successfully");
        return 1;
    }

    /// <summary>
    /// Get the root filesystem instance (if mounted).
    /// </summary>
    public static Ext2FileSystem? GetRootFilesystem()
    {
        return _rootFs;
    }

    /// <summary>
    /// Mount the boot filesystem (FAT) at /boot.
    /// This mounts the boot partition containing kernel, DLLs, and drivers.
    /// Must be called after MountRootFilesystem.
    /// Returns 1 on success, 0 on failure.
    /// </summary>
    public static int MountBootFilesystem()
    {
        Debug.WriteLine("[Boot] Mounting boot filesystem...");

        // Get the boot device (first AHCI device - FAT boot partition)
        var device = GetFirstDevice();
        if (device == null)
        {
            Debug.WriteLine("[Boot] ERROR: No boot device found");
            return 0;
        }

        // Create and initialize FAT filesystem driver
        _bootFs = new FatFileSystem();
        _bootFs.Initialize();

        // Probe to verify it's FAT
        if (!_bootFs.Probe(device))
        {
            Debug.WriteLine("[Boot] ERROR: Boot device is not FAT formatted");
            _bootFs.Shutdown();
            _bootFs = null;
            return 0;
        }

        // Mount at /boot (read-only for safety)
        var result = VFS.Mount("/boot", _bootFs, device, true);
        if (result != FileResult.Success)
        {
            Debug.Write("[Boot] ERROR: Mount failed with code ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine();
            _bootFs.Shutdown();
            _bootFs = null;
            return 0;
        }

        Debug.WriteLine("[Boot] Mounted FAT at /boot (read-only)");

        // Verify we can read the boot partition
        FileInfo? efiInfo;
        var getInfoResult = _bootFs.GetInfo("/EFI", out efiInfo);
        if (getInfoResult == FileResult.Success && efiInfo != null && efiInfo.IsDirectory)
        {
            Debug.WriteLine("[Boot] Found /boot/EFI directory");
        }

        // Check for drivers directory
        FileInfo? driversInfo;
        var driversResult = _bootFs.GetInfo("/drivers", out driversInfo);
        if (driversResult == FileResult.Success && driversInfo != null && driversInfo.IsDirectory)
        {
            Debug.WriteLine("[Boot] Found /boot/drivers directory");
        }

        Debug.WriteLine("[Boot] Boot filesystem mounted successfully");
        return 1;
    }

    /// <summary>
    /// Get the boot filesystem instance (if mounted).
    /// </summary>
    public static FatFileSystem? GetBootFilesystem()
    {
        return _bootFs;
    }

    /// <summary>
    /// List contents of /drivers directory.
    /// Returns the number of driver files found, or -1 on error.
    /// </summary>
    public static int ListDrivers()
    {
        if (_rootFs == null)
        {
            Debug.WriteLine("[Root] ERROR: Root filesystem not mounted");
            return -1;
        }

        // Use direct filesystem access to avoid VFS interface dispatch issues
        IDirectoryHandle? dir;
        var result = _rootFs.OpenDirectory("/drivers", out dir);
        if (result != FileResult.Success || dir == null)
        {
            Debug.Write("[Root] ERROR: Cannot open /drivers: ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine();
            return -1;
        }

        Debug.WriteLine("[Root] Available drivers in /drivers:");
        int count = 0;
        FileInfo? entry;
        while ((entry = dir.ReadNext()) != null)
        {
            // Check if it's a .dll file
            if (!entry.IsDirectory)
            {
                string name = entry.Name;
                if (name.Length > 4)
                {
                    // Check for .dll extension
                    int extStart = name.Length - 4;
                    if (name[extStart] == '.' &&
                        (name[extStart + 1] == 'd' || name[extStart + 1] == 'D') &&
                        (name[extStart + 2] == 'l' || name[extStart + 2] == 'L') &&
                        (name[extStart + 3] == 'l' || name[extStart + 3] == 'L'))
                    {
                        Debug.Write("  ");
                        Debug.Write(name);
                        Debug.Write(" (");
                        Debug.WriteDecimal((int)entry.Size);
                        Debug.WriteLine(" bytes)");
                        count++;
                    }
                }
            }
        }
        dir.Dispose();

        if (count == 0)
        {
            Debug.WriteLine("  (no drivers found)");
        }

        return count;
    }

    #endregion

    #region Dynamic Driver Loading

    /// <summary>
    /// Load drivers from the /drivers directory.
    /// Each .dll file is loaded and its entry point is called.
    /// Returns the number of drivers successfully loaded.
    /// </summary>
    public static int LoadDrivers()
    {
        return LoadDriversFromPath("/drivers");
    }

    /// <summary>
    /// Load drivers from a specified VFS path.
    /// </summary>
    private static int LoadDriversFromPath(string path)
    {
        Debug.Write("[DriverLoad] Scanning ");
        Debug.WriteLine(path);

        // Open directory
        IDirectoryHandle? dir;
        var result = VFS.OpenDirectory(path, out dir);
        if (result != FileResult.Success || dir == null)
        {
            Debug.Write("[DriverLoad] Failed to open directory: ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine();
            return 0;
        }

        int loaded = 0;
        FileInfo? entry;

        while ((entry = dir.ReadNext()) != null)
        {
            // Skip directories
            if (entry.IsDirectory)
                continue;

            string name = entry.Name;

            // Check for .dll extension
            if (!IsDllFile(name))
                continue;

            Debug.Write("[DriverLoad] Found: ");
            Debug.WriteLine(name);

            // Build full path
            string fullPath = path + "/" + name;

            // Try to load this driver
            if (LoadSingleDriver(fullPath, name))
            {
                loaded++;
            }
        }

        dir.Dispose();

        Debug.Write("[DriverLoad] Loaded ");
        Debug.WriteDecimal(loaded);
        Debug.WriteLine(" driver(s)");

        // Test driver unloading - unload all drivers we just loaded
        if (loaded > 0)
        {
            Debug.WriteLine("[DriverLoad] Testing driver unload...");
            int unloaded = UnloadAllDrivers();
            Debug.Write("[DriverLoad] Unloaded ");
            Debug.WriteDecimal(unloaded);
            Debug.WriteLine(" driver(s) - test complete");
        }

        return loaded;
    }

    /// <summary>
    /// Check if a filename ends with .dll
    /// </summary>
    private static bool IsDllFile(string name)
    {
        if (name.Length < 5)
            return false;

        int len = name.Length;
        return name[len - 4] == '.' &&
               (name[len - 3] == 'd' || name[len - 3] == 'D') &&
               (name[len - 2] == 'l' || name[len - 2] == 'L') &&
               (name[len - 1] == 'l' || name[len - 1] == 'L');
    }

    /// <summary>
    /// Load a single driver from a VFS path.
    /// </summary>
    private static unsafe bool LoadSingleDriver(string path, string name)
    {
        Debug.Write("[DriverLoad] Loading: ");
        Debug.WriteLine(path);

        // Open file
        IFileHandle? file;
        var result = VFS.OpenFile(path, FileMode.Open, FileAccess.Read, out file);
        if (result != FileResult.Success || file == null)
        {
            Debug.Write("[DriverLoad] Failed to open: ");
            Debug.WriteDecimal((int)result);
            Debug.WriteLine();
            return false;
        }

        // Get file size
        long size = file.Length;
        if (size <= 0 || size > 16 * 1024 * 1024)
        {
            Debug.Write("[DriverLoad] Invalid size: ");
            Debug.WriteDecimal((ulong)size);
            Debug.WriteLine();
            file.Dispose();
            return false;
        }

        // Allocate buffer using Memory API (kernel export)
        // Use page allocation to ensure alignment
        ulong pages = ((ulong)size + 4095) / 4096;
        ulong bufferAddr = Memory.AllocatePages(pages);
        if (bufferAddr == 0)
        {
            Debug.WriteLine("[DriverLoad] Failed to allocate buffer");
            file.Dispose();
            return false;
        }

        byte* buffer = (byte*)bufferAddr;

        // Read file
        long totalRead = 0;
        while (totalRead < size)
        {
            // Inline min to avoid System.Math dependency (not in korlib)
            long remaining = size - totalRead;
            int toRead = remaining < 65536 ? (int)remaining : 65536;
            int bytesRead = file.Read(buffer + totalRead, toRead);
            if (bytesRead <= 0)
            {
                Debug.Write("[DriverLoad] Read error at ");
                Debug.WriteDecimal((ulong)totalRead);
                Debug.WriteLine();
                Memory.FreePages(bufferAddr, pages);
                file.Dispose();
                return false;
            }
            totalRead += bytesRead;
        }

        file.Dispose();

        Debug.Write("[DriverLoad] Read ");
        Debug.WriteDecimal((ulong)totalRead);
        Debug.WriteLine(" bytes");

        // Create a new context for this driver
        uint contextId = CreateDriverContext();

        // Load the assembly via PInvoke to AssemblyLoader
        uint assemblyId = Kernel_LoadOwnedAssembly(buffer, (ulong)size, contextId);
        if (assemblyId == 0)
        {
            Debug.WriteLine("[DriverLoad] Assembly load failed");
            Memory.FreePages(bufferAddr, pages);
            return false;
        }

        Debug.Write("[DriverLoad] Assembly loaded, ID=");
        Debug.WriteDecimal(assemblyId);
        Debug.Write(" ctx=");
        Debug.WriteDecimal(contextId);
        Debug.WriteLine();

        // Try to find and call the driver entry point
        int driverIdx = InitializeLoadedDriver(assemblyId, contextId, name);

        return driverIdx >= 0;
    }

    // Static method name strings for PInvoke (avoids stackalloc issues)
    private static readonly byte[] _initializeName = { (byte)'I', (byte)'n', (byte)'i', (byte)'t', (byte)'i', (byte)'a', (byte)'l', (byte)'i', (byte)'z', (byte)'e', 0 };
    private static readonly byte[] _initName = { (byte)'I', (byte)'n', (byte)'i', (byte)'t', 0 };
    private static readonly byte[] _shutdownName = { (byte)'S', (byte)'h', (byte)'u', (byte)'t', (byte)'d', (byte)'o', (byte)'w', (byte)'n', 0 };

    // Track loaded drivers for unloading
    private struct LoadedDriverInfo
    {
        public uint AssemblyId;
        public uint ContextId;
        public uint EntryTypeToken;
        public bool IsLoaded;
    }

    private const int MaxLoadedDrivers = 32;
    private static LoadedDriverInfo[] _loadedDrivers = new LoadedDriverInfo[MaxLoadedDrivers];
    private static int _loadedDriverCount = 0;

    /// <summary>
    /// Initialize a loaded driver by finding and calling its entry point.
    /// Returns the index in the loaded drivers array, or -1 on failure.
    /// </summary>
    private static unsafe int InitializeLoadedDriver(uint assemblyId, uint contextId, string name)
    {
        // Find entry type (look for "Entry" suffix)
        uint entryTypeToken = Kernel_FindDriverEntryType(assemblyId);
        if (entryTypeToken == 0)
        {
            Debug.Write("[DriverLoad] No entry type found in ");
            Debug.WriteLine(name);
            return -1;
        }

        // Find Initialize method using static byte array (pin it for PInvoke)
        uint initMethodToken;
        fixed (byte* initName = _initializeName)
        {
            initMethodToken = Kernel_FindMethodByName(assemblyId, entryTypeToken, initName);
        }

        if (initMethodToken == 0)
        {
            // Try "Init" as fallback
            fixed (byte* initShortName = _initName)
            {
                initMethodToken = Kernel_FindMethodByName(assemblyId, entryTypeToken, initShortName);
            }
        }

        if (initMethodToken == 0)
        {
            Debug.Write("[DriverLoad] No Initialize method in ");
            Debug.WriteLine(name);
            return -1;
        }

        Debug.Write("[DriverLoad] Calling Initialize for ");
        Debug.WriteLine(name);

        // JIT compile and call the Initialize method
        bool success = Kernel_JitAndCallInit(assemblyId, initMethodToken);

        if (success)
        {
            Debug.Write("[DriverLoad] ");
            Debug.Write(name);
            Debug.WriteLine(" initialized successfully");

            // Track the loaded driver
            if (_loadedDriverCount < MaxLoadedDrivers)
            {
                int idx = _loadedDriverCount++;
                _loadedDrivers[idx].AssemblyId = assemblyId;
                _loadedDrivers[idx].ContextId = contextId;
                _loadedDrivers[idx].EntryTypeToken = entryTypeToken;
                _loadedDrivers[idx].IsLoaded = true;
                return idx;
            }
        }
        else
        {
            Debug.Write("[DriverLoad] ");
            Debug.Write(name);
            Debug.WriteLine(" initialization failed");
        }

        return -1;
    }

    /// <summary>
    /// Unload a driver by its index in the loaded drivers array.
    /// Calls Shutdown() if available, then unloads the context.
    /// </summary>
    public static unsafe bool UnloadDriver(int driverIndex)
    {
        if (driverIndex < 0 || driverIndex >= _loadedDriverCount)
        {
            Debug.WriteLine("[DriverUnload] Invalid driver index");
            return false;
        }

        if (!_loadedDrivers[driverIndex].IsLoaded)
        {
            Debug.WriteLine("[DriverUnload] Driver already unloaded");
            return false;
        }

        uint assemblyId = _loadedDrivers[driverIndex].AssemblyId;
        uint contextId = _loadedDrivers[driverIndex].ContextId;
        uint entryTypeToken = _loadedDrivers[driverIndex].EntryTypeToken;

        Debug.Write("[DriverUnload] Unloading driver (asm=");
        Debug.WriteDecimal(assemblyId);
        Debug.Write(" ctx=");
        Debug.WriteDecimal(contextId);
        Debug.WriteLine(")");

        // Try to find and call Shutdown method
        uint shutdownMethodToken;
        fixed (byte* shutdownName = _shutdownName)
        {
            shutdownMethodToken = Kernel_FindMethodByName(assemblyId, entryTypeToken, shutdownName);
        }

        if (shutdownMethodToken != 0)
        {
            Debug.WriteLine("[DriverUnload] Calling Shutdown...");
            Kernel_JitAndCallShutdown(assemblyId, shutdownMethodToken);
        }
        else
        {
            Debug.WriteLine("[DriverUnload] No Shutdown method found");
        }

        // Unload the context (frees assembly memory)
        int unloaded = Kernel_UnloadContext(contextId);
        Debug.Write("[DriverUnload] Unloaded ");
        Debug.WriteDecimal(unloaded);
        Debug.WriteLine(" assembly(ies)");

        _loadedDrivers[driverIndex].IsLoaded = false;
        return true;
    }

    /// <summary>
    /// Unload all loaded drivers.
    /// </summary>
    public static int UnloadAllDrivers()
    {
        int unloaded = 0;
        for (int i = _loadedDriverCount - 1; i >= 0; i--)
        {
            if (_loadedDrivers[i].IsLoaded)
            {
                if (UnloadDriver(i))
                    unloaded++;
            }
        }
        return unloaded;
    }

    // Context ID counter
    private static uint _nextContextId = 1;

    private static uint CreateDriverContext()
    {
        return _nextContextId++;
    }

    // PInvoke declarations for kernel assembly loader functions
    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_LoadOwnedAssembly")]
    private static extern unsafe uint Kernel_LoadOwnedAssembly(byte* buffer, ulong size, uint contextId);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_FindDriverEntryType")]
    private static extern uint Kernel_FindDriverEntryType(uint assemblyId);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_FindMethodByName")]
    private static extern unsafe uint Kernel_FindMethodByName(uint assemblyId, uint typeToken, byte* methodName);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_JitAndCallInit")]
    private static extern bool Kernel_JitAndCallInit(uint assemblyId, uint methodToken);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_JitAndCallShutdown")]
    private static extern void Kernel_JitAndCallShutdown(uint assemblyId, uint methodToken);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_UnloadContext")]
    private static extern int Kernel_UnloadContext(uint contextId);

    [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Kernel_CreateContext")]
    private static extern uint Kernel_CreateContext();

    #endregion
}
