// ProtonOS korlib - Environment
// Provides Environment.FailFast by importing from kernel PAL.

using System.Runtime.InteropServices;

namespace System;

public static partial class Environment
{
    /// <summary>Gets the newline string defined for this environment.</summary>
    public static string NewLine => "\n";

#if KORLIB_IL
    // Stubs for IL build - actual implementation is in AOT kernel
    // These stubs should never actually be called because the AOT registry
    // provides the native implementation. The IL is only for type resolution.
    /// <summary>
    /// Terminates the process immediately without running finalizers.
    /// </summary>
    public static void FailFast(string? message)
    {
        // Stub - should be resolved to AOT implementation at runtime
        while (true) { }
    }

    /// <summary>
    /// Terminates the process immediately without running finalizers.
    /// </summary>
    public static void FailFast(string? message, Exception? exception)
    {
        // Stub - should be resolved to AOT implementation at runtime
        while (true) { }
    }
#else
    [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
    private static extern void PalFailFast();

    /// <summary>
    /// Terminates the process immediately without running finalizers.
    /// </summary>
    public static void FailFast(string? message)
    {
        // Message is ignored - we just halt
        PalFailFast();
    }

    /// <summary>
    /// Terminates the process immediately without running finalizers.
    /// </summary>
    public static void FailFast(string? message, Exception? exception)
    {
        // Message and exception are ignored - we just halt
        PalFailFast();
    }
#endif
}
