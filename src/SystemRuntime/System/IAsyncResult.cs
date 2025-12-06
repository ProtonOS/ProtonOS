// ProtonOS System.Runtime - IAsyncResult
// Represents the status of an asynchronous operation.

using System.Threading;

namespace System
{
    /// <summary>
    /// Represents the status of an asynchronous operation.
    /// </summary>
    public interface IAsyncResult
    {
        /// <summary>Gets a user-defined object that qualifies or contains information about an asynchronous operation.</summary>
        object? AsyncState { get; }

        /// <summary>Gets a WaitHandle that is used to wait for an asynchronous operation to complete.</summary>
        WaitHandle AsyncWaitHandle { get; }

        /// <summary>Gets a value that indicates whether the asynchronous operation completed synchronously.</summary>
        bool CompletedSynchronously { get; }

        /// <summary>Gets a value that indicates whether the asynchronous operation has completed.</summary>
        bool IsCompleted { get; }
    }
}

namespace System.Threading
{
    /// <summary>
    /// Encapsulates operating system-specific objects that wait for exclusive access to shared resources.
    /// </summary>
    public abstract class WaitHandle : IDisposable
    {
        /// <summary>Indicates that a WaitAny operation timed out before any of the wait handles were signaled.</summary>
        public const int WaitTimeout = 0x102;

        /// <summary>Blocks the current thread until the current WaitHandle receives a signal.</summary>
        public virtual bool WaitOne()
        {
            return WaitOne(-1);
        }

        /// <summary>Blocks the current thread until the current WaitHandle receives a signal, using a 32-bit signed integer to specify the time interval.</summary>
        public virtual bool WaitOne(int millisecondsTimeout)
        {
            // Default implementation - derived classes should override
            return false;
        }

        /// <summary>Blocks the current thread until the current WaitHandle receives a signal, using a TimeSpan to specify the time interval.</summary>
        public virtual bool WaitOne(TimeSpan timeout)
        {
            return WaitOne((int)timeout.TotalMilliseconds);
        }

        /// <summary>Releases all resources used by the current instance of the WaitHandle class.</summary>
        public virtual void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>Releases the unmanaged resources used by the WaitHandle and optionally releases the managed resources.</summary>
        protected virtual void Dispose(bool disposing)
        {
        }
    }
}
