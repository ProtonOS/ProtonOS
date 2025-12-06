// ProtonOS korlib - ICollection<T> interface

namespace System.Collections.Generic;

/// <summary>
/// Defines methods to manipulate generic collections.
/// </summary>
/// <typeparam name="T">The type of the elements in the collection.</typeparam>
public interface ICollection<T> : IEnumerable<T>
{
    /// <summary>
    /// Gets the number of elements contained in the ICollection&lt;T&gt;.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets a value indicating whether the ICollection&lt;T&gt; is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Adds an item to the ICollection&lt;T&gt;.
    /// </summary>
    /// <param name="item">The object to add to the ICollection&lt;T&gt;.</param>
    void Add(T item);

    /// <summary>
    /// Removes all items from the ICollection&lt;T&gt;.
    /// </summary>
    void Clear();

    /// <summary>
    /// Determines whether the ICollection&lt;T&gt; contains a specific value.
    /// </summary>
    /// <param name="item">The object to locate in the ICollection&lt;T&gt;.</param>
    /// <returns>true if item is found in the ICollection&lt;T&gt;; otherwise, false.</returns>
    bool Contains(T item);

    /// <summary>
    /// Copies the elements of the ICollection&lt;T&gt; to an Array, starting at a particular Array index.
    /// </summary>
    /// <param name="array">
    /// The one-dimensional Array that is the destination of the elements copied from ICollection&lt;T&gt;.
    /// </param>
    /// <param name="arrayIndex">The zero-based index in array at which copying begins.</param>
    void CopyTo(T[] array, int arrayIndex);

    /// <summary>
    /// Removes the first occurrence of a specific object from the ICollection&lt;T&gt;.
    /// </summary>
    /// <param name="item">The object to remove from the ICollection&lt;T&gt;.</param>
    /// <returns>
    /// true if item was successfully removed from the ICollection&lt;T&gt;; otherwise, false.
    /// This method also returns false if item is not found in the original ICollection&lt;T&gt;.
    /// </returns>
    bool Remove(T item);
}
