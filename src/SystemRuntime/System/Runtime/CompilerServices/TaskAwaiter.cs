// ProtonOS System.Runtime - TaskAwaiter
// Provides an object that waits for the completion of an asynchronous task.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides an object that waits for the completion of an asynchronous task.
    /// </summary>
    public readonly struct TaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly Task _task;

        internal TaskAwaiter(Task task)
        {
            _task = task;
        }

        /// <summary>Gets whether the task being awaited is completed.</summary>
        public bool IsCompleted => _task.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous task.</summary>
        public void GetResult()
        {
            if (_task.IsFaulted && _task.Exception != null)
            {
                throw _task.Exception.InnerExceptions.Count == 1
                    ? _task.Exception.InnerExceptions[0]
                    : _task.Exception;
            }
            if (_task.IsCanceled)
            {
                throw new TaskCanceledException(_task);
            }
        }

        /// <summary>Schedules the continuation action for this TaskAwaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            _task.AddContinuation(continuation);
        }

        /// <summary>Schedules the continuation action for this TaskAwaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            _task.AddContinuation(continuation);
        }
    }

    /// <summary>
    /// Provides an object that waits for the completion of an asynchronous task and provides the result.
    /// </summary>
    public readonly struct TaskAwaiter<TResult> : ICriticalNotifyCompletion
    {
        private readonly Task<TResult> _task;

        internal TaskAwaiter(Task<TResult> task)
        {
            _task = task;
        }

        /// <summary>Gets whether the task being awaited is completed.</summary>
        public bool IsCompleted => _task.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous task.</summary>
        public TResult GetResult()
        {
            if (_task.IsFaulted && _task.Exception != null)
            {
                throw _task.Exception.InnerExceptions.Count == 1
                    ? _task.Exception.InnerExceptions[0]
                    : _task.Exception;
            }
            if (_task.IsCanceled)
            {
                throw new TaskCanceledException(_task);
            }
            return _task.Result;
        }

        /// <summary>Schedules the continuation action for this TaskAwaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            _task.AddContinuation(continuation);
        }

        /// <summary>Schedules the continuation action for this TaskAwaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            _task.AddContinuation(continuation);
        }
    }
}
