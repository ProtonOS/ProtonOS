// ProtonOS kernel - DDK ACPI Exports
// Exposes ACPI table access to JIT-compiled drivers.

using System.Runtime.InteropServices;
using ProtonOS.Platform;

namespace ProtonOS.Exports.DDK;

/// <summary>
/// DDK exports for ACPI table access.
/// </summary>
public static unsafe class ACPIExports
{
    /// <summary>
    /// Find an ACPI table by its 4-character signature.
    /// </summary>
    /// <param name="signature">4-byte signature packed as uint32</param>
    /// <returns>Pointer to table header, or null if not found</returns>
    [UnmanagedCallersOnly(EntryPoint = "Kernel_FindACPITable")]
    public static ACPITableHeader* FindACPITable(uint signature)
    {
        byte sig0 = (byte)(signature);
        byte sig1 = (byte)(signature >> 8);
        byte sig2 = (byte)(signature >> 16);
        byte sig3 = (byte)(signature >> 24);

        return ACPI.FindTable(sig0, sig1, sig2, sig3);
    }
}
