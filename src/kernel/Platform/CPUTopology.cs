// ProtonOS kernel - CPU Topology
// Enumerates CPUs from ACPI MADT table for SMP support.

using System.Runtime.InteropServices;

namespace ProtonOS.Platform;

/// <summary>
/// Information about a single CPU in the system
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct CpuInfo
{
    public uint CpuIndex;              // Kernel-assigned CPU index (0, 1, 2...)
    public uint ApicId;                // Hardware APIC ID
    public byte AcpiProcessorId;       // ACPI-assigned processor ID
    public uint NumaNode;              // NUMA node (proximity domain)
    public bool IsBsp;                 // Is Bootstrap Processor
    public bool IsOnline;              // Currently running
    public bool IsEnabled;             // Enabled in MADT (can be started)
}

/// <summary>
/// Information about an I/O APIC in the system
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IOApicInfo
{
    public byte IOApicId;              // I/O APIC ID
    public uint Address;               // Physical MMIO address
    public uint GsiBase;               // Global System Interrupt base
}

/// <summary>
/// Interrupt source override (ISA IRQ to GSI mapping)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InterruptOverride
{
    public byte Source;                // ISA IRQ number (0-15)
    public uint Gsi;                   // Global System Interrupt
    public ushort Flags;               // Polarity and trigger mode
}

/// <summary>
/// CPU topology enumeration from ACPI MADT
/// </summary>
public static unsafe class CPUTopology
{
    public const int MaxCpus = 64;         // Maximum supported CPUs
    public const int MaxIOApics = 8;       // Maximum I/O APICs
    public const int MaxOverrides = 24;    // Maximum interrupt overrides

    private static CpuInfo* _cpus;
    private static int _cpuCount;
    private static int _bspIndex;
    private static uint _bspApicId;

    private static IOApicInfo* _ioApics;
    private static int _ioApicCount;

    private static InterruptOverride* _overrides;
    private static int _overrideCount;

    private static uint _localApicAddress;
    private static bool _hasLegacyPics;
    private static bool _initialized;

    /// <summary>
    /// Number of CPUs detected
    /// </summary>
    public static int CpuCount => _cpuCount;

    /// <summary>
    /// Index of the Bootstrap Processor in the CPU array
    /// </summary>
    public static int BspIndex => _bspIndex;

    /// <summary>
    /// APIC ID of the Bootstrap Processor
    /// </summary>
    public static uint BspApicId => _bspApicId;

    /// <summary>
    /// Number of I/O APICs detected
    /// </summary>
    public static int IOApicCount => _ioApicCount;

    /// <summary>
    /// Physical address of Local APIC (from MADT)
    /// </summary>
    public static uint LocalApicAddress => _localApicAddress;

    /// <summary>
    /// Whether legacy 8259 PICs are present
    /// </summary>
    public static bool HasLegacyPics => _hasLegacyPics;

    /// <summary>
    /// Whether topology has been initialized
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Get CPU info by index
    /// </summary>
    public static CpuInfo* GetCpu(int index)
    {
        if (index < 0 || index >= _cpuCount)
            return null;
        return &_cpus[index];
    }

    /// <summary>
    /// Get I/O APIC info by index
    /// </summary>
    public static IOApicInfo* GetIOApic(int index)
    {
        if (index < 0 || index >= _ioApicCount)
            return null;
        return &_ioApics[index];
    }

    /// <summary>
    /// Find CPU by APIC ID
    /// </summary>
    public static CpuInfo* FindCpuByApicId(uint apicId)
    {
        for (int i = 0; i < _cpuCount; i++)
        {
            if (_cpus[i].ApicId == apicId)
                return &_cpus[i];
        }
        return null;
    }

    /// <summary>
    /// Get interrupt override for an ISA IRQ
    /// </summary>
    public static InterruptOverride* GetOverride(byte isaIrq)
    {
        for (int i = 0; i < _overrideCount; i++)
        {
            if (_overrides[i].Source == isaIrq)
                return &_overrides[i];
        }
        return null;
    }

