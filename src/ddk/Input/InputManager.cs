// ProtonOS DDK - Input Manager
// Centralizes input from all input devices.

using System;
using System.Collections.Generic;

namespace ProtonOS.DDK.Input;

/// <summary>
/// Delegate for input event callbacks.
/// </summary>
public delegate void InputEventCallback(ref InputEvent evt);

/// <summary>
/// Manages input devices and event routing.
/// </summary>
public static class InputManager
{
    private static List<IInputDevice> _devices = new();
    private static List<InputEvent> _eventQueue = new();
    private static KeyModifiers _modifierState;
    private static MouseButtons _buttonState;
    private static int _mouseX, _mouseY;
    private static bool _initialized;

    // Event callbacks
    private static InputEventCallback? _keyCallback;
    private static InputEventCallback? _mouseCallback;
    private static InputEventCallback? _touchCallback;

    /// <summary>
    /// All registered input devices.
    /// </summary>
    public static IReadOnlyList<IInputDevice> Devices => _devices;

    /// <summary>
    /// Current keyboard modifier state.
    /// </summary>
    public static KeyModifiers ModifierState => _modifierState;

    /// <summary>
    /// Current mouse button state.
    /// </summary>
    public static MouseButtons ButtonState => _buttonState;

    /// <summary>
    /// Current mouse X position.
    /// </summary>
    public static int MouseX => _mouseX;

    /// <summary>
    /// Current mouse Y position.
    /// </summary>
    public static int MouseY => _mouseY;

    /// <summary>
    /// Initialize the input manager.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized)
            return;

        _devices = new List<IInputDevice>();
        _eventQueue = new List<InputEvent>();
        _modifierState = KeyModifiers.None;
        _buttonState = MouseButtons.None;
        _mouseX = 0;
        _mouseY = 0;
        _initialized = true;
    }

    /// <summary>
    /// Register an input device.
    /// </summary>
    public static void RegisterDevice(IInputDevice device)
    {
        if (!_initialized)
            Initialize();

        _devices.Add(device);
    }

    /// <summary>
    /// Unregister an input device.
    /// </summary>
    public static void UnregisterDevice(IInputDevice device)
    {
        _devices.Remove(device);
    }

    /// <summary>
    /// Set callback for keyboard events.
    /// </summary>
    public static void SetKeyCallback(InputEventCallback? callback)
    {
        _keyCallback = callback;
    }

    /// <summary>
    /// Set callback for mouse events.
    /// </summary>
    public static void SetMouseCallback(InputEventCallback? callback)
    {
        _mouseCallback = callback;
    }

    /// <summary>
    /// Set callback for touch events.
    /// </summary>
    public static void SetTouchCallback(InputEventCallback? callback)
    {
        _touchCallback = callback;
    }

    /// <summary>
    /// Poll all devices and process events.
    /// Call this regularly from the main loop.
    /// </summary>
    public static void Update()
    {
        if (!_initialized)
            return;

        Span<InputEvent> buffer = stackalloc InputEvent[32];

        foreach (var device in _devices)
        {
            int count = device.Poll(buffer, 32);
            for (int i = 0; i < count; i++)
            {
                ProcessEvent(ref buffer[i]);
            }
        }
    }

    /// <summary>
    /// Process a single input event.
    /// </summary>
    private static void ProcessEvent(ref InputEvent evt)
    {
        // Update state based on event type
        switch (evt.Type)
        {
            case InputEventType.KeyDown:
                UpdateModifiers(evt.ScanCode, true);
                evt.Modifiers = _modifierState;
                _keyCallback?.Invoke(ref evt);
                break;

            case InputEventType.KeyUp:
                UpdateModifiers(evt.ScanCode, false);
                evt.Modifiers = _modifierState;
                _keyCallback?.Invoke(ref evt);
                break;

            case InputEventType.MouseMove:
                _mouseX += evt.X;
                _mouseY += evt.Y;
                // Clamp to screen bounds if we know them
                // TODO: Get screen dimensions
                evt.Modifiers = _modifierState;
                evt.ButtonState = _buttonState;
                _mouseCallback?.Invoke(ref evt);
                break;

            case InputEventType.MouseButtonDown:
                _buttonState |= evt.Button;
                evt.ButtonState = _buttonState;
                evt.Modifiers = _modifierState;
                _mouseCallback?.Invoke(ref evt);
                break;

            case InputEventType.MouseButtonUp:
                _buttonState &= ~evt.Button;
                evt.ButtonState = _buttonState;
                evt.Modifiers = _modifierState;
                _mouseCallback?.Invoke(ref evt);
                break;

            case InputEventType.MouseScroll:
                evt.Modifiers = _modifierState;
                evt.ButtonState = _buttonState;
                _mouseCallback?.Invoke(ref evt);
                break;

            case InputEventType.TouchStart:
            case InputEventType.TouchMove:
            case InputEventType.TouchEnd:
                _touchCallback?.Invoke(ref evt);
                break;
        }

        // Add to event queue for polling
        _eventQueue.Add(evt);
    }

    /// <summary>
    /// Update modifier state based on key press/release.
    /// </summary>
    private static void UpdateModifiers(ScanCode scanCode, bool pressed)
    {
        KeyModifiers mod = scanCode switch
        {
            ScanCode.LeftShift => KeyModifiers.LeftShift,
            ScanCode.RightShift => KeyModifiers.RightShift,
            ScanCode.LeftControl => KeyModifiers.LeftControl,
            ScanCode.RightControl => KeyModifiers.RightControl,
            ScanCode.LeftAlt => KeyModifiers.LeftAlt,
            ScanCode.RightAlt => KeyModifiers.RightAlt,
            ScanCode.LeftMeta => KeyModifiers.LeftMeta,
            ScanCode.RightMeta => KeyModifiers.RightMeta,
            ScanCode.CapsLock when pressed => KeyModifiers.CapsLock, // Toggle on press only
            ScanCode.NumLock when pressed => KeyModifiers.NumLock,
            ScanCode.ScrollLock when pressed => KeyModifiers.ScrollLock,
            _ => KeyModifiers.None
        };

        if (mod == KeyModifiers.None)
            return;

        // Toggle keys toggle on press
        if (scanCode == ScanCode.CapsLock || scanCode == ScanCode.NumLock || scanCode == ScanCode.ScrollLock)
        {
            _modifierState ^= mod;
        }
        else if (pressed)
        {
            _modifierState |= mod;
        }
        else
        {
            _modifierState &= ~mod;
        }
    }

    /// <summary>
    /// Get and clear pending events.
    /// </summary>
    public static InputEvent[] GetPendingEvents()
    {
        var events = _eventQueue.ToArray();
        _eventQueue.Clear();
        return events;
    }

    /// <summary>
    /// Clear all pending events.
    /// </summary>
    public static void ClearEvents()
    {
        _eventQueue.Clear();
    }

    /// <summary>
    /// Set absolute mouse position.
    /// </summary>
    public static void SetMousePosition(int x, int y)
    {
        _mouseX = x;
        _mouseY = y;
    }

    /// <summary>
    /// Get the primary keyboard device.
    /// </summary>
    public static IKeyboard? GetKeyboard()
    {
        foreach (var device in _devices)
        {
            if (device is IKeyboard keyboard)
                return keyboard;
        }
        return null;
    }

    /// <summary>
    /// Get the primary mouse device.
    /// </summary>
    public static IMouse? GetMouse()
    {
        foreach (var device in _devices)
        {
            if (device is IMouse mouse)
                return mouse;
        }
        return null;
    }
}
