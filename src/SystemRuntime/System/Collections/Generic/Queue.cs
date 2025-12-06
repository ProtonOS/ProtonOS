// ProtonOS System.Runtime - Queue<T>
// Represents a first-in, first-out collection of objects.

namespace System.Collections.Generic
{
    /// <summary>
    /// Represents a first-in, first-out collection of objects.
    /// </summary>
    public class Queue<T> : IEnumerable<T>, ICollection, IReadOnlyCollection<T>
    {
        private T[] _array;
        private int _head;
        private int _tail;
        private int _size;
        private int _version;

        private const int DefaultCapacity = 4;

        /// <summary>Initializes a new instance of the Queue class that is empty and has the default initial capacity.</summary>
        public Queue()
        {
            _array = Array.Empty<T>();
        }

        /// <summary>Initializes a new instance of the Queue class that is empty and has the specified initial capacity.</summary>
        public Queue(int capacity)
        {
            if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _array = new T[capacity];
        }

        /// <summary>Initializes a new instance of the Queue class that contains elements copied from the specified collection.</summary>
        public Queue(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            if (collection is ICollection<T> c)
            {
                _array = new T[c.Count];
                c.CopyTo(_array, 0);
                _size = _array.Length;
            }
            else
            {
                _array = Array.Empty<T>();
                foreach (T item in collection)
                {
                    Enqueue(item);
                }
            }
        }

        /// <summary>Gets the number of elements contained in the Queue.</summary>
        public int Count => _size;

        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        /// <summary>Removes all objects from the Queue.</summary>
        public void Clear()
        {
            if (_size != 0)
            {
                if (_head < _tail)
                {
                    Array.Clear(_array, _head, _size);
                }
                else
                {
                    Array.Clear(_array, _head, _array.Length - _head);
                    Array.Clear(_array, 0, _tail);
                }
                _size = 0;
            }
            _head = 0;
            _tail = 0;
            _version++;
        }

        /// <summary>Determines whether an element is in the Queue.</summary>
        public bool Contains(T item)
        {
            if (_size == 0) return false;

            if (_head < _tail)
            {
                return Array.IndexOf(_array, item, _head, _size) >= 0;
            }

            return Array.IndexOf(_array, item, _head, _array.Length - _head) >= 0 ||
                   Array.IndexOf(_array, item, 0, _tail) >= 0;
        }

        /// <summary>Copies the Queue elements to an existing one-dimensional Array, starting at the specified array index.</summary>
        public void CopyTo(T[] array, int arrayIndex)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
            if (array.Length - arrayIndex < _size) throw new ArgumentException("Array too small");

            if (_size == 0) return;

            if (_head < _tail)
            {
                Array.Copy(_array, _head, array, arrayIndex, _size);
            }
            else
            {
                int firstPart = _array.Length - _head;
                Array.Copy(_array, _head, array, arrayIndex, firstPart);
                Array.Copy(_array, 0, array, arrayIndex + firstPart, _tail);
            }
        }

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

        /// <summary>Removes and returns the object at the beginning of the Queue.</summary>
        public T Dequeue()
        {
            if (_size == 0) throw new InvalidOperationException("Queue is empty");

            T removed = _array[_head];
            _array[_head] = default!;
            _head = (_head + 1) % _array.Length;
            _size--;
            _version++;
            return removed;
        }

        /// <summary>Adds an object to the end of the Queue.</summary>
        public void Enqueue(T item)
        {
            if (_size == _array.Length)
            {
                Grow(_size + 1);
            }

            _array[_tail] = item;
            _tail = (_tail + 1) % _array.Length;
            _size++;
            _version++;
        }

        /// <summary>Returns an enumerator that iterates through the Queue.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
        IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

        /// <summary>Returns the object at the beginning of the Queue without removing it.</summary>
        public T Peek()
        {
            if (_size == 0) throw new InvalidOperationException("Queue is empty");
            return _array[_head];
        }

        /// <summary>Copies the Queue elements to a new array.</summary>
        public T[] ToArray()
        {
            if (_size == 0) return Array.Empty<T>();

            T[] arr = new T[_size];
            CopyTo(arr, 0);
            return arr;
        }

        /// <summary>Sets the capacity to the actual number of elements in the Queue.</summary>
        public void TrimExcess()
        {
            int threshold = (int)(_array.Length * 0.9);
            if (_size < threshold)
            {
                T[] newArray = ToArray();
                _array = newArray;
                _head = 0;
                _tail = _size;
                _version++;
            }
        }

        /// <summary>Attempts to remove and return the object at the beginning of the Queue.</summary>
        public bool TryDequeue(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = Dequeue();
            return true;
        }

        /// <summary>Attempts to return the object at the beginning of the Queue without removing it.</summary>
        public bool TryPeek(out T result)
        {
            if (_size == 0)
            {
                result = default!;
                return false;
            }
            result = _array[_head];
            return true;
        }

        private void Grow(int capacity)
        {
            int newCapacity = _array.Length == 0 ? DefaultCapacity : 2 * _array.Length;
            if (newCapacity < capacity) newCapacity = capacity;

            T[] newArray = new T[newCapacity];
            if (_size > 0)
            {
                if (_head < _tail)
                {
                    Array.Copy(_array, _head, newArray, 0, _size);
                }
                else
                {
                    Array.Copy(_array, _head, newArray, 0, _array.Length - _head);
                    Array.Copy(_array, 0, newArray, _array.Length - _head, _tail);
                }
            }
            _array = newArray;
            _head = 0;
            _tail = _size;
        }

        /// <summary>Enumerates the elements of a Queue.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly Queue<T> _queue;
            private readonly int _version;
            private int _index;
            private T _current;

            internal Enumerator(Queue<T> queue)
            {
                _queue = queue;
                _version = queue._version;
                _index = -1;
                _current = default!;
            }

            public T Current => _current;
            object? IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _queue._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                if (_index < _queue._size - 1)
                {
                    _index++;
                    _current = _queue._array[(_queue._head + _index) % _queue._array.Length];
                    return true;
                }
                return false;
            }

            public void Reset()
            {
                if (_version != _queue._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _index = -1;
                _current = default!;
            }

            public void Dispose() { }
        }
    }
}
