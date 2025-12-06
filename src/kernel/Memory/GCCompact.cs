// ProtonOS kernel - GC Compaction
// Implements the Lisp-2 compacting garbage collection algorithm.
//
// The Lisp-2 algorithm uses three passes:
//   Pass 1: Compute forwarding addresses for all live objects
//   Pass 2: Update all references to use forwarding addresses
//   Pass 3: Move objects to their new locations
//
// This approach handles pinned objects by simply skipping them during compaction.
// Gaps created by pinned objects remain as free space.

using System;
using ProtonOS.Platform;
using ProtonOS.Threading;
using ProtonOS.Runtime;
using ProtonOS.X64;

namespace ProtonOS.Memory;

/// <summary>
/// Compacting garbage collector using the Lisp-2 algorithm.
/// Called by GC.Collect when compaction is requested or needed.
/// </summary>
public static unsafe class GCCompact
{
    // Statistics
    private static ulong _objectsMoved;
    private static ulong _bytesMoved;
    private static ulong _objectsSkipped;  // Pinned objects

    /// <summary>
    /// Perform full compaction of the SOH.
    /// Must be called after mark phase, while world is stopped.
    /// </summary>
    public static void Compact()
    {
        _objectsMoved = 0;
        _bytesMoved = 0;
        _objectsSkipped = 0;

        // Pass 1: Compute forwarding addresses
        ComputeForwardingAddresses();

        // Pass 2: Update all references
        UpdateReferences();

        // Pass 3: Move objects
        MoveObjects();

        DebugConsole.Write("[GCCompact] Moved ");
        DebugConsole.WriteDecimal((uint)_objectsMoved);
        DebugConsole.Write(" objects (");
        DebugConsole.WriteDecimal((uint)(_bytesMoved / 1024));
        DebugConsole.Write(" KB), skipped ");
        DebugConsole.WriteDecimal((uint)_objectsSkipped);
        DebugConsole.WriteLine(" pinned");
    }

    /// <summary>
    /// Pass 1: Walk live objects and compute their new addresses.
    /// Sets forwarding pointers for all objects that will move.
    /// </summary>
    private static void ComputeForwardingAddresses()
    {
        ulong regionSize = GCHeap.RegionSize;
        byte* region = GCHeap.FirstRegion;

        // Compact pointer - where the next object should be placed
        byte* compactPtr = null;
        byte* currentRegion = null;

        while (region != null)
        {
            byte* current = region + 8; // Skip region header

            // For current region, scan up to AllocPtr
            byte* regionEnd;
            if (region == GCHeap.FirstRegion)
            {
                regionEnd = GCHeap.AllocPtr;
            }
            else
            {
                regionEnd = region + regionSize;
            }

            // Initialize compactPtr for first region
            if (compactPtr == null)
            {
                compactPtr = current;
                currentRegion = region;
            }

            while (current + GCHeap.ObjectHeaderSize < regionEnd)
            {
                void* obj = current + GCHeap.ObjectHeaderSize;
                uint blockSize = GCHeap.GetBlockSize(obj);
                if (blockSize == 0)
                    break;

                bool isFree = GCHeap.IsFreeBlock(obj);
                bool isMarked = !isFree && GCHeap.IsMarked(obj);
                bool isPinned = !isFree && GCHeap.IsPinned(obj);
                bool isLOH = !isFree && GCHeap.IsLOHObject(obj);

                if (isMarked && !isPinned && !isLOH)
                {
                    // Live, unpinned SOH object - will be moved
                    void* newAddr = compactPtr + GCHeap.ObjectHeaderSize;

                    if (newAddr != obj)
                    {
                        // Object will move - set forwarding pointer
                        GCHeap.SetForwardingAddress(obj, newAddr);
                    }

                    compactPtr += blockSize;
                }
                else if (isPinned && isMarked)
                {
                    // Pinned object - stays in place
                    // Jump compactPtr to after this object (creating a gap before it if needed)
                    _objectsSkipped++;

                    // If there's a gap before the pinned object, we could track it for free list
                    // For simplicity, we just skip past it
                    if ((byte*)obj - GCHeap.ObjectHeaderSize > compactPtr)
                    {
                        // There's a gap - could add to free list
                        // For now, just move compactPtr past the pinned object
                    }
                    compactPtr = current + blockSize;
                }
                // Free blocks and unmarked objects are skipped (not moved)

                current += blockSize;
            }

            region = *(byte**)region;
        }

        // Remember where allocation should continue after compaction
        // This will be set in MoveObjects after we know the final position
    }

