// ProtonOS FAT Filesystem Driver - Structures
// BPB and FAT-specific data structures

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Drivers.Storage.Fat;

/// <summary>
/// FAT filesystem variant.
/// </summary>
public enum FatType
{
    Unknown,
    Fat12,
    Fat16,
    Fat32,
}

/// <summary>
/// FAT Boot Sector / BIOS Parameter Block (common fields).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 36)]
public unsafe struct FatBpb
{
    [FieldOffset(0)]
    public fixed byte JmpBoot[3];       // 0x00: Jump instruction

    [FieldOffset(3)]
    public fixed byte OemName[8];       // 0x03: OEM name (e.g., "MSDOS5.0")

    [FieldOffset(11)]
    public ushort BytsPerSec;           // 0x0B: Bytes per sector (512, 1024, 2048, 4096)

    [FieldOffset(13)]
    public byte SecPerClus;             // 0x0D: Sectors per cluster (1, 2, 4, 8, 16, 32, 64, 128)

    [FieldOffset(14)]
    public ushort RsvdSecCnt;           // 0x0E: Reserved sector count

    [FieldOffset(16)]
    public byte NumFATs;                // 0x10: Number of FAT copies (usually 2)

    [FieldOffset(17)]
    public ushort RootEntCnt;           // 0x11: Root entry count (FAT12/16 only)

    [FieldOffset(19)]
    public ushort TotSec16;             // 0x13: Total sectors (16-bit, 0 if > 65535)

    [FieldOffset(21)]
    public byte Media;                  // 0x15: Media type (0xF8 for fixed disk)

    [FieldOffset(22)]
    public ushort FATSz16;              // 0x16: Sectors per FAT (FAT12/16)

    [FieldOffset(24)]
    public ushort SecPerTrk;            // 0x18: Sectors per track

    [FieldOffset(26)]
    public ushort NumHeads;             // 0x1A: Number of heads

    [FieldOffset(28)]
    public uint HiddSec;                // 0x1C: Hidden sectors

    [FieldOffset(32)]
    public uint TotSec32;               // 0x20: Total sectors (32-bit)
}

/// <summary>
/// FAT12/FAT16 Extended Boot Record (following BPB).
/// Starts at offset 36 (0x24) in the boot sector, immediately after the BPB.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 26)]
public unsafe struct Fat16Ebr
{
    [FieldOffset(0)]
    public byte DrvNum;                 // 0x24: Drive number

    [FieldOffset(1)]
    public byte Reserved1;              // 0x25: Reserved

    [FieldOffset(2)]
    public byte BootSig;                // 0x26: Extended boot signature (0x29)

    [FieldOffset(3)]
    public uint VolID;                  // 0x27: Volume serial number

    [FieldOffset(7)]
    public fixed byte VolLab[11];       // 0x2B: Volume label

    [FieldOffset(18)]
    public fixed byte FilSysType[8];    // 0x36: Filesystem type ("FAT12   ", "FAT16   ")
}

/// <summary>
/// FAT32 Extended Boot Record (following BPB).
/// Starts at offset 36 (0x24) in the boot sector, immediately after the BPB.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 54)]
public unsafe struct Fat32Ebr
{
    [FieldOffset(0)]
    public uint FATSz32;                // 0x24: Sectors per FAT (FAT32)

    [FieldOffset(4)]
    public ushort ExtFlags;             // 0x28: Extended flags

    [FieldOffset(6)]
    public ushort FSVer;                // 0x2A: Filesystem version

    [FieldOffset(8)]
    public uint RootClus;               // 0x2C: Root directory cluster

    [FieldOffset(12)]
    public ushort FSInfo;               // 0x30: FSInfo sector number

    [FieldOffset(14)]
    public ushort BkBootSec;            // 0x32: Backup boot sector

    [FieldOffset(16)]
    public fixed byte Reserved[12];     // 0x34: Reserved

    [FieldOffset(28)]
    public byte DrvNum;                 // 0x40: Drive number

    [FieldOffset(29)]
    public byte Reserved1;              // 0x41: Reserved

    [FieldOffset(30)]
    public byte BootSig;                // 0x42: Extended boot signature (0x29)

    [FieldOffset(31)]
    public uint VolID;                  // 0x43: Volume serial number

    [FieldOffset(35)]
    public fixed byte VolLab[11];       // 0x47: Volume label

    [FieldOffset(46)]
    public fixed byte FilSysType[8];    // 0x52: Filesystem type ("FAT32   ")
}

