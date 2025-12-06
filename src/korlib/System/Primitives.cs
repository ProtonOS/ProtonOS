// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
// Enhanced for ProtonOS with interface implementations
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
//
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

namespace System
{
    public struct Void { }

    // The layout of primitive types is special cased because it would be recursive.
    // These really don't need any fields to work - the compiler/runtime handles them specially.

    public struct Boolean : IEquatable<bool>, IComparable<bool>, IComparable
    {
        public static readonly string TrueString = "True";
        public static readonly string FalseString = "False";

        // Note: The runtime injects the actual value, no field needed

        public bool Equals(bool other) => this == other;

        public override bool Equals(object? obj) => obj is bool b && this == b;

        public override int GetHashCode() => this ? 1 : 0;

        public int CompareTo(bool other)
        {
            if (this == other) return 0;
            return this ? 1 : -1;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not bool b) throw new ArgumentException("Object must be Boolean");
            return CompareTo(b);
        }

        public override string ToString() => this ? TrueString : FalseString;

        public static bool Parse(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value == TrueString || value == "true") return true;
            if (value == FalseString || value == "false") return false;
            throw new FormatException("String was not recognized as a valid Boolean.");
        }

        public static bool TryParse(string? value, out bool result)
        {
            if (value == TrueString || value == "true")
            {
                result = true;
                return true;
            }
            if (value == FalseString || value == "false")
            {
                result = false;
                return true;
            }
            result = false;
            return false;
        }
    }

    public struct Char : IEquatable<char>, IComparable<char>, IComparable
    {
        public const char MaxValue = (char)0xFFFF;
        public const char MinValue = (char)0;

        public bool Equals(char other) => this == other;

        public override bool Equals(object? obj) => obj is char c && this == c;

        public override int GetHashCode() => (int)this;

        public int CompareTo(char other) => (int)this - (int)other;

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not char c) throw new ArgumentException("Object must be Char");
            return CompareTo(c);
        }

        public override string ToString() => new string(this, 1);

        public static bool IsDigit(char c) => c >= '0' && c <= '9';

        public static bool IsLetter(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z');

        public static bool IsLetterOrDigit(char c) => IsLetter(c) || IsDigit(c);

        public static bool IsWhiteSpace(char c) =>
            c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v';

        public static bool IsUpper(char c) => c >= 'A' && c <= 'Z';

        public static bool IsLower(char c) => c >= 'a' && c <= 'z';

        public static char ToUpper(char c) => IsLower(c) ? (char)(c - 32) : c;

        public static char ToLower(char c) => IsUpper(c) ? (char)(c + 32) : c;

        public static bool IsControl(char c) => c < 0x20 || (c >= 0x7F && c < 0xA0);

        public static bool IsPunctuation(char c) =>
            (c >= '!' && c <= '/') || (c >= ':' && c <= '@') ||
            (c >= '[' && c <= '`') || (c >= '{' && c <= '~');
    }

    public struct SByte : IEquatable<sbyte>, IComparable<sbyte>, IComparable
    {
        public const sbyte MaxValue = 127;
        public const sbyte MinValue = -128;

        public bool Equals(sbyte other) => this == other;

        public override bool Equals(object? obj) => obj is sbyte s && this == s;

        public override int GetHashCode() => (int)this;

        public int CompareTo(sbyte other) => (int)this - (int)other;

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not sbyte s) throw new ArgumentException("Object must be SByte");
            return CompareTo(s);
        }

        public override string ToString() => Int32.FormatInt32((int)this);
    }

    public struct Byte : IEquatable<byte>, IComparable<byte>, IComparable
    {
        public const byte MaxValue = 255;
        public const byte MinValue = 0;

        public bool Equals(byte other) => this == other;

        public override bool Equals(object? obj) => obj is byte b && this == b;

        public override int GetHashCode() => (int)this;

        public int CompareTo(byte other) => (int)this - (int)other;

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not byte b) throw new ArgumentException("Object must be Byte");
            return CompareTo(b);
        }

        public override string ToString() => Int32.FormatInt32((int)this);
    }

    public struct Int16 : IEquatable<short>, IComparable<short>, IComparable
    {
        public const short MaxValue = 32767;
        public const short MinValue = -32768;

        public bool Equals(short other) => this == other;

        public override bool Equals(object? obj) => obj is short s && this == s;

        public override int GetHashCode() => (int)this;

        public int CompareTo(short other) => (int)this - (int)other;

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not short s) throw new ArgumentException("Object must be Int16");
            return CompareTo(s);
        }

        public override string ToString() => Int32.FormatInt32((int)this);
    }

    public struct UInt16 : IEquatable<ushort>, IComparable<ushort>, IComparable
    {
        public const ushort MaxValue = 65535;
        public const ushort MinValue = 0;

        public bool Equals(ushort other) => this == other;

        public override bool Equals(object? obj) => obj is ushort u && this == u;

        public override int GetHashCode() => (int)this;

        public int CompareTo(ushort other) => (int)this - (int)other;

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not ushort u) throw new ArgumentException("Object must be UInt16");
            return CompareTo(u);
        }

        public override string ToString() => Int32.FormatInt32((int)this);
    }

    public struct Int32 : IEquatable<int>, IComparable<int>, IComparable
    {
        public const int MaxValue = 0x7FFFFFFF;
        public const int MinValue = unchecked((int)0x80000000);

        public bool Equals(int other) => this == other;

        public override bool Equals(object? obj) => obj is int i && this == i;

        public override int GetHashCode() => this;

        public int CompareTo(int other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not int i) throw new ArgumentException("Object must be Int32");
            return CompareTo(i);
        }

        public override string ToString() => FormatInt32(this);

        // Internal helper for formatting integers
        internal static unsafe string FormatInt32(int value)
        {
            if (value == 0) return "0";

            bool negative = value < 0;
            if (negative && value == MinValue)
            {
                // Special case: MinValue cannot be negated
                return "-2147483648";
            }

            if (negative) value = -value;

            // Max int is 2147483647 (10 digits) + sign = 11 chars
            char* buffer = stackalloc char[12];
            int pos = 11;
            buffer[11] = '\0';

            while (value > 0)
            {
                pos--;
                buffer[pos] = (char)('0' + (value % 10));
                value /= 10;
            }

            if (negative)
            {
                pos--;
                buffer[pos] = '-';
            }

            return new string(buffer + pos, 0, 11 - pos);
        }

        public static int Parse(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!TryParse(s, out int result))
                throw new FormatException("Input string was not in a correct format.");
            return result;
        }

        public static bool TryParse(string? s, out int result)
        {
            result = 0;
            if (s == null || s.Length == 0) return false;

            int i = 0;
            bool negative = false;

            if (s[0] == '-')
            {
                negative = true;
                i = 1;
            }
            else if (s[0] == '+')
            {
                i = 1;
            }

            if (i >= s.Length) return false;

            long value = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (c - '0');
                if (value > (negative ? 2147483648L : 2147483647L)) return false;
                i++;
            }

            result = negative ? -(int)value : (int)value;
            return true;
        }
    }

    public struct UInt32 : IEquatable<uint>, IComparable<uint>, IComparable
    {
        public const uint MaxValue = 0xFFFFFFFF;
        public const uint MinValue = 0;

        public bool Equals(uint other) => this == other;

        public override bool Equals(object? obj) => obj is uint u && this == u;

        public override int GetHashCode() => (int)this;

        public int CompareTo(uint other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not uint u) throw new ArgumentException("Object must be UInt32");
            return CompareTo(u);
        }

        public override string ToString() => FormatUInt32(this);

        internal static unsafe string FormatUInt32(uint value)
        {
            if (value == 0) return "0";

            char* buffer = stackalloc char[11];
            int pos = 10;
            buffer[10] = '\0';

            while (value > 0)
            {
                pos--;
                buffer[pos] = (char)('0' + (value % 10));
                value /= 10;
            }

            return new string(buffer + pos, 0, 10 - pos);
        }

        public static uint Parse(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!TryParse(s, out uint result))
                throw new FormatException("Input string was not in a correct format.");
            return result;
        }

        public static bool TryParse(string? s, out uint result)
        {
            result = 0;
            if (s == null || s.Length == 0) return false;

            int i = 0;
            if (s[0] == '+') i = 1;
            if (i >= s.Length) return false;

            ulong value = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;
                value = value * 10 + (uint)(c - '0');
                if (value > MaxValue) return false;
                i++;
            }

            result = (uint)value;
            return true;
        }
    }

    public struct Int64 : IEquatable<long>, IComparable<long>, IComparable
    {
        public const long MaxValue = 0x7FFFFFFFFFFFFFFFL;
        public const long MinValue = unchecked((long)0x8000000000000000L);

        public bool Equals(long other) => this == other;

        public override bool Equals(object? obj) => obj is long l && this == l;

        public override int GetHashCode() => (int)this ^ (int)(this >> 32);

        public int CompareTo(long other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not long l) throw new ArgumentException("Object must be Int64");
            return CompareTo(l);
        }

        public override string ToString() => FormatInt64(this);

        internal static unsafe string FormatInt64(long value)
        {
            if (value == 0) return "0";

            bool negative = value < 0;
            if (negative && value == MinValue)
            {
                return "-9223372036854775808";
            }

            if (negative) value = -value;

            char* buffer = stackalloc char[21];
            int pos = 20;
            buffer[20] = '\0';

            while (value > 0)
            {
                pos--;
                buffer[pos] = (char)('0' + (value % 10));
                value /= 10;
            }

            if (negative)
            {
                pos--;
                buffer[pos] = '-';
            }

            return new string(buffer + pos, 0, 20 - pos);
        }

        public static long Parse(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!TryParse(s, out long result))
                throw new FormatException("Input string was not in a correct format.");
            return result;
        }

        public static bool TryParse(string? s, out long result)
        {
            result = 0;
            if (s == null || s.Length == 0) return false;

            int i = 0;
            bool negative = false;

            if (s[0] == '-')
            {
                negative = true;
                i = 1;
            }
            else if (s[0] == '+')
            {
                i = 1;
            }

            if (i >= s.Length) return false;

            ulong value = 0;
            ulong limit = negative ? 9223372036854775808UL : 9223372036854775807UL;

            while (i < s.Length)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;

                ulong digit = (uint)(c - '0');
                if (value > (limit - digit) / 10) return false;
                value = value * 10 + digit;
                i++;
            }

            if (value > limit) return false;

            result = negative ? -(long)value : (long)value;
            return true;
        }
    }

    public struct UInt64 : IEquatable<ulong>, IComparable<ulong>, IComparable
    {
        public const ulong MaxValue = 0xFFFFFFFFFFFFFFFFUL;
        public const ulong MinValue = 0;

        public bool Equals(ulong other) => this == other;

        public override bool Equals(object? obj) => obj is ulong u && this == u;

        public override int GetHashCode() => (int)this ^ (int)(this >> 32);

        public int CompareTo(ulong other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not ulong u) throw new ArgumentException("Object must be UInt64");
            return CompareTo(u);
        }

        public override string ToString() => FormatUInt64(this);

        internal static unsafe string FormatUInt64(ulong value)
        {
            if (value == 0) return "0";

            char* buffer = stackalloc char[21];
            int pos = 20;
            buffer[20] = '\0';

            while (value > 0)
            {
                pos--;
                buffer[pos] = (char)('0' + (value % 10));
                value /= 10;
            }

            return new string(buffer + pos, 0, 20 - pos);
        }

        public static ulong Parse(string s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            if (!TryParse(s, out ulong result))
                throw new FormatException("Input string was not in a correct format.");
            return result;
        }

        public static bool TryParse(string? s, out ulong result)
        {
            result = 0;
            if (s == null || s.Length == 0) return false;

            int i = 0;
            if (s[0] == '+') i = 1;
            if (i >= s.Length) return false;

            ulong value = 0;

            while (i < s.Length)
            {
                char c = s[i];
                if (c < '0' || c > '9') return false;

                ulong digit = (uint)(c - '0');
                // Check for overflow
                if (value > (MaxValue - digit) / 10) return false;
                value = value * 10 + digit;
                i++;
            }

            result = value;
            return true;
        }
    }

    public struct IntPtr : IEquatable<nint>, IComparable<nint>, IComparable
    {
        public static readonly nint Zero = 0;

        public static unsafe int Size => sizeof(nint);

        public bool Equals(nint other) => this == other;

        public override bool Equals(object? obj) => obj is nint n && this == n;

        public override int GetHashCode() => (int)this;

        public int CompareTo(nint other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not nint n) throw new ArgumentException("Object must be IntPtr");
            return CompareTo(n);
        }

        public override string ToString()
        {
            if (Size == 8)
                return Int64.FormatInt64((long)this);
            else
                return Int32.FormatInt32((int)this);
        }

        public unsafe void* ToPointer() => (void*)this;

        public int ToInt32() => (int)this;

        public long ToInt64() => (long)this;
    }

    public struct UIntPtr : IEquatable<nuint>, IComparable<nuint>, IComparable
    {
        public static readonly nuint Zero = 0;

        public static unsafe int Size => sizeof(nuint);

        public bool Equals(nuint other) => this == other;

        public override bool Equals(object? obj) => obj is nuint n && this == n;

        public override int GetHashCode() => (int)this;

        public int CompareTo(nuint other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            return 0;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not nuint n) throw new ArgumentException("Object must be UIntPtr");
            return CompareTo(n);
        }

        public override string ToString()
        {
            if (Size == 8)
                return UInt64.FormatUInt64((ulong)this);
            else
                return UInt32.FormatUInt32((uint)this);
        }

        public unsafe void* ToPointer() => (void*)this;

        public uint ToUInt32() => (uint)this;

        public ulong ToUInt64() => (ulong)this;
    }

    public struct Single : IEquatable<float>, IComparable<float>, IComparable
    {
        public const float MinValue = -3.40282346638528859e+38f;
        public const float MaxValue = 3.40282346638528859e+38f;
        public const float Epsilon = 1.401298E-45f;
        public const float NaN = 0.0f / 0.0f;
        public const float NegativeInfinity = -1.0f / 0.0f;
        public const float PositiveInfinity = 1.0f / 0.0f;

        public bool Equals(float other)
        {
            if (IsNaN(this)) return IsNaN(other);
            return this == other;
        }

        public override bool Equals(object? obj) => obj is float f && Equals(f);

        public override unsafe int GetHashCode()
        {
            float f = this;
            if (f == 0) return 0;
            return *(int*)&f;
        }

        public int CompareTo(float other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            if (this == other) return 0;
            // NaN handling
            if (IsNaN(this)) return IsNaN(other) ? 0 : -1;
            return 1;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not float f) throw new ArgumentException("Object must be Single");
            return CompareTo(f);
        }

        public override string ToString() => "Single"; // Full formatting would need more infrastructure

        public static bool IsNaN(float f) => f != f;

        public static bool IsInfinity(float f) => f == PositiveInfinity || f == NegativeInfinity;

        public static bool IsPositiveInfinity(float f) => f == PositiveInfinity;

        public static bool IsNegativeInfinity(float f) => f == NegativeInfinity;

        public static bool IsFinite(float f) => !IsNaN(f) && !IsInfinity(f);
    }

    public struct Double : IEquatable<double>, IComparable<double>, IComparable
    {
        public const double MinValue = -1.7976931348623157E+308;
        public const double MaxValue = 1.7976931348623157E+308;
        public const double Epsilon = 4.9406564584124654E-324;
        public const double NaN = 0.0 / 0.0;
        public const double NegativeInfinity = -1.0 / 0.0;
        public const double PositiveInfinity = 1.0 / 0.0;

        public bool Equals(double other)
        {
            if (IsNaN(this)) return IsNaN(other);
            return this == other;
        }

        public override bool Equals(object? obj) => obj is double d && Equals(d);

        public override unsafe int GetHashCode()
        {
            double d = this;
            if (d == 0) return 0;
            long bits = *(long*)&d;
            return (int)bits ^ (int)(bits >> 32);
        }

        public int CompareTo(double other)
        {
            if (this < other) return -1;
            if (this > other) return 1;
            if (this == other) return 0;
            // NaN handling
            if (IsNaN(this)) return IsNaN(other) ? 0 : -1;
            return 1;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not double d) throw new ArgumentException("Object must be Double");
            return CompareTo(d);
        }

        public override string ToString() => "Double"; // Full formatting would need more infrastructure

        public static bool IsNaN(double d) => d != d;

        public static bool IsInfinity(double d) => d == PositiveInfinity || d == NegativeInfinity;

        public static bool IsPositiveInfinity(double d) => d == PositiveInfinity;

        public static bool IsNegativeInfinity(double d) => d == NegativeInfinity;

        public static bool IsFinite(double d) => !IsNaN(d) && !IsInfinity(d);
    }
}
