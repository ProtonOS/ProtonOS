// ProtonOS DDK - Framebuffer Interface

using System;
using ProtonOS.DDK.Drivers;

namespace ProtonOS.DDK.Graphics;

/// <summary>
/// Pixel format.
/// </summary>
public enum PixelFormat
{
    Unknown = 0,

    /// <summary>32-bit BGRA (blue in lowest byte).</summary>
    BGRA32,

    /// <summary>32-bit RGBA (red in lowest byte).</summary>
    RGBA32,

    /// <summary>32-bit BGRx (blue in lowest byte, alpha ignored).</summary>
    BGRX32,

    /// <summary>32-bit RGBx (red in lowest byte, alpha ignored).</summary>
    RGBX32,

    /// <summary>24-bit BGR.</summary>
    BGR24,

    /// <summary>24-bit RGB.</summary>
    RGB24,

    /// <summary>16-bit RGB (5-6-5).</summary>
    RGB565,

    /// <summary>8-bit indexed color.</summary>
    Indexed8,
}

/// <summary>
/// Framebuffer information.
/// </summary>
public struct FramebufferInfo
{
    /// <summary>Width in pixels.</summary>
    public uint Width;

    /// <summary>Height in pixels.</summary>
    public uint Height;

    /// <summary>Bytes per scan line (may include padding).</summary>
    public uint Pitch;

    /// <summary>Bits per pixel.</summary>
    public uint BitsPerPixel;

    /// <summary>Pixel format.</summary>
    public PixelFormat Format;

    /// <summary>Physical address of framebuffer memory.</summary>
    public ulong PhysicalAddress;

    /// <summary>Virtual address of framebuffer memory.</summary>
    public ulong VirtualAddress;

    /// <summary>Size of framebuffer in bytes.</summary>
    public ulong Size;

    /// <summary>Bytes per pixel.</summary>
    public uint BytesPerPixel => BitsPerPixel / 8;
}

/// <summary>
/// Interface for framebuffer access.
/// </summary>
public unsafe interface IFramebuffer : IDriver
{
    /// <summary>
    /// Framebuffer information.
    /// </summary>
    FramebufferInfo Info { get; }

    /// <summary>
    /// Pointer to framebuffer memory.
    /// </summary>
    byte* Buffer { get; }

    /// <summary>
    /// Width in pixels.
    /// </summary>
    uint Width => Info.Width;

    /// <summary>
    /// Height in pixels.
    /// </summary>
    uint Height => Info.Height;

    /// <summary>
    /// Set a pixel at (x, y) to the specified color.
    /// </summary>
    void SetPixel(int x, int y, uint color);

    /// <summary>
    /// Get the color of a pixel at (x, y).
    /// </summary>
    uint GetPixel(int x, int y);

    /// <summary>
    /// Fill a rectangle with a solid color.
    /// </summary>
    void FillRect(int x, int y, int width, int height, uint color);

    /// <summary>
    /// Copy pixels from a buffer to the framebuffer.
    /// </summary>
    void CopyRect(int destX, int destY, int width, int height, byte* source, int sourcePitch);

    /// <summary>
    /// Scroll the framebuffer contents.
    /// </summary>
    void Scroll(int dx, int dy, uint fillColor);

    /// <summary>
    /// Clear the entire framebuffer to a color.
    /// </summary>
    void Clear(uint color);

    /// <summary>
    /// Synchronize the framebuffer (wait for vsync or flush).
    /// </summary>
    void Sync();
}

/// <summary>
/// Helper for color operations.
/// </summary>
public static class Color
{
    /// <summary>
    /// Create a 32-bit BGRA color.
    /// </summary>
    public static uint BGRA(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24);
    }

    /// <summary>
    /// Create a 32-bit RGBA color.
    /// </summary>
    public static uint RGBA(byte r, byte g, byte b, byte a = 255)
    {
        return (uint)r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);
    }

    /// <summary>
    /// Extract red component from BGRA color.
    /// </summary>
    public static byte GetR_BGRA(uint color) => (byte)(color >> 16);

    /// <summary>
    /// Extract green component from BGRA color.
    /// </summary>
    public static byte GetG_BGRA(uint color) => (byte)(color >> 8);

    /// <summary>
    /// Extract blue component from BGRA color.
    /// </summary>
    public static byte GetB_BGRA(uint color) => (byte)color;

    /// <summary>
    /// Extract alpha component from BGRA color.
    /// </summary>
    public static byte GetA_BGRA(uint color) => (byte)(color >> 24);

    // Common colors (BGRA format)
    public static readonly uint Black = BGRA(0, 0, 0);
    public static readonly uint White = BGRA(255, 255, 255);
    public static readonly uint Red = BGRA(255, 0, 0);
    public static readonly uint Green = BGRA(0, 255, 0);
    public static readonly uint Blue = BGRA(0, 0, 255);
    public static readonly uint Yellow = BGRA(255, 255, 0);
    public static readonly uint Cyan = BGRA(0, 255, 255);
    public static readonly uint Magenta = BGRA(255, 0, 255);
    public static readonly uint Gray = BGRA(128, 128, 128);
    public static readonly uint DarkGray = BGRA(64, 64, 64);
    public static readonly uint LightGray = BGRA(192, 192, 192);
}
