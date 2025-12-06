// ProtonOS korlib - Caller information attributes for C# 5.0+

namespace System.Runtime.CompilerServices;

/// <summary>
/// Allows you to obtain the method or property name of the caller to the method.
/// </summary>
/// <remarks>
/// Apply this attribute to optional parameters with default values.
/// The compiler will inject the caller's member name at the call site.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class CallerMemberNameAttribute : Attribute
{
    public CallerMemberNameAttribute() { }
}

/// <summary>
/// Allows you to obtain the full path of the source file that contains the caller.
/// </summary>
/// <remarks>
/// Apply this attribute to optional parameters with default values.
/// The compiler will inject the source file path at the call site.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class CallerFilePathAttribute : Attribute
{
    public CallerFilePathAttribute() { }
}

/// <summary>
/// Allows you to obtain the line number in the source file at which the method is called.
/// </summary>
/// <remarks>
/// Apply this attribute to optional parameters with default values.
/// The compiler will inject the source line number at the call site.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
public sealed class CallerLineNumberAttribute : Attribute
{
    public CallerLineNumberAttribute() { }
}

/// <summary>
/// Allows you to capture the expression passed to a method as a string.
/// </summary>
/// <remarks>
/// Apply this attribute to optional parameters with default values.
/// The compiler will inject the string representation of the argument expression.
/// This is commonly used for ArgumentException helpers and assertion methods.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class CallerArgumentExpressionAttribute : Attribute
{
    /// <summary>
    /// Gets the name of the targeted parameter whose expression should be captured.
    /// </summary>
    public string ParameterName { get; }

    /// <summary>
    /// Initializes a new instance that captures the expression for the specified parameter.
    /// </summary>
    /// <param name="parameterName">The name of the parameter whose expression to capture.</param>
    public CallerArgumentExpressionAttribute(string parameterName)
    {
        ParameterName = parameterName;
    }
}
