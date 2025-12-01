// ProtonOS kernel - GCDesc Parser
// Enumerates object reference fields from MethodTable's GCDesc metadata.
//
// GCDesc is stored BEFORE the MethodTable in memory and describes which fields
// in an object contain GC references. This is essential for the mark phase.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Runtime;

/// <summary>
/// A series entry in GCDesc describing a contiguous run of reference fields.
/// Stored before MethodTable, growing backwards in memory.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct GCDescSeries
{
    /// <summary>
    /// Adjusted size: (actual_byte_count - base_size).
    /// Add base_size back to get actual byte count of references.
    /// </summary>
    public nint SeriesSize;

    /// <summary>
    /// Byte offset from object start to first reference in this series.
    /// </summary>
    public nint StartOffset;
}

/// <summary>
/// Provides methods to parse GCDesc and enumerate object references.
/// </summary>
public static unsafe class GCDescHelper
{
    /// <summary>
    /// Check if a MethodTable indicates the type has GC pointers.
    /// </summary>
    public static bool HasPointers(MethodTable* mt)
    {
        return mt->HasPointers;
    }

    /// <summary>
    /// Get the series count from GCDesc.
    /// Located at ((nint*)mt)[-1].
    /// </summary>
    /// <returns>
    /// Positive: number of series for regular objects.
    /// Negative: indicates value-type array (absolute value is series count).
    /// Zero: no GCDesc (shouldn't happen if HasPointers is true).
    /// </returns>
    public static nint GetSeriesCount(MethodTable* mt)
    {
        nint* pMT = (nint*)mt;
        return pMT[-1];
    }

    /// <summary>
    /// Get a pointer to the GCDesc series array.
    /// Series are stored growing backwards from the series count.
    /// </summary>
    public static GCDescSeries* GetSeriesArray(MethodTable* mt)
    {
        // Series count is at mt[-1], series array starts at mt[-2] and grows backwards
        return (GCDescSeries*)((nint*)mt - 1);
    }

    /// <summary>
    /// Enumerate all reference fields in an object.
    /// </summary>
    /// <param name="obj">Pointer to the object (at MethodTable*).</param>
    /// <param name="mt">The object's MethodTable.</param>
    /// <param name="callback">Called for each reference field with its address.</param>
    public static void EnumerateReferences(void* obj, MethodTable* mt, delegate*<void**, void> callback)
    {
        if (!mt->HasPointers)
            return;

        nint seriesCount = GetSeriesCount(mt);

        if (seriesCount <= 0)
        {
            // Negative count indicates value-type array - handle separately
            if (seriesCount < 0)
            {
                EnumerateValueTypeArrayReferences(obj, mt, -seriesCount, callback);
            }
            return;
        }

        // Regular object with series
        nint baseSize = (nint)mt->_uBaseSize;
        GCDescSeries* series = GetSeriesArray(mt);

        for (nint i = 1; i <= seriesCount; i++)
        {
            nint offset = series[-i].StartOffset;
            nint size = series[-i].SeriesSize + baseSize;
            nint count = size / sizeof(nint);

            void** refPtr = (void**)((byte*)obj + offset);

            for (nint j = 0; j < count; j++)
            {
                callback(&refPtr[j]);
            }
        }
    }

    /// <summary>
    /// Enumerate references in a value-type array (array of structs containing refs).
    /// </summary>
    private static void EnumerateValueTypeArrayReferences(void* obj, MethodTable* mt, nint seriesCount,
        delegate*<void**, void> callback)
    {
        // For value-type arrays, we need the element count and element size
        int elementCount = *(int*)((byte*)obj + sizeof(nint)); // Length is after MethodTable*
        ushort componentSize = mt->_usComponentSize;

        if (elementCount == 0 || componentSize == 0)
            return;

        GCDescSeries* series = GetSeriesArray(mt);

        // Calculate where array elements start (after header: MethodTable* + length)
        byte* elementsStart = (byte*)obj + mt->_uBaseSize - (uint)(elementCount * componentSize);

        // For each element in the array
        for (int elemIdx = 0; elemIdx < elementCount; elemIdx++)
        {
            byte* element = elementsStart + (elemIdx * componentSize);

            // For each series in the value type
            for (nint i = 1; i <= seriesCount; i++)
            {
                nint offset = series[-i].StartOffset;
                nint size = series[-i].SeriesSize;
                nint count = size / sizeof(nint);

                void** refPtr = (void**)(element + offset);

                for (nint j = 0; j < count; j++)
                {
                    callback(&refPtr[j]);
                }
            }
        }
    }

