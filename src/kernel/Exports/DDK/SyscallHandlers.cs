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

    /// <summary>
    /// Register a getdents handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* path, byte* buf, int count, long* offset)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterGetdentsHandler")]
    public static void RegisterGetdentsHandler(delegate* unmanaged<byte*, byte*, int, long*, int> handler)
    {
        SyscallDispatch.RegisterGetdentsHandler(handler);
    }

    /// <summary>
    /// Register an access handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* path, int mode)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterAccessHandler")]
    public static void RegisterAccessHandler(delegate* unmanaged<byte*, int, int> handler)
    {
        SyscallDispatch.RegisterAccessHandler(handler);
    }

    /// <summary>
    /// Register a rename handler.
    /// </summary>
    /// <param name="handler">Function pointer: int handler(byte* oldpath, byte* newpath)</param>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_RegisterRenameHandler")]
    public static void RegisterRenameHandler(delegate* unmanaged<byte*, byte*, int> handler)
    {
        SyscallDispatch.RegisterRenameHandler(handler);
    }
}
