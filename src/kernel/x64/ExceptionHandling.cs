// ProtonOS kernel - x64 Exception Handling Infrastructure
// Provides SEH-compatible exception handling for JIT code and kernel.
// Based on Windows x64 exception handling model (RUNTIME_FUNCTION, UNWIND_INFO).

using System;
using System.Runtime.InteropServices;
using ProtonOS.Platform;
using ProtonOS.Memory;
using ProtonOS.Threading;
using ProtonOS.Runtime;

namespace ProtonOS.X64;

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
/// Unwind operation codes (x64 ABI)
/// </summary>
public static class UnwindOpCodes
{
    public const byte UWOP_PUSH_NONVOL = 0;      // 1 slot: Push nonvolatile register
    public const byte UWOP_ALLOC_LARGE = 1;     // 2-3 slots: Large stack allocation
    public const byte UWOP_ALLOC_SMALL = 2;     // 1 slot: Small stack allocation (8-128 bytes)
    public const byte UWOP_SET_FPREG = 3;       // 1 slot: Establish frame pointer
    public const byte UWOP_SAVE_NONVOL = 4;     // 2 slots: Save nonvolatile register (MOV)
    public const byte UWOP_SAVE_NONVOL_FAR = 5; // 3 slots: Save nonvolatile register (large offset)
    public const byte UWOP_EPILOG = 6;          // 1-2 slots: Epilog info (Windows 10+)
    public const byte UWOP_SPARE_CODE = 7;      // Reserved
    public const byte UWOP_SAVE_XMM128 = 8;     // 2 slots: Save XMM register
    public const byte UWOP_SAVE_XMM128_FAR = 9; // 3 slots: Save XMM register (large offset)
    public const byte UWOP_PUSH_MACHFRAME = 10; // 1 slot: Push machine frame (interrupt/exception)
}

/// <summary>
/// Register indices for unwind operations (x64 ABI)
/// </summary>
public static class UnwindRegister
{
    public const int RAX = 0;
    public const int RCX = 1;
    public const int RDX = 2;
    public const int RBX = 3;
    public const int RSP = 4;
    public const int RBP = 5;
    public const int RSI = 6;
    public const int RDI = 7;
    public const int R8 = 8;
    public const int R9 = 9;
    public const int R10 = 10;
    public const int R11 = 11;
    public const int R12 = 12;
    public const int R13 = 13;
    public const int R14 = 14;
    public const int R15 = 15;
}

/// <summary>
/// UNWIND_CODE structure (2 bytes per slot)
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct UnwindCode
{
    public byte CodeOffset;   // Offset in prolog
    public byte OpAndInfo;    // UnwindOp (low 4 bits), OpInfo (high 4 bits)

    public byte UnwindOp => (byte)(OpAndInfo & 0x0F);
    public byte OpInfo => (byte)(OpAndInfo >> 4);
}

/// <summary>
/// Structure to track locations where nonvolatile registers were saved.
/// Used during unwinding to locate saved register values on stack.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct KNonvolatileContextPointers
{
    // Floating point (XMM) register pointers
    public ulong* Xmm0;
    public ulong* Xmm1;
    public ulong* Xmm2;
    public ulong* Xmm3;
    public ulong* Xmm4;
    public ulong* Xmm5;
    public ulong* Xmm6;
    public ulong* Xmm7;
    public ulong* Xmm8;
    public ulong* Xmm9;
    public ulong* Xmm10;
    public ulong* Xmm11;
    public ulong* Xmm12;
    public ulong* Xmm13;
    public ulong* Xmm14;
    public ulong* Xmm15;

    // Integer register pointers
    public ulong* Rax;
    public ulong* Rcx;
    public ulong* Rdx;
    public ulong* Rbx;
    public ulong* Rsp;
    public ulong* Rbp;
    public ulong* Rsi;
    public ulong* Rdi;
    public ulong* R8;
    public ulong* R9;
    public ulong* R10;
    public ulong* R11;
    public ulong* R12;
    public ulong* R13;
    public ulong* R14;
    public ulong* R15;
}

/// <summary>
/// Handler type for RtlVirtualUnwind
/// </summary>
public static class UNW_HandlerType
{
    public const uint UNW_FLAG_NHANDLER = 0;
    public const uint UNW_FLAG_EHANDLER = 1;
    public const uint UNW_FLAG_UHANDLER = 2;
}

// =============================================================================
// NativeAOT Exception Handling Structures
// The bflat/NativeAOT compiler emits EH clause data in a custom format.
// =============================================================================

/// <summary>
/// Flags for NativeAOT EH info block header.
/// </summary>
public static class NativeAotEHFlags
{
    public const byte HasEHInfo = 0x02;  // EH clause data follows
}

/// <summary>
/// NativeAOT EH clause types.
/// </summary>
public enum EHClauseKind : byte
{
    Typed = 0,      // catch (SpecificException)
    Fault = 1,      // fault handler (finally that only runs on exception)
    Filter = 2,     // catch when (filter expression)
    Finally = 3,    // finally handler
}

/// <summary>
/// Parsed NativeAOT EH clause.
/// </summary>
public struct NativeAotEHClause
{
    public EHClauseKind Kind;
    public uint TryStartOffset;   // RVA offset from function start
    public uint TryEndOffset;     // RVA offset from function start
    public uint HandlerOffset;    // RVA offset of handler funclet from function start
    public uint FilterOffset;     // RVA offset of filter funclet (for Filter kind)
    public ulong CatchTypeMethodTable;    // MethodTable pointer for catch type (for Typed kind)
    public uint LeaveTargetOffset; // RVA offset where execution continues after catch/filter handler returns
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
    // Up to 2048 registered code regions (increased to support large numbers of JIT'd methods)
    public fixed byte Data[2048 * 40]; // 2048 entries × sizeof(FunctionTableEntry)
}

/// <summary>
/// Exception handling infrastructure for x64.
/// Manages function tables for SEH and dispatches exceptions.
/// </summary>
public static unsafe class ExceptionHandling
{
    // ======================== Managed Exception Entry Points ========================

    /// <summary>
    /// C# handler called by assembly RhpThrowEx.
    /// This is the main entry point for managed exception throwing.
    /// </summary>
    /// <param name="exceptionObject">The exception object being thrown</param>
    /// <param name="context">Context captured at throw site (can be modified)</param>
    [UnmanagedCallersOnly(EntryPoint = "RhpThrowEx_Handler")]
    public static void RhpThrowEx_Handler(void* exceptionObject, ExceptionContext* context)
    {
        // Dispatch the exception using NativeAOT EH tables
        bool handled = DispatchNativeAotException(exceptionObject, context);

        if (!handled)
        {
            // Unhandled exception - fatal
            DebugConsole.WriteLine("[EH] FATAL: Unhandled managed exception!");
            DebugConsole.Write("[EH] Exception object at: 0x");
            DebugConsole.WriteHex((ulong)exceptionObject);
            DebugConsole.WriteLine();
            DebugConsole.Write("[EH] Thrown from RIP: 0x");
            DebugConsole.WriteHex(context->Rip);
            DebugConsole.WriteLine();

            // Print stack trace
            DebugConsole.WriteLine("[EH] Stack trace:");
            PrintStackTrace(context);

            // Halt - can't continue without a handler
            CPU.Halt();
        }

        // If handled, the context has been modified to point to the handler.
        // Assembly will restore context and jump to handler address.
    }

    /// <summary>
    /// C# handler called by assembly RhpThrowHwEx.
    /// Converts hardware exception codes to managed exception objects and throws.
    /// </summary>
    /// <param name="exceptionCode">The Win32 exception code (e.g., 0xC0000005 for access violation)</param>
    /// <param name="context">Context captured at fault site</param>
    [UnmanagedCallersOnly(EntryPoint = "RhpThrowHwEx_Handler")]
    public static void RhpThrowHwEx_Handler(uint exceptionCode, ExceptionContext* context)
    {
        // Create appropriate exception object based on exception code
        object exceptionObject = CreateExceptionFromHwCode(exceptionCode);

        // Dispatch the exception using NativeAOT EH tables
        bool handled = DispatchNativeAotException(&exceptionObject, context);

        if (!handled)
        {
            DebugConsole.WriteLine("[EH] FATAL: Unhandled hardware exception!");
            DebugConsole.Write("[EH] Code: 0x");
            DebugConsole.WriteHex(exceptionCode);
            DebugConsole.WriteLine();
            DebugConsole.Write("[EH] Faulting RIP: 0x");
            DebugConsole.WriteHex(context->Rip);
            DebugConsole.WriteLine();
            PrintStackTrace(context);
            CPU.Halt();
        }
    }

    /// <summary>
    /// Create a managed exception object from a hardware exception code.
    /// </summary>
    private static object CreateExceptionFromHwCode(uint exceptionCode)
    {
        // Map Win32 exception codes to .NET exception types
        return exceptionCode switch
        {
            ExceptionCodes.EXCEPTION_ACCESS_VIOLATION => new NullReferenceException(),
            ExceptionCodes.EXCEPTION_INT_DIVIDE_BY_ZERO => new DivideByZeroException(),
            ExceptionCodes.EXCEPTION_FLT_DIVIDE_BY_ZERO => new DivideByZeroException(),
            ExceptionCodes.EXCEPTION_INT_OVERFLOW => new OverflowException(),
            ExceptionCodes.EXCEPTION_FLT_OVERFLOW => new OverflowException(),
            ExceptionCodes.EXCEPTION_ARRAY_BOUNDS_EXCEEDED => new IndexOutOfRangeException(),
            ExceptionCodes.EXCEPTION_STACK_OVERFLOW => new StackOverflowException(),
            _ => new Exception() // Generic exception for unknown codes
        };
    }

