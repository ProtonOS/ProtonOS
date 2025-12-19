// ProtonOS korlib - DateTimeKind enum

namespace System;

/// <summary>
/// Specifies whether a DateTime object represents a local time, a UTC time,
/// or is not specified as either local time or UTC.
/// </summary>
public enum DateTimeKind
{
    /// <summary>
    /// The time represented is not specified as either local time or UTC.
    /// </summary>
    Unspecified = 0,

    /// <summary>
    /// The time represented is UTC.
    /// </summary>
    Utc = 1,

    /// <summary>
    /// The time represented is local time.
    /// </summary>
    Local = 2
}
