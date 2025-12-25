// ProtonOS AHCI Driver - Port Implementation
// Handles individual SATA port operations

using System;
using System.Runtime.InteropServices;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// Port state.
/// </summary>
public enum AhciPortState
{
    Uninitialized,
    NoDevice,
    DevicePresent,
    Ready,
    Error
}

/// <summary>
/// Device type detected on port.
/// </summary>
public enum AhciDeviceType
{
    None,
    Ata,       // SATA drive
    Atapi,     // SATAPI device (CD/DVD)
    Semb,      // Enclosure Management Bridge
    PortMult   // Port Multiplier
}

/// <summary>
/// AHCI port handler.
/// Manages a single SATA port on the HBA.
/// </summary>
public unsafe class AhciPort : IDisposable
{
    // Port index and register base
    private readonly int _portIndex;
    private readonly byte* _portRegs;

    // Port state
    private AhciPortState _state = AhciPortState.Uninitialized;
    private AhciDeviceType _deviceType = AhciDeviceType.None;

    // DMA buffers for command list and received FIS
    private DMABuffer _cmdListBuffer;
    private DMABuffer _fisBuffer;
    private DMABuffer _cmdTableBuffer;  // Single command table for now

    // Device info from IDENTIFY
    private ulong _sectorCount;
    private uint _sectorSize = AhciConst.SECTOR_SIZE;
    private string _modelNumber = "";
    private string _serialNumber = "";
    private string _firmwareRev = "";
    private bool _lba48Supported;

    // Command slot management (simple: use slot 0 only for now)
    private const int CMD_SLOT = 0;

    // Pointers to structures
    private HbaCommandHeader* _cmdList;
    private HbaReceivedFis* _receivedFis;
    private HbaCommandTable* _cmdTable;

    public int PortIndex => _portIndex;
    public AhciPortState State => _state;
    public AhciDeviceType DeviceType => _deviceType;
    public ulong SectorCount => _sectorCount;
    public uint SectorSize => _sectorSize;
    public string ModelNumber => _modelNumber;
    public string SerialNumber => _serialNumber;
    public bool IsReady => _state == AhciPortState.Ready;

    public AhciPort(int portIndex, byte* portRegs)
    {
        _portIndex = portIndex;
        _portRegs = portRegs;
    }

    /// <summary>
    /// Initialize the port and detect attached device.
    /// </summary>
    public bool Initialize()
    {
        // Allocate DMA buffers for command structures
        if (!AllocateBuffers())
        {
            Debug.Write("[AHCI] Port ");
            Debug.WriteDecimal(_portIndex);
            Debug.WriteLine(": Failed to allocate DMA buffers");
            _state = AhciPortState.Error;
            return false;
        }

        // Stop the command engine before configuring
        StopCommandEngine();

        // Set up command list and FIS base addresses
        ulong clbPhys = _cmdListBuffer.PhysicalAddress;
        ulong fbPhys = _fisBuffer.PhysicalAddress;

        WritePort(PortRegs.CLB, (uint)clbPhys);
        WritePort(PortRegs.CLBU, (uint)(clbPhys >> 32));
        WritePort(PortRegs.FB, (uint)fbPhys);
        WritePort(PortRegs.FBU, (uint)(fbPhys >> 32));

        // Clear pending interrupts
        WritePort(PortRegs.IS, 0xFFFFFFFF);

        // Clear error register
        WritePort(PortRegs.SERR, 0xFFFFFFFF);

        // Start the command engine
        StartCommandEngine();

        // Detect device
        if (!DetectDevice())
        {
            _state = AhciPortState.NoDevice;
            return true; // Not an error, just no device
        }

        // Issue IDENTIFY command
        if (!IdentifyDevice())
        {
            Debug.Write("[AHCI] Port ");
            Debug.WriteDecimal(_portIndex);
            Debug.WriteLine(": IDENTIFY failed");
            _state = AhciPortState.Error;
            return false;
        }

        _state = AhciPortState.Ready;
        return true;
    }

