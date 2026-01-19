// ProtonOS kernel - DDK Thread Exports
// Exposes thread management operations to JIT-compiled drivers and tests.

using System.Runtime.InteropServices;
using ProtonOS.Threading;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for thread management.
/// </summary>
public static unsafe class ThreadExports
{
    /// <summary>
    /// Create a new thread.
    /// </summary>
    /// <param name="entryPoint">Thread entry point function pointer</param>
    /// <param name="parameter">Parameter passed to entry point</param>
    /// <param name="stackSize">Stack size in bytes (0 = default 64KB)</param>
    /// <param name="flags">Creation flags (4 = CREATE_SUSPENDED)</param>
    /// <param name="threadId">Output: assigned thread ID</param>
    /// <returns>Thread handle (pointer), or null on failure</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_CreateThread")]
    public static Thread* CreateThread(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        uint* threadId)
    {
        uint id;
        var thread = Scheduler.CreateThread(entryPoint, parameter, stackSize, flags, out id);
        if (threadId != null)
            *threadId = id;
        return thread;
    }

    /// <summary>
    /// Exit the current thread with the specified exit code.
    /// This function does not return.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_ExitThread")]
    public static void ExitThread(uint exitCode)
    {
        Scheduler.ExitThread(exitCode);
    }

    /// <summary>
    /// Get the current thread ID.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCurrentThreadId")]
    public static uint GetCurrentThreadId()
    {
        return Scheduler.GetCurrentThreadId();
    }

    /// <summary>
    /// Get the current thread handle (pointer).
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetCurrentThread")]
    public static Thread* GetCurrentThread()
    {
        return Scheduler.CurrentThread;
    }

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_Sleep")]
    public static void Sleep(uint milliseconds)
    {
        Scheduler.Sleep(milliseconds);
    }

    /// <summary>
    /// Yield execution to another ready thread.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_Yield")]
    public static void Yield()
    {
        Scheduler.Yield();
    }

    /// <summary>
    /// Get the exit code of a thread.
    /// </summary>
    /// <param name="thread">Thread pointer</param>
    /// <param name="exitCode">Output: exit code (259 = STILL_ACTIVE)</param>
    /// <returns>true if successful</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetExitCodeThread")]
    public static bool GetExitCodeThread(Thread* thread, uint* exitCode)
    {
        if (thread == null || exitCode == null)
            return false;

        if (thread->State != ThreadState.Terminated)
        {
            *exitCode = 259; // STILL_ACTIVE
            return true;
        }

        *exitCode = thread->ExitCode;
        return true;
    }

    /// <summary>
    /// Get the state of a thread.
    /// </summary>
    /// <param name="thread">Thread pointer</param>
    /// <returns>Thread state (0=Created, 1=Ready, 2=Running, 3=Blocked, 4=Suspended, 5=Terminated)</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetThreadState")]
    public static int GetThreadState(Thread* thread)
    {
        if (thread == null)
            return -1;
        return (int)thread->State;
    }

    /// <summary>
    /// Suspend a thread.
    /// </summary>
    /// <param name="thread">Thread pointer</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_SuspendThread")]
    public static int SuspendThread(Thread* thread)
    {
        if (thread == null)
            return -1;
        return Scheduler.SuspendThread(thread);
    }

    /// <summary>
    /// Resume a suspended thread.
    /// </summary>
    /// <param name="thread">Thread pointer</param>
    /// <returns>Previous suspend count, or -1 on error</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_ResumeThread")]
    public static int ResumeThread(Thread* thread)
    {
        if (thread == null)
            return -1;
        return Scheduler.ResumeThread(thread);
    }

    /// <summary>
    /// Get the total number of active threads.
    /// </summary>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetThreadCount")]
    public static int GetThreadCount()
    {
        return Scheduler.ThreadCount;
    }

    /// <summary>
    /// Get scheduler statistics.
    /// </summary>
    /// <param name="running">Output: number of running/ready threads</param>
    /// <param name="blocked">Output: number of blocked threads</param>
    /// <param name="contextSwitches">Output: total context switches across all CPUs</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_GetSchedulerStats")]
    public static void GetSchedulerStats(int* running, int* blocked, ulong* contextSwitches)
    {
        int runningCount = 0;
        int blockedCount = 0;
        ulong ctxSwitches = 0;

        // Get context switch count from all CPUs
        if (PerCpu.IsInitialized)
        {
            for (int i = 0; i < PerCpu.CpuCount; i++)
            {
                var perCpu = PerCpu.GetCpu(i);
                if (perCpu != null)
                    ctxSwitches += perCpu->ContextSwitchCount;
            }
        }

        // Count threads by state
        if (Scheduler.IsInitialized)
        {
            Scheduler.GlobalLock.Acquire();
            try
            {
                var thread = Scheduler.AllThreadsHead;
                while (thread != null)
                {
                    switch (thread->State)
                    {
                        case ThreadState.Running:
                        case ThreadState.Ready:
                            runningCount++;
                            break;
                        case ThreadState.Blocked:
                            blockedCount++;
                            break;
                    }
                    thread = thread->NextAll;
                }
            }
            finally
            {
                Scheduler.GlobalLock.Release();
            }
        }

        if (running != null)
            *running = runningCount;
        if (blocked != null)
            *blocked = blockedCount;
        if (contextSwitches != null)
            *contextSwitches = ctxSwitches;
    }
}
