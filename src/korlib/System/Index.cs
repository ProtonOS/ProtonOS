// ProtonOS korlib - Index type for C# 8.0+ index/range syntax

namespace System;

/// <summary>
/// Represents a type that can be used to index a collection either from the start or the end.
/// </summary>
/// <remarks>
/// Index is used by the C# compiler to support the ^ operator (index from end).
/// For example: array[^1] gets the last element.
/// </remarks>
public readonly struct Index : IEquatable<Index>
{
    private readonly int _value;

    /// <summary>
    /// Constructs an Index using a value and indicating if the index is from the start or the end.
    /// </summary>
    /// <param name="value">The index value. Must be non-negative.</param>
    /// <param name="fromEnd">Indicates whether the index is from the start (false) or from the end (true).</param>
    public Index(int value, bool fromEnd = false)
    {
        if (value < 0)
        {
            Environment.FailFast("Index value must be non-negative");
        }

        if (fromEnd)
            _value = ~value;
        else
            _value = value;
    }

    // Private constructor for internal use
    private Index(int value)
    {
        _value = value;
    }

    /// <summary>
    /// Creates an Index pointing at first element.
    /// </summary>
    public static Index Start => new Index(0);

    /// <summary>
    /// Creates an Index pointing at beyond last element.
    /// </summary>
    public static Index End => new Index(~0);

    /// <summary>
    /// Creates an Index from the start at the position indicated by the value.
    /// </summary>
    public static Index FromStart(int value)
    {
        if (value < 0)
        {
            Environment.FailFast("Index value must be non-negative");
        }
        return new Index(value);
    }

    /// <summary>
    /// Creates an Index from the end at the position indicated by the value.
    /// </summary>
    public static Index FromEnd(int value)
    {
        if (value < 0)
        {
            Environment.FailFast("Index value must be non-negative");
        }
        return new Index(~value);
    }

    /// <summary>
    /// Returns the index value.
    /// </summary>
    public int Value
    {
        get
        {
            if (_value < 0)
                return ~_value;
            else
                return _value;
        }
    }

    /// <summary>
    /// Indicates whether the index is from the start or the end.
    /// </summary>
    public bool IsFromEnd => _value < 0;

    /// <summary>
    /// Calculates the offset from the start using the given collection length.
    /// </summary>
    /// <param name="length">The length of the collection.</param>
    /// <returns>The offset from the start of the collection.</returns>
    public int GetOffset(int length)
    {
        int offset = _value;
        if (IsFromEnd)
        {
            offset += length + 1;
        }
        return offset;
    }

    /// <summary>
    /// Indicates whether the current Index object is equal to another Index.
    /// </summary>
    public override bool Equals(object? value) => value is Index other && _value == other._value;

    /// <summary>
    /// Indicates whether the current Index object is equal to another Index.
    /// </summary>
    public bool Equals(Index other) => _value == other._value;

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode() => _value;

    /// <summary>
    /// Converts integer number to an Index.
    /// </summary>
    public static implicit operator Index(int value) => FromStart(value);

    /// <summary>
    /// Returns the string representation of this Index.
    /// </summary>
    public override string ToString()
    {
        // Simple implementation until primitives have ToString
        if (IsFromEnd)
            return "^Index";
        return "Index";
    }
}
