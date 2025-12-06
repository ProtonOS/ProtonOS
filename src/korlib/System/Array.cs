// bflat minimal runtime library
// Copyright (C) 2021-2022 Michal Strehovsky
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

using System.Runtime.CompilerServices;
using System.Collections;

namespace System
{
    public abstract class Array : IEnumerable
    {
        private readonly int _length;

        /// <summary>
        /// Gets the total number of elements in the array.
        /// </summary>
        public int Length => _length;

        /// <summary>
        /// Gets a 64-bit integer that represents the total number of elements in the array.
        /// </summary>
        public long LongLength => _length;

        /// <summary>
        /// Gets the rank (number of dimensions) of the Array.
        /// </summary>
        public int Rank => 1;  // Only single-dimensional arrays supported

        /// <summary>
        /// Returns an empty array.
        /// </summary>
        public static T[] Empty<T>() => EmptyArray<T>.Value;

        /// <summary>
        /// Copies a range of elements from an Array starting at the specified source index
        /// and pastes them to another Array starting at the specified destination index.
        /// </summary>
        public static unsafe void Copy(Array sourceArray, int sourceIndex, Array destinationArray, int destinationIndex, int length)
        {
            if (sourceArray == null)
                Environment.FailFast("Array.Copy: sourceArray is null");
            if (destinationArray == null)
                Environment.FailFast("Array.Copy: destinationArray is null");
            if (sourceIndex < 0 || destinationIndex < 0 || length < 0)
                Environment.FailFast("Array.Copy: negative index or length");
            if (sourceIndex + length > sourceArray.Length)
                Environment.FailFast("Array.Copy: source overflow");
            if (destinationIndex + length > destinationArray.Length)
                Environment.FailFast("Array.Copy: destination overflow");

            // Get element size from source array's MethodTable
            void* srcMT = *(void**)Unsafe.AsPointer(ref Unsafe.As<Array, byte>(ref sourceArray));
            ushort elementSize = *(ushort*)srcMT;

            // Get raw data pointers using Unsafe.AsPointer
            ref byte srcRef = ref Unsafe.As<RawArrayData>(sourceArray).Data;
            ref byte dstRef = ref Unsafe.As<RawArrayData>(destinationArray).Data;
            byte* src = (byte*)Unsafe.AsPointer(ref srcRef);
            byte* dst = (byte*)Unsafe.AsPointer(ref dstRef);

            // Calculate byte offsets
            int srcByteOffset = sourceIndex * elementSize;
            int dstByteOffset = destinationIndex * elementSize;
            int byteCount = length * elementSize;

            // Copy bytes
            for (int i = 0; i < byteCount; i++)
            {
                dst[dstByteOffset + i] = src[srcByteOffset + i];
            }
        }

        /// <summary>
        /// Copies a range of elements from an Array and pastes them to another Array.
        /// </summary>
        public static void Copy(Array sourceArray, Array destinationArray, int length)
        {
            Copy(sourceArray, 0, destinationArray, 0, length);
        }

        /// <summary>
        /// Copies a range of elements from an Array and pastes them to another Array (64-bit).
        /// </summary>
        public static void Copy(Array sourceArray, long sourceIndex, Array destinationArray, long destinationIndex, long length)
        {
            Copy(sourceArray, (int)sourceIndex, destinationArray, (int)destinationIndex, (int)length);
        }

        /// <summary>
        /// Sets a range of elements in an array to the default value of each element type.
        /// </summary>
        public static unsafe void Clear(Array array, int index, int length)
        {
            if (array == null)
                Environment.FailFast("Array.Clear: array is null");
            if (index < 0 || length < 0)
                Environment.FailFast("Array.Clear: negative index or length");
            if (index + length > array.Length)
                Environment.FailFast("Array.Clear: overflow");

            // Get element size from array's MethodTable
            void* pMT = *(void**)Unsafe.AsPointer(ref Unsafe.As<Array, byte>(ref array));
            ushort elementSize = *(ushort*)pMT;

            // Get raw data pointer using Unsafe.AsPointer
            ref byte dataRef = ref Unsafe.As<RawArrayData>(array).Data;
            byte* data = (byte*)Unsafe.AsPointer(ref dataRef);
            int byteOffset = index * elementSize;
            int byteCount = length * elementSize;

            // Zero out bytes
            for (int i = 0; i < byteCount; i++)
            {
                data[byteOffset + i] = 0;
            }
        }

        /// <summary>
        /// Sets all elements in an array to the default value.
        /// </summary>
        public static void Clear(Array array)
        {
            if (array == null)
                Environment.FailFast("Array.Clear: array is null");
            Clear(array, 0, array.Length);
        }

        /// <summary>
        /// Searches for the specified object and returns the index of its first occurrence.
        /// </summary>
        public static int IndexOf<T>(T[] array, T value)
        {
            return IndexOf(array, value, 0, array?.Length ?? 0);
        }

        /// <summary>
        /// Searches for the specified object starting at the specified index.
        /// </summary>
        public static int IndexOf<T>(T[] array, T value, int startIndex)
        {
            return IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);
        }

