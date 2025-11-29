// netos netlib - Environment
// Provides Environment.FailFast by importing from kernel PAL.

using System.Runtime.InteropServices;

namespace System;

public static partial class Environment
{
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
}
