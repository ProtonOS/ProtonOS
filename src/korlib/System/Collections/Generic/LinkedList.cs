// ProtonOS korlib - LinkedList<T>
// Represents a doubly linked list.

namespace System.Collections.Generic
{
    /// <summary>
    /// Represents a doubly linked list.
    /// </summary>
    /// <typeparam name="T">Specifies the element type of the linked list.</typeparam>
    public class LinkedList<T> : ICollection<T>, IReadOnlyCollection<T>, ICollection
    {
        internal LinkedListNode<T>? _head;
        internal int _count;
        internal int _version;

        /// <summary>
        /// Initializes a new instance of the LinkedList class that is empty.
        /// </summary>
        public LinkedList()
        {
        }

        /// <summary>
        /// Initializes a new instance of the LinkedList class that contains elements copied from the specified IEnumerable.
        /// </summary>
        public LinkedList(IEnumerable<T> collection)
        {
            if (collection == null) throw new ArgumentNullException(nameof(collection));

            foreach (T item in collection)
            {
                AddLast(item);
            }
        }

        /// <summary>Gets the number of nodes actually contained in the LinkedList.</summary>
        public int Count => _count;

        /// <summary>Gets the first node of the LinkedList.</summary>
        public LinkedListNode<T>? First => _head;

        /// <summary>Gets the last node of the LinkedList.</summary>
        public LinkedListNode<T>? Last => _head?._previous;

        bool ICollection<T>.IsReadOnly => false;
        bool ICollection.IsSynchronized => false;
        object ICollection.SyncRoot => this;

        /// <summary>
        /// Adds a new node containing the specified value at the end of the LinkedList.
        /// </summary>
        public LinkedListNode<T> AddLast(T value)
        {
            LinkedListNode<T> newNode = new LinkedListNode<T>(this, value);
            if (_head == null)
            {
                InsertNodeToEmptyList(newNode);
            }
            else
            {
                InsertNodeBefore(_head, newNode);
            }
            return newNode;
        }

        /// <summary>
        /// Adds the specified new node at the end of the LinkedList.
        /// </summary>
        public void AddLast(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (_head == null)
            {
                InsertNodeToEmptyList(node);
            }
            else
            {
                InsertNodeBefore(_head, node);
            }
            node._list = this;
        }

        /// <summary>
        /// Adds a new node containing the specified value at the start of the LinkedList.
        /// </summary>
        public LinkedListNode<T> AddFirst(T value)
        {
            LinkedListNode<T> newNode = new LinkedListNode<T>(this, value);
            if (_head == null)
            {
                InsertNodeToEmptyList(newNode);
            }
            else
            {
                InsertNodeBefore(_head, newNode);
                _head = newNode;
            }
            return newNode;
        }

        /// <summary>
        /// Adds the specified new node at the start of the LinkedList.
        /// </summary>
        public void AddFirst(LinkedListNode<T> node)
        {
            ValidateNewNode(node);
            if (_head == null)
            {
                InsertNodeToEmptyList(node);
            }
            else
            {
                InsertNodeBefore(_head, node);
                _head = node;
            }
            node._list = this;
        }

        /// <summary>
        /// Adds a new node containing the specified value after the specified existing node.
        /// </summary>
        public LinkedListNode<T> AddAfter(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            LinkedListNode<T> newNode = new LinkedListNode<T>(node._list!, value);
            InsertNodeBefore(node._next!, newNode);
            return newNode;
        }

        /// <summary>
        /// Adds the specified new node after the specified existing node.
        /// </summary>
        public void AddAfter(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            ValidateNode(node);
            ValidateNewNode(newNode);
            InsertNodeBefore(node._next!, newNode);
            newNode._list = this;
        }

        /// <summary>
        /// Adds a new node containing the specified value before the specified existing node.
        /// </summary>
        public LinkedListNode<T> AddBefore(LinkedListNode<T> node, T value)
        {
            ValidateNode(node);
            LinkedListNode<T> newNode = new LinkedListNode<T>(node._list!, value);
            InsertNodeBefore(node, newNode);
            if (node == _head)
            {
                _head = newNode;
            }
            return newNode;
        }

