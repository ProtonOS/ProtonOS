// ProtonOS korlib - Collection<T>
// Provides the base class for a generic collection.
// Note: Uses List<T> directly instead of IList<T> to avoid JIT interface dispatch issues.

using System.Collections.Generic;

namespace System.Collections.ObjectModel
{
    /// <summary>
    /// Provides the base class for a generic collection.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    public class Collection<T> : IList<T>, IReadOnlyList<T>, IList
    {
        // Using List<T> directly instead of IList<T> to avoid interface dispatch issues
        private readonly List<T> _items;

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
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            // Copy items instead of wrapping - avoid interface dispatch
            _items = new List<T>();
            for (int i = 0; i < list.Count; i++)
                _items.Add(list[i]);
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
                if ((uint)index >= (uint)_items.Count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                SetItem(index, value);
            }
        }

        object? IList.this[int index]
        {
            get => _items[index];
            set => this[index] = (T)value!;
        }

        bool ICollection<T>.IsReadOnly => false;
        bool IList.IsReadOnly => false;
        bool IList.IsFixedSize => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        /// <summary>Adds an object to the end of the Collection.</summary>
        public void Add(T item)
        {
            int index = _items.Count;
            InsertItem(index, item);
        }

        int IList.Add(object? value)
        {
            Add((T)value!);
            return Count - 1;
        }

        /// <summary>Removes all elements from the Collection.</summary>
        public void Clear()
        {
            ClearItems();
        }

        void IList.Clear() => Clear();

        /// <summary>Copies the entire Collection to a compatible one-dimensional Array.</summary>
        public void CopyTo(T[] array, int index) => _items.CopyTo(array, index);

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is T[] tArray)
            {
                _items.CopyTo(tArray, index);
            }
            else
            {
                throw new ArgumentException("Invalid array type");
            }
        }

        /// <summary>Determines whether an element is in the Collection.</summary>
        public bool Contains(T item) => _items.Contains(item);

        bool IList.Contains(object? value)
        {
            if (value is T t)
                return Contains(t);
            return false;
        }

        /// <summary>Returns an enumerator that iterates through the Collection.</summary>
        public IEnumerator<T> GetEnumerator() => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();

        /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence.</summary>
        public int IndexOf(T item) => _items.IndexOf(item);

        int IList.IndexOf(object? value)
        {
            if (value is T t)
                return IndexOf(t);
            return -1;
        }

        /// <summary>Inserts an element into the Collection at the specified index.</summary>
        public void Insert(int index, T item)
        {
            if ((uint)index > (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            InsertItem(index, item);
        }

        void IList.Insert(int index, object? value)
        {
            Insert(index, (T)value!);
        }

        /// <summary>Removes the first occurrence of a specific object from the Collection.</summary>
        public bool Remove(T item)
        {
            int index = _items.IndexOf(item);
            if (index < 0)
                return false;
            RemoveItem(index);
            return true;
        }

        void IList.Remove(object? value)
        {
            if (value is T t)
                Remove(t);
        }

        /// <summary>Removes the element at the specified index of the Collection.</summary>
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)_items.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            RemoveItem(index);
        }

        void IList.RemoveAt(int index) => RemoveAt(index);

        /// <summary>Removes all elements from the Collection.</summary>
        protected void ClearItems()
        {
            _items.Clear();
        }

        /// <summary>Inserts an element into the Collection at the specified index.</summary>
        protected void InsertItem(int index, T item)
        {
            _items.Insert(index, item);
        }

        /// <summary>Removes the element at the specified index of the Collection.</summary>
        protected void RemoveItem(int index)
        {
            _items.RemoveAt(index);
        }

        /// <summary>Replaces the element at the specified index.</summary>
        protected void SetItem(int index, T item)
        {
            _items[index] = item;
        }
    }
}
