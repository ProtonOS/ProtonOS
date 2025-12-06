// ProtonOS System.Runtime - EqualityComparer<T>
// Provides a base class for implementations of IEqualityComparer<T>.

namespace System.Collections.Generic
{
    /// <summary>
    /// Provides a base class for implementations of the IEqualityComparer{T} generic interface.
    /// </summary>
    public abstract class EqualityComparer<T> : IEqualityComparer<T>, IEqualityComparer
    {
        private static volatile EqualityComparer<T>? _default;

        /// <summary>
        /// Returns a default equality comparer for the type specified by the generic argument.
        /// </summary>
        public static EqualityComparer<T> Default
        {
            get
            {
                if (_default == null)
                {
                    _default = CreateComparer();
                }
                return _default;
            }
        }

        private static EqualityComparer<T> CreateComparer()
        {
            // For now, use the generic object comparer
            // In a full implementation, we would check for IEquatable<T> and use specialized comparers
            return new ObjectEqualityComparer<T>();
        }

        /// <summary>
        /// When overridden in a derived class, determines whether two objects of type T are equal.
        /// </summary>
        public abstract bool Equals(T? x, T? y);

        /// <summary>
        /// When overridden in a derived class, serves as a hash function for the specified object.
        /// </summary>
        public abstract int GetHashCode(T obj);

        bool IEqualityComparer.Equals(object? x, object? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            if (x is T tx && y is T ty) return Equals(tx, ty);
            throw new ArgumentException("Invalid type");
        }

        int IEqualityComparer.GetHashCode(object obj)
        {
            if (obj == null) return 0;
            if (obj is T t) return GetHashCode(t);
            throw new ArgumentException("Invalid type");
        }
    }

    /// <summary>
    /// Default equality comparer that uses Object.Equals and Object.GetHashCode.
    /// </summary>
    internal sealed class ObjectEqualityComparer<T> : EqualityComparer<T>
    {
        public override bool Equals(T? x, T? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Equals(y);
        }

        public override int GetHashCode(T obj)
        {
            return obj?.GetHashCode() ?? 0;
        }
    }
}