        /// <summary>
        /// Searches for the specified object in a range of elements.
        /// </summary>
        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null)
                Environment.FailFast("Array.IndexOf: array is null");
            if (startIndex < 0 || count < 0)
                Environment.FailFast("Array.IndexOf: negative index or count");
            if (startIndex + count > array.Length)
                Environment.FailFast("Array.IndexOf: overflow");

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                if (EqualsHelper(array[i], value))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Searches for the specified object and returns the index of its last occurrence.
        /// </summary>
        public static int LastIndexOf<T>(T[] array, T value)
        {
            if (array == null)
                Environment.FailFast("Array.LastIndexOf: array is null");
            return LastIndexOf(array, value, array.Length - 1, array.Length);
        }

        /// <summary>
        /// Searches for the specified object and returns the index of its last occurrence.
        /// </summary>
        public static int LastIndexOf<T>(T[] array, T value, int startIndex)
        {
            if (array == null)
                Environment.FailFast("Array.LastIndexOf: array is null");
            return LastIndexOf(array, value, startIndex, startIndex + 1);
        }

        /// <summary>
        /// Searches for the specified object and returns the index of its last occurrence.
        /// </summary>
        public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null)
                Environment.FailFast("Array.LastIndexOf: array is null");
            if (array.Length == 0)
                return -1;
            if (startIndex < 0 || count < 0)
                Environment.FailFast("Array.LastIndexOf: negative index or count");
            if (startIndex >= array.Length)
                Environment.FailFast("Array.LastIndexOf: startIndex out of range");

            int endIndex = startIndex - count + 1;
            if (endIndex < 0)
                Environment.FailFast("Array.LastIndexOf: count too large");

            for (int i = startIndex; i >= endIndex; i--)
            {
                if (EqualsHelper(array[i], value))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Helper method for equality comparison without depending on EqualityComparer&lt;T&gt;.
        /// </summary>
        private static bool EqualsHelper<T>(T a, T b)
        {
            if (a == null)
                return b == null;
            return a.Equals(b);
        }

        /// <summary>
        /// Determines whether the specified array contains elements that match the conditions.
        /// </summary>
        public static bool Exists<T>(T[] array, Predicate<T> match)
        {
            return FindIndex(array, match) != -1;
        }

        /// <summary>
        /// Searches for an element that matches the conditions and returns the first occurrence.
        /// </summary>
        public static T? Find<T>(T[] array, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.Find: array is null");
            if (match == null)
                Environment.FailFast("Array.Find: match is null");

            for (int i = 0; i < array.Length; i++)
            {
                if (match(array[i]))
                    return array[i];
            }
            return default;
        }

        /// <summary>
        /// Searches for an element that matches the conditions and returns the zero-based index.
        /// </summary>
        public static int FindIndex<T>(T[] array, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.FindIndex: array is null");
            return FindIndex(array, 0, array.Length, match);
        }

        /// <summary>
        /// Searches for an element that matches the conditions and returns the zero-based index.
        /// </summary>
        public static int FindIndex<T>(T[] array, int startIndex, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.FindIndex: array is null");
            return FindIndex(array, startIndex, array.Length - startIndex, match);
        }

        /// <summary>
        /// Searches for an element that matches the conditions and returns the zero-based index.
        /// </summary>
        public static int FindIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.FindIndex: array is null");
            if (match == null)
                Environment.FailFast("Array.FindIndex: match is null");
            if (startIndex < 0 || count < 0)
                Environment.FailFast("Array.FindIndex: negative index or count");
            if (startIndex + count > array.Length)
                Environment.FailFast("Array.FindIndex: overflow");

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                if (match(array[i]))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Retrieves all elements that match the conditions defined by the specified predicate.
        /// </summary>
        public static T[] FindAll<T>(T[] array, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.FindAll: array is null");
            if (match == null)
                Environment.FailFast("Array.FindAll: match is null");

            // First pass: count matches
            int count = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (match(array[i]))
                    count++;
            }

            if (count == 0)
                return Empty<T>();

            // Second pass: collect matches
            T[] result = new T[count];
            int idx = 0;
            for (int i = 0; i < array.Length; i++)
            {
                if (match(array[i]))
                    result[idx++] = array[i];
            }
            return result;
        }

        /// <summary>
        /// Reverses the sequence of elements in the entire array.
        /// </summary>
        public static void Reverse<T>(T[] array)
        {
            if (array == null)
                Environment.FailFast("Array.Reverse: array is null");
            Reverse(array, 0, array.Length);
        }

        /// <summary>
        /// Reverses the sequence of a subset of elements in the array.
        /// </summary>
        public static void Reverse<T>(T[] array, int index, int length)
        {
            if (array == null)
                Environment.FailFast("Array.Reverse: array is null");
            if (index < 0 || length < 0)
                Environment.FailFast("Array.Reverse: negative index or length");
            if (index + length > array.Length)
                Environment.FailFast("Array.Reverse: overflow");

            int i = index;
            int j = index + length - 1;
            while (i < j)
            {
                T temp = array[i];
                array[i] = array[j];
                array[j] = temp;
                i++;
                j--;
            }
        }

        /// <summary>
        /// Resizes the array to the specified new size.
        /// </summary>
        public static void Resize<T>(ref T[]? array, int newSize)
        {
            if (newSize < 0)
                Environment.FailFast("Array.Resize: negative size");

            T[]? oldArray = array;
            if (oldArray == null)
            {
                array = new T[newSize];
                return;
            }

            if (oldArray.Length != newSize)
            {
                T[] newArray = new T[newSize];
                int copyLength = oldArray.Length < newSize ? oldArray.Length : newSize;
                for (int i = 0; i < copyLength; i++)
                {
                    newArray[i] = oldArray[i];
                }
                array = newArray;
            }
        }

        /// <summary>
        /// Performs the specified action on each element of the array.
        /// </summary>
        public static void ForEach<T>(T[] array, Action<T> action)
        {
            if (array == null)
                Environment.FailFast("Array.ForEach: array is null");
            if (action == null)
                Environment.FailFast("Array.ForEach: action is null");

            for (int i = 0; i < array.Length; i++)
            {
                action(array[i]);
            }
        }

        /// <summary>
        /// Determines whether every element in the array matches the conditions.
        /// </summary>
        public static bool TrueForAll<T>(T[] array, Predicate<T> match)
        {
            if (array == null)
                Environment.FailFast("Array.TrueForAll: array is null");
            if (match == null)
                Environment.FailFast("Array.TrueForAll: match is null");

            for (int i = 0; i < array.Length; i++)
            {
                if (!match(array[i]))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Converts an array of one type to an array of another type.
        /// </summary>
        public static TOutput[] ConvertAll<TInput, TOutput>(TInput[] array, Converter<TInput, TOutput> converter)
        {
            if (array == null)
                Environment.FailFast("Array.ConvertAll: array is null");
            if (converter == null)
                Environment.FailFast("Array.ConvertAll: converter is null");

            TOutput[] result = new TOutput[array.Length];
            for (int i = 0; i < array.Length; i++)
            {
                result[i] = converter(array[i]);
            }
            return result;
        }

        /// <summary>
        /// Fills the array with the specified value.
        /// </summary>
        public static void Fill<T>(T[] array, T value)
        {
            if (array == null)
                Environment.FailFast("Array.Fill: array is null");

            for (int i = 0; i < array.Length; i++)
            {
                array[i] = value;
            }
        }

        /// <summary>
        /// Fills a range of the array with the specified value.
        /// </summary>
        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            if (array == null)
                Environment.FailFast("Array.Fill: array is null");
            if (startIndex < 0 || count < 0)
                Environment.FailFast("Array.Fill: negative index or count");
            if (startIndex + count > array.Length)
                Environment.FailFast("Array.Fill: overflow");

            int endIndex = startIndex + count;
            for (int i = startIndex; i < endIndex; i++)
            {
                array[i] = value;
            }
        }

        /// <summary>
        /// Determines whether the specified array contains the specified value.
        /// </summary>
        public static bool Contains<T>(T[] array, T value)
        {
            return IndexOf(array, value) >= 0;
        }

        /// <summary>
        /// Returns an IEnumerator for the Array.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return new ArrayEnumerator(this);
        }

        /// <summary>
        /// Gets the lower bound of the specified dimension.
        /// </summary>
        public int GetLowerBound(int dimension)
        {
            if (dimension != 0)
                Environment.FailFast("Array.GetLowerBound: invalid dimension");
            return 0;
        }

        /// <summary>
        /// Gets the upper bound of the specified dimension.
        /// </summary>
        public int GetUpperBound(int dimension)
        {
            if (dimension != 0)
                Environment.FailFast("Array.GetUpperBound: invalid dimension");
            return _length - 1;
        }

        /// <summary>
        /// Gets the length of the specified dimension.
        /// </summary>
        public int GetLength(int dimension)
        {
            if (dimension != 0)
                Environment.FailFast("Array.GetLength: invalid dimension");
            return _length;
        }
    }

    /// <summary>
    /// Generic array type marker for the runtime.
    /// </summary>
    internal class Array<T> : Array { }

    /// <summary>
    /// Provides cached empty arrays.
    /// </summary>
    internal static class EmptyArray<T>
    {
        internal static readonly T[] Value = new T[0];
    }

    /// <summary>
    /// Simple enumerator for arrays.
    /// </summary>
    internal sealed class ArrayEnumerator : IEnumerator
    {
        private readonly Array _array;
        private int _index;

        internal ArrayEnumerator(Array array)
        {
            _array = array;
            _index = -1;
        }

        public bool MoveNext()
        {
            int nextIndex = _index + 1;
            if (nextIndex < _array.Length)
            {
                _index = nextIndex;
                return true;
            }
            return false;
        }

        public object? Current
        {
            get
            {
                if (_index < 0 || _index >= _array.Length)
                    Environment.FailFast("ArrayEnumerator: invalid position");
                // For non-generic enumeration, we need to box the element
                // This is a simplified implementation
                return null; // Would need runtime support to get element at index
            }
        }

        public void Reset()
        {
            _index = -1;
        }
    }
}
