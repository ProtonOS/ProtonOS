// ProtonOS kernel - Garbage Collector
// Mark phase implementation with stop-the-world multi-thread root enumeration.
//
// Mark Phase Algorithm:
//   1. Stop all threads (stop-the-world)
//   2. Clear all mark bits (prepare for new GC cycle)
//   3. Enumerate roots:
//      - Static roots from GCStaticRegion
//      - Stack roots from all threads
//   4. Mark each root object and push to work queue
//   5. Process work queue: for each object, mark and traverse references via GCDesc
//   6. Resume all threads
//
// Design decisions:
//   - Mark bit in object header (bit 0) - already implemented in GCHeap
//   - Iterative marking with explicit work queue to avoid stack overflow
//   - Single-threaded marking (stop-the-world, no concurrent marking)

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Threading;
using ProtonOS.Runtime;
using ProtonOS.X64;

namespace ProtonOS.Memory;

/// <summary>
/// Garbage collector for the managed heap.
/// Currently implements mark-sweep with stop-the-world collection.
/// </summary>
public static unsafe class GC
{
    // Mark stack (work queue) for iterative marking
    // Fixed-size array to avoid heap allocation during GC
    private const int MarkStackCapacity = 4096;
    private static void** _markStack;
    private static int _markStackTop;

    // GC statistics
    private static ulong _collectionsPerformed;
    private static ulong _objectsMarked;
    private static ulong _rootsFound;

    // State
    private static bool _initialized;
    private static bool _gcInProgress;

    /// <summary>
    /// Initialize the garbage collector.
    /// Must be called after GCHeap and PageAllocator are initialized.
    /// </summary>
    public static bool Init()
    {
        if (_initialized)
            return true;

        // Allocate mark stack from page allocator (avoid using GCHeap during GC)
        ulong stackSizeBytes = (ulong)(MarkStackCapacity * sizeof(void*));
        ulong pages = (stackSizeBytes + PageAllocator.PageSize - 1) / PageAllocator.PageSize;
        ulong physAddr = PageAllocator.AllocatePages(pages);

        if (physAddr == 0)
        {
            DebugConsole.WriteLine("[GC] Failed to allocate mark stack!");
            return false;
        }

        _markStack = (void**)VirtualMemory.PhysToVirt(physAddr);
        _markStackTop = 0;

        _collectionsPerformed = 0;
        _objectsMarked = 0;
        _rootsFound = 0;
        _gcInProgress = false;

        _initialized = true;

        DebugConsole.Write("[GC] Initialized, mark stack at 0x");
        DebugConsole.WriteHex((ulong)_markStack);
        DebugConsole.Write(" (capacity=");
        DebugConsole.WriteDecimal(MarkStackCapacity);
        DebugConsole.WriteLine(")");

        return true;
    }

    /// <summary>
    /// Perform a full garbage collection.
    /// </summary>
    /// <returns>Number of objects marked as reachable.</returns>
    public static int Collect()
    {
        if (!_initialized || !GCHeap.IsInitialized)
        {
            DebugConsole.WriteLine("[GC] Not initialized!");
            return -1;
        }

        if (_gcInProgress)
        {
            DebugConsole.WriteLine("[GC] Collection already in progress!");
            return -1;
        }

        _gcInProgress = true;
        _collectionsPerformed++;

        DebugConsole.Write("[GC] Starting collection #");
        DebugConsole.WriteDecimal((uint)_collectionsPerformed);
        DebugConsole.WriteLine("...");

        // Phase 1: Stop the world
        StopTheWorld();

        // Phase 2: Clear all mark bits
        ClearAllMarks();

        // Phase 3: Mark phase
        _markStackTop = 0;
        _objectsMarked = 0;
        _rootsFound = 0;

        // Enumerate and mark all roots
        MarkRoots();

        // Process the mark stack (transitive closure)
        ProcessMarkStack();

        DebugConsole.Write("[GC] Mark phase complete: ");
        DebugConsole.WriteDecimal((uint)_rootsFound);
        DebugConsole.Write(" roots, ");
        DebugConsole.WriteDecimal((uint)_objectsMarked);
        DebugConsole.WriteLine(" objects marked");

        // Phase 4: Sweep phase - free unmarked objects
        int freedCount = Sweep();

        // Phase 5: Resume the world
        ResumeTheWorld();

        _gcInProgress = false;

        return (int)_objectsMarked;
    }

