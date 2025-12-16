// ProtonOS korlib - DDK Thread API
// These stubs exist only for IL metadata - JIT code resolves to native exports via token registry.

#if KORLIB_IL
using System;

namespace ProtonOS.Kernel;

/// <summary>
/// Opaque thread handle type.
/// </summary>
public struct ThreadHandle
{
    internal nint _handle;
}

/// <summary>
/// Thread state enumeration.
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
/// DDK Thread API.
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

    /// <summary>
    /// Create a new thread.
    /// </summary>
    /// <param name="entryPoint">Thread entry point function pointer.</param>
    /// <param name="parameter">Parameter passed to entry point.</param>
    /// <param name="stackSize">Stack size in bytes (0 = default 64KB).</param>
    /// <param name="flags">Creation flags (CREATE_SUSPENDED = 4).</param>
    /// <param name="threadId">Output: assigned thread ID.</param>
    /// <returns>Thread handle, or null on failure.</returns>
    public static void* CreateThread(
        delegate* unmanaged<void*, uint> entryPoint,
        void* parameter,
        nuint stackSize,
        uint flags,
        uint* threadId) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Exit the current thread with the specified exit code.
    /// This function does not return.
    /// </summary>
    public static void ExitThread(uint exitCode) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the current thread ID.
    /// </summary>
    public static uint GetCurrentThreadId() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the current thread handle.
    /// </summary>
    public static void* GetCurrentThread() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Sleep for the specified number of milliseconds.
    /// </summary>
    public static void Sleep(uint milliseconds) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Yield execution to another ready thread.
    /// </summary>
    public static void Yield() => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the exit code of a thread.
    /// </summary>
    /// <param name="thread">Thread handle.</param>
    /// <param name="exitCode">Output: exit code (STILL_ACTIVE if still running).</param>
    /// <returns>true if successful.</returns>
    public static bool GetExitCodeThread(void* thread, uint* exitCode) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the state of a thread.
    /// </summary>
    public static int GetThreadState(void* thread) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Suspend a thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error.</returns>
    public static int SuspendThread(void* thread) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Resume a suspended thread.
    /// </summary>
    /// <returns>Previous suspend count, or -1 on error.</returns>
    public static int ResumeThread(void* thread) => throw new PlatformNotSupportedException();

    /// <summary>
    /// Get the total number of active threads.
    /// </summary>
    public static int GetThreadCount() => throw new PlatformNotSupportedException();
}
#endif
