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
        /// Note: This requires korlib's Type implementation to be included.
        /// </summary>
        public static unsafe Type? GetTargetType(TypedReference value)
        {
            // TODO: Enable when Type.cs is included in korlib build
            // return Type.GetTypeFromHandle(new RuntimeTypeHandle(value._type));
            throw new NotSupportedException("GetTargetType requires korlib Type implementation");
        }

        /// <summary>
        /// Converts the TypedReference to an object by boxing the value.
        /// </summary>
        public static unsafe object? ToObject(TypedReference value)
        {
            if (value._type == 0)
                return null;

            // Get the MethodTable pointer
            void* mt = (void*)value._type;

            // Check if this is a reference type (object pointer)
            // For reference types, _value IS the object reference
            // For value types, _value points to the value data
            bool isValueType = Reflection_IsValueType(mt);

            if (!isValueType)
            {
                // Reference type: _value is the object pointer itself
                return Unsafe.As<nint, object>(ref Unsafe.AsRef<nint>(&value._value));
            }

            // Value type: need to box the value
            int valueSize = Reflection_GetValueSize(mt);

            // Box the value using kernel helper
            void* boxed = Reflection_BoxValue(mt, (void*)value._value, valueSize);
            if (boxed == null)
                return null;

            // Convert void* to object reference
            return Unsafe.As<nint, object>(ref *(nint*)&boxed);
        }

        [DllImport("*", EntryPoint = "Reflection_IsValueType", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe bool Reflection_IsValueType(void* methodTable);

        [DllImport("*", EntryPoint = "Reflection_GetValueSize", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe int Reflection_GetValueSize(void* methodTable);

        [DllImport("*", EntryPoint = "Reflection_BoxValue", CallingConvention = CallingConvention.Cdecl)]
        private static extern unsafe void* Reflection_BoxValue(void* methodTable, void* valueData, int valueSize);

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
