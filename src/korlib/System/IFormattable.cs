// ProtonOS korlib - IFormattable interface

namespace System;

/// <summary>
/// Provides functionality to format the value of an object into a string representation.
/// </summary>
public interface IFormattable
{
    /// <summary>
    /// Formats the value of the current instance using the specified format.
    /// </summary>
    /// <param name="format">
    /// The format to use, or null to use the default format defined for the type.
    /// </param>
    /// <param name="formatProvider">
    /// The provider to use to format the value, or null to obtain the numeric format
    /// information from the current locale setting.
    /// </param>
    /// <returns>The value of the current instance in the specified format.</returns>
    string ToString(string? format, IFormatProvider? formatProvider);
}
