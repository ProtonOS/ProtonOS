// netos mernel - PAL Thread APIs
// Win32-compatible thread management APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.Threading;
using Kernel.X64;

namespace Kernel.PAL;

/// <summary>
/// Context flags for GetThreadContext/SetThreadContext.
/// These control which parts of the CONTEXT structure are read/written.
/// </summary>
public static class ContextFlags
{
    public const uint CONTEXT_AMD64 = 0x00100000;
    public const uint CONTEXT_CONTROL = CONTEXT_AMD64 | 0x0001;  // SS:RSP, CS:RIP, RFLAGS, RBP
    public const uint CONTEXT_INTEGER = CONTEXT_AMD64 | 0x0002;  // RAX-R15
    public const uint CONTEXT_SEGMENTS = CONTEXT_AMD64 | 0x0004; // DS, ES, FS, GS
    public const uint CONTEXT_FLOATING_POINT = CONTEXT_AMD64 | 0x0008;
    public const uint CONTEXT_DEBUG_REGISTERS = CONTEXT_AMD64 | 0x0010;
    public const uint CONTEXT_FULL = CONTEXT_CONTROL | CONTEXT_INTEGER | CONTEXT_FLOATING_POINT;
    public const uint CONTEXT_ALL = CONTEXT_FULL | CONTEXT_SEGMENTS | CONTEXT_DEBUG_REGISTERS;
}

/// <summary>
/// x64 CONTEXT structure for Get/SetThreadContext.
/// This is a simplified version that includes the registers needed for stack walking and SEH.
/// Full Win32 CONTEXT includes XMM registers, debug registers, etc.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Context
{
    // Context control flags - specifies which registers are valid
    public uint ContextFlags;

    // Segment registers (CONTEXT_SEGMENTS)
    public ushort SegCs;
    public ushort SegDs;
    public ushort SegEs;
    public ushort SegFs;
    public ushort SegGs;
    public ushort SegSs;

    // Control registers (CONTEXT_CONTROL)
    public ulong Rip;
    public ulong Rsp;
    public ulong Rbp;
    public ulong EFlags;

    // Integer registers (CONTEXT_INTEGER)
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
}

/// <summary>
/// Thread creation flags.
/// </summary>
public static class ThreadCreationFlags
{
    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint STACK_SIZE_PARAM_IS_A_RESERVATION = 0x00010000;
}

/// <summary>
/// Thread handle - opaque wrapper around kernel Thread*.
/// In Win32, handles are opaque values, but we use the thread pointer directly.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ThreadHandle
{
    public Thread* Thread;

    public bool IsValid => Thread != null;
    public static ThreadHandle Invalid => default;
}

/// <summary>
/// PAL Thread APIs - Win32-compatible thread management functions.
/// These are thin wrappers over kernel Scheduler services.
/// </summary>
public static unsafe class ThreadApi
{
    /// <summary>
    /// Create a new thread.
    /// </summary>
    /// <param name="lpThreadAttributes">Security attributes (ignored)</param>
    /// <param name="dwStackSize">Stack size in bytes (0 = default)</param>
    /// <param name="lpStartAddress">Thread entry point</param>
    /// <param name="lpParameter">Parameter passed to thread</param>
    /// <param name="dwCreationFlags">Creation flags (CREATE_SUSPENDED, etc.)</param>
    /// <param name="lpThreadId">Output: thread ID</param>
    /// <returns>Thread handle, or Invalid on failure</returns>
    public static ThreadHandle CreateThread(
        void* lpThreadAttributes,
        nuint dwStackSize,
        delegate* unmanaged<void*, uint> lpStartAddress,
        void* lpParameter,
        uint dwCreationFlags,
        out uint lpThreadId)
    {
        lpThreadId = 0;

        if (lpStartAddress == null)
            return ThreadHandle.Invalid;

        // Map Win32 flags to kernel flags
        uint kernelFlags = 0;
        if ((dwCreationFlags & ThreadCreationFlags.CREATE_SUSPENDED) != 0)
            kernelFlags |= ThreadFlags.CreateSuspended;

        var thread = Scheduler.CreateThread(
            lpStartAddress,
            lpParameter,
            dwStackSize,
            kernelFlags,
            out lpThreadId);

        return new ThreadHandle { Thread = thread };
    }

