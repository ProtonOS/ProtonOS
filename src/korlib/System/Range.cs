// ProtonOS korlib - Range type for C# 8.0+ index/range syntax

namespace System;

/// <summary>
/// Represents a range that has start and end indexes.
/// </summary>
/// <remarks>
/// Range is used by the C# compiler to support the .. operator.
/// For example: array[1..3] gets elements at index 1 and 2.
/// </remarks>
public readonly struct Range : IEquatable<Range>
{
    /// <summary>
    /// Gets the inclusive start index of the Range.
    /// </summary>
    public Index Start { get; }

    /// <summary>
    /// Gets the exclusive end index of the Range.
    /// </summary>
    public Index End { get; }

    /// <summary>
    /// Constructs a Range object using the start and end indexes.
    /// </summary>
    /// <param name="start">The inclusive start index of the range.</param>
    /// <param name="end">The exclusive end index of the range.</param>
    public Range(Index start, Index end)
    {
        Start = start;
        End = end;
    }

    /// <summary>
    /// Indicates whether the current Range object is equal to another object.
    /// </summary>
    public override bool Equals(object? value) =>
        value is Range r &&
        r.Start.Equals(Start) &&
        r.End.Equals(End);

    /// <summary>
    /// Indicates whether the current Range object is equal to another Range.
    /// </summary>
    public bool Equals(Range other) => other.Start.Equals(Start) && other.End.Equals(End);

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    public override int GetHashCode()
    {
        return Start.GetHashCode() * 31 + End.GetHashCode();
    }

    /// <summary>
    /// Returns the string representation of this Range.
    /// </summary>
    public override string ToString()
    {
        // Simple implementation until primitives have ToString
        return "Range";
    }

    /// <summary>
    /// Creates a Range object starting from start index to the end of the collection.
    /// </summary>
    public static Range StartAt(Index start) => new Range(start, Index.End);

    /// <summary>
    /// Creates a Range object starting from first element in the collection to the end Index.
    /// </summary>
    public static Range EndAt(Index end) => new Range(Index.Start, end);

    /// <summary>
    /// Creates a Range object starting from first element to the end.
    /// </summary>
    public static Range All => new Range(Index.Start, Index.End);

    /// <summary>
    /// Calculates the start offset and length of range object using a collection length.
    /// </summary>
    /// <param name="length">The length of the collection.</param>
    /// <returns>The start offset and length of the range.</returns>
    public (int Offset, int Length) GetOffsetAndLength(int length)
    {
        int start = Start.GetOffset(length);
        int end = End.GetOffset(length);

        if ((uint)end > (uint)length || (uint)start > (uint)end)
        {
            Environment.FailFast("Range out of bounds");
        }

        return (start, end - start);
    }
}
