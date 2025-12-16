// ProtonOS korlib - DefaultInterpolatedStringHandler
// Minimal handler for string interpolation without StringBuilder.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides a handler used by the language compiler to process interpolated strings into String instances.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct DefaultInterpolatedStringHandler
    {
        private char[] _chars;
        private int _pos;

        /// <summary>
        /// Creates a handler used to translate an interpolated string into a String.
        /// </summary>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            // Estimate capacity: literal chars + some space per formatted value
            int estimatedCapacity = literalLength + (formattedCount * 16);
            _chars = new char[estimatedCapacity];
            _pos = 0;
        }

        /// <summary>
        /// Creates a handler with format provider (ignored in minimal impl).
        /// </summary>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider)
            : this(literalLength, formattedCount)
        {
        }

        private void EnsureCapacity(int additionalChars)
        {
            if (_pos + additionalChars > _chars.Length)
            {
                int newCapacity = _chars.Length * 2;
                if (newCapacity < _pos + additionalChars)
                    newCapacity = _pos + additionalChars + 16;

                char[] newChars = new char[newCapacity];
                for (int i = 0; i < _pos; i++)
                    newChars[i] = _chars[i];
                _chars = newChars;
            }
        }

        /// <summary>
        /// Writes the specified string to the handler.
        /// </summary>
        public void AppendLiteral(string value)
        {
            if (value == null) return;
            EnsureCapacity(value.Length);
            for (int i = 0; i < value.Length; i++)
                _chars[_pos++] = value[i];
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        public void AppendFormatted<T>(T value)
        {
            if (value is not null)
            {
                string? str = value.ToString();
                if (str != null)
                    AppendLiteral(str);
            }
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        public void AppendFormatted<T>(T value, string? format)
        {
            AppendFormatted(value);
        }

        /// <summary>
        /// Writes the specified value to the handler with alignment.
        /// </summary>
        public void AppendFormatted<T>(T value, int alignment)
        {
            string? str = value?.ToString();
            if (str is not null)
            {
                if (alignment > 0 && str.Length < alignment)
                {
                    // Right-align: pad with spaces on the left
                    int padding = alignment - str.Length;
                    EnsureCapacity(alignment);
                    for (int i = 0; i < padding; i++)
                        _chars[_pos++] = ' ';
                    for (int i = 0; i < str.Length; i++)
                        _chars[_pos++] = str[i];
                }
                else if (alignment < 0 && str.Length < -alignment)
                {
                    // Left-align: pad with spaces on the right
                    int padding = -alignment - str.Length;
                    EnsureCapacity(-alignment);
                    for (int i = 0; i < str.Length; i++)
                        _chars[_pos++] = str[i];
                    for (int i = 0; i < padding; i++)
                        _chars[_pos++] = ' ';
                }
                else
                {
                    AppendLiteral(str);
                }
            }
        }

        /// <summary>
        /// Writes the specified value to the handler with alignment and format.
        /// </summary>
        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            AppendFormatted(value, alignment);
        }

        /// <summary>
        /// Writes the specified string to the handler.
        /// </summary>
        public void AppendFormatted(string? value)
        {
            if (value != null)
                AppendLiteral(value);
        }

        /// <summary>
        /// Writes the specified string to the handler with alignment.
        /// </summary>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        {
            if (value is not null)
            {
                if (alignment > 0 && value.Length < alignment)
                {
                    int padding = alignment - value.Length;
                    EnsureCapacity(alignment);
                    for (int i = 0; i < padding; i++)
                        _chars[_pos++] = ' ';
                    for (int i = 0; i < value.Length; i++)
                        _chars[_pos++] = value[i];
                }
                else if (alignment < 0 && value.Length < -alignment)
                {
                    int padding = -alignment - value.Length;
                    EnsureCapacity(-alignment);
                    for (int i = 0; i < value.Length; i++)
                        _chars[_pos++] = value[i];
                    for (int i = 0; i < padding; i++)
                        _chars[_pos++] = ' ';
                }
                else
                {
                    AppendLiteral(value);
                }
            }
        }

        /// <summary>
        /// Writes the specified object to the handler.
        /// </summary>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null)
        {
            AppendFormatted(value?.ToString(), alignment, format);
        }

        /// <summary>
        /// Gets the built String and clears the handler.
        /// </summary>
        public string ToStringAndClear()
        {
            string result = new string(_chars, 0, _pos);
            _pos = 0;
            return result;
        }

        /// <summary>
        /// Gets the built String.
        /// </summary>
        public override string ToString()
        {
            return new string(_chars, 0, _pos);
        }
    }
}
