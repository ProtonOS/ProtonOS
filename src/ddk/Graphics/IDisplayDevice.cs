// ProtonOS DDK - Display Device Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Graphics;

/// <summary>
/// Display capabilities.
/// </summary>
[Flags]
public enum DisplayCapabilities
{
    None = 0,

    /// <summary>Supports mode switching.</summary>
    ModeSwitching = 1 << 0,

    /// <summary>Supports hardware cursor.</summary>
    HardwareCursor = 1 << 1,

    /// <summary>Supports VSync.</summary>
    VSync = 1 << 2,

    /// <summary>Supports double buffering.</summary>
    DoubleBuffer = 1 << 3,

    /// <summary>Supports hardware acceleration.</summary>
    HardwareAcceleration = 1 << 4,

    /// <summary>Supports multiple outputs.</summary>
    MultipleOutputs = 1 << 5,

    /// <summary>Supports hot-plug detection.</summary>
    HotPlug = 1 << 6,

    /// <summary>Supports EDID reading.</summary>
    EDID = 1 << 7,
}

/// <summary>
/// Display mode information.
/// </summary>
public struct DisplayMode
{
    /// <summary>Width in pixels.</summary>
    public uint Width;

    /// <summary>Height in pixels.</summary>
    public uint Height;

    /// <summary>Bits per pixel.</summary>
    public uint BitsPerPixel;

    /// <summary>Refresh rate in Hz.</summary>
    public uint RefreshRate;

    /// <summary>Pixel format.</summary>
    public PixelFormat Format;

    /// <summary>Mode index (driver-specific).</summary>
    public int ModeIndex;

    public override string ToString()
    {
        return $"{Width}x{Height}x{BitsPerPixel} @{RefreshRate}Hz";
    }
}

/// <summary>
/// Display output connector information.
/// </summary>
public class DisplayOutput
{
    /// <summary>Output index.</summary>
    public int Index;

    /// <summary>Output name.</summary>
    public string Name = "";

    /// <summary>True if a display is connected.</summary>
    public bool IsConnected;

    /// <summary>Current mode.</summary>
    public DisplayMode CurrentMode;

    /// <summary>Preferred (native) mode.</summary>
    public DisplayMode? PreferredMode;

    /// <summary>All supported modes.</summary>
    public DisplayMode[] SupportedModes = Array.Empty<DisplayMode>();
}

/// <summary>
/// Interface for display/graphics device drivers.
/// </summary>
public interface IDisplayDevice : IDriver
{
    /// <summary>
    /// Device name for identification.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Device capabilities.
    /// </summary>
    DisplayCapabilities Capabilities { get; }

    /// <summary>
    /// Number of output connectors.
    /// </summary>
    int OutputCount { get; }

    /// <summary>
    /// Get information about an output.
    /// </summary>
    DisplayOutput GetOutput(int index);

    /// <summary>
    /// Get all supported display modes.
    /// </summary>
    DisplayMode[] GetSupportedModes();

    /// <summary>
    /// Get the current display mode.
    /// </summary>
    DisplayMode GetCurrentMode();

    /// <summary>
    /// Set the display mode.
    /// </summary>
    /// <param name="mode">Desired mode</param>
    /// <returns>true if mode was set successfully</returns>
    bool SetMode(DisplayMode mode);

    /// <summary>
    /// Get the framebuffer for the current mode.
    /// </summary>
    IFramebuffer? GetFramebuffer();

    /// <summary>
    /// Set the hardware cursor position.
    /// </summary>
    void SetCursorPosition(int x, int y);

    /// <summary>
    /// Set the hardware cursor visibility.
    /// </summary>
    void SetCursorVisible(bool visible);

    /// <summary>
    /// Set the hardware cursor image.
    /// </summary>
    /// <param name="width">Cursor width</param>
    /// <param name="height">Cursor height</param>
    /// <param name="hotspotX">Hotspot X offset</param>
    /// <param name="hotspotY">Hotspot Y offset</param>
    /// <param name="pixels">Cursor pixel data (BGRA32)</param>
    unsafe void SetCursorImage(int width, int height, int hotspotX, int hotspotY, byte* pixels);

    /// <summary>
    /// Wait for vertical sync.
    /// </summary>
    void WaitVSync();

    /// <summary>
    /// Swap front and back buffers (if double buffered).
    /// </summary>
    void SwapBuffers();

    /// <summary>
    /// Set callback for hot-plug events.
    /// </summary>
    void SetHotPlugCallback(Action<int, bool>? callback);
}

/// <summary>
/// Manages multiple display devices.
/// </summary>
public static class DisplayManager
{
    private static IDisplayDevice? _primaryDevice;
    private static System.Collections.Generic.List<IDisplayDevice> _devices = new();

    /// <summary>
    /// Primary display device.
    /// </summary>
    public static IDisplayDevice? PrimaryDevice => _primaryDevice;

    /// <summary>
    /// All display devices.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<IDisplayDevice> Devices => _devices;

    /// <summary>
    /// Register a display device.
    /// </summary>
    public static void RegisterDevice(IDisplayDevice device)
    {
        _devices.Add(device);
        if (_primaryDevice == null)
            _primaryDevice = device;
    }

    /// <summary>
    /// Unregister a display device.
    /// </summary>
    public static void UnregisterDevice(IDisplayDevice device)
    {
        _devices.Remove(device);
        if (_primaryDevice == device)
            _primaryDevice = _devices.Count > 0 ? _devices[0] : null;
    }

    /// <summary>
    /// Set the primary display device.
    /// </summary>
    public static void SetPrimaryDevice(IDisplayDevice device)
    {
        if (_devices.Contains(device))
            _primaryDevice = device;
    }
}