    // ======================== Current Exception Tracking (for rethrow) ========================

    // Per-thread current exception tracking - stored in dedicated TLS slots
    private const uint InvalidTlsSlot = 0xFFFFFFFF;
    private static uint _currentExceptionTlsSlot = InvalidTlsSlot;
    private static uint _currentExceptionRipTlsSlot = InvalidTlsSlot;  // Original throw site RIP
    private static uint _currentExceptionRspTlsSlot = InvalidTlsSlot;  // Original throw site RSP
    private static uint _currentExceptionRbpTlsSlot = InvalidTlsSlot;  // Original throw site RBP
    private static uint _currentHandlerClauseTlsSlot = InvalidTlsSlot; // Which clause caught it

    /// <summary>
    /// Initialize the TLS slots for current exception tracking.
    /// Must be called after TLS is initialized.
    /// </summary>
    public static void InitCurrentExceptionTracking()
    {
        _currentExceptionTlsSlot = PAL.TLS.TlsAlloc();
        _currentExceptionRipTlsSlot = PAL.TLS.TlsAlloc();
        _currentExceptionRspTlsSlot = PAL.TLS.TlsAlloc();
        _currentExceptionRbpTlsSlot = PAL.TLS.TlsAlloc();
        _currentHandlerClauseTlsSlot = PAL.TLS.TlsAlloc();
        if (_currentExceptionTlsSlot == InvalidTlsSlot)
        {
            DebugConsole.WriteLine("[EH] WARNING: Failed to allocate TLS slot for current exception");
        }
    }

