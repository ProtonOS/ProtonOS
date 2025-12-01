// ProtonOS kernel - x64 Stack Root Enumeration
// Walks the stack and enumerates all live GC references using GCInfo.
//
// For each stack frame:
//   1. Look up RUNTIME_FUNCTION to find the function
//   2. Get UNWIND_INFO and from that, GCInfo
//   3. Calculate code offset within the function
//   4. Find the safe point for that offset
//   5. For each live slot at that safe point, compute the address
//   6. Report the GC reference to the callback
//
// This is used by the GC during mark phase to find all stack roots.

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Runtime;
using ProtonOS.Memory;

namespace ProtonOS.X64;

/// <summary>
/// Enumerates GC roots from the stack using GCInfo.
/// x64-specific implementation.
/// </summary>
public static unsafe class StackRoots
{
    /// <summary>
    /// Enumerate all stack roots for a given thread context.
    /// </summary>
    /// <param name="context">The exception context (register state) to start from.</param>
    /// <param name="callback">Called for each live GC reference found.</param>
    /// <param name="callbackContext">User context passed to callback.</param>
    /// <returns>Total number of roots found.</returns>
    public static int EnumerateStackRoots(ExceptionContext* context, delegate*<void**, void*, void> callback, void* callbackContext)
    {
        if (context == null || callback == null)
            return 0;

        int totalRoots = 0;
        int totalFrames = 0;
        int framesWithSlots = 0;
        int liveSlotsChecked = 0;
        int slotsInHeap = 0;
        ExceptionContext walkContext = *context;
        int maxFrames = 100; // Safety limit

        for (int frameIndex = 0; frameIndex < maxFrames && walkContext.Rip != 0; frameIndex++)
        {
            // Find the function for this RIP
            ulong imageBase;
            var funcEntry = ExceptionHandling.LookupFunctionEntry(walkContext.Rip, out imageBase);

            if (funcEntry != null)
            {
                // Calculate code offset within function
                uint codeOffset = (uint)(walkContext.Rip - (imageBase + funcEntry->BeginAddress));

                // Get GCInfo for this function
                var unwindInfo = (UnwindInfo*)(imageBase + funcEntry->UnwindInfoAddress);
                byte* gcInfo = GCInfoHelper.GetGCInfo(imageBase, unwindInfo);

                // Debug: show frame info (first few frames only)
                // Disabled verbose output - too much noise
                // if (frameIndex < 5) { ... }

                if (gcInfo != null)
                {
                    totalFrames++;

                    // Compute CallerSP by virtually unwinding a copy of the context
                    // CallerSP = the RSP value after unwinding this frame completely
                    ExceptionContext unwoundContext = walkContext;
                    ulong callerSP;
                    if (ExceptionHandling.VirtualUnwind(&unwoundContext, null))
                    {
                        callerSP = unwoundContext.Rsp;
                    }
                    else
                    {
                        // Can't unwind - use RSP + 8 as fallback (return address)
                        callerSP = walkContext.Rsp + 8;
                    }

                    // Enumerate live slots at this code offset
                    int frameRoots = EnumerateFrameRoots(
                        gcInfo,
                        codeOffset,
                        &walkContext,
                        callerSP,
                        callback,
                        callbackContext,
                        false, // Disable verbose debug
                        out int numSlots,
                        out int numLive,
                        out int numHeap);

                    if (numSlots > 0) framesWithSlots++;
                    liveSlotsChecked += numLive;
                    slotsInHeap += numHeap;

                    // Print when we find roots
                    if (frameRoots > 0)
                    {
                        DebugConsole.Write("  [StackRoot] Frame ");
                        DebugConsole.WriteDecimal((uint)frameIndex);
                        DebugConsole.Write(" offset=");
                        DebugConsole.WriteDecimal(codeOffset);
                        DebugConsole.Write(": ");
                        DebugConsole.WriteDecimal((uint)frameRoots);
                        DebugConsole.WriteLine(" root(s)");
                    }
                    totalRoots += frameRoots;
                }

                // Unwind to next frame
                if (!ExceptionHandling.VirtualUnwind(&walkContext, null))
                    break;
            }
            else
            {
                // No unwind info - assume leaf function, just pop return address
                if (walkContext.Rsp == 0)
                    break;

                walkContext.Rip = *(ulong*)walkContext.Rsp;
                walkContext.Rsp += 8;

                if (walkContext.Rip == 0)
                    break;
            }
        }

        // Print summary for current thread
        DebugConsole.Write("  [Stack] ");
        DebugConsole.WriteDecimal((uint)totalFrames);
        DebugConsole.Write(" frames, ");
        DebugConsole.WriteDecimal((uint)framesWithSlots);
        DebugConsole.Write(" with slots, ");
        DebugConsole.WriteDecimal((uint)liveSlotsChecked);
        DebugConsole.Write(" live, ");
        DebugConsole.WriteDecimal((uint)slotsInHeap);
        DebugConsole.WriteLine(" in heap");

        return totalRoots;
    }