    /// <summary>
    /// Allocate DMA buffers for command list, received FIS, and command table.
    /// </summary>
    private bool AllocateBuffers()
    {
        // Command list: 32 entries * 32 bytes = 1024 bytes, 1KB aligned
        _cmdListBuffer = DMA.Allocate(AhciConst.CMD_LIST_SIZE);
        if (!_cmdListBuffer.IsValid)
            return false;

        // Received FIS: 256 bytes, 256-byte aligned
        _fisBuffer = DMA.Allocate(AhciConst.RECV_FIS_SIZE);
        if (!_fisBuffer.IsValid)
        {
            DMA.Free(ref _cmdListBuffer);
            return false;
        }

        // Command table: 256 bytes per table (128 minimum + PRDTs)
        // For now, allocate one table with room for multiple PRDs
        _cmdTableBuffer = DMA.Allocate(AhciConst.CMD_TABLE_SIZE + 16 * AhciConst.PRD_ENTRY_SIZE);
        if (!_cmdTableBuffer.IsValid)
        {
            DMA.Free(ref _cmdListBuffer);
            DMA.Free(ref _fisBuffer);
            return false;
        }

        // Zero all buffers
        DMA.Zero(_cmdListBuffer);
        DMA.Zero(_fisBuffer);
        DMA.Zero(_cmdTableBuffer);

        // Set up pointers
        _cmdList = (HbaCommandHeader*)_cmdListBuffer.VirtualAddress;
        _receivedFis = (HbaReceivedFis*)_fisBuffer.VirtualAddress;
        _cmdTable = (HbaCommandTable*)_cmdTableBuffer.VirtualAddress;

        // Set up command header to point to command table
        _cmdList[CMD_SLOT].CtbaPhys = _cmdTableBuffer.PhysicalAddress;

        return true;
    }

    /// <summary>
    /// Stop the command engine (must be done before reconfiguring).
    /// </summary>
    private void StopCommandEngine()
    {
        uint cmd = ReadPort(PortRegs.CMD);

        // If already stopped, nothing to do
        if ((cmd & (uint)(PortCmd.ST | PortCmd.CR | PortCmd.FRE | PortCmd.FR)) == 0)
            return;

        // Clear ST (command engine)
        cmd &= ~(uint)PortCmd.ST;
        WritePort(PortRegs.CMD, cmd);

        // Wait for CR (command list running) to clear
        int timeout = AhciConst.TIMEOUT_RESET;
        while ((ReadPort(PortRegs.CMD) & (uint)PortCmd.CR) != 0 && --timeout > 0) { }

        // Clear FRE (FIS receive enable)
        cmd = ReadPort(PortRegs.CMD);
        cmd &= ~(uint)PortCmd.FRE;
        WritePort(PortRegs.CMD, cmd);

        // Wait for FR (FIS receive running) to clear
        timeout = AhciConst.TIMEOUT_RESET;
        while ((ReadPort(PortRegs.CMD) & (uint)PortCmd.FR) != 0 && --timeout > 0) { }
    }

    /// <summary>
    /// Start the command engine.
    /// </summary>
    private void StartCommandEngine()
    {
        // Wait for CR to be clear
        int timeout = AhciConst.TIMEOUT_RESET;
        while ((ReadPort(PortRegs.CMD) & (uint)PortCmd.CR) != 0 && --timeout > 0) { }

        // Enable FRE first
        uint cmd = ReadPort(PortRegs.CMD);
        cmd |= (uint)PortCmd.FRE;
        WritePort(PortRegs.CMD, cmd);

        // Then enable ST
        cmd |= (uint)PortCmd.ST;
        WritePort(PortRegs.CMD, cmd);
    }

