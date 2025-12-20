// ProtonOS korlib - SystemException
// Base class for system exceptions.

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
}
