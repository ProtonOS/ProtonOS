// netos mernel - PAL Exception APIs
// Win32-compatible exception handling APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.X64;
using Kernel.Platform;

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
        record.ExceptionAddress = null; // Will be filled in by handler
        record.NumberParameters = numberOfArguments > 15 ? 15 : numberOfArguments;

        for (uint i = 0; i < record.NumberParameters; i++)
        {
            record.ExceptionInformation[i] = arguments[i];
        }

        // For now, just log and potentially crash if unhandled
        // A full implementation would do stack unwinding
        DebugConsole.Write("[SEH] RaiseException: 0x");
        DebugConsole.WriteHex(exceptionCode);
        DebugConsole.WriteLine();

        // If noncontinuable, this should terminate
        if ((exceptionFlags & ExceptionFlags.EXCEPTION_NONCONTINUABLE) != 0)
        {
            DebugConsole.WriteLine("[SEH] FATAL: Noncontinuable exception raised");
            Cpu.HaltForever();
        }
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
}
