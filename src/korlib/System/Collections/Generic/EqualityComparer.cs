// korlib - EqualityComparer<T>
// Provides a base class for implementations of IEqualityComparer<T>.

namespace System.Collections.Generic;

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
        // Use the generic object comparer
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
        // Special case for strings: compare directly without virtual dispatch
        // Cast to object first to avoid JIT issues with pattern matching on T
        object? ox = x;
        object? oy = y;
        if (ox is string sx && oy is string sy)
        {
            if (ReferenceEquals(sx, sy)) return true;
            if (sx.Length != sy.Length) return false;
            for (int i = 0; i < sx.Length; i++)
            {
                if (sx[i] != sy[i]) return false;
            }
            return true;
        }
        // Handle string null comparison
        if (ox is string && oy == null) return false;
        if (oy is string && ox == null) return false;

        // For value types, we can't use virtual dispatch (JIT doesn't support it yet).
        // Instead, use the static Object.Equals(object, object) which our AOT
        // implementation handles correctly by comparing boxed value bytes.
        return object.Equals(x, y);
    }

    public override int GetHashCode(T obj)
    {
        if (obj == null) return 0;

        // Special case for strings: call String.GetHashCode directly
        // because virtual dispatch to String.GetHashCode doesn't work yet
        // (String's vtable doesn't have the correct slots populated)
        // Cast to object first to avoid JIT issues
        object o = obj;
        if (o is string s)
        {
            // Call String's GetHashCode implementation directly
            int hash = 5381;
            for (int i = 0; i < s.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ s[i];
            }
            return hash;
        }

        return obj.GetHashCode();
    }
}
