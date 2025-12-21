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
    public struct RuntimeTypeHandle
    {
        private readonly nint _value;

        internal RuntimeTypeHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeTypeHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeTypeHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeTypeHandle left, RuntimeTypeHandle right) => left._value != right._value;
    }

    public struct RuntimeMethodHandle
    {
        private readonly nint _value;

        internal RuntimeMethodHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeMethodHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeMethodHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeMethodHandle left, RuntimeMethodHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeMethodHandle left, RuntimeMethodHandle right) => left._value != right._value;
    }

    public struct RuntimeFieldHandle
    {
        private readonly nint _value;

        internal RuntimeFieldHandle(nint value)
        {
            _value = value;
        }

        public nint Value => _value;

        public override bool Equals(object? obj)
        {
            if (obj is RuntimeFieldHandle other)
                return _value == other._value;
            return false;
        }

        public bool Equals(RuntimeFieldHandle handle) => _value == handle._value;
        public override int GetHashCode() => _value.GetHashCode();

        public static bool operator ==(RuntimeFieldHandle left, RuntimeFieldHandle right) => left._value == right._value;
        public static bool operator !=(RuntimeFieldHandle left, RuntimeFieldHandle right) => left._value != right._value;
    }
}
