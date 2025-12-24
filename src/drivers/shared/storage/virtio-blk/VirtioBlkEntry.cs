// ProtonOS VirtioBlk Entry Point
// Simple static entry point for JIT compilation

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;
using ProtonOS.Drivers.Virtio;
using ProtonOS.Drivers.Storage.Fat;

namespace ProtonOS.Drivers.Storage.VirtioBlk;

/// <summary>
/// Static entry point for VirtioBlk driver, designed for JIT compilation.
/// </summary>
public static unsafe class VirtioBlkEntry
{
    // Active device instance
    private static VirtioBlkDevice? _device;

    /// <summary>
    /// Check if this driver supports the given PCI device.
    /// </summary>
    /// <param name="vendorId">PCI vendor ID</param>
    /// <param name="deviceId">PCI device ID</param>
    /// <returns>true if this driver can handle the device</returns>
    public static bool Probe(ushort vendorId, ushort deviceId)
    {
        // Check for virtio vendor
        if (vendorId != 0x1AF4)
            return false;

        // Check for virtio-blk device IDs
        // Legacy: 0x1001, Modern: 0x1042
        return deviceId == 0x1001 || deviceId == 0x1042;
    }

    /// <summary>
    /// Bind the driver to a PCI device.
    /// </summary>
    /// <param name="bus">PCI bus number</param>
    /// <param name="device">PCI device number</param>
    /// <param name="function">PCI function number</param>
    /// <returns>true if binding succeeded</returns>
    public static bool Bind(byte bus, byte device, byte function)
    {
        // Build PciDeviceInfo from bus/device/function
        // Note: PciDeviceInfo is a class, so we need to instantiate it (not use default which would be null)
        PciDeviceInfo pciDevice = new PciDeviceInfo();
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

        // Read BARs and program any unprogrammed ones
        // For 64-bit BARs, skip the upper half index
        // Use local variable for out parameter to avoid JIT issues with array element out params
        int i = 0;
        while (i < 6)
        {
            PciBar bar;
            ReadAndProgramBar(bus, device, function, i, out bar);
            pciDevice.Bars[i] = bar;

            // If this is a 64-bit BAR, skip the next index (upper 32 bits)
            if (bar.Is64Bit)
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

        // Create and initialize the virtio block device
        Debug.WriteLine("[virtio-blk] Binding to PCI {0}:{1}.{2}", bus, device, function);
        _device = new VirtioBlkDevice();

        // Initialize virtio device (handles modern/legacy detection, feature negotiation)
        // Cast to VirtioDevice to ensure we call the base method, not the IDriver.Initialize()
        bool initResult = ((VirtioDevice)_device).Initialize(pciDevice);
        if (!initResult)
        {
            Debug.WriteLine("[virtio-blk] VirtioDevice.Initialize failed");
            _device = null;
            return false;
        }

        // Initialize block-specific functionality
        bool blkResult = _device.InitializeBlockDevice();
        if (!blkResult)
        {
            Debug.WriteLine("[virtio-blk] InitializeBlockDevice failed");
            _device.Dispose();
            _device = null;
            return false;
        }

        Debug.WriteLine("[virtio-blk] Driver bound successfully");
        return true;
    }

    /// <summary>
    /// Get the active block device instance.
    /// </summary>
    public static VirtioBlkDevice? GetDevice() => _device;

    /// <summary>
    /// Test reading from the block device. Returns 1 on success, 0 on failure.
    /// </summary>
    public static int TestRead()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-blk] TestRead: No device bound");
            return 0;
        }

        Debug.WriteLine("[virtio-blk] TestRead: Reading sector 0...");

        // Debug: print device state
        Debug.Write("[virtio-blk] BlockSize=");
        Debug.WriteHex(_device.BlockSize);
        Debug.Write(" BlockCount=");
        Debug.WriteHex(_device.BlockCount);
        Debug.WriteLine();

        // Allocate buffer for one sector (512 bytes typically)
        uint blockSize = _device.BlockSize;
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong bufferPhys = Memory.AllocatePages(pageCount);
        if (bufferPhys == 0)
        {
            Debug.WriteLine("[virtio-blk] TestRead: Failed to allocate buffer");
            return 0;
        }

        // Convert to virtual address for access
        byte* buffer = (byte*)Memory.PhysToVirt(bufferPhys);

        // Clear buffer
        for (uint i = 0; i < blockSize; i++)
            buffer[i] = 0;

        // Read sector 0
        Debug.WriteLine("[virtio-blk] TestRead: Calling _device.Read...");
        int result = _device.Read(0, 1, buffer);
        Debug.Write("[virtio-blk] TestRead: _device.Read returned ");
        Debug.WriteHex((uint)result);
        Debug.WriteLine();

        if (result == 1)
        {
            Debug.WriteLine("[virtio-blk] TestRead: Read successful!");

            // Print first 16 bytes as hex
            Debug.Write("[virtio-blk] First 16 bytes: ");
            for (int i = 0; i < 16; i++)
            {
                Debug.WriteHex(buffer[i]);
            }
            Debug.WriteLine();

            // Also print capacity info
            Debug.WriteLine("[virtio-blk] Capacity: {0} blocks x {1} bytes = {2} MB",
                _device.BlockCount, _device.BlockSize,
                (_device.BlockCount * _device.BlockSize) / (1024 * 1024));

            Memory.FreePages(bufferPhys, pageCount);
            return 1;
        }
        else
        {
            Debug.WriteLine("[virtio-blk] TestRead: Read failed with result {0}", result);
            Memory.FreePages(bufferPhys, pageCount);
            return 0;
        }
    }

    /// <summary>
    /// Test writing to the block device. Returns 1 on success, 0 on failure.
    /// Writes to sector 1000 (far from boot sector) with test pattern, then reads back to verify.
    /// </summary>
    public static int TestWrite()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-blk] TestWrite: No device bound");
            return 0;
        }

        // Use sector 1000 (far from boot sector)
        ulong testSector = 1000;
        uint blockSize = _device.BlockSize;

        Debug.WriteLine("[virtio-blk] TestWrite: Writing test pattern to sector {0}...", testSector);

        // Allocate buffers for write data and read-back verification
        ulong pageCount = (blockSize + 4095) / 4096;
        ulong writePhys = Memory.AllocatePages(pageCount);
        ulong readPhys = Memory.AllocatePages(pageCount);
        if (writePhys == 0 || readPhys == 0)
        {
            Debug.WriteLine("[virtio-blk] TestWrite: Failed to allocate buffers");
            if (writePhys != 0) Memory.FreePages(writePhys, pageCount);
            if (readPhys != 0) Memory.FreePages(readPhys, pageCount);
            return 0;
        }

        byte* writeBuffer = (byte*)Memory.PhysToVirt(writePhys);
        byte* readBuffer = (byte*)Memory.PhysToVirt(readPhys);

        // Fill write buffer with test pattern
        for (uint i = 0; i < blockSize; i++)
        {
            writeBuffer[i] = (byte)(i ^ 0xA5);  // XOR with 0xA5 for distinct pattern
        }

        // Write the sector
        int writeResult = _device.Write(testSector, 1, writeBuffer);
        if (writeResult != 1)
        {
            Debug.WriteLine("[virtio-blk] TestWrite: Write failed with result {0}", writeResult);
            Memory.FreePages(writePhys, pageCount);
            Memory.FreePages(readPhys, pageCount);
            return 0;
        }
        Debug.WriteLine("[virtio-blk] TestWrite: Write succeeded");

        // Clear read buffer
        for (uint i = 0; i < blockSize; i++)
        {
            readBuffer[i] = 0;
        }

        // Read back the sector
        int readResult = _device.Read(testSector, 1, readBuffer);
        if (readResult != 1)
        {
            Debug.WriteLine("[virtio-blk] TestWrite: Read-back failed with result {0}", readResult);
            Memory.FreePages(writePhys, pageCount);
            Memory.FreePages(readPhys, pageCount);
            return 0;
        }
        Debug.WriteLine("[virtio-blk] TestWrite: Read-back succeeded");

        // Verify data matches
        int mismatchCount = 0;
        for (uint i = 0; i < blockSize; i++)
        {
            if (writeBuffer[i] != readBuffer[i])
            {
                mismatchCount++;
                if (mismatchCount <= 3)
                {
                    Debug.Write("[virtio-blk] Mismatch at ");
                    Debug.WriteHex(i);
                    Debug.Write(": wrote ");
                    Debug.WriteHex(writeBuffer[i]);
                    Debug.Write(" read ");
                    Debug.WriteHex(readBuffer[i]);
                    Debug.WriteLine();
                }
            }
        }

        Memory.FreePages(writePhys, pageCount);
        Memory.FreePages(readPhys, pageCount);

        if (mismatchCount == 0)
        {
            Debug.WriteLine("[virtio-blk] TestWrite: Verification PASSED - all {0} bytes match", blockSize);
            return 1;
        }
        else
        {
            Debug.WriteLine("[virtio-blk] TestWrite: Verification FAILED - {0} mismatches", mismatchCount);
            return 0;
        }
    }

    /// <summary>
    /// Test mounting a FAT filesystem on the block device. Returns 1 on success, 0 on failure.
    /// </summary>
    public static int TestFatMount()
    {
        if (_device == null)
        {
            Debug.WriteLine("[virtio-blk] TestFatMount: No device bound");
            return 0;
        }

        Debug.WriteLine("[virtio-blk] TestFatMount: Creating FAT filesystem...");

        // Create FAT filesystem driver
        var fatFs = new FatFileSystem();

        // Probe to check if it's a FAT filesystem
        if (!fatFs.Probe(_device))
        {
            Debug.WriteLine("[virtio-blk] TestFatMount: Probe failed - not a FAT filesystem");
            return 0;
        }
        Debug.WriteLine("[virtio-blk] TestFatMount: FAT filesystem detected");

        // Mount the filesystem
        var mountResult = fatFs.Mount(_device, readOnly: false);
        if (mountResult != FileResult.Success)
        {
            Debug.WriteLine("[virtio-blk] TestFatMount: Mount failed with result {0}", (int)mountResult);
            return 0;
        }
        Debug.WriteLine("[virtio-blk] TestFatMount: Mounted {0} filesystem", fatFs.FilesystemName);

        // Test direct FAT operations (skip VFS for now)
        Debug.WriteLine("[virtio-blk] TestFatMount: Testing direct FAT directory access...");

        // Open root directory directly on the FAT filesystem
        IDirectoryHandle? dirHandle;
        var openResult = fatFs.OpenDirectory("/", out dirHandle);
        if (openResult != FileResult.Success || dirHandle == null)
        {
            Debug.Write("[virtio-blk] TestFatMount: OpenDirectory failed with result ");
            Debug.WriteDecimal((int)openResult);
            Debug.WriteLine();
            fatFs.Unmount();
            return 0;
        }
        Debug.WriteLine("[virtio-blk] TestFatMount: Root directory opened");

        // List root directory
        Debug.WriteLine("[virtio-blk] TestFatMount: Listing root directory contents...");
        int fileCount = 0;
        // Use explicit loop to avoid JIT issues with combined assignment-null-check
        while (true)
        {
            var entry = dirHandle.ReadNext();
            if (entry == null)
                break;
            Debug.Write("[virtio-blk]   ");
            if (entry.IsDirectory)
                Debug.Write("[DIR]  ");
            else
                Debug.Write("[FILE] ");
            Debug.Write(entry.Name);
            if (!entry.IsDirectory)
            {
                Debug.Write(" (");
                Debug.WriteDecimal((int)entry.Size);
                Debug.Write(" bytes)");
            }
            Debug.WriteLine();
            fileCount++;
        }
        dirHandle.Dispose();

        Debug.Write("[virtio-blk] TestFatMount: Found ");
        Debug.WriteDecimal(fileCount);
        Debug.WriteLine(" entries");

        // Try to read hello.txt (should exist on test.img)
        Debug.WriteLine("[virtio-blk] TestFatMount: Trying to read /hello.txt...");
        IFileHandle? fileHandle;
        var fileOpenResult = fatFs.OpenFile("/hello.txt", FileMode.Open, FileAccess.Read, out fileHandle);
        if (fileOpenResult == FileResult.Success && fileHandle != null)
        {
            Debug.Write("[virtio-blk] TestFatMount: File opened, size = ");
            Debug.WriteDecimal((int)fileHandle.Length);
            Debug.WriteLine(" bytes");

            // Read entire file (up to 256 bytes)
            int size = (int)fileHandle.Length;
            if (size > 256) size = 256;
            byte* readBuf = stackalloc byte[size];
            int bytesRead = fileHandle.Read(readBuf, size);
            if (bytesRead > 0)
            {
                Debug.Write("[virtio-blk] TestFatMount: Content: ");
                // Build string from bytes (simple ASCII)
                char* charBuf = stackalloc char[81];
                int outIdx = 0;
                for (int i = 0; i < bytesRead && outIdx < 80; i++)
                {
                    char c = (char)readBuf[i];
                    if (c >= 32 && c < 127)
                        charBuf[outIdx++] = c;
                    else if (c == '\n' || c == '\r')
                        ; // skip newlines
                    else
                        charBuf[outIdx++] = '.';
                }
                charBuf[outIdx] = '\0';
                Debug.Write(new string(charBuf));
                Debug.WriteLine();
            }
            fileHandle.Dispose();
        }
        else
        {
            Debug.Write("[virtio-blk] TestFatMount: File not found (result ");
            Debug.WriteDecimal((int)fileOpenResult);
            Debug.WriteLine(")");
        }

        fatFs.Unmount();
        Debug.WriteLine("[virtio-blk] TestFatMount: SUCCESS - FAT filesystem works!");
        return 1;
    }

    // Static counter for allocating BAR addresses in MMIO space above 4GB
    // We use 0xC0000000 as base (3GB mark) for simplicity - below typical MMIO hole
    // NOTE: We initialize in EnsureBarAddressInitialized() because static initializers
    // may not run for JIT-compiled driver assemblies
    private static ulong _nextBarAddress;
    private static bool _barAddressInitialized;

    private static void EnsureBarAddressInitialized()
    {
        if (!_barAddressInitialized)
        {
            _nextBarAddress = 0xC0000000;
            _barAddressInitialized = true;
        }
    }

    /// <summary>
    /// Read a PCI BAR, program it if needed, and store the BAR info.
    /// This handles BARs that UEFI didn't program with addresses.
    /// </summary>
    private static unsafe void ReadAndProgramBar(byte bus, byte device, byte function, int barIndex, out PciBar result)
    {
        // Ensure static fields are initialized (may not happen automatically for JIT'd drivers)
        EnsureBarAddressInitialized();

        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        // Read original BAR value
        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        // For unprogrammed BARs (value=0), we need to probe size to determine type
        // First, probe the BAR to see if it exists at all
        PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
        uint sizeLow = PCI.ReadConfig32(bus, device, function, barOffset);
        PCI.WriteConfig32(bus, device, function, barOffset, barValue);

        // Debug: Show probe result
        Debug.WriteHex(0xBAB00000u | (uint)barIndex);
        Debug.WriteHex(barValue);
        Debug.WriteHex(sizeLow);

        // Debug: test comparisons explicitly
        // Debug.WriteHex(0xBAB00010u); // About to test sizeLow == 0
        bool isZeroSize = sizeLow == 0;
        // Debug.WriteHex(isZeroSize ? 0xBAB00011u : 0xBAB00012u);
        // Debug.WriteHex(0xBAB00020u); // About to test sizeLow == 0xFFFFFFFF
        bool isAllOnes = sizeLow == 0xFFFFFFFF;
        // Debug.WriteHex(isAllOnes ? 0xBAB00021u : 0xBAB00022u);
        // Debug.WriteHex(0xBAB00030u); // About to test OR
        bool shouldSkip = isZeroSize || isAllOnes;
        // Debug.WriteHex(shouldSkip ? 0xBAB00031u : 0xBAB00032u);

        // If sizeLow is 0 or all 1s with no valid bits, BAR doesn't exist
        if (shouldSkip)
        {
            // Debug.WriteHex(0xBAB00040u); // Inside skip branch
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = false;
            empty.IsPrefetchable = false;
            result = empty;
            // Debug.WriteHex(0xBAB00041u); // About to return
            return;
        }
        // Debug.WriteHex(0xBAB00050u); // After skip check, continuing

        // Determine BAR type from the probed value
        bool isIO = (sizeLow & 1) != 0;
        // Debug.WriteHex(isIO ? 0xBAB10001u : 0xBAB10000u);

        // Skip I/O BARs for now - we only need memory BARs for modern virtio
        if (isIO)
        {
            ReadBar(bus, device, function, barIndex, out result);
            return;
        }

        // Check if it's a 64-bit BAR (look at type bits of the probe response)
        bool is64Bit = ((sizeLow >> 1) & 3) == 2;
        bool isPrefetchable = (sizeLow & 8) != 0;

        // Get the current base address
        // IMPORTANT: Use 0UL to force ulong arithmetic and avoid JIT sign-extension bug
        ulong baseAddr = (barValue & 0xFFFFFFF0u) & 0xFFFFFFFFUL;
        uint upperBar = 0;

        if (is64Bit && barIndex < 5)
        {
            upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            Debug.WriteHex(0xBAB15000u); // Upper BAR read
            Debug.WriteHex(upperBar);
            // Use explicit ulong cast with mask to avoid sign-extension
            baseAddr |= ((ulong)upperBar & 0xFFFFFFFFUL) << 32;
            Debug.WriteHex(0xBAB15001u); // After OR
            Debug.WriteHex((uint)(baseAddr >> 32));
            Debug.WriteHex((uint)baseAddr);
        }

        // Calculate size
        // IMPORTANT: Mask with 0xFFFFFFFFUL to avoid JIT sign-extension bug
        ulong size;
        if (is64Bit && barIndex < 5)
        {
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), 0xFFFFFFFF);
            uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), upperBar);

            ulong sizeMask = (((ulong)sizeHigh & 0xFFFFFFFFUL) << 32) | ((sizeLow & 0xFFFFFFF0u) & 0xFFFFFFFFUL);
            size = ~sizeMask + 1;
        }
        else
        {
            ulong masked = (sizeLow & 0xFFFFFFF0u) & 0xFFFFFFFFUL;
            size = (~masked + 1) & 0xFFFFFFFFUL;
        }

        // Debug.WriteHex(0xBAB20000u | (uint)barIndex); // size/base snapshot
        // Debug.WriteHex((uint)(baseAddr >> 32));
        // Debug.WriteHex((uint)baseAddr);
        // Debug.WriteHex((uint)(size >> 32));
        // Debug.WriteHex((uint)size);

        // Handle non-zero base address case (BAR already programmed by firmware)
        // Use early return pattern to avoid JIT brfalse bug with large if-blocks
        if (baseAddr != 0)
        {
            // Debug.WriteHex(0xBAB27000u | (uint)barIndex); // non-zero path
            PciBar earlyResult;
            earlyResult.Index = barIndex;
            earlyResult.BaseAddress = baseAddr;
            earlyResult.Size = size;
            earlyResult.IsIO = false;
            earlyResult.Is64Bit = is64Bit;
            earlyResult.IsPrefetchable = isPrefetchable;
            result = earlyResult;
            return;
        }

        // Base address is 0 - need to program the BAR if size is non-zero
        if (size == 0)
        {
            // BAR exists but has no size - return empty
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = is64Bit;
            empty.IsPrefetchable = isPrefetchable;
            result = empty;
            return;
        }

        // Align the address to the BAR size (BARs must be naturally aligned)
        ulong alignedAddr = (_nextBarAddress + size - 1) & ~(size - 1);

        // Debug.WriteHex(0xBAB30000u | (uint)barIndex); // programming BAR
        // Debug.WriteHex((uint)(alignedAddr >> 32));
        // Debug.WriteHex((uint)alignedAddr);

        // Program the BAR
        if (is64Bit && barIndex < 5)
        {
            // 64-bit BAR: program both low and high parts
            uint newLow = (uint)(alignedAddr & 0xFFFFFFF0) | (sizeLow & 0xF);
            uint newHigh = (uint)(alignedAddr >> 32);

            PCI.WriteConfig32(bus, device, function, barOffset, newLow);
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), newHigh);

            baseAddr = alignedAddr;
        }
        else
        {
            // 32-bit BAR
            uint newValue = (uint)(alignedAddr & 0xFFFFFFF0) | (sizeLow & 0xF);
            PCI.WriteConfig32(bus, device, function, barOffset, newValue);
            baseAddr = alignedAddr & 0xFFFFFFFF;
        }

        // Update next available address
        _nextBarAddress = alignedAddr + size;

        // Debug.WriteHex(0xBAB30080u | (uint)barIndex); // next free addr
        // Debug.WriteHex((uint)(_nextBarAddress >> 32));
        // Debug.WriteHex((uint)_nextBarAddress);

        // Enable memory space decoding in the PCI command register
        ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
        if ((cmd & 0x02) == 0)
        {
            cmd |= 0x02;
            PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);
        }

        // Return the programmed BAR info
        PciBar local;
        local.Index = barIndex;
        local.BaseAddress = baseAddr;
        local.Size = size;
        local.IsIO = false;
        local.Is64Bit = is64Bit;
        local.IsPrefetchable = isPrefetchable;
        result = local;
    }

    /// <summary>
    /// Read a PCI I/O BAR and its size (used only for I/O BARs).
    /// </summary>
    private static void ReadBar(byte bus, byte device, byte function, int barIndex, out PciBar result)
    {
        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        // Read original BAR value
        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        // Check if BAR is valid
        if (barValue == 0)
        {
            PciBar empty;
            empty.Index = barIndex;
            empty.BaseAddress = 0;
            empty.Size = 0;
            empty.IsIO = false;
            empty.Is64Bit = false;
            empty.IsPrefetchable = false;
            result = empty;
            return;
        }

        bool isIO = (barValue & 1) != 0;

        if (isIO)
        {
            // I/O BAR
            ulong baseAddr = barValue & 0xFFFFFFFC;

            // Read size by writing all 1s
            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeValue = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size = ~(sizeValue & 0xFFFFFFFC) + 1;
            size &= 0xFFFF; // I/O BARs are 16-bit

            PciBar local;
            local.Index = barIndex;
            local.BaseAddress = baseAddr;
            local.Size = size;
            local.IsIO = true;
            local.Is64Bit = false;
            local.IsPrefetchable = false;
            result = local;
        }
        else
        {
            // Memory BAR
            bool is64Bit = ((barValue >> 1) & 3) == 2;
            bool isPrefetchable = (barValue & 8) != 0;

            ulong baseAddr = barValue & 0xFFFFFFF0;
            uint upperBar = 0;

            // For 64-bit BARs, read upper 32 bits
            if (is64Bit && barIndex < 5)
            {
                upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                baseAddr |= (ulong)upperBar << 32;
            }

            // Read size by writing all 1s
            PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
            uint sizeLow = PCI.ReadConfig32(bus, device, function, barOffset);
            PCI.WriteConfig32(bus, device, function, barOffset, barValue);

            ulong size;
            if (is64Bit && barIndex < 5)
            {
                // For 64-bit BARs, also probe the upper BAR
                PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), 0xFFFFFFFF);
                uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
                PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), upperBar);

                // Combine and calculate size
                ulong sizeMask = ((ulong)sizeHigh << 32) | (sizeLow & 0xFFFFFFF0);
                size = ~sizeMask + 1;
            }
            else
            {
                size = ~((ulong)(sizeLow & 0xFFFFFFF0)) + 1;
                size &= 0xFFFFFFFF; // 32-bit BAR
            }

            PciBar local;
            local.Index = barIndex;
            local.BaseAddress = baseAddr;
            local.Size = size;
            local.IsIO = false;
            local.Is64Bit = is64Bit;
            local.IsPrefetchable = isPrefetchable;
            result = local;
        }
    }
}
