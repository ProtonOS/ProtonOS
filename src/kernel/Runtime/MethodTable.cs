// netos kernel - MethodTable structure
// Mirrors the NativeAOT MethodTable layout for GC and runtime inspection.
//
// This is a kernel-side copy of the MethodTable struct. The layout must match
// exactly what NativeAOT/bflat emits. netlib has its own internal copy for allocation.

using System.Runtime.InteropServices;

namespace Kernel.Runtime;

/// <summary>
/// MethodTable flags from NativeAOT runtime.
/// These are in the high 16 bits of CombinedFlags (which is _usFlags << 16 | _usComponentSize).
/// </summary>
public static class MTFlags
{
    // From src/coreclr/nativeaot/Runtime/inc/MethodTable.h

    /// <summary>Type contains GC references and has a GCDesc before the MethodTable.</summary>
    public const uint HasPointers = 0x01000000;

    /// <summary>Type has a finalizer that should be called when collected.</summary>
    public const uint HasFinalizer = 0x00100000;

    /// <summary>Type has optional fields (MethodTable is variable size).</summary>
    public const uint HasOptionalFields = 0x00040000;

    /// <summary>Type is an array.</summary>
    public const uint IsArray = 0x00080000;

    /// <summary>Type has component size (array or string).</summary>
    public const uint HasComponentSize = 0x80000000;

    /// <summary>Mask for component size in low 16 bits.</summary>
    public const uint ComponentSizeMask = 0x0000FFFF;
}

/// <summary>
/// NativeAOT MethodTable structure.
/// Every managed object starts with a pointer to its MethodTable.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct MethodTable
{
    /// <summary>
    /// For arrays/strings: element size in bytes.
    /// For other types: 0.
    /// </summary>
    public ushort _usComponentSize;

    /// <summary>
    /// Type flags (high 16 bits when combined with component size).
    /// </summary>
    public ushort _usFlags;

    /// <summary>
    /// Base instance size in bytes (includes MethodTable* but not object header).
    /// </summary>
    public uint _uBaseSize;

    /// <summary>
    /// For arrays: element type's MethodTable.
    /// For other types: base type or interface.
    /// </summary>
    public MethodTable* _relatedType;

    /// <summary>Number of virtual method slots.</summary>
    public ushort _usNumVtableSlots;

    /// <summary>Number of implemented interfaces.</summary>
    public ushort _usNumInterfaces;

    /// <summary>Cached hash code for the type.</summary>
    public uint _uHashCode;

    // VTable entries follow after this struct

    /// <summary>
    /// Get the combined 32-bit flags value.
    /// Layout: (flags << 16) | componentSize
    /// </summary>
    public uint CombinedFlags => ((uint)_usFlags << 16) | _usComponentSize;

    /// <summary>Whether this type contains GC references.</summary>
    public bool HasPointers => (CombinedFlags & MTFlags.HasPointers) != 0;

    /// <summary>Whether this type has a finalizer.</summary>
    public bool HasFinalizer => (CombinedFlags & MTFlags.HasFinalizer) != 0;

    /// <summary>Whether this is an array type.</summary>
    public bool IsArray => (CombinedFlags & MTFlags.IsArray) != 0;

    /// <summary>Whether this type has a component size (array or string).</summary>
    public bool HasComponentSize => (CombinedFlags & MTFlags.HasComponentSize) != 0;

    /// <summary>Get the component size for arrays/strings.</summary>
    public ushort ComponentSize => _usComponentSize;

    /// <summary>Get the base instance size.</summary>
    public uint BaseSize => _uBaseSize;
}
