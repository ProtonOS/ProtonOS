// ProtonOS korlib - Nullable attributes for C# 8.0+ nullable reference types

namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
/// <remarks>
/// The compiler emits this attribute on types and members to indicate nullability annotations.
/// The byte array encodes nullability for each component of the type.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Event |
    AttributeTargets.Field |
    AttributeTargets.GenericParameter |
    AttributeTargets.Parameter |
    AttributeTargets.Property |
    AttributeTargets.ReturnValue,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NullableAttribute : Attribute
{
    /// <summary>
    /// The nullability flags for the type.
    /// </summary>
    public readonly byte[] NullableFlags;

    /// <summary>
    /// Initializes the attribute with a single nullability flag.
    /// </summary>
    public NullableAttribute(byte flag)
    {
        NullableFlags = new byte[] { flag };
    }

    /// <summary>
    /// Initializes the attribute with an array of nullability flags.
    /// </summary>
    public NullableAttribute(byte[] flags)
    {
        NullableFlags = flags;
    }
}

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
/// <remarks>
/// The compiler emits this attribute on types to indicate the default nullable context.
/// This reduces the size of metadata by avoiding per-member NullableAttribute annotations.
/// </remarks>
[AttributeUsage(
    AttributeTargets.Class |
    AttributeTargets.Delegate |
    AttributeTargets.Interface |
    AttributeTargets.Method |
    AttributeTargets.Struct,
    AllowMultiple = false,
    Inherited = false)]
public sealed class NullableContextAttribute : Attribute
{
    /// <summary>
    /// The default nullable flag for members in this context.
    /// </summary>
    public readonly byte Flag;

    /// <summary>
    /// Initializes the attribute with the specified default nullable flag.
    /// </summary>
    public NullableContextAttribute(byte flag)
    {
        Flag = flag;
    }
}

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
[AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
public sealed class NullablePublicOnlyAttribute : Attribute
{
    /// <summary>
    /// Indicates whether the annotation only applies to public and protected members.
    /// </summary>
    public readonly bool IncludesInternals;

    /// <summary>
    /// Initializes the attribute.
    /// </summary>
    public NullablePublicOnlyAttribute(bool includesInternals)
    {
        IncludesInternals = includesInternals;
    }
}