    /// <summary>
    /// Pass 2: Update all references to point to new locations.
    /// </summary>
    private static void UpdateReferences()
    {
        // Update static roots
        StaticRoots.EnumerateStaticRoots(&UpdateReferenceCallback);

        // Update string pool roots
        if (StringPool.IsInitialized)
        {
            StringPool.EnumerateRoots(&UpdateReferenceCallback);
        }

        // Update stack roots from all threads
        UpdateStackRoots();

        // Update references within live objects
        UpdateObjectReferences();
    }

    /// <summary>
    /// Callback to update a reference slot if it points to a moved object.
    /// </summary>
    private static void UpdateReferenceCallback(void** slot)
    {
        void* obj = *slot;
        if (obj == null)
            return;

        if (!GCHeap.IsInHeap(obj))
            return;

        if (GCHeap.HasForwardingPointer(obj))
        {
            *slot = GCHeap.GetForwardingAddress(obj);
        }
    }

    /// <summary>
    /// Update stack roots for all threads.
    /// </summary>
    private static void UpdateStackRoots()
    {
        if (!Scheduler.IsInitialized)
        {
            // No scheduler - update current stack
            UpdateCurrentThreadStackRoots();
            return;
        }

        ref var schedLock = ref Scheduler.SchedulerLock;
        schedLock.Acquire();

        var currentThread = Scheduler.CurrentThread;

        for (var thread = Scheduler.AllThreadsHead; thread != null; thread = thread->NextAll)
        {
            if (thread->State == ThreadState.Terminated)
                continue;

            if (thread == currentThread)
            {
                schedLock.Release();
                UpdateCurrentThreadStackRoots();
                schedLock.Acquire();
            }
            else
            {
                UpdateThreadStackRoots(thread);
            }
        }

        schedLock.Release();
    }

    /// <summary>
    /// Update stack roots for the current thread.
    /// </summary>
    private static void UpdateCurrentThreadStackRoots()
    {
        ExceptionContext context = default;
        capture_context(&context);
        StackRoots.EnumerateStackRoots(&context, &StackRootUpdateCallback, null);
    }

    /// <summary>
    /// Update stack roots for a specific thread.
    /// </summary>
    private static void UpdateThreadStackRoots(Thread* thread)
    {
        ExceptionContext context;
        context.Rip = thread->Context.Rip;
        context.Rsp = thread->Context.Rsp;
        context.Rbp = thread->Context.Rbp;
        context.Rflags = thread->Context.Rflags;
        context.Rax = thread->Context.Rax;
        context.Rbx = thread->Context.Rbx;
        context.Rcx = thread->Context.Rcx;
        context.Rdx = thread->Context.Rdx;
        context.Rsi = thread->Context.Rsi;
        context.Rdi = thread->Context.Rdi;
        context.R8 = thread->Context.R8;
        context.R9 = thread->Context.R9;
        context.R10 = thread->Context.R10;
        context.R11 = thread->Context.R11;
        context.R12 = thread->Context.R12;
        context.R13 = thread->Context.R13;
        context.R14 = thread->Context.R14;
        context.R15 = thread->Context.R15;
        context.Cs = (ushort)thread->Context.Cs;
        context.Ss = (ushort)thread->Context.Ss;

        StackRoots.EnumerateStackRoots(&context, &StackRootUpdateCallback, null);
    }

    /// <summary>
    /// Callback for stack root update.
    /// </summary>
    private static void StackRootUpdateCallback(void** slot, void* callbackContext)
    {
        UpdateReferenceCallback(slot);
    }

