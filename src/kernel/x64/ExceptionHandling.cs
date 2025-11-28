// netos mernel - x64 Exception Handling Infrastructure
// Provides SEH-compatible exception handling for JIT code and kernel.
// Based on Windows x64 exception handling model (RUNTIME_FUNCTION, UNWIND_INFO).

using System.Runtime.InteropServices;
using Kernel.Platform;
using Kernel.Memory;
using Kernel.Threading;

namespace Kernel.X64;

/// <summary>
/// Exception codes (Win32 compatible)
/// </summary>
public static class ExceptionCodes
{
    public const uint EXCEPTION_ACCESS_VIOLATION = 0xC0000005;
    public const uint EXCEPTION_ARRAY_BOUNDS_EXCEEDED = 0xC000008C;
    public const uint EXCEPTION_BREAKPOINT = 0x80000003;
    public const uint EXCEPTION_DATATYPE_MISALIGNMENT = 0x80000002;
    public const uint EXCEPTION_FLT_DENORMAL_OPERAND = 0xC000008D;
    public const uint EXCEPTION_FLT_DIVIDE_BY_ZERO = 0xC000008E;
    public const uint EXCEPTION_FLT_INEXACT_RESULT = 0xC000008F;
    public const uint EXCEPTION_FLT_INVALID_OPERATION = 0xC0000090;
    public const uint EXCEPTION_FLT_OVERFLOW = 0xC0000091;
    public const uint EXCEPTION_FLT_STACK_CHECK = 0xC0000092;
    public const uint EXCEPTION_FLT_UNDERFLOW = 0xC0000093;
    public const uint EXCEPTION_ILLEGAL_INSTRUCTION = 0xC000001D;
    public const uint EXCEPTION_IN_PAGE_ERROR = 0xC0000006;
    public const uint EXCEPTION_INT_DIVIDE_BY_ZERO = 0xC0000094;
    public const uint EXCEPTION_INT_OVERFLOW = 0xC0000095;
    public const uint EXCEPTION_INVALID_DISPOSITION = 0xC0000026;
    public const uint EXCEPTION_NONCONTINUABLE_EXCEPTION = 0xC0000025;
    public const uint EXCEPTION_PRIV_INSTRUCTION = 0xC0000096;
    public const uint EXCEPTION_SINGLE_STEP = 0x80000004;
    public const uint EXCEPTION_STACK_OVERFLOW = 0xC00000FD;
}

/// <summary>
/// Exception flags
/// </summary>
public static class ExceptionFlags
{
    public const uint EXCEPTION_NONCONTINUABLE = 0x01;
    public const uint EXCEPTION_UNWINDING = 0x02;
    public const uint EXCEPTION_EXIT_UNWIND = 0x04;
    public const uint EXCEPTION_STACK_INVALID = 0x08;
    public const uint EXCEPTION_NESTED_CALL = 0x10;
    public const uint EXCEPTION_TARGET_UNWIND = 0x20;
    public const uint EXCEPTION_COLLIDED_UNWIND = 0x40;
}

/// <summary>
/// Exception disposition returned by handler
/// </summary>
public enum ExceptionDisposition : int
{
    ExceptionContinueExecution = 0,
    ExceptionContinueSearch = 1,
    ExceptionNestedException = 2,
    ExceptionCollidedUnwind = 3
}

/// <summary>
/// Exception record structure (Win32 compatible)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ExceptionRecord
{
    public uint ExceptionCode;
    public uint ExceptionFlags;
    public ExceptionRecord* ExceptionRecord_;  // Chained exception
    public void* ExceptionAddress;
    public uint NumberParameters;
    public fixed ulong ExceptionInformation[15];  // EXCEPTION_MAXIMUM_PARAMETERS
}

/// <summary>
/// Context record for x64 (simplified - just what we need for unwinding)
/// Full CONTEXT would include XMM registers, debug registers, etc.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ExceptionContext
{
    // Control registers
    public ulong Rip;
    public ulong Rsp;
    public ulong Rbp;
    public ulong Rflags;

    // General purpose registers
    public ulong Rax;
    public ulong Rbx;
    public ulong Rcx;
    public ulong Rdx;
    public ulong Rsi;
    public ulong Rdi;
    public ulong R8;
    public ulong R9;
    public ulong R10;
    public ulong R11;
    public ulong R12;
    public ulong R13;
    public ulong R14;
    public ulong R15;

    // Segment registers
    public ushort Cs;
    public ushort Ss;
}

