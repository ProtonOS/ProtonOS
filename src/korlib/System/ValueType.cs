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

using System.Runtime;
using System.Runtime.CompilerServices;

namespace System
{
    public abstract class ValueType
    {
        /// <summary>
        /// Determines whether this instance and a specified object have the same value.
        /// For value types, compares the raw bytes of the boxed values.
        /// </summary>
        public override unsafe bool Equals(object? obj)
        {
            if (obj == null)
                return false;

            // Get the MethodTable pointers for both objects
            // Object layout: [MethodTable* at offset 0][data...]
            MethodTable* thisMT = this.m_pMethodTable;
            MethodTable* otherMT = obj.m_pMethodTable;

            // Must be the same type
            if (thisMT != otherMT)
                return false;

            // Get the value size from MethodTable
            // ValueTypeSize = BaseSize - sizeof(MethodTable*) = BaseSize - 8
            uint valueSize = thisMT->ValueTypeSize;
            if (valueSize == 0)
                return true;  // Empty struct, always equal

            // Get pointers to the value data (skip the MethodTable pointer)
            // The m_pMethodTable field is at offset 0 of the object.
            // The value data starts at offset 8 (after the MethodTable pointer).
            // We use fixed to get the address of the MethodTable pointer field,
            // then offset to get to the value data.
            fixed (MethodTable** pThisMT = &this.m_pMethodTable)
            {
                fixed (MethodTable** pOtherMT = &obj.m_pMethodTable)
                {
                    byte* thisData = (byte*)pThisMT + sizeof(nint);
                    byte* otherData = (byte*)pOtherMT + sizeof(nint);

                    // Compare byte by byte
                    for (uint i = 0; i < valueSize; i++)
                    {
                        if (thisData[i] != otherData[i])
                            return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// For value types, hashes the raw bytes of the boxed value.
        /// </summary>
        public override unsafe int GetHashCode()
        {
            // Get the MethodTable pointer
            MethodTable* mt = this.m_pMethodTable;

            // Get the value size
            uint valueSize = mt->ValueTypeSize;
            if (valueSize == 0)
                return 0;  // Empty struct

            // Get pointer to the value data (skip the MethodTable pointer)
            fixed (MethodTable** pThisMT = &this.m_pMethodTable)
            {
                byte* data = (byte*)pThisMT + sizeof(nint);

                // FNV-1a hash of the raw bytes
                int hash = unchecked((int)2166136261);
                for (uint i = 0; i < valueSize; i++)
                {
                    hash ^= data[i];
                    hash *= 16777619;
                }

                return hash;
            }
        }
    }
}
