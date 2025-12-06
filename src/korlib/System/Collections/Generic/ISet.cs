// ProtonOS korlib - ISet<T> interface

namespace System.Collections.Generic;

/// <summary>
/// Provides the base interface for the abstraction of sets.
/// </summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
public interface ISet<T> : ICollection<T>
{
    /// <summary>
    /// Adds an element to the current set and returns a value to indicate if the element
    /// was successfully added.
    /// </summary>
    /// <param name="item">The element to add to the set.</param>
    /// <returns>true if the element is added to the set; false if the element is already in the set.</returns>
    new bool Add(T item);

    /// <summary>
    /// Removes all elements in the specified collection from the current set.
    /// </summary>
    /// <param name="other">The collection of items to remove from the set.</param>
    void ExceptWith(IEnumerable<T> other);

    /// <summary>
    /// Modifies the current set so that it contains only elements that are also in a
    /// specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    void IntersectWith(IEnumerable<T> other);

    /// <summary>
    /// Determines whether the current set is a proper (strict) subset of a specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>true if the current set is a proper subset of other; otherwise, false.</returns>
    bool IsProperSubsetOf(IEnumerable<T> other);

    /// <summary>
    /// Determines whether the current set is a proper (strict) superset of a specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>true if the current set is a proper superset of other; otherwise, false.</returns>
    bool IsProperSupersetOf(IEnumerable<T> other);

    /// <summary>
    /// Determines whether a set is a subset of a specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>true if the current set is a subset of other; otherwise, false.</returns>
    bool IsSubsetOf(IEnumerable<T> other);

    /// <summary>
    /// Determines whether the current set is a superset of a specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>true if the current set is a superset of other; otherwise, false.</returns>
    bool IsSupersetOf(IEnumerable<T> other);

    /// <summary>
    /// Determines whether the current set overlaps with the specified collection.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>
    /// true if the current set and other share at least one common element; otherwise, false.
    /// </returns>
    bool Overlaps(IEnumerable<T> other);

    /// <summary>
    /// Determines whether the current set and the specified collection contain the same elements.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    /// <returns>
    /// true if the current set is equal to other; otherwise, false.
    /// </returns>
    bool SetEquals(IEnumerable<T> other);

    /// <summary>
    /// Modifies the current set so that it contains only elements that are present either
    /// in the current set or in the specified collection, but not both.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    void SymmetricExceptWith(IEnumerable<T> other);

    /// <summary>
    /// Modifies the current set so that it contains all elements that are present in the
    /// current set, in the specified collection, or in both.
    /// </summary>
    /// <param name="other">The collection to compare to the current set.</param>
    void UnionWith(IEnumerable<T> other);
}
