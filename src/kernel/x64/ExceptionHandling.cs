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