    /// <summary>
    /// Get the current thread ID.
    /// </summary>
    public static uint GetCurrentThreadId()
    {
        return Scheduler.GetCurrentThreadId();
    }

    /// <summary>
    /// Get a pseudo-handle to the current thread.
    /// Note: This returns the actual thread pointer, not a pseudo-handle.
    /// </summary>
    public static ThreadHandle GetCurrentThread()
    {
        return new ThreadHandle { Thread = Scheduler.CurrentThread };
    }

    /// <summary>
    /// Exit the current thread with the specified exit code.
    /// This function does not return.
    /// </summary>
    public static void ExitThread(uint dwExitCode)
    {
        Scheduler.ExitThread(dwExitCode);
    }

    /// <summary>
    /// Terminate a thread.
    /// WARNING: This is dangerous and should be avoided. Use with caution.
    /// </summary>
    public static bool TerminateThread(ThreadHandle hThread, uint dwExitCode)
    {
        if (!hThread.IsValid)
            return false;

        // TODO: Implement thread termination in scheduler
        // For now, this is a stub - forceful termination is complex
        return false;
    }

    /// <summary>
    /// Suspend a thread.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int SuspendThread(ThreadHandle hThread)
    {
        if (!hThread.IsValid)
            return -1;

        return Scheduler.SuspendThread(hThread.Thread);
    }

    /// <summary>
    /// Resume a suspended thread.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int ResumeThread(ThreadHandle hThread)
    {
        if (!hThread.IsValid)
            return -1;

        return Scheduler.ResumeThread(hThread.Thread);
    }

    /// <summary>
    /// Get the exit code of a thread.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <param name="lpExitCode">Output: exit code</param>
    /// <returns>True if successful</returns>
    public static bool GetExitCodeThread(ThreadHandle hThread, out uint lpExitCode)
    {
        lpExitCode = 0;

        if (!hThread.IsValid)
            return false;

        // Check if thread is still running
        if (hThread.Thread->State != ThreadState.Terminated)
        {
            lpExitCode = 259; // STILL_ACTIVE
            return true;
        }

        lpExitCode = hThread.Thread->ExitCode;
        return true;
    }

    /// <summary>
    /// Set thread priority.
    /// </summary>
    public static bool SetThreadPriority(ThreadHandle hThread, int nPriority)
    {
        if (!hThread.IsValid)
            return false;

        // Map Win32 priority to kernel priority
        // Win32: -15 (IDLE) to +15 (TIME_CRITICAL), 0 = NORMAL
        int priority;
        if (nPriority <= -2)
            priority = ThreadPriority.Lowest;
        else if (nPriority >= 2)
            priority = ThreadPriority.Highest;
        else
            priority = ThreadPriority.Normal;

        hThread.Thread->Priority = priority;
        return true;
    }

    /// <summary>
    /// Get thread priority.
    /// </summary>
    public static int GetThreadPriority(ThreadHandle hThread)
    {
        if (!hThread.IsValid)
            return 0x7FFFFFFF; // THREAD_PRIORITY_ERROR_RETURN

        // Thread.Priority is already an int, just return it directly
        // It maps to ThreadPriority constants (Lowest=-2, Normal=0, Highest=2, etc.)
        return hThread.Thread->Priority;
    }

    /// <summary>
    /// Yield execution to another thread.
    /// Returns true if another thread was scheduled.
    /// </summary>
    public static bool SwitchToThread()
    {
        Scheduler.Yield();
        return true;
    }

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    public static void Sleep(uint dwMilliseconds)
    {
        if (dwMilliseconds == 0)
        {
            // Sleep(0) yields to other threads
            Scheduler.Yield();
        }
        else
        {
            Scheduler.Sleep(dwMilliseconds);
        }
    }

