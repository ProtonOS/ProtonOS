// ProtonOS korlib - CancellationToken
// Propagates notification that operations should be canceled.

namespace System.Threading
{
    /// <summary>
    /// Propagates notification that operations should be canceled.
    /// </summary>
    public readonly struct CancellationToken
    {
        private readonly CancellationTokenSource? _source;

        /// <summary>Returns an empty CancellationToken value.</summary>
        public static CancellationToken None => default;

        /// <summary>Gets whether cancellation has been requested for this token.</summary>
        public bool IsCancellationRequested => _source?.IsCancellationRequested ?? false;

        /// <summary>Gets whether this token is capable of being in the canceled state.</summary>
        public bool CanBeCanceled => _source != null;

        internal CancellationToken(CancellationTokenSource source)
        {
            _source = source;
        }

        /// <summary>Throws a OperationCanceledException if this token has had cancellation requested.</summary>
        public void ThrowIfCancellationRequested()
        {
            if (IsCancellationRequested)
            {
                throw new OperationCanceledException(this);
            }
        }

        /// <summary>Registers a delegate that will be called when this CancellationToken is canceled.</summary>
        public CancellationTokenRegistration Register(Action callback)
        {
            return Register(callback, false);
        }

        /// <summary>Registers a delegate that will be called when this CancellationToken is canceled.</summary>
        public CancellationTokenRegistration Register(Action callback, bool useSynchronizationContext)
        {
            if (_source == null) return default;
            return _source.InternalRegister(callback);
        }

        /// <summary>Registers a delegate that will be called when this CancellationToken is canceled.</summary>
        public CancellationTokenRegistration Register(Action<object?> callback, object? state)
        {
            return Register(callback, state, false);
        }

        /// <summary>Registers a delegate that will be called when this CancellationToken is canceled.</summary>
        public CancellationTokenRegistration Register(Action<object?> callback, object? state, bool useSynchronizationContext)
        {
            if (_source == null) return default;
            // Use helper class instead of lambda to avoid JIT closure issues
            var wrapper = new StateCallbackWrapper(callback, state);
            return _source.InternalRegister(wrapper.Invoke);
        }

        public override bool Equals(object? other)
        {
            return other is CancellationToken token && Equals(token);
        }

        public bool Equals(CancellationToken other)
        {
            return _source == other._source;
        }

        public override int GetHashCode()
        {
            return _source?.GetHashCode() ?? 0;
        }

        public static bool operator ==(CancellationToken left, CancellationToken right) => left.Equals(right);
        public static bool operator !=(CancellationToken left, CancellationToken right) => !left.Equals(right);
    }

    /// <summary>
    /// Helper class to wrap a callback with state, avoiding compiler-generated closures.
    /// </summary>
    internal sealed class StateCallbackWrapper
    {
        private readonly Action<object?> _callback;
        private readonly object? _state;

        internal StateCallbackWrapper(Action<object?> callback, object? state)
        {
            _callback = callback;
            _state = state;
        }

        internal void Invoke()
        {
            _callback(_state);
        }
    }

    /// <summary>
    /// Represents a callback delegate that has been registered with a CancellationToken.
    /// </summary>
    public readonly struct CancellationTokenRegistration : IDisposable
    {
        private readonly CancellationTokenSource? _source;
        private readonly Action? _callback;

        internal CancellationTokenRegistration(CancellationTokenSource source, Action callback)
        {
            _source = source;
            _callback = callback;
        }

        /// <summary>Disposes of the registration and unregisters the target callback from the associated CancellationToken.</summary>
        public void Dispose()
        {
            _source?.Unregister(_callback);
        }
    }
}
