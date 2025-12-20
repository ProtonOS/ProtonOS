// ProtonOS korlib - OperationCanceledException
// The exception that is thrown in a thread upon cancellation of an operation.
// Note: CancellationToken support not yet available - simplified version.

namespace System
{
    /// <summary>
    /// The exception that is thrown in a thread upon cancellation of an operation that the thread was executing.
    /// </summary>
    public class OperationCanceledException : SystemException
    {
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
    }
}
