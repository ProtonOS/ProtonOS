// ProtonOS System.Runtime - HashSet<T>
// Represents a set of values.

namespace System.Collections.Generic
{
    /// <summary>
    /// Represents a set of values.
    /// </summary>
    public class HashSet<T> : ISet<T>, ICollection<T>, IReadOnlyCollection<T>
    {
        private struct Entry
        {
            public int hashCode;
            public int next;
            public T value;
        }

        private int[]? _buckets;
        private Entry[]? _entries;
        private int _count;
        private int _freeList;
        private int _freeCount;
        private int _version;
        private readonly IEqualityComparer<T> _comparer;

        private const int StartOfFreeList = -3;

        /// <summary>Initializes a new empty HashSet.</summary>
        public HashSet() : this((IEqualityComparer<T>?)null) { }

        /// <summary>Initializes a new empty HashSet with specified comparer.</summary>
        public HashSet(IEqualityComparer<T>? comparer)
        {
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        /// <summary>Initializes a new empty HashSet with specified capacity.</summary>
        public HashSet(int capacity) : this(capacity, null) { }

        /// <summary>Initializes a new empty HashSet with specified capacity and comparer.</summary>
        public HashSet(int capacity, IEqualityComparer<T>? comparer)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (capacity > 0) Initialize(capacity);
            _comparer = comparer ?? EqualityComparer<T>.Default;
        }

        /// <summary>Initializes a new HashSet with elements from the specified collection.</summary>
        public HashSet(IEnumerable<T> collection) : this(collection, null) { }

