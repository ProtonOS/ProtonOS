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
    public abstract class Attribute { }

    /// <summary>
    /// Specifies the application elements on which it is valid to apply an attribute.
    /// </summary>
    [Flags]
    public enum AttributeTargets
    {
        Assembly = 0x0001,
        Module = 0x0002,
        Class = 0x0004,
        Struct = 0x0008,
        Enum = 0x0010,
        Constructor = 0x0020,
        Method = 0x0040,
        Property = 0x0080,
        Field = 0x0100,
        Event = 0x0200,
        Interface = 0x0400,
        Parameter = 0x0800,
        Delegate = 0x1000,
        ReturnValue = 0x2000,
        GenericParameter = 0x4000,
        All = Assembly | Module | Class | Struct | Enum | Constructor |
              Method | Property | Field | Event | Interface | Parameter |
              Delegate | ReturnValue | GenericParameter
    }

    /// <summary>
    /// Indicates that an enumeration can be treated as a bit field.
    /// </summary>
    [AttributeUsage(AttributeTargets.Enum, Inherited = false)]
    public sealed class FlagsAttribute : Attribute
    {
        public FlagsAttribute() { }
    }

    public sealed class AttributeUsageAttribute : Attribute
    {
        //Constructors 
        public AttributeUsageAttribute(AttributeTargets validOn) 
        {
        }

       public bool AllowMultiple
       {
           get { return false; }
           set { }
       }

       public bool Inherited
       {
           get { return false; }
           set { }
       }
    }

    /// <summary>
    /// Indicates that a method will allow a variable number of arguments in its invocation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = true, AllowMultiple = false)]
    public sealed class ParamArrayAttribute : Attribute
    {
        public ParamArrayAttribute() { }
    }

    /// <summary>
    /// Marks the program elements that are no longer in use.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum |
                    AttributeTargets.Interface | AttributeTargets.Constructor | AttributeTargets.Method |
                    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event |
                    AttributeTargets.Delegate, Inherited = false)]
    public sealed class ObsoleteAttribute : Attribute
    {
        public ObsoleteAttribute() { }
        public ObsoleteAttribute(string? message) => Message = message;
        public ObsoleteAttribute(string? message, bool error) => (Message, IsError) = (message, error);

        public string? Message { get; }
        public bool IsError { get; }
    }
}
