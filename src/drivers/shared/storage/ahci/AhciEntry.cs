// ProtonOS AHCI Entry Point
// Static entry point for JIT compilation

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;
using ProtonOS.Drivers.Storage.Fat;

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
}