        /// <summary>Initializes a new HashSet with elements from the specified collection and comparer.</summary>
        public HashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
            : this(comparer)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));
            if (collection is HashSet<T> otherAsHashSet && AreEqualityComparersEqual(this, otherAsHashSet))
            {
                CopyFrom(otherAsHashSet);
            }
            else
            {
                if (collection is ICollection<T> coll)
                {
                    int count = coll.Count;
                    if (count > 0) Initialize(count);
                }
                UnionWith(collection);
            }
        }

        private void CopyFrom(HashSet<T> source)
        {
            int count = source._count;
            if (count == 0) return;

            int capacity = source._buckets!.Length;
            Initialize(capacity);

            Entry[]? entries = source._entries;
            for (int i = 0; i < count; i++)
            {
                if (entries![i].hashCode >= 0)
                {
                    AddIfNotPresent(entries[i].value, out _);
                }
            }
        }

        private static bool AreEqualityComparersEqual(HashSet<T> set1, HashSet<T> set2)
        {
            return set1._comparer.Equals(set2._comparer);
        }

        /// <summary>Gets the IEqualityComparer used to determine equality of values.</summary>
        public IEqualityComparer<T> Comparer => _comparer;

        /// <summary>Gets the number of elements contained in the HashSet.</summary>
        public int Count => _count - _freeCount;

        bool ICollection<T>.IsReadOnly => false;

        /// <summary>Adds the specified element to a HashSet.</summary>
        public bool Add(T item) => AddIfNotPresent(item, out _);

        void ICollection<T>.Add(T item) => AddIfNotPresent(item, out _);

        /// <summary>Removes all elements from a HashSet.</summary>
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

        /// <summary>Determines whether a HashSet contains the specified element.</summary>
        public bool Contains(T item) => FindItemIndex(item) >= 0;

        /// <summary>Copies the elements of a HashSet to an array.</summary>
        public void CopyTo(T[] array) => CopyTo(array, 0, Count);

        /// <summary>Copies the elements of a HashSet to an array, starting at the specified index.</summary>
        public void CopyTo(T[] array, int arrayIndex) => CopyTo(array, arrayIndex, Count);

        /// <summary>Copies the specified number of elements of a HashSet to an array, starting at the specified index.</summary>
        public void CopyTo(T[] array, int arrayIndex, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (arrayIndex > array.Length || count > array.Length - arrayIndex) throw new ArgumentException("Invalid offset and count");

            Entry[]? entries = _entries;
            for (int i = 0; i < _count && count > 0; i++)
            {
                if (entries![i].hashCode >= 0)
                {
                    array[arrayIndex++] = entries[i].value;
                    count--;
                }
            }
        }

        /// <summary>Removes the specified element from a HashSet.</summary>
        public bool Remove(T item)
        {
            if (_buckets != null)
            {
                Entry[]? entries = _entries;
                int hashCode = item == null ? 0 : _comparer.GetHashCode(item) & 0x7FFFFFFF;
                int bucket = hashCode % _buckets.Length;
                int last = -1;
                int i = _buckets[bucket] - 1;

                while (i >= 0)
                {
                    ref Entry entry = ref entries![i];
                    if (entry.hashCode == hashCode && _comparer.Equals(entry.value, item))
                    {
                        if (last < 0)
                        {
                            _buckets[bucket] = entry.next + 1;
                        }
                        else
                        {
                            entries[last].next = entry.next;
                        }
                        entry.hashCode = -1;
                        entry.next = StartOfFreeList - _freeList;
                        entry.value = default!;
                        _freeList = i;
                        _freeCount++;
                        _version++;
                        return true;
                    }
                    last = i;
                    i = entry.next;
                }
            }
            return false;
        }

        /// <summary>Returns an enumerator that iterates through a HashSet.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        private int FindItemIndex(T item)
        {
            if (_buckets != null)
            {
                Entry[]? entries = _entries;
                int hashCode = item == null ? 0 : _comparer.GetHashCode(item) & 0x7FFFFFFF;
                int i = _buckets[hashCode % _buckets.Length] - 1;

                while (i >= 0)
                {
                    if (entries![i].hashCode == hashCode && _comparer.Equals(entries[i].value, item))
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

        private bool AddIfNotPresent(T value, out int location)
        {
            if (_buckets == null) Initialize(0);

            Entry[]? entries = _entries;
            int hashCode = value == null ? 0 : _comparer.GetHashCode(value) & 0x7FFFFFFF;
            int bucket = hashCode % _buckets!.Length;
            int i = _buckets[bucket] - 1;

            while (i >= 0)
            {
                if (entries![i].hashCode == hashCode && _comparer.Equals(entries[i].value, value))
                {
                    location = i;
                    return false;
                }
                i = entries[i].next;
            }

            int index;
            if (_freeCount > 0)
            {
                index = _freeList;
                _freeList = StartOfFreeList - entries![_freeList].next;
                _freeCount--;
            }
            else
            {
                if (_count == entries!.Length)
                {
                    Resize();
                    bucket = hashCode % _buckets.Length;
                }
                index = _count;
                _count++;
                entries = _entries;
            }

            entries![index].hashCode = hashCode;
            entries[index].next = _buckets[bucket] - 1;
            entries[index].value = value;
            _buckets[bucket] = index + 1;
            _version++;
            location = index;
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

        // Set operations

        /// <summary>Modifies the current HashSet to contain all elements that are present in itself, the specified collection, or both.</summary>
        public void UnionWith(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            foreach (T item in other)
            {
                AddIfNotPresent(item, out _);
            }
        }

        /// <summary>Modifies the current HashSet to contain only elements that are present in both itself and the specified collection.</summary>
        public void IntersectWith(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0) return;

            if (other is ICollection<T> otherAsCollection)
            {
                if (otherAsCollection.Count == 0)
                {
                    Clear();
                    return;
                }
            }

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            Entry[]? entries = _entries;
            for (int i = 0; i < _count; i++)
            {
                if (entries![i].hashCode >= 0)
                {
                    T item = entries[i].value;
                    if (!otherSet.Contains(item))
                    {
                        Remove(item);
                    }
                }
            }
        }

        /// <summary>Removes all elements in the specified collection from the current HashSet.</summary>
        public void ExceptWith(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0) return;
            if (other == this)
            {
                Clear();
                return;
            }
            foreach (T item in other)
            {
                Remove(item);
            }
        }

        /// <summary>Modifies the current HashSet to contain only elements that are present either in itself or in the specified collection, but not both.</summary>
        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0)
            {
                UnionWith(other);
                return;
            }
            if (other == this)
            {
                Clear();
                return;
            }

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            foreach (T item in otherSet)
            {
                if (!Remove(item))
                {
                    AddIfNotPresent(item, out _);
                }
            }
        }

        /// <summary>Determines whether a HashSet is a subset of the specified collection.</summary>
        public bool IsSubsetOf(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0) return true;

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            if (Count > otherSet.Count) return false;

            return IsSubsetOfHashSet(otherSet);
        }

        /// <summary>Determines whether a HashSet is a proper subset of the specified collection.</summary>
        public bool IsProperSubsetOf(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (other is ICollection<T> otherAsCollection)
            {
                if (Count == 0) return otherAsCollection.Count > 0;
            }

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            if (Count >= otherSet.Count) return false;

            return IsSubsetOfHashSet(otherSet);
        }

        /// <summary>Determines whether a HashSet is a superset of the specified collection.</summary>
        public bool IsSupersetOf(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            if (other is ICollection<T> otherAsCollection)
            {
                if (otherAsCollection.Count == 0) return true;
            }

            foreach (T item in other)
            {
                if (!Contains(item)) return false;
            }
            return true;
        }

        /// <summary>Determines whether a HashSet is a proper superset of the specified collection.</summary>
        public bool IsProperSupersetOf(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0) return false;

            if (other is ICollection<T> otherAsCollection)
            {
                if (otherAsCollection.Count == 0) return true;
            }

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            if (otherSet.Count >= Count) return false;

            return IsSupersetOf(otherSet);
        }

        /// <summary>Determines whether the current HashSet and the specified collection contain the same elements.</summary>
        public bool SetEquals(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));

            HashSet<T> otherSet = other as HashSet<T> ?? new HashSet<T>(other, _comparer);
            if (Count != otherSet.Count) return false;

            return IsSubsetOfHashSet(otherSet);
        }

        /// <summary>Determines whether the current HashSet overlaps with the specified collection.</summary>
        public bool Overlaps(IEnumerable<T> other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Count == 0) return false;

            foreach (T item in other)
            {
                if (Contains(item)) return true;
            }
            return false;
        }

        private bool IsSubsetOfHashSet(HashSet<T> other)
        {
            Entry[]? entries = _entries;
            for (int i = 0; i < _count; i++)
            {
                if (entries![i].hashCode >= 0)
                {
                    if (!other.Contains(entries[i].value)) return false;
                }
            }
            return true;
        }

        /// <summary>Removes all elements that match the conditions defined by the specified predicate.</summary>
        public int RemoveWhere(Predicate<T> match)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));

            Entry[]? entries = _entries;
            int numRemoved = 0;
            for (int i = 0; i < _count; i++)
            {
                if (entries![i].hashCode >= 0)
                {
                    T value = entries[i].value;
                    if (match(value))
                    {
                        if (Remove(value))
                        {
                            numRemoved++;
                        }
                    }
                }
            }
            return numRemoved;
        }

        /// <summary>Sets the capacity of a HashSet to the actual number of elements it contains.</summary>
        public void TrimExcess()
        {
            int count = Count;
            int newSize = GetPrime(count);
            if (newSize >= (_entries?.Length ?? 0)) return;

            Entry[] oldEntries = _entries!;
            int oldCount = _count;

            Initialize(newSize);

            for (int i = 0; i < oldCount; i++)
            {
                if (oldEntries[i].hashCode >= 0)
                {
                    AddIfNotPresent(oldEntries[i].value, out _);
                }
            }
        }

        // Simple prime number helpers
        private static readonly int[] s_primes = {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591, 17519
        };

        private static int GetPrime(int min)
        {
            if (min < 0) throw new ArgumentException("Capacity overflow");
            foreach (int prime in s_primes)
            {
                if (prime >= min) return prime;
            }
            return min | 1;
        }

        private static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;
            if ((uint)newSize > int.MaxValue) return int.MaxValue;
            return GetPrime(newSize);
        }

        /// <summary>Enumerates the elements of a HashSet.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly HashSet<T> _hashSet;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(HashSet<T> hashSet)
            {
                _hashSet = hashSet;
                _version = hashSet._version;
                _index = 0;
                _current = default!;
            }

            public T Current => _current;
            object? IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _hashSet._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                while ((uint)_index < (uint)_hashSet._count)
                {
                    ref Entry entry = ref _hashSet._entries![_index++];
                    if (entry.hashCode >= 0)
                    {
                        _current = entry.value;
                        return true;
                    }
                }

                _index = _hashSet._count + 1;
                _current = default!;
                return false;
            }

            public void Reset()
            {
                if (_version != _hashSet._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _index = 0;
                _current = default!;
            }

            public void Dispose() { }
        }
    }
}
