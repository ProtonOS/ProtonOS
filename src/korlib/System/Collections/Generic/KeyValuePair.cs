// ProtonOS korlib - KeyValuePair<TKey, TValue> struct

namespace System.Collections.Generic;

/// <summary>
/// Defines a key/value pair that can be set or retrieved.
/// </summary>
/// <typeparam name="TKey">The type of the key.</typeparam>
/// <typeparam name="TValue">The type of the value.</typeparam>
public readonly struct KeyValuePair<TKey, TValue>
{
    /// <summary>
    /// Gets the key in the key/value pair.
    /// </summary>
    public TKey Key { get; }

    /// <summary>
    /// Gets the value in the key/value pair.
    /// </summary>
    public TValue Value { get; }

    /// <summary>
    /// Initializes a new instance of the KeyValuePair&lt;TKey, TValue&gt; structure with
    /// the specified key and value.
    /// </summary>
    /// <param name="key">The object defined in each key/value pair.</param>
    /// <param name="value">The definition associated with key.</param>
    public KeyValuePair(TKey key, TValue value)
    {
        Key = key;
        Value = value;
    }

    /// <summary>
    /// Returns a string representation of the KeyValuePair&lt;TKey, TValue&gt;.
    /// </summary>
    /// <returns>A string representation of the KeyValuePair&lt;TKey, TValue&gt;.</returns>
    public override string ToString()
    {
        // Simple implementation until generics have proper ToString support
        return "KeyValuePair";
    }

    /// <summary>
    /// Deconstructs the current KeyValuePair&lt;TKey, TValue&gt;.
    /// </summary>
    /// <param name="key">The key of this pair.</param>
    /// <param name="value">The value of this pair.</param>
    public void Deconstruct(out TKey key, out TValue value)
    {
        key = Key;
        value = Value;
    }
}

/// <summary>
/// Creates instances of the KeyValuePair&lt;TKey, TValue&gt; struct.
/// </summary>
public static class KeyValuePair
{
    /// <summary>
    /// Creates a new key/value pair instance.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="key">The key of the pair.</param>
    /// <param name="value">The value of the pair.</param>
    /// <returns>A key/value pair containing the specified key and value.</returns>
    public static KeyValuePair<TKey, TValue> Create<TKey, TValue>(TKey key, TValue value)
    {
        return new KeyValuePair<TKey, TValue>(key, value);
    }
}
