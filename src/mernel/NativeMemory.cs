// netos mernel - Native memory allocation
// Simple bump allocator for early kernel initialization.
// Will be replaced with proper memory management later.

using System.Runtime.InteropServices;

namespace Mernel;

/// <summary>
/// Internal struct to hold the fixed buffer (can't have fixed buffers in static classes)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct MemoryBuffer
{
    // 64KB static buffer for early allocations
    public const int Size = 64 * 1024;
    public fixed byte Data[Size];
}

/// <summary>
/// Simple bump allocator for early kernel use.
/// Allocates from a static buffer until proper memory management is set up.
/// </summary>
public static unsafe class NativeMemory
{
    private static MemoryBuffer _buffer;
    private static int _offset = 0;

    /// <summary>
    /// Allocate memory (8-byte aligned)
    /// </summary>
    public static void* Alloc(nuint size)
    {
        // Align to 8 bytes
        int alignedOffset = (_offset + 7) & ~7;
        int newOffset = alignedOffset + (int)size;

        if (newOffset > MemoryBuffer.Size)
        {
            // Out of memory - halt for now
            // In a real kernel, this would panic
            return null;
        }

        _offset = newOffset;

        fixed (byte* buf = _buffer.Data)
        {
            return buf + alignedOffset;
        }
    }

    /// <summary>
    /// Allocate zeroed memory
    /// </summary>
    public static void* AllocZeroed(nuint size)
    {
        void* ptr = Alloc(size);
        if (ptr != null)
        {
            byte* p = (byte*)ptr;
            for (nuint i = 0; i < size; i++)
                p[i] = 0;
        }
        return ptr;
    }

    /// <summary>
    /// Free is a no-op for bump allocator
    /// </summary>
    public static void Free(void* ptr)
    {
        // No-op - bump allocator doesn't support freeing
    }

    /// <summary>
    /// Get total bytes allocated
    /// </summary>
    public static int BytesAllocated => _offset;
}
