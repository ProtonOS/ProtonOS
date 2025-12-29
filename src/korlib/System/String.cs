// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
// Enhanced for ProtonOS
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

using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    public sealed class String : IEquatable<string>, IComparable<string>, IComparable
    {
        // The layout of the string type is a contract with the compiler.
        private readonly int _length;
        private char _firstChar;

        /// <summary>
        /// Gets the empty string.
        /// </summary>
        /// <remarks>
        /// Note: Implemented as property because static field initialization causes bflat AOT issues.
        /// The JIT handles ldsfld String::Empty specially by synthesizing an empty string.
        /// </remarks>
        public static string Empty => "";

        public int Length => _length;

        [IndexerName("Chars")]
        public unsafe char this[int index]
        {
            [System.Runtime.CompilerServices.Intrinsic]
            get
            {
                return System.Runtime.CompilerServices.Unsafe.Add(ref _firstChar, index);
            }
        }

        // Compiler-required equality operators
        public static unsafe bool operator ==(string? a, string? b)
        {
            if ((object?)a == (object?)b) return true;
            if (a is null || b is null) return false;

            int lenA = a.Length;
            int lenB = b.Length;
            if (lenA != lenB) return false;

            // Debug: for 9-char strings (likely "StaticAdd"), show more detail
            if (lenA == 9)
            {
                byte* prefix = stackalloc byte[20];
                prefix[0] = (byte)'['; prefix[1] = (byte)'S'; prefix[2] = (byte)'9';
                prefix[3] = (byte)']'; prefix[4] = (byte)' '; prefix[5] = 0;

                // Print first char codes
                int firstA = (int)a[0];
                int firstB = (int)b[0];
                Debug_PrintInt(prefix, firstA * 1000 + firstB); // First chars

                // Print result
                bool match = true;
                for (int j = 0; j < lenA; j++)
                {
                    if (a[j] != b[j])
                    {
                        match = false;
                        // Print mismatch position and chars
                        byte* prefix2 = stackalloc byte[20];
                        prefix2[0] = (byte)'['; prefix2[1] = (byte)'S'; prefix2[2] = (byte)'9';
                        prefix2[3] = (byte)'!'; prefix2[4] = (byte)']'; prefix2[5] = 0;
                        Debug_PrintInt(prefix2, j * 10000 + (int)a[j] * 100 + (int)b[j]);
                        break;
                    }
                }
                if (match)
                {
                    byte* prefix3 = stackalloc byte[20];
                    prefix3[0] = (byte)'['; prefix3[1] = (byte)'S'; prefix3[2] = (byte)'9';
                    prefix3[3] = (byte)'='; prefix3[4] = (byte)']'; prefix3[5] = 0;
                    Debug_PrintInt(prefix3, 1); // Match!
                }
            }

            for (int i = 0; i < lenA; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }

        [System.Runtime.InteropServices.DllImport("*", EntryPoint = "Debug_PrintInt", CallingConvention = System.Runtime.InteropServices.CallingConvention.Cdecl)]
        private static extern unsafe void Debug_PrintInt(byte* prefix, int value);

        public static bool operator !=(string? a, string? b) => !(a == b);

        public bool Equals(string? other) => this == other;

        public override bool Equals(object? obj) => obj is string s && this == s;

        public override int GetHashCode()
        {
            int hash = 5381;
            for (int i = 0; i < _length; i++)
            {
                hash = ((hash << 5) + hash) ^ this[i];
            }
            return hash;
        }

        public int CompareTo(string? other)
        {
            if (other == null) return 1;

            int len = _length < other._length ? _length : other._length;
            for (int i = 0; i < len; i++)
            {
                int diff = this[i] - other[i];
                if (diff != 0) return diff;
            }
            return _length - other._length;
        }

        public int CompareTo(object? obj)
        {
            if (obj == null) return 1;
            if (obj is not string s) throw new ArgumentException("Object must be String");
            return CompareTo(s);
        }

        public override string ToString() => this;

        // Static methods

        public static bool IsNullOrEmpty(string? value) => value == null || value.Length == 0;

        public static bool IsNullOrWhiteSpace(string? value)
        {
            if (value == null) return true;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i])) return false;
            }
            return true;
        }

        public static string Concat(string? str0, string? str1)
        {
            if (IsNullOrEmpty(str0)) return str1 ?? Empty;
            if (IsNullOrEmpty(str1)) return str0!;

            int len0 = str0!.Length;
            int len1 = str1!.Length;
            string result = FastNewString(len0 + len1);

            for (int i = 0; i < len0; i++)
                Unsafe.Add(ref result._firstChar, i) = str0[i];
            for (int i = 0; i < len1; i++)
                Unsafe.Add(ref result._firstChar, len0 + i) = str1[i];

            return result;
        }

        public static string Concat(string? str0, string? str1, string? str2)
        {
            return Concat(Concat(str0, str1), str2);
        }

        public static string Concat(string? str0, string? str1, string? str2, string? str3)
        {
            return Concat(Concat(str0, str1), Concat(str2, str3));
        }

        // Additional Concat overloads for longer concatenations (avoids params boxing)
        public static string Concat(string? str0, string? str1, string? str2, string? str3, string? str4)
        {
            return Concat(Concat(str0, str1, str2, str3), str4);
        }

        public static string Concat(string? str0, string? str1, string? str2, string? str3, string? str4, string? str5)
        {
            return Concat(Concat(str0, str1, str2, str3), Concat(str4, str5));
        }

        public static string Concat(string? str0, string? str1, string? str2, string? str3, string? str4, string? str5, string? str6)
        {
            return Concat(Concat(str0, str1, str2, str3), Concat(str4, str5, str6));
        }

        public static string Concat(string? str0, string? str1, string? str2, string? str3, string? str4, string? str5, string? str6, string? str7)
        {
            return Concat(Concat(str0, str1, str2, str3), Concat(str4, str5, str6, str7));
        }

        public static string Concat(params string?[] values)
        {
            if (values == null || values.Length == 0) return Empty;
            if (values.Length == 1) return values[0] ?? Empty;

            // Calculate total length
            int totalLen = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null)
                    totalLen += values[i]!.Length;
            }

            if (totalLen == 0) return Empty;

            // Allocate and copy
            string result = FastNewString(totalLen);
            int pos = 0;
            for (int i = 0; i < values.Length; i++)
            {
                string? s = values[i];
                if (s != null)
                {
                    for (int j = 0; j < s.Length; j++)
                        Unsafe.Add(ref result._firstChar, pos++) = s[j];
                }
            }

            return result;
        }

        // Instance methods

        public bool Contains(char c)
        {
            for (int i = 0; i < _length; i++)
            {
                if (this[i] == c) return true;
            }
            return false;
        }

        public bool Contains(string value)
        {
            return IndexOf(value) >= 0;
        }

        public int IndexOf(char c)
        {
            for (int i = 0; i < _length; i++)
            {
                if (this[i] == c) return i;
            }
            return -1;
        }

        public int IndexOf(char c, int startIndex)
        {
            for (int i = startIndex; i < _length; i++)
            {
                if (this[i] == c) return i;
            }
            return -1;
        }

        public int IndexOf(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) return 0;
            if (value.Length > _length) return -1;

            for (int i = 0; i <= _length - value.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < value.Length; j++)
                {
                    if (this[i + j] != value[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        public int LastIndexOf(char c)
        {
            for (int i = _length - 1; i >= 0; i--)
            {
                if (this[i] == c) return i;
            }
            return -1;
        }

        public bool StartsWith(char c) => _length > 0 && this[0] == c;

        public bool StartsWith(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length > _length) return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (this[i] != value[i]) return false;
            }
            return true;
        }

        public bool EndsWith(char c) => _length > 0 && this[_length - 1] == c;

        public bool EndsWith(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length > _length) return false;

            int offset = _length - value.Length;
            for (int i = 0; i < value.Length; i++)
            {
                if (this[offset + i] != value[i]) return false;
            }
            return true;
        }

        public string Substring(int startIndex)
        {
            if (startIndex < 0 || startIndex > _length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            return Substring(startIndex, _length - startIndex);
        }

        public string Substring(int startIndex, int length)
        {
            if (startIndex < 0 || startIndex > _length)
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (length < 0 || startIndex + length > _length)
                throw new ArgumentOutOfRangeException(nameof(length));

            if (length == 0) return Empty;
            if (startIndex == 0 && length == _length) return this;

            string result = FastNewString(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = this[startIndex + i];
            return result;
        }

        public string ToLower()
        {
            string result = FastNewString(_length);
            for (int i = 0; i < _length; i++)
                Unsafe.Add(ref result._firstChar, i) = char.ToLower(this[i]);
            return result;
        }

        public string ToUpper()
        {
            string result = FastNewString(_length);
            for (int i = 0; i < _length; i++)
                Unsafe.Add(ref result._firstChar, i) = char.ToUpper(this[i]);
            return result;
        }

        public string Trim()
        {
            int start = 0;
            int end = _length - 1;

            while (start <= end && char.IsWhiteSpace(this[start])) start++;
            while (end >= start && char.IsWhiteSpace(this[end])) end--;

            return Substring(start, end - start + 1);
        }

        public string TrimStart()
        {
            int start = 0;
            while (start < _length && char.IsWhiteSpace(this[start])) start++;
            return Substring(start);
        }

        public string TrimEnd()
        {
            int end = _length - 1;
            while (end >= 0 && char.IsWhiteSpace(this[end])) end--;
            return Substring(0, end + 1);
        }

        public string Replace(char oldChar, char newChar)
        {
            string result = FastNewString(_length);
            for (int i = 0; i < _length; i++)
            {
                char c = this[i];
                Unsafe.Add(ref result._firstChar, i) = (c == oldChar) ? newChar : c;
            }
            return result;
        }

        public string Replace(string oldValue, string newValue)
        {
            if (oldValue == null) throw new ArgumentNullException(nameof(oldValue));
            if (oldValue.Length == 0) throw new ArgumentException("String cannot be empty");
            newValue ??= Empty;

            // Count occurrences first
            int count = 0;
            int pos = 0;
            while ((pos = IndexOfInternal(oldValue, pos)) >= 0)
            {
                count++;
                pos += oldValue.Length;
            }

            if (count == 0) return this;

            // Calculate new length
            int newLength = _length + (newValue.Length - oldValue.Length) * count;
            if (newLength == 0) return Empty;

            string result = FastNewString(newLength);
            int srcPos = 0;
            int dstPos = 0;

            while ((pos = IndexOfInternal(oldValue, srcPos)) >= 0)
            {
                // Copy characters before the match
                int copyLen = pos - srcPos;
                for (int i = 0; i < copyLen; i++)
                    Unsafe.Add(ref result._firstChar, dstPos++) = this[srcPos + i];

                // Copy replacement
                for (int i = 0; i < newValue.Length; i++)
                    Unsafe.Add(ref result._firstChar, dstPos++) = newValue[i];

                srcPos = pos + oldValue.Length;
            }

            // Copy remaining characters
            while (srcPos < _length)
                Unsafe.Add(ref result._firstChar, dstPos++) = this[srcPos++];

            return result;
        }

        private int IndexOfInternal(string value, int startIndex)
        {
            if (value.Length == 0) return startIndex;
            if (startIndex + value.Length > _length) return -1;

            for (int i = startIndex; i <= _length - value.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < value.Length; j++)
                {
                    if (this[i + j] != value[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        public string PadLeft(int totalWidth) => PadLeft(totalWidth, ' ');

        public string PadLeft(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException(nameof(totalWidth));
            if (totalWidth <= _length) return this;

            int padCount = totalWidth - _length;
            string result = FastNewString(totalWidth);

            for (int i = 0; i < padCount; i++)
                Unsafe.Add(ref result._firstChar, i) = paddingChar;
            for (int i = 0; i < _length; i++)
                Unsafe.Add(ref result._firstChar, padCount + i) = this[i];

            return result;
        }

        public string PadRight(int totalWidth) => PadRight(totalWidth, ' ');

        public string PadRight(int totalWidth, char paddingChar)
        {
            if (totalWidth < 0) throw new ArgumentOutOfRangeException(nameof(totalWidth));
            if (totalWidth <= _length) return this;

            string result = FastNewString(totalWidth);

            for (int i = 0; i < _length; i++)
                Unsafe.Add(ref result._firstChar, i) = this[i];
            for (int i = _length; i < totalWidth; i++)
                Unsafe.Add(ref result._firstChar, i) = paddingChar;

            return result;
        }

        public unsafe ReadOnlySpan<char> AsSpan()
        {
            fixed (char* ptr = &_firstChar)
            {
                return new ReadOnlySpan<char>(ptr, _length);
            }
        }

        public unsafe ReadOnlySpan<char> AsSpan(int start)
        {
            if ((uint)start > (uint)_length) throw new ArgumentOutOfRangeException(nameof(start));
            fixed (char* ptr = &_firstChar)
            {
                return new ReadOnlySpan<char>(ptr + start, _length - start);
            }
        }

        public unsafe ReadOnlySpan<char> AsSpan(int start, int length)
        {
            if ((uint)start > (uint)_length) throw new ArgumentOutOfRangeException(nameof(start));
            if ((uint)length > (uint)(_length - start)) throw new ArgumentOutOfRangeException(nameof(length));
            fixed (char* ptr = &_firstChar)
            {
                return new ReadOnlySpan<char>(ptr + start, length);
            }
        }

        /// <summary>
        /// Gets a reference to the first character.
        /// Used by fixed statements for pinning strings.
        /// </summary>
        public ref readonly char GetPinnableReference()
        {
            return ref _firstChar;
        }

        /// <summary>
        /// Copies a specified number of characters from a specified position in this string
        /// to a specified position in a destination character array.
        /// </summary>
        public void CopyTo(int sourceIndex, char[] destination, int destinationIndex, int count)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (sourceIndex < 0) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (destinationIndex < 0) throw new ArgumentOutOfRangeException(nameof(destinationIndex));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (sourceIndex > _length - count) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
            if (destinationIndex > destination.Length - count) throw new ArgumentOutOfRangeException(nameof(destinationIndex));

            for (int i = 0; i < count; i++)
                destination[destinationIndex + i] = this[sourceIndex + i];
        }

        public char[] ToCharArray()
        {
            if (_length == 0) return new char[0];
            char[] result = new char[_length];
            for (int i = 0; i < _length; i++)
                result[i] = this[i];
            return result;
        }

        public char[] ToCharArray(int startIndex, int length)
        {
            if (startIndex < 0 || startIndex > _length) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (length < 0 || startIndex + length > _length) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return new char[0];

            char[] result = new char[length];
            for (int i = 0; i < length; i++)
                result[i] = this[startIndex + i];
            return result;
        }

        public int IndexOf(string value, int startIndex)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (startIndex < 0 || startIndex > _length) throw new ArgumentOutOfRangeException(nameof(startIndex));
            return IndexOfInternal(value, startIndex);
        }

        public int LastIndexOf(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Length == 0) return _length;
            if (value.Length > _length) return -1;

            for (int i = _length - value.Length; i >= 0; i--)
            {
                bool found = true;
                for (int j = 0; j < value.Length; j++)
                {
                    if (this[i + j] != value[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found) return i;
            }
            return -1;
        }

        public string Insert(int startIndex, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (startIndex < 0 || startIndex > _length) throw new ArgumentOutOfRangeException(nameof(startIndex));

            if (value.Length == 0) return this;
            if (_length == 0) return value;

            string result = FastNewString(_length + value.Length);

            for (int i = 0; i < startIndex; i++)
                Unsafe.Add(ref result._firstChar, i) = this[i];
            for (int i = 0; i < value.Length; i++)
                Unsafe.Add(ref result._firstChar, startIndex + i) = value[i];
            for (int i = startIndex; i < _length; i++)
                Unsafe.Add(ref result._firstChar, value.Length + i) = this[i];

            return result;
        }

        public string Remove(int startIndex)
        {
            if (startIndex < 0 || startIndex >= _length) throw new ArgumentOutOfRangeException(nameof(startIndex));
            return Substring(0, startIndex);
        }

        public string Remove(int startIndex, int count)
        {
            if (startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (startIndex + count > _length) throw new ArgumentOutOfRangeException();

            if (count == 0) return this;
            if (startIndex == 0 && count == _length) return Empty;

            string result = FastNewString(_length - count);

            for (int i = 0; i < startIndex; i++)
                Unsafe.Add(ref result._firstChar, i) = this[i];
            for (int i = startIndex + count; i < _length; i++)
                Unsafe.Add(ref result._firstChar, i - count) = this[i];

            return result;
        }

        public unsafe string[] Split(char separator)
        {
            // Count occurrences
            int count = 1;
            for (int i = 0; i < _length; i++)
            {
                if (this[i] == separator) count++;
            }

            string[] result = new string[count];
            int start = 0;
            int resultIndex = 0;

            for (int i = 0; i < _length; i++)
            {
                if (this[i] == separator)
                {
                    result[resultIndex++] = Substring(start, i - start);
                    start = i + 1;
                }
            }
            result[resultIndex] = Substring(start);

            return result;
        }

        public static string Join(char separator, string?[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) return Empty;

            // Calculate total length
            int totalLength = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null) totalLength += values[i]!.Length;
                if (i > 0) totalLength++; // separator
            }

            string result = FastNewString(totalLength);
            int pos = 0;

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    Unsafe.Add(ref result._firstChar, pos++) = separator;
                }
                if (values[i] != null)
                {
                    string s = values[i]!;
                    for (int j = 0; j < s.Length; j++)
                        Unsafe.Add(ref result._firstChar, pos++) = s[j];
                }
            }

            return result;
        }

        public static string Join(string? separator, string?[] values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (values.Length == 0) return Empty;

            separator ??= Empty;

            // Calculate total length
            int totalLength = 0;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != null) totalLength += values[i]!.Length;
                if (i > 0) totalLength += separator.Length;
            }

            string result = FastNewString(totalLength);
            int pos = 0;

            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0)
                {
                    for (int j = 0; j < separator.Length; j++)
                        Unsafe.Add(ref result._firstChar, pos++) = separator[j];
                }
                if (values[i] != null)
                {
                    string s = values[i]!;
                    for (int j = 0; j < s.Length; j++)
                        Unsafe.Add(ref result._firstChar, pos++) = s[j];
                }
            }

            return result;
        }

        // Constructors

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern unsafe String(char* value);

        private static unsafe string Ctor(char* ptr)
        {
            char* cur = ptr;
            while (*cur++ != 0) ;

            string result = FastNewString((int)(cur - ptr - 1));
            for (int i = 0; i < cur - ptr - 1; i++)
                Unsafe.Add(ref result._firstChar, i) = ptr[i];
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern unsafe String(char* value, int startIndex, int length);

        private static unsafe string Ctor(char* ptr, int startIndex, int length)
        {
            if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Empty;

            string result = FastNewString(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = ptr[startIndex + i];
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern unsafe String(sbyte* value);

        private static unsafe string Ctor(sbyte* ptr)
        {
            sbyte* cur = ptr;
            while (*cur++ != 0) ;

            string result = FastNewString((int)(cur - ptr - 1));
            for (int i = 0; i < cur - ptr - 1; i++)
            {
                if (ptr[i] > 0x7F)
                    Environment.FailFast(null);
                Unsafe.Add(ref result._firstChar, i) = (char)ptr[i];
            }
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern String(char c, int count);

        private static string Ctor(char c, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0) return Empty;

            string result = FastNewString(count);
            for (int i = 0; i < count; i++)
                Unsafe.Add(ref result._firstChar, i) = c;
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern String(char[] value);

        private static string Ctor(char[] value)
        {
            if (value == null || value.Length == 0) return Empty;

            string result = FastNewString(value.Length);
            for (int i = 0; i < value.Length; i++)
                Unsafe.Add(ref result._firstChar, i) = value[i];
            return result;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern String(char[] value, int startIndex, int length);

        private static string Ctor(char[] value, int startIndex, int length)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (startIndex < 0) throw new ArgumentOutOfRangeException(nameof(startIndex));
            if (length < 0 || startIndex + length > value.Length)
                throw new ArgumentOutOfRangeException(nameof(length));
            if (length == 0) return Empty;

            string result = FastNewString(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = value[startIndex + i];
            return result;
        }

        internal static unsafe string FastNewString(int numChars)
        {
            return NewString("".m_pMethodTable, numChars);

            [MethodImpl(MethodImplOptions.InternalCall)]
            [RuntimeImport("*", "RhpNewArray")]
            static extern string NewString(MethodTable* pMT, int numElements);
        }

        // String constructor factory methods - these are registered in AotMethodRegistry
        // and called by the JIT when it sees newobj for String constructors.

        /// <summary>
        /// Factory method for String(char[], int, int) constructor.
        /// Called by the JIT instead of actually calling the constructor.
        /// </summary>
        public static string Ctor_CharArrayStartLength(char[] value, int startIndex, int length)
        {
            if (value == null || length == 0)
                return Empty;
            if (startIndex < 0 || length < 0 || startIndex + length > value.Length)
                return Empty;

            string result = FastNewString(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = value[startIndex + i];
            return result;
        }

        /// <summary>
        /// Factory method for String(char[]) constructor.
        /// </summary>
        public static string Ctor_CharArray(char[] value)
        {
            if (value == null || value.Length == 0)
                return Empty;

            string result = FastNewString(value.Length);
            for (int i = 0; i < value.Length; i++)
                Unsafe.Add(ref result._firstChar, i) = value[i];
            return result;
        }

        /// <summary>
        /// Factory method for String(char*, int, int) constructor.
        /// Used by reflection APIs for BytePtrToString.
        /// </summary>
        public static unsafe string Ctor_CharPtrStartLength(char* value, int startIndex, int length)
        {
            if (value == null || length <= 0)
                return Empty;

            string result = FastNewString(length);
            for (int i = 0; i < length; i++)
                Unsafe.Add(ref result._firstChar, i) = value[startIndex + i];
            return result;
        }

        // String.Format implementation for AOT code
        // Supports {0}, {1}, {2}, {3} placeholders

        public static string Format(string format, object? arg0)
        {
            return FormatHelper(format, arg0, null, null, null);
        }

        public static string Format(string format, object? arg0, object? arg1)
        {
            return FormatHelper(format, arg0, arg1, null, null);
        }

        public static string Format(string format, object? arg0, object? arg1, object? arg2)
        {
            return FormatHelper(format, arg0, arg1, arg2, null);
        }

        public static string Format(string format, params object?[] args)
        {
            if (format == null) return Empty;
            if (args == null || args.Length == 0) return format;

            // Calculate result length first
            int resultLen = 0;
            int i = 0;
            while (i < format.Length)
            {
                if (format[i] == '{' && i + 2 < format.Length && format[i + 2] == '}')
                {
                    char indexChar = format[i + 1];
                    if (indexChar >= '0' && indexChar <= '9')
                    {
                        int argIndex = indexChar - '0';
                        if (argIndex < args.Length && args[argIndex] != null)
                        {
                            string? argStr = args[argIndex]?.ToString();
                            if (argStr != null)
                                resultLen += argStr.Length;
                        }
                        i += 3;
                        continue;
                    }
                }
                resultLen++;
                i++;
            }

            // Build result
            string result = FastNewString(resultLen);
            int pos = 0;
            i = 0;
            while (i < format.Length)
            {
                if (format[i] == '{' && i + 2 < format.Length && format[i + 2] == '}')
                {
                    char indexChar = format[i + 1];
                    if (indexChar >= '0' && indexChar <= '9')
                    {
                        int argIndex = indexChar - '0';
                        if (argIndex < args.Length && args[argIndex] != null)
                        {
                            string? argStr = args[argIndex]?.ToString();
                            if (argStr != null)
                            {
                                for (int j = 0; j < argStr.Length; j++)
                                    Unsafe.Add(ref result._firstChar, pos++) = argStr[j];
                            }
                        }
                        i += 3;
                        continue;
                    }
                }
                Unsafe.Add(ref result._firstChar, pos++) = format[i];
                i++;
            }

            return result;
        }

        private static string FormatHelper(string format, object? arg0, object? arg1, object? arg2, object? arg3)
        {
            if (format == null) return Empty;

            // Convert args to strings with format support
            string? s0 = null, s1 = null, s2 = null, s3 = null;

            // Calculate result length and format arguments
            int resultLen = 0;
            int i = 0;
            while (i < format.Length)
            {
                if (format[i] == '{' && i + 1 < format.Length)
                {
                    // Find the closing brace
                    int closeBrace = format.IndexOf('}', i + 1);
                    if (closeBrace > i + 1)
                    {
                        // Parse the placeholder content
                        int argIndex = -1;
                        string? argFormat = null;
                        int colonPos = -1;

                        // Look for colon (format specifier separator)
                        for (int k = i + 1; k < closeBrace; k++)
                        {
                            if (format[k] == ':')
                            {
                                colonPos = k;
                                break;
                            }
                        }

                        // Parse argument index
                        char indexChar = format[i + 1];
                        if (indexChar >= '0' && indexChar <= '9')
                        {
                            argIndex = indexChar - '0';
                        }

                        // Extract format specifier if present
                        if (colonPos > 0 && colonPos < closeBrace - 1)
                        {
                            argFormat = format.Substring(colonPos + 1, closeBrace - colonPos - 1);
                        }

                        // Format the argument
                        string? argStr = argIndex switch
                        {
                            0 => s0 ?? (s0 = FormatArg(arg0, argFormat)),
                            1 => s1 ?? (s1 = FormatArg(arg1, argFormat)),
                            2 => s2 ?? (s2 = FormatArg(arg2, argFormat)),
                            3 => s3 ?? (s3 = FormatArg(arg3, argFormat)),
                            _ => null
                        };

                        if (argStr != null)
                            resultLen += argStr.Length;

                        i = closeBrace + 1;
                        continue;
                    }
                }
                resultLen++;
                i++;
            }

            // Build result
            string result = FastNewString(resultLen);
            int pos = 0;
            i = 0;
            while (i < format.Length)
            {
                if (format[i] == '{' && i + 1 < format.Length)
                {
                    int closeBrace = format.IndexOf('}', i + 1);
                    if (closeBrace > i + 1)
                    {
                        char indexChar = format[i + 1];
                        string? argStr = indexChar switch
                        {
                            '0' => s0,
                            '1' => s1,
                            '2' => s2,
                            '3' => s3,
                            _ => null
                        };
                        if (argStr != null)
                        {
                            for (int j = 0; j < argStr.Length; j++)
                                Unsafe.Add(ref result._firstChar, pos++) = argStr[j];
                        }
                        i = closeBrace + 1;
                        continue;
                    }
                }
                Unsafe.Add(ref result._firstChar, pos++) = format[i];
                i++;
            }

            return result;
        }

        private static string? FormatArg(object? arg, string? format)
        {
            if (arg == null) return null;

            // For now, just use ToString() - format specifiers require type identification
            // which needs TypeCast runtime support we don't have
            // TODO: Add format support once we have TypeCast or alternative approach
            return arg.ToString();
        }
    }
}
