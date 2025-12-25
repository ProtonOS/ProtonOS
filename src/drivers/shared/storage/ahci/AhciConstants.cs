// ProtonOS AHCI Driver - Constants and Definitions
// AHCI 1.3.1 Specification compliant

using System;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// HBA Generic Host Control register offsets (from BAR5).
/// </summary>
public static class HbaRegs
{
    public const int CAP = 0x00;      // Host Capabilities
    public const int GHC = 0x04;      // Global Host Control
    public const int IS = 0x08;       // Interrupt Status
    public const int PI = 0x0C;       // Ports Implemented
    public const int VS = 0x10;       // AHCI Version
    public const int CCC_CTL = 0x14;  // Command Completion Coalescing Control
    public const int CCC_PORTS = 0x18;// Command Completion Coalescing Ports
    public const int EM_LOC = 0x1C;   // Enclosure Management Location
    public const int EM_CTL = 0x20;   // Enclosure Management Control
    public const int CAP2 = 0x24;     // Host Capabilities Extended
    public const int BOHC = 0x28;     // BIOS/OS Handoff Control and Status
}

/// <summary>
/// Port register offsets (from BAR5 + 0x100 + port * 0x80).
/// </summary>
public static class PortRegs
{
    public const int CLB = 0x00;      // Command List Base Address (low 32 bits)
    public const int CLBU = 0x04;     // Command List Base Address (high 32 bits)
    public const int FB = 0x08;       // FIS Base Address (low 32 bits)
    public const int FBU = 0x0C;      // FIS Base Address (high 32 bits)
    public const int IS = 0x10;       // Interrupt Status
    public const int IE = 0x14;       // Interrupt Enable
    public const int CMD = 0x18;      // Command and Status
    public const int TFD = 0x20;      // Task File Data
    public const int SIG = 0x24;      // Signature
    public const int SSTS = 0x28;     // SATA Status (SCR0: SStatus)
    public const int SCTL = 0x2C;     // SATA Control (SCR2: SControl)
    public const int SERR = 0x30;     // SATA Error (SCR1: SError)
    public const int SACT = 0x34;     // SATA Active (NCQ)
    public const int CI = 0x38;       // Command Issue
    public const int SNTF = 0x3C;     // SATA Notification
    public const int FBS = 0x40;      // FIS-Based Switching Control
    public const int DEVSLP = 0x44;   // Device Sleep

    // Port registers are at 0x100 + port * 0x80
    public const int PORT_BASE = 0x100;
    public const int PORT_SIZE = 0x80;
}

/// <summary>
/// HBA Capabilities (CAP) register bits.
/// </summary>
[Flags]
public enum HbaCap : uint
{
    NP_MASK = 0x1F,        // Number of Ports (0-based, so add 1)
    SXS = 1u << 5,         // Supports External SATA
    EMS = 1u << 6,         // Enclosure Management Supported
    CCCS = 1u << 7,        // Command Completion Coalescing Supported
    NCS_MASK = 0x1F00,     // Number of Command Slots (shift >> 8, 0-based)
    NCS_SHIFT = 8,
    PSC = 1u << 13,        // Partial State Capable
    SSC = 1u << 14,        // Slumber State Capable
    PMD = 1u << 15,        // PIO Multiple DRQ Block
    FBSS = 1u << 16,       // FIS-based Switching Supported
    SPM = 1u << 17,        // Supports Port Multiplier
    SAM = 1u << 18,        // Supports AHCI Mode Only
    ISS_MASK = 0xF00000,   // Interface Speed Support (shift >> 20)
    ISS_SHIFT = 20,
    SCLO = 1u << 24,       // Supports Command List Override
    SAL = 1u << 25,        // Supports Activity LED
    SALP = 1u << 26,       // Supports Aggressive Link Power Management
    SSS = 1u << 27,        // Supports Staggered Spin-up
    SMPS = 1u << 28,       // Supports Mechanical Presence Switch
    SSNTF = 1u << 29,      // Supports SNotification Register
    SNCQ = 1u << 30,       // Supports Native Command Queuing
    S64A = 1u << 31,       // Supports 64-bit Addressing
}

/// <summary>
/// Global Host Control (GHC) register bits.
/// </summary>
[Flags]
public enum HbaGhc : uint
{
    HR = 1u << 0,          // HBA Reset
    IE = 1u << 1,          // Interrupt Enable
    MRSM = 1u << 2,        // MSI Revert to Single Message
    AE = 1u << 31,         // AHCI Enable
}

