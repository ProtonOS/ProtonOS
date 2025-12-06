// ProtonOS System.Runtime - Guid
// Represents a globally unique identifier (GUID).

namespace System
{
    /// <summary>
    /// Represents a globally unique identifier (GUID).
    /// </summary>
    public readonly struct Guid : IEquatable<Guid>, IComparable<Guid>, IComparable
    {
        /// <summary>A read-only instance of the Guid structure whose value is all zeros.</summary>
        public static readonly Guid Empty = new Guid();

        private readonly int _a;    // bytes 0-3
        private readonly short _b;  // bytes 4-5
        private readonly short _c;  // bytes 6-7
        private readonly byte _d;   // byte 8
        private readonly byte _e;   // byte 9
        private readonly byte _f;   // byte 10
        private readonly byte _g;   // byte 11
        private readonly byte _h;   // byte 12
        private readonly byte _i;   // byte 13
        private readonly byte _j;   // byte 14
        private readonly byte _k;   // byte 15

        /// <summary>Initializes a new instance of the Guid structure using the specified bytes.</summary>
        public Guid(byte[] b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (b.Length != 16) throw new ArgumentException("Byte array must be exactly 16 bytes long");

            _a = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
            _b = (short)(b[4] | (b[5] << 8));
            _c = (short)(b[6] | (b[7] << 8));
            _d = b[8];
            _e = b[9];
            _f = b[10];
            _g = b[11];
            _h = b[12];
            _i = b[13];
            _j = b[14];
            _k = b[15];
        }

        /// <summary>Initializes a new instance of the Guid structure using the specified integers and bytes.</summary>
        public Guid(int a, short b, short c, byte[] d)
        {
            if (d == null) throw new ArgumentNullException(nameof(d));
            if (d.Length != 8) throw new ArgumentException("Byte array must be exactly 8 bytes long");

            _a = a;
            _b = b;
            _c = c;
            _d = d[0];
            _e = d[1];
            _f = d[2];
            _g = d[3];
            _h = d[4];
            _i = d[5];
            _j = d[6];
            _k = d[7];
        }

        /// <summary>Initializes a new instance of the Guid structure using the specified integers and bytes.</summary>
        public Guid(int a, short b, short c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
        {
            _a = a;
            _b = b;
            _c = c;
            _d = d;
            _e = e;
            _f = f;
            _g = g;
            _h = h;
            _i = i;
            _j = j;
            _k = k;
        }

        /// <summary>Initializes a new instance of the Guid structure using the specified unsigned integers and bytes.</summary>
        public Guid(uint a, ushort b, ushort c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
            : this((int)a, (short)b, (short)c, d, e, f, g, h, i, j, k)
        {
        }

        /// <summary>Returns a 16-element byte array that contains the value of this instance.</summary>
        public byte[] ToByteArray()
        {
            return new byte[]
            {
                (byte)_a, (byte)(_a >> 8), (byte)(_a >> 16), (byte)(_a >> 24),
                (byte)_b, (byte)(_b >> 8),
                (byte)_c, (byte)(_c >> 8),
                _d, _e, _f, _g, _h, _i, _j, _k
            };
        }

        /// <summary>
        /// Initializes a new instance of the Guid structure by using the value represented by the specified string.
        /// </summary>
        public Guid(string g)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            // Simple parsing - expects format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            // Remove dashes and braces
            var clean = g.Replace("-", "").Replace("{", "").Replace("}", "");
            if (clean.Length != 32) throw new FormatException("Invalid Guid format");

            // Parse hex
            _a = ParseHex(clean, 0, 8);
            _b = (short)ParseHex(clean, 8, 4);
            _c = (short)ParseHex(clean, 12, 4);
            _d = (byte)ParseHex(clean, 16, 2);
            _e = (byte)ParseHex(clean, 18, 2);
            _f = (byte)ParseHex(clean, 20, 2);
            _g = (byte)ParseHex(clean, 22, 2);
            _h = (byte)ParseHex(clean, 24, 2);
            _i = (byte)ParseHex(clean, 26, 2);
            _j = (byte)ParseHex(clean, 28, 2);
            _k = (byte)ParseHex(clean, 30, 2);
        }

        private static int ParseHex(string s, int start, int length)
        {
            int result = 0;
            for (int i = 0; i < length; i++)
            {
                char c = s[start + i];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = c - 'a' + 10;
                else if (c >= 'A' && c <= 'F') digit = c - 'A' + 10;
                else throw new FormatException("Invalid hex character");
                result = (result << 4) | digit;
            }
            return result;
        }

        // Equality
        public bool Equals(Guid other)
        {
            return _a == other._a && _b == other._b && _c == other._c &&
                   _d == other._d && _e == other._e && _f == other._f &&
                   _g == other._g && _h == other._h && _i == other._i &&
                   _j == other._j && _k == other._k;
        }

        public override bool Equals(object? obj) => obj is Guid g && Equals(g);

        public static bool operator ==(Guid a, Guid b) => a.Equals(b);
        public static bool operator !=(Guid a, Guid b) => !a.Equals(b);

        public override int GetHashCode()
        {
            return _a ^ ((_b << 16) | (ushort)_c) ^ ((_f << 24) | _k);
        }

        // Comparison
        public int CompareTo(Guid other)
        {
            int result;
            if ((result = _a.CompareTo(other._a)) != 0) return result;
            if ((result = _b.CompareTo(other._b)) != 0) return result;
            if ((result = _c.CompareTo(other._c)) != 0) return result;
            if ((result = _d.CompareTo(other._d)) != 0) return result;
            if ((result = _e.CompareTo(other._e)) != 0) return result;
            if ((result = _f.CompareTo(other._f)) != 0) return result;
            if ((result = _g.CompareTo(other._g)) != 0) return result;
            if ((result = _h.CompareTo(other._h)) != 0) return result;
            if ((result = _i.CompareTo(other._i)) != 0) return result;
            if ((result = _j.CompareTo(other._j)) != 0) return result;
            return _k.CompareTo(other._k);
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not Guid g) throw new ArgumentException("Object must be Guid");
            return CompareTo(g);
        }

        public override string ToString()
        {
            // Format: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            return ToHex(_a, 8) + "-" +
                   ToHex(_b, 4) + "-" +
                   ToHex(_c, 4) + "-" +
                   ToHex(_d, 2) + ToHex(_e, 2) + "-" +
                   ToHex(_f, 2) + ToHex(_g, 2) + ToHex(_h, 2) +
                   ToHex(_i, 2) + ToHex(_j, 2) + ToHex(_k, 2);
        }

        private static string ToHex(int value, int digits)
        {
            char[] chars = new char[digits];
            for (int i = digits - 1; i >= 0; i--)
            {
                int digit = value & 0xF;
                chars[i] = digit < 10 ? (char)('0' + digit) : (char)('a' + digit - 10);
                value >>= 4;
            }
            return new string(chars);
        }

        public string ToString(string? format)
        {
            // D is the default format
            return ToString();
        }
    }
}