    /// <summary>
    /// Stop all threads except the current one.
    /// </summary>
    private static void StopTheWorld()
    {
        if (!Scheduler.IsInitialized)
            return;

        DebugConsole.WriteLine("[GC] Stopping the world...");

        // Disable scheduling to prevent new context switches
        Scheduler.DisableScheduling();

        // Suspend all threads except the current one
        ref var schedLock = ref Scheduler.SchedulerLock;
        schedLock.Acquire();

        var currentThread = Scheduler.CurrentThread;
        for (var thread = Scheduler.AllThreadsHead; thread != null; thread = thread->NextAll)
        {
            if (thread != currentThread && thread->State != ThreadState.Terminated)
            {
                // Note: Thread is already not running since scheduling is disabled
                // and we hold the scheduler lock. Just mark as suspended.
                if (thread->State == ThreadState.Ready)
                {
                    thread->State = ThreadState.Suspended;
                    thread->SuspendCount++;
                }
            }
        }

        schedLock.Release();
    }

    /// <summary>
    /// Resume all suspended threads.
    /// </summary>
    private static void ResumeTheWorld()
    {
        if (!Scheduler.IsInitialized)
            return;

        DebugConsole.WriteLine("[GC] Resuming the world...");

        ref var schedLock = ref Scheduler.SchedulerLock;
        schedLock.Acquire();

        var currentThread = Scheduler.CurrentThread;
        for (var thread = Scheduler.AllThreadsHead; thread != null; thread = thread->NextAll)
        {
            if (thread != currentThread && thread->State == ThreadState.Suspended)
            {
                // Only resume threads we suspended for GC (SuspendCount == 1)
                // Don't resume threads that were already suspended before GC
                if (thread->SuspendCount == 1)
                {
                    thread->SuspendCount = 0;
                    thread->State = ThreadState.Ready;
                }
            }
        }

        schedLock.Release();

        // Re-enable scheduling
        Scheduler.EnableScheduling();
    }

    // Debug flag for verbose GC tracing
    private static bool _traceGC = false;
    public static void SetTraceGC(bool trace) { _traceGC = trace; }

    /// <summary>
    /// Clear all mark bits in preparation for a new GC cycle.
    /// Walks all heap regions and clears mark bits on all live objects.
    /// Uses block size from header to step through blocks (handles free blocks correctly).
    /// </summary>
    private static void ClearAllMarks()
    {
        ulong regionSize = GCHeap.RegionSize;
        byte* region = GCHeap.FirstRegion;
        int regionCount = 0;
        int objectCount = 0;
        int freeCount = 0;

        while (region != null)
        {
            regionCount++;
            byte* current = region + 8; // Skip region header (next pointer)

            // For the current (active) region, only scan up to AllocPtr
            byte* regionEnd;
            if (region == GCHeap.FirstRegion)
            {
                regionEnd = GCHeap.AllocPtr;
            }
            else
            {
                regionEnd = region + regionSize;
            }

            if (_traceGC)
            {
                DebugConsole.Write("[GC] Walk region ");
                DebugConsole.WriteHex((ulong)current);
                DebugConsole.Write(" to ");
                DebugConsole.WriteHex((ulong)regionEnd);
                DebugConsole.WriteLine();
            }

            // Walk blocks in this region using block size header
            while (current + GCHeap.ObjectHeaderSize < regionEnd)
            {
                // Object reference is after the headers
                void* obj = current + GCHeap.ObjectHeaderSize;

                // Get block size from block size header
                uint blockSize = GCHeap.GetBlockSize(obj);
                if (blockSize == 0)
                {
                    if (_traceGC)
                    {
                        DebugConsole.Write("[GC]   Block at ");
                        DebugConsole.WriteHex((ulong)current);
                        DebugConsole.WriteLine(" has size=0, stopping");
                    }
                    break; // End of allocated blocks
                }

                bool isFree = GCHeap.IsFreeBlock(obj);

                if (_traceGC)
                {
                    DebugConsole.Write("[GC]   Block ");
                    DebugConsole.WriteHex((ulong)current);
                    DebugConsole.Write(" size=");
                    DebugConsole.WriteDecimal(blockSize);
                    DebugConsole.Write(isFree ? " FREE" : " LIVE");
                    DebugConsole.WriteLine();
                }

                // Check if this is a free block (skip it, don't clear marks)
                if (!isFree)
                {
                    // Live object - clear the mark bit
                    GCHeap.ClearMark(obj);
                    objectCount++;
                }
                else
                {
                    freeCount++;
                }

                // Move to next block using block size
                current += blockSize;
            }

            // Move to next region
            region = *(byte**)region;
        }

        DebugConsole.Write("[GC] Cleared marks on ");
        DebugConsole.WriteDecimal((uint)objectCount);
        DebugConsole.Write(" objects (");
        DebugConsole.WriteDecimal((uint)freeCount);
        DebugConsole.Write(" free) in ");
        DebugConsole.WriteDecimal((uint)regionCount);
        DebugConsole.WriteLine(" region(s)");
    }

