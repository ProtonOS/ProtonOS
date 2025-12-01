// netos mernel - ACPI table parsing
// Finds ACPI tables from UEFI configuration table to locate hardware like HPET.

using System;
using System.Runtime.InteropServices;

namespace Kernel.Platform;

// ============================================================================
// ACPI GUIDs for UEFI Configuration Table
// ============================================================================

/// <summary>
/// UEFI Configuration Table entry
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct EFIConfigurationTable
{
    public Guid VendorGuid;
    public void* VendorTable;
}

/// <summary>
/// GUID structure matching UEFI format
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Guid
{
    public uint Data1;
    public ushort Data2;
    public ushort Data3;
    public ulong Data4;  // Actually 8 bytes

    public bool Equals(uint d1, ushort d2, ushort d3, ulong d4)
    {
        return Data1 == d1 && Data2 == d2 && Data3 == d3 && Data4 == d4;
    }
}

// ============================================================================
// ACPI Table Structures
// ============================================================================

/// <summary>
/// ACPI Root System Description Pointer (RSDP) - v1
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPIRSDP
{
    public fixed byte Signature[8];    // "RSD PTR "
    public byte Checksum;
    public fixed byte OemId[6];
    public byte Revision;              // 0 = ACPI 1.0, 2 = ACPI 2.0+
    public uint RsdtAddress;           // 32-bit physical address of RSDT
}

/// <summary>
/// ACPI RSDP v2 extended fields
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPIRSDP2
{
    public ACPIRSDP V1;
    public uint Length;                // Total length of RSDP
    public ulong XsdtAddress;          // 64-bit physical address of XSDT
    public byte ExtendedChecksum;
    public fixed byte Reserved[3];
}

/// <summary>
/// Common ACPI table header
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPITableHeader
{
    public fixed byte Signature[4];    // Table signature (e.g., "XSDT", "HPET")
    public uint Length;                // Total table length including header
    public byte Revision;
    public byte Checksum;
    public fixed byte OemId[6];
    public fixed byte OemTableId[8];
    public uint OemRevision;
    public uint CreatorId;
    public uint CreatorRevision;
}

/// <summary>
/// ACPI XSDT (Extended System Description Table)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPIXSDT
{
    public ACPITableHeader Header;
    // Followed by array of 64-bit pointers to other ACPI tables
}

/// <summary>
/// ACPI RSDT (Root System Description Table) - 32-bit version
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPIRSDT
{
    public ACPITableHeader Header;
    // Followed by array of 32-bit pointers to other ACPI tables
}

/// <summary>
/// ACPI Generic Address Structure
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ACPIGenericAddress
{
    public byte AddressSpaceId;        // 0 = System Memory, 1 = System I/O
    public byte RegisterBitWidth;
    public byte RegisterBitOffset;
    public byte AccessSize;            // 0 = undefined, 1 = byte, 2 = word, 3 = dword, 4 = qword
    public ulong Address;
}

/// <summary>
/// ACPI HPET table
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPIHPET
{
    public ACPITableHeader Header;
    public uint EventTimerBlockId;
    public ACPIGenericAddress BaseAddress;
    public byte HpetNumber;
    public ushort MinimumClockTick;
    public byte PageProtection;
}

// ============================================================================
// ACPI Parser
// ============================================================================

/// <summary>
/// ACPI table parser - finds tables from UEFI configuration table
/// </summary>
public static unsafe class ACPI
{
    // ACPI 2.0 RSDP GUID: 8868E871-E4F1-11D3-BC22-0080C73C8881
    private const uint AcpiGuid1 = 0x8868E871;
    private const ushort AcpiGuid2 = 0xE4F1;
    private const ushort AcpiGuid3 = 0x11D3;
    private const ulong AcpiGuid4 = 0x8188C773800022BC;  // Byte-swapped for little-endian

    // ACPI 1.0 RSDP GUID: EB9D2D30-2D88-11D3-9A16-0090273FC14D
    private const uint Acpi1Guid1 = 0xEB9D2D30;
    private const ushort Acpi1Guid2 = 0x2D88;
    private const ushort Acpi1Guid3 = 0x11D3;
    private const ulong Acpi1Guid4 = 0x4DC13F279000169A;  // Byte-swapped for little-endian

    private static ACPIRSDP2* _rsdp;
    private static ACPIXSDT* _xsdt;
    private static ACPIRSDT* _rsdt;
    private static bool _initialized;
    private static bool _useXsdt;  // true = 64-bit XSDT, false = 32-bit RSDT

    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Initialize ACPI by finding RSDP from UEFI configuration table.
    /// Must be called before ExitBootServices (needs SystemTable).
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        var systemTable = UEFIBoot.SystemTable;
        if (systemTable == null)
        {
            DebugConsole.WriteLine("[ACPI] No UEFI system table!");
            return false;
        }

        DebugConsole.WriteLine("[ACPI] Searching for RSDP...");