    /// <summary>
    /// Check if a device is present on this port.
    /// </summary>
    private bool DetectDevice()
    {
        uint ssts = ReadPort(PortRegs.SSTS);
        uint det = ssts & PortSsts.DET_MASK;
        uint ipm = (ssts & PortSsts.IPM_MASK) >> (int)PortSsts.IPM_SHIFT;

        // Check for device presence and active link
        if (det != PortSsts.DET_PHY || ipm != PortSsts.IPM_ACTIVE)
        {
            _deviceType = AhciDeviceType.None;
            return false;
        }

        // Check device signature
        uint sig = ReadPort(PortRegs.SIG);
        _deviceType = sig switch
        {
            DeviceSignature.ATA => AhciDeviceType.Ata,
            DeviceSignature.ATAPI => AhciDeviceType.Atapi,
            DeviceSignature.SEMB => AhciDeviceType.Semb,
            DeviceSignature.PM => AhciDeviceType.PortMult,
            _ => AhciDeviceType.Ata  // Default to ATA
        };

        _state = AhciPortState.DevicePresent;
        return true;
    }

    /// <summary>
    /// Issue IDENTIFY DEVICE command to get device information.
    /// </summary>
    private bool IdentifyDevice()
    {
        // Verify command table is valid
        if (_cmdTable == null)
            return false;

        // Allocate buffer for IDENTIFY data (512 bytes)
        var identifyBuffer = DMA.Allocate(512);
        if (!identifyBuffer.IsValid)
            return false;

        DMA.Zero(identifyBuffer);

        // Build IDENTIFY command (inlined to avoid JIT method call issues)
        byte* fisPtr = (byte*)_cmdTable->Cfis;
        // Clear first 20 bytes
        *(ulong*)fisPtr = 0;
        *((ulong*)fisPtr + 1) = 0;
        *((uint*)fisPtr + 4) = 0;
        // Set fields
        byte cmd = _deviceType == AhciDeviceType.Atapi
            ? AtaCmd.IDENTIFY_PACKET_DEVICE
            : AtaCmd.IDENTIFY_DEVICE;
        fisPtr[0] = 0x27;  // FisType.RegH2D
        fisPtr[1] = 0x80;  // Flags: C bit = 1
        fisPtr[2] = cmd;   // Command
        fisPtr[3] = 0;     // FeatureLo
        fisPtr[4] = 0;     // Lba0
        fisPtr[5] = 0;     // Lba1
        fisPtr[6] = 0;     // Lba2
        fisPtr[7] = 0x40;  // Device (LBA mode)
        fisPtr[8] = 0;     // Lba3
        fisPtr[9] = 0;     // Lba4
        fisPtr[10] = 0;    // Lba5
        fisPtr[11] = 0;    // FeatureHi
        fisPtr[12] = 1;    // CountLo (1 sector for identify)
        fisPtr[13] = 0;    // CountHi
        fisPtr[14] = 0;    // Icc
        fisPtr[15] = 0;    // Control

        // Set up command header
        ref var header = ref _cmdList[CMD_SLOT];
        header.Flags1 = (byte)((sizeof(FisRegH2D) / 4) & 0x1F);  // CFL in DWORDs
        header.Flags2 = (byte)CmdHeaderFlags2.C;  // Clear busy on R_OK
        header.PrdtLength = 1;
        header.PrdByteCount = 0;

        // Set up PRD
        ref var prd = ref _cmdTable->Prdt0;
        prd.DataBaseAddress = identifyBuffer.PhysicalAddress;
        prd.Reserved = 0;
        prd.Dbc = 512 - 1;  // Byte count minus 1

        // Issue command
        if (!IssueCommand(CMD_SLOT))
        {
            DMA.Free(ref identifyBuffer);
            return false;
        }

        // Parse IDENTIFY data
        var identify = (AtaIdentifyData*)identifyBuffer.VirtualAddress;
        ParseIdentifyData(identify);

        DMA.Free(ref identifyBuffer);
        return true;
    }

