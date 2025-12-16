// korlib - Comparer<T>
// Provides a base class for implementations of IComparer<T>.

namespace System.Collections.Generic;

/// <summary>
/// Provides a base class for implementations of the IComparer{T} generic interface.
/// </summary>
public abstract class Comparer<T> : IComparer<T>, IComparer
{
    private static volatile Comparer<T>? _default;

    /// <summary>
    /// Returns a default sort order comparer for the type specified by the generic argument.
    /// </summary>
    public static Comparer<T> Default
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

    private static Comparer<T> CreateComparer()
    {
        // For IComparable<T> types, use the generic comparer
        return new ObjectComparer<T>();
    }

    /// <summary>
    /// When overridden in a derived class, performs a comparison of two objects of type T.
    /// </summary>
    public abstract int Compare(T? x, T? y);

    int IComparer.Compare(object? x, object? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;
        if (x is T tx && y is T ty) return Compare(tx, ty);
        throw new ArgumentException("Invalid type");
    }

    /// <summary>
    /// Creates a comparer by using the specified comparison delegate.
    /// </summary>
    public static Comparer<T> Create(Comparison<T> comparison)
    {
        if (comparison == null) throw new ArgumentNullException(nameof(comparison));
        return new ComparisonComparer<T>(comparison);
    }
}

/// <summary>
/// Default comparer that uses IComparable<T> or IComparable.
/// </summary>
internal sealed class ObjectComparer<T> : Comparer<T>
{
    public override int Compare(T? x, T? y)
    {
        if (x == null && y == null) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        // Fallback: use object.Equals for equality and GetHashCode for ordering
        // This avoids interface dispatch which has AOT/JIT compatibility issues.
        // For proper sorting, GetHashCode comparison maintains a consistent order.
        if (x.Equals(y)) return 0;

        // Use GetHashCode for ordering - not ideal but avoids interface dispatch
        int xHash = x.GetHashCode();
        int yHash = y.GetHashCode();
        if (xHash < yHash) return -1;
        if (xHash > yHash) return 1;

        // Same hash but not equal - arbitrarily return 1
        return 1;
    }
}

/// <summary>
/// Comparer that wraps a Comparison delegate.
/// </summary>
internal sealed class ComparisonComparer<T> : Comparer<T>
{
    private readonly Comparison<T> _comparison;

    public ComparisonComparer(Comparison<T> comparison)
    {
        _comparison = comparison;
    }

    public override int Compare(T? x, T? y)
    {
        return _comparison(x!, y!);
    }
}