/// <summary>
/// RUNTIME_FUNCTION structure for x64 SEH.
/// Describes a function's exception handling info.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct RuntimeFunction
{
    public uint BeginAddress;      // RVA of function start
    public uint EndAddress;        // RVA of function end
    public uint UnwindInfoAddress; // RVA of UNWIND_INFO structure
}

/// <summary>
/// UNWIND_INFO header for x64 SEH.
/// Describes how to unwind a function's stack frame.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UnwindInfo
{
    // Version (3 bits), Flags (5 bits)
    // Flags: UNW_FLAG_EHANDLER (0x01), UNW_FLAG_UHANDLER (0x02), UNW_FLAG_CHAININFO (0x04)
    public byte VersionAndFlags;
    public byte SizeOfProlog;
    public byte CountOfUnwindCodes;
    // Frame register (4 bits), Frame register offset (4 bits)
    public byte FrameRegisterAndOffset;
    // Followed by: UNWIND_CODE array[CountOfUnwindCodes]
    // Then if UNW_FLAG_EHANDLER or UNW_FLAG_UHANDLER: exception handler RVA (4 bytes)
    // Then: language-specific handler data

    public byte Version => (byte)(VersionAndFlags & 0x7);
    public byte Flags => (byte)(VersionAndFlags >> 3);
    public byte FrameRegister => (byte)(FrameRegisterAndOffset & 0xF);
    public byte FrameOffset => (byte)(FrameRegisterAndOffset >> 4);
}

/// <summary>
/// Unwind flags for UNWIND_INFO
/// </summary>
public static class UnwindFlags
{
    public const byte UNW_FLAG_NHANDLER = 0x00;
    public const byte UNW_FLAG_EHANDLER = 0x01;  // Exception handler
    public const byte UNW_FLAG_UHANDLER = 0x02;  // Unwind handler
    public const byte UNW_FLAG_CHAININFO = 0x04; // Chained to another RUNTIME_FUNCTION
}

/// <summary>
/// Function table entry for dynamically registered code regions.
/// Used for JIT-compiled code.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct FunctionTableEntry
{
    public ulong BaseAddress;        // Base address of code region
    public RuntimeFunction* Functions; // Pointer to RUNTIME_FUNCTION array
    public uint FunctionCount;       // Number of entries
    public bool InUse;               // Entry is active
}

/// <summary>
/// Exception handler callback type.
/// </summary>
public unsafe delegate ExceptionDisposition ExceptionHandler(
    ExceptionRecord* exceptionRecord,
    void* establisherFrame,
    ExceptionContext* context,
    void* dispatcherContext);

/// <summary>
/// Unhandled exception filter callback type.
/// Returns: EXCEPTION_EXECUTE_HANDLER (1), EXCEPTION_CONTINUE_SEARCH (0), EXCEPTION_CONTINUE_EXECUTION (-1)
/// </summary>
public unsafe delegate int UnhandledExceptionFilter(ExceptionRecord* exceptionRecord, ExceptionContext* context);

/// <summary>
/// Static storage for function table entries
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FunctionTableStorage
{
    // Up to 64 registered code regions
    public fixed byte Data[64 * 40]; // 64 entries × sizeof(FunctionTableEntry)
}

/// <summary>
/// Exception handling infrastructure for x64.
/// Manages function tables for SEH and dispatches exceptions.
/// </summary>
public static unsafe class ExceptionHandling
{
    private const int MaxFunctionTables = 64;

    private static FunctionTableStorage _tableStorage;
    private static SpinLock _lock;
    private static bool _initialized;

    // Unhandled exception filter (can be set by user code)
    private static delegate* unmanaged<ExceptionRecord*, ExceptionContext*, int> _unhandledFilter;