    /// <summary>
    /// Parse IDENTIFY DEVICE data.
    /// </summary>
    private void ParseIdentifyData(AtaIdentifyData* identify)
    {
        // Work around JIT bug with struct field access by reading directly via pointer
        byte* raw = (byte*)identify;

        // Words 60-61 (offset 120-123): Total sectors 28-bit
        uint totalSectors28 = *(uint*)(raw + 120);

        // Word 83 (offset 166-167): Command set 2 supported
        ushort cmdSet2 = *(ushort*)(raw + 166);

        // Words 100-103 (offset 200-207): Total sectors 48-bit
        ulong totalSectors48 = *(ulong*)(raw + 200);

        // Get sector count (prefer 48-bit LBA if supported)
        if ((cmdSet2 & (1 << 10)) != 0)
        {
            _lba48Supported = true;
            _sectorCount = totalSectors48;
        }
        else
        {
            _lba48Supported = false;
            _sectorCount = totalSectors28;
        }

        // Get sector size (default 512, check for 4K)
        if ((identify->FieldValidity & (1 << 2)) != 0 &&
            (identify->Various[13] & (1 << 12)) != 0)
        {
            // Logical sector size is specified
            _sectorSize = (uint)(identify->Various[14] | (identify->Various[15] << 16)) * 2;
        }
        else
        {
            _sectorSize = AhciConst.SECTOR_SIZE;
        }

        // Extract model number (bytes are swapped in pairs)
        _modelNumber = ExtractAtaString(identify->ModelNumber, 40);
        _serialNumber = ExtractAtaString(identify->SerialNumber, 20);
        _firmwareRev = ExtractAtaString(identify->FirmwareRev, 8);
    }

    /// <summary>
    /// Extract ATA string (bytes are swapped in pairs).
    /// </summary>
    private static string ExtractAtaString(byte* data, int length)
    {
        char* chars = stackalloc char[length];
        int outLen = 0;

        for (int i = 0; i < length; i += 2)
        {
            if (i + 1 < length && data[i + 1] != 0 && data[i + 1] != ' ')
                chars[outLen++] = (char)data[i + 1];
            if (data[i] != 0 && data[i] != ' ')
                chars[outLen++] = (char)data[i];
        }

        // Trim trailing spaces
        while (outLen > 0 && chars[outLen - 1] == ' ')
            outLen--;

        return new string(chars, 0, outLen);
    }

    /// <summary>
    /// Build a Host-to-Device Register FIS.
    /// </summary>
    private void BuildH2DFis(FisRegH2D* fis, byte command, ulong lba, uint count)
    {
        // Clear FIS (20 bytes)
        *(ulong*)fis = 0;
        *((ulong*)fis + 1) = 0;
        *((uint*)fis + 4) = 0;

        // Set FIS fields via pointer arithmetic (avoid struct field access)
        byte* p = (byte*)fis;
        p[0] = 0x27;  // FisType.RegH2D
        p[1] = 0x80;  // Flags: C bit = 1 (command)
        p[2] = command;  // Command
        p[3] = 0;     // FeatureLo
        p[4] = (byte)lba;         // Lba0
        p[5] = (byte)(lba >> 8);  // Lba1
        p[6] = (byte)(lba >> 16); // Lba2
        p[7] = 0x40;  // Device (LBA mode)
        p[8] = (byte)(lba >> 24); // Lba3
        p[9] = (byte)(lba >> 32); // Lba4
        p[10] = (byte)(lba >> 40); // Lba5
        p[11] = 0;    // FeatureHi
        p[12] = (byte)count;       // CountLo
        p[13] = (byte)(count >> 8); // CountHi
        p[14] = 0;    // Icc
        p[15] = 0;    // Control
    }

