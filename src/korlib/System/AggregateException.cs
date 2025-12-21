// ProtonOS korlib - AggregateException
// Represents one or more errors that occur during application execution.

using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace System
{
    /// <summary>
    /// Represents one or more errors that occur during application execution.
    /// </summary>
    public class AggregateException : Exception
    {
        private readonly ReadOnlyCollection<Exception> _innerExceptions;

        /// <summary>Gets a read-only collection of the Exception instances that caused the current exception.</summary>
        public ReadOnlyCollection<Exception> InnerExceptions => _innerExceptions;

        /// <summary>Initializes a new instance of the AggregateException class with a system-supplied message.</summary>
        public AggregateException() : this("One or more errors occurred.")
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message.</summary>
        public AggregateException(string? message) : base(message)
        {
            var emptyList = new List<Exception>();
            _innerExceptions = new ReadOnlyCollection<Exception>(emptyList);
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message and inner exception.</summary>
        public AggregateException(string? message, Exception innerException) : base(message, innerException)
        {
            var list = new List<Exception>();
            list.Add(innerException);
            _innerExceptions = new ReadOnlyCollection<Exception>(list);
        }

        /// <summary>Initializes a new instance of the AggregateException class with references to the inner exceptions.</summary>
        public AggregateException(params Exception[] innerExceptions) : this("One or more errors occurred.", innerExceptions)
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message and references to the inner exceptions.</summary>
        public AggregateException(string? message, params Exception[] innerExceptions) : base(message, innerExceptions.Length > 0 ? innerExceptions[0] : null)
        {
            if (innerExceptions == null) throw new ArgumentNullException(nameof(innerExceptions));
            var list = new List<Exception>();
            for (int i = 0; i < innerExceptions.Length; i++)
            {
                list.Add(innerExceptions[i]);
            }
            _innerExceptions = new ReadOnlyCollection<Exception>(list);
        }

        /// <summary>Initializes a new instance of the AggregateException class with a List of inner exceptions.</summary>
        public AggregateException(string? message, List<Exception> innerExceptions) : base(message, innerExceptions.Count > 0 ? innerExceptions[0] : null)
        {
            if (innerExceptions == null) throw new ArgumentNullException(nameof(innerExceptions));
            _innerExceptions = new ReadOnlyCollection<Exception>(innerExceptions);
        }

        /// <summary>Initializes a new instance of the AggregateException class with an enumerable of inner exceptions.</summary>
        public AggregateException(IEnumerable<Exception> innerExceptions) : this("One or more errors occurred.", innerExceptions)
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with a List of inner exceptions.</summary>
        public AggregateException(List<Exception> innerExceptions) : this("One or more errors occurred.", innerExceptions)
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with an enumerable of inner exceptions.</summary>
        public AggregateException(string? message, IEnumerable<Exception> innerExceptions) : base(message)
        {
            if (innerExceptions == null) throw new ArgumentNullException(nameof(innerExceptions));
            var list = new List<Exception>();
            foreach (var ex in innerExceptions)
            {
                list.Add(ex);
            }
            _innerExceptions = new ReadOnlyCollection<Exception>(list);
        }

        /// <summary>Flattens an AggregateException instances into a single, new instance.</summary>
        public AggregateException Flatten()
        {
            var flattenedExceptions = new List<Exception>();
            var exceptionsToFlatten = new List<AggregateException>();
            exceptionsToFlatten.Add(this);

            while (exceptionsToFlatten.Count > 0)
            {
                int lastIndex = exceptionsToFlatten.Count - 1;
                var current = exceptionsToFlatten[lastIndex];
                exceptionsToFlatten.RemoveAt(lastIndex);

                for (int i = 0; i < current._innerExceptions.Count; i++)
                {
                    var inner = current._innerExceptions[i];
                    if (inner is AggregateException aggregate)
                    {
                        exceptionsToFlatten.Add(aggregate);
                    }
                    else
                    {
                        flattenedExceptions.Add(inner);
                    }
                }
            }

            return new AggregateException("One or more errors occurred.", flattenedExceptions);
        }

        /// <summary>Invokes a handler on each Exception contained by this AggregateException.</summary>
        public void Handle(Func<Exception, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            List<Exception>? unhandled = null;
            for (int i = 0; i < _innerExceptions.Count; i++)
            {
                var inner = _innerExceptions[i];
                if (!predicate(inner))
                {
                    if (unhandled == null) unhandled = new List<Exception>();
                    unhandled.Add(inner);
                }
            }

            if (unhandled != null && unhandled.Count > 0)
            {
                throw new AggregateException("One or more errors occurred.", unhandled);
            }
        }

        /// <summary>Returns the root cause of this exception.</summary>
        public override Exception GetBaseException()
        {
            Exception? back = this;
            Exception? backGrandparent = back;

            while (back.InnerException != null)
            {
                back = back.InnerException;
                if (back.InnerException != null)
                {
                    backGrandparent = back;
                }
            }

            // If we have an AggregateException with a single inner exception, drill down
            if (back is AggregateException agg && agg._innerExceptions.Count == 1)
            {
                return agg._innerExceptions[0].GetBaseException();
            }

            return back;
        }

        /// <summary>Creates and returns a string representation of the current exception.</summary>
        public override string ToString()
        {
            // Note: Full ToString with inner exceptions requires interface dispatch in ReadOnlyCollection
            // which needs RhpInitialDynamicInterfaceDispatch support. For now, use base implementation.
            // TODO: Implement proper AOT interface dispatch support
            return base.ToString() ?? "AggregateException";
        }
    }
}