    /// <summary>
    /// Sweep phase: walk the heap and free unmarked objects.
    /// Builds a new free list from all garbage objects.
    /// Uses block size from header to step through blocks.
    /// </summary>
    /// <returns>Number of objects freed.</returns>
    private static int Sweep()
    {
        // Clear existing free list - we'll rebuild it
        GCHeap.ClearFreeList();

        ulong regionSize = GCHeap.RegionSize;
        byte* region = GCHeap.FirstRegion;
        int freedCount = 0;
        ulong freedBytes = 0;

        while (region != null)
        {
            byte* current = region + 8; // Skip region header (next pointer)

            // For the current (active) region, only scan up to AllocPtr
            byte* regionEnd;
            if (region == GCHeap.FirstRegion)
            {
                regionEnd = GCHeap.AllocPtr;
            }
            else
            {
                regionEnd = region + regionSize;
            }

            // Walk blocks in this region using block size header
            while (current + GCHeap.ObjectHeaderSize < regionEnd)
            {
                void* obj = current + GCHeap.ObjectHeaderSize;

                // Get block size from block size header
                uint blockSize = GCHeap.GetBlockSize(obj);
                if (blockSize == 0)
                    break; // End of allocated blocks

                // Skip blocks that are already free (from previous cycle or never used)
                if (GCHeap.IsFreeBlock(obj))
                {
                    // Already free - just add back to free list
                    GCHeap.AddToFreeList(obj);
                    freedBytes += blockSize;
                    // Don't increment freedCount - this was already free
                }
                else if (GCHeap.IsMarked(obj))
                {
                    // Live object - clear mark for next cycle
                    GCHeap.ClearMark(obj);
                }
                else
                {
                    // Garbage - add to free list
                    GCHeap.AddToFreeList(obj);
                    freedCount++;
                    freedBytes += blockSize;
                }

                // Move to next block using block size
                current += blockSize;
            }

            // Move to next region
            region = *(byte**)region;
        }

        DebugConsole.Write("[GC] Sweep complete: freed ");
        DebugConsole.WriteDecimal((uint)freedCount);
        DebugConsole.Write(" objects (");
        DebugConsole.WriteDecimal((uint)(freedBytes / 1024));
        DebugConsole.WriteLine(" KB)");

        return freedCount;
    }

    /// <summary>
    /// Enumerate and mark all roots (static + stack).
    /// </summary>
    private static void MarkRoots()
    {
        // Mark static roots
        MarkStaticRoots();

        // Mark stack roots from all threads
        MarkStackRootsAllThreads();
    }

    /// <summary>
    /// Mark all static roots.
    /// </summary>
    private static void MarkStaticRoots()
    {
        int staticCount = 0;

        StaticRoots.EnumerateStaticRoots(&StaticRootCallback);

        // Note: staticCount is updated by callback, but we can't easily track it
        // with the current callback signature. We'll rely on _rootsFound counter.
    }

    /// <summary>
    /// Callback for static root enumeration.
    /// </summary>
    private static void StaticRootCallback(void** objRefSlot)
    {
        void* obj = *objRefSlot;
        if (obj != null && GCHeap.IsInHeap(obj))
        {
            MarkAndPush(obj);
            _rootsFound++;
        }
    }

    /// <summary>
    /// Mark stack roots from all threads.
    /// </summary>
    private static void MarkStackRootsAllThreads()
    {
        if (!Scheduler.IsInitialized)
        {
            // No scheduler - just mark current stack
            MarkCurrentThreadStackRoots();
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
                // Current thread - need to capture context now
                schedLock.Release();
                MarkCurrentThreadStackRoots();
                schedLock.Acquire();
            }
            else
            {
                // Other thread - use saved context from scheduler
                MarkThreadStackRoots(thread);
            }
        }