    /// <summary>
    /// Issue a command and wait for completion.
    /// </summary>
    private bool IssueCommand(int slot)
    {
        // Clear interrupt status
        WritePort(PortRegs.IS, 0xFFFFFFFF);

        // Issue command
        WritePort(PortRegs.CI, 1u << slot);

        // Wait for completion
        int timeout = AhciConst.TIMEOUT_CMD;
        while (timeout-- > 0)
        {
            uint is_reg = ReadPort(PortRegs.IS);

            // Check for errors
            if ((is_reg & (uint)(PortInterrupt.TFES | PortInterrupt.HBFS |
                                  PortInterrupt.HBDS | PortInterrupt.IFS)) != 0)
            {
                Debug.Write("[AHCI] Command error IS=0x");
                Debug.WriteHex(is_reg);
                Debug.Write(" TFD=0x");
                Debug.WriteHex(ReadPort(PortRegs.TFD));
                Debug.WriteLine();
                return false;
            }

            // Check if command completed
            if ((ReadPort(PortRegs.CI) & (1u << slot)) == 0)
            {
                // Verify status
                uint tfd = ReadPort(PortRegs.TFD);
                if ((tfd & PortTfd.STS_ERR) != 0)
                {
                    Debug.Write("[AHCI] Command completed with error TFD=0x");
                    Debug.WriteHex(tfd);
                    Debug.WriteLine();
                    return false;
                }
                return true;
            }
        }

        Debug.WriteLine("[AHCI] Command timeout");
        return false;
    }

    /// <summary>
    /// Read sectors from the device.
    /// </summary>
    public bool ReadSectors(ulong lba, uint count, byte* buffer)
    {
        if (_state != AhciPortState.Ready || count == 0)
            return false;

        // Allocate DMA buffer
        ulong totalBytes = count * _sectorSize;
        var dataBuffer = DMA.Allocate(totalBytes);
        if (!dataBuffer.IsValid)
            return false;

        // Build READ DMA EXT command (inlined)
        byte* fisPtr = (byte*)_cmdTable->Cfis;
        *(ulong*)fisPtr = 0;
        *((ulong*)fisPtr + 1) = 0;
        *((uint*)fisPtr + 4) = 0;
        byte cmd = _lba48Supported ? AtaCmd.READ_DMA_EXT : AtaCmd.READ_DMA;
        fisPtr[0] = 0x27;  // FisType.RegH2D
        fisPtr[1] = 0x80;  // Flags: C bit = 1
        fisPtr[2] = cmd;
        fisPtr[3] = 0;     // FeatureLo
        fisPtr[4] = (byte)lba;
        fisPtr[5] = (byte)(lba >> 8);
        fisPtr[6] = (byte)(lba >> 16);
        fisPtr[7] = 0x40;  // Device (LBA mode)
        fisPtr[8] = (byte)(lba >> 24);
        fisPtr[9] = (byte)(lba >> 32);
        fisPtr[10] = (byte)(lba >> 40);
        fisPtr[11] = 0;    // FeatureHi
        fisPtr[12] = (byte)count;
        fisPtr[13] = (byte)(count >> 8);
        fisPtr[14] = 0;
        fisPtr[15] = 0;

        // Set up command header
        ref var header = ref _cmdList[CMD_SLOT];
        header.Flags1 = (byte)((sizeof(FisRegH2D) / 4) & 0x1F);  // CFL
        header.Flags2 = (byte)CmdHeaderFlags2.C;
        header.PrdtLength = 1;
        header.PrdByteCount = 0;

        // Set up PRD
        ref var prd = ref _cmdTable->Prdt0;
        prd.DataBaseAddress = dataBuffer.PhysicalAddress;
        prd.Reserved = 0;
        prd.Dbc = (uint)(totalBytes - 1);

        // Issue command
        bool success = IssueCommand(CMD_SLOT);

        if (success)
        {
            // Copy data to user buffer
            DMA.CopyFrom(dataBuffer, buffer, totalBytes);
        }

        DMA.Free(ref dataBuffer);
        return success;
    }

