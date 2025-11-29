// netos netlib - Exception
// Minimal stub for Exception type required by various APIs.

namespace System;

public class Exception
{
    public virtual string? Message => null;

    public Exception() { }
    public Exception(string? message) { }
    public Exception(string? message, Exception? innerException) { }
}
