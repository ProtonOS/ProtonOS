// ProtonOS kernel - I/O APIC Driver
// Manages interrupt routing from external devices to CPUs.

using ProtonOS.Platform;
using ProtonOS.Memory;

namespace ProtonOS.X64;

/// <summary>
/// I/O APIC register offsets (accessed via IOREGSEL/IOWIN)
/// </summary>
public static class IOAPICRegisters
{
    public const uint Id = 0x00;               // I/O APIC ID
    public const uint Version = 0x01;          // I/O APIC Version
    public const uint Arbitration = 0x02;      // Arbitration ID
    public const uint RedirectionTableBase = 0x10;  // First redirection entry (each entry is 2 registers)
}

/// <summary>
/// I/O APIC Redirection Entry flags (64-bit entry)
/// </summary>
public static class IOAPICRedirection
{
    // Bits 0-7: Interrupt vector
    public const ulong VectorMask = 0xFF;

    // Bits 8-10: Delivery Mode
    public const ulong DeliveryFixed = 0 << 8;       // Fixed interrupt
    public const ulong DeliveryLowestPriority = 1 << 8;  // Lowest priority
    public const ulong DeliverySmi = 2 << 8;         // SMI
    public const ulong DeliveryNmi = 4 << 8;         // NMI
    public const ulong DeliveryInit = 5 << 8;        // INIT
    public const ulong DeliveryExtInt = 7 << 8;      // ExtINT (8259 mode)

    // Bit 11: Destination Mode
    public const ulong DestPhysical = 0 << 11;       // Physical APIC ID
    public const ulong DestLogical = 1 << 11;        // Logical APIC ID

    // Bit 12: Delivery Status (read-only)
    public const ulong StatusIdle = 0 << 12;
    public const ulong StatusPending = 1 << 12;

    // Bit 13: Polarity
    public const ulong PolarityActiveHigh = 0 << 13;
    public const ulong PolarityActiveLow = 1 << 13;

    // Bit 14: Remote IRR (read-only, level-triggered only)
    public const ulong RemoteIrr = 1 << 14;

    // Bit 15: Trigger Mode
    public const ulong TriggerEdge = 0 << 15;
    public const ulong TriggerLevel = 1 << 15;

    // Bit 16: Mask
    public const ulong Masked = 1 << 16;

    // Bits 56-63: Destination (for physical mode, APIC ID in bits 56-59)
}

/// <summary>
/// I/O APIC driver for routing external interrupts
/// </summary>
public static unsafe class IOAPIC
{
    // Standard ISA IRQ to IDT vector mapping (IRQ + 32)
    private const int IsaIrqBase = 32;

    // Maximum number of I/O APICs supported
    private const int MaxIOApics = 8;

    // State for each I/O APIC
    private struct IOApicState
    {
        public ulong BaseAddress;      // MMIO base address
        public uint GsiBase;           // Global System Interrupt base
        public uint MaxRedirEntries;   // Number of redirection entries
        public byte Id;                // I/O APIC ID
    }

    private static IOApicState* _ioApics;
    private static int _ioApicCount;
    private static bool _initialized;

    /// <summary>
    /// Whether the I/O APIC is initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initialize all I/O APICs found in MADT
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        DebugConsole.WriteLine("[IOAPIC] Initializing...");

        int count = CPUTopology.IOApicCount;
        if (count == 0)
        {
            DebugConsole.WriteLine("[IOAPIC] No I/O APICs found in MADT");
            return false;
        }

        // Allocate state array
        _ioApics = (IOApicState*)HeapAllocator.AllocZeroed((ulong)(sizeof(IOApicState) * MaxIOApics));
        if (_ioApics == null)
        {
            DebugConsole.WriteLine("[IOAPIC] Failed to allocate state array");
            return false;
        }

        // Initialize each I/O APIC
        for (int i = 0; i < count && i < MaxIOApics; i++)
        {
            var info = CPUTopology.GetIOApic(i);
            if (info == null)
                continue;

            InitIOApic(i, info);
            _ioApicCount++;
        }

        // Mask all ISA IRQs initially (they'll be unmasked as drivers register)
        // IRQs 0-15 are the standard ISA IRQs
        for (int irq = 0; irq < 16; irq++)
        {
            MaskIrq(irq);
        }

        // Disable legacy 8259 PICs if present
        if (CPUTopology.HasLegacyPics)
        {
            DisableLegacyPic();
        }

        _initialized = true;

        DebugConsole.Write("[IOAPIC] Initialized ");
        DebugConsole.WriteDecimal(_ioApicCount);
        DebugConsole.WriteLine(" I/O APIC(s)");

