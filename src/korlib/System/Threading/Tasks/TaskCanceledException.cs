// ProtonOS korlib - TaskCanceledException
// Represents an exception used to communicate task cancellation.
// Note: Task and CancellationToken support not yet available - simplified version.

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents an exception used to communicate task cancellation.
    /// </summary>
    public class TaskCanceledException : OperationCanceledException
    {
        /// <summary>Initializes a new instance of the TaskCanceledException class with a default message.</summary>
        public TaskCanceledException() : this("A task was canceled.")
        {
        }

        /// <summary>Initializes a new instance of the TaskCanceledException class with a specified message.</summary>
        public TaskCanceledException(string? message) : base(message)
        {
        }

        /// <summary>Initializes a new instance of the TaskCanceledException class with a specified message and inner exception.</summary>
        public TaskCanceledException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
