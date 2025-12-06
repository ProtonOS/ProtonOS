// ProtonOS korlib - IDictionary<TKey, TValue> interface

namespace System.Collections.Generic;

/// <summary>
/// Represents a generic collection of key/value pairs.
/// </summary>
/// <typeparam name="TKey">The type of keys in the dictionary.</typeparam>
/// <typeparam name="TValue">The type of values in the dictionary.</typeparam>
public interface IDictionary<TKey, TValue> : ICollection<KeyValuePair<TKey, TValue>>
{
    /// <summary>
    /// Gets or sets the element with the specified key.
    /// </summary>
    /// <param name="key">The key of the element to get or set.</param>
    /// <returns>The element with the specified key.</returns>
    TValue this[TKey key] { get; set; }

    /// <summary>
    /// Gets an ICollection&lt;TKey&gt; containing the keys of the IDictionary&lt;TKey, TValue&gt;.
    /// </summary>
    ICollection<TKey> Keys { get; }

    /// <summary>
    /// Gets an ICollection&lt;TValue&gt; containing the values in the IDictionary&lt;TKey, TValue&gt;.
    /// </summary>
    ICollection<TValue> Values { get; }

    /// <summary>
    /// Adds an element with the provided key and value to the IDictionary&lt;TKey, TValue&gt;.
    /// </summary>
    /// <param name="key">The object to use as the key of the element to add.</param>
    /// <param name="value">The object to use as the value of the element to add.</param>
    void Add(TKey key, TValue value);

    /// <summary>
    /// Determines whether the IDictionary&lt;TKey, TValue&gt; contains an element with the specified key.
    /// </summary>
    /// <param name="key">The key to locate in the IDictionary&lt;TKey, TValue&gt;.</param>
    /// <returns>
    /// true if the IDictionary&lt;TKey, TValue&gt; contains an element with the key; otherwise, false.
    /// </returns>
    bool ContainsKey(TKey key);

    /// <summary>
    /// Removes the element with the specified key from the IDictionary&lt;TKey, TValue&gt;.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <returns>
    /// true if the element is successfully removed; otherwise, false.
    /// This method also returns false if key was not found in the original IDictionary&lt;TKey, TValue&gt;.
    /// </returns>
    bool Remove(TKey key);

    /// <summary>
    /// Gets the value associated with the specified key.
    /// </summary>
    /// <param name="key">The key whose value to get.</param>
    /// <param name="value">
    /// When this method returns, the value associated with the specified key, if the key is found;
    /// otherwise, the default value for the type of the value parameter.
    /// </param>
    /// <returns>
    /// true if the object that implements IDictionary&lt;TKey, TValue&gt; contains an element with
    /// the specified key; otherwise, false.
    /// </returns>
    bool TryGetValue(TKey key, out TValue value);
}
