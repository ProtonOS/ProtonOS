// ProtonOS korlib - Predicate and related delegates

namespace System
{
    /// <summary>
    /// Represents the method that defines a set of criteria and determines
    /// whether the specified object meets those criteria.
    /// </summary>
    /// <typeparam name="T">The type of the object to compare.</typeparam>
    /// <param name="obj">The object to compare against the criteria.</param>
    /// <returns>true if obj meets the criteria; otherwise, false.</returns>
    public delegate bool Predicate<in T>(T obj);

    /// <summary>
    /// Represents the method that compares two objects of the same type.
    /// </summary>
    /// <typeparam name="T">The type of the objects to compare.</typeparam>
    /// <param name="x">The first object to compare.</param>
    /// <param name="y">The second object to compare.</param>
    /// <returns>
    /// A signed integer that indicates the relative values of x and y:
    /// less than 0 if x is less than y, 0 if x equals y, greater than 0 if x is greater than y.
    /// </returns>
    public delegate int Comparison<in T>(T x, T y);

    /// <summary>
    /// Represents a method that converts an object from one type to another.
    /// </summary>
    /// <typeparam name="TInput">The type of object that is to be converted.</typeparam>
    /// <typeparam name="TOutput">The type the input object is to be converted to.</typeparam>
    /// <param name="input">The object to convert.</param>
    /// <returns>The TOutput that represents the converted TInput.</returns>
    public delegate TOutput Converter<in TInput, out TOutput>(TInput input);
}
