// ProtonOS System.Runtime - AsyncMethodBuilder
// Represents a builder for asynchronous methods that do not return a value.

using System.Collections.Generic;
using System.Threading.Tasks;

namespace System.Runtime.CompilerServices
{
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
            awaiter.OnCompleted(() => boxedStateMachine.MoveNext());
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            awaiter.UnsafeOnCompleted(() => boxedStateMachine.MoveNext());
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
            awaiter.OnCompleted(() => boxedStateMachine.MoveNext());
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            // Box the state machine to capture it
            IAsyncStateMachine boxedStateMachine = stateMachine;
            awaiter.UnsafeOnCompleted(() => boxedStateMachine.MoveNext());
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
            awaiter.OnCompleted(() => boxedStateMachine.MoveNext());
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            IAsyncStateMachine boxedStateMachine = stateMachine;
            awaiter.UnsafeOnCompleted(() => boxedStateMachine.MoveNext());
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that return a ValueTask.
    /// </summary>
    public struct AsyncValueTaskMethodBuilder
    {
        private AsyncTaskMethodBuilder _methodBuilder;
        private bool _haveResult;

        /// <summary>Gets the ValueTask for this builder.</summary>
        public ValueTask Task
        {
            get
            {
                if (_haveResult)
                    return default;
                return new ValueTask(_methodBuilder.Task);
            }
        }

        /// <summary>Creates an instance of the AsyncValueTaskMethodBuilder struct.</summary>
        public static AsyncValueTaskMethodBuilder Create() => default;

        /// <summary>Initiates the builder's execution with the associated state machine.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.Start(ref stateMachine);
        }

        /// <summary>Associates the builder with the specified state machine.</summary>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _methodBuilder.SetStateMachine(stateMachine);
        }

        /// <summary>Marks the task as successfully completed.</summary>
        public void SetResult()
        {
            if (_methodBuilder.Task == System.Threading.Tasks.Task.CompletedTask)
            {
                _haveResult = true;
            }
            else
            {
                _methodBuilder.SetResult();
            }
        }

        /// <summary>Marks the task as failed.</summary>
        public void SetException(Exception exception)
        {
            _methodBuilder.SetException(exception);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that return a ValueTask{TResult}.
    /// </summary>
    public struct AsyncValueTaskMethodBuilder<TResult>
    {
        private AsyncTaskMethodBuilder<TResult> _methodBuilder;
        private TResult? _result;
        private bool _haveResult;

        /// <summary>Gets the ValueTask for this builder.</summary>
        public ValueTask<TResult> Task
        {
            get
            {
                if (_haveResult)
                    return new ValueTask<TResult>(_result!);
                return new ValueTask<TResult>(_methodBuilder.Task);
            }
        }

        /// <summary>Creates an instance of the AsyncValueTaskMethodBuilder struct.</summary>
        public static AsyncValueTaskMethodBuilder<TResult> Create() => default;

        /// <summary>Initiates the builder's execution with the associated state machine.</summary>
        public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.Start(ref stateMachine);
        }

        /// <summary>Associates the builder with the specified state machine.</summary>
        public void SetStateMachine(IAsyncStateMachine stateMachine)
        {
            _methodBuilder.SetStateMachine(stateMachine);
        }

        /// <summary>Marks the task as successfully completed.</summary>
        public void SetResult(TResult result)
        {
            _result = result;
            _haveResult = true;
        }

        /// <summary>Marks the task as failed.</summary>
        public void SetException(Exception exception)
        {
            _methodBuilder.SetException(exception);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>Schedules the state machine to proceed to the next action when the specified awaiter completes.</summary>
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            _methodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }
}