    /// <summary>
    /// Sleep for the specified number of milliseconds with alertable option.
    /// If bAlertable is true, the function can return early if an APC is queued.
    /// </summary>
    /// <param name="dwMilliseconds">Time to sleep in milliseconds. Use INFINITE (0xFFFFFFFF) for infinite wait.</param>
    /// <param name="bAlertable">If true, function returns when an APC is queued to the thread.</param>
    /// <returns>0 if timeout elapsed, WAIT_IO_COMPLETION (0xC0) if returned due to APC.</returns>
    public static uint SleepEx(uint dwMilliseconds, bool bAlertable)
    {
        if (dwMilliseconds == 0)
        {
            // SleepEx(0, true) should still check for APCs
            if (bAlertable && Scheduler.HasPendingApc(Scheduler.CurrentThread))
            {
                Scheduler.DeliverApcs();
                return WaitResult.IoCompletion;
            }
            Scheduler.Yield();
            return 0;
        }

        return Scheduler.SleepEx(dwMilliseconds, bAlertable);
    }

    /// <summary>
    /// Queue an asynchronous procedure call (APC) to a thread.
    /// The APC function will be called when the thread enters an alertable wait state.
    /// </summary>
    /// <param name="pfnAPC">Pointer to the APC function. Signature: void ApcProc(ULONG_PTR dwParam)</param>
    /// <param name="hThread">Handle to the thread to queue the APC to</param>
    /// <param name="dwData">Parameter to pass to the APC function</param>
    /// <returns>True if the APC was queued successfully</returns>
    public static bool QueueUserAPC(delegate* unmanaged<nuint, void> pfnAPC, ThreadHandle hThread, nuint dwData)
    {
        if (pfnAPC == null || !hThread.IsValid)
            return false;

        return Scheduler.QueueApc(hThread.Thread, pfnAPC, dwData);
    }

    /// <summary>
    /// Close a thread handle.
    /// In our implementation, handles don't need explicit cleanup,
    /// but this is provided for API compatibility.
    /// </summary>
    public static bool CloseHandle(ThreadHandle hThread)
    {
        // No-op in our implementation
        return hThread.IsValid;
    }

    /// <summary>
    /// Flush the instruction cache for a range of addresses.
    /// Critical for JIT compilation.
    /// </summary>
    /// <param name="hProcess">Process handle (ignored - we only have one process)</param>
    /// <param name="lpBaseAddress">Start address</param>
    /// <param name="dwSize">Size in bytes</param>
    /// <returns>Always returns true on x64</returns>
    public static bool FlushInstructionCache(void* hProcess, void* lpBaseAddress, nuint dwSize)
    {
        // On x64, the CPU ensures cache coherency between instruction and data caches
        // for self-modifying code. However, we still need a memory barrier to ensure
        // all writes are visible before we start executing the code.
        Cpu.MemoryBarrier();

        // On some x64 implementations, we might need to serialize execution.
        // A full serialization can be done with cpuid, but mfence is usually sufficient
        // for JIT scenarios since we're not running on the same core immediately.

        return true;
    }

    /// <summary>
    /// Get the context (register state) of a thread.
    /// The thread should be suspended before calling this.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <param name="lpContext">Pointer to CONTEXT structure to receive the context.
    /// ContextFlags field must be set to indicate which parts to retrieve.</param>
    /// <returns>True on success</returns>
    public static bool GetThreadContext(ThreadHandle hThread, Context* lpContext)
    {
        if (!hThread.IsValid || lpContext == null)
            return false;

        var thread = hThread.Thread;
        uint flags = lpContext->ContextFlags;

        // For the current running thread, we can't get accurate context
        // (the registers are in use). Thread should be suspended.
        if (thread == Scheduler.CurrentThread && thread->State == ThreadState.Running)
        {
            // Return what we can from saved context, but it may be stale
        }

        // Copy from thread's saved CpuContext based on requested flags
        ref CpuContext ctx = ref thread->Context;

        if ((flags & ContextFlags.CONTEXT_CONTROL) != 0)
        {
            lpContext->Rip = ctx.Rip;
            lpContext->Rsp = ctx.Rsp;
            lpContext->Rbp = ctx.Rbp;
            lpContext->EFlags = ctx.Rflags;
            lpContext->SegCs = (ushort)ctx.Cs;
            lpContext->SegSs = (ushort)ctx.Ss;
        }

        if ((flags & ContextFlags.CONTEXT_INTEGER) != 0)
        {
            lpContext->Rax = ctx.Rax;
            lpContext->Rbx = ctx.Rbx;
            lpContext->Rcx = ctx.Rcx;
            lpContext->Rdx = ctx.Rdx;
            lpContext->Rsi = ctx.Rsi;
            lpContext->Rdi = ctx.Rdi;
            lpContext->R8 = ctx.R8;
            lpContext->R9 = ctx.R9;
            lpContext->R10 = ctx.R10;
            lpContext->R11 = ctx.R11;
            lpContext->R12 = ctx.R12;
            lpContext->R13 = ctx.R13;
            lpContext->R14 = ctx.R14;
            lpContext->R15 = ctx.R15;
        }

        if ((flags & ContextFlags.CONTEXT_SEGMENTS) != 0)
        {
            // We don't track DS/ES/FS/GS separately, use defaults
            lpContext->SegDs = 0x10;  // Kernel data segment
            lpContext->SegEs = 0x10;
            lpContext->SegFs = 0;
            lpContext->SegGs = 0;
        }

        return true;
    }

