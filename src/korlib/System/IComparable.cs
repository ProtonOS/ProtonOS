// ProtonOS korlib - IComparable interfaces

namespace System;

/// <summary>
/// Defines a generalized comparison method that a value type or class implements
/// to create a type-specific comparison method for ordering or sorting its instances.
/// </summary>
public interface IComparable
{
    /// <summary>
    /// Compares the current instance with another object of the same type and returns
    /// an integer that indicates whether the current instance precedes, follows, or
    /// occurs in the same position in the sort order as the other object.
    /// </summary>
    /// <param name="obj">An object to compare with this instance.</param>
    /// <returns>
    /// A value that indicates the relative order of the objects being compared.
    /// Less than zero: This instance precedes obj in the sort order.
    /// Zero: This instance occurs in the same position in the sort order as obj.
    /// Greater than zero: This instance follows obj in the sort order.
    /// </returns>
    int CompareTo(object? obj);
}

/// <summary>
/// Defines a generalized comparison method that a value type or class implements
/// to create a type-specific comparison method for ordering or sorting its instances.
/// </summary>
/// <typeparam name="T">The type of object to compare.</typeparam>
public interface IComparable<in T>
{
    /// <summary>
    /// Compares the current instance with another object of the same type and returns
    /// an integer that indicates whether the current instance precedes, follows, or
    /// occurs in the same position in the sort order as the other object.
    /// </summary>
    /// <param name="other">An object to compare with this instance.</param>
    /// <returns>
    /// A value that indicates the relative order of the objects being compared.
    /// Less than zero: This instance precedes other in the sort order.
    /// Zero: This instance occurs in the same position in the sort order as other.
    /// Greater than zero: This instance follows other in the sort order.
    /// </returns>
    int CompareTo(T? other);
}
