// ProtonOS System.Runtime - OperationCanceledException
// The exception that is thrown in a thread upon cancellation of an operation.

using System.Threading;

namespace System
{
    /// <summary>
    /// The exception that is thrown in a thread upon cancellation of an operation that the thread was executing.
    /// </summary>
    public class OperationCanceledException : SystemException
    {
        private readonly CancellationToken _cancellationToken;

        /// <summary>Gets a token associated with the operation that was canceled.</summary>
        public CancellationToken CancellationToken => _cancellationToken;

        /// <summary>Initializes a new instance of the OperationCanceledException class with a default message.</summary>
        public OperationCanceledException() : this("The operation was canceled.")
        {
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message.</summary>
        public OperationCanceledException(string? message) : base(message)
        {
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message and inner exception.</summary>
        public OperationCanceledException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a cancellation token.</summary>
        public OperationCanceledException(CancellationToken token) : this("The operation was canceled.", token)
        {
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message and cancellation token.</summary>
        public OperationCanceledException(string? message, CancellationToken token) : base(message)
        {
            _cancellationToken = token;
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message, inner exception, and cancellation token.</summary>
        public OperationCanceledException(string? message, Exception? innerException, CancellationToken token) : base(message, innerException)
        {
            _cancellationToken = token;
        }
    }
}
