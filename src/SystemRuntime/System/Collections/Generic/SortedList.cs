// ProtonOS System.Runtime - SortedList<TKey, TValue>
// Represents a collection of key/value pairs that are sorted by key.

namespace System.Collections.Generic
{
    /// <summary>
    /// Represents a collection of key/value pairs that are sorted by the keys.
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the collection.</typeparam>
    /// <typeparam name="TValue">The type of values in the collection.</typeparam>
    public class SortedList<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue>, IDictionary
        where TKey : notnull
    {
        private const int DefaultCapacity = 4;

        private TKey[] _keys;
        private TValue[] _values;
        private int _size;
        private int _version;
        private readonly IComparer<TKey> _comparer;
        private KeyList? _keyList;
        private ValueList? _valueList;

        /// <summary>Initializes a new instance of the SortedList class that is empty.</summary>
        public SortedList()
        {
            _keys = Array.Empty<TKey>();
            _values = Array.Empty<TValue>();
            _comparer = Comparer<TKey>.Default;
        }

        /// <summary>Initializes a new instance of the SortedList class that is empty and has the specified initial capacity.</summary>
        public SortedList(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _keys = new TKey[capacity];
            _values = new TValue[capacity];
            _comparer = Comparer<TKey>.Default;
        }

        /// <summary>Initializes a new instance of the SortedList class that is empty and uses the specified comparer.</summary>
        public SortedList(IComparer<TKey>? comparer) : this()
        {
            _comparer = comparer ?? Comparer<TKey>.Default;
        }

        /// <summary>Initializes a new instance of the SortedList class that is empty, has the specified initial capacity, and uses the specified comparer.</summary>
        public SortedList(int capacity, IComparer<TKey>? comparer) : this(capacity)
        {
            _comparer = comparer ?? Comparer<TKey>.Default;
        }

        /// <summary>Initializes a new instance of the SortedList class that contains elements copied from the specified dictionary.</summary>
        public SortedList(IDictionary<TKey, TValue> dictionary) : this(dictionary, null)
        {
        }

        /// <summary>Initializes a new instance of the SortedList class that contains elements copied from the specified dictionary and uses the specified comparer.</summary>
        public SortedList(IDictionary<TKey, TValue> dictionary, IComparer<TKey>? comparer)
            : this(dictionary?.Count ?? 0, comparer)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

            int count = dictionary.Count;
            if (count > 0)
            {
                // Copy keys and values, then sort
                dictionary.Keys.CopyTo(_keys, 0);
                dictionary.Values.CopyTo(_values, 0);
                _size = count;

                // Sort by keys using a simple bubble sort with parallel swaps
                // More efficient sorts can be implemented later if needed
                for (int i = 0; i < _size - 1; i++)
                {
                    for (int j = i + 1; j < _size; j++)
                    {
                        if (_comparer.Compare(_keys[i], _keys[j]) > 0)
                        {
                            TKey tempKey = _keys[i];
                            _keys[i] = _keys[j];
                            _keys[j] = tempKey;

                            TValue tempValue = _values[i];
                            _values[i] = _values[j];
                            _values[j] = tempValue;
                        }
                    }
                }
            }
        }

        /// <summary>Gets or sets the capacity of the SortedList.</summary>
        public int Capacity
        {
            get => _keys.Length;
            set
            {
                if (value < _size) throw new ArgumentOutOfRangeException(nameof(value));
                if (value != _keys.Length)
                {
                    if (value > 0)
                    {
                        TKey[] newKeys = new TKey[value];
                        TValue[] newValues = new TValue[value];
                        if (_size > 0)
                        {
                            Array.Copy(_keys, newKeys, _size);
                            Array.Copy(_values, newValues, _size);
                        }
                        _keys = newKeys;
                        _values = newValues;
                    }
                    else
                    {
                        _keys = Array.Empty<TKey>();
                        _values = Array.Empty<TValue>();
                    }
                }
            }
        }

