// ProtonOS AHCI Driver - Data Structures
// AHCI 1.3.1 Specification compliant

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// Command header structure (32 bytes per entry, 32 entries in command list).
/// Located in system memory, pointed to by PxCLB/PxCLBU.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HbaCommandHeader
{
    // DWORD 0
    // Bits 0-4: Command FIS Length in DWORDs (2-16)
    // Bit 5: ATAPI (A)
    // Bit 6: Write (W) - direction H2D
    // Bit 7: Prefetchable (P)
    public byte Flags1;

    // Bits 0: Reset (R)
    // Bit 1: BIST (B)
    // Bit 2: Clear Busy upon R_OK (C)
    // Bits 4-7: Port Multiplier Port (PMP)
    public byte Flags2;

    // Physical Region Descriptor Table Length (number of entries)
    public ushort PrdtLength;

    // DWORD 1: Physical Region Descriptor Byte Count (updated by HBA)
    public uint PrdByteCount;

    // DWORD 2-3: Command Table Base Address (128-byte aligned)
    public ulong CtbaPhys;

    // DWORD 4-7: Reserved
    public ulong Reserved0;
    public ulong Reserved1;
}

/// <summary>
/// Physical Region Descriptor Table entry (16 bytes per entry).
/// Located at the end of the Command Table.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct HbaPrdt
{
    // DWORD 0-1: Data Base Address (2-byte aligned for DMA)
    public ulong DataBaseAddress;

    // DWORD 2: Reserved
    public uint Reserved;

    // DWORD 3:
    // Bits 0-21: Data Byte Count (0 = 1 byte, max 4MB - 1)
    // Bit 22-30: Reserved
    // Bit 31: Interrupt on Completion (I)
    public uint Dbc;
}

/// <summary>
/// Command Table structure.
/// Contains Command FIS, ATAPI Command, and PRDT entries.
/// Must be 128-byte aligned.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HbaCommandTable
{
    // Command FIS (64 bytes, only first 20 typically used for H2D Register FIS)
    public fixed byte Cfis[64];

    // ATAPI Command (16 bytes, for ATAPI devices)
    public fixed byte Acmd[16];

    // Reserved (48 bytes)
    public fixed byte Reserved[48];

    // Physical Region Descriptor Table (variable length, starts at offset 0x80)
    // We define a single entry here; actual tables may have more
    // Each entry is 16 bytes
    public HbaPrdt Prdt0;
}

/// <summary>
/// Register FIS - Host to Device (20 bytes).
/// Used to send ATA commands to the device.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FisRegH2D
{
    // DWORD 0
    public FisType FisType;    // 0x27 for H2D
    public byte Flags;         // Bit 7: C (command), Bit 6-4: Reserved, Bit 3-0: PM Port
    public byte Command;       // ATA command
    public byte FeatureLo;     // Features (7:0)

    // DWORD 1
    public byte Lba0;          // LBA (7:0)
    public byte Lba1;          // LBA (15:8)
    public byte Lba2;          // LBA (23:16)
    public byte Device;        // Device register

    // DWORD 2
    public byte Lba3;          // LBA (31:24)
    public byte Lba4;          // LBA (39:32)
    public byte Lba5;          // LBA (47:40)
    public byte FeatureHi;     // Features (15:8)

    // DWORD 3
    public byte CountLo;       // Sector Count (7:0)
    public byte CountHi;       // Sector Count (15:8)
    public byte Icc;           // Isochronous Command Completion
    public byte Control;       // Control register

    // DWORD 4
    public uint Reserved;      // Reserved (must be 0)
}

/// <summary>
/// Register FIS - Device to Host (20 bytes).
/// Received from device after command completion.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FisRegD2H
{
    // DWORD 0
    public FisType FisType;    // 0x34 for D2H
    public byte Flags;         // Bit 6: I (interrupt), Bit 3-0: PM Port
    public byte Status;        // ATA status register
    public byte Error;         // ATA error register

    // DWORD 1
    public byte Lba0;          // LBA (7:0)
    public byte Lba1;          // LBA (15:8)
    public byte Lba2;          // LBA (23:16)
    public byte Device;        // Device register

    // DWORD 2
    public byte Lba3;          // LBA (31:24)
    public byte Lba4;          // LBA (39:32)
    public byte Lba5;          // LBA (47:40)
    public byte Reserved0;

    // DWORD 3
    public byte CountLo;       // Sector Count (7:0)
    public byte CountHi;       // Sector Count (15:8)
    public ushort Reserved1;

    // DWORD 4
    public uint Reserved2;
}

/// <summary>
/// PIO Setup FIS - Device to Host (20 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FisPioSetup
{
    // DWORD 0
    public FisType FisType;    // 0x5F for PIO Setup
    public byte Flags;         // Bit 6: I, Bit 5: D (direction), Bit 3-0: PM Port
    public byte Status;        // ATA status register
    public byte Error;         // ATA error register

    // DWORD 1
    public byte Lba0;
    public byte Lba1;
    public byte Lba2;
    public byte Device;

    // DWORD 2
    public byte Lba3;
    public byte Lba4;
    public byte Lba5;
    public byte Reserved0;

    // DWORD 3
    public byte CountLo;
    public byte CountHi;
    public byte Reserved1;
    public byte EStatus;       // New value of status register

    // DWORD 4
    public ushort TransferCount; // Transfer count in bytes
    public ushort Reserved2;
}

