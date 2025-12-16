// korlib - KeyNotFoundException
// Exception thrown when a key is not present in a collection.

namespace System.Collections.Generic;

/// <summary>
/// The exception that is thrown when the key specified for accessing an element
/// in a collection does not match any key in the collection.
/// </summary>
public class KeyNotFoundException : Exception
{
    public KeyNotFoundException()
        : base("The given key was not present in the dictionary.")
    {
    }

    public KeyNotFoundException(string? message)
        : base(message)
    {
    }

    public KeyNotFoundException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
