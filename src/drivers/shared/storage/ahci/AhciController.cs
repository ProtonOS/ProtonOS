// ProtonOS AHCI Driver - Controller Implementation
// Manages the AHCI Host Bus Adapter

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Kernel;
using ProtonOS.DDK.Platform;

namespace ProtonOS.Drivers.Storage.Ahci;

/// <summary>
/// AHCI Host Bus Adapter controller.
/// Manages the HBA and its ports.
/// </summary>
public unsafe class AhciController : IDisposable
{
    // PCI device info
    private PciDeviceInfo _pciDevice;

    // HBA memory-mapped registers (from BAR5)
    private byte* _hbaRegs;
    private ulong _hbaPhysAddr;

    // Controller info
    private uint _capabilities;
    private uint _capabilities2;
    private uint _version;
    private int _portCount;
    private int _cmdSlotCount;
    private uint _portsImplemented;

    // Ports
    private AhciPort?[] _ports = new AhciPort?[AhciConst.MAX_PORTS];
    private int _activePortCount;

    public bool IsInitialized { get; private set; }
    public int PortCount => _portCount;
    public int ActivePortCount => _activePortCount;
    public uint Version => _version;

    /// <summary>
    /// Initialize the AHCI controller from PCI device info.
    /// </summary>
    public bool Initialize(PciDeviceInfo pciDevice)
    {
        _pciDevice = pciDevice;

        // Enable bus mastering and memory space
        PCI.EnableBusMaster(pciDevice.Address);
        PCI.EnableMemorySpace(pciDevice.Address);

        // Get BAR5 (ABAR - AHCI Base Address Register)
        var bar5 = pciDevice.Bars[5];
        if (!bar5.IsValid || bar5.IsIO)
        {
            Debug.WriteLine("[AHCI] BAR5 is invalid or I/O");
            return false;
        }

        _hbaPhysAddr = bar5.BaseAddress;
        _hbaRegs = (byte*)Memory.PhysToVirt(_hbaPhysAddr);

        // Read capabilities
        _capabilities = ReadHba(HbaRegs.CAP);
        _capabilities2 = ReadHba(HbaRegs.CAP2);
        _version = ReadHba(HbaRegs.VS);
        _portsImplemented = ReadHba(HbaRegs.PI);

        // Extract port count and command slot count from capabilities
        _portCount = (int)(_capabilities & (uint)HbaCap.NP_MASK) + 1;
        _cmdSlotCount = (int)((_capabilities >> (int)HbaCap.NCS_SHIFT) & 0x1F) + 1;

        // Enable AHCI mode
        if (!EnableAhciMode())
        {
            Debug.WriteLine("[AHCI] Failed to enable AHCI mode");
            return false;
        }

        // Enumerate and initialize ports
        if (!EnumeratePorts())
        {
            Debug.WriteLine("[AHCI] Failed to enumerate ports");
            return false;
        }

        IsInitialized = true;
        Debug.Write("[AHCI] ");
        Debug.WriteDecimal(_activePortCount);
        Debug.Write(" device(s) on ");
        Debug.Write(pciDevice.Address.ToString());
        Debug.WriteLine();

        return true;
    }

    /// <summary>
    /// Enable AHCI mode (set GHC.AE).
    /// </summary>
    private bool EnableAhciMode()
    {
        uint ghc = ReadHba(HbaRegs.GHC);

        // Check if AHCI-only mode
        if ((_capabilities & (uint)HbaCap.SAM) != 0)
        {
            // Already in AHCI mode
        }
        else
        {
            // Enable AHCI mode
            ghc |= (uint)HbaGhc.AE;
            WriteHba(HbaRegs.GHC, ghc);

            // Verify
            ghc = ReadHba(HbaRegs.GHC);
            if ((ghc & (uint)HbaGhc.AE) == 0)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Perform HBA reset (optional, can be disruptive).
    /// </summary>
    private bool ResetController()
    {
        uint ghc = ReadHba(HbaRegs.GHC);

        // Set HR (HBA Reset)
        ghc |= (uint)HbaGhc.HR;
        WriteHba(HbaRegs.GHC, ghc);

        // Wait for HR to clear (reset complete)
        int timeout = AhciConst.TIMEOUT_RESET;
        while ((ReadHba(HbaRegs.GHC) & (uint)HbaGhc.HR) != 0 && --timeout > 0) { }

        if (timeout <= 0)
            return false;

        // Re-enable AHCI mode after reset
        ghc = ReadHba(HbaRegs.GHC);
        ghc |= (uint)HbaGhc.AE;
        WriteHba(HbaRegs.GHC, ghc);

        return true;
    }

    /// <summary>
    /// Enumerate all implemented ports.
    /// </summary>
    private bool EnumeratePorts()
    {
        _activePortCount = 0;

        for (int i = 0; i < AhciConst.MAX_PORTS; i++)
        {
            // Check if port is implemented
            if ((_portsImplemented & (1u << i)) == 0)
                continue;

            // Calculate port register base
            byte* portRegs = _hbaRegs + PortRegs.PORT_BASE + (i * PortRegs.PORT_SIZE);

            // Create and initialize port
            var port = new AhciPort(i, portRegs);
            if (port.Initialize())
            {
                _ports[i] = port;
                if (port.IsReady)
                    _activePortCount++;
            }
            else
            {
                port.Dispose();
            }
        }

        return true;
    }

    /// <summary>
    /// Get a port by index.
    /// </summary>
    public AhciPort? GetPort(int index)
    {
        if (index < 0 || index >= AhciConst.MAX_PORTS)
            return null;
        return _ports[index];
    }

    /// <summary>
    /// Get the first ready port with a device.
    /// </summary>
    public AhciPort? GetFirstReadyPort()
    {
        for (int i = 0; i < AhciConst.MAX_PORTS; i++)
        {
            var port = _ports[i];
            if (port != null && port.IsReady)
                return port;
        }
        return null;
    }

    /// <summary>
    /// Get all ready ports.
    /// </summary>
    public AhciPort[] GetReadyPorts()
    {
        var result = new AhciPort[_activePortCount];
        int idx = 0;

        for (int i = 0; i < AhciConst.MAX_PORTS && idx < _activePortCount; i++)
        {
            var port = _ports[i];
            if (port != null && port.IsReady)
                result[idx++] = port;
        }

        return result;
    }

    /// <summary>
    /// Read an HBA register.
    /// </summary>
    private uint ReadHba(int offset)
    {
        return *(uint*)(_hbaRegs + offset);
    }

    /// <summary>
    /// Write an HBA register.
    /// </summary>
    private void WriteHba(int offset, uint value)
    {
        *(uint*)(_hbaRegs + offset) = value;
    }

    /// <summary>
    /// Clean up resources.
    /// </summary>
    public void Dispose()
    {
        for (int i = 0; i < AhciConst.MAX_PORTS; i++)
        {
            _ports[i]?.Dispose();
            _ports[i] = null;
        }
        IsInitialized = false;
    }
}
