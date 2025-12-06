// ProtonOS korlib - TupleElementNamesAttribute for named tuple support

namespace System.Runtime.CompilerServices;

/// <summary>
/// Indicates that the use of a value tuple on a member is meant to be treated as a tuple
/// with element names.
/// </summary>
/// <remarks>
/// This attribute is embedded by the compiler and should not be used directly in source code.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Field |
    AttributeTargets.Parameter |
    AttributeTargets.Property |
    AttributeTargets.ReturnValue |
    AttributeTargets.Class |
    AttributeTargets.Struct |
    AttributeTargets.Event,
    AllowMultiple = false,
    Inherited = false)]
public sealed class TupleElementNamesAttribute : Attribute
{
    /// <summary>
    /// Gets the names of the tuple elements.
    /// </summary>
    public string?[] TransformNames { get; }

    /// <summary>
    /// Initializes a new instance with the specified element names.
    /// </summary>
    /// <param name="transformNames">
    /// The names of the tuple elements. A null value indicates the corresponding
    /// element is unnamed.
    /// </param>
    public TupleElementNamesAttribute(string?[] transformNames)
    {
        TransformNames = transformNames;
    }
}