    /// <summary>
    /// Write sectors to the device.
    /// </summary>
    public bool WriteSectors(ulong lba, uint count, byte* buffer)
    {
        if (_state != AhciPortState.Ready || count == 0)
            return false;

        // Allocate DMA buffer
        ulong totalBytes = count * _sectorSize;
        var dataBuffer = DMA.Allocate(totalBytes);
        if (!dataBuffer.IsValid)
            return false;

        // Copy data to DMA buffer
        DMA.CopyTo(dataBuffer, buffer, totalBytes);

        // Build WRITE DMA EXT command (inlined)
        byte* fisPtr = (byte*)_cmdTable->Cfis;
        *(ulong*)fisPtr = 0;
        *((ulong*)fisPtr + 1) = 0;
        *((uint*)fisPtr + 4) = 0;
        byte cmd = _lba48Supported ? AtaCmd.WRITE_DMA_EXT : AtaCmd.WRITE_DMA;
        fisPtr[0] = 0x27;  // FisType.RegH2D
        fisPtr[1] = 0x80;  // Flags: C bit = 1
        fisPtr[2] = cmd;
        fisPtr[3] = 0;     // FeatureLo
        fisPtr[4] = (byte)lba;
        fisPtr[5] = (byte)(lba >> 8);
        fisPtr[6] = (byte)(lba >> 16);
        fisPtr[7] = 0x40;  // Device (LBA mode)
        fisPtr[8] = (byte)(lba >> 24);
        fisPtr[9] = (byte)(lba >> 32);
        fisPtr[10] = (byte)(lba >> 40);
        fisPtr[11] = 0;    // FeatureHi
        fisPtr[12] = (byte)count;
        fisPtr[13] = (byte)(count >> 8);
        fisPtr[14] = 0;
        fisPtr[15] = 0;

        // Set up command header
        ref var header = ref _cmdList[CMD_SLOT];
        header.Flags1 = (byte)(((sizeof(FisRegH2D) / 4) & 0x1F) | (1 << 6));  // CFL + W bit
        header.Flags2 = (byte)CmdHeaderFlags2.C;
        header.PrdtLength = 1;
        header.PrdByteCount = 0;

        // Set up PRD
        ref var prd = ref _cmdTable->Prdt0;
        prd.DataBaseAddress = dataBuffer.PhysicalAddress;
        prd.Reserved = 0;
        prd.Dbc = (uint)(totalBytes - 1);

        // Issue command
        bool success = IssueCommand(CMD_SLOT);

        DMA.Free(ref dataBuffer);
        return success;
    }

    /// <summary>
    /// Flush cache to disk.
    /// </summary>
    public bool Flush()
    {
        if (_state != AhciPortState.Ready)
            return false;

        // Build FLUSH CACHE EXT command (inlined)
        byte* fisPtr = (byte*)_cmdTable->Cfis;
        *(ulong*)fisPtr = 0;
        *((ulong*)fisPtr + 1) = 0;
        *((uint*)fisPtr + 4) = 0;
        byte cmd = _lba48Supported ? AtaCmd.FLUSH_CACHE_EXT : AtaCmd.FLUSH_CACHE;
        fisPtr[0] = 0x27;  // FisType.RegH2D
        fisPtr[1] = 0x80;  // Flags: C bit = 1
        fisPtr[2] = cmd;
        fisPtr[7] = 0x40;  // Device (LBA mode)

        // Set up command header (no data transfer)
        ref var header = ref _cmdList[CMD_SLOT];
        header.Flags1 = (byte)((sizeof(FisRegH2D) / 4) & 0x1F);
        header.Flags2 = (byte)CmdHeaderFlags2.C;
        header.PrdtLength = 0;
        header.PrdByteCount = 0;

        return IssueCommand(CMD_SLOT);
    }

    /// <summary>
    /// Read a port register.
    /// </summary>
    private uint ReadPort(int offset)
    {
        return *(uint*)(_portRegs + offset);
    }

    /// <summary>
    /// Write a port register.
    /// </summary>
    private void WritePort(int offset, uint value)
    {
        *(uint*)(_portRegs + offset) = value;
    }

    /// <summary>
    /// Clean up resources.
    /// </summary>
    public void Dispose()
    {
        StopCommandEngine();
        DMA.Free(ref _cmdListBuffer);
        DMA.Free(ref _fisBuffer);
        DMA.Free(ref _cmdTableBuffer);
        _state = AhciPortState.Uninitialized;
    }
}