    /// <summary>
    /// Initialize CPU topology by parsing MADT.
    /// Must be called after ACPI.Init() and before SMP.Init().
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        DebugConsole.WriteLine("[CPUTopology] Parsing MADT...");

        // Allocate storage for CPU info (static allocation for now, before heap)
        // Using a simple approach: allocate from a static buffer
        _cpus = (CpuInfo*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(CpuInfo) * MaxCpus));
        _ioApics = (IOApicInfo*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(IOApicInfo) * MaxIOApics));
        _overrides = (InterruptOverride*)Memory.HeapAllocator.AllocZeroed((ulong)(sizeof(InterruptOverride) * MaxOverrides));

        if (_cpus == null || _ioApics == null || _overrides == null)
        {
            DebugConsole.WriteLine("[CPUTopology] Failed to allocate memory!");
            return false;
        }

        // Find MADT
        var madt = ACPI.FindMadt();
        if (madt == null)
        {
            DebugConsole.WriteLine("[CPUTopology] MADT not found!");
            // Fall back to single CPU (BSP only)
            _cpuCount = 1;
            _bspIndex = 0;
            _cpus[0].CpuIndex = 0;
            _cpus[0].ApicId = 0;  // Will be filled in later from APIC
            _cpus[0].IsBsp = true;
            _cpus[0].IsOnline = true;
            _cpus[0].IsEnabled = true;
            _initialized = true;
            return true;
        }

        // Read Local APIC address from MADT header
        _localApicAddress = madt->LocalApicAddress;
        _hasLegacyPics = (madt->Flags & 1) != 0;

        DebugConsole.Write("[CPUTopology] Local APIC at 0x");
        DebugConsole.WriteHex(_localApicAddress);
        DebugConsole.Write(" Legacy PICs: ");
        DebugConsole.WriteLine(_hasLegacyPics ? "yes" : "no");

        // Parse MADT entries
        byte* entryPtr = (byte*)madt + sizeof(ACPIMADT);
        byte* endPtr = (byte*)madt + madt->Header.Length;

        while (entryPtr < endPtr)
        {
            var header = (MADTEntryHeader*)entryPtr;

            if (header->Length == 0)
            {
                DebugConsole.WriteLine("[CPUTopology] Invalid MADT entry length 0!");
                break;
            }

            switch (header->Type)
            {
                case MADTEntryType.LocalApic:
                    ParseLocalApic((MADTLocalApic*)entryPtr);
                    break;

                case MADTEntryType.IOApic:
                    ParseIOApic((MADTIOApic*)entryPtr);
                    break;

                case MADTEntryType.InterruptSourceOverride:
                    ParseInterruptSourceOverride((MADTInterruptSourceOverride*)entryPtr);
                    break;

                case MADTEntryType.LocalX2Apic:
                    ParseLocalX2Apic((MADTLocalX2Apic*)entryPtr);
                    break;

                // Other entry types can be ignored for now
            }

            entryPtr += header->Length;
        }

        // Identify BSP - it's the CPU currently executing this code
        // Read APIC ID from the Local APIC register
        _bspApicId = ReadBspApicId();

        DebugConsole.Write("[CPUTopology] BSP APIC ID: ");
        DebugConsole.WriteHex((ushort)_bspApicId);
        DebugConsole.WriteLine();

        // Find BSP in CPU list and mark it
        _bspIndex = -1;
        for (int i = 0; i < _cpuCount; i++)
        {
            if (_cpus[i].ApicId == _bspApicId)
            {
                _cpus[i].IsBsp = true;
                _cpus[i].IsOnline = true;
                _bspIndex = i;
                break;
            }
        }

        if (_bspIndex < 0)
        {
            // BSP not found in MADT - add it manually
            DebugConsole.WriteLine("[CPUTopology] BSP not found in MADT, adding manually");
            if (_cpuCount < MaxCpus)
            {
                _bspIndex = _cpuCount;
                _cpus[_cpuCount].CpuIndex = (uint)_cpuCount;
                _cpus[_cpuCount].ApicId = _bspApicId;
                _cpus[_cpuCount].IsBsp = true;
                _cpus[_cpuCount].IsOnline = true;
                _cpus[_cpuCount].IsEnabled = true;
                _cpuCount++;
            }
        }

        DebugConsole.Write("[CPUTopology] Detected ");
        DebugConsole.WriteDecimal(_cpuCount);
        DebugConsole.Write(" CPU(s), ");
        DebugConsole.WriteDecimal(_ioApicCount);
        DebugConsole.Write(" I/O APIC(s), ");
        DebugConsole.WriteDecimal(_overrideCount);
        DebugConsole.WriteLine(" IRQ override(s)");

        // Log each CPU (verbose - commented out)
        // for (int i = 0; i < _cpuCount; i++)
        // {
        //     DebugConsole.Write("[CPUTopology]   CPU ");
        //     DebugConsole.WriteDecimal(i);
        //     DebugConsole.Write(": APIC ID ");
        //     DebugConsole.WriteDecimal((int)_cpus[i].ApicId);
        //     if (_cpus[i].IsBsp)
        //         DebugConsole.Write(" (BSP)");
        //     if (!_cpus[i].IsEnabled)
        //         DebugConsole.Write(" (disabled)");
        //     DebugConsole.WriteLine();
        // }

        _initialized = true;
        return true;
    }

    private static void ParseLocalApic(MADTLocalApic* entry)
    {
        // Check if processor is usable
        bool enabled = (entry->Flags & MADTLocalApicFlags.Enabled) != 0;
        bool onlineCapable = (entry->Flags & MADTLocalApicFlags.OnlineCapable) != 0;

        if (!enabled && !onlineCapable)
            return;  // CPU cannot be used

        if (_cpuCount >= MaxCpus)
        {
            DebugConsole.WriteLine("[CPUTopology] Too many CPUs, ignoring");
            return;
        }

        _cpus[_cpuCount].CpuIndex = (uint)_cpuCount;
        _cpus[_cpuCount].ApicId = entry->ApicId;
        _cpus[_cpuCount].AcpiProcessorId = entry->AcpiProcessorId;
        _cpus[_cpuCount].IsBsp = false;  // Will be set later
        _cpus[_cpuCount].IsOnline = false;  // Will be set when AP starts
        _cpus[_cpuCount].IsEnabled = enabled;

        _cpuCount++;
    }

    private static void ParseLocalX2Apic(MADTLocalX2Apic* entry)
    {
        // Check if processor is usable
        bool enabled = (entry->Flags & MADTLocalApicFlags.Enabled) != 0;
        bool onlineCapable = (entry->Flags & MADTLocalApicFlags.OnlineCapable) != 0;

        if (!enabled && !onlineCapable)
            return;  // CPU cannot be used

        if (_cpuCount >= MaxCpus)
        {
            DebugConsole.WriteLine("[CPUTopology] Too many CPUs, ignoring");
            return;
        }

        _cpus[_cpuCount].CpuIndex = (uint)_cpuCount;
        _cpus[_cpuCount].ApicId = entry->X2ApicId;
        _cpus[_cpuCount].AcpiProcessorId = (byte)entry->AcpiProcessorUid;
        _cpus[_cpuCount].IsBsp = false;
        _cpus[_cpuCount].IsOnline = false;
        _cpus[_cpuCount].IsEnabled = enabled;

        _cpuCount++;
    }

    private static void ParseIOApic(MADTIOApic* entry)
    {
        if (_ioApicCount >= MaxIOApics)
        {
            DebugConsole.WriteLine("[CPUTopology] Too many I/O APICs, ignoring");
            return;
        }

        _ioApics[_ioApicCount].IOApicId = entry->IOApicId;
        _ioApics[_ioApicCount].Address = entry->IOApicAddress;
        _ioApics[_ioApicCount].GsiBase = entry->GlobalSystemInterruptBase;

        // I/O APIC detail debug (verbose - commented out)
        // DebugConsole.Write("[CPUTopology]   I/O APIC ");
        // DebugConsole.WriteDecimal(_ioApicCount);
        // DebugConsole.Write(": ID ");
        // DebugConsole.WriteDecimal(entry->IOApicId);
        // DebugConsole.Write(" at 0x");
        // DebugConsole.WriteHex(entry->IOApicAddress);
        // DebugConsole.Write(" GSI base ");
        // DebugConsole.WriteDecimal((int)entry->GlobalSystemInterruptBase);
        // DebugConsole.WriteLine();

        _ioApicCount++;
    }

    private static void ParseInterruptSourceOverride(MADTInterruptSourceOverride* entry)
    {
        if (_overrideCount >= MaxOverrides)
        {
            DebugConsole.WriteLine("[CPUTopology] Too many interrupt overrides, ignoring");
            return;
        }

        _overrides[_overrideCount].Source = entry->Source;
        _overrides[_overrideCount].Gsi = entry->GlobalSystemInterrupt;
        _overrides[_overrideCount].Flags = entry->Flags;

        _overrideCount++;
    }

    /// <summary>
    /// Read the BSP's APIC ID from the Local APIC ID register.
    /// This is used to identify which CPU in the MADT is the BSP.
    /// </summary>
    private static uint ReadBspApicId()
    {
        // Local APIC is at physical address 0xFEE00000 (default) or from MADT
        // The ID register is at offset 0x20
        // For now, use the default address - VirtualMemory hasn't necessarily mapped it yet
        // but in UEFI, the physical address is typically identity-mapped

        ulong apicBase = _localApicAddress != 0 ? _localApicAddress : 0xFEE00000;

        // Read APIC ID register (offset 0x20)
        // The APIC ID is in bits 24-31
        uint* idRegister = (uint*)(apicBase + 0x20);
        return (*idRegister) >> 24;
    }

    /// <summary>
    /// Mark a CPU as online (called when AP successfully starts)
    /// </summary>
    public static void SetCpuOnline(uint apicId, bool online)
    {
        for (int i = 0; i < _cpuCount; i++)
        {
            if (_cpus[i].ApicId == apicId)
            {
                _cpus[i].IsOnline = online;
                return;
            }
        }
    }

    /// <summary>
    /// Get count of online CPUs
    /// </summary>
    public static int GetOnlineCpuCount()
    {
        int count = 0;
        for (int i = 0; i < _cpuCount; i++)
        {
            if (_cpus[i].IsOnline)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Update NUMA node assignments for all CPUs.
    /// Must be called after NumaTopology.Init().
    /// </summary>
    public static void UpdateNumaInfo()
    {
        if (!_initialized || !NumaTopology.IsInitialized)
            return;

        for (int i = 0; i < _cpuCount; i++)
        {
            _cpus[i].NumaNode = NumaTopology.GetNodeForApicId(_cpus[i].ApicId);
        }

        // NUMA node assignment debug (verbose - commented out)
        // DebugConsole.WriteLine("[CPUTopology] Updated NUMA node assignments:");
        // for (int i = 0; i < _cpuCount; i++)
        // {
        //     DebugConsole.Write("[CPUTopology]   CPU ");
        //     DebugConsole.WriteDecimal(i);
        //     DebugConsole.Write(" (APIC ");
        //     DebugConsole.WriteDecimal((int)_cpus[i].ApicId);
        //     DebugConsole.Write(") -> Node ");
        //     DebugConsole.WriteDecimal((int)_cpus[i].NumaNode);
        //     DebugConsole.WriteLine();
        // }
    }
}
