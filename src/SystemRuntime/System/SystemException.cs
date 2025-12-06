// ProtonOS System.Runtime - SystemException
// Serves as the base class for system exceptions namespace.

namespace System
{
    /// <summary>
    /// Serves as the base class for system exceptions namespace.
    /// </summary>
    public class SystemException : Exception
    {
        /// <summary>Initializes a new instance of the SystemException class.</summary>
        public SystemException() : base("System error.")
        {
        }

        /// <summary>Initializes a new instance of the SystemException class with a specified error message.</summary>
        public SystemException(string? message) : base(message)
        {
        }

        /// <summary>Initializes a new instance of the SystemException class with a specified error message and a reference to the inner exception.</summary>
        public SystemException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    /// <summary>
    /// The exception that is thrown when an attempt to access an object that has been disposed.
    /// </summary>
    public class ObjectDisposedException : InvalidOperationException
    {
        private readonly string? _objectName;

        /// <summary>Gets the name of the disposed object.</summary>
        public string? ObjectName => _objectName;

        /// <summary>Initializes a new instance of the ObjectDisposedException class with a string containing the name of the disposed object.</summary>
        public ObjectDisposedException(string? objectName) : this(objectName, "Cannot access a disposed object.")
        {
        }

        /// <summary>Initializes a new instance of the ObjectDisposedException class with a specified object name and message.</summary>
        public ObjectDisposedException(string? objectName, string? message) : base(message)
        {
            _objectName = objectName;
        }

        /// <summary>Initializes a new instance of the ObjectDisposedException class with a specified error message and a reference to the inner exception.</summary>
        public ObjectDisposedException(string? message, Exception? innerException) : base(message, innerException)
        {
        }

        public override string Message
        {
            get
            {
                string message = base.Message;
                if (!string.IsNullOrEmpty(_objectName))
                {
                    return message + Environment.NewLine + "Object name: '" + _objectName + "'.";
                }
                return message;
            }
        }
    }
}