    /// <summary>
    /// Initialize exception handling subsystem.
    /// </summary>
    public static void Init()
    {
        if (_initialized) return;

        // Clear function table storage
        fixed (byte* ptr = _tableStorage.Data)
        {
            for (int i = 0; i < MaxFunctionTables * sizeof(FunctionTableEntry); i++)
                ptr[i] = 0;
        }

        _lock = default;
        _unhandledFilter = null;
        _initialized = true;

        DebugConsole.WriteLine("[SEH] Exception handling initialized");
    }

    /// <summary>
    /// Register a function table for a code region (like RtlAddFunctionTable).
    /// </summary>
    /// <param name="functionTable">Pointer to RUNTIME_FUNCTION array</param>
    /// <param name="entryCount">Number of entries in the array</param>
    /// <param name="baseAddress">Base address of the code region</param>
    /// <returns>True on success</returns>
    public static bool AddFunctionTable(RuntimeFunction* functionTable, uint entryCount, ulong baseAddress)
    {
        if (!_initialized) Init();

        _lock.Acquire();

        fixed (byte* ptr = _tableStorage.Data)
        {
            var tables = (FunctionTableEntry*)ptr;

            // Find free slot
            for (int i = 0; i < MaxFunctionTables; i++)
            {
                if (!tables[i].InUse)
                {
                    tables[i].BaseAddress = baseAddress;
                    tables[i].Functions = functionTable;
                    tables[i].FunctionCount = entryCount;
                    tables[i].InUse = true;

                    _lock.Release();
                    return true;
                }
            }
        }

        _lock.Release();
        return false; // No free slots
    }

    /// <summary>
    /// Remove a function table (like RtlDeleteFunctionTable).
    /// </summary>
    public static bool DeleteFunctionTable(RuntimeFunction* functionTable)
    {
        if (!_initialized) return false;

        _lock.Acquire();

        fixed (byte* ptr = _tableStorage.Data)
        {
            var tables = (FunctionTableEntry*)ptr;

            for (int i = 0; i < MaxFunctionTables; i++)
            {
                if (tables[i].InUse && tables[i].Functions == functionTable)
                {
                    tables[i].InUse = false;
                    _lock.Release();
                    return true;
                }
            }
        }

        _lock.Release();
        return false;
    }

    /// <summary>
    /// Look up the RUNTIME_FUNCTION for an instruction pointer (like RtlLookupFunctionEntry).
    /// </summary>
    /// <param name="controlPc">Instruction pointer to look up</param>
    /// <param name="imageBase">Receives the base address of the code region</param>
    /// <returns>Pointer to RUNTIME_FUNCTION or null if not found</returns>
    public static RuntimeFunction* LookupFunctionEntry(ulong controlPc, out ulong imageBase)
    {
        imageBase = 0;

        if (!_initialized)
        {
            return null;
        }

        _lock.Acquire();

        fixed (byte* ptr = _tableStorage.Data)
        {
            var tables = (FunctionTableEntry*)ptr;

            for (int i = 0; i < MaxFunctionTables; i++)
            {
                if (!tables[i].InUse)
                    continue;

                ulong baseAddr = tables[i].BaseAddress;
                var functions = tables[i].Functions;
                uint count = tables[i].FunctionCount;

                // Binary search through RUNTIME_FUNCTION array
                uint rva = (uint)(controlPc - baseAddr);

                int left = 0;
                int right = (int)count - 1;

                while (left <= right)
                {
                    int mid = (left + right) / 2;
                    var func = &functions[mid];

                    if (rva < func->BeginAddress)
                    {
                        right = mid - 1;
                    }
                    else if (rva >= func->EndAddress)
                    {
                        left = mid + 1;
                    }
                    else
                    {
                        // Found it
                        imageBase = baseAddr;
                        _lock.Release();
                        return func;
                    }
                }
            }
        }

        _lock.Release();
        return null;
    }

    /// <summary>
    /// Set the unhandled exception filter.
    /// </summary>
    public static delegate* unmanaged<ExceptionRecord*, ExceptionContext*, int> SetUnhandledExceptionFilter(
        delegate* unmanaged<ExceptionRecord*, ExceptionContext*, int> filter)
    {
        var old = _unhandledFilter;
        _unhandledFilter = filter;
        return old;
    }