        schedLock.Release();
    }

    /// <summary>
    /// Mark stack roots for the current thread.
    /// </summary>
    private static void MarkCurrentThreadStackRoots()
    {
        // Capture current context
        ExceptionContext context = default;
        capture_context(&context);

        DebugConsole.Write("[GC] Walking stack from RIP=0x");
        DebugConsole.WriteHex(context.Rip);
        DebugConsole.Write(" RSP=0x");
        DebugConsole.WriteHex(context.Rsp);
        DebugConsole.WriteLine();

        int count = StackRoots.EnumerateStackRoots(&context, &StackRootCallback, null);

        DebugConsole.Write("[GC] Current thread: ");
        DebugConsole.WriteDecimal((uint)count);
        DebugConsole.WriteLine(" stack roots");
    }

    /// <summary>
    /// Mark stack roots for a specific thread using its saved context.
    /// </summary>
    private static void MarkThreadStackRoots(Thread* thread)
    {
        // Convert CPUContext to ExceptionContext
        // The layouts are similar but not identical
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

        int count = StackRoots.EnumerateStackRoots(&context, &StackRootCallback, null);

        DebugConsole.Write("[GC] Thread ");
        DebugConsole.WriteDecimal(thread->Id);
        DebugConsole.Write(": ");
        DebugConsole.WriteDecimal((uint)count);
        DebugConsole.WriteLine(" stack roots");
    }

    /// <summary>
    /// Callback for stack root enumeration.
    /// </summary>
    private static void StackRootCallback(void** objRefSlot, void* callbackContext)
    {
        void* obj = *objRefSlot;
        if (obj != null && GCHeap.IsInHeap(obj))
        {
            MarkAndPush(obj);
            _rootsFound++;
        }
    }

    /// <summary>
    /// Mark an object and push it to the work queue if not already marked.
    /// </summary>
    private static void MarkAndPush(void* obj)
    {
        if (obj == null)
            return;

        // Check if already marked
        if (GCHeap.IsMarked(obj))
            return;

        // Mark it
        GCHeap.SetMark(obj);
        _objectsMarked++;

        // Push to mark stack for processing
        if (_markStackTop < MarkStackCapacity)
        {
            _markStack[_markStackTop++] = obj;
        }
        else
        {
            // Mark stack overflow - this is a problem
            // In production, we'd need to handle this (grow stack, use bitmap, etc.)
            DebugConsole.WriteLine("[GC] WARNING: Mark stack overflow!");
        }
    }

    /// <summary>
    /// Process the mark stack until empty.
    /// For each object, traverse its reference fields and mark reachable objects.
    /// </summary>
    private static void ProcessMarkStack()
    {
        while (_markStackTop > 0)
        {
            // Pop an object
            void* obj = _markStack[--_markStackTop];

            // Get its MethodTable
            MethodTable* mt = *(MethodTable**)obj;
            if (mt == null)
                continue;

            // If the type has pointers, enumerate and mark them
            if (mt->HasPointers)
            {
                GCDescHelper.EnumerateReferences(obj, mt, &ReferenceCallback);
            }
        }
    }

    /// <summary>
    /// Callback for GCDesc reference enumeration.
    /// </summary>
    private static void ReferenceCallback(void** refSlot)
    {
        void* obj = *refSlot;
        if (obj != null && GCHeap.IsInHeap(obj))
        {
            MarkAndPush(obj);
        }
    }

    /// <summary>
    /// Check if an object is marked (reachable).
    /// </summary>
    public static bool IsObjectMarked(void* obj)
    {
        if (obj == null || !GCHeap.IsInHeap(obj))
            return false;
        return GCHeap.IsMarked(obj);
    }

    /// <summary>
    /// Get GC statistics.
    /// </summary>
    public static void GetStats(out ulong collections, out ulong lastMarked, out ulong lastRoots)
    {
        collections = _collectionsPerformed;
        lastMarked = _objectsMarked;
        lastRoots = _rootsFound;
    }

    /// <summary>
    /// Check if GC is initialized.
    /// </summary>
    public static bool IsInitialized => _initialized;

    /// <summary>
    /// Check if a GC is currently in progress.
    /// </summary>
    public static bool IsCollecting => _gcInProgress;

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void capture_context(ExceptionContext* context);
}