    /// <summary>
    /// Dump GCDesc information for a MethodTable.
    /// </summary>
    public static void DumpGCDesc(MethodTable* mt, string? typeName = null)
    {
        DebugConsole.Write("[GCDesc] MT=0x");
        DebugConsole.WriteHex((ulong)mt);

        if (typeName != null)
        {
            DebugConsole.Write(" (");
            DebugConsole.Write(typeName);
            DebugConsole.Write(")");
        }

        DebugConsole.Write(" BaseSize=");
        DebugConsole.WriteDecimal(mt->_uBaseSize);
        DebugConsole.Write(" CompSize=");
        DebugConsole.WriteDecimal(mt->_usComponentSize);
        DebugConsole.Write(" Flags=0x");
        DebugConsole.WriteHex(mt->CombinedFlags);

        if (mt->HasPointers)
            DebugConsole.Write(" [HasPtrs]");
        if (mt->HasFinalizer)
            DebugConsole.Write(" [Finalizer]");
        if (mt->IsArray)
            DebugConsole.Write(" [Array]");

        DebugConsole.WriteLine();

        if (!mt->HasPointers)
        {
            DebugConsole.WriteLine("  No GCDesc (no pointers)");
            return;
        }

        nint seriesCount = GetSeriesCount(mt);
        DebugConsole.Write("  SeriesCount=");
        DebugConsole.WriteDecimal((int)seriesCount);
        DebugConsole.WriteLine();

        if (seriesCount <= 0)
        {
            if (seriesCount < 0)
            {
                DebugConsole.WriteLine("  (Value-type array format)");
            }
            return;
        }

        GCDescSeries* series = GetSeriesArray(mt);
        nint baseSize = (nint)mt->_uBaseSize;

        for (nint i = 1; i <= seriesCount; i++)
        {
            nint offset = series[-i].StartOffset;
            nint size = series[-i].SeriesSize + baseSize;
            nint refCount = size / sizeof(nint);

            DebugConsole.Write("  Series[");
            DebugConsole.WriteDecimal((int)(i - 1));
            DebugConsole.Write("]: Offset=");
            DebugConsole.WriteDecimal((int)offset);
            DebugConsole.Write(" Size=");
            DebugConsole.WriteDecimal((int)size);
            DebugConsole.Write(" Refs=");
            DebugConsole.WriteDecimal((int)refCount);
            DebugConsole.WriteLine();
        }
    }

