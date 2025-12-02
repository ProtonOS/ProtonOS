// System.Buffer - minimal stub for NativeAOT JIT helpers
// Provides Memmove which is used for struct initialization with fixed-size arrays
// BulkMoveWithWriteBarrier is used when copying memory containing GC references

using System.Runtime.CompilerServices;

namespace System;

public static unsafe class Buffer
{
    // Ref-based overload for byte arrays
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Memmove(ref byte destination, ref byte source, nuint byteCount)
    {
        byte* src = (byte*)Unsafe.AsPointer(ref source);
        byte* dst = (byte*)Unsafe.AsPointer(ref destination);

        // Non-overlapping or forward copy safe
        if (dst <= src || dst >= src + byteCount)
        {
            for (nuint i = 0; i < byteCount; i++)
            {
                dst[i] = src[i];
            }
        }
        else
        {
            // Backward copy for overlapping regions where dst > src
            for (nuint i = byteCount; i > 0; i--)
            {
                dst[i - 1] = src[i - 1];
            }
        }
    }

    // BulkMoveWithWriteBarrier - required by NativeAOT for copying memory with GC references
    // In a full runtime, this would notify the GC about reference updates.
    // For our kernel, we just do a simple copy (no concurrent GC to notify).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void BulkMoveWithWriteBarrier(ref byte destination, ref byte source, nuint byteCount)
    {
        // Simple copy - no write barrier needed for our non-concurrent GC
        Memmove(ref destination, ref source, byteCount);
    }
}
