// ProtonOS kernel - Syscall Handler Registration Exports
// Allows DDK to register callbacks for filesystem syscalls.

using System.Runtime.InteropServices;
using ProtonOS.Syscall;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// Exports for registering syscall handlers from the DDK.
/// </summary>
public static unsafe class SyscallHandlers
{
    /// <summary>
    /// Register a mkdir handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* path, int mode)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterMkdirHandler")]
    public static void RegisterMkdirHandler(delegate* unmanaged<byte*, int, int> handler)
    {
        SyscallDispatch.RegisterMkdirHandler(handler);
    }

    /// <summary>
    /// Register an rmdir handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* path)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterRmdirHandler")]
    public static void RegisterRmdirHandler(delegate* unmanaged<byte*, int> handler)
    {
        SyscallDispatch.RegisterRmdirHandler(handler);
    }

    /// <summary>
    /// Register an unlink handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* path)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterUnlinkHandler")]
    public static void RegisterUnlinkHandler(delegate* unmanaged<byte*, int> handler)
    {
        SyscallDispatch.RegisterUnlinkHandler(handler);
    }
}
