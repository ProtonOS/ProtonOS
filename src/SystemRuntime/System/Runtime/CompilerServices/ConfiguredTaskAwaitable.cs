// ProtonOS System.Runtime - ConfiguredTaskAwaitable
// Provides an awaitable object that enables configured awaits on a task.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides an awaitable object that enables configured awaits on a task.
    /// </summary>
    public readonly struct ConfiguredTaskAwaitable
    {
        private readonly Task _task;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredTaskAwaitable(Task task, bool continueOnCapturedContext)
        {
            _task = task;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        /// <summary>Gets an awaiter for this awaitable.</summary>
        public ConfiguredTaskAwaiter GetAwaiter() => new ConfiguredTaskAwaiter(_task, _continueOnCapturedContext);

        /// <summary>Provides an awaiter for a ConfiguredTaskAwaitable.</summary>
        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
        {
            private readonly Task _task;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredTaskAwaiter(Task task, bool continueOnCapturedContext)
            {
                _task = task;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            /// <summary>Gets whether the task being awaited is completed.</summary>
            public bool IsCompleted => _task.IsCompleted;

            /// <summary>Ends the await on the completed task.</summary>
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
    }

    /// <summary>
    /// Provides an awaitable object that enables configured awaits on a task.
    /// </summary>
    public readonly struct ConfiguredTaskAwaitable<TResult>
    {
        private readonly Task<TResult> _task;
        private readonly bool _continueOnCapturedContext;

        internal ConfiguredTaskAwaitable(Task<TResult> task, bool continueOnCapturedContext)
        {
            _task = task;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        /// <summary>Gets an awaiter for this awaitable.</summary>
        public ConfiguredTaskAwaiter GetAwaiter() => new ConfiguredTaskAwaiter(_task, _continueOnCapturedContext);

        /// <summary>Provides an awaiter for a ConfiguredTaskAwaitable.</summary>
        public readonly struct ConfiguredTaskAwaiter : ICriticalNotifyCompletion
        {
            private readonly Task<TResult> _task;
            private readonly bool _continueOnCapturedContext;

            internal ConfiguredTaskAwaiter(Task<TResult> task, bool continueOnCapturedContext)
            {
                _task = task;
                _continueOnCapturedContext = continueOnCapturedContext;
            }

            /// <summary>Gets whether the task being awaited is completed.</summary>
            public bool IsCompleted => _task.IsCompleted;

            /// <summary>Ends the await on the completed task.</summary>
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
}
