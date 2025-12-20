// ProtonOS korlib - AsyncMethodBuilder
// Represents a builder for asynchronous methods.

using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Helper class to wrap a state machine for continuation.
    /// Avoids compiler-generated closures which have JIT vtable issues.
    /// </summary>
    internal sealed class StateMachineContinuation
    {
        private readonly IAsyncStateMachine _stateMachine;

        internal StateMachineContinuation(IAsyncStateMachine stateMachine)
        {
            _stateMachine = stateMachine;
        }

        internal void MoveNext()
        {
            _stateMachine.MoveNext();
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that do not return a value.
    /// </summary>
    public struct AsyncTaskMethodBuilder
    {
        private Task? _task;

        /// <summary>Gets the task for this builder.</summary>
        public Task Task => _task ??= new Task();

        /// <summary>Creates an instance of the AsyncTaskMethodBuilder struct.</summary>
        public static AsyncTaskMethodBuilder Create() => default;

        /// <summary>Initiates the builder's execution with the associated state machine.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        /// <summary>Associates the builder with the specified state machine.</summary>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            // Not needed for simple implementation
        }

        /// <summary>Marks the task as successfully completed.</summary>
        public void SetResult()
        {
            if (_task == null)
            {
                _task = System.Threading.Tasks.Task.CompletedTask;
            }
            else
            {
                _task.SetResult();
            }
        }

        /// <summary>Marks the task as failed.</summary>
        public void SetException(Exception exception)
        {
            (_task ??= new Task()).SetException(exception);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that return a value.
    /// </summary>
    public struct AsyncTaskMethodBuilder<TResult>
    {
        private Task<TResult>? _task;

        /// <summary>Gets the task for this builder.</summary>
        public Task<TResult> Task => _task ??= new Task<TResult>();

        /// <summary>Creates an instance of the AsyncTaskMethodBuilder struct.</summary>
        public static AsyncTaskMethodBuilder<TResult> Create() => default;

        /// <summary>Initiates the builder's execution with the associated state machine.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        /// <summary>Associates the builder with the specified state machine.</summary>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            // Not needed for simple implementation
        }

        /// <summary>Marks the task as successfully completed.</summary>
        public void SetResult(TResult result)
        {
            if (_task == null)
            {
                _task = System.Threading.Tasks.Task.FromResult(result);
            }
            else
            {
                _task.SetResult(result);
            }
        }

        /// <summary>Marks the task as failed.</summary>
        public void SetException(Exception exception)
        {
            (_task ??= new Task<TResult>()).SetException(exception);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that return void.
    /// </summary>
    public struct AsyncVoidMethodBuilder
    {
        /// <summary>Creates an instance of the AsyncVoidMethodBuilder struct.</summary>
        public static AsyncVoidMethodBuilder Create() => default;

        /// <summary>Initiates the builder's execution with the associated state machine.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            stateMachine.MoveNext();
        }

        /// <summary>Associates the builder with the specified state machine.</summary>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            // Not needed for simple implementation
        }

        /// <summary>Marks the method as successfully completed.</summary>
        public void SetResult()
        {
            // Nothing to do for async void
        }

        /// <summary>Marks the method as failed.</summary>
        public void SetException(Exception exception)
        {
            // For async void, we should throw on the captured context
            // For now, just throw directly
            throw exception;
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.OnCompleted(continuation.MoveNext);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            IAsyncStateMachine boxedStateMachine = stateMachine;
            var continuation = new StateMachineContinuation(boxedStateMachine);
            awaiter.UnsafeOnCompleted(continuation.MoveNext);
        }
    }
}