/// <summary>
/// Port Command and Status (PxCMD) register bits.
/// </summary>
[Flags]
public enum PortCmd : uint
{
    ST = 1u << 0,          // Start (DMA engine)
    SUD = 1u << 1,         // Spin-Up Device
    POD = 1u << 2,         // Power On Device
    CLO = 1u << 3,         // Command List Override
    FRE = 1u << 4,         // FIS Receive Enable
    CCS_MASK = 0x1F00,     // Current Command Slot (read-only)
    CCS_SHIFT = 8,
    MPSS = 1u << 13,       // Mechanical Presence Switch State
    FR = 1u << 14,         // FIS Receive Running (read-only)
    CR = 1u << 15,         // Command List Running (read-only)
    CPS = 1u << 16,        // Cold Presence State
    PMA = 1u << 17,        // Port Multiplier Attached
    HPCP = 1u << 18,       // Hot Plug Capable Port
    MPSP = 1u << 19,       // Mechanical Presence Switch Attached
    CPD = 1u << 20,        // Cold Presence Detection
    ESP = 1u << 21,        // External SATA Port
    FBSCP = 1u << 22,      // FIS-Based Switching Capable Port
    APSTE = 1u << 23,      // Automatic Partial to Slumber Transition Enable
    ATAPI = 1u << 24,      // Device is ATAPI
    DLAE = 1u << 25,       // Drive LED on ATAPI Enable
    ALPE = 1u << 26,       // Aggressive Link Power Management Enable
    ASP = 1u << 27,        // Aggressive Slumber / Partial
    ICC_MASK = 0xF0000000, // Interface Communication Control
    ICC_SHIFT = 28,
}

/// <summary>
/// Port SATA Status (PxSSTS) register bits.
/// </summary>
public static class PortSsts
{
    // Device Detection (DET) - bits 0:3
    public const uint DET_MASK = 0x0F;
    public const uint DET_NONE = 0x0;       // No device detected
    public const uint DET_PRESENT = 0x1;    // Device present, no PHY
    public const uint DET_PHY = 0x3;        // Device present and PHY established
    public const uint DET_OFFLINE = 0x4;    // PHY offline mode

    // Speed (SPD) - bits 4:7
    public const uint SPD_MASK = 0xF0;
    public const uint SPD_SHIFT = 4;
    public const uint SPD_NONE = 0x0;       // No device detected
    public const uint SPD_GEN1 = 0x1;       // 1.5 Gbps
    public const uint SPD_GEN2 = 0x2;       // 3.0 Gbps
    public const uint SPD_GEN3 = 0x3;       // 6.0 Gbps

    // Interface Power Management (IPM) - bits 8:11
    public const uint IPM_MASK = 0xF00;
    public const uint IPM_SHIFT = 8;
    public const uint IPM_NONE = 0x0;       // No device or not active
    public const uint IPM_ACTIVE = 0x1;     // Active state
    public const uint IPM_PARTIAL = 0x2;    // Partial power state
    public const uint IPM_SLUMBER = 0x6;    // Slumber power state
    public const uint IPM_DEVSLEEP = 0x8;   // DevSleep power state
}

/// <summary>
/// Port Task File Data (PxTFD) register bits.
/// </summary>
public static class PortTfd
{
    // Status byte (low 8 bits)
    public const uint STS_ERR = 1u << 0;    // Error
    public const uint STS_DRQ = 1u << 3;    // Data Request
    public const uint STS_DF = 1u << 5;     // Device Fault
    public const uint STS_DRDY = 1u << 6;   // Device Ready
    public const uint STS_BSY = 1u << 7;    // Busy

    public const uint STATUS_MASK = 0xFF;
    public const uint ERROR_MASK = 0xFF00;
    public const uint ERROR_SHIFT = 8;
}

/// <summary>
/// Port Interrupt Status/Enable (PxIS/PxIE) register bits.
/// </summary>
[Flags]
public enum PortInterrupt : uint
{
    DHRS = 1u << 0,        // Device to Host Register FIS Interrupt
    PSS = 1u << 1,         // PIO Setup FIS Interrupt
    DSS = 1u << 2,         // DMA Setup FIS Interrupt
    SDBS = 1u << 3,        // Set Device Bits Interrupt
    UFS = 1u << 4,         // Unknown FIS Interrupt
    DPS = 1u << 5,         // Descriptor Processed
    PCS = 1u << 6,         // Port Connect Change Status
    DMPS = 1u << 7,        // Device Mechanical Presence Status
    PRCS = 1u << 22,       // PhyRdy Change Status
    IPMS = 1u << 23,       // Incorrect Port Multiplier Status
    OFS = 1u << 24,        // Overflow Status
    INFS = 1u << 26,       // Interface Non-fatal Error Status
    IFS = 1u << 27,        // Interface Fatal Error Status
    HBDS = 1u << 28,       // Host Bus Data Error Status
    HBFS = 1u << 29,       // Host Bus Fatal Error Status
    TFES = 1u << 30,       // Task File Error Status
    CPDS = 1u << 31,       // Cold Port Detect Status
}

