// ProtonOS korlib - ReadOnlyCollection<T>
// Provides a read-only wrapper around a list.

using System.Collections.Generic;

namespace System.Collections.ObjectModel
{
    /// <summary>
    /// Provides the base class for a generic read-only collection.
    /// </summary>
    public class ReadOnlyCollection<T> : IList<T>, IReadOnlyList<T>, IList
    {
        private readonly IList<T> _list;

        /// <summary>Initializes a new instance of the ReadOnlyCollection class that is a read-only wrapper around the specified list.</summary>
        public ReadOnlyCollection(IList<T> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));
            _list = list;
        }

        /// <summary>Gets the element at the specified index.</summary>
        public T this[int index] => _list[index];

        T IList<T>.this[int index]
        {
            get => _list[index];
            set => throw new NotSupportedException("Collection is read-only");
        }

        object? IList.this[int index]
        {
            get => _list[index];
            set => throw new NotSupportedException("Collection is read-only");
        }

        /// <summary>Gets the number of elements contained in the ReadOnlyCollection.</summary>
        public int Count => _list.Count;

        /// <summary>Gets the IList that the ReadOnlyCollection wraps.</summary>
        protected IList<T> Items => _list;

        bool ICollection<T>.IsReadOnly => true;
        bool IList.IsReadOnly => true;
        bool IList.IsFixedSize => true;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;  // Simplified - avoid pattern matching

        /// <summary>Determines whether an element is in the ReadOnlyCollection.</summary>
        public bool Contains(T value) => _list.Contains(value);

        bool IList.Contains(object? value)
        {
            if (value is T t)
                return Contains(t);
            return false;
        }

        /// <summary>Copies the entire ReadOnlyCollection to a compatible one-dimensional Array.</summary>
        public void CopyTo(T[] array, int index) => _list.CopyTo(array, index);

        void ICollection.CopyTo(Array array, int index)
        {
            if (array is T[] tArray)
            {
                CopyTo(tArray, index);
            }
            else
            {
                throw new ArgumentException("Invalid array type");
            }
        }

        /// <summary>Returns an enumerator that iterates through the ReadOnlyCollection.</summary>
        public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();

        /// <summary>Searches for the specified object and returns the zero-based index of the first occurrence.</summary>
        public int IndexOf(T value) => _list.IndexOf(value);

        int IList.IndexOf(object? value)
        {
            if (value is T t)
                return IndexOf(t);
            return -1;
        }

        // Not supported operations
        void ICollection<T>.Add(T value) => throw new NotSupportedException("Collection is read-only");
        void ICollection<T>.Clear() => throw new NotSupportedException("Collection is read-only");
        void IList<T>.Insert(int index, T value) => throw new NotSupportedException("Collection is read-only");
        bool ICollection<T>.Remove(T value) => throw new NotSupportedException("Collection is read-only");
        void IList<T>.RemoveAt(int index) => throw new NotSupportedException("Collection is read-only");

        int IList.Add(object? value) => throw new NotSupportedException("Collection is read-only");
        void IList.Clear() => throw new NotSupportedException("Collection is read-only");
        void IList.Insert(int index, object? value) => throw new NotSupportedException("Collection is read-only");
        void IList.Remove(object? value) => throw new NotSupportedException("Collection is read-only");
        void IList.RemoveAt(int index) => throw new NotSupportedException("Collection is read-only");
    }
}
