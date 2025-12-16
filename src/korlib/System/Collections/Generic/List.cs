// korlib - List<T>
// Represents a strongly typed list of objects that can be accessed by index.

namespace System.Collections.Generic;

/// <summary>
/// Represents a strongly typed list of objects that can be accessed by index.
/// </summary>
public class List<T> : IList<T>, IReadOnlyList<T>, IList
{
    private const int DefaultCapacity = 4;

    private T[] _items;
    private int _size;
    private int _version;

    /// <summary>
    /// Initializes a new instance of the List class that is empty and has the default initial capacity.
    /// </summary>
    public List()
    {
        _items = new T[0];
    }

    /// <summary>
    /// Initializes a new instance of the List class that is empty and has the specified initial capacity.
    /// </summary>
    public List(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _items = capacity == 0 ? new T[0] : new T[capacity];
    }

    /// <summary>
    /// Initializes a new instance of the List class that contains elements copied from the specified collection.
    /// </summary>
    public List(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));

        if (collection is ICollection<T> c)
        {
            int count = c.Count;
            if (count == 0)
            {
                _items = new T[0];
            }
            else
            {
                _items = new T[count];
                c.CopyTo(_items, 0);
                _size = count;
            }
        }
        else
        {
            _items = new T[0];
            foreach (T item in collection)
            {
                Add(item);
            }
        }
    }

    /// <summary>Gets or sets the total number of elements the internal data structure can hold.</summary>
    public int Capacity
    {
        get => _items.Length;
        set
        {
            if (value < _size) throw new ArgumentOutOfRangeException(nameof(value));
            if (value != _items.Length)
            {
                if (value > 0)
                {
                    T[] newItems = new T[value];
                    if (_size > 0)
                    {
                        Array.Copy(_items, newItems, _size);
                    }
                    _items = newItems;
                }
                else
                {
                    _items = new T[0];
                }
            }
        }
    }

    /// <summary>Gets the number of elements contained in the List.</summary>
    public int Count => _size;

    bool ICollection<T>.IsReadOnly => false;
    bool IList.IsReadOnly => false;
    bool IList.IsFixedSize => false;
    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    /// <summary>Gets or sets the element at the specified index.</summary>
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
        }
        set
        {
            if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
            _items[index] = value;
            _version++;
        }
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }

    /// <summary>Adds an object to the end of the List.</summary>
    public void Add(T item)
    {
        T[] array = _items;
        int size = _size;
        _version++;
        if ((uint)size < (uint)array.Length)
        {
            _size = size + 1;
            array[size] = item;
        }
        else
        {
            AddWithResize(item);
        }
    }

    private void AddWithResize(T item)
    {
        int size = _size;
        Grow(size + 1);
        _size = size + 1;
        _items[size] = item;
    }

    int IList.Add(object? item)
    {
        Add((T)item!);
        return _size - 1;
    }

    /// <summary>Adds the elements of the specified collection to the end of the List.</summary>
    public void AddRange(IEnumerable<T> collection)
    {
        InsertRange(_size, collection);
    }

    /// <summary>Removes all elements from the List.</summary>
    public void Clear()
    {
        _version++;
        if (_size > 0)
        {
            Array.Clear(_items, 0, _size);
            _size = 0;
        }
    }

    /// <summary>Determines whether an element is in the List.</summary>
    public bool Contains(T item)
    {
        return _size > 0 && IndexOf(item) >= 0;
    }

    bool IList.Contains(object? item) => item is T t && Contains(t);

    /// <summary>Copies the entire List to a compatible one-dimensional array.</summary>
    public void CopyTo(T[] array) => CopyTo(array, 0);

    /// <summary>Copies the entire List to a compatible one-dimensional array, starting at the specified index.</summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        Array.Copy(_items, 0, array, arrayIndex, _size);
    }

    void ICollection.CopyTo(Array array, int arrayIndex)
    {
        Array.Copy(_items, 0, array, arrayIndex, _size);
    }

    /// <summary>Copies a range of elements from the List to a compatible one-dimensional array.</summary>
    public void CopyTo(int index, T[] array, int arrayIndex, int count)
    {
        if (_size - index < count) throw new ArgumentException("Invalid offset and count");
        Array.Copy(_items, index, array, arrayIndex, count);
    }

    private void Grow(int capacity)
    {
        int newCapacity = _items.Length == 0 ? DefaultCapacity : 2 * _items.Length;
        if (newCapacity < capacity) newCapacity = capacity;
        Capacity = newCapacity;
    }

    /// <summary>Determines whether the List contains elements that match the conditions defined by the specified predicate.</summary>
    public bool Exists(Predicate<T> match)
    {
        return FindIndex(match) >= 0;
    }

    /// <summary>Searches for an element that matches the conditions defined by the specified predicate.</summary>
    public T? Find(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        for (int i = 0; i < _size; i++)
        {
            if (match(_items[i])) return _items[i];
        }
        return default;
    }

    /// <summary>Retrieves all the elements that match the conditions defined by the specified predicate.</summary>
    public List<T> FindAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        List<T> list = new List<T>();
        for (int i = 0; i < _size; i++)
        {
            if (match(_items[i])) list.Add(_items[i]);
        }
        return list;
    }

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the first occurrence.</summary>
    public int FindIndex(Predicate<T> match) => FindIndex(0, _size, match);

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the first occurrence.</summary>
    public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, _size - startIndex, match);

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the first occurrence.</summary>
    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        if ((uint)startIndex > (uint)_size) throw new ArgumentOutOfRangeException(nameof(startIndex));
        if (count < 0 || startIndex > _size - count) throw new ArgumentOutOfRangeException(nameof(count));
        if (match == null) throw new ArgumentNullException(nameof(match));

        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (match(_items[i])) return i;
        }
        return -1;
    }

    /// <summary>Searches for an element that matches the conditions and returns the last occurrence.</summary>
    public T? FindLast(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        for (int i = _size - 1; i >= 0; i--)
        {
            if (match(_items[i])) return _items[i];
        }
        return default;
    }

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the last occurrence.</summary>
    public int FindLastIndex(Predicate<T> match) => _size > 0 ? FindLastIndex(_size - 1, _size, match) : -1;

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the last occurrence.</summary>
    public int FindLastIndex(int startIndex, Predicate<T> match) => FindLastIndex(startIndex, startIndex + 1, match);

    /// <summary>Searches for an element that matches the conditions and returns the zero-based index of the last occurrence.</summary>
    public int FindLastIndex(int startIndex, int count, Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        if (_size == 0)
        {
            if (startIndex != -1) throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
        else
        {
            if ((uint)startIndex >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(startIndex));
        }
        if (count < 0 || startIndex - count + 1 < 0) throw new ArgumentOutOfRangeException(nameof(count));

        int endIndex = startIndex - count;
        for (int i = startIndex; i > endIndex; i--)
        {
            if (match(_items[i])) return i;
        }
        return -1;
    }

    /// <summary>Performs the specified action on each element of the List.</summary>
    public void ForEach(Action<T> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        int version = _version;
        for (int i = 0; i < _size; i++)
        {
            if (version != _version) throw new InvalidOperationException("Collection was modified");
            action(_items[i]);
        }
    }

    /// <summary>Returns an enumerator that iterates through the List.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <summary>Creates a shallow copy of a range of elements in the source List.</summary>
    public List<T> GetRange(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_size - index < count) throw new ArgumentException("Invalid offset and count");

        List<T> list = new List<T>(count);
        Array.Copy(_items, index, list._items, 0, count);
        list._size = count;
        return list;
    }

    /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence.</summary>