    /// <summary>
    /// Set the context (register state) of a thread.
    /// The thread should be suspended before calling this.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <param name="lpContext">Pointer to CONTEXT structure with new values.
    /// ContextFlags field indicates which parts to set.</param>
    /// <returns>True on success</returns>
    public static bool SetThreadContext(ThreadHandle hThread, Context* lpContext)
    {
        if (!hThread.IsValid || lpContext == null)
            return false;

        var thread = hThread.Thread;
        uint flags = lpContext->ContextFlags;

        // Cannot set context of currently running thread
        if (thread == Scheduler.CurrentThread && thread->State == ThreadState.Running)
            return false;

        // Copy to thread's saved CpuContext based on requested flags
        ref CpuContext ctx = ref thread->Context;

        if ((flags & ContextFlags.CONTEXT_CONTROL) != 0)
        {
            ctx.Rip = lpContext->Rip;
            ctx.Rsp = lpContext->Rsp;
            ctx.Rbp = lpContext->Rbp;
            ctx.Rflags = lpContext->EFlags;
            ctx.Cs = lpContext->SegCs;
            ctx.Ss = lpContext->SegSs;
        }

        if ((flags & ContextFlags.CONTEXT_INTEGER) != 0)
        {
            ctx.Rax = lpContext->Rax;
            ctx.Rbx = lpContext->Rbx;
            ctx.Rcx = lpContext->Rcx;
            ctx.Rdx = lpContext->Rdx;
            ctx.Rsi = lpContext->Rsi;
            ctx.Rdi = lpContext->Rdi;
            ctx.R8 = lpContext->R8;
            ctx.R9 = lpContext->R9;
            ctx.R10 = lpContext->R10;
            ctx.R11 = lpContext->R11;
            ctx.R12 = lpContext->R12;
            ctx.R13 = lpContext->R13;
            ctx.R14 = lpContext->R14;
            ctx.R15 = lpContext->R15;
        }

        return true;
    }

    /// <summary>
    /// Capture the current context (RtlCaptureContext equivalent).
    /// Saves the current CPU registers into the provided CONTEXT structure.
    /// </summary>
    /// <param name="lpContext">Pointer to CONTEXT structure to receive current context</param>
    public static void RtlCaptureContext(Context* lpContext)
    {
        if (lpContext == null)
            return;

        var thread = Scheduler.CurrentThread;
        if (thread == null)
            return;

        // Set flags to indicate what we captured
        lpContext->ContextFlags = ContextFlags.CONTEXT_FULL;

        // For the current thread, we get the saved context from the last context switch
        // This won't be perfectly accurate for the current running thread, but it's
        // what we have available without inline assembly
        ref CpuContext ctx = ref thread->Context;

        lpContext->Rip = ctx.Rip;
        lpContext->Rsp = ctx.Rsp;
        lpContext->Rbp = ctx.Rbp;
        lpContext->EFlags = ctx.Rflags;
        lpContext->SegCs = (ushort)ctx.Cs;
        lpContext->SegSs = (ushort)ctx.Ss;

        lpContext->Rax = ctx.Rax;
        lpContext->Rbx = ctx.Rbx;
        lpContext->Rcx = ctx.Rcx;
        lpContext->Rdx = ctx.Rdx;
        lpContext->Rsi = ctx.Rsi;
        lpContext->Rdi = ctx.Rdi;
        lpContext->R8 = ctx.R8;
        lpContext->R9 = ctx.R9;
        lpContext->R10 = ctx.R10;
        lpContext->R11 = ctx.R11;
        lpContext->R12 = ctx.R12;
        lpContext->R13 = ctx.R13;
        lpContext->R14 = ctx.R14;
        lpContext->R15 = ctx.R15;

        lpContext->SegDs = 0x10;
        lpContext->SegEs = 0x10;
        lpContext->SegFs = 0;
        lpContext->SegGs = 0;
    }

