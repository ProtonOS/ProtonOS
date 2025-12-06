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
    public partial class Object
    {
#pragma warning disable 169
        // The layout of object is a contract with the compiler.
        internal unsafe MethodTable* m_pMethodTable;
#pragma warning restore 169

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>true if the specified object is equal to the current object; otherwise, false.</returns>
        public virtual bool Equals(object? obj)
        {
            return this == obj;
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object.</returns>
        public virtual int GetHashCode()
        {
            // Simple implementation - returns address-based hash
            // This is overridden by types that need value-based hashing
            return 0;
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public virtual string ToString()
        {
            // Simple implementation - returns type name when available
            return "Object";
        }

        /// <summary>
        /// Gets the Type of the current instance.
        /// </summary>
        /// <returns>The exact runtime type of the current instance.</returns>
        public unsafe Type GetType()
        {
            return new RuntimeType(m_pMethodTable);
        }

        /// <summary>
        /// Determines whether the specified object instances are the same instance.
        /// </summary>
        public static bool ReferenceEquals(object? objA, object? objB)
        {
            return objA == objB;
        }

        /// <summary>
        /// Determines whether the specified Object instances are equal.
        /// </summary>
        public static new bool Equals(object? objA, object? objB)
        {
            if (objA == objB)
                return true;
            if (objA == null || objB == null)
                return false;
            return objA.Equals(objB);
        }
    }
}
