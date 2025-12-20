// ProtonOS korlib - ValueTaskAwaiter
// Provides an object that waits for the completion of a ValueTask.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Provides an awaiter for a ValueTask.
    /// </summary>
    public readonly struct ValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask _value;

        /// <summary>Initializes the awaiter.</summary>
        internal ValueTaskAwaiter(ValueTask value)
        {
            _value = value;
        }

        /// <summary>Gets whether the ValueTask being awaited is completed.</summary>
        public bool IsCompleted => _value.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous operation.</summary>
        public void GetResult()
        {
            _value.GetResult();
        }

        /// <summary>Schedules the continuation action for this ValueTaskAwaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.GetAwaiter().OnCompleted(continuation);
            else
                continuation?.Invoke();
        }

        /// <summary>Schedules the continuation action for this ValueTaskAwaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.GetAwaiter().UnsafeOnCompleted(continuation);
            else
                continuation?.Invoke();
        }
    }

    /// <summary>
    /// Provides an awaiter for a ValueTask{TResult}.
    /// </summary>
    public readonly struct ValueTaskAwaiter<TResult> : ICriticalNotifyCompletion
    {
        private readonly ValueTask<TResult> _value;

        /// <summary>Initializes the awaiter.</summary>
        internal ValueTaskAwaiter(ValueTask<TResult> value)
        {
            _value = value;
        }

        /// <summary>Gets whether the ValueTask being awaited is completed.</summary>
        public bool IsCompleted => _value.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous operation.</summary>
        public TResult GetResult()
        {
            return _value.Result;
        }

        /// <summary>Schedules the continuation action for this ValueTaskAwaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.GetAwaiter().OnCompleted(continuation);
            else
                continuation?.Invoke();
        }

        /// <summary>Schedules the continuation action for this ValueTaskAwaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.GetAwaiter().UnsafeOnCompleted(continuation);
            else
                continuation?.Invoke();
        }
    }

    /// <summary>
    /// Provides an awaitable for a configured ValueTask.
    /// </summary>
    public readonly struct ConfiguredValueTaskAwaitable
    {
        private readonly ValueTask _value;

        internal ConfiguredValueTaskAwaitable(ValueTask value)
        {
            _value = value;
        }

        /// <summary>Returns an awaiter for this awaitable.</summary>
        public ConfiguredValueTaskAwaiter GetAwaiter() => new ConfiguredValueTaskAwaiter(_value);
    }

    /// <summary>
    /// Provides an awaiter for a ConfiguredValueTaskAwaitable.
    /// </summary>
    public readonly struct ConfiguredValueTaskAwaiter : ICriticalNotifyCompletion
    {
        private readonly ValueTask _value;

        internal ConfiguredValueTaskAwaiter(ValueTask value)
        {
            _value = value;
        }

        /// <summary>Gets whether the task being awaited is completed.</summary>
        public bool IsCompleted => _value.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous operation.</summary>
        public void GetResult() => _value.GetResult();

        /// <summary>Schedules the continuation action for this awaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.ConfigureAwait(_value.ContinueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            else
                continuation?.Invoke();
        }

        /// <summary>Schedules the continuation action for this awaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.ConfigureAwait(_value.ContinueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
            else
                continuation?.Invoke();
        }
    }

    /// <summary>
    /// Provides an awaitable for a configured ValueTask{TResult}.
    /// </summary>
    public readonly struct ConfiguredValueTaskAwaitable<TResult>
    {
        private readonly ValueTask<TResult> _value;

        internal ConfiguredValueTaskAwaitable(ValueTask<TResult> value)
        {
            _value = value;
        }

        /// <summary>Returns an awaiter for this awaitable.</summary>
        public ConfiguredValueTaskAwaiter<TResult> GetAwaiter() => new ConfiguredValueTaskAwaiter<TResult>(_value);
    }

    /// <summary>
    /// Provides an awaiter for a ConfiguredValueTaskAwaitable{TResult}.
    /// </summary>
    public readonly struct ConfiguredValueTaskAwaiter<TResult> : ICriticalNotifyCompletion
    {
        private readonly ValueTask<TResult> _value;

        internal ConfiguredValueTaskAwaiter(ValueTask<TResult> value)
        {
            _value = value;
        }

        /// <summary>Gets whether the task being awaited is completed.</summary>
        public bool IsCompleted => _value.IsCompleted;

        /// <summary>Ends the wait for the completion of the asynchronous operation.</summary>
        public TResult GetResult() => _value.Result;

        /// <summary>Schedules the continuation action for this awaiter.</summary>
        public void OnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.ConfigureAwait(_value.ContinueOnCapturedContext).GetAwaiter().OnCompleted(continuation);
            else
                continuation?.Invoke();
        }

        /// <summary>Schedules the continuation action for this awaiter.</summary>
        public void UnsafeOnCompleted(Action continuation)
        {
            var task = _value.InternalTask;
            if (task != null)
                task.ConfigureAwait(_value.ContinueOnCapturedContext).GetAwaiter().UnsafeOnCompleted(continuation);
            else
                continuation?.Invoke();
        }
    }
}
