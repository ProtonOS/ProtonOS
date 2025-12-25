// ProtonOS AHCI Driver - Block Device Implementation
// Implements IBlockDevice for AHCI/SATA devices

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Storage;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// AHCI block device driver.
/// Wraps an AhciPort to implement IBlockDevice.
/// One instance per SATA device.
/// </summary>
public unsafe class AhciDriver : IBlockDevice
{
    // Port this driver wraps
    private readonly AhciPort _port;

    // Driver state
    private DriverState _state = DriverState.Loaded;
    private readonly string _deviceName;

    // Static device counter for naming
    private static int _deviceCounter;

    #region IDriver Implementation

    public string DriverName => "ahci";
    public Version DriverVersion => new Version(1, 0, 0);
    public DriverType Type => DriverType.Storage;
    public DriverState State => _state;

    public bool Initialize()
    {
        _state = DriverState.Initializing;

        if (!_port.IsReady)
        {
            _state = DriverState.Failed;
            return false;
        }

        _state = DriverState.Running;
        Debug.Write("[AhciDriver] Initialized: ");
        Debug.Write(_deviceName);
        Debug.Write(" ");
        Debug.WriteDecimal((uint)(BlockCount >> 32));
        Debug.Write(":");
        Debug.WriteDecimal((uint)BlockCount);
        Debug.Write(" sectors (");
        Debug.WriteDecimal((uint)(TotalBytes / (1024 * 1024)));
        Debug.WriteLine(" MB)");

        return true;
    }

    public void Shutdown()
    {
        _state = DriverState.Stopping;
        _state = DriverState.Stopped;
    }

    public void Suspend()
    {
        _state = DriverState.Suspended;
    }

    public void Resume()
    {
        _state = DriverState.Running;
    }

    #endregion

    #region IBlockDevice Implementation

    public string DeviceName => _deviceName;

    public ulong BlockCount => _port.SectorCount;

    public uint BlockSize => _port.SectorSize;

    public BlockDeviceCapabilities Capabilities =>
        BlockDeviceCapabilities.Read |
        BlockDeviceCapabilities.Write |
        BlockDeviceCapabilities.Flush;

    public ulong TotalBytes => BlockCount * BlockSize;

    public int Read(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (buffer == null || blockCount == 0)
            return (int)BlockResult.InvalidParameter;

        if (startBlock + blockCount > BlockCount)
            return (int)BlockResult.InvalidParameter;

        if (_state != DriverState.Running)
            return (int)BlockResult.NotReady;

        // Read in chunks if necessary (ATA command limit is 65536 sectors for 48-bit LBA)
        uint remaining = blockCount;
        ulong currentBlock = startBlock;
        byte* currentBuffer = buffer;

        while (remaining > 0)
        {
            // Limit to 256 sectors per command (128KB for 512-byte sectors)
            uint toRead = remaining > 256 ? 256 : remaining;

            if (!_port.ReadSectors(currentBlock, toRead, currentBuffer))
            {
                Debug.Write("[AhciDriver] Read failed at block ");
                Debug.WriteDecimal((uint)currentBlock);
                Debug.WriteLine();
                return (int)BlockResult.IoError;
            }

            remaining -= toRead;
            currentBlock += toRead;
            currentBuffer += toRead * BlockSize;
        }

        return (int)blockCount;
    }

    public int Write(ulong startBlock, uint blockCount, byte* buffer)
    {
        if (buffer == null || blockCount == 0)
            return (int)BlockResult.InvalidParameter;

        if (startBlock + blockCount > BlockCount)
            return (int)BlockResult.InvalidParameter;

        if (_state != DriverState.Running)
            return (int)BlockResult.NotReady;

        // Write in chunks
        uint remaining = blockCount;
        ulong currentBlock = startBlock;
        byte* currentBuffer = buffer;

        while (remaining > 0)
        {
            uint toWrite = remaining > 256 ? 256 : remaining;

            if (!_port.WriteSectors(currentBlock, toWrite, currentBuffer))
            {
                Debug.Write("[AhciDriver] Write failed at block ");
                Debug.WriteDecimal((uint)currentBlock);
                Debug.WriteLine();
                return (int)BlockResult.IoError;
            }

            remaining -= toWrite;
            currentBlock += toWrite;
            currentBuffer += toWrite * BlockSize;
        }

        return (int)blockCount;
    }

    public BlockResult Flush()
    {
        if (_state != DriverState.Running)
            return BlockResult.NotReady;

        if (!_port.Flush())
            return BlockResult.IoError;

        return BlockResult.Success;
    }

    public BlockResult Discard(ulong startBlock, uint blockCount)
    {
        // TRIM/Discard not implemented yet
        return BlockResult.Success;
    }

    #endregion

    /// <summary>
    /// Create a new AHCI driver for a port.
    /// </summary>
    public AhciDriver(AhciPort port)
    {
        _port = port;
        _deviceName = "sata" + _deviceCounter++;
    }

    /// <summary>
    /// Get the underlying port.
    /// </summary>
    public AhciPort Port => _port;

    /// <summary>
    /// Get the model number of the device.
    /// </summary>
    public string ModelNumber => _port.ModelNumber;

    /// <summary>
    /// Get the serial number of the device.
    /// </summary>
    public string SerialNumber => _port.SerialNumber;
}
