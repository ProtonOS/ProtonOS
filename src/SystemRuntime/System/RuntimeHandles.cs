// ProtonOS System.Runtime - Runtime Handles
// Handle structures for reflection support.

namespace System
{
    /// <summary>
    /// Represents a handle to the internal metadata representation of a type.
    /// </summary>
    public struct RuntimeTypeHandle
    {
        private readonly nint _value;

        internal RuntimeTypeHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeTypeHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeTypeHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value != right._value;
    }

    /// <summary>
    /// Represents a handle to the internal metadata representation of a method.
    /// The handle encodes (assemblyId &lt;&lt; 32) | methodToken as a single nint.
    /// </summary>
    public struct RuntimeMethodHandle
    {
        private readonly nint _value;

        internal RuntimeMethodHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeMethodHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeMethodHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeMethodHandle left, RuntimeMethodHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeMethodHandle left, RuntimeMethodHandle right) => left._value != right._value;
    }

    /// <summary>
    /// Represents a handle to the internal metadata representation of a field.
    /// The handle encodes (assemblyId &lt;&lt; 32) | fieldToken as a single nint.
    /// </summary>
    public struct RuntimeFieldHandle
    {
        private readonly nint _value;

        internal RuntimeFieldHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeFieldHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeFieldHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeFieldHandle left, RuntimeFieldHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeFieldHandle left, RuntimeFieldHandle right) => left._value != right._value;
    }
}