    /// <summary>
    /// Set the current exception for the current thread (called when entering catch block).
    /// </summary>
    public static void SetCurrentException(void* exceptionObject)
    {
        if (_currentExceptionTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionTlsSlot, exceptionObject);
        }
    }

    /// <summary>
    /// Set the original throw site RIP (called when entering catch block).
    /// This is used by rethrow to know where to resume exception dispatch.
    /// </summary>
    public static void SetCurrentExceptionRip(ulong rip)
    {
        if (_currentExceptionRipTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRipTlsSlot, (void*)rip);
        }
    }

    /// <summary>
    /// Set the original throw site RSP (called when entering catch block).
    /// </summary>
    public static void SetCurrentExceptionRsp(ulong rsp)
    {
        if (_currentExceptionRspTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRspTlsSlot, (void*)rsp);
        }
    }

    /// <summary>
    /// Set the original throw site RBP (called when entering catch block).
    /// </summary>
    public static void SetCurrentExceptionRbp(ulong rbp)
    {
        if (_currentExceptionRbpTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRbpTlsSlot, (void*)rbp);
        }
    }

    /// <summary>
    /// Set which clause index caught the exception (called when entering catch block).
    /// On rethrow, we'll search starting from the NEXT clause.
    /// </summary>
    public static void SetCurrentHandlerClause(uint clauseIndex)
    {
        if (_currentHandlerClauseTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentHandlerClauseTlsSlot, (void*)(ulong)(clauseIndex + 1));  // +1 so 0 means none
        }
    }

    /// <summary>
    /// Get the current exception for the current thread (for rethrow).
    /// </summary>
    public static void* GetCurrentException()
    {
        if (_currentExceptionTlsSlot != InvalidTlsSlot)
        {
            return PAL.TLS.TlsGetValue(_currentExceptionTlsSlot);
        }
        return null;
    }

    /// <summary>
    /// Get the original throw site RIP (for rethrow).
    /// </summary>
    public static ulong GetCurrentExceptionRip()
    {
        if (_currentExceptionRipTlsSlot != InvalidTlsSlot)
        {
            return (ulong)PAL.TLS.TlsGetValue(_currentExceptionRipTlsSlot);
        }
        return 0;
    }

    /// <summary>
    /// Get the original throw site RSP (for rethrow).
    /// </summary>
    public static ulong GetCurrentExceptionRsp()
    {
        if (_currentExceptionRspTlsSlot != InvalidTlsSlot)
        {
            return (ulong)PAL.TLS.TlsGetValue(_currentExceptionRspTlsSlot);
        }
        return 0;
    }

    /// <summary>
    /// Get the original throw site RBP (for rethrow).
    /// </summary>
    public static ulong GetCurrentExceptionRbp()
    {
        if (_currentExceptionRbpTlsSlot != InvalidTlsSlot)
        {
            return (ulong)PAL.TLS.TlsGetValue(_currentExceptionRbpTlsSlot);
        }
        return 0;
    }

    /// <summary>
    /// Get which clause index caught the exception (for rethrow).
    /// Returns the index of the NEXT clause to search (0-based), or 0 if none.
    /// </summary>
    public static uint GetCurrentHandlerClause()
    {
        if (_currentHandlerClauseTlsSlot != InvalidTlsSlot)
        {
            return (uint)(ulong)PAL.TLS.TlsGetValue(_currentHandlerClauseTlsSlot);
        }
        return 0;
    }

    /// <summary>
    /// Clear the current exception (called when leaving catch block normally).
    /// </summary>
    public static void ClearCurrentException()
    {
        if (_currentExceptionTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionTlsSlot, null);
        }
        if (_currentExceptionRipTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRipTlsSlot, null);
        }
        if (_currentExceptionRspTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRspTlsSlot, null);
        }
        if (_currentExceptionRbpTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentExceptionRbpTlsSlot, null);
        }
        if (_currentHandlerClauseTlsSlot != InvalidTlsSlot)
        {
            PAL.TLS.TlsSetValue(_currentHandlerClauseTlsSlot, null);
        }
    }

    /// <summary>
    /// C# handler called by assembly RhpRethrow.
    /// Rethrows the current exception (for bare "throw;" statements).
    /// </summary>
    /// <param name="context">Context captured at rethrow site</param>
    [UnmanagedCallersOnly(EntryPoint = "RhpRethrow_Handler")]
    public static void RhpRethrow_Handler(ExceptionContext* context)
    {
        void* currentException = GetCurrentException();
        ulong originalRip = GetCurrentExceptionRip();
        ulong originalRsp = GetCurrentExceptionRsp();
        ulong originalRbp = GetCurrentExceptionRbp();
        uint startClause = GetCurrentHandlerClause();

        if (currentException == null)
        {
            DebugConsole.WriteLine("[EH] FATAL: RhpRethrow called with no current exception!");
            CPU.Halt();
        }

        if (originalRip == 0)
        {
            // Fallback: use context values if original not saved
            originalRip = context->Rip;
            originalRsp = context->Rsp;
            originalRbp = context->Rbp;
        }

        // Create a modified context using the original throw site's RIP/RSP/RBP
        // This allows us to search for handlers in the original function with correct frame info
        ExceptionContext rethrowContext = *context;
        rethrowContext.Rip = originalRip;
        rethrowContext.Rsp = originalRsp;
        rethrowContext.Rbp = originalRbp;

        // Dispatch the rethrown exception, starting from the next clause
        bool handled = DispatchNativeAotExceptionForRethrow(currentException, &rethrowContext, startClause, context);

        if (!handled)
        {
            DebugConsole.WriteLine("[EH] FATAL: Unhandled rethrown exception!");
            PrintStackTrace(context);
            CPU.Halt();
        }
    }

    /// <summary>
    /// Print a simple stack trace for debugging.
    /// </summary>
    private static void PrintStackTrace(ExceptionContext* context)
    {
        ExceptionContext walkContext = *context;
        int maxFrames = 20;

        for (int frame = 0; frame < maxFrames && walkContext.Rip != 0; frame++)
        {
            DebugConsole.Write("  [");
            DebugConsole.WriteDecimal(frame);
            DebugConsole.Write("] RIP=0x");
            DebugConsole.WriteHex(walkContext.Rip);

            // Try to find function info
            ulong imageBase;
            var funcEntry = LookupFunctionEntry(walkContext.Rip, out imageBase);
            if (funcEntry != null)
            {
                DebugConsole.Write(" (func at 0x");
                DebugConsole.WriteHex(imageBase + funcEntry->BeginAddress);
                DebugConsole.Write(")");
            }
            DebugConsole.WriteLine();

            // Unwind one frame
            if (!VirtualUnwind(&walkContext, null))
                break;
        }
    }

    private const int MaxFunctionTables = 2048;

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

        // Initialize TLS slot for current exception tracking (for rethrow support)
        InitCurrentExceptionTracking();

        // Register kernel's function table from PE .pdata section
        RegisterKernelFunctionTable();
    }

    /// <summary>
    /// Register the kernel's function table from the PE .pdata section.
    /// This is needed for exception handling to work on kernel code.
    /// </summary>
    private static void RegisterKernelFunctionTable()
    {
        // Get kernel image base from UEFI
        ulong imageBase = UEFIBoot.ImageBase;
        if (imageBase == 0)
            return;

        // Parse PE header to find .pdata section
        var dosHeader = (ImageDosHeader*)imageBase;

        // Verify DOS signature
        if (dosHeader->e_magic != 0x5A4D)  // "MZ"
            return;

        // Get NT headers
        var ntHeaders = (ImageNtHeaders64*)(imageBase + (ulong)dosHeader->e_lfanew);

        // Verify PE signature
        if (ntHeaders->Signature != 0x00004550)  // "PE\0\0"
            return;

        // Verify PE32+ (64-bit)
        if (ntHeaders->OptionalHeader.Magic != 0x20b)
            return;

        // Get exception directory (.pdata)
        var exceptionDir = &ntHeaders->OptionalHeader.ExceptionTable;
        if (exceptionDir->VirtualAddress == 0 || exceptionDir->Size == 0)
            return;

        // Calculate number of RUNTIME_FUNCTION entries
        uint entryCount = exceptionDir->Size / (uint)sizeof(RuntimeFunction);
        var functionTable = (RuntimeFunction*)(imageBase + exceptionDir->VirtualAddress);

        // Register the kernel's function table
        AddFunctionTable(functionTable, entryCount, imageBase);
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

            // Debug: show what we're searching for
            // DebugConsole.Write("[LookupFunc] Searching for RIP=0x");
            // DebugConsole.WriteHex(controlPc);
            // DebugConsole.WriteLine();

            for (int i = 0; i < MaxFunctionTables; i++)
            {
                if (!tables[i].InUse)
                    continue;

                ulong baseAddr = tables[i].BaseAddress;
                var functions = tables[i].Functions;
                uint count = tables[i].FunctionCount;

                // Debug: show each table being checked
                // DebugConsole.Write("[LookupFunc]   table ");
                // DebugConsole.WriteDecimal((uint)i);
                // DebugConsole.Write(": base=0x");
                // DebugConsole.WriteHex(baseAddr);
                // DebugConsole.Write(" count=");
                // DebugConsole.WriteDecimal(count);
                // DebugConsole.WriteLine();

                // Skip if controlPc is before this code region (would overflow when cast to uint)
                if (controlPc < baseAddr)
                    continue;

                // Calculate RVA (offset within the code region)
                uint rva = (uint)(controlPc - baseAddr);

                // Quick upper bound check - if RVA is huge, skip this table
                // (avoids searching when controlPc is way past this code region)
                if (rva > 0x100000)  // 1MB max method size is very generous
                    continue;

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
    /// Look up the main function (RVA 0) for a code region with the given base address.
    /// Used when handling exceptions in funclets - we need to find the parent method.
    /// </summary>
    /// <param name="targetBase">Base address of the code region to search</param>
    /// <returns>Pointer to RUNTIME_FUNCTION for main function, or null if not found</returns>
    public static RuntimeFunction* LookupMainFunctionByBase(ulong targetBase)
    {
        if (!_initialized)
            return null;

        _lock.Acquire();

        fixed (byte* ptr = _tableStorage.Data)
        {
            var tables = (FunctionTableEntry*)ptr;

            for (int i = 0; i < MaxFunctionTables; i++)
            {
                if (!tables[i].InUse)
                    continue;

                ulong baseAddr = tables[i].BaseAddress;
                if (baseAddr != targetBase)
                    continue;

                var functions = tables[i].Functions;
                uint count = tables[i].FunctionCount;

                // The main function should be at index 0 (RVA 0) for JIT methods
                if (count > 0 && functions[0].BeginAddress == 0)
                {
                    _lock.Release();
                    return &functions[0];
                }

                // If not at index 0, search for it
                for (uint j = 0; j < count; j++)
                {
                    if (functions[j].BeginAddress == 0)
                    {
                        _lock.Release();
                        return &functions[j];
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
            exceptionRecord.ExceptionInformation[1] = CPU.ReadCr2(); // Fault address
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

        // Try NativeAOT exception dispatch for managed code
        // Create the appropriate exception object and dispatch through NativeAOT EH system
        object exceptionObj = CreateExceptionFromHwCode(exceptionRecord.ExceptionCode);
        if (exceptionObj != null)
        {
            // Get raw pointer to exception object (object references ARE the pointer in unsafe code)
            void* exPtr = System.Runtime.CompilerServices.Unsafe.AsPointer(ref exceptionObj);
            exPtr = *(void**)exPtr;  // Dereference to get actual object address

            bool handled = DispatchNativeAotException(exPtr, &context);
            if (handled)
            {
                // Exception was handled, update frame and continue
                ContextToFrame(&context, frame);
                return true;
            }
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

    // ======================== RtlVirtualUnwind Implementation ========================

    /// <summary>
    /// Virtually unwind a single stack frame.
    /// This is the core function for stack unwinding on x64.
    /// </summary>
    /// <param name="handlerType">UNW_FLAG_EHANDLER or UNW_FLAG_UHANDLER to look for handlers</param>
    /// <param name="imageBase">Base address of the image containing the function</param>
    /// <param name="controlPc">The instruction pointer within the function</param>
    /// <param name="functionEntry">Pointer to RUNTIME_FUNCTION for this function</param>
    /// <param name="context">Context to unwind (modified in place)</param>
    /// <param name="handlerData">Receives language-specific handler data</param>
    /// <param name="establisherFrame">Receives the establisher frame (RSP after prolog)</param>
    /// <param name="contextPointers">Optional - receives pointers to where registers were saved</param>
    /// <returns>Pointer to exception handler routine, or null if none</returns>
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
        if (functionEntry == null || context == null)
            return null;

        // Get UNWIND_INFO
        var unwindInfo = (UnwindInfo*)(imageBase + functionEntry->UnwindInfoAddress);

        // Calculate RVA of controlPc within the function
        uint prologOffset = (uint)(controlPc - (imageBase + functionEntry->BeginAddress));

        // Get pointer to unwind codes array (starts at offset 4 in UNWIND_INFO)
        var unwindCodes = (UnwindCode*)((byte*)unwindInfo + 4);

        // If we have a frame register, get the frame pointer value
        ulong frameBase;
        if (unwindInfo->FrameRegister != 0)
        {
            frameBase = GetRegisterValue(context, unwindInfo->FrameRegister);
            frameBase -= (ulong)(unwindInfo->FrameOffset * 16);
        }
        else
        {
            frameBase = context->Rsp;
        }

        // Process unwind codes in order (they're stored in reverse prolog order)
        int i = 0;
        while (i < unwindInfo->CountOfUnwindCodes)
        {
            var code = unwindCodes[i];

            // Skip codes that are beyond our current position in the prolog
            // (i.e., the prolog code hasn't executed yet)
            if (code.CodeOffset > prologOffset)
            {
                // Skip this code and any extra slots it uses
                i += GetUnwindCodeSlots(code.UnwindOp, code.OpInfo, &unwindCodes[i]);
                continue;
            }

            // Process the unwind operation
            switch (code.UnwindOp)
            {
                case UnwindOpCodes.UWOP_PUSH_NONVOL:
                    // Pop the register from stack
                    PopRegister(context, contextPointers, code.OpInfo);
                    i++;
                    break;

                case UnwindOpCodes.UWOP_ALLOC_LARGE:
                    if (code.OpInfo == 0)
                    {
                        // Next slot contains size / 8
                        uint allocSize = (uint)(*(ushort*)&unwindCodes[i + 1]) * 8;
                        context->Rsp += allocSize;
                        i += 2;
                    }
                    else
                    {
                        // Next two slots contain full 32-bit size
                        uint allocSize = *(uint*)&unwindCodes[i + 1];
                        context->Rsp += allocSize;
                        i += 3;
                    }
                    break;

                case UnwindOpCodes.UWOP_ALLOC_SMALL:
                    // Size = OpInfo * 8 + 8 (range 8-128 bytes)
                    context->Rsp += (ulong)(code.OpInfo * 8 + 8);
                    i++;
                    break;

                case UnwindOpCodes.UWOP_SET_FPREG:
                    // RSP = frame register - frame offset
                    context->Rsp = GetRegisterValue(context, unwindInfo->FrameRegister)
                                   - (ulong)(unwindInfo->FrameOffset * 16);
                    i++;
                    break;

                case UnwindOpCodes.UWOP_SAVE_NONVOL:
                    {
                        // Offset from RSP where register was saved
                        uint offset = (uint)(*(ushort*)&unwindCodes[i + 1]) * 8;
                        ulong savedAddr = frameBase + offset;
                        RestoreRegisterFromStack(context, contextPointers, code.OpInfo, savedAddr);
                        i += 2;
                    }
                    break;

                case UnwindOpCodes.UWOP_SAVE_NONVOL_FAR:
                    {
                        // Full 32-bit offset
                        uint offset = *(uint*)&unwindCodes[i + 1];
                        ulong savedAddr = frameBase + offset;
                        RestoreRegisterFromStack(context, contextPointers, code.OpInfo, savedAddr);
                        i += 3;
                    }
                    break;

                case UnwindOpCodes.UWOP_SAVE_XMM128:
                    // Skip XMM register restoration for now (we don't track XMM)
                    i += 2;
                    break;

                case UnwindOpCodes.UWOP_SAVE_XMM128_FAR:
                    // Skip XMM register restoration for now
                    i += 3;
                    break;

                case UnwindOpCodes.UWOP_PUSH_MACHFRAME:
                    {
                        // Machine frame pushed by hardware interrupt/exception
                        // OpInfo: 0 = no error code, 1 = error code present
                        ulong rspOffset = code.OpInfo != 0 ? 8UL : 0UL; // Skip error code if present

                        // Machine frame layout (from low to high):
                        // [error code if OpInfo=1]
                        // RIP (8 bytes)
                        // CS  (8 bytes)
                        // RFLAGS (8 bytes)
                        // RSP (8 bytes)
                        // SS  (8 bytes)
                        context->Rip = *(ulong*)(context->Rsp + rspOffset);
                        context->Rflags = *(ulong*)(context->Rsp + rspOffset + 16);
                        context->Rsp = *(ulong*)(context->Rsp + rspOffset + 24);
                        i++;
                    }
                    break;

                case UnwindOpCodes.UWOP_EPILOG:
                case UnwindOpCodes.UWOP_SPARE_CODE:
                    // Skip - not relevant for unwinding
                    i++;
                    break;

                default:
                    // Unknown opcode - skip one slot
                    i++;
                    break;
            }
        }

        // Handle chained unwind info
        if ((unwindInfo->Flags & UnwindFlags.UNW_FLAG_CHAININFO) != 0)
        {
            // Get chained RUNTIME_FUNCTION (after unwind codes, rounded to DWORD boundary)
            int codeArraySize = (unwindInfo->CountOfUnwindCodes + 1) & ~1;
            var chainedFunction = (RuntimeFunction*)((byte*)unwindInfo + 4 + codeArraySize * 2);

            // Recursively unwind the chained function
            return RtlVirtualUnwind(handlerType, imageBase, controlPc,
                chainedFunction, context, handlerData, establisherFrame, contextPointers);
        }

        // Set establisher frame (RSP after unwinding the prolog)
        if (establisherFrame != null)
        {
            *establisherFrame = context->Rsp;
        }

        // Pop return address to get RIP for caller
        context->Rip = *(ulong*)context->Rsp;
        context->Rsp += 8;

        // Check for exception/unwind handler
        void* handler = null;
        if (handlerData != null)
            *handlerData = null;

        if ((handlerType == UNW_HandlerType.UNW_FLAG_EHANDLER &&
             (unwindInfo->Flags & UnwindFlags.UNW_FLAG_EHANDLER) != 0) ||
            (handlerType == UNW_HandlerType.UNW_FLAG_UHANDLER &&
             (unwindInfo->Flags & UnwindFlags.UNW_FLAG_UHANDLER) != 0))
        {
            // Get handler RVA (after unwind codes)
            int codeArraySize = (unwindInfo->CountOfUnwindCodes + 1) & ~1;
            var handlerRva = (uint*)((byte*)unwindInfo + 4 + codeArraySize * 2);
            handler = (void*)(imageBase + *handlerRva);

            // Handler data follows the handler RVA
            if (handlerData != null)
            {
                *handlerData = handlerRva + 1;
            }
        }

        return handler;
    }

    /// <summary>
    /// Get the number of UNWIND_CODE slots an operation uses.
    /// </summary>
    private static int GetUnwindCodeSlots(byte unwindOp, byte opInfo, UnwindCode* codes)
    {
        switch (unwindOp)
        {
            case UnwindOpCodes.UWOP_PUSH_NONVOL:
            case UnwindOpCodes.UWOP_ALLOC_SMALL:
            case UnwindOpCodes.UWOP_SET_FPREG:
            case UnwindOpCodes.UWOP_PUSH_MACHFRAME:
            case UnwindOpCodes.UWOP_EPILOG:
            case UnwindOpCodes.UWOP_SPARE_CODE:
                return 1;

            case UnwindOpCodes.UWOP_ALLOC_LARGE:
                return opInfo == 0 ? 2 : 3;

            case UnwindOpCodes.UWOP_SAVE_NONVOL:
            case UnwindOpCodes.UWOP_SAVE_XMM128:
                return 2;

            case UnwindOpCodes.UWOP_SAVE_NONVOL_FAR:
            case UnwindOpCodes.UWOP_SAVE_XMM128_FAR:
                return 3;

            default:
                return 1;
        }
    }

    /// <summary>
    /// Get a register value from context by register index.
    /// </summary>
    private static ulong GetRegisterValue(ExceptionContext* context, int regIndex)
    {
        return regIndex switch
        {
            UnwindRegister.RAX => context->Rax,
            UnwindRegister.RCX => context->Rcx,
            UnwindRegister.RDX => context->Rdx,
            UnwindRegister.RBX => context->Rbx,
            UnwindRegister.RSP => context->Rsp,
            UnwindRegister.RBP => context->Rbp,
            UnwindRegister.RSI => context->Rsi,
            UnwindRegister.RDI => context->Rdi,
            UnwindRegister.R8 => context->R8,
            UnwindRegister.R9 => context->R9,
            UnwindRegister.R10 => context->R10,
            UnwindRegister.R11 => context->R11,
            UnwindRegister.R12 => context->R12,
            UnwindRegister.R13 => context->R13,
            UnwindRegister.R14 => context->R14,
            UnwindRegister.R15 => context->R15,
            _ => 0
        };
    }

    /// <summary>
    /// Set a register value in context by register index.
    /// </summary>
    private static void SetRegisterValue(ExceptionContext* context, int regIndex, ulong value)
    {
        switch (regIndex)
        {
            case UnwindRegister.RAX: context->Rax = value; break;
            case UnwindRegister.RCX: context->Rcx = value; break;
            case UnwindRegister.RDX: context->Rdx = value; break;
            case UnwindRegister.RBX: context->Rbx = value; break;
            case UnwindRegister.RSP: context->Rsp = value; break;
            case UnwindRegister.RBP: context->Rbp = value; break;
            case UnwindRegister.RSI: context->Rsi = value; break;
            case UnwindRegister.RDI: context->Rdi = value; break;
            case UnwindRegister.R8: context->R8 = value; break;
            case UnwindRegister.R9: context->R9 = value; break;
            case UnwindRegister.R10: context->R10 = value; break;
            case UnwindRegister.R11: context->R11 = value; break;
            case UnwindRegister.R12: context->R12 = value; break;
            case UnwindRegister.R13: context->R13 = value; break;
            case UnwindRegister.R14: context->R14 = value; break;
            case UnwindRegister.R15: context->R15 = value; break;
        }
    }

    /// <summary>
    /// Pop a register from stack (reverse of PUSH).
    /// </summary>
    private static void PopRegister(ExceptionContext* context, KNonvolatileContextPointers* ctxPtrs, int regIndex)
    {
        ulong value = *(ulong*)context->Rsp;
        ulong* stackLocation = (ulong*)context->Rsp;
        context->Rsp += 8;

        SetRegisterValue(context, regIndex, value);

        // Record where the register was saved
        if (ctxPtrs != null)
        {
            SetContextPointer(ctxPtrs, regIndex, stackLocation);
        }
    }

    /// <summary>
    /// Restore a register from a stack location (reverse of MOV [rsp+offset], reg).
    /// </summary>
    private static void RestoreRegisterFromStack(ExceptionContext* context, KNonvolatileContextPointers* ctxPtrs,
        int regIndex, ulong stackAddr)
    {
        ulong value = *(ulong*)stackAddr;
        SetRegisterValue(context, regIndex, value);

        // Record where the register was saved
        if (ctxPtrs != null)
        {
            SetContextPointer(ctxPtrs, regIndex, (ulong*)stackAddr);
        }
    }

    /// <summary>
    /// Set the pointer in context pointers for a register.
    /// </summary>
    private static void SetContextPointer(KNonvolatileContextPointers* ctxPtrs, int regIndex, ulong* location)
    {
        switch (regIndex)
        {
            case UnwindRegister.RAX: ctxPtrs->Rax = location; break;
            case UnwindRegister.RCX: ctxPtrs->Rcx = location; break;
            case UnwindRegister.RDX: ctxPtrs->Rdx = location; break;
            case UnwindRegister.RBX: ctxPtrs->Rbx = location; break;
            case UnwindRegister.RSP: ctxPtrs->Rsp = location; break;
            case UnwindRegister.RBP: ctxPtrs->Rbp = location; break;
            case UnwindRegister.RSI: ctxPtrs->Rsi = location; break;
            case UnwindRegister.RDI: ctxPtrs->Rdi = location; break;
            case UnwindRegister.R8: ctxPtrs->R8 = location; break;
            case UnwindRegister.R9: ctxPtrs->R9 = location; break;
            case UnwindRegister.R10: ctxPtrs->R10 = location; break;
            case UnwindRegister.R11: ctxPtrs->R11 = location; break;
            case UnwindRegister.R12: ctxPtrs->R12 = location; break;
            case UnwindRegister.R13: ctxPtrs->R13 = location; break;
            case UnwindRegister.R14: ctxPtrs->R14 = location; break;
            case UnwindRegister.R15: ctxPtrs->R15 = location; break;
        }
    }

    // =============================================================================
    // NativeAOT EH Info Parsing
    // =============================================================================

    /// <summary>
    /// Get the NativeAOT EH info pointer from a function's UNWIND_INFO.
    ///
    /// NativeAOT/bflat format (after standard UNWIND_INFO):
    /// - [header 4 bytes] [unwind codes] [padding to DWORD] [MethodAssociatedData RVA] [Handler RVA]
    ///
    /// Layout:
    /// - Header: 4 bytes
    /// - UnwindCodes: CountOfUnwindCodes * 2 bytes
    /// - Padding: to align to DWORD boundary
    /// - MethodAssociatedData RVA: 4 bytes - points to managed EH clause data
    /// - Exception Handler RVA: 4 bytes - points to native handler (e.g., RhpExceptionHandler)
    ///
    /// The MethodAssociatedData points to:
    /// - Clause count (NativeUnsigned encoded)
    /// - Clause data for each clause
    /// </summary>
    /// <param name="imageBase">Base address of the image</param>
    /// <param name="unwindInfo">Pointer to UNWIND_INFO</param>
    /// <returns>Pointer to EH info data, or null if none</returns>
    // Unwind block flags (from NativeAOT runtime)
    private const byte UBF_FUNC_KIND_MASK = 0x03;
    private const byte UBF_FUNC_KIND_ROOT = 0x00;
    private const byte UBF_FUNC_KIND_HANDLER = 0x01;
    private const byte UBF_FUNC_KIND_FILTER = 0x02;
    private const byte UBF_FUNC_HAS_EHINFO = 0x04;
    private const byte UBF_FUNC_REVERSE_PINVOKE = 0x08;
    private const byte UBF_FUNC_HAS_ASSOCIATED_DATA = 0x10;

    /// <summary>
    /// Check if a function is a funclet (handler or filter) rather than a root function.
    /// Funclets don't have their own EH info - their parent function does.
    /// </summary>
    /// <param name="imageBase">Base address of the image</param>
    /// <param name="unwindInfo">Pointer to UNWIND_INFO</param>
    /// <returns>True if this is a funclet, false if root function</returns>
    public static bool IsFunclet(ulong imageBase, UnwindInfo* unwindInfo)
    {
        if (unwindInfo == null)
            return false;

        // Calculate offset to end of UNWIND_INFO structure
        int headerSize = 4;
        int unwindArrayBytes = unwindInfo->CountOfUnwindCodes * 2;
        int unwindInfoSize = headerSize + unwindArrayBytes;

        if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
        {
            unwindInfoSize = (unwindInfoSize + 3) & ~3;
            unwindInfoSize += 4;
        }

        // Read unwind block flags byte
        byte* p = (byte*)unwindInfo + unwindInfoSize;
        byte unwindBlockFlags = *p;

        // Check func kind - non-zero means funclet (handler or filter)
        byte funcKind = (byte)(unwindBlockFlags & UBF_FUNC_KIND_MASK);
        return funcKind != UBF_FUNC_KIND_ROOT;
    }

    /// <summary>
    /// Get the EH clause index that a funclet belongs to.
    /// The clause index is stored in bits 2-7 of the unwind block flags byte.
    /// </summary>
    /// <param name="unwindInfo">Pointer to UNWIND_INFO</param>
    /// <returns>EH clause index (0-63), or -1 if not a funclet</returns>
    public static int GetFuncletClauseIndex(UnwindInfo* unwindInfo)
    {
        if (unwindInfo == null)
            return -1;

        // Calculate offset to end of UNWIND_INFO structure
        int headerSize = 4;
        int unwindArrayBytes = unwindInfo->CountOfUnwindCodes * 2;
        int unwindInfoSize = headerSize + unwindArrayBytes;

        if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
        {
            unwindInfoSize = (unwindInfoSize + 3) & ~3;
            unwindInfoSize += 4;
        }

        // Read unwind block flags byte
        byte* p = (byte*)unwindInfo + unwindInfoSize;
        byte unwindBlockFlags = *p;

        // Check if this is actually a funclet
        byte funcKind = (byte)(unwindBlockFlags & UBF_FUNC_KIND_MASK);
        if (funcKind == UBF_FUNC_KIND_ROOT)
            return -1;

        // Extract clause index from bits 2-7
        return (unwindBlockFlags >> 2) & 0x3F;
    }

    public static byte* GetNativeAotEHInfo(ulong imageBase, UnwindInfo* unwindInfo)
    {
        if (unwindInfo == null)
            return null;

        // Calculate offset to end of UNWIND_INFO structure
        // Layout per NativeAOT CoffNativeCodeManager.cpp:
        //   offsetof(UNWIND_INFO, UnwindCode) + sizeof(UNWIND_CODE) * CountOfUnwindCodes
        // This is header (4 bytes) + CountOfUnwindCodes * 2 (NO rounding to even)
        int headerSize = 4;
        int unwindArrayBytes = unwindInfo->CountOfUnwindCodes * 2;
        int unwindInfoSize = headerSize + unwindArrayBytes;

        // If exception handler flags are set, need to DWORD align and add exception handler RVA
        if ((unwindInfo->Flags & (UnwindFlags.UNW_FLAG_EHANDLER | UnwindFlags.UNW_FLAG_UHANDLER)) != 0)
        {
            // Align to DWORD boundary
            unwindInfoSize = (unwindInfoSize + 3) & ~3;
            // Add exception handler RVA
            unwindInfoSize += 4;
        }

        // After UNWIND_INFO comes the unwind block flags byte
        byte* p = (byte*)unwindInfo + unwindInfoSize;
        byte unwindBlockFlags = *p++;

        // Skip associated data RVA if present
        if ((unwindBlockFlags & UBF_FUNC_HAS_ASSOCIATED_DATA) != 0)
        {
            p += 4;  // Skip 4-byte associated data RVA
        }

        // Check if function has EH info
        if ((unwindBlockFlags & UBF_FUNC_HAS_EHINFO) == 0)
        {
            return null;
        }

        // Read the EHInfo RVA
        uint ehInfoRva = *(uint*)p;

        // Validate the RVA - it should be non-zero and point to a reasonable address
        if (ehInfoRva == 0 || ehInfoRva > 0x100000)  // Max 1MB RVA, reasonable for kernel
        {
            return null;
        }

        // Return pointer to ehinfo data (starts with clause count)
        return (byte*)(imageBase + ehInfoRva);
    }

    /// <summary>
    /// Read a NativeAOT-encoded unsigned integer from a byte stream.
    /// This is NOT LEB128 - it uses a custom format from NativePrimitiveDecoder.
    ///
    /// Encoding (based on low bits of first byte):
    /// - bit0 = 0: 1-byte, value = byte >> 1 (0-127)
    /// - bit1 = 0, bit0 = 1: 2-byte, value = (b0 >> 2) | (b1 << 6)
    /// - bit2 = 0, bits0-1 = 1: 3-byte, value = (b0 >> 3) | (b1 << 5) | (b2 << 13)
    /// - bit3 = 0, bits0-2 = 1: 4-byte, value = (b0 >> 4) | (b1 << 4) | (b2 << 12) | (b3 << 20)
    /// - bits0-3 = 1: 5-byte, skip first byte, read 4-byte little-endian value
    /// </summary>
    private static uint ReadNativeUnsigned(ref byte* ptr)
    {
        byte b0 = *ptr++;

        // 1-byte encoding: bit 0 = 0
        if ((b0 & 1) == 0)
        {
            return (uint)(b0 >> 1);
        }

        // 2-byte encoding: bit 1 = 0, bit 0 = 1
        if ((b0 & 2) == 0)
        {
            byte b1 = *ptr++;
            return (uint)((b0 >> 2) | (b1 << 6));
        }

        // 3-byte encoding: bit 2 = 0, bits 0-1 = 1
        if ((b0 & 4) == 0)
        {
            byte b1 = *ptr++;
            byte b2 = *ptr++;
            return (uint)((b0 >> 3) | (b1 << 5) | (b2 << 13));
        }

        // 4-byte encoding: bit 3 = 0, bits 0-2 = 1
        if ((b0 & 8) == 0)
        {
            byte b1 = *ptr++;
            byte b2 = *ptr++;
            byte b3 = *ptr++;
            return (uint)((b0 >> 4) | (b1 << 4) | (b2 << 12) | (b3 << 20));
        }

        // 5-byte encoding: all low nibble bits = 1, full 32-bit follows
        uint result = (uint)(*ptr++);
        result |= (uint)(*ptr++) << 8;
        result |= (uint)(*ptr++) << 16;
        result |= (uint)(*ptr++) << 24;
        return result;
    }

    /// <summary>
    /// Parse NativeAOT EH clause data and find a matching catch handler.
    ///
    /// NativeAOT ehinfo format (per CoffNativeCodeManager.cpp):
    /// The ehinfo data stream starts DIRECTLY with the clause count - NO flags byte here.
    /// (The flags byte is in the unwind block extension, which we handle in GetNativeAotEHInfo.)
    ///
    /// Format:
    /// - Clause count (NativeUnsigned)
    /// - For each clause:
    ///   - TryStartOffset (NativeUnsigned)
    ///   - (TryLength << 2) | ClauseKind (NativeUnsigned) - kind is low 2 bits
    ///   - HandlerOffset (NativeUnsigned) - for typed/fault/finally
    ///   - TypeRVA (32-bit relative) - for Typed clauses only
    ///   - FilterOffset (NativeUnsigned) - for Filter clauses only
    /// </summary>
    /// <param name="ehInfo">Pointer to EH info data</param>
    /// <param name="imageBase">Base address of the image</param>
    /// <param name="functionRva">RVA of the function start</param>
    /// <param name="offsetInFunction">Offset within function where exception occurred</param>
    /// <param name="exceptionTypeRva">RVA of the exception type (0 for any)</param>
    /// <param name="clause">Receives the matching clause if found</param>
    /// <param name="foundClauseIndex">Receives the index of the found clause</param>
    /// <param name="startClause">Start searching from this clause index (for rethrow)</param>
    /// <param name="framePointer">Frame pointer for filter evaluation (0 to skip filter clauses)</param>
    /// <param name="exceptionObject">Exception object for filter evaluation (0 to skip filter clauses)</param>
    /// <returns>True if a matching clause was found</returns>
    public static bool FindMatchingEHClause(
        byte* ehInfo,
        ulong imageBase,
        uint functionRva,
        uint offsetInFunction,
        ulong exceptionTypeRva,
        out NativeAotEHClause clause,
        out uint foundClauseIndex,
        uint startClause = 0,
        ulong framePointer = 0,
        ulong exceptionObject = 0)
    {
        clause = default;
        foundClauseIndex = 0;

        if (ehInfo == null)
            return false;

        byte* ptr = ehInfo;

        // Read clause count directly (NO flags byte - that's in the unwind block extension)
        uint clauseCount = ReadNativeUnsigned(ref ptr);

        // Sanity check clause count
        if (clauseCount > 100)
        {
            return false;
        }

        // Parse each clause looking for a match
        for (uint i = 0; i < clauseCount; i++)
        {
            // Read try start offset
            uint tryStart = ReadNativeUnsigned(ref ptr);

            // Read (tryLength << 2) | clauseKind
            uint tryEndDeltaAndKind = ReadNativeUnsigned(ref ptr);
            byte kind = (byte)(tryEndDeltaAndKind & 0x3);
            uint tryLength = tryEndDeltaAndKind >> 2;
            uint tryEnd = tryStart + tryLength;

            // Read handler offset (for typed/fault/finally)
            uint handlerOffset = 0;
            if (kind != (byte)EHClauseKind.Filter)
            {
                handlerOffset = ReadNativeUnsigned(ref ptr);
            }

            // Read type-specific data
            uint filterOffset = 0;
            ulong catchTypeMethodTable = 0;
            uint leaveTargetOffset = 0;

            if (kind == (byte)EHClauseKind.Typed)
            {
                // Read MethodTable pointer (8 bytes)
                catchTypeMethodTable = *(ulong*)ptr;
                ptr += 8;
            }
            else if (kind == (byte)EHClauseKind.Filter)
            {
                // For filter, read handler offset then filter offset
                handlerOffset = ReadNativeUnsigned(ref ptr);
                filterOffset = ReadNativeUnsigned(ref ptr);
            }

            // Read leave target offset for catch/filter handlers
            if (kind == (byte)EHClauseKind.Typed || kind == (byte)EHClauseKind.Filter)
            {
                leaveTargetOffset = ReadNativeUnsigned(ref ptr);
            }

            // Skip clauses before startClause (for rethrow)
            if (i < startClause)
            {
                continue;
            }

            // Check if the exception offset falls within this try region
            if (offsetInFunction >= tryStart && offsetInFunction < tryEnd)
            {
                // Only match catch (Typed) and Filter clauses for Pass 1
                // Finally/Fault handlers are executed in Pass 2
                if (kind == (byte)EHClauseKind.Typed)
                {
                    // Check if exception type is assignable to catch type
                    // If no catch type is specified (0), it catches all exceptions
                    bool typeMatches = false;
                    if (catchTypeMethodTable == 0)
                    {
                        // catch { } or catch (Exception) where type wasn't resolved
                        typeMatches = true;
                    }
                    else if (exceptionObject != 0)
                    {
                        // Get exception object's MethodTable (first field of any object)
                        MethodTable* exceptionMT = *(MethodTable**)exceptionObject;
                        MethodTable* catchMT = (MethodTable*)catchTypeMethodTable;

                        // Use IsAssignableTo to check if exception type is compatible with catch type
                        typeMatches = TypeHelpers.IsAssignableTo(exceptionMT, catchMT);
                    }

                    if (typeMatches)
                    {
                        clause.Kind = (EHClauseKind)kind;
                        clause.TryStartOffset = tryStart;
                        clause.TryEndOffset = tryEnd;
                        clause.HandlerOffset = handlerOffset;
                        clause.FilterOffset = filterOffset;
                        clause.CatchTypeMethodTable = catchTypeMethodTable;
                        clause.LeaveTargetOffset = leaveTargetOffset;
                        foundClauseIndex = i;
                        return true;
                    }
                    // Type doesn't match - continue to next clause
                }
                else if (kind == (byte)EHClauseKind.Filter)
                {
                    // Filter clause: need to execute the filter funclet to see if it handles the exception
                    // Skip if we don't have the required context for filter evaluation
                    if (framePointer == 0 || exceptionObject == 0)
                    {
                        continue;
                    }

                    // Calculate the filter funclet address
                    ulong filterAddr = imageBase + functionRva + filterOffset;

                    // Execute the filter funclet
                    bool filterResult = ExecuteFilterFunclet(filterAddr, framePointer, exceptionObject);

                    if (filterResult)
                    {
                        // Filter returned 1 (EXCEPTION_EXECUTE_HANDLER) - this clause handles the exception
                        clause.Kind = EHClauseKind.Filter;
                        clause.TryStartOffset = tryStart;
                        clause.TryEndOffset = tryEnd;
                        clause.HandlerOffset = handlerOffset;
                        clause.FilterOffset = filterOffset;
                        clause.CatchTypeMethodTable = 0;  // Filter clauses don't have a type
                        clause.LeaveTargetOffset = leaveTargetOffset;
                        foundClauseIndex = i;
                        return true;
                    }
                    // Filter returned 0 (EXCEPTION_CONTINUE_SEARCH) - continue looking
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Find all finally/fault clauses in a function that cover a given offset.
    /// Returns the number of finally/fault handlers found.
    /// </summary>
    /// <param name="ehInfo">Pointer to EH info data</param>
    /// <param name="offsetInFunction">Current offset within the function</param>
    /// <param name="clauses">Buffer to store found clauses (caller must provide space)</param>
    /// <param name="maxClauses">Maximum number of clauses the buffer can hold</param>
    /// <returns>Number of finally/fault clauses found</returns>
    public static int FindFinallyClausesInRange(
        byte* ehInfo,
        uint offsetInFunction,
        NativeAotEHClause* clauses,
        int maxClauses)
    {
        if (ehInfo == null || clauses == null || maxClauses <= 0)
            return 0;

        byte* ptr = ehInfo;
        uint clauseCount = ReadNativeUnsigned(ref ptr);

        if (clauseCount > 100)
            return 0;

        int foundCount = 0;

        for (uint i = 0; i < clauseCount && foundCount < maxClauses; i++)
        {
            uint tryStart = ReadNativeUnsigned(ref ptr);
            uint tryEndDeltaAndKind = ReadNativeUnsigned(ref ptr);
            byte kind = (byte)(tryEndDeltaAndKind & 0x3);
            uint tryLength = tryEndDeltaAndKind >> 2;
            uint tryEnd = tryStart + tryLength;

            uint handlerOffset = 0;
            if (kind != (byte)EHClauseKind.Filter)
            {
                handlerOffset = ReadNativeUnsigned(ref ptr);
            }

            // Skip type-specific data
            if (kind == (byte)EHClauseKind.Typed)
            {
                ptr += 8; // Skip MethodTable pointer (8 bytes)
                ReadNativeUnsigned(ref ptr); // Skip leave target offset
            }
            else if (kind == (byte)EHClauseKind.Filter)
            {
                ReadNativeUnsigned(ref ptr); // handler offset
                ReadNativeUnsigned(ref ptr); // filter offset
                ReadNativeUnsigned(ref ptr); // Skip leave target offset
            }

            // Check if this is a finally/fault that covers our offset
            if (offsetInFunction >= tryStart && offsetInFunction < tryEnd)
            {
                if (kind == (byte)EHClauseKind.Finally || kind == (byte)EHClauseKind.Fault)
                {
                    clauses[foundCount].Kind = (EHClauseKind)kind;
                    clauses[foundCount].TryStartOffset = tryStart;
                    clauses[foundCount].TryEndOffset = tryEnd;
                    clauses[foundCount].HandlerOffset = handlerOffset;
                    clauses[foundCount].FilterOffset = 0;
                    clauses[foundCount].CatchTypeMethodTable = 0;
                    foundCount++;
                }
            }
        }

        return foundCount;
    }

    /// <summary>
    /// Execute a finally/fault handler by calling it as a function.
    /// The handler is expected to end with 'endfinally' which does a ret.
    /// </summary>
    /// <param name="handlerAddress">Address of the finally handler code</param>
    /// <param name="framePointer">RBP value for the function containing the handler</param>
    private static void ExecuteFinallyHandler(ulong handlerAddress, ulong framePointer)
    {
        // Call the finally handler as a function
        // The handler expects RBP to be set up to access locals
        // endfinally does 'mov rsp, rbp; pop rbp; ret' so it will return here
        CallFinallyHandler(handlerAddress, framePointer);
    }

    /// <summary>
    /// Assembly helper to call a finally handler with proper frame setup.
    /// Implemented in native.asm
    /// </summary>
    [DllImport("*", EntryPoint = "call_finally_handler")]
    private static extern void CallFinallyHandler(ulong handlerAddress, ulong framePointer);

    /// <summary>
    /// Execute a filter funclet to evaluate a "catch when (condition)" expression.
    /// </summary>
    /// <param name="filterAddress">Address of the filter funclet code</param>
    /// <param name="framePointer">RBP value for the function containing the filter</param>
    /// <param name="exceptionObject">The exception object being filtered</param>
    /// <returns>true if filter returns 1 (handle), false if returns 0 (don't handle)</returns>
    private static bool ExecuteFilterFunclet(ulong filterAddress, ulong framePointer, ulong exceptionObject)
    {
        // Call the filter funclet
        // Returns 0 = don't handle, 1 = handle
        int result = CallFilterFunclet(filterAddress, framePointer, exceptionObject);
        return result == 1;
    }

    /// <summary>
    /// Assembly helper to call a filter funclet with proper frame setup.
    /// Implemented in native.asm
    /// </summary>
    /// <param name="filterAddress">Address of the filter funclet</param>
    /// <param name="framePointer">Parent frame pointer (passed in RDX for funclet prolog)</param>
    /// <param name="exceptionObject">Exception object (passed in RCX as first parameter)</param>
    /// <returns>Filter result: 0 = don't handle, 1 = handle</returns>
    [DllImport("*", EntryPoint = "call_filter_funclet")]
    private static extern int CallFilterFunclet(ulong filterAddress, ulong framePointer, ulong exceptionObject);

    /// <summary>
    /// Dispatch a .NET exception using NativeAOT EH tables.
    /// This performs the two-pass exception handling:
    /// Pass 1: Search for a matching catch handler
    /// Pass 2: Unwind stack and execute finally/fault handlers
    /// </summary>
    /// <param name="exceptionObject">The exception object being thrown</param>
    /// <param name="context">Context at the point of throw</param>
    /// <returns>True if exception was handled, false if unhandled</returns>
    public static bool DispatchNativeAotException(void* exceptionObject, ExceptionContext* context)
    {
        if (!_initialized) Init();

        // Clear any stale exception state from previous exceptions.
        // This is important when handling multiple sequential exceptions -
        // the TLS state from a previous catch must not interfere with new exceptions.
        ClearCurrentException();

        // Pass 1: Search for a handler
        ExceptionContext searchContext = *context;
        int maxFrames = 100;
        int frame = 0;

        while (frame < maxFrames && searchContext.Rip != 0)
        {
            ulong imageBase;
            var funcEntry = LookupFunctionEntry(searchContext.Rip, out imageBase);

            if (funcEntry == null)
            {
                // No unwind info - assume leaf function, just pop return address
                ulong newRip = *(ulong*)searchContext.Rsp;
                searchContext.Rip = newRip;
                searchContext.Rsp += 8;
                frame++;
                continue;
            }

            // Get UNWIND_INFO
            var unwindInfo = (UnwindInfo*)(imageBase + funcEntry->UnwindInfoAddress);

            // Check if this is a funclet (catch/finally handler) - if so, continue EH search
            // in the PARENT function, not in whoever called the funclet
            // Funclets don't have their own EH clauses, their parent function does
            bool isFunclet = IsFunclet(imageBase, unwindInfo);
            if (isFunclet)
            {
                // Get the parent frame pointer - with the simple funclet prolog
                // (push rbp; mov rbp, rdx), RBP IS the parent frame pointer
                ulong parentFramePtr = searchContext.Rbp;

                // Find the main (parent) function for this code region
                var mainFunc = LookupMainFunctionByBase(imageBase);
                if (mainFunc == null)
                {
                    // Fall back to unwinding
                    ulong funcletEstablisherFrame;
                    RtlVirtualUnwind(
                        UNW_HandlerType.UNW_FLAG_NHANDLER,
                        imageBase,
                        searchContext.Rip,
                        funcEntry,
                        &searchContext,
                        null,
                        &funcletEstablisherFrame,
                        null);
                    frame++;
                    continue;
                }

                // Get the parent function's unwind info
                var mainUnwindInfo = (UnwindInfo*)(imageBase + mainFunc->UnwindInfoAddress);
                byte* mainEhInfo = GetNativeAotEHInfo(imageBase, mainUnwindInfo);

                if (mainEhInfo != null)
                {
                    // The exception occurred while executing a catch/finally handler (funclet).
                    // Get the clause index this funclet belongs to.
                    int funcletClauseIdx = GetFuncletClauseIndex(unwindInfo);

                    // When an exception escapes from clause N's handler, search starting
                    // from clause N+1. This implements the correct nested try/catch semantics.
                    uint startSearchClause = (funcletClauseIdx >= 0) ? (uint)(funcletClauseIdx + 1) : 0;

                    // We need to use an offset that's inside the outer try block.
                    // For nested try/catch compiled with funclets, the try ranges in native
                    // code cover the main body code only. We use offset 0x2F (just after prolog)
                    // which should be inside all try blocks.
                    uint searchOffset = 0x2F;  // Standard prolog size for frame-based functions

                    NativeAotEHClause clause;
                    uint foundClauseIndex;
                    if (FindMatchingEHClause(mainEhInfo, imageBase, mainFunc->BeginAddress, searchOffset, 0,
                        out clause, out foundClauseIndex, startSearchClause, parentFramePtr, (ulong)exceptionObject))
                    {
                        // Found a handler in the parent function!
                        // Transfer to the handler
                        ulong handlerAddr = imageBase + mainFunc->BeginAddress + clause.HandlerOffset;

                        // Set up context for transfer to handler
                        // Use the ORIGINAL throw context's RSP (not searchContext which was corrupted by unwinding)
                        // The new funclet prolog (push rbp; mov rbp, rdx) needs room for push + ret addr
                        context->Rip = handlerAddr;
                        context->Rsp = context->Rsp - 16;  // Use original RSP from throw site, leave room for push and ret
                        context->Rbp = parentFramePtr;  // Keep parent frame for new funclet's RBP source
                        context->Rcx = (ulong)exceptionObject;
                        context->Rdx = parentFramePtr;  // Pass parent's frame pointer to new handler

                        // Write return address at RSP
                        ulong leaveTargetAddr = imageBase + mainFunc->BeginAddress + clause.LeaveTargetOffset;
                        *(ulong*)(context->Rsp) = leaveTargetAddr;

                        // Update exception tracking for nested rethrow
                        SetCurrentException(exceptionObject);
                        SetCurrentExceptionRip(searchContext.Rip);
                        SetCurrentHandlerClause(foundClauseIndex);

                        return true;
                    }
                }

                // No handler found in parent function - continue unwinding from the parent
                // Use parent's frame to find the return address and continue the search
                // Parent's return address is at [parentFramePtr + 8]
                searchContext.Rip = *(ulong*)(parentFramePtr + 8);
                searchContext.Rsp = parentFramePtr + 16;  // After the return address
                searchContext.Rbp = *(ulong*)parentFramePtr;  // Saved RBP from parent's prolog

                frame++;
                continue;
            }

            // Try to get NativeAOT EH info
            byte* ehInfo = GetNativeAotEHInfo(imageBase, unwindInfo);

            if (ehInfo != null)
            {
                // Calculate offset within function
                uint offsetInFunc = (uint)(searchContext.Rip - (imageBase + funcEntry->BeginAddress));

                // Look for matching catch clause (Pass 1 finds catch and filter clauses)
                // Pass frame pointer and exception object for filter funclet evaluation
                NativeAotEHClause clause;
                uint foundClauseIndex;
                if (FindMatchingEHClause(ehInfo, imageBase, funcEntry->BeginAddress, offsetInFunc, 0,
                    out clause, out foundClauseIndex, 0, searchContext.Rbp, (ulong)exceptionObject))
                {
                    // Found a catch handler!
                    // ========== Pass 2: Execute finally/fault handlers ==========
                    // Walk from throw site to catch frame, executing finally handlers

                    int catchFrame = frame;
                    ulong catchImageBase = imageBase;
                    RuntimeFunction* catchFuncEntry = funcEntry;
                    ExceptionContext catchSearchContext = searchContext;

                    // Start from throw site again
                    ExceptionContext pass2Context = *context;
                    int pass2Frame = 0;

                    while (pass2Frame <= catchFrame && pass2Context.Rip != 0)
                    {
                        ulong p2ImageBase;
                        var p2FuncEntry = LookupFunctionEntry(pass2Context.Rip, out p2ImageBase);

                        if (p2FuncEntry == null)
                        {
                            // Leaf function - no finally handlers possible
                            pass2Context.Rip = *(ulong*)pass2Context.Rsp;
                            pass2Context.Rsp += 8;
                            pass2Frame++;
                            continue;
                        }

                        var p2UnwindInfo = (UnwindInfo*)(p2ImageBase + p2FuncEntry->UnwindInfoAddress);

                        // Skip funclets
                        if (IsFunclet(p2ImageBase, p2UnwindInfo))
                        {
                            ulong funcletFrame;
                            RtlVirtualUnwind(UNW_HandlerType.UNW_FLAG_NHANDLER, p2ImageBase,
                                pass2Context.Rip, p2FuncEntry, &pass2Context, null, &funcletFrame, null);
                            pass2Frame++;
                            continue;
                        }

                        byte* p2EhInfo = GetNativeAotEHInfo(p2ImageBase, p2UnwindInfo);
                        if (p2EhInfo != null)
                        {
                            uint p2OffsetInFunc = (uint)(pass2Context.Rip - (p2ImageBase + p2FuncEntry->BeginAddress));

                            // Find all finally/fault clauses in this frame
                            NativeAotEHClause* finallyClauses = stackalloc NativeAotEHClause[16];
                            int finallyCount = FindFinallyClausesInRange(p2EhInfo, p2OffsetInFunc, finallyClauses, 16);

                            // Execute finally handlers (in order found - innermost first)
                            for (int i = 0; i < finallyCount; i++)
                            {
                                ulong finallyAddr = p2ImageBase + p2FuncEntry->BeginAddress + finallyClauses[i].HandlerOffset;
                                ExecuteFinallyHandler(finallyAddr, pass2Context.Rbp);
                            }
                        }

                        // Unwind to next frame
                        ulong estFrame;
                        RtlVirtualUnwind(UNW_HandlerType.UNW_FLAG_NHANDLER, p2ImageBase,
                            pass2Context.Rip, p2FuncEntry, &pass2Context, null, &estFrame, null);
                        pass2Frame++;
                    }

                    // ========== Transfer to catch handler ==========
                    ulong handlerAddr = catchImageBase + catchFuncEntry->BeginAddress + clause.HandlerOffset;

                    // Transfer control to handler
                    // The funclet prologue is: push rbp; mov rbp, rdx
                    // The funclet epilogue is: add rsp, 8; ret (preserves RBP for leave target)
                    //
                    // IMPORTANT: Use RBP (frame pointer) as the base for stack calculations,
                    // not the unwound RSP which may overlap with the interrupt frame.
                    // The funclet's stack should be at RBP - offset (within the function's frame).
                    //
                    // Stack layout at funclet entry (RSP = RBP - 0x100):
                    //   [RSP]   = leaveTargetAddr (return addr for ret)
                    //   [RSP-8] = space for push rbp
                    //
                    // After push rbp (RSP becomes RSP - 8):
                    //   [RSP]    = saved old RBP
                    //   [RSP+8]  = leaveTargetAddr
                    //
                    // After add rsp, 8 (RSP becomes original RSP):
                    //   [RSP]    = leaveTargetAddr
                    //
                    // After ret:
                    //   RIP = leaveTargetAddr (correct!)
                    //   RBP = parent frame pointer (unchanged, set by funclet's mov rbp, rdx)
                    //
                    // Use RBP - 0x100 as base to avoid overlap with:
                    // 1. The function's locals (typically at RBP - 0x40 to RBP - 0x80)
                    // 2. The interrupt frame (much lower on the stack)
                    ulong targetRsp = catchSearchContext.Rbp - 0x100;

                    context->Rip = handlerAddr;
                    context->Rsp = targetRsp - 8;  // Leave room for push rbp
                    context->Rbp = catchSearchContext.Rbp;  // Keep the frame pointer
                    context->Rcx = (ulong)exceptionObject;
                    context->Rdx = catchSearchContext.Rbp;  // Pass frame pointer

                    // Write the return address at RSP
                    ulong leaveTargetAddr = catchImageBase + catchFuncEntry->BeginAddress + clause.LeaveTargetOffset;
                    *(ulong*)(context->Rsp) = leaveTargetAddr;

                    // Set current exception info for rethrow support
                    SetCurrentException(exceptionObject);
                    SetCurrentExceptionRip(catchSearchContext.Rip);
                    SetCurrentExceptionRsp(catchSearchContext.Rsp);
                    SetCurrentExceptionRbp(catchSearchContext.Rbp);
                    SetCurrentHandlerClause(foundClauseIndex);

                    return true;
                }
            }

            // Unwind one frame for pass 1 search
            ulong establisherFrame;
            RtlVirtualUnwind(
                UNW_HandlerType.UNW_FLAG_NHANDLER,
                imageBase,
                searchContext.Rip,
                funcEntry,
                &searchContext,
                null,
                &establisherFrame,
                null);

            frame++;
        }

        // No handler found
        return false;
    }

    /// <summary>
    /// Dispatch a rethrown exception.
    /// Similar to DispatchNativeAotException but starts searching from a specific clause.
    /// </summary>
    /// <param name="exceptionObject">The exception object being rethrown</param>
    /// <param name="searchContext">Context with RIP at the original throw site</param>
    /// <param name="startClause">Clause index to start searching from (skip previous clauses)</param>
    /// <param name="actualContext">The actual context to modify for handler transfer</param>
    /// <returns>True if exception was handled, false if unhandled</returns>
    public static bool DispatchNativeAotExceptionForRethrow(
        void* exceptionObject,
        ExceptionContext* searchContext,
        uint startClause,
        ExceptionContext* actualContext)
    {
        if (!_initialized) Init();

        int maxFrames = 100;
        int frame = 0;
        ExceptionContext walkContext = *searchContext;

        while (frame < maxFrames && walkContext.Rip != 0)
        {
            ulong imageBase;
            var funcEntry = LookupFunctionEntry(walkContext.Rip, out imageBase);

            if (funcEntry == null)
            {
                walkContext.Rip = *(ulong*)walkContext.Rsp;
                walkContext.Rsp += 8;
                frame++;
                startClause = 0;  // Reset for subsequent frames
                continue;
            }

            var unwindInfo = (UnwindInfo*)(imageBase + funcEntry->UnwindInfoAddress);

            // Skip funclets
            bool isFunclet = IsFunclet(imageBase, unwindInfo);
            if (isFunclet)
            {
                ulong funcletEstablisherFrame;
                RtlVirtualUnwind(
                    UNW_HandlerType.UNW_FLAG_NHANDLER,
                    imageBase,
                    walkContext.Rip,
                    funcEntry,
                    &walkContext,
                    null,
                    &funcletEstablisherFrame,
                    null);
                frame++;
                startClause = 0;  // Reset for subsequent frames
                continue;
            }

            byte* ehInfo = GetNativeAotEHInfo(imageBase, unwindInfo);

            if (ehInfo != null)
            {
                uint offsetInFunc = (uint)(walkContext.Rip - (imageBase + funcEntry->BeginAddress));

                NativeAotEHClause clause;
                uint foundClauseIndex;
                // For frame 0, use startClause; for subsequent frames, search from 0
                uint searchFrom = (frame == 0) ? startClause : 0;
                // Pass frame pointer and exception object for filter funclet evaluation
                if (FindMatchingEHClause(ehInfo, imageBase, funcEntry->BeginAddress, offsetInFunc, 0,
                    out clause, out foundClauseIndex, searchFrom, walkContext.Rbp, (ulong)exceptionObject))
                {
                    ulong handlerAddr = imageBase + funcEntry->BeginAddress + clause.HandlerOffset;

                    // Update the actual context for transfer
                    // Use walkContext (unwound to parent frame) for frame info,
                    // not actualContext (which is the funclet's frame)
                    // The funclet prologue is: push rbp; mov rbp, rdx
                    // So we need RSP-16 to leave room for push AND return address at RSP+8
                    actualContext->Rip = handlerAddr;
                    actualContext->Rsp = walkContext.Rsp - 16;  // Leave room for push and return addr
                    actualContext->Rbp = walkContext.Rbp;  // Parent's frame pointer
                    actualContext->Rcx = (ulong)exceptionObject;
                    actualContext->Rdx = walkContext.Rbp;  // Pass parent's frame pointer to funclet

                    // Write return address at RSP+8 (where ret will find it after pop rbp)
                    // The return address is the leave target - where execution continues after the handler
                    ulong leaveTargetAddr = imageBase + funcEntry->BeginAddress + clause.LeaveTargetOffset;
                    *(ulong*)(actualContext->Rsp + 8) = leaveTargetAddr;

                    // Update exception tracking for nested rethrow
                    SetCurrentException(exceptionObject);
                    SetCurrentExceptionRip(walkContext.Rip);
                    SetCurrentHandlerClause(foundClauseIndex);

                    return true;
                }
            }

            // Unwind one frame
            ulong establisherFrame;
            RtlVirtualUnwind(
                UNW_HandlerType.UNW_FLAG_NHANDLER,
                imageBase,
                walkContext.Rip,
                funcEntry,
                &walkContext,
                null,
                &establisherFrame,
                null);

            frame++;
            startClause = 0;  // Reset for subsequent frames
        }

        // No handler found
        return false;
    }

    /// <summary>
    /// PAL-style virtual unwind that unwinds one frame.
    /// Simpler interface than RtlVirtualUnwind.
    /// </summary>
    /// <param name="context">Context to unwind (modified in place)</param>
    /// <param name="contextPointers">Optional - receives pointers to saved registers</param>
    /// <returns>True if unwound successfully, false if at end of stack</returns>
    public static bool VirtualUnwind(ExceptionContext* context, KNonvolatileContextPointers* contextPointers)
    {
        if (context == null || context->Rip == 0)
            return false;

        // Look up function entry for current RIP
        ulong imageBase;
        var functionEntry = LookupFunctionEntry(context->Rip, out imageBase);

        if (functionEntry != null)
        {
            // Have unwind info - use RtlVirtualUnwind
            ulong establisherFrame;
            RtlVirtualUnwind(
                UNW_HandlerType.UNW_FLAG_NHANDLER,
                imageBase,
                context->Rip,
                functionEntry,
                context,
                null,
                &establisherFrame,
                contextPointers);

            return context->Rip != 0;
        }
        else
        {
            // No unwind info - assume leaf function (no prolog)
            // Just pop return address
            if (context->Rsp == 0)
                return false;

            context->Rip = *(ulong*)context->Rsp;
            context->Rsp += 8;

            return context->Rip != 0;
        }
    }
}
