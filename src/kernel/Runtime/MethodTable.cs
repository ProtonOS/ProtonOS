// ProtonOS kernel - MethodTable structure
// Mirrors the NativeAOT MethodTable layout for GC and runtime inspection.
//
// This is a kernel-side copy of the MethodTable struct. The layout must match
// exactly what NativeAOT/bflat emits. korlib has its own internal copy for allocation.
//
// Extended Layout for Type Hierarchy and Interface Dispatch:
// [0]   _usComponentSize (2 bytes) - element size for arrays/strings
// [2]   _usFlags (2 bytes) - type flags
// [4]   _uBaseSize (4 bytes) - base instance size
// [8]   _relatedType (8 bytes) - parent MT* for classes, element MT* for arrays
// [16]  _usNumVtableSlots (2 bytes)
// [18]  _usNumInterfaces (2 bytes)
// [20]  _uHashCode (4 bytes)
// [24]  VTable[0..NumVtableSlots-1] (8 bytes each)
// [24 + NumVtableSlots*8]  InterfaceMap[0..NumInterfaces-1] (8 bytes each, just MethodTable*)
// [24 + NumVtableSlots*8 + NumInterfaces*8]  Optional fields (TypeManager, WritableData, DispatchMap, etc.)

using System.Runtime.InteropServices;

using ProtonOS.Platform;
using ProtonOS.Runtime.JIT;

namespace ProtonOS.Runtime;

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

    /// <summary>Type has a dispatch map for interface method resolution.</summary>
    public const uint HasDispatchMap = 0x00040000;

    /// <summary>Type is an array.</summary>
    public const uint IsArray = 0x00080000;

    /// <summary>Type is an interface.</summary>
    public const uint IsInterface = 0x00020000;

    /// <summary>Type has component size (array or string).</summary>
    public const uint HasComponentSize = 0x80000000;

    /// <summary>Mask for component size in low 16 bits.</summary>
    public const uint ComponentSizeMask = 0x0000FFFF;

    /// <summary>Type is a value type (struct/enum).</summary>
    public const uint IsValueType = 0x00200000;

    /// <summary>Type is Nullable&lt;T&gt; (requires special boxing/unboxing semantics).</summary>
    public const uint IsNullable = 0x00010000;

    /// <summary>Type is a delegate (inherits from MulticastDelegate).</summary>
    public const uint IsDelegate = 0x00800000;  // In upper 16 bits (flags region)

    /// <summary>Generic interface has variant type parameters (covariant or contravariant).</summary>
    public const uint HasVariance = 0x00400000;
}

/// <summary>
/// NativeAOT dispatch cell type (from rhbinder.h).
/// </summary>
public enum DispatchCellType : ushort
{
    InterfaceAndSlot = 0x0,
    MetadataToken = 0x1,
    VTableOffset = 0x2,
}

/// <summary>
/// NativeAOT InterfaceDispatchCacheHeader structure (from rhbinder.h).
/// Contains cached interface type and slot information.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct InterfaceDispatchCacheHeader
{
    public MethodTable* m_pInterfaceType;
    public uint m_slotIndexOrMetadataTokenEncoded;

    // Encoding flags for m_slotIndexOrMetadataTokenEncoded
    public const uint CH_TypeAndSlotIndex = 0x0;
    public const uint CH_MetadataToken = 0x1;
    public const uint CH_Mask = 0x3;
    public const uint CH_Shift = 0x2;

    public DispatchCellInfo GetDispatchCellInfo()
    {
        DispatchCellInfo cellInfo = default;
        uint encoded = m_slotIndexOrMetadataTokenEncoded;

        if ((encoded & CH_Mask) == CH_TypeAndSlotIndex)
        {
            cellInfo.CellType = DispatchCellType.InterfaceAndSlot;
            cellInfo.InterfaceType = m_pInterfaceType;
            cellInfo.InterfaceSlot = (ushort)(encoded >> (int)CH_Shift);
        }
        else if ((encoded & CH_Mask) == CH_MetadataToken)
        {
            cellInfo.CellType = DispatchCellType.MetadataToken;
            cellInfo.MetadataToken = encoded >> (int)CH_Shift;
        }
        cellInfo.HasCache = 1;
        return cellInfo;
    }
}

/// <summary>
/// Information extracted from an InterfaceDispatchCell.
/// </summary>
public unsafe struct DispatchCellInfo
{
    public DispatchCellType CellType;
    public MethodTable* InterfaceType;
    public ushort InterfaceSlot;
    public byte HasCache;
    public uint MetadataToken;
    public uint VTableOffset;
}

