// ProtonOS korlib - IComparer interface (non-generic)

namespace System.Collections;

/// <summary>
/// Exposes a method that compares two objects.
/// </summary>
public interface IComparer
{
    /// <summary>
    /// Compares two objects and returns a value indicating whether one is less than,
    /// equal to, or greater than the other.
    /// </summary>
    /// <param name="x">The first object to compare.</param>
    /// <param name="y">The second object to compare.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of x and y.
    /// Less than zero: x is less than y.
    /// Zero: x equals y.
    /// Greater than zero: x is greater than y.
    /// </returns>
    int Compare(object? x, object? y);
}
