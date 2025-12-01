// netos mernel - PAL Exception APIs
// Win32-compatible exception handling APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.X64;
using Kernel.Platform;
using Kernel.Threading;

namespace Kernel.PAL;

/// <summary>
/// PAL Exception APIs - Win32-compatible exception handling functions.
/// </summary>
public static unsafe class Exception
{
    /// <summary>
    /// Set the unhandled exception filter.
    /// Returns the previous filter.
    /// </summary>
    public static delegate* unmanaged<ExceptionRecord*, ExceptionContext*, int> SetUnhandledExceptionFilter(
        delegate* unmanaged<ExceptionRecord*, ExceptionContext*, int> filter)
    {
        return ExceptionHandling.SetUnhandledExceptionFilter(filter);
    }

    /// <summary>
    /// Register a function table for dynamically generated code.
    /// This is the equivalent of RtlAddFunctionTable.
    /// </summary>
    /// <param name="functionTable">Pointer to RUNTIME_FUNCTION array</param>
    /// <param name="entryCount">Number of entries</param>
    /// <param name="baseAddress">Base address of the code region</param>
    /// <returns>True on success</returns>
    public static bool RtlAddFunctionTable(RuntimeFunction* functionTable, uint entryCount, ulong baseAddress)
    {
        return ExceptionHandling.AddFunctionTable(functionTable, entryCount, baseAddress);
    }

    /// <summary>
    /// Remove a previously registered function table.
    /// This is the equivalent of RtlDeleteFunctionTable.
    /// </summary>
    public static bool RtlDeleteFunctionTable(RuntimeFunction* functionTable)
    {
        return ExceptionHandling.DeleteFunctionTable(functionTable);
    }

    /// <summary>
    /// Look up the runtime function entry for an instruction pointer.
    /// This is the equivalent of RtlLookupFunctionEntry.
    /// </summary>
    /// <param name="controlPc">Instruction pointer to look up</param>
    /// <param name="imageBase">Receives the base address of the containing image</param>
    /// <param name="historyTable">Ignored (for API compatibility)</param>
    /// <returns>Pointer to RUNTIME_FUNCTION or null if not found</returns>
    public static RuntimeFunction* RtlLookupFunctionEntry(ulong controlPc, out ulong imageBase, void* historyTable)
    {
        return ExceptionHandling.LookupFunctionEntry(controlPc, out imageBase);
    }

    /// <summary>
    /// Raise an exception.
    /// This dispatches the exception to the unhandled exception filter if one is registered.
    /// If the filter returns EXCEPTION_CONTINUE_EXECUTION (-1), execution continues.
    /// If the exception is noncontinuable or the filter returns EXCEPTION_EXECUTE_HANDLER (1),
    /// the process/thread is terminated.
    /// </summary>
    /// <param name="exceptionCode">Exception code to raise</param>
    /// <param name="exceptionFlags">Exception flags</param>
    /// <param name="numberOfArguments">Number of arguments</param>
    /// <param name="arguments">Pointer to arguments array</param>
    public static void RaiseException(uint exceptionCode, uint exceptionFlags, uint numberOfArguments, ulong* arguments)
    {
        // Build exception record
        ExceptionRecord record;
        record.ExceptionCode = exceptionCode;
        record.ExceptionFlags = exceptionFlags;
        record.ExceptionRecord_ = null;
        record.NumberParameters = numberOfArguments > 15 ? 15 : numberOfArguments;

        // Copy exception parameters
        for (uint i = 0; i < record.NumberParameters; i++)
        {
            record.ExceptionInformation[i] = arguments != null ? arguments[i] : 0;
        }

        // Capture context - use the current thread's saved context
        // Note: This won't be perfectly accurate for the exact call site, but it's
        // sufficient for exception handling purposes
        ExceptionContext context;
        CaptureContext(&context);

        // Set exception address to caller's return address (approximation)
        // In a real implementation this would be the actual faulting instruction
        record.ExceptionAddress = (void*)context.Rip;

        // Log the exception
        DebugConsole.Write("[SEH] RaiseException: 0x");
        DebugConsole.WriteHex(exceptionCode);
        if ((exceptionFlags & ExceptionFlags.EXCEPTION_NONCONTINUABLE) != 0)
            DebugConsole.Write(" (noncontinuable)");
        DebugConsole.WriteLine();

        // Dispatch to unhandled exception filter
        int filterResult = ExceptionHandling.DispatchRaisedException(&record, &context);

        if (filterResult == -1) // EXCEPTION_CONTINUE_EXECUTION
        {
            // Filter handled it, continue execution
            // Note: In a real implementation, we would restore the modified context
            DebugConsole.WriteLine("[SEH] Exception handled, continuing execution");
            return;
        }

        // Exception was not handled or filter returned EXCEPTION_EXECUTE_HANDLER
        // If noncontinuable, this is fatal
        if ((exceptionFlags & ExceptionFlags.EXCEPTION_NONCONTINUABLE) != 0)
        {
            DebugConsole.WriteLine("[SEH] FATAL: Noncontinuable exception raised");
            CPU.HaltForever();
        }

        // For continuable exceptions that weren't handled, halt the thread
        DebugConsole.WriteLine("[SEH] Unhandled exception - halting");
        CPU.HaltForever();
    }

