// ProtonOS System.Runtime - DefaultInterpolatedStringHandler
// Provides a handler for string interpolation expressions.

using System.Text;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides a handler used by the language compiler to process interpolated strings into String instances.
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct DefaultInterpolatedStringHandler
    {
        private readonly StringBuilder _builder;

        /// <summary>
        /// Creates a handler used to translate an interpolated string into a String.
        /// </summary>
        /// <param name="literalLength">The number of constant characters outside of interpolation expressions in the interpolated string.</param>
        /// <param name="formattedCount">The number of interpolation expressions in the interpolated string.</param>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount)
        {
            // Estimate capacity: literal chars + some space per formatted value
            int estimatedCapacity = literalLength + (formattedCount * 11);
            _builder = new StringBuilder(estimatedCapacity);
        }

        /// <summary>
        /// Creates a handler used to translate an interpolated string into a String.
        /// </summary>
        /// <param name="literalLength">The number of constant characters outside of interpolation expressions in the interpolated string.</param>
        /// <param name="formattedCount">The number of interpolation expressions in the interpolated string.</param>
        /// <param name="provider">An object that supplies culture-specific formatting information.</param>
        public DefaultInterpolatedStringHandler(int literalLength, int formattedCount, IFormatProvider? provider)
            : this(literalLength, formattedCount)
        {
            // Provider is ignored in this minimal implementation
        }

        /// <summary>
        /// Writes the specified string to the handler.
        /// </summary>
        /// <param name="value">The string to write.</param>
        public void AppendLiteral(string value)
        {
            _builder.Append(value);
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        public void AppendFormatted<T>(T value)
        {
            if (value is not null)
            {
                _builder.Append(value.ToString());
            }
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        /// <param name="format">The format string.</param>
        public void AppendFormatted<T>(T value, string? format)
        {
            // Format specifier is ignored in this minimal implementation
            AppendFormatted(value);
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        /// <param name="alignment">The minimum number of characters for the formatted value.</param>
        public void AppendFormatted<T>(T value, int alignment)
        {
            string? str = value?.ToString();
            if (str is not null)
            {
                if (alignment > 0 && str.Length < alignment)
                {
                    // Right-align: pad with spaces on the left
                    _builder.Append(' ', alignment - str.Length);
                    _builder.Append(str);
                }
                else if (alignment < 0 && str.Length < -alignment)
                {
                    // Left-align: pad with spaces on the right
                    _builder.Append(str);
                    _builder.Append(' ', -alignment - str.Length);
                }
                else
                {
                    _builder.Append(str);
                }
            }
        }

        /// <summary>
        /// Writes the specified value to the handler.
        /// </summary>
        /// <typeparam name="T">The type of the value to write.</typeparam>
        /// <param name="value">The value to write.</param>
        /// <param name="alignment">The minimum number of characters for the formatted value.</param>
        /// <param name="format">The format string.</param>
        public void AppendFormatted<T>(T value, int alignment, string? format)
        {
            // Format specifier is ignored in this minimal implementation
            AppendFormatted(value, alignment);
        }

        /// <summary>
        /// Writes the specified string to the handler.
        /// </summary>
        /// <param name="value">The string to write.</param>
        public void AppendFormatted(string? value)
        {
            _builder.Append(value);
        }

        /// <summary>
        /// Writes the specified string to the handler with alignment.
        /// </summary>
        /// <param name="value">The string to write.</param>
        /// <param name="alignment">The minimum number of characters for the formatted value.</param>
        /// <param name="format">The format string (ignored).</param>
        public void AppendFormatted(string? value, int alignment = 0, string? format = null)
        {
            if (value is not null)
            {
                if (alignment > 0 && value.Length < alignment)
                {
                    _builder.Append(' ', alignment - value.Length);
                    _builder.Append(value);
                }
                else if (alignment < 0 && value.Length < -alignment)
                {
                    _builder.Append(value);
                    _builder.Append(' ', -alignment - value.Length);
                }
                else
                {
                    _builder.Append(value);
                }
            }
        }

        /// <summary>
        /// Writes the specified object to the handler.
        /// </summary>
        /// <param name="value">The object to write.</param>
        /// <param name="alignment">The minimum number of characters for the formatted value.</param>
        /// <param name="format">The format string (ignored).</param>
        public void AppendFormatted(object? value, int alignment = 0, string? format = null)
        {
            AppendFormatted(value?.ToString(), alignment, format);
        }

        /// <summary>
        /// Gets the built String and clears the handler.
        /// </summary>
        /// <returns>The built string.</returns>
        public string ToStringAndClear()
        {
            string result = _builder.ToString();
            _builder.Clear();
            return result;
        }

        /// <summary>
        /// Gets the built String.
        /// </summary>
        /// <returns>The built string.</returns>
        public override string ToString()
        {
            return _builder.ToString();
        }
    }
}