    /// <summary>
    /// Convert CPU interrupt vector to Win32 exception code.
    /// </summary>
    public static uint VectorToExceptionCode(int vector)
    {
        return vector switch
        {
            0 => ExceptionCodes.EXCEPTION_INT_DIVIDE_BY_ZERO,
            1 => ExceptionCodes.EXCEPTION_SINGLE_STEP,
            3 => ExceptionCodes.EXCEPTION_BREAKPOINT,
            4 => ExceptionCodes.EXCEPTION_INT_OVERFLOW,
            5 => ExceptionCodes.EXCEPTION_ARRAY_BOUNDS_EXCEEDED,
            6 => ExceptionCodes.EXCEPTION_ILLEGAL_INSTRUCTION,
            13 => ExceptionCodes.EXCEPTION_ACCESS_VIOLATION, // GPF often from bad memory access
            14 => ExceptionCodes.EXCEPTION_ACCESS_VIOLATION, // Page fault
            17 => ExceptionCodes.EXCEPTION_DATATYPE_MISALIGNMENT,
            _ => 0xE0000000 | (uint)vector, // Custom exception code
        };
    }

    /// <summary>
    /// Create ExceptionContext from InterruptFrame.
    /// </summary>
    public static void FrameToContext(InterruptFrame* frame, ExceptionContext* context)
    {
        context->Rip = frame->Rip;
        context->Rsp = frame->Rsp;
        context->Rbp = frame->Rbp;
        context->Rflags = frame->Rflags;
        context->Rax = frame->Rax;
        context->Rbx = frame->Rbx;
        context->Rcx = frame->Rcx;
        context->Rdx = frame->Rdx;
        context->Rsi = frame->Rsi;
        context->Rdi = frame->Rdi;
        context->R8 = frame->R8;
        context->R9 = frame->R9;
        context->R10 = frame->R10;
        context->R11 = frame->R11;
        context->R12 = frame->R12;
        context->R13 = frame->R13;
        context->R14 = frame->R14;
        context->R15 = frame->R15;
        context->Cs = (ushort)frame->Cs;
        context->Ss = (ushort)frame->Ss;
    }

    /// <summary>
    /// Update InterruptFrame from ExceptionContext (for continuing execution).
    /// </summary>
    public static void ContextToFrame(ExceptionContext* context, InterruptFrame* frame)
    {
        frame->Rip = context->Rip;
        frame->Rsp = context->Rsp;
        frame->Rbp = context->Rbp;
        frame->Rflags = context->Rflags;
        frame->Rax = context->Rax;
        frame->Rbx = context->Rbx;
        frame->Rcx = context->Rcx;
        frame->Rdx = context->Rdx;
        frame->Rsi = context->Rsi;
        frame->Rdi = context->Rdi;
        frame->R8 = context->R8;
        frame->R9 = context->R9;
        frame->R10 = context->R10;
        frame->R11 = context->R11;
        frame->R12 = context->R12;
        frame->R13 = context->R13;
        frame->R14 = context->R14;
        frame->R15 = context->R15;
    }