    /// <summary>
    /// Restore execution to the context specified in the CONTEXT structure.
    /// This function does not return - execution continues at Context.Rip.
    /// Used for exception unwinding and continuation.
    /// </summary>
    /// <param name="lpContext">Pointer to CONTEXT structure with target state</param>
    /// <param name="lpExceptionRecord">Optional exception record (unused, for API compatibility)</param>
    public static void RtlRestoreContext(Context* lpContext, void* lpExceptionRecord)
    {
        if (lpContext == null)
            return;

        // The restore_pal_context function in assembly does not return
        // It loads all registers from the Context and jumps to Rip
        Cpu.RestorePalContext(lpContext);

        // This code is never reached
    }

    /// <summary>
    /// Set the description (name) of a thread.
    /// This is used for debugging and diagnostic purposes.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <param name="lpThreadDescription">Description string (null-terminated wide string)</param>
    /// <returns>HRESULT (S_OK on success)</returns>
    public static int SetThreadDescription(ThreadHandle hThread, char* lpThreadDescription)
    {
        // Thread descriptions are not implemented in netos
        // This is a stub that always succeeds
        // In a full implementation, we would store this in the Thread structure
        return 0;  // S_OK
    }

    /// <summary>
    /// Get the description (name) of a thread.
    /// </summary>
    /// <param name="hThread">Thread handle</param>
    /// <param name="ppszThreadDescription">Receives pointer to description string</param>
    /// <returns>HRESULT (S_OK on success)</returns>
    public static int GetThreadDescription(ThreadHandle hThread, char** ppszThreadDescription)
    {
        // Return empty string (not implemented)
        if (ppszThreadDescription != null)
            *ppszThreadDescription = null;
        return 0;  // S_OK
    }

    /// <summary>
    /// Enhanced version of QueueUserAPC with additional flags.
    /// Windows 10+ API that supports special APC flags.
    /// </summary>
    /// <param name="pfnAPC">Pointer to the APC function</param>
    /// <param name="hThread">Handle to the thread</param>
    /// <param name="dwData">Parameter to pass to APC function</param>
    /// <param name="dwFlags">APC flags (QUEUE_USER_APC_FLAGS_*)</param>
    /// <returns>True if APC was queued successfully</returns>
    public static bool QueueUserAPC2(
        delegate* unmanaged<nuint, void> pfnAPC,
        ThreadHandle hThread,
        nuint dwData,
        uint dwFlags)
    {
        // For now, ignore the flags and use regular QueueUserAPC
        // QUEUE_USER_APC_FLAGS_SPECIAL_USER_APC (0x1) could be handled differently
        return QueueUserAPC(pfnAPC, hThread, dwData);
    }
}

/// <summary>
/// QueueUserAPC2 flags.
/// </summary>
public static class QueueUserApcFlags
{
    public const uint QUEUE_USER_APC_FLAGS_NONE = 0;
    public const uint QUEUE_USER_APC_FLAGS_SPECIAL_USER_APC = 1;
}

/// <summary>
/// Thread flags for kernel Scheduler.
/// Must match Kernel.Threading.ThreadFlags values.
/// </summary>
internal static class ThreadFlags
{
    public const uint CreateSuspended = 0x00000004;  // Must match Kernel.Threading.ThreadFlags.CreateSuspended
}