/// <summary>
/// DMA Setup FIS - Bidirectional (28 bytes).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FisDmaSetup
{
    // DWORD 0
    public FisType FisType;    // 0x41 for DMA Setup
    public byte Flags;         // Bit 6: I, Bit 5: D, Bit 4: A (auto-activate), Bit 3-0: PM
    public ushort Reserved0;

    // DWORD 1-2: DMA Buffer Identifier
    public ulong DmaBufferId;

    // DWORD 3: Reserved
    public uint Reserved1;

    // DWORD 4: DMA Buffer Offset
    public uint DmaBufferOffset;

    // DWORD 5: Transfer Count
    public uint TransferCount;

    // DWORD 6: Reserved
    public uint Reserved2;
}

/// <summary>
/// Set Device Bits FIS - Device to Host (8 bytes).
/// Used for NCQ completion notification.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct FisSetDevBits
{
    // DWORD 0
    public FisType FisType;    // 0xA1 for Set Device Bits
    public byte Flags;         // Bit 6: I, Bit 5: N (notification), Bit 3-0: PM Port
    public byte StatusLo;      // Status Low (bits 0, 1, 2 of status)
    public byte StatusHi;      // Status Hi + Error

    // DWORD 1: Protocol specific
    public uint SActive;       // NCQ: bits set for completed commands
}

/// <summary>
/// Received FIS structure (256 bytes, 256-byte aligned).
/// HBA writes received FIS data here.
/// Located in system memory, pointed to by PxFB/PxFBU.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HbaReceivedFis
{
    // DMA Setup FIS (offset 0x00, 28 bytes)
    public FisDmaSetup DmaSetup;
    public fixed byte Reserved0[4];

    // PIO Setup FIS (offset 0x20, 20 bytes)
    public FisPioSetup PioSetup;
    public fixed byte Reserved1[12];

    // D2H Register FIS (offset 0x40, 20 bytes)
    public FisRegD2H RegD2H;
    public fixed byte Reserved2[4];

    // Set Device Bits FIS (offset 0x58, 8 bytes)
    public FisSetDevBits SetDevBits;

    // Unknown FIS (offset 0x60, 64 bytes)
    public fixed byte UnknownFis[64];

    // Reserved (offset 0xA0, 96 bytes)
    public fixed byte Reserved3[96];
}

/// <summary>
/// ATA IDENTIFY DEVICE data structure (512 bytes).
/// Returned by IDENTIFY DEVICE command.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct AtaIdentifyData
{
    // Word 0: General configuration
    public ushort GeneralConfig;

    // Words 1-9: Obsolete/Retired
    public fixed ushort Obsolete1[9];

    // Words 10-19: Serial number (20 ASCII chars, space padded)
    public fixed byte SerialNumber[20];

    // Words 20-22: Obsolete
    public fixed ushort Obsolete2[3];

    // Words 23-26: Firmware revision (8 ASCII chars)
    public fixed byte FirmwareRev[8];

    // Words 27-46: Model number (40 ASCII chars, space padded)
    public fixed byte ModelNumber[40];

    // Word 47: Max sectors per interrupt (READ/WRITE MULTIPLE)
    public ushort MaxSectorsPerInt;

    // Word 48: Trusted Computing feature set
    public ushort TrustedComputing;

    // Word 49-50: Capabilities
    public ushort Capabilities1;
    public ushort Capabilities2;

    // Words 51-52: Obsolete
    public fixed ushort Obsolete3[2];

    // Word 53: Field validity
    public ushort FieldValidity;

    // Words 54-58: Obsolete
    public fixed ushort Obsolete4[5];

    // Word 59: Current sectors per interrupt
    public ushort CurrentSectorsPerInt;

    // Words 60-61: Total addressable sectors (28-bit LBA)
    public uint TotalSectors28;

    // Word 62: Obsolete
    public ushort Obsolete5;

    // Word 63: Multiword DMA modes
    public ushort MultiwordDma;

    // Word 64: PIO modes supported
    public ushort PioModes;

    // Word 65-68: Timing info
    public ushort MinMultiwordDmaCycle;
    public ushort RecMultiwordDmaCycle;
    public ushort MinPioCycleNoFlow;
    public ushort MinPioCycleFlow;

    // Words 69-74: Reserved
    public fixed ushort Reserved1[6];

    // Words 75-79: Queue depth and SATA capabilities
    public ushort QueueDepth;
    public ushort SataCapabilities;
    public ushort SataCapabilities2;
    public ushort SataFeaturesSupported;
    public ushort SataFeaturesEnabled;

    // Word 80: Major version
    public ushort MajorVersion;

    // Word 81: Minor version
    public ushort MinorVersion;

    // Words 82-84: Command sets supported
    public ushort CmdSet1Supported;
    public ushort CmdSet2Supported;
    public ushort CmdSetExt;

    // Words 85-87: Command sets enabled
    public ushort CmdSet1Enabled;
    public ushort CmdSet2Enabled;
    public ushort CmdSetExtEnabled;

    // Word 88: Ultra DMA modes
    public ushort UltraDma;

    // Words 89-99: Various
    public fixed ushort Various[11];

    // Words 100-103: Total addressable sectors (48-bit LBA)
    public ulong TotalSectors48;

    // Words 104-127: Various
    public fixed ushort Various2[24];

    // Words 128-159: Security
    public fixed ushort Security[32];

    // Words 160-255: Reserved/Vendor specific
    public fixed ushort Reserved2[96];
}
