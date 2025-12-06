// ProtonOS DDK - Input Type Definitions

using System;

namespace ProtonOS.DDK.Input;

/// <summary>
/// Input device type.
/// </summary>
public enum InputDeviceType
{
    Unknown = 0,
    Keyboard,
    Mouse,
    Touchpad,
    Touchscreen,
    Gamepad,
    Joystick,
    Tablet,
}

/// <summary>
/// Input event type.
/// </summary>
public enum InputEventType
{
    /// <summary>Key pressed down.</summary>
    KeyDown,

    /// <summary>Key released.</summary>
    KeyUp,

    /// <summary>Mouse/pointer moved.</summary>
    MouseMove,

    /// <summary>Mouse button pressed.</summary>
    MouseButtonDown,

    /// <summary>Mouse button released.</summary>
    MouseButtonUp,

    /// <summary>Mouse wheel scrolled.</summary>
    MouseScroll,

    /// <summary>Touch started.</summary>
    TouchStart,

    /// <summary>Touch moved.</summary>
    TouchMove,

    /// <summary>Touch ended.</summary>
    TouchEnd,

    /// <summary>Gamepad button pressed.</summary>
    GamepadButtonDown,

    /// <summary>Gamepad button released.</summary>
    GamepadButtonUp,

    /// <summary>Gamepad axis moved.</summary>
    GamepadAxis,
}

/// <summary>
/// Keyboard modifier keys.
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    LeftShift = 1 << 0,
    RightShift = 1 << 1,
    LeftControl = 1 << 2,
    RightControl = 1 << 3,
    LeftAlt = 1 << 4,
    RightAlt = 1 << 5,
    LeftMeta = 1 << 6,    // Windows/Command key
    RightMeta = 1 << 7,
    CapsLock = 1 << 8,
    NumLock = 1 << 9,
    ScrollLock = 1 << 10,

    Shift = LeftShift | RightShift,
    Control = LeftControl | RightControl,
    Alt = LeftAlt | RightAlt,
    Meta = LeftMeta | RightMeta,
}

/// <summary>
/// Mouse buttons.
/// </summary>
[Flags]
public enum MouseButtons
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Middle = 1 << 2,
    Button4 = 1 << 3,
    Button5 = 1 << 4,
}

/// <summary>
/// Scan code (hardware key code).
/// Based on USB HID usage codes.
/// </summary>
public enum ScanCode : byte
{
    None = 0x00,

    // Letters
    A = 0x04, B = 0x05, C = 0x06, D = 0x07, E = 0x08, F = 0x09, G = 0x0A, H = 0x0B,
    I = 0x0C, J = 0x0D, K = 0x0E, L = 0x0F, M = 0x10, N = 0x11, O = 0x12, P = 0x13,
    Q = 0x14, R = 0x15, S = 0x16, T = 0x17, U = 0x18, V = 0x19, W = 0x1A, X = 0x1B,
    Y = 0x1C, Z = 0x1D,

    // Numbers
    D1 = 0x1E, D2 = 0x1F, D3 = 0x20, D4 = 0x21, D5 = 0x22,
    D6 = 0x23, D7 = 0x24, D8 = 0x25, D9 = 0x26, D0 = 0x27,

    // Control
    Enter = 0x28,
    Escape = 0x29,
    Backspace = 0x2A,
    Tab = 0x2B,
    Space = 0x2C,

    // Punctuation
    Minus = 0x2D,
    Equals = 0x2E,
    LeftBracket = 0x2F,
    RightBracket = 0x30,
    Backslash = 0x31,
    Semicolon = 0x33,
    Apostrophe = 0x34,
    Grave = 0x35,
    Comma = 0x36,
    Period = 0x37,
    Slash = 0x38,

    // Function keys
    CapsLock = 0x39,
    F1 = 0x3A, F2 = 0x3B, F3 = 0x3C, F4 = 0x3D, F5 = 0x3E, F6 = 0x3F,
    F7 = 0x40, F8 = 0x41, F9 = 0x42, F10 = 0x43, F11 = 0x44, F12 = 0x45,

    // Navigation
    PrintScreen = 0x46,
    ScrollLock = 0x47,
    Pause = 0x48,
    Insert = 0x49,
    Home = 0x4A,
    PageUp = 0x4B,
    Delete = 0x4C,
    End = 0x4D,
    PageDown = 0x4E,
    Right = 0x4F,
    Left = 0x50,
    Down = 0x51,
    Up = 0x52,

    // Numpad
    NumLock = 0x53,
    NumpadDivide = 0x54,
    NumpadMultiply = 0x55,
    NumpadMinus = 0x56,
    NumpadPlus = 0x57,
    NumpadEnter = 0x58,
    Numpad1 = 0x59, Numpad2 = 0x5A, Numpad3 = 0x5B,
    Numpad4 = 0x5C, Numpad5 = 0x5D, Numpad6 = 0x5E,
    Numpad7 = 0x5F, Numpad8 = 0x60, Numpad9 = 0x61,
    Numpad0 = 0x62, NumpadPeriod = 0x63,

    // Modifiers
    LeftControl = 0xE0,
    LeftShift = 0xE1,
    LeftAlt = 0xE2,
    LeftMeta = 0xE3,
    RightControl = 0xE4,
    RightShift = 0xE5,
    RightAlt = 0xE6,
    RightMeta = 0xE7,
}

/// <summary>
/// Input event data.
/// This is an unmanaged struct for efficient event passing.
/// </summary>
public struct InputEvent
{
    /// <summary>Event type.</summary>
    public InputEventType Type;

    /// <summary>Timestamp (kernel ticks).</summary>
    public ulong Timestamp;

    /// <summary>Device index that generated this event (-1 if unknown).</summary>
    public int DeviceIndex;

    /// <summary>Scan code for keyboard events.</summary>
    public ScanCode ScanCode;

    /// <summary>Translated character (if applicable).</summary>
    public char Character;

    /// <summary>Current modifier key state.</summary>
    public KeyModifiers Modifiers;

    /// <summary>Mouse button for button events.</summary>
    public MouseButtons Button;

    /// <summary>Current mouse button state.</summary>
    public MouseButtons ButtonState;

    /// <summary>X position or delta.</summary>
    public int X;

    /// <summary>Y position or delta.</summary>
    public int Y;

    /// <summary>Scroll delta (wheel events).</summary>
    public int ScrollDelta;

    /// <summary>Touch ID for multi-touch.</summary>
    public int TouchId;

    /// <summary>Pressure for touch/tablet events (0-1).</summary>
    public float Pressure;
}
