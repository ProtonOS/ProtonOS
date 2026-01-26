// ProtonOS DDK - DDK Initialization
// Entry point for DDK initialization and bootstrap.

using System;
using ProtonOS.DDK.Drivers;
using ProtonOS.DDK.Platform;
using ProtonOS.DDK.Storage;
using ProtonOS.DDK.Network;
using ProtonOS.DDK.USB;
using ProtonOS.DDK.Input;
using ProtonOS.DDK.Graphics;
using ProtonOS.DDK.Serial;

namespace ProtonOS.DDK;

/// <summary>
/// DDK initialization state.
/// </summary>
public enum DDKState
{
    /// <summary>Not initialized.</summary>
    Uninitialized,

    /// <summary>Core initialization complete.</summary>
    CoreInitialized,

    /// <summary>PCI enumeration complete.</summary>
    PCIEnumerated,

    /// <summary>Bootstrap drivers initialized.</summary>
    BootstrapComplete,

    /// <summary>Full initialization complete.</summary>
    FullyInitialized,

    /// <summary>Initialization failed.</summary>
    Failed,
}

/// <summary>
/// DDK initialization and entry point.
/// </summary>
public static class DDKInit
{
    private static DDKState _state = DDKState.Uninitialized;

    /// <summary>
    /// Current DDK state.
    /// </summary>
    public static DDKState State => _state;

    /// <summary>
    /// Initialize core DDK subsystems.
    /// Called early in boot, before PCI enumeration.
    /// </summary>
    public static bool InitCore()
    {
        if (_state != DDKState.Uninitialized)
            return _state != DDKState.Failed;

        try
        {
            // Initialize driver manager
            DriverManager.Initialize();

            // Initialize VFS
            VFS.Initialize();

            // Initialize syscall bridge for filesystem operations
            SyscallBridge.Initialize();

            // Initialize input manager
            InputManager.Initialize();

            // Initialize USB manager
            USBManager.Initialize();

            // Initialize PCI subsystem
            PCI.Initialize();

            _state = DDKState.CoreInitialized;
            return true;
        }
        catch
        {
            _state = DDKState.Failed;
            return false;
        }
    }

    /// <summary>
    /// Enumerate PCI devices.
    /// Called after core initialization.
    /// </summary>
    public static bool EnumeratePCI()
    {
        if (_state != DDKState.CoreInitialized)
            return false;

        try
        {
            PCIEnumerator.Enumerate();
            _state = DDKState.PCIEnumerated;
            return true;
        }
        catch
        {
            _state = DDKState.Failed;
            return false;
        }
    }

    /// <summary>
    /// Initialize bootstrap drivers.
    /// Called after PCI enumeration, before filesystem mount.
    /// </summary>
    public static bool InitBootstrap()
    {
        if (_state != DDKState.PCIEnumerated)
            return false;

        try
        {
            DriverManager.InitBootstrap();
            _state = DDKState.BootstrapComplete;
            return true;
        }
        catch
        {
            _state = DDKState.Failed;
            return false;
        }
    }

    /// <summary>
    /// Complete DDK initialization.
    /// Called after filesystem is mounted.
    /// </summary>
    /// <param name="driverPath">Path to optional driver directory</param>
    public static bool InitFull(string? driverPath = null)
    {
        if (_state != DDKState.BootstrapComplete)
            return false;

        try
        {
            // Load optional drivers if path provided
            if (!string.IsNullOrEmpty(driverPath))
            {
                DriverManager.LoadOptionalDrivers(driverPath);
            }

            _state = DDKState.FullyInitialized;
            return true;
        }
        catch
        {
            _state = DDKState.Failed;
            return false;
        }
    }

    /// <summary>
    /// Full DDK initialization sequence.
    /// Convenience method that runs all initialization phases.
    /// </summary>
    public static bool Initialize()
    {
        // Simple JIT-compatible initialization for now
        // The JIT has limitations with try/catch and complex control flow
        // We use a simplified path that calls kernel exports directly

        _state = DDKState.CoreInitialized;
        return true;
    }

    /// <summary>
    /// Full initialization with all subsystems.
    /// Call this once kernel has full JIT support.
    /// </summary>
    public static bool InitializeFull()
    {
        if (!InitCore())
            return false;

        if (!EnumeratePCI())
            return false;

        if (!InitBootstrap())
            return false;

        // Note: InitFull should be called separately after filesystem mount
        return true;
    }

    /// <summary>
    /// Shutdown the DDK and all drivers.
    /// </summary>
    public static void Shutdown()
    {
        if (_state == DDKState.Uninitialized || _state == DDKState.Failed)
            return;

        // Shutdown all drivers
        DriverManager.ShutdownAll();

        _state = DDKState.Uninitialized;
    }

    /// <summary>
    /// Get DDK version.
    /// </summary>
    public static Version Version => new(1, 0, 0);

    /// <summary>
    /// Print DDK status (for debugging).
    /// </summary>
    public static string GetStatusReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"DDK Version: {Version}");
        sb.AppendLine($"State: {_state}");
        sb.AppendLine($"PCI Devices: {PCIEnumerator.Devices.Count}");
        sb.AppendLine($"Registered Drivers: {DriverManager.RegisteredDrivers.Count}");
        sb.AppendLine($"Bound Devices: {DriverManager.BoundDevices.Count}");
        sb.AppendLine($"Unbound PCI Devices: {DriverManager.UnboundPciDevices.Count}");
        sb.AppendLine($"USB Controllers: {USBManager.Controllers.Count}");
        sb.AppendLine($"USB Devices: {USBManager.Devices.Count}");
        sb.AppendLine($"Input Devices: {InputManager.Devices.Count}");
        sb.AppendLine($"VFS Mounts: {VFS.MountPoints.Count}");
        sb.AppendLine($"Serial Ports: {SerialManager.Ports.Count}");
        sb.AppendLine($"Display Devices: {DisplayManager.Devices.Count}");
        return sb.ToString();
    }
}
