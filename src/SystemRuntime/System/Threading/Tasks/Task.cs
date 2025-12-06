// ProtonOS System.Runtime - Task
// Represents an asynchronous operation.

using System.Runtime.CompilerServices;
using System.Collections.Generic;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Represents an asynchronous operation.
    /// </summary>
    public class Task : IAsyncResult
    {
        private volatile TaskStatus _status;
        private Exception? _exception;
        private readonly object _lock = new object();
        private List<Action>? _continuations;
        private readonly CancellationToken _cancellationToken;

        /// <summary>Gets a task that has already completed successfully.</summary>
        public static Task CompletedTask { get; } = new Task(TaskStatus.RanToCompletion);

        /// <summary>Gets the TaskStatus of this Task.</summary>
        public TaskStatus Status => _status;

        /// <summary>Gets whether this Task instance has completed execution.</summary>
        public bool IsCompleted => _status >= TaskStatus.RanToCompletion;

        /// <summary>Gets whether the Task completed due to being canceled.</summary>
        public bool IsCanceled => _status == TaskStatus.Canceled;

        /// <summary>Gets whether the Task completed due to an unhandled exception.</summary>
        public bool IsFaulted => _status == TaskStatus.Faulted;

        /// <summary>Gets whether this Task has completed successfully.</summary>
        public bool IsCompletedSuccessfully => _status == TaskStatus.RanToCompletion;

        /// <summary>Gets the AggregateException that caused the Task to end prematurely.</summary>
        public AggregateException? Exception => _exception as AggregateException ?? (_exception != null ? new AggregateException(_exception) : null);

        // IAsyncResult implementation
        object? IAsyncResult.AsyncState => null;
        WaitHandle IAsyncResult.AsyncWaitHandle => throw new NotSupportedException();
        bool IAsyncResult.CompletedSynchronously => false;

        /// <summary>Creates a task in the completed state.</summary>
        protected internal Task(TaskStatus status)
        {
            _status = status;
        }

        /// <summary>Initializes a new Task.</summary>
        public Task(Action action) : this(action, CancellationToken.None)
        {
        }

        /// <summary>Initializes a new Task with a CancellationToken.</summary>
        public Task(Action action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            _cancellationToken = cancellationToken;
            _status = TaskStatus.Created;
        }

        /// <summary>Creates a task for internal use that's already running.</summary>
        internal Task()
        {
            _status = TaskStatus.WaitingForActivation;
        }

        /// <summary>Gets an awaiter used to await this Task.</summary>
        public TaskAwaiter GetAwaiter() => new TaskAwaiter(this);

        /// <summary>Configures an awaiter used to await this Task.</summary>
        public ConfiguredTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredTaskAwaitable(this, continueOnCapturedContext);
        }

        /// <summary>Creates a continuation that executes when the target Task completes.</summary>
        public Task ContinueWith(Action<Task> continuationAction)
        {
            if (continuationAction == null) throw new ArgumentNullException(nameof(continuationAction));

            var tcs = new TaskCompletionSource<object?>();

            Action continuation = () =>
            {
                try
                {
                    continuationAction(this);
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            if (IsCompleted)
            {
                continuation();
            }
            else
            {
                lock (_lock)
                {
                    if (IsCompleted)
                    {
                        continuation();
                    }
                    else
                    {
                        _continuations ??= new List<Action>();
                        _continuations.Add(continuation);
                    }
                }
            }

            return tcs.Task;
        }

        /// <summary>Waits for the Task to complete execution.</summary>
        public void Wait()
        {
            // In a proper implementation, this would block the thread
            // For a kernel environment, we'd spin or use a synchronization primitive
            SpinWait();
        }

        /// <summary>Waits for the Task to complete execution within a specified time interval.</summary>
        public bool Wait(int millisecondsTimeout)
        {
            // Simplified - just spin wait
            SpinWait();
            return IsCompleted;
        }

        /// <summary>Waits for the Task to complete execution with a timeout.</summary>
        public bool Wait(TimeSpan timeout)
        {
            return Wait((int)timeout.TotalMilliseconds);
        }

        private void SpinWait()
        {
            // Simple spin wait - in a real implementation would yield
            while (!IsCompleted)
            {
                // Spin
            }
        }

        internal void SetResult()
        {
            CompleteWith(TaskStatus.RanToCompletion);
        }

        internal void SetException(Exception exception)
        {
            _exception = exception;
            CompleteWith(TaskStatus.Faulted);
        }

        internal void SetCanceled(CancellationToken cancellationToken)
        {
            _exception = new TaskCanceledException(this);
            CompleteWith(TaskStatus.Canceled);
        }

        private void CompleteWith(TaskStatus status)
        {
            List<Action>? continuationsToRun = null;

            lock (_lock)
            {
                if (_status >= TaskStatus.RanToCompletion) return;
                _status = status;

                if (_continuations != null)
                {
                    continuationsToRun = new List<Action>(_continuations);
                    _continuations = null;
                }
            }

            if (continuationsToRun != null)
            {
                foreach (var continuation in continuationsToRun)
                {
                    try
                    {
                        continuation();
                    }
                    catch
                    {
                        // Swallow continuation exceptions
                    }
                }
            }
        }

        internal void AddContinuation(Action continuation)
        {
            if (IsCompleted)
            {
                continuation();
                return;
            }

            lock (_lock)
            {
                if (IsCompleted)
                {
                    continuation();
                    return;
                }
                _continuations ??= new List<Action>();
                _continuations.Add(continuation);
            }
        }

        // Static factory methods

        /// <summary>Creates a task that completes after a specified number of milliseconds.</summary>
        public static Task Delay(int millisecondsDelay)
        {
            if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
            if (millisecondsDelay == 0) return CompletedTask;

            // In kernel mode, we'd use a timer here
            // For now, return a completed task
            return CompletedTask;
        }

        /// <summary>Creates a task that completes after a specified time interval.</summary>
        public static Task Delay(TimeSpan delay)
        {
            return Delay((int)delay.TotalMilliseconds);
        }

        /// <summary>Creates a Task that's completed successfully with the specified result.</summary>
        public static Task<TResult> FromResult<TResult>(TResult result)
        {
            return new Task<TResult>(result);
        }

        /// <summary>Creates a Task that has completed with a specified exception.</summary>
        public static Task FromException(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            var task = new Task();
            task.SetException(exception);
            return task;
        }

        /// <summary>Creates a Task that has completed with a specified exception.</summary>
        public static Task<TResult> FromException<TResult>(Exception exception)
        {
            if (exception == null) throw new ArgumentNullException(nameof(exception));
            var task = new Task<TResult>();
            task.SetException(exception);
            return task;
        }

        /// <summary>Creates a Task that's completed due to cancellation.</summary>
        public static Task FromCanceled(CancellationToken cancellationToken)
        {
            var task = new Task();
            task.SetCanceled(cancellationToken);
            return task;
        }

        /// <summary>Creates a Task that's completed due to cancellation.</summary>
        public static Task<TResult> FromCanceled<TResult>(CancellationToken cancellationToken)
        {
            var task = new Task<TResult>();
            task.SetCanceled(cancellationToken);
            return task;
        }

        /// <summary>Queues the specified work to run on the thread pool.</summary>
        public static Task Run(Action action)
        {
            return Run(action, CancellationToken.None);
        }

        /// <summary>Queues the specified work to run on the thread pool.</summary>
        public static Task Run(Action action, CancellationToken cancellationToken)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            // In kernel mode, we'd schedule this on a work queue
            // For now, just run synchronously
            var task = new Task();
            try
            {
                action();
                task.SetResult();
            }
            catch (Exception ex)
            {
                task.SetException(ex);
            }
            return task;
        }

        /// <summary>Queues the specified work to run on the thread pool.</summary>
        public static Task<TResult> Run<TResult>(Func<TResult> function)
        {
            return Run(function, CancellationToken.None);
        }

        /// <summary>Queues the specified work to run on the thread pool.</summary>
        public static Task<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));

            var task = new Task<TResult>();
            try
            {
                task.SetResult(function());
            }
            catch (Exception ex)
            {
                task.SetException(ex);
            }
            return task;
        }

        /// <summary>Creates a task that will complete when all of the supplied tasks have completed.</summary>
        public static Task WhenAll(params Task[] tasks)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0) return CompletedTask;

            var tcs = new TaskCompletionSource<object?>();
            int remaining = tasks.Length;
            List<Exception>? exceptions = null;
            object lockObj = new object();

            foreach (var task in tasks)
            {
                task.ContinueWith(t =>
                {
                    lock (lockObj)
                    {
                        if (t.IsFaulted && t._exception != null)
                        {
                            exceptions ??= new List<Exception>();
                            exceptions.Add(t._exception);
                        }

                        remaining--;
                        if (remaining == 0)
                        {
                            if (exceptions != null)
                            {
                                tcs.SetException(new AggregateException(exceptions));
                            }
                            else
                            {
                                tcs.SetResult(null);
                            }
                        }
                    }
                });
            }

            return tcs.Task;
        }

        /// <summary>Creates a task that will complete when any of the supplied tasks have completed.</summary>
        public static Task<Task> WhenAny(params Task[] tasks)
        {
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            if (tasks.Length == 0) throw new ArgumentException("At least one task is required", nameof(tasks));

            var tcs = new TaskCompletionSource<Task>();

            foreach (var task in tasks)
            {
                task.ContinueWith(t =>
                {
                    tcs.TrySetResult(t);
                });
            }

            return tcs.Task;
        }
    }

    /// <summary>
    /// Represents an asynchronous operation that can return a value.
    /// </summary>
    public class Task<TResult> : Task
    {
        private TResult? _result;

        /// <summary>Gets the result value of this Task.</summary>
        public TResult Result
        {
            get
            {
                Wait();
                if (IsFaulted && Exception != null) throw Exception;
                if (IsCanceled) throw new TaskCanceledException(this);
                return _result!;
            }
        }

        internal Task() : base()
        {
        }

        internal Task(TResult result) : base(TaskStatus.RanToCompletion)
        {
            _result = result;
        }

        /// <summary>Gets an awaiter used to await this Task.</summary>
        public new TaskAwaiter<TResult> GetAwaiter() => new TaskAwaiter<TResult>(this);

        /// <summary>Configures an awaiter used to await this Task.</summary>
        public new ConfiguredTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredTaskAwaitable<TResult>(this, continueOnCapturedContext);
        }

        internal void SetResult(TResult result)
        {
            _result = result;
            base.SetResult();
        }

        /// <summary>Creates a continuation that executes when the target Task completes.</summary>
        public Task<TNewResult> ContinueWith<TNewResult>(Func<Task<TResult>, TNewResult> continuationFunction)
        {
            if (continuationFunction == null) throw new ArgumentNullException(nameof(continuationFunction));

            var tcs = new TaskCompletionSource<TNewResult>();

            Action continuation = () =>
            {
                try
                {
                    var result = continuationFunction(this);
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            };

            AddContinuation(continuation);
            return tcs.Task;
        }
    }
}
