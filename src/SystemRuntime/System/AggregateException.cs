// ProtonOS System.Runtime - AggregateException
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
            _innerExceptions = new ReadOnlyCollection<Exception>(Array.Empty<Exception>());
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message and inner exception.</summary>
        public AggregateException(string? message, Exception innerException) : base(message, innerException)
        {
            _innerExceptions = new ReadOnlyCollection<Exception>(new Exception[] { innerException });
        }

        /// <summary>Initializes a new instance of the AggregateException class with references to the inner exceptions.</summary>
        public AggregateException(IEnumerable<Exception> innerExceptions) : this("One or more errors occurred.", innerExceptions)
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with references to the inner exceptions.</summary>
        public AggregateException(params Exception[] innerExceptions) : this("One or more errors occurred.", (IEnumerable<Exception>)innerExceptions)
        {
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message and references to the inner exceptions.</summary>
        public AggregateException(string? message, IEnumerable<Exception> innerExceptions) : base(message, GetFirstOrNull(innerExceptions))
        {
            if (innerExceptions == null) throw new ArgumentNullException(nameof(innerExceptions));
            var list = new List<Exception>(innerExceptions);
            _innerExceptions = new ReadOnlyCollection<Exception>(list);
        }

        /// <summary>Initializes a new instance of the AggregateException class with a specified message and references to the inner exceptions.</summary>
        public AggregateException(string? message, params Exception[] innerExceptions) : this(message, (IEnumerable<Exception>)innerExceptions)
        {
        }

        private static Exception? GetFirstOrNull(IEnumerable<Exception>? exceptions)
        {
            if (exceptions == null) return null;
            using var enumerator = exceptions.GetEnumerator();
            return enumerator.MoveNext() ? enumerator.Current : null;
        }

        /// <summary>Flattens an AggregateException instances into a single, new instance.</summary>
        public AggregateException Flatten()
        {
            var flattenedExceptions = new List<Exception>();
            var exceptionsToFlatten = new List<AggregateException> { this };

            while (exceptionsToFlatten.Count > 0)
            {
                var current = exceptionsToFlatten[exceptionsToFlatten.Count - 1];
                exceptionsToFlatten.RemoveAt(exceptionsToFlatten.Count - 1);

                foreach (var inner in current._innerExceptions)
                {
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

            return new AggregateException(Message, flattenedExceptions);
        }

        /// <summary>Invokes a handler on each Exception contained by this AggregateException.</summary>
        public void Handle(Func<Exception, bool> predicate)
        {
            if (predicate == null) throw new ArgumentNullException(nameof(predicate));

            List<Exception>? unhandled = null;
            foreach (var inner in _innerExceptions)
            {
                if (!predicate(inner))
                {
                    unhandled ??= new List<Exception>();
                    unhandled.Add(inner);
                }
            }

            if (unhandled != null && unhandled.Count > 0)
            {
                throw new AggregateException(Message, unhandled);
            }
        }

        /// <summary>Returns the AggregateException root cause.</summary>
        public override Exception GetBaseException()
        {
            Exception? back = this;
            AggregateException? backAsAggregate = this;
            while (backAsAggregate != null && backAsAggregate._innerExceptions.Count == 1)
            {
                back = back!.InnerException;
                backAsAggregate = back as AggregateException;
            }
            return back!;
        }

        public override string ToString()
        {
            string text = base.ToString();
            for (int i = 0; i < _innerExceptions.Count; i++)
            {
                text = text + Environment.NewLine + "--->" + _innerExceptions[i].ToString() + "<---";
            }
            return text;
        }
    }
}