    /// <summary>
    /// Dispatch an exception to registered handlers.
    /// Called from Arch.DispatchInterrupt for CPU exceptions.
    /// </summary>
    /// <returns>True if exception was handled, false if unhandled</returns>
    public static bool DispatchException(InterruptFrame* frame, int vector)
    {
        if (!_initialized) Init();

        // Build exception record
        ExceptionRecord exceptionRecord;
        exceptionRecord.ExceptionCode = VectorToExceptionCode(vector);
        exceptionRecord.ExceptionFlags = 0;
        exceptionRecord.ExceptionRecord_ = null;
        exceptionRecord.ExceptionAddress = (void*)frame->Rip;
        exceptionRecord.NumberParameters = 0;

        // Add extra info for specific exceptions
        if (vector == 14) // Page fault
        {
            exceptionRecord.NumberParameters = 2;
            // Error code bit 0: 0=not present, 1=protection violation
            // Error code bit 1: 0=read, 1=write
            exceptionRecord.ExceptionInformation[0] = (frame->ErrorCode & 2) != 0 ? 1UL : 0UL; // Write access
            exceptionRecord.ExceptionInformation[1] = Cpu.ReadCr2(); // Fault address
        }

        // Build context
        ExceptionContext context;
        FrameToContext(frame, &context);

        // Look up function entry for RIP
        ulong imageBase;
        var runtimeFunction = LookupFunctionEntry(frame->Rip, out imageBase);

        // If we found a registered function with exception handler, call it
        if (runtimeFunction != null)
        {
            var unwindInfo = (UnwindInfo*)(imageBase + runtimeFunction->UnwindInfoAddress);

            if ((unwindInfo->Flags & UnwindFlags.UNW_FLAG_EHANDLER) != 0)
            {
                // Get handler address (after unwind codes)
                int unwindCodeSize = (unwindInfo->CountOfUnwindCodes + 1) & ~1; // Round up to even
                var handlerRva = (uint*)((byte*)unwindInfo + 4 + unwindCodeSize * 2);
                var handler = (delegate*<ExceptionRecord*, void*, ExceptionContext*, void*, ExceptionDisposition>)
                    (imageBase + *handlerRva);

                // Call handler
                var disposition = handler(&exceptionRecord, null, &context, null);

                if (disposition == ExceptionDisposition.ExceptionContinueExecution)
                {
                    // Handler fixed it, update frame and continue
                    ContextToFrame(&context, frame);
                    return true;
                }
            }
        }

        // Try unhandled exception filter
        if (_unhandledFilter != null)
        {
            int result = _unhandledFilter(&exceptionRecord, &context);

            if (result == -1) // EXCEPTION_CONTINUE_EXECUTION
            {
                // Filter handled exception, update frame from modified context
                ContextToFrame(&context, frame);
                return true;
            }
            if (result == 1) // EXCEPTION_EXECUTE_HANDLER
            {
                // Filter wants to handle it - but we don't have a handler to call
                // In a full implementation this would unwind and call a handler
                return false;
            }
            // result == 0: EXCEPTION_CONTINUE_SEARCH - fall through
        }

        // Exception not handled
        return false;
    }

    /// <summary>
    /// Dispatch a software-raised exception (from RaiseException API).
    /// This is similar to DispatchException but takes pre-built exception record and context.
    /// </summary>
    /// <param name="exceptionRecord">The exception record</param>
    /// <param name="context">The context at the time of the exception</param>
    /// <returns>
    /// -1 (EXCEPTION_CONTINUE_EXECUTION) if exception was handled and execution should continue,
    /// 0 (EXCEPTION_CONTINUE_SEARCH) if exception was not handled,
    /// 1 (EXCEPTION_EXECUTE_HANDLER) if a handler should be executed (unhandled in our impl)
    /// </returns>
    public static int DispatchRaisedException(ExceptionRecord* exceptionRecord, ExceptionContext* context)
    {
        if (!_initialized) Init();

        // Look up function entry for RIP
        ulong imageBase;
        var runtimeFunction = LookupFunctionEntry(context->Rip, out imageBase);

        // If we found a registered function with exception handler, call it
        if (runtimeFunction != null)
        {
            var unwindInfo = (UnwindInfo*)(imageBase + runtimeFunction->UnwindInfoAddress);

            if ((unwindInfo->Flags & UnwindFlags.UNW_FLAG_EHANDLER) != 0)
            {
                // Get handler address (after unwind codes)
                int unwindCodeSize = (unwindInfo->CountOfUnwindCodes + 1) & ~1; // Round up to even
                var handlerRva = (uint*)((byte*)unwindInfo + 4 + unwindCodeSize * 2);
                var handler = (delegate*<ExceptionRecord*, void*, ExceptionContext*, void*, ExceptionDisposition>)
                    (imageBase + *handlerRva);

                // Call handler
                var disposition = handler(exceptionRecord, null, context, null);

                if (disposition == ExceptionDisposition.ExceptionContinueExecution)
                {
                    return -1; // EXCEPTION_CONTINUE_EXECUTION
                }
            }
        }

        // Try unhandled exception filter
        if (_unhandledFilter != null)
        {
            int result = _unhandledFilter(exceptionRecord, context);
            return result;
        }

        // No filter registered, return continue search
        return 0; // EXCEPTION_CONTINUE_SEARCH
    }
}