    /// <summary>
    /// Enumerate GC roots in a single stack frame.
    /// </summary>
    /// <param name="callerSP">The stack pointer of the caller (RSP after unwinding this frame)</param>
    private static int EnumerateFrameRoots(
        byte* gcInfo,
        uint codeOffset,
        ExceptionContext* context,
        ulong callerSP,
        delegate*<void**, void*, void> callback,
        void* callbackContext,
        bool debug,
        out int numSlots,
        out int numLive,
        out int numHeap)
    {
        numSlots = 0;
        numLive = 0;
        numHeap = 0;

        var decoder = new GCInfoDecoder(gcInfo);

        if (!decoder.DecodeHeader(debug))
            return 0;

        if (!decoder.DecodeSlotTable())
            return 0;

        numSlots = (int)decoder.NumTrackedSlots;

        // If no tracked slots, nothing to report
        if (decoder.NumTrackedSlots == 0)
            return 0;

        if (!decoder.DecodeSlotDefinitionsAndSafePoints(debug))
            return 0;

        // Find safe point for this code offset
        uint safePointIndex = decoder.FindSafePointIndex(codeOffset);

        if (safePointIndex >= decoder.NumSafePoints)
            return 0; // Not at a safe point

        int rootCount = 0;

        // Enumerate all tracked slots
        for (uint slotIndex = 0; slotIndex < decoder.NumTrackedSlots; slotIndex++)
        {
            bool isLive = decoder.IsSlotLiveAtSafePoint(safePointIndex, slotIndex);

            if (!isLive)
                continue;

            numLive++;
            GCSlot slot = decoder.GetSlot(slotIndex);

            // Compute the address of this slot
            void** slotAddress = GetSlotAddress(&slot, context, callerSP, decoder.StackBaseRegister);

            if (slotAddress != null)
            {
                // Only report if it actually points to something in the GC heap
                void* objRef = *slotAddress;
                if (objRef != null && GCHeap.IsInHeap(objRef))
                {
                    numHeap++;
                    callback(slotAddress, callbackContext);
                    rootCount++;
                }
                else if (objRef != null)
                {
                    // Debug: print live slot not in heap with slot details
                    DebugConsole.Write("    [NotInHeap] slot=");
                    DebugConsole.WriteDecimal(slotIndex);
                    if (slot.IsRegister)
                    {
                        DebugConsole.Write(" REG=");
                        DebugConsole.WriteDecimal(slot.RegisterNumber);
                    }
                    else
                    {
                        DebugConsole.Write(" STK base=");
                        DebugConsole.WriteDecimal((uint)slot.StackBase);
                        DebugConsole.Write(" off=");
                        // Print signed offset
                        if (slot.StackOffset < 0)
                        {
                            DebugConsole.Write("-");
                            DebugConsole.WriteDecimal((uint)(-slot.StackOffset));
                        }
                        else
                        {
                            DebugConsole.WriteDecimal((uint)slot.StackOffset);
                        }
                    }
                    DebugConsole.Write(" addr=0x");
                    DebugConsole.WriteHex((ulong)slotAddress);
                    DebugConsole.Write(" val=0x");
                    DebugConsole.WriteHex((ulong)objRef);
                    DebugConsole.WriteLine();
                }
            }
        }

        return rootCount;
    }

    /// <summary>
    /// Compute the address of a GC slot given the current context.
    /// </summary>
    /// <param name="callerSP">The stack pointer of the caller (RSP after unwinding this frame)</param>
    private static void** GetSlotAddress(GCSlot* slot, ExceptionContext* context, ulong callerSP, int stackBaseRegister)
    {
        if (slot->IsRegister)
        {
            // Register slot - return pointer to the register in the context
            return GetRegisterAddress(context, slot->RegisterNumber);
        }
        else
        {
            // Stack slot - compute address based on base and offset
            ulong baseAddr;
            switch (slot->StackBase)
            {
                case GCSlotBase.Stack:
                    // Relative to RSP
                    baseAddr = context->Rsp;
                    break;
                case GCSlotBase.CallerSP:
                    // Relative to caller's RSP (the SP value after unwinding this frame)
                    // CallerSP is the value of RSP in the caller before it made the call
                    baseAddr = callerSP;
                    break;
                case GCSlotBase.FramePointer:
                    // Relative to frame pointer (RBP or other)
                    if (stackBaseRegister >= 0)
                    {
                        var regPtr = GetRegisterAddress(context, (byte)stackBaseRegister);
                        baseAddr = regPtr != null ? *(ulong*)regPtr : context->Rbp;
                    }
                    else
                    {
                        baseAddr = context->Rbp;
                    }
                    break;
                default:
                    return null;
            }

            // Stack offsets are signed and already denormalized to bytes in GCInfo decoder
            // Important: We need signed arithmetic here since offsets can be negative (above RSP)
            long signedOffset = slot->StackOffset;
            return (void**)((long)baseAddr + signedOffset);
        }
    }