        return true;
    }

    /// <summary>
    /// Initialize a single I/O APIC
    /// </summary>
    private static void InitIOApic(int index, IOApicInfo* info)
    {
        _ioApics[index].BaseAddress = info->Address;
        _ioApics[index].GsiBase = info->GsiBase;
        _ioApics[index].Id = info->IOApicId;

        // Read version register to get max redirection entries
        uint version = ReadRegister(info->Address, IOAPICRegisters.Version);
        _ioApics[index].MaxRedirEntries = ((version >> 16) & 0xFF) + 1;

        DebugConsole.Write("[IOAPIC] #");
        DebugConsole.WriteDecimal(index);
        DebugConsole.Write(" ID ");
        DebugConsole.WriteDecimal(info->IOApicId);
        DebugConsole.Write(" at 0x");
        DebugConsole.WriteHex(info->Address);
        DebugConsole.Write(" GSI ");
        DebugConsole.WriteDecimal((int)info->GsiBase);
        DebugConsole.Write("-");
        DebugConsole.WriteDecimal((int)(info->GsiBase + _ioApics[index].MaxRedirEntries - 1));
        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Read an I/O APIC register
    /// </summary>
    private static uint ReadRegister(ulong baseAddress, uint reg)
    {
        // Write register index to IOREGSEL (offset 0x00)
        *(uint*)baseAddress = reg;
        // Read value from IOWIN (offset 0x10)
        return *(uint*)(baseAddress + 0x10);
    }

    /// <summary>
    /// Write an I/O APIC register
    /// </summary>
    private static void WriteRegister(ulong baseAddress, uint reg, uint value)
    {
        // Write register index to IOREGSEL (offset 0x00)
        *(uint*)baseAddress = reg;
        // Write value to IOWIN (offset 0x10)
        *(uint*)(baseAddress + 0x10) = value;
    }

    /// <summary>
    /// Read a 64-bit redirection entry
    /// </summary>
    private static ulong ReadRedirectionEntry(ulong baseAddress, int entry)
    {
        uint regLow = IOAPICRegisters.RedirectionTableBase + (uint)(entry * 2);
        uint regHigh = regLow + 1;

        uint low = ReadRegister(baseAddress, regLow);
        uint high = ReadRegister(baseAddress, regHigh);

        return ((ulong)high << 32) | low;
    }

    /// <summary>
    /// Write a 64-bit redirection entry
    /// </summary>
    private static void WriteRedirectionEntry(ulong baseAddress, int entry, ulong value)
    {
        uint regLow = IOAPICRegisters.RedirectionTableBase + (uint)(entry * 2);
        uint regHigh = regLow + 1;

        // Write high first (contains destination), then low (which enables the entry)
        WriteRegister(baseAddress, regHigh, (uint)(value >> 32));
        WriteRegister(baseAddress, regLow, (uint)(value & 0xFFFFFFFF));
    }

    /// <summary>
    /// Find the I/O APIC responsible for a given GSI
    /// </summary>
    private static int FindIOApicForGsi(uint gsi)
    {
        for (int i = 0; i < _ioApicCount; i++)
        {
            uint gsiEnd = _ioApics[i].GsiBase + _ioApics[i].MaxRedirEntries - 1;
            if (gsi >= _ioApics[i].GsiBase && gsi <= gsiEnd)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Convert an ISA IRQ to a GSI, handling interrupt source overrides
    /// </summary>
    private static uint IrqToGsi(int irq, out ushort flags)
    {
        flags = 0;

        // Check for interrupt source override
        var ovr = CPUTopology.GetOverride((byte)irq);
        if (ovr != null)
        {
            flags = ovr->Flags;
            return ovr->Gsi;
        }

        // No override - IRQ maps directly to GSI
        return (uint)irq;
    }

    /// <summary>
    /// Set the routing for an IRQ to deliver to a specific CPU with a specific vector
    /// </summary>
    public static void SetIrqRoute(int irq, int vector, uint destApicId)
    {
        if (!_initialized || irq < 0 || irq > 23)
            return;

        // Convert IRQ to GSI
        ushort flags;
        uint gsi = IrqToGsi(irq, out flags);

        // Find the I/O APIC for this GSI
        int ioApicIndex = FindIOApicForGsi(gsi);
        if (ioApicIndex < 0)
        {
            DebugConsole.Write("[IOAPIC] No I/O APIC for GSI ");
            DebugConsole.WriteDecimal((int)gsi);
            DebugConsole.WriteLine();
            return;
        }

        // Calculate entry index within this I/O APIC
        int entry = (int)(gsi - _ioApics[ioApicIndex].GsiBase);

        // Build redirection entry
        ulong redirEntry = (ulong)vector;

        // Delivery mode: fixed
        redirEntry |= IOAPICRedirection.DeliveryFixed;

        // Destination mode: physical
        redirEntry |= IOAPICRedirection.DestPhysical;

        // Polarity from override flags (bit 1-0: 00=default, 01=high, 11=low)
        if ((flags & 0x02) != 0)  // Polarity specified
        {
            if ((flags & 0x01) != 0)  // Active low
                redirEntry |= IOAPICRedirection.PolarityActiveLow;
        }
        // else default to active high for ISA IRQs

        // Trigger mode from override flags (bit 3-2: 00=default, 01=edge, 11=level)
        if ((flags & 0x08) != 0)  // Trigger mode specified
        {
            if ((flags & 0x04) != 0)  // Level triggered
                redirEntry |= IOAPICRedirection.TriggerLevel;
        }
        // else default to edge triggered for ISA IRQs

        // Destination APIC ID in bits 56-63
        redirEntry |= ((ulong)destApicId << 56);

        // Write the entry (unmasked)
        WriteRedirectionEntry(_ioApics[ioApicIndex].BaseAddress, entry, redirEntry);
    }

    /// <summary>
    /// Mask (disable) an IRQ
    /// </summary>
    public static void MaskIrq(int irq)
    {
        if (!_initialized || irq < 0 || irq > 23)
            return;

        ushort flags;
        uint gsi = IrqToGsi(irq, out flags);

        int ioApicIndex = FindIOApicForGsi(gsi);
        if (ioApicIndex < 0)
            return;

        int entry = (int)(gsi - _ioApics[ioApicIndex].GsiBase);

        // Read current entry and set mask bit
        ulong current = ReadRedirectionEntry(_ioApics[ioApicIndex].BaseAddress, entry);
        current |= IOAPICRedirection.Masked;
        WriteRedirectionEntry(_ioApics[ioApicIndex].BaseAddress, entry, current);
    }

    /// <summary>
    /// Unmask (enable) an IRQ
    /// </summary>
    public static void UnmaskIrq(int irq)
    {
        if (!_initialized || irq < 0 || irq > 23)
            return;

        ushort flags;
        uint gsi = IrqToGsi(irq, out flags);

        int ioApicIndex = FindIOApicForGsi(gsi);
        if (ioApicIndex < 0)
            return;

        int entry = (int)(gsi - _ioApics[ioApicIndex].GsiBase);

        // Read current entry and clear mask bit
        ulong current = ReadRedirectionEntry(_ioApics[ioApicIndex].BaseAddress, entry);
        current &= ~IOAPICRedirection.Masked;
        WriteRedirectionEntry(_ioApics[ioApicIndex].BaseAddress, entry, current);
    }

    /// <summary>
    /// Set up standard ISA IRQ routing (IRQ N -> vector 32+N, to BSP)
    /// </summary>
    public static void SetupIsaIrqs()
    {
        if (!_initialized)
            return;

        uint bspApicId = CPUTopology.BspApicId;

        // Route all ISA IRQs to BSP with standard vectors
        for (int irq = 0; irq < 16; irq++)
        {
            int vector = IsaIrqBase + irq;
            SetIrqRoute(irq, vector, bspApicId);
        }

        DebugConsole.WriteLine("[IOAPIC] ISA IRQs routed to BSP");
    }

    /// <summary>
    /// Disable the legacy 8259 PICs
    /// </summary>
    private static void DisableLegacyPic()
    {
        // Mask all interrupts on both PICs
        // PIC1 command: 0x20, data: 0x21
        // PIC2 command: 0xA0, data: 0xA1

        // ICW1: Begin initialization sequence
        CPU.OutByte(0x20, 0x11);
        CPU.OutByte(0xA0, 0x11);

        // ICW2: Remap to vectors 0x20-0x27 and 0x28-0x2F
        CPU.OutByte(0x21, 0x20);
        CPU.OutByte(0xA1, 0x28);

        // ICW3: Master/slave wiring
        CPU.OutByte(0x21, 0x04);
        CPU.OutByte(0xA1, 0x02);

        // ICW4: 8086 mode
        CPU.OutByte(0x21, 0x01);
        CPU.OutByte(0xA1, 0x01);

        // Mask all interrupts
        CPU.OutByte(0x21, 0xFF);
        CPU.OutByte(0xA1, 0xFF);

        DebugConsole.WriteLine("[IOAPIC] Legacy 8259 PICs disabled");
    }
}
