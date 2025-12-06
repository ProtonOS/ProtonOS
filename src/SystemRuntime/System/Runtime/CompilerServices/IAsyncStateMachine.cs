// ProtonOS System.Runtime - IAsyncStateMachine
// Represents state machines that are generated for async methods.

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Represents state machines that are generated for async methods.
    /// </summary>
    public interface IAsyncStateMachine
    {
        /// <summary>
        /// Moves the state machine to its next state.
        /// </summary>
        void MoveNext();

        /// <summary>
        /// Configures the state machine with a heap-allocated replica.
        /// </summary>
        void SetStateMachine(IAsyncStateMachine stateMachine);
    }
}
