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

namespace System
{
    public struct Nullable<T> where T : struct
    {
        private readonly bool _hasValue;
        private T _value;

        public Nullable(T value) => (_hasValue, _value) = (true, value);

        public readonly bool HasValue => _hasValue;

        public readonly T Value
        {
            get
            {
                if (!_hasValue)
                    Environment.FailFast(null);
                return _value;
            }
        }

        public readonly T GetValueOrDefault() => _value;

        public readonly T GetValueOrDefault(T defaultValue) => _hasValue ? _value : defaultValue;

        /// <summary>
        /// Indicates whether this instance and a specified object are equal.
        /// Handles the special case where boxing Nullable&lt;T&gt; produces boxed T.
        /// </summary>
        public override bool Equals(object? other)
        {
            if (!_hasValue) return other == null;
            if (other == null) return false;
            // Boxing a Nullable<T> with HasValue=true produces boxed T, not boxed Nullable<T>
            // Compare by boxing our value and using the inner type's Equals
            object boxedValue = _value;
            return boxedValue.Equals(other);
        }

        /// <summary>
        /// Returns the hash code for this instance.
        /// </summary>
        public override int GetHashCode()
        {
            return _hasValue ? _value.GetHashCode() : 0;
        }

        public static implicit operator Nullable<T>(T value) => new Nullable<T>(value);

        public static explicit operator T(Nullable<T> value) => value.Value;
    }
}
