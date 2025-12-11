// ProtonOS System.Runtime - StringBuilder
// Represents a mutable string of characters.
// This implementation avoids Array.Copy and exceptions for JIT compatibility.

namespace System.Text
{
    /// <summary>
    /// Represents a mutable string of characters.
    /// </summary>
    public sealed class StringBuilder
    {
        private const int DefaultCapacity = 16;
        private char[] _buffer;
        private int _length;

        /// <summary>Initializes a new instance of the StringBuilder class.</summary>
        public StringBuilder() : this(DefaultCapacity)
        {
        }

        /// <summary>Initializes a new instance with the specified capacity.</summary>
        public StringBuilder(int capacity)
        {
            if (capacity < 0) capacity = DefaultCapacity;
            _buffer = new char[capacity > 0 ? capacity : DefaultCapacity];
            _length = 0;
        }

        /// <summary>Initializes a new instance with the specified string.</summary>
        public StringBuilder(string? value)
        {
            if (value == null)
            {
                _buffer = new char[DefaultCapacity];
                _length = 0;
            }
            else
            {
                _buffer = new char[value.Length > DefaultCapacity ? value.Length : DefaultCapacity];
                value.CopyTo(0, _buffer, 0, value.Length);
                _length = value.Length;
            }
        }

        /// <summary>Initializes a new instance with the specified string and capacity.</summary>
        public StringBuilder(string? value, int capacity)
        {
            if (capacity < 0) capacity = DefaultCapacity;

            int valueLen = value?.Length ?? 0;
            int actualCapacity = capacity > valueLen ? capacity : valueLen;
            actualCapacity = actualCapacity > 0 ? actualCapacity : DefaultCapacity;

            _buffer = new char[actualCapacity];
            if (value != null)
            {
                value.CopyTo(0, _buffer, 0, value.Length);
                _length = value.Length;
            }
            else
            {
                _length = 0;
            }
        }

        /// <summary>Gets or sets the length of the current StringBuilder object.</summary>
        public int Length
        {
            get => _length;
            set
            {
                if (value < 0) value = 0;
                if (value > _buffer.Length) EnsureCapacity(value);
                if (value > _length)
                {
                    // Fill with null characters
                    for (int i = _length; i < value; i++)
                        _buffer[i] = '\0';
                }
                _length = value;
            }
        }

        /// <summary>Gets or sets the maximum number of characters that can be contained in memory allocated by this instance.</summary>
        public int Capacity
        {
            get => _buffer.Length;
            set
            {
                if (value < _length) value = _length;
                if (value != _buffer.Length)
                {
                    var newBuffer = new char[value];
                    CopyChars(_buffer, 0, newBuffer, 0, _length);
                    _buffer = newBuffer;
                }
            }
        }

