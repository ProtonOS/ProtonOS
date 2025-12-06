// ProtonOS DDK - Input Device Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Input;

/// <summary>
/// Input device capabilities.
/// </summary>
[Flags]
public enum InputCapabilities
{
    None = 0,

    /// <summary>Device generates key events.</summary>
    Keys = 1 << 0,

    /// <summary>Device generates relative motion events.</summary>
    RelativeMotion = 1 << 1,

    /// <summary>Device generates absolute position events.</summary>
    AbsolutePosition = 1 << 2,

    /// <summary>Device has buttons.</summary>
    Buttons = 1 << 3,

    /// <summary>Device has scroll wheel.</summary>
    Scroll = 1 << 4,

    /// <summary>Device supports multi-touch.</summary>
    MultiTouch = 1 << 5,

    /// <summary>Device reports pressure.</summary>
    Pressure = 1 << 6,

    /// <summary>Device has LEDs (caps lock, etc.).</summary>
    LEDs = 1 << 7,
}

/// <summary>
/// Keyboard LED state.
/// </summary>
[Flags]
public enum KeyboardLEDs
{
    None = 0,
    CapsLock = 1 << 0,
    NumLock = 1 << 1,
    ScrollLock = 1 << 2,
}

/// <summary>
/// Interface for input device drivers.
/// </summary>
public interface IInputDevice : IDriver
{
    /// <summary>
    /// Device name for identification.
    /// </summary>
    string DeviceName { get; }

    /// <summary>
    /// Device type.
    /// </summary>
    InputDeviceType DeviceType { get; }

    /// <summary>
    /// Device capabilities.
    /// </summary>
    InputCapabilities Capabilities { get; }

    /// <summary>
    /// Poll the device for events.
    /// Returns the number of events written to the buffer.
    /// </summary>
    /// <param name="buffer">Buffer to receive events</param>
    /// <param name="maxEvents">Maximum events to return</param>
    /// <returns>Number of events returned</returns>
    int Poll(Span<InputEvent> buffer, int maxEvents);

    /// <summary>
    /// Check if there are pending events.
    /// </summary>
    bool HasPendingEvents { get; }
}

/// <summary>
/// Interface for keyboard devices.
/// </summary>
public interface IKeyboard : IInputDevice
{
    /// <summary>
    /// Current modifier key state.
    /// </summary>
    KeyModifiers ModifierState { get; }

    /// <summary>
    /// Get/set keyboard LED state.
    /// </summary>
    KeyboardLEDs LEDState { get; set; }

    /// <summary>
    /// Check if a key is currently pressed.
    /// </summary>
    bool IsKeyPressed(ScanCode scanCode);

    /// <summary>
    /// Set the key repeat rate.
    /// </summary>
    /// <param name="delayMs">Initial delay before repeat starts</param>
    /// <param name="rateMs">Time between repeats</param>
    void SetRepeatRate(int delayMs, int rateMs);
}

/// <summary>
/// Interface for mouse/pointer devices.
/// </summary>
public interface IMouse : IInputDevice
{
    /// <summary>
    /// Current button state.
    /// </summary>
    MouseButtons ButtonState { get; }

    /// <summary>
    /// Number of buttons.
    /// </summary>
    int ButtonCount { get; }

    /// <summary>
    /// True if device uses relative motion (mouse).
    /// False if absolute (touchpad, tablet).
    /// </summary>
    bool IsRelative { get; }

    /// <summary>
    /// Set mouse resolution/sensitivity.
    /// </summary>
    void SetResolution(int dpi);
}
