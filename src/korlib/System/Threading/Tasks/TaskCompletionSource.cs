// ProtonOS korlib - TaskCompletionSource
// Represents the producer side of a Task unbound to a delegate.

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents the producer side of a Task{TResult} unbound to a delegate.
    /// </summary>
    public class TaskCompletionSource<TResult>
    {
        private readonly Task<TResult> _task;

        /// <summary>Creates a TaskCompletionSource.</summary>
        public TaskCompletionSource()
        {
            _task = new Task<TResult>();
        }

        /// <summary>Gets the Task created by this TaskCompletionSource.</summary>
        public Task<TResult> Task => _task;

        /// <summary>Transitions the underlying Task into the RanToCompletion state.</summary>
        public void SetResult(TResult result)
        {
            if (!TrySetResult(result))
            {
                throw new InvalidOperationException("Task has already completed");
            }
        }

        /// <summary>Attempts to transition the underlying Task into the RanToCompletion state.</summary>
        public bool TrySetResult(TResult result)
        {
            if (_task.IsCompleted) return false;
            _task.SetResult(result);
            return true;
        }

        /// <summary>Transitions the underlying Task into the Faulted state.</summary>
        public void SetException(Exception exception)
        {
            if (!TrySetException(exception))
            {
                throw new InvalidOperationException("Task has already completed");
            }
        }

        /// <summary>Attempts to transition the underlying Task into the Faulted state.</summary>
        public bool TrySetException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            if (_task.IsCompleted) return false;
            _task.SetException(exception);
            return true;
        }

        /// <summary>Transitions the underlying Task into the Canceled state.</summary>
        public void SetCanceled()
        {
            SetCanceled(default);
        }

        /// <summary>Transitions the underlying Task into the Canceled state.</summary>
        public void SetCanceled(CancellationToken cancellationToken)
        {
            if (!TrySetCanceled(cancellationToken))
            {
                throw new InvalidOperationException("Task has already completed");
            }
        }

        /// <summary>Attempts to transition the underlying Task into the Canceled state.</summary>
        public bool TrySetCanceled()
        {
            return TrySetCanceled(default);
        }

        /// <summary>Attempts to transition the underlying Task into the Canceled state.</summary>
        public bool TrySetCanceled(CancellationToken cancellationToken)
        {
            if (_task.IsCompleted) return false;
            _task.SetCanceled(cancellationToken);
            return true;
        }
    }
}
