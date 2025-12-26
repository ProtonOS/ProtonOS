// ProtonOS EXT2 Filesystem Driver - On-disk structures
// Based on Linux ext2 filesystem format

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.Drivers.Storage.Ext2;

/// <summary>
/// EXT2 superblock - located at byte offset 1024 from start of volume.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Ext2Superblock
{
    public uint InodesCount;          // Total inodes
    public uint BlocksCount;          // Total blocks
    public uint ReservedBlocksCount;  // Reserved blocks for superuser
    public uint FreeBlocksCount;      // Free blocks
    public uint FreeInodesCount;      // Free inodes
    public uint FirstDataBlock;       // First data block (0 for 1K blocks, 1 for larger)
    public uint LogBlockSize;         // Block size = 1024 << LogBlockSize
    public uint LogFragSize;          // Fragment size (deprecated)
    public uint BlocksPerGroup;       // Blocks per block group
    public uint FragsPerGroup;        // Fragments per group (deprecated)
    public uint InodesPerGroup;       // Inodes per block group
    public uint MTime;                // Last mount time
    public uint WTime;                // Last write time
    public ushort MntCount;           // Mount count since last fsck
    public ushort MaxMntCount;        // Max mounts before fsck required
    public ushort Magic;              // Magic number (0xEF53)
    public ushort State;              // Filesystem state
    public ushort Errors;             // What to do on errors
    public ushort MinorRevLevel;      // Minor revision level
    public uint LastCheck;            // Last fsck time
    public uint CheckInterval;        // Max time between fscks
    public uint CreatorOs;            // OS that created filesystem
    public uint RevLevel;             // Revision level
    public ushort DefResuid;          // Default UID for reserved blocks
    public ushort DefResgid;          // Default GID for reserved blocks

    // Extended superblock fields (rev >= 1)
    public uint FirstIno;             // First non-reserved inode
    public ushort InodeSize;          // Size of inode structure
    public ushort BlockGroupNr;       // Block group of this superblock
    public uint FeatureCompat;        // Compatible feature set
    public uint FeatureIncompat;      // Incompatible feature set
    public uint FeatureRoCompat;      // Read-only compatible feature set
    public fixed byte Uuid[16];       // 128-bit UUID
    public fixed byte VolumeName[16]; // Volume name
    public fixed byte LastMounted[64];// Last mount point
    public uint AlgoBitmap;           // Compression algorithm bitmap

    // Performance hints
    public byte PreallocBlocks;       // Blocks to preallocate for files
    public byte PreallocDirBlocks;    // Blocks to preallocate for dirs
    public ushort ReservedGdtBlocks;  // Reserved GDT blocks for growth

    // Journaling (ext3) - not used for ext2
    public fixed byte JournalUuid[16];
    public uint JournalInum;
    public uint JournalDev;
    public uint LastOrphan;

    // Directory indexing
    public fixed uint HashSeed[4];
    public byte DefHashVersion;
    public fixed byte Reserved1[3];

    // Other options
    public uint DefaultMountOpts;
    public uint FirstMetaBg;
    public fixed byte Reserved2[760]; // Padding to 1024 bytes
}

/// <summary>
/// EXT2 block group descriptor - 32 bytes each.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Ext2GroupDesc
{
    public uint BlockBitmap;        // Block bitmap block number
    public uint InodeBitmap;        // Inode bitmap block number
    public uint InodeTable;         // First inode table block
    public ushort FreeBlocksCount;  // Free blocks in group
    public ushort FreeInodesCount;  // Free inodes in group
    public ushort UsedDirsCount;    // Directories in group
    public ushort Pad;              // Alignment padding
    public fixed byte Reserved[12]; // Reserved for future use
}

/// <summary>
/// EXT2 inode structure - typically 128 bytes (can be larger in newer revisions).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Ext2Inode
{
    public ushort Mode;             // File mode (type and permissions)
    public ushort Uid;              // Owner UID
    public uint Size;               // Size in bytes (lower 32 bits)
    public uint Atime;              // Access time
    public uint Ctime;              // Creation time
    public uint Mtime;              // Modification time
    public uint Dtime;              // Deletion time
    public ushort Gid;              // Group ID
    public ushort LinksCount;       // Hard links count
    public uint Blocks;             // 512-byte blocks count
    public uint Flags;              // File flags
    public uint Osd1;               // OS-dependent value 1
    public fixed uint Block[15];    // Block pointers (12 direct + 3 indirect)
    public uint Generation;         // File version (for NFS)
    public uint FileAcl;            // File ACL block
    public uint DirAcl;             // Directory ACL / high 32 bits of size
    public uint FragAddr;           // Fragment address (deprecated)
    public fixed byte Osd2[12];     // OS-dependent value 2
}

/// <summary>
/// EXT2 directory entry - variable length.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Ext2DirEntry
{
    public uint Inode;              // Inode number
    public ushort RecLen;           // Directory entry length
    public byte NameLen;            // Name length
    public byte FileType;           // File type (EXT2_FT_*)
    // Name follows (up to 255 bytes, not null-terminated)
    // Use fixed buffer for easy access
    public fixed byte Name[256];
}