        /// <summary>
        /// Adds the specified new node before the specified existing node.
        /// </summary>
        public void AddBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            ValidateNode(node);
            ValidateNewNode(newNode);
            InsertNodeBefore(node, newNode);
            newNode._list = this;
            if (node == _head)
            {
                _head = newNode;
            }
        }

        /// <summary>Removes all nodes from the LinkedList.</summary>
        public void Clear()
        {
            LinkedListNode<T>? current = _head;
            while (current != null)
            {
                LinkedListNode<T> temp = current;
                current = current._next;
                temp.Invalidate();
                if (current == _head)
                {
                    break;
                }
            }
            _head = null;
            _count = 0;
            _version++;
        }

        /// <summary>
        /// Determines whether a value is in the LinkedList.
        /// </summary>
        public bool Contains(T value)
        {
            return Find(value) != null;
        }

        /// <summary>
        /// Copies the entire LinkedList to a compatible one-dimensional Array.
        /// </summary>
        public void CopyTo(T[] array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0 || index > array.Length) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < _count) throw new ArgumentException("Insufficient space in destination array");

            LinkedListNode<T>? node = _head;
            if (node != null)
            {
                do
                {
                    array[index++] = node._value;
                    node = node._next;
                } while (node != _head);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            if (array == null) throw new ArgumentNullException(nameof(array));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            if (array.Length - index < _count) throw new ArgumentException("Insufficient space in destination array");

            if (array is T[] tArray)
            {
                CopyTo(tArray, index);
            }
            else
            {
                throw new ArgumentException("Invalid array type");
            }
        }

        /// <summary>
        /// Finds the first node that contains the specified value.
        /// </summary>
        public LinkedListNode<T>? Find(T value)
        {
            LinkedListNode<T>? node = _head;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (node != null)
            {
                if (value != null)
                {
                    do
                    {
                        if (comparer.Equals(node._value, value))
                        {
                            return node;
                        }
                        node = node._next;
                    } while (node != _head);
                }
                else
                {
                    do
                    {
                        if (node._value == null)
                        {
                            return node;
                        }
                        node = node._next;
                    } while (node != _head);
                }
            }
            return null;
        }

        /// <summary>
        /// Finds the last node that contains the specified value.
        /// </summary>
        public LinkedListNode<T>? FindLast(T value)
        {
            if (_head == null) return null;

            LinkedListNode<T>? last = _head._previous;
            LinkedListNode<T>? node = last;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            if (node != null)
            {
                if (value != null)
                {
                    do
                    {
                        if (comparer.Equals(node._value, value))
                        {
                            return node;
                        }
                        node = node._previous;
                    } while (node != last);
                }
                else
                {
                    do
                    {
                        if (node._value == null)
                        {
                            return node;
                        }
                        node = node._previous;
                    } while (node != last);
                }
            }
            return null;
        }

        /// <summary>Returns an enumerator that iterates through the LinkedList.</summary>
        public Enumerator GetEnumerator() => new Enumerator(this);

        IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Removes the first occurrence of the specified value from the LinkedList.
        /// </summary>
        public bool Remove(T value)
        {
            LinkedListNode<T>? node = Find(value);
            if (node != null)
            {
                InternalRemoveNode(node);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Removes the specified node from the LinkedList.
        /// </summary>
        public void Remove(LinkedListNode<T> node)
        {
            ValidateNode(node);
            InternalRemoveNode(node);
        }

        /// <summary>
        /// Removes the node at the start of the LinkedList.
        /// </summary>
        public void RemoveFirst()
        {
            if (_head == null) throw new InvalidOperationException("The LinkedList is empty");
            InternalRemoveNode(_head);
        }

        /// <summary>
        /// Removes the node at the end of the LinkedList.
        /// </summary>
        public void RemoveLast()
        {
            if (_head == null) throw new InvalidOperationException("The LinkedList is empty");
            InternalRemoveNode(_head._previous!);
        }

        void ICollection<T>.Add(T value)
        {
            AddLast(value);
        }

        private void InsertNodeToEmptyList(LinkedListNode<T> newNode)
        {
            newNode._next = newNode;
            newNode._previous = newNode;
            _head = newNode;
            _version++;
            _count++;
        }

        private void InsertNodeBefore(LinkedListNode<T> node, LinkedListNode<T> newNode)
        {
            newNode._next = node;
            newNode._previous = node._previous;
            node._previous!._next = newNode;
            node._previous = newNode;
            _version++;
            _count++;
        }

        private void InternalRemoveNode(LinkedListNode<T> node)
        {
            if (node._next == node)
            {
                _head = null;
            }
            else
            {
                node._next!._previous = node._previous;
                node._previous!._next = node._next;
                if (_head == node)
                {
                    _head = node._next;
                }
            }
            node.Invalidate();
            _count--;
            _version++;
        }

        private void ValidateNewNode(LinkedListNode<T> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node._list != null) throw new InvalidOperationException("Node already belongs to another list");
        }

        private void ValidateNode(LinkedListNode<T> node)
        {
            if (node == null) throw new ArgumentNullException(nameof(node));
            if (node._list != this) throw new InvalidOperationException("Node does not belong to this list");
        }

        /// <summary>Enumerates the elements of a LinkedList.</summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly LinkedList<T> _list;
            private LinkedListNode<T>? _node;
            private readonly int _version;
            private T? _current;
            private int _index;

            internal Enumerator(LinkedList<T> list)
            {
                _list = list;
                _version = list._version;
                _node = list._head;
                _current = default;
                _index = 0;
            }

            public T Current => _current!;
            object? IEnumerator.Current => _current;

            public bool MoveNext()
            {
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }

                if (_node == null)
                {
                    _index = _list._count + 1;
                    return false;
                }

                _index++;
                _current = _node._value;
                _node = _node._next;
                if (_node == _list._head)
                {
                    _node = null;
                }
                return true;
            }

            public void Reset()
            {
                if (_version != _list._version)
                {
                    throw new InvalidOperationException("Collection was modified");
                }
                _current = default;
                _node = _list._head;
                _index = 0;
            }

            public void Dispose() { }
        }
    }

    /// <summary>
    /// Represents a node in a LinkedList.
    /// </summary>
    /// <typeparam name="T">Specifies the element type of the linked list.</typeparam>
    public sealed class LinkedListNode<T>
    {
        internal LinkedList<T>? _list;
        internal LinkedListNode<T>? _next;
        internal LinkedListNode<T>? _previous;
        internal T _value;

        /// <summary>
        /// Initializes a new instance of the LinkedListNode class, containing the specified value.
        /// </summary>
        public LinkedListNode(T value)
        {
            _value = value;
        }

        internal LinkedListNode(LinkedList<T> list, T value)
        {
            _list = list;
            _value = value;
        }

        /// <summary>Gets the LinkedList that the LinkedListNode belongs to.</summary>
        public LinkedList<T>? List => _list;

        /// <summary>Gets the next node in the LinkedList.</summary>
        public LinkedListNode<T>? Next
        {
            get { return _next == null || _next == _list?._head ? null : _next; }
        }

        /// <summary>Gets the previous node in the LinkedList.</summary>
        public LinkedListNode<T>? Previous
        {
            get { return _previous == null || this == _list?._head ? null : _previous; }
        }

        /// <summary>Gets the value contained in the node.</summary>
        public T Value
        {
            get => _value;
            set => _value = value;
        }

        /// <summary>Gets a reference to the value contained in the node.</summary>
        public ref T ValueRef => ref _value;

        internal void Invalidate()
        {
            _list = null;
            _next = null;
            _previous = null;
        }
    }
}