    /// <summary>
    /// Capture the current context for exception handling.
    /// </summary>
    private static void CaptureContext(ExceptionContext* context)
    {
        var thread = Scheduler.CurrentThread;
        if (thread == null)
        {
            // No thread context available, zero out
            *context = default;
            return;
        }

        ref CPUContext ctx = ref thread->Context;

        context->Rip = ctx.Rip;
        context->Rsp = ctx.Rsp;
        context->Rbp = ctx.Rbp;
        context->Rflags = ctx.Rflags;
        context->Rax = ctx.Rax;
        context->Rbx = ctx.Rbx;
        context->Rcx = ctx.Rcx;
        context->Rdx = ctx.Rdx;
        context->Rsi = ctx.Rsi;
        context->Rdi = ctx.Rdi;
        context->R8 = ctx.R8;
        context->R9 = ctx.R9;
        context->R10 = ctx.R10;
        context->R11 = ctx.R11;
        context->R12 = ctx.R12;
        context->R13 = ctx.R13;
        context->R14 = ctx.R14;
        context->R15 = ctx.R15;
        context->Cs = (ushort)ctx.Cs;
        context->Ss = (ushort)ctx.Ss;
    }

    /// <summary>
    /// Get the current exception record (for use in exception handlers).
    /// Returns null if not in an exception handler.
    /// </summary>
    public static ExceptionRecord* GetExceptionInformation()
    {
        // This would require tracking the current exception in TLS
        // For now, return null
        return null;
    }

    /// <summary>
    /// Get the current exception code (for use in exception handlers).
    /// Returns 0 if not in an exception handler.
    /// </summary>
    public static uint GetExceptionCode()
    {
        var record = GetExceptionInformation();
        return record != null ? record->ExceptionCode : 0;
    }

    /// <summary>
    /// Virtually unwind to the caller of the context frame.
    /// This is the equivalent of RtlVirtualUnwind from the Windows API.
    /// </summary>
    /// <param name="handlerType">Type of handler to look for (UNW_FLAG_* values)</param>
    /// <param name="imageBase">Base address of the image containing the function</param>
    /// <param name="controlPc">The instruction pointer to unwind from</param>
    /// <param name="functionEntry">The RUNTIME_FUNCTION for the function</param>
    /// <param name="context">The context to unwind (modified in place)</param>
    /// <param name="handlerData">Receives handler-specific data if a handler is found</param>
    /// <param name="establisherFrame">Receives the establisher frame pointer (RSP)</param>
    /// <param name="contextPointers">Optional pointers to where registers were saved</param>
    /// <returns>Pointer to the exception handler routine, or null if none</returns>
    public static void* RtlVirtualUnwind(
        uint handlerType,
        ulong imageBase,
        ulong controlPc,
        RuntimeFunction* functionEntry,
        ExceptionContext* context,
        void** handlerData,
        ulong* establisherFrame,
        KNonvolatileContextPointers* contextPointers)
    {
        return ExceptionHandling.RtlVirtualUnwind(
            handlerType,
            imageBase,
            controlPc,
            functionEntry,
            context,
            handlerData,
            establisherFrame,
            contextPointers);
    }

    /// <summary>
    /// Virtually unwind one stack frame (PAL-style simplified API).
    /// This is the equivalent of PAL_VirtualUnwind from the CoreCLR PAL.
    /// </summary>
    /// <param name="context">The context to unwind (modified in place)</param>
    /// <param name="contextPointers">Optional pointers to where registers were saved</param>
    /// <returns>True if unwinding succeeded, false if at end of stack</returns>
    public static bool PAL_VirtualUnwind(ExceptionContext* context, KNonvolatileContextPointers* contextPointers)
    {
        return ExceptionHandling.VirtualUnwind(context, contextPointers);
    }

    // ========================================================================
    // Runtime Exception Exports for netlib
    // These are called by compiler-generated code for throw statements.
    // ========================================================================
    // RhpThrowEx, RhpRethrow, RhpThrowHwEx are implemented in native.asm
    // They capture context and call RhpThrowEx_Handler in ExceptionHandling.cs
    // ========================================================================
}
