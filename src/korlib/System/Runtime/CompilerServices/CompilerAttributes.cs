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

namespace System.Runtime.CompilerServices
{
    internal sealed class IntrinsicAttribute : Attribute { }

    public enum MethodImplOptions
    {
        Unmanaged = 0x0004,
        NoInlining = 0x0008,
        ForwardRef = 0x0010,
        Synchronized = 0x0020,
        NoOptimization = 0x0040,
        PreserveSig = 0x0080,
        AggressiveInlining = 0x0100,
        AggressiveOptimization = 0x0200,
        InternalCall = 0x1000
    }

    public sealed class MethodImplAttribute : Attribute
    {
        public MethodImplAttribute(MethodImplOptions methodImplOptions) { }
    }

    public sealed class IndexerNameAttribute: Attribute
    {
        public IndexerNameAttribute(string indexerName) { }
    }

    public class CallConvCdecl { }
    public class CallConvFastcall { }
    public class CallConvStdcall { }
    public class CallConvSuppressGCTransition { }
    public class CallConvThiscall { }
    public class CallConvMemberFunction { }

    /// <summary>
    /// Used by the C# compiler to mark volatile fields.
    /// </summary>
    public static class IsVolatile { }

    public sealed class InlineArrayAttribute : Attribute
    {
        public InlineArrayAttribute(int size) { }
    }

    /// <summary>
    /// Used by the C# compiler to mark fixed-size buffer fields.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public sealed class FixedBufferAttribute : Attribute
    {
        public FixedBufferAttribute(Type elementType, int length)
        {
            ElementType = elementType;
            Length = length;
        }

        public Type ElementType { get; }
        public int Length { get; }
    }

    public sealed class ExtensionAttribute : Attribute { }

    /// <summary>
    /// Indicates the attributed type is to be used as an interpolated string handler.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerAttribute : Attribute { }

    /// <summary>
    /// Indicates which arguments to a method should be passed to the InterpolatedStringHandler constructor.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    public sealed class InterpolatedStringHandlerArgumentAttribute : Attribute
    {
        public InterpolatedStringHandlerArgumentAttribute(string argument)
        {
            Arguments = new string[] { argument };
        }

        public InterpolatedStringHandlerArgumentAttribute(string argument1, string argument2)
        {
            Arguments = new string[] { argument1, argument2 };
        }

        public string[] Arguments { get; }
    }
}