        /// <summary>Gets the IComparer used to order the elements of the SortedList.</summary>
        public IComparer<TKey> Comparer => _comparer;

        /// <summary>Gets the number of key/value pairs contained in the SortedList.</summary>
        public int Count => _size;

        /// <summary>Gets a collection containing the keys in the SortedList.</summary>
        public IList<TKey> Keys => GetKeyListHelper();

        ICollection<TKey> IDictionary<TKey, TValue>.Keys => GetKeyListHelper();
        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => GetKeyListHelper();
        ICollection IDictionary.Keys => GetKeyListHelper();

        /// <summary>Gets a collection containing the values in the SortedList.</summary>
        public IList<TValue> Values => GetValueListHelper();

        ICollection<TValue> IDictionary<TKey, TValue>.Values => GetValueListHelper();
        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => GetValueListHelper();
        ICollection IDictionary.Values => GetValueListHelper();

        bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;
        bool IDictionary.IsReadOnly => false;
        bool IDictionary.IsFixedSize => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        private KeyList GetKeyListHelper() => _keyList ??= new KeyList(this);
        private ValueList GetValueListHelper() => _valueList ??= new ValueList(this);

        /// <summary>Gets or sets the value associated with the specified key.</summary>
        public TValue this[TKey key]
        {
            get
            {
                int index = IndexOfKey(key);
                if (index >= 0)
                {
                    return _values[index];
                }
                throw new KeyNotFoundException($"The given key '{key}' was not present in the dictionary.");
            }
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                int index = BinarySearch(_keys, 0, _size, key, _comparer);
                if (index >= 0)
                {
                    _values[index] = value;
                    _version++;
                }
                else
                {
                    Insert(~index, key, value);
                }
            }
        }

        object? IDictionary.this[object key]
        {
            get => key is TKey k ? this[k] : null;
            set
            {
                if (key == null) throw new ArgumentNullException(nameof(key));
                if (key is not TKey k) throw new ArgumentException("Wrong key type", nameof(key));
                this[k] = (TValue)value!;
            }
        }

        /// <summary>Adds an element with the specified key and value into the SortedList.</summary>
        public void Add(TKey key, TValue value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            int index = BinarySearch(_keys, 0, _size, key, _comparer);
            if (index >= 0)
            {
                throw new ArgumentException("An item with the same key has already been added.");
            }
            Insert(~index, key, value);
        }

        void IDictionary.Add(object key, object? value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key is not TKey k) throw new ArgumentException("Wrong key type", nameof(key));
            Add(k, (TValue)value!);
        }

        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> keyValuePair)
            => Add(keyValuePair.Key, keyValuePair.Value);

        /// <summary>Removes all elements from the SortedList.</summary>
        public void Clear()
        {
            _version++;
            if (_size > 0)
            {
                Array.Clear(_keys, 0, _size);
                Array.Clear(_values, 0, _size);
                _size = 0;
            }
        }

        /// <summary>Determines whether the SortedList contains a specific key.</summary>
        public bool ContainsKey(TKey key)
        {
            return IndexOfKey(key) >= 0;
        }

        bool IDictionary.Contains(object key) => key is TKey k && ContainsKey(k);

        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> keyValuePair)
        {
            int index = IndexOfKey(keyValuePair.Key);
            if (index >= 0 && EqualityComparer<TValue>.Default.Equals(_values[index], keyValuePair.Value))
            {
                return true;
            }
            return false;
        }

        /// <summary>Determines whether the SortedList contains a specific value.</summary>
        public bool ContainsValue(TValue value)
        {
            return IndexOfValue(value) >= 0;
        }

        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < _size) throw new ArgumentException("Insufficient space in array");

            for (int i = 0; i < _size; i++)
            {
                array[arrayIndex + i] = new KeyValuePair<TKey, TValue>(_keys[i], _values[i]);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Rank != 1) throw new ArgumentException("Multi-dimensional arrays not supported");
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < _size) throw new ArgumentException("Insufficient space in array");

            if (array is KeyValuePair<TKey, TValue>[] pairs)
            {
                for (int i = 0; i < _size; i++)
                {
                    pairs[index + i] = new KeyValuePair<TKey, TValue>(_keys[i], _values[i]);
                }
            }
            else if (array is DictionaryEntry[] entries)
            {
                for (int i = 0; i < _size; i++)
                {
                    entries[index + i] = new DictionaryEntry(_keys[i], _values[i]);
                }
            }
            else if (array is object?[] objects)
            {
                for (int i = 0; i < _size; i++)
                {
                    objects[index + i] = new KeyValuePair<TKey, TValue>(_keys[i], _values[i]);
                }
            }
            else
            {
                throw new ArgumentException("Invalid array type");
            }
        }

        /// <summary>Returns an enumerator that iterates through the SortedList.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator()
            => new Enumerator(this, Enumerator.KeyValuePair);

        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, Enumerator.KeyValuePair);

        IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, Enumerator.DictEntry);

        /// <summary>Gets the key at the specified index.</summary>
        public TKey GetKeyAtIndex(int index)
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            return _keys[index];
        }

        /// <summary>Gets the value at the specified index.</summary>
        public TValue GetValueAtIndex(int index)
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            return _values[index];
        }

        /// <summary>Sets the value at the specified index.</summary>
        public void SetValueAtIndex(int index, TValue value)
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            _values[index] = value;
            _version++;
        }

        /// <summary>Searches for the specified key and returns the zero-based index within the SortedList.</summary>
        public int IndexOfKey(TKey key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            int index = BinarySearch(_keys, 0, _size, key, _comparer);
            return index >= 0 ? index : -1;
        }

        /// <summary>Searches for the specified value and returns the zero-based index of the first occurrence.</summary>
        public int IndexOfValue(TValue value)
        {
            return Array.IndexOf(_values, value, 0, _size);
        }

        private void Insert(int index, TKey key, TValue value)
        {
            if (_size == _keys.Length) EnsureCapacity(_size + 1);
            if (index < _size)
            {
                Array.Copy(_keys, index, _keys, index + 1, _size - index);
                Array.Copy(_values, index, _values, index + 1, _size - index);
            }
            _keys[index] = key;
            _values[index] = value;
            _size++;
            _version++;
        }

        /// <summary>Removes the element with the specified key from the SortedList.</summary>
        public bool Remove(TKey key)
        {
            int index = IndexOfKey(key);
            if (index >= 0)
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        void IDictionary.Remove(object key)
        {
            if (key is TKey k) Remove(k);
        }

        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> keyValuePair)
        {
            int index = IndexOfKey(keyValuePair.Key);
            if (index >= 0 && EqualityComparer<TValue>.Default.Equals(_values[index], keyValuePair.Value))
            {
                RemoveAt(index);
                return true;
            }
            return false;
        }

        /// <summary>Removes the element at the specified index of the SortedList.</summary>
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            _size--;
            if (index < _size)
            {
                Array.Copy(_keys, index + 1, _keys, index, _size - index);
                Array.Copy(_values, index + 1, _values, index, _size - index);
            }
            _keys[_size] = default!;
            _values[_size] = default!;
            _version++;
        }

        /// <summary>Sets the capacity to the actual number of elements in the SortedList.</summary>
        public void TrimExcess()
        {
            int threshold = (int)(_keys.Length * 0.9);
            if (_size < threshold)
            {
                Capacity = _size;
            }
        }

        /// <summary>Gets the value associated with the specified key.</summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            int index = IndexOfKey(key);
            if (index >= 0)
            {
                value = _values[index];
                return true;
            }
            value = default!;
            return false;
        }

        private void EnsureCapacity(int min)
        {
            int newCapacity = _keys.Length == 0 ? DefaultCapacity : _keys.Length * 2;
            if (newCapacity < min) newCapacity = min;
            Capacity = newCapacity;
        }

        private static int BinarySearch(TKey[] array, int index, int length, TKey value, IComparer<TKey> comparer)
        {
            int lo = index;
            int hi = index + length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                int comp = comparer.Compare(array[mid], value);
                if (comp == 0) return mid;
                if (comp < 0)
                {
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return ~lo;
        }

        /// <summary>Enumerates the elements of a SortedList.</summary>
        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>, IDictionaryEnumerator
        {
            private readonly SortedList<TKey, TValue> _sortedList;
            private TKey? _key;
            private TValue? _value;
            private int _index;
            private readonly int _version;
            private readonly int _getEnumeratorRetType;

            internal const int KeyValuePair = 1;
            internal const int DictEntry = 2;

            internal Enumerator(SortedList<TKey, TValue> sortedList, int getEnumeratorRetType)
            {
                _sortedList = sortedList;
                _index = 0;
                _version = sortedList._version;
                _getEnumeratorRetType = getEnumeratorRetType;
                _key = default;
                _value = default;
            }

            public KeyValuePair<TKey, TValue> Current => new KeyValuePair<TKey, TValue>(_key!, _value!);

            object IEnumerator.Current
            {
                get
                {
                    if (_index == 0 || _index == _sortedList._size + 1)
                    {
                        throw new InvalidOperationException("Enumeration has not started or has ended");
                    }
                    if (_getEnumeratorRetType == DictEntry)
                    {
                        return new DictionaryEntry(_key!, _value);
                    }
                    return new KeyValuePair<TKey, TValue>(_key!, _value!);
                }
            }

            DictionaryEntry IDictionaryEnumerator.Entry
            {
                get
                {
                    if (_index == 0 || _index == _sortedList._size + 1)
                    {
                        throw new InvalidOperationException("Enumeration has not started or has ended");
                    }
                    return new DictionaryEntry(_key!, _value);
                }
            }

            object IDictionaryEnumerator.Key
            {
                get
                {
                    if (_index == 0 || _index == _sortedList._size + 1)
                    {
                        throw new InvalidOperationException("Enumeration has not started or has ended");
                    }
                    return _key!;
                }
            }

            object? IDictionaryEnumerator.Value
            {
                get
                {
                    if (_index == 0 || _index == _sortedList._size + 1)
                    {
                        throw new InvalidOperationException("Enumeration has not started or has ended");
                    }
                    return _value;
                }
            }

            public bool MoveNext()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                if ((uint)_index < (uint)_sortedList._size)
                {
                    _key = _sortedList._keys[_index];
                    _value = _sortedList._values[_index];
                    _index++;
                    return true;
                }

                _index = _sortedList._size + 1;
                _key = default;
                _value = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _index = 0;
                _key = default;
                _value = default;
            }

            public void Dispose() { }
        }

        /// <summary>Represents the collection of keys in a SortedList.</summary>
        private sealed class KeyList : IList<TKey>, ICollection
        {
            private readonly SortedList<TKey, TValue> _dict;

            internal KeyList(SortedList<TKey, TValue> dictionary)
            {
                _dict = dictionary;
            }

            public int Count => _dict._size;
            bool ICollection<TKey>.IsReadOnly => true;
            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => ((ICollection)_dict).SyncRoot;

            public TKey this[int index]
            {
                get => _dict.GetKeyAtIndex(index);
                set => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            }

            public void Add(TKey key) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public void Clear() => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public bool Contains(TKey key) => _dict.ContainsKey(key);

            public void CopyTo(TKey[] array, int arrayIndex)
            {
                Array.Copy(_dict._keys, 0, array, arrayIndex, _dict._size);
            }

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                if (array is TKey[] keys)
                {
                    CopyTo(keys, arrayIndex);
                }
                else
                {
                    throw new ArgumentException("Invalid array type");
                }
            }

            public void Insert(int index, TKey value) => throw new NotSupportedException("This operation is not supported on SortedList nested types");

            public IEnumerator<TKey> GetEnumerator()
            {
                return new SortedListKeyEnumerator(_dict);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int IndexOf(TKey key) => _dict.IndexOfKey(key);
            public bool Remove(TKey key) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public void RemoveAt(int index) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
        }

        /// <summary>Represents the collection of values in a SortedList.</summary>
        private sealed class ValueList : IList<TValue>, ICollection
        {
            private readonly SortedList<TKey, TValue> _dict;

            internal ValueList(SortedList<TKey, TValue> dictionary)
            {
                _dict = dictionary;
            }

            public int Count => _dict._size;
            bool ICollection<TValue>.IsReadOnly => true;
            bool ICollection.IsSynchronized => false;
            object ICollection.SyncRoot => ((ICollection)_dict).SyncRoot;

            public TValue this[int index]
            {
                get => _dict.GetValueAtIndex(index);
                set => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            }

            public void Add(TValue value) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public void Clear() => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public bool Contains(TValue value) => _dict.ContainsValue(value);

            public void CopyTo(TValue[] array, int arrayIndex)
            {
                Array.Copy(_dict._values, 0, array, arrayIndex, _dict._size);
            }

            void ICollection.CopyTo(Array array, int arrayIndex)
            {
                if (array is TValue[] values)
                {
                    CopyTo(values, arrayIndex);
                }
                else
                {
                    throw new ArgumentException("Invalid array type");
                }
            }

            public void Insert(int index, TValue value) => throw new NotSupportedException("This operation is not supported on SortedList nested types");

            public IEnumerator<TValue> GetEnumerator()
            {
                return new SortedListValueEnumerator(_dict);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public int IndexOf(TValue value) => _dict.IndexOfValue(value);
            public bool Remove(TValue value) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
            public void RemoveAt(int index) => throw new NotSupportedException("This operation is not supported on SortedList nested types");
        }

        private struct SortedListKeyEnumerator : IEnumerator<TKey>
        {
            private readonly SortedList<TKey, TValue> _sortedList;
            private int _index;
            private readonly int _version;
            private TKey? _currentKey;

            internal SortedListKeyEnumerator(SortedList<TKey, TValue> sortedList)
            {
                _sortedList = sortedList;
                _index = 0;
                _version = sortedList._version;
                _currentKey = default;
            }

            public TKey Current => _currentKey!;
            object IEnumerator.Current => _currentKey!;

            public bool MoveNext()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                if ((uint)_index < (uint)_sortedList._size)
                {
                    _currentKey = _sortedList._keys[_index];
                    _index++;
                    return true;
                }

                _index = _sortedList._size + 1;
                _currentKey = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _index = 0;
                _currentKey = default;
            }

            public void Dispose() { }
        }

        private struct SortedListValueEnumerator : IEnumerator<TValue>
        {
            private readonly SortedList<TKey, TValue> _sortedList;
            private int _index;
            private readonly int _version;
            private TValue? _currentValue;

            internal SortedListValueEnumerator(SortedList<TKey, TValue> sortedList)
            {
                _sortedList = sortedList;
                _index = 0;
                _version = sortedList._version;
                _currentValue = default;
            }

            public TValue Current => _currentValue!;
            object? IEnumerator.Current => _currentValue;

            public bool MoveNext()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                if ((uint)_index < (uint)_sortedList._size)
                {
                    _currentValue = _sortedList._values[_index];
                    _index++;
                    return true;
                }

                _index = _sortedList._size + 1;
                _currentValue = default;
                return false;
            }

            public void Reset()
            {
                if (_version != _sortedList._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _index = 0;
                _currentValue = default;
            }

            public void Dispose() { }
        }
    }
}
