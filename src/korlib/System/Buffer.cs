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

    /// <summary>
    /// Copies a block of memory from one location to another.
    /// </summary>
    /// <param name="source">A pointer to the source memory.</param>
    /// <param name="destination">A pointer to the destination memory.</param>
    /// <param name="destinationSizeInBytes">The size of the destination buffer.</param>
    /// <param name="sourceBytesToCopy">The number of bytes to copy.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void MemoryCopy(void* source, void* destination, long destinationSizeInBytes, long sourceBytesToCopy)
    {
        if (sourceBytesToCopy > destinationSizeInBytes)
            Environment.FailFast("Buffer overflow in MemoryCopy");

        byte* src = (byte*)source;
        byte* dst = (byte*)destination;

        // Simple byte-by-byte copy
        for (long i = 0; i < sourceBytesToCopy; i++)
        {
            dst[i] = src[i];
        }
    }

    /// <summary>
    /// Copies a specified number of bytes from a source array starting at a particular
    /// offset to a destination array starting at a particular offset.
    /// </summary>
    public static void BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
    {
        if (src == null || dst == null)
            Environment.FailFast("BlockCopy: null array");

        byte* srcPtr = (byte*)Unsafe.AsPointer(ref Unsafe.As<Runtime.CompilerServices.RawArrayData>(src).Data);
        byte* dstPtr = (byte*)Unsafe.AsPointer(ref Unsafe.As<Runtime.CompilerServices.RawArrayData>(dst).Data);

        for (int i = 0; i < count; i++)
        {
            dstPtr[dstOffset + i] = srcPtr[srcOffset + i];
        }
    }

    /// <summary>
    /// Returns the number of bytes in the specified array.
    /// </summary>
    public static int ByteLength(Array array)
    {
        if (array == null)
            Environment.FailFast("ByteLength: null array");

        // Get element size from MethodTable
        void* pMT = *(void**)Unsafe.AsPointer(ref Unsafe.As<Array, byte>(ref array));
        ushort componentSize = *(ushort*)pMT;

        return array.Length * componentSize;
    }

    /// <summary>
    /// Retrieves a byte at a specified location in a specified array.
    /// </summary>
    public static byte GetByte(Array array, int index)
    {
        if (array == null)
            Environment.FailFast("GetByte: null array");

        byte* ptr = (byte*)Unsafe.AsPointer(ref Unsafe.As<Runtime.CompilerServices.RawArrayData>(array).Data);
        return ptr[index];
    }

    /// <summary>
    /// Assigns a specified value to a byte at a particular location in a specified array.
    /// </summary>
    public static void SetByte(Array array, int index, byte value)
    {
        if (array == null)
            Environment.FailFast("SetByte: null array");

        byte* ptr = (byte*)Unsafe.AsPointer(ref Unsafe.As<Runtime.CompilerServices.RawArrayData>(array).Data);
        ptr[index] = value;
    }
}
