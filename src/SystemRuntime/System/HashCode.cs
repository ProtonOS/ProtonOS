// ProtonOS System.Runtime - HashCode
// Combines the hash codes for multiple values into a single hash code.

namespace System
{
    /// <summary>
    /// Combines the hash codes for multiple values into a single hash code.
    /// </summary>
    public struct HashCode
    {
        private const uint Prime1 = 2654435761U;
        private const uint Prime2 = 2246822519U;
        private const uint Prime3 = 3266489917U;
        private const uint Prime4 = 668265263U;
        private const uint Prime5 = 374761393U;

        private uint _hash;
        private int _length;

        /// <summary>
        /// Diffuses the hash code returned by the specified value.
        /// </summary>
        public static int Combine<T1>(T1 value1)
        {
            uint hash = Prime5 + 4;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines two values into a hash code.
        /// </summary>
        public static int Combine<T1, T2>(T1 value1, T2 value2)
        {
            uint hash = Prime5 + 8;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines three values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
        {
            uint hash = Prime5 + 12;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines four values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
        {
            uint hash = Prime5 + 16;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value4?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines five values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
        {
            uint hash = Prime5 + 20;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value4?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value5?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines six values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
        {
            uint hash = Prime5 + 24;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value4?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value5?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value6?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines seven values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3, T4, T5, T6, T7>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            uint hash = Prime5 + 28;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value4?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value5?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value6?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value7?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Combines eight values into a hash code.
        /// </summary>
        public static int Combine<T1, T2, T3, T4, T5, T6, T7, T8>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6, T7 value7, T8 value8)
        {
            uint hash = Prime5 + 32;
            hash = MixState(hash, (uint)(value1?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value2?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value3?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value4?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value5?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value6?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value7?.GetHashCode() ?? 0));
            hash = MixState(hash, (uint)(value8?.GetHashCode() ?? 0));
            return (int)MixFinal(hash);
        }

        /// <summary>
        /// Adds a single value to the hash code.
        /// </summary>
        public void Add<T>(T value)
        {
            _hash = MixState(_hash + Prime5, (uint)(value?.GetHashCode() ?? 0));
            _length += 4;
        }

        /// <summary>
        /// Adds a single value to the hash code, using the specified equality comparer.
        /// </summary>
        public void Add<T>(T value, System.Collections.Generic.IEqualityComparer<T>? comparer)
        {
            int hash = value != null && comparer != null ? comparer.GetHashCode(value) : (value?.GetHashCode() ?? 0);
            _hash = MixState(_hash + Prime5, (uint)hash);
            _length += 4;
        }

        /// <summary>
        /// Calculates the final hash code after consecutive Add invocations.
        /// </summary>
        public int ToHashCode()
        {
            return (int)MixFinal(_hash + (uint)_length);
        }

        private static uint MixState(uint v1, uint v2)
        {
            return RotateLeft(v1 + v2 * Prime2, 13) * Prime1;
        }

        private static uint MixFinal(uint hash)
        {
            hash ^= hash >> 15;
            hash *= Prime2;
            hash ^= hash >> 13;
            hash *= Prime3;
            hash ^= hash >> 16;
            return hash;
        }

        private static uint RotateLeft(uint value, int offset)
        {
            return (value << offset) | (value >> (32 - offset));
        }

        /// <summary>
        /// This method is not supported and should not be called.
        /// </summary>
        [Obsolete("HashCode is a mutable struct and should not be compared with other HashCodes. Use ToHashCode to retrieve the computed hash code.", true)]
        public override int GetHashCode()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// This method is not supported and should not be called.
        /// </summary>
        [Obsolete("HashCode is a mutable struct and should not be compared with other HashCodes.", true)]
        public override bool Equals(object? obj)
        {
            throw new NotSupportedException();
        }
    }
}