    /// <summary>
    /// Update references within all live objects.
    /// </summary>
    private static void UpdateObjectReferences()
    {
        ulong regionSize = GCHeap.RegionSize;
        byte* region = GCHeap.FirstRegion;

        while (region != null)
        {
            byte* current = region + 8;

            byte* regionEnd;
            if (region == GCHeap.FirstRegion)
            {
                regionEnd = GCHeap.AllocPtr;
            }
            else
            {
                regionEnd = region + regionSize;
            }

            while (current + GCHeap.ObjectHeaderSize < regionEnd)
            {
                void* obj = current + GCHeap.ObjectHeaderSize;
                uint blockSize = GCHeap.GetBlockSize(obj);
                if (blockSize == 0)
                    break;

                bool isFree = GCHeap.IsFreeBlock(obj);
                bool isMarked = !isFree && GCHeap.IsMarked(obj);

                if (isMarked)
                {
                    // Live object - update its reference fields
                    MethodTable* mt = *(MethodTable**)obj;
                    if (mt != null && mt->HasPointers)
                    {
                        GCDescHelper.EnumerateReferences(obj, mt, &UpdateReferenceCallback);
                    }
                }

                current += blockSize;
            }

            region = *(byte**)region;
        }
    }

    /// <summary>
    /// Pass 3: Move objects to their new locations.
    /// </summary>
    private static void MoveObjects()
    {
        ulong regionSize = GCHeap.RegionSize;
        byte* region = GCHeap.FirstRegion;
        byte* lastMovedEnd = null;

        while (region != null)
        {
            byte* current = region + 8;

            byte* regionEnd;
            if (region == GCHeap.FirstRegion)
            {
                regionEnd = GCHeap.AllocPtr;
            }
            else
            {
                regionEnd = region + regionSize;
            }

            // Track the start of this region for allocation pointer reset
            if (lastMovedEnd == null)
            {
                lastMovedEnd = current;
            }

            while (current + GCHeap.ObjectHeaderSize < regionEnd)
            {
                void* obj = current + GCHeap.ObjectHeaderSize;
                uint blockSize = GCHeap.GetBlockSize(obj);
                if (blockSize == 0)
                    break;

                bool hasForwarding = GCHeap.HasForwardingPointer(obj);

                if (hasForwarding)
                {
                    void* newAddr = GCHeap.GetForwardingAddress(obj);

                    if (newAddr != obj)
                    {
                        // Move the entire block (headers + object)
                        byte* srcBlock = (byte*)obj - GCHeap.ObjectHeaderSize;
                        byte* dstBlock = (byte*)newAddr - GCHeap.ObjectHeaderSize;

                        // Use memmove for potentially overlapping regions
                        Memmove(dstBlock, srcBlock, blockSize);

                        _objectsMoved++;
                        _bytesMoved += blockSize;
                    }

                    // Clear forwarding pointer on the new location
                    GCHeap.ClearForwardingPointer(newAddr);

                    // Clear mark bit for next GC cycle
                    GCHeap.ClearMark(newAddr);

                    // Track end of moved data
                    lastMovedEnd = (byte*)newAddr - GCHeap.ObjectHeaderSize + blockSize;
                }
                else
                {
                    // Object didn't move (pinned or already in place)
                    bool isFree = GCHeap.IsFreeBlock(obj);
                    bool isMarked = !isFree && GCHeap.IsMarked(obj);

                    if (isMarked)
                    {
                        // Clear mark bit
                        GCHeap.ClearMark(obj);
                        lastMovedEnd = current + blockSize;
                    }
                }

                current += blockSize;
            }

            region = *(byte**)region;
        }

        // Reset allocation pointer to end of compacted data
        if (lastMovedEnd != null)
        {
            GCHeap.SetAllocPtr(lastMovedEnd);
        }

        // Clear the free list since compaction eliminates fragmentation
        GCHeap.ClearFreeList();
    }

    /// <summary>
    /// Memory move that handles overlapping regions.
    /// </summary>
    private static void Memmove(byte* dst, byte* src, uint size)
    {
        if (dst == src || size == 0)
            return;

        if (dst < src)
        {
            // Copy forward
            for (uint i = 0; i < size; i++)
                dst[i] = src[i];
        }
        else
        {
            // Copy backward (overlapping, dst > src)
            for (uint i = size; i > 0; i--)
                dst[i - 1] = src[i - 1];
        }
    }

    /// <summary>
    /// Get compaction statistics from the last compaction.
    /// </summary>
    public static void GetStats(out ulong objectsMoved, out ulong bytesMoved, out ulong objectsSkipped)
    {
        objectsMoved = _objectsMoved;
        bytesMoved = _bytesMoved;
        objectsSkipped = _objectsSkipped;
    }

    [System.Runtime.InteropServices.DllImport("*", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
    private static extern void capture_context(ExceptionContext* context);
}