/// <summary>
/// EXT2 magic number.
/// </summary>
public static class Ext2Magic
{
    public const ushort MAGIC = 0xEF53;
}

/// <summary>
/// EXT2 filesystem states.
/// </summary>
public static class Ext2State
{
    public const ushort VALID = 1;    // Unmounted cleanly
    public const ushort ERROR = 2;    // Errors detected
}

/// <summary>
/// EXT2 inode mode flags (file type).
/// </summary>
public static class Ext2FileMode
{
    // File type mask
    public const ushort S_IFMT   = 0xF000;
    public const ushort S_IFSOCK = 0xC000;  // Socket
    public const ushort S_IFLNK  = 0xA000;  // Symbolic link
    public const ushort S_IFREG  = 0x8000;  // Regular file
    public const ushort S_IFBLK  = 0x6000;  // Block device
    public const ushort S_IFDIR  = 0x4000;  // Directory
    public const ushort S_IFCHR  = 0x2000;  // Character device
    public const ushort S_IFIFO  = 0x1000;  // FIFO

    // Permission bits
    public const ushort S_ISUID  = 0x0800;  // Set UID
    public const ushort S_ISGID  = 0x0400;  // Set GID
    public const ushort S_ISVTX  = 0x0200;  // Sticky bit
    public const ushort S_IRUSR  = 0x0100;  // Owner read
    public const ushort S_IWUSR  = 0x0080;  // Owner write
    public const ushort S_IXUSR  = 0x0040;  // Owner execute
    public const ushort S_IRGRP  = 0x0020;  // Group read
    public const ushort S_IWGRP  = 0x0010;  // Group write
    public const ushort S_IXGRP  = 0x0008;  // Group execute
    public const ushort S_IROTH  = 0x0004;  // Others read
    public const ushort S_IWOTH  = 0x0002;  // Others write
    public const ushort S_IXOTH  = 0x0001;  // Others execute
}

/// <summary>
/// EXT2 directory entry file types.
/// </summary>
public static class Ext2FileType
{
    public const byte FT_UNKNOWN  = 0;
    public const byte FT_REG_FILE = 1;
    public const byte FT_DIR      = 2;
    public const byte FT_CHRDEV   = 3;
    public const byte FT_BLKDEV   = 4;
    public const byte FT_FIFO     = 5;
    public const byte FT_SOCK     = 6;
    public const byte FT_SYMLINK  = 7;
}

/// <summary>
/// Special inode numbers.
/// </summary>
public static class Ext2Inodes
{
    public const uint BAD_INO         = 1;   // Bad blocks inode
    public const uint ROOT_INO        = 2;   // Root directory inode
    public const uint ACL_IDX_INO     = 3;   // ACL index inode
    public const uint ACL_DATA_INO    = 4;   // ACL data inode
    public const uint BOOT_LOADER_INO = 5;   // Boot loader inode
    public const uint UNDEL_DIR_INO   = 6;   // Undelete directory inode
    public const uint FIRST_INO       = 11;  // First non-reserved inode (rev 0)
}

/// <summary>
/// Block pointer indices in inode.
/// </summary>
public static class Ext2BlockPtrs
{
    public const int NDIR_BLOCKS = 12;       // Direct block pointers
    public const int IND_BLOCK   = 12;       // Single indirect block
    public const int DIND_BLOCK  = 13;       // Double indirect block
    public const int TIND_BLOCK  = 14;       // Triple indirect block
    public const int N_BLOCKS    = 15;       // Total block pointers
}

/// <summary>
/// Compatible feature flags.
/// </summary>
public static class Ext2FeatureCompat
{
    public const uint DIR_PREALLOC  = 0x0001;  // Directory preallocation
    public const uint IMAGIC_INODES = 0x0002;  // Imagic inodes
    public const uint HAS_JOURNAL   = 0x0004;  // Has journal (ext3)
    public const uint EXT_ATTR      = 0x0008;  // Extended attributes
    public const uint RESIZE_INO    = 0x0010;  // Resize inode
    public const uint DIR_INDEX     = 0x0020;  // Directory indexing
}

/// <summary>
/// Incompatible feature flags.
/// </summary>
public static class Ext2FeatureIncompat
{
    public const uint COMPRESSION = 0x0001;    // Compression
    public const uint FILETYPE    = 0x0002;    // Directory entries have file type
    public const uint RECOVER     = 0x0004;    // Needs recovery (ext3)
    public const uint JOURNAL_DEV = 0x0008;    // Journal device
    public const uint META_BG     = 0x0010;    // Meta block groups
}

/// <summary>
/// Read-only compatible feature flags.
/// </summary>
public static class Ext2FeatureRoCompat
{
    public const uint SPARSE_SUPER = 0x0001;   // Sparse superblock
    public const uint LARGE_FILE   = 0x0002;   // Large files (>2GB)
    public const uint BTREE_DIR    = 0x0004;   // B-tree directories
}
