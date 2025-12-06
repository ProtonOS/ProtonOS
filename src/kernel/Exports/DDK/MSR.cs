// ProtonOS kernel - DDK MSR Exports
// Exposes x86 Model Specific Register operations to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.X64;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for x86 MSR (Model Specific Register) operations.
/// </summary>
public static class MSRExports
{
    [UnmanagedCallersOnly(EntryPoint = "Kernel_ReadMSR")]
    public static ulong ReadMSR(uint msr) => CPU.ReadMsr(msr);

    [UnmanagedCallersOnly(EntryPoint = "Kernel_WriteMSR")]
    public static void WriteMSR(uint msr, ulong value) => CPU.WriteMsr(msr, value);
}
