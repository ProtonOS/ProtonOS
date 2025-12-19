// ProtonOS korlib - TimeSpan
// Represents a time interval.

namespace System;

/// <summary>
/// Represents a time interval.
/// </summary>
public readonly struct TimeSpan : IEquatable<TimeSpan>, IComparable<TimeSpan>, IComparable
{
    /// <summary>The number of ticks per millisecond.</summary>
    public const long TicksPerMillisecond = 10000;
    /// <summary>The number of ticks per second.</summary>
    public const long TicksPerSecond = TicksPerMillisecond * 1000;
    /// <summary>The number of ticks per minute.</summary>
    public const long TicksPerMinute = TicksPerSecond * 60;
    /// <summary>The number of ticks per hour.</summary>
    public const long TicksPerHour = TicksPerMinute * 60;
    /// <summary>The number of ticks per day.</summary>
    public const long TicksPerDay = TicksPerHour * 24;

    /// <summary>Represents the zero TimeSpan value.</summary>
    public static readonly TimeSpan Zero = new TimeSpan(0);
    /// <summary>Represents the minimum TimeSpan value.</summary>
    public static readonly TimeSpan MinValue = new TimeSpan(long.MinValue);
    /// <summary>Represents the maximum TimeSpan value.</summary>
    public static readonly TimeSpan MaxValue = new TimeSpan(long.MaxValue);

    private readonly long _ticks;

    /// <summary>Gets the number of ticks that represent the value of this TimeSpan.</summary>
    public long Ticks => _ticks;

    /// <summary>Gets the days component of this TimeSpan.</summary>
    public int Days => (int)(_ticks / TicksPerDay);

    /// <summary>Gets the hours component of this TimeSpan.</summary>
    public int Hours => (int)((_ticks / TicksPerHour) % 24);

    /// <summary>Gets the minutes component of this TimeSpan.</summary>
    public int Minutes => (int)((_ticks / TicksPerMinute) % 60);

    /// <summary>Gets the seconds component of this TimeSpan.</summary>
    public int Seconds => (int)((_ticks / TicksPerSecond) % 60);

    /// <summary>Gets the milliseconds component of this TimeSpan.</summary>
    public int Milliseconds => (int)((_ticks / TicksPerMillisecond) % 1000);

    /// <summary>Gets the value of this TimeSpan expressed in whole and fractional days.</summary>
    public double TotalDays => (double)_ticks / TicksPerDay;

    /// <summary>Gets the value of this TimeSpan expressed in whole and fractional hours.</summary>
    public double TotalHours => (double)_ticks / TicksPerHour;

    /// <summary>Gets the value of this TimeSpan expressed in whole and fractional minutes.</summary>
    public double TotalMinutes => (double)_ticks / TicksPerMinute;

    /// <summary>Gets the value of this TimeSpan expressed in whole and fractional seconds.</summary>
    public double TotalSeconds => (double)_ticks / TicksPerSecond;

    /// <summary>Gets the value of this TimeSpan expressed in whole and fractional milliseconds.</summary>
    public double TotalMilliseconds => (double)_ticks / TicksPerMillisecond;

    /// <summary>Initializes a new TimeSpan to a specified number of ticks.</summary>
    public TimeSpan(long ticks) => _ticks = ticks;

    /// <summary>Initializes a new TimeSpan to a specified number of hours, minutes, and seconds.</summary>
    public TimeSpan(int hours, int minutes, int seconds)
    {
        _ticks = hours * TicksPerHour + minutes * TicksPerMinute + seconds * TicksPerSecond;
    }

    /// <summary>Initializes a new TimeSpan to a specified number of days, hours, minutes, and seconds.</summary>
    public TimeSpan(int days, int hours, int minutes, int seconds)
    {
        _ticks = days * TicksPerDay +
                 hours * TicksPerHour +
                 minutes * TicksPerMinute +
                 seconds * TicksPerSecond;
    }

    /// <summary>Initializes a new TimeSpan to a specified number of days, hours, minutes, seconds, and milliseconds.</summary>
    public TimeSpan(int days, int hours, int minutes, int seconds, int milliseconds)
    {
        _ticks = days * TicksPerDay +
                 hours * TicksPerHour +
                 minutes * TicksPerMinute +
                 seconds * TicksPerSecond +
                 milliseconds * TicksPerMillisecond;
    }

    // Factory methods
    public static TimeSpan FromDays(double value) => new TimeSpan((long)(value * TicksPerDay));
    public static TimeSpan FromHours(double value) => new TimeSpan((long)(value * TicksPerHour));
    public static TimeSpan FromMinutes(double value) => new TimeSpan((long)(value * TicksPerMinute));
    public static TimeSpan FromSeconds(double value) => new TimeSpan((long)(value * TicksPerSecond));
    public static TimeSpan FromMilliseconds(double value) => new TimeSpan((long)(value * TicksPerMillisecond));
    public static TimeSpan FromTicks(long value) => new TimeSpan(value);

    // Arithmetic
    public TimeSpan Add(TimeSpan ts) => new TimeSpan(_ticks + ts._ticks);
    public TimeSpan Subtract(TimeSpan ts) => new TimeSpan(_ticks - ts._ticks);
    public TimeSpan Negate() => new TimeSpan(-_ticks);
    public TimeSpan Duration() => new TimeSpan(_ticks >= 0 ? _ticks : -_ticks);

    // Operators
    public static TimeSpan operator +(TimeSpan t) => t;
    public static TimeSpan operator -(TimeSpan t) => new TimeSpan(-t._ticks);
    public static TimeSpan operator +(TimeSpan t1, TimeSpan t2) => new TimeSpan(t1._ticks + t2._ticks);
    public static TimeSpan operator -(TimeSpan t1, TimeSpan t2) => new TimeSpan(t1._ticks - t2._ticks);
    public static TimeSpan operator *(TimeSpan t, double factor) => new TimeSpan((long)(t._ticks * factor));
    public static TimeSpan operator *(double factor, TimeSpan t) => new TimeSpan((long)(t._ticks * factor));
    public static TimeSpan operator /(TimeSpan t, double divisor) => new TimeSpan((long)(t._ticks / divisor));
    public static double operator /(TimeSpan t1, TimeSpan t2) => (double)t1._ticks / t2._ticks;

    public static bool operator ==(TimeSpan t1, TimeSpan t2) => t1._ticks == t2._ticks;
    public static bool operator !=(TimeSpan t1, TimeSpan t2) => t1._ticks != t2._ticks;
    public static bool operator <(TimeSpan t1, TimeSpan t2) => t1._ticks < t2._ticks;
    public static bool operator <=(TimeSpan t1, TimeSpan t2) => t1._ticks <= t2._ticks;
    public static bool operator >(TimeSpan t1, TimeSpan t2) => t1._ticks > t2._ticks;
    public static bool operator >=(TimeSpan t1, TimeSpan t2) => t1._ticks >= t2._ticks;

    // Comparison
    public int CompareTo(TimeSpan other) => _ticks.CompareTo(other._ticks);

    public int CompareTo(object? obj)
    {
        if (obj == null) return 1;
        if (obj is not TimeSpan ts) throw new ArgumentException("Object must be TimeSpan");
        return CompareTo(ts);
    }

    public static int Compare(TimeSpan t1, TimeSpan t2) => t1._ticks.CompareTo(t2._ticks);

    // Equality
    public bool Equals(TimeSpan other) => _ticks == other._ticks;
    public override bool Equals(object? obj) => obj is TimeSpan ts && _ticks == ts._ticks;
    public override int GetHashCode() => _ticks.GetHashCode();

    public override string ToString()
    {
        long ticks = _ticks >= 0 ? _ticks : -_ticks;
        bool negative = _ticks < 0;

        int days = (int)(ticks / TicksPerDay);
        int hours = (int)((ticks / TicksPerHour) % 24);
        int minutes = (int)((ticks / TicksPerMinute) % 60);
        int seconds = (int)((ticks / TicksPerSecond) % 60);

        if (days > 0)
        {
            return (negative ? "-" : "") + days.ToString() + "." +
                   hours.ToString().PadLeft(2, '0') + ":" +
                   minutes.ToString().PadLeft(2, '0') + ":" +
                   seconds.ToString().PadLeft(2, '0');
        }
        else
        {
            return (negative ? "-" : "") +
                   hours.ToString().PadLeft(2, '0') + ":" +
                   minutes.ToString().PadLeft(2, '0') + ":" +
                   seconds.ToString().PadLeft(2, '0');
        }
    }
}
