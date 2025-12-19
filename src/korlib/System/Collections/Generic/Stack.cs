// ProtonOS korlib - Stack<T>
// Represents a variable size last-in-first-out (LIFO) collection of instances of the same specified type.

namespace System.Collections.Generic;

/// <summary>
/// Represents a variable size last-in-first-out (LIFO) collection of instances of the same specified type.
/// </summary>
public class Stack<T> : IEnumerable<T>, ICollection, IReadOnlyCollection<T>
{
    private T[] _array;
    private int _size;
    private int _version;

    private const int DefaultCapacity = 4;

    /// <summary>Initializes a new instance of the Stack class that is empty and has the default initial capacity.</summary>
    public Stack()
    {
        _array = new T[0];
    }

    /// <summary>Initializes a new instance of the Stack class that is empty and has the specified initial capacity.</summary>
    public Stack(int capacity)
    {
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _array = new T[capacity];
    }

    /// <summary>Initializes a new instance of the Stack class that contains elements copied from the specified collection.</summary>
    public Stack(IEnumerable<T> collection)
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
            _array = new T[0];
            foreach (T item in collection)
            {
                Push(item);
            }
        }
    }

    /// <summary>Gets the number of elements contained in the Stack.</summary>
    public int Count => _size;

    bool ICollection.IsSynchronized => false;
    object ICollection.SyncRoot => this;

    /// <summary>Removes all objects from the Stack.</summary>
    public void Clear()
    {
        Array.Clear(_array, 0, _size);
        _size = 0;
        _version++;
    }

    /// <summary>Determines whether an element is in the Stack.</summary>
    public bool Contains(T item)
    {
        if (_size == 0) return false;
        var comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < _size; i++)
        {
            if (comparer.Equals(_array[i], item))
                return true;
        }
        return false;
    }

    /// <summary>Copies the Stack to an existing one-dimensional Array, starting at the specified array index.</summary>
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (array == null) throw new ArgumentNullException(nameof(array));
        if (arrayIndex < 0 || arrayIndex > array.Length) throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < _size) throw new ArgumentException("Array too small");

        if (_size == 0) return;

        // Copy in reverse order (LIFO order)
        for (int i = 0; i < _size; i++)
        {
            array[arrayIndex + i] = _array[_size - 1 - i];
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

    /// <summary>Returns an enumerator that iterates through the Stack.</summary>
    public Enumerator GetEnumerator() => new Enumerator(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    /// <summary>Returns the object at the top of the Stack without removing it.</summary>
    public T Peek()
    {
        if (_size == 0) throw new InvalidOperationException("Stack is empty");
        return _array[_size - 1];
    }

    /// <summary>Removes and returns the object at the top of the Stack.</summary>
    public T Pop()
    {
        if (_size == 0) throw new InvalidOperationException("Stack is empty");

        _version++;
        T item = _array[--_size];
        _array[_size] = default!;
        return item;
    }

    /// <summary>Inserts an object at the top of the Stack.</summary>
    public void Push(T item)
    {
        if (_size == _array.Length)
        {
            Grow(_size + 1);
        }
        _array[_size++] = item;
        _version++;
    }

    /// <summary>Copies the Stack to a new array.</summary>
    public T[] ToArray()
    {
        if (_size == 0) return new T[0];

        T[] arr = new T[_size];
        // Copy in reverse order (LIFO order)
        for (int i = 0; i < _size; i++)
        {
            arr[i] = _array[_size - 1 - i];
        }
        return arr;
    }

    /// <summary>Sets the capacity to the actual number of elements in the Stack.</summary>
    public void TrimExcess()
    {
        int threshold = (int)(_array.Length * 0.9);
        if (_size < threshold)
        {
            T[] newArray = new T[_size];
            Array.Copy(_array, newArray, _size);
            _array = newArray;
            _version++;
        }
    }

    /// <summary>Attempts to return the object at the top of the Stack without removing it.</summary>
    public bool TryPeek(out T result)
    {
        if (_size == 0)
        {
            result = default!;
            return false;
        }
        result = _array[_size - 1];
        return true;
    }

    /// <summary>Attempts to remove and return the object at the top of the Stack.</summary>
    public bool TryPop(out T result)
    {
        if (_size == 0)
        {
            result = default!;
            return false;
        }
        result = Pop();
        return true;
    }

    private void Grow(int capacity)
    {
        int newCapacity = _array.Length == 0 ? DefaultCapacity : 2 * _array.Length;
        if (newCapacity < capacity) newCapacity = capacity;

        T[] newArray = new T[newCapacity];
        Array.Copy(_array, newArray, _size);
        _array = newArray;
    }

    /// <summary>Enumerates the elements of a Stack.</summary>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly Stack<T> _stack;
        private readonly int _version;
        private int _index;
        private T _current;

        internal Enumerator(Stack<T> stack)
        {
            _stack = stack;
            _version = stack._version;
            _index = -2;
            _current = default!;
        }

        public T Current => _current;
        object? IEnumerator.Current => _current;

        public bool MoveNext()
        {
            if (_version != _stack._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }

            bool retval;
            if (_index == -2)
            {
                _index = _stack._size - 1;
                retval = _index >= 0;
                if (retval)
                    _current = _stack._array[_index];
                return retval;
            }

            if (_index == -1)
            {
                return false;
            }

            retval = --_index >= 0;
            _current = retval ? _stack._array[_index] : default!;
            return retval;
        }

        public void Reset()
        {
            if (_version != _stack._version)
            {
                throw new InvalidOperationException("Collection was modified");
            }
            _index = -2;
            _current = default!;
        }

        public void Dispose() { }
    }
}
