// korlib - Dictionary<TKey, TValue>
// Represents a collection of keys and values.

namespace System.Collections.Generic;

/// <summary>
/// Represents a collection of keys and values.
/// </summary>
public class Dictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary
    where TKey : notnull
{
    private struct Entry
    {
        public int hashCode;
        public int next;
        public TKey key;
        public TValue value;
    }

    private int[]? _buckets;
    private Entry[]? _entries;
    private int _count;
    private int _freeList;
    private int _freeCount;
    private int _version;
    // NOTE: We store as EqualityComparer<TKey> to use virtual dispatch instead of interface dispatch
    // This works around a limitation where generic types inheriting interfaces from generic base
    // types don't have interface maps properly set up in our runtime.
    private readonly EqualityComparer<TKey> _comparer;
    private KeyCollection? _keys;
    private ValueCollection? _values;

    private const int StartOfFreeList = -3;

    /// <summary>Initializes a new empty Dictionary.</summary>
    public Dictionary() : this(0, null) { }

    /// <summary>Initializes a new empty Dictionary with specified capacity.</summary>
    public Dictionary(int capacity) : this(capacity, null) { }

    /// <summary>Initializes a new empty Dictionary with specified comparer.</summary>
    public Dictionary(IEqualityComparer<TKey>? comparer) : this(0, comparer) { }

    /// <summary>Initializes a new empty Dictionary with specified capacity and comparer.</summary>
    public Dictionary(int capacity, IEqualityComparer<TKey>? comparer)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (capacity > 0) Initialize(capacity);
        // Use EqualityComparer<TKey>.Default for virtual dispatch instead of interface dispatch
        // Custom comparers that aren't EqualityComparer<TKey> subclasses are not supported
        _comparer = (comparer as EqualityComparer<TKey>) ?? EqualityComparer<TKey>.Default;
    }

    /// <summary>Initializes a new Dictionary with elements from the specified dictionary.</summary>
    public Dictionary(IDictionary<TKey, TValue> dictionary) : this(dictionary, null) { }

    /// <summary>Initializes a new Dictionary with elements from the specified dictionary and comparer.</summary>
    public Dictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey>? comparer)
        : this(dictionary?.Count ?? 0, comparer)
    {
        if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));
        foreach (KeyValuePair<TKey, TValue> pair in dictionary)
        {
            Add(pair.Key, pair.Value);
        }
    }

    /// <summary>Gets the IEqualityComparer used to determine equality of keys.</summary>
    public IEqualityComparer<TKey> Comparer => _comparer;

    /// <summary>Gets the number of key/value pairs contained in the Dictionary.</summary>
    public int Count => _count - _freeCount;

    /// <summary>Gets a collection containing the keys in the Dictionary.</summary>
    public KeyCollection Keys => _keys ??= new KeyCollection(this);

    ICollection<TKey> IDictionary<TKey, TValue>.Keys => Keys;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
    ICollection IDictionary.Keys => Keys;

    /// <summary>Gets a collection containing the values in the Dictionary.</summary>
    public ValueCollection Values => _values ??= new ValueCollection(this);

    ICollection<TValue> IDictionary<TKey, TValue>.Values => Values;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    ICollection IDictionary.Values => Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
    bool IDictionary.IsReadOnly => false;
    bool IDictionary.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    /// <summary>Gets or sets the value associated with the specified key.</summary>
    public TValue this[TKey key]
    {
        get
        {
            int i = FindEntry(key);
            if (i >= 0) return _entries![i].value;
            throw new KeyNotFoundException();
        }
        set
        {
            bool modified = TryInsert(key, value, InsertionBehavior.OverwriteExisting);
        }
    }

    object? IDictionary.this[object key]
    {
        get => key is TKey k ? this[k] : null;
        set => this[(TKey)key] = (TValue)value!;
    }

    /// <summary>Adds the specified key and value to the dictionary.</summary>
    public void Add(TKey key, TValue value)
    {
        bool modified = TryInsert(key, value, InsertionBehavior.ThrowOnExisting);
    }

    void IDictionary.Add(object key, object? value) => Add((TKey)key, (TValue)value!);

    void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair)
        => Add(keyValuePair.Key, keyValuePair.Value);

    /// <summary>Removes all keys and values from the Dictionary.</summary>
    public void Clear()
    {
        int count = _count;
        if (count > 0)
        {
            Array.Clear(_buckets!, 0, _buckets!.Length);
            _count = 0;
            _freeList = -1;
            _freeCount = 0;
            Array.Clear(_entries!, 0, count);
        }
        _version++;
    }

    /// <summary>Determines whether the Dictionary contains the specified key.</summary>
    public bool ContainsKey(TKey key) => FindEntry(key) >= 0;

    bool IDictionary.Contains(object key) => key is TKey k && ContainsKey(k);

    /// <summary>Determines whether the Dictionary contains a specific value.</summary>
    public bool ContainsValue(TValue value)
    {
        Entry[]? entries = _entries;
        if (value == null)
        {
            for (int i = 0; i < _count; i++)
            {
                if (entries![i].next >= -1 && entries[i].value == null) return true;
            }
        }
        else
        {
            EqualityComparer<TValue> defaultComparer = EqualityComparer<TValue>.Default;
            for (int i = 0; i < _count; i++)
            {
                if (entries![i].next >= -1 && defaultComparer.Equals(entries[i].value, value)) return true;
            }
        }
        return false;
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
    {
        int i = FindEntry(keyValuePair.Key);
        if (i >= 0 && EqualityComparer<TValue>.Default.Equals(_entries![i].value, keyValuePair.Value))
        {
            return true;
        }
        return false;
    }

    private void CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if ((uint)index > (uint)array.Length) throw new ArgumentOutOfRangeException(nameof(index));
        if (array.Length - index < Count) throw new ArgumentException("Array too small");

        int count = _count;
        Entry[]? entries = _entries;
        for (int i = 0; i < count; i++)
        {
            if (entries![i].next >= -1)
            {
                array[index++] = new KeyValuePair<TKey, TValue>(entries[i].key, entries[i].value);
            }
        }
    }

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int index)
        => CopyTo(array, index);

    void ICollection.CopyTo(Array array, int index)
    {
        if (array is KeyValuePair<TKey, TValue>[] pairs)
        {
            CopyTo(pairs, index);
        }
        else
        {
            throw new ArgumentException("Invalid array type");
        }
    }

    /// <summary>Returns an enumerator that iterates through the Dictionary.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
        => new Enumerator(this, Enumerator.KeyValuePair);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

    IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DictEntry);

    private int FindEntry(TKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        int[]? buckets = _buckets;
        if (buckets != null)
        {
            Entry[]? entries = _entries;
            int hashCode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int i = buckets[hashCode % buckets.Length] - 1;
            while ((uint)i < (uint)entries!.Length)
            {
                if (entries[i].hashCode == hashCode && _comparer.Equals(entries[i].key, key))
                {
                    return i;
                }
                i = entries[i].next;
            }
        }
        return -1;
    }

    private int Initialize(int capacity)
    {
        int size = GetPrime(capacity);
        _buckets = new int[size];
        _entries = new Entry[size];
        _freeList = -1;
        return size;
    }

    private enum InsertionBehavior { None, OverwriteExisting, ThrowOnExisting }

    private bool TryInsert(TKey key, TValue value, InsertionBehavior behavior)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        if (_buckets == null) Initialize(0);

        Entry[]? entries = _entries;
        int hashCode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
        int targetBucket = hashCode % _buckets!.Length;
        int i = _buckets[targetBucket] - 1;

        while ((uint)i < (uint)entries!.Length)
        {
            if (entries[i].hashCode == hashCode && _comparer.Equals(entries[i].key, key))
            {
                if (behavior == InsertionBehavior.OverwriteExisting)
                {
                    entries[i].value = value;
                    _version++;
                    return true;
                }
                if (behavior == InsertionBehavior.ThrowOnExisting)
                {
                    throw new ArgumentException("Key already exists");
                }
                return false;
            }
            i = entries[i].next;
        }

        int index;
        if (_freeCount > 0)
        {
            index = _freeList;
            _freeList = StartOfFreeList - entries[_freeList].next;
            _freeCount--;
        }
        else
        {
            if (_count == entries.Length)
            {
                Resize();
                targetBucket = hashCode % _buckets.Length;
            }
            index = _count;
            _count++;
            entries = _entries;
        }

        entries![index].hashCode = hashCode;
        entries[index].next = _buckets[targetBucket] - 1;
        entries[index].key = key;
        entries[index].value = value;
        _buckets[targetBucket] = index + 1;
        _version++;
        return true;
    }

    private void Resize() => Resize(ExpandPrime(_count));

    private void Resize(int newSize)
    {
        Entry[] newEntries = new Entry[newSize];
        int count = _count;
        Array.Copy(_entries!, newEntries, count);

        _buckets = new int[newSize];
        for (int i = 0; i < count; i++)
        {
            if (newEntries[i].hashCode >= 0)
            {
                int bucket = newEntries[i].hashCode % newSize;
                newEntries[i].next = _buckets[bucket] - 1;
                _buckets[bucket] = i + 1;
            }
        }
        _entries = newEntries;
    }

    /// <summary>Removes the value with the specified key from the Dictionary.</summary>
    public bool Remove(TKey key)
    {
        if (key == null) throw new ArgumentNullException(nameof(key));

        if (_buckets != null)
        {
            Entry[]? entries = _entries;
            int hashCode = _comparer.GetHashCode(key) & 0x7FFFFFFF;
            int bucket = hashCode % _buckets.Length;
            int last = -1;
            int i = _buckets[bucket] - 1;

            while ((uint)i < (uint)entries!.Length)
            {
                if (entries[i].hashCode == hashCode && _comparer.Equals(entries[i].key, key))
                {
                    if (last < 0)
                    {
                        _buckets[bucket] = entries[i].next + 1;
                    }
                    else
                    {
                        entries[last].next = entries[i].next;
                    }
                    entries[i].next = StartOfFreeList - _freeList;
                    entries[i].key = default!;
                    entries[i].value = default!;
                    _freeList = i;
                    _freeCount++;
                    _version++;
                    return true;
                }
                last = i;
                i = entries[i].next;
            }
        }
        return false;
    }

    void IDictionary.Remove(object key)
    {
        if (key is TKey k) Remove(k);
    }

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
    {
        int i = FindEntry(keyValuePair.Key);
        if (i >= 0 && EqualityComparer<TValue>.Default.Equals(_entries![i].value, keyValuePair.Value))
        {
            Remove(keyValuePair.Key);
            return true;
        }
        return false;
    }

    /// <summary>Gets the value associated with the specified key.</summary>
    public bool TryGetValue(TKey key, out TValue value)
    {
        int i = FindEntry(key);
        if (i >= 0)
        {
            value = _entries![i].value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>Attempts to add the specified key and value to the dictionary.</summary>
    public bool TryAdd(TKey key, TValue value)
    {
        return TryInsert(key, value, InsertionBehavior.None);
    }

    // Simple prime number helpers
    private static readonly int[] s_primes = {
        3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
        1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519,
        21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437, 187751,
        225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263, 1674319,
        2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369
    };

    private static int GetPrime(int min)
    {
        if (min < 0) throw new ArgumentException("Capacity overflow");
        foreach (int prime in s_primes)
        {
            if (prime >= min) return prime;
        }
        // Beyond our table, return min * 2 + 1 as approximation
        return min | 1;
    }

    private static int ExpandPrime(int oldSize)
    {
        int newSize = 2 * oldSize;
        if ((uint)newSize > int.MaxValue) return int.MaxValue;
        return GetPrime(newSize);
    }

    /// <summary>Enumerates the elements of a Dictionary.</summary>
    public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
    {
        private readonly Dictionary<TKey, TValue> _dictionary;
        private readonly int _version;
        private int _index;
        private KeyValuePair<TKey, TValue> _current;
        private readonly int _getEnumeratorRetType;

        internal const int KeyValuePair = 1;
        internal const int DictEntry = 2;

        internal Enumerator(Dictionary<TKey, TValue> dictionary, int getEnumeratorRetType)
        {
            _dictionary = dictionary;
            _version = dictionary._version;
            _index = 0;
            _getEnumeratorRetType = getEnumeratorRetType;
            _current = default;
        }

        public KeyValuePair<TKey, TValue> Current => _current;

        object IEnumerator.Current
        {
            get
            {
                if (_getEnumeratorRetType == DictEntry)
                {
                    return new DictionaryEntry(_current.Key, _current.Value);
                }
                return _current;
            }
        }

        DictionaryEntry IDictionaryEnumerator.Entry => new DictionaryEntry(_current.Key, _current.Value);
        object IDictionaryEnumerator.Key => _current.Key;
        object? IDictionaryEnumerator.Value => _current.Value;

        public bool MoveNext()
        {
            if (_version != _dictionary._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }

            Entry[]? entries = _dictionary._entries;
            while ((uint)_index < (uint)_dictionary._count)
            {
                int idx = _index++;
                if (entries![idx].next >= -1)
                {
                    // Store key and value directly in _current's fields to avoid KeyValuePair constructor
                    // This works around potential JIT issues with generic struct construction
                    _current = new KeyValuePair<TKey, TValue>(entries[idx].key, entries[idx].value);
                    return true;
                }
            }

            _index = _dictionary._count + 1;
            _current = default;
            return false;
        }

        public void Reset()
        {
            if (_version != _dictionary._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }
            _index = 0;
            _current = default;
        }

        public void Dispose() { }
    }

    /// <summary>Represents the collection of keys in a Dictionary.</summary>
    public sealed class KeyCollection : ICollection<TKey>, ICollection, IReadOnlyCollection<TKey>
    {
        private readonly Dictionary<TKey, TValue> _dictionary;

        public KeyCollection(Dictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        }

        public int Count => _dictionary.Count;
        bool ICollection<TKey>.IsReadOnly => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

        public Enumerator GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

        public void CopyTo(TKey[] array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < _dictionary.Count) throw new ArgumentException("Array too small");

            int count = _dictionary._count;
            Entry[]? entries = _dictionary._entries;
            for (int i = 0; i < count; i++)
            {
                if (entries![i].next >= -1) array[index++] = entries[i].key;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is TKey[] keys) CopyTo(keys, index);
            else throw new ArgumentException("Invalid array type");
        }

        bool ICollection<TKey>.Contains(TKey item) => _dictionary.ContainsKey(item);
        void ICollection<TKey>.Add(TKey item) => throw new NotSupportedException();
        bool ICollection<TKey>.Remove(TKey item) => throw new NotSupportedException();
        void ICollection<TKey>.Clear() => throw new NotSupportedException();

        public struct Enumerator : IEnumerator<TKey>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private int _index;
            private readonly int _version;
            private TKey? _currentKey;

            internal Enumerator(Dictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _currentKey = default;
            }

            public TKey Current => _currentKey!;
            object IEnumerator.Current => _currentKey!;

            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified");

                Entry[]? entries = _dictionary._entries;
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int idx = _index++;
                    if (entries![idx].next >= -1)
                    {
                        _currentKey = entries[idx].key;
                        return true;
                    }
                }
                _index = _dictionary._count + 1;
                _currentKey = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified");
                _index = 0;
                _currentKey = default;
            }

            public void Dispose() { }
        }
    }

    /// <summary>Represents the collection of values in a Dictionary.</summary>
    public sealed class ValueCollection : ICollection<TValue>, ICollection, IReadOnlyCollection<TValue>
    {
        private readonly Dictionary<TKey, TValue> _dictionary;

        public ValueCollection(Dictionary<TKey, TValue> dictionary)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
        }

        public int Count => _dictionary.Count;
        bool ICollection<TValue>.IsReadOnly => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => ((ICollection)_dictionary).SyncRoot;

        public Enumerator GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator<TValue> IEnumerable<TValue>.GetEnumerator() => new Enumerator(_dictionary);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(_dictionary);

        public void CopyTo(TValue[] array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < _dictionary.Count) throw new ArgumentException("Array too small");

            int count = _dictionary._count;
            Entry[]? entries = _dictionary._entries;
            for (int i = 0; i < count; i++)
            {
                if (entries![i].next >= -1) array[index++] = entries[i].value;
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is TValue[] values) CopyTo(values, index);
            else throw new ArgumentException("Invalid array type");
        }

        bool ICollection<TValue>.Contains(TValue item) => _dictionary.ContainsValue(item);
        void ICollection<TValue>.Add(TValue item) => throw new NotSupportedException();
        bool ICollection<TValue>.Remove(TValue item) => throw new NotSupportedException();
        void ICollection<TValue>.Clear() => throw new NotSupportedException();

        public struct Enumerator : IEnumerator<TValue>
        {
            private readonly Dictionary<TKey, TValue> _dictionary;
            private int _index;
            private readonly int _version;
            private TValue? _currentValue;

            internal Enumerator(Dictionary<TKey, TValue> dictionary)
            {
                _dictionary = dictionary;
                _version = dictionary._version;
                _index = 0;
                _currentValue = default;
            }

            public TValue Current => _currentValue!;
            object? IEnumerator.Current => _currentValue;

            public bool MoveNext()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified");

                Entry[]? entries = _dictionary._entries;
                while ((uint)_index < (uint)_dictionary._count)
                {
                    int idx = _index++;
                    if (entries![idx].next >= -1)
                    {
                        _currentValue = entries[idx].value;
                        return true;
                    }
                }
                _index = _dictionary._count + 1;
                _currentValue = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _dictionary._version)
                    throw new InvalidOperationException("Collection was modified");
                _index = 0;
                _currentValue = default;
            }

            public void Dispose() { }
        }
    }
}
