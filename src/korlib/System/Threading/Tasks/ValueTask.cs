// ProtonOS korlib - ValueTask
// Provides value type wrappers for Task and Task<TResult> to avoid allocation
// when the result is already available synchronously.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Threading.Tasks
{
    /// <summary>
    /// Provides an awaitable result of an asynchronous operation.
    /// </summary>
    /// <remarks>
    /// ValueTask avoids heap allocation when the operation completes synchronously.
    /// Use ValueTask when operations complete synchronously most of the time.
    /// </remarks>
    public readonly struct ValueTask : IEquatable<ValueTask>
    {
        private readonly Task? _task;
        private readonly bool _continueOnCapturedContext;

        /// <summary>Creates a ValueTask wrapping a completed task.</summary>
        public ValueTask(Task task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            _continueOnCapturedContext = true;
        }

        private ValueTask(Task? task, bool continueOnCapturedContext)
        {
            _task = task;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        /// <summary>Creates a successfully completed ValueTask.</summary>
        public static ValueTask CompletedTask => default;

        /// <summary>Creates a ValueTask with a cancellation.</summary>
        public static ValueTask FromCanceled(CancellationToken cancellationToken)
        {
            return new ValueTask(Task.FromCanceled(cancellationToken));
        }

        /// <summary>Creates a ValueTask with an exception.</summary>
        public static ValueTask FromException(Exception exception)
        {
            return new ValueTask(Task.FromException(exception));
        }

        /// <summary>Gets whether the ValueTask represents a completed operation.</summary>
        public bool IsCompleted => _task == null || _task.IsCompleted;

        /// <summary>Gets whether the ValueTask completed successfully.</summary>
        public bool IsCompletedSuccessfully =>
            _task == null || _task.Status == TaskStatus.RanToCompletion;

        /// <summary>Gets whether the ValueTask completed due to an unhandled exception.</summary>
        public bool IsFaulted => _task != null && _task.IsFaulted;

        /// <summary>Gets whether the ValueTask completed due to being canceled.</summary>
        public bool IsCanceled => _task != null && _task.IsCanceled;

        /// <summary>Gets an awaiter for this ValueTask.</summary>
        public ValueTaskAwaiter GetAwaiter() => new ValueTaskAwaiter(this);

        /// <summary>Configures an awaiter for this ValueTask.</summary>
        public ConfiguredValueTaskAwaitable ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredValueTaskAwaitable(
                new ValueTask(_task, continueOnCapturedContext));
        }

        /// <summary>Gets the result, blocking if necessary.</summary>
        internal void GetResult()
        {
            if (_task == null)
                return;

            _task.GetAwaiter().GetResult();
        }

        /// <summary>Gets the underlying task, if any.</summary>
        public Task? AsTask() => _task ?? Task.CompletedTask;

        /// <summary>Preserves the object so it survives async/await state machine boxing.</summary>
        public ValueTask Preserve() => _task == null ? this : new ValueTask(AsTask()!);

        public bool Equals(ValueTask other) => _task == other._task;
        public override bool Equals(object? obj) => obj is ValueTask vt && Equals(vt);
        public override int GetHashCode() => _task?.GetHashCode() ?? 0;
        public static bool operator ==(ValueTask left, ValueTask right) => left.Equals(right);
        public static bool operator !=(ValueTask left, ValueTask right) => !left.Equals(right);

        internal bool ContinueOnCapturedContext => _continueOnCapturedContext;
        internal Task? InternalTask => _task;
    }

    /// <summary>
    /// Provides an awaitable result of an asynchronous operation that returns a value.
    /// </summary>
    /// <typeparam name="TResult">The type of the result produced.</typeparam>
    public readonly struct ValueTask<TResult> : IEquatable<ValueTask<TResult>>
    {
        private readonly Task<TResult>? _task;
        private readonly TResult? _result;
        private readonly bool _hasResult;
        private readonly bool _continueOnCapturedContext;

        /// <summary>Creates a ValueTask wrapping the specified task.</summary>
        public ValueTask(Task<TResult> task)
        {
            _task = task ?? throw new ArgumentNullException(nameof(task));
            _result = default;
            _hasResult = false;
            _continueOnCapturedContext = true;
        }

        /// <summary>Creates a ValueTask with a known result.</summary>
        public ValueTask(TResult result)
        {
            _task = null;
            _result = result;
            _hasResult = true;
            _continueOnCapturedContext = true;
        }

        private ValueTask(Task<TResult>? task, TResult? result, bool hasResult, bool continueOnCapturedContext)
        {
            _task = task;
            _result = result;
            _hasResult = hasResult;
            _continueOnCapturedContext = continueOnCapturedContext;
        }

        /// <summary>Creates a ValueTask with a cancellation.</summary>
        public static ValueTask<TResult> FromCanceled(CancellationToken cancellationToken)
        {
            return new ValueTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));
        }

        /// <summary>Creates a ValueTask with an exception.</summary>
        public static ValueTask<TResult> FromException(Exception exception)
        {
            return new ValueTask<TResult>(Task.FromException<TResult>(exception));
        }

        /// <summary>Creates a completed ValueTask with the specified result.</summary>
        public static ValueTask<TResult> FromResult(TResult result) => new ValueTask<TResult>(result);

        /// <summary>Gets whether the ValueTask represents a completed operation.</summary>
        public bool IsCompleted => _hasResult || (_task != null && _task.IsCompleted);

        /// <summary>Gets whether the ValueTask completed successfully.</summary>
        public bool IsCompletedSuccessfully =>
            _hasResult || (_task != null && _task.Status == TaskStatus.RanToCompletion);

        /// <summary>Gets whether the ValueTask completed due to an unhandled exception.</summary>
        public bool IsFaulted => _task != null && _task.IsFaulted;

        /// <summary>Gets whether the ValueTask completed due to being canceled.</summary>
        public bool IsCanceled => _task != null && _task.IsCanceled;

        /// <summary>Gets the result.</summary>
        public TResult Result
        {
            get
            {
                if (_hasResult)
                    return _result!;

                if (_task == null)
                    throw new InvalidOperationException("ValueTask not initialized");

                return _task.GetAwaiter().GetResult();
            }
        }

        /// <summary>Gets an awaiter for this ValueTask.</summary>
        public ValueTaskAwaiter<TResult> GetAwaiter() => new ValueTaskAwaiter<TResult>(this);

        /// <summary>Configures an awaiter for this ValueTask.</summary>
        public ConfiguredValueTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext)
        {
            return new ConfiguredValueTaskAwaitable<TResult>(
                new ValueTask<TResult>(_task, _result, _hasResult, continueOnCapturedContext));
        }

        /// <summary>Gets the underlying task, if any.</summary>
        public Task<TResult>? AsTask()
        {
            if (_hasResult)
                return Task.FromResult(_result!);
            return _task;
        }

        /// <summary>Preserves the object so it survives async/await state machine boxing.</summary>
        public ValueTask<TResult> Preserve()
        {
            if (_hasResult)
                return this;
            return new ValueTask<TResult>(AsTask()!);
        }

        public bool Equals(ValueTask<TResult> other) =>
            _hasResult == other._hasResult &&
            _task == other._task &&
            EqualityComparer<TResult>.Default.Equals(_result, other._result);

        public override bool Equals(object? obj) => obj is ValueTask<TResult> vt && Equals(vt);
        public override int GetHashCode() => _hasResult ? _result?.GetHashCode() ?? 0 : _task?.GetHashCode() ?? 0;
        public static bool operator ==(ValueTask<TResult> left, ValueTask<TResult> right) => left.Equals(right);
        public static bool operator !=(ValueTask<TResult> left, ValueTask<TResult> right) => !left.Equals(right);

        internal bool ContinueOnCapturedContext => _continueOnCapturedContext;
        internal bool HasResult => _hasResult;
        internal TResult? InternalResult => _result;
        internal Task<TResult>? InternalTask => _task;
    }
}
