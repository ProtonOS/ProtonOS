// ProtonOS korlib - Exception
// Exception class hierarchy for managed exception support.

namespace System;

/// <summary>
/// Base class for all exceptions.
/// </summary>
public class Exception
{
    private string? _message;
    private Exception? _innerException;

    public virtual string? Message => _message;
    public Exception? InnerException => _innerException;

    public Exception()
    {
        _message = null;
        _innerException = null;
    }

    public Exception(string? message)
    {
        _message = message;
        _innerException = null;
    }

    public Exception(string? message, Exception? innerException)
    {
        _message = message;
        _innerException = innerException;
    }
}

/// <summary>
/// Exception thrown when a method receives an invalid argument.
/// </summary>
public class ArgumentException : Exception
{
    public ArgumentException() : base() { }
    public ArgumentException(string? message) : base(message) { }
    public ArgumentException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a null argument is passed to a method that doesn't accept it.
/// </summary>
public class ArgumentNullException : ArgumentException
{
    public ArgumentNullException() : base() { }
    public ArgumentNullException(string? paramName) : base(paramName) { }
    public ArgumentNullException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an argument is outside the allowable range of values.
/// </summary>
public class ArgumentOutOfRangeException : ArgumentException
{
    public ArgumentOutOfRangeException() : base() { }
    public ArgumentOutOfRangeException(string? paramName) : base(paramName) { }
    public ArgumentOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a method call is invalid for the object's current state.
/// </summary>
public class InvalidOperationException : Exception
{
    public InvalidOperationException() : base() { }
    public InvalidOperationException(string? message) : base(message) { }
    public InvalidOperationException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a requested method or operation is not supported.
/// </summary>
public class NotSupportedException : Exception
{
    public NotSupportedException() : base() { }
    public NotSupportedException(string? message) : base(message) { }
    public NotSupportedException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a requested method or operation is not implemented.
/// </summary>
public class NotImplementedException : Exception
{
    public NotImplementedException() : base() { }
    public NotImplementedException(string? message) : base(message) { }
    public NotImplementedException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an array index is out of bounds.
/// </summary>
public class IndexOutOfRangeException : Exception
{
    public IndexOutOfRangeException() : base() { }
    public IndexOutOfRangeException(string? message) : base(message) { }
    public IndexOutOfRangeException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when attempting to dereference a null object reference.
/// </summary>
public class NullReferenceException : Exception
{
    public NullReferenceException() : base() { }
    public NullReferenceException(string? message) : base(message) { }
    public NullReferenceException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when there is not enough memory to continue execution.
/// </summary>
public class OutOfMemoryException : Exception
{
    public OutOfMemoryException() : base() { }
    public OutOfMemoryException(string? message) : base(message) { }
    public OutOfMemoryException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown for errors in arithmetic, casting, or conversion operations.
/// </summary>
public class ArithmeticException : Exception
{
    public ArithmeticException() : base() { }
    public ArithmeticException(string? message) : base(message) { }
    public ArithmeticException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an arithmetic operation results in an overflow.
/// </summary>
public class OverflowException : ArithmeticException
{
    public OverflowException() : base() { }
    public OverflowException(string? message) : base(message) { }
    public OverflowException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when an attempt is made to divide by zero.
/// </summary>
public class DivideByZeroException : ArithmeticException
{
    public DivideByZeroException() : base() { }
    public DivideByZeroException(string? message) : base(message) { }
    public DivideByZeroException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown for platform-specific feature failures.
/// </summary>
public class PlatformNotSupportedException : NotSupportedException
{
    public PlatformNotSupportedException() : base() { }
    public PlatformNotSupportedException(string? message) : base(message) { }
    public PlatformNotSupportedException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when a type cannot be cast to another type.
/// </summary>
public class InvalidCastException : Exception
{
    public InvalidCastException() : base() { }
    public InvalidCastException(string? message) : base(message) { }
    public InvalidCastException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when array element types don't match.
/// </summary>
public class ArrayTypeMismatchException : Exception
{
    public ArrayTypeMismatchException() : base() { }
    public ArrayTypeMismatchException(string? message) : base(message) { }
    public ArrayTypeMismatchException(string? message, Exception? innerException) : base(message, innerException) { }
}

/// <summary>
/// Exception thrown when the execution stack overflows.
/// </summary>
public class StackOverflowException : Exception
{
    public StackOverflowException() : base() { }
    public StackOverflowException(string? message) : base(message) { }
    public StackOverflowException(string? message, Exception? innerException) : base(message, innerException) { }
}
