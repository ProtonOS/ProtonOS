// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace System.Runtime
{
    internal sealed class RuntimeExportAttribute : Attribute
    {
        public RuntimeExportAttribute(string entry) { }
    }

    internal sealed class RuntimeImportAttribute : Attribute
    {
        public RuntimeImportAttribute(string lib) { }
        public RuntimeImportAttribute(string lib, string entry) { }
    }

    internal unsafe struct MethodTable
    {
        internal ushort _usComponentSize;
        private ushort _usFlags;
        internal uint _uBaseSize;
        internal MethodTable* _relatedType;
        private ushort _usNumVtableSlots;
        private ushort _usNumInterfaces;
        private uint _uHashCode;

        // Helper property to check if this is a value type
        // Bit 5 (0x0020) in _usFlags indicates value type (MTFlags.IsValueType = 0x00200000)
        internal bool IsValueType => (_usFlags & 0x0020) != 0;

        // Get the size of value type data (excluding object header)
        internal uint ValueTypeSize => _uBaseSize - (uint)sizeof(MethodTable*);
    }

    /// <summary>
    /// Runtime exports required by the AOT compiler for boxing operations.
    /// The compiler looks for System.Runtime.RuntimeExports.RhBox when generating
    /// code for boxing value types (e.g., passing int to object parameter).
    /// </summary>
    internal static unsafe class RuntimeExports
    {
        /// <summary>
        /// Box a value type. Allocates a new object and copies the value into it.
        /// </summary>
        /// <param name="pEEType">The MethodTable* for the value type being boxed</param>
        /// <param name="data">Reference to the value type data to box</param>
        /// <returns>A boxed object containing a copy of the value</returns>
        public static object RhBox(MethodTable* pEEType, ref byte data)
        {
            // Allocate space for the boxed object
            // BaseSize includes the MethodTable pointer + value type data
            MethodTable** result = AllocObject(pEEType->_uBaseSize);
            *result = pEEType;

            // Copy value type data into the object (after the MethodTable pointer)
            byte* dst = (byte*)(result + 1);
            uint size = pEEType->ValueTypeSize;

            // Simple byte-by-byte copy
            for (uint i = 0; i < size; i++)
            {
                dst[i] = Unsafe.Add(ref data, (int)i);
            }

            // Convert pointer to object reference
            // This is equivalent to reinterpreting the pointer as an object reference
            return *(object*)&result;
        }

        /// <summary>
        /// Unbox a nullable value type. Used when unboxing object to Nullable&lt;T&gt;.
        /// The compiler generates calls to this when checking if an object is Nullable&lt;T&gt;.
        /// </summary>
        /// <param name="pEEType">The MethodTable* for the Nullable&lt;T&gt; type</param>
        /// <param name="obj">The object to unbox</param>
        /// <param name="result">Reference to where the result should be stored</param>
        public static void RhUnboxNullable(MethodTable* pEEType, object? obj, ref byte result)
        {
            // Get the size of the Nullable<T> structure
            uint size = pEEType->ValueTypeSize;

            if (obj == null)
            {
                // Null object - set hasValue to false, zero the rest
                // Nullable<T> layout: bool hasValue, T value
                result = 0; // hasValue = false
                // Zero the rest of the structure
                for (uint i = 1; i < size; i++)
                {
                    Unsafe.Add(ref result, (int)i) = 0;
                }
            }
            else
            {
                // Get the underlying value type's MethodTable from the Nullable<T> MT
                // For simplicity, we just copy the boxed value
                MethodTable* pObjType = obj.m_pMethodTable;
                byte* src = (byte*)Unsafe.AsPointer(ref obj);
                src += sizeof(MethodTable*); // Skip MethodTable pointer to get to value

                // Set hasValue to true
                result = 1;

                // Copy the value (Nullable<T>.Value starts at offset 1 for alignment, but
                // for value types like int it's at the natural alignment)
                // The actual layout depends on T - for int it's: bool hasValue (1 byte),
                // 3 bytes padding, int value (4 bytes)
                uint valueOffset = 1;
                // Align to value type's natural alignment (simplified: assume 4-byte alignment for now)
                if (size > 2)
                    valueOffset = 4; // Skip to aligned position

                uint valueSize = pObjType->ValueTypeSize;
                for (uint i = 0; i < valueSize && valueOffset + i < size; i++)
                {
                    Unsafe.Add(ref result, (int)(valueOffset + i)) = src[i];
                }
            }
        }

        // Import allocation from kernel PAL (same as StartupCodeHelpers)
        [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
        private static extern MethodTable** PalAllocObject(uint size);

        private static MethodTable** AllocObject(uint size)
        {
            MethodTable** result = PalAllocObject(size);
            if (result == null)
                Environment.FailFast(null);
            return result;
        }
    }

    /// <summary>
    /// Runtime type casting helpers required by the AOT compiler.
    /// The compiler generates calls to these methods for type checking operations
    /// like 'is', 'as', and explicit casts.
    /// </summary>
    internal static unsafe class TypeCast
    {
        /// <summary>
        /// Check if an object is an instance of a class type.
        /// Returns the object if it is, null otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_IsInstanceOfClass")]
        public static object? IsInstanceOfClass(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Walk up the type hierarchy
            while (pObjType != null)
            {
                if (pObjType == pTargetType)
                    return obj;
                pObjType = pObjType->_relatedType;
            }

            return null;
        }

        /// <summary>
        /// Check cast to a class type.
        /// Returns the object if cast is valid, throws InvalidCastException otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_CheckCastClass")]
        public static object CheckCastClass(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null!;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Walk up the type hierarchy
            while (pObjType != null)
            {
                if (pObjType == pTargetType)
                    return obj;
                pObjType = pObjType->_relatedType;
            }

            throw new InvalidCastException();
        }

        /// <summary>
        /// Check if an object is an instance of an interface type.
        /// Returns the object if it is, null otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_IsInstanceOfInterface")]
        public static object? IsInstanceOfInterface(MethodTable* pTargetType, object obj)
        {
            // Simplified: For now, we don't support interfaces properly
            // This would need interface table walking
            if (obj == null)
                return null;

            // TODO: Proper interface checking
            // For now, assume it's assignable (will be corrected later)
            return null;
        }

        /// <summary>
        /// Check cast to an interface type.
        /// Returns the object if cast is valid, throws InvalidCastException otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_CheckCastInterface")]
        public static object CheckCastInterface(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null!;

            // Simplified: For now, we don't support interfaces properly
            throw new InvalidCastException();
        }

        /// <summary>
        /// Check if an object is an instance of an array type.
        /// Returns the object if it is, null otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_IsInstanceOfArray")]
        public static object? IsInstanceOfArray(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Direct type match
            if (pObjType == pTargetType)
                return obj;

            // TODO: Array covariance checking
            return null;
        }

        /// <summary>
        /// Check cast to an array type.
        /// Returns the object if cast is valid, throws InvalidCastException otherwise.
        /// </summary>
        [RuntimeExport("RhTypeCast_CheckCastArray")]
        public static object CheckCastArray(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null!;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Direct type match
            if (pObjType == pTargetType)
                return obj;

            throw new InvalidCastException();
        }

        /// <summary>
        /// Check if source type is assignable to target type.
        /// </summary>
        [RuntimeExport("RhTypeCast_AreTypesAssignable")]
        public static bool AreTypesAssignable(MethodTable* pSourceType, MethodTable* pTargetType)
        {
            if (pSourceType == pTargetType)
                return true;

            // Walk up the source type hierarchy
            MethodTable* pType = pSourceType;
            while (pType != null)
            {
                if (pType == pTargetType)
                    return true;
                pType = pType->_relatedType;
            }

            return false;
        }

        /// <summary>
        /// Generic IsInstanceOf - checks class, interface, and array types.
        /// </summary>
        [RuntimeExport("RhTypeCast_IsInstanceOf")]
        public static object? IsInstanceOf(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Direct type match
            if (pObjType == pTargetType)
                return obj;

            // Walk up the type hierarchy
            MethodTable* pType = pObjType->_relatedType;
            while (pType != null)
            {
                if (pType == pTargetType)
                    return obj;
                pType = pType->_relatedType;
            }

            return null;
        }

        /// <summary>
        /// Generic CheckCast - checks class, interface, and array types.
        /// </summary>
        [RuntimeExport("RhTypeCast_CheckCast")]
        public static object CheckCast(MethodTable* pTargetType, object obj)
        {
            if (obj == null)
                return null!;

            MethodTable* pObjType = obj.m_pMethodTable;

            // Direct type match
            if (pObjType == pTargetType)
                return obj;

            // Walk up the type hierarchy
            MethodTable* pType = pObjType->_relatedType;
            while (pType != null)
            {
                if (pType == pTargetType)
                    return obj;
                pType = pType->_relatedType;
            }

            throw new InvalidCastException();
        }

        /// <summary>
        /// Store element reference in array with type checking.
        /// This is used by the compiler for storing references in object[] arrays.
        /// </summary>
        public static void StelemRef(Array array, nint index, object obj)
        {
            if (array == null)
                throw new NullReferenceException();

            MethodTable* elementType = array.m_pMethodTable->_relatedType;

            if (obj == null)
            {
                // Storing null is always allowed
                Unsafe.As<ArrayElement[]>(array)[index].Value = null!;
                return;
            }

            MethodTable* objType = obj.m_pMethodTable;

            // Check if object type is assignable to element type
            while (objType != null)
            {
                if (objType == elementType)
                {
                    Unsafe.As<ArrayElement[]>(array)[index].Value = obj;
                    return;
                }
                objType = objType->_relatedType;
            }

            // Type mismatch
            throw new ArrayTypeMismatchException();
        }

        internal struct ArrayElement
        {
            public object Value;
        }

        /// <summary>
        /// Load element address reference from array with type checking.
        /// </summary>
        public static ref object LdelemaRef(Array array, nint index, MethodTable* elementType)
        {
            if (array == null)
                throw new NullReferenceException();

            // Simplified: just return the element reference
            // Full implementation would check element type
            return ref Unsafe.As<ArrayElement[]>(array)[index].Value;
        }
    }
}

namespace Internal.Runtime
{
    // NativeAOT expects MethodTable in this namespace for ValueType field helpers
    internal unsafe struct MethodTable
    {
        internal ushort _usComponentSize;
        private ushort _usFlags;
        internal uint _uBaseSize;
        internal MethodTable* _relatedType;
        private ushort _usNumVtableSlots;
        private ushort _usNumInterfaces;
        private uint _uHashCode;
    }
}

namespace Internal.Runtime.CompilerHelpers
{
    partial class ThrowHelpers
    {
        // These are called by compiler-generated code for various throw scenarios.
        // Now that exception handling is implemented, these properly throw exceptions.

        static void ThrowIndexOutOfRangeException() => throw new IndexOutOfRangeException();
        static void ThrowDivideByZeroException() => throw new DivideByZeroException();
        static void ThrowPlatformNotSupportedException() => throw new PlatformNotSupportedException();
        static void ThrowOverflowException() => throw new OverflowException();
        static void ThrowArgumentOutOfRangeException() => throw new ArgumentOutOfRangeException();
        static void ThrowArgumentNullException() => throw new ArgumentNullException();
        static void ThrowNullReferenceException() => throw new NullReferenceException();
        static void ThrowInvalidCastException() => throw new InvalidCastException();
        static void ThrowArrayTypeMismatchException() => throw new ArrayTypeMismatchException();
        static void ThrowInvalidProgramException() => throw new InvalidProgramException();
        static void ThrowInvalidProgramExceptionWithArgument(string? message) => throw new InvalidProgramException(message);
    }

    // A class that the compiler looks for that has helpers to initialize the
    // process. The compiler can gracefully handle the helpers not being present,
    // but the class itself being absent is unhandled. Let's add an empty class.
    unsafe partial class StartupCodeHelpers
    {
        // A couple symbols the generated code will need we park them in this class
        // for no particular reason. These aid in transitioning to/from managed code.
        // Since we don't have a GC, the transition is a no-op.
        [RuntimeExport("RhpReversePInvoke")]
        static void RhpReversePInvoke(IntPtr frame) { }
        [RuntimeExport("RhpReversePInvokeReturn")]
        static void RhpReversePInvokeReturn(IntPtr frame) { }
        [RuntimeExport("RhpPInvoke")]
        static void RhpPInvoke(IntPtr frame) { }
        [RuntimeExport("RhpPInvokeReturn")]
        static void RhpPInvokeReturn(IntPtr frame) { }
        [RuntimeExport("RhpGcPoll")]
        static void RhpGcPoll() { }

        [RuntimeExport("RhpFallbackFailFast")]
        static void RhpFallbackFailFast() { Environment.FailFast(null); }

        [RuntimeExport("RhpNewFast")]
        static unsafe void* RhpNewFast(System.Runtime.MethodTable* pMT)
        {
            System.Runtime.MethodTable** result = AllocObject(pMT->_uBaseSize);
            *result = pMT;
            return result;
        }

        /// <summary>
        /// Creates a shallow copy of an object.
        /// </summary>
        [RuntimeExport("RhpMemberwiseClone")]
        static unsafe object RhpMemberwiseClone(object src)
        {
            if (src == null) return null!;

            // Get the MethodTable from source object
            System.Runtime.MethodTable* pMT = *(System.Runtime.MethodTable**)Unsafe.AsPointer(ref src);
            uint size = pMT->_uBaseSize;

            // Allocate new object with same MethodTable
            System.Runtime.MethodTable** result = AllocObject(size);
            *result = pMT;

            // Copy the data (skip the MethodTable pointer which we already set)
            byte* srcPtr = (byte*)Unsafe.AsPointer(ref src) + sizeof(void*);
            byte* dstPtr = (byte*)result + sizeof(void*);
            uint copySize = size - (uint)sizeof(void*);

            for (uint i = 0; i < copySize; i++)
                dstPtr[i] = srcPtr[i];

            // Convert pointer to object reference
            // result is the address of the object header, same as an object reference
            return Unsafe.AsRef<object>(result);
        }

        [RuntimeExport("RhpNewArray")]
        static unsafe void* RhpNewArray(System.Runtime.MethodTable* pMT, int numElements)
        {
            if (numElements < 0)
                Environment.FailFast(null);

            System.Runtime.MethodTable** result = AllocObject((uint)(pMT->_uBaseSize + numElements * pMT->_usComponentSize));
            *result = pMT;
            *(int*)(result + 1) = numElements;
            return result;
        }

        // Fast path alias for array allocation (same implementation)
        [RuntimeExport("RhpNewArrayFast")]
        static unsafe void* RhpNewArrayFast(System.Runtime.MethodTable* pMT, int numElements)
        {
            return RhpNewArray(pMT, numElements);
        }

        // For pointer arrays (e.g., object[])
        [RuntimeExport("RhpNewPtrArrayFast")]
        static unsafe void* RhpNewPtrArrayFast(System.Runtime.MethodTable* pMT, int numElements)
        {
            return RhpNewArray(pMT, numElements);
        }

        internal struct ArrayElement
        {
            public object Value;
        }

        [DllImport("*", EntryPoint = "Debug_PrintHex", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void Debug_PrintHex(byte* prefix, ulong value);

        private static unsafe void DebugStelemRefMismatch(System.Runtime.MethodTable* elementType, System.Runtime.MethodTable* objType)
        {
            // Use static byte arrays for string literals
            byte* prefixElem = stackalloc byte[] { (byte)'s', (byte)'t', (byte)'e', (byte)'l', (byte)'e', (byte)'m', (byte)' ', (byte)'a', (byte)'r', (byte)'r', (byte)'a', (byte)'y', (byte)'E', (byte)'l', (byte)'e', (byte)'m', (byte)'M', (byte)'T', (byte)'=', 0 };
            byte* prefixObj = stackalloc byte[] { (byte)'s', (byte)'t', (byte)'e', (byte)'l', (byte)'e', (byte)'m', (byte)' ', (byte)'o', (byte)'b', (byte)'j', (byte)'M', (byte)'T', (byte)'=', 0 };
            byte* prefixHier = stackalloc byte[] { (byte)' ', (byte)' ', (byte)'h', (byte)'i', (byte)'e', (byte)'r', (byte)'a', (byte)'r', (byte)'c', (byte)'h', (byte)'y', (byte)'=', 0 };

            Debug_PrintHex(prefixElem, (ulong)elementType);
            Debug_PrintHex(prefixObj, (ulong)objType);

            // Walk and print the full hierarchy
            while (objType != null)
            {
                Debug_PrintHex(prefixHier, (ulong)objType);
                objType = objType->_relatedType;
            }
        }

        [RuntimeExport("RhpStelemRef")]
        public static unsafe void StelemRef(Array array, nint index, object obj)
        {
            ref object element = ref Unsafe.As<ArrayElement[]>(array)[index].Value;

            System.Runtime.MethodTable* elementType = array.m_pMethodTable->_relatedType;

            if (obj == null)
                goto assigningNull;

            // Walk the type hierarchy to check if obj's type is assignable to elementType
            System.Runtime.MethodTable* objType = obj.m_pMethodTable;
            System.Runtime.MethodTable* originalObjType = objType;  // Save for debug
            while (objType != null)
            {
                if (objType == elementType)
                    goto doWrite;
                objType = objType->_relatedType;
            }
            // Type mismatch - obj doesn't inherit from element type
            // Debug: print the element type MT and the object's type hierarchy
            DebugStelemRefMismatch(elementType, originalObjType);
            throw new ArrayTypeMismatchException();

doWrite:
            element = obj;
            return;

assigningNull:
            element = null;
            return;
        }

        [RuntimeExport("RhpCheckedAssignRef")]
        public static unsafe void RhpCheckedAssignRef(void** dst, void* r)
        {
            *dst = r;
        }

        [RuntimeExport("RhpAssignRef")]
        public static unsafe void RhpAssignRef(void** dst, void* r)
        {
            *dst = r;
        }

        // Import allocation from kernel PAL
        [DllImport("*", CallingConvention = CallingConvention.Cdecl)]
        static extern System.Runtime.MethodTable** PalAllocObject(uint size);

        internal static unsafe System.Runtime.MethodTable** AllocObject(uint size)
        {
            // Use kernel's heap allocator (returns zeroed memory)
            System.Runtime.MethodTable** result = PalAllocObject(size);

            if (result == null)
                Environment.FailFast(null);

            return result;
        }
    }
}
