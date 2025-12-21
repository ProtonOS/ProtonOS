// ProtonOS korlib - Half
// Represents a half-precision floating-point number.

namespace System;

/// <summary>
/// Represents a half-precision floating-point number (IEEE 754 binary16).
/// </summary>
public readonly struct Half : IEquatable<Half>, IComparable<Half>, IComparable
{
    private readonly ushort _value;

    /// <summary>
    /// Represents the smallest positive Half value that is greater than zero.
    /// </summary>
    public static Half Epsilon => new Half(0x0001);

    /// <summary>
    /// Represents the largest possible value of Half.
    /// </summary>
    public static Half MaxValue => new Half(0x7BFF);

    /// <summary>
    /// Represents the smallest possible value of Half.
    /// </summary>
    public static Half MinValue => new Half(0xFBFF);

    /// <summary>
    /// Represents not a number (NaN).
    /// </summary>
    public static Half NaN => new Half(0xFE00);

    /// <summary>
    /// Represents negative infinity.
    /// </summary>
    public static Half NegativeInfinity => new Half(0xFC00);

    /// <summary>
    /// Represents positive infinity.
    /// </summary>
    public static Half PositiveInfinity => new Half(0x7C00);

    /// <summary>
    /// Represents the number zero.
    /// </summary>
    public static Half Zero => new Half(0);

    private Half(ushort value)
    {
        _value = value;
    }

    /// <summary>
    /// Creates a Half from its raw bit representation.
    /// </summary>
    internal static Half FromBits(ushort bits) => new Half(bits);

    /// <summary>
    /// Gets the raw bit representation of this Half.
    /// </summary>
    internal ushort GetBits() => _value;

    /// <summary>
    /// Determines whether the specified value is finite (not NaN or infinity).
    /// </summary>
    public static bool IsFinite(Half value)
    {
        return (value._value & 0x7C00) != 0x7C00;
    }

    /// <summary>
    /// Determines whether the specified value is infinity.
    /// </summary>
    public static bool IsInfinity(Half value)
    {
        return (value._value & 0x7FFF) == 0x7C00;
    }

    /// <summary>
    /// Determines whether the specified value is NaN.
    /// </summary>
    public static bool IsNaN(Half value)
    {
        return (value._value & 0x7C00) == 0x7C00 && (value._value & 0x03FF) != 0;
    }

    /// <summary>
    /// Determines whether the specified value is negative.
    /// </summary>
    public static bool IsNegative(Half value)
    {
        return (value._value & 0x8000) != 0;
    }

    /// <summary>
    /// Determines whether the specified value is negative infinity.
    /// </summary>
    public static bool IsNegativeInfinity(Half value)
    {
        return value._value == 0xFC00;
    }

    /// <summary>
    /// Determines whether the specified value is positive infinity.
    /// </summary>
    public static bool IsPositiveInfinity(Half value)
    {
        return value._value == 0x7C00;
    }

    /// <summary>
    /// Converts this Half to a single-precision float.
    /// </summary>
    public static explicit operator float(Half value)
    {
        // Extract components
        uint sign = (uint)(value._value >> 15) & 1;
        uint exp = (uint)(value._value >> 10) & 0x1F;
        uint mantissa = (uint)(value._value & 0x3FF);

        uint result;

        if (exp == 0)
        {
            // Zero or subnormal
            if (mantissa == 0)
            {
                result = sign << 31;
            }
            else
            {
                // Subnormal - normalize it
                exp = 1;
                while ((mantissa & 0x400) == 0)
                {
                    mantissa <<= 1;
                    exp--;
                }
                mantissa &= 0x3FF;
                exp = 127 - 15 + exp;
                result = (sign << 31) | (exp << 23) | (mantissa << 13);
            }
        }
        else if (exp == 0x1F)
        {
            // Infinity or NaN
            result = (sign << 31) | 0x7F800000 | (mantissa << 13);
        }
        else
        {
            // Normal number
            exp = exp - 15 + 127;
            result = (sign << 31) | (exp << 23) | (mantissa << 13);
        }

        unsafe
        {
            return *(float*)&result;
        }
    }

    /// <summary>
    /// Converts a single-precision float to Half.
    /// </summary>
    public static explicit operator Half(float value)
    {
        uint floatBits;
        unsafe
        {
            floatBits = *(uint*)&value;
        }

        uint sign = (floatBits >> 16) & 0x8000;
        int exp = (int)((floatBits >> 23) & 0xFF) - 127 + 15;
        uint mantissa = floatBits & 0x7FFFFF;

        ushort result;

        if (exp <= 0)
        {
            // Underflow to zero or subnormal
            if (exp < -10)
            {
                result = (ushort)sign;
            }
            else
            {
                // Subnormal
                mantissa |= 0x800000;
                int shift = 14 - exp;
                mantissa >>= shift;
                result = (ushort)(sign | mantissa);
            }
        }
        else if (exp >= 0x1F)
        {
            // Overflow to infinity
            result = (ushort)(sign | 0x7C00);
        }
        else
        {
            // Normal number
            result = (ushort)(sign | (exp << 10) | (mantissa >> 13));
        }

        return new Half(result);
    }

    public bool Equals(Half other) => _value == other._value;
    public override bool Equals(object? obj) => obj is Half h && Equals(h);
    public override int GetHashCode() => _value;

    public int CompareTo(Half other)
    {
        if (IsNaN(this))
            return IsNaN(other) ? 0 : -1;
        if (IsNaN(other))
            return 1;

        // Handle sign
        bool thisNeg = IsNegative(this);
        bool otherNeg = IsNegative(other);

        if (thisNeg != otherNeg)
            return thisNeg ? -1 : 1;

        // Both same sign - compare magnitude
        // Use direct comparison instead of ushort.CompareTo which isn't in AOT registry
        int cmp;
        if (_value < other._value)
            cmp = -1;
        else if (_value > other._value)
            cmp = 1;
        else
            cmp = 0;
        return thisNeg ? -cmp : cmp;
    }

    public int CompareTo(object? obj)
    {
        if (obj is Half h)
            return CompareTo(h);
        throw new ArgumentException("Object must be of type Half");
    }

    public static bool operator ==(Half left, Half right) => left._value == right._value;
    public static bool operator !=(Half left, Half right) => left._value != right._value;
    public static bool operator <(Half left, Half right) => left.CompareTo(right) < 0;
    public static bool operator >(Half left, Half right) => left.CompareTo(right) > 0;
    public static bool operator <=(Half left, Half right) => left.CompareTo(right) <= 0;
    public static bool operator >=(Half left, Half right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        return ((float)this).ToString();
    }
}