        // Search configuration table for ACPI RSDP
        ulong tableCount = systemTable->NumberOfTableEntries;
        var configTable = (EFIConfigurationTable*)systemTable->ConfigurationTable;

        DebugConsole.Write("[ACPI] ConfigurationTable at 0x");
        DebugConsole.WriteHex((ulong)configTable);
        DebugConsole.Write(" entries: ");
        DebugConsole.WriteHex((ushort)tableCount);
        DebugConsole.WriteLine();

        void* rsdpPtr = null;
        bool isAcpi2 = false;

        for (ulong i = 0; i < tableCount; i++)
        {
            var entry = &configTable[i];

            // Check for ACPI 2.0+ GUID first (preferred)
            if (entry->VendorGuid.Equals(AcpiGuid1, AcpiGuid2, AcpiGuid3, AcpiGuid4))
            {
                rsdpPtr = entry->VendorTable;
                isAcpi2 = true;
                DebugConsole.WriteLine("[ACPI] Found ACPI 2.0+ RSDP");
                break;
            }

            // Check for ACPI 1.0 GUID
            if (entry->VendorGuid.Equals(Acpi1Guid1, Acpi1Guid2, Acpi1Guid3, Acpi1Guid4))
            {
                rsdpPtr = entry->VendorTable;
                isAcpi2 = false;
                DebugConsole.WriteLine("[ACPI] Found ACPI 1.0 RSDP");
                // Keep searching for 2.0 version
            }
        }

        if (rsdpPtr == null)
        {
            DebugConsole.WriteLine("[ACPI] RSDP not found!");
            return false;
        }

        // Validate RSDP signature
        var rsdp = (ACPIRSDP*)rsdpPtr;
        if (rsdp->Signature[0] != 'R' || rsdp->Signature[1] != 'S' ||
            rsdp->Signature[2] != 'D' || rsdp->Signature[3] != ' ' ||
            rsdp->Signature[4] != 'P' || rsdp->Signature[5] != 'T' ||
            rsdp->Signature[6] != 'R' || rsdp->Signature[7] != ' ')
        {
            DebugConsole.WriteLine("[ACPI] Invalid RSDP signature!");
            return false;
        }

        _rsdp = (ACPIRSDP2*)rsdpPtr;

        DebugConsole.Write("[ACPI] RSDP at 0x");
        DebugConsole.WriteHex((ulong)rsdpPtr);
        DebugConsole.Write(" revision ");
        DebugConsole.WriteHex((ushort)rsdp->Revision);
        DebugConsole.WriteLine();

        // Use XSDT if ACPI 2.0+ and XSDT address is valid
        if (isAcpi2 && rsdp->Revision >= 2 && _rsdp->XsdtAddress != 0)
        {
            _xsdt = (ACPIXSDT*)_rsdp->XsdtAddress;
            _useXsdt = true;
            DebugConsole.Write("[ACPI] XSDT at 0x");
            DebugConsole.WriteHex(_rsdp->XsdtAddress);
            DebugConsole.WriteLine();
        }
        else
        {
            _rsdt = (ACPIRSDT*)rsdp->RsdtAddress;
            _useXsdt = false;
            DebugConsole.Write("[ACPI] RSDT at 0x");
            DebugConsole.WriteHex(rsdp->RsdtAddress);
            DebugConsole.WriteLine();
        }

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Find an ACPI table by its 4-character signature
    /// </summary>
    public static ACPITableHeader* FindTable(byte sig0, byte sig1, byte sig2, byte sig3)
    {
        if (!_initialized)
            return null;

        if (_useXsdt && _xsdt != null)
        {
            // XSDT uses 64-bit pointers
            int entryCount = (int)((_xsdt->Header.Length - (uint)sizeof(ACPITableHeader)) / 8);
            ulong* entries = (ulong*)((byte*)_xsdt + sizeof(ACPITableHeader));

            for (int i = 0; i < entryCount; i++)
            {
                var table = (ACPITableHeader*)entries[i];
                if (table != null &&
                    table->Signature[0] == sig0 && table->Signature[1] == sig1 &&
                    table->Signature[2] == sig2 && table->Signature[3] == sig3)
                {
                    return table;
                }
            }
        }
        else if (_rsdt != null)
        {
            // RSDT uses 32-bit pointers
            int entryCount = (int)((_rsdt->Header.Length - (uint)sizeof(ACPITableHeader)) / 4);
            uint* entries = (uint*)((byte*)_rsdt + sizeof(ACPITableHeader));

            for (int i = 0; i < entryCount; i++)
            {
                var table = (ACPITableHeader*)(ulong)entries[i];
                if (table != null &&
                    table->Signature[0] == sig0 && table->Signature[1] == sig1 &&
                    table->Signature[2] == sig2 && table->Signature[3] == sig3)
                {
                    return table;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Find the HPET table
    /// </summary>
    public static ACPIHPET* FindHpet()
    {
        return (ACPIHPET*)FindTable((byte)'H', (byte)'P', (byte)'E', (byte)'T');
    }
}