    /// <summary>
    /// Test GCDesc parsing by walking frozen objects from RTR header.
    /// Frozen objects are laid out as: [8-byte header][MethodTable*][object data...]
    /// </summary>
    public static void TestWithFrozenObjects()
    {
        if (!ReadyToRunInfo.IsInitialized)
        {
            DebugConsole.WriteLine("[GCDesc] RTR not initialized");
            return;
        }

        if (!ReadyToRunInfo.GetFrozenObjectRegion(out void* start, out void* end))
        {
            DebugConsole.WriteLine("[GCDesc] No frozen object region");
            return;
        }

        ulong regionSize = (ulong)((byte*)end - (byte*)start);
        DebugConsole.Write("[GCDesc] Frozen region: ");
        DebugConsole.WriteDecimal((uint)regionSize);
        DebugConsole.WriteLine(" bytes");

        // Walk frozen objects
        // Format: [8-byte object header][8-byte MethodTable*][object data...]
        // Object size = BaseSize for non-arrays, BaseSize + Length*ComponentSize for arrays
        byte* current = (byte*)start;
        byte* regionEnd = (byte*)end;
        ulong imageBase = ReadyToRunInfo.ImageBase;

        int objectCount = 0;
        int stringCount = 0;
        int withPtrsCount = 0;
        int totalRefs = 0;

        while (current + 16 <= regionEnd) // Need at least header + MT*
        {
            // Object header is at current, MethodTable* is at current+8
            ulong objHeader = *(ulong*)current;
            MethodTable* mt = *(MethodTable**)(current + 8);

            // Validate MT pointer is within image
            if ((ulong)mt < imageBase || (ulong)mt >= imageBase + 0x200000)
            {
                // Skip 8 bytes and try again (might be padding)
                current += 8;
                continue;
            }

            uint baseSize = mt->_uBaseSize;
            if (baseSize < 16 || baseSize > 0x100000)
            {
                current += 8;
                continue;
            }

            // Calculate actual object size
            uint objSize;
            if (mt->HasComponentSize && mt->ComponentSize > 0)
            {
                // Array or string - read length from object
                int length = *(int*)(current + 16); // Length is after MT*
                if (length < 0 || length > 0x100000)
                {
                    current += 8;
                    continue;
                }
                objSize = baseSize + (uint)(length * mt->ComponentSize);
                if (mt->ComponentSize == 2) // Likely string
                    stringCount++;
            }
            else
            {
                objSize = baseSize;
            }

            objectCount++;

            // Check for GCDesc
            if (mt->HasPointers)
            {
                withPtrsCount++;
                nint seriesCount = GetSeriesCount(mt);
                if (seriesCount > 0 && seriesCount < 100)
                {
                    GCDescSeries* series = GetSeriesArray(mt);
                    nint mtBaseSize = (nint)mt->_uBaseSize;

                    for (nint i = 1; i <= seriesCount; i++)
                    {
                        nint size = series[-i].SeriesSize + mtBaseSize;
                        if (size > 0 && size < 1000)
                            totalRefs += (int)(size / sizeof(nint));
                    }
                }
            }

            // Move to next object: header(8) + objSize, aligned to 8
            uint totalSize = 8 + objSize;
            totalSize = (totalSize + 7) & ~7u;
            current += totalSize;
        }

        DebugConsole.Write("[GCDesc] ");
        DebugConsole.WriteDecimal(objectCount);
        DebugConsole.Write(" objects (");
        DebugConsole.WriteDecimal(stringCount);
        DebugConsole.Write(" strings), ");
        DebugConsole.WriteDecimal(withPtrsCount);
        DebugConsole.Write(" with refs, ");
        DebugConsole.WriteDecimal(totalRefs);
        DebugConsole.WriteLine(" total ref slots");
    }

    /// <summary>
    /// Test GCDesc with a heap-allocated object that has references.
    /// Call this after heap is initialized.
    /// </summary>
    public static void TestWithHeapObject()
    {
        // Create an Exception - it has reference fields (message, innerException, etc.)
        var ex = new Exception();

        // Get the MethodTable pointer from the object
        // Object layout: [MethodTable*][fields...]
        // 'ex' is a reference that contains the object address
        // Use Unsafe.As to reinterpret the reference as IntPtr to get the raw address
        nint objAddr = System.Runtime.CompilerServices.Unsafe.As<Exception, nint>(ref ex);
        MethodTable* mt = *(MethodTable**)objAddr;

        DebugConsole.Write("[GCDesc] Exception MT=0x");
        DebugConsole.WriteHex((ulong)mt);
        DebugConsole.Write(" BaseSize=");
        DebugConsole.WriteDecimal(mt->_uBaseSize);
        DebugConsole.Write(" Flags=0x");
        DebugConsole.WriteHex(mt->CombinedFlags);

        if (mt->HasPointers)
        {
            DebugConsole.Write(" [HasPtrs]");
            nint seriesCount = GetSeriesCount(mt);
            DebugConsole.Write(" Series=");
            DebugConsole.WriteDecimal((int)seriesCount);

            if (seriesCount > 0)
            {
                GCDescSeries* series = GetSeriesArray(mt);
                nint baseSize = (nint)mt->_uBaseSize;
                int totalRefSlots = 0;

                for (nint i = 1; i <= seriesCount; i++)
                {
                    nint offset = series[-i].StartOffset;
                    nint size = series[-i].SeriesSize + baseSize;
                    nint refCount = size / sizeof(nint);
                    totalRefSlots += (int)refCount;

                    DebugConsole.Write(" [off=");
                    DebugConsole.WriteDecimal((int)offset);
                    DebugConsole.Write(",cnt=");
                    DebugConsole.WriteDecimal((int)refCount);
                    DebugConsole.Write("]");
                }

                DebugConsole.Write(" Total=");
                DebugConsole.WriteDecimal(totalRefSlots);
            }
        }
        else
        {
            DebugConsole.Write(" [NO refs - unexpected!]");
        }

        DebugConsole.WriteLine();
    }
}
