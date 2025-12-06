// ProtonOS System.Runtime - CancellationTokenSource
// Signals to a CancellationToken that it should be canceled.

using System.Collections.Generic;

namespace System.Threading
{
    /// <summary>
    /// Signals to a CancellationToken that it should be canceled.
    /// </summary>
    public class CancellationTokenSource : IDisposable
    {
        private volatile bool _isCancellationRequested;
        private volatile bool _isDisposed;
        private List<Action>? _callbacks;
        private readonly object _lock = new object();

        /// <summary>Gets whether cancellation has been requested for this CancellationTokenSource.</summary>
        public bool IsCancellationRequested => _isCancellationRequested;

        /// <summary>Gets the CancellationToken associated with this CancellationTokenSource.</summary>
        public CancellationToken Token
        {
            get
            {
                ThrowIfDisposed();
                return new CancellationToken(this);
            }
        }

        /// <summary>Communicates a request for cancellation.</summary>
        public void Cancel()
        {
            Cancel(false);
        }

        /// <summary>Communicates a request for cancellation.</summary>
        public void Cancel(bool throwOnFirstException)
        {
            ThrowIfDisposed();
            if (_isCancellationRequested) return;

            _isCancellationRequested = true;

            List<Exception>? exceptions = null;
            List<Action>? callbacksToRun = null;

            lock (_lock)
            {
                if (_callbacks != null)
                {
                    callbacksToRun = new List<Action>(_callbacks);
                }
            }

            if (callbacksToRun != null)
            {
                foreach (var callback in callbacksToRun)
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception ex)
                    {
                        if (throwOnFirstException) throw;
                        exceptions ??= new List<Exception>();
                        exceptions.Add(ex);
                    }
                }
            }

            if (exceptions != null && exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }
        }

        /// <summary>Schedules a Cancel operation on this CancellationTokenSource.</summary>
        public void CancelAfter(int millisecondsDelay)
        {
            // In a full implementation, this would use a timer
            // For now, just validate the parameter
            if (millisecondsDelay < -1) throw new ArgumentOutOfRangeException(nameof(millisecondsDelay));
            ThrowIfDisposed();
        }

        /// <summary>Schedules a Cancel operation on this CancellationTokenSource.</summary>
        public void CancelAfter(TimeSpan delay)
        {
            CancelAfter((int)delay.TotalMilliseconds);
        }

        internal CancellationTokenRegistration InternalRegister(Action callback)
        {
            ThrowIfDisposed();

            if (_isCancellationRequested)
            {
                callback();
                return default;
            }

            lock (_lock)
            {
                if (_isCancellationRequested)
                {
                    callback();
                    return default;
                }

                _callbacks ??= new List<Action>();
                _callbacks.Add(callback);
            }

            return new CancellationTokenRegistration(this, callback);
        }

        internal void Unregister(Action? callback)
        {
            if (callback == null || _isDisposed) return;

            lock (_lock)
            {
                _callbacks?.Remove(callback);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed) throw new ObjectDisposedException(nameof(CancellationTokenSource));
        }

        /// <summary>Releases the resources used by this CancellationTokenSource.</summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            if (disposing)
            {
                lock (_lock)
                {
                    _callbacks?.Clear();
                    _callbacks = null;
                }
            }
        }

        /// <summary>Creates a CancellationTokenSource that will be in the canceled state when any of the source tokens are in the canceled state.</summary>
        public static CancellationTokenSource CreateLinkedTokenSource(CancellationToken token1, CancellationToken token2)
        {
            var cts = new CancellationTokenSource();

            if (token1.CanBeCanceled)
            {
                token1.Register(() => cts.Cancel());
            }
            if (token2.CanBeCanceled)
            {
                token2.Register(() => cts.Cancel());
            }

            return cts;
        }

        /// <summary>Creates a CancellationTokenSource that will be in the canceled state when any of the source tokens are in the canceled state.</summary>
        public static CancellationTokenSource CreateLinkedTokenSource(params CancellationToken[] tokens)
        {
            if (tokens == null) throw new ArgumentNullException(nameof(tokens));

            var cts = new CancellationTokenSource();

            foreach (var token in tokens)
            {
                if (token.CanBeCanceled)
                {
                    token.Register(() => cts.Cancel());
                }
            }

            return cts;
        }
    }
}
