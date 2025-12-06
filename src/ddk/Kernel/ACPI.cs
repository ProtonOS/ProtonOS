// ProtonOS DDK - ACPI Kernel Wrappers
// DllImport wrappers for ACPI table access.

using System;
using System.Runtime.InteropServices;

namespace ProtonOS.DDK.Kernel;

/// <summary>
/// ACPI table header structure.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ACPITableHeader
{
    public fixed byte Signature[4];
    public uint Length;
    public byte Revision;
    public byte Checksum;
    public fixed byte OemId[6];
    public fixed byte OemTableId[8];
    public uint OemRevision;
    public uint CreatorId;
    public uint CreatorRevision;

    public bool SignatureEquals(uint sig)
    {
        fixed (byte* ptr = Signature)
        {
            return *(uint*)ptr == sig;
        }
    }
}

/// <summary>
/// DDK wrappers for kernel ACPI table access APIs.
/// </summary>
public static unsafe class ACPI
{
    [DllImport("*", EntryPoint = "Kernel_FindACPITable")]
    public static extern ACPITableHeader* FindTable(uint signature);

    /// <summary>
    /// Create a 4-byte signature from characters.
    /// </summary>
    public static uint MakeSignature(char c0, char c1, char c2, char c3)
    {
        return (uint)c0 | ((uint)c1 << 8) | ((uint)c2 << 16) | ((uint)c3 << 24);
    }

    // Common ACPI table signatures
    public static readonly uint MCFG = MakeSignature('M', 'C', 'F', 'G');
    public static readonly uint MADT = MakeSignature('A', 'P', 'I', 'C');
    public static readonly uint SRAT = MakeSignature('S', 'R', 'A', 'T');
    public static readonly uint SLIT = MakeSignature('S', 'L', 'I', 'T');
    public static readonly uint HPET = MakeSignature('H', 'P', 'E', 'T');
    public static readonly uint FADT = MakeSignature('F', 'A', 'C', 'P');
    public static readonly uint DSDT = MakeSignature('D', 'S', 'D', 'T');
    public static readonly uint SSDT = MakeSignature('S', 'S', 'D', 'T');
}
