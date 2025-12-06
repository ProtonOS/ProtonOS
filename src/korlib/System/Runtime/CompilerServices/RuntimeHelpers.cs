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

using System.Runtime.InteropServices;

namespace System.Runtime.CompilerServices
{
    public class RuntimeHelpers
    {
        public static unsafe int OffsetToStringData => sizeof(IntPtr) + sizeof(int);

        /// <summary>
        /// Determines whether the specified Object instances are reference equal.
        /// </summary>
        public static bool Equals(object? o1, object? o2)
        {
            // Default implementation: reference equality
            return o1 == o2;
        }

        /// <summary>
        /// Gets a hash code for an object based on its identity (memory address).
        /// </summary>
        public static unsafe int GetHashCode(object o)
        {
            // Return a hash based on the object's memory address
            // This is a simple identity-based hash
            if (o == null)
                return 0;
            return (int)(nuint)Unsafe.AsPointer(ref Unsafe.As<object, byte>(ref o));
        }

        /// <summary>
        /// Initializes an array from static data embedded in the assembly.
        /// This is called by the compiler when initializing arrays with literal values.
        /// </summary>
        /// <param name="array">The array to initialize.</param>
        /// <param name="fldHandle">A handle to the static data field containing initial values.</param>
        public static unsafe void InitializeArray(Array array, RuntimeFieldHandle fldHandle)
        {
            // The RuntimeFieldHandle contains a pointer to the field data in the PE image.
            // For AOT-compiled code, this is a pointer to the static initializer data.
            // We need to copy the data from the field to the array's data section.

            if (array == null)
                return;

            // Get the source data pointer from the field handle
            byte* src = (byte*)fldHandle.Value;
            if (src == null)
                return;

            // Get element size from the MethodTable
            void* pMT = *(void**)Unsafe.AsPointer(ref Unsafe.As<Array, byte>(ref array));
            ushort componentSize = *(ushort*)pMT;

            int totalSize = array.Length * componentSize;

            // Get destination pointer - array data follows the object header and length field
            // Use Unsafe to get pointer to raw array data
            ref byte rawRef = ref Unsafe.As<RawArrayData>(array).Data;
            byte* dest = (byte*)Unsafe.AsPointer(ref rawRef);

            // Copy the static data to the array
            for (int i = 0; i < totalSize; i++)
            {
                dest[i] = src[i];
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal class RawArrayData
    {
        public uint Length;
#if X64 || ARM64
        public uint Padding;
#elif X86 || ARM
        // No padding on 32bit
#else
#error Nope
#endif
        public byte Data;
    }
}