/// <summary>
/// FAT directory entry (32 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public unsafe struct FatDirEntry
{
    [FieldOffset(0)]
    public fixed byte Name[11];         // 0x00: 8.3 filename

    [FieldOffset(11)]
    public byte Attr;                   // 0x0B: File attributes

    [FieldOffset(12)]
    public byte NTRes;                  // 0x0C: Reserved (NT)

    [FieldOffset(13)]
    public byte CrtTimeTenth;           // 0x0D: Creation time tenths

    [FieldOffset(14)]
    public ushort CrtTime;              // 0x0E: Creation time

    [FieldOffset(16)]
    public ushort CrtDate;              // 0x10: Creation date

    [FieldOffset(18)]
    public ushort LstAccDate;           // 0x12: Last access date

    [FieldOffset(20)]
    public ushort FstClusHI;            // 0x14: High word of first cluster (FAT32)

    [FieldOffset(22)]
    public ushort WrtTime;              // 0x16: Write time

    [FieldOffset(24)]
    public ushort WrtDate;              // 0x18: Write date

    [FieldOffset(26)]
    public ushort FstClusLO;            // 0x1A: Low word of first cluster

    [FieldOffset(28)]
    public uint FileSize;               // 0x1C: File size in bytes
}

/// <summary>
/// FAT Long Filename Entry (32 bytes).
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 32)]
public unsafe struct FatLfnEntry
{
    [FieldOffset(0)]
    public byte Ord;                    // 0x00: Order/sequence number

    [FieldOffset(1)]
    public fixed byte Name1[10];        // 0x01: Characters 1-5 (UTF-16)

    [FieldOffset(11)]
    public byte Attr;                   // 0x0B: Attributes (always 0x0F for LFN)

    [FieldOffset(12)]
    public byte Type;                   // 0x0C: Type (0)

    [FieldOffset(13)]
    public byte Chksum;                 // 0x0D: Checksum of 8.3 name

    [FieldOffset(14)]
    public fixed byte Name2[12];        // 0x0E: Characters 6-11 (UTF-16)

    [FieldOffset(26)]
    public ushort FstClusLO;            // 0x1A: First cluster (always 0)

    [FieldOffset(28)]
    public fixed byte Name3[4];         // 0x1C: Characters 12-13 (UTF-16)
}

/// <summary>
/// FAT file attributes.
/// </summary>
[Flags]
public enum FatAttr : byte
{
    None = 0x00,
    ReadOnly = 0x01,
    Hidden = 0x02,
    System = 0x04,
    VolumeId = 0x08,
    Directory = 0x10,
    Archive = 0x20,
    LongName = ReadOnly | Hidden | System | VolumeId,  // 0x0F
}

/// <summary>
/// Special FAT cluster values.
/// </summary>
public static class FatCluster
{
    public const uint Free = 0x00000000;
    public const uint Reserved = 0x00000001;

    // End of chain markers (FAT12)
    public const uint EndOfChain12 = 0x00000FF8;
    public const uint BadCluster12 = 0x00000FF7;

    // End of chain markers (FAT16)
    public const uint EndOfChain16 = 0x0000FFF8;
    public const uint BadCluster16 = 0x0000FFF7;

    // End of chain markers (FAT32)
    public const uint EndOfChain32 = 0x0FFFFFF8;
    public const uint BadCluster32 = 0x0FFFFFF7;

    /// <summary>
    /// Check if a cluster value indicates end of chain.
    /// </summary>
    public static bool IsEndOfChain(uint cluster, FatType fatType)
    {
        return fatType switch
        {
            FatType.Fat12 => cluster >= EndOfChain12,
            FatType.Fat16 => cluster >= EndOfChain16,
            FatType.Fat32 => (cluster & 0x0FFFFFFF) >= EndOfChain32,
            _ => true,
        };
    }

    /// <summary>
    /// Check if a cluster is valid (not free, bad, or end of chain).
    /// </summary>
    public static bool IsValid(uint cluster, FatType fatType, uint maxCluster)
    {
        if (cluster < 2 || cluster > maxCluster)
            return false;

        return fatType switch
        {
            FatType.Fat12 => cluster < BadCluster12,
            FatType.Fat16 => cluster < BadCluster16,
            FatType.Fat32 => (cluster & 0x0FFFFFFF) < BadCluster32,
            _ => false,
        };
    }
}