/// <summary>
/// NativeAOT InterfaceDispatchCell structure (from rhbinder.h).
/// One of these is allocated per interface call site.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct InterfaceDispatchCell
{
    /// <summary>Call this code to execute the interface dispatch.</summary>
    public nuint m_pStub;

    /// <summary>
    /// Context used by the stub above. Has complex encoding:
    /// - If low 2 bits are set: initial dispatch, encodes interface/slot
    /// - If low 2 bits are 0 and value >= 0x1000: cache pointer
    /// - If low 2 bits are 0 and value < 0x1000: vtable offset
    /// </summary>
    public nuint m_pCache;

    // Cache pointer encoding flags
    public const nuint IDC_CachePointerIsInterfaceRelativePointer = 0x3;
    public const nuint IDC_CachePointerIsIndirectedInterfaceRelativePointer = 0x2;
    public const nuint IDC_CachePointerIsInterfacePointerOrMetadataToken = 0x1;
    public const nuint IDC_CachePointerPointsAtCache = 0x0;
    public const nuint IDC_CachePointerMask = 0x3;
    public const nuint IDC_MaxVTableOffsetPlusOne = 0x1000;

    /// <summary>
    /// Parse this dispatch cell to extract interface type and slot information.
    /// </summary>
    public DispatchCellInfo GetDispatchCellInfo()
    {
        nuint cachePointerValue = m_pCache;
        DispatchCellInfo cellInfo = default;

        // Check for VTable offset (value < 0x1000 and low bits are 0)
        if (cachePointerValue < IDC_MaxVTableOffsetPlusOne &&
            (cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerPointsAtCache)
        {
            cellInfo.VTableOffset = (uint)cachePointerValue;
            cellInfo.CellType = DispatchCellType.VTableOffset;
            cellInfo.HasCache = 1;
            return cellInfo;
        }

        // Check for cache pointer (low bits are 0, value >= 0x1000)
        if ((cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerPointsAtCache)
        {
            // It's a cache pointer - read the InterfaceDispatchCacheHeader
            InterfaceDispatchCacheHeader* cacheHeader = (InterfaceDispatchCacheHeader*)cachePointerValue;
            return cacheHeader->GetDispatchCellInfo();
        }

        // Otherwise, walk to the terminator cell to get slot and flags
        fixed (InterfaceDispatchCell* self = &this)
        {
            InterfaceDispatchCell* currentCell = self;
            while (currentCell->m_pStub != 0)
            {
                currentCell++;
            }
            nuint cachePointerValueFlags = currentCell->m_pCache;

            // Cell type is in bits 16-31, slot is in bits 0-15
            cellInfo.CellType = (DispatchCellType)(cachePointerValueFlags >> 16);
            cellInfo.InterfaceSlot = (ushort)cachePointerValueFlags;
        }

        if (cellInfo.CellType == DispatchCellType.InterfaceAndSlot)
        {
            // Extract interface type based on encoding
            if ((cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerIsInterfacePointerOrMetadataToken)
            {
                // Direct interface pointer with low bit set
                cellInfo.InterfaceType = (MethodTable*)(cachePointerValue & ~IDC_CachePointerMask);
            }
            else if ((cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerIsInterfaceRelativePointer ||
                     (cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerIsIndirectedInterfaceRelativePointer)
            {
                // Relative pointer to interface
                fixed (nuint* pCache = &m_pCache)
                {
                    nuint interfacePointerValue = (nuint)pCache + (nuint)(int)cachePointerValue;
                    interfacePointerValue &= ~IDC_CachePointerMask;
                    if ((cachePointerValue & IDC_CachePointerMask) == IDC_CachePointerIsInterfaceRelativePointer)
                    {
                        cellInfo.InterfaceType = (MethodTable*)interfacePointerValue;
                    }
                    else
                    {
                        cellInfo.InterfaceType = *(MethodTable**)interfacePointerValue;
                    }
                }
            }
        }
        else if (cellInfo.CellType == DispatchCellType.MetadataToken)
        {
            cellInfo.MetadataToken = (uint)(cachePointerValue >> 2);
        }

        return cellInfo;
    }
}

/// <summary>
/// Entry in a DispatchMap mapping interface method to implementation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct DispatchMapEntry
{
    /// <summary>Index into the InterfaceMap array.</summary>
    public ushort _usInterfaceIndex;

    /// <summary>Slot number within the interface.</summary>
    public ushort _usInterfaceMethodSlot;

    /// <summary>Implementation vtable slot (or sealed virtual slot offset).</summary>
    public ushort _usImplMethodSlot;
}

/// <summary>
/// DispatchMap structure for mapping interface methods to implementations.
/// Layout: Header(8 bytes) + Entries[numStandard + numDefault]
/// Note: Sealed virtual slots are stored separately in the MethodTable's optional fields,
/// accessed via GetSealedVirtualSlot() on MethodTable, NOT via this structure.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct DispatchMap
{
    private ushort _standardEntryCount;
    private ushort _defaultEntryCount;
    private ushort _standardStaticEntryCount;
    private ushort _defaultStaticEntryCount;
    private DispatchMapEntry _firstEntry;

    public uint NumStandardEntries => _standardEntryCount;
    public uint NumDefaultEntries => _defaultEntryCount;
    public uint NumStandardStaticEntries => _standardStaticEntryCount;
    public uint NumDefaultStaticEntries => _defaultStaticEntryCount;

    /// <summary>Total number of dispatch entries (standard + default, excluding static).</summary>
    public uint NumEntries => (uint)(_standardEntryCount + _defaultEntryCount);

    public DispatchMapEntry* GetEntry(int index)
    {
        fixed (DispatchMapEntry* pFirst = &_firstEntry)
        {
            return pFirst + index;
        }
    }
}

/// <summary>
/// Interface map entry for kernel-generated types (JIT/dynamic types).
/// Note: AOT-compiled types from bflat use a different layout (just MethodTable**)
/// with DispatchMap for method slot resolution.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct InterfaceMapEntry
{
    /// <summary>Pointer to the interface's MethodTable.</summary>
    public MethodTable* InterfaceMT;

    /// <summary>Starting vtable slot index for this interface's methods (kernel types only).</summary>
    public ushort StartSlot;

    /// <summary>Padding to align to 16 bytes.</summary>
    private ushort _padding1;
    private uint _padding2;

    /// <summary>Size of an InterfaceMapEntry in bytes.</summary>
    public const int Size = 16;
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
    /// Overloaded field with context-dependent meaning (ECMA-335 / NativeAOT layout):
    /// - For classes: parent (base) type's MethodTable
    /// - For arrays: element type's MethodTable
    /// - For generic instantiations: first type argument's MethodTable
    /// - For Nullable&lt;T&gt;: underlying type T's MethodTable
    /// - For interfaces: typically null (AOT doesn't set this)
    ///
    /// Use the semantic accessors instead of direct access:
    /// - GetParentType() for class hierarchy
    /// - GetArrayElementType() for array element type
    /// - GetFirstTypeArgument() for generic type arguments
    /// - GetNullableUnderlyingType() for Nullable&lt;T&gt;
    /// </summary>
    public MethodTable* _relatedType;

    /// <summary>Number of virtual method slots.</summary>
    public ushort _usNumVtableSlots;

    /// <summary>Number of implemented interfaces.</summary>
    public ushort _usNumInterfaces;

    // ==================================================================================
    // TYPE HASH CODE - CROSS-WORLD INCOMPATIBILITY
    // ==================================================================================
    // The hash code is used for fast type identity checks, but has an important caveat:
    //
    // AOT Types (compiled by ILC at build time):
    //   - Hash computed by TypeHashingAlgorithms.ComputeGenericInstanceHash() in ILC
    //   - Uses TypeKey algorithm based on namespace, name, and type arguments
    //   - Consistent within the AOT world but uses different algorithm than kernel
    //
    // Kernel Types (created at runtime by JIT/generic instantiation):
    //   - Hash computed inline in GetOrCreateGenericInstMethodTable()
    //   - Uses multiply-add (h = h*31 + MT) over type arg pointers, then XOR with genDefToken
    //   - Consistent within kernel world but incompatible with AOT hashes
    //
    // IMPORTANT: Hash comparison is only valid between types from the SAME world.
    // Use AreInterfacesStructurallyEquivalent() for cross-world comparisons, which
    // detects world membership via address ranges (AOT types at 0x1D000000+, kernel
    // types in heap at lower addresses).
    //
    // Special cases:
    //   - Hash of 0 indicates "not computed" - skip hash comparison
    //   - Variance flags may be stored in low bits for variant generic interfaces
    // ==================================================================================
    /// <summary>Cached hash code for the type (see documentation above for cross-world caveats).</summary>
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

    /// <summary>Whether this is a value type (struct/enum).</summary>
    public bool IsValueType => (CombinedFlags & MTFlags.IsValueType) != 0;

    /// <summary>Whether this is Nullable&lt;T&gt; (requires special boxing/unboxing).</summary>
    public bool IsNullable => (CombinedFlags & MTFlags.IsNullable) != 0;

    /// <summary>Whether this is a delegate type (inherits from MulticastDelegate).</summary>
    public bool IsDelegate => (CombinedFlags & MTFlags.IsDelegate) != 0;

    /// <summary>Whether this type has a component size (array or string).</summary>
    public bool HasComponentSize => (CombinedFlags & MTFlags.HasComponentSize) != 0;

    /// <summary>Whether this type has a dispatch map for interface resolution.</summary>
    public bool HasDispatchMap => (CombinedFlags & MTFlags.HasDispatchMap) != 0;

    /// <summary>Get the component size for arrays/strings.</summary>
    public ushort ComponentSize => _usComponentSize;

    /// <summary>Get the base instance size.</summary>
    public uint BaseSize => _uBaseSize;

    /// <summary>Get the number of vtable slots.</summary>
    public ushort NumVtableSlots => _usNumVtableSlots;

    /// <summary>Get the number of implemented interfaces.</summary>
    public ushort NumInterfaces => _usNumInterfaces;

    /// <summary>
    /// Size of the MethodTable header in bytes (before vtable).
    /// </summary>
    public const int HeaderSize = 24;  // 2+2+4+8+2+2+4 = 24 bytes

    // =========================================================================
    // Optional Fields Layout (NativeAOT RelPtr format)
    // These offsets are relative to GetOptionalFieldsOffset()
    // Each field is a 4-byte relative pointer (RelPtr)
    // =========================================================================

    /// <summary>Offset of TypeManagerIndirection RelPtr in optional fields.</summary>
    public const int OptFieldsOffset_TypeManagerIndirection = 0;

    /// <summary>Offset of WritableData RelPtr in optional fields.</summary>
    public const int OptFieldsOffset_WritableData = 4;

    /// <summary>Offset of DispatchMap RelPtr in optional fields.</summary>
    public const int OptFieldsOffset_DispatchMap = 8;

    /// <summary>Offset of SealedVirtualSlots table RelPtr in optional fields.</summary>
    public const int OptFieldsOffset_SealedVirtualSlots = 12;

    /// <summary>
    /// Get a pointer to the vtable array (immediately follows the header).
    /// </summary>
    public nint* GetVtablePtr()
    {
        fixed (MethodTable* self = &this)
        {
            return (nint*)((byte*)self + HeaderSize);
        }
    }

    /// <summary>
    /// Get the function pointer at a specific vtable slot.
    /// Only checks the regular vtable, not sealed virtual slots.
    /// For a unified lookup that handles sealed slots too, use GetVirtualSlot().
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>The function pointer, or null if slot is out of range.</returns>
    public nint GetVtableSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _usNumVtableSlots)
            return 0;

        nint* vtable = GetVtablePtr();
        return vtable[slotIndex];
    }

    /// <summary>
    /// Get a virtual method pointer by slot index, handling both regular vtable slots
    /// and sealed virtual slots (for AOT types with dispatch maps).
    /// This is the preferred method for resolving virtual calls as it handles all cases.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index (may exceed _usNumVtableSlots for sealed slots).</param>
    /// <returns>The function pointer, or 0 if not found.</returns>
    public nint GetVirtualSlot(int slotIndex)
    {
        if (slotIndex < 0)
            return 0;

        // Check regular vtable first
        if (slotIndex < _usNumVtableSlots)
        {
            nint* vtable = GetVtablePtr();
            return vtable[slotIndex];
        }

        // For slots beyond vtable, check sealed virtual slots (AOT types only)
        if (HasDispatchMap)
        {
            int sealedSlotIndex = slotIndex - _usNumVtableSlots;
            return GetSealedVirtualSlot(sealedSlotIndex);
        }

        return 0;
    }

    /// <summary>
    /// Get the byte offset from the start of the MethodTable to a vtable slot.
    /// Used for generating runtime vtable lookup code.
    /// </summary>
    /// <param name="slotIndex">Zero-based slot index.</param>
    /// <returns>Byte offset from MethodTable start.</returns>
    public static int GetVtableSlotOffset(int slotIndex)
    {
        return HeaderSize + (slotIndex * sizeof(nint));
    }

    /// <summary>
    /// Get the parent type's MethodTable (for class hierarchy walking).
    /// For arrays, this returns null (use _relatedType for element type instead).
    /// </summary>
    public MethodTable* GetParentType()
    {
        // Arrays use _relatedType for element type, not parent
        if (IsArray)
            return null;
        return _relatedType;
    }

    /// <summary>
    /// Get the array element type's MethodTable.
    /// Only valid for array types - returns null for non-arrays.
    /// </summary>
    public MethodTable* GetArrayElementType()
    {
        if (!IsArray)
            return null;
        return _relatedType;
    }

    /// <summary>
    /// Get the first type argument's MethodTable for generic instantiations.
    /// For types like List&lt;int&gt;, returns the MethodTable for int.
    /// Returns null for non-generic types or open generic definitions.
    /// Note: For multi-argument generics like Dictionary&lt;K,V&gt;, only returns the first arg.
    /// Use the generic type argument cache for additional arguments.
    /// </summary>
    public MethodTable* GetFirstTypeArgument()
    {
        // Arrays and interfaces don't use _relatedType for type args
        if (IsArray || IsInterface)
            return null;
        // For generic instantiations, _relatedType stores the first type argument
        // We can't easily distinguish generic types from regular classes here,
        // but callers typically know the context
        return _relatedType;
    }

    /// <summary>
    /// Get the underlying type's MethodTable for Nullable&lt;T&gt;.
    /// Returns null if this is not a Nullable type.
    /// </summary>
    public MethodTable* GetNullableUnderlyingType()
    {
        if (!IsNullable)
            return null;
        return _relatedType;
    }

    /// <summary>
    /// Check if this type is a reference type (class, interface, array, string).
    /// Value types have ComponentSize == 0 and are not arrays/interfaces.
    /// </summary>
    public bool IsReferenceType
    {
        get
        {
            // Arrays are reference types
            if (IsArray)
                return true;
            // Interfaces are reference types
            if (IsInterface)
                return true;
            // Strings have ComponentSize=2 (char size) and HasComponentSize flag
            if (HasComponentSize && _usComponentSize > 0)
                return true;
            // Classes have pointers (GC tracked) - this indicates reference type
            if (HasPointers)
                return true;
            // Classes have a parent type (except System.Object which is the root)
            // Value types are sealed with no parent (except ValueType itself)
            if (_relatedType != null)
                return true;
            return false;
        }
    }

    /// <summary>Whether this is an interface type.</summary>
    public bool IsInterface => (CombinedFlags & MTFlags.IsInterface) != 0;

    /// <summary>Whether this interface definition has variant type parameters.</summary>
    public bool HasVariance => (CombinedFlags & MTFlags.HasVariance) != 0;

    // ==================================================================================
    // INTERFACE MAP ACCESSORS
    // ==================================================================================
    // The interface map has two formats depending on type origin:
    //
    // AOT Types (HasDispatchMap = true):
    //   - Simple MethodTable* array (8 bytes per entry)
    //   - Uses DispatchMap for slot resolution (interface -> vtable mapping)
    //   - Use GetAotInterfaceMapPtr() for raw access
    //
    // Kernel Types (HasDispatchMap = false):
    //   - InterfaceMapEntry array (16 bytes per entry)
    //   - Contains StartSlot field for contiguous slot layout
    //   - Use GetInterfaceMapPtr() for raw access
    //
    // Unified accessors that handle both formats:
    //   - GetInterfaceAt(index) - returns MethodTable* for interface
    //   - GetInterfaceIndex(MT*) - finds interface in map
    //   - GetVtableSlotForInterfaceMethod(ifaceMT, slot) - resolves method slot
    //   - ImplementsInterfaceWithSlot(...) - checks interface implementation
    // ==================================================================================

    /// <summary>
    /// Get a pointer to the interface map as InterfaceMapEntry* (for kernel-generated types).
    /// Kernel types use 16-byte InterfaceMapEntry with StartSlot field.
    /// </summary>
    public InterfaceMapEntry* GetInterfaceMapPtr()
    {
        fixed (MethodTable* self = &this)
        {
            // Interface map is after the vtable
            return (InterfaceMapEntry*)((byte*)self + HeaderSize + _usNumVtableSlots * sizeof(nint));
        }
    }

    /// <summary>
    /// Get a pointer to the interface map as MethodTable** (for AOT-compiled types).
    /// AOT types from NativeAOT/bflat use simple MethodTable* array.
    /// </summary>
    public MethodTable** GetAotInterfaceMapPtr()
    {
        fixed (MethodTable* self = &this)
        {
            // Interface map is after the vtable
            return (MethodTable**)((byte*)self + HeaderSize + _usNumVtableSlots * sizeof(nint));
        }
    }

    /// <summary>
    /// Get the byte offset from the start of the MethodTable to the interface map.
    /// </summary>
    public int GetInterfaceMapOffset()
    {
        return HeaderSize + _usNumVtableSlots * sizeof(nint);
    }

    /// <summary>
    /// Get the byte offset from the start of the MethodTable to optional fields.
    /// Optional fields come after the interface map.
    /// AOT types use 8-byte interface entries, kernel types use 16-byte InterfaceMapEntry.
    /// </summary>
    public int GetOptionalFieldsOffset()
    {
        // AOT types have HasDispatchMap, kernel types don't
        int interfaceEntrySize = HasDispatchMap ? sizeof(nint) : InterfaceMapEntry.Size;
        return GetInterfaceMapOffset() + _usNumInterfaces * interfaceEntrySize;
    }

    /// <summary>
    /// Get the dispatch map pointer (if HasDispatchMap is true).
    /// The dispatch map is stored after TypeManager indirection and WritableData pointers.
    /// </summary>
    public DispatchMap* GetDispatchMap()
    {
        if (!HasDispatchMap)
            return null;

        fixed (MethodTable* self = &this)
        {
            // Debug: show MT layout
            int optOffset = GetOptionalFieldsOffset();

            // For AOT types, the dispatch map is stored as a RelPtr in optional fields
            // at offset OptFieldsOffset_DispatchMap (after TypeManagerIndirection and WritableData)
            byte* pOptional = (byte*)self + optOffset;
            byte* pDispatchMapRelPtr = pOptional + OptFieldsOffset_DispatchMap;
            int relativeOffset = *(int*)pDispatchMapRelPtr;

            // Validate the relative offset is reasonable (within ±1MB of the MT)
            // This catches garbage values that would point to invalid memory
            if (relativeOffset < -0x100000 || relativeOffset > 0x100000)
            {
                return null;
            }

            DispatchMap* result = (DispatchMap*)(pDispatchMapRelPtr + relativeOffset);

            // Validate the dispatch map address is in a reasonable range
            // (should be in kernel memory, not user or unmapped space)
            ulong resultAddr = (ulong)result;
            if (resultAddr < 0x10000 || resultAddr > 0xFFFF800000000000)
            {
                return null;
            }

            // Validate entry counts are reasonable:
            // - Not excessively large (max ~100 entries per interface is generous)
            // - Total entries shouldn't exceed numInterfaces * reasonable_methods_per_interface
            uint numStd = result->NumStandardEntries;
            uint numDef = result->NumDefaultEntries;
            uint maxReasonableEntries = (uint)(_usNumInterfaces + 1) * 50;  // 50 methods per interface is very generous

            if (numStd > maxReasonableEntries || numDef > maxReasonableEntries ||
                (numStd + numDef) > maxReasonableEntries)
            {
                return null;
            }

            return result;
        }
    }

    /// <summary>
    /// Get a sealed virtual slot method address from the sealed slots table.
    /// For AOT types, sealed slots are stored as 4-byte RelPtrs in a table referenced
    /// from the optional fields at offset 12 (after TypeManagerIndirection, WritableData, DispatchMap).
    /// </summary>
    /// <param name="sealedSlotIndex">Index into the sealed slots table (implSlot - numVtableSlots)</param>
    /// <returns>The method address, or 0 if not found or invalid</returns>
    public nint GetSealedVirtualSlot(int sealedSlotIndex)
    {
        if (!HasDispatchMap || sealedSlotIndex < 0)
            return 0;

        fixed (MethodTable* self = &this)
        {
            int optOffset = GetOptionalFieldsOffset();

            // SealedVirtualSlots RelPtr is at OptFieldsOffset_SealedVirtualSlots in optional fields
            byte* sealedSlotsRelPtrAddr = (byte*)self + optOffset + OptFieldsOffset_SealedVirtualSlots;
            int sealedSlotsRelPtr = *(int*)sealedSlotsRelPtrAddr;

            // Check for null/invalid RelPtr
            if (sealedSlotsRelPtr == 0)
                return 0;

            byte* sealedSlotsTable = sealedSlotsRelPtrAddr + sealedSlotsRelPtr;

            // Each sealed slot is a 4-byte RelPtr in the table
            byte* slotRelPtrAddr = sealedSlotsTable + sealedSlotIndex * 4;
            int slotRelPtr = *(int*)slotRelPtrAddr;
            nint methodAddr = (nint)(slotRelPtrAddr + slotRelPtr);

            return methodAddr;
        }
    }

    /// <summary>
    /// Find an interface in the interface map by index.
    /// Handles both AOT types (MethodTable**) and kernel types (InterfaceMapEntry*).
    /// </summary>
    /// <param name="index">Index into the interface map.</param>
    /// <returns>The interface MethodTable, or null if out of bounds.</returns>
    public MethodTable* GetInterface(int index)
    {
        if (index < 0 || index >= _usNumInterfaces)
            return null;

        if (HasDispatchMap)
        {
            // AOT type - simple MethodTable* array
            MethodTable** map = GetAotInterfaceMapPtr();
            return map[index];
        }
        else
        {
            // Kernel type - InterfaceMapEntry array
            InterfaceMapEntry* map = GetInterfaceMapPtr();
            return map[index].InterfaceMT;
        }
    }

    /// <summary>
    /// Find an interface in the interface map and return its index.
    /// Handles both AOT types and kernel types.
    /// </summary>
    /// <param name="interfaceMT">The interface MethodTable to find.</param>
    /// <returns>The index in the interface map, or -1 if not found.</returns>
    public int FindInterfaceIndex(MethodTable* interfaceMT)
    {
        if (_usNumInterfaces == 0)
            return -1;

        if (HasDispatchMap)
        {
            // AOT type - simple MethodTable* array
            MethodTable** map = GetAotInterfaceMapPtr();
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                if (map[i] == interfaceMT)
                    return i;
            }
        }
        else
        {
            // Kernel type - InterfaceMapEntry array
            InterfaceMapEntry* map = GetInterfaceMapPtr();
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                if (map[i].InterfaceMT == interfaceMT)
                    return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// Find an interface that is variant-compatible with the target interface.
    /// This handles covariance/contravariance for generic interfaces.
    /// Returns the index, or -1 if not found.
    /// </summary>
    public int FindVariantCompatibleInterfaceIndex(MethodTable* targetInterfaceMT)
    {
        if (_usNumInterfaces == 0 || targetInterfaceMT == null)
            return -1;

        if (HasDispatchMap)
        {
            // AOT type - simple MethodTable* array
            MethodTable** map = GetAotInterfaceMapPtr();
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                MethodTable* implInterface = map[i];
                if (implInterface == targetInterfaceMT)
                    return i;  // Exact match

                // Check structural type equality (same generic def + type args)
                // This handles AOT vs JIT MT mismatches for the same type
                if (implInterface != null && IsStructurallyEqual(implInterface, targetInterfaceMT))
                    return i;  // Same type (different MT pointers)

                // Check variance compatibility
                if (implInterface != null && implInterface->IsVariantCompatibleWith(targetInterfaceMT))
                    return i;  // Variant-compatible
            }

            // Fallback: check if any implemented interface inherits from the target interface
            // This handles the case where we're looking for IEnumerator but have IEnumerator<T>
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                MethodTable* implInterface = map[i];
                if (implInterface != null && implInterface->ImplementsInterface(targetInterfaceMT))
                    return i;
            }
        }
        else
        {
            // Kernel type - InterfaceMapEntry array
            InterfaceMapEntry* map = GetInterfaceMapPtr();

            // Track best candidate - when there are multiple matches (e.g., IEnumerable, IReadOnlyCollection,
            // IReadOnlyList all with 1 slot), we want the most specific one (highest startSlot)
            int bestCandidateIdx = -1;
            int bestStartSlot = -1;

            for (int i = 0; i < _usNumInterfaces; i++)
            {
                MethodTable* implInterface = map[i].InterfaceMT;

                // Exact match check
                if (implInterface == targetInterfaceMT)
                {
                    return i;  // Exact match - return immediately
                }

                // Check structural type equality (same generic def + type args)
                if (implInterface != null && IsStructurallyEqual(implInterface, targetInterfaceMT))
                {
                    // Track this as a candidate, prefer higher startSlot (more specific interface)
                    int curStartSlot = (int)map[i].StartSlot;
                    if (curStartSlot > bestStartSlot)
                    {
                        bestCandidateIdx = i;
                        bestStartSlot = curStartSlot;
                    }
                    continue;
                }

                // Check variance compatibility
                if (implInterface != null && implInterface->IsVariantCompatibleWith(targetInterfaceMT))
                {
                    // Also track variance matches, but prefer structural matches
                    if (bestCandidateIdx < 0)
                    {
                        bestCandidateIdx = i;
                        bestStartSlot = (int)map[i].StartSlot;
                    }
                }
            }

            // Return best candidate (if any)
            if (bestCandidateIdx >= 0)
            {
                return bestCandidateIdx;
            }

            // Fallback: check if any implemented interface inherits from the target interface
            // This handles the case where we're looking for IEnumerator but have IEnumerator<T>
            // which inherits from IEnumerator.
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                MethodTable* implInterface = map[i].InterfaceMT;
                if (implInterface != null && implInterface->ImplementsInterface(targetInterfaceMT))
                    return i;
            }
            return -1;
        }
        return -1;
    }

    /// <summary>
    /// Check if this interface MethodTable implements another interface.
    /// Used to handle interface inheritance (e.g., IEnumerator&lt;T&gt; implements IEnumerator).
    /// </summary>
    /// <param name="targetInterface">The interface to check for.</param>
    /// <returns>True if this interface implements the target interface.</returns>
    public bool ImplementsInterface(MethodTable* targetInterface)
    {
        if (targetInterface == null)
            return false;

        // Check our own interface map
        if (_usNumInterfaces == 0)
            return false;

        if (HasDispatchMap)
        {
            // AOT type - simple MethodTable* array
            MethodTable** map = GetAotInterfaceMapPtr();
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                if (map[i] == targetInterface)
                    return true;
                if (map[i] != null && IsStructurallyEqual(map[i], targetInterface))
                    return true;
            }
        }
        else
        {
            // Kernel type - InterfaceMapEntry array
            InterfaceMapEntry* map = GetInterfaceMapPtr();
            for (int i = 0; i < _usNumInterfaces; i++)
            {
                if (map[i].InterfaceMT == targetInterface)
                    return true;
                if (map[i].InterfaceMT != null && IsStructurallyEqual(map[i].InterfaceMT, targetInterface))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if two MethodTables represent structurally the same type.
    /// This handles cases where AOT and JIT create different MTs for the same type.
    /// For generic types, compares hash code (encodes def token) and type arguments.
    /// </summary>
    private static bool IsStructurallyEqual(MethodTable* mt1, MethodTable* mt2)
    {
        if (mt1 == null || mt2 == null)
            return mt1 == mt2;

        if (mt1 == mt2)
            return true;

        // Skip if either is null or not a valid MT (basic sanity check)
        if ((ulong)mt1 < 0x10000 || (ulong)mt2 < 0x10000)
            return false;

        // For interface dispatch, we need to find an interface in the object's map
        // that has the same method layout as the target interface.
        // The key comparison is vtable slot count - if they match, the interface
        // likely has methods at the same positions.
        //
        // Note: This is a heuristic. For precise matching, we'd need to compare
        // the actual interface definition token. But for generic interfaces like
        // IList<Exception> vs IList<Exception>, if the slot counts match and
        // the type arguments are compatible, we can use this interface.

        // Compare vtable slot count - same interface should have same number of methods
        // If slot counts differ, check if we should use hash code instead
        // AOT interface MTs often have malformed slot counts (1 instead of actual)
        if (mt1->_usNumVtableSlots != mt2->_usNumVtableSlots)
        {
            // Try hash code comparison for AOT interfaces with malformed slots
            if (mt1->_uHashCode != 0 && mt2->_uHashCode != 0 && mt1->_uHashCode == mt2->_uHashCode)
            {
                return true;
            }
            return false;
        }

        // Detect if both MTs are from the same "world" (AOT or Kernel)
        // AOT types are loaded from boot image at high addresses (0x1DA00000+)
        // Kernel-created types are in heap at low addresses (typically < 0x02000000)
        // Note: We can't use HasDispatchMap for interfaces since they don't have dispatch maps
        const ulong AotBaseAddress = 0x1D000000;  // Conservative - AOT image range
        bool mt1IsAot = (ulong)mt1 >= AotBaseAddress;
        bool mt2IsAot = (ulong)mt2 >= AotBaseAddress;
        bool sameWorld = (mt1IsAot == mt2IsAot);

        // Only compare hash codes when both are from the same world
        // (AOT uses different hash algorithm than kernel)
        bool hash1Valid = mt1->_uHashCode != 0;
        bool hash2Valid = mt2->_uHashCode != 0;

        // Check for generic interface matching based on type arguments FIRST
        // _relatedType stores the first type argument MT for generic types
        // We do this before hash comparison because generic interfaces from different
        // instantiation contexts may have different hash codes but same structure.
        MethodTable* rel1 = mt1->_relatedType;
        MethodTable* rel2 = mt2->_relatedType;

        if (rel1 != null && rel2 != null)
        {
            // Both have type args - compare them thoroughly
            if (rel1 == rel2)
                return true;  // Same type arg MT - definitely same type

            // Different MT pointers - check if they represent the same type
            // Compare base size for type equivalence (compatible sizes)
            if (rel1->_uBaseSize != rel2->_uBaseSize)
            {
                // Type argument size mismatch. This can happen when:
                // 1. AOT interface _relatedType is garbage/not properly set
                // 2. Different class hierarchies have different sizes
                // For generic interface matching, if slot counts matched and one type arg
                // looks valid (size >= 16, i.e. a real reference type), accept the match.
                // Size < 16 suggests garbage or uninitialized _relatedType.
                if (rel1->_uBaseSize >= 16 || rel2->_uBaseSize >= 16)
                {
                    // At least one looks like a valid reference type - accept
                    return true;
                }
                // Both look invalid, reject
                return false;
            }

            // If type args are from same world and have hash codes, they should match
            bool rel1IsAot = (ulong)rel1 >= AotBaseAddress;
            bool rel2IsAot = (ulong)rel2 >= AotBaseAddress;
            if (rel1IsAot == rel2IsAot && rel1->_uHashCode != 0 && rel2->_uHashCode != 0 &&
                rel1->_uHashCode != rel2->_uHashCode)
            {
                return false;
            }

            // Same size type args with compatible hashes - accept
            return true;
        }

        // Cross-world matching: AOT interface vs Kernel interface
        // In this case, we can't rely on hash codes, only on structural similarity
        if ((rel1 != null && rel2 == null) || (rel1 == null && rel2 != null))
        {
            // Mixed case: one has type arg, other doesn't
            // AOT doesn't always set _relatedType for interfaces
            // Accept if slot counts matched AND we're comparing cross-world
            // (i.e., one AOT and one kernel interface)
            if (!sameWorld)
            {
                return true;
            }
            // Same world with type arg asymmetry - reject (different interfaces)
            return false;
        }

        // Neither has type args - this is the NON-GENERIC interface case
        // For non-generic interfaces from the same world with valid hash codes, compare hashes
        // (but only for kernel-created interfaces where we set _uHashCode properly)
        if (sameWorld)
        {
            // Only be strict if BOTH have valid hash codes set
            // AOT interfaces don't always have _uHashCode set properly
            if (hash1Valid && hash2Valid)
            {
                // Same world, both have hash codes - they must match
                return mt1->_uHashCode == mt2->_uHashCode;
            }
            // One or both missing hash codes - fall through to slot count check
        }

        // Cross-world non-generic interfaces: compare hash codes if available
        // If both have valid hash codes, they must match for same type
        if (hash1Valid && hash2Valid)
        {
            return mt1->_uHashCode == mt2->_uHashCode;
        }

        // One or both don't have hash codes - use slot count as heuristic for cross-world
        // This is still risky but necessary for AOT/JIT interop
        return true;
    }

    /// <summary>
    /// Get the vtable slot for an interface method.
    /// For AOT types: uses dispatch map.
    /// For kernel types: uses InterfaceMapEntry.StartSlot + methodSlot.
    /// </summary>
    /// <param name="interfaceMT">The interface MethodTable.</param>
    /// <param name="methodSlot">The method slot within the interface (0-based).</param>
    /// <returns>The implementation vtable slot index, or -1 if not found.</returns>
    public int GetInterfaceMethodSlot(MethodTable* interfaceMT, int methodSlot)
    {
        // Find the interface index in the interface map
        int interfaceIndex = FindVariantCompatibleInterfaceIndex(interfaceMT);

        DebugConsole.Write("[GetIfaceSlot] ifaceMT=0x");
        DebugConsole.WriteHex((ulong)interfaceMT);
        DebugConsole.Write(" methodSlot=");
        DebugConsole.WriteDecimal((uint)methodSlot);
        DebugConsole.Write(" ifaceIdx=");
        DebugConsole.WriteDecimal((uint)interfaceIndex);
        DebugConsole.Write(" hasDispMap=");
        DebugConsole.Write(HasDispatchMap ? "Y" : "N");
        DebugConsole.WriteLine();

        if (interfaceIndex < 0)
        {
            // Interface not found in map - try fallback search using dispatch map
            // This handles cases where AOT and JIT create different MTs for the same interface
            if (HasDispatchMap && interfaceMT != null)
            {
                DispatchMap* dispatchMap = GetDispatchMap();
                if (dispatchMap != null)
                {
                    // Search all dispatch map entries for one with the target method slot
                    // For generic interface dispatch, we can use slot count as a heuristic
                    uint targetSlotCount = interfaceMT->_usNumVtableSlots;
                    uint numEntries = dispatchMap->NumStandardEntries + dispatchMap->NumDefaultEntries;

                    // First try: find entry with matching method slot and interface slot count
                    for (uint i = 0; i < numEntries; i++)
                    {
                        DispatchMapEntry* entry = dispatchMap->GetEntry((int)i);
                        if (entry->_usInterfaceMethodSlot == methodSlot)
                        {
                            int ifIdx = entry->_usInterfaceIndex;
                            if (ifIdx >= 0 && ifIdx < _usNumInterfaces)
                            {
                                MethodTable* mapIface = GetInterface(ifIdx);
                                if (mapIface != null)
                                {
                                    // Check if slot count matches
                                    if (mapIface->_usNumVtableSlots == targetSlotCount)
                                    {
                                        return entry->_usImplMethodSlot;
                                    }
                                }
                            }
                        }
                    }

                    // Second try: if slot count match failed, just find any entry with method slot
                    // This is riskier but may work for simple cases
                    for (uint i = 0; i < numEntries; i++)
                    {
                        DispatchMapEntry* entry = dispatchMap->GetEntry((int)i);
                        if (entry->_usInterfaceMethodSlot == methodSlot)
                        {
                            return entry->_usImplMethodSlot;
                        }
                    }
                }
            }

            return -1;
        }

        // AOT types use dispatch map
        if (HasDispatchMap)
        {
            DispatchMap* dispatchMap = GetDispatchMap();
            if (dispatchMap != null)
            {
                // Search the dispatch map for matching entry
                uint numEntries = dispatchMap->NumStandardEntries + dispatchMap->NumDefaultEntries;

                for (uint i = 0; i < numEntries; i++)
                {
                    DispatchMapEntry* entry = dispatchMap->GetEntry((int)i);
                    if (entry->_usInterfaceIndex == interfaceIndex &&
                        entry->_usInterfaceMethodSlot == methodSlot)
                    {
                        return entry->_usImplMethodSlot;
                    }
                }
            }
            // Dispatch map lookup failed
            return -1;
        }

        // Kernel types use InterfaceMapEntry with StartSlot
        // BUT: A single method can implement multiple interface methods (e.g., List<T>.Count
        // implements ICollection<T>.Count, IReadOnlyCollection<T>.Count, ICollection.Count).
        // The vtable layout assigns sequential slots to interfaces, but all implementations
        // point to the same underlying method. We need to find the implementation slot.
        InterfaceMapEntry* map = GetInterfaceMapPtr();
        int interfaceSlot = map[interfaceIndex].StartSlot + methodSlot;

        DebugConsole.Write("  kernel dispatch: startSlot=");
        DebugConsole.WriteDecimal((uint)map[interfaceIndex].StartSlot);
        DebugConsole.Write(" + methodSlot=");
        DebugConsole.WriteDecimal((uint)methodSlot);
        DebugConsole.Write(" = ");
        DebugConsole.WriteDecimal((uint)interfaceSlot);
        DebugConsole.Write(" mapIfaceMT=0x");
        DebugConsole.WriteHex((ulong)map[interfaceIndex].InterfaceMT);
        DebugConsole.WriteLine();

        // NOTE: We return the calculated interface slot directly. If a method implements
        // multiple interfaces (e.g., ICollection.Count and IReadOnlyCollection.Count),
        // each interface slot in the vtable will point to the same compiled code.
        // This approach correctly handles explicit interface implementations where
        // different interfaces with methods of the same NAME have DIFFERENT implementations.
        return interfaceSlot;
    }

    /// <summary>
    /// Check if an object of this type can be assigned to a variable of the target type.
    /// Implements the CLR IsAssignableFrom semantics.
    /// </summary>
    /// <param name="targetType">The target type to check assignment compatibility with.</param>
    /// <returns>True if assignment is valid, false otherwise.</returns>
    public bool IsAssignableTo(MethodTable* targetType)
    {
        if (targetType == null)
            return false;

        fixed (MethodTable* self = &this)
        {
            // Same type - always assignable
            if (self == targetType)
                return true;

            // Target is interface - check if we implement it
            if (targetType->IsInterface)
            {
                // Check this type's interface list
                if (ImplementsInterface(targetType))
                    return true;

                // Also check parent types' interfaces
                MethodTable* parent = GetParentType();
                while (parent != null)
                {
                    if (parent->ImplementsInterface(targetType))
                        return true;
                    parent = parent->GetParentType();
                }

                // Check variance compatibility for generic interfaces
                // Iterate all implemented interfaces and see if any are variant-compatible
                int numInterfaces = _usNumInterfaces;
                for (int i = 0; i < numInterfaces; i++)
                {
                    MethodTable* implInterface = GetInterface(i);
                    if (implInterface != null && implInterface->IsVariantCompatibleWith(targetType))
                        return true;
                }

                // Also check parent's implemented interfaces for variance
                parent = GetParentType();
                while (parent != null)
                {
                    int parentNumInterfaces = parent->_usNumInterfaces;
                    for (int i = 0; i < parentNumInterfaces; i++)
                    {
                        MethodTable* implInterface = parent->GetInterface(i);
                        if (implInterface != null && implInterface->IsVariantCompatibleWith(targetType))
                            return true;
                    }
                    parent = parent->GetParentType();
                }

                return false;
            }

            // Target is a class - walk the parent chain
            MethodTable* current = GetParentType();
            while (current != null)
            {
                if (current == targetType)
                    return true;
                current = current->GetParentType();
            }

            // Array covariance: T[] is assignable to U[] if T is assignable to U (for reference types only)
            // This allows string[] to be assigned to object[], for example.
            if (IsArray && targetType->IsArray)
            {
                MethodTable* sourceElem = GetArrayElementType();
                MethodTable* targetElem = targetType->GetArrayElementType();

                // Both element types must be reference types for covariance
                // (int[] is NOT assignable to object[] - value type arrays are invariant)
                if (sourceElem != null && targetElem != null &&
                    sourceElem->IsReferenceType && targetElem->IsReferenceType)
                {
                    // Recursive check: is source element assignable to target element?
                    return sourceElem->IsAssignableTo(targetElem);
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Check if this generic interface instantiation is variant-compatible with the target.
    /// For covariant (out T): source T must be assignable to target T
    /// For contravariant (in T): target T must be assignable to source T
    /// </summary>
    public bool IsVariantCompatibleWith(MethodTable* targetMT)
    {
        fixed (MethodTable* self = &this)
        {
            // Both must be interfaces
            if (!IsInterface || !targetMT->IsInterface)
                return false;

            // Get generic definitions
            MethodTable* sourceDefMT = AssemblyLoader.GetGenericDefinitionMT(self);
            MethodTable* targetDefMT = AssemblyLoader.GetGenericDefinitionMT(targetMT);

            // Must be same generic definition (both non-null)
            if (sourceDefMT == null || targetDefMT == null || sourceDefMT != targetDefMT)
            {
                // Don't log - this happens frequently for non-matching interfaces
                return false;
            }

            // Definition must have variance
            if (!sourceDefMT->HasVariance)
                return false;

            // Get variance flags from definition's _uHashCode (bits 0-1 for first param)
            uint variance = sourceDefMT->_uHashCode & 0x3;

            // Get type arguments from _relatedType (first type arg)
            MethodTable* sourceArg = _relatedType;
            MethodTable* targetArg = targetMT->_relatedType;

            if (sourceArg == null || targetArg == null)
                return false;

            // Same type args - always compatible
            if (sourceArg == targetArg)
                return true;

            if (variance == 1)  // Covariant (out T): ICovariant<Derived> -> ICovariant<Base>
                return sourceArg->IsAssignableTo(targetArg);
            else if (variance == 2)  // Contravariant (in T): IContravariant<Base> -> IContravariant<Derived>
                return targetArg->IsAssignableTo(sourceArg);

            // Invariant - must be exact match (already checked above)
            return false;
        }
    }

    /// <summary>
    /// Calculate the total size of a MethodTable with the given vtable and interface counts.
    /// Note: This only includes the fixed portion. Optional fields (dispatch map, etc.) are extra.
    /// </summary>
    public static int CalculateTotalSize(int numVtableSlots, int numInterfaces)
    {
        // Interface map is just MethodTable** (8 bytes per interface)
        return HeaderSize + (numVtableSlots * sizeof(nint)) + (numInterfaces * sizeof(nint));
    }
}

/// <summary>
/// Static helper functions for type operations called by JIT code.
/// </summary>
public static unsafe class TypeHelpers
{
    /// <summary>
    /// Check if objectMT is assignable to targetMT.
    /// Called by JIT-generated castclass/isinst code.
    /// Signature: bool IsAssignableTo(MethodTable* objectMT, MethodTable* targetMT)
    /// </summary>
    public static bool IsAssignableTo(MethodTable* objectMT, MethodTable* targetMT)
    {
        if (objectMT == null || targetMT == null)
            return false;

        return objectMT->IsAssignableTo(targetMT);
    }

    /// <summary>
    /// Find the vtable slot for an interface method on an object.
    /// Called by JIT-generated interface callvirt code.
    /// Signature: int GetInterfaceSlot(MethodTable* objectMT, MethodTable* interfaceMT, int methodIndex)
    /// Returns -1 if interface not implemented.
    /// </summary>
    public static int GetInterfaceSlot(MethodTable* objectMT, MethodTable* interfaceMT, int methodIndex)
    {
        if (objectMT == null || interfaceMT == null)
            return -1;

        return objectMT->GetInterfaceMethodSlot(interfaceMT, methodIndex);
    }

    /// <summary>
    /// Get the function pointer for an interface method call.
    /// Called by JIT-generated interface callvirt code.
    /// Signature: void* GetInterfaceMethod(void* obj, MethodTable* interfaceMT, int methodIndex)
    /// </summary>
    public static void* GetInterfaceMethod(void* obj, MethodTable* interfaceMT, int methodIndex)
    {
        DebugConsole.Write("[GetIfaceMethod] obj=0x");
        DebugConsole.WriteHex((ulong)obj);
        DebugConsole.Write(" iface=0x");
        DebugConsole.WriteHex((ulong)interfaceMT);
        DebugConsole.Write(" idx=");
        DebugConsole.WriteDecimal((uint)methodIndex);
        DebugConsole.WriteLine();

        if (obj == null)
            return null;

        MethodTable* objectMT = *(MethodTable**)obj;
        int slot = objectMT->GetInterfaceMethodSlot(interfaceMT, methodIndex);

        DebugConsole.Write("  objMT=0x");
        DebugConsole.WriteHex((ulong)objectMT);
        DebugConsole.Write(" slot=");
        DebugConsole.WriteDecimal((uint)slot);
        DebugConsole.Write(" numVtableSlots=");
        DebugConsole.WriteDecimal((uint)objectMT->_usNumVtableSlots);
        DebugConsole.WriteLine();

        // Dump vtable entries around the slot
        nint* vtable = (nint*)((byte*)objectMT + MethodTable.HeaderSize);
        DebugConsole.Write("  vtable[");
        DebugConsole.WriteDecimal((uint)slot);
        DebugConsole.Write("]=0x");
        DebugConsole.WriteHex((ulong)vtable[slot]);
        DebugConsole.WriteLine();

        if (slot < 0)
        {
            DebugConsole.Write("[GetIfaceMethod] obj MT=0x");
            DebugConsole.WriteHex((ulong)objectMT);
            DebugConsole.Write(" iface MT=0x");
            DebugConsole.WriteHex((ulong)interfaceMT);
            DebugConsole.Write(" idx=");
            DebugConsole.WriteDecimal((uint)methodIndex);
            DebugConsole.Write(" slot=-1 FAIL!");
            DebugConsole.WriteLine();
            DebugConsole.Write("  obj numIfaces=");
            DebugConsole.WriteDecimal((uint)objectMT->_usNumInterfaces);
            DebugConsole.Write(" HasDispatchMap=");
            DebugConsole.Write(objectMT->HasDispatchMap ? "Y" : "N");
            DebugConsole.WriteLine();

            // Dump object's interface map
            if (!objectMT->HasDispatchMap)
            {
                InterfaceMapEntry* map = objectMT->GetInterfaceMapPtr();
                for (int i = 0; i < objectMT->_usNumInterfaces && i < 5; i++)
                {
                    DebugConsole.Write("  iface[");
                    DebugConsole.WriteDecimal((uint)i);
                    DebugConsole.Write("] MT=0x");
                    DebugConsole.WriteHex((ulong)map[i].InterfaceMT);
                    DebugConsole.Write(" startSlot=");
                    DebugConsole.WriteDecimal((uint)map[i].StartSlot);
                    DebugConsole.WriteLine();
                }
            }
            return null;
        }

        // Ensure the vtable slot is compiled (may be lazy-compiled)
        // Use the return value which handles out-of-bounds vtable slots (e.g., for sealed types like String)
        nint methodCode = JitStubs.EnsureVtableSlotCompiled((nint)obj, (short)slot);

        return (void*)methodCode;
    }

    /// <summary>
    /// Resolve interface method for AOT dynamic dispatch.
    /// Called by RhpInitialDynamicInterfaceDispatch assembly stub.
    /// This is the entry point that parses the dispatch cell.
    /// </summary>
    /// <param name="obj">The object to dispatch on (this pointer)</param>
    /// <param name="pDispatchCell">Pointer to the InterfaceDispatchCell</param>
    [UnmanagedCallersOnly(EntryPoint = "RhpResolveInterfaceMethod")]
    public static void* RhpResolveInterfaceMethod(void* obj, InterfaceDispatchCell* pDispatchCell)
    {
        if (obj == null || pDispatchCell == null)
            return null;

        // Parse the dispatch cell to get interface type and slot
        DispatchCellInfo cellInfo = pDispatchCell->GetDispatchCellInfo();

        DebugConsole.Write("[RhpResolve] obj=0x");
        DebugConsole.WriteHex((ulong)obj);
        DebugConsole.Write(" cell=0x");
        DebugConsole.WriteHex((ulong)pDispatchCell);
        DebugConsole.Write(" type=");
        DebugConsole.WriteDecimal((uint)cellInfo.CellType);
        DebugConsole.WriteLine();

        if (cellInfo.CellType == DispatchCellType.VTableOffset)
        {
            // Direct vtable offset - just read from the vtable
            MethodTable* objectMT = *(MethodTable**)obj;
            nint* vtable = (nint*)((byte*)objectMT + MethodTable.HeaderSize);
            int slotIndex = (int)(cellInfo.VTableOffset / (uint)sizeof(nint));
            DebugConsole.Write("  VTableOffset=");
            DebugConsole.WriteDecimal(cellInfo.VTableOffset);
            DebugConsole.Write(" slotIdx=");
            DebugConsole.WriteDecimal((uint)slotIndex);
            DebugConsole.Write(" result=0x");
            DebugConsole.WriteHex((ulong)vtable[slotIndex]);
            DebugConsole.WriteLine();
            return (void*)vtable[slotIndex];
        }
        else if (cellInfo.CellType == DispatchCellType.InterfaceAndSlot)
        {
            // Interface dispatch - use dispatch map lookup
            MethodTable* interfaceMT = cellInfo.InterfaceType;
            int methodSlot = cellInfo.InterfaceSlot;

            DebugConsole.Write("  IfaceAndSlot: iface=0x");
            DebugConsole.WriteHex((ulong)interfaceMT);
            DebugConsole.Write(" methodSlot=");
            DebugConsole.WriteDecimal((uint)methodSlot);
            DebugConsole.WriteLine();

            if (interfaceMT == null)
                return null;

            void* result = GetInterfaceMethod(obj, interfaceMT, methodSlot);
            DebugConsole.Write("  GetInterfaceMethod result=0x");
            DebugConsole.WriteHex((ulong)result);
            DebugConsole.WriteLine();
            return result;
        }

        return null;
    }

}
