// ProtonOS korlib - TypedReference support for varargs
// Layout must match JIT's mkrefany/refanyval/refanytype implementation

using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace System
{
    /// <summary>
    /// Describes objects that contain both a managed pointer to a location
    /// and a runtime representation of the type that may be stored at that location.
    /// Used for varargs (__arglist) support.
    /// </summary>
    /// <remarks>
    /// Layout (16 bytes on x64):
    ///   +0: nint _value  (pointer to the actual data)
    ///   +8: nint _type   (MethodTable* for the type)
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public ref struct TypedReference
    {
        // These fields must match the JIT's TypedReference layout exactly
        // mkrefany pushes: [Value][Type] (Value at lower address)
        internal readonly nint _value;  // pointer to data
        internal readonly nint _type;   // RuntimeTypeHandle value (MethodTable*)

        /// <summary>
        /// Gets the RuntimeTypeHandle for the type of the referenced value.
        /// </summary>
        public static RuntimeTypeHandle TargetTypeToken(TypedReference value)
        {
            return new RuntimeTypeHandle(value._type);
        }

        /// <summary>
        /// Gets the Type of the referenced value.
        /// </summary>
        public static unsafe Type? GetTargetType(TypedReference value)
        {
            if (value._type == 0)
                return null;
            return Type.GetTypeFromHandle(new RuntimeTypeHandle(value._type));
        }

        /// <summary>
        /// Converts the TypedReference to an object by boxing the value.
        /// </summary>
        public static unsafe object? ToObject(TypedReference value)
        {
            if (value._type == 0)
                return null;

            // Get the MethodTable pointer
            nint mt = value._type;

            // Check if this is a reference type (object pointer)
            // For reference types, _value IS the object reference
            // For value types, _value points to the value data

            // Read flags from MethodTable to determine if value type
            // MT layout: +0 = flags (includes IsValueType bit)
            ushort flags = *(ushort*)(mt + 2);  // _usFlags at offset 2
            bool isValueType = (flags & 0x0004) != 0;  // MTFlags.IsValueType >> 16

            if (!isValueType)
            {
                // Reference type: _value is the object pointer itself
                return Unsafe.As<nint, object>(ref Unsafe.AsRef<nint>(&value._value));
            }

            // Value type: need to box the value
            // Get the size from MethodTable.BaseSize
            uint baseSize = *(uint*)(mt + 4);  // _uBaseSize at offset 4

            // Allocate boxed object (TODO: this would need RhpNewFast)
            // For now, return null for value types - full implementation requires allocator access
            return null;
        }

        /// <summary>
        /// Returns true if this TypedReference is null/empty.
        /// </summary>
        public bool IsNull => _type == 0;

        /// <summary>
        /// Sets the target of this TypedReference. Not supported in this implementation.
        /// </summary>
        public static void SetTypedReference(TypedReference target, object? value)
        {
            throw new NotSupportedException();
        }

        public override bool Equals(object? o)
        {
            throw new NotSupportedException();
        }

        public override int GetHashCode()
        {
            if (_type == 0)
                return 0;
            return _value.GetHashCode();
        }
    }
}
