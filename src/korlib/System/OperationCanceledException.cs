// ProtonOS korlib - OperationCanceledException
// The exception that is thrown in a thread upon cancellation of an operation.

namespace System
{
    /// <summary>
    /// The exception that is thrown in a thread upon cancellation of an operation that the thread was executing.
    /// </summary>
    public class OperationCanceledException : SystemException
    {
        /// <summary>Gets the CancellationToken associated with the operation that was canceled.</summary>
        public Threading.CancellationToken CancellationToken { get; }

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

        /// <summary>Initializes a new instance of the OperationCanceledException class with a CancellationToken.</summary>
        public OperationCanceledException(Threading.CancellationToken token) : this("The operation was canceled.")
        {
            CancellationToken = token;
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message and CancellationToken.</summary>
        public OperationCanceledException(string? message, Threading.CancellationToken token) : base(message)
        {
            CancellationToken = token;
        }

        /// <summary>Initializes a new instance of the OperationCanceledException class with a specified message, inner exception, and CancellationToken.</summary>
        public OperationCanceledException(string? message, Exception? innerException, Threading.CancellationToken token) : base(message, innerException)
        {
            CancellationToken = token;
        }
    }
}
