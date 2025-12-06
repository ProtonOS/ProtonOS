// ProtonOS korlib - ICollection interface (non-generic)

namespace System.Collections;

/// <summary>
/// Defines size, enumerators, and synchronization methods for all non-generic collections.
/// </summary>
public interface ICollection : IEnumerable
{
    /// <summary>
    /// Gets the number of elements contained in the ICollection.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Gets a value indicating whether access to the ICollection is synchronized (thread safe).
    /// </summary>
    bool IsSynchronized { get; }

    /// <summary>
    /// Gets an object that can be used to synchronize access to the ICollection.
    /// </summary>
    object SyncRoot { get; }

    /// <summary>
    /// Copies the elements of the ICollection to an Array, starting at a particular Array index.
    /// </summary>
    /// <param name="array">
    /// The one-dimensional Array that is the destination of the elements copied from ICollection.
    /// </param>
    /// <param name="index">The zero-based index in array at which copying begins.</param>
    void CopyTo(Array array, int index);
}