#if KORLIB_IL
    public int IndexOf(T item) => IndexOfInternal(_items, item, 0, _size);
#else
    public int IndexOf(T item) => Array.IndexOf(_items, item, 0, _size);
#endif

    int IList.IndexOf(object? item) => item is T t ? IndexOf(t) : -1;

    /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence within the range.</summary>
#if KORLIB_IL
    public int IndexOf(T item, int index) => IndexOfInternal(_items, item, index, _size - index);
#else
    public int IndexOf(T item, int index) => Array.IndexOf(_items, item, index, _size - index);
#endif

    /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence within the range.</summary>
#if KORLIB_IL
    public int IndexOf(T item, int index, int count) => IndexOfInternal(_items, item, index, count);
#else
    public int IndexOf(T item, int index, int count) => Array.IndexOf(_items, item, index, count);
#endif

#if KORLIB_IL
    /// <summary>Internal IndexOf implementation for JIT compilation from korlib.dll.</summary>
    private static int IndexOfInternal(T[] array, T value, int startIndex, int count)
    {
        int endIndex = startIndex + count;
        for (int i = startIndex; i < endIndex; i++)
        {
            if (EqualityComparer<T>.Default.Equals(array[i], value))
                return i;
        }
        return -1;
    }
#endif

    /// <summary>Inserts an element into the List at the specified index.</summary>
    public void Insert(int index, T item)
    {
        if ((uint)index > (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
        if (_size == _items.Length) Grow(_size + 1);
        if (index < _size)
        {
            Array.Copy(_items, index, _items, index + 1, _size - index);
        }
        _items[index] = item;
        _size++;
        _version++;
    }

    void IList.Insert(int index, object? item) => Insert(index, (T)item!);

    /// <summary>Inserts the elements of a collection into the List at the specified index.</summary>
    public void InsertRange(int index, IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        if ((uint)index > (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));

        // Check for arrays first - arrays in our runtime don't implement ICollection<T>
        // interface yet, so we handle them specially for performance
        if (collection is T[] array)
        {
            int count = array.Length;
            if (count > 0)
            {
                if (_items.Length - _size < count)
                {
                    Grow(_size + count);
                }
                if (index < _size)
                {
                    Array.Copy(_items, index, _items, index + count, _size - index);
                }
                Array.Copy(array, 0, _items, index, count);
                _size += count;
            }
        }
        else if (collection is ICollection<T> c)
        {
            int count = c.Count;
            if (count > 0)
            {
                if (_items.Length - _size < count)
                {
                    Grow(_size + count);
                }
                if (index < _size)
                {
                    Array.Copy(_items, index, _items, index + count, _size - index);
                }
                c.CopyTo(_items, index);
                _size += count;
            }
        }
        else
        {
            foreach (T item in collection)
            {
                Insert(index++, item);
            }
        }
        _version++;
    }

    /// <summary>Searches for the specified object and returns the zero-based index of the last occurrence.</summary>
    public int LastIndexOf(T item) => _size > 0 ? LastIndexOf(item, _size - 1, _size) : -1;

    /// <summary>Searches for the specified object and returns the zero-based index of the last occurrence within the range.</summary>
    public int LastIndexOf(T item, int index) => LastIndexOf(item, index, index + 1);

    /// <summary>Searches for the specified object and returns the zero-based index of the last occurrence within the range.</summary>
    public int LastIndexOf(T item, int index, int count)
    {
        if (_size == 0) return -1;
        if (index < 0 || count < 0) throw new ArgumentOutOfRangeException();
        if (index >= _size || count > index + 1) throw new ArgumentOutOfRangeException();

        return Array.LastIndexOf(_items, item, index, count);
    }

    /// <summary>Removes the first occurrence of a specific object from the List.</summary>
    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }
        return false;
    }

    void IList.Remove(object? item)
    {
        if (item is T t) Remove(t);
    }

    /// <summary>Removes all the elements that match the conditions defined by the specified predicate.</summary>
    public int RemoveAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));

        int freeIndex = 0;
        while (freeIndex < _size && !match(_items[freeIndex])) freeIndex++;
        if (freeIndex >= _size) return 0;

        int current = freeIndex + 1;
        while (current < _size)
        {
            while (current < _size && match(_items[current])) current++;
            if (current < _size)
            {
                _items[freeIndex++] = _items[current++];
            }
        }

        Array.Clear(_items, freeIndex, _size - freeIndex);
        int result = _size - freeIndex;
        _size = freeIndex;
        _version++;
        return result;
    }

    /// <summary>Removes the element at the specified index of the List.</summary>
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_size) throw new ArgumentOutOfRangeException(nameof(index));
        _size--;
        if (index < _size)
        {
            Array.Copy(_items, index + 1, _items, index, _size - index);
        }
        _items[_size] = default!;
        _version++;
    }

    /// <summary>Removes a range of elements from the List.</summary>
    public void RemoveRange(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_size - index < count) throw new ArgumentException("Invalid offset and count");

        if (count > 0)
        {
            _size -= count;
            if (index < _size)
            {
                Array.Copy(_items, index + count, _items, index, _size - index);
            }
            Array.Clear(_items, _size, count);
            _version++;
        }
    }

    /// <summary>Reverses the order of the elements in the entire List.</summary>
    public void Reverse() => Reverse(0, _size);

    /// <summary>Reverses the order of the elements in the specified range.</summary>
    public void Reverse(int index, int count)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_size - index < count) throw new ArgumentException("Invalid offset and count");

        Array.Reverse(_items, index, count);
        _version++;
    }

    /// <summary>Sorts the elements in the entire List using the default comparer.</summary>
    public void Sort() => Sort(0, _size, null);

    /// <summary>Sorts the elements in the entire List using the specified comparer.</summary>
    public void Sort(IComparer<T>? comparer) => Sort(0, _size, comparer);

    /// <summary>Sorts the elements in a range of elements in the List using the specified comparer.</summary>
    public void Sort(int index, int count, IComparer<T>? comparer)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (_size - index < count) throw new ArgumentException("Invalid offset and count");

        if (count > 1)
        {
            // Use local sort implementation to avoid SDK Array.Sort signature differences
            SortHelper(_items, index, count, comparer);
        }
        _version++;
    }

    /// <summary>Sorts the elements in the entire List using the specified comparison.</summary>
    public void Sort(Comparison<T> comparison)
    {
        if (comparison == null) throw new ArgumentNullException(nameof(comparison));
        if (_size > 1)
        {
            SortHelper(_items, 0, _size, Comparer<T>.Create(comparison));
        }
        _version++;
    }

    // Local sort implementation - uses QuickSort with InsertionSort for small partitions
    private static void SortHelper(T[] array, int index, int length, IComparer<T>? comparer)
    {
        if (length < 2) return;

        // Use insertion sort for small arrays
        if (length <= 16)
        {
            InsertionSortHelper(array, index, length, comparer);
            return;
        }

        // QuickSort for larger arrays
        QuickSortHelper(array, index, index + length - 1, comparer);
    }

    private static void InsertionSortHelper(T[] array, int index, int length, IComparer<T>? comparer)
    {
        for (int i = index + 1; i < index + length; i++)
        {
            T key = array[i];
            int j = i - 1;
            while (j >= index && CompareHelper(array[j], key, comparer) > 0)
            {
                array[j + 1] = array[j];
                j--;
            }
            array[j + 1] = key;
        }
    }

    private static void QuickSortHelper(T[] array, int left, int right, IComparer<T>? comparer)
    {
        while (left < right)
        {
            // Use insertion sort for small partitions
            if (right - left < 16)
            {
                InsertionSortHelper(array, left, right - left + 1, comparer);
                return;
            }

            // Median-of-three pivot selection
            int mid = left + (right - left) / 2;
            if (CompareHelper(array[left], array[mid], comparer) > 0)
                Swap(array, left, mid);
            if (CompareHelper(array[left], array[right], comparer) > 0)
                Swap(array, left, right);
            if (CompareHelper(array[mid], array[right], comparer) > 0)
                Swap(array, mid, right);

            T pivot = array[mid];
            Swap(array, mid, right - 1);

            int i = left;
            int j = right - 1;

            while (true)
            {
                while (CompareHelper(array[++i], pivot, comparer) < 0) { }
                while (CompareHelper(array[--j], pivot, comparer) > 0) { }
                if (i >= j) break;
                Swap(array, i, j);
            }

            Swap(array, i, right - 1);

            // Tail recursion optimization - recurse on smaller partition
            if (i - left < right - i)
            {
                QuickSortHelper(array, left, i - 1, comparer);
                left = i + 1;
            }
            else
            {
                QuickSortHelper(array, i + 1, right, comparer);
                right = i - 1;
            }
        }
    }

    private static void Swap(T[] array, int i, int j)
    {
        T temp = array[i];
        array[i] = array[j];
        array[j] = temp;
    }

    private static int CompareHelper(T x, T y, IComparer<T>? comparer)
    {
        // Use Comparer<T>.Default when no comparer is provided
        // This ensures proper comparison via IComparable<T> or IComparable
        if (comparer == null)
            comparer = Comparer<T>.Default;
        return comparer.Compare(x, y);
    }

    /// <summary>Copies the elements of the List to a new array.</summary>
    public T[] ToArray()
    {
        if (_size == 0) return new T[0];
        T[] array = new T[_size];
        Array.Copy(_items, array, _size);
        return array;
    }

    /// <summary>Sets the capacity to the actual number of elements in the List.</summary>
    public void TrimExcess()
    {
        int threshold = (int)(_items.Length * 0.9);
        if (_size < threshold)
        {
            Capacity = _size;
        }
    }

    /// <summary>Determines whether every element in the List matches the conditions defined by the specified predicate.</summary>
    public bool TrueForAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));
        for (int i = 0; i < _size; i++)
        {
            if (!match(_items[i])) return false;
        }
        return true;
    }

    /// <summary>Enumerates the elements of a List.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly List<T> _list;
        private int _index;
        private readonly int _version;
        private T? _current;

        internal Enumerator(List<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            _current = default;
        }

        public T Current => _current!;
        object? IEnumerator.Current => _current;

        public bool MoveNext()
        {
            List<T> localList = _list;
            if (_version == localList._version && (uint)_index < (uint)localList._size)
            {
                _current = localList._items[_index];
                _index++;
                return true;
            }
            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }
            _index = _list._size + 1;
            _current = default;
            return false;
        }

        public void Reset()
        {
            if (_version != _list._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }
            _index = 0;
            _current = default;
        }

        public void Dispose() { }
    }
}
