// ProtonOS DDK - Thread Management API
// Provides thread operations for drivers and JIT code.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// Thread states matching kernel ThreadState enum.
/// </summary>
public enum ThreadState
{
    Created = 0,
    Ready = 1,
    Running = 2,
    Blocked = 3,
    Suspended = 4,
    Terminated = 5
}

/// <summary>
/// Provides thread management operations for drivers and JIT code.
/// </summary>
public static unsafe class Thread
{
    /// <summary>
    /// Create suspended flag (thread starts suspended).
    /// </summary>
    public const uint CREATE_SUSPENDED = 4;

    /// <summary>
    /// Exit code indicating thread is still active.
    /// </summary>
    public const uint STILL_ACTIVE = 259;

    [DllImport("*", EntryPoint = "Kernel_CreateThread")]
    private static extern void* Kernel_CreateThread(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        uint* threadId);

    [DllImport("*", EntryPoint = "Kernel_ExitThread")]
    private static extern void Kernel_ExitThread(uint exitCode);

    [DllImport("*", EntryPoint = "Kernel_GetCurrentThreadId")]
    private static extern uint Kernel_GetCurrentThreadId();

    [DllImport("*", EntryPoint = "Kernel_GetCurrentThread")]
    private static extern void* Kernel_GetCurrentThread();

    [DllImport("*", EntryPoint = "Kernel_Sleep")]
    private static extern void Kernel_Sleep(uint milliseconds);

    [DllImport("*", EntryPoint = "Kernel_Yield")]
    private static extern void Kernel_Yield();

    [DllImport("*", EntryPoint = "Kernel_GetExitCodeThread")]
    private static extern bool Kernel_GetExitCodeThread(void* thread, uint* exitCode);

    [DllImport("*", EntryPoint = "Kernel_GetThreadState")]
    private static extern int Kernel_GetThreadState(void* thread);

    [DllImport("*", EntryPoint = "Kernel_SuspendThread")]
    private static extern int Kernel_SuspendThread(void* thread);

    [DllImport("*", EntryPoint = "Kernel_ResumeThread")]
    private static extern int Kernel_ResumeThread(void* thread);

    [DllImport("*", EntryPoint = "Kernel_GetThreadCount")]
    private static extern int Kernel_GetThreadCount();

    [DllImport("*", EntryPoint = "Kernel_GetSchedulerStats")]
    private static extern void Kernel_GetSchedulerStats(int* running, int* blocked, ulong* contextSwitches);

    /// <summary>
    /// Create a new thread.
    /// </summary>
    /// <param name="entryPoint">Thread entry point function pointer.</param>
    /// <param name="parameter">Parameter passed to entry point.</param>
    /// <param name="stackSize">Stack size in bytes (0 = default 64KB).</param>
    /// <param name="flags">Creation flags (CREATE_SUSPENDED = 4).</param>
    /// <param name="threadId">Output: assigned thread ID.</param>
    /// <returns>Thread handle pointer, or null on failure.</returns>
    public static void* CreateThread(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        out uint threadId)
    {
        fixed (uint* idPtr = &threadId)
        {
            return Kernel_CreateThread(entryPoint, parameter, stackSize, flags, idPtr);
        }
    }

    /// <summary>
    /// Exit the current thread with the specified exit code.
    /// This function does not return.
    /// </summary>
    public static void ExitThread(uint exitCode) => Kernel_ExitThread(exitCode);

    /// <summary>
    /// Get the current thread ID.
    /// </summary>
    public static uint GetCurrentThreadId() => Kernel_GetCurrentThreadId();

    /// <summary>
    /// Get the current thread handle.
    /// </summary>
    public static void* GetCurrentThread() => Kernel_GetCurrentThread();

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    public static void Sleep(uint milliseconds) => Kernel_Sleep(milliseconds);

    /// <summary>
    /// Yield execution to another ready thread.
    /// </summary>
    public static void Yield() => Kernel_Yield();

    /// <summary>
    /// Get the exit code of a thread.
    /// </summary>
    /// <param name="thread">Thread handle.</param>
    /// <param name="exitCode">Output: exit code (STILL_ACTIVE if still running).</param>
    /// <returns>true if successful.</returns>
    public static bool GetExitCodeThread(void* thread, out uint exitCode)
    {
        fixed (uint* codePtr = &exitCode)
        {
            return Kernel_GetExitCodeThread(thread, codePtr);
        }
    }

    /// <summary>
    /// Get the state of a thread.
    /// </summary>
    public static ThreadState GetThreadState(void* thread) => (ThreadState)Kernel_GetThreadState(thread);

    /// <summary>
    /// Suspend a thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error.</returns>
    public static int SuspendThread(void* thread) => Kernel_SuspendThread(thread);

    /// <summary>
    /// Resume a suspended thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error.</returns>
    public static int ResumeThread(void* thread) => Kernel_ResumeThread(thread);

    /// <summary>
    /// Get the total number of active threads.
    /// </summary>
    public static int GetThreadCount() => Kernel_GetThreadCount();

    /// <summary>
    /// Get scheduler statistics.
    /// </summary>
    /// <param name="running">Number of running/ready threads.</param>
    /// <param name="blocked">Number of blocked threads.</param>
    /// <param name="contextSwitches">Total context switches across all CPUs.</param>
    public static void GetSchedulerStats(out int running, out int blocked, out ulong contextSwitches)
    {
        fixed (int* runPtr = &running)
        fixed (int* blkPtr = &blocked)
        fixed (ulong* ctxPtr = &contextSwitches)
        {
            Kernel_GetSchedulerStats(runPtr, blkPtr, ctxPtr);
        }
    }
}
