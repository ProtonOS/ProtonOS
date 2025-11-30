// netos kernel - x64 Stack Root Enumeration
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
using Kernel.Platform;
using Kernel.Runtime;
using Kernel.Memory;

namespace Kernel.X64;

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
                if (frameIndex < 5)
                {
                    DebugConsole.Write("  Frame ");
                    DebugConsole.WriteDecimal((uint)frameIndex);
                    DebugConsole.Write(": RIP=0x");
                    DebugConsole.WriteHex(walkContext.Rip);
                    DebugConsole.Write(" offset=");
                    DebugConsole.WriteDecimal(codeOffset);
                    DebugConsole.Write(" gcInfo=");
                    DebugConsole.WriteHex((ulong)gcInfo);
                    DebugConsole.Write(" uwFlags=0x");
                    DebugConsole.WriteHex(unwindInfo->Flags);
                    DebugConsole.Write(" uwCodes=");
                    DebugConsole.WriteDecimal(unwindInfo->CountOfUnwindCodes);
                    DebugConsole.WriteLine();
                }

                if (gcInfo != null)
                {
                    // Enumerate live slots at this code offset
                    int frameRoots = EnumerateFrameRoots(
                        gcInfo,
                        codeOffset,
                        &walkContext,
                        callback,
                        callbackContext,
                        frameIndex < 5); // Pass debug flag for first few frames
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

        return totalRoots;
    }

    /// <summary>
    /// Enumerate GC roots in a single stack frame.
    /// </summary>
    private static int EnumerateFrameRoots(
        byte* gcInfo,
        uint codeOffset,
        ExceptionContext* context,
        delegate*<void**, void*, void> callback,
        void* callbackContext,
        bool debug = false)
    {
        var decoder = new GCInfoDecoder(gcInfo);

        if (!decoder.DecodeHeader(debug))
        {
            if (debug) DebugConsole.WriteLine("    -> DecodeHeader failed");
            return 0;
        }

        if (!decoder.DecodeSlotTable())
        {
            if (debug) DebugConsole.WriteLine("    -> DecodeSlotTable failed");
            return 0;
        }

        if (debug)
        {
            DebugConsole.Write("    -> regs=");
            DebugConsole.WriteDecimal(decoder.NumRegisters);
            DebugConsole.Write(" stk=");
            DebugConsole.WriteDecimal(decoder.NumStackSlots);
            DebugConsole.Write(" safePoints=");
            DebugConsole.WriteDecimal(decoder.NumSafePoints);
            DebugConsole.WriteLine();
        }

        // If no tracked slots, nothing to report
        if (decoder.NumTrackedSlots == 0)
            return 0;

        if (!decoder.DecodeSlotDefinitionsAndSafePoints(debug))
        {
            if (debug) DebugConsole.WriteLine("    -> DecodeSlotDefinitionsAndSafePoints failed");
            return 0;
        }

        // Find safe point for this code offset
        uint safePointIndex = decoder.FindSafePointIndex(codeOffset);
        if (debug)
        {
            DebugConsole.Write("    -> safePointIndex=");
            DebugConsole.WriteDecimal(safePointIndex);
            DebugConsole.WriteLine();
        }

        if (safePointIndex >= decoder.NumSafePoints)
            return 0; // Not at a safe point

        int rootCount = 0;

        // Enumerate all tracked slots
        for (uint slotIndex = 0; slotIndex < decoder.NumTrackedSlots; slotIndex++)
        {
            bool isLive = decoder.IsSlotLiveAtSafePoint(safePointIndex, slotIndex);

            if (debug)
            {
                GCSlot slotInfo = decoder.GetSlot(slotIndex);
                DebugConsole.Write("    -> slot ");
                DebugConsole.WriteDecimal(slotIndex);
                DebugConsole.Write(" live=");
                DebugConsole.Write(isLive ? "Y" : "N");
                DebugConsole.Write(" isReg=");
                DebugConsole.Write(slotInfo.IsRegister ? "Y" : "N");
                if (slotInfo.IsRegister)
                {
                    DebugConsole.Write(" reg=");
                    DebugConsole.WriteDecimal(slotInfo.RegisterNumber);
                }
                else
                {
                    DebugConsole.Write(" off=");
                    DebugConsole.WriteDecimal((uint)slotInfo.StackOffset);
                }
                DebugConsole.WriteLine();
            }

            if (!isLive)
                continue;

            GCSlot slot = decoder.GetSlot(slotIndex);

            // Compute the address of this slot
            void** slotAddress = GetSlotAddress(&slot, context, decoder.StackBaseRegister);

            if (slotAddress != null)
            {
                // Only report if it actually points to something in the GC heap
                void* objRef = *slotAddress;
                if (debug)
                {
                    DebugConsole.Write("      addr=0x");
                    DebugConsole.WriteHex((ulong)slotAddress);
                    DebugConsole.Write(" val=0x");
                    DebugConsole.WriteHex((ulong)objRef);
                    DebugConsole.Write(" inHeap=");
                    DebugConsole.Write(objRef != null && GCHeap.IsInHeap(objRef) ? "Y" : "N");
                    DebugConsole.WriteLine();
                }
                if (objRef != null && GCHeap.IsInHeap(objRef))
                {
                    callback(slotAddress, callbackContext);
                    rootCount++;
                }
            }
        }

        return rootCount;
    }

    /// <summary>
    /// Compute the address of a GC slot given the current context.
    /// </summary>
    private static void** GetSlotAddress(GCSlot* slot, ExceptionContext* context, int stackBaseRegister)
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
                    // Relative to caller's RSP (RSP + frame size)
                    // This is tricky - for now, treat as RSP-relative
                    // In practice, the offset should account for this
                    baseAddr = context->Rsp;
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

            // Stack offsets are already denormalized to bytes in GCInfo decoder
            return (void**)(baseAddr + (ulong)slot->StackOffset);
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
