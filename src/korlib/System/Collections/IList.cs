// ProtonOS korlib - IList interface (non-generic)

namespace System.Collections;

/// <summary>
/// Represents a non-generic collection of objects that can be individually accessed by index.
/// </summary>
public interface IList : ICollection
{
    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the element to get or set.</param>
    /// <returns>The element at the specified index.</returns>
    object? this[int index] { get; set; }

    /// <summary>
    /// Gets a value indicating whether the IList has a fixed size.
    /// </summary>
    bool IsFixedSize { get; }

    /// <summary>
    /// Gets a value indicating whether the IList is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Adds an item to the IList.
    /// </summary>
    /// <param name="value">The object to add to the IList.</param>
    /// <returns>The position into which the new element was inserted.</returns>
    int Add(object? value);

    /// <summary>
    /// Removes all items from the IList.
    /// </summary>
    void Clear();

    /// <summary>
    /// Determines whether the IList contains a specific value.
    /// </summary>
    /// <param name="value">The object to locate in the IList.</param>
    /// <returns>true if the Object is found in the IList; otherwise, false.</returns>
    bool Contains(object? value);

    /// <summary>
    /// Determines the index of a specific item in the IList.
    /// </summary>
    /// <param name="value">The object to locate in the IList.</param>
    /// <returns>The index of value if found in the list; otherwise, -1.</returns>
    int IndexOf(object? value);

    /// <summary>
    /// Inserts an item to the IList at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index at which value should be inserted.</param>
    /// <param name="value">The object to insert into the IList.</param>
    void Insert(int index, object? value);

    /// <summary>
    /// Removes the first occurrence of a specific object from the IList.
    /// </summary>
    /// <param name="value">The object to remove from the IList.</param>
    void Remove(object? value);

    /// <summary>
    /// Removes the IList item at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the item to remove.</param>
    void RemoveAt(int index);
}
