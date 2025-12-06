// ProtonOS System.Runtime - ArraySegment<T>
// Delimits a section of a one-dimensional array.

using System.Collections;
using System.Collections.Generic;

namespace System
{
    /// <summary>
    /// Delimits a section of a one-dimensional array.
    /// </summary>
    public readonly struct ArraySegment<T> : IList<T>, IReadOnlyList<T>
    {
        /// <summary>Represents the empty array segment.</summary>
        public static ArraySegment<T> Empty => default;

        private readonly T[]? _array;
        private readonly int _offset;
        private readonly int _count;

        /// <summary>Gets the original array containing the range of elements that the array segment delimits.</summary>
        public T[]? Array => _array;

        /// <summary>Gets the position of the first element in the range delimited by the array segment.</summary>
        public int Offset => _offset;

        /// <summary>Gets the number of elements in the range delimited by the array segment.</summary>
        public int Count => _count;

        /// <summary>Gets or sets the element at the specified index.</summary>
        public T this[int index]
        {
            get
            {
                if (_array == null)
                    throw new InvalidOperationException("Array is null");
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                return _array[_offset + index];
            }
            set
            {
                if (_array == null)
                    throw new InvalidOperationException("Array is null");
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));
                _array[_offset + index] = value;
            }
        }

        /// <summary>Initializes a new instance of the ArraySegment structure that delimits all the elements in the specified array.</summary>
        public ArraySegment(T[] array)
        {
            _array = array ?? throw new ArgumentNullException(nameof(array));
            _offset = 0;
            _count = array.Length;
        }

        /// <summary>Initializes a new instance of the ArraySegment structure that delimits the specified range of elements in the specified array.</summary>
        public ArraySegment(T[] array, int offset, int count)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (array.Length - offset < count) throw new ArgumentException("Invalid offset and count");

            _array = array;
            _offset = offset;
            _count = count;
        }

        /// <summary>Copies the contents of this ArraySegment into a destination array.</summary>
        public void CopyTo(T[] destination) => CopyTo(destination, 0);

        /// <summary>Copies the contents of this ArraySegment into a destination array starting at the specified destination index.</summary>
        public void CopyTo(T[] destination, int destinationIndex)
        {
            if (_array == null)
                throw new InvalidOperationException("Array is null");
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));
            if (destinationIndex < 0 || destination.Length - destinationIndex < _count)
                throw new ArgumentOutOfRangeException(nameof(destinationIndex));

            for (int i = 0; i < _count; i++)
                destination[destinationIndex + i] = _array[_offset + i];
        }

        /// <summary>Copies the contents of this ArraySegment into a destination ArraySegment.</summary>
        public void CopyTo(ArraySegment<T> destination)
        {
            if (_array == null)
                throw new InvalidOperationException("Array is null");
            if (destination._array == null)
                throw new InvalidOperationException("Destination array is null");
            if (destination._count < _count)
                throw new ArgumentException("Destination too small");

            for (int i = 0; i < _count; i++)
                destination._array[destination._offset + i] = _array[_offset + i];
        }

        /// <summary>Returns an empty ArraySegment if the current ArraySegment equals Empty; otherwise, returns the current instance.</summary>
        public ArraySegment<T> Slice(int index)
        {
            if (_array == null)
                throw new InvalidOperationException("Array is null");
            if ((uint)index > (uint)_count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new ArraySegment<T>(_array, _offset + index, _count - index);
        }

        /// <summary>Forms a slice out of the current ArraySegment starting at index for the specified count.</summary>
        public ArraySegment<T> Slice(int index, int count)
        {
            if (_array == null)
                throw new InvalidOperationException("Array is null");
            if ((uint)index > (uint)_count || (uint)count > (uint)(_count - index))
                throw new ArgumentOutOfRangeException();
            return new ArraySegment<T>(_array, _offset + index, count);
        }

        /// <summary>Copies the contents of this ArraySegment into a new array.</summary>
        public T[] ToArray()
        {
            if (_array == null || _count == 0)
                return System.Array.Empty<T>();

            T[] result = new T[_count];
            for (int i = 0; i < _count; i++)
                result[i] = _array[_offset + i];
            return result;
        }

        // Equality
        public override bool Equals(object? obj)
        {
            return obj is ArraySegment<T> other &&
                   _array == other._array &&
                   _offset == other._offset &&
                   _count == other._count;
        }

        public bool Equals(ArraySegment<T> other)
        {
            return _array == other._array &&
                   _offset == other._offset &&
                   _count == other._count;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(_array, _offset, _count);
        }

        public static bool operator ==(ArraySegment<T> a, ArraySegment<T> b) => a.Equals(b);
        public static bool operator !=(ArraySegment<T> a, ArraySegment<T> b) => !a.Equals(b);

        // IList<T> implementation
        bool ICollection<T>.IsReadOnly => false;

        int IList<T>.IndexOf(T item)
        {
            if (_array == null) return -1;
            for (int i = 0; i < _count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_array[_offset + i], item))
                    return i;
            }
            return -1;
        }

        void IList<T>.Insert(int index, T item) => throw new NotSupportedException();
        void IList<T>.RemoveAt(int index) => throw new NotSupportedException();
        void ICollection<T>.Add(T item) => throw new NotSupportedException();
        void ICollection<T>.Clear() => throw new NotSupportedException();

        bool ICollection<T>.Contains(T item)
        {
            if (_array == null) return false;
            for (int i = 0; i < _count; i++)
            {
                if (EqualityComparer<T>.Default.Equals(_array[_offset + i], item))
                    return true;
            }
            return false;
        }

        bool ICollection<T>.Remove(T item) => throw new NotSupportedException();

        // IEnumerable<T> implementation
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Provides an enumerator for the elements of an ArraySegment.
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly T[]? _array;
            private readonly int _start;
            private readonly int _end;
            private int _current;

            internal Enumerator(ArraySegment<T> segment)
            {
                _array = segment._array;
                _start = segment._offset;
                _end = segment._offset + segment._count;
                _current = _start - 1;
            }

            public T Current => _array![_current];
            object? IEnumerator.Current => Current;

            public bool MoveNext()
            {
                if (_current < _end)
                {
                    _current++;
                    return _current < _end;
                }
                return false;
            }

            public void Reset() => _current = _start - 1;
            public void Dispose() { }
        }
    }
}
