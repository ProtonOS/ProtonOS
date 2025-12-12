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
// [24 + NumVtableSlots*8]  InterfaceMap[0..NumInterfaces-1] (16 bytes each)
//
// InterfaceMapEntry Layout:
// [0]  InterfaceMT* (8 bytes) - pointer to interface's MethodTable
// [8]  StartSlot (2 bytes) - first vtable slot for this interface's methods
// [10] Padding (6 bytes)

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

    /// <summary>Type has optional fields (MethodTable is variable size).</summary>
    public const uint HasOptionalFields = 0x00040000;

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
}

/// <summary>
/// Entry in the interface map stored after the vtable in MethodTable.
/// Maps an interface type to the starting vtable slot for its methods.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public unsafe struct InterfaceMapEntry
{
    /// <summary>Pointer to the interface's MethodTable.</summary>
    public MethodTable* InterfaceMT;

    /// <summary>Starting vtable slot index for this interface's methods.</summary>
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

    /// <summary>Whether this is a value type (struct/enum).</summary>
    public bool IsValueType => (CombinedFlags & MTFlags.IsValueType) != 0;

    /// <summary>Whether this is Nullable&lt;T&gt; (requires special boxing/unboxing).</summary>
    public bool IsNullable => (CombinedFlags & MTFlags.IsNullable) != 0;

    /// <summary>Whether this is a delegate type (inherits from MulticastDelegate).</summary>
    public bool IsDelegate => (CombinedFlags & MTFlags.IsDelegate) != 0;

    /// <summary>Whether this type has a component size (array or string).</summary>
    public bool HasComponentSize => (CombinedFlags & MTFlags.HasComponentSize) != 0;

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

    /// <summary>
    /// Get a pointer to the interface map (immediately follows vtable).
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
    /// Get the byte offset from the start of the MethodTable to the interface map.
    /// </summary>
    public int GetInterfaceMapOffset()
    {
        return HeaderSize + _usNumVtableSlots * sizeof(nint);
    }

    /// <summary>
    /// Find an interface in the interface map.
    /// </summary>
    /// <param name="interfaceMT">The interface MethodTable to find.</param>
    /// <returns>The InterfaceMapEntry if found, null otherwise.</returns>
    public InterfaceMapEntry* FindInterface(MethodTable* interfaceMT)
    {
        if (_usNumInterfaces == 0)
            return null;

        InterfaceMapEntry* map = GetInterfaceMapPtr();
        for (int i = 0; i < _usNumInterfaces; i++)
        {
            if (map[i].InterfaceMT == interfaceMT)
                return &map[i];
        }
        return null;
    }

    /// <summary>
    /// Check if this type implements the given interface.
    /// </summary>
    public bool ImplementsInterface(MethodTable* interfaceMT)
    {
        return FindInterface(interfaceMT) != null;
    }

    /// <summary>
    /// Get the vtable slot for an interface method.
    /// </summary>
    /// <param name="interfaceMT">The interface MethodTable.</param>
    /// <param name="methodIndex">The method index within the interface (0-based).</param>
    /// <returns>The vtable slot index, or -1 if interface not found.</returns>
    public int GetInterfaceMethodSlot(MethodTable* interfaceMT, int methodIndex)
    {
        InterfaceMapEntry* entry = FindInterface(interfaceMT);
        if (entry == null)
            return -1;
        return entry->StartSlot + methodIndex;
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
    /// Calculate the total size of a MethodTable with the given vtable and interface counts.
    /// </summary>
    public static int CalculateTotalSize(int numVtableSlots, int numInterfaces)
    {
        return HeaderSize + (numVtableSlots * sizeof(nint)) + (numInterfaces * InterfaceMapEntry.Size);
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
        if (obj == null)
            return null;

        MethodTable* objectMT = *(MethodTable**)obj;
        int slot = objectMT->GetInterfaceMethodSlot(interfaceMT, methodIndex);
        if (slot < 0)
        {
            // Debug: interface not found
            DebugConsole.Write("[GetInterfaceMethod] obj=0x");
            DebugConsole.WriteHex((ulong)obj);
            DebugConsole.Write(" objMT=0x");
            DebugConsole.WriteHex((ulong)objectMT);
            DebugConsole.Write(" ifaceMT=0x");
            DebugConsole.WriteHex((ulong)interfaceMT);
            DebugConsole.Write(" idx=");
            DebugConsole.WriteDecimal((uint)methodIndex);
            DebugConsole.Write(" numIfaces=");
            DebugConsole.WriteDecimal(objectMT->_usNumInterfaces);
            DebugConsole.WriteLine(" NOT FOUND");
            return null;
        }

        // Ensure the vtable slot is compiled (may be lazy-compiled)
        JitStubs.EnsureVtableSlotCompiled((nint)obj, (short)slot);

        return (void*)objectMT->GetVtableSlot(slot);
    }
}
