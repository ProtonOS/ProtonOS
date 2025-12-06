// ProtonOS korlib - IAsyncDisposable interface

namespace System;

/// <summary>
/// Provides a mechanism for releasing unmanaged resources asynchronously.
/// </summary>
public interface IAsyncDisposable
{
    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or
    /// resetting unmanaged resources asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    // Note: Returns ValueTask when async infrastructure is available
    // For now, we define it without the return type dependency
    void DisposeAsync();
}
