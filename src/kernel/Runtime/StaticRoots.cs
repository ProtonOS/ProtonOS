// netos kernel - Static Roots Enumeration
// Enumerates all static fields containing object references for GC root scanning.
//
// NativeAOT generates a __GCStaticRegion containing pointers to static GC roots.
// The region format is an array of pointers, each pointing to a StaticGcDesc structure:
//   [StaticGcDesc*][StaticGcDesc*][StaticGcDesc*]...
//
// Each StaticGcDesc contains information about the static field(s) and the actual
// object references that need to be tracked as GC roots.

using System;
using System.Runtime.InteropServices;
using Kernel.Platform;

namespace Kernel.Runtime;

/// <summary>
/// Enumerates static GC roots from the __GCStaticRegion.
/// </summary>
public static unsafe class StaticRoots
{
    /// <summary>
    /// Enumerate all static GC roots.
    /// </summary>
    /// <param name="callback">Called for each static root with pointer to the object reference slot.</param>
    /// <remarks>
    /// After InitializeStatics runs, each static block contains a pointer to a "static holder"
    /// object that holds all static fields for a type. The holder's MethodTable has GCDesc
    /// metadata describing which fields contain GC references. We enumerate those reference
    /// fields and report them as roots.
    /// </remarks>
    public static void EnumerateStaticRoots(delegate*<void**, void> callback)
    {
        if (!ReadyToRunInfo.GetGCStaticRegion(out void* start, out void* end))
        {
            DebugConsole.WriteLine("[StaticRoots] No GCStaticRegion found");
            return;
        }

        // The GCStaticRegion uses relative pointers (int32 offsets)
        // Each entry is a 4-byte relative pointer to a "static block"
        int* current = (int*)start;
        int* regionEnd = (int*)end;

        while (current < regionEnd)
        {
            int relOffset = *current;
            if (relOffset != 0)
            {
                // Relative pointer: target = &relOffset + relOffset
                nint* pBlock = (nint*)((byte*)current + relOffset);

                // The block value contains either:
                // - An uninitialized marker (low bit set) with relative EEType pointer
                // - An initialized pointer to a "static holder" object
                nint blockValue = *pBlock;

                const nint Uninitialized = 1;
                if ((blockValue & Uninitialized) == 0 && blockValue != 0)
                {
                    // Initialized - blockValue is a pointer to the static holder object
                    void* holderObj = (void*)blockValue;

                    // Get the holder's MethodTable to enumerate its reference fields
                    MethodTable* mt = *(MethodTable**)holderObj;
                    if (mt != null && mt->HasPointers)
                    {
                        // Use GCDesc to enumerate reference fields in the holder
                        GCDescHelper.EnumerateReferences(holderObj, mt, callback);
                    }
                }
            }
            current++;
        }
    }

    /// <summary>
    /// Test and dump static roots information.
    /// </summary>
    public static void DumpStaticRoots()
    {
        if (!ReadyToRunInfo.GetGCStaticRegion(out void* start, out void* end))
        {
            DebugConsole.WriteLine("[StaticRoots] No GCStaticRegion found");
            return;
        }

        ulong regionSize = (ulong)((byte*)end - (byte*)start);

        DebugConsole.Write("[StaticRoots] GCStaticRegion: 0x");
        DebugConsole.WriteHex((ulong)start);
        DebugConsole.Write(" - 0x");
        DebugConsole.WriteHex((ulong)end);
        DebugConsole.Write(" Size=");
        DebugConsole.WriteDecimal((uint)regionSize);
        DebugConsole.WriteLine(" bytes");

        // Dump raw bytes at the region
        DebugConsole.Write("  Raw bytes: ");
        byte* rawPtr = (byte*)start;
        for (int i = 0; i < (int)regionSize && i < 32; i++)
        {
            DebugConsole.WriteHex(rawPtr[i]);
            DebugConsole.Write(" ");
        }
        DebugConsole.WriteLine();

        // The GCStaticRegion uses relative pointers (int32 offsets)
        // Each entry is a 4-byte relative pointer to a "static block"
        int entryCount = (int)(regionSize / sizeof(int));
        DebugConsole.Write("  Entry count (4-byte): ");
        DebugConsole.WriteDecimal(entryCount);
        DebugConsole.WriteLine();

        int* current = (int*)start;
        int* regionEnd = (int*)end;
        int index = 0;
        int validCount = 0;

        while (current < regionEnd)
        {
            int relOffset = *current;
            // Relative pointer: target = &relOffset + relOffset
            nint* pBlock = (nint*)((byte*)current + relOffset);

            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(index);
            DebugConsole.Write("] RelOffset=0x");
            DebugConsole.WriteHex((uint)relOffset);
            DebugConsole.Write(" -> Block=0x");
            DebugConsole.WriteHex((ulong)pBlock);

            // The block contains another relative pointer to the MethodTable
            // with flags in the low bits
            nint blockValue = *pBlock;
            DebugConsole.Write(" BlockVal=0x");
            DebugConsole.WriteHex((ulong)blockValue);

            // Check if using relative pointers (low bit of block address indicates initialized)
            const nint Uninitialized = 1;
            if ((blockValue & Uninitialized) != 0)
            {
                DebugConsole.Write(" [Uninitialized]");
                // The value (with low bit masked) is a relative pointer to the EEType
                // For relative pointers: target = &pBlock + (blockValue >> 1) (shifted)
                // Actually, the masked value is the relative offset
                nint mtOffset = blockValue & ~Uninitialized;
                // The EEType is at: (byte*)pBlock + mtOffset (treating mtOffset as offset)
                // But this may be an absolute address or we need to read another relptr
            }
            else
            {
                // After initialization, this slot contains the pinned object reference
                void* objRef = (void*)blockValue;
                DebugConsole.Write(" -> ObjRef=0x");
                DebugConsole.WriteHex((ulong)objRef);
                if (objRef != null)
                {
                    MethodTable* mt = *(MethodTable**)objRef;
                    DebugConsole.Write(" MT=0x");
                    DebugConsole.WriteHex((ulong)mt);
                }
            }

            DebugConsole.WriteLine();
            validCount++;
            current++;
            index++;
        }

        DebugConsole.Write("[StaticRoots] Found ");
        DebugConsole.WriteDecimal(validCount);
        DebugConsole.WriteLine(" static block entries");
    }

    /// <summary>
    /// Count static roots (for GC statistics).
    /// </summary>
    public static int CountStaticRoots()
    {
        if (!ReadyToRunInfo.GetGCStaticRegion(out void* start, out void* end))
            return 0;

        int count = 0;
        void** current = (void**)start;
        void** regionEnd = (void**)end;

        while (current < regionEnd)
        {
            if (*current != null)
                count++;
            current++;
        }

        return count;
    }
}
