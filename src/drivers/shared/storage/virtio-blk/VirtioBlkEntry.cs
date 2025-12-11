// ProtonOS VirtioBlk Entry Point
// Simple static entry point for JIT compilation

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;
using ProtonOS.Drivers.Virtio;

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
        _device = new VirtioBlkDevice();
        // Debug.WriteHex(0xBEEF0001u); // Created VirtioBlkDevice

        // Initialize virtio device (handles modern/legacy detection, feature negotiation)
        // Cast to VirtioDevice to ensure we call the base method, not the IDriver.Initialize()
        bool initResult = ((VirtioDevice)_device).Initialize(pciDevice);
        // Debug.WriteHex(initResult ? 0xFACE0001u : 0xFACE0000u);  // Trace initResult before branch
        if (!initResult)
        {
            // Debug.WriteHex(0xDEAD0001u);
            _device = null;
            return false;
        }

        // Debug.WriteHex(0xBEEF0003u); // Initialize succeeded

        // Initialize block-specific functionality
        bool blkResult = _device.InitializeBlockDevice();
        while (!blkResult)
        {
            // Debug.WriteHex(0xDEAD0002u);
            _device.Dispose();
            _device = null;
            return false;
        }

        // Debug.WriteHex(0x00010000u); // Success marker
        return true;
    }

    /// <summary>
    /// Get the active block device instance.
    /// </summary>
    public static VirtioBlkDevice? GetDevice() => _device;

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
        // Debug.WriteHex(0xBAB00000u | (uint)barIndex);
        // Debug.WriteHex(barValue);
        // Debug.WriteHex(sizeLow);

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
        ulong baseAddr = barValue & 0xFFFFFFF0;
        uint upperBar = 0;

        if (is64Bit && barIndex < 5)
        {
            upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            // Debug.WriteHex(0xBAB15000u); // Upper BAR read
            // Debug.WriteHex(upperBar);
            baseAddr |= (ulong)upperBar << 32;
            // Debug.WriteHex(0xBAB15001u); // After OR
            // Debug.WriteHex((uint)(baseAddr >> 32));
            // Debug.WriteHex((uint)baseAddr);
        }

        // Calculate size
        ulong size;
        if (is64Bit && barIndex < 5)
        {
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), 0xFFFFFFFF);
            uint sizeHigh = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), upperBar);

            ulong sizeMask = ((ulong)sizeHigh << 32) | (sizeLow & 0xFFFFFFF0);
            size = ~sizeMask + 1;
        }
        else
        {
            size = ~((ulong)(sizeLow & 0xFFFFFFF0)) + 1;
            size &= 0xFFFFFFFF;
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