    /// <summary>
    /// Get a pointer to a register value in the context.
    /// x64 register numbers from unwind codes:
    /// 0=RAX, 1=RCX, 2=RDX, 3=RBX, 4=RSP, 5=RBP, 6=RSI, 7=RDI
    /// 8=R8, 9=R9, 10=R10, 11=R11, 12=R12, 13=R13, 14=R14, 15=R15
    /// </summary>
    private static void** GetRegisterAddress(ExceptionContext* context, byte regNumber)
    {
        return regNumber switch
        {
            0 => (void**)&context->Rax,
            1 => (void**)&context->Rcx,
            2 => (void**)&context->Rdx,
            3 => (void**)&context->Rbx,
            4 => (void**)&context->Rsp,
            5 => (void**)&context->Rbp,
            6 => (void**)&context->Rsi,
            7 => (void**)&context->Rdi,
            8 => (void**)&context->R8,
            9 => (void**)&context->R9,
            10 => (void**)&context->R10,
            11 => (void**)&context->R11,
            12 => (void**)&context->R12,
            13 => (void**)&context->R13,
            14 => (void**)&context->R14,
            15 => (void**)&context->R15,
            _ => null
        };
    }

    /// <summary>
    /// Test and dump stack roots for the current thread.
    /// </summary>
    public static void DumpStackRoots()
    {
        DebugConsole.WriteLine("[StackRoots] Enumerating stack roots...");

        // Call helper that has a managed object on the stack
        DumpStackRootsWithObject();
    }

    /// <summary>
    /// Helper that allocates a managed object and dumps stack roots while it's live.
    /// The object local variable should be tracked as a GC root by the compiler.
    /// </summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void DumpStackRootsWithObject()
    {
        // Allocate a real managed object - this will be tracked by GCInfo
        object testObj = new object();

        // Show the object address
        nint objAddr = System.Runtime.CompilerServices.Unsafe.As<object, nint>(ref testObj);
        DebugConsole.Write("[StackRoots] Test object on stack at 0x");
        DebugConsole.WriteHex((ulong)objAddr);
        DebugConsole.WriteLine();

        // Capture current context using native helper
        ExceptionContext context = default;
        CaptureContext(&context);

        int count = 0;

        // Use a simple callback that just prints and counts
        delegate*<void**, void*, void> callback = &DumpRootCallback;
        count = EnumerateStackRoots(&context, callback, &count);

        DebugConsole.Write("[StackRoots] Found ");
        DebugConsole.WriteDecimal((uint)count);
        DebugConsole.WriteLine(" stack roots");

        // Use testObj after enumeration to ensure it's not optimized away
        // GC.KeepAlive equivalent
        if (testObj != null)
        {
            DebugConsole.Write("[StackRoots] Test object still live at 0x");
            DebugConsole.WriteHex((ulong)objAddr);
            DebugConsole.WriteLine();
        }
    }

    /// <summary>
    /// Callback for DumpStackRoots that prints each root.
    /// </summary>
    private static void DumpRootCallback(void** objRefLocation, void* context)
    {
        void* objRef = *objRefLocation;
        DebugConsole.Write("  Root at 0x");
        DebugConsole.WriteHex((ulong)objRefLocation);
        DebugConsole.Write(" -> obj=0x");
        DebugConsole.WriteHex((ulong)objRef);

        // Try to get the MethodTable
        if (objRef != null)
        {
            MethodTable* mt = *(MethodTable**)objRef;
            DebugConsole.Write(" MT=0x");
            DebugConsole.WriteHex((ulong)mt);
        }

        DebugConsole.WriteLine();
    }

    /// <summary>
    /// Capture the current thread's context.
    /// Uses native assembly to get accurate register values.
    /// </summary>
    private static void CaptureContext(ExceptionContext* context)
    {
        // Use native assembly to capture registers
        // The native function fills in the context struct
        capture_context(context);
    }

    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void capture_context(ExceptionContext* context);
}