        /// <summary>Gets or sets the character at the specified character position in this instance.</summary>
        public char this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_length) return '\0';
                return _buffer[index];
            }
            set
            {
                if ((uint)index < (uint)_length)
                    _buffer[index] = value;
            }
        }

        /// <summary>Ensures that the capacity is at least the specified value.</summary>
        public int EnsureCapacity(int capacity)
        {
            if (capacity < 0) capacity = 0;
            if (_buffer.Length < capacity)
            {
                int newCapacity = _buffer.Length * 2;
                if (newCapacity < capacity) newCapacity = capacity;
                Capacity = newCapacity;
            }
            return _buffer.Length;
        }

        /// <summary>Appends the string representation of a specified char to this instance.</summary>
        public StringBuilder Append(char value)
        {
            if (_length >= _buffer.Length) EnsureCapacity(_length + 1);
            _buffer[_length++] = value;
            return this;
        }

        /// <summary>Appends a specified number of copies of a character to this instance.</summary>
        public StringBuilder Append(char value, int repeatCount)
        {
            if (repeatCount <= 0) return this;

            EnsureCapacity(_length + repeatCount);
            for (int i = 0; i < repeatCount; i++)
                _buffer[_length++] = value;
            return this;
        }

        /// <summary>Appends a copy of the specified string to this instance.</summary>
        public StringBuilder Append(string? value)
        {
            if (string.IsNullOrEmpty(value)) return this;

            EnsureCapacity(_length + value.Length);
            value.CopyTo(0, _buffer, _length, value.Length);
            _length += value.Length;
            return this;
        }

        /// <summary>Appends a copy of a substring to this instance.</summary>
        public StringBuilder Append(string? value, int startIndex, int count)
        {
            if (value == null || count <= 0) return this;
            if (startIndex < 0) startIndex = 0;
            if (startIndex + count > value.Length) count = value.Length - startIndex;
            if (count <= 0) return this;

            EnsureCapacity(_length + count);
            value.CopyTo(startIndex, _buffer, _length, count);
            _length += count;
            return this;
        }

        /// <summary>Appends the string representation of a specified object to this instance.</summary>
        public StringBuilder Append(object? value)
        {
            return value == null ? this : Append(value.ToString());
        }

        /// <summary>Appends the string representation of a specified Boolean value to this instance.</summary>
        public StringBuilder Append(bool value)
        {
            return Append(value ? "True" : "False");
        }

        /// <summary>Appends the string representation of a specified Int32 value to this instance.</summary>
        public StringBuilder Append(int value)
        {
            return Append(value.ToString());
        }

        /// <summary>Appends the string representation of a specified Int64 value to this instance.</summary>
        public StringBuilder Append(long value)
        {
            return Append(value.ToString());
        }

        /// <summary>Appends the string representation of a specified UInt32 value to this instance.</summary>
        public StringBuilder Append(uint value)
        {
            return Append(value.ToString());
        }

        /// <summary>Appends the string representation of a specified UInt64 value to this instance.</summary>
        public StringBuilder Append(ulong value)
        {
            return Append(value.ToString());
        }

        /// <summary>Appends the specified character array to this instance.</summary>
        public StringBuilder Append(char[]? value)
        {
            if (value == null || value.Length == 0) return this;

            EnsureCapacity(_length + value.Length);
            CopyChars(value, 0, _buffer, _length, value.Length);
            _length += value.Length;
            return this;
        }

        /// <summary>Appends a subarray of characters to this instance.</summary>
        public StringBuilder Append(char[]? value, int startIndex, int charCount)
        {
            if (value == null || charCount <= 0) return this;
            if (startIndex < 0) startIndex = 0;
            if (startIndex + charCount > value.Length) charCount = value.Length - startIndex;
            if (charCount <= 0) return this;

            EnsureCapacity(_length + charCount);
            CopyChars(value, startIndex, _buffer, _length, charCount);
            _length += charCount;
            return this;
        }

        /// <summary>Appends a copy of the specified ReadOnlySpan to this instance.</summary>
        public StringBuilder Append(ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return this;

            EnsureCapacity(_length + value.Length);
            for (int i = 0; i < value.Length; i++)
                _buffer[_length + i] = value[i];
            _length += value.Length;
            return this;
        }

        /// <summary>Appends the default line terminator to the end of this instance.</summary>
        public StringBuilder AppendLine()
        {
            return Append(Environment.NewLine);
        }

        /// <summary>Appends a copy of the specified string followed by the default line terminator to the end of this instance.</summary>
        public StringBuilder AppendLine(string? value)
        {
            Append(value);
            return AppendLine();
        }

        /// <summary>Inserts a string into this instance at the specified position.</summary>
        public StringBuilder Insert(int index, string? value)
        {
            if ((uint)index > (uint)_length) return this;
            if (string.IsNullOrEmpty(value)) return this;

            EnsureCapacity(_length + value.Length);

            // Shift existing characters to the right
            CopyCharsBackward(_buffer, index, _buffer, index + value.Length, _length - index);

            // Insert new characters
            value.CopyTo(0, _buffer, index, value.Length);
            _length += value.Length;
            return this;
        }

        /// <summary>Inserts a character into this instance at the specified position.</summary>
        public StringBuilder Insert(int index, char value)
        {
            if ((uint)index > (uint)_length) return this;

            EnsureCapacity(_length + 1);
            CopyCharsBackward(_buffer, index, _buffer, index + 1, _length - index);
            _buffer[index] = value;
            _length++;
            return this;
        }

        /// <summary>Inserts an object into this instance at the specified position.</summary>
        public StringBuilder Insert(int index, object? value)
        {
            return value == null ? this : Insert(index, value.ToString());
        }

        /// <summary>Inserts an Int32 into this instance at the specified position.</summary>
        public StringBuilder Insert(int index, int value)
        {
            return Insert(index, value.ToString());
        }

        /// <summary>Inserts a Boolean into this instance at the specified position.</summary>
        public StringBuilder Insert(int index, bool value)
        {
            return Insert(index, value ? "True" : "False");
        }

        /// <summary>Removes the specified range of characters from this instance.</summary>
        public StringBuilder Remove(int startIndex, int length)
        {
            if (startIndex < 0 || length <= 0) return this;
            if (startIndex >= _length) return this;
            if (startIndex + length > _length) length = _length - startIndex;

            // Shift remaining characters to the left
            CopyChars(_buffer, startIndex + length, _buffer, startIndex, _length - startIndex - length);
            _length -= length;
            return this;
        }

        /// <summary>Replaces all occurrences of a specified character with another character.</summary>
        public StringBuilder Replace(char oldChar, char newChar)
        {
            for (int i = 0; i < _length; i++)
            {
                if (_buffer[i] == oldChar)
                    _buffer[i] = newChar;
            }
            return this;
        }

        /// <summary>Replaces occurrences of a specified character with another character within a substring.</summary>
        public StringBuilder Replace(char oldChar, char newChar, int startIndex, int count)
        {
            if (startIndex < 0) startIndex = 0;
            if (count <= 0) return this;
            if (startIndex + count > _length) count = _length - startIndex;

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                if (_buffer[i] == oldChar)
                    _buffer[i] = newChar;
            }
            return this;
        }

        /// <summary>Replaces all occurrences of a specified string with another string.</summary>
        public StringBuilder Replace(string oldValue, string? newValue)
        {
            return Replace(oldValue, newValue, 0, _length);
        }

        /// <summary>Replaces occurrences of a specified string with another string within a substring.</summary>
        public StringBuilder Replace(string oldValue, string? newValue, int startIndex, int count)
        {
            if (oldValue == null || oldValue.Length == 0) return this;
            if (startIndex < 0) startIndex = 0;
            if (count <= 0) return this;
            if (startIndex + count > _length) count = _length - startIndex;

            newValue ??= string.Empty;

            int i = startIndex;
            int endIndex = startIndex + count;

            while (i <= endIndex - oldValue.Length)
            {
                bool match = true;
                for (int j = 0; j < oldValue.Length; j++)
                {
                    if (_buffer[i + j] != oldValue[j])
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    // Remove old value
                    Remove(i, oldValue.Length);
                    endIndex -= oldValue.Length;

                    // Insert new value
                    if (newValue.Length > 0)
                    {
                        Insert(i, newValue);
                        endIndex += newValue.Length;
                        i += newValue.Length;
                    }
                }
                else
                {
                    i++;
                }
            }

            return this;
        }

        /// <summary>Removes all characters from this instance.</summary>
        public StringBuilder Clear()
        {
            _length = 0;
            return this;
        }

        /// <summary>Converts the value of this instance to a String.</summary>
        public override string ToString()
        {
            return new string(_buffer, 0, _length);
        }

        /// <summary>Converts a substring of this instance to a String.</summary>
        public string ToString(int startIndex, int length)
        {
            if (startIndex < 0) startIndex = 0;
            if (length <= 0) return string.Empty;
            if (startIndex >= _length) return string.Empty;
            if (startIndex + length > _length) length = _length - startIndex;

            return new string(_buffer, startIndex, length);
        }

        /// <summary>Copies the characters from a specified segment of this instance to a specified segment of a destination Char array.</summary>
        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (destination == null) return;
            if (sourceIndex < 0) sourceIndex = 0;
            if (destinationIndex < 0) destinationIndex = 0;
            if (count <= 0) return;
            if (sourceIndex + count > _length) count = _length - sourceIndex;
            if (destinationIndex + count > destination.Length) count = destination.Length - destinationIndex;
            if (count <= 0) return;

            CopyChars(_buffer, sourceIndex, destination, destinationIndex, count);
        }

        /// <summary>Copies the characters from a specified segment of this instance to a destination Span.</summary>
        public void CopyTo(int sourceIndex, Span<char> destination, int count)
        {
            if (sourceIndex < 0) sourceIndex = 0;
            if (count <= 0) return;
            if (sourceIndex + count > _length) count = _length - sourceIndex;
            if (count > destination.Length) count = destination.Length;
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
                destination[i] = _buffer[sourceIndex + i];
        }

        /// <summary>Returns a value indicating whether this instance is equal to a specified object.</summary>
        public bool Equals(StringBuilder? sb)
        {
            if (sb == null) return false;
            if (ReferenceEquals(this, sb)) return true;
            if (_length != sb._length) return false;

            for (int i = 0; i < _length; i++)
            {
                if (_buffer[i] != sb._buffer[i]) return false;
            }
            return true;
        }

        /// <summary>Returns a ReadOnlySpan representing the characters in this instance.</summary>
        public ReadOnlySpan<char> AsSpan() => new ReadOnlySpan<char>(_buffer, 0, _length);

        /// <summary>Returns a ReadOnlySpan representing a substring of this instance.</summary>
        public ReadOnlySpan<char> AsSpan(int start) => new ReadOnlySpan<char>(_buffer, start, _length - start);

        /// <summary>Returns a ReadOnlySpan representing a substring of this instance.</summary>
        public ReadOnlySpan<char> AsSpan(int start, int length) => new ReadOnlySpan<char>(_buffer, start, length);

        // Helper method to copy chars without using Array.Copy
        private static void CopyChars(char[] source, int sourceIndex, char[] dest, int destIndex, int count)
        {
            for (int i = 0; i < count; i++)
            {
                dest[destIndex + i] = source[sourceIndex + i];
            }
        }

        // Helper method to copy chars backwards (for overlapping regions)
        private static void CopyCharsBackward(char[] source, int sourceIndex, char[] dest, int destIndex, int count)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                dest[destIndex + i] = source[sourceIndex + i];
            }
        }
    }
}
