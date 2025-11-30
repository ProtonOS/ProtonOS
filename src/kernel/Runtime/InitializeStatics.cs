// netos kernel - Static Field Initialization
// Initializes the GCStaticRegion by allocating pinned objects for each static GC field.
//
// NativeAOT generates a GCStaticRegion containing relative pointers to "static blocks".
// Each static block starts with a value containing:
//   - Bits [63:2]: Relative pointer to the MethodTable (EEType)
//   - Bit 1: HasPreInitializedData flag
//   - Bit 0: Uninitialized flag (1 = needs initialization)
//
// InitializeStatics walks the region and for each uninitialized block:
//   1. Extracts the MethodTable via relative pointer decoding
//   2. Allocates a pinned object of the appropriate size
//   3. Stores the object reference back in the block (clearing the Uninitialized flag)
//
// After initialization, the block value becomes the actual object reference, and
// static fields work correctly.

using System;
using System.Runtime.InteropServices;
using Kernel.Memory;
using Kernel.Platform;

namespace Kernel.Runtime;

/// <summary>
/// Flags used in GC static block values.
/// </summary>
public static class GCStaticRegionFlags
{
    /// <summary>Block needs initialization (bit 0).</summary>
    public const nint Uninitialized = 1;

    /// <summary>Block has pre-initialized data blob (bit 1).</summary>
    public const nint HasPreInitializedData = 2;

    /// <summary>Mask for all flags (bits 0-1).</summary>
    public const nint FlagsMask = 3;
}

/// <summary>
/// Initializes static GC fields from the GCStaticRegion.
/// Must be called early in kernel startup, after heap is available but before
/// any static GC fields are accessed.
/// </summary>
public static unsafe class InitializeStatics
{
    private static bool _initialized;

    /// <summary>
    /// Initialize all static GC fields.
    /// </summary>
    /// <returns>True if successful, false if GCStaticRegion not found or error.</returns>
    public static bool Init()
    {
        if (_initialized)
            return true;

        if (!ReadyToRunInfo.GetGCStaticRegion(out void* start, out void* end))
        {
            DebugConsole.WriteLine("[InitStatics] No GCStaticRegion found");
            return false;
        }

        ulong regionSize = (ulong)((byte*)end - (byte*)start);
        int entryCount = (int)(regionSize / sizeof(int));

        DebugConsole.Write("[InitStatics] Processing ");
        DebugConsole.WriteDecimal(entryCount);
        DebugConsole.WriteLine(" static blocks...");

        int initializedCount = 0;
        int skippedCount = 0;

        // Walk the region as 4-byte relative pointers
        int* current = (int*)start;
        int* regionEnd = (int*)end;

        while (current < regionEnd)
        {
            // Decode relative pointer to get static block address
            int relOffset = *current;
            int* pBlock = (int*)((byte*)current + relOffset);

            // Read the block value as 32-bit signed (NativeAOT uses relptr32 format)
            int blockValue32 = *pBlock;

            // Check if this block needs initialization (flags are in low bits)
            if ((blockValue32 & (int)GCStaticRegionFlags.Uninitialized) != 0)
            {
                // The block value is a 32-bit signed relative pointer with flags in low bits
                // Mask off flags and use as signed offset to get MethodTable address
                int mtRelOffset32 = blockValue32 & ~(int)GCStaticRegionFlags.FlagsMask;

                // Calculate MethodTable address: block address + signed offset
                MethodTable* mt = (MethodTable*)((byte*)pBlock + mtRelOffset32);

                if (mt == null)
                {
                    DebugConsole.WriteLine("[InitStatics] Warning: null MethodTable");
                    current++;
                    continue;
                }

                // Allocate the static object using the base size from MethodTable
                // IMPORTANT: Allocate from GCHeap so GC can find and mark these objects!
                uint objectSize = mt->BaseSize;
                if (objectSize < 24) objectSize = 24; // Minimum object size (MT* + sync block)

                void* obj = GCHeap.AllocZeroed(objectSize);
                if (obj == null)
                {
                    DebugConsole.WriteLine("[InitStatics] Failed to allocate static object!");
                    return false;
                }

                // Set up the object header: first pointer is the MethodTable
                *(MethodTable**)obj = mt;

                // Check for pre-initialized data
                if ((blockValue32 & (int)GCStaticRegionFlags.HasPreInitializedData) != 0)
                {
                    // The pre-initialized data follows the relative pointer in the block
                    // Copy it to the allocated object
                    byte* preInitData = (byte*)pBlock + sizeof(int);
                    byte* objData = (byte*)obj + sizeof(void*); // Skip MT pointer
                    ulong dataSize = objectSize - (uint)sizeof(void*);

                    for (ulong i = 0; i < dataSize; i++)
                        objData[i] = preInitData[i];
                }

                // Store the object reference back in the block
                // The block slot needs to hold a full pointer (nint), not just int
                // After initialization, the block holds the object reference directly
                *(nint*)pBlock = (nint)obj;

                initializedCount++;
            }
            else
            {
                // Already initialized (or no GC statics)
                skippedCount++;
            }

            current++;
        }

        DebugConsole.Write("[InitStatics] Initialized ");
        DebugConsole.WriteDecimal(initializedCount);
        DebugConsole.Write(" blocks, skipped ");
        DebugConsole.WriteDecimal(skippedCount);
        DebugConsole.WriteLine();

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Check if statics have been initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;
}
