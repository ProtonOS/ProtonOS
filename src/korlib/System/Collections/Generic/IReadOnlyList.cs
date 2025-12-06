// ProtonOS korlib - IReadOnlyList<T> interface

namespace System.Collections.Generic;

/// <summary>
/// Represents a read-only collection of elements that can be accessed by index.
/// </summary>
/// <typeparam name="T">The type of elements in the read-only list.</typeparam>
public interface IReadOnlyList<out T> : IReadOnlyCollection<T>
{
    /// <summary>
    /// Gets the element at the specified index in the read-only list.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get.</param>
    /// <returns>The element at the specified index in the read-only list.</returns>
    T this[int index] { get; }
}
