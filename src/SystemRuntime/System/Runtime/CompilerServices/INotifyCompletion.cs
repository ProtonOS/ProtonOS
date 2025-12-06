// ProtonOS System.Runtime - INotifyCompletion
// Represents an operation that schedules continuations when it completes.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Represents an operation that schedules continuations when it completes.
    /// </summary>
    public interface INotifyCompletion
    {
        /// <summary>
        /// Schedules the continuation action that's invoked when the instance completes.
        /// </summary>
        void OnCompleted(Action continuation);
    }

    /// <summary>
    /// Represents an awaiter that schedules continuations when an await operation completes.
    /// </summary>
    public interface ICriticalNotifyCompletion : INotifyCompletion
    {
        /// <summary>
        /// Schedules the continuation action that's invoked when the instance completes.
        /// </summary>
        void UnsafeOnCompleted(Action continuation);
    }
}
