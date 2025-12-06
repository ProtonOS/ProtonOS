// ProtonOS System.Runtime - Collection<T>
// Provides the base class for a generic collection.

using System.Collections.Generic;

namespace System.Collections.ObjectModel
{
    /// <summary>
    /// Provides the base class for a generic collection.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    public class Collection<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private readonly IList<T> _items;

        /// <summary>
        /// Initializes a new instance of the Collection class that is empty.
        /// </summary>
        public Collection()
        {
            _items = new List<T>();
        }

        /// <summary>
        /// Initializes a new instance of the Collection class as a wrapper for the specified list.
        /// </summary>
        public Collection(IList<T> list)
        {
            _items = list ?? throw new ArgumentNullException(nameof(list));
        }

        /// <summary>Gets the number of elements actually contained in the Collection.</summary>
        public int Count => _items.Count;

        /// <summary>Gets a IList wrapper around the Collection.</summary>
        protected IList<T> Items => _items;

        /// <summary>Gets or sets the element at the specified index.</summary>
        public T this[int index]
        {
            get => _items[index];
            set
            {
                if (_items.IsReadOnly)
                {
                    throw new NotSupportedException("Collection is read-only");
                }
                if ((uint)index >= (uint)_items.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                SetItem(index, value);
            }
        }

        object? IList.this[int index]
        {
            get => _items[index];
            set => this[index] = (T)value!;
        }

        bool ICollection<T>.IsReadOnly => _items.IsReadOnly;
        bool IList.IsReadOnly => _items.IsReadOnly;
        bool IList.IsFixedSize => _items is IList list ? list.IsFixedSize : _items.IsReadOnly;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => _items is ICollection c ? c.SyncRoot : this;

        /// <summary>Adds an object to the end of the Collection.</summary>
        public void Add(T item)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            int index = _items.Count;
            InsertItem(index, item);
        }

        int IList.Add(object? value)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            ThrowIfNotCompatible(value);
            Add((T)value!);
            return Count - 1;
        }

        /// <summary>Removes all elements from the Collection.</summary>
        public void Clear()
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            ClearItems();
        }

        /// <summary>Copies the entire Collection to a compatible one-dimensional Array.</summary>
        public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (array.Rank != 1) throw new ArgumentException("Multi-dimensional arrays not supported");
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < Count) throw new ArgumentException("Insufficient space in destination array");

            if (array is T[] tArray)
            {
                _items.CopyTo(tArray, index);
            }
            else
            {
                if (array is not object?[] objArray)
                    throw new ArgumentException("Invalid array type");

                int count = _items.Count;
                for (int i = 0; i < count; i++)
                {
                    objArray[index++] = _items[i];
                }
            }
        }

        /// <summary>Determines whether an element is in the Collection.</summary>
        public bool Contains(T item) => _items.Contains(item);

        bool IList.Contains(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return Contains((T)value!);
            }
            return false;
        }

        /// <summary>Returns an enumerator that iterates through the Collection.</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence.</summary>
        public int IndexOf(T item) => _items.IndexOf(item);

        int IList.IndexOf(object? value)
        {
            if (IsCompatibleObject(value))
            {
                return IndexOf((T)value!);
            }
            return -1;
        }

        /// <summary>Inserts an element into the Collection at the specified index.</summary>
        public void Insert(int index, T item)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            if ((uint)index > (uint)_items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            InsertItem(index, item);
        }

        void IList.Insert(int index, object? value)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            ThrowIfNotCompatible(value);
            Insert(index, (T)value!);
        }

        /// <summary>Removes the first occurrence of a specific object from the Collection.</summary>
        public bool Remove(T item)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            int index = _items.IndexOf(item);
            if (index < 0)
            {
                return false;
            }
            RemoveItem(index);
            return true;
        }

        void IList.Remove(object? value)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            if (IsCompatibleObject(value))
            {
                Remove((T)value!);
            }
        }

        /// <summary>Removes the element at the specified index of the Collection.</summary>
        public void RemoveAt(int index)
        {
            if (_items.IsReadOnly)
            {
                throw new NotSupportedException("Collection is read-only");
            }
            if ((uint)index >= (uint)_items.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }
            RemoveItem(index);
        }

        /// <summary>Removes all elements from the Collection. Subclasses can override to add behavior.</summary>
        protected virtual void ClearItems()
        {
            _items.Clear();
        }

        /// <summary>Inserts an element into the Collection at the specified index. Subclasses can override to add behavior.</summary>
        protected virtual void InsertItem(int index, T item)
        {
            _items.Insert(index, item);
        }

        /// <summary>Removes the element at the specified index of the Collection. Subclasses can override to add behavior.</summary>
        protected virtual void RemoveItem(int index)
        {
            _items.RemoveAt(index);
        }

        /// <summary>Replaces the element at the specified index. Subclasses can override to add behavior.</summary>
        protected virtual void SetItem(int index, T item)
        {
            _items[index] = item;
        }

        private static bool IsCompatibleObject(object? value)
        {
            return value is T || (value == null && default(T) == null);
        }

        private static void ThrowIfNotCompatible(object? value)
        {
            if (!IsCompatibleObject(value))
            {
                throw new ArgumentException($"Value is not of type {typeof(T)}", nameof(value));
            }
        }
    }
}
