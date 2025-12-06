// ProtonOS korlib - IsExternalInit for C# 9.0+ init accessors

namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved to be used by the compiler for tracking metadata.
/// This class should not be used by developers in source code.
/// </summary>
/// <remarks>
/// This type is required by the C# compiler to support init-only setters in properties.
/// For example: public int Value { get; init; }
/// </remarks>
public static class IsExternalInit
{
}
