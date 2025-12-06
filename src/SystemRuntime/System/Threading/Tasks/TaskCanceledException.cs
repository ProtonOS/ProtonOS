// ProtonOS System.Runtime - TaskCanceledException
// Represents an exception used to communicate task cancellation.

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents an exception used to communicate task cancellation.
    /// </summary>
    public class TaskCanceledException : OperationCanceledException
    {
        private readonly Task? _canceledTask;

        /// <summary>Gets the task associated with this exception.</summary>
        public Task? Task => _canceledTask;

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

        /// <summary>Initializes a new instance of the TaskCanceledException class with a specified message, inner exception, and cancellation token.</summary>
        public TaskCanceledException(string? message, Exception? innerException, CancellationToken token) : base(message, innerException, token)
        {
        }

        /// <summary>Initializes a new instance of the TaskCanceledException class with a reference to the Task that has been canceled.</summary>
        public TaskCanceledException(Task? task) : base("A task was canceled.")
        {
            _canceledTask = task;
        }
    }
}