/// <summary>
/// Device signatures from PxSIG register.
/// </summary>
public static class DeviceSignature
{
    public const uint ATA = 0x00000101;     // SATA drive
    public const uint ATAPI = 0xEB140101;   // SATAPI device
    public const uint SEMB = 0xC33C0101;    // Enclosure Management Bridge
    public const uint PM = 0x96690101;      // Port Multiplier
}

/// <summary>
/// FIS (Frame Information Structure) types.
/// </summary>
public enum FisType : byte
{
    RegH2D = 0x27,         // Register FIS - Host to Device
    RegD2H = 0x34,         // Register FIS - Device to Host
    DmaActivate = 0x39,    // DMA Activate FIS
    DmaSetup = 0x41,       // DMA Setup FIS
    Data = 0x46,           // Data FIS
    Bist = 0x58,           // BIST Activate FIS
    PioSetup = 0x5F,       // PIO Setup FIS
    SetDevBits = 0xA1,     // Set Device Bits FIS
}

/// <summary>
/// ATA commands.
/// </summary>
public static class AtaCmd
{
    // Identify commands
    public const byte IDENTIFY_DEVICE = 0xEC;
    public const byte IDENTIFY_PACKET_DEVICE = 0xA1;

    // Read commands
    public const byte READ_DMA = 0xC8;
    public const byte READ_DMA_EXT = 0x25;
    public const byte READ_SECTORS = 0x20;
    public const byte READ_SECTORS_EXT = 0x24;

    // Write commands
    public const byte WRITE_DMA = 0xCA;
    public const byte WRITE_DMA_EXT = 0x35;
    public const byte WRITE_SECTORS = 0x30;
    public const byte WRITE_SECTORS_EXT = 0x34;

    // Cache commands
    public const byte FLUSH_CACHE = 0xE7;
    public const byte FLUSH_CACHE_EXT = 0xEA;

    // Power management
    public const byte STANDBY_IMMEDIATE = 0xE0;
    public const byte IDLE_IMMEDIATE = 0xE1;
    public const byte SLEEP = 0xE6;

    // Other
    public const byte SET_FEATURES = 0xEF;
    public const byte PACKET = 0xA0;
}

/// <summary>
/// Command header flags (first byte).
/// </summary>
[Flags]
public enum CmdHeaderFlags1 : byte
{
    CFL_MASK = 0x1F,       // Command FIS Length (in DWORDs, 2-16)
    A = 1 << 5,            // ATAPI command
    W = 1 << 6,            // Write (1 = H2D data transfer)
    P = 1 << 7,            // Prefetchable
}

/// <summary>
/// Command header flags (second byte).
/// </summary>
[Flags]
public enum CmdHeaderFlags2 : byte
{
    R = 1 << 0,            // Reset
    B = 1 << 1,            // BIST
    C = 1 << 2,            // Clear Busy upon R_OK
    PMP_MASK = 0xF0,       // Port Multiplier Port (shift >> 4)
    PMP_SHIFT = 4,
}

/// <summary>
/// AHCI driver constants.
/// </summary>
public static class AhciConst
{
    // Maximum ports per HBA (per AHCI spec)
    public const int MAX_PORTS = 32;

    // Maximum command slots per port
    public const int MAX_CMD_SLOTS = 32;

    // Command list size (32 entries * 32 bytes)
    public const int CMD_LIST_SIZE = MAX_CMD_SLOTS * 32;

    // Received FIS size (256 bytes, must be 256-byte aligned)
    public const int RECV_FIS_SIZE = 256;

    // Command table size (minimum 128 bytes for CFIS + ACMD + 1 PRD)
    // We allocate more for multiple PRD entries
    public const int CMD_TABLE_SIZE = 256;

    // PRD entry size
    public const int PRD_ENTRY_SIZE = 16;

    // Maximum bytes per PRD entry (4MB - 2, bit 0 must be 0)
    public const uint PRD_MAX_BYTES = 0x400000 - 2;

    // Sector size (512 bytes for most drives)
    public const uint SECTOR_SIZE = 512;

    // Timeout values (in loop iterations)
    public const int TIMEOUT_SPINUP = 1000000;
    public const int TIMEOUT_CMD = 5000000;
    public const int TIMEOUT_RESET = 1000000;
}
