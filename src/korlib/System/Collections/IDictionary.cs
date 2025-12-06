// ProtonOS korlib - IDictionary interface (non-generic)

namespace System.Collections;

/// <summary>
/// Represents a non-generic collection of key/value pairs.
/// </summary>
public interface IDictionary : ICollection
{
    /// <summary>
    /// Gets or sets the element with the specified key.
    /// </summary>
    /// <param name="key">The key of the element to get or set.</param>
    /// <returns>The element with the specified key.</returns>
    object? this[object key] { get; set; }

    /// <summary>
    /// Gets a value indicating whether the IDictionary object has a fixed size.
    /// </summary>
    bool IsFixedSize { get; }

    /// <summary>
    /// Gets a value indicating whether the IDictionary object is read-only.
    /// </summary>
    bool IsReadOnly { get; }

    /// <summary>
    /// Gets an ICollection object containing the keys of the IDictionary object.
    /// </summary>
    ICollection Keys { get; }

    /// <summary>
    /// Gets an ICollection object containing the values in the IDictionary object.
    /// </summary>
    ICollection Values { get; }

    /// <summary>
    /// Adds an element with the provided key and value to the IDictionary object.
    /// </summary>
    /// <param name="key">The Object to use as the key of the element to add.</param>
    /// <param name="value">The Object to use as the value of the element to add.</param>
    void Add(object key, object? value);

    /// <summary>
    /// Removes all elements from the IDictionary object.
    /// </summary>
    void Clear();

    /// <summary>
    /// Determines whether the IDictionary object contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the IDictionary object.</param>
    /// <returns>true if the IDictionary contains an element with the key; otherwise, false.</returns>
    bool Contains(object key);

    /// <summary>
    /// Returns an IDictionaryEnumerator object for the IDictionary object.
    /// </summary>
    /// <returns>An IDictionaryEnumerator object for the IDictionary object.</returns>
    IDictionaryEnumerator GetEnumerator();

    /// <summary>
    /// Removes the element with the specified key from the IDictionary object.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    void Remove(object key);
}

/// <summary>
/// Enumerates the elements of a non-generic dictionary.
/// </summary>
public interface IDictionaryEnumerator : IEnumerator
{
    /// <summary>
    /// Gets both the key and the value of the current dictionary entry.
    /// </summary>
    DictionaryEntry Entry { get; }

    /// <summary>
    /// Gets the key of the current dictionary entry.
    /// </summary>
    object Key { get; }

    /// <summary>
    /// Gets the value of the current dictionary entry.
    /// </summary>
    object? Value { get; }
}

/// <summary>
/// Defines a dictionary key/value pair that can be set or retrieved.
/// </summary>
public struct DictionaryEntry
{
    private object _key;
    private object? _value;

    /// <summary>
    /// Initializes a new instance of the DictionaryEntry type with the specified key and value.
    /// </summary>
    public DictionaryEntry(object key, object? value)
    {
        _key = key;
        _value = value;
    }

    /// <summary>
    /// Gets or sets the key in the key/value pair.
    /// </summary>
    public object Key
    {
        get => _key;
        set => _key = value;
    }

    /// <summary>
    /// Gets or sets the value in the key/value pair.
    /// </summary>
    public object? Value
    {
        get => _value;
        set => _value = value;
    }

    /// <summary>
    /// Deconstructs the current DictionaryEntry.
    /// </summary>
    public void Deconstruct(out object key, out object? value)
    {
        key = Key;
        value = Value;
    }
}
