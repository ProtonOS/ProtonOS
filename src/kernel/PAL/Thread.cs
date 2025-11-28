// netos mernel - PAL Thread APIs
// Win32-compatible thread management APIs for PAL compatibility.

using System.Runtime.InteropServices;
using Kernel.Threading;
using Kernel.X64;

namespace Kernel.PAL;

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

        // TODO: Implement in scheduler
        // For now, return error
        return -1;
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

        // TODO: Implement in scheduler
        // For now, return error
        return -1;
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
}

/// <summary>
/// Thread flags for kernel Scheduler.
/// </summary>
internal static class ThreadFlags
{
    public const uint CreateSuspended = 0x00000001;
}
