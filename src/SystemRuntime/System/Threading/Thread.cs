// System.Threading.Thread - Thread management for JIT-compiled code
// Provides wrappers around kernel thread exports.

using System.Runtime.InteropServices;

namespace System.Threading;

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
/// Thread creation flags.
/// </summary>
public static class ThreadCreationFlags
{
    public const uint None = 0;
    public const uint CreateSuspended = 4;
}

/// <summary>
/// Thread handle representing a kernel thread.
/// </summary>
public unsafe struct ThreadHandle
{
    private void* _ptr;

    public bool IsValid => _ptr != null;
    public void* Pointer => _ptr;

    internal ThreadHandle(void* ptr) => _ptr = ptr;

    public static ThreadHandle Invalid => default;
}

/// <summary>
/// Provides low-level thread management operations.
/// Named KernelThread to avoid conflict with .NET's System.Threading.Thread.
/// </summary>
public static unsafe class KernelThread
{
    /// <summary>
    /// Thread entry point delegate type.
    /// </summary>
    public delegate uint ThreadStartRoutine(void* parameter);

    /// <summary>
    /// Create a new thread.
    /// </summary>
    /// <param name="entryPoint">Thread entry point (must be an unmanaged function pointer)</param>
    /// <param name="parameter">Parameter passed to thread entry point</param>
    /// <param name="stackSize">Stack size in bytes (0 = default 64KB)</param>
    /// <param name="flags">Creation flags (ThreadCreationFlags.CreateSuspended to start suspended)</param>
    /// <param name="threadId">Receives the thread ID</param>
    /// <returns>Thread handle, or invalid handle on failure</returns>
    public static ThreadHandle Create(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        out uint threadId)
    {
        fixed (uint* idPtr = &threadId)
        {
            void* thread = Kernel_CreateThread(entryPoint, parameter, stackSize, flags, idPtr);
            return new ThreadHandle(thread);
        }
    }

    /// <summary>
    /// Exit the current thread with the specified exit code.
    /// This function does not return.
    /// </summary>
    public static void Exit(uint exitCode)
    {
        Kernel_ExitThread(exitCode);
    }

    /// <summary>
    /// Get the current thread's ID.
    /// </summary>
    public static uint CurrentThreadId => Kernel_GetCurrentThreadId();

    /// <summary>
    /// Get the current thread's handle.
    /// </summary>
    public static ThreadHandle CurrentThread => new ThreadHandle(Kernel_GetCurrentThread());

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    public static void Sleep(uint milliseconds)
    {
        Kernel_Sleep(milliseconds);
    }

    /// <summary>
    /// Yield execution to another ready thread.
    /// </summary>
    public static void Yield()
    {
        Kernel_Yield();
    }

    /// <summary>
    /// Get the exit code of a thread.
    /// </summary>
    /// <param name="thread">Thread handle</param>
    /// <param name="exitCode">Receives the exit code (259 = STILL_ACTIVE)</param>
    /// <returns>true if successful</returns>
    public static bool GetExitCode(ThreadHandle thread, out uint exitCode)
    {
        fixed (uint* codePtr = &exitCode)
        {
            return Kernel_GetExitCodeThread(thread.Pointer, codePtr);
        }
    }

    /// <summary>
    /// Get the state of a thread.
    /// </summary>
    public static ThreadState GetState(ThreadHandle thread)
    {
        return (ThreadState)Kernel_GetThreadState(thread.Pointer);
    }

    /// <summary>
    /// Suspend a thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int Suspend(ThreadHandle thread)
    {
        return Kernel_SuspendThread(thread.Pointer);
    }

    /// <summary>
    /// Resume a suspended thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error</returns>
    public static int Resume(ThreadHandle thread)
    {
        return Kernel_ResumeThread(thread.Pointer);
    }

    /// <summary>
    /// Get the total number of active threads.
    /// </summary>
    public static int ThreadCount => Kernel_GetThreadCount();

    // Kernel exports via DllImport("*")
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
}
