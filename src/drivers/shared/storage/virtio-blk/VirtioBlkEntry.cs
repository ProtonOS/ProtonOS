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
        // Use hex markers instead of strings (0xBEEF0001 = step 1, etc.)
        Debug.WriteHex(0xBEEF0001u);

        // Build PciDeviceInfo from bus/device/function
        // Note: Must manually create Bars array since struct inline field initializers
        // don't run with object initializer syntax
        Debug.WriteHex(0xBEEF0002u);
        var pciDevice = new PciDeviceInfo
        {
            Address = new PciAddress(bus, device, function),
            Bars = new PciBar[6]
        };
        Debug.WriteHex(0xBEEF0003u);

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
        Debug.WriteHex(0xBEEF0004u);

        // Debug: print the Bars array reference
        Debug.WriteHex(0xBAA00001u);  // Array ref marker
        fixed (PciBar* barsPtr = pciDevice.Bars)
        {
            Debug.WriteHex((uint)((ulong)barsPtr >> 32));
            Debug.WriteHex((uint)(ulong)barsPtr);
        }

        for (int i = 0; i < 6; i++)
        {
            // Debug: print the address of the element we're about to pass
            Debug.WriteHex(0xBAA00010u | (uint)i);  // Element addr marker + index
            fixed (PciBar* elemPtr = &pciDevice.Bars[i])
            {
                Debug.WriteHex((uint)((ulong)elemPtr >> 32));
                Debug.WriteHex((uint)(ulong)elemPtr);
            }

            // Read and program the BAR, storing result directly in the array
            ReadAndProgramBar(bus, device, function, i, out pciDevice.Bars[i]);

            // Debug: show what was stored
            Debug.WriteHex(0xBA600000u | (uint)i);
            Debug.WriteHex((uint)(pciDevice.Bars[i].BaseAddress >> 32));
            Debug.WriteHex((uint)pciDevice.Bars[i].BaseAddress);
        }
        Debug.WriteHex(0xBEEF0005u);

        // Create and initialize the virtio block device
        Debug.WriteHex(0xBEEF0006u);
        _device = new VirtioBlkDevice();
        Debug.WriteHex(0xBEEF0007u);

        // Initialize virtio device (handles modern/legacy detection, feature negotiation)
        Debug.WriteHex(0xBEEF0008u);
        if (!_device.Initialize(pciDevice))
        {
            Debug.WriteHex(0xDEADFFA1u); // Failed
            _device = null;
            return false;
        }
        Debug.WriteHex(0xBEEF0009u);

        // Initialize block-specific functionality
        Debug.WriteHex(0xBEEF000Au);
        if (!_device.InitializeBlockDevice())
        {
            Debug.WriteHex(0xDEADFFA2u); // Failed
            _device.Dispose();
            _device = null;
            return false;
        }
        Debug.WriteHex(0xBEEF000Bu);

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
        // DEBUG: Print entry marker and the address of the out parameter
        Debug.WriteHex(0xBAC00001u);  // Callee entry marker
        Debug.WriteHex((uint)barIndex);  // Show which BAR we're processing

        // Print the address of the out parameter (arg4) to debug
        fixed (PciBar* resultPtr = &result)
        {
            Debug.WriteHex(0xBAC00002u);  // out param address marker
            Debug.WriteHex((uint)((ulong)resultPtr >> 32));
            Debug.WriteHex((uint)(ulong)resultPtr);
        }

        // Ensure static fields are initialized (may not happen automatically for JIT'd drivers)
        EnsureBarAddressInitialized();

        ushort barOffset = (ushort)(PCI.PCI_BAR0 + barIndex * 4);

        // Read original BAR value
        uint barValue = PCI.ReadConfig32(bus, device, function, barOffset);

        // Debug: show raw BAR register value
        Debug.WriteHex(0xBA500000u | (uint)barIndex);
        Debug.WriteHex(barValue);

        // Check if BAR is not implemented at all
        if (barValue == 0)
        {
            Debug.WriteHex(0xBAD00001u);  // About to init empty BAR result
            // Use simple assignment to avoid complex IL from object initializer syntax
            result = default;
            Debug.WriteHex(0xBAD00003u);  // After default init
            result.Index = barIndex;
            Debug.WriteHex(0xBAD00002u);  // After init
            return;
        }

        bool isIO = (barValue & 1) != 0;

        // Skip I/O BARs for now - we only need memory BARs for modern virtio
        if (isIO)
        {
            ReadBar(bus, device, function, barIndex, out result);
            return;
        }

        // Check if it's a 64-bit BAR
        bool is64Bit = ((barValue >> 1) & 3) == 2;
        bool isPrefetchable = (barValue & 8) != 0;

        // Get the current base address
        ulong baseAddr = barValue & 0xFFFFFFF0;
        uint upperBar = 0;

        if (is64Bit && barIndex < 5)
        {
            upperBar = PCI.ReadConfig32(bus, device, function, (ushort)(barOffset + 4));
            baseAddr |= (ulong)upperBar << 32;
        }

        // Probe the BAR size
        PCI.WriteConfig32(bus, device, function, barOffset, 0xFFFFFFFF);
        uint sizeLow = PCI.ReadConfig32(bus, device, function, barOffset);
        PCI.WriteConfig32(bus, device, function, barOffset, barValue);

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

        // Debug: show what we found
        Debug.WriteHex(0xBA510000u | (uint)barIndex);
        Debug.WriteHex((uint)(baseAddr >> 32));
        Debug.WriteHex((uint)baseAddr);
        Debug.WriteHex((uint)(size >> 32));
        Debug.WriteHex((uint)size);

        // If base address is 0 but size is non-zero, we need to program the BAR
        if (baseAddr == 0 && size > 0)
        {
            Debug.WriteHex(0xBA520000u | (uint)barIndex); // Programming BAR

            // Debug: show _nextBarAddress before alignment
            Debug.WriteHex(0xBA540000u | (uint)barIndex);
            Debug.WriteHex((uint)(_nextBarAddress >> 32));
            Debug.WriteHex((uint)_nextBarAddress);

            // Align the address to the BAR size (BARs must be naturally aligned)
            ulong alignedAddr = (_nextBarAddress + size - 1) & ~(size - 1);

            // Debug: show aligned address
            Debug.WriteHex(0xBA550000u | (uint)barIndex);
            Debug.WriteHex((uint)(alignedAddr >> 32));
            Debug.WriteHex((uint)alignedAddr);

            // Program the BAR
            if (is64Bit && barIndex < 5)
            {
                // 64-bit BAR: program both low and high parts
                uint newLow = (uint)(alignedAddr & 0xFFFFFFF0) | (barValue & 0xF);
                uint newHigh = (uint)(alignedAddr >> 32);

                PCI.WriteConfig32(bus, device, function, barOffset, newLow);
                PCI.WriteConfig32(bus, device, function, (ushort)(barOffset + 4), newHigh);

                baseAddr = alignedAddr;
            }
            else
            {
                // 32-bit BAR
                uint newValue = (uint)(alignedAddr & 0xFFFFFFF0) | (barValue & 0xF);
                PCI.WriteConfig32(bus, device, function, barOffset, newValue);
                baseAddr = alignedAddr & 0xFFFFFFFF;
            }

            // Update next available address
            _nextBarAddress = alignedAddr + size;

            // Enable memory space decoding in the PCI command register
            ushort cmd = PCI.ReadConfig16(bus, device, function, PCI.PCI_COMMAND);
            if ((cmd & 0x02) == 0) // Memory space enable bit
            {
                cmd |= 0x02;
                PCI.WriteConfig16(bus, device, function, PCI.PCI_COMMAND, cmd);
            }

            Debug.WriteHex(0xBA530000u | (uint)barIndex); // BAR programmed
            Debug.WriteHex((uint)(baseAddr >> 32));
            Debug.WriteHex((uint)baseAddr);
        }

        // Initialize result struct
        result = default;
        result.Index = barIndex;
        result.BaseAddress = baseAddr;
        result.Size = size;
        result.IsIO = false;
        result.Is64Bit = is64Bit;
        result.IsPrefetchable = isPrefetchable;
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
            result = default;
            result.Index = barIndex;
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

            result = default;
            result.Index = barIndex;
            result.BaseAddress = baseAddr;
            result.Size = size;
            result.IsIO = true;
            result.Is64Bit = false;
            result.IsPrefetchable = false;
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

            result = default;
            result.Index = barIndex;
            result.BaseAddress = baseAddr;
            result.Size = size;
            result.IsIO = false;
            result.Is64Bit = is64Bit;
            result.IsPrefetchable = isPrefetchable;
        }
    }
}
