// ProtonOS DDK - Proc Filesystem Content Generator Interface

namespace ProtonOS.DDK.Storage.Proc;

/// <summary>
/// Interface for generating dynamic content for /proc files.
/// Each generator produces the content for one or more proc entries.
/// </summary>
public interface IProcContentGenerator
{
    /// <summary>
    /// Generate the content for this proc entry.
    /// Called when the proc file is opened.
    /// </summary>
    /// <returns>The file content as a string.</returns>
    string Generate();
}
